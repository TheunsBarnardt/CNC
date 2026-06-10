import { useCallback, useEffect, useRef, useState } from "react";
import {
  AlignCenterHorizontal,
  AlignCenterVertical,
  AlignEndHorizontal,
  AlignEndVertical,
  AlignStartHorizontal,
  AlignStartVertical,
  Copy,
  Magnet,
  Maximize,
  RotateCcw,
  RotateCw,
  Trash2,
  ZoomIn,
  ZoomOut,
} from "lucide-react";
import { Button } from "@/components/ui/button";
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
import type { GeometryPath, Part, ProjectDto, TableOrigin } from "@/lib/project";

interface ViewportProps {
  project: ProjectDto;
  geometry: Map<string, GeometryPath[]>;
  selectedPartId: string | null;
  onSelect: (id: string | null) => void;
  /** Live (optimistic) part update while dragging — local state only. */
  onPartChange: (part: Part) => void;
  /** Persist a part's transform (drag end / toolbar action). */
  onPartCommit: (part: Part) => void;
  onDuplicate: (id: string) => void;
  onDelete: (id: string) => void;
}

interface View {
  scale: number; // px per mm
  tx: number;
  ty: number;
}

type DragMode = "pan" | "move" | "rotate";

interface DragState {
  mode: DragMode;
  pointerId: number;
  startScreen: Vec;
  startView: View;
  startPart?: Part;
  /** Most recent optimistic value — committed on release. Kept here because
   * the `project` prop can lag a frame behind the last pointermove. */
  lastPart?: Part;
  moved: boolean;
}

const SNAP_STEPS = [1, 5, 10, 25, 50];
const ROTATE_HANDLE_PX = 28;

/**
 * 2D table viewport (Canvas 2D): grid, origin marker, placed parts,
 * pan/zoom/fit, select/move/rotate with snap-to-grid.
 */
