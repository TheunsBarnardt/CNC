using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
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
/// parts. Handles pan / zoom / select / move interactions directly.
/// </summary>
public sealed class ViewportControl : Control
{
    // ── view transform ────────────────────────────────────────────────────
    private float _scale = 0.5f;   // pixels per mm
    private float _tx = 20;        // table-origin screen X (px)
    private float _ty = 20;        // table-origin screen Y (px, from bottom)
    private bool _fitted;

    // ── display options ───────────────────────────────────────────────────
    private bool _darkCanvas = true;
    private bool _showGrid   = true;
    private const int RULER_PX = 20;

    // ── data (set by MainWindow when ViewModel changes) ───────────────────
    private MainViewModel? _vm;
    private TableSettings  _table  = new();
    private List<Part>     _parts  = [];
    private List<Layer>    _layers = [];
    private Dictionary<Guid, List<ModelPathGeometry>> _geometry = new();
    private Guid? _selectedId;

    // ── simulation state (set by SimulationBar on each tick) ─────────────
    public SimState? SimState { get; set; }

    // ── drag state ────────────────────────────────────────────────────────
    private enum DragMode { None, Pan, Move }
    private DragMode _drag;
    private Point    _dragStart;
    private float    _dragTx, _dragTy;
    private double   _partStartX, _partStartY;

    // ── pen tool state ─────────────────────────────────────────────────────
    private List<(double x, double y)> _penPoints = [];

    // ── events ────────────────────────────────────────────────────────────
    public event Action<Guid?    >? SelectionChanged;
    public event Action<Part     >? PartMoved;     // optimistic (during drag)
    public event Action<Part     >? PartCommitted; // final (drag end)

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

    // ── data sync ─────────────────────────────────────────────────────────
    private void OnProjectChanged()
    {
        if (_vm is null) return;
        var p = _vm.Project;
        _table    = p.Table;
        _parts    = [.. p.Parts];
        _layers   = [.. p.Layers];
        _geometry = _vm.Geometry;
        _selectedId = _vm.SelectedPart?.Id;

        if (!_fitted && Bounds.Width > 0) { _fitted = true; FitToView(); }
        InvalidateVisual();
    }

    // ── coordinate helpers ────────────────────────────────────────────────
    private float ToSX(double worldX) => (float)(worldX * _scale + _tx);
    private float ToSY(double worldY) => (float)(Bounds.Height - (worldY * _scale + _ty));
    private double ToWX(float sx)     => (sx - _tx) / _scale;
    private double ToWY(float sy)     => (Bounds.Height - sy - _ty) / _scale;

