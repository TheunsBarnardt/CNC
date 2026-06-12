import { useCallback, useEffect, useState } from "react";
import { Library, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { libraryApi, type ElementInfo, type FileSummary, type ProjectDto } from "@/lib/project";

interface Props {
  /** Files in the current project — user can save one as a library element. */
  files: FileSummary[];
  onInsert: (project: ProjectDto) => void;
  disabled?: boolean;
}

export function LibraryPanel({ files, onInsert, disabled = false }: Props) {
  const [open, setOpen] = useState(false);
  const [elements, setElements] = useState<ElementInfo[]>([]);
  const [saveName, setSaveName] = useState("");
  const [saveFileId, setSaveFileId] = useState("");
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      setElements(await libraryApi.list());
    } catch (err) {
      setError((err as Error).message);
    }
  }, []);

  useEffect(() => {
    if (open) void refresh();
  }, [open, refresh]);

  const handleSave = async () => {
    if (!saveName.trim() || !saveFileId) return;
    try {
      await libraryApi.save(saveName.trim(), saveFileId);
      setSaveName("");
      setError(null);
      await refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const handleInsert = async (id: string) => {
    try {
      const project = await libraryApi.insert(id);
      onInsert(project);
      setOpen(false);
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await libraryApi.delete(id);
      await refresh();
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="size-9"
          title="Element library — save & reuse shapes"
          disabled={disabled}
        >
          <Library className="size-5" />
        </Button>
      </PopoverTrigger>
      <PopoverContent side="right" align="start" className="w-72 p-3">
        <div className="mb-2 text-xs font-semibold">Element Library</div>

        {/* Save a file as a library element */}
        {files.length > 0 && (
          <div className="mb-3 space-y-1.5 rounded border p-2">
            <p className="text-[10px] text-muted-foreground">Save element from current project</p>
            <select
              className="h-7 w-full rounded border bg-background px-1 text-xs"
              value={saveFileId}
              onChange={(e) => setSaveFileId(e.target.value)}
            >
              <option value="">Pick a file…</option>
              {files.map((f) => (
                <option key={f.id} value={f.id}>{f.displayName}</option>
              ))}
            </select>
            <div className="flex gap-1">
              <Input
                className="h-7 flex-1 text-xs"
                placeholder="Element name…"
                value={saveName}
                onChange={(e) => setSaveName(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && void handleSave()}
              />
              <Button
                variant="outline"
                size="icon"
                className="size-7 shrink-0"
                disabled={!saveName.trim() || !saveFileId}
                onClick={() => void handleSave()}
                title="Save to library"
              >
                <Plus className="size-3" />
              </Button>
            </div>
          </div>
        )}

        {/* Library elements list */}
        {elements.length === 0 ? (
          <p className="py-2 text-center text-[10px] text-muted-foreground">
            No saved elements. Save a shape from your project above.
          </p>
        ) : (
          <div className="max-h-56 space-y-0.5 overflow-y-auto">
            {elements.map((el) => (
              <div
                key={el.id}
                className="group flex items-center gap-1.5 rounded px-1.5 py-1 hover:bg-accent"
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate text-xs font-medium">{el.name}</p>
                  <p className="text-[10px] text-muted-foreground">
                    {el.widthMm.toFixed(0)}×{el.heightMm.toFixed(0)}mm · {el.pathCount} path{el.pathCount !== 1 ? "s" : ""}
                  </p>
                </div>
                <Button
                  variant="ghost"
                  size="icon"
                  className="size-6 shrink-0 opacity-0 group-hover:opacity-100"
                  title="Insert into canvas"
                  onClick={() => void handleInsert(el.id)}
                >
                  <Plus className="size-3" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  className="size-6 shrink-0 text-destructive opacity-0 group-hover:opacity-100"
                  title="Delete from library"
                  onClick={() => void handleDelete(el.id)}
                >
                  <Trash2 className="size-3" />
                </Button>
              </div>
            ))}
          </div>
        )}

        {error && (
          <p className="mt-1 text-[10px] text-destructive">{error}</p>
        )}
      </PopoverContent>
    </Popover>
  );
}
