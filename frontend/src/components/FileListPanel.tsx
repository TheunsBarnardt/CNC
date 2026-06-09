import { useState } from "react";
import { AlertTriangle, Check, Eye, EyeOff, Pencil, Trash2, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  displayLength,
  unitSuffix,
  type FileSummary,
  type Units,
} from "@/lib/project";

interface Props {
  files: FileSummary[];
  units: Units;
  onToggleVisible: (id: string, visible: boolean) => void;
  onRename: (id: string, displayName: string) => void;
  onDelete: (id: string) => void;
}

/** Imported-files panel: visibility, rename, delete + parsed-geometry summary. */
export function FileListPanel({ files, units, onToggleVisible, onRename, onDelete }: Props) {
  if (files.length === 0) {
    return (
      <p className="py-6 text-center text-sm text-muted-foreground">
        No files imported yet.
      </p>
    );
  }

  return (
    <ul className="divide-y">
      {files.map((file) => (
        <FileRow
          key={file.id}
          file={file}
          units={units}
          onToggleVisible={onToggleVisible}
          onRename={onRename}
          onDelete={onDelete}
        />
      ))}
    </ul>
  );
}

function FileRow({
  file,
  units,
  onToggleVisible,
  onRename,
  onDelete,
}: { file: FileSummary } & Omit<Props, "files">) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(file.displayName);

  const commitRename = () => {
    setEditing(false);
    const name = draft.trim();
    if (name && name !== file.displayName) onRename(file.id, name);
    else setDraft(file.displayName);
  };

  const suffix = unitSuffix(units);

  return (
    <li className="space-y-1.5 py-3 first:pt-0 last:pb-0">
      <div className="flex items-center gap-2">
        <Button
          variant="ghost"
          size="icon"
          className="size-7 shrink-0"
          aria-label={file.visible ? "Hide file" : "Show file"}
          onClick={() => onToggleVisible(file.id, !file.visible)}
        >
          {file.visible ? <Eye className="size-4" /> : <EyeOff className="size-4 text-muted-foreground" />}
        </Button>

        {editing ? (
          <div className="flex flex-1 items-center gap-1">
            <Input
              autoFocus
              value={draft}
              className="h-7"
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") commitRename();
                if (e.key === "Escape") {
                  setDraft(file.displayName);
                  setEditing(false);
                }
              }}
            />
            <Button variant="ghost" size="icon" className="size-7" onClick={commitRename} aria-label="Save name">
              <Check className="size-4" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              className="size-7"
              aria-label="Cancel rename"
              onClick={() => {
                setDraft(file.displayName);
                setEditing(false);
              }}
            >
              <X className="size-4" />
            </Button>
          </div>
        ) : (
          <>
            <span
              className={
                "flex-1 truncate text-sm font-medium" +
                (file.visible ? "" : " text-muted-foreground line-through")
              }
              title={file.fileName}
            >
              {file.displayName}
            </span>
            <Badge variant="outline" className="shrink-0 uppercase">
              {file.kind}
            </Badge>
            <Button
              variant="ghost"
              size="icon"
              className="size-7 shrink-0"
              aria-label="Rename file"
              onClick={() => setEditing(true)}
            >
              <Pencil className="size-4" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              className="size-7 shrink-0 text-destructive"
              aria-label="Delete file"
              onClick={() => onDelete(file.id)}
            >
              <Trash2 className="size-4" />
            </Button>
          </>
        )}
      </div>

      {/* Parsed-entity summary — the Task 1 "prove parsing works" view. */}
      <div className="pl-9 text-xs text-muted-foreground">
        {file.pathCount} path{file.pathCount === 1 ? "" : "s"} ({file.closedPathCount} closed,{" "}
        {file.openPathCount} open) · {file.totalPoints.toLocaleString()} points ·{" "}
        {displayLength(file.widthMm, units)} × {displayLength(file.heightMm, units)} {suffix}
        {file.layers.length > 0 && <> · layers: {file.layers.join(", ")}</>}
      </div>

      {file.warnings.map((warning) => (
        <div key={warning} className="flex items-start gap-1.5 pl-9 text-xs text-amber-600 dark:text-amber-500">
          <AlertTriangle className="mt-0.5 size-3 shrink-0" />
          {warning}
        </div>
      ))}
    </li>
  );
}
