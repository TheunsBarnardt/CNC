using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Backend.Cam;
using Backend.Geometry;
using Backend.Models;
using Backend.Simulation;
using Desktop.ViewModels;
using SkiaSharp;
using System.Collections.Generic;
using ModelPathGeometry = Backend.Models.PathGeometry;

namespace Desktop.Controls;

/// <summary>
/// SkiaSharp canvas that renders the cutting table, grid, rulers, and placed
/// parts. Handles pan / zoom / select / move / resize / rotate interactions.
/// </summary>
public sealed class ViewportControl : Control
{
    // ── view transform ────────────────────────────────────────────────────
    private float _scale = 0.5f;
    private float _tx = 20;
    private float _ty = 20;
    private bool _fitted;

    // ── display options ───────────────────────────────────────────────────
    private bool _darkCanvas = true;
    private bool _showGrid   = true;
    private bool _snap;
    private const int RULER_PX = 20;

    // ── live cursor + alignment-guide state ───────────────────────────────
    private Point? _cursor;
    private double? _guideX, _guideY;
    private float _gridMinorMm = 10;

    // ── data ──────────────────────────────────────────────────────────────
    private MainViewModel? _vm;
    private TableSettings  _table  = new();
    private List<Part>     _parts  = [];
    private List<Layer>    _layers = [];
    private Dictionary<Guid, List<ModelPathGeometry>> _geometry = new();
    private Guid? _selectedId;

    // ── simulation state ──────────────────────────────────────────────────
    public SimState? SimState { get; set; }

    // ── user guides ───────────────────────────────────────────────────────
    private List<Guide> _userGuides = [];

    // ── drag state ────────────────────────────────────────────────────────
    private enum DragMode { None, Pan, Move, Resize, Rotate, Select, MoveGuide, CreateGuide }
    private DragMode _drag;
    private Point    _dragStart;
    private float    _dragTx, _dragTy;
    private double   _partStartX, _partStartY;
    private int      _dragGuideIdx = -1;
    private double   _newGuideAngleDeg;

    // ── resize state ──────────────────────────────────────────────────────
    private int    _resizeHandle = -1;
    private double _resizeAnchorX, _resizeAnchorY;
    private double _localW, _localH;
    private bool   _resizeAffectsX, _resizeAffectsY;
    private bool   _anchorXOnRight, _anchorYOnTop;

    // ── rotate state ──────────────────────────────────────────────────────
    private double _rotateCenterX, _rotateCenterY;
    private double _rotateStartAngle;
    private double _origRotation;

    // ── draw tool state ────────────────────────────────────────────────────
    public enum DrawToolType { None, Line, Rectangle, Circle, Ellipse, Polygon, Star }
    private DrawToolType _drawTool = DrawToolType.None;
    private Point2? _drawStart;
    private Point2? _drawEnd;
    public event Action<DrawToolType, double, double, double, double>? DrawShapeRequested;
    public event Action? DrawToolCancelled;

    public void SetDrawTool(DrawToolType tool)
    {
        _drawTool  = tool;
        _drawStart = null;
        _drawEnd   = null;
        Cursor = tool != DrawToolType.None ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
        InvalidateVisual();
    }

    public void CancelDrawTool()
    {
        _drawTool  = DrawToolType.None;
        _drawStart = null;
        _drawEnd   = null;
        Cursor     = Cursor.Default;
        InvalidateVisual();
    }

    // ── pen tool state ─────────────────────────────────────────────────────
    private List<(double x, double y)> _penPoints = [];

    // ── node edit state ────────────────────────────────────────────────────
    private bool _nodeEditMode;
    private Guid? _editingPartId;
    private int? _selectedNodeIndex;
    private double _nodeHitRadius = 5;

    // ── events ────────────────────────────────────────────────────────────
    public event Action<Guid?    >? SelectionChanged;
    public event Action<Part     >? PartMoved;
    public event Action<Part     >? PartCommitted;
    /// <summary>Fires at the START of any move/resize/rotate drag — use to checkpoint undo.</summary>
    public event Action? TransformStarted;
    /// <summary>Fires when a node is selected in node-edit mode. Passes world X,Y or null when deselected.</summary>
    public event Action<double?, double?>? NodeSelected;
    /// <summary>Fires when user drags from a ruler to create a guide.</summary>
    public event Action<double, double, double>? GuideCreateRequested; // (worldX, worldY, angleDeg)
    /// <summary>Fires when user finishes moving a guide.</summary>
    public event Action<Guide>? GuideMoved;
    /// <summary>Fires when user double-clicks a guide line.</summary>
    public event Action<Guide>? GuideEditRequested;

    public ViewportControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    // ── public API ────────────────────────────────────────────────────────
    public void Attach(MainViewModel vm)
    {
        _vm = vm;
        vm.ProjectChanged += OnProjectChanged;
        OnProjectChanged();
    }

    public void SetDarkCanvas(bool dark) { _darkCanvas = dark; InvalidateVisual(); }
    public void SetShowGrid(bool show)   { _showGrid   = show;  InvalidateVisual(); }

    public bool SnapEnabled => _snap;
    public void SetSnap(bool snap)       { _snap = snap; InvalidateVisual(); }

    public void FitToView()
    {
        var b = Bounds;
        if (b.Width == 0 || b.Height == 0 || _table.WidthMm == 0) return;
        float margin = RULER_PX + 20;
        float aw = (float)b.Width  - margin * 2;
        float ah = (float)b.Height - margin * 2;
        _scale = Math.Min(aw / (float)_table.WidthMm, ah / (float)_table.HeightMm);
        _tx = margin + (aw - (float)_table.WidthMm * _scale) / 2;
        _ty = margin + (ah - (float)_table.HeightMm * _scale) / 2;
        InvalidateVisual();
    }

    // ── node edit mode ──────────────────────────────────────────────────
    public void EnterNodeEditMode(Part part)
    {
        _nodeEditMode = true;
        _editingPartId = part.Id;
        _selectedNodeIndex = null;
        InvalidateVisual();
    }

    public void ExitNodeEditMode()
    {
        _nodeEditMode = false;
        _editingPartId = null;
        _selectedNodeIndex = null;
        InvalidateVisual();
    }

    // ── data sync ─────────────────────────────────────────────────────────
    private void OnProjectChanged()
    {
        if (_vm is null) return;
        var p = _vm.Project;
        _table      = p.Table;
        _parts      = [.. p.Parts];
        _layers     = [.. p.Layers];
        _userGuides = [.. p.Guides];
        _geometry   = _vm.Geometry;
        _selectedId = _vm.SelectedPart?.Id;

        if (!_fitted && Bounds.Width > 0) { _fitted = true; FitToView(); }
        InvalidateVisual();
    }

    // ── coordinate helpers ────────────────────────────────────────────────
    private float ToSX(double worldX) => (float)(worldX * _scale + _tx);
    private float ToSY(double worldY) => (float)(Bounds.Height - (worldY * _scale + _ty));
    private double ToWX(float sx)     => (sx - _tx) / _scale;
    private double ToWY(float sy)     => (Bounds.Height - sy - _ty) / _scale;

    /// <summary>Parse "#rrggbb" into SKColor. Returns null for invalid / empty strings.</summary>
    private static SKColor? ParseHexColor(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#') return null;
        if (!int.TryParse(hex[1..], System.Globalization.NumberStyles.HexNumber, null, out int rgb)) return null;
        return new SKColor((byte)(rgb >> 16), (byte)((rgb >> 8) & 0xff), (byte)(rgb & 0xff));
    }

    // ── handle hit-testing ────────────────────────────────────────────────

