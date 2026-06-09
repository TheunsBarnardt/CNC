// Types + API client for the backend project endpoints. Mirrors
// backend/Api/ProjectApi.cs DTOs (camelCased, enums as strings).
import { BACKEND_URL } from "@/lib/backend";

export type Units = "Millimeters" | "Inches";
export type TableOrigin =
  | "BottomLeft"
  | "BottomRight"
  | "TopLeft"
  | "TopRight"
  | "Center";
export type ImportedFileKind = "Svg" | "Dxf";

export interface TableSettings {
  widthMm: number;
  heightMm: number;
  origin: TableOrigin;
  materialThicknessMm: number;
}

export interface FileSummary {
  id: string;
  fileName: string;
  displayName: string;
  kind: ImportedFileKind;
  visible: boolean;
  pathCount: number;
  closedPathCount: number;
  openPathCount: number;
  totalPoints: number;
  widthMm: number;
  heightMm: number;
  layers: string[];
  warnings: string[];
}

export interface ProjectDto {
  name: string;
  units: Units;
  table: TableSettings;
  files: FileSummary[];
}

export interface ImportResult {
  fileName: string;
  ok: boolean;
  error: string | null;
  file: FileSummary | null;
}

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`;
    try {
      const body = (await res.json()) as { error?: string };
      if (body.error) message = body.error;
    } catch {
      // keep the status text
    }
    throw new Error(message);
  }
  return res.json() as Promise<T>;
}

export const projectApi = {
  get: () => fetch(`${BACKEND_URL}/api/project`).then((r) => json<ProjectDto>(r)),

  newProject: () =>
    fetch(`${BACKEND_URL}/api/project/new`, { method: "POST" }).then((r) =>
      json<ProjectDto>(r),
    ),

  updateSettings: (settings: {
    name?: string;
    units?: Units;
    table?: TableSettings;
  }) =>
    fetch(`${BACKEND_URL}/api/project/settings`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(settings),
    }).then((r) => json<ProjectDto>(r)),

  importFiles: (files: File[]) => {
    const form = new FormData();
    for (const f of files) form.append("files", f, f.name);
    return fetch(`${BACKEND_URL}/api/project/files`, {
      method: "POST",
      body: form,
    }).then((r) => json<{ results: ImportResult[]; project: ProjectDto }>(r));
  },

  updateFile: (id: string, patch: { displayName?: string; visible?: boolean }) =>
    fetch(`${BACKEND_URL}/api/project/files/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(patch),
    }).then((r) => json<ProjectDto>(r)),

  deleteFile: (id: string) =>
    fetch(`${BACKEND_URL}/api/project/files/${id}`, { method: "DELETE" }).then(
      (r) => json<ProjectDto>(r),
    ),

  exportUrl: `${BACKEND_URL}/api/project/export`,

  loadProject: (file: File) => {
    const form = new FormData();
    form.append("file", file, file.name);
    return fetch(`${BACKEND_URL}/api/project/load`, {
      method: "POST",
      body: form,
    }).then((r) => json<ProjectDto>(r));
  },
};

// --- unit display helpers ---------------------------------------------------
// Everything is stored in mm; Inches is purely a display/entry preference.

export const MM_PER_INCH = 25.4;

export function displayLength(mm: number, units: Units): string {
  return units === "Inches" ? (mm / MM_PER_INCH).toFixed(3) : mm.toFixed(1);
}

export function parseLengthToMm(text: string, units: Units): number | null {
  const value = Number(text);
  if (!Number.isFinite(value)) return null;
  return units === "Inches" ? value * MM_PER_INCH : value;
}

export function unitSuffix(units: Units): string {
  return units === "Inches" ? "in" : "mm";
}
