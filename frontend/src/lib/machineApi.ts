import { BACKEND_URL } from "@/lib/backend";

export interface ConnectionInfo {
  isConnected: boolean;
  port: string | null;
  baud: number | null;
  isJobRunning: boolean;
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

  stop: () => send("POST", "stop"),
};
