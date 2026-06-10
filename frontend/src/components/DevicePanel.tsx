import { useCallback, useEffect, useState } from "react";
import { Cable, Unplug, RefreshCw, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { machineApi, type ConnectionInfo } from "@/lib/machineApi";
import { useMachineHeartbeat } from "@/hooks/useMachineHeartbeat";

const BAUD_OPTIONS = [115200, 57600, 38400, 19200, 9600];

/**
 * Connection controls + live DRO for the right-panel Device card.
 * REST handles port selection / connect / disconnect; SignalR delivers live status.
 */
export function DevicePanel() {
  const { status } = useMachineHeartbeat();

  const [ports, setPorts] = useState<string[]>([]);
  const [selectedPort, setSelectedPort] = useState<string>("");
  const [selectedBaud, setSelectedBaud] = useState(115200);
  const [connInfo, setConnInfo] = useState<ConnectionInfo | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadPorts = useCallback(async () => {
    try {
      const list = await machineApi.ports();
      setPorts(list);
      if (list.length > 0 && !selectedPort) setSelectedPort(list[0]);
    } catch {
      /* ignore — backend may be briefly unavailable */
    }
  }, [selectedPort]);

  const loadConnection = useCallback(async () => {
    try {
      const info = await machineApi.connection();
      setConnInfo(info);
      if (info.isConnected && info.port) {
        setSelectedPort(info.port);
        setSelectedBaud(info.baud ?? 115200);
      }
    } catch {
      /* ignore */
    }
  }, []);

  useEffect(() => {
    void loadPorts();
    void loadConnection();
  }, [loadPorts, loadConnection]);

  const handleConnect = async () => {
    if (!selectedPort) return;
    setBusy(true);
    setError(null);
    try {
      await machineApi.connect(selectedPort, selectedBaud);
      await loadConnection();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const handleDisconnect = async () => {
    setBusy(true);
    setError(null);
    try {
      await machineApi.disconnect();
      await loadConnection();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const isConnected = connInfo?.isConnected ?? false;
  const machineState = status?.machineState ?? "—";
  const stateColor =
    machineState === "Idle" ? "text-emerald-500"
    : machineState === "Run" ? "text-blue-500"
    : machineState.startsWith("Alarm") ? "text-destructive"
    : "text-muted-foreground";

  return (
    <div className="grid gap-3">
      {/* Connection state chip */}
      <div className="flex items-center gap-2">
        <span
          className={cn(
            "size-2 shrink-0 rounded-full",
            isConnected ? "bg-emerald-500" : "bg-muted-foreground/40",
          )}
        />
        <span className="text-xs text-muted-foreground">
          {isConnected
            ? `${connInfo?.port} · ${connInfo?.baud} baud`
            : "Not connected"}
        </span>
      </div>

      {/* DRO — only shown when a status is streaming */}
      {isConnected && status && (
        <div className="rounded-md border bg-muted/30 px-3 py-2">
          <div className="mb-1 flex items-center justify-between">
            <span className={cn("text-xs font-medium", stateColor)}>{machineState}</span>
          </div>
          <div className="grid grid-cols-3 gap-1 font-mono text-xs">
            <span className="text-muted-foreground">X</span>
            <span className="text-muted-foreground">Y</span>
            <span className="text-muted-foreground">Z</span>
            <span>{status.x.toFixed(3)}</span>
            <span>{status.y.toFixed(3)}</span>
            <span>{status.z.toFixed(3)}</span>
          </div>
        </div>
      )}

      {/* Controls: hidden when connected */}
      {!isConnected && (
        <div className="grid gap-2">
          <div className="flex gap-1">
            <div className="flex-1">
              <Label className="text-xs">Port</Label>
              <Select value={selectedPort} onValueChange={setSelectedPort}>
                <SelectTrigger className="mt-1 h-8 text-xs">
                  <SelectValue placeholder="Select port…" />
                </SelectTrigger>
                <SelectContent>
                  {ports.map((p) => (
                    <SelectItem key={p} value={p}>
                      {p}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="mt-auto">
              <Button
                variant="ghost"
                size="icon"
                className="h-8 w-8"
                onClick={() => void loadPorts()}
                title="Refresh port list"
              >
                <RefreshCw className="size-3.5" />
              </Button>
            </div>
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
        </div>
      )}

      {/* Connect / Disconnect button */}
      {isConnected ? (
        <Button
          size="sm"
          variant="outline"
          onClick={() => void handleDisconnect()}
          disabled={busy}
        >
          {busy ? (
            <Loader2 className="size-4 animate-spin" />
          ) : (
            <Unplug className="size-4" />
          )}
          Disconnect
        </Button>
      ) : (
        <Button
          size="sm"
          onClick={() => void handleConnect()}
          disabled={busy || !selectedPort}
        >
          {busy ? (
            <Loader2 className="size-4 animate-spin" />
          ) : (
            <Cable className="size-4" />
          )}
          Connect
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
