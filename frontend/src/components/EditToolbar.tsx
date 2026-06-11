import { useState } from "react";
import {
  AlignCenterHorizontal,
  AlignCenterVertical,
  AlignEndHorizontal,
  AlignEndVertical,
  AlignStartHorizontal,
  AlignStartVertical,
  ArrowDown,
  ArrowUp,
  ChevronsDown,
  ChevronsUp,
  Copy,
  FlipHorizontal2,
  FlipVertical2,
  Link2,
  Link2Off,
  RotateCcw,
  RotateCw,
  Spline,
  Trash2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Separator } from "@/components/ui/separator";
import { ArrayPanel } from "@/components/ArrayPanel";
import type { BBox } from "@/lib/geometry";
import type { Part } from "@/lib/project";

export type AlignEdge = "left" | "centerX" | "right" | "bottom" | "centerY" | "top";

interface Props {
  part: Part;
  /** Selected part's world-space bounding box. */
  bbox: BBox;
  /** Natural (unscaled) file size in mm. */
  naturalSize: { w: number; h: number };
  onTransform: (part: Part) => void;
  onRotateBy: (deg: number) => void;
  onAlign: (edge: AlignEdge) => void;
  onDuplicate: () => void;
  onDelete: () => void;
  onReorder: (direction: "up" | "down" | "front" | "back") => void;
  onOffset: (offsetMm: number) => void;
  /** Simplify path nodes (RDP). Double-click part to enter node-edit mode. */
  onSimplify?: () => void;
  onArray?: (type: "grid" | "circular", params: object) => void;
  onTestArray?: (params: object) => void;
}

