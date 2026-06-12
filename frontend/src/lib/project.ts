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
export type ImportedFileKind = "Svg" | "Dxf" | "Shape";

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
  hasBitmap: boolean;
  bitmapTraceSettingsJson: string | null;
}

export interface BitmapTraceSettings {
  thresholdPercent: number;
  invert: boolean;
  brightness: number;   // -1..1
  contrast: number;     // 0.1..3
  mode: "outline" | "centerline";
  simplifyToleranceMm: number;
}

export type LayerOperationMode = "Cut" | "Score" | "Engrave";

/** A named layer for grouping parts (visibility / lock). */
export interface Layer {
  id: string;
  name: string;
  color: string;
  visible: boolean;
  locked: boolean;
  operationMode: LayerOperationMode;
  feedRateMmMinOverride: number | null;
  laserPowerPercentOverride: number | null;
}

/**
 * A placed instance of a file on the table. Mirrors backend Part: translation
 * in mm + rotation CCW (degrees) about the file's local bbox center, with
 * optional scale (defaults to 1; -1 = mirrored).
 */
export interface Part {
  id: string;
  fileId: string;
  x: number;
  y: number;
  rotationDeg: number;
  scaleX: number;
  scaleY: number;
  layerId: string | null;
}

export interface ProjectDto {
  name: string;
  units: Units;
  table: TableSettings;
  files: FileSummary[];
  parts: Part[];
  layers: Layer[];
}

