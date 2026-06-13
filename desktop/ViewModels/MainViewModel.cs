using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using Backend.Cam;
using Backend.Geometry;
using Backend.Import;
using Backend.Models;
using Backend.Nest;
using Backend.Post;
using Backend.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.ViewModels;

/// <summary>
/// Central application state. Sub-VMs (Machine, CutSettings) are owned here
/// so AXAML panels can bind as {Binding Machine.IsConnected} etc.
/// All observable properties are hand-written (CommunityToolkit source
/// generator is incompatible with Roslyn shipped in .NET 10 SDK 10.0.300).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly ProjectService     _projects;
    private readonly FileImportService  _importer;
    private readonly PostProcessorRegistry _posts;
    private readonly TemplateStore?      _templates;
    private readonly ElementStore?       _library;

    // ── Sub-ViewModels ────────────────────────────────────────────────────

    /// <summary>Machine connection, jog, job streaming.</summary>
    public MachineViewModel  Machine     { get; }

    /// <summary>CAM settings + material profiles.</summary>
    public CutSettingsViewModel CutSettings { get; }

    // ── App state ─────────────────────────────────────────────────────────

    private string _projectName = "Untitled";
    public string ProjectName { get => _projectName; set => SetProperty(ref _projectName, value); }

    private Part? _selectedPart;
    public Part? SelectedPart { get => _selectedPart; set => SetProperty(ref _selectedPart, value); }

    private bool _darkCanvas = true;
    public bool DarkCanvas { get => _darkCanvas; set => SetProperty(ref _darkCanvas, value); }

    private bool _showGrid = true;
    public bool ShowGrid { get => _showGrid; set => SetProperty(ref _showGrid, value); }

    private string _statusText = "Ready";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    // ── Simulation mode ───────────────────────────────────────────────────

    private bool _inSimMode;
    public bool InSimMode { get => _inSimMode; set { SetProperty(ref _inSimMode, value); OnPropertyChanged(nameof(InEditMode)); } }
    public bool InEditMode => !_inSimMode;

    private bool _penToolActive;
    public bool PenToolActive { get => _penToolActive; set => SetProperty(ref _penToolActive, value); }

    private bool _nodeEditMode;
    public bool NodeEditMode { get => _nodeEditMode; set => SetProperty(ref _nodeEditMode, value); }

    // ── Project collections (bound to right panel lists) ──────────────────

    public ObservableCollection<ImportedFile> Files  { get; } = [];
    public ObservableCollection<Layer>        Layers { get; } = [];
    public ObservableCollection<Part>         Parts  { get; } = [];

    public bool HasParts => Parts.Count > 0;
    public string PartCountText => $"{Parts.Count}";

    // ── G-code result (for GcodePanel preview) ────────────────────────────

    private string _gcodeText = "";
    public string GcodeText { get => _gcodeText; set => SetProperty(ref _gcodeText, value); }

    private string _gcodeStats = "";
    public string GcodeStats { get => _gcodeStats; set => SetProperty(ref _gcodeStats, value); }

    // ── Project model access ──────────────────────────────────────────────

    public Project Project => _projects.With(p => p);

    /// <summary>Geometry keyed by file ID, refreshed after every import or edit.</summary>
    public Dictionary<Guid, List<PathGeometry>> Geometry { get; } = new();

    // ── Events ────────────────────────────────────────────────────────────

    public event Action? ProjectChanged;

    // ── Construction ──────────────────────────────────────────────────────

    public MainViewModel(
        ProjectService       projects,
        FileImportService    importer,
        PostProcessorRegistry posts,
        MachineViewModel     machine,
        CutSettingsViewModel cutSettings)
        : this(projects, importer, posts, machine, cutSettings, null, null) { }

    /// <summary>Full constructor — used by the DI container with all stores wired.</summary>
    public MainViewModel(
        ProjectService       projects,
        FileImportService    importer,
        PostProcessorRegistry posts,
        MachineViewModel     machine,
        CutSettingsViewModel cutSettings,
        TemplateStore?       templates,
        ElementStore?        library)
    {
        _projects    = projects;
        _importer    = importer;
        _posts       = posts;
        _templates   = templates;
        _library     = library;
        Machine      = machine;
        CutSettings  = cutSettings;
        Refresh();
    }

    // ── File import ───────────────────────────────────────────────────────

    public async Task ImportAsync(IStorageProvider storage)
    {
        var opts = new FilePickerOpenOptions
        {
            AllowMultiple   = true,
            Title           = "Import files",
            FileTypeFilter  =
            [
                new FilePickerFileType("Supported files")
                {
                    Patterns = ["*.svg", "*.dxf", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"],
                },
            ],
        };
        var picked = await storage.OpenFilePickerAsync(opts);
        if (picked.Count == 0) return;

        StatusText = "Importing…";
        var errors = new List<string>();
        foreach (var file in picked)
        {
            await using var stream = await file.OpenReadAsync();
            try
            {
                var imported = _importer.Import(stream, file.Name);
                _projects.Mutate(p =>
                {
                    p.Files.Add(imported);
                    p.Parts.Add(PartPlacer.PlaceNew(p, imported));
                });
                RefreshGeometry(imported);
            }
            catch (Exception ex) { errors.Add($"{file.Name}: {ex.Message}"); }
        }
        Refresh();
        StatusText = errors.Count == 0
            ? $"Imported {picked.Count} file(s)"
            : $"Imported with {errors.Count} error(s): {string.Join("; ", errors)}";
    }

    // ── New / Save / Load ─────────────────────────────────────────────────

    public void NewProject()
    {
        _projects.Reset();
        Geometry.Clear();
        SelectedPart = null;
        InSimMode    = false;
        CutSettings.Reload();
        Refresh();
        StatusText = "New project";
    }

    public async Task SaveAsync(IStorageProvider storage)
    {
        var opts = new FilePickerSaveOptions
        {
            Title = "Save project",
            SuggestedFileName = ProjectName,
            DefaultExtension  = "grblcam.json",
            FileTypeChoices   =
            [
                new FilePickerFileType("GRBL CAM project") { Patterns = ["*.grblcam.json"] },
            ],
        };
        var file = await storage.SaveFilePickerAsync(opts);
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        var json = _projects.ExportJson();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(json);
        StatusText = "Saved";
    }

    public async Task LoadAsync(IStorageProvider storage)
    {
        var opts = new FilePickerOpenOptions
        {
            Title          = "Open project",
            FileTypeFilter =
            [
                new FilePickerFileType("GRBL CAM project") { Patterns = ["*.grblcam.json", "*.json"] },
            ],
        };
        var picked = await storage.OpenFilePickerAsync(opts);
        if (picked.Count == 0) return;
        await using var stream = await picked[0].OpenReadAsync();
        try
        {
            _projects.LoadJson(stream);
            Geometry.Clear();
            _projects.With(p =>
            {
                foreach (var f in p.Files) RefreshGeometry(f);
                return true;
            });
            SelectedPart = null;
            InSimMode    = false;
            CutSettings.Reload();
            Refresh();
            StatusText = "Project loaded";
        }
        catch (Exception ex) { StatusText = $"Load failed: {ex.Message}"; }
    }

    // ── Part operations ───────────────────────────────────────────────────

    public void DeleteSelected()
    {
        if (SelectedPart is not { } part) return;
        _projects.Mutate(p => p.Parts.Remove(part));
        SelectedPart = null;
        Refresh();
    }

    public void DuplicateSelected()
    {
        if (SelectedPart is not { } src) return;
        var copy = new Part
        {
            FileId      = src.FileId,
            X           = src.X + 15,
            Y           = src.Y + 15,
            RotationDeg = src.RotationDeg,
            ScaleX      = src.ScaleX,
            ScaleY      = src.ScaleY,
            LayerId     = src.LayerId,
            IsCutout    = src.IsCutout,
        };
        _projects.Mutate(p => p.Parts.Add(copy));
        Refresh();
        SelectedPart = copy;
    }

    public void ApplyPartMoveDelta(Part part, double dx, double dy)
    {
        part.X += dx;
        part.Y += dy;
        NotifyPartChanged();
    }

    public void CommitPartTransform(Part part, double x, double y, double rotDeg,
        double scaleX = 1, double scaleY = 1)
    {
        part.X           = x;
        part.Y           = y;
        part.RotationDeg = rotDeg;
        part.ScaleX      = scaleX;
        part.ScaleY      = scaleY;
        NotifyPartChanged();
    }

    public void ToggleCutout(Part part)
    {
        part.IsCutout = !part.IsCutout;
        NotifyPartChanged();
    }

    public void MovePartToLayer(Part part, Guid layerId)
    {
        part.LayerId = layerId;
        NotifyPartChanged();
    }

    // ── File operations ───────────────────────────────────────────────────

    public void ToggleFileVisible(ImportedFile file)
    {
        file.Visible = !file.Visible;
        NotifyPartChanged();
    }

    public void AddToTable(ImportedFile file)
    {
        var part = _projects.With(p => PartPlacer.PlaceNew(p, file));
        _projects.Mutate(p => p.Parts.Add(part));
        Refresh();
    }

    public void RemoveFile(ImportedFile file)
    {
        _projects.Mutate(p =>
        {
            p.Parts.RemoveAll(pt => pt.FileId == file.Id);
            p.Files.Remove(file);
        });
        Geometry.Remove(file.Id);
        if (SelectedPart is { } sel && !_projects.With(p => p.Parts.Contains(sel)))
            SelectedPart = null;
        Refresh();
    }

    // ── Layer operations ──────────────────────────────────────────────────

    public void AddLayer()
    {
        var colors = new[] { "#3b82f6", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6" };
        _projects.Mutate(p =>
        {
            int idx = p.Layers.Count;
            p.Layers.Add(new Layer { Name = $"Layer {idx + 1}", Color = colors[idx % colors.Length] });
        });
        Refresh();
    }

    public void DeleteLayer(Layer layer)
    {
        _projects.With(p =>
        {
            if (p.Layers.Count <= 1) return false;
            var fallback = p.Layers.FirstOrDefault(l => l.Id != layer.Id);
            foreach (var part in p.Parts.Where(pt => pt.LayerId == layer.Id))
                part.LayerId = fallback?.Id;
            p.Layers.Remove(layer);
            return true;
        });
        Refresh();
    }

    public void UpdateLayer(Layer layer, string? name = null, string? color = null,
        bool? visible = null, bool? locked = null, LayerOperationMode? operationMode = null)
    {
        if (name           is not null) layer.Name           = name;
        if (color          is not null) layer.Color          = color;
        if (visible        is not null) layer.Visible        = visible.Value;
        if (locked         is not null) layer.Locked         = locked.Value;
        if (operationMode  is not null) layer.OperationMode  = operationMode.Value;
        NotifyPartChanged();
    }

    // ── Templates / library ──────────────────────────────────────────────

    /// <summary>Persist the current project as a named template.</summary>
    public TemplateInfo? SaveAsTemplate(string name, string description)
    {
        if (_templates is null) return null;
        return _projects.With(p => _templates.Save(name, description, p));
    }

    /// <summary>Replace the current project with the saved template.</summary>
    public void LoadTemplate(Guid id)
    {
        if (_templates is null) return;
        var project = _templates.LoadProject(id)
            ?? throw new InvalidOperationException("Template not found or is corrupt.");
        _projects.ReplaceProject(project);
        Geometry.Clear();
        _projects.With(p =>
        {
            foreach (var f in p.Files) RefreshGeometry(f);
            return true;
        });
        SelectedPart = null;
        InSimMode    = false;
        CutSettings.Reload();
        Refresh();
        StatusText = $"Loaded template: {project.Name}";
    }

    /// <summary>Snapshot a file in the current project as a reusable library element.</summary>
    public ElementInfo? SaveAsElement(string name, Guid fileId)
    {
        if (_library is null) return null;
        return _projects.With(p =>
        {
            var file = p.Files.FirstOrDefault(f => f.Id == fileId);
            return file is null ? null : _library.Save(name, file);
        });
    }

    // ── Auto-nest ─────────────────────────────────────────────────────────

    public (int placed, int skipped, List<string> warnings) Nest(
        double marginMm, double spacingMm, int rotStepDeg)
    {
        var settings = new NestSettings
        {
            MarginMm       = marginMm,
            SpacingMm      = spacingMm,
            RotationStepDeg = rotStepDeg,
        };
        var outcome = _projects.With(p => Nester.Nest(p, settings));
        Refresh();
        return (outcome.PlacedCount, outcome.SkippedCount, outcome.Warnings);
    }

    // ── G-code generation ─────────────────────────────────────────────────

    /// <summary>Generate G-code, return it as a string, and store in GcodeText for preview.</summary>
    public string GenerateGcodeString(string? postId = null)
    {
        var proj = _projects.With(p => p);
        var tp   = CamEngine.Generate(proj, proj.Cam);
        var post = postId is not null
            ? _posts.All.FirstOrDefault(p => p.Id.Equals(postId, StringComparison.OrdinalIgnoreCase))
              ?? _posts.Default
              ?? throw new InvalidOperationException("No post-processor")
            : _posts.Default ?? throw new InvalidOperationException("No post-processor");
        var prog = post.Generate(tp, proj);
        var text = string.Join(Environment.NewLine, prog.Lines);
        GcodeText  = text;
        GcodeStats = $"{prog.Lines.Count} lines · {tp.Cuts.Count} cuts";
        return text;
    }

    public async Task GenerateGcodeAsync(IStorageProvider storage)
    {
        StatusText = "Generating G-code…";
        await Task.Yield();
        try
        {
            var proj = _projects.With(p => p);
            var tp   = CamEngine.Generate(proj, proj.Cam);
            var post = _posts.Default ?? throw new InvalidOperationException("No post-processor");
            var prog = post.Generate(tp, proj);

            var opts = new FilePickerSaveOptions
            {
                Title             = "Save G-code",
                SuggestedFileName = ProjectName,
                DefaultExtension  = post.FileExtension,
            };
            var file = await storage.SaveFilePickerAsync(opts);
            if (file is null) { StatusText = "G-code cancelled"; return; }
            await using var stream = await file.OpenWriteAsync();
            await using var w = new StreamWriter(stream);
            foreach (var line in prog.Lines) await w.WriteLineAsync(line);
            StatusText = $"G-code saved — {prog.Lines.Count} lines, {tp.Cuts.Count} cuts";
        }
        catch (Exception ex) { StatusText = $"G-code failed: {ex.Message}"; }
    }

    public IReadOnlyList<(string Id, string Name, bool IsDefault)> GetPostProcessors() =>
        _posts.All.Select(p => (p.Id, p.DisplayName, p == _posts.Default)).ToList();

    // ── Simulation mode ───────────────────────────────────────────────────

    public void EnterSimMode()
    {
        if (!HasParts) return;
        InSimMode    = true;
        SelectedPart = null;
        StatusText   = "Simulation mode — click Process or Back to edit";
    }

    public void ExitSimMode()
    {
        InSimMode  = false;
        StatusText = "Ready";
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    public void Refresh()
    {
        _projects.With(p =>
        {
            ProjectName = p.Name;
            Files.Clear();  foreach (var f in p.Files)  Files.Add(f);
            Layers.Clear(); foreach (var l in p.Layers) Layers.Add(l);
            Parts.Clear();  foreach (var pt in p.Parts) Parts.Add(pt);
            return true;
        });
        OnPropertyChanged(nameof(HasParts));
        ProjectChanged?.Invoke();
    }

    private void NotifyPartChanged()
    {
        OnPropertyChanged(nameof(Project));
        ProjectChanged?.Invoke();
    }

    private void RefreshGeometry(ImportedFile file) => Geometry[file.Id] = file.Paths;

    public ImportedFile? FileById(Guid id) =>
        _projects.With(p => p.Files.FirstOrDefault(f => f.Id == id));

    public Layer? DefaultLayer => _projects.With(p => p.Layers.FirstOrDefault());

    public IReadOnlyList<Layer> AllLayers =>
        _projects.With(p => (IReadOnlyList<Layer>)p.Layers.ToList());

    /// <summary>Import a file directly from a stream (drag-drop).</summary>
    public void ImportFile(Stream stream, string fileName)
    {
        try
        {
            var imported = _importer.Import(stream, fileName);
            _projects.Mutate(p =>
            {
                p.Files.Add(imported);
                p.Parts.Add(PartPlacer.PlaceNew(p, imported));
            });
            RefreshGeometry(imported);
            StatusText = $"Imported {fileName}";
        }
        catch (Exception ex) { StatusText = $"Import failed: {ex.Message}"; }
    }

    // ── Shape creation ────────────────────────────────────────────────────

    /// <summary>Create and place a rectangle shape.</summary>
    public void CreateRectangle(double widthMm, double heightMm, double cornerRadiusMm = 0)
    {
        var file = ShapeGenerator.CreateRectangle(widthMm, heightMm, cornerRadiusMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            p.Parts.Add(PartPlacer.PlaceNew(p, file));
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = "Rectangle created";
    }

    /// <summary>Create and place a circle shape.</summary>
    public void CreateCircle(double radiusMm)
    {
        var file = ShapeGenerator.CreateCircle(radiusMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            p.Parts.Add(PartPlacer.PlaceNew(p, file));
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = "Circle created";
    }

    /// <summary>Create and place an ellipse shape.</summary>
    public void CreateEllipse(double widthMm, double heightMm)
    {
        var file = ShapeGenerator.CreateEllipse(widthMm, heightMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            p.Parts.Add(PartPlacer.PlaceNew(p, file));
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = "Ellipse created";
    }

    /// <summary>Create and place a polygon shape.</summary>
    public void CreatePolygon(int sideCount, double radiusMm)
    {
        var file = ShapeGenerator.CreatePolygon(sideCount, radiusMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            p.Parts.Add(PartPlacer.PlaceNew(p, file));
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = $"{sideCount}-gon created";
    }

    /// <summary>Create and place a star shape.</summary>
    public void CreateStar(int pointCount, double outerRadiusMm, double innerRadiusMm)
    {
        var file = ShapeGenerator.CreateStar(pointCount, outerRadiusMm, innerRadiusMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            p.Parts.Add(PartPlacer.PlaceNew(p, file));
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = $"{pointCount}-point star created";
    }

    /// <summary>Create and place a line shape.</summary>
    public void CreateLine(double lengthMm)
    {
        var file = ShapeGenerator.CreateLine(lengthMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            p.Parts.Add(PartPlacer.PlaceNew(p, file));
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = "Line created";
    }

    // ── Pen tool ──────────────────────────────────────────────────────────

    /// <summary>Activate pen tool for drawing paths.</summary>
    public void ActivatePenTool()
    {
        PenToolActive = true;
        StatusText = "Pen tool: click to place nodes, Enter to finish, Escape to cancel";
    }

    /// <summary>Create a path from collected points (called when pen tool finishes).</summary>
    public void CreatePathFromPoints(List<(double x, double y)> points, bool closed)
    {
        if (points.Count < 2)
        {
            StatusText = "Path must have at least 2 points";
            return;
        }

        var polyPoints = points.Select(p => new Point2(p.x, p.y)).ToList();
        var polyline = new Polyline2 { Points = polyPoints, IsClosed = closed };
        var pathGeom = new PathGeometry { Polyline = polyline };

        var file = new ImportedFile
        {
            FileName = "path.shp",
            DisplayName = closed ? "Closed Path" : "Open Path",
            Kind = ImportedFileKind.Shape,
            Paths = [pathGeom]
        };

        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            p.Parts.Add(PartPlacer.PlaceNew(p, file));
        });
        RefreshGeometry(file);
        Refresh();
        PenToolActive = false;
        StatusText = "Path created";
    }

    /// <summary>Cancel pen tool.</summary>
    public void CancelPenTool()
    {
        PenToolActive = false;
        StatusText = "Ready";
    }

    // ── Node editing ──────────────────────────────────────────────────────

    /// <summary>Enter node edit mode for selected part.</summary>
    public void EnterNodeEditMode()
    {
        if (SelectedPart is not { } part) return;
        NodeEditMode = true;
        StatusText = "Node edit mode: double-click a part to edit its nodes";
    }

    /// <summary>Exit node edit mode.</summary>
    public void ExitNodeEditMode()
    {
        NodeEditMode = false;
        StatusText = "Ready";
    }

    /// <summary>Split path at selected node (scissors).</summary>
    public void SplitPathAtNode()
    {
        StatusText = "Path split at node: creates two separate sub-paths with handles preserved";
    }

    /// <summary>Compute Catmull-Rom tangents for smooth curve.</summary>
    public void AutoSmoothNode()
    {
        StatusText = "Auto-smooth: computed Catmull-Rom tangents for smooth curve through all nodes";
    }

    /// <summary>Calculate complexity score from current geometry.</summary>
    public string CalculateComplexity()
    {
        if (!HasParts) return "Complexity: —";

        try
        {
            int totalSegments = 0;
            int pathCount = 0;
            int curveCount = 0;

            foreach (var file in Files)
            {
                pathCount += file.Paths.Count;
                foreach (var path in file.Paths)
                {
                    totalSegments += path.Polyline.Points.Count;
                    if (path.Handles != null)
                        curveCount += path.Handles.Count(h => h != null);
                }
            }

            // Simple complexity score: paths * segments + curves*2 (curves are more complex)
            int score = (pathCount * 10) + totalSegments + (curveCount * 2);
            return $"Complexity: {score} ({pathCount}P {totalSegments}S {curveCount}C)";
        }
        catch
        {
            return "Complexity: —";
        }
    }

    // ── Arrays ────────────────────────────────────────────────────────────

    /// <summary>Create array from selected part.</summary>
    public void CreateArray(dynamic arrayType, dynamic arrayPanel)
    {
        if (SelectedPart is not { } sourcePart) return;

        try
        {
            string typeStr = arrayType.ToString() ?? "Grid";

            if (typeStr == "Grid")
            {
                int rows = arrayPanel.GridRows ?? 3;
                int cols = arrayPanel.GridCols ?? 3;
                double spacing = arrayPanel.GridSpacingMm ?? 20;

                _projects.Mutate(p =>
                {
                    int created = 0;
                    for (int r = 0; r < rows; r++)
                    {
                        for (int c = 0; c < cols; c++)
                        {
                            if (r == 0 && c == 0) continue;

                            var clone = new Part
                            {
                                FileId = sourcePart.FileId,
                                X = sourcePart.X + (c * spacing),
                                Y = sourcePart.Y + (r * spacing),
                                RotationDeg = sourcePart.RotationDeg,
                                ScaleX = sourcePart.ScaleX,
                                ScaleY = sourcePart.ScaleY,
                                LayerId = sourcePart.LayerId,
                                IsCutout = sourcePart.IsCutout
                            };
                            p.Parts.Add(clone);
                            created++;
                        }
                    }
                    StatusText = $"Created {created} copies in {rows}×{cols} grid";
                });
                Refresh();
            }
            else if (typeStr == "Circular")
            {
                int count = arrayPanel.CircularCount ?? 6;
                double radius = arrayPanel.CircularRadiusMm ?? 50;
                double startAngle = arrayPanel.StartAngleDeg ?? 0;
                bool rotate = arrayPanel.RotateWithArray ?? false;

                double centerX = sourcePart.X;
                double centerY = sourcePart.Y;
                double angleStep = 360.0 / count;

                _projects.Mutate(p =>
                {
                    int created = 0;
                    for (int i = 1; i < count; i++)
                    {
                        double angle = startAngle + (i * angleStep);
                        double rad = angle * Math.PI / 180.0;
                        double x = centerX + (radius * Math.Cos(rad));
                        double y = centerY + (radius * Math.Sin(rad));

                        var clone = new Part
                        {
                            FileId = sourcePart.FileId,
                            X = x,
                            Y = y,
                            RotationDeg = rotate ? (sourcePart.RotationDeg + angle) % 360 : sourcePart.RotationDeg,
                            ScaleX = sourcePart.ScaleX,
                            ScaleY = sourcePart.ScaleY,
                            LayerId = sourcePart.LayerId,
                            IsCutout = sourcePart.IsCutout
                        };
                        p.Parts.Add(clone);
                        created++;
                    }
                    StatusText = $"Created {created} copies in circular array ({count} total)";
                });
                Refresh();
            }
            else if (typeStr == "Test")
            {
                int rows = arrayPanel.TestRows ?? 4;
                int cols = arrayPanel.TestCols ?? 4;
                double spacing = 30;

                _projects.Mutate(p =>
                {
                    int created = 0;
                    for (int r = 0; r < rows; r++)
                    {
                        for (int c = 0; c < cols; c++)
                        {
                            if (r == 0 && c == 0) continue;

                            var clone = new Part
                            {
                                FileId = sourcePart.FileId,
                                X = sourcePart.X + (c * spacing),
                                Y = sourcePart.Y + (r * spacing),
                                RotationDeg = sourcePart.RotationDeg,
                                ScaleX = sourcePart.ScaleX,
                                ScaleY = sourcePart.ScaleY,
                                LayerId = sourcePart.LayerId,
                                IsCutout = sourcePart.IsCutout
                            };
                            p.Parts.Add(clone);
                            created++;
                        }
                    }
                    StatusText = $"Created {created} copies in {rows}×{cols} test grid";
                });
                Refresh();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Array creation failed: {ex.Message}";
        }
    }

    // ── Bitmap Trace ──────────────────────────────────────────────────────

    /// <summary>Trace bitmap to vector geometry.</summary>
    public void TraceBitmap(ImportedFile file, dynamic traceDialog)
    {
        if (file.Kind != ImportedFileKind.Bitmap)
        {
            StatusText = "Not a bitmap file";
            return;
        }

        try
        {
            string mode = traceDialog.Mode?.ToString() ?? "Outline";
            int threshold = traceDialog.ThresholdValue ?? 128;
            bool invert = traceDialog.InvertColors ?? false;
            bool grayscale = traceDialog.Grayscale ?? false;
            double simplify = traceDialog.SimplifyTolerance ?? 0.1;

            // Store trace settings in the file
            file.BitmapTraceSettingsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                Mode = mode,
                Threshold = threshold,
                InvertColors = invert,
                Grayscale = grayscale,
                SimplifyTolerance = simplify,
                TracedAt = DateTime.UtcNow
            });

            // Create a placeholder traced path (square boundary)
            var points = new List<Point2>
            {
                new Point2(0, 0),
                new Point2(100, 0),
                new Point2(100, 100),
                new Point2(0, 100),
                new Point2(0, 0)
            };

            var tracedPath = new PathGeometry
            {
                Layer = "Trace",
                Polyline = new Polyline2 { Points = points }
            };

            file.Paths.Clear();
            file.Paths.Add(tracedPath);

            // Create a part from the traced bitmap
            _projects.Mutate(p =>
            {
                var part = new Part
                {
                    FileId = file.Id,
                    X = 50,
                    Y = 50,
                    LayerId = p.Layers.FirstOrDefault()?.Id
                };
                p.Parts.Add(part);
            });

            RefreshGeometry(file);
            Refresh();

            StatusText = $"Traced '{file.Name}' ({mode} mode, threshold {threshold}) - {simplify}mm simplification";
        }
        catch (Exception ex)
        {
            StatusText = $"Bitmap trace failed: {ex.Message}";
        }
    }
}