export function Viewport({
  project,
  geometry,
  selectedPartId,
  onSelect,
  onPartChange,
  onPartCommit,
  onDuplicate,
  onDelete,
}: ViewportProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ w: 0, h: 0 });
  const [view, setView] = useState<View>({ scale: 0.5, tx: 0, ty: 0 });
  const [snap, setSnap] = useState(true);
  const [snapStep, setSnapStep] = useState(10);
  const [cursorMm, setCursorMm] = useState<Vec | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const fittedRef = useRef(false);

  const { table } = project;
  const selectedPart = project.parts.find((p) => p.id === selectedPartId) ?? null;

  // --- coordinate mapping (world mm Y-up ↔ screen px Y-down) ---------------
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

  // Track container size (devicePixelRatio handled at draw time).
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const observer = new ResizeObserver(() => {
      setSize({ w: el.clientWidth, h: el.clientHeight });
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  // Fit once the canvas has a size, and re-fit when the table changes.
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
        return {
          scale,
          tx: screen[0] - wx * scale,
          ty: size.h - screen[1] - wy * scale,
        };
      });
    },
    [size.h],
  );

  // --- interactions ---------------------------------------------------------

  const screenPos = (e: { clientX: number; clientY: number }): Vec => {
    const rect = canvasRef.current!.getBoundingClientRect();
    return [e.clientX - rect.left, e.clientY - rect.top];
  };

  /** Rotation-handle position for the selected part, in world mm. */
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

  const handlePointerDown = (e: React.PointerEvent) => {
    if (e.button !== 0 && e.button !== 1) return;
    try {
      canvasRef.current?.setPointerCapture(e.pointerId);
    } catch {
      // Pointer may already be gone (fast taps) — capture is best-effort.
    }
    canvasRef.current?.focus();
    const screen = screenPos(e);
    const world = toWorld(screen);

    // Middle button always pans.
    if (e.button === 0 && selectedPart) {
      const handle = rotateHandleWorld(selectedPart);
      if (handle) {
        const hs = toScreen(handle);
        const dx = screen[0] - hs[0],
          dy = screen[1] - hs[1];
        if (dx * dx + dy * dy <= 100) {
          dragRef.current = {
            mode: "rotate",
            pointerId: e.pointerId,
            startScreen: screen,
            startView: view,
            startPart: { ...selectedPart },
            moved: false,
          };
          return;
        }
      }
    }

    if (e.button === 0) {
      // Topmost part first (parts render in list order).
      const tolerance = 4 / view.scale;
      for (let i = project.parts.length - 1; i >= 0; i--) {
        const part = project.parts[i];
        const file = project.files.find((f) => f.id === part.fileId);
        const paths = geometry.get(part.fileId);
        if (!file?.visible || !paths) continue;
        if (hitTestPart(part, paths, world, tolerance)) {
          onSelect(part.id);
          dragRef.current = {
            mode: "move",
            pointerId: e.pointerId,
            startScreen: screen,
            startView: view,
            startPart: { ...part },
            moved: false,
          };
          return;
        }
      }
      onSelect(null);
    }

    dragRef.current = {
      mode: "pan",
      pointerId: e.pointerId,
      startScreen: screen,
      startView: view,
      moved: false,
    };
  };

  const handlePointerMove = (e: React.PointerEvent) => {
    const screen = screenPos(e);
    const world = toWorld(screen);
    setCursorMm(world);

    const drag = dragRef.current;
    if (!drag || drag.pointerId !== e.pointerId) return;
    const dxs = screen[0] - drag.startScreen[0];
    const dys = screen[1] - drag.startScreen[1];
    if (Math.abs(dxs) + Math.abs(dys) > 2) drag.moved = true;

    if (drag.mode === "pan") {
      setView({
        ...drag.startView,
        tx: drag.startView.tx + dxs,
        ty: drag.startView.ty - dys,
      });
      return;
    }

    if (!drag.startPart) return;
    const current = drag.lastPart ?? drag.startPart;

    if (drag.mode === "move") {
      let x = drag.startPart.x + dxs / view.scale;
      let y = drag.startPart.y - dys / view.scale;
      if (snap) {
        x = Math.round(x / snapStep) * snapStep;
        y = Math.round(y / snapStep) * snapStep;
      }
      drag.lastPart = { ...current, x, y };
      onPartChange(drag.lastPart);
    } else if (drag.mode === "rotate") {
      const paths = geometry.get(drag.startPart.fileId);
      if (!paths) return;
      const pivot = localPivot(paths);
      // World position of the rotation pivot is translation + pivot.
      const cx = drag.startPart.x + pivot[0];
      const cy = drag.startPart.y + pivot[1];
      let deg =
        (Math.atan2(world[1] - cy, world[0] - cx) * 180) / Math.PI - 90;
      deg = e.shiftKey ? Math.round(deg / 15) * 15 : Math.round(deg);
      deg = ((deg % 360) + 360) % 360;
      drag.lastPart = { ...current, rotationDeg: deg };
      onPartChange(drag.lastPart);
    }
  };

  const handlePointerUp = (e: React.PointerEvent) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== e.pointerId) return;
    dragRef.current = null;
    if (drag.mode !== "pan" && drag.lastPart && drag.moved) {
      onPartCommit(drag.lastPart);
    }
  };

  const handleWheel = (e: React.WheelEvent) => {
    zoomAt(screenPos(e), e.deltaY < 0 ? 1.15 : 1 / 1.15);
  };

  const nudge = (dx: number, dy: number) => {
    if (!selectedPart) return;
    const part = { ...selectedPart, x: selectedPart.x + dx, y: selectedPart.y + dy };
    onPartChange(part);
    onPartCommit(part);
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (!selectedPart) return;
    const step = e.shiftKey ? 10 : 1;
    switch (e.key) {
      case "Delete":
      case "Backspace":
        onDelete(selectedPart.id);
        break;
      case "d":
        if (e.ctrlKey || e.metaKey) {
          e.preventDefault();
          onDuplicate(selectedPart.id);
        }
        break;
      case "ArrowLeft":
        e.preventDefault();
        nudge(-step, 0);
        break;
      case "ArrowRight":
        e.preventDefault();
        nudge(step, 0);
        break;
      case "ArrowUp":
        e.preventDefault();
        nudge(0, step);
        break;
      case "ArrowDown":
        e.preventDefault();
        nudge(0, -step);
        break;
    }
  };

  const rotateBy = (deg: number) => {
    if (!selectedPart) return;
    const rotationDeg = (((selectedPart.rotationDeg + deg) % 360) + 360) % 360;
    const part = { ...selectedPart, rotationDeg };
    onPartChange(part);
    onPartCommit(part);
  };

  /** Align the selected part's world bbox against the table. */
  const align = (edge: "left" | "centerX" | "right" | "bottom" | "centerY" | "top") => {
    if (!selectedPart) return;
    const paths = geometry.get(selectedPart.fileId);
    if (!paths) return;
    const b = partWorldBBox(selectedPart, paths);
    const w = b.maxX - b.minX,
      h = b.maxY - b.minY;
    let dx = 0,
      dy = 0;
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

  // --- drawing --------------------------------------------------------------

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
    const colBg = cssVar("--background", "#fff");
    const colCard = cssVar("--card", "#fafafa");
    const colBorder = cssVar("--border", "#ddd");
    const colMuted = cssVar("--muted-foreground", "#888");
    const colFg = cssVar("--foreground", "#222");
    const colPrimary = cssVar("--primary", "#16a34a");
    const colDestructive = cssVar("--destructive", "#dc2626");

    ctx.fillStyle = colBg;
    ctx.fillRect(0, 0, size.w, size.h);

    // Table sheet.
    const [tx0, ty0] = toScreen([0, table.heightMm]);
    const tableWpx = table.widthMm * view.scale;
    const tableHpx = table.heightMm * view.scale;
    ctx.fillStyle = colCard;
    ctx.fillRect(tx0, ty0, tableWpx, tableHpx);

    // Grid (only inside the table). Pick the smallest step that stays legible.
    const minor =
      [1, 2, 5, 10, 25, 50, 100, 250].find((s) => s * view.scale >= 10) ?? 500;
    ctx.save();
    ctx.beginPath();
    ctx.rect(tx0, ty0, tableWpx, tableHpx);
    ctx.clip();
    for (let gx = 0; gx <= table.widthMm; gx += minor) {
      const [sx] = toScreen([gx, 0]);
      const isMajor = gx % (minor * 5) === 0;
      ctx.strokeStyle = colBorder;
      ctx.globalAlpha = isMajor ? 0.9 : 0.4;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(sx, ty0);
      ctx.lineTo(sx, ty0 + tableHpx);
      ctx.stroke();
    }
    for (let gy = 0; gy <= table.heightMm; gy += minor) {
      const [, sy] = toScreen([0, gy]);
      const isMajor = gy % (minor * 5) === 0;
      ctx.strokeStyle = colBorder;
      ctx.globalAlpha = isMajor ? 0.9 : 0.4;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(tx0, sy);
      ctx.lineTo(tx0 + tableWpx, sy);
      ctx.stroke();
    }
    ctx.restore();
    ctx.globalAlpha = 1;

    // Table border.
    ctx.strokeStyle = colMuted;
    ctx.lineWidth = 1.5;
    ctx.strokeRect(tx0, ty0, tableWpx, tableHpx);

    // Origin marker (machine zero per table settings).
    const originWorld: Record<TableOrigin, Vec> = {
      BottomLeft: [0, 0],
      BottomRight: [table.widthMm, 0],
      TopLeft: [0, table.heightMm],
      TopRight: [table.widthMm, table.heightMm],
      Center: [table.widthMm / 2, table.heightMm / 2],
    };
    const [ox, oy] = toScreen(originWorld[table.origin]);
    ctx.strokeStyle = colPrimary;
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.arc(ox, oy, 6, 0, Math.PI * 2);
    ctx.moveTo(ox - 11, oy);
    ctx.lineTo(ox + 11, oy);
    ctx.moveTo(ox, oy - 11);
    ctx.lineTo(ox, oy + 11);
    ctx.stroke();

    // Parts.
    for (const part of project.parts) {
      const file = project.files.find((f) => f.id === part.fileId);
      const paths = geometry.get(part.fileId);
      if (!file?.visible || !paths) continue;
      const pivot = localPivot(paths);
      const selected = part.id === selectedPartId;
      const b = partWorldBBox(part, paths);
      const outOfBounds =
        b.minX < -1e-6 ||
        b.minY < -1e-6 ||
        b.maxX > table.widthMm + 1e-6 ||
        b.maxY > table.heightMm + 1e-6;
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
        // Dashed world-bbox + rotation handle.
        const [bx0, by0] = toScreen([b.minX, b.maxY]);
        const [bx1, by1] = toScreen([b.maxX, b.minY]);
        ctx.setLineDash([4, 4]);
        ctx.strokeStyle = colPrimary;
        ctx.lineWidth = 1;
        ctx.strokeRect(bx0, by0, bx1 - bx0, by1 - by0);
        ctx.setLineDash([]);

        const handle = rotateHandleWorld(part);
        if (handle) {
          const top = partToWorld(part, pivot, [
            pivot[0],
            pivot[1] + (bboxOfPaths(paths).maxY - bboxOfPaths(paths).minY) / 2,
          ]);
          const [hx, hy] = toScreen(handle);
          const [tx, ty] = toScreen(top);
          ctx.strokeStyle = colPrimary;
          ctx.beginPath();
          ctx.moveTo(tx, ty);
          ctx.lineTo(hx, hy);
          ctx.stroke();
          ctx.beginPath();
          ctx.arc(hx, hy, 5, 0, Math.PI * 2);
          ctx.fillStyle = colPrimary;
          ctx.fill();
        }
      }
    }
  }, [project, geometry, selectedPartId, view, size, toScreen, rotateHandleWorld, table]);

  // --- render ---------------------------------------------------------------

  const iconBtn = "size-7";
  return (
    <div ref={containerRef} className="relative h-full w-full overflow-hidden rounded-lg border">
      <canvas
        ref={canvasRef}
        tabIndex={0}
        className="block h-full w-full cursor-crosshair outline-none"
        style={{ width: size.w, height: size.h }}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
        onWheel={handleWheel}
        onKeyDown={handleKeyDown}
        onPointerLeave={() => setCursorMm(null)}
      />

      {/* Toolbar */}
      <div className="absolute left-2 top-2 flex items-center gap-1 rounded-md border bg-background/90 p-1 shadow-sm backdrop-blur">
        <Button variant="ghost" size="icon" className={iconBtn} title="Fit to view" onClick={fitToView}>
          <Maximize className="size-4" />
        </Button>
        <Button variant="ghost" size="icon" className={iconBtn} title="Zoom in"
          onClick={() => zoomAt([size.w / 2, size.h / 2], 1.3)}>
          <ZoomIn className="size-4" />
        </Button>
        <Button variant="ghost" size="icon" className={iconBtn} title="Zoom out"
          onClick={() => zoomAt([size.w / 2, size.h / 2], 1 / 1.3)}>
          <ZoomOut className="size-4" />
        </Button>
        <Separator orientation="vertical" className="h-5" />
        <Button
          variant={snap ? "secondary" : "ghost"}
          size="icon"
          className={iconBtn}
          title={`Snap to grid (${snapStep}mm)`}
          onClick={() => setSnap((s) => !s)}
        >
          <Magnet className="size-4" />
        </Button>
        <Select value={String(snapStep)} onValueChange={(v) => setSnapStep(Number(v))}>
          <SelectTrigger size="sm" className="h-7 w-[72px] text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {SNAP_STEPS.map((s) => (
              <SelectItem key={s} value={String(s)}>
                {s} mm
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Separator orientation="vertical" className="h-5" />
        <Button variant="ghost" size="icon" className={iconBtn} title="Rotate 90° CCW"
          disabled={!selectedPart} onClick={() => rotateBy(90)}>
          <RotateCcw className="size-4" />
        </Button>
        <Button variant="ghost" size="icon" className={iconBtn} title="Rotate 90° CW"
          disabled={!selectedPart} onClick={() => rotateBy(-90)}>
          <RotateCw className="size-4" />
        </Button>
        <Button variant="ghost" size="icon" className={iconBtn} title="Duplicate (Ctrl+D)"
          disabled={!selectedPart} onClick={() => selectedPart && onDuplicate(selectedPart.id)}>
          <Copy className="size-4" />
        </Button>
        <Button variant="ghost" size="icon" className={`${iconBtn} text-destructive`} title="Delete (Del)"
          disabled={!selectedPart} onClick={() => selectedPart && onDelete(selectedPart.id)}>
          <Trash2 className="size-4" />
        </Button>
        <Separator orientation="vertical" className="h-5" />
        <Button variant="ghost" size="icon" className={iconBtn} title="Align left"
          disabled={!selectedPart} onClick={() => align("left")}>
          <AlignStartVertical className="size-4" />
        </Button>
        <Button variant="ghost" size="icon" className={iconBtn} title="Center horizontally"
          disabled={!selectedPart} onClick={() => align("centerX")}>
          <AlignCenterVertical className="size-4" />
        </Button>
        <Button variant="ghost" size="icon" className={iconBtn} title="Align right"
          disabled={!selectedPart} onClick={() => align("right")}>
          <AlignEndVertical className="size-4" />
        </Button>
        <Button variant="ghost" size="icon" className={iconBtn} title="Align bottom"
          disabled={!selectedPart} onClick={() => align("bottom")}>
          <AlignStartHorizontal className="size-4" />
        </Button>
        <Button variant="ghost" size="icon" className={iconBtn} title="Center vertically"
          disabled={!selectedPart} onClick={() => align("centerY")}>
          <AlignCenterHorizontal className="size-4" />
        </Button>
        <Button variant="ghost" size="icon" className={iconBtn} title="Align top"
          disabled={!selectedPart} onClick={() => align("top")}>
          <AlignEndHorizontal className="size-4" />
        </Button>
      </div>

      {/* Status footer */}
      <div className="pointer-events-none absolute bottom-2 left-2 rounded bg-background/80 px-2 py-0.5 font-mono text-xs text-muted-foreground backdrop-blur">
        {cursorMm ? `${cursorMm[0].toFixed(1)}, ${cursorMm[1].toFixed(1)} mm` : "—"}
      </div>
      <div className="pointer-events-none absolute bottom-2 right-2 rounded bg-background/80 px-2 py-0.5 font-mono text-xs text-muted-foreground backdrop-blur">
        {(view.scale * 100).toFixed(0)}%
        {selectedPart && ` · ${selectedPart.rotationDeg.toFixed(0)}°`}
      </div>
    </div>
  );
}
