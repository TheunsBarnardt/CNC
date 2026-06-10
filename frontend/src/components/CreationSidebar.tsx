import { useRef } from "react";
import { FileUp, Loader2, Shapes, Type } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";

const ACCEPTED = [".svg", ".dxf"];

interface Props {
  onImport: (files: File[]) => void;
  busy: boolean;
  /** Creation is disabled while in processing-preview mode. */
  disabled?: boolean;
}

/**
 * Left creation rail (xTool Studio anatomy): import + creation tools.
 * Shape and text tools are stubs until Task 14 (shape & text creation).
 */
export function CreationSidebar({ onImport, busy, disabled = false }: Props) {
  const inputRef = useRef<HTMLInputElement>(null);

  return (
    <div className="flex w-12 shrink-0 flex-col items-center gap-1 border-r bg-card py-2">
      <Button
        variant="ghost"
        size="icon"
        className="size-9"
        title="Import SVG / DXF files"
        disabled={busy || disabled}
        onClick={() => inputRef.current?.click()}
      >
        {busy ? <Loader2 className="size-5 animate-spin" /> : <FileUp className="size-5" />}
      </Button>
      <Separator className="my-1 w-7" />
      <Button
        variant="ghost"
        size="icon"
        className="size-9"
        title="Shapes — coming with the shape & text tools task"
        disabled
      >
        <Shapes className="size-5" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        className="size-9"
        title="Text — coming with the shape & text tools task"
        disabled
      >
        <Type className="size-5" />
      </Button>
      <input
        ref={inputRef}
        type="file"
        accept={ACCEPTED.join(",")}
        multiple
        hidden
        onChange={(e) => {
          const files = Array.from(e.target.files ?? []).filter((f) =>
            ACCEPTED.some((ext) => f.name.toLowerCase().endsWith(ext)),
          );
          if (files.length > 0) onImport(files);
          e.target.value = ""; // allow re-importing the same file
        }}
      />
    </div>
  );
}
