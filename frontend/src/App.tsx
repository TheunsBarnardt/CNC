import { useCallback, useEffect, useRef, useState } from "react";
import { Download, FilePlus2, FolderOpen, Pencil, PlayCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { CreationSidebar } from "@/components/CreationSidebar";
import type { ActiveTool } from "@/lib/tools";
import { CutSettingsCard } from "@/components/CutSettingsCard";
import { FileListPanel } from "@/components/FileListPanel";
import { NestCard } from "@/components/NestCard";
import { GcodePanel } from "@/components/GcodePanel";
import { ProjectSettingsCard } from "@/components/ProjectSettingsCard";
import { SimulationBar } from "@/components/SimulationBar";
import { DevicePanel } from "@/components/DevicePanel";
import { LayersPanel } from "@/components/LayersPanel";
import { SettingsDialog } from "@/components/SettingsDialog";
import { Viewport } from "@/components/Viewport";
import { buildSimulation, formatTime, type Simulation } from "@/lib/simulation";
import {
  projectApi,
  type FileGeometry,
  type GeometryPath,
  type Layer,
  type Part,
  type ProjectDto,
  type TableSettings,
  type Units,
} from "@/lib/project";
import { loadGuides, saveGuides, type Guide } from "@/lib/guides";
import { useSettings } from "@/lib/settings";

/** Workspace modes (xTool anatomy): edit the layout, or preview the processing. */
type Mode = "edit" | "preview";

const ACCEPTED = [".svg", ".dxf"];

function App() {
  const [project, setProject] = useState<ProjectDto | null>(null);
  const [geometry, setGeometry] = useState<Map<string, GeometryPath[]>>(new Map());
  const [selectedPartId, setSelectedPartId] = useState<string | null>(null);
  const [secondaryPartId, setSecondaryPartId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [importing, setImporting] = useState(false);
  const [importErrors, setImportErrors] = useState<string[]>([]);
  const [mode, setMode] = useState<Mode>("edit");
  const [activeTool, setActiveTool] = useState<ActiveTool>({ type: "select" });
  const [simulation, setSimulation] = useState<Simulation | null>(null);
  const [simTime, setSimTime] = useState(0);
  const openProjectRef = useRef<HTMLInputElement>(null);
  const [guides, setGuides] = useState<Guide[]>(loadGuides);
  const [activeLayerId, setActiveLayerId] = useState<string | null>(null);
  const { settings, update: updateSettings_ } = useSettings();

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

  // Drop selections if parts disappeared (delete/load/new).
  useEffect(() => {
    if (selectedPartId && !project?.parts.some((p) => p.id === selectedPartId))
      setSelectedPartId(null);
    if (secondaryPartId && !project?.parts.some((p) => p.id === secondaryPartId))
      setSecondaryPartId(null);
  }, [project, selectedPartId, secondaryPartId]);

  // Keep active layer in sync with project (default to first layer).
  useEffect(() => {
    if (!project) return;
    if (!activeLayerId || !project.layers.some((l) => l.id === activeLayerId))
      setActiveLayerId(project.layers[0]?.id ?? null);
  }, [project, activeLayerId]);

  const handleGuidesChange = useCallback((next: Guide[]) => {
    setGuides(next);
    saveGuides(next);
  }, []);

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
          scaleX: part.scaleX,
          scaleY: part.scaleY,
        }),
      ),
    [run],
  );

  const handleReorder = useCallback(
    (id: string, direction: "up" | "down" | "front" | "back") =>
      void run(() => projectApi.reorderPart(id, direction)),
    [run],
  );

  const handleOffset = useCallback(
    (id: string, offsetMm: number) =>
      void run(() => projectApi.offsetPart(id, offsetMm), true),
    [run],
  );

  const handleNodesChanged = useCallback(
    (fileId: string, pathId: string, points: [number, number][]) => {
      void run(() => projectApi.updatePathNodes(fileId, pathId, points), true);
    },
    [run],
  );

  const handleSimplify = useCallback(
    (fileId: string) =>
      void run(() => projectApi.simplifyFile(fileId, 0.1), true),
    [run],
  );

  const handleArray = useCallback(
    (id: string, type: "grid" | "circular", params: object) =>
      void run(() =>
        projectApi.arrayPart(id, { type, ...params } as Parameters<typeof projectApi.arrayPart>[1]),
      ),
    [run],
  );

  const handleTestArray = useCallback(
    (id: string, params: object) => {
      const p = params as Parameters<typeof projectApi.testArray>[1];
      projectApi.testArray(id, p)
        .then((blob) => {
          const url = URL.createObjectURL(blob);
          const a = document.createElement("a");
          a.href = url;
          a.download = "test-array.nc";
          a.click();
          URL.revokeObjectURL(url);
        })
        .catch((err: Error) => setError(err.message));
    },
    [],
  );

  const handleBoolean = useCallback(
    (op: "unite" | "subtract" | "intersect") => {
      if (!selectedPartId || !secondaryPartId) return;
      void run(async () => {
        const result = await projectApi.booleanParts(op, [selectedPartId, secondaryPartId]);
        setSelectedPartId(null);
        setSecondaryPartId(null);
        return result;
      }, true);
    },
    [run, selectedPartId, secondaryPartId],
  );

  const handleCreateLayer = useCallback(
    () => void run(() => projectApi.createLayer()),
    [run],
  );

  const handleUpdateLayer = useCallback(
    (id: string, patch: Partial<Layer>) =>
      void run(() => projectApi.updateLayer(id, patch)),
    [run],
  );

  const handleDeleteLayer = useCallback(
    (id: string) => void run(() => projectApi.deleteLayer(id)),
    [run],
  );

  /** Called by Viewport when a drawn shape or text is ready to persist. */
  const handleShapeCreated = useCallback(
    async (paths: GeometryPath[], name: string, worldX: number, worldY: number) => {
      try {
        const prevIds = new Set(project?.parts.map((p) => p.id) ?? []);
        const updated = await projectApi.createSyntheticFile(name, paths, worldX, worldY);
        setProject(updated);
        await refreshGeometry();
        const newPart = updated.parts.find((p) => !prevIds.has(p.id));
        if (newPart) setSelectedPartId(newPart.id);
        setError(null);
      } catch (err) {
        setError((err as Error).message);
      }
    },
    [refreshGeometry, project],
  );

  /** Fetch the toolpath and build the playback timeline. */
  const handleSimulate = useCallback(async (): Promise<Simulation | null> => {
    if (!project) return null;
    const toolpath = await projectApi.generateToolpath();
    const sim = buildSimulation(toolpath, project.table);
    setSimulation(sim);
    return sim;
  }, [project]);

  const hasParts = (project?.parts.length ?? 0) > 0;

  /** Enter/leave processing preview. Entering recomputes the simulation so the
   * estimate always reflects the current layout — purely a computation, no
   * machine involved. */
  const switchMode = (next: Mode) => {
    setMode(next);
    setSelectedPartId(null);
    setActiveTool({ type: "select" });
    setSimulation(null);
    setSimTime(0);
    if (next === "preview" && hasParts) {
      handleSimulate().catch((err: Error) => setError(err.message));
    }
  };

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
            <SettingsDialog settings={settings} onUpdate={updateSettings_} />
            <Separator orientation="vertical" className="h-5" />
            <div className="flex items-center gap-1.5">
              <Button
                variant="ghost"
                size="sm"
                disabled={mode === "preview"}
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
                disabled={mode === "preview"}
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
            <Separator orientation="vertical" className="h-5" />
            {/* The xTool-style "Process" entry point: layout → preview & export. */}
            {mode === "edit" ? (
              <Button size="sm" disabled={!hasParts} onClick={() => switchMode("preview")}>
                <PlayCircle className="size-4" /> Process
              </Button>
            ) : (
              <Button size="sm" variant="secondary" onClick={() => switchMode("edit")}>
                <Pencil className="size-4" /> Back to edit
              </Button>
            )}
          </div>
        </div>
      </header>

      <main className="flex min-h-0 flex-1">
        {/* Left creation rail */}
        <CreationSidebar
          activeTool={activeTool}
          onToolChange={setActiveTool}
          onImport={handleImport}
          busy={importing}
          disabled={mode === "preview"}
        />

        {/* Canvas area */}
        <div className="flex min-w-0 flex-1 flex-col gap-2 p-4">
          {error && (
            <div className="rounded-md border border-destructive/50 bg-destructive/10 px-4 py-2 text-sm text-destructive">
              {error}
            </div>
          )}
          {importErrors.map((msg) => (
            <div
              key={msg}
              className="rounded-md border border-destructive/50 bg-destructive/10 px-4 py-2 text-xs text-destructive"
            >
              {msg}
            </div>
          ))}
          <div
            className="min-h-0 flex-1"
            onDragOver={(e) => mode === "edit" && e.preventDefault()}
            onDrop={(e) => {
              if (mode !== "edit") return;
              e.preventDefault();
              const files = Array.from(e.dataTransfer.files).filter((f) =>
                ACCEPTED.some((ext) => f.name.toLowerCase().endsWith(ext)),
              );
              if (files.length > 0) void handleImport(files);
            }}
          >
            {project && (
              <Viewport
                project={project}
                geometry={geometry}
                selectedPartId={selectedPartId}
                secondaryPartId={secondaryPartId}
                onSelect={setSelectedPartId}
                onSecondarySelect={setSecondaryPartId}
                onPartChange={handlePartChange}
                onPartCommit={handlePartCommit}
                onDuplicate={(id) => void run(() => projectApi.duplicatePart(id))}
                onDelete={(id) => void run(() => projectApi.deletePart(id))}
                onReorder={handleReorder}
                onOffset={handleOffset}
                onNodesChanged={handleNodesChanged}
                onSimplify={handleSimplify}
                onBoolean={handleBoolean}
                onArray={handleArray}
                onTestArray={handleTestArray}
                guides={guides}
                onGuidesChange={handleGuidesChange}
                layers={project?.layers}
                settings={settings}
                simulation={mode === "preview" ? simulation : null}
                simTime={simTime}
                readOnly={mode === "preview"}
                activeTool={activeTool}
                onShapeCreated={handleShapeCreated}
                onToolReset={() => setActiveTool({ type: "select" })}
              />
            )}
          </div>
          {mode === "preview" && (
            <SimulationBar
              simulation={simulation}
              simTime={simTime}
              onSimTimeChange={setSimTime}
              onGenerate={handleSimulate}
              onClose={() => switchMode("edit")}
              hasParts={hasParts}
            />
          )}
        </div>

        {/* Right panel: device status first, then processing parameters */}
        <aside className="flex w-[330px] shrink-0 flex-col gap-4 overflow-y-auto border-l p-4">
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-base">Device</CardTitle>
              <CardDescription>GRBL serial connection</CardDescription>
            </CardHeader>
            <CardContent>
              <DevicePanel />
            </CardContent>
          </Card>

          {mode === "edit" ? (
            <>
              <Card>
                <CardHeader className="pb-3">
                  <CardTitle className="text-base">Files</CardTitle>
                  <CardDescription>
                    {project?.files.length
                      ? `${project.files.length} file${project.files.length === 1 ? "" : "s"}, ${project?.parts.length} part${project?.parts.length === 1 ? "" : "s"} placed`
                      : "Import via the left rail or drop SVG/DXF onto the canvas"}
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
                  <CardTitle className="text-base">Layers</CardTitle>
                  <CardDescription>Visibility and lock per group</CardDescription>
                </CardHeader>
                <CardContent>
                  {project && (
                    <LayersPanel
                      layers={project.layers}
                      activeLayerId={activeLayerId}
                      onSetActive={setActiveLayerId}
                      onCreate={handleCreateLayer}
                      onUpdate={handleUpdateLayer}
                      onDelete={handleDeleteLayer}
                    />
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader className="pb-3">
                  <CardTitle className="text-base">Arrange</CardTitle>
                  <CardDescription>
                    Auto-nest parts to save material
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <NestCard hasParts={hasParts} onNested={setProject} />
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
                  <CardTitle className="text-base">Cut settings</CardTitle>
                  <CardDescription>
                    Plasma parameters &amp; material profiles
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  {project && (
                    <CutSettingsCard
                      materialThicknessMm={project.table.materialThicknessMm}
                    />
                  )}
                </CardContent>
              </Card>
            </>
          ) : (
            <>
              <Card>
                <CardHeader className="pb-3">
                  <CardTitle className="text-base">Processing</CardTitle>
                  <CardDescription>Estimated from the simulated toolpath</CardDescription>
                </CardHeader>
                <CardContent>
                  {simulation ? (
                    <dl className="grid grid-cols-2 gap-x-3 gap-y-1.5 text-xs">
                      <dt className="text-muted-foreground">Estimated time</dt>
                      <dd className="text-right font-mono">{formatTime(simulation.totalS)}</dd>
                      <dt className="text-muted-foreground">Cuts (pierces)</dt>
                      <dd className="text-right font-mono">{simulation.cutCount}</dd>
                      <dt className="text-muted-foreground">Cut length</dt>
                      <dd className="text-right font-mono">
                        {simulation.totalCutLengthMm.toFixed(0)} mm
                      </dd>
                    </dl>
                  ) : (
                    <p className="text-xs text-muted-foreground">
                      {hasParts ? "Simulating…" : "Place parts on the table first."}
                    </p>
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
                  <GcodePanel hasParts={hasParts} />
                </CardContent>
              </Card>
            </>
          )}
        </aside>
      </main>
    </div>
  );
}

export default App;
