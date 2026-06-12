import { Check, Scissors, Spline } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";

interface Props {
  /** World-space X of the selected node (null when no node selected). */
  nodeX: number | null;
  nodeY: number | null;
  /** Whether the selected node is smooth (has Bézier handles). */
  isSmooth: boolean | null;
  canSplit: boolean;
  onMakeSharp: () => void;
  onMakeSmooth: () => void;
  onScissors: () => void;
  onSimplify: () => void;
  onDone: () => void;
}

/** Toolbar shown at the top of the viewport while in node-edit mode. */
export function NodeEditToolbar({
  nodeX,
  nodeY,
  isSmooth,
  canSplit,
  onMakeSharp,
  onMakeSmooth,
  onScissors,
  onSimplify,
  onDone,
}: Props) {
  const iconBtn = "size-7";
  const fmt = (v: number) => v.toFixed(2);

  return (
    <div className="absolute left-1/2 top-2 flex max-w-[calc(100%-1rem)] -translate-x-1/2 flex-wrap items-center justify-center gap-1.5 rounded-md border bg-background/95 px-2 py-1 shadow-md backdrop-blur">
      {/* Selected node coordinates */}
      <span className="font-mono text-xs text-muted-foreground">
        X{" "}
        <span className="text-foreground">{nodeX !== null ? fmt(nodeX) : "—"}</span>
      </span>
      <span className="font-mono text-xs text-muted-foreground">
        Y{" "}
        <span className="text-foreground">{nodeY !== null ? fmt(nodeY) : "—"}</span>
      </span>

      <Separator orientation="vertical" className="h-5" />

      {/* Node type buttons */}
      <Button
        variant={isSmooth === false ? "secondary" : "ghost"}
        size="icon"
        className={iconBtn}
        title="Sharp corner — straight segments meet at this node"
        disabled={isSmooth === null}
        onClick={onMakeSharp}
      >
        {/* Sharp corner icon: angled path */}
        <svg viewBox="0 0 16 16" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.5">
          <polyline points="2,13 8,3 14,13" strokeLinejoin="miter" />
          <circle cx="8" cy="3" r="2" fill="currentColor" stroke="none" />
        </svg>
      </Button>
      <Button
        variant={isSmooth === true ? "secondary" : "ghost"}
        size="icon"
        className={iconBtn}
        title="Smooth corner — Bézier handles at this node"
        disabled={isSmooth === null}
        onClick={onMakeSmooth}
      >
        {/* Smooth node icon: curve */}
        <svg viewBox="0 0 16 16" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.5">
          <path d="M2,13 C4,3 12,3 14,13" strokeLinecap="round" />
          <circle cx="8" cy="3.5" r="2" fill="currentColor" stroke="none" />
        </svg>
      </Button>

      <Separator orientation="vertical" className="h-5" />

      {/* Simplify */}
      <Button
        variant="ghost"
        size="icon"
        className={iconBtn}
        title="Simplify path (Ramer-Douglas-Peucker, 0.1 mm tolerance)"
        onClick={onSimplify}
      >
        <Spline className="size-4" />
      </Button>

      {/* Scissors — split path at selected node */}
      <Button
        variant="ghost"
        size="icon"
        className={iconBtn}
        title="Split path at selected node (Scissors)"
        disabled={!canSplit}
        onClick={onScissors}
      >
        <Scissors className="size-4" />
      </Button>

      <Separator orientation="vertical" className="h-5" />

      {/* Done */}
      <Button
        size="sm"
        className="h-7 gap-1.5 bg-green-600 px-2 text-xs text-white hover:bg-green-700"
        onClick={onDone}
        title="Exit node edit (Esc)"
      >
        <Check className="size-3.5" />
        Done
      </Button>
    </div>
  );
}
