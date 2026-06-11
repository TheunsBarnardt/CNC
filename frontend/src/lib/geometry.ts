// Part-transform + hit-testing math for the viewport. Mirrors the backend's
// PartTransform (backend/Models/PartTransform.cs) exactly:
//   world = (part.x, part.y) + R(rotationDeg) · (local − pivot) + pivot
// where pivot = the file's local bounding-box center.
import type { GeometryPath, Part } from "@/lib/project";

export interface BBox {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export type Vec = [number, number];

export function bboxOfPaths(paths: GeometryPath[]): BBox {
  let minX = Infinity,
    minY = Infinity,
    maxX = -Infinity,
    maxY = -Infinity;
  for (const path of paths) {
    for (const [x, y] of path.points) {
      if (x < minX) minX = x;
      if (y < minY) minY = y;
      if (x > maxX) maxX = x;
      if (y > maxY) maxY = y;
    }
  }
  if (minX > maxX) return { minX: 0, minY: 0, maxX: 0, maxY: 0 };
  return { minX, minY, maxX, maxY };
}

export function localPivot(paths: GeometryPath[]): Vec {
  const b = bboxOfPaths(paths);
  return [(b.minX + b.maxX) / 2, (b.minY + b.maxY) / 2];
}

export function partToWorld(part: Part, pivot: Vec, local: Vec): Vec {
  const r = (part.rotationDeg * Math.PI) / 180;
  const cos = Math.cos(r),
    sin = Math.sin(r);
  // Apply scale around pivot before rotation (enables mirror via negative scale).
  const sx = part.scaleX ?? 1, sy = part.scaleY ?? 1;
  const dx = (local[0] - pivot[0]) * sx;
  const dy = (local[1] - pivot[1]) * sy;
  return [
    part.x + pivot[0] + dx * cos - dy * sin,
    part.y + pivot[1] + dx * sin + dy * cos,
  ];
}

export function worldToPartLocal(part: Part, pivot: Vec, world: Vec): Vec {
  const r = (part.rotationDeg * Math.PI) / 180;
  const cos = Math.cos(r),
    sin = Math.sin(r);
  const ux = world[0] - part.x - pivot[0],
    uy = world[1] - part.y - pivot[1];
  // Inverse rotation then inverse scale.
  const sx = part.scaleX ?? 1, sy = part.scaleY ?? 1;
  const rdx = ux * cos + uy * sin;
  const rdy = -ux * sin + uy * cos;
  return [
    pivot[0] + (sx !== 0 ? rdx / sx : 0),
    pivot[1] + (sy !== 0 ? rdy / sy : 0),
  ];
}

/** Axis-aligned world bbox of a placed part. */
export function partWorldBBox(part: Part, paths: GeometryPath[]): BBox {
  const pivot = localPivot(paths);
  let minX = Infinity,
    minY = Infinity,
    maxX = -Infinity,
    maxY = -Infinity;
  for (const path of paths) {
    for (const p of path.points) {
      const [x, y] = partToWorld(part, pivot, p as Vec);
      if (x < minX) minX = x;
      if (y < minY) minY = y;
      if (x > maxX) maxX = x;
      if (y > maxY) maxY = y;
    }
  }
  if (minX > maxX) return { minX: 0, minY: 0, maxX: 0, maxY: 0 };
  return { minX, minY, maxX, maxY };
}

function pointInPolygon(p: Vec, points: [number, number][]): boolean {
  let inside = false;
  for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
    const [xi, yi] = points[i];
    const [xj, yj] = points[j];
    if (
      yi > p[1] !== yj > p[1] &&
      p[0] < ((xj - xi) * (p[1] - yi)) / (yj - yi) + xi
    )
      inside = true;
  }
  return inside;
}

function distToSegmentSq(p: Vec, a: Vec, b: Vec): number {
  const abx = b[0] - a[0],
    aby = b[1] - a[1];
  const lenSq = abx * abx + aby * aby;
  let t = lenSq > 0 ? ((p[0] - a[0]) * abx + (p[1] - a[1]) * aby) / lenSq : 0;
  t = Math.max(0, Math.min(1, t));
  const dx = p[0] - (a[0] + t * abx),
    dy = p[1] - (a[1] + t * aby);
  return dx * dx + dy * dy;
}

/**
 * Hit test in part-local space: inside any closed contour, or within
 * `tolerance` (world mm) of any open path segment.
 */
export function hitTestPart(
  part: Part,
  paths: GeometryPath[],
  world: Vec,
  tolerance: number,
): boolean {
  const pivot = localPivot(paths);
  const local = worldToPartLocal(part, pivot, world);
  const tolSq = tolerance * tolerance;
  for (const path of paths) {
    if (path.closed) {
      if (pointInPolygon(local, path.points)) return true;
      // Also allow grabbing the outline itself.
      for (let i = 0; i < path.points.length; i++) {
        const a = path.points[i] as Vec;
        const b = path.points[(i + 1) % path.points.length] as Vec;
        if (distToSegmentSq(local, a, b) <= tolSq) return true;
      }
    } else {
      for (let i = 0; i + 1 < path.points.length; i++) {
        if (
          distToSegmentSq(local, path.points[i] as Vec, path.points[i + 1] as Vec) <= tolSq
        )
          return true;
      }
    }
  }
  return false;
}
