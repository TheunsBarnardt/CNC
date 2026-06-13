# TASKS.md — Build Sequence for AvaloniaUI Desktop App

Work top to bottom. Each task is a **separate Claude Code session/prompt**. Don't batch
them — one focused task at a time gives the best results. Tick the box when done.

> For every task, Claude Code should first read `PLASMA_CAM_PLAN.md` and `CLAUDE.md`.

---

## Milestone 1 — CAM Core (the real v1)

### [ ] Task 0 — Project skeleton (AvaloniaUI + backend DI)

**Status:** ✅ Backend complete. AvaloniaUI skeleton exists but incomplete.

**Paste this:**
> Read `PLASMA_CAM_PLAN.md` and `CLAUDE.md` for full context. This task scaffolds the AvaloniaUI
> desktop app skeleton with DI wiring to the backend — no CAM features yet:
> 1. Verify AvaloniaUI app `/desktop` builds and runs (`dotnet run`).
> 2. Confirm backend class library `/backend` loads into the app via DI.
> 3. Create stub MainViewModel and MainWindow.xaml with basic layout:
>    - Title bar (app name, version)
>    - Main content area (placeholder for viewport)
>    - Right-side panels (placeholder stubs for files, settings, device)
> 4. Wire the backend services (ProjectService, FileImportService, etc.) into the DI container
>    in `App.axaml.cs`.
> 5. Update `README.md` and `CLAUDE.md` — confirm `cd desktop && dotnet run` launches the app.
> Stop and test; no CAM features yet.

### [ ] Task 1 — Project & file model + SVG/DXF import

**Status:** ✅ Backend complete (SVG, DXF, bitmap import). AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. Implement the project/file panel UI in AvaloniaUI:
> - Backend: Project model, file import, geometry parsing (all complete; verify no regressions)
> - Frontend (AvaloniaUI):
>   - A **FilesPanel** view model and XAML: list imported files with visibility toggle, rename, delete
>   - **File summary:** show entity count (paths, circles, etc.) and import warnings per file
>   - **ProjectSettings** view model: units (mm/inch), table size, origin, thickness; persist to JSON
>   - **Import UI:** file picker to import SVG/DXF/PNG files; drag-drop onto main area (deferred to Task 2)
> - Services: Inject FileImportService, ProjectService into view models
> - No viewport rendering yet (Task 2). Just lists/forms to confirm parsing works.
> Stop and summarize.

### [ ] Task 2 — Table viewport + part placement (SkiaSharp canvas)

**Status:** ✅ Backend placement model complete. AvaloniaUI viewport skeleton exists but unpainted.

**Paste this:**
> Read the plan and CLAUDE.md. Build the 2D table viewport in AvaloniaUI using SkiaSharp:
> - **ViewportControl (SkiaSharp):** renders table (grid, origin marker), parts as transformed polylines
> - **Camera:** pan (drag), zoom (wheel at cursor), fit-to-view, grid toggle
> - **Selection & Interaction:**
>   - Click to select part (highlight with outline)
>   - Drag to move (with snap-to-grid, configurable 1–50mm)
>   - Drag corner circle to rotate (shift=15° increments)
>   - Arrow keys nudge selected part
>   - Delete key removes selected part
> - **EditToolbar:** floating panel over viewport with X/Y inputs, rotation, ±90° buttons, duplicate, delete
> - **Alignment:** snap to table edges/center; visual feedback during drag
> - Out-of-bounds parts stroke red
> - Backend calls: Use DirectlyInjected ProjectService to fetch/patch parts
> Keep CAM out of this task. Stop and summarize.

### [ ] Task 3 — CAM engine: kerf, lead-in/out, pierce, ordering

**Status:** ✅ Complete and tested. No UI work needed; backend-only.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend is already complete.** Verify:
> - CamEngine.cs handles kerf (Clipper2), lead-in/out, pierce, cut ordering
> - ContourClassifier, KerfOffsetter, LeadBuilder, CutOrderer all present
> - Per-layer operation mode resolution (Cut/Score/Engrave)
> - Unit tests exist and pass (if restored)
> Run `dotnet build` and verify no regressions. No changes needed. Move to Task 4.

