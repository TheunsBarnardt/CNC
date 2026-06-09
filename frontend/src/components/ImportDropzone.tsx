import { useCallback, useRef, useState } from "react";
import { FileUp } from "lucide-react";
import { cn } from "@/lib/utils";

interface Props {
  onImport: (files: File[]) => void;
  busy: boolean;
}

const ACCEPTED = [".svg", ".dxf"];

/** Drag-and-drop (or click-to-browse) import zone for SVG/DXF files. */
export function ImportDropzone({ onImport, busy }: Props) {
  const [dragOver, setDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFiles = useCallback(
    (list: FileList | null) => {
      if (!list || list.length === 0) return;
      const files = Array.from(list).filter((f) =>
        ACCEPTED.some((ext) => f.name.toLowerCase().endsWith(ext)),
      );
      if (files.length > 0) onImport(files);
    },
    [onImport],
  );

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label="Import SVG or DXF files"
      onClick={() => inputRef.current?.click()}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") inputRef.current?.click();
      }}
      onDragOver={(e) => {
        e.preventDefault();
        setDragOver(true);
      }}
      onDragLeave={() => setDragOver(false)}
      onDrop={(e) => {
        e.preventDefault();
        setDragOver(false);
        handleFiles(e.dataTransfer.files);
      }}
      className={cn(
        "flex cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed p-8 text-center transition-colors",
        dragOver ? "border-primary bg-accent" : "border-border hover:bg-accent/50",
        busy && "pointer-events-none opacity-60",
      )}
    >
      <FileUp className="size-6 text-muted-foreground" />
      <div className="text-sm font-medium">
        {busy ? "Importing…" : "Drop SVG / DXF files here"}
      </div>
      <div className="text-xs text-muted-foreground">
        or click to browse — multiple files supported
      </div>
      <input
        ref={inputRef}
        type="file"
        accept={ACCEPTED.join(",")}
        multiple
        hidden
        onChange={(e) => {
          handleFiles(e.target.files);
          e.target.value = ""; // allow re-importing the same file
        }}
      />
    </div>
  );
}