    // ── interaction ───────────────────────────────────────────────────────
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos    = e.GetPosition(this);
        float factor = e.Delta.Y > 0 ? 1.12f : 0.89f;
        float newScale = Math.Clamp(_scale * factor, 0.02f, 100f);
        float wx   = (float)(pos.X - _tx) / _scale;
        float wy   = (float)(Bounds.Height - pos.Y - _ty) / _scale;
        _tx = (float)pos.X - wx * newScale;
        _ty = (float)(Bounds.Height - pos.Y) - wy * newScale;
        _scale = newScale;
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
                var pwx = ToWX((float)pos.X);
                var pwy = ToWY((float)pos.Y);
                _penPoints.Add((pwx, pwy));
                InvalidateVisual();
            }
            return;
        }

        // Middle mouse or right-drag → pan
        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed)
        {
            _drag = DragMode.Pan;
            _dragStart = pos;
            _dragTx = _tx; _dragTy = _ty;
            e.Pointer.Capture(this);
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        // Skip ruler area
        if (pos.X < RULER_PX || pos.Y < RULER_PX) return;

        // Hit-test parts (back-to-front so top-most wins)
        var wx = ToWX((float)pos.X);
        var wy = ToWY((float)pos.Y);
        Part? hit = null;
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            var part = _parts[i];
            var layer = _layers.FirstOrDefault(l => l.Id == part.LayerId);
            if (layer is { Visible: false } || layer is { Locked: true }) continue;
            if (!_geometry.TryGetValue(part.FileId, out var geom)) continue;
            if (HitTestPart(part, geom, wx, wy))
            {
                hit = part;
                break;
            }
        }

        if (hit is not null)
        {
            _selectedId = hit.Id;
            SelectionChanged?.Invoke(hit.Id);

            // Begin move drag
            _drag = DragMode.Move;
            _dragStart    = pos;
            _partStartX   = hit.X;
            _partStartY   = hit.Y;
            e.Pointer.Capture(this);
        }
        else
        {
            // Click on empty → deselect and start pan
            _selectedId = null;
            SelectionChanged?.Invoke(null);
            _drag = DragMode.Pan;
            _dragStart = pos;
            _dragTx = _tx; _dragTy = _ty;
            e.Pointer.Capture(this);
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag == DragMode.None) return;
        var pos = e.GetPosition(this);
        double dx = pos.X - _dragStart.X;
        double dy = pos.Y - _dragStart.Y;

        if (_drag == DragMode.Pan)
        {
            _tx = _dragTx + (float)dx;
            _ty = _dragTy - (float)dy;  // screen Y flipped
            InvalidateVisual();
        }
        else if (_drag == DragMode.Move && _selectedId.HasValue)
        {
            var part = _parts.FirstOrDefault(p => p.Id == _selectedId.Value);
            if (part is not null)
            {
                part.X = _partStartX + dx / _scale;
                part.Y = _partStartY - dy / _scale;  // screen Y flipped
                PartMoved?.Invoke(part);
                InvalidateVisual();
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == DragMode.Move && _selectedId.HasValue)
        {
            var part = _parts.FirstOrDefault(p => p.Id == _selectedId.Value);
            if (part is not null) PartCommitted?.Invoke(part);
        }
        _drag = DragMode.None;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Pen tool shortcuts
        if (_vm?.PenToolActive == true)
        {
            switch (e.Key)
            {
                case Key.Return:
                    // Enter = finish path (closed if Shift held)
                    bool closed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                    _vm.CreatePathFromPoints(_penPoints, closed);
                    _penPoints.Clear();
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    // Escape = cancel
                    _vm.CancelPenTool();
                    _penPoints.Clear();
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Back: case Key.Delete:
                    // Backspace = undo last point
                    if (_penPoints.Count > 0)
                    {
                        _penPoints.RemoveAt(_penPoints.Count - 1);
                        InvalidateVisual();
                        e.Handled = true;
                    }
                    break;
            }
            if (e.Handled) return;
        }

        var part = _selectedId.HasValue
            ? _parts.FirstOrDefault(p => p.Id == _selectedId.Value)
            : null;
        if (part is null) return;
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Delete: case Key.Back:
                _vm?.DeleteSelected();
                break;
            case Key.Left:  part.X -= step; PartCommitted?.Invoke(part); InvalidateVisual(); e.Handled = true; break;
            case Key.Right: part.X += step; PartCommitted?.Invoke(part); InvalidateVisual(); e.Handled = true; break;
            case Key.Up:    part.Y += step; PartCommitted?.Invoke(part); InvalidateVisual(); e.Handled = true; break;
            case Key.Down:  part.Y -= step; PartCommitted?.Invoke(part); InvalidateVisual(); e.Handled = true; break;
        }
    }

    // ── layout ────────────────────────────────────────────────────────────
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
        // ── colors ────────────────────────────────────────────────────────
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

        // ── background ────────────────────────────────────────────────────
        canvas.Clear(colBg);

        // ── table ─────────────────────────────────────────────────────────
        float tx0 = ToSX(0), ty0 = ToSY((float)_table.HeightMm);
        float tw  = (float)_table.WidthMm * _scale;
        float th  = (float)_table.HeightMm * _scale;

        using var tablePaint = new SKPaint { Color = colTable, IsAntialias = false };
        canvas.DrawRect(tx0, ty0, tw, th, tablePaint);

        // ── grid ──────────────────────────────────────────────────────────
        if (_showGrid && _scale > 0.05f)
        {
            float[] stepsM = [1, 2, 5, 10, 25, 50, 100, 250, 500];
            float minor = stepsM.FirstOrDefault(s => s * _scale >= 8, 500);
            float major = minor * 5;
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

        // ── table border ──────────────────────────────────────────────────
        using var bdrP = new SKPaint { Color = colBorder, StrokeWidth = 1.5f, IsStroke = true, IsAntialias = true };
        canvas.DrawRect(tx0, ty0, tw, th, bdrP);

        // ── origin marker ─────────────────────────────────────────────────
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

            SKColor strokeCol = outBounds ? colRed : selected ? colPrim : colCyan;

            var pivot = LocalCenter(geom);

            // Draw each path
            foreach (var pg in geom)
            {
                if (pg.Polyline.Points.Count < 2) continue;
                using var path = BuildPath(part, pivot, pg);
                bool closed = pg.Polyline.IsClosed;

                if (closed)
                {
                    // Fill
                    float fillAlpha = isCutout ? 0.06f : (selected ? 0.14f : 0.07f);
                    using var fp = new SKPaint
                    {
                        Color = strokeCol.WithAlpha((byte)(fillAlpha * 255)),
                        IsAntialias = true,
                    };
                    canvas.DrawPath(path, fp);

                    // Cutout hatch
                    if (isCutout)
                    {
                        canvas.Save();
                        canvas.ClipPath(path);
                        using var hp = new SKPaint { Color = strokeCol.WithAlpha(0x28), StrokeWidth = 1, IsStroke = true };
                        var bds = path.Bounds;
                        for (float d = -bds.Height; d < bds.Width + bds.Height; d += 7)
                        {
                            canvas.DrawLine(bds.Left + d, bds.Top,
                                            bds.Left + d + bds.Height, bds.Bottom, hp);
                        }
                        canvas.Restore();
                    }
                }

                float lw = selected ? 2f : 1.25f;
                using var sp = new SKPaint
                {
                    Color       = strokeCol,
                    StrokeWidth = lw,
                    IsStroke    = true,
                    IsAntialias = true,
                };
                if (isCutout) sp.PathEffect = SKPathEffect.CreateDash([5f, 3f], 0);
                canvas.DrawPath(path, sp);
            }

            // Selection box + handles
            if (selected)
            {
                var (bMin, bMax) = WorldBounds(part, geom);
                float bx0 = ToSX(bMin.X), by0 = ToSY(bMax.Y);
                float bx1 = ToSX(bMax.X), by1 = ToSY(bMin.Y);
                using var selP = new SKPaint
                {
                    Color = colFg.WithAlpha(0xaa), StrokeWidth = 1, IsStroke = true,
                    PathEffect = SKPathEffect.CreateDash([4f, 4f], 0),
                };
                canvas.DrawRect(SKRect.Create(bx0, by0, bx1 - bx0, by1 - by0), selP);

                // Resize handles (corners + edge midpoints)
                float mx = (bx0 + bx1) / 2, my = (by0 + by1) / 2;
                float[] hx = [bx0, mx, bx1, bx0, bx1, bx0, mx, bx1];
                float[] hy = [by0, by0, by0, my,  my,  by1, by1, by1];
                using var hFill   = new SKPaint { Color = _darkCanvas ? new(0x30,0x30,0x30) : SKColors.White };
                using var hStroke = new SKPaint { Color = colFg.WithAlpha(0xdd), StrokeWidth = 1.5f, IsStroke = true, IsAntialias = true };
                for (int i = 0; i < 8; i++)
                {
                    var r = SKRect.Create(hx[i] - 4, hy[i] - 4, 8, 8);
                    canvas.DrawRect(r, hFill);
                    canvas.DrawRect(r, hStroke);
                }
            }
        }

        // ── simulation torch-head ─────────────────────────────────────────
        if (SimState is { } ss)
        {
            float sx = ToSX(ss.Position.X);
            float sy = ToSY(ss.Position.Y);
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

        // ── pen tool points ───────────────────────────────────────────────
        if (_vm?.PenToolActive == true && _penPoints.Count > 0)
        {
            using var penPaint = new SKPaint { Color = colPrim, StrokeWidth = 2, IsStroke = false, IsAntialias = true };
            using var linePaint = new SKPaint { Color = colPrim.WithAlpha(0x80), StrokeWidth = 1, IsStroke = true, IsAntialias = true };

            // Draw lines connecting points
            for (int i = 1; i < _penPoints.Count; i++)
            {
                float x0 = ToSX(_penPoints[i - 1].x);
                float y0 = ToSY(_penPoints[i - 1].y);
                float x1 = ToSX(_penPoints[i].x);
                float y1 = ToSY(_penPoints[i].y);
                canvas.DrawLine(x0, y0, x1, y1, linePaint);
            }

            // Draw points as circles
            foreach (var pt in _penPoints)
            {
                float sx = ToSX(pt.x);
                float sy = ToSY(pt.y);
                canvas.DrawCircle(sx, sy, 4, penPaint);
            }

            // Draw status text
            using var statusPaint = new SKPaint { Color = colPrim, TextSize = 12, IsAntialias = true };
            canvas.DrawText($"Pen: {_penPoints.Count} points (Enter=finish, Shift+Enter=close, Esc=cancel)", 30, 45, statusPaint);
        }

        // ── rulers ────────────────────────────────────────────────────────
        RenderRulers(canvas, w, h, colRuler, colFg, colMuted);
    }

    private void RenderRulers(SKCanvas canvas, float w, float h,
        SKColor colRuler, SKColor colFg, SKColor colMuted)
    {
        using var bg  = new SKPaint { Color = colRuler };
        using var txt = new SKPaint { Color = colFg.WithAlpha(0xaa), TextSize = 9f, IsAntialias = true };
        using var tk  = new SKPaint { Color = colFg.WithAlpha(0x66), StrokeWidth = 1, IsStroke = true };

        // Background strips
        canvas.DrawRect(0, 0, w, RULER_PX, bg);
        canvas.DrawRect(0, 0, RULER_PX, h, bg);
        canvas.DrawRect(0, 0, RULER_PX, RULER_PX, bg); // corner

        float[] steps = [1, 2, 5, 10, 25, 50, 100, 250, 500, 1000];
        float tickStep = steps.FirstOrDefault(s => s * _scale >= 30, 1000);

        // Horizontal (X axis → top ruler)
        for (float gx = 0; gx <= _table.WidthMm; gx += tickStep)
        {
            float sx = ToSX(gx);
            if (sx < RULER_PX || sx > w) continue;
            canvas.DrawLine(sx, RULER_PX - 5, sx, RULER_PX, tk);
            canvas.DrawText($"{(int)gx}", sx + 2, RULER_PX - 6, txt);
        }

        // Vertical (Y axis → left ruler), labels rotated
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

        // Divider lines
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
        return minX > maxX
            ? (new(0,0), new(0,0))
            : (new(minX, minY), new(maxX, maxY));
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
        var pts   = pg.Polyline.Points;
        var hdls  = pg.Handles;
        var skp   = new SKPath();
        if (pts.Count == 0) return skp;

        var w0 = PartTransform.Apply(part, pivot, pts[0]);
        skp.MoveTo(ToSX(w0.X), ToSY(w0.Y));

        int n = pts.Count;
        int segCount = pg.Polyline.IsClosed ? n : n - 1;
        for (int i = 0; i < segCount; i++)
        {
            int j = (i + 1) % n;
            var hOut = hdls?[i];
            var hIn  = hdls?[j];
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
        return wx >= mn.X - tol && wx <= mx.X + tol
            && wy >= mn.Y - tol && wy <= mx.Y + tol;
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