### [ ] Task 4 — Pluggable post-processor + GRBL output + G-code preview

**Status:** ✅ Backend complete (plasma, laser, vinyl posts). AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend is complete.** Add AvaloniaUI UI:
> - **GcodePanel** view model + XAML:
>   - Button: "Generate G-code" (calls backend CamEngine + PostProcessorRegistry)
>   - Post-processor selector (dropdown): GrblPlasma, GrblLaser, GrblVinyl
>   - Stats display: line count, estimated time, pierce count, total cut length
>   - Warnings list (colored text if any)
>   - Monospace code preview (scrollable, read-only)
>   - Button: "Download .nc" (save to file)
> - Inject PostProcessorRegistry, ProjectService into view model
> Keep it simple — no editing of G-code post-output yet (deferred). Stop and summarize.

### [ ] Task 5 — Toolpath simulation / playback (SkiaSharp overlay)

**Status:** ✅ Backend simulation logic complete (Simulation.cs). AvaloniaUI rendering needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend Simulation.cs exists.** Add AvaloniaUI visualization:
> - **SimulationBar** view model + XAML (floating panel under viewport):
>   - Button: "Simulate" (fetch toolpath from backend, build time-indexed segments)
>   - Play/Pause/Stop buttons, scrub slider (time), speed selector (0.5x–16x)
>   - Display: elapsed / total time
>   - Button: "Regenerate" (rebuild sim if parts changed)
> - **Viewport overlay rendering (SkiaSharp):**
>   - Rapid moves: dashed grey lines
>   - Cut moves: orange solid, leads lighter
>   - Direction arrows per cut (showing travel direction)
>   - Torch head: glowing circle when on, crosshair when off
>   - Alpha blend: done portion brighter, remaining dimmer
> - Controls hidden if simulation not active; auto-close button
> Deferred: auto-invalidate stale sim on part changes (manual regenerate for now).
> Stop and summarize.

### [ ] Task 6 — Material profiles

**Status:** ✅ Backend complete (ProfileStore, persistence). AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend ProfileStore exists.** Add AvaloniaUI UI:
> - **CutSettingsCard** view model + XAML:
>   - Profile selector (dropdown): lists all saved profiles
>   - Select action: applies profile values (kerf, feed, pierce delay, cut/pierce height) to project CAM
>   - Editable CAM fields: feed rate, kerf, pierce delay, cut height, pierce height
>   - Laser power % (for laser mode)
>   - Lead type selector (line/arc), lead length
>   - Save button: "Save current as new profile" (prompts for name + material)
>   - Update/Delete buttons (for selected profile)
> - Inject ProfileStore, ProjectService into view model
> - Thickness from table settings (read-only in profile)
> This completes Milestone 1. Stop and summarize the end-to-end workflow: import → arrange → CAM → G-code → simulate.

---

## Milestone 2 — Auto-Nesting

### [ ] Task 7 — Auto-nesting

