using Backend.Geometry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Backend.Import.Bitmap;

public enum TraceMode { Outline, Centerline }

public sealed record BitmapTraceSettings(
    int ThresholdPercent = 50,
    bool Invert = false,
    float Brightness = 0f,             // -1..1 additive offset
    float Contrast = 1f,               // 0.1..3 multiplier
    TraceMode Mode = TraceMode.Outline,
    double SimplifyToleranceMm = 0.3
);

/// <summary>
/// Converts a raster image to a list of vector polylines via contour or skeleton tracing.
/// All returned coordinates are in mm, Y-up (flipped from image Y-down).
/// </summary>
public static class BitmapTracer
{
    // Moore neighbors in clockwise order starting from N (index 0 = N, 1 = NE, ...)
    private static readonly (int dx, int dy)[] Moore =
    [
        ( 0, -1), ( 1, -1), ( 1,  0), ( 1,  1),
        ( 0,  1), (-1,  1), (-1,  0), (-1, -1),
    ];

    public static (List<Polyline2> Paths, double WidthMm, double HeightMm) Trace(
        byte[] imageData, BitmapTraceSettings settings)
    {
        using var img = Image.Load<Rgba32>(imageData);

        // ---- mm/pixel conversion from DPI metadata ----
        double dpiX = img.Metadata.HorizontalResolution > 0
            ? img.Metadata.HorizontalResolution : 96.0;
        double dpiY = img.Metadata.VerticalResolution > 0
            ? img.Metadata.VerticalResolution : 96.0;

        double mmPerPxX = img.Metadata.ResolutionUnits switch
        {
            PixelResolutionUnit.PixelsPerCentimeter => 10.0 / dpiX,
            PixelResolutionUnit.PixelsPerMeter      => 1000.0 / dpiX,
            _ => 25.4 / dpiX,
        };
        double mmPerPxY = img.Metadata.ResolutionUnits switch
        {
            PixelResolutionUnit.PixelsPerCentimeter => 10.0 / dpiY,
            PixelResolutionUnit.PixelsPerMeter      => 1000.0 / dpiY,
            _ => 25.4 / dpiY,
        };

        double widthMm  = img.Width  * mmPerPxX;
        double heightMm = img.Height * mmPerPxY;

        // ---- Apply visual adjustments ----
        img.Mutate(ctx =>
        {
            if (Math.Abs(settings.Brightness) > 0.001f || Math.Abs(settings.Contrast - 1f) > 0.001f)
                ctx.Brightness(1f + settings.Brightness).Contrast(settings.Contrast);
            ctx.Grayscale();
        });

        // ---- Threshold to binary (true = foreground / dark) ----
        int w = img.Width, h = img.Height;
        float threshold = settings.ThresholdPercent / 100f;
        var bin = new bool[w, h];

        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    bool dark = row[x].R / 255f < threshold;
                    bin[x, y] = settings.Invert ? !dark : dark;
                }
            }
        });

        // ---- Trace ----
        List<Polyline2> rawPaths = settings.Mode == TraceMode.Centerline
            ? TraceCenterlines(bin, w, h)
            : TraceOutlines(bin, w, h);

        // ---- Convert pixel → mm and flip Y ----
        double tolMm = Math.Max(0.05, settings.SimplifyToleranceMm);
        var result = new List<Polyline2>(rawPaths.Count);

        foreach (var px in rawPaths)
        {
            var mmPoints = px.Points
                .Select(p => new Point2(p.X * mmPerPxX, heightMm - p.Y * mmPerPxY))
                .ToList();

            var poly = new Polyline2 { IsClosed = px.IsClosed, Points = mmPoints };
            var simplified = RdpSimplify(poly, tolMm);
            int minPts = simplified.IsClosed ? 3 : 2;
            if (simplified.Points.Count >= minPts)
                result.Add(simplified);
        }

        return (result, widthMm, heightMm);
    }

    // ============================================================
    // OUTLINE TRACING — Moore neighborhood boundary tracing
    // Jacob's stopping criterion for correct termination.
    // ============================================================
    private static List<Polyline2> TraceOutlines(bool[,] bin, int w, int h)
    {
        var visited = new bool[w, h];
        var paths   = new List<Polyline2>();

        for (int sy = 0; sy < h; sy++)
        for (int sx = 0; sx < w; sx++)
        {
            if (!bin[sx, sy] || visited[sx, sy]) continue;

            var boundary = TraceSingleContour(bin, visited, sx, sy, w, h);
            if (boundary.Count >= 3)
                paths.Add(new Polyline2 { IsClosed = true, Points = boundary });
        }

        return paths;
    }

    private static List<Point2> TraceSingleContour(
        bool[,] bin, bool[,] visited, int sx, int sy, int w, int h)
    {
        // Jacob's Moore-neighbor tracing
        // b = last pixel checked (backtrack), c = current pixel
        var result  = new List<Point2>();
        int cx = sx, cy = sy;
        // Initial backtrack: conceptually the pixel to the left (W)
        int bx = sx - 1, by = sy;

        // Compute initial backtrack as the last checked neighbor that is NOT the start
        // (simulated: assume we arrived from the West)
        int initialBx = bx, initialBy = by;

        bool first = true;
        const int maxSteps = 1_000_000; // safety
        int steps = 0;

        while (steps++ < maxSteps)
        {
            if (!first && cx == sx && cy == sy && bx == initialBx && by == initialBy)
                break; // Jacob stopping criterion
            first = false;

            result.Add(new Point2(cx, cy));
            visited[cx, cy] = true;

            // Find index of backtrack in Moore array
            int btIdx = MooreIndex(bx - cx, by - cy);

            bool found = false;
            for (int i = 1; i <= 8; i++)
            {
                int ni  = (btIdx + i) % 8;
                int nx  = cx + Moore[ni].dx;
                int ny  = cy + Moore[ni].dy;
                int pni = (btIdx + i - 1) % 8; // pixel before n

                if (nx >= 0 && nx < w && ny >= 0 && ny < h && bin[nx, ny])
                {
                    // Update backtrack: the pixel we checked just before nx,ny
                    bx = cx + Moore[pni].dx;
                    by = cy + Moore[pni].dy;
                    cx = nx;
                    cy = ny;
                    found = true;
                    break;
                }
            }

            if (!found) break; // isolated pixel
        }

        return result;
    }

    private static int MooreIndex(int dx, int dy)
    {
        for (int i = 0; i < 8; i++)
            if (Moore[i].dx == dx && Moore[i].dy == dy) return i;
        return 6; // default W if not found (off-grid backtrack)
    }

    // ============================================================
    // CENTERLINE — Zhang-Suen thinning then skeleton path tracing
    // ============================================================
    private static List<Polyline2> TraceCenterlines(bool[,] bin, int w, int h)
    {
        var skeleton = ZhangSuenThin(bin, w, h);
        return TraceSkeletonPaths(skeleton, w, h);
    }

    // Zhang-Suen thinning algorithm (iterative, standard implementation)
    private static bool[,] ZhangSuenThin(bool[,] src, int w, int h)
    {
        var img = (bool[,])src.Clone();
        bool changed = true;

        while (changed)
        {
            changed = false;
            var toRemove = new List<(int x, int y)>();

            // Pass 1
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                if (!img[x, y]) continue;
                if (ZhangSuenCheck(img, x, y, pass: 1)) toRemove.Add((x, y));
            }
            foreach (var (x, y) in toRemove) { img[x, y] = false; changed = true; }

            toRemove.Clear();

            // Pass 2
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                if (!img[x, y]) continue;
                if (ZhangSuenCheck(img, x, y, pass: 2)) toRemove.Add((x, y));
            }
            foreach (var (x, y) in toRemove) { img[x, y] = false; changed = true; }
        }

        return img;
    }

    private static bool ZhangSuenCheck(bool[,] img, int x, int y, int pass)
    {
        // Neighbors in clockwise order: P2(N), P3(NE), P4(E), P5(SE),
        //                               P6(S), P7(SW), P8(W), P9(NW)
        bool p2 = img[x,   y-1]; bool p3 = img[x+1, y-1];
        bool p4 = img[x+1, y  ]; bool p5 = img[x+1, y+1];
        bool p6 = img[x,   y+1]; bool p7 = img[x-1, y+1];
        bool p8 = img[x-1, y  ]; bool p9 = img[x-1, y-1];

        int n = (p2?1:0)+(p3?1:0)+(p4?1:0)+(p5?1:0)+(p6?1:0)+(p7?1:0)+(p8?1:0)+(p9?1:0);
        if (n < 2 || n > 6) return false;

        // Count 0→1 transitions in ordered sequence
        int t = 0;
        bool[] ring = [p2, p3, p4, p5, p6, p7, p8, p9, p2];
        for (int i = 0; i < 8; i++) if (!ring[i] && ring[i+1]) t++;
        if (t != 1) return false;

        if (pass == 1) return !(p2 && p4 && p6) && !(p4 && p6 && p8);
        else           return !(p2 && p4 && p8) && !(p2 && p6 && p8);
    }

    // Trace paths along the skeleton by walking connected pixels
    private static List<Polyline2> TraceSkeletonPaths(bool[,] skel, int w, int h)
    {
        var visited = new bool[w, h];
        var paths   = new List<Polyline2>();

        // Collect all skeleton pixels
        var pixels = new HashSet<(int, int)>();
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (skel[x, y]) pixels.Add((x, y));

        // Find endpoints (exactly 1 foreground neighbor) and start traces from them
        // Then handle cycles
        int FgNeighborCount(int x, int y)
        {
            int cnt = 0;
            foreach (var (dx, dy) in Moore)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && skel[nx, ny]) cnt++;
            }
            return cnt;
        }

        // Start from endpoints first
        foreach (var (ex, ey) in pixels.OrderBy(p => FgNeighborCount(p.Item1, p.Item2)))
        {
            if (visited[ex, ey]) continue;
            if (FgNeighborCount(ex, ey) > 2 && pixels.Any(p => !visited[p.Item1, p.Item2] && FgNeighborCount(p.Item1, p.Item2) <= 1))
                continue; // defer junction pixels

            var path = new List<Point2>();
            int cx = ex, cy = ey;

            while (!visited[cx, cy])
            {
                visited[cx, cy] = true;
                path.Add(new Point2(cx, cy));

                bool moved = false;
                foreach (var (dx, dy) in Moore)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h && skel[nx, ny] && !visited[nx, ny])
                    {
                        cx = nx; cy = ny; moved = true; break;
                    }
                }
                if (!moved) break;
            }

            if (path.Count >= 2)
                paths.Add(new Polyline2 { IsClosed = false, Points = path });
        }

        return paths;
    }

    // ============================================================
    // Ramer-Douglas-Peucker simplification
    // ============================================================
    private static Polyline2 RdpSimplify(Polyline2 poly, double toleranceMm)
    {
        if (poly.Points.Count <= 2) return poly;

        var pts = poly.IsClosed
            ? [.. poly.Points, poly.Points[0]]  // close the loop for proper simplification
            : poly.Points;

        var keep = new bool[pts.Count];
        keep[0] = true;
        keep[pts.Count - 1] = true;
        RdpRecurse(pts, 0, pts.Count - 1, toleranceMm * toleranceMm, keep);

        var simplified = pts.Where((_, i) => keep[i]).ToList();
        if (poly.IsClosed && simplified.Count >= 2 &&
            simplified[^1].X == simplified[0].X && simplified[^1].Y == simplified[0].Y)
            simplified.RemoveAt(simplified.Count - 1); // remove duplicated close point

        return new Polyline2 { IsClosed = poly.IsClosed, Points = simplified };
    }

    private static void RdpRecurse(
        List<Point2> pts, int start, int end, double tolSq, bool[] keep)
    {
        if (end - start <= 1) return;

        double maxDistSq = 0;
        int maxIdx = start;

        var (ax, ay) = (pts[start].X, pts[start].Y);
        var (bx, by) = (pts[end].X, pts[end].Y);
        double dx = bx - ax, dy = by - ay;
        double lenSq = dx * dx + dy * dy;

        for (int i = start + 1; i < end; i++)
        {
            double px = pts[i].X - ax, py = pts[i].Y - ay;
            double distSq;
            if (lenSq < 1e-12)
            {
                distSq = px * px + py * py;
            }
            else
            {
                double t = Math.Clamp((px * dx + py * dy) / lenSq, 0, 1);
                double qx = px - t * dx, qy = py - t * dy;
                distSq = qx * qx + qy * qy;
            }

            if (distSq > maxDistSq) { maxDistSq = distSq; maxIdx = i; }
        }

        if (maxDistSq > tolSq)
        {
            keep[maxIdx] = true;
            RdpRecurse(pts, start, maxIdx, tolSq, keep);
            RdpRecurse(pts, maxIdx, end, tolSq, keep);
        }
    }
}
