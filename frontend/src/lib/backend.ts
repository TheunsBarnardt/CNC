// Central place for backend wiring (REST base URL + types shared with the API).

export const BACKEND_URL =
  import.meta.env.VITE_BACKEND_URL ?? "http://localhost:5100";

/** Shape of GET /api/health. */
export interface HealthResponse {
  status: string;
  service: string;
  timeUtc: string;
}

/** Mirrors the backend MachineStatus record (camelCased over the wire). */
export interface MachineStatus {
  connectionState: "Disconnected" | "Connecting" | "Connected" | "Error";
  machineState: string;
  x: number;
  y: number;
  z: number;
  timestamp: string;
  heartbeatSequence: number;
}

export async function fetchHealth(signal?: AbortSignal): Promise<HealthResponse> {
  const res = await fetch(`${BACKEND_URL}/api/health`, { signal });
  if (!res.ok) {
    throw new Error(`Health check failed: ${res.status} ${res.statusText}`);
  }
  return res.json();
}
