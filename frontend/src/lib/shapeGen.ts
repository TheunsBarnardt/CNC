/**
 * Pure geometry generators for in-app shape creation.
 * All functions return paths in local space normalized to bbox min = (0,0),
 * matching the convention used by imported SVG/DXF files.
 */
import type { GeometryPath } from "./project";

type Pt = [number, number];

/** Normalize a list of paths so their combined bounding box starts at (0,0). */
function normalize(paths: GeometryPath[]): GeometryPath[] {
  if (paths.length === 0) return paths;
  let minX = Infinity,
    minY = Infinity;
  for (const p of paths)
    for (const [x, y] of p.points) {
      if (x < minX) minX = x;
      if (y < minY) minY = y;
    }
  return paths.map((p) => ({
    ...p,
    points: p.points.map(([x, y]) => [x - minX, y - minY] as Pt),
  }));
}

/** A single open line segment from (0,0) to (dx,dy), normalized. */
export function genLine(x1: number, y1: number, x2: number, y2: number): GeometryPath[] {
  return normalize([{ layer: null, closed: false, points: [[x1, y1], [x2, y2]] }]);
}

/** Rectangle with optional corner radius (approximated with arcs). */
export function genRect(w: number, h: number, r = 0): GeometryPath[] {
  if (w <= 0 || h <= 0) return [];
  const cr = Math.min(r, w / 2, h / 2);
  let pts: Pt[];

  if (cr <= 0) {
    pts = [[0, 0], [w, 0], [w, h], [0, h]];
  } else {
    // Approximate each rounded corner with arc segments (8 pts per corner).
    const N = 8;
    pts = [];
    const corners: [cx: number, cy: number, startAngle: number][] = [
      [cr, cr, Math.PI],            // bottom-left
      [w - cr, cr, Math.PI * 1.5],  // bottom-right
      [w - cr, h - cr, 0],          // top-right
      [cr, h - cr, Math.PI * 0.5],  // top-left
    ];
    for (const [cx, cy, start] of corners) {
      for (let i = 0; i <= N; i++) {
        const a = start + (i / N) * (Math.PI / 2);
        pts.push([cx + cr * Math.cos(a), cy + cr * Math.sin(a)]);
      }
    }
  }

  return [{ layer: null, closed: true, points: pts }];
}

const CIRCLE_SEGS = 128;

/** Circle approximated as a polygon with many segments. */
export function genCircle(rx: number, ry: number): GeometryPath[] {
  if (rx <= 0 || ry <= 0) return [];
  const pts: Pt[] = [];
  for (let i = 0; i < CIRCLE_SEGS; i++) {
    const a = (i / CIRCLE_SEGS) * Math.PI * 2;
    pts.push([rx + rx * Math.cos(a), ry + ry * Math.sin(a)]);
  }
  return [{ layer: null, closed: true, points: pts }];
}

/** Regular polygon centered at bbox center. */
export function genPolygon(r: number, sides: number): GeometryPath[] {
  const n = Math.max(3, Math.round(sides));
  if (r <= 0) return [];
  const pts: Pt[] = [];
  for (let i = 0; i < n; i++) {
    // Start at top (−π/2 offset) so a 3-sided polygon has a flat bottom.
    const a = ((i / n) * Math.PI * 2) - Math.PI / 2;
    pts.push([r + r * Math.cos(a), r + r * Math.sin(a)]);
  }
  return [{ layer: null, closed: true, points: pts }];
}

/** Star with N points. innerRatio = inner-radius / outer-radius (0.1–0.9). */
export function genStar(outerR: number, points: number, innerRatio: number): GeometryPath[] {
  const n = Math.max(3, Math.round(points));
  if (outerR <= 0) return [];
  const innerR = outerR * Math.max(0.1, Math.min(0.9, innerRatio));
  const pts: Pt[] = [];
  const total = n * 2;
  for (let i = 0; i < total; i++) {
    const a = ((i / total) * Math.PI * 2) - Math.PI / 2;
    const r = i % 2 === 0 ? outerR : innerR;
    pts.push([outerR + r * Math.cos(a), outerR + r * Math.sin(a)]);
  }
  return [{ layer: null, closed: true, points: pts }];
}

// --- convenience: generate from two world points ----------------------------

/** Line from two world points; returns normalized paths + initial placement. */
export function lineFromPoints(
  x1: number, y1: number, x2: number, y2: number,
): { paths: GeometryPath[]; x: number; y: number } {
  const minX = Math.min(x1, x2), minY = Math.min(y1, y2);
  const paths = normalize([
    { layer: null, closed: false, points: [[x1, y1], [x2, y2]] },
  ]);
  return { paths, x: minX, y: minY };
}

/** Axis-aligned rectangle from two diagonal world points. */
export function rectFromPoints(
  x1: number, y1: number, x2: number, y2: number, cornerR = 0,
): { paths: GeometryPath[]; x: number; y: number } {
  const [lx, rx] = x1 < x2 ? [x1, x2] : [x2, x1];
  const [by, ty] = y1 < y2 ? [y1, y2] : [y2, y1];
  return { paths: genRect(rx - lx, ty - by, cornerR), x: lx, y: by };
}

/** Ellipse fitted into a bounding box from two diagonal world points. */
export function ellipseFromPoints(
  x1: number, y1: number, x2: number, y2: number,
): { paths: GeometryPath[]; x: number; y: number } {
  const [lx, rx] = x1 < x2 ? [x1, x2] : [x2, x1];
  const [by, ty] = y1 < y2 ? [y1, y2] : [y2, y1];
  return { paths: genCircle((rx - lx) / 2, (ty - by) / 2), x: lx, y: by };
}

/** Circle/Ellipse from center + radial drag point. */
export function circleFromCenter(
  cx: number, cy: number, ex: number, ey: number,
): { paths: GeometryPath[]; x: number; y: number } {
  const r = Math.hypot(ex - cx, ey - cy);
  return { paths: genCircle(r, r), x: cx - r, y: cy - r };
}

/** Polygon from center + radius drag point. */
export function polygonFromCenter(
  cx: number, cy: number, ex: number, ey: number, sides: number,
): { paths: GeometryPath[]; x: number; y: number } {
  const r = Math.hypot(ex - cx, ey - cy);
  return { paths: genPolygon(r, sides), x: cx - r, y: cy - r };
}

/** Star from center + outer-radius drag point. */
export function starFromCenter(
  cx: number, cy: number, ex: number, ey: number,
  points: number, innerRatio: number,
): { paths: GeometryPath[]; x: number; y: number } {
  const r = Math.hypot(ex - cx, ey - cy);
  return { paths: genStar(r, points, innerRatio), x: cx - r, y: cy - r };
}
