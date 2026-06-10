# TASKS.md — Build Sequence & Ready-to-Paste Prompts

Work top to bottom. Each task is a **separate Claude Code session/prompt**. Don't batch
them — one focused task at a time gives the best results. Tick the box when done.

> For every task, Claude Code should first read `PLASMA_CAM_PLAN.md` and `CLAUDE.md`.

---

## Milestone 1 — CAM Core (the real v1)

### [x] Task 0 — Project skeleton
<!-- Done: /backend (ASP.NET Core, GET /api/health, SignalR /hubs/machine heartbeat every 2s,
     IMachineConnection + FakeMachineConnection, CORS for :5173). /frontend (Vite+React+TS+
     Tailwind v4+shadcn/ui) demo page shows live health + heartbeat. Verified end-to-end via a
     JS SignalR client. No CAM features. Nothing deferred. -->
**Paste this:**
> Read `PLASMA_CAM_PLAN.md` and `CLAUDE.md` for full context. For this task, scaffold ONLY
> the skeleton — no CAM features yet:
> 1. Create `/frontend`: Vite + React + TypeScript + Tailwind + shadcn/ui, with one demo
>    page that calls the backend health endpoint and shows the result.
> 2. Create `/backend`: an ASP.NET Core Web API with a `GET /api/health` endpoint and a
>    SignalR hub at `/hubs/machine` that broadcasts a heartbeat every few seconds.
> 3. Add an `IMachineConnection` interface and a `FakeMachineConnection` implementation
>    (no real serial yet) that the hub uses to emit fake status.
> 4. Wire CORS so the frontend (dev port) can reach the backend.
> 5. Update `README.md` and the Commands section of `CLAUDE.md` with exact run steps.
> Confirm both run and the frontend shows a live heartbeat from the backend. Then stop.

### [x] Task 1 — Project & file model + SVG/DXF import
<!-- Done: Project model (name, units, table size/origin/thickness) backend + frontend.
     Neutral geometry: curves flattened to polylines (0.05mm chord tol) in mm, Y-up,
     normalized per file to (0,0). SVG importer (paths incl. arcs/béziers, shapes,
     transforms, unit/viewBox handling, Y-flip; text/use/image skipped with warnings).
     DXF importer via netDxf (lines, polylines+bulges, arcs, circles, ellipses, splines;
     $INSUNITS scaling). REST: /api/project get/new/settings/files(import,patch,delete)/
     export/load. Frontend: drag-drop import, file panel (visibility/rename/delete +
     parsed-entity summary + warnings), table settings card (unit-aware), save/load.
     28 unit tests (backend.Tests). Deferred to later tasks: block inserts (<use>/INSERT)
     and text outlines; geometry not yet sent to frontend (viewport = Task 2). -->
**Paste this:**
> Read the plan and CLAUDE.md. Implement the project/file foundation for Milestone 1:
> - A "Project" model (table size, units, origin, thickness) on backend + frontend types.
> - Drag-and-drop import of multiple SVG and DXF files. Parse them into a neutral internal
>   geometry model (paths as polylines/curves). Use a maintained DXF parser library; for
>   SVG, parse paths to geometry.
> - A file-list panel UI (shadcn): list imported files, toggle visibility, rename, delete.
> - Save/load a project to a single project file (JSON) including imported geometry.
> Don't build toolpaths or the canvas yet beyond what's needed to confirm parsing works
> (a simple list/log of parsed entities is fine). Stop and summarize.

### [x] Task 2 — Table viewport + part placement
<!-- Done: Part model (fileId + x/y translation + rotation CCW about local bbox center) —
     the shared nesting-ready transform; PartTransform (backend) mirrored in frontend
     lib/geometry.ts. Imports auto-place via naive shelf placement (PartPlacer; real
     packing = M2). REST: parts create/patch/duplicate/delete + /geometry (local-space
     polylines). Canvas 2D viewport: table + adaptive grid + origin marker (per origin
     setting), pan (drag empty/middle), wheel zoom at cursor, fit; select (point-in-
     polygon hit test), drag-move with snap-to-grid (1-50mm), rotation handle (shift=15°),
     rotate ±90 buttons, duplicate (Ctrl+D), delete (Del), arrow-key nudge, align to
     table edges/center; out-of-bounds parts stroke red. 34 backend tests. Verified in
     browser (drag/rotate/align persisted via PATCH). Deferred: multi-select/marquee,
     part-level visibility (file-level only). -->
