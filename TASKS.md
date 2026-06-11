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

### [x] Task 4 — Pluggable post-processor + GRBL output + G-code preview
<!-- Done: backend/Post/. IPostProcessor (Id/DisplayName/FileExtension + Generate(toolpath,
     project) → GcodeProgram) + PostProcessorRegistry; new dialect = one DI registration,
     CAM untouched. GrblPlasmaPostProcessor: G21/G90/G17/G94 preamble, safety M5 before
     any motion, per cut G0→M3 S1000→G4 pierce dwell→G1+F→M5, work-origin shift per
     TableOrigin (axes stay Y-up; flips are machine $3 config), invariant-culture numbers,
     park at origin + M2. Cut/pierce heights emitted as header comments only — Z/THC words
     deferred to machine control (M3). REST: GET /api/posts, POST /api/project/gcode
     (runs CAM + post, returns gcode/stats/warnings/filename). Frontend: G-code card —
     generate, stats, warnings, monospace preview, download .nc. 12 new unit tests
     (64 total). Deferred: post-specific options UI (e.g. S-value, end-of-job position). -->
**Paste this:**
> Read the plan and CLAUDE.md. Add the post-processor layer:
> - Define a `IPostProcessor` interface that turns the neutral toolpath into G-code.
> - Implement a GRBL-plasma post-processor (torch on/off via M3/M5, pierce delay, feed
>   rates, units, work origin).
> - Frontend: a G-code preview panel and an export/download to `.nc`/`.gcode`.
> Keep it pluggable so laser/vinyl posts can be added later without touching the CAM engine.
> Stop and summarize.

### [x] Task 5 — Toolpath simulation / playback
<!-- Done: frontend-only, driven by the neutral toolpath (POST /toolpath; no G-code
     parsing needed). lib/simulation.ts: time-indexed segment list (rapid/pierce-dwell/
     cut, leads flagged) from per-cut feed rates + pierce delays; rapids at assumed
     6000mm/min until the real machine reports $110/$111 (M3); binary-search stateAt(t);
     starts/parks at the work origin like the post. Viewport overlay: cuts orange
     (leads lighter), rapids dashed grey, done/remaining by alpha, live segment split
     at the head, one direction arrow per cut, torch head with glow when on / crosshair
     when off. SimulationBar under the viewport: Simulate (fetch+build+autoplay),
     play/pause/stop, scrub slider, 0.5-16x speed, elapsed/total, regenerate, close.
     Deferred: auto-invalidate stale sim when parts/settings change (manual regenerate
     button for now); per-cut info popup on hover. -->
**Paste this:**
> Read the plan and CLAUDE.md. Add toolpath simulation in the viewport:
> - Visualize cut moves vs rapid (travel) moves distinctly, with cut direction.
> - Playback controls: play/pause, scrub timeline, speed control.
> - Drive the simulation from the neutral toolpath (and/or parsed G-code) — show the head
>   moving along the path like a video.
> Stop and summarize.

### [x] Task 6 — Material profiles
<!-- Done: MaterialProfile (name, material, thickness, kerf/feed/pierce delay/cut+pierce
     height) + ProfileStore persisted app-level to %LOCALAPPDATA%/diy-grbl-cam/
     material-profiles.json (corrupt file set aside as .corrupt + reseeded; seeds = 3
     "example, tune for your machine" mild-steel profiles ~45A ballpark). REST:
     /api/profiles CRUD + /export + /import (merge by id). Frontend "Cut settings" card:
     profile picker (select = applies numbers to project CAM via PUT /cam), update/
     delete selected, save-current-as-new (thickness from table settings), editable CAM
     fields incl. lead type/length (leads stay per-project — geometry preference, not
     material). 7 new unit tests (71 total). Deferred: material/thickness as separate
     editable fields on profiles (name carries it for now); profile import/export UI
     buttons (endpoints exist). MILESTONE 1 COMPLETE. -->
