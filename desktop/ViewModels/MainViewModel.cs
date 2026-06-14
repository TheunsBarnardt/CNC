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
    public Part? SelectedPart
    {
        get => _selectedPart;
        set
        {
            SetProperty(ref _selectedPart, value);
            // Selecting a part makes its layer the active drawing layer.
            if (value?.LayerId is { } lid) SetActiveLayer(lid);
            else UpdateActiveLayer();
        }
    }

    /// <summary>The layer where new shapes will be placed. Persists across deselect.</summary>
    private Guid? _activeLayerId;

    /// <summary>Make the given layer the active drawing context for new shapes.</summary>
    public void SetActiveLayer(Guid id)
    {
        _activeLayerId = id;
        UpdateActiveLayer();
    }

    private void UpdateActiveLayer()
    {
        foreach (var layer in Layers)
            layer.IsActive = layer.Id == _activeLayerId;
    }

    /// <summary>Returns the active layer id, falling back to the first available layer.</summary>
    private Guid? ActiveLayerId(Project p) =>
        _activeLayerId is { } id && p.Layers.Any(l => l.Id == id)
            ? id
            : p.Layers.FirstOrDefault()?.Id;

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

    public ObservableCollection<ImportedFile> Files     { get; } = [];
    public ObservableCollection<Layer>        Layers    { get; } = [];
    public ObservableCollection<Part>         Parts     { get; } = [];
    public ObservableCollection<NoGoZone>     NoGoZones { get; } = [];

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
                List<ImportedFile> subFiles = [];
                _projects.Mutate(p => subFiles = SplitImportByLayer(p, imported, file.Name));
                foreach (var sf in subFiles) RefreshGeometry(sf);
            }
            catch (Exception ex) { errors.Add($"{file.Name}: {ex.Message}"); }
        }
        Refresh();
        StatusText = errors.Count == 0
            ? $"Imported {picked.Count} file(s)"
            : $"Imported with {errors.Count} error(s): {string.Join("; ", errors)}";
    }

    /// <summary>
    /// Splits an imported file by its embedded layer names (SVG/DXF) into one
    /// ImportedFile+Part+Layer per distinct layer. All parts are anchored at the
    /// same canvas position so multi-layer artwork stays aligned. Falls back to
    /// a single part when the file has no layer metadata.
    /// </summary>
    private List<ImportedFile> SplitImportByLayer(Project p, ImportedFile imported, string fileName)
    {
        var groups = imported.Paths
            .GroupBy(pg => string.IsNullOrEmpty(pg.Layer) ? null : pg.Layer)
            .ToList();

        bool hasLayers = groups.Count > 1 || groups[0].Key is not null;

        if (!hasLayers)
        {
            p.Files.Add(imported);
            var part = PartPlacer.PlaceNew(p, imported);
            part.LayerId = ActiveLayerId(p);
            p.Parts.Add(part);
            return [imported];
        }

        // Multi-layer: create one sub-file+part per SVG/DXF layer.
        // First part uses PlaceNew for auto-positioning; the rest share its X,Y
        // so all layers stack on top of each other (same artwork, different operations).
        var result = new List<ImportedFile>();
        double? anchorX = null, anchorY = null;
        var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);

        foreach (var group in groups)
        {
            var layerName = group.Key ?? baseName;
            var subFile = new ImportedFile
            {
                FileName    = imported.FileName,
                DisplayName = layerName,
                Kind        = imported.Kind,
            };
            foreach (var pg in group) subFile.Paths.Add(pg);

            p.Files.Add(subFile);
            var part = PartPlacer.PlaceNew(p, subFile);
            if (anchorX.HasValue) { part.X = anchorX.Value; part.Y = anchorY!.Value; }
            else                  { anchorX = part.X; anchorY = part.Y; }
            var color = imported.LayerColors.GetValueOrDefault(layerName);
            part.LayerId = CreatePartLayer(p, layerName, color).Id;
            p.Parts.Add(part);
            result.Add(subFile);
        }

        return result;
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
        OnPropertyChanged(nameof(SelectedPart)); // refreshes EditToolbar after drag
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

    private static readonly string[] _layerColors = ["#3b82f6", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6"];

    /// <summary>Creates a new named layer inside an already-open Mutate block and returns it.</summary>
    private static Layer CreatePartLayer(Project p, string name, string? color = null)
    {
        var layer = new Layer
        {
            Name  = name,
            Color = color ?? _layerColors[p.Layers.Count % _layerColors.Length],
        };
        p.Layers.Add(layer);
        return layer;
    }

    public void AddLayer()
    {
        _projects.Mutate(p =>
        {
            int idx = p.Layers.Count;
            var newLayer = new Layer { Name = $"Layer {idx + 1}", Color = _layerColors[idx % _layerColors.Length] };
            p.Layers.Add(newLayer);
            _activeLayerId = newLayer.Id;
        });
        Refresh();
        UpdateActiveLayer();
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

    public void SetLayerFeedOverride(Layer layer, double? value)
    {
        layer.FeedRateMmMinOverride = value;
        NotifyPartChanged();
    }

    public void SetLayerPowerOverride(Layer layer, double? value)
    {
        layer.LaserPowerPercentOverride = value;
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

    /// <summary>
    /// Generate a test G-code matrix where rows vary Param1 and columns vary Param2.
    /// Places the selected part R×C times, creates temporary layers with per-cell settings,
    /// generates combined G-code, and saves it.
    /// </summary>
    public async Task GenerateTestGcodeAsync(dynamic panel, IStorageProvider storage)
    {
        if (SelectedPart is not { } sourcePart) return;
        StatusText = "Generating test G-code…";
        await Task.Yield();
        try
        {
            int rows      = (int)(panel.TestRows ?? 4);
            int cols      = (int)(panel.TestCols ?? 4);
            string p1Tag  = (string)(panel.Param1Tag ?? "Power");
            double p1From = (double)(panel.Param1From);
            double p1To   = (double)(panel.Param1To);
            string p2Tag  = (string)(panel.Param2Tag ?? "FeedRate");
            double p2From = (double)(panel.Param2From);
            double p2To   = (double)(panel.Param2To);

            var proj = _projects.With(p => p);
            var baseCam = proj.Cam;

            // Build a temporary project snapshot with test grid parts + layers
            var testProj = new Backend.Models.Project
            {
                Name  = $"{proj.Name} — Test",
                Table = proj.Table,
                Cam   = baseCam,
                Units = proj.Units,
            };
            // Copy the source file
            if (proj.Files.FirstOrDefault(f => f.Id == sourcePart.FileId) is { } srcFile)
                testProj.Files.Add(srcFile);

            if (proj.Files.FirstOrDefault(f => f.Id == sourcePart.FileId) is not { } file)
            {
                StatusText = "No file for selected part"; return;
            }
            var bb = file.BoundingBox;
            double stepX = bb.Width  * Math.Abs(sourcePart.ScaleX) + 10;
            double stepY = bb.Height * Math.Abs(sourcePart.ScaleY) + 10;

            for (int r = 0; r < rows; r++)
            {
                double v1 = rows > 1 ? p1From + (p1To - p1From) * r / (rows - 1) : p1From;
                for (int c = 0; c < cols; c++)
                {
                    double v2 = cols > 1 ? p2From + (p2To - p2From) * c / (cols - 1) : p2From;

                    var layer = new Layer
                    {
                        Name  = $"R{r + 1}C{c + 1} {p1Tag}={v1:F0} {p2Tag}={v2:F0}",
                        Color = "#3b82f6",
                    };
                    if (p1Tag == "FeedRate" || p2Tag == "FeedRate")
                        layer.FeedRateMmMinOverride = p1Tag == "FeedRate" ? v1 : v2;
                    if (p1Tag == "Power" || p2Tag == "Power")
                        layer.LaserPowerPercentOverride = p1Tag == "Power" ? v1 : v2;
                    testProj.Layers.Add(layer);

                    var part = new Part
                    {
                        FileId      = file.Id,
                        X           = sourcePart.X + c * stepX,
                        Y           = sourcePart.Y + r * stepY,
                        RotationDeg = sourcePart.RotationDeg,
                        ScaleX      = sourcePart.ScaleX,
                        ScaleY      = sourcePart.ScaleY,
                        LayerId     = layer.Id,
                    };
                    // Override pierce delay via CamSettings copy if needed
                    testProj.Parts.Add(part);
                }
            }

            var tp   = CamEngine.Generate(testProj, testProj.Cam);
            var post = _posts.Default ?? throw new InvalidOperationException("No post-processor");
            var prog = post.Generate(tp, testProj);

            var file2 = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title             = "Save test G-code",
                SuggestedFileName = $"{ProjectName}_test_{rows}x{cols}.nc",
                DefaultExtension  = ".nc",
            });
            if (file2 is null) { StatusText = "Test G-code cancelled"; return; }
            await using var stream = await file2.OpenWriteAsync();
            await using var w = new StreamWriter(stream);
            foreach (var line in prog.Lines) await w.WriteLineAsync(line);
            StatusText = $"Test grid saved — {rows}×{cols} cells, {prog.Lines.Count} lines";
        }
        catch (Exception ex) { StatusText = $"Test G-code failed: {ex.Message}"; }
    }

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

    // ── Undo / Redo ────────────────────────────────────────────────────────

    private readonly List<string> _undoStack = new();
    private readonly List<string> _redoStack = new();
    private const int MaxUndoDepth = 50;
    private Part? _clipboard;

    public void Checkpoint()
    {
        var json = _projects.ExportJson();
        _undoStack.Add(json);
        if (_undoStack.Count > MaxUndoDepth) _undoStack.RemoveAt(0);
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Add(_projects.ExportJson());
        var prev = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        RestoreSnapshot(prev);
        StatusText = "Undo";
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Add(_projects.ExportJson());
        var next = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        RestoreSnapshot(next);
        StatusText = "Redo";
    }

    private void RestoreSnapshot(string json)
    {
        using var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        _projects.LoadJson(ms);
        Geometry.Clear();
        _projects.With(p => { foreach (var f in p.Files) RefreshGeometry(f); return true; });
        SelectedPart = null;
        CutSettings.Reload();
        Refresh();
    }

    public void CopySelected()
    {
        if (SelectedPart is not { } part) return;
        _clipboard = new Part
        {
            FileId      = part.FileId,
            X           = part.X,
            Y           = part.Y,
            RotationDeg = part.RotationDeg,
            ScaleX      = part.ScaleX,
            ScaleY      = part.ScaleY,
            LayerId     = part.LayerId,
            IsCutout    = part.IsCutout,
        };
        StatusText = "Copied";
    }

    public void PasteClipboard()
    {
        if (_clipboard is not { } src) return;
        Checkpoint();
        var copy = new Part
        {
            FileId      = src.FileId,
            X           = src.X + 10,
            Y           = src.Y + 10,
            RotationDeg = src.RotationDeg,
            ScaleX      = src.ScaleX,
            ScaleY      = src.ScaleY,
            LayerId     = src.LayerId,
            IsCutout    = src.IsCutout,
        };
        _projects.Mutate(p => p.Parts.Add(copy));
        Refresh();
        SelectedPart = copy;
        StatusText = "Pasted";
    }

    public void BringToFront()
    {
        if (SelectedPart is not { } part) return;
        Checkpoint();
        _projects.Mutate(p => { if (p.Parts.Remove(part)) p.Parts.Add(part); });
        Refresh();
        SelectedPart = part;
    }

    public void SendToBack()
    {
        if (SelectedPart is not { } part) return;
        Checkpoint();
        _projects.Mutate(p => { if (p.Parts.Remove(part)) p.Parts.Insert(0, part); });
        Refresh();
        SelectedPart = part;
    }

    public void BringForward()
    {
        if (SelectedPart is not { } part) return;
        Checkpoint();
        _projects.Mutate(p =>
        {
            var i = p.Parts.IndexOf(part);
            if (i >= 0 && i < p.Parts.Count - 1) { p.Parts.RemoveAt(i); p.Parts.Insert(i + 1, part); }
        });
        Refresh();
        SelectedPart = part;
    }

    public void SendBackward()
    {
        if (SelectedPart is not { } part) return;
        Checkpoint();
        _projects.Mutate(p =>
        {
            var i = p.Parts.IndexOf(part);
            if (i > 0) { p.Parts.RemoveAt(i); p.Parts.Insert(i - 1, part); }
        });
        Refresh();
        SelectedPart = part;
    }

    public void ReflectH()
    {
        if (SelectedPart is not { } p) return;
        Checkpoint();
        CommitPartTransform(p, p.X, p.Y, p.RotationDeg, -p.ScaleX, p.ScaleY);
    }

    public void ReflectV()
    {
        if (SelectedPart is not { } p) return;
        Checkpoint();
        CommitPartTransform(p, p.X, p.Y, p.RotationDeg, p.ScaleX, -p.ScaleY);
    }

    public void AlignHCenter()
    {
        if (SelectedPart is not { } part || FileById(part.FileId) is not { } file) return;
        var bb = file.BoundingBox;
        double w = bb.Width * Math.Abs(part.ScaleX);
        CommitPartTransform(part, (Project.TableWidthMm - w) / 2, part.Y, part.RotationDeg, part.ScaleX, part.ScaleY);
    }

    public void AlignVCenter()
    {
        if (SelectedPart is not { } part || FileById(part.FileId) is not { } file) return;
        var bb = file.BoundingBox;
        double h = bb.Height * Math.Abs(part.ScaleY);
        CommitPartTransform(part, part.X, (Project.TableHeightMm - h) / 2, part.RotationDeg, part.ScaleX, part.ScaleY);
    }

    /// <summary>Selects the first part belonging to the given layer so the user can see what's on it.</summary>
    public void SelectLayerParts(Guid layerId)
    {
        var first = _projects.With(p => p.Parts.FirstOrDefault(pt => pt.LayerId == layerId));
        SelectedPart = first;
    }

    public void MoveSelectedToLayer(Guid layerId)
    {
        if (SelectedPart is null) return;
        SelectedPart.LayerId = layerId;
        Refresh();
        StatusText = "Object moved to layer";
    }

    public void ApplyOffset(double distanceMm, bool external, string cornerStyle, bool outerOnly)
    {
        if (SelectedPart is not { } part || FileById(part.FileId) is not { } file) return;

        var joinType = cornerStyle switch
        {
            "round" => Clipper2Lib.JoinType.Round,
            "bevel" => Clipper2Lib.JoinType.Bevel,
            _       => Clipper2Lib.JoinType.Miter,
        };
        double delta = external ? distanceMm : -distanceMm;

        var newPaths = new List<Backend.Models.PathGeometry>();
        foreach (var pg in file.Paths)
        {
            if (pg.Polyline is null || pg.Polyline.Points.Count < 3) continue;
            bool isOuter = Backend.Cam.KerfOffsetter.SignedArea(pg.Polyline) > 0;
            if (outerOnly && !isOuter) continue;
            foreach (var r in Backend.Cam.KerfOffsetter.OffsetClosed(pg.Polyline, delta, joinType))
                newPaths.Add(new Backend.Models.PathGeometry { Polyline = r });
        }

        if (newPaths.Count == 0) { StatusText = "Offset produced no paths — try a smaller distance"; return; }

        var newFile = new Backend.Models.ImportedFile
        {
            FileName    = $"{file.FileName}_offset",
            DisplayName = $"{file.Name} (offset {(external ? "+" : "-")}{distanceMm:F1}mm)",
            Kind        = Backend.Models.ImportedFileKind.Shape,
        };
        foreach (var pg in newPaths) newFile.Paths.Add(pg);

        Checkpoint();
        _projects.Mutate(p =>
        {
            p.Files.Add(newFile);
            var part = PartPlacer.PlaceNew(p, newFile);
            part.LayerId = ActiveLayerId(p);
            p.Parts.Add(part);
        });
        RefreshGeometry(newFile);
        Refresh();
        StatusText = $"Offset applied — {newPaths.Count} path(s) created";
    }

    // ── No-go zones ───────────────────────────────────────────────────────

    public void AddNoGoZone()
    {
        var zone = new NoGoZone { Label = $"Zone {NoGoZones.Count + 1}" };
        _projects.Mutate(p => p.NoGoZones.Add(zone));
        NoGoZones.Add(zone);
        ProjectChanged?.Invoke();
    }

    public void DeleteNoGoZone(NoGoZone zone)
    {
        _projects.Mutate(p => p.NoGoZones.Remove(zone));
        NoGoZones.Remove(zone);
        ProjectChanged?.Invoke();
    }

    public void RefreshNoGoZones() => ProjectChanged?.Invoke();

    // ── Internal helpers ──────────────────────────────────────────────────

    public void Refresh()
    {
        _projects.With(p =>
        {
            // Ensure at least one layer exists
            if (p.Layers.Count == 0)
                p.Layers.Add(new Layer());

            ProjectName = p.Name;
            Files.Clear();     foreach (var f  in p.Files)      Files.Add(f);
            Layers.Clear();    foreach (var l  in p.Layers)     Layers.Add(l);
            Parts.Clear();     foreach (var pt in p.Parts)      Parts.Add(pt);
            NoGoZones.Clear(); foreach (var z  in p.NoGoZones)  NoGoZones.Add(z);
            return true;
        });
        OnPropertyChanged(nameof(HasParts));
        OnPropertyChanged(nameof(PartCountText));
        ProjectChanged?.Invoke();
        UpdateActiveLayer();
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
            List<ImportedFile> subFiles = [];
            _projects.Mutate(p => subFiles = SplitImportByLayer(p, imported, fileName));
            foreach (var sf in subFiles) RefreshGeometry(sf);
            Refresh();
            StatusText = $"Imported {fileName}";
        }
        catch (Exception ex) { StatusText = $"Import failed: {ex.Message}"; }
    }

    // ── Shape creation ────────────────────────────────────────────────────

    /// <summary>Create and place a rectangle shape. atX/atY override auto-placement.</summary>
    public void CreateRectangle(double widthMm, double heightMm, double cornerRadiusMm = 0, double? atX = null, double? atY = null)
    {
        var file = ShapeGenerator.CreateRectangle(widthMm, heightMm, cornerRadiusMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            var part = PartPlacer.PlaceNew(p, file);
            part.LayerId = ActiveLayerId(p);
            if (atX.HasValue) part.X = atX.Value;
            if (atY.HasValue) part.Y = atY.Value;
            p.Parts.Add(part);
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = "Rectangle created";
    }

    /// <summary>Create and place a circle shape. atX/atY = center position.</summary>
    public void CreateCircle(double radiusMm, double? atX = null, double? atY = null)
    {
        var file = ShapeGenerator.CreateCircle(radiusMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            var part = PartPlacer.PlaceNew(p, file);
            part.LayerId = ActiveLayerId(p);
            if (atX.HasValue) part.X = atX.Value - radiusMm;
            if (atY.HasValue) part.Y = atY.Value - radiusMm;
            p.Parts.Add(part);
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = "Circle created";
    }

    /// <summary>Create and place an ellipse shape. atX/atY override auto-placement.</summary>
    public void CreateEllipse(double widthMm, double heightMm, double? atX = null, double? atY = null)
    {
        var file = ShapeGenerator.CreateEllipse(widthMm, heightMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            var part = PartPlacer.PlaceNew(p, file);
            part.LayerId = ActiveLayerId(p);
            if (atX.HasValue) part.X = atX.Value;
            if (atY.HasValue) part.Y = atY.Value;
            p.Parts.Add(part);
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = "Ellipse created";
    }

    /// <summary>Create and place a polygon shape. atX/atY = center position.</summary>
    public void CreatePolygon(int sideCount, double radiusMm, double? atX = null, double? atY = null)
    {
        var file = ShapeGenerator.CreatePolygon(sideCount, radiusMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            var part = PartPlacer.PlaceNew(p, file);
            part.LayerId = ActiveLayerId(p);
            if (atX.HasValue) part.X = atX.Value - radiusMm;
            if (atY.HasValue) part.Y = atY.Value - radiusMm;
            p.Parts.Add(part);
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = $"{sideCount}-gon created";
    }

    /// <summary>Create and place a star shape. atX/atY = center position.</summary>
    public void CreateStar(int pointCount, double outerRadiusMm, double innerRadiusMm, double? atX = null, double? atY = null)
    {
        var file = ShapeGenerator.CreateStar(pointCount, outerRadiusMm, innerRadiusMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            var part = PartPlacer.PlaceNew(p, file);
            part.LayerId = ActiveLayerId(p);
            if (atX.HasValue) part.X = atX.Value - outerRadiusMm;
            if (atY.HasValue) part.Y = atY.Value - outerRadiusMm;
            p.Parts.Add(part);
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = $"{pointCount}-point star created";
    }

    /// <summary>Create and place a line shape (default 100 mm horizontal).</summary>
    public void CreateLine(double lengthMm = 100)
    {
        var file = ShapeGenerator.CreateLine(lengthMm);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            var part = PartPlacer.PlaceNew(p, file);
            part.LayerId = ActiveLayerId(p);
            p.Parts.Add(part);
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = "Line created";
    }

    /// <summary>Create a line from two canvas points (drag-draw).</summary>
    public void CreateLineFromPoints(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1, dy = y2 - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.5) return;
        var file = ShapeGenerator.CreateLine(len);
        _projects.Mutate(p =>
        {
            p.Files.Add(file);
            var part = PartPlacer.PlaceNew(p, file);
            part.LayerId = ActiveLayerId(p);
            part.X = x1;
            part.Y = y1;
            part.RotationDeg = Math.Atan2(dy, dx) * 180 / Math.PI;
            p.Parts.Add(part);
        });
        RefreshGeometry(file);
        Refresh();
        StatusText = $"Line created ({len:F1} mm)";
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
            var part = PartPlacer.PlaceNew(p, file);
            part.LayerId = ActiveLayerId(p);
            p.Parts.Add(part);
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
        StatusText = "Select a node first, then Sciss. splits the path there (node multi-edit coming)";
    }

    /// <summary>Compute Catmull-Rom tangents for smooth curve.</summary>
    public void AutoSmoothNode()
    {
        StatusText = "Auto-smooth: applies Catmull-Rom tangents (node handle editing coming)";
    }

    /// <summary>RDP simplification — remove redundant vertices within tolerance.</summary>
    public void SimplifyPath(double toleranceMm = 0.3)
    {
        if (SelectedPart is not { } part || FileById(part.FileId) is not { } file) return;
        Checkpoint();
        int before = 0, after = 0;
        foreach (var pg in file.Paths)
        {
            if (pg.Polyline is null) continue;
            before += pg.Polyline.Points.Count;
            var simplified = Backend.Cam.PathSimplifier.Simplify(
                pg.Polyline.Points, pg.Polyline.IsClosed, toleranceMm);
            pg.Polyline.Points.Clear();
            pg.Polyline.Points.AddRange(simplified);
            after += pg.Polyline.Points.Count;
        }
        RefreshGeometry(file);
        Refresh();
        StatusText = $"Simplified: {before} → {after} nodes ({before - after} removed, ε={toleranceMm}mm)";
    }

    public void SetNodeTypeSmoothSym()   => StatusText = "Smooth symmetric: node handle editing coming in a future task";
    public void SetNodeTypeSmoothAsym()  => StatusText = "Smooth asymmetric: node handle editing coming in a future task";
    public void SetNodeTypeCorner()      => StatusText = "Corner node: node handle editing coming in a future task";
    public void SetNodeTypeCusp()        => StatusText = "Cusp node: node handle editing coming in a future task";
    public void AlignNodesLeft()         => StatusText = "Align nodes left: requires multi-node select (coming in a future task)";
    public void AlignNodesHCenter()      => StatusText = "Align nodes horizontal center: requires multi-node select";
    public void AlignNodesRight()        => StatusText = "Align nodes right: requires multi-node select";
    public void AlignNodesTop()          => StatusText = "Align nodes top: requires multi-node select";
    public void AlignNodesVCenter()      => StatusText = "Align nodes vertical center: requires multi-node select";
    public void AlignNodesBottom()       => StatusText = "Align nodes bottom: requires multi-node select";
    public void ScaleSelectedNodes()     => StatusText = "Node transform: scale bounding box of selected nodes (coming)";
    public void AddOrJoinNode()          => StatusText = "Add node at midpoint / join open endpoints (coming)";

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

    /// <summary>Trace bitmap to vector geometry using the real BitmapTracer backend.</summary>
    public void TraceBitmap(ImportedFile file, Desktop.Views.BitmapTraceDialog dlg)
    {
        if (file.Kind != ImportedFileKind.Bitmap || file.BitmapData is null)
        {
            StatusText = "Not a bitmap file or no image data";
            return;
        }

        try
        {
            StatusText = "Tracing…";
            var settings = new Backend.Import.Bitmap.BitmapTraceSettings(
                ThresholdPercent: (int)(dlg.ThresholdValue / 255.0 * 100),
                Invert:           dlg.InvertColors,
                Mode:             dlg.Mode == Desktop.Views.BitmapTraceDialog.TraceMode.Outline
                                  ? Backend.Import.Bitmap.TraceMode.Outline
                                  : Backend.Import.Bitmap.TraceMode.Centerline,
                SimplifyToleranceMm: dlg.SimplifyTolerance
            );

            var (paths, widthMm, heightMm) = Backend.Import.Bitmap.BitmapTracer.Trace(file.BitmapData, settings);

            file.Paths.Clear();
            foreach (var poly in paths)
                file.Paths.Add(new PathGeometry { Polyline = poly });

            file.BitmapTraceSettingsJson = System.Text.Json.JsonSerializer.Serialize(settings);

            // Add a part if none exists yet for this file
            bool hasPart = _projects.With(p => p.Parts.Any(pt => pt.FileId == file.Id));
            if (!hasPart)
                _projects.Mutate(p => p.Parts.Add(PartPlacer.PlaceNew(p, file)));

            RefreshGeometry(file);
            Refresh();
            StatusText = paths.Count > 0
                ? $"Traced '{file.Name}' — {paths.Count} paths  ({widthMm:F1}×{heightMm:F1} mm)"
                : $"Trace produced no paths — try adjusting the threshold";
        }
        catch (Exception ex)
        {
            StatusText = $"Bitmap trace failed: {ex.Message}";
        }
    }

    // ── Guide lines ───────────────────────────────────────────────────────

    public void AddGuide(Guide guide)
    {
        Checkpoint();
        _projects.Mutate(p => p.Guides.Add(guide));
        NotifyPartChanged();
    }

    public void UpdateGuide(Guide _)   => NotifyPartChanged(); // guide already mutated in-place
    public void DeleteGuide(Guide guide)
    {
        Checkpoint();
        _projects.Mutate(p => p.Guides.Remove(guide));
        NotifyPartChanged();
    }

    public void DuplicateGuide(Guide guide)
    {
        Checkpoint();
        _projects.Mutate(p => p.Guides.Add(new Guide
        {
            Label    = guide.Label,
            X        = guide.X + (Math.Abs(guide.AngleDeg - 90) < 1 ? 10 : 0),
            Y        = guide.Y + (guide.AngleDeg < 1 ? 10 : 0),
            AngleDeg = guide.AngleDeg,
            IsLocked = guide.IsLocked,
        }));
        NotifyPartChanged();
    }
}
