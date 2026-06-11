/**
 * Convert text to geometry paths using opentype.js.
 * Fonts are loaded from /public/fonts/ and cached for the session.
 */
import * as opentype from "opentype.js";
import type { GeometryPath } from "./project";

export interface FontInfo {
  name: string;
  url: string;
}

export const AVAILABLE_FONTS: FontInfo[] = [
  { name: "Roboto", url: "/fonts/Roboto-Regular.ttf" },
  { name: "Roboto Bold", url: "/fonts/Roboto-Bold.ttf" },
];

const fontCache = new Map<string, opentype.Font>();

export async function loadFont(url: string): Promise<opentype.Font> {
  if (fontCache.has(url)) return fontCache.get(url)!;
  // opentype.js v2 requires fetching the buffer manually and calling parse().
  const buffer = await fetch(url).then((r) => r.arrayBuffer());
  const font = opentype.parse(buffer);
  fontCache.set(url, font);
  return font;
}

// Bezier flattening tolerance in font-coordinate units (at RENDER_SIZE=1000, ~0.05%).
const TOL = 0.5;

function ptLineDist(
  px: number, py: number,
  ax: number, ay: number,
  bx: number, by: number,
): number {
  const dx = bx - ax, dy = by - ay;
  const len2 = dx * dx + dy * dy;
  if (len2 < 1e-12) return Math.hypot(px - ax, py - ay);
  const t = Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / len2));
  return Math.hypot(px - (ax + t * dx), py - (ay + t * dy));
}

function flatCubic(
  pts: [number, number][],
  x0: number, y0: number,
  x1: number, y1: number,
  x2: number, y2: number,
  x3: number, y3: number,
  depth = 0,
): void {
  if (depth > 14) { pts.push([x3, y3]); return; }
  if (
    ptLineDist(x1, y1, x0, y0, x3, y3) < TOL &&
    ptLineDist(x2, y2, x0, y0, x3, y3) < TOL
  ) {
    pts.push([x3, y3]);
    return;
  }
  const mx01x = (x0 + x1) / 2, mx01y = (y0 + y1) / 2;
  const mx12x = (x1 + x2) / 2, mx12y = (y1 + y2) / 2;
  const mx23x = (x2 + x3) / 2, mx23y = (y2 + y3) / 2;
  const m012x = (mx01x + mx12x) / 2, m012y = (mx01y + mx12y) / 2;
  const m123x = (mx12x + mx23x) / 2, m123y = (mx12y + mx23y) / 2;
  const mx = (m012x + m123x) / 2, my = (m012y + m123y) / 2;
  flatCubic(pts, x0, y0, mx01x, mx01y, m012x, m012y, mx, my, depth + 1);
  flatCubic(pts, mx, my, m123x, m123y, mx23x, mx23y, x3, y3, depth + 1);
}

function flatQuad(
  pts: [number, number][],
  x0: number, y0: number,
  x1: number, y1: number,
  x2: number, y2: number,
): void {
  // Elevate quadratic → cubic.
  const cp1x = x0 + (2 / 3) * (x1 - x0), cp1y = y0 + (2 / 3) * (y1 - y0);
  const cp2x = x2 + (2 / 3) * (x1 - x2), cp2y = y2 + (2 / 3) * (y1 - y2);
  flatCubic(pts, x0, y0, cp1x, cp1y, cp2x, cp2y, x2, y2);
}

function commandsToGeometry(cmds: opentype.PathCommand[]): GeometryPath[] {
  const result: GeometryPath[] = [];
  let pts: [number, number][] = [];
  let cx = 0, cy = 0;

  const flush = (closed: boolean) => {
    if (pts.length >= 2) result.push({ layer: null, closed, points: pts });
    pts = [];
  };

  for (const cmd of cmds) {
    switch (cmd.type) {
      case "M":
        if (pts.length >= 2) flush(false);
        else pts = [];
        cx = cmd.x; cy = -cmd.y; // flip Y: opentype is screen-Y-down
        pts = [[cx, cy]];
        break;
      case "L":
        cx = cmd.x; cy = -cmd.y;
        pts.push([cx, cy]);
        break;
      case "C": {
        const end: [number, number][] = [];
        flatCubic(end, cx, cy, cmd.x1, -cmd.y1, cmd.x2, -cmd.y2, cmd.x, -cmd.y);
        for (const p of end) pts.push(p);
        cx = cmd.x; cy = -cmd.y;
        break;
      }
      case "Q": {
        const end: [number, number][] = [];
        flatQuad(end, cx, cy, cmd.x1, -cmd.y1, cmd.x, -cmd.y);
        for (const p of end) pts.push(p);
        cx = cmd.x; cy = -cmd.y;
        break;
      }
      case "Z":
        flush(true);
        break;
    }
  }
  if (pts.length >= 2) flush(false);
  return result;
}

function normalizePaths(paths: GeometryPath[]): GeometryPath[] {
  if (paths.length === 0) return paths;
  let minX = Infinity, minY = Infinity;
  for (const p of paths)
    for (const [x, y] of p.points) {
      if (x < minX) minX = x;
      if (y < minY) minY = y;
    }
  return paths.map((p) => ({
    ...p,
    points: p.points.map(([x, y]) => [x - minX, y - minY] as [number, number]),
  }));
}

export interface TextOptions {
  text: string;
  fontUrl: string;
  fontSizeMm: number;
  letterSpacing: number; // extra em units between letters (0 = normal)
  bold: boolean;
  italic: boolean;
}

/**
 * Convert text string to normalized GeometryPath[] using opentype.js.
 * Returns paths (bbox min at 0,0) and the resulting bounding box size in mm.
 */
export async function textToGeometry(opts: TextOptions): Promise<{
  paths: GeometryPath[];
  widthMm: number;
  heightMm: number;
}> {
  const font = await loadFont(opts.fontUrl);

  const RENDER_SIZE = 1000; // font units → scale to mm after
  const opPath = font.getPath(opts.text, 0, 0, RENDER_SIZE, {
    letterSpacing: opts.letterSpacing,
  });

  const raw = commandsToGeometry(opPath.commands);
  if (raw.length === 0) return { paths: [], widthMm: 0, heightMm: 0 };

  // Measure raw height to derive scale.
  let minY = Infinity, maxY = -Infinity;
  let minX = Infinity, maxX = -Infinity;
  for (const p of raw)
    for (const [x, y] of p.points) {
      if (x < minX) minX = x;
      if (y < minY) minY = y;
      if (x > maxX) maxX = x;
      if (y > maxY) maxY = y;
    }
  const rawH = maxY - minY;
  if (rawH < 1) return { paths: [], widthMm: 0, heightMm: 0 };

  const scale = opts.fontSizeMm / rawH;
  const scaled: GeometryPath[] = raw.map((p) => ({
    ...p,
    points: p.points.map(([x, y]) => [x * scale, y * scale] as [number, number]),
  }));

  const normalized = normalizePaths(scaled);

  let wMax = 0, hMax = 0;
  for (const p of normalized)
    for (const [x, y] of p.points) {
      if (x > wMax) wMax = x;
      if (y > hMax) hMax = y;
    }

  return { paths: normalized, widthMm: wMax, heightMm: hMax };
}
