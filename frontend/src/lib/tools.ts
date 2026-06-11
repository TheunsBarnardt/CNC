/** Active tool state — shared between CreationSidebar and Viewport. */

export type ShapeToolType =
  | "line"
  | "rect"
  | "circle"
  | "ellipse"
  | "polygon"
  | "star";

export interface ShapeOptions {
  cornerRadiusMm: number; // rect only
  sides: number;          // polygon only (3–12)
  starPoints: number;     // star only (3–12)
  innerRatio: number;     // star only (0.1–0.9)
}

export const DEFAULT_SHAPE_OPTIONS: ShapeOptions = {
  cornerRadiusMm: 0,
  sides: 6,
  starPoints: 5,
  innerRatio: 0.4,
};

export type ActiveTool =
  | { type: "select" }
  | { type: ShapeToolType; options: ShapeOptions }
  | { type: "pen" }
  | { type: "text" };
