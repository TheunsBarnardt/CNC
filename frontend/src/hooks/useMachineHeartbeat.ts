import { useEffect, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { BACKEND_URL, type MachineStatus } from "@/lib/backend";

export type HubState = "connecting" | "connected" | "disconnected";

export interface MachineHeartbeat {
  /** State of the SignalR connection itself. */
  hubState: HubState;
  /** Most recent status pushed by the server, or null before the first tick. */
  status: MachineStatus | null;
}

/**
 * Subscribes to the backend's /hubs/machine SignalR hub and exposes the latest
 * machine-status heartbeat. The connection is opened on mount and torn down on
 * unmount; SignalR handles automatic reconnection.
 */
export function useMachineHeartbeat(): MachineHeartbeat {
  const [hubState, setHubState] = useState<HubState>("connecting");
  const [status, setStatus] = useState<MachineStatus | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(`${BACKEND_URL}/hubs/machine`)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connectionRef.current = connection;

    connection.on("machineStatus", (next: MachineStatus) => setStatus(next));
    connection.onreconnecting(() => setHubState("connecting"));
    connection.onreconnected(() => setHubState("connected"));
    connection.onclose(() => setHubState("disconnected"));

    connection
      .start()
      .then(() => setHubState("connected"))
      .catch(() => setHubState("disconnected"));

    return () => {
      // Avoid stopping mid-handshake (logs a noisy warning otherwise).
      if (connection.state === HubConnectionState.Connected) {
        void connection.stop();
      } else {
        connection.start().then(() => connection.stop()).catch(() => {});
      }
    };
  }, []);

  return { hubState, status };
}