/** One file's local-space geometry, as sent by GET /api/project/geometry. */
export interface GeometryPath {
  id?: string;  // only present on paths received from backend; undefined for client-constructed paths
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
    patch: { x?: number; y?: number; rotationDeg?: number; scaleX?: number; scaleY?: number },
  ) =>
    fetch(`${BACKEND_URL}/api/project/parts/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(patch),
    }).then((r) => json<ProjectDto>(r)),

  reorderPart: (id: string, direction: "up" | "down" | "front" | "back") =>
    fetch(`${BACKEND_URL}/api/project/parts/${id}/reorder`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ direction }),
    }).then((r) => json<ProjectDto>(r)),

  offsetPart: (id: string, offsetMm: number) =>
    fetch(`${BACKEND_URL}/api/project/parts/${id}/offset`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ offsetMm }),
    }).then((r) => json<ProjectDto>(r)),

  updatePathNodes: (fileId: string, pathId: string, points: [number, number][]) =>
    fetch(`${BACKEND_URL}/api/project/files/${fileId}/paths/${pathId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ points }),
    }).then((r) => json<ProjectDto>(r)),

  simplifyFile: (fileId: string, toleranceMm: number) =>
    fetch(`${BACKEND_URL}/api/project/files/${fileId}/simplify`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ toleranceMm }),
    }).then((r) => json<ProjectDto>(r)),

  arrayPart: (
    id: string,
    request: {
      type: "grid" | "circular";
      rows?: number; cols?: number;
      spacingXMm?: number; spacingYMm?: number;
      count?: number; radiusMm?: number;
      startAngleDeg?: number; rotateWithArray?: boolean;
    },
  ) =>
    fetch(`${BACKEND_URL}/api/project/parts/${id}/array`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }).then((r) => json<ProjectDto>(r)),

  testArray: (
    id: string,
    request: {
      rows: number; cols: number; spacingMm: number;
      param1: { type: string; min: number; max: number };
      param2: { type: string; min: number; max: number };
    },
  ): Promise<Blob> =>
    fetch(`${BACKEND_URL}/api/project/parts/${id}/test-array`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }).then((r) => {
      if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
      return r.blob();
    }),

  booleanParts: (
    operation: "unite" | "subtract" | "intersect",
    partIds: string[],
    deleteSources = true,
  ) =>
    fetch(`${BACKEND_URL}/api/project/parts/boolean`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operation, partIds, deleteSources }),
    }).then((r) => json<ProjectDto>(r)),

  createLayer: (name?: string, color?: string) =>
    fetch(`${BACKEND_URL}/api/project/layers`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: name ?? null, color: color ?? null }),
    }).then((r) => json<ProjectDto>(r)),

  updateLayer: (
    id: string,
    patch: {
      name?: string;
      color?: string;
      visible?: boolean;
      locked?: boolean;
      operationMode?: LayerOperationMode;
      feedRateMmMinOverride?: number | null;
      laserPowerPercentOverride?: number | null;
    },
  ) =>
    fetch(`${BACKEND_URL}/api/project/layers/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(patch),
    }).then((r) => json<ProjectDto>(r)),

  deleteLayer: (id: string) =>
    fetch(`${BACKEND_URL}/api/project/layers/${id}`, { method: "DELETE" }).then(
      (r) => json<ProjectDto>(r),
    ),

  duplicatePart: (id: string) =>
    fetch(`${BACKEND_URL}/api/project/parts/${id}/duplicate`, {
      method: "POST",
    }).then((r) => json<ProjectDto>(r)),

  deletePart: (id: string) =>
    fetch(`${BACKEND_URL}/api/project/parts/${id}`, { method: "DELETE" }).then(
      (r) => json<ProjectDto>(r),
    ),

  bitmapImageUrl: (fileId: string) =>
    `${BACKEND_URL}/api/project/files/${fileId}/bitmap-image`,

  retraceBitmap: (fileId: string, settings: BitmapTraceSettings) =>
    fetch(`${BACKEND_URL}/api/project/files/${fileId}/retrace`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        thresholdPercent: settings.thresholdPercent,
        invert: settings.invert,
        brightness: settings.brightness,
        contrast: settings.contrast,
        mode: settings.mode,
        simplifyToleranceMm: settings.simplifyToleranceMm,
      }),
    }).then((r) => json<ProjectDto>(r)),

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

  /** Create a synthetic file from raw geometry paths (shapes drawn/created in-app). */
  createSyntheticFile: (
    displayName: string,
    paths: GeometryPath[],
    initialX: number,
    initialY: number,
  ) =>
    fetch(`${BACKEND_URL}/api/project/files/synthetic`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        displayName,
        paths: paths.map((p) => ({ closed: p.closed, layer: p.layer, points: p.points })),
        initialX,
        initialY,
      }),
    }).then((r) => json<ProjectDto>(r)),

  loadProject: (file: File) => {
    const form = new FormData();
    form.append("file", file, file.name);
    return fetch(`${BACKEND_URL}/api/project/load`, {
      method: "POST",
      body: form,
    }).then((r) => json<ProjectDto>(r));
  },
};

// --- Templates & Library types ----------------------------------------------

export interface TemplateInfo {
  id: string;
  name: string;
  createdAt: string;
  partCount: number;
  fileCount: number;
  description: string;
}

export interface ElementInfo {
  id: string;
  name: string;
  createdAt: string;
  pathCount: number;
  widthMm: number;
  heightMm: number;
}

export const templateApi = {
  list: () =>
    fetch(`${BACKEND_URL}/api/templates`).then((r) => json<TemplateInfo[]>(r)),

  save: (name: string, description = "") =>
    fetch(`${BACKEND_URL}/api/templates`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name, description }),
    }).then((r) => json<TemplateInfo>(r)),

  load: (id: string) =>
    fetch(`${BACKEND_URL}/api/templates/${id}/load`, { method: "POST" }).then(
      (r) => json<ProjectDto>(r),
    ),

  delete: (id: string) =>
    fetch(`${BACKEND_URL}/api/templates/${id}`, { method: "DELETE" }).then(
      (r) => { if (!r.ok) throw new Error(`${r.status} ${r.statusText}`); },
    ),
};

export const libraryApi = {
  list: () =>
    fetch(`${BACKEND_URL}/api/library`).then((r) => json<ElementInfo[]>(r)),

  save: (name: string, fileId: string) =>
    fetch(`${BACKEND_URL}/api/library`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name, fileId }),
    }).then((r) => json<ElementInfo>(r)),

  insert: (id: string) =>
    fetch(`${BACKEND_URL}/api/library/${id}/insert`, { method: "POST" }).then(
      (r) => json<ProjectDto>(r),
    ),

  delete: (id: string) =>
    fetch(`${BACKEND_URL}/api/library/${id}`, { method: "DELETE" }).then(
      (r) => { if (!r.ok) throw new Error(`${r.status} ${r.statusText}`); },
    ),
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
