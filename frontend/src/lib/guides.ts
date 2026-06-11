export interface Guide {
  id: string;
  /** 'h' = horizontal line at constant Y; 'v' = vertical line at constant X. */
  axis: "h" | "v";
  posMm: number;
  locked: boolean;
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
