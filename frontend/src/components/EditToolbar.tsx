import { useState } from "react";
import {
  AlignCenterHorizontal,
  AlignCenterVertical,
  AlignEndHorizontal,
  AlignEndVertical,
  AlignStartHorizontal,
  AlignStartVertical,
  Copy,
  FlipHorizontal2,
  FlipVertical2,
  RotateCcw,
  RotateCw,
  Trash2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Separator } from "@/components/ui/separator";
import type { BBox } from "@/lib/geometry";
import type { Part } from "@/lib/project";

export type AlignEdge = "left" | "centerX" | "right" | "bottom" | "centerY" | "top";

interface Props {
  part: Part;
  /** Selected part's world-space bounding box. */
  bbox: BBox;
  /** Apply + persist a new transform (used by the X/Y/angle inputs). */
  onTransform: (part: Part) => void;
  onRotateBy: (deg: number) => void;
  onAlign: (edge: AlignEdge) => void;
  onDuplicate: () => void;
  onDelete: () => void;
}

/**
 * Floating editing toolbar shown over the canvas when a part is selected
 * (xTool Studio anatomy). Position/angle are precise numeric inputs; W/H are
 * read-only because parts have no scale in the transform model yet, and
 * mirror needs model support too — both stubbed, not silently added.
 */
export function EditToolbar({
  part,
  bbox,
  onTransform,
  onRotateBy,
  onAlign,
  onDuplicate,
  onDelete,
}: Props) {
  // Draft strings so typing doesn't move the part until commit (blur/Enter).
  const [draft, setDraft] = useState<Record<string, string>>({});

  const numInput = (
    key: "x" | "y" | "angle",
    label: string,
    current: number,
    commitValue: (n: number) => void,
  ) => {
    const value = draft[key] ?? current.toFixed(key === "angle" ? 0 : 1);
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
          className="h-7 w-[64px] px-1.5 text-xs"
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
  const w = bbox.maxX - bbox.minX;
  const h = bbox.maxY - bbox.minY;

  return (
    <div className="absolute left-1/2 top-2 flex max-w-[calc(100%-1rem)] -translate-x-1/2 flex-wrap items-center justify-center gap-1.5 rounded-md border bg-background/95 px-2 py-1 shadow-md backdrop-blur">
      {/* Position = world bbox min corner — what a user reads off the table. */}
      {numInput("x", "X", bbox.minX, (n) =>
        onTransform({ ...part, x: part.x + (n - bbox.minX) }),
      )}
      {numInput("y", "Y", bbox.minY, (n) =>
        onTransform({ ...part, y: part.y + (n - bbox.minY) }),
      )}
      <span
        className="font-mono text-xs text-muted-foreground"
        title="Size (read-only — parts have no scale yet)"
      >
        {w.toFixed(1)}×{h.toFixed(1)}
      </span>
      {numInput("angle", "∠", part.rotationDeg, (n) =>
        onTransform({ ...part, rotationDeg: ((n % 360) + 360) % 360 }),
      )}

      <Separator orientation="vertical" className="h-5" />
      <Button variant="ghost" size="icon" className={iconBtn} title="Rotate 90° CCW"
        onClick={() => onRotateBy(90)}>
        <RotateCcw className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn} title="Rotate 90° CW"
        onClick={() => onRotateBy(-90)}>
        <RotateCw className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn}
        title="Mirror horizontally — needs part-model support, coming later" disabled>
        <FlipHorizontal2 className="size-4" />
      </Button>
      <Button variant="ghost" size="icon" className={iconBtn}
        title="Mirror vertically — needs part-model support, coming later" disabled>
        <FlipVertical2 className="size-4" />
      </Button>

      <Separator orientation="vertical" className="h-5" />
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
