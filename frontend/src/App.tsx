import { useCallback, useEffect, useRef, useState } from "react";
import { Download, FolderOpen, FilePlus2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { ImportDropzone } from "@/components/ImportDropzone";
import { FileListPanel } from "@/components/FileListPanel";
import { GcodePanel } from "@/components/GcodePanel";
import { ProjectSettingsCard } from "@/components/ProjectSettingsCard";
import { StatusBar } from "@/components/StatusBar";
import { Viewport } from "@/components/Viewport";
import {
  projectApi,
  type FileGeometry,
  type GeometryPath,
  type Part,
  type ProjectDto,
  type TableSettings,
  type Units,
} from "@/lib/project";

function App() {
  const [project, setProject] = useState<ProjectDto | null>(null);
  const [geometry, setGeometry] = useState<Map<string, GeometryPath[]>>(new Map());
  const [selectedPartId, setSelectedPartId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [importing, setImporting] = useState(false);
  const [importErrors, setImportErrors] = useState<string[]>([]);
  const openProjectRef = useRef<HTMLInputElement>(null);

  const applyGeometry = (files: FileGeometry[]) =>
    setGeometry(new Map(files.map((f) => [f.fileId, f.paths])));

  const refreshGeometry = useCallback(async () => {
    applyGeometry((await projectApi.getGeometry()).files);
  }, []);

  /** Run a project-mutating call; geometry is refetched when file set changes. */
  const run = useCallback(
    async (action: () => Promise<ProjectDto>, reloadGeometry = false) => {
      try {
        const updated = await action();
        setProject(updated);
        if (reloadGeometry) await refreshGeometry();
        setError(null);
        return updated;
      } catch (err) {
        setError((err as Error).message);
        return null;
      }
    },
    [refreshGeometry],
  );

  useEffect(() => {
    void run(projectApi.get, true);
  }, [run]);

  // Drop selection if the selected part disappeared (delete/load/new).
  useEffect(() => {
    if (selectedPartId && !project?.parts.some((p) => p.id === selectedPartId))
      setSelectedPartId(null);
  }, [project, selectedPartId]);

  const handleImport = useCallback(async (files: File[]) => {
    setImporting(true);
    setImportErrors([]);
    try {
      const { results, project: updated } = await projectApi.importFiles(files);
      setProject(updated);
      await refreshGeometry();
      setImportErrors(
        results.filter((r) => !r.ok).map((r) => `${r.fileName}: ${r.error}`),
      );
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setImporting(false);
    }
  }, [refreshGeometry]);

  const updateSettings = useCallback(
    (settings: { name?: string; units?: Units; table?: TableSettings }) =>
      void run(() => projectApi.updateSettings(settings)),
    [run],
  );

  /** Optimistic local update while dragging in the viewport. */
  const handlePartChange = useCallback((part: Part) => {
    setProject((p) =>
      p ? { ...p, parts: p.parts.map((x) => (x.id === part.id ? part : x)) } : p,
    );
  }, []);

  const handlePartCommit = useCallback(
    (part: Part) =>
      void run(() =>
        projectApi.updatePart(part.id, {
          x: part.x,
          y: part.y,
          rotationDeg: part.rotationDeg,
        }),
      ),
    [run],
  );

  return (
    <div className="flex h-screen flex-col bg-background text-foreground">
      <header className="shrink-0 border-b">
        <div className="flex items-center justify-between gap-4 px-4 py-2.5">
          <div className="flex items-baseline gap-3">
            <h1 className="text-lg font-semibold tracking-tight">
              DIY GRBL Cutting CAM
            </h1>
            {project && (
              <span className="text-sm text-muted-foreground">{project.name}</span>
            )}
          </div>
          <div className="flex items-center gap-4">
            <StatusBar />
            <Separator orientation="vertical" className="h-5" />
            <div className="flex items-center gap-1.5">
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  if (
                    !project ||
                    project.files.length === 0 ||
                    confirm("Start a new project? Unsaved changes are lost.")
                  )
                    void run(projectApi.newProject, true);
                }}
              >
                <FilePlus2 className="size-4" /> New
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => openProjectRef.current?.click()}
              >
                <FolderOpen className="size-4" /> Open
              </Button>
              <Button variant="ghost" size="sm" asChild>
                <a href={projectApi.exportUrl} download>
                  <Download className="size-4" /> Save
                </a>
              </Button>
              <input
                ref={openProjectRef}
                type="file"
                accept=".json,.grblcam.json"
                hidden
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  if (file) void run(() => projectApi.loadProject(file), true);
                  e.target.value = "";
                }}
              />
            </div>
          </div>
        </div>
      </header>

      <main className="flex min-h-0 flex-1 gap-4 p-4">
        {/* Viewport — the main work area */}
        <div className="flex min-w-0 flex-1 flex-col gap-2">
          {error && (
            <div className="rounded-md border border-destructive/50 bg-destructive/10 px-4 py-2 text-sm text-destructive">
              {error}
            </div>
          )}
          {project && (
            <Viewport
              project={project}
              geometry={geometry}
              selectedPartId={selectedPartId}
              onSelect={setSelectedPartId}
              onPartChange={handlePartChange}
              onPartCommit={handlePartCommit}
              onDuplicate={(id) => void run(() => projectApi.duplicatePart(id))}
              onDelete={(id) => void run(() => projectApi.deletePart(id))}
            />
          )}
        </div>

        {/* Sidebar */}
        <aside className="flex w-[330px] shrink-0 flex-col gap-4 overflow-y-auto">
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-base">Import</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              <ImportDropzone onImport={handleImport} busy={importing} />
              {importErrors.map((msg) => (
                <div
                  key={msg}
                  className="rounded-md border border-destructive/50 bg-destructive/10 px-3 py-1.5 text-xs text-destructive"
                >
                  {msg}
                </div>
              ))}
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-base">Files</CardTitle>
              <CardDescription>
                {project?.files.length
                  ? `${project.files.length} file${project.files.length === 1 ? "" : "s"}, ${project?.parts.length} part${project?.parts.length === 1 ? "" : "s"} placed`
                  : "Imported files appear here"}
              </CardDescription>
            </CardHeader>
            <CardContent>
              {project && (
                <FileListPanel
                  files={project.files}
                  units={project.units}
                  onToggleVisible={(id, visible) =>
                    void run(() => projectApi.updateFile(id, { visible }))
                  }
                  onRename={(id, displayName) =>
                    void run(() => projectApi.updateFile(id, { displayName }))
                  }
                  onDelete={(id) => void run(() => projectApi.deleteFile(id), true)}
                  onAddToTable={(id) => void run(() => projectApi.createPart(id))}
                />
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-base">Table &amp; sheet</CardTitle>
            </CardHeader>
            <CardContent>
              {project && (
                <ProjectSettingsCard project={project} onUpdate={updateSettings} />
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-base">G-code</CardTitle>
              <CardDescription>
                Toolpath → GRBL via the selected post-processor
              </CardDescription>
            </CardHeader>
            <CardContent>
              <GcodePanel hasParts={(project?.parts.length ?? 0) > 0} />
            </CardContent>
          </Card>
        </aside>
      </main>
    </div>
  );
}

export default App;