    /// <summary>Returns handle index 0-7 if pointer is over a resize handle, else -1.</summary>
    private int HitTestHandle(Point screenPos)
    {
        if (!_selectedId.HasValue) return -1;
        var part = _parts.FirstOrDefault(p => p.Id == _selectedId.Value);
        if (part is null || !_geometry.TryGetValue(part.FileId, out var geom)) return -1;

        var (bMin, bMax) = WorldBounds(part, geom);
        float bx0 = ToSX(bMin.X), by0 = ToSY(bMax.Y);
        float bx1 = ToSX(bMax.X), by1 = ToSY(bMin.Y);
        float mx = (bx0 + bx1) / 2, my = (by0 + by1) / 2;
        float[] hx = [bx0, mx, bx1, bx0, bx1, bx0, mx, bx1];
        float[] hy = [by0, by0, by0, my,  my,  by1, by1, by1];

        const float TOL = 7f;
        for (int i = 0; i < 8; i++)
            if (Math.Abs(screenPos.X - hx[i]) <= TOL && Math.Abs(screenPos.Y - hy[i]) <= TOL)
                return i;
        return -1;
    }

    /// <summary>Returns true if pointer is over the rotation handle (circle above top-center).</summary>
    private bool HitTestRotateHandle(Point screenPos)
    {
        if (!_selectedId.HasValue) return false;
        var part = _parts.FirstOrDefault(p => p.Id == _selectedId.Value);
        if (part is null || !_geometry.TryGetValue(part.FileId, out var geom)) return false;

        var (bMin, bMax) = WorldBounds(part, geom);
        float bx0 = ToSX(bMin.X), bx1 = ToSX(bMax.X);
        float by0 = ToSY(bMax.Y);
        float rx = (bx0 + bx1) / 2;
        float ry = by0 - 22;

        float dx = (float)(screenPos.X - rx);
        float dy = (float)(screenPos.Y - ry);
        return dx * dx + dy * dy <= 8 * 8;
    }

    /// <summary>Sets resize anchor and flags from handle index. Must call AFTER computing WorldBounds.</summary>
    private void SetResizeState(int hIdx, (Point2 Min, Point2 Max) bounds, Part part)
    {
        double cx = (bounds.Min.X + bounds.Max.X) / 2;
        double cy = (bounds.Min.Y + bounds.Max.Y) / 2;

        // Which anchor world X/Y stays fixed (opposite side from the dragged handle)
        _resizeAnchorX = hIdx switch {
            0 or 3 or 5 => bounds.Max.X,
            1 or 6      => cx,
            _           => bounds.Min.X,
        };
        _resizeAnchorY = hIdx switch {
            0 or 1 or 2 => bounds.Min.Y,
            3 or 4      => cy,
            _           => bounds.Max.Y,
        };

        _anchorXOnRight = (hIdx == 0 || hIdx == 3 || hIdx == 5);
        _anchorYOnTop   = (hIdx == 5 || hIdx == 6 || hIdx == 7);

        _resizeAffectsX = (hIdx != 1 && hIdx != 6);
        _resizeAffectsY = (hIdx != 3 && hIdx != 4);

        _resizeHandle = hIdx;

        // Get local (un-scaled) file dimensions
        if (_vm?.FileById(part.FileId) is { } file)
        {
            var bb = file.BoundingBox;
            _localW = bb.Width;
            _localH = bb.Height;
        }
        else
        {
            // Fallback: infer from world bounds / current scale
            _localW = Math.Abs(part.ScaleX) > 1e-9 ? (bounds.Max.X - bounds.Min.X) / Math.Abs(part.ScaleX) : 1;
            _localH = Math.Abs(part.ScaleY) > 1e-9 ? (bounds.Max.Y - bounds.Min.Y) / Math.Abs(part.ScaleY) : 1;
        }
    }

    // ── interaction ───────────────────────────────────────────────────────
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var mods = e.KeyModifiers;

        if (mods.HasFlag(KeyModifiers.Control))
        {
            // Ctrl + wheel → zoom toward cursor
            var pos = e.GetPosition(this);
            float factor   = e.Delta.Y > 0 ? 1.12f : 0.89f;
            float newScale = Math.Clamp(_scale * factor, 0.02f, 100f);
            float wx = (float)(pos.X - _tx) / _scale;
            float wy = (float)(Bounds.Height - pos.Y - _ty) / _scale;
            _tx = (float)pos.X - wx * newScale;
            _ty = (float)(Bounds.Height - pos.Y) - wy * newScale;
            _scale = newScale;
        }
        else if (mods.HasFlag(KeyModifiers.Shift))
        {
            // Shift + wheel → pan left/right
            _tx += (float)(e.Delta.Y * 60);
        }
        else
        {
            // Plain wheel → pan up/down (Y-up world: positive delta scrolls up)
            _ty += (float)(e.Delta.Y * 60);
        }

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        // Pen tool: left-click collects points
        if (_vm?.PenToolActive == true && props.IsLeftButtonPressed)
        {
            if (pos.X >= RULER_PX && pos.Y >= RULER_PX)
            {
                _penPoints.Add((ToWX((float)pos.X), ToWY((float)pos.Y)));
                InvalidateVisual();
            }
            return;
        }

        // Draw tool: start a shape drag
        if (_drawTool != DrawToolType.None && props.IsLeftButtonPressed
            && pos.X >= RULER_PX && pos.Y >= RULER_PX)
        {
            double wx = ToWX((float)pos.X), wy = ToWY((float)pos.Y);
            if (_snap && _gridMinorMm > 0)
            { wx = Math.Round(wx / _gridMinorMm) * _gridMinorMm; wy = Math.Round(wy / _gridMinorMm) * _gridMinorMm; }
            _drawStart = new Point2(wx, wy);
            _drawEnd   = _drawStart;
            e.Pointer.Capture(this);
            return;
        }

        // Node edit mode
        if (_nodeEditMode && props.IsLeftButtonPressed)
        {
            if (pos.X >= RULER_PX && pos.Y >= RULER_PX)
                HitTestNode(ToWX((float)pos.X), ToWY((float)pos.Y));
            return;
        }

        // Double-click → enter node edit
        if (props.IsLeftButtonPressed && e.ClickCount == 2)
        {
            if (pos.X >= RULER_PX && pos.Y >= RULER_PX)
            {
                Part? dpart = HitTestPartAtPoint(ToWX((float)pos.X), ToWY((float)pos.Y));
                if (dpart is not null)
                {
                    EnterNodeEditMode(dpart);
                    _vm!.StatusText = "Node edit — double-click edits nodes. Esc to exit.";
                    return;
                }
            }
        }

