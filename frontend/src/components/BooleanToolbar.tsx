import { Button } from "@/components/ui/button";

interface Props {
  primaryId: string;
  secondaryId: string;
  onBoolean: (op: "unite" | "subtract" | "intersect") => void;
  onClear: () => void;
}

/**
 * Floating toolbar shown when two parts are selected (primary + shift-click
 * secondary). Provides Clipper2 pathfinder operations that consume both parts
 * and produce a new one.
 */
export function BooleanToolbar({ onBoolean, onClear }: Props) {
  return (
    <div className="absolute left-1/2 bottom-12 flex -translate-x-1/2 items-center gap-1.5 rounded-md border bg-background/95 px-3 py-1.5 shadow-md backdrop-blur">
      <span className="mr-1 text-xs text-muted-foreground">2 selected — Pathfinder:</span>
      <Button size="sm" variant="outline" className="h-7 px-2 text-xs" onClick={() => onBoolean("unite")}
        title="Unite: merge both shapes into one">
        Unite
      </Button>
      <Button size="sm" variant="outline" className="h-7 px-2 text-xs" onClick={() => onBoolean("subtract")}
        title="Subtract: remove secondary from primary">
        Subtract
      </Button>
      <Button size="sm" variant="outline" className="h-7 px-2 text-xs" onClick={() => onBoolean("intersect")}
        title="Intersect: keep only the overlapping region">
        Intersect
      </Button>
      <Button size="sm" variant="ghost" className="h-7 px-2 text-xs text-muted-foreground"
        onClick={onClear} title="Clear secondary selection">
        ✕
      </Button>
    </div>
  );
}