**Paste this:**
> Read the plan and CLAUDE.md. Build the 2D viewport (Canvas 2D):
> - Render the table (from project table size) with grid and origin marker.
> - Render imported parts in table coordinates.
> - Pan, zoom, fit-to-view.
> - Select, move, rotate, duplicate parts. Use the shared part-transform model described
>   in the plan (must be nesting-ready — don't bake assumptions that block auto-nest later).
> - Snap-to-grid and basic alignment.
> Keep CAM out of this task. Stop and summarize.

### [x] Task 3 — CAM engine: kerf, lead-in/out, pierce, ordering
<!-- Done: backend/Cam/. Cut sides auto-classified by containment depth (even=Outside,
     odd=Inside, open=OnLine). Kerf via Clipper2 InflatePaths (±kerf/2, round joins,
     per-contour; too-small contours warn + skip). Direction normalized: outer CCW,
     holes CW → waste always right of travel; leads (line/arc quarter-circle, tangent
     at pierce) approach from waste side; pierce at longest-segment midpoint. Ordering:
     children-before-parents constraint + nearest-neighbor rapids from origin. Neutral
     Toolpath model (Cut: points incl. leads, lead point counts, side, feeds, pierce
     delay). CamSettings persisted on Project; REST: GET/PUT /api/project/cam, POST
     /api/project/toolpath. 18 new unit tests (52 total). Deferred: per-path cut-side
     UI overrides (engine classifies automatically; override model can come with the
     operations UI in Task 4/6); kerf interaction between near-touching contours. -->
**Paste this:**
> Read the plan and CLAUDE.md. Implement the plasma CAM engine on the backend using
> Clipper2:
> - Per-path cut side: inside / outside / on-line.
> - Kerf compensation via Clipper2 offsetting (offset = kerf/2 by side).
> - Lead-in / lead-out (line and arc options).
> - Pierce point placement + pierce delay.
> - Cut ordering: inner contours before outer; minimize rapids reasonably.
> Output a NEUTRAL toolpath model (controller-agnostic), per the architecture. Add unit
> tests for the offsetting and ordering logic. No G-code dialect yet. Stop and summarize.

### [ ] Task 4 — Pluggable post-processor + GRBL output + G-code preview
**Paste this:**
> Read the plan and CLAUDE.md. Add the post-processor layer:
> - Define a `IPostProcessor` interface that turns the neutral toolpath into G-code.
> - Implement a GRBL-plasma post-processor (torch on/off via M3/M5, pierce delay, feed
>   rates, units, work origin).
> - Frontend: a G-code preview panel and an export/download to `.nc`/`.gcode`.
> Keep it pluggable so laser/vinyl posts can be added later without touching the CAM engine.
> Stop and summarize.

### [ ] Task 5 — Toolpath simulation / playback
**Paste this:**
> Read the plan and CLAUDE.md. Add toolpath simulation in the viewport:
> - Visualize cut moves vs rapid (travel) moves distinctly, with cut direction.
> - Playback controls: play/pause, scrub timeline, speed control.
> - Drive the simulation from the neutral toolpath (and/or parsed G-code) — show the head
>   moving along the path like a video.
> Stop and summarize.

### [ ] Task 6 — Material profiles
**Paste this:**
> Read the plan and CLAUDE.md. Add material profiles:
> - Save/load presets per material + thickness: feed rate, pierce delay, kerf width,
>   cut height.
> - UI to pick/create/edit profiles; selecting one populates the relevant CAM settings.
> - Persist profiles with the app (and optionally export/import).
> This completes Milestone 1. Stop and summarize what M1 now does end-to-end.

---

## Milestone 2 — Auto-Nesting
### [ ] Task 7 — Auto-nesting
**Paste this:**
> Read the plan and CLAUDE.md. Add auto-nesting that arranges parts on the sheet to save
> material, reusing the existing part-transform/placement model (manual placement must
> still work). Lean on an existing open-source nesting approach (e.g. SVGnest/DeepNest
> core concepts) rather than writing packing math from scratch. Add a "Nest" action with
> sheet margins and spacing settings. Stop and summarize.

---

## Milestone 3 — Machine Control (GRBL)  ⚠️ safety-critical
### [ ] Task 8 — Real serial connection (replace fake)
**Paste this:**
> Read the plan and CLAUDE.md. Implement a real GRBL serial connection behind the existing
> `IMachineConnection` interface (System.IO.Ports): list ports, connect/disconnect, send
> lines, read status reports, surface state over the SignalR hub. Keep `FakeMachine` for
> testing and simulation. NO motion/streaming yet — connection + status only. Stop and summarize.

### [ ] Task 9 — Jog, home, and run job
**Paste this:**
> Read the plan and CLAUDE.md. Add machine control UI + backend: jog (with step sizes),
> homing, set work zero, and G-code streaming with the GRBL character-counting/ok flow.
> Live position + run progress over SignalR. SAFETY: explicit user action required to
> start; prominent stop/feed-hold; respect soft limits/table bounds; never auto-start.
> Stop and summarize.

### [ ] Task 10 — Pause / stop / resume-from-pause + job log
**Paste this:**
> Read the plan and CLAUDE.md. Add feed-hold/pause, safe stop, resume-from-pause, and a
> per-job log (lines sent, position, events, timestamps). This is the "all-in-one" moment.
> Stop and summarize.

---

## Milestone 4 — Power-Loss Recovery & Machine-Type Modes  ⚠️ hard / careful
### [ ] Task 11 — Power-loss recovery
> Persist job checkpoint (line + position); on restart offer safe recovery: re-home,
> re-establish position, re-pierce, resume from a safe point. Treat with extreme care.

### [ ] Task 12 — Laser mode
> Add a laser operation mode + laser post-processor (beam on/off + power %, no kerf/pierce).

### [ ] Task 13 — Vinyl / drag-knife mode
> Add a drag-knife operation mode: knife up/down (Z or servo), blade-offset + overcut
> compensation for sharp corners. No kerf/pierce/THC.

---

## Working agreement for Claude Code
- One task per session. Read the plan + CLAUDE.md first.
- Don't pull later tasks forward. Flag scope creep instead of acting on it.
- Tick the box here and note deferrals when you finish a task.
