import { useCallback, useEffect, useState } from "react";
import { BookTemplate, Plus, Trash2, FolderOpen } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { templateApi, type ProjectDto, type TemplateInfo } from "@/lib/project";

interface Props {
  onLoad: (project: ProjectDto) => void;
}

export function TemplateDialog({ onLoad }: Props) {
  const [open, setOpen] = useState(false);
  const [templates, setTemplates] = useState<TemplateInfo[]>([]);
  const [saveName, setSaveName] = useState("");
  const [saveDesc, setSaveDesc] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const refresh = useCallback(async () => {
    try {
      setTemplates(await templateApi.list());
    } catch (err) {
      setError((err as Error).message);
    }
  }, []);

  useEffect(() => {
    if (open) void refresh();
  }, [open, refresh]);

  const handleSave = async () => {
    if (!saveName.trim()) return;
    setLoading(true);
    try {
      await templateApi.save(saveName.trim(), saveDesc.trim());
      setSaveName("");
      setSaveDesc("");
      setError(null);
      await refresh();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  };

  const handleLoad = async (id: string) => {
    setLoading(true);
    try {
      const project = await templateApi.load(id);
      onLoad(project);
      setOpen(false);
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await templateApi.delete(id);
      await refresh();
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="ghost" size="sm" title="Project templates">
          <BookTemplate className="size-4" /> Templates
        </Button>
      </DialogTrigger>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Project Templates</DialogTitle>
        </DialogHeader>

        {/* Save current project as template */}
        <div className="space-y-2 rounded-md border p-3">
          <p className="text-xs font-medium text-muted-foreground">Save current project as template</p>
          <div className="grid gap-1.5">
            <Label className="text-xs" htmlFor="tpl-name">Name</Label>
            <Input
              id="tpl-name"
              className="h-8 text-xs"
              placeholder="My template…"
              value={saveName}
              onChange={(e) => setSaveName(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && void handleSave()}
            />
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs" htmlFor="tpl-desc">Description (optional)</Label>
            <Input
              id="tpl-desc"
              className="h-8 text-xs"
              placeholder="What this template is for…"
              value={saveDesc}
              onChange={(e) => setSaveDesc(e.target.value)}
            />
          </div>
          <Button
            size="sm"
            className="h-7 text-xs"
            disabled={!saveName.trim() || loading}
            onClick={() => void handleSave()}
          >
            <Plus className="size-3" /> Save template
          </Button>
        </div>

        {/* Template list */}
        <div className="space-y-1">
          <p className="text-xs font-medium text-muted-foreground">Saved templates</p>
          {templates.length === 0 ? (
            <p className="py-2 text-center text-xs text-muted-foreground">
              No templates yet. Save the current project above.
            </p>
          ) : (
            <div className="max-h-64 space-y-1 overflow-y-auto">
              {templates.map((t) => (
                <div
                  key={t.id}
                  className="flex items-center gap-2 rounded px-2 py-1.5 hover:bg-accent"
                >
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium">{t.name}</p>
                    <p className="text-[10px] text-muted-foreground">
                      {t.partCount} part{t.partCount !== 1 ? "s" : ""} ·{" "}
                      {new Date(t.createdAt).toLocaleDateString()}
                      {t.description && ` · ${t.description}`}
                    </p>
                  </div>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="size-7 shrink-0"
                    title="Load template"
                    disabled={loading}
                    onClick={() => void handleLoad(t.id)}
                  >
                    <FolderOpen className="size-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="size-7 shrink-0 text-destructive"
                    title="Delete template"
                    onClick={() => void handleDelete(t.id)}
                  >
                    <Trash2 className="size-3" />
                  </Button>
                </div>
              ))}
            </div>
          )}
        </div>

        {error && (
          <div className="rounded-md border border-destructive/50 bg-destructive/10 px-3 py-1.5 text-xs text-destructive">
            {error}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
