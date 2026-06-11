import { useCallback, useEffect, useRef, useState } from "react";
import {
  Grid3x3,
  Magnet,
  Maximize,
  Moon,
  Sun,
  ZoomIn,
  ZoomOut,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { EditToolbar, type AlignEdge } from "@/components/EditToolbar";
import { BooleanToolbar } from "@/components/BooleanToolbar";
import { TextPanel } from "@/components/TextPanel";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Separator } from "@/components/ui/separator";
import {
  bboxOfPaths,
  hitTestPart,
  localPivot,
  partWorldBBox,
  type Vec,
} from "@/lib/geometry";
import { partToWorld } from "@/lib/geometry";
import type { GeometryPath, Layer, Part, ProjectDto, TableOrigin } from "@/lib/project";
import { stateAt, type Simulation } from "@/lib/simulation";
import type { ActiveTool } from "@/lib/tools";
import type { Guide } from "@/lib/guides";
import type { AppSettings } from "@/lib/settings";
import {
  circleFromCenter,
  ellipseFromPoints,
  lineFromPoints,
  polygonFromCenter,
  rectFromPoints,
  starFromCenter,
} from "@/lib/shapeGen";

interface ViewportProps {
  project: ProjectDto;
  geometry: Map<string, GeometryPath[]>;
  selectedPartId: string | null;
  secondaryPartId?: string | null;
  onSelect: (id: string | null) => void;
  onSecondarySelect?: (id: string | null) => void;
  onPartChange: (part: Part) => void;
  onPartCommit: (part: Part) => void;
  onDuplicate: (id: string) => void;
  onDelete: (id: string) => void;
  onReorder?: (id: string, direction: "up" | "down" | "front" | "back") => void;
  onOffset?: (id: string, offsetMm: number) => void;
  onNodesChanged?: (fileId: string, pathId: string, points: [number, number][]) => void;
  onSimplify?: (fileId: string) => void;
  onBoolean?: (op: "unite" | "subtract" | "intersect") => void;
  onArray?: (id: string, type: "grid" | "circular", params: object) => void;
  onTestArray?: (id: string, params: object) => void;
  guides?: Guide[];
  onGuidesChange?: (guides: Guide[]) => void;
  layers?: Layer[];
  settings?: AppSettings;
  simulation?: Simulation | null;
  simTime?: number;
  readOnly?: boolean;
  activeTool?: ActiveTool;
  onShapeCreated?: (paths: GeometryPath[], name: string, worldX: number, worldY: number) => void;
  onToolReset?: () => void;
}

interface View {
  scale: number;
  tx: number;
  ty: number;
}

type DragMode = "pan" | "move" | "rotate" | "node";

interface DragState {
  mode: DragMode;
  pointerId: number;
  startScreen: Vec;
  startView: View;
  startPart?: Part;
  lastPart?: Part;
  moved: boolean;
  // Node-drag fields:
  nodePathIdx?: number;
  nodeIdx?: number;
  nodeStartWorld?: Vec;
  nodeDragWorld?: Vec; // live position during drag (for canvas preview)
}

/** Node editing state: tracks which path/node is selected/hovered. */
interface NodeEditState {
  partId: string;
  fileId: string;
  /** The path + node index of the currently selected node (or null). */
  selectedNode: { pathIdx: number; nodeIdx: number } | null;
  /** Hovered node (for highlight). */
  hoveredNode: { pathIdx: number; nodeIdx: number } | null;
  /** Hovered segment midpoint (for "add node" affordance). */
  hoveredSeg: { pathIdx: number; segIdx: number } | null;
}

// Shape drawing state: rubber-band from start to current world point.
interface ShapeDrawState {
  phase: "shape";
  start: Vec;
  current: Vec;
  pointerId: number;
}

// Pen tool state.
interface PenNode {
  anchor: Vec;
  handleOut: Vec | null; // outgoing control point (used for next segment)
  handleIn: Vec | null;  // incoming control point
}

interface PenState {
  phase: "pen";
  nodes: PenNode[];
  dragging: boolean;     // true while dragging handle from last node
  hoverPos: Vec | null;
}

// Text placement state.
interface TextState {
  phase: "text";
  worldPos: Vec;
}

type DrawState = ShapeDrawState | PenState | TextState | null;

const SNAP_STEPS = [1, 5, 10, 25, 50];
const ROTATE_HANDLE_PX = 28;

