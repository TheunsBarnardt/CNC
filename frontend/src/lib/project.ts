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

/**
 * A placed instance of a file on the table. Mirrors backend Part: translation
 * in mm + rotation CCW (degrees) about the file's local bbox center.
 */
export interface Part {
  id: string;
  fileId: string;
  x: number;
  y: number;
  rotationDeg: number;
}

export interface ProjectDto {
  name: string;
  units: Units;
  table: TableSettings;
  files: FileSummary[];
  parts: Part[];
}

/** One file's local-space geometry, as sent by GET /api/project/geometry. */
export interface GeometryPath {
  layer: string | null;
  closed: boolean;
  points: [number, number][];
}

export interface FileGeometry {
  fileId: string;
  paths: GeometryPath[];
}

export interface ImportResult {
  fileName: string;
  ok: boolean;
  error: string | null;
  file: FileSummary | null;
}

export type CutSide = "Outside" | "Inside" | "OnLine";
export type LeadType = "None" | "Line" | "Arc";
export type MachineType = "Plasma" | "Laser" | "VinylKnife";

/** CAM parameters persisted on the project (mirrors CamSettings). */
export interface CamSettings {
  operationMode: MachineType;
  feedRateMmMin: number;
  // Plasma-only
  kerfWidthMm: number;
  pierceDelayS: number;
  cutHeightMm: number;
  pierceHeightMm: number;
  leadInType: LeadType;
  leadInLengthMm: number;
  leadOutType: LeadType;
  leadOutLengthMm: number;
  // Laser-only
  laserPowerPercent: number;
  // Vinyl / drag-knife only
  vinylBladeOffsetMm: number;
  vinylOvercutMm: number;
  vinylKnifeUpMm: number;
  vinylKnifeDownMm: number;
}

/** App-level material preset (mirrors MaterialProfile). */
export interface MaterialProfile {
  id: string;
  name: string;
  material: string;
  thicknessMm: number;
  kerfWidthMm: number;
  feedRateMmMin: number;
  pierceDelayS: number;
  cutHeightMm: number;
  pierceHeightMm: number;
}

/** One torch-on motion from POST /api/project/toolpath (mirrors CutDto). */
export interface ToolpathCut {
  partId: string;
  sourcePathId: string;
  layer: string | null;
  side: CutSide;
  closed: boolean;
  leadInPointCount: number;
  leadOutPointCount: number;
  pierceDelayS: number;
  feedRateMmMin: number;
  cutLengthMm: number;
  points: [number, number][];
}

export interface ToolpathResult {
  cuts: ToolpathCut[];
  totalCutLengthMm: number;
  totalRapidLengthMm: number;
  warnings: string[];
}

/** Settings for one auto-nest run (POST /api/project/nest). */
export interface NestSettings {
  marginMm: number;
  spacingMm: number;
  rotationStepDeg: number;
}

export interface NestResult {
  project: ProjectDto;
  placedCount: number;
  skippedCount: number;
  warnings: string[];
}

/** A registered post-processor (GET /api/posts). */
export interface PostProcessorInfo {
  id: string;
  displayName: string;
  description: string;
  fileExtension: string;
  isDefault: boolean;
}

/** Result of POST /api/project/gcode. */
export interface GcodeResult {
  postId: string;
  fileName: string;
  gcode: string;
  lineCount: number;
  cutCount: number;
  totalCutLengthMm: number;
  totalRapidLengthMm: number;
  warnings: string[];
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

  getGeometry: () =>
    fetch(`${BACKEND_URL}/api/project/geometry`).then((r) =>
      json<{ files: FileGeometry[] }>(r),
    ),

  createPart: (fileId: string) =>
    fetch(`${BACKEND_URL}/api/project/parts`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fileId }),
    }).then((r) => json<ProjectDto>(r)),

  updatePart: (
    id: string,
    patch: { x?: number; y?: number; rotationDeg?: number },
  ) =>
    fetch(`${BACKEND_URL}/api/project/parts/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(patch),
    }).then((r) => json<ProjectDto>(r)),

  duplicatePart: (id: string) =>
    fetch(`${BACKEND_URL}/api/project/parts/${id}/duplicate`, {
      method: "POST",
    }).then((r) => json<ProjectDto>(r)),

  deletePart: (id: string) =>
    fetch(`${BACKEND_URL}/api/project/parts/${id}`, { method: "DELETE" }).then(
      (r) => json<ProjectDto>(r),
    ),

  getCam: () =>
    fetch(`${BACKEND_URL}/api/project/cam`).then((r) => json<CamSettings>(r)),

  updateCam: (settings: CamSettings) =>
    fetch(`${BACKEND_URL}/api/project/cam`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(settings),
    }).then((r) => json<CamSettings>(r)),

  listProfiles: () =>
    fetch(`${BACKEND_URL}/api/profiles`).then((r) => json<MaterialProfile[]>(r)),

  createProfile: (profile: Omit<MaterialProfile, "id">) =>
    fetch(`${BACKEND_URL}/api/profiles`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(profile),
    }).then((r) => json<MaterialProfile>(r)),

  updateProfile: (profile: MaterialProfile) =>
    fetch(`${BACKEND_URL}/api/profiles/${profile.id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(profile),
    }).then((r) => json<MaterialProfile>(r)),

  deleteProfile: (id: string) =>
    fetch(`${BACKEND_URL}/api/profiles/${id}`, { method: "DELETE" }).then((r) => {
      if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
    }),

  nest: (settings: NestSettings) =>
    fetch(`${BACKEND_URL}/api/project/nest`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(settings),
    }).then((r) => json<NestResult>(r)),

  generateToolpath: () =>
    fetch(`${BACKEND_URL}/api/project/toolpath`, { method: "POST" }).then((r) =>
      json<ToolpathResult>(r),
    ),

  listPosts: () =>
    fetch(`${BACKEND_URL}/api/posts`).then((r) => json<PostProcessorInfo[]>(r)),

  generateGcode: (postId?: string) =>
    fetch(`${BACKEND_URL}/api/project/gcode`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ postId: postId ?? null }),
    }).then((r) => json<GcodeResult>(r)),

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