export function EditToolbar({
  part,
  bbox,
  naturalSize,
  onTransform,
  onRotateBy,
  onAlign,
  onDuplicate,
  onDelete,
  onReorder,
  onOffset,
  onSimplify,
  onArray,
  onTestArray,
}: Props) {
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [aspectLocked, setAspectLocked] = useState(true);
  const [offsetDraft, setOffsetDraft] = useState("");

  const currentW = Math.abs(part.scaleX) * naturalSize.w;
  const currentH = Math.abs(part.scaleY) * naturalSize.h;

  const numInput = (
    key: string,
    label: string,
    current: number,
    commitValue: (n: number) => void,
    widthClass = "w-[64px]",
  ) => {
    const precision = key === "angle" ? 0 : 1;
    const value = draft[key] ?? current.toFixed(precision);
    const commit = () => {
      const n = Number(draft[key]);
      setDraft((d) => {
        const { [key]: _, ...rest } = d;
        return rest;
      });
      if (draft[key] === undefined || !Number.isFinite(n)) return;
      commitValue(n);
    };
    return (
      <label className="flex items-center gap-1 text-xs text-muted-foreground">
        {label}
        <Input
          className={`h-7 ${widthClass} px-1.5 text-xs`}
          inputMode="decimal"
          value={value}
          onChange={(e) => setDraft((d) => ({ ...d, [key]: e.target.value }))}
          onBlur={commit}
          onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
        />
      </label>
    );
  };

  const iconBtn = "size-7";

  const commitW = (newW: number) => {
    if (newW <= 0 || naturalSize.w <= 0) return;
    const newScaleX = (newW / naturalSize.w) * Math.sign(part.scaleX || 1);
    if (aspectLocked && naturalSize.h > 0) {
      const ratio = newW / currentW;
      const newScaleY = part.scaleY * ratio;
      onTransform({ ...part, scaleX: newScaleX, scaleY: newScaleY });
    } else {
      onTransform({ ...part, scaleX: newScaleX });
    }
  };

  const commitH = (newH: number) => {
    if (newH <= 0 || naturalSize.h <= 0) return;
    const newScaleY = (newH / naturalSize.h) * Math.sign(part.scaleY || 1);
    if (aspectLocked && naturalSize.w > 0) {
      const ratio = newH / currentH;
      const newScaleX = part.scaleX * ratio;
      onTransform({ ...part, scaleX: newScaleX, scaleY: newScaleY });
    } else {
      onTransform({ ...part, scaleY: newScaleY });
    }
  };

  const commitOffset = () => {
    const n = Number(offsetDraft);
    if (!Number.isFinite(n) || n === 0) return;
    setOffsetDraft("");
    onOffset(n);
  };

  return (
    <div className="absolute left-1/2 top-2 flex max-w-[calc(100%-1rem)] -translate-x-1/2 flex-wrap items-center justify-center gap-1.5 rounded-md border bg-background/95 px-2 py-1 shadow-md backdrop-blur">
      {/* X/Y position */}
      {numInput("x", "X", bbox.minX, (n) =>
        onTransform({ ...part, x: part.x + (n - bbox.minX) }),
      )}
      {numInput("y", "Y", bbox.minY, (n) =>
        onTransform({ ...part, y: part.y + (n - bbox.minY) }),
      )}

      <Separator orientation="vertical" className="h-5" />

      {/* W / H with aspect lock */}
      {numInput("w", "W", currentW, commitW)}
      <Button
        variant="ghost"
        size="icon"
        className="size-6"
        title={aspectLocked ? "Unlock aspect ratio" : "Lock aspect ratio"}
        onClick={() => setAspectLocked((a) => !a)}
      >
        {aspectLocked ? <Link2 className="size-3" /> : <Link2Off className="size-3" />}
      </Button>
      {numInput("h", "H", currentH, commitH)}

      {/* Rotation */}
      {numInput("angle", "∠", part.rotationDeg, (n) =>
        onTransform({ ...part, rotationDeg: ((n % 360) + 360) % 360 }),
      )}

      <Separator orientation="vertical" className="h-5" />

      {/* Rotate 90 */}
      <Button variant="ghost" size="icon" className={iconBtn} title="Rotate 90° CCW"
        onClick={() => onRotateBy(90)}>
        <RotateCcw className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Rotate 90° CW"
        onClick={() => onRotateBy(-90)}>
        <RotateCw className="size-4" />
      </Button>

      {/* Mirror */}
      <Button variant="ghost" size="icon" className={iconBtn} title="Mirror horizontal"
        onClick={() => onTransform({ ...part, scaleX: -part.scaleX })}>
        <FlipHorizontal2 className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Mirror vertical"
        onClick={() => onTransform({ ...part, scaleY: -part.scaleY })}>
        <FlipVertical2 className="size-4" />
      </Button>

      <Separator orientation="vertical" className="h-5" />

      {/* Stacking order */}
      <Button variant="ghost" size="icon" className={iconBtn} title="Bring to front"
        onClick={() => onReorder("front")}>
        <ChevronsUp className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Bring forward"
        onClick={() => onReorder("up")}>
        <ArrowUp className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Send backward"
        onClick={() => onReorder("down")}>
        <ArrowDown className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Send to back"
        onClick={() => onReorder("back")}>
        <ChevronsDown className="size-4" />
      </Button>

      <Separator orientation="vertical" className="h-5" />

      {/* Align */}
      <Button variant="ghost" size="icon" className={iconBtn} title="Align left"
        onClick={() => onAlign("left")}>
        <AlignStartVertical className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Center horizontally"
        onClick={() => onAlign("centerX")}>
        <AlignCenterVertical className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Align right"
        onClick={() => onAlign("right")}>
        <AlignEndVertical className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Align bottom"
        onClick={() => onAlign("bottom")}>
        <AlignStartHorizontal className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Center vertically"
        onClick={() => onAlign("centerY")}>
        <AlignCenterHorizontal className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Align top"
        onClick={() => onAlign("top")}>
        <AlignEndHorizontal className="size-4" />
      </Button>

      <Separator orientation="vertical" className="h-5" />

      {/* Contour offset */}
      <label className="flex items-center gap-1 text-xs text-muted-foreground">
        Offset
        <Input
          className="h-7 w-[56px] px-1.5 text-xs"
          inputMode="decimal"
          placeholder="±mm"
          value={offsetDraft}
          onChange={(e) => setOffsetDraft(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && commitOffset()}
          title="Contour offset in mm (positive = outward, negative = inward). Press Enter to apply."
        />
      </label>

      <Separator orientation="vertical" className="h-5" />

      {onSimplify && (
        <Button variant="ghost" size="icon" className={iconBtn}
          title="Simplify path nodes (Ramer-Douglas-Peucker, 0.1mm tolerance)"
          onClick={onSimplify}>
          <Spline className="size-4" />
        </Button>
      )}

      {onArray && onTestArray && (
        <ArrayPanel onArray={onArray} onTestArray={onTestArray} />
      )}

      <Button variant="ghost" size="icon" className={iconBtn} title="Duplicate (Ctrl+D)"
        onClick={onDuplicate}>
        <Copy className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={`${iconBtn} text-destructive`}
        title="Delete (Del)" onClick={onDelete}>
        <Trash2 className="size-4" />
      </Button>
    </div>
  );
}
