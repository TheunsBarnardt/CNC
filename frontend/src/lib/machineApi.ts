import { BACKEND_URL } from "@/lib/backend";

export interface ConnectionInfo {
  isConnected: boolean;
  port: string | null;
  baud: number | null;
  isJobRunning: boolean;
}

export type JobLogEvent =
  | "Started"
  | "Progress"
  | "FeedHold"
  | "Resumed"
  | "Completed"
  | "Error"
  | "Stopped";

export interface JobLogEntry {
  timestamp: string;
  event: JobLogEvent;
  lineNumber: number | null;
  lineTotal: number | null;
  x: number | null;
  y: number | null;
  z: number | null;
  message: string | null;
}

async function json<T>(r: Response): Promise<T> {
  if (!r.ok) {
    const body = await r.json().catch(() => ({ error: r.statusText })) as { error?: string };
    throw new Error(body?.error ?? r.statusText);
  }
  return r.json() as Promise<T>;
}

async function send(method: string, path: string, body?: unknown): Promise<void> {
  const r = await fetch(`${BACKEND_URL}/api/machine/${path}`, {
    method,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!r.ok) {
    const b = await r.json().catch(() => ({ error: r.statusText })) as { error?: string };
    throw new Error(b?.error ?? r.statusText);
  }
}

export const machineApi = {
  ports: (): Promise<string[]> =>
    fetch(`${BACKEND_URL}/api/machine/ports`).then((r) => json<string[]>(r)),

  connection: (): Promise<ConnectionInfo> =>
    fetch(`${BACKEND_URL}/api/machine/connection`).then((r) => json<ConnectionInfo>(r)),

  connect: (port: string, baud: number): Promise<{ message: string }> =>
    fetch(`${BACKEND_URL}/api/machine/connect`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ port, baud }),
    }).then((r) => json(r)),

  disconnect: () => send("POST", "disconnect"),

  jog: (axis: string, distance: number, feed: number) =>
    send("POST", "jog", { axis, distance, feed }),

  home: () => send("POST", "home"),

  zero: () => send("POST", "zero"),

  run: () => send("POST", "run"),

  feedHold: () => send("POST", "feed-hold"),

  resume: () => send("POST", "resume"),

  /** Hard stop: cancel streaming + Ctrl-X soft reset. Machine enters Alarm. */
  stop: () => send("POST", "stop"),

  /** Soft stop: cancel streaming only. Machine drains its buffer and goes idle. */
  stopJob: () => send("POST", "stop-job"),

  /** Send $X to clear Alarm lock after E-Stop or homing failure. */
  unlock: () => send("POST", "unlock"),

  jobLog: (): Promise<JobLogEntry[]> =>
    fetch(`${BACKEND_URL}/api/machine/job-log`).then((r) => json<JobLogEntry[]>(r)),
};
