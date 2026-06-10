// Toolpath playback model. Turns the neutral toolpath (ordered cuts; rapids
// implicit between them) into a flat, time-indexed list of motion segments so
// the viewport can scrub to any moment like a video.
import type { Vec } from "@/lib/geometry";
import type { TableSettings, ToolpathCut, ToolpathResult } from "@/lib/project";

/**
 * Rapid traverse rate used for time estimates. GRBL doesn't tell us its
 * $110/$111 max rates until we're connected (Milestone 3), so assume a
 * typical DIY-gantry value; cut timing uses the real per-cut feed rates.
 */
export const RAPID_RATE_MM_MIN = 6000;

export type SegmentKind = "rapid" | "pierce" | "cut";

export interface SimSegment {
  kind: SegmentKind;
  /** Part of a lead-in/lead-out rather than the contour proper. */
  lead: boolean;
  from: Vec;
  to: Vec;
  /** Timeline window in seconds; t0 === t1 never happens (zero-length skipped). */
  t0: number;
  t1: number;
  cutIndex: number;
}

export interface Simulation {
  segments: SimSegment[];
  totalS: number;
  cutCount: number;
  totalCutLengthMm: number;
  totalRapidLengthMm: number;
  warnings: string[];
}

/** Where the head starts and parks — mirrors the post-processor's work origin. */
export function workOriginPoint(table: TableSettings): Vec {
  switch (table.origin) {
    case "BottomRight": return [table.widthMm, 0];
    case "TopLeft": return [0, table.heightMm];
    case "TopRight": return [table.widthMm, table.heightMm];
    case "Center": return [table.widthMm / 2, table.heightMm / 2];
    default: return [0, 0];
  }
}

function dist(a: Vec, b: Vec): number {
  return Math.hypot(b[0] - a[0], b[1] - a[1]);
}

export function buildSimulation(
  toolpath: ToolpathResult,
  table: TableSettings,
): Simulation {
  const segments: SimSegment[] = [];
  let t = 0;
  let pos = workOriginPoint(table);

  const push = (
    kind: SegmentKind,
    lead: boolean,
    from: Vec,
    to: Vec,
    seconds: number,
    cutIndex: number,
  ) => {
    if (seconds <= 0) return;
    segments.push({ kind, lead, from, to, t0: t, t1: t + seconds, cutIndex });
    t += seconds;
  };

  toolpath.cuts.forEach((cut: ToolpathCut, cutIndex: number) => {
    const points = cut.points;
    if (points.length === 0) return;

    const pierce = points[0] as Vec;
    push("rapid", false, pos, pierce, (dist(pos, pierce) / RAPID_RATE_MM_MIN) * 60, cutIndex);
    // Dwell: the torch burns in place while the arc establishes.
    push("pierce", false, pierce, pierce, cut.pierceDelayS, cutIndex);

    const mmPerS = cut.feedRateMmMin / 60;
    for (let i = 1; i < points.length; i++) {
      const from = points[i - 1] as Vec;
      const to = points[i] as Vec;
      // Segment i (ending at point i) is lead-in while i <= leadInPointCount,
      // lead-out once it starts at or past the lead-out boundary.
      const lead =
        i <= cut.leadInPointCount ||
        i - 1 >= points.length - 1 - cut.leadOutPointCount;
      push("cut", lead, from, to, dist(from, to) / mmPerS, cutIndex);
    }
    pos = points[points.length - 1] as Vec;
  });

  // Park where the post-processor parks, so playback matches the G-code.
  const origin = workOriginPoint(table);
  push("rapid", false, pos, origin, (dist(pos, origin) / RAPID_RATE_MM_MIN) * 60, -1);

  return {
    segments,
    totalS: t,
    cutCount: toolpath.cuts.length,
    totalCutLengthMm: toolpath.totalCutLengthMm,
    totalRapidLengthMm: toolpath.totalRapidLengthMm,
    warnings: toolpath.warnings,
  };
}

export interface SimState {
  pos: Vec;
  torchOn: boolean;
  /** Index into toolpath.cuts, or -1 outside any cut. */
  cutIndex: number;
  segmentIndex: number;
}

/** Head state at time t (clamped to the timeline). Binary search + lerp. */
export function stateAt(sim: Simulation, tRaw: number): SimState | null {
  const { segments } = sim;
  if (segments.length === 0) return null;
  const t = Math.min(Math.max(tRaw, 0), sim.totalS);

  let lo = 0,
    hi = segments.length - 1;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (segments[mid].t1 < t) lo = mid + 1;
    else hi = mid;
  }
  const seg = segments[lo];
  const f = seg.t1 === seg.t0 ? 1 : (t - seg.t0) / (seg.t1 - seg.t0);
  return {
    pos: [
      seg.from[0] + (seg.to[0] - seg.from[0]) * f,
      seg.from[1] + (seg.to[1] - seg.from[1]) * f,
    ],
    torchOn: seg.kind !== "rapid",
    cutIndex: seg.cutIndex,
    segmentIndex: lo,
  };
}

export function formatTime(seconds: number): string {
  const s = Math.max(0, Math.round(seconds));
  const m = Math.floor(s / 60);
  return `${m}:${String(s % 60).padStart(2, "0")}`;
}