export function Viewport({
  project,
  geometry,
  selectedPartId,
  secondaryPartId = null,
  onSelect,
  onSecondarySelect,
  onPartChange,
  onPartCommit,
  onDuplicate,
  onDelete,
  onReorder,
  onOffset,
  onNodesChanged,
  onSimplify,
  onBoolean,
  onArray,
  onTestArray,
  guides = [],
  onGuidesChange,
  layers,
  settings,
  simulation = null,
  simTime = 0,
  readOnly = false,
  activeTool = { type: "select" },
  onShapeCreated,
  onToolReset,
}: ViewportProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ w: 0, h: 0 });
  const [view, setView] = useState<View>({ scale: 0.5, tx: 0, ty: 0 });
  const [snap, setSnap] = useState(true);
  const [snapStep, setSnapStep] = useState(10);
  const [showGrid, setShowGrid] = useState(true);
  const [darkCanvas, setDarkCanvas] = useState(false);
  const [cursorMm, setCursorMm] = useState<Vec | null>(null);
  const [drawState, setDrawState] = useState<DrawState>(null);
  const [nodeEdit, setNodeEdit] = useState<NodeEditState | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const fittedRef = useRef(false);
  // Guide being created (id=null) or dragged (id=guideId).
  const [guideDrag, setGuideDrag] = useState<{ id: string | null; axis: "h" | "v"; posMm: number } | null>(null);

  const RULER_PX = 20; // thickness of ruler strips in CSS px

  const { table } = project;
  const selectedPart = project.parts.find((p) => p.id === selectedPartId) ?? null;

  const isDrawing = activeTool.type !== "select";

  // Reset draw state when tool changes.
  useEffect(() => {
    setDrawState(null);
    setNodeEdit(null);
  }, [activeTool.type]);

  // Exit node-edit when selection moves to a different part.
  useEffect(() => {
    setNodeEdit((ne) => (ne && ne.partId !== selectedPartId ? null : ne));
  }, [selectedPartId]);

  // --- coordinate mapping ---------------------------------------------------
  const toScreen = useCallback(
    (w: Vec): Vec => [w[0] * view.scale + view.tx, size.h - (w[1] * view.scale + view.ty)],
    [view, size.h],
  );
  const toWorld = useCallback(
    (s: Vec): Vec => [
      (s[0] - view.tx) / view.scale,
      (size.h - s[1] - view.ty) / view.scale,
    ],
    [view, size.h],
  );

  const fitToView = useCallback(() => {
    if (size.w === 0 || size.h === 0) return;
    const scale = Math.min(size.w / table.widthMm, size.h / table.heightMm) * 0.9;
    setView({
      scale,
      tx: (size.w - table.widthMm * scale) / 2,
      ty: (size.h - table.heightMm * scale) / 2,
    });
  }, [size, table.widthMm, table.heightMm]);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const observer = new ResizeObserver(() => {
      setSize({ w: el.clientWidth, h: el.clientHeight });
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (size.w > 0 && !fittedRef.current) {
      fittedRef.current = true;
      fitToView();
    }
  }, [size, fitToView]);
  useEffect(() => {
    if (fittedRef.current) fitToView();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [table.widthMm, table.heightMm]);

  const zoomAt = useCallback(
    (screen: Vec, factor: number) => {
      setView((v) => {
        const scale = Math.min(50, Math.max(0.02, v.scale * factor));
        const wx = (screen[0] - v.tx) / v.scale;
        const wy = (size.h - screen[1] - v.ty) / v.scale;
        return { scale, tx: screen[0] - wx * scale, ty: size.h - screen[1] - wy * scale };
      });
    },
    [size.h],
  );

  const screenPos = (e: { clientX: number; clientY: number }): Vec => {
    const rect = canvasRef.current!.getBoundingClientRect();
    return [e.clientX - rect.left, e.clientY - rect.top];
  };

  const snapWorld = (w: Vec): Vec =>
    snap ? [Math.round(w[0] / snapStep) * snapStep, Math.round(w[1] / snapStep) * snapStep] : w;

  // --- pen tool helpers -----------------------------------------------------

  const finalizePen = (nodes: PenNode[], closed: boolean) => {
    if (nodes.length < 2) return;
    const flatPts: [number, number][] = nodes.map((n) => [n.anchor[0], n.anchor[1]]);
    if (closed && flatPts.length > 0) {
      // Duplicate first point for closed visual — backend handles closed flag.
    }
    const path: GeometryPath = { layer: null, closed, points: flatPts };
    const minX = Math.min(...flatPts.map((p) => p[0]));
    const minY = Math.min(...flatPts.map((p) => p[1]));
    const name = "Pen path";
    onShapeCreated?.([path], name, minX, minY);
    onToolReset?.();
  };

  // --- pointer handlers -----------------------------------------------------

  const handlePointerDown = (e: React.PointerEvent) => {
    // Right-click on guide: toggle lock.
    if (e.button === 2 && !readOnly && (settings?.showGuides ?? true)) {
      const s = screenPos(e);
      for (const guide of guides) {
        const dist = guide.axis === "v"
          ? Math.abs(s[0] - (toScreen([guide.posMm, 0])[0]))
          : Math.abs(s[1] - (toScreen([0, guide.posMm])[1]));
        if (dist < 7) {
          onGuidesChange?.(guides.map((g) => g.id === guide.id ? { ...g, locked: !g.locked } : g));
          return;
        }
      }
    }
    if (e.button !== 0 && e.button !== 1) return;
    try { canvasRef.current?.setPointerCapture(e.pointerId); } catch { /* best-effort */ }
    canvasRef.current?.focus();
    const screen = screenPos(e);
    const world = toWorld(screen);
    const snapped = snapWorld(world);

    // ── Guide creation / dragging ──────────────────────────────────────────
    if (!readOnly && e.button === 0) {
      const R = RULER_PX;
      const inTopRuler = screen[1] < R && screen[0] >= R;
      const inLeftRuler = screen[0] < R && screen[1] >= R;
      if ((settings?.showRulers ?? true) && (inTopRuler || inLeftRuler)) {
        setGuideDrag({ id: null, axis: inTopRuler ? "v" : "h", posMm: inTopRuler ? world[0] : world[1] });
        return;
      }
      if (settings?.showGuides ?? true) {
        for (const guide of guides) {
          if (guide.locked) continue;
          let dist: number;
          if (guide.axis === "v") {
            const [sx] = toScreen([guide.posMm, 0]);
            dist = Math.abs(screen[0] - sx);
          } else {
            const [, sy] = toScreen([0, guide.posMm]);
            dist = Math.abs(screen[1] - sy);
          }
          if (dist < 7) {
            setGuideDrag({ id: guide.id, axis: guide.axis, posMm: guide.posMm });
            return;
          }
        }
      }
    }
    // ──────────────────────────────────────────────────────────────────────

    // === NODE EDITING MODE ===
    if (!readOnly && nodeEdit && e.button === 0) {
      const part = project.parts.find((p) => p.id === nodeEdit.partId);
      const paths = geometry.get(nodeEdit.fileId);
      if (part && paths) {
        const pivot = localPivot(paths);
        const NODE_RADIUS = 8 / view.scale;
        const SEG_RADIUS = 6 / view.scale;

        // Check if clicking a node.
        for (let pi = 0; pi < paths.length; pi++) {
          const path = paths[pi];
          for (let ni = 0; ni < path.points.length; ni++) {
            const wp = nodeWorldPos(part, paths, pi, ni);
            if (Math.hypot(world[0] - wp[0], world[1] - wp[1]) < NODE_RADIUS) {
              setNodeEdit((ne) => ne ? { ...ne, selectedNode: { pathIdx: pi, nodeIdx: ni } } : ne);
              dragRef.current = {
                mode: "node", pointerId: e.pointerId,
                startScreen: screen, startView: view,
                nodePathIdx: pi, nodeIdx: ni,
                nodeStartWorld: wp, nodeDragWorld: wp,
                moved: false,
              };
              return;
            }
          }
        }

        // Check if clicking a segment midpoint (add node).
        for (let pi = 0; pi < paths.length; pi++) {
          const path = paths[pi];
          const ptCount = path.points.length;
          const segs = path.closed ? ptCount : ptCount - 1;
          for (let si = 0; si < segs; si++) {
            const a = path.points[si] as Vec;
            const b = path.points[(si + 1) % ptCount] as Vec;
            const wa = partToWorld(part, pivot, a);
            const wb = partToWorld(part, pivot, b);
            const mx = (wa[0] + wb[0]) / 2, my = (wa[1] + wb[1]) / 2;
            if (Math.hypot(world[0] - mx, world[1] - my) < SEG_RADIUS) {
              addNodeOnSegment(pi, si);
              return;
            }
          }
        }
      }
      // Clicked outside — exit node-edit if outside the part's bbox.
      setNodeEdit(null);
      return;
    }

    // === DRAWING TOOLS ===
    if (!readOnly && activeTool.type !== "select") {
      // Text: click to place the text panel.
      if (activeTool.type === "text") {
        setDrawState({ phase: "text", worldPos: snapped });
        return;
      }

      // Pen tool: click to add nodes.
      if (activeTool.type === "pen") {
        setDrawState((ds) => {
          const state = ds?.phase === "pen" ? ds : null;
          const nodes = state?.nodes ?? [];

          // Click on first node to close path.
          if (nodes.length >= 2) {
            const first = nodes[0].anchor;
            const [fx, fy] = toScreen(first);
            const [sx, sy] = toScreen(snapped);
            if (Math.hypot(fx - sx, fy - sy) < 12) {
              // Close path.
              setTimeout(() => finalizePen(nodes, true), 0);
              return null;
            }
          }

          const newNode: PenNode = { anchor: snapped, handleOut: null, handleIn: null };
          return {
            phase: "pen",
            nodes: [...nodes, newNode],
            dragging: true,
            hoverPos: snapped,
          };
        });
        return;
      }

      // Shape rubber-band tools: start drag.
      setDrawState({ phase: "shape", start: snapped, current: snapped, pointerId: e.pointerId });
      return;
    }

    // === SELECT TOOL ===
    if (e.button === 0 && selectedPart && !readOnly) {
      const handle = rotateHandleWorld(selectedPart);
      if (handle) {
        const hs = toScreen(handle);
        const dx = screen[0] - hs[0], dy = screen[1] - hs[1];
        if (dx * dx + dy * dy <= 100) {
          dragRef.current = {
            mode: "rotate", pointerId: e.pointerId,
            startScreen: screen, startView: view,
            startPart: { ...selectedPart }, moved: false,
          };
          return;
        }
      }
    }

    if (e.button === 0 && !readOnly) {
      const tolerance = 4 / view.scale;
      for (let i = project.parts.length - 1; i >= 0; i--) {
        const part = project.parts[i];
        const file = project.files.find((f) => f.id === part.fileId);
        const paths = geometry.get(part.fileId);
        if (!file?.visible || !paths) continue;
        const partLayer = layers?.find((l) => l.id === part.layerId);
        if (partLayer && (!partLayer.visible || partLayer.locked)) continue;
        if (hitTestPart(part, paths, world, tolerance)) {
          if (e.shiftKey && selectedPartId && part.id !== selectedPartId) {
            // Shift-click: set secondary selection for boolean ops.
            onSecondarySelect?.(part.id);
            return;
          }
          // Double-click on already-selected part: enter node-edit mode.
          if (e.detail === 2 && part.id === selectedPartId) {
            setNodeEdit({
              partId: part.id,
              fileId: part.fileId,
              selectedNode: null,
              hoveredNode: null,
              hoveredSeg: null,
            });
            return;
          }
          onSelect(part.id);
          onSecondarySelect?.(null);
          dragRef.current = {
            mode: "move", pointerId: e.pointerId,
            startScreen: screen, startView: view,
            startPart: { ...part }, moved: false,
          };
          return;
        }
      }
      onSelect(null);
      onSecondarySelect?.(null);
    }

    dragRef.current = {
      mode: "pan", pointerId: e.pointerId,
      startScreen: screen, startView: view, moved: false,
    };
  };

  const handlePointerMove = (e: React.PointerEvent) => {
    const screen = screenPos(e);
    const world = toWorld(screen);
    const snapped = snapWorld(world);
    setCursorMm(world);

    // Guide drag update.
    if (guideDrag) {
      setGuideDrag((gd) => gd ? { ...gd, posMm: gd.axis === "v" ? world[0] : world[1] } : null);
      return;
    }

    // Update drawing preview.
    if (!readOnly && activeTool.type !== "select") {
      if (activeTool.type === "pen") {
        setDrawState((ds) => {
          if (ds?.phase !== "pen") return ds;
          let nodes = ds.nodes;
          if (ds.dragging && nodes.length > 0) {
            const last = nodes[nodes.length - 1];
            const drag: Vec = [snapped[0] - last.anchor[0], snapped[1] - last.anchor[1]];
            const updated: PenNode = {
              ...last,
              handleOut: [last.anchor[0] + drag[0], last.anchor[1] + drag[1]],
              handleIn: [last.anchor[0] - drag[0], last.anchor[1] - drag[1]],
            };
            nodes = [...nodes.slice(0, -1), updated];
          }
          return { ...ds, nodes, hoverPos: snapped };
        });
        return;
      }

      if (drawState?.phase === "shape" && drawState.pointerId === e.pointerId) {
        setDrawState({ ...drawState, current: snapped });
        return;
      }
      return;
    }

    // Node-edit hover detection (when not dragging a node).
    if (nodeEdit && !dragRef.current) {
      const part = project.parts.find((p) => p.id === nodeEdit.partId);
      const paths = geometry.get(nodeEdit.fileId);
      if (part && paths) {
        const NODE_RADIUS = 8 / view.scale;
        const SEG_RADIUS = 6 / view.scale;
        const pivot = localPivot(paths);
        let hNode: NodeEditState["hoveredNode"] = null;
        let hSeg: NodeEditState["hoveredSeg"] = null;
        outer: for (let pi = 0; pi < paths.length && !hNode; pi++) {
          for (let ni = 0; ni < paths[pi].points.length; ni++) {
            const wp = partToWorld(part, pivot, paths[pi].points[ni] as Vec);
            if (Math.hypot(world[0] - wp[0], world[1] - wp[1]) < NODE_RADIUS) {
              hNode = { pathIdx: pi, nodeIdx: ni };
              break outer;
            }
          }
        }
        if (!hNode) {
          outerSeg: for (let pi = 0; pi < paths.length; pi++) {
            const path = paths[pi];
            const ptCount = path.points.length;
            const segs = path.closed ? ptCount : ptCount - 1;
            for (let si = 0; si < segs; si++) {
              const wa = partToWorld(part, pivot, path.points[si] as Vec);
              const wb = partToWorld(part, pivot, path.points[(si + 1) % ptCount] as Vec);
              const mx = (wa[0] + wb[0]) / 2, my = (wa[1] + wb[1]) / 2;
              if (Math.hypot(world[0] - mx, world[1] - my) < SEG_RADIUS) {
                hSeg = { pathIdx: pi, segIdx: si };
                break outerSeg;
              }
            }
          }
        }
        setNodeEdit((ne) => ne ? { ...ne, hoveredNode: hNode, hoveredSeg: hSeg } : ne);
      }
    }

    const drag = dragRef.current;
    if (!drag || drag.pointerId !== e.pointerId) return;
    const dxs = screen[0] - drag.startScreen[0];
    const dys = screen[1] - drag.startScreen[1];
    if (Math.abs(dxs) + Math.abs(dys) > 2) drag.moved = true;

    if (drag.mode === "node") {
      drag.nodeDragWorld = world;
      return;
    }

    if (drag.mode === "pan") {
      setView({ ...drag.startView, tx: drag.startView.tx + dxs, ty: drag.startView.ty - dys });
      return;
    }
    if (!drag.startPart) return;
    const current = drag.lastPart ?? drag.startPart;
    if (drag.mode === "move") {
      let x = drag.startPart.x + dxs / view.scale;
      let y = drag.startPart.y - dys / view.scale;
      if (snap) { x = Math.round(x / snapStep) * snapStep; y = Math.round(y / snapStep) * snapStep; }
      // Snap to guide lines.
      if ((settings?.snapToGuides ?? true) && guides.length > 0) {
        const tempPart = { ...current, x, y };
        const pths = geometry.get(tempPart.fileId);
        if (pths) {
          const b = partWorldBBox(tempPart, pths);
          for (const guide of guides) {
            const thr = snapStep;
            if (guide.axis === "v") {
              if (Math.abs(b.minX - guide.posMm) < thr) x += guide.posMm - b.minX;
              else if (Math.abs(b.maxX - guide.posMm) < thr) x += guide.posMm - b.maxX;
            } else {
              if (Math.abs(b.minY - guide.posMm) < thr) y += guide.posMm - b.minY;
              else if (Math.abs(b.maxY - guide.posMm) < thr) y += guide.posMm - b.maxY;
            }
          }
        }
      }
      drag.lastPart = { ...current, x, y };
      onPartChange(drag.lastPart);
    } else if (drag.mode === "rotate") {
      const paths = geometry.get(drag.startPart.fileId);
      if (!paths) return;
      const pivot = localPivot(paths);
      const cx = drag.startPart.x + pivot[0], cy = drag.startPart.y + pivot[1];
      let deg = (Math.atan2(world[1] - cy, world[0] - cx) * 180) / Math.PI - 90;
      deg = e.shiftKey ? Math.round(deg / 15) * 15 : Math.round(deg);
      deg = ((deg % 360) + 360) % 360;
      drag.lastPart = { ...current, rotationDeg: deg };
      onPartChange(drag.lastPart);
    }
  };

  const handlePointerUp = (e: React.PointerEvent) => {
    const screen = screenPos(e);
    const world = toWorld(screen);
    const snapped = snapWorld(world);

    // Guide commit / delete.
    if (guideDrag) {
      const outside =
        screen[0] < RULER_PX || screen[1] < RULER_PX ||
        screen[0] > size.w || screen[1] > size.h;
      if (outside && guideDrag.id) {
        onGuidesChange?.(guides.filter((g) => g.id !== guideDrag.id));
      } else if (!outside) {
        if (guideDrag.id === null) {
          onGuidesChange?.([
            ...guides,
            { id: crypto.randomUUID(), axis: guideDrag.axis, posMm: guideDrag.posMm, locked: false },
          ]);
        } else {
          onGuidesChange?.(
            guides.map((g) => (g.id === guideDrag.id ? { ...g, posMm: guideDrag.posMm } : g)),
          );
        }
      }
      setGuideDrag(null);
      return;
    }

    // Finish pen drag (release handle).
    if (!readOnly && activeTool.type === "pen") {
      setDrawState((ds) => ds?.phase === "pen" ? { ...ds, dragging: false } : ds);
      return;
    }

    // Finalize shape rubber-band.
    if (!readOnly && drawState?.phase === "shape" && drawState.pointerId === e.pointerId) {
      const { start, current } = { ...drawState, current: snapped };
      const minDist = 3 / view.scale; // Ignore tiny drags (misclick).
      if (Math.hypot(current[0] - start[0], current[1] - start[1]) > minDist) {
        commitShape(start, current);
      }
      setDrawState(null);
      return;
    }

    const drag = dragRef.current;
    if (!drag || drag.pointerId !== e.pointerId) return;
    if (drag.mode === "node") {
      if (drag.moved) commitNodeDrag();
      dragRef.current = null;
      return;
    }
    dragRef.current = null;
    if (drag.mode !== "pan" && drag.lastPart && drag.moved) onPartCommit(drag.lastPart);
  };

  // Build geometry from the two drag-points and notify the parent.
  const commitShape = (start: Vec, end: Vec) => {
    const result = getShapeResult(start, end);
    if (!result || result.paths.length === 0) return;
    const nameMap: Record<string, string> = {
      line: "Line", rect: "Rectangle", circle: "Circle",
      ellipse: "Ellipse", polygon: "Polygon", star: "Star",
    };
    const name = nameMap[activeTool.type] ?? "Shape";
    onShapeCreated?.(result.paths, name, result.x, result.y);
    onToolReset?.();
  };

  const handleWheel = (e: React.WheelEvent) => {
    if (settings?.mouseWheelPans) {
      // Pan: shift = horizontal, plain = vertical.
      setView((v) => ({
        ...v,
        tx: e.shiftKey ? v.tx - e.deltaY : v.tx,
        ty: e.shiftKey ? v.ty : v.ty + e.deltaY,
      }));
    } else {
      zoomAt(screenPos(e), e.deltaY < 0 ? 1.15 : 1 / 1.15);
    }
  };

  const nudge = (dx: number, dy: number) => {
    if (!selectedPart) return;
    const part = { ...selectedPart, x: selectedPart.x + dx, y: selectedPart.y + dy };
    onPartChange(part);
    onPartCommit(part);
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    // Escape: exit node-edit first; then cancel drawing tool.
    if (e.key === "Escape") {
      if (nodeEdit) {
        setNodeEdit(null);
        return;
      }
      if (activeTool.type !== "select") {
        if (drawState?.phase === "pen" && (drawState as PenState).nodes.length >= 2) {
          finalizePen((drawState as PenState).nodes, false);
        }
        setDrawState(null);
        onToolReset?.();
        return;
      }
    }

    // Delete selected node in node-edit mode.
    if ((e.key === "Delete" || e.key === "Backspace") && nodeEdit?.selectedNode) {
      e.preventDefault();
      deleteSelectedNode();
      return;
    }

    // Enter finishes open pen path.
    if (e.key === "Enter" && activeTool.type === "pen") {
      if (drawState?.phase === "pen") {
        finalizePen((drawState as PenState).nodes, false);
        setDrawState(null);
        onToolReset?.();
      }
      return;
    }

    if (!selectedPart || readOnly) return;
    const step = e.shiftKey ? 10 : 1;
    switch (e.key) {
      case "Delete": case "Backspace": onDelete(selectedPart.id); break;
      case "d":
        if (e.ctrlKey || e.metaKey) { e.preventDefault(); onDuplicate(selectedPart.id); }
        break;
      case "ArrowLeft": e.preventDefault(); nudge(-step, 0); break;
      case "ArrowRight": e.preventDefault(); nudge(step, 0); break;
      case "ArrowUp": e.preventDefault(); nudge(0, step); break;
      case "ArrowDown": e.preventDefault(); nudge(0, -step); break;
    }
  };

  const rotateHandleWorld = useCallback(
    (part: Part): Vec | null => {
      const paths = geometry.get(part.fileId);
      if (!paths) return null;
      const pivot = localPivot(paths);
      const b = bboxOfPaths(paths);
      const lift = (b.maxY - b.minY) / 2 + ROTATE_HANDLE_PX / view.scale;
      return partToWorld(part, pivot, [pivot[0], pivot[1] + lift]);
    },
    [geometry, view.scale],
  );

  const rotateBy = (deg: number) => {
    if (!selectedPart) return;
    const rotationDeg = (((selectedPart.rotationDeg + deg) % 360) + 360) % 360;
    const part = { ...selectedPart, rotationDeg };
    onPartChange(part);
    onPartCommit(part);
  };

  // --- node editing helpers -------------------------------------------------

  /** Get the world position of a node, taking current drag into account. */
  const nodeWorldPos = useCallback(
    (part: Part, paths: GeometryPath[], pathIdx: number, nodeIdx: number): Vec => {
      const drag = dragRef.current;
      const pivot = localPivot(paths);
      const local = paths[pathIdx].points[nodeIdx] as Vec;
      const world = partToWorld(part, pivot, local);
      if (
        drag?.mode === "node" &&
        drag.nodePathIdx === pathIdx &&
        drag.nodeIdx === nodeIdx &&
        drag.nodeDragWorld
      )
        return drag.nodeDragWorld;
      return world;
    },
    [],
  );

  const commitNodeDrag = useCallback(() => {
    const drag = dragRef.current;
    if (drag?.mode !== "node" || !drag.nodeDragWorld || !nodeEdit) return;
    const { partId, fileId } = nodeEdit;
    const part = project.parts.find((p) => p.id === partId);
    const paths = geometry.get(fileId);
    if (!part || !paths || drag.nodePathIdx === undefined) return;

    const pivot = localPivot(paths);
    const path = paths[drag.nodePathIdx];
    if (!path) return;

    // Rebuild the point list: replace the dragged node with the new local position.
    const localDrag = worldToPartLocal(part, pivot, drag.nodeDragWorld);
    const newPoints: [number, number][] = path.points.map((pt, i) =>
      i === drag.nodeIdx ? [localDrag[0], localDrag[1]] : [pt[0], pt[1]],
    );
    onNodesChanged?.(fileId, path.id, newPoints);
  }, [nodeEdit, project.parts, geometry, onNodesChanged]);

  const deleteSelectedNode = useCallback(() => {
    if (!nodeEdit?.selectedNode) return;
    const { fileId, selectedNode: { pathIdx, nodeIdx } } = nodeEdit;
    const paths = geometry.get(fileId);
    if (!paths) return;
    const path = paths[pathIdx];
    const minNodes = path.closed ? 3 : 2;
    if (path.points.length <= minNodes) return; // can't reduce further
    const newPoints: [number, number][] = path.points.filter((_, i) => i !== nodeIdx);
    onNodesChanged?.(fileId, path.id, newPoints);
    setNodeEdit((ne) => ne ? { ...ne, selectedNode: null } : ne);
  }, [nodeEdit, geometry, onNodesChanged]);

  const addNodeOnSegment = useCallback(
    (pathIdx: number, segIdx: number) => {
      if (!nodeEdit) return;
      const { fileId } = nodeEdit;
      const paths = geometry.get(fileId);
      if (!paths) return;
      const path = paths[pathIdx];
      const ptCount = path.points.length;
      const aIdx = segIdx;
      const bIdx = (segIdx + 1) % ptCount;
      const a = path.points[aIdx], b = path.points[bIdx];
      const mid: [number, number] = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
      const newPoints: [number, number][] = [...path.points.map((p): [number, number] => [p[0], p[1]])];
      newPoints.splice(bIdx, 0, mid);
      onNodesChanged?.(fileId, path.id, newPoints);
    },
    [nodeEdit, geometry, onNodesChanged],
  );

  const align = (edge: "left" | "centerX" | "right" | "bottom" | "centerY" | "top") => {
    if (!selectedPart) return;
    const paths = geometry.get(selectedPart.fileId);
    if (!paths) return;
    const b = partWorldBBox(selectedPart, paths);
    const w = b.maxX - b.minX, h = b.maxY - b.minY;
    let dx = 0, dy = 0;
    if (edge === "left") dx = -b.minX;
    if (edge === "right") dx = table.widthMm - b.maxX;
    if (edge === "centerX") dx = (table.widthMm - w) / 2 - b.minX;
    if (edge === "bottom") dy = -b.minY;
    if (edge === "top") dy = table.heightMm - b.maxY;
    if (edge === "centerY") dy = (table.heightMm - h) / 2 - b.minY;
    const part = { ...selectedPart, x: selectedPart.x + dx, y: selectedPart.y + dy };
    onPartChange(part);
    onPartCommit(part);
  };

  // --- shape geometry helper (used for both preview and commit) ------------

  function getShapeResult(
    start: Vec, current: Vec,
  ): { paths: GeometryPath[]; x: number; y: number } | null {
    if (activeTool.type === "select" || activeTool.type === "pen" || activeTool.type === "text")
      return null;
    const opts = activeTool.options;
    switch (activeTool.type) {
      case "line":    return lineFromPoints(start[0], start[1], current[0], current[1]);
      case "rect":    return rectFromPoints(start[0], start[1], current[0], current[1], opts.cornerRadiusMm);
      case "circle":  return circleFromCenter(start[0], start[1], current[0], current[1]);
      case "ellipse": return ellipseFromPoints(start[0], start[1], current[0], current[1]);
      case "polygon": return polygonFromCenter(start[0], start[1], current[0], current[1], opts.sides);
      case "star":    return starFromCenter(start[0], start[1], current[0], current[1], opts.starPoints, opts.innerRatio);
    }
    return null;
  }

  // --- drawing (canvas) -----------------------------------------------------

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || size.w === 0) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = size.w * dpr;
    canvas.height = size.h * dpr;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.scale(dpr, dpr);

    const css = getComputedStyle(canvas);
    const cssVar = (name: string, fallback: string) =>
      css.getPropertyValue(name).trim() || fallback;
    const colBg = darkCanvas ? "#16161a" : cssVar("--background", "#fff");
    const colCard = darkCanvas ? "#222228" : cssVar("--card", "#fafafa");
    const colBorder = darkCanvas ? "#3a3a42" : cssVar("--border", "#ddd");
    const colMuted = darkCanvas ? "#9a9aa5" : cssVar("--muted-foreground", "#888");
    const colFg = darkCanvas ? "#e6e6ea" : cssVar("--foreground", "#222");
    const colPrimary = cssVar("--primary", "#16a34a");
    const colDestructive = cssVar("--destructive", "#dc2626");

    ctx.fillStyle = colBg;
    ctx.fillRect(0, 0, size.w, size.h);

    const [tx0, ty0] = toScreen([0, table.heightMm]);
    const tableWpx = table.widthMm * view.scale;
    const tableHpx = table.heightMm * view.scale;
    ctx.fillStyle = colCard;
    ctx.fillRect(tx0, ty0, tableWpx, tableHpx);

    const minor =
      [1, 2, 5, 10, 25, 50, 100, 250].find((s) => s * view.scale >= 10) ?? 500;
    ctx.save();
    ctx.beginPath();
    ctx.rect(tx0, ty0, tableWpx, tableHpx);
    ctx.clip();
    if (showGrid) {
      for (let gx = 0; gx <= table.widthMm; gx += minor) {
        const [sx] = toScreen([gx, 0]);
        ctx.strokeStyle = colBorder;
        ctx.globalAlpha = gx % (minor * 5) === 0 ? 0.9 : 0.4;
        ctx.lineWidth = 1;
        ctx.beginPath(); ctx.moveTo(sx, ty0); ctx.lineTo(sx, ty0 + tableHpx); ctx.stroke();
      }
      for (let gy = 0; gy <= table.heightMm; gy += minor) {
        const [, sy] = toScreen([0, gy]);
        ctx.strokeStyle = colBorder;
        ctx.globalAlpha = gy % (minor * 5) === 0 ? 0.9 : 0.4;
        ctx.lineWidth = 1;
        ctx.beginPath(); ctx.moveTo(tx0, sy); ctx.lineTo(tx0 + tableWpx, sy); ctx.stroke();
      }
    }
    ctx.restore();
    ctx.globalAlpha = 1;

    ctx.strokeStyle = colMuted;
    ctx.lineWidth = 1.5;
    ctx.strokeRect(tx0, ty0, tableWpx, tableHpx);

    const originWorld: Record<TableOrigin, Vec> = {
      BottomLeft: [0, 0], BottomRight: [table.widthMm, 0],
      TopLeft: [0, table.heightMm], TopRight: [table.widthMm, table.heightMm],
      Center: [table.widthMm / 2, table.heightMm / 2],
    };
    const [ox, oy] = toScreen(originWorld[table.origin]);
    ctx.strokeStyle = colPrimary;
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.arc(ox, oy, 6, 0, Math.PI * 2);
    ctx.moveTo(ox - 11, oy); ctx.lineTo(ox + 11, oy);
    ctx.moveTo(ox, oy - 11); ctx.lineTo(ox, oy + 11);
    ctx.stroke();

    // Parts.
    for (const part of project.parts) {
      const file = project.files.find((f) => f.id === part.fileId);
      const paths = geometry.get(part.fileId);
      if (!file?.visible || !paths) continue;
      const partLayer = layers?.find((l) => l.id === part.layerId);
      if (partLayer && !partLayer.visible) continue;
      const pivot = localPivot(paths);
      const selected = part.id === selectedPartId;
      const b = partWorldBBox(part, paths);
      const outOfBounds =
        b.minX < -1e-6 || b.minY < -1e-6 ||
        b.maxX > table.widthMm + 1e-6 || b.maxY > table.heightMm + 1e-6;
      const strokeCol = outOfBounds ? colDestructive : selected ? colPrimary : colFg;

      for (const path of paths) {
        if (path.points.length < 2) continue;
        ctx.beginPath();
        const [mx, my] = toScreen(partToWorld(part, pivot, path.points[0] as Vec));
        ctx.moveTo(mx, my);
        for (let i = 1; i < path.points.length; i++) {
          const [lx, ly] = toScreen(partToWorld(part, pivot, path.points[i] as Vec));
          ctx.lineTo(lx, ly);
        }
        if (path.closed) {
          ctx.closePath();
          ctx.fillStyle = strokeCol;
          ctx.globalAlpha = selected ? 0.14 : 0.07;
          ctx.fill();
          ctx.globalAlpha = 1;
        }
        ctx.strokeStyle = strokeCol;
        ctx.lineWidth = selected ? 2 : 1.25;
        ctx.stroke();
      }

      if (selected) {
        const [bx0, by0] = toScreen([b.minX, b.maxY]);
        const [bx1, by1] = toScreen([b.maxX, b.minY]);
        ctx.setLineDash([4, 4]);
        ctx.strokeStyle = colPrimary;
        ctx.lineWidth = 1;
        ctx.strokeRect(bx0, by0, bx1 - bx0, by1 - by0);
        ctx.setLineDash([]);
        const handle = rotateHandleWorld(part);
        if (handle) {
          const top = partToWorld(part, localPivot(paths), [
            localPivot(paths)[0],
            localPivot(paths)[1] + (bboxOfPaths(paths).maxY - bboxOfPaths(paths).minY) / 2,
          ]);
          const [hx, hy] = toScreen(handle);
          const [htx, hty] = toScreen(top);
          ctx.strokeStyle = colPrimary;
          ctx.beginPath(); ctx.moveTo(htx, hty); ctx.lineTo(hx, hy); ctx.stroke();
          ctx.beginPath(); ctx.arc(hx, hy, 5, 0, Math.PI * 2);
          ctx.fillStyle = colPrimary; ctx.fill();
        }
      }
    }

    // --- draw preview (shape rubber-band) -----------------------------------
    if (drawState?.phase === "shape") {
      const { start, current } = drawState;
      const result = getShapeResult(start, current);
      if (result) {
        ctx.strokeStyle = colPrimary;
        ctx.lineWidth = 1.5;
        ctx.setLineDash([5, 3]);
        ctx.globalAlpha = 0.8;
        for (const p of result.paths) {
          if (p.points.length < 2) continue;
          ctx.beginPath();
          // Translate local-space points by world placement origin.
          ctx.moveTo(...toScreen([p.points[0][0] + result.x, p.points[0][1] + result.y] as Vec));
          for (let i = 1; i < p.points.length; i++)
            ctx.lineTo(...toScreen([p.points[i][0] + result.x, p.points[i][1] + result.y] as Vec));
          if (p.closed) ctx.closePath();
          ctx.stroke();
        }
        ctx.setLineDash([]);
        ctx.globalAlpha = 1;
      }
    }

    // --- pen tool preview ---------------------------------------------------
    if (drawState?.phase === "pen") {
      const { nodes, hoverPos } = drawState as PenState;
      ctx.strokeStyle = colPrimary;
      ctx.lineWidth = 1.5;

      // Draw completed segments.
      for (let i = 1; i < nodes.length; i++) {
        const a = nodes[i - 1], b = nodes[i];
        ctx.beginPath();
        ctx.moveTo(...toScreen(a.anchor));
        if (a.handleOut || b.handleIn) {
          const cp1 = a.handleOut ?? a.anchor;
          const cp2 = b.handleIn ?? b.anchor;
          ctx.bezierCurveTo(
            ...toScreen(cp1), ...toScreen(cp2), ...toScreen(b.anchor),
          );
        } else {
          ctx.lineTo(...toScreen(b.anchor));
        }
        ctx.stroke();
      }

      // Preview segment to hover position.
      if (nodes.length > 0 && hoverPos) {
        const last = nodes[nodes.length - 1];
        ctx.setLineDash([4, 3]);
        ctx.globalAlpha = 0.6;
        ctx.beginPath();
        ctx.moveTo(...toScreen(last.anchor));
        if (last.handleOut) {
          ctx.bezierCurveTo(
            ...toScreen(last.handleOut),
            ...toScreen(hoverPos),
            ...toScreen(hoverPos),
          );
        } else {
          ctx.lineTo(...toScreen(hoverPos));
        }
        ctx.stroke();
        ctx.setLineDash([]);
        ctx.globalAlpha = 1;
      }

      // Draw node dots; highlight first node as "close" indicator when hoverable.
      for (let i = 0; i < nodes.length; i++) {
        const [nx, ny] = toScreen(nodes[i].anchor);
        const isFirst = i === 0 && nodes.length >= 2;
        const closeRadius = 10; // px
        const nearFirst =
          isFirst &&
          hoverPos != null &&
          (() => {
            const [fx, fy] = toScreen(nodes[0].anchor);
            const [hx, hy] = toScreen(hoverPos);
            return Math.hypot(fx - hx, fy - hy) < closeRadius;
          })();
        ctx.beginPath();
        ctx.arc(nx, ny, nearFirst ? 7 : 4, 0, Math.PI * 2);
        ctx.fillStyle = nearFirst ? colPrimary : colCard;
        ctx.strokeStyle = colPrimary;
        ctx.lineWidth = 1.5;
        ctx.fill();
        ctx.stroke();

        // Draw handles.
        const n = nodes[i];
        if (n.handleOut) {
          const [hox, hoy] = toScreen(n.handleOut);
          ctx.strokeStyle = colMuted;
          ctx.lineWidth = 1;
          ctx.beginPath(); ctx.moveTo(nx, ny); ctx.lineTo(hox, hoy); ctx.stroke();
          ctx.beginPath(); ctx.arc(hox, hoy, 3, 0, Math.PI * 2);
          ctx.fillStyle = colMuted; ctx.fill();
        }
      }
    }

    // --- secondary selection highlight (for boolean ops) --------------------
    if (secondaryPartId) {
      const secPart = project.parts.find((p) => p.id === secondaryPartId);
      const secPaths = secPart ? geometry.get(secPart.fileId) : undefined;
      if (secPart && secPaths) {
        const sb = partWorldBBox(secPart, secPaths);
        const [sx0, sy0] = toScreen([sb.minX, sb.maxY]);
        const [sx1, sy1] = toScreen([sb.maxX, sb.minY]);
        ctx.setLineDash([6, 3]);
        ctx.strokeStyle = "#f59e0b"; // amber for secondary
        ctx.lineWidth = 1.5;
        ctx.strokeRect(sx0, sy0, sx1 - sx0, sy1 - sy0);
        ctx.setLineDash([]);
      }
    }

    // --- node editing overlay -----------------------------------------------
    if (nodeEdit) {
      const nePart = project.parts.find((p) => p.id === nodeEdit.partId);
      const nePaths = geometry.get(nodeEdit.fileId);
      if (nePart && nePaths) {
        const pivot = localPivot(nePaths);
        const drag = dragRef.current;

        for (let pi = 0; pi < nePaths.length; pi++) {
          const path = nePaths[pi];
          const ptCount = path.points.length;
          const segs = path.closed ? ptCount : ptCount - 1;

          // Draw segment midpoint dots (add-node affordance).
          for (let si = 0; si < segs; si++) {
            const a = path.points[si] as Vec;
            const b = path.points[(si + 1) % ptCount] as Vec;
            const wa = partToWorld(nePart, pivot, a);
            const wb = partToWorld(nePart, pivot, b);
            const mx = (wa[0] + wb[0]) / 2, my = (wa[1] + wb[1]) / 2;
            const [smx, smy] = toScreen([mx, my]);
            const hovered = nodeEdit.hoveredSeg?.pathIdx === pi && nodeEdit.hoveredSeg?.segIdx === si;
            ctx.beginPath();
            ctx.arc(smx, smy, hovered ? 4 : 3, 0, Math.PI * 2);
            ctx.fillStyle = hovered ? colPrimary : colMuted;
            ctx.globalAlpha = 0.6;
            ctx.fill();
            ctx.globalAlpha = 1;
          }

          // Draw node dots.
          for (let ni = 0; ni < ptCount; ni++) {
            const isDragging = drag?.mode === "node" && drag.nodePathIdx === pi && drag.nodeIdx === ni;
            const wp: Vec = isDragging && drag?.nodeDragWorld
              ? drag.nodeDragWorld
              : partToWorld(nePart, pivot, path.points[ni] as Vec);
            const [snx, sny] = toScreen(wp);
            const selected = nodeEdit.selectedNode?.pathIdx === pi && nodeEdit.selectedNode?.nodeIdx === ni;
            const hovered = nodeEdit.hoveredNode?.pathIdx === pi && nodeEdit.hoveredNode?.nodeIdx === ni;
            ctx.beginPath();
            ctx.arc(snx, sny, selected || hovered ? 6 : 4, 0, Math.PI * 2);
            ctx.fillStyle = selected ? colPrimary : colCard;
            ctx.strokeStyle = hovered ? colPrimary : colFg;
            ctx.lineWidth = 1.5;
            ctx.fill();
            ctx.stroke();
          }
        }
      }
    }

    // --- toolpath simulation overlay ----------------------------------------
    if (simulation && simulation.segments.length > 0) {
      const colCut = "#f97316";
      const colLead = "#fdba74";
      const line = (from: Vec, to: Vec) => {
        const [x0, y0] = toScreen(from);
        const [x1, y1] = toScreen(to);
        ctx.beginPath(); ctx.moveTo(x0, y0); ctx.lineTo(x1, y1); ctx.stroke();
      };

      for (const seg of simulation.segments) {
        const done = seg.t1 <= simTime;
        const active = seg.t0 <= simTime && simTime < seg.t1;
        if (seg.kind === "pierce") continue;
        if (seg.kind === "rapid") {
          ctx.setLineDash([5, 4]);
          ctx.strokeStyle = colMuted;
          ctx.globalAlpha = done || active ? 0.8 : 0.35;
          ctx.lineWidth = 1;
          line(seg.from, seg.to);
          ctx.setLineDash([]);
          continue;
        }
        ctx.strokeStyle = seg.lead ? colLead : colCut;
        ctx.globalAlpha = done ? 1 : 0.3;
        ctx.lineWidth = done ? 2.5 : 1.5;
        if (active) {
          const f = (simTime - seg.t0) / (seg.t1 - seg.t0);
          const mid: Vec = [seg.from[0] + (seg.to[0] - seg.from[0]) * f, seg.from[1] + (seg.to[1] - seg.from[1]) * f];
          ctx.globalAlpha = 1; ctx.lineWidth = 2.5; line(seg.from, mid);
          ctx.globalAlpha = 0.3; ctx.lineWidth = 1.5; line(mid, seg.to);
        } else {
          line(seg.from, seg.to);
        }
      }
      ctx.globalAlpha = 1;

      const longest = new Map<number, (typeof simulation.segments)[number]>();
      for (const seg of simulation.segments) {
        if (seg.kind !== "cut" || seg.lead || seg.cutIndex < 0) continue;
        const cur = longest.get(seg.cutIndex);
        const len = Math.hypot(seg.to[0] - seg.from[0], seg.to[1] - seg.from[1]);
        if (!cur || len > Math.hypot(cur.to[0] - cur.from[0], cur.to[1] - cur.from[1]))
          longest.set(seg.cutIndex, seg);
      }
      ctx.fillStyle = colCut;
      for (const seg of longest.values()) {
        const [x0, y0] = toScreen(seg.from), [x1, y1] = toScreen(seg.to);
        const a = Math.atan2(y1 - y0, x1 - x0);
        const mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
        ctx.beginPath();
        ctx.moveTo(mx + 6 * Math.cos(a), my + 6 * Math.sin(a));
        ctx.lineTo(mx + 6 * Math.cos(a + 2.6), my + 6 * Math.sin(a + 2.6));
        ctx.lineTo(mx + 6 * Math.cos(a - 2.6), my + 6 * Math.sin(a - 2.6));
        ctx.closePath(); ctx.fill();
      }

      const head = stateAt(simulation, simTime);
      if (head) {
        const [hx, hy] = toScreen(head.pos);
        if (head.torchOn) {
          const glow = ctx.createRadialGradient(hx, hy, 1, hx, hy, 12);
          glow.addColorStop(0, "rgba(249,115,22,0.85)");
          glow.addColorStop(1, "rgba(249,115,22,0)");
          ctx.fillStyle = glow;
          ctx.beginPath(); ctx.arc(hx, hy, 12, 0, Math.PI * 2); ctx.fill();
          ctx.fillStyle = "#fff7ed";
          ctx.beginPath(); ctx.arc(hx, hy, 3, 0, Math.PI * 2); ctx.fill();
          ctx.strokeStyle = colCut; ctx.lineWidth = 1.5; ctx.stroke();
        } else {
          ctx.strokeStyle = colFg; ctx.lineWidth = 1.5;
          ctx.beginPath(); ctx.arc(hx, hy, 5, 0, Math.PI * 2); ctx.stroke();
          ctx.beginPath();
          ctx.moveTo(hx - 8, hy); ctx.lineTo(hx + 8, hy);
          ctx.moveTo(hx, hy - 8); ctx.lineTo(hx, hy + 8);
          ctx.stroke();
        }
      }
    }

    // ── Dimension indicators (guide ↔ dragged-part edge) ─────────────────────
    const drag = dragRef.current;
    if (drag?.mode === "move" && selectedPartId && (settings?.showGuides ?? true) && guides.length > 0) {
      const part = project.parts.find((p) => p.id === selectedPartId);
      if (part) {
        const pths = geometry.get(part.fileId);
        if (pths) {
          const b = partWorldBBox(part, pths);
          ctx.strokeStyle = "#f97316";
          ctx.fillStyle = "#f97316";
          ctx.font = "11px system-ui";
          ctx.textAlign = "center";
          ctx.lineWidth = 1;
          for (const guide of guides) {
            let dist: number, p1: Vec, p2: Vec;
            if (guide.axis === "v") {
              const dMin = Math.abs(b.minX - guide.posMm);
              const dMax = Math.abs(b.maxX - guide.posMm);
              const edge = dMin < dMax ? b.minX : b.maxX;
              dist = Math.abs(edge - guide.posMm);
              if (dist > 200) continue;
              const cy = (b.minY + b.maxY) / 2;
              p1 = [guide.posMm, cy]; p2 = [edge, cy];
            } else {
              const dMin = Math.abs(b.minY - guide.posMm);
              const dMax = Math.abs(b.maxY - guide.posMm);
              const edge = dMin < dMax ? b.minY : b.maxY;
              dist = Math.abs(edge - guide.posMm);
              if (dist > 200) continue;
              const cx = (b.minX + b.maxX) / 2;
              p1 = [cx, guide.posMm]; p2 = [cx, edge];
            }
            ctx.setLineDash([3, 3]);
            ctx.beginPath();
            ctx.moveTo(...toScreen(p1)); ctx.lineTo(...toScreen(p2));
            ctx.stroke();
            ctx.setLineDash([]);
            const mid: Vec = [(p1[0] + p2[0]) / 2, (p1[1] + p2[1]) / 2];
            const [mx, my] = toScreen(mid);
            const label = `${dist.toFixed(2)} mm`;
            ctx.fillStyle = darkCanvas ? "#0a0a0a" : "#fff";
            const tw = ctx.measureText(label).width;
            ctx.fillRect(mx - tw / 2 - 3, my - 11, tw + 6, 14);
            ctx.fillStyle = "#f97316";
            ctx.fillText(label, mx, my);
          }
        }
      }
    }

    // ── Guide lines ───────────────────────────────────────────────────────────
    if (settings?.showGuides ?? true) {
      const allGuides: Array<{ id: string | null; axis: "h" | "v"; posMm: number; locked: boolean }> =
        guides.map((g) => ({ ...g }));
      // Overlay the live drag position.
      if (guideDrag) {
        const idx = allGuides.findIndex((g) => g.id === guideDrag.id);
        if (idx >= 0) allGuides[idx] = { ...allGuides[idx], posMm: guideDrag.posMm };
        else allGuides.push({ id: null, axis: guideDrag.axis, posMm: guideDrag.posMm, locked: false });
      }
      for (const g of allGuides) {
        ctx.strokeStyle = g.locked ? "#a855f7" : "#e91e8c";
        ctx.globalAlpha = 0.75;
        ctx.lineWidth = 1;
        ctx.setLineDash(g.locked ? [5, 3] : []);
        if (g.axis === "h") {
          const [, sy] = toScreen([0, g.posMm]);
          ctx.beginPath(); ctx.moveTo(0, sy); ctx.lineTo(size.w, sy); ctx.stroke();
        } else {
          const [sx] = toScreen([g.posMm, 0]);
          ctx.beginPath(); ctx.moveTo(sx, 0); ctx.lineTo(sx, size.h); ctx.stroke();
        }
        ctx.setLineDash([]);
        ctx.globalAlpha = 1;
      }
    }

    // ── Rulers (drawn last — on top of everything) ───────────────────────────
    if (settings?.showRulers ?? true) {
      const R = RULER_PX;
      const rulerBg = darkCanvas ? "#18181b" : "#f4f4f5";
      const rulerBorder = darkCanvas ? "#3f3f46" : "#d4d4d8";
      const tickCol = darkCanvas ? "#71717a" : "#a1a1aa";
      const labelCol = darkCanvas ? "#a1a1aa" : "#52525b";

      // Backgrounds
      ctx.fillStyle = rulerBg;
      ctx.fillRect(0, 0, size.w, R);
      ctx.fillRect(0, 0, R, size.h);
      // Corner square
      ctx.fillStyle = darkCanvas ? "#27272a" : "#e4e4e7";
      ctx.fillRect(0, 0, R, R);
      // Borders
      ctx.strokeStyle = rulerBorder;
      ctx.lineWidth = 0.5;
      ctx.beginPath();
      ctx.moveTo(0, R); ctx.lineTo(size.w, R);   // bottom of top ruler
      ctx.moveTo(R, 0); ctx.lineTo(R, size.h);   // right of left ruler
      ctx.stroke();

      const minor = [1, 2, 5, 10, 25, 50, 100, 250].find((s) => s * view.scale >= 20) ?? 500;
      ctx.strokeStyle = tickCol;
      ctx.fillStyle = labelCol;
      ctx.lineWidth = 0.5;
      ctx.font = `9px system-ui`;
      ctx.textAlign = "center";
      ctx.textBaseline = "top";

      // Top ruler — X axis
      const xStart = Math.floor((0 - view.tx) / view.scale / minor) * minor;
      const xEnd = Math.ceil((size.w - view.tx) / view.scale / minor) * minor;
      for (let wx = xStart; wx <= xEnd; wx += minor) {
        const sx = wx * view.scale + view.tx;
        const major = wx % (minor * 5) === 0;
        ctx.beginPath();
        ctx.moveTo(sx, major ? R - 9 : R - 5);
        ctx.lineTo(sx, R);
        ctx.stroke();
        if (major) ctx.fillText(String(wx), sx, 2);
      }

      // Left ruler — Y axis (world Y-up, screen Y-down)
      ctx.textAlign = "right";
      ctx.textBaseline = "middle";
      const yStart = Math.floor((-view.ty) / view.scale / minor) * minor;
      const yEnd = Math.ceil((size.h - view.ty) / view.scale / minor) * minor;
      for (let wy = yStart; wy <= yEnd; wy += minor) {
        const sy = size.h - (wy * view.scale + view.ty);
        const major = wy % (minor * 5) === 0;
        ctx.beginPath();
        ctx.moveTo(major ? R - 9 : R - 5, sy);
        ctx.lineTo(R, sy);
        ctx.stroke();
        if (major) {
          ctx.save();
          ctx.translate(2, sy);
          ctx.rotate(-Math.PI / 2);
          ctx.textAlign = "center";
          ctx.fillText(String(wy), 0, 0);
          ctx.restore();
        }
      }
    }
  }, [project, geometry, selectedPartId, secondaryPartId, view, size, toScreen, rotateHandleWorld, table,
      simulation, simTime, showGrid, darkCanvas, drawState, activeTool, nodeEdit,
      guides, guideDrag, settings]); // eslint-disable-line react-hooks/exhaustive-deps

  // --- render ---------------------------------------------------------------

  const selectedPaths = selectedPart ? geometry.get(selectedPart.fileId) : undefined;
  const selectedBBox =
    selectedPart && selectedPaths ? partWorldBBox(selectedPart, selectedPaths) : null;

  const applyTransform = (part: Part) => { onPartChange(part); onPartCommit(part); };

  const cursorStyle =
    isDrawing
      ? activeTool.type === "text"
        ? "text"
        : "crosshair"
      : "default";

  const iconBtn = "size-7";

  // Convert text-panel's world position to screen position for overlay placement.
  const textScreenPos =
    drawState?.phase === "text"
      ? toScreen((drawState as TextState).worldPos)
      : null;

  return (
    <div ref={containerRef} className="relative h-full w-full overflow-hidden rounded-lg border">
      <canvas
        ref={canvasRef}
        tabIndex={0}
        className="block h-full w-full outline-none"
        style={{ width: size.w, height: size.h, cursor: cursorStyle }}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
        onWheel={handleWheel}
        onKeyDown={handleKeyDown}
        onPointerLeave={() => setCursorMm(null)}
        onContextMenu={(e) => e.preventDefault()}
      />

      {/* Floating edit toolbar — selection only (hidden in node-edit mode). */}
      {!readOnly && activeTool.type === "select" && selectedPart && selectedBBox && !nodeEdit && (() => {
        const selFile = project.files.find((f) => f.id === selectedPart.fileId);
        const naturalSize = { w: selFile?.widthMm ?? 1, h: selFile?.heightMm ?? 1 };
        return (
          <EditToolbar
            part={selectedPart}
            bbox={selectedBBox}
            naturalSize={naturalSize}
            onTransform={applyTransform}
            onRotateBy={rotateBy}
            onAlign={(edge: AlignEdge) => align(edge)}
            onDuplicate={() => onDuplicate(selectedPart.id)}
            onDelete={() => onDelete(selectedPart.id)}
            onReorder={(dir) => onReorder?.(selectedPart.id, dir)}
            onOffset={(mm) => onOffset?.(selectedPart.id, mm)}
            onSimplify={() => onSimplify?.(selectedPart.fileId)}
            onArray={(type, params) => onArray?.(selectedPart.id, type, params)}
            onTestArray={(params) => onTestArray?.(selectedPart.id, params)}
          />
        );
      })()}

      {/* Boolean toolbar — shown when a secondary part is shift-selected. */}
      {!readOnly && selectedPartId && secondaryPartId && (
        <BooleanToolbar
          primaryId={selectedPartId}
          secondaryId={secondaryPartId}
          onBoolean={(op) => onBoolean?.(op)}
          onClear={() => onSecondarySelect?.(null)}
        />
      )}

      {/* Node-edit mode hint + exit button. */}
      {!readOnly && nodeEdit && (
        <div className="absolute left-1/2 top-2 -translate-x-1/2 flex items-center gap-2 rounded-md bg-background/90 border px-3 py-1 text-xs text-muted-foreground shadow-sm backdrop-blur select-none">
          <span>Node edit — drag nodes · click segment to add · Del to delete</span>
          <button
            className="ml-1 text-muted-foreground hover:text-foreground"
            onClick={() => setNodeEdit(null)}
            title="Exit node edit (Esc)"
          >
            ✕
          </button>
        </div>
      )}

      {/* Text panel — appears at click position. */}
      {!readOnly && drawState?.phase === "text" && textScreenPos && (
        <div
          className="absolute z-30"
          style={{
            left: Math.min(textScreenPos[0], size.w - 300),
            top: Math.max(8, textScreenPos[1] - 20),
          }}
        >
          <TextPanel
            onPlace={(paths, name) => {
              const worldPos = (drawState as TextState).worldPos;
              onShapeCreated?.(paths, name, worldPos[0], worldPos[1]);
              setDrawState(null);
              onToolReset?.();
            }}
            onCancel={() => { setDrawState(null); onToolReset?.(); }}
          />
        </div>
      )}

      {/* Pen tool hint bar */}
      {!readOnly && activeTool.type === "pen" && drawState?.phase === "pen" && (
        <div className="absolute left-1/2 top-2 -translate-x-1/2 rounded-md bg-background/90 border px-3 py-1 text-xs text-muted-foreground shadow-sm backdrop-blur pointer-events-none select-none">
          Click to add nodes · Drag to curve · Click first node or Enter to finish · Esc to cancel
        </div>
      )}
      {!readOnly && activeTool.type === "pen" && (!drawState || drawState.phase !== "pen") && (
        <div className="absolute left-1/2 top-2 -translate-x-1/2 rounded-md bg-background/90 border px-3 py-1 text-xs text-muted-foreground shadow-sm backdrop-blur pointer-events-none select-none">
          Click on canvas to start drawing · Esc to cancel
        </div>
      )}

      {/* Shape tool hint */}
      {!readOnly && ["line","rect","circle","ellipse","polygon","star"].includes(activeTool.type) && (
        <div className="absolute left-1/2 top-2 -translate-x-1/2 rounded-md bg-background/90 border px-3 py-1 text-xs text-muted-foreground shadow-sm backdrop-blur pointer-events-none select-none">
          Click + drag to draw · Esc to cancel
        </div>
      )}

      {/* Bottom bar */}
      <div className="absolute inset-x-0 bottom-0 flex items-center justify-between gap-2 border-t bg-background/90 px-2 py-1 backdrop-blur">
        <span className="shrink-0 font-mono text-xs text-muted-foreground">
          {cursorMm ? `${cursorMm[0].toFixed(1)}, ${cursorMm[1].toFixed(1)} mm` : "—"}
        </span>
        <div className="flex items-center gap-1">
          <Button variant={snap ? "secondary" : "ghost"} size="icon" className={iconBtn}
            title={`Snap to grid (${snapStep}mm)`} onClick={() => setSnap((s) => !s)}>
            <Magnet className="size-4" />
          </Button>
          <Select value={String(snapStep)} onValueChange={(v) => setSnapStep(Number(v))}>
            <SelectTrigger size="sm" className="h-7 w-[72px] text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {SNAP_STEPS.map((s) => (
                <SelectItem key={s} value={String(s)}>{s} mm</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Separator orientation="vertical" className="h-5" />
          <Button variant={showGrid ? "secondary" : "ghost"} size="icon" className={iconBtn}
            title="Toggle grid" onClick={() => setShowGrid((g) => !g)}>
            <Grid3x3 className="size-4" />
          </Button>
          <Button variant="ghost" size="icon" className={iconBtn}
            title={darkCanvas ? "Light canvas" : "Dark canvas"}
            onClick={() => setDarkCanvas((d) => !d)}>
            {darkCanvas ? <Sun className="size-4" /> : <Moon className="size-4" />}
          </Button>
          <Separator orientation="vertical" className="h-5" />
          <Button variant="ghost" size="icon" className={iconBtn} title="Zoom out"
            onClick={() => zoomAt([size.w / 2, size.h / 2], 1 / 1.3)}>
            <ZoomOut className="size-4" />
          </Button>
          <span className="w-[44px] text-center font-mono text-xs text-muted-foreground">
            {(view.scale * 100).toFixed(0)}%
          </span>
          <Button variant="ghost" size="icon" className={iconBtn} title="Zoom in"
            onClick={() => zoomAt([size.w / 2, size.h / 2], 1.3)}>
            <ZoomIn className="size-4" />
          </Button>
          <Button variant="ghost" size="icon" className={iconBtn} title="Fit to view" onClick={fitToView}>
            <Maximize className="size-4" />
          </Button>
        </div>
      </div>
    </div>
  );
}
