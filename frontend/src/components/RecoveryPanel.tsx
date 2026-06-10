import { useState } from "react";
import { AlertTriangle, Home, CrosshairIcon, Play, X, RotateCcw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { machineApi, type RecoveryInfo } from "@/lib/machineApi";
import { cn } from "@/lib/utils";

interface Props {
  info: RecoveryInfo;
  /** Current machine state string from SignalR (e.g. "Idle", "Run", "Alarm"). */
  machineState: string;
  isConnected: boolean;
  onDismiss: () => void;
  onRecoveryStarted: () => void;
}

export function RecoveryPanel({
  info,
  machineState,
  isConnected,
  onDismiss,
  onRecoveryStarted,
}: Props) {
  const [busy, setBusy] = useState(false);
  const [zeroConfirmed, setZeroConfirmed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [homedThisSession, setHomedThisSession] = useState(false);

  const isIdle = machineState.toLowerCase() === "idle";
  const pct =
    info.totalLines > 0
      ? Math.round((info.lastLineDone / info.totalLines) * 100)
      : 0;
  const interrupted = new Date(info.checkpointAt);
  const skippedLines = info.lastLineDone - info.resumeFromLine;

  const doHome = async () => {
    setBusy(true);
    setError(null);
    try {
      await machineApi.home();
      setHomedThisSession(true);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const doRecovery = async () => {
    setBusy(true);
    setError(null);
    try {
      await machineApi.startRecovery();
      onRecoveryStarted();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const doDismiss = async () => {
    setBusy(true);
    try {
      await machineApi.dismissRecovery();
      onDismiss();
    } catch {
      onDismiss(); // dismiss UI even if the server call fails
    } finally {
      setBusy(false);
    }
  };

  const canRecover = isConnected && isIdle && homedThisSession && zeroConfirmed;

  return (
    <div className="rounded-md border border-amber-500/40 bg-amber-500/5 p-3">
      {/* Header */}
      <div className="mb-2 flex items-start justify-between gap-2">
        <div className="flex items-center gap-1.5">
          <AlertTriangle className="size-3.5 shrink-0 text-amber-500" />
          <span className="text-xs font-semibold text-amber-600 dark:text-amber-400">
            Recovery available
          </span>
        </div>
        <button
          onClick={() => void doDismiss()}
          disabled={busy}
          className="text-muted-foreground hover:text-foreground"
          title="Dismiss checkpoint"
        >
          <X className="size-3.5" />
        </button>
      </div>

      {/* Job summary */}
      <div className="mb-3 space-y-0.5 text-xs text-muted-foreground">
        <div>
          Interrupted{" "}
          {interrupted.toLocaleDateString(undefined, {
            month: "short",
            day: "numeric",
            hour: "2-digit",
            minute: "2-digit",
          })}
        </div>
        <div>
          Line{" "}
          <span className="font-mono font-semibold text-foreground">
            {info.lastLineDone}
          </span>{" "}
          of{" "}
          <span className="font-mono font-semibold text-foreground">
            {info.totalLines}
          </span>{" "}
          ({pct}%)
        </div>
        <div>
          Last position:{" "}
          <span className="font-mono">
            X{info.lastX.toFixed(2)} Y{info.lastY.toFixed(2)}
          </span>
        </div>
        <div className="text-muted-foreground/70">
          Will re-run from line {info.resumeFromLine}
          {skippedLines > 0 && ` (skips ${skippedLines} completed lines)`}
        </div>
      </div>

      {/* Step-by-step recovery guide */}
      {isConnected && (
        <div className="space-y-2">
          {/* Step 1 — Home */}
          <div className="flex items-center gap-2">
            <span
              className={cn(
                "flex size-4 shrink-0 items-center justify-center rounded-full text-[10px] font-bold",
                homedThisSession
                  ? "bg-emerald-500 text-white"
                  : "bg-muted text-muted-foreground",
              )}
            >
              {homedThisSession ? "✓" : "1"}
            </span>
            <Button
              size="sm"
              variant="outline"
              className="h-7 flex-1 text-xs"
              onClick={() => void doHome()}
              disabled={busy || homedThisSession}
            >
              <Home className="size-3.5" />
              Home machine
            </Button>
          </div>

          {/* Step 2 — confirm work zero */}
          <div className="flex items-start gap-2">
            <span
              className={cn(
                "mt-0.5 flex size-4 shrink-0 items-center justify-center rounded-full text-[10px] font-bold",
                zeroConfirmed
                  ? "bg-emerald-500 text-white"
                  : "bg-muted text-muted-foreground",
              )}
            >
              {zeroConfirmed ? "✓" : "2"}
            </span>
            <div className="flex-1 space-y-1">
              <p className="text-xs text-muted-foreground">
                Move machine to original work origin (where work zero was set),
                then set zero.
              </p>
              <Button
                size="sm"
                variant="outline"
                className="h-7 w-full text-xs"
                onClick={() => void machineApi.zero().catch(() => {})}
                disabled={busy || !homedThisSession}
              >
                <CrosshairIcon className="size-3.5" />
                Set Zero
              </Button>
              <div className="flex items-center gap-1.5">
                <Checkbox
                  id="zero-confirmed"
                  checked={zeroConfirmed}
                  onCheckedChange={(v: boolean | "indeterminate") => setZeroConfirmed(v === true)}
                  disabled={!homedThisSession}
                />
                <Label htmlFor="zero-confirmed" className="cursor-pointer text-xs">
                  Work origin is set correctly
                </Label>
              </div>
            </div>
          </div>

          {/* Step 3 — start recovery */}
          <Button
            size="sm"
            className={cn(
              "w-full",
              canRecover
                ? "bg-amber-600 text-white hover:bg-amber-700"
                : "",
            )}
            onClick={() => void doRecovery()}
            disabled={busy || !canRecover}
            title={
              !isIdle
                ? `Machine must be Idle (currently ${machineState})`
                : !homedThisSession
                  ? "Home the machine first"
                  : !zeroConfirmed
                    ? "Confirm work origin is set"
                    : "Start recovery"
            }
          >
            {busy ? (
              <RotateCcw className="size-4 animate-spin" />
            ) : (
              <Play className="size-4" />
            )}
            Start Recovery
          </Button>
        </div>
      )}

      {!isConnected && (
        <p className="text-xs text-muted-foreground">
          Connect to the machine to begin recovery.
        </p>
      )}

      {error && (
        <div className="mt-2 rounded border border-destructive/50 bg-destructive/10 px-2 py-1 text-xs text-destructive">
          {error}
        </div>
      )}
    </div>
  );
}
