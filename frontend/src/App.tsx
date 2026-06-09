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
import { ProjectSettingsCard } from "@/components/ProjectSettingsCard";
import { StatusBar } from "@/components/StatusBar";
import { projectApi, type ProjectDto, type Units, type TableSettings } from "@/lib/project";

function App() {
  const [project, setProject] = useState<ProjectDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [importing, setImporting] = useState(false);
  const [importErrors, setImportErrors] = useState<string[]>([]);
  const openProjectRef = useRef<HTMLInputElement>(null);

  const run = useCallback(async (action: () => Promise<ProjectDto>) => {
    try {
      setProject(await action());
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  }, []);

  useEffect(() => {
    void run(projectApi.get);
  }, [run]);

  const handleImport = useCallback(
    async (files: File[]) => {
      setImporting(true);
      setImportErrors([]);
      try {
        const { results, project: updated } = await projectApi.importFiles(files);
        setProject(updated);
        setImportErrors(
          results.filter((r) => !r.ok).map((r) => `${r.fileName}: ${r.error}`),
        );
        setError(null);
      } catch (err) {
        setError((err as Error).message);
      } finally {
        setImporting(false);
      }
    },
    [],
  );

  const updateSettings = useCallback(
    (settings: { name?: string; units?: Units; table?: TableSettings }) =>
      run(() => projectApi.updateSettings(settings)),
    [run],
  );

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-6 py-3">
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
                    void run(projectApi.newProject);
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
                  if (file) void run(() => projectApi.loadProject(file));
                  e.target.value = "";
                }}
              />
            </div>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-5xl space-y-6 px-6 py-6">
        {error && (
          <div className="rounded-md border border-destructive/50 bg-destructive/10 px-4 py-2 text-sm text-destructive">
            {error}
          </div>
        )}

        <div className="grid gap-6 md:grid-cols-[1fr_minmax(280px,340px)]">
          <div className="space-y-6">
            <Card>
              <CardHeader>
                <CardTitle>Import</CardTitle>
                <CardDescription>
                  Vector artwork (SVG, DXF) — parsed into cutting geometry.
                </CardDescription>
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
              <CardHeader>
                <CardTitle>Files</CardTitle>
                <CardDescription>
                  {project?.files.length
                    ? `${project.files.length} imported file${project.files.length === 1 ? "" : "s"}`
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
                    onDelete={(id) => void run(() => projectApi.deleteFile(id))}
                  />
                )}
              </CardContent>
            </Card>
          </div>

          <Card className="h-fit">
            <CardHeader>
              <CardTitle>Table & sheet</CardTitle>
              <CardDescription>Machine bed and material setup.</CardDescription>
            </CardHeader>
            <CardContent>
              {project && (
                <ProjectSettingsCard project={project} onUpdate={updateSettings} />
              )}
            </CardContent>
          </Card>
        </div>

        <p className="text-xs text-muted-foreground">
          Task 1 — import &amp; project foundation. The table viewport arrives in Task 2.
        </p>
      </main>
    </div>
  );
}

export default App;
