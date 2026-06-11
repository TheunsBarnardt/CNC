import { useRef, useState } from "react";
import { Eye, EyeOff, Lock, LockOpen, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import type { Layer } from "@/lib/project";

interface Props {
  layers: Layer[];
  activeLayerId: string | null;
  onSetActive: (id: string) => void;
  onCreate: () => void;
  onUpdate: (id: string, patch: Partial<Layer>) => void;
  onDelete: (id: string) => void;
}

export function LayersPanel({
  layers,
  activeLayerId,
  onSetActive,
  onCreate,
  onUpdate,
  onDelete,
}: Props) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draftName, setDraftName] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  const commitRename = (id: string) => {
    if (draftName.trim()) onUpdate(id, { name: draftName.trim() });
    setEditingId(null);
  };

  return (
    <div className="flex flex-col gap-1">
      {/* Layer rows — top = front of stacking order */}
      {[...layers].reverse().map((layer) => {
        const isActive = layer.id === activeLayerId;
        return (
          <div
            key={layer.id}
            className={`group flex items-center gap-1.5 rounded px-1.5 py-1 text-sm ${
              isActive ? "bg-accent" : "hover:bg-accent/50"
            }`}
            onClick={() => onSetActive(layer.id)}
          >
            {/* Color swatch / picker */}
            <label className="relative cursor-pointer" title="Layer color" onClick={(e) => e.stopPropagation()}>
              <span
                className="block h-3.5 w-3.5 rounded-sm border border-border"
                style={{ background: layer.color }}
              />
              <input
                type="color"
                className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
                value={layer.color}
                onChange={(e) => onUpdate(layer.id, { color: e.target.value })}
              />
            </label>

            {/* Name — double-click to rename */}
            {editingId === layer.id ? (
              <input
                ref={inputRef}
                className="h-5 flex-1 rounded border border-primary bg-background px-1 text-xs outline-none"
                value={draftName}
                onChange={(e) => setDraftName(e.target.value)}
                onBlur={() => commitRename(layer.id)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") commitRename(layer.id);
                  if (e.key === "Escape") setEditingId(null);
                }}
                autoFocus
                onClick={(e) => e.stopPropagation()}
              />
            ) : (
              <span
                className="flex-1 cursor-pointer truncate text-xs"
                onDoubleClick={(e) => {
                  e.stopPropagation();
                  setEditingId(layer.id);
                  setDraftName(layer.name);
                  setTimeout(() => inputRef.current?.select(), 0);
                }}
                title="Double-click to rename"
              >
                {layer.name}
              </span>
            )}

            {/* Visibility */}
            <Button
              variant="ghost"
              size="icon"
              className="size-6 opacity-0 group-hover:opacity-100"
              title={layer.visible ? "Hide layer" : "Show layer"}
              onClick={(e) => {
                e.stopPropagation();
                onUpdate(layer.id, { visible: !layer.visible });
              }}
            >
              {layer.visible ? <Eye className="size-3" /> : <EyeOff className="size-3 text-muted-foreground" />}
            </Button>

            {/* Lock */}
            <Button
              variant="ghost"
              size="icon"
              className="size-6 opacity-0 group-hover:opacity-100"
              title={layer.locked ? "Unlock layer" : "Lock layer"}
              onClick={(e) => {
                e.stopPropagation();
                onUpdate(layer.id, { locked: !layer.locked });
              }}
            >
              {layer.locked
                ? <Lock className="size-3 text-muted-foreground" />
                : <LockOpen className="size-3" />}
            </Button>

            {/* Delete (only if more than one layer) */}
            {layers.length > 1 && (
              <Button
                variant="ghost"
                size="icon"
                className="size-6 opacity-0 group-hover:opacity-100 text-destructive"
                title="Delete layer"
                onClick={(e) => {
                  e.stopPropagation();
                  onDelete(layer.id);
                }}
              >
                <Trash2 className="size-3" />
              </Button>
            )}
          </div>
        );
      })}

      <Button
        variant="ghost"
        size="sm"
        className="mt-1 h-7 justify-start gap-1.5 text-xs text-muted-foreground"
        onClick={onCreate}
      >
        <Plus className="size-3" /> Add layer
      </Button>
    </div>
  );
}