**Status:** ✅ Backend Nester.cs complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend Nester exists.** Add AvaloniaUI UI:
> - **NestPanel** view model + XAML (popover in right panel):
>   - Nesting settings:
>     - Margin (mm): buffer around table edge
>     - Spacing (mm): gap between placed parts
>     - Rotation candidates: checkboxes for 0°, 45°, 90°, 180°
>   - Button: "Nest" → calls backend, updates part positions
>   - Results display:
>     - "X of Y parts placed"
>     - Per-part warnings (too small, couldn't fit, etc.)
> - Inject ProjectService, Nester into view model
> - Manual placement still works; nesting is optional
> Stop and summarize.

---

## Milestone 3 — Machine Control (GRBL) ⚠️ safety-critical

### [ ] Task 8 — Real serial connection (replace fake)

**Status:** ✅ Backend SerialMachineConnection complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend SerialMachineConnection exists.** Add AvaloniaUI UI:
> - **DevicePanel** view model + XAML:
>   - Port selector (dropdown): "Refresh" button to list available ports
>   - Baud rate selector (default 115200)
>   - Connect/Disconnect buttons
>   - Connection status: green "Connected" or red "Disconnected"
>   - Live DRO (digital readout): X, Y, Z position (updated live from MachineConnectionManager)
>   - When connected: show "Idle" / "Run" / "Alarm" state
> - Inject MachineConnectionManager into view model, subscribe to StatusChanged events
> - No motion commands yet (Task 9). Just connection + status display.
> Stop and summarize.

### [ ] Task 9 — Jog, home, set zero, run job

**Status:** ✅ Backend motion logic complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend jog/home/run exists.** Add AvaloniaUI UI:
> - **DevicePanel** extensions:
>   - **Jog controls:** XY grid (8 arrow buttons), separate Z ±/- buttons
>   - Step size selector: 0.1, 1, 10, 100 mm
>   - Buttons: Home, Set Zero
> - **Job control** (only shown when connected):
>   - "Run Job" button (disabled if job not loaded)
>   - Progress bar + line counter (e.g., "125 / 500 lines")
>   - "Feed Hold" button (toggles pause)
>   - "Resume" button (only when paused)
>   - "E-Stop" button (red, destructive — Ctrl+X)
>   - "Disconnect" button
> - Inject MachineConnectionManager, subscribe to job progress events
> - Jog hidden during active job (safety)
> - Safety: E-stop always wins; no auto-motion
> Stop and summarize.

### [ ] Task 10 — Pause / stop / resume-from-pause + job log

**Status:** ✅ Backend job log + pause/resume complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend JobLogEntry and pause/resume exist.** Add UI:
> - **JobLogPanel** view model + XAML (dockable in right panel during job):
>   - List of job events (scrollable):
>     - Timestamp, Event type (Started/Progress/FeedHold/Resumed/Completed/Error/Stopped)
>     - Line number, X/Y/Z position, optional message
>   - Color coding: green (Started/Completed), yellow (Progress), orange (FeedHold), red (Error/Stopped)
>   - Auto-scroll to latest entry
> - **DevicePanel** extensions:
>   - "Stop Job" button (soft stop — cancel streaming, machine drains buffer, goes Idle)
>   - "Unlock ($X)" button (only shown in Alarm state, clears alarm)
> - Poll backend job log every 2s during active job; final fetch on completion
> - Inject MachineConnectionManager, ProjectService into view model
> Stop and summarize.

### [ ] Task 11 — Power-loss recovery

**Status:** ✅ Backend CheckpointService complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend CheckpointService exists.** Add AvaloniaUI UI:
> - **RecoveryPanel** view model + XAML (appears on app startup if checkpoint exists):
>   - Job summary: start time, last line done, total lines
>   - Guided recovery (3 steps):
>     1. "Home" button (runs homing sequence)
>     2. "Set Zero" button (user confirms machine is at correct position)
>     3. "Start Recovery" button (builds + streams recovery G-code from last safe point)
>   - Status display (grayed out steps, highlights current step)
>   - "Dismiss Checkpoint" button (if user doesn't want to recover)
> - Recovery only allowed when machine is Idle
> - Inject MachineConnectionManager, CheckpointService into view model
> This completes Milestone 3 (machine control). Stop and summarize.

---

## Milestone 4 — Power-Loss Recovery & Machine-Type Modes

### [ ] Task 12 — Laser mode

**Status:** ✅ Backend laser CAM + post-processor complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend laser mode exists.** Add AvaloniaUI UI:
> - **CutSettingsCard** extensions:
>   - Machine type selector (radio buttons or dropdown): Plasma / Laser / VinylKnife
>   - Laser mode shows: Feed rate + Power % (0–100)
>   - Laser mode hides: Kerf, Pierce delay, Pierce height, Lead-in/out, Profile picker
> - Inject ProjectService, CamSettings into view model
> - CAM engine automatically skips kerf/leads in laser mode
> - Laser post-processor selected automatically when laser mode chosen
> Stop and summarize.

### [ ] Task 13 — Vinyl / drag-knife mode

**Status:** ✅ Backend vinyl CAM + post-processor complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend vinyl mode exists.** Add AvaloniaUI UI:
> - **CutSettingsCard** extensions (Vinyl/Drag-Knife mode):
>   - Shows: Feed rate, Blade offset (mm), Overcut (mm), Knife up Z, Knife down Z
>   - Hides: Kerf, Pierce delay, Laser power, Leads
> - Inject ProjectService, CamSettings into view model
> - CAM engine applies DragKnifeCompensator in vinyl mode
> This completes Milestone 4. Stop and summarize.

---

## Milestone 5 — xTool Studio Feature Parity

### [ ] Task 14 — Shape, pen & text creation tools

**Status:** ✅ Backend shape/text generation complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend synthetic file creation exists.** Add AvaloniaUI UI:
> - **CreationSidebar** (left panel) with toggle buttons:
>   - Line, Rectangle, Circle, Ellipse, Polygon, Star, Pen, Text
> - **Shape tools:** click+drag on viewport for rubber-band preview; release to create
>   - Rectangle: corner radius slider
>   - Polygon: n-sides spinner
>   - Star: point count + inner ratio sliders
> - **Pen tool:** Bézier state machine
>   - Click to place corner nodes
>   - Drag node to pull smooth handles
>   - Hover first node shows "close path" indicator
>   - Enter = finish open path, Escape = commit, click first node = close
> - **Text tool:** click canvas → floating TextPanel (text, font picker, size mm, letter spacing)
>   - Fonts: Roboto Regular/Bold (bundled); opentype.js converts glyphs to polylines
> - All shapes become parts (using synthetic file pattern)
> - Inject FileImportService, ProjectService into view models
> Stop and summarize.

### [ ] Task 15 — Object editing: precise transforms, mirror, group, offset

**Status:** ✅ Backend transform model complete (scale, rotation, stacking). AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend Part model has ScaleX/ScaleY; offset works.** Add UI:
> - **EditToolbar** (floating panel over viewport on selection):
>   - Precise input fields: X (world bbox min), Y, Width, Height (with aspect-lock toggle)
>   - Rotation field (degrees, 0–359)
>   - Buttons: Rotate ±90°, Mirror H, Mirror V
>   - Stacking buttons: Bring to Front, Send to Back, Up, Down
>   - Alignment buttons (6 options): align left/center-H/right, align top/center-V/bottom
>   - Contour offset input (±mm spinbox, Enter applies, creates new part via backend)
>   - Buttons: Duplicate, Delete
> - Wraps on narrow viewport
> - Inject ProjectService, direct part patching via view model
> Deferred: group/ungroup (needs multi-select). Stop and summarize.

### [ ] Task 16 — Vector node editing + path operations

**Status:** ✅ Backend node editing + booleans complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend node PATCH, simplify, boolean ops exist.** Add UI:
> - **Node edit mode:** double-click part to enter (exit: Escape or Done button)
>   - Viewport renders all path vertices as hollow circles, segment midpoints as smaller dots
>   - Click + drag node to reposition (PATCH on release)
>   - Click segment midpoint to insert node
>   - Select node, press Delete to remove (respects min constraints: 2 for open, 3 for closed)
> - **NodeEditToolbar:** X/Y fields, sharp/smooth corner toggle, Scissors (split), Simplify button, Done
> - **Pathfinder booleans:** shift-click a second part → BooleanToolbar appears
>   - Buttons: Unite, Subtract, Intersect (post-process creates new part, deletes sources by default)
> - Inject ProjectService, FileImportService into view models
> Deferred: symmetric handle constraint, multi-node selection. Stop and summarize.

### [ ] Task 17 — Arrays & material test grid

**Status:** ✅ Backend array generation + test grid complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend arrays and test grid exist.** Add UI:
> - **ArrayPanel** (popover from EditToolbar):
>   - Tabs: Grid | Circular | Test
>   - **Grid tab:** Rows, Cols spinners; auto-step (calculates spacing to fit table)
>   - **Circular tab:** Count, Radius, Start angle, Rotate with array checkbox
>   - **Test tab:** Two CAM param selectors (e.g., Feed × Pierce delay), grid rows/cols
>     - Button: "Download test G-code" (file picker, saves as .nc)
> - Array button on EditToolbar opens popover
> - Inject ProjectService, PostProcessorRegistry into view model
> Stop and summarize.

### [ ] Task 18 — Bitmap import + trace to vector

**Status:** ✅ Backend bitmap import + tracer complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend BitmapImporter + BitmapTracer exist.** Add UI:
> - **Import:** file picker accepts .png/.jpg/.bmp/.gif/.webp
> - **BitmapTraceDialog** (appears for bitmap files in file list):
>   - Mode selector: Outline / Centerline
>   - Threshold slider (binary threshold, 0–255)
>   - Filters: brightness, contrast, invert, grayscale (checkboxes or sliders)
>   - Simplify tolerance (default 0.1mm)
>   - Button: "Retrace" → updates geometry
> - **Viewport:** render bitmap preview under parts with affine transform (rotation/scale correct)
> - Cache keyed by file ID; auto-refresh on file list changes
> - Inject FileImportService into view model
> Deferred: halftone/dither (depends on per-layer engrave mode). Stop and summarize.

### [ ] Task 19 — Templates, element library, canvas QoL & efficiency tools

**Status:** ✅ Backend TemplateStore + ElementStore complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend template/library persistence exists.** Add UI:
> - **Per-layer operation mode:** LayersPanel shows dropdown per layer: Cut / Score / Engrave
>   - Engrave multiplies laser power ×0.2, feed ×1.5 (configurable)
> - **Templates button** (header):
>   - TemplateDialog: save form (name input) + list of saved templates (load/delete buttons)
>   - Load imports project snapshot (no bitmap data)
> - **LibraryPanel** (left sidebar popover):
>   - Save current part to library (name input)
>   - List of saved elements (insert button, delete button)
> - **Canvas controls** (bottom bar or viewport context menu):
>   - Grid toggle (show/hide)
>   - Snap toggle (on/off)
>   - Canvas light/dark toggle (independent of app theme)
> - **Measurement overlay:** when part selected, show W×H readout below bbox
> - Inject TemplateStore, ElementStore, ProjectService into view models
> Deferred: smart fill, batch parameter assignment (needs multi-select). This ends Milestone 5.
> Stop and summarize end-to-end: import → design → arrange → nest → CAM → simulate → cut + recover.

---

## Milestone 5 Extra (Milestone 6 if deferred)

### [ ] Task 20 (if needed) — Advanced viewport features

*Placeholder for future enhancements: angled guides, ruler, lock-all, context menus, etc.*

### [ ] Task 21 — Node edit UX: smooth/sharp nodes, Bézier handles, node toolbar, scissors

**Status:** ✅ Backend handles (Bézier control points) complete. AvaloniaUI UI needed.

**Paste this:**
> Read the plan and CLAUDE.md. **Backend PathGeometry.Handles exists; FlattenPath works.** Add UI:
> - **Node editing (extension to Task 16):**
>   - Nodes render as: circles (smooth) or squares (sharp corners)
>   - Handles render as small squares with stem lines when node selected
>   - Drag handle to adjust Bézier control point (persists to backend)
>   - Smooth/sharp toggle button on NodeEditToolbar
>   - Auto-smooth: smooth button computes Catmull-Rom tangents
> - **Scissors:** split path at selected node into two open sub-paths (persists handles)
> - Deferred: symmetric handle constraint, multi-node selection, node alignment
> Stop and verify all node editing works end-to-end.

---

## Build & Verification Checklist

After each task:
- [ ] Code compiles: `dotnet build` (both backend + desktop)
- [ ] No new compiler warnings (except known CVE warnings on SixLabors.ImageSharp)
- [ ] Feature works as described (manual test in AvaloniaUI app)
- [ ] Update TASKS.md (tick the box, note deferrals)
- [ ] Commit with clear message

---

## Working Agreement

- One task per session. Read `PLASMA_CAM_PLAN.md` + `CLAUDE.md` first.
- Don't pull later tasks forward. Flag scope creep instead of silently expanding.
- Keep AvaloniaUI focus: MVVM pattern, XAML + code-behind, direct service injection.
- Test the feature, not just the build. Safety-critical code (machine control) gets extra scrutiny.