        // Middle/right → pan
        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed)
        {
            _drag = DragMode.Pan;
            _dragStart = pos;
            _dragTx = _tx; _dragTy = _ty;
            e.Pointer.Capture(this);
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        // ── Drag from ruler → create a guide ─────────────────────────────
        if (pos.X < RULER_PX && pos.Y >= RULER_PX)
        {
            _newGuideAngleDeg = 90; // vertical guide from left ruler
            _drag = DragMode.CreateGuide;
            _dragStart = pos;
            e.Pointer.Capture(this);
            return;
        }
        if (pos.Y < RULER_PX && pos.X >= RULER_PX)
        {
            _newGuideAngleDeg = 0; // horizontal guide from top ruler
            _drag = DragMode.CreateGuide;
            _dragStart = pos;
            e.Pointer.Capture(this);
            return;
        }

        if (pos.X < RULER_PX || pos.Y < RULER_PX) return;

        // ── Check rotation handle first (small target, must win) ──────────
        if (_selectedId.HasValue && HitTestRotateHandle(pos))
        {
            var part = _parts.FirstOrDefault(p => p.Id == _selectedId.Value);
            if (part is not null && _geometry.TryGetValue(part.FileId, out var geom))
            {
                var (bMin, bMax) = WorldBounds(part, geom);
                _rotateCenterX   = (bMin.X + bMax.X) / 2;
                _rotateCenterY   = (bMin.Y + bMax.Y) / 2;
                _origRotation    = part.RotationDeg;
                double wx = ToWX((float)pos.X), wy = ToWY((float)pos.Y);
                _rotateStartAngle = Math.Atan2(wy - _rotateCenterY, wx - _rotateCenterX);
                _drag      = DragMode.Rotate;
                _dragStart = pos;
                e.Pointer.Capture(this);
                TransformStarted?.Invoke();
                return;
            }
        }

        // ── Check resize handles ──────────────────────────────────────────
        int hIdx = HitTestHandle(pos);
        if (hIdx >= 0)
        {
            var part = _parts.FirstOrDefault(p => p.Id == _selectedId!.Value);
            if (part is not null && _geometry.TryGetValue(part.FileId, out var geom))
            {
                var bounds = WorldBounds(part, geom);
                SetResizeState(hIdx, bounds, part);
                _partStartX   = part.X;
                _partStartY   = part.Y;
                _drag         = DragMode.Resize;
                _dragStart    = pos;
                e.Pointer.Capture(this);
                TransformStarted?.Invoke();
                return;
            }
        }

        // ── Hit-test guides ───────────────────────────────────────────────
        {
            int gi = HitTestGuide(pos);
            if (gi >= 0)
            {
                if (e.ClickCount == 2)
                {
                    GuideEditRequested?.Invoke(_userGuides[gi]);
                    e.Handled = true;
                    return;
                }
                if (!_userGuides[gi].IsLocked)
                {
                    _dragGuideIdx = gi;
                    _drag = DragMode.MoveGuide;
                    _dragStart = pos;
                    e.Pointer.Capture(this);
                    return;
                }
            }
        }

        // ── Hit-test parts ────────────────────────────────────────────────
        double pwx = ToWX((float)pos.X);
        double pwy = ToWY((float)pos.Y);
        Part? hit = null;
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            var part = _parts[i];
            var layer = _layers.FirstOrDefault(l => l.Id == part.LayerId);
            if (layer is { Visible: false } || layer is { Locked: true }) continue;
            if (!_geometry.TryGetValue(part.FileId, out var geom)) continue;
            if (HitTestPart(part, geom, pwx, pwy)) { hit = part; break; }
        }

        if (hit is not null)
        {
            _selectedId = hit.Id;
            SelectionChanged?.Invoke(hit.Id);
            _drag         = DragMode.Move;
            _dragStart    = pos;
            _partStartX   = hit.X;
            _partStartY   = hit.Y;
            e.Pointer.Capture(this);
            TransformStarted?.Invoke();
        }
        else
        {
            // Click on empty → deselect + rubber-band
            _selectedId = null;
            SelectionChanged?.Invoke(null);
            _drag = DragMode.Select;
            _dragStart = pos;
            e.Pointer.Capture(this);
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        _cursor = pos;

        // Draw tool: update live preview end point
        if (_drawTool != DrawToolType.None && _drawStart.HasValue
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            double wx = ToWX((float)pos.X), wy = ToWY((float)pos.Y);
            if (_snap && _gridMinorMm > 0)
            { wx = Math.Round(wx / _gridMinorMm) * _gridMinorMm; wy = Math.Round(wy / _gridMinorMm) * _gridMinorMm; }
            _drawEnd = new Point2(wx, wy);
            InvalidateVisual();
            return;
        }

        if (_drag == DragMode.None) { InvalidateVisual(); return; }

        double dx = pos.X - _dragStart.X;
        double dy = pos.Y - _dragStart.Y;

        switch (_drag)
        {
            case DragMode.Pan:
                _tx = _dragTx + (float)dx;
                _ty = _dragTy - (float)dy;
                break;

            case DragMode.Move when _selectedId.HasValue:
            {
                var part = _parts.FirstOrDefault(p => p.Id == _selectedId.Value);
                if (part is not null)
                {
                    part.X = _partStartX + dx / _scale;
                    part.Y = _partStartY - dy / _scale;
                    ApplyDragAssist(part);
                    PartMoved?.Invoke(part);
                }
                break;
            }

            case DragMode.Resize when _selectedId.HasValue:
            {
                var part = _parts.FirstOrDefault(p => p.Id == _selectedId.Value);
                if (part is not null) ApplyResize(part, pos);
                break;
            }

            case DragMode.Rotate when _selectedId.HasValue:
            {
                var part = _parts.FirstOrDefault(p => p.Id == _selectedId.Value);
                if (part is not null) ApplyRotate(part, pos, e.KeyModifiers);
                break;
            }

            case DragMode.Select:
                // no state update needed — just redraw the rubber-band
                break;

            case DragMode.MoveGuide when _dragGuideIdx >= 0:
            {
                var g = _userGuides[_dragGuideIdx];
                // Vertical (≈90°): follow mouse X; Horizontal (≈0°): follow mouse Y;
                // Angled: move pass-through point to mouse
                double mwx = ToWX((float)pos.X), mwy = ToWY((float)pos.Y);
                if (Math.Abs(g.AngleDeg - 90) < 1) g.X = mwx;
                else if (g.AngleDeg < 1)            g.Y = mwy;
                else { g.X = mwx; g.Y = mwy; }
                break;
            }
            // CreateGuide: cursor position visible in render via _cursor — no extra state needed
        }
        InvalidateVisual();
    }

    private void ApplyResize(Part part, Point pos)
    {
        if (_localW < 1e-9 || _localH < 1e-9) return;

        double mouseWX = ToWX((float)pos.X);
        double mouseWY = ToWY((float)pos.Y);
        double halfW   = _localW / 2;
        double halfH   = _localH / 2;

        double newScaleX = Math.Abs(part.ScaleX);
        double newScaleY = Math.Abs(part.ScaleY);
        double newX = part.X;
        double newY = part.Y;

        if (_resizeAffectsX)
        {
            double newW = Math.Abs(mouseWX - _resizeAnchorX);
            newScaleX   = Math.Max(0.01, newW / _localW);
            // Ensure the anchor edge stays fixed in world space.
            // bMax.X = Part.X + halfW * (1 + ScaleX)  →  newX = anchor - halfW*(1+newScaleX)
            // bMin.X = Part.X + halfW * (1 - ScaleX)  →  newX = anchor - halfW*(1-newScaleX)
            newX = _anchorXOnRight
                ? _resizeAnchorX - halfW * (1 + newScaleX)
                : _resizeAnchorX - halfW * (1 - newScaleX);
        }

        if (_resizeAffectsY)
        {
            double newH = Math.Abs(mouseWY - _resizeAnchorY);
            newScaleY   = Math.Max(0.01, newH / _localH);
            // bMax.Y = Part.Y + halfH * (1 + ScaleY)  →  newY = anchor - halfH*(1+newScaleY)
            // bMin.Y = Part.Y + halfH * (1 - ScaleY)  →  newY = anchor - halfH*(1-newScaleY)
            newY = _anchorYOnTop
                ? _resizeAnchorY - halfH * (1 + newScaleY)
                : _resizeAnchorY - halfH * (1 - newScaleY);
        }

        part.X      = newX;
        part.Y      = newY;
        part.ScaleX = newScaleX;
        part.ScaleY = newScaleY;
        PartMoved?.Invoke(part);
    }

    private void ApplyRotate(Part part, Point pos, KeyModifiers mods)
    {
        double wx = ToWX((float)pos.X);
        double wy = ToWY((float)pos.Y);
        double currentAngle = Math.Atan2(wy - _rotateCenterY, wx - _rotateCenterX);
        double delta = (currentAngle - _rotateStartAngle) * 180 / Math.PI;
        double newRot = (_origRotation + delta % 360 + 360) % 360;
        if (mods.HasFlag(KeyModifiers.Control))
            newRot = Math.Round(newRot / 15) * 15;
        part.RotationDeg = newRot;
        PartMoved?.Invoke(part);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _cursor = null;
        InvalidateVisual();
    }

    private void ApplyDragAssist(Part part)
    {
        _guideX = _guideY = null;
        if (!_geometry.TryGetValue(part.FileId, out var geom)) return;

        var (mn, mx) = WorldBounds(part, geom);
        double cx = (mn.X + mx.X) / 2, cy = (mn.Y + mx.Y) / 2;
        double tol = 6 / _scale;

        var targetsX = new List<double> { 0, _table.WidthMm / 2, _table.WidthMm };
        var targetsY = new List<double> { 0, _table.HeightMm / 2, _table.HeightMm };
        // Add user-guide positions as snap targets
        foreach (var g in _userGuides)
        {
            if (Math.Abs(g.AngleDeg - 90) < 1) targetsX.Add(g.X); // vertical guide
            else if (g.AngleDeg < 1)            targetsY.Add(g.Y); // horizontal guide
        }
        foreach (var other in _parts)
        {
            if (other.Id == part.Id) continue;
            if (!_geometry.TryGetValue(other.FileId, out var og)) continue;
            var (omn, omx) = WorldBounds(other, og);
            targetsX.Add(omn.X); targetsX.Add((omn.X + omx.X) / 2); targetsX.Add(omx.X);
            targetsY.Add(omn.Y); targetsY.Add((omn.Y + omx.Y) / 2); targetsY.Add(omx.Y);
        }

        double bestDx = tol; bool foundX = false; double guideX = 0;
        foreach (var t in targetsX)
            foreach (var r in new[] { mn.X, cx, mx.X })
                if (Math.Abs(t - r) < bestDx) { bestDx = Math.Abs(t - r); part.X += t - r; guideX = t; foundX = true; }
        if (foundX) _guideX = guideX;

        double bestDy = tol; bool foundY = false; double guideY = 0;
        foreach (var t in targetsY)
            foreach (var r in new[] { mn.Y, cy, mx.Y })
                if (Math.Abs(t - r) < bestDy) { bestDy = Math.Abs(t - r); part.Y += t - r; guideY = t; foundY = true; }
        if (foundY) _guideY = guideY;

        if (_snap && _gridMinorMm > 0)
        {
            if (!foundX) part.X = Math.Round(part.X / _gridMinorMm) * _gridMinorMm;
            if (!foundY) part.Y = Math.Round(part.Y / _gridMinorMm) * _gridMinorMm;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pos = e.GetPosition(this);

        // Draw tool: finalize shape on mouse-up
        if (_drawTool != DrawToolType.None && _drawStart.HasValue && _drawEnd.HasValue)
        {
            var s = _drawStart.Value;
            var d = _drawEnd.Value;
            if (Math.Abs(s.X - d.X) > 0.5 || Math.Abs(s.Y - d.Y) > 0.5)
                DrawShapeRequested?.Invoke(_drawTool, s.X, s.Y, d.X, d.Y);
            _drawStart = null;
            _drawEnd   = null;
            InvalidateVisual();
            e.Pointer.Capture(null);
            return;
        }

        switch (_drag)
        {
            case DragMode.Move:
            case DragMode.Resize:
            case DragMode.Rotate:
                if (_selectedId.HasValue)
                {
                    var part = _parts.FirstOrDefault(p => p.Id == _selectedId.Value);
                    if (part is not null) PartCommitted?.Invoke(part);
                }
                _guideX = _guideY = null;
                break;

            case DragMode.Select:
                FinalizeRubberBand(pos);
                break;

            case DragMode.MoveGuide when _dragGuideIdx >= 0:
                GuideMoved?.Invoke(_userGuides[_dragGuideIdx]);
                _dragGuideIdx = -1;
                break;

            case DragMode.CreateGuide:
                // Only create if mouse reached the canvas (past both rulers)
                if (pos.X >= RULER_PX && pos.Y >= RULER_PX)
                {
                    double gx = _newGuideAngleDeg == 90 ? ToWX((float)pos.X) : 0;
                    double gy = _newGuideAngleDeg == 0  ? ToWY((float)pos.Y) : 0;
                    GuideCreateRequested?.Invoke(gx, gy, _newGuideAngleDeg);
                }
                break;
        }

        _drag = DragMode.None;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private void FinalizeRubberBand(Point endPos)
    {
        // Convert screen rect to world rect
        double wx0 = Math.Min(ToWX((float)_dragStart.X), ToWX((float)endPos.X));
        double wx1 = Math.Max(ToWX((float)_dragStart.X), ToWX((float)endPos.X));
        double wy0 = Math.Min(ToWY((float)_dragStart.Y), ToWY((float)endPos.Y));
        double wy1 = Math.Max(ToWY((float)_dragStart.Y), ToWY((float)endPos.Y));
        double minSize = 3 / _scale;  // must drag at least 3px to count as selection

        if (wx1 - wx0 < minSize && wy1 - wy0 < minSize) return;  // tiny drag → keep current selection

        // Select topmost part whose AABB overlaps the band
        Part? selected = null;
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            var part = _parts[i];
            var layer = _layers.FirstOrDefault(l => l.Id == part.LayerId);
            if (layer is { Visible: false } || layer is { Locked: true }) continue;
            if (!_geometry.TryGetValue(part.FileId, out var geom)) continue;
            var (mn, mx) = WorldBounds(part, geom);
            if (mx.X >= wx0 && mn.X <= wx1 && mx.Y >= wy0 && mn.Y <= wy1)
            { selected = part; break; }
        }

        _selectedId = selected?.Id;
        SelectionChanged?.Invoke(_selectedId);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Escape cancels active draw tool
        if (_drawTool != DrawToolType.None && e.Key == Key.Escape)
        {
            CancelDrawTool();
            DrawToolCancelled?.Invoke();
            e.Handled = true;
            return;
        }

        if (_nodeEditMode)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    ExitNodeEditMode();
                    if (_vm is not null) _vm.StatusText = "Node edit exited";
                    e.Handled = true;
                    break;
                case Key.Delete: case Key.Back:
                    if (_selectedNodeIndex.HasValue)
                    {
                        if (_vm is not null) _vm.StatusText = "Node deletion not yet implemented";
                        e.Handled = true;
                    }
                    break;
            }
            if (e.Handled) return;
        }

        if (_vm?.PenToolActive == true)
        {
            switch (e.Key)
            {
                case Key.Return:
                    bool closed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                    _vm.CreatePathFromPoints(_penPoints, closed);
                    _penPoints.Clear();
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    _vm.CancelPenTool();
                    _penPoints.Clear();
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Back: case Key.Delete:
                    if (_penPoints.Count > 0) { _penPoints.RemoveAt(_penPoints.Count - 1); InvalidateVisual(); e.Handled = true; }
                    break;
            }
            if (e.Handled) return;
        }

        var selPart = _selectedId.HasValue
            ? _parts.FirstOrDefault(p => p.Id == _selectedId.Value)
            : null;
        if (selPart is null) return;
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Delete: case Key.Back:
                _vm?.DeleteSelected();
                break;
            case Key.Left:  selPart.X -= step; PartCommitted?.Invoke(selPart); InvalidateVisual(); e.Handled = true; break;
            case Key.Right: selPart.X += step; PartCommitted?.Invoke(selPart); InvalidateVisual(); e.Handled = true; break;
            case Key.Up:    selPart.Y += step; PartCommitted?.Invoke(selPart); InvalidateVisual(); e.Handled = true; break;
            case Key.Down:  selPart.Y -= step; PartCommitted?.Invoke(selPart); InvalidateVisual(); e.Handled = true; break;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!_fitted && finalSize.Width > 0) { _fitted = true; FitToView(); }
        return base.ArrangeOverride(finalSize);
    }

    // ── render ────────────────────────────────────────────────────────────
    public override void Render(DrawingContext context) =>
        context.Custom(new DrawOp(this, new Rect(Bounds.Size)));

    private void RenderScene(SKCanvas canvas, float w, float h)
    {
        SKColor colBg     = _darkCanvas ? new(0x1a,0x1a,0x1a) : new(0xe8,0xe8,0xe8);
        SKColor colTable  = _darkCanvas ? new(0x28,0x28,0x28) : new(0xff,0xff,0xff);
        SKColor colGrid   = _darkCanvas ? new(0xff,0xff,0xff,0x14) : new(0x00,0x00,0x00,0x14);
        SKColor colFg     = _darkCanvas ? new(0xe0,0xe0,0xe0) : new(0x1a,0x1a,0x1a);
        SKColor colBorder = _darkCanvas ? new(0x44,0x44,0x44) : new(0xc0,0xc0,0xc0);
        SKColor colPrim   = new(0x3b,0x82,0xf6);
        SKColor colCyan   = new(0x00,0xBE,0xFE);
        SKColor colRed    = new(0xef,0x44,0x44);
        SKColor colRuler  = _darkCanvas ? new(0x20,0x20,0x20) : new(0xd4,0xd4,0xd4);
        SKColor colMuted  = _darkCanvas ? new(0x60,0x60,0x60) : new(0x99,0x99,0x99);

        canvas.Clear(colBg);

        float tx0 = ToSX(0), ty0 = ToSY((float)_table.HeightMm);
        float tw  = (float)_table.WidthMm * _scale;
        float th  = (float)_table.HeightMm * _scale;

        using var tablePaint = new SKPaint { Color = colTable, IsAntialias = false };
        canvas.DrawRect(tx0, ty0, tw, th, tablePaint);

        if (_showGrid && _scale > 0.05f)
        {
            float[] stepsM = [1, 2, 5, 10, 25, 50, 100, 250, 500];
            float minor = stepsM.FirstOrDefault(s => s * _scale >= 8, 500);
            float major = minor * 5;
            _gridMinorMm = minor;
            using var gp = new SKPaint { Color = colGrid, StrokeWidth = 1, IsAntialias = false };
            for (float gx = 0; gx <= _table.WidthMm + 0.5; gx += minor)
            {
                gp.Color = gx % major < 0.001 ? new(colGrid.Red, colGrid.Green, colGrid.Blue, 0x28) : colGrid;
                float sx = ToSX(gx);
                canvas.DrawLine(sx, ty0, sx, ty0 + th, gp);
            }
            for (float gy = 0; gy <= _table.HeightMm + 0.5; gy += minor)
            {
                gp.Color = gy % major < 0.001 ? new(colGrid.Red, colGrid.Green, colGrid.Blue, 0x28) : colGrid;
                float sy = ToSY(gy);
                canvas.DrawLine(tx0, sy, tx0 + tw, sy, gp);
            }
        }

        using var bdrP = new SKPaint { Color = colBorder, StrokeWidth = 1.5f, IsStroke = true, IsAntialias = true };
        canvas.DrawRect(tx0, ty0, tw, th, bdrP);

        float ox = ToSX(0), oy = ToSY(0);
        using var origP = new SKPaint { Color = colPrim, StrokeWidth = 1.5f, IsStroke = true, IsAntialias = true };
        canvas.DrawLine(ox - 7, oy, ox + 7, oy, origP);
        canvas.DrawLine(ox, oy - 7, ox, oy + 7, origP);
        canvas.DrawCircle(ox, oy, 4, origP);

        // ── parts ─────────────────────────────────────────────────────────
        foreach (var part in _parts)
        {
            if (!_geometry.TryGetValue(part.FileId, out var geom)) continue;
            var layer = _layers.FirstOrDefault(l => l.Id == part.LayerId);
            if (layer is { Visible: false }) continue;

            bool selected  = part.Id == _selectedId;
            bool isCutout  = part.IsCutout;
            bool outBounds = IsOutOfBounds(part, geom);
            SKColor layerCol = ParseHexColor(layer?.Color) ?? colCyan;
            SKColor strokeCol = outBounds ? colRed : selected ? colPrim : layerCol;
            var pivot = LocalCenter(geom);

            foreach (var pg in geom)
            {
                if (pg.Polyline.Points.Count < 2) continue;
                using var path = BuildPath(part, pivot, pg);
                bool isClosed = pg.Polyline.IsClosed;

                if (isClosed)
                {
                    float fillAlpha = isCutout ? 0.06f : (selected ? 0.14f : 0.07f);
                    using var fp = new SKPaint { Color = strokeCol.WithAlpha((byte)(fillAlpha * 255)), IsAntialias = true };
                    canvas.DrawPath(path, fp);

                    if (isCutout)
                    {
                        canvas.Save();
                        canvas.ClipPath(path);
                        using var hp = new SKPaint { Color = strokeCol.WithAlpha(0x28), StrokeWidth = 1, IsStroke = true };
                        var bds = path.Bounds;
                        for (float d = -bds.Height; d < bds.Width + bds.Height; d += 7)
                            canvas.DrawLine(bds.Left + d, bds.Top, bds.Left + d + bds.Height, bds.Bottom, hp);
                        canvas.Restore();
                    }
                }

                float lw = selected ? 2f : 1.25f;
                using var sp = new SKPaint { Color = strokeCol, StrokeWidth = lw, IsStroke = true, IsAntialias = true };
                if (isCutout) sp.PathEffect = SKPathEffect.CreateDash([5f, 3f], 0);
                canvas.DrawPath(path, sp);
            }

            // Selection box, resize handles, and rotation handle
            if (selected)
            {
                var (bMin, bMax) = WorldBounds(part, geom);
                float bx0 = ToSX(bMin.X), by0 = ToSY(bMax.Y);
                float bx1 = ToSX(bMax.X), by1 = ToSY(bMin.Y);

                // Dashed selection rect
                using var selP = new SKPaint
                {
                    Color = colFg.WithAlpha(0xaa), StrokeWidth = 1, IsStroke = true,
                    PathEffect = SKPathEffect.CreateDash([4f, 4f], 0),
                };
                canvas.DrawRect(SKRect.Create(bx0, by0, bx1 - bx0, by1 - by0), selP);

                // 8 resize handles (corners + edge midpoints)
                float hmx = (bx0 + bx1) / 2, hmy = (by0 + by1) / 2;
                float[] hx = [bx0, hmx, bx1, bx0, bx1, bx0, hmx, bx1];
                float[] hy = [by0, by0, by0, hmy, hmy, by1, by1, by1];
                using var hFill   = new SKPaint { Color = _darkCanvas ? new(0x30,0x30,0x30) : SKColors.White };
                using var hStroke = new SKPaint { Color = colFg.WithAlpha(0xdd), StrokeWidth = 1.5f, IsStroke = true, IsAntialias = true };
                for (int i = 0; i < 8; i++)
                {
                    var r = SKRect.Create(hx[i] - 4, hy[i] - 4, 8, 8);
                    canvas.DrawRect(r, hFill);
                    canvas.DrawRect(r, hStroke);
                }

                // Rotation handle: circle above top-center
                float rx = hmx, ry = by0 - 22;
                using var stemP = new SKPaint { Color = colFg.WithAlpha(0x88), StrokeWidth = 1, IsStroke = true };
                canvas.DrawLine(rx, by0 - 5, rx, ry + 6, stemP);
                using var rotFill   = new SKPaint { Color = _darkCanvas ? new(0x30,0x30,0x30) : SKColors.White, IsAntialias = true };
                using var rotStroke = new SKPaint { Color = new(0xff,0xaa,0x00), StrokeWidth = 1.5f, IsStroke = true, IsAntialias = true };
                canvas.DrawCircle(rx, ry, 6, rotFill);
                canvas.DrawCircle(rx, ry, 6, rotStroke);

                // Tab markers: small perpendicular ticks at each tab boundary
                if (part.TabCount > 0)
                {
                    using var tabP = new SKPaint { Color = new SKColor(0xff, 0x80, 0x00), StrokeWidth = 2f, IsStroke = true, IsAntialias = true };
                    using var tabFill = new SKPaint { Color = new SKColor(0xff, 0x80, 0x00, 0x30), IsAntialias = true };
                    foreach (var pg in geom)
                    {
                        if (pg.Polyline.Points.Count < 2 || !pg.Polyline.IsClosed) continue;
                        var wPts = pg.Polyline.Points
                            .Select(p => PartTransform.Apply(part, LocalCenter(geom), p))
                            .ToList();
                        var (tabPts, tabSpans) = TabBuilder.Apply(wPts, part.TabCount, part.TabWidthMm);
                        foreach (var (start, end) in tabSpans)
                        {
                            // Draw a small filled rect across the path at each tab span
                            float sx0 = ToSX((float)tabPts[start].X), sy0 = ToSY((float)tabPts[start].Y);
                            float sx1 = ToSX((float)tabPts[end].X),   sy1 = ToSY((float)tabPts[end].Y);
                            // Segment direction for perpendicular tick
                            float dx = sx1 - sx0, dy = sy1 - sy0;
                            float len = MathF.Sqrt(dx * dx + dy * dy);
                            if (len > 0.5f)
                            {
                                float nx = -dy / len * 5, ny = dx / len * 5;
                                using var tabPath2 = new SKPath();
                                tabPath2.MoveTo(sx0 + nx, sy0 + ny);
                                tabPath2.LineTo(sx1 + nx, sy1 + ny);
                                tabPath2.LineTo(sx1 - nx, sy1 - ny);
                                tabPath2.LineTo(sx0 - nx, sy0 - ny);
                                tabPath2.Close();
                                canvas.DrawPath(tabPath2, tabFill);
                                canvas.DrawPath(tabPath2, tabP);
                            }
                        }
                    }
                }
            }
        }

        // Simulation torch head
        if (SimState is { } ss)
        {
            float sx = ToSX(ss.Position.X), sy = ToSY(ss.Position.Y);
            using var torchPaint = new SKPaint { Color = new(0xff, 0x80, 0x00), IsAntialias = true };
            if (ss.TorchOn)
            {
                torchPaint.Color = new(0xff, 0x60, 0x00);
                canvas.DrawCircle(sx, sy, 6, torchPaint);
                torchPaint.Color = new(0xff, 0xcc, 0x00, 0x80);
                canvas.DrawCircle(sx, sy, 12, torchPaint);
            }
            else
            {
                torchPaint.Color = new(0x88, 0x88, 0x88);
                torchPaint.IsStroke = true;
                torchPaint.StrokeWidth = 1.5f;
                canvas.DrawCircle(sx, sy, 5, torchPaint);
            }
        }

        // Pen tool points
        if (_vm?.PenToolActive == true && _penPoints.Count > 0)
        {
            using var penPaint  = new SKPaint { Color = colPrim, StrokeWidth = 2, IsAntialias = true };
            using var linePaint = new SKPaint { Color = colPrim.WithAlpha(0x80), StrokeWidth = 1, IsStroke = true, IsAntialias = true };
            for (int i = 1; i < _penPoints.Count; i++)
                canvas.DrawLine(ToSX(_penPoints[i-1].x), ToSY(_penPoints[i-1].y),
                                ToSX(_penPoints[i].x),   ToSY(_penPoints[i].y), linePaint);
            foreach (var pt in _penPoints)
                canvas.DrawCircle(ToSX(pt.x), ToSY(pt.y), 4, penPaint);
            using var statusPaint = new SKPaint { Color = colPrim, TextSize = 12, IsAntialias = true };
            canvas.DrawText($"Pen: {_penPoints.Count} pts  (Enter=finish  Shift+Enter=close  Esc=cancel)", 30, 45, statusPaint);
        }

        // Node edit overlays
        if (_nodeEditMode && _editingPartId.HasValue)
        {
            var part = _parts.FirstOrDefault(p => p.Id == _editingPartId.Value);
            if (part is not null && _geometry.TryGetValue(part.FileId, out var geom))
            {
                var pivot = LocalCenter(geom);
                using var nodePaint   = new SKPaint { Color = colPrim, IsAntialias = true };
                using var selectPaint = new SKPaint { Color = colRed,  IsAntialias = true };
                using var handlePaint = new SKPaint { Color = colCyan, StrokeWidth = 1, IsStroke = true, IsAntialias = true };
                using var stemPaint   = new SKPaint { Color = colCyan, StrokeWidth = 1, IsStroke = true, IsAntialias = true };

                int nodeIdx = 0;
                foreach (var pg in geom)
                {
                    int pathNodeIdx = 0;
                    foreach (var local in pg.Polyline.Points)
                    {
                        var world = PartTransform.Apply(part, pivot, local);
                        float nsx = ToSX(world.X), nsy = ToSY(world.Y);
                        bool isSel  = nodeIdx == _selectedNodeIndex;
                        bool hasHdl = pg.Handles != null && pathNodeIdx < pg.Handles.Count && pg.Handles[pathNodeIdx] != null;

                        if (hasHdl) canvas.DrawCircle(nsx, nsy, 5, isSel ? selectPaint : nodePaint);
                        else        canvas.DrawRect(nsx - 4, nsy - 4, 8, 8, isSel ? selectPaint : nodePaint);

                        if (isSel && hasHdl && pg.Handles![pathNodeIdx] is { } handle)
                        {
                            if (handle.Length >= 2)
                            {
                                var ih = PartTransform.Apply(part, pivot, new(handle[0], handle[1]));
                                canvas.DrawLine(nsx, nsy, ToSX(ih.X), ToSY(ih.Y), stemPaint);
                                canvas.DrawRect(ToSX(ih.X)-3, ToSY(ih.Y)-3, 6, 6, handlePaint);
                            }
                            if (handle.Length >= 4)
                            {
                                var oh = PartTransform.Apply(part, pivot, new(handle[2], handle[3]));
                                canvas.DrawLine(nsx, nsy, ToSX(oh.X), ToSY(oh.Y), stemPaint);
                                canvas.DrawRect(ToSX(oh.X)-3, ToSY(oh.Y)-3, 6, 6, handlePaint);
                            }
                        }
                        nodeIdx++; pathNodeIdx++;
                    }
                }
                using var s2 = new SKPaint { Color = colPrim, TextSize = 12, IsAntialias = true };
                canvas.DrawText($"Node edit: {nodeIdx} nodes  (Esc to exit)", 30, 45, s2);
            }
        }

        // Rubber-band selection rect
        if (_drag == DragMode.Select && _cursor is { } cur)
        {
            float sx0 = (float)Math.Min(_dragStart.X, cur.X);
            float sy0 = (float)Math.Min(_dragStart.Y, cur.Y);
            float sx1 = (float)Math.Max(_dragStart.X, cur.X);
            float sy1 = (float)Math.Max(_dragStart.Y, cur.Y);
            using var bandFill = new SKPaint { Color = new(0x3b,0x82,0xf6,0x30) };
            canvas.DrawRect(sx0, sy0, sx1-sx0, sy1-sy0, bandFill);
            using var bandLine = new SKPaint
            {
                Color = new(0x3b,0x82,0xf6,0xcc), StrokeWidth = 1, IsStroke = true,
                PathEffect = SKPathEffect.CreateDash([4f,4f], 0),
            };
            canvas.DrawRect(sx0, sy0, sx1-sx0, sy1-sy0, bandLine);
        }

        // Draw-tool ghost preview
        if (_drawTool != DrawToolType.None && _drawStart.HasValue)
        {
            var s = _drawStart.Value;
            var d = _drawEnd ?? s;
            float sx1 = ToSX(s.X), sy1 = ToSY(s.Y);
            float sx2 = ToSX(d.X), sy2 = ToSY(d.Y);
            float minX = Math.Min(sx1, sx2), minY = Math.Min(sy1, sy2);
            float rw   = Math.Abs(sx2 - sx1),  rh  = Math.Abs(sy2 - sy1);

            using var ghost = new SKPaint
            {
                Color = new SKColor(0x36, 0x8b, 0xff, 200),
                StrokeWidth = 1.5f,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash([6f, 4f], 0f),
            };
            switch (_drawTool)
            {
                case DrawToolType.Line:
                    canvas.DrawLine(sx1, sy1, sx2, sy2, ghost);
                    break;
                case DrawToolType.Rectangle:
                    canvas.DrawRect(minX, minY, rw, rh, ghost);
                    break;
                case DrawToolType.Ellipse:
                    canvas.DrawOval(minX + rw / 2, minY + rh / 2, rw / 2, rh / 2, ghost);
                    break;
                case DrawToolType.Circle:
                    float cr = (float)Math.Sqrt((sx2 - sx1) * (sx2 - sx1) + (sy2 - sy1) * (sy2 - sy1));
                    canvas.DrawCircle(sx1, sy1, cr, ghost);
                    break;
                case DrawToolType.Polygon:
                case DrawToolType.Star:
                    float pr = (float)Math.Sqrt((sx2 - sx1) * (sx2 - sx1) + (sy2 - sy1) * (sy2 - sy1));
                    canvas.DrawCircle(sx1, sy1, pr, ghost);
                    break;
            }
            // Crosshair at current mouse tip
            using var cross = new SKPaint { Color = new SKColor(0x36, 0x8b, 0xff, 180), StrokeWidth = 1f };
            canvas.DrawLine(sx2 - 12, sy2, sx2 + 12, sy2, cross);
            canvas.DrawLine(sx2, sy2 - 12, sx2, sy2 + 12, cross);
        }

        RenderGuidesAndCursor(canvas, w, h, colFg);
        RenderRulers(canvas, w, h, colRuler, colFg, colMuted);
        RenderCoordinateHud(canvas, w, h, colFg);
    }

    private void RenderGuidesAndCursor(SKCanvas canvas, float w, float h, SKColor colFg)
    {
        float cx = ToSX(_table.WidthMm  / 2);
        float cy = ToSY(_table.HeightMm / 2);
        using var centreP = new SKPaint
        {
            Color = new(0x3b,0x82,0xf6,0x44), StrokeWidth = 1, IsStroke = true, IsAntialias = false,
            PathEffect = SKPathEffect.CreateDash([3f,5f], 0),
        };
        canvas.DrawLine(cx, RULER_PX, cx, h, centreP);
        canvas.DrawLine(RULER_PX, cy, w, cy, centreP);

        // ── Snap / alignment guides (magenta, shown during drag) ──────────
        SKColor colGuide = new(0xff, 0x3d, 0x9a);
        using var guideP = new SKPaint
        {
            Color = colGuide, StrokeWidth = 1.4f, IsStroke = true, IsAntialias = false,
            PathEffect = SKPathEffect.CreateDash([5f,3f], 0),
        };
        if (_guideX is { } gx) { float sx = ToSX(gx); canvas.DrawLine(sx, RULER_PX, sx, h, guideP); }
        if (_guideY is { } gy) { float sy = ToSY(gy); canvas.DrawLine(RULER_PX, sy, w, sy, guideP); }

        // ── User-placed guide lines (cyan-blue, permanent) ─────────────────
        float ext = Math.Max(w, h) * 2 + 100;
        SKColor colUserGuide = new(0x00, 0x99, 0xff, 0xd0);
        using var ugP = new SKPaint { Color = colUserGuide, StrokeWidth = 1f, IsStroke = true, IsAntialias = false };
        using var ugLockedP = new SKPaint
        {
            Color = new(0x00, 0x99, 0xff, 0x70), StrokeWidth = 1f, IsStroke = true, IsAntialias = false,
            PathEffect = SKPathEffect.CreateDash([4f, 3f], 0),
        };
        using var ugLabelP = new SKPaint { Color = colUserGuide, TextSize = 10, IsAntialias = true };

        canvas.Save();
        canvas.ClipRect(new SKRect(RULER_PX, RULER_PX, w, h));
        foreach (var g in _userGuides)
        {
            float gsx = ToSX(g.X), gsy = ToSY(g.Y);
            float theta = (float)(g.AngleDeg * Math.PI / 180);
            float ld = (float)Math.Cos(theta);
            float ud = -(float)Math.Sin(theta); // screen Y is flipped
            var paint = g.IsLocked ? ugLockedP : ugP;
            canvas.DrawLine(gsx - ld * ext, gsy - ud * ext,
                            gsx + ld * ext, gsy + ud * ext, paint);
            if (!string.IsNullOrEmpty(g.Label))
                canvas.DrawText(g.Label, gsx + 3, gsy - 3, ugLabelP);
            // Small square at the reference point
            canvas.DrawRect(gsx - 3, gsy - 3, 6, 6, ugP);
        }

        // Ghost guide while dragging from ruler
        if (_drag == DragMode.CreateGuide && _cursor is { } cc && cc.X >= RULER_PX && cc.Y >= RULER_PX)
        {
            float pgx = _newGuideAngleDeg == 90 ? (float)cc.X : (float)(RULER_PX + 10);
            float pgy = _newGuideAngleDeg == 0  ? (float)cc.Y : (float)(RULER_PX + 10);
            float theta2 = (float)(_newGuideAngleDeg * Math.PI / 180);
            float ld2 = (float)Math.Cos(theta2), ud2 = -(float)Math.Sin(theta2);
            using var ghostP = new SKPaint
            {
                Color = new(0x00, 0x99, 0xff, 0x88), StrokeWidth = 1.5f, IsStroke = true,
                PathEffect = SKPathEffect.CreateDash([6f, 3f], 0),
            };
            canvas.DrawLine(pgx - ld2 * ext, pgy - ud2 * ext,
                            pgx + ld2 * ext, pgy + ud2 * ext, ghostP);
        }
        canvas.Restore();

        // ── Cursor crosshair + ruler tick ─────────────────────────────────
        if (_cursor is { } c && _drag != DragMode.Pan
            && c.X >= RULER_PX && c.Y >= RULER_PX && c.X <= w && c.Y <= h)
        {
            using var crossP = new SKPaint { Color = colFg.WithAlpha(0x55), StrokeWidth = 1, IsStroke = true };
            canvas.DrawLine((float)c.X, RULER_PX, (float)c.X, h, crossP);
            canvas.DrawLine(RULER_PX, (float)c.Y, w, (float)c.Y, crossP);
            using var tickP = new SKPaint { Color = new(0x3b,0x82,0xf6), StrokeWidth = 2, IsStroke = true };
            canvas.DrawLine((float)c.X, 0, (float)c.X, RULER_PX, tickP);
            canvas.DrawLine(0, (float)c.Y, RULER_PX, (float)c.Y, tickP);
        }
    }

    private void RenderCoordinateHud(SKCanvas canvas, float w, float h, SKColor colFg)
    {
        if (_cursor is not { } c) return;
        if (c.X < RULER_PX || c.Y < RULER_PX || c.X > w || c.Y > h) return;
        double wx = ToWX((float)c.X), wy = ToWY((float)c.Y);
        string text = $"X {wx,7:0.0}   Y {wy,7:0.0} mm";
        using var txt = new SKPaint { Color = colFg, TextSize = 11.5f, IsAntialias = true };
        float tw = txt.MeasureText(text), pad = 7f, bw = tw + pad*2, bh = 21f;
        float bx = w - bw - 10, by = h - bh - 10;
        using var box = new SKPaint { Color = new(0xCC,0x10,0x10,0x13), IsAntialias = true };
        using var bdr = new SKPaint { Color = new(0x2a,0x2a,0x30), IsStroke = true, StrokeWidth = 1, IsAntialias = true };
        var rect = new SKRect(bx, by, bx+bw, by+bh);
        canvas.DrawRoundRect(rect, 4, 4, box);
        canvas.DrawRoundRect(rect, 4, 4, bdr);
        canvas.DrawText(text, bx + pad, by + bh - 6.5f, txt);
    }

    private void RenderRulers(SKCanvas canvas, float w, float h, SKColor colRuler, SKColor colFg, SKColor colMuted)
    {
        using var bg  = new SKPaint { Color = colRuler };
        using var txt = new SKPaint { Color = colFg.WithAlpha(0xaa), TextSize = 9f, IsAntialias = true };
        using var tk  = new SKPaint { Color = colFg.WithAlpha(0x66), StrokeWidth = 1, IsStroke = true };
        canvas.DrawRect(0, 0, w, RULER_PX, bg);
        canvas.DrawRect(0, 0, RULER_PX, h, bg);
        canvas.DrawRect(0, 0, RULER_PX, RULER_PX, bg);

        float[] steps = [1, 2, 5, 10, 25, 50, 100, 250, 500, 1000];
        float tickStep = steps.FirstOrDefault(s => s * _scale >= 30, 1000);

        for (float gx = 0; gx <= _table.WidthMm; gx += tickStep)
        {
            float sx = ToSX(gx);
            if (sx < RULER_PX || sx > w) continue;
            canvas.DrawLine(sx, RULER_PX - 5, sx, RULER_PX, tk);
            canvas.DrawText($"{(int)gx}", sx + 2, RULER_PX - 6, txt);
        }
        canvas.Save();
        for (float gy = 0; gy <= _table.HeightMm; gy += tickStep)
        {
            float sy = ToSY(gy);
            if (sy < RULER_PX || sy > h) continue;
            canvas.DrawLine(RULER_PX - 5, sy, RULER_PX, sy, tk);
            canvas.Save();
            canvas.Translate(RULER_PX - 6, sy - 2);
            canvas.RotateDegrees(-90);
            canvas.DrawText($"{(int)gy}", 0, 0, txt);
            canvas.Restore();
        }
        canvas.Restore();
        using var div = new SKPaint { Color = colMuted, StrokeWidth = 1, IsStroke = true };
        canvas.DrawLine(RULER_PX, RULER_PX, w, RULER_PX, div);
        canvas.DrawLine(RULER_PX, RULER_PX, RULER_PX, h, div);
    }

    // ── geometry helpers ──────────────────────────────────────────────────
    private static Point2 LocalCenter(List<ModelPathGeometry> paths)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var pg in paths)
        foreach (var pt in pg.Polyline.Points)
        {
            if (pt.X < minX) minX = pt.X;
            if (pt.Y < minY) minY = pt.Y;
            if (pt.X > maxX) maxX = pt.X;
            if (pt.Y > maxY) maxY = pt.Y;
        }
        return minX > maxX ? new(0, 0) : new((minX + maxX) / 2, (minY + maxY) / 2);
    }

    private static (Point2 Min, Point2 Max) WorldBounds(Part part, List<ModelPathGeometry> paths)
    {
        var pivot = LocalCenter(paths);
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var pg in paths)
        foreach (var local in pg.Polyline.Points)
        {
            var wp = PartTransform.Apply(part, pivot, local);
            if (wp.X < minX) minX = wp.X;
            if (wp.Y < minY) minY = wp.Y;
            if (wp.X > maxX) maxX = wp.X;
            if (wp.Y > maxY) maxY = wp.Y;
        }
        return minX > maxX ? (new(0,0), new(0,0)) : (new(minX, minY), new(maxX, maxY));
    }

    private bool IsOutOfBounds(Part part, List<ModelPathGeometry> paths)
    {
        var (mn, mx) = WorldBounds(part, paths);
        return mn.X < -1e-6 || mn.Y < -1e-6
            || mx.X > _table.WidthMm + 1e-6
            || mx.Y > _table.HeightMm + 1e-6;
    }

    private SKPath BuildPath(Part part, Point2 pivot, ModelPathGeometry pg)
    {
        var pts = pg.Polyline.Points;
        var hdls = pg.Handles;
        var skp = new SKPath();
        if (pts.Count == 0) return skp;
        var w0 = PartTransform.Apply(part, pivot, pts[0]);
        skp.MoveTo(ToSX(w0.X), ToSY(w0.Y));
        int n = pts.Count;
        int segCount = pg.Polyline.IsClosed ? n : n - 1;
        for (int i = 0; i < segCount; i++)
        {
            int j = (i + 1) % n;
            var hOut = hdls?[i]; var hIn = hdls?[j];
            var ancI = pts[i]; var ancJ = pts[j];
            bool hasCurve = (hOut is { Length: >= 4 } && (hOut[2] != 0 || hOut[3] != 0))
                         || (hIn  is { Length: >= 4 } && (hIn [0] != 0 || hIn [1] != 0));
            if (hasCurve)
            {
                var cp1 = PartTransform.Apply(part, pivot, new(ancI.X + (hOut?[2] ?? 0), ancI.Y + (hOut?[3] ?? 0)));
                var cp2 = PartTransform.Apply(part, pivot, new(ancJ.X + (hIn?[0]  ?? 0), ancJ.Y + (hIn?[1]  ?? 0)));
                var wp  = PartTransform.Apply(part, pivot, ancJ);
                skp.CubicTo(ToSX(cp1.X), ToSY(cp1.Y), ToSX(cp2.X), ToSY(cp2.Y), ToSX(wp.X), ToSY(wp.Y));
            }
            else
            {
                var wp = PartTransform.Apply(part, pivot, ancJ);
                skp.LineTo(ToSX(wp.X), ToSY(wp.Y));
            }
        }
        if (pg.Polyline.IsClosed) skp.Close();
        return skp;
    }

    private static bool HitTestPart(Part part, List<ModelPathGeometry> paths, double wx, double wy)
    {
        var (mn, mx) = WorldBounds(part, paths);
        const double tol = 3;
        return wx >= mn.X - tol && wx <= mx.X + tol && wy >= mn.Y - tol && wy <= mx.Y + tol;
    }

    private Part? HitTestPartAtPoint(double wx, double wy)
    {
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            var part = _parts[i];
            var layer = _layers.FirstOrDefault(l => l.Id == part.LayerId);
            if (layer is { Visible: false } || layer is { Locked: true }) continue;
            if (!_geometry.TryGetValue(part.FileId, out var geom)) continue;
            if (HitTestPart(part, geom, wx, wy)) return part;
        }
        return null;
    }

    /// <summary>Returns the index of the guide within 5px of the screen point, or -1.</summary>
    private int HitTestGuide(Point screenPos)
    {
        const float TOL = 6f;
        for (int i = 0; i < _userGuides.Count; i++)
        {
            var g   = _userGuides[i];
            float gsx = ToSX(g.X), gsy = ToSY(g.Y);
            float theta = (float)(g.AngleDeg * Math.PI / 180);
            // Line direction in screen space (Y is flipped): (cos θ, –sin θ)
            float ld = (float)Math.Cos(theta);
            float ud = -(float)Math.Sin(theta);
            // Normal to line = (-ud, ld)
            float nx = -ud, ny = ld;
            float px = (float)screenPos.X - gsx;
            float py = (float)screenPos.Y - gsy;
            float dist = Math.Abs(px * nx + py * ny);
            if (dist <= TOL) return i;
        }
        return -1;
    }

    private void HitTestNode(double wx, double wy)
    {
        if (_editingPartId is null) return;
        var part = _parts.FirstOrDefault(p => p.Id == _editingPartId.Value);
        if (part is null || !_geometry.TryGetValue(part.FileId, out var geom)) return;
        var pivot = LocalCenter(geom);
        double minDist = _nodeHitRadius / _scale;
        int? hitNode = null;
        for (int pathIdx = 0; pathIdx < geom.Count; pathIdx++)
        {
            var pg = geom[pathIdx];
            for (int nodeIdx = 0; nodeIdx < pg.Polyline.Points.Count; nodeIdx++)
            {
                var local = pg.Polyline.Points[nodeIdx];
                var world = PartTransform.Apply(part, pivot, local);
                double d = Math.Sqrt(Math.Pow(world.X - wx, 2) + Math.Pow(world.Y - wy, 2));
                if (d < minDist) { minDist = d; hitNode = nodeIdx; }
            }
        }
        _selectedNodeIndex = hitNode;

        // Fire event so toolbar can display node world position
        if (hitNode.HasValue && part is not null)
        {
            // Walk geom in the same order as HitTestNode above to find the node world pos
            int ni = 0;
            double? evtX = null, evtY = null;
            foreach (var pg in geom)
            {
                for (int i = 0; i < pg.Polyline.Points.Count; i++, ni++)
                {
                    if (ni == hitNode.Value)
                    {
                        var w = PartTransform.Apply(part, pivot, pg.Polyline.Points[i]);
                        evtX = w.X; evtY = w.Y;
                        goto nodeFound;
                    }
                }
            }
            nodeFound:
            NodeSelected?.Invoke(evtX, evtY);
        }
        else
        {
            NodeSelected?.Invoke(null, null);
        }

        InvalidateVisual();
    }

    // ── ICustomDrawOperation ──────────────────────────────────────────────
    private sealed class DrawOp : ICustomDrawOperation
    {
        private readonly ViewportControl _owner;
        public Rect Bounds { get; }
        public DrawOp(ViewportControl owner, Rect bounds) { _owner = owner; Bounds = bounds; }
        public void Dispose() { }
        public bool HitTest(Point p) => Bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Render(ImmediateDrawingContext context)
        {
            var skia = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            if (skia is null) return;
            using var lease = skia.Lease();
            _owner.RenderScene(lease.SkCanvas, (float)Bounds.Width, (float)Bounds.Height);
        }
    }
}
