import { useEffect, useRef, useState } from "react";
import { ScrollText } from "lucide-react";
import { machineApi, type JobLogEntry, type JobLogEvent } from "@/lib/machineApi";
import { cn } from "@/lib/utils";

const EVENT_LABEL: Record<JobLogEvent, string> = {
  Started:   "Started",
  Progress:  "Progress",
  FeedHold:  "Hold",
  Resumed:   "Resumed",
  Completed: "Done",
  Error:     "Error",
  Stopped:   "Stopped",
};

const EVENT_COLOR: Record<JobLogEvent, string> = {
  Started:   "text-blue-500",
  Progress:  "text-muted-foreground",
  FeedHold:  "text-amber-500",
  Resumed:   "text-emerald-500",
  Completed: "text-emerald-500",
  Error:     "text-destructive",
  Stopped:   "text-amber-500",
};

function formatTime(ts: string): string {
  const d = new Date(ts);
  return d.toLocaleTimeString(undefined, { hour12: false });
}

interface Props {
  /** Poll for updates while a job is running. */
  isJobRunning: boolean;
}

export function JobLogPanel({ isJobRunning }: Props) {
  const [entries, setEntries] = useState<JobLogEntry[]>([]);
  const bottomRef = useRef<HTMLDivElement>(null);

  const load = () =>
    machineApi.jobLog().then(setEntries).catch(() => {});

  // Load once on mount, then poll every 2 s while a job is running.
  useEffect(() => { void load(); }, []);
  useEffect(() => {
    if (!isJobRunning) {
      // Final fetch when job ends.
      void load();
      return;
    }
    const id = setInterval(() => void load(), 2000);
    return () => clearInterval(id);
  }, [isJobRunning]);

  // Auto-scroll to bottom when new entries arrive.
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [entries.length]);

  if (entries.length === 0) return null;

  return (
    <div className="grid gap-1">
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <ScrollText className="size-3.5" />
        Job log
      </div>
      <div className="max-h-48 overflow-y-auto rounded-md border bg-muted/20 p-1.5 font-mono text-xs">
        {entries.map((e, i) => (
          <div key={i} className="flex gap-1.5 py-0.5">
            <span className="shrink-0 text-muted-foreground/60">
              {formatTime(e.timestamp)}
            </span>
            <span className={cn("w-14 shrink-0 font-semibold", EVENT_COLOR[e.event])}>
              {EVENT_LABEL[e.event]}
            </span>
            {e.lineNumber != null && e.lineTotal != null && (
              <span className="text-muted-foreground">
                {e.lineNumber}/{e.lineTotal}
              </span>
            )}
            {e.x != null && (
              <span className="text-muted-foreground/70">
                X{e.x.toFixed(2)} Y{e.y?.toFixed(2)} Z{e.z?.toFixed(2)}
              </span>
            )}
            {e.message && (
              <span className="truncate text-muted-foreground">{e.message}</span>
            )}
          </div>
        ))}
        <div ref={bottomRef} />
      </div>
    </div>
  );
}
