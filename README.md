# DIY GRBL Cutting CAM — All-in-One

A free, modern, all-in-one desktop app for **DIY GRBL-based 2D cutting machines** —
plasma (primary), with laser, vinyl/drag-knife, and router support planned. Import vector
art (SVG/DXF) → arrange & nest → generate toolpaths → simulate → cut directly via a GRBL
serial connection.

> Think "the free xTool for DIY CNC builders." What matters is the **controller, not the
> machine**: any machine running GRBL/grblHAL is a target.

## Status
**Tasks 0–4 complete.** The app now goes from imported art to downloadable G-code:
- Pluggable post-processor layer: `IPostProcessor` turns the neutral toolpath into a
  controller dialect. GRBL plasma post included (M3/M5 torch, G4 pierce dwell, work-origin
  aware, mm absolute). Frontend G-code panel: generate, preview, download `.nc`.
- Plasma CAM engine (backend, Clipper2): cut-side classification (outer/hole/on-line),
  kerf compensation, line/arc lead-in/out with waste-side pierces on longest segments,
  inner-before-outer cut ordering with nearest-neighbor rapids — emitted as a neutral,
  controller-agnostic toolpath (G-code dialects arrive with the post-processor task).
- 2D table viewport (Canvas 2D): grid, origin marker, pan/zoom/fit; select, drag-move
  with snap-to-grid, rotate (handle or ±90°), duplicate, delete, align-to-table; parts
  out of bounds highlight red. Placement uses the shared nesting-ready part transform
  (translation + rotation), so Milestone 2 auto-nesting drives the same model.
- Drag-and-drop import of multiple **SVG** and **DXF** files, parsed on the backend into
  a neutral geometry model (curves flattened to mm polylines, per-file warnings for
  unsupported entities like text).
- File panel: visibility toggle, rename, delete, parsed-entity summary per file.
- Table & sheet settings (size, origin, units mm/inch, material thickness).
- Save/load the whole project (settings + geometry) as a single JSON file.
- Live backend health + machine heartbeat (fake connection) in the header.

Next up: toolpath simulation, then material profiles complete Milestone 1 — see
`TASKS.md` for the build sequence. Planning docs:
- **`PLASMA_CAM_PLAN.md`** — full design doc (vision, scope, stack, milestones, architecture). Source of truth.
- **`CLAUDE.md`** — conventions and guardrails for Claude Code.
- **`TASKS.md`** — the build sequence with ready-to-paste prompts.

## Tech stack
- Frontend: React + TypeScript + Vite + shadcn/ui (Tailwind)
- Backend: C# / ASP.NET Core + SignalR
- Geometry: Clipper2 (C#)
- Serial: System.IO.Ports (behind an interface; fake impl until machine-control milestone)

## How to build it with Claude Code
1. Open this folder in Claude Code.
2. It will auto-read `CLAUDE.md`. Tell it to also read `PLASMA_CAM_PLAN.md`.
3. Open `TASKS.md` and paste **Task 0** as your first prompt. Do one task per session,
   top to bottom. Don't batch tasks.
4. Review each result, commit, then move to the next task.

## Running
Start the backend, then the frontend (two terminals):

```sh
# 1. Backend — ASP.NET Core API + SignalR  → http://localhost:5100
cd backend
dotnet run --launch-profile http

# 2. Frontend — Vite dev server            → http://localhost:5173
cd frontend
npm install
npm run dev
```

Open http://localhost:5173. The page should show the backend health as **online** and
the machine heartbeat as **live**, with the heartbeat number ticking up every ~2s.

- Health endpoint: `GET http://localhost:5100/api/health`
- SignalR hub: `http://localhost:5100/hubs/machine` (event `machineStatus`)
- The frontend reads the backend URL from `frontend/.env` (`VITE_BACKEND_URL`).
- Headless heartbeat check (backend running): `cd frontend && node scripts/heartbeat-check.mjs`

## Supported hardware (target)
- **Full (design → cut):** GRBL boards (e.g. Arduino Uno + CNC Shield V3) and grblHAL
  32-bit boards (ESP32/Teensy/STM32) with THC for plasma.
- **CAM-only:** Mach3 USB boards and turnkey/proprietary machines (design + export G-code,
  cut in their own software). Reflashing such a board to grblHAL moves it into full support.

## Safety
This software eventually drives torches and lasers. Machine control is conservative by
design: explicit user action to start, E-stop always wins, bounds-checked motion, never
auto-run. See the safety notes in `PLASMA_CAM_PLAN.md`.

## License
TBD (intended to be free/open for the DIY community).
