export interface Guide {
  id: string;
  /** 'h' = horizontal line at constant Y
   *  'v' = vertical line at constant X
   *  'a' = angled line through pointMm at angleDeg degrees */
  axis: "h" | "v" | "a";
  /** For h: Y position (mm).  For v: X position (mm).  For a: unused (0). */
  posMm: number;
  /** Angled guide: angle in degrees, 0 = horizontal, CCW positive. */
  angleDeg?: number;
  /** Angled guide: world-space point the line passes through. */
  pointMm?: [number, number];
  locked: boolean;
  label?: string;
}

const STORAGE_KEY = "grblcam-guides";

export function loadGuides(): Guide[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as Guide[]) : [];
  } catch {
    return [];
  }
}

export function saveGuides(guides: Guide[]): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(guides));
}
