// One-off end-to-end check: connect to the SignalR hub as a JS client and
// confirm machine-status heartbeats arrive. Run with the backend up:
//   node scripts/heartbeat-check.mjs
import { HubConnectionBuilder } from "@microsoft/signalr";

const url = process.env.VITE_BACKEND_URL ?? "http://localhost:5100";
const conn = new HubConnectionBuilder().withUrl(`${url}/hubs/machine`).build();

let count = 0;
conn.on("machineStatus", async (s) => {
  console.log(`tick #${s.heartbeatSequence} state=${s.machineState} X=${s.x} Y=${s.y}`);
  if (++count >= 3) {
    await conn.stop();
    process.exit(0);
  }
});

await conn.start();
console.log("connected to", `${url}/hubs/machine`);
setTimeout(() => { console.error("no heartbeats within 8s"); process.exit(1); }, 8000);
