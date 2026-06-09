import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";
import { fetchHealth } from "@/lib/backend";
import { useMachineHeartbeat } from "@/hooks/useMachineHeartbeat";

/** Compact backend/machine status chips (the Task 0 demo, miniaturized). */
export function StatusBar() {
  const [healthy, setHealthy] = useState<boolean | null>(null);
  const { hubState, status } = useMachineHeartbeat();

  useEffect(() => {
    const controller = new AbortController();
    const check = () =>
      fetchHealth(controller.signal)
        .then(() => setHealthy(true))
        .catch(() => !controller.signal.aborted && setHealthy(false));
    check();
    const timer = setInterval(check, 10_000);
    return () => {
      controller.abort();
      clearInterval(timer);
    };
  }, []);

  return (
    <div className="flex items-center gap-4 text-xs text-muted-foreground">
      <span className="flex items-center gap-1.5">
        <Dot ok={healthy === true} bad={healthy === false} />
        backend
      </span>
      <span className="flex items-center gap-1.5">
        <Dot ok={hubState === "connected"} bad={hubState === "disconnected"} />
        machine
        {status && hubState === "connected" && (
          <span className="font-mono">
            {status.machineState} · X{status.x.toFixed(1)} Y{status.y.toFixed(1)}
          </span>
        )}
      </span>
    </div>
  );
}

function Dot({ ok, bad }: { ok: boolean; bad: boolean }) {
  return (
    <span
      className={cn(
        "size-2 rounded-full",
        ok ? "bg-emerald-500" : bad ? "bg-destructive" : "bg-muted-foreground/40",
      )}
    />
  );
}
