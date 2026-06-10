import { useCallback, useEffect, useState } from "react";
import {
  Cable,
  Unplug,
  RefreshCw,
  Loader2,
  Home,
  CrosshairIcon,
  Play,
  Pause,
  Square,
  OctagonX,
  RotateCcw,
  ChevronUp,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ArrowUp,
  ArrowDown,
  Unlock,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Progress } from "@/components/ui/progress";
import { cn } from "@/lib/utils";
import { machineApi, type ConnectionInfo } from "@/lib/machineApi";
import { useMachineHeartbeat } from "@/hooks/useMachineHeartbeat";
import { JobLogPanel } from "@/components/JobLogPanel";

const BAUD_OPTIONS = [115200, 57600, 38400, 19200, 9600];
const JOG_STEPS = [0.1, 1, 10, 100];
const JOG_FEED = 1000; // mm/min default

export function DevicePanel() {
  const { status } = useMachineHeartbeat();

  const [ports, setPorts] = useState<string[]>([]);
  const [selectedPort, setSelectedPort] = useState<string>("");
  const [selectedBaud, setSelectedBaud] = useState(115200);
  const [connInfo, setConnInfo] = useState<ConnectionInfo | null>(null);
  const [busy, setBusy] = useState(false);
  const [jogStep, setJogStep] = useState(1);
  const [error, setError] = useState<string | null>(null);

  const loadPorts = useCallback(async () => {
    try {
      const list = await machineApi.ports();
      setPorts(list);
      if (list.length > 0 && !selectedPort) setSelectedPort(list[0]);
    } catch { /* ignore */ }
  }, [selectedPort]);

  const loadConnection = useCallback(async () => {
    try {
      const info = await machineApi.connection();
      setConnInfo(info);
      if (info.isConnected && info.port) {
        setSelectedPort(info.port);
        setSelectedBaud(info.baud ?? 115200);
      }
    } catch { /* ignore */ }
  }, []);

  useEffect(() => {
    void loadPorts();
    void loadConnection();
  }, [loadPorts, loadConnection]);

  const act = async (fn: () => Promise<unknown>, reloadConn = false) => {
    setBusy(true);
    setError(null);
    try {
      await fn();
      if (reloadConn) await loadConnection();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const jog = (axis: string, sign: 1 | -1) =>
    void act(() => machineApi.jog(axis, sign * jogStep, JOG_FEED));

  const isConnected = connInfo?.isConnected ?? false;

  // Derive job-running state from live SignalR status (always up-to-date)
  // rather than from the stale REST response.
  const isJobRunning = status?.jobTotal != null;

  const machineState = status?.machineState ?? "—";
  const isAlarm = machineState.toLowerCase().startsWith("alarm");
  const isHold  = machineState.toLowerCase().startsWith("hold");
  const isRun   = machineState.toLowerCase() === "run";

  const stateColor = isAlarm
    ? "text-destructive"
    : isRun
      ? "text-blue-500"
      : machineState === "Idle"
        ? "text-emerald-500"
        : "text-muted-foreground";

  const jobPct =
    status?.jobTotal && status.jobDone != null
      ? Math.round((status.jobDone / status.jobTotal) * 100)
      : null;

  return (
    <div className="grid gap-3">
      {/* ── Connection status chip ─────────────────────── */}
      <div className="flex items-center gap-2">
        <span
          className={cn(
            "size-2 shrink-0 rounded-full",
            isConnected ? "bg-emerald-500" : "bg-muted-foreground/40",
          )}
        />
        <span className="text-xs text-muted-foreground">
          {isConnected
            ? `${connInfo?.port} · ${connInfo?.baud?.toLocaleString()} baud`
            : "Not connected"}
        </span>
      </div>

      {/* ── DRO ───────────────────────────────────────── */}
      {isConnected && status && (
        <div className="rounded-md border bg-muted/30 px-3 py-2">
          <div className="mb-1.5 flex items-center gap-2">
            <span className={cn("text-xs font-semibold", stateColor)}>
              {machineState}
            </span>
            {jobPct != null && (
              <span className="ml-auto text-xs text-muted-foreground">
                {status.jobDone}/{status.jobTotal} lines
              </span>
            )}
          </div>
          {jobPct != null && (
            <Progress value={jobPct} className="mb-2 h-1.5" />
          )}
          <div className="grid grid-cols-3 gap-x-2 font-mono text-xs">
            {(["X", "Y", "Z"] as const).map((ax) => (
              <div key={ax} className="flex flex-col">
                <span className="text-muted-foreground">{ax}</span>
                <span>{(status[ax.toLowerCase() as "x" | "y" | "z"]).toFixed(3)}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Alarm unlock ──────────────────────────────── */}
      {isConnected && isAlarm && (
        <Button
          size="sm"
          variant="outline"
          className="border-amber-500/50 text-amber-600"
          onClick={() => void act(() => machineApi.unlock())}
          disabled={busy}
        >
          <Unlock className="size-4" />
          Unlock ($X)
        </Button>
      )}

      {/* ── Connection controls (disconnected) ────────── */}
      {!isConnected && (
        <div className="grid gap-2">
          <div className="flex items-end gap-1">
            <div className="flex-1">
              <Label className="text-xs">Port</Label>
              <Select value={selectedPort} onValueChange={setSelectedPort}>
                <SelectTrigger className="mt-1 h-8 text-xs">
                  <SelectValue placeholder="Select port…" />
                </SelectTrigger>
                <SelectContent>
                  {ports.map((p) => (
                    <SelectItem key={p} value={p}>{p}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <Button
              variant="ghost"
              size="icon"
              className="h-8 w-8 shrink-0"
              onClick={() => void loadPorts()}
              title="Refresh ports"
            >
              <RefreshCw className="size-3.5" />
            </Button>
          </div>
          <div>
            <Label className="text-xs">Baud rate</Label>
            <Select
              value={String(selectedBaud)}
              onValueChange={(v) => setSelectedBaud(Number(v))}
            >
              <SelectTrigger className="mt-1 h-8 text-xs">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {BAUD_OPTIONS.map((b) => (
                  <SelectItem key={b} value={String(b)}>
                    {b.toLocaleString()}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button
            size="sm"
            onClick={() => void act(() => machineApi.connect(selectedPort, selectedBaud), true)}
            disabled={busy || !selectedPort}
          >
            {busy ? <Loader2 className="size-4 animate-spin" /> : <Cable className="size-4" />}
            Connect
          </Button>
        </div>
      )}

      {/* ── Jog controls (connected, no active job) ───── */}
      {isConnected && !isJobRunning && !isAlarm && (
        <div className="grid gap-2">
          {/* Step size */}
          <div className="flex items-center gap-1">
            <span className="text-xs text-muted-foreground">Step (mm)</span>
            <div className="ml-auto flex gap-1">
              {JOG_STEPS.map((s) => (
                <Button
                  key={s}
                  size="sm"
                  variant={jogStep === s ? "default" : "outline"}
                  className="h-6 px-2 text-xs"
                  onClick={() => setJogStep(s)}
                >
                  {s}
                </Button>
              ))}
            </div>
          </div>

          {/* XY grid */}
          <div className="grid grid-cols-3 gap-1">
            <div />
            <Button size="icon" variant="outline" className="h-8 w-full"
              onClick={() => jog("Y", 1)} disabled={busy}>
              <ChevronUp className="size-4" />
            </Button>
            <div />
            <Button size="icon" variant="outline" className="h-8 w-full"
              onClick={() => jog("X", -1)} disabled={busy}>
              <ChevronLeft className="size-4" />
            </Button>
            <div className="flex items-center justify-center text-xs text-muted-foreground">XY</div>
            <Button size="icon" variant="outline" className="h-8 w-full"
              onClick={() => jog("X", 1)} disabled={busy}>
              <ChevronRight className="size-4" />
            </Button>
            <div />
            <Button size="icon" variant="outline" className="h-8 w-full"
              onClick={() => jog("Y", -1)} disabled={busy}>
              <ChevronDown className="size-4" />
            </Button>
            <div />
          </div>

          {/* Z axis */}
          <div className="grid grid-cols-3 gap-1">
            <Button size="icon" variant="outline" className="h-8 w-full"
              onClick={() => jog("Z", 1)} disabled={busy}>
              <ArrowUp className="size-4" />
            </Button>
            <div className="flex items-center justify-center text-xs text-muted-foreground">Z</div>
            <Button size="icon" variant="outline" className="h-8 w-full"
              onClick={() => jog("Z", -1)} disabled={busy}>
              <ArrowDown className="size-4" />
            </Button>
          </div>

          {/* Home + Zero */}
          <div className="grid grid-cols-2 gap-1">
            <Button size="sm" variant="outline"
              onClick={() => void act(() => machineApi.home())}
              disabled={busy}>
              <Home className="size-4" />
              Home
            </Button>
            <Button size="sm" variant="outline"
              onClick={() => void act(() => machineApi.zero())}
              disabled={busy}>
              <CrosshairIcon className="size-4" />
              Set Zero
            </Button>
          </div>
        </div>
      )}

      {/* ── Job controls (connected) ───────────────────── */}
      {isConnected && !isAlarm && (
        <div className="grid gap-1">
          {/* Start job */}
          {!isJobRunning && !isHold && (
            <Button size="sm"
              onClick={() => void act(() => machineApi.run(), true)}
              disabled={busy}>
              <Play className="size-4" />
              Run Job
            </Button>
          )}

          {/* Feed hold — shown while running and not already held */}
          {isJobRunning && !isHold && (
            <Button size="sm" variant="outline"
              onClick={() => void act(() => machineApi.feedHold())}>
              <Pause className="size-4" />
              Feed Hold
            </Button>
          )}

          {/* Resume — shown while in hold state */}
          {isHold && (
            <Button size="sm" variant="outline"
              onClick={() => void act(() => machineApi.resume())}>
              <RotateCcw className="size-4" />
              Resume
            </Button>
          )}

          {/* Stop Job (soft) — cancel streaming, machine drains buffer */}
          {isJobRunning && (
            <Button size="sm" variant="outline"
              onClick={() => void act(() => machineApi.stopJob(), true)}>
              <Square className="size-4" />
              Stop Job
            </Button>
          )}
        </div>
      )}

      {/* ── E-Stop (always visible when connected) ────── */}
      {isConnected && (
        <Button
          size="sm"
          variant="destructive"
          onClick={() => void act(() => machineApi.stop(), true)}
          className="w-full"
          title="Soft reset (Ctrl-X) — stops all motion immediately, enters Alarm state"
        >
          <OctagonX className="size-4" />
          E-Stop
        </Button>
      )}

      {/* ── Job log ───────────────────────────────────── */}
      {isConnected && (
        <JobLogPanel isJobRunning={isJobRunning} />
      )}

      {/* ── Disconnect ────────────────────────────────── */}
      {isConnected && (
        <Button
          size="sm"
          variant="outline"
          onClick={() => void act(() => machineApi.disconnect(), true)}
          disabled={busy}
        >
          <Unplug className="size-4" />
          Disconnect
        </Button>
      )}

      {error && (
        <div className="rounded-md border border-destructive/50 bg-destructive/10 px-3 py-1.5 text-xs text-destructive">
          {error}
        </div>
      )}
    </div>
  );
}