**Paste this:**
> Read the plan and CLAUDE.md. Add material profiles:
> - Save/load presets per material + thickness: feed rate, pierce delay, kerf width,
>   cut height.
> - UI to pick/create/edit profiles; selecting one populates the relevant CAM settings.
> - Persist profiles with the app (and optionally export/import).
> This completes Milestone 1. Stop and summarize what M1 now does end-to-end.

### [x] Task 6b — Workspace UX alignment with xTool Studio
*Reference: the xTool Studio Basics course (https://support.xtool.com/academy/course?id=6)
and desktop UI overview (https://support.xtool.com/article/2409). Goal: the app should
feel familiar to xTool/XCS users — same workspace anatomy, our CAM underneath.*
<!-- DONE: frontend-only reshuffle, no CAM/backend changes. Left creation rail
     (CreationSidebar: import via file picker, shapes/text disabled stubs → Task 14;
     drag-drop import moved onto the canvas, ImportDropzone removed). Floating
     EditToolbar over the canvas on selection: precise X/Y (world-bbox min) + angle
     inputs, W×H readout, rotate ±90°, align ×6, duplicate, delete — absorbed the old
     top-of-canvas button row; wraps on narrow canvases. Bottom bar inside the
     viewport: cursor mm readout, snap+step, grid toggle, light/dark canvas (fixed
     palette, independent of app theme), zoom −/%/+/fit. Right panel: Device status
     card first (StatusBar moved out of header), then Files / Table & sheet / Cut
     settings. New Process button (header) → preview mode: viewport goes read-only
     (pan/zoom only), simulation auto-generates, Processing card (estimated time,
     pierces, cut length) + G-code card replace the edit cards; SimulationBar lives
     here. Deferred (need part-model support, NOT silently added): mirror H/V +
     editable size (disabled/read-only in toolbar), stacking order (no UI). -->

**Paste this:**
> Read the plan and CLAUDE.md. Rework the workspace layout to follow xTool Studio's
> anatomy (see the reference links in TASKS.md Task 6b) without changing any CAM/backend
> behavior:
> - Left sidebar: creation/import tools (import; basic shapes and text can be stubs or
>   deferred — flag, don't silently expand scope).
> - Floating editing toolbar when a part is selected: precise X/Y/size/rotation inputs,
>   align, mirror, stacking — replacing/absorbing the current top-of-canvas button row.
> - Right panel: device/connection status on top, then per-object processing parameters
>   (cut settings, material profile) — the current Table/CAM/G-code cards reorganized
>   into this flow.
> - Bottom bar: zoom controls + canvas options (grid toggle, light/dark canvas).
> - Processing preview as its own page/mode (ties into the Task 5 simulation) with
>   estimated processing time.
> Keep it one reviewable task: layout + interaction reshuffle only. Stop and summarize.

---

## Milestone 2 — Auto-Nesting
### [x] Task 7 — Auto-nesting
<!-- Done: Backend Nester.cs uses SVGnest/DeepNest greedy approach via Clipper2: parts
     ordered largest-first, each tried at discrete rotation candidates (0/45/90/180°
     configurable), placed at bottom-left feasible grid position; collision tested with
     Clipper2 Intersect (inflated by spacing); margin enforced against table bounds.
     Drives the same Part.X/Y/RotationDeg transform as manual dragging. REST:
     POST /api/project/nest → {project, placedCount, skippedCount, warnings}.
     Frontend: NestCard (margin/spacing/rotation settings + Nest button + outcome
     summary + per-part warnings), wired into right-panel "Arrange" card.
     8 unit tests in NesterTests.cs. Manual placement unaffected. -->
**Paste this:**
> Read the plan and CLAUDE.md. Add auto-nesting that arranges parts on the sheet to save
> material, reusing the existing part-transform/placement model (manual placement must
> still work). Lean on an existing open-source nesting approach (e.g. SVGnest/DeepNest
> core concepts) rather than writing packing math from scratch. Add a "Nest" action with
> sheet margins and spacing settings. Stop and summarize.

---

## Milestone 3 — Machine Control (GRBL)  ⚠️ safety-critical
### [x] Task 8 — Real serial connection (replace fake)
<!-- Done: SerialMachineConnection implements IMachineConnection via System.IO.Ports:
     opens port (DtrEnable=false to prevent Arduino auto-reset / plasma relay energize),
     sends '?' every 200ms in a background Task.Run loop, parses GRBL status reports
     (<State|WPos:x,y,z|...>) preferring WPos over MPos, fires StatusChanged and caches
     last status for GetStatus(). MachineConnectionManager wraps the active connection
     (starts as FakeMachineConnection, swaps to SerialMachineConnection at runtime via
     ConnectSerialAsync/DisconnectSerialAsync, thread-safe inner swap with event
     re-subscription, reverts to Fake on disconnect). REST: GET /api/machine/ports,
     GET /api/machine/connection, POST /api/machine/connect, POST /api/machine/disconnect.
     System.IO.Ports NuGet added. Frontend DevicePanel: port dropdown + refresh, baud
     selector, Connect/Disconnect button, live DRO (X/Y/Z from SignalR) shown when
     connected. FakeMachineConnection retained for testing/simulation. No motion. -->
**Paste this:**
> Read the plan and CLAUDE.md. Implement a real GRBL serial connection behind the existing
> `IMachineConnection` interface (System.IO.Ports): list ports, connect/disconnect, send
> lines, read status reports, surface state over the SignalR hub. Keep `FakeMachine` for
> testing and simulation. NO motion/streaming yet — connection + status only. Stop and summarize.

### [x] Task 9 — Jog, home, and run job
<!-- Done: SerialMachineConnection refactored to separate ReadLoopAsync (dispatches lines
     to status parser or Channel<string> response queue) + PollSenderAsync (sends '?' every
     200ms, skips tick if write lock held). SemaphoreSlim write lock prevents concurrent
     writes. Motion: JogAsync ($J=G91 G21 Xd Ff), HomeAsync ($H), SetZeroAsync (G10 L20 P1),
     FeedHold/Resume/SoftReset as real-time bytes (no lock). RunGcodeAsync: character-counting
     protocol — tracks in-flight byte count vs GrblBuffer=127, waits for ok from response
     channel, throws on error:X. MachineConnectionManager: StartJob (background Task),
     StopJobAsync (cancel+await), enriches MachineStatus with JobTotal/JobDone.
     HeartbeatBroadcaster now subscribes to StatusChanged for immediate push + keeps 2s
     periodic heartbeat. MachineStatus record: +JobTotal/JobDone (nullable, default null).
     G-code cached in ProjectService after POST /api/project/gcode.
     REST: POST /api/machine/jog, /home, /zero, /run (202), /feed-hold, /resume, /stop.
     GET /api/machine/connection now includes isJobRunning. Frontend DevicePanel:
     jog grid (XY + Z), step-size selector (0.1/1/10/100mm), Home/Set Zero buttons, Run Job,
     Feed Hold/Resume, E-Stop (destructive), Disconnect. Progress bar + line counter during job.
     Controls shown only when serial-connected; jog hidden during active job. -->
**Paste this:**
> Read the plan and CLAUDE.md. Add machine control UI + backend: jog (with step sizes),
> homing, set work zero, and G-code streaming with the GRBL character-counting/ok flow.
> Live position + run progress over SignalR. SAFETY: explicit user action required to
> start; prominent stop/feed-hold; respect soft limits/table bounds; never auto-start.
> Stop and summarize.

### [x] Task 10 — Pause / stop / resume-from-pause + job log
<!-- Done: Backend — JobLogEntry model (Timestamp/Event/LineNumber/LineTotal/X/Y/Z/Message);
     MachineConnectionManager detects Hold/Resume state transitions in OnInnerStatus
     ("Run"→"Hold" logs FeedHold, "Hold"→"Run" logs Resumed) and logs Started/Progress
     (every 50 lines)/FeedHold/Resumed/Completed/Error/Stopped entries; GetJobLog() returns
     snapshot. SerialMachineConnection: UnlockAsync ($X). New REST: GET /api/machine/job-log,
     POST /api/machine/stop-job (soft stop — cancel streaming only, no Ctrl-X; machine drains
     GRBL buffer and goes idle, no Alarm state), POST /api/machine/unlock ($X clear alarm).
     Frontend — machineApi.ts: jobLog()/stopJob()/unlock() + JobLogEntry type. JobLogPanel
     component: polls every 2s during job, final fetch on completion, colour-coded events,
     auto-scrolls. DevicePanel: isJobRunning from live SignalR status.jobTotal (was stale REST);
     isHold uses startsWith("hold") for Hold:0/Hold:1 sub-states; "Stop Job" button (soft);
     "Unlock ($X)" button shown only in Alarm state; E-Stop keeps Ctrl-X behaviour;
     JobLogPanel embedded in panel when connected. -->
**Paste this:**
> Read the plan and CLAUDE.md. Add feed-hold/pause, safe stop, resume-from-pause, and a
> per-job log (lines sent, position, events, timestamps). This is the "all-in-one" moment.
> Stop and summarize.

---

## Milestone 4 — Power-Loss Recovery & Machine-Type Modes  ⚠️ hard / careful
### [x] Task 11 — Power-loss recovery
<!-- Done: CheckpointService persists job checkpoints to %LOCALAPPDATA%/diy-grbl-cam/
     two-file layout: job-checkpoint-gcode.txt (written once on job start) +
     job-checkpoint-meta.json (updated every 50 lines, on FeedHold, on error/stop).
     Checkpoint cleared on clean completion; retained on stop/error/power-loss.
     BuildRecoveryGcode: finds the last M3 at or before lastLineDone, scans back to
     its preceding G0 (rapid-to-position), returns preamble + everything from that G0
     so the interrupted cut re-runs from scratch (fresh pierce — correct for plasma).
     REST: GET /api/machine/recovery (info + resumeFromLine), POST /api/machine/
     recovery/start (verifies Idle, builds + streams recovery G-code), DELETE
     /api/machine/recovery (dismiss checkpoint). MachineConnectionManager: injects
     CheckpointService, writes checkpoint at start+progress+events.
     Frontend: RecoveryPanel shows job summary + 3-step guided recovery (Home →
     Set Zero confirmation → Start Recovery); gated — Start Recovery requires Idle
     state + user confirmation of zero. Checkpoint info loaded on panel mount.
     8 unit tests in CheckpointServiceTests.cs. Deferred: recovery across app restarts
     needs no extra work (files persist); multi-session job ID tracking not needed
     for MVP. -->
> Persist job checkpoint (line + position); on restart offer safe recovery: re-home,
> re-establish position, re-pierce, resume from a safe point. Treat with extreme care.
> Summarize, compact and commit. 

### [x] Task 12 — Laser mode
<!-- Done: MachineType enum (Plasma/Laser) + LaserPowerPercent field added to CamSettings.
     CamEngine: when OperationMode==Laser, skips kerf offsetting (paths used as-is) and
     skips lead-in/out; PierceDelayS forced to 0. Cut ordering still applied.
     GrblLaserPostProcessor: Id="grbl-laser", M3 S{power} (0–1000 scale from
     LaserPowerPercent), M5 off, no G4 pierce, header comment reminds user to set $32=1.
     Registered alongside plasma post in DI; GcodePanel post-picker shows both when
     backend returns 2+ processors. Frontend CutSettingsCard: Machine type selector at
     top (Plasma/Laser); Laser mode shows Feed + Power %, hides all plasma-only fields
     (kerf, pierce, height, leads, profile picker). project.ts CamSettings type updated.
     9 unit tests in GrblLaserPostProcessorTests.cs + 2 laser CAM tests in CamEngineTests.
     Deferred: per-layer engrave/score vs cut assignment (Task 19); M4 dynamic-power mode
     option (user can edit post-output manually; $32=1 note in header covers the basics). -->
> Add a laser operation mode + laser post-processor (beam on/off + power %, no kerf/pierce).
> Summarize, compact and commit. 

### [x] Task 13 — Vinyl / drag-knife mode
<!-- Done: VinylKnife added to MachineType enum. DragKnifeCompensator (backend/Cam/)
     transforms design paths to machine pivot paths: blade trails behind pivot by
     VinylBladeOffsetMm; at each corner the pivot sweeps a small arc around the corner
     vertex to re-align the blade (15°/step, threshold 5°); closed contours extended by
     VinylOvercutMm to ensure clean closure. CamEngine: vinyl mode skips kerf offsetting
     and leads, applies compensator in step 5, marks cut as non-closed.
     GrblVinylPostProcessor: knife up/down via G0 Z, no M3/M5 spindle, G1 cuts along
     compensated pivot path; registered in Program.cs.
     Frontend: VinylKnife added to MachineType type; CamSettings extended with
     vinylBladeOffsetMm, vinylOvercutMm, vinylKnifeUpMm, vinylKnifeDownMm; CutSettingsCard
     shows vinyl-specific field set with hint text.
     Tests: 8 DragKnifeCompensatorTests, 3 CamEngine vinyl tests, 6 GrblVinylPostProcessorTests
     (run after backend restart — running exe locks the dll).
     Committed as Task 13. Milestone 4 complete. -->
> Add a drag-knife operation mode: knife up/down (Z or servo), blade-offset + overcut
> compensation for sharp corners. No kerf/pierce/THC.
> Summarize, compact and commit. 

---

## Milestone 5 — xTool Studio feature parity (user-requested)
*Bring in xTool Studio's design/editing functionality so xTool/XCS users feel at home.
Reference: https://support.xtool.com/academy/course?id=6,
https://support.xtool.com/article/2409, and the full learning center at
https://support.xtool.com/learning-center?campaign=support_academy&node=c8007d78-ec47-49e2-b168-b32e37c3b387.
Task 6b covers the workspace layout; these tasks cover the functionality.
Each is one session; split further if a task grows.*

### [x] Task 14 — Shape, pen & text creation tools
<!-- Done: Left-sidebar shape tools: line, rectangle (corner radius), circle, ellipse,
     polygon (n-sides), star (points + inner ratio) — click+drag rubber-band on canvas;
     preview drawn during drag. Pen tool: full Bézier state machine — click for corner
     nodes, drag to pull smooth handles, hover-first-node shows close indicator, Enter
     finishes open path, Escape commits, click first node closes. Text tool: click canvas
     → floating TextPanel (text/font/size mm/letter-spacing) → opentype.js v2
     (fetch+parse buffer) converts glyphs to polylines via adaptive Bézier flattening,
     Y-flipped, bbox normalized to (0,0), scaled to fontSizeMm. All shapes use the
     "synthetic file" pattern: POST /api/project/files/synthetic creates an ImportedFile
     with Kind=Shape + auto-placed Part; InitialX/Y optionally overrides placement.
     ActiveTool union type in tools.ts; ShapeGen.ts: genLine/genRect/genCircle/genPolygon/
     genStar + fromPoints convenience wrappers. Roboto-Regular.ttf + Roboto-Bold.ttf
     bundled in /public/fonts/. opentype.js v2 via npm. shadcn Popover added.
     Deferred: bold/italic font variants in TextPanel (bold field exists, only regular
     bundled); editable W/H/rotation on shapes requires Task 15 node editing. -->
> Left-sidebar creation tools on the canvas: line, rectangle (with corner radius),
> circle/ellipse, polygon, star; **Pen tool** (Bézier path drawing — click for corner
> nodes, drag to pull smooth handles, close path to form a shape; output is the same
> polyline/curve geometry as imported SVG/DXF so CAM picks it up unchanged); text
> objects with font selection, size, style, letter/line spacing, and convert-text-to-paths
> so CAM consumes outlines (closes the current "<text> not supported" import warning).
> Objects use the existing part-transform model.
> Summarize, compact and commit. 

### [x] Task 15 — Object editing: precise transforms, mirror, group, offset
<!-- Done: Part model extended with ScaleX/ScaleY (both default 1.0) on backend.
     partToWorld/worldToPartLocal in geometry.ts updated: scale applied around pivot
     before rotation (negative scale = mirror, consistent with rotate model).
     Backend: PATCH /parts/{id} now accepts scaleX/scaleY; POST /parts/{id}/reorder
     moves part in stacking order (up/down/front/back); POST /parts/{id}/offset creates
     a new part+file by inflating the source geometry via KerfOffsetter.OffsetClosed
     (closed paths) or Clipper.InflatePaths with EndType.Round (open paths). Duplicate
     copies ScaleX/ScaleY. Frontend: Part type gains scaleX/scaleY; projectApi gets
     reorderPart + offsetPart; EditToolbar fully implemented: numeric X/Y (world bbox
     min), W/H with aspect-lock toggle (computes scaleX/Y from naturalSize), rotation,
     rotate±90, mirror H/V (toggles sign of scaleX/Y), stacking order ×4, align ×6,
     contour-offset input (±mm, Enter to apply), duplicate, delete.
     Deferred: group/ungroup — requires multi-select which was deferred in Task 2;
     flagged, not silently added. -->
> Floating-toolbar functionality: numeric X/Y/W/H/rotation entry with aspect lock,
> mirror horizontal/vertical, group/ungroup, stacking order, and contour offset
> (Clipper2 — reuse the kerf offsetter, don't hand-roll).
> Summarize, compact and commit. 

### [ ] Task 16 — Vector node editing + path operations
> Node-level editing of imported/created paths: move/add/delete nodes, node types
> (corner/smooth), path simplification, split path (scissors). Pathfinder booleans via
> Clipper2: unite, subtract, intersect, weld overlapping text/shapes.
> Summarize, compact and commit. 

### [ ] Task 17 — Arrays & material test grid
> Grid array and circular array of parts (drives the same part-transform model as
> nesting). Material test array generator: a grid of test cuts varying two parameters
> (e.g. feed × pierce delay for plasma; power × speed once laser mode exists) to dial
> in material profiles — xTool's "array test" equivalent for finding good settings.
> Summarize, compact and commit. 

### [ ] Task 18 — Bitmap import + trace to vector
> Import PNG/JPG, auto-trace (and center-line trace) to vector paths for cutting.
> Image filters/adjustments (grayscale, invert, brightness/contrast, halftone/dither)
> matter mainly for laser engraving — implement alongside or after Task 12 (laser mode),
> where engrave processing actually consumes them.
> Summarize, compact and commit. 

### [ ] Task 19 — Templates, element library, canvas QoL & efficiency tools
> Project templates and a reusable element/shape library; canvas light/dark toggle,
> grid show/hide, snap settings UI; per-object processing-mode assignment UI
> (cut/engrave/score per layer or object — wires the existing per-layer provenance
> into operations). **Efficiency tools** (xTool Studio "Design Editing > Efficiency
> Tools" section): smart fill (flood-fill a closed region to create a cut path),
> object measurement/ruler overlay, step-and-repeat / quick-duplicate with offset,
> batch processing-parameter assignment across a selection.

> Out of parity scope (flag if requested): AImake/AI image generation, xTool account
> login, xTool-proprietary device features (smart detection, camera framing).
> Summarize, compact and commit. 

---

## Working agreement for Claude Code
- One task per session. Read the plan + CLAUDE.md first.
- Don't pull later tasks forward. Flag scope creep instead of acting on it.
- Tick the box here and note deferrals when you finish a task.
