import { BACKEND_URL } from "@/lib/backend";

export interface ConnectionInfo {
  isConnected: boolean;
  port: string | null;
  baud: number | null;
}

async function json<T>(r: Response): Promise<T> {
  if (!r.ok) {
    const body = await r.text().catch(() => r.statusText);
    throw new Error(body || r.statusText);
  }
  return r.json() as Promise<T>;
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

  disconnect: (): Promise<{ message: string }> =>
    fetch(`${BACKEND_URL}/api/machine/disconnect`, { method: "POST" }).then((r) => json(r)),
};
