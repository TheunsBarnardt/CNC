# Project Plan — Free DIY CNC Plasma CAM + Control App

*"The free xTool for DIY CNC builders" — design, simulate, and cut, all in one app.
For DIY GRBL machines: plasma, laser, vinyl, and router.*

---

## 1. Vision

A single, modern, free desktop application for DIY CNC plasma builders that takes a
project from imported artwork all the way to a finished cut — without forcing the user
to bounce between separate CAD, CAM, and machine-control programs.

The app should feel as modern and approachable as today's 3D-printer slicers
(OrcaSlicer, Bambu Studio) rather than the dated industrial CNC tooling that currently
dominates the space. The long-term goal is to become the default go-to app for the DIY
CNC plasma community.

---

## 1a. What This App Is (and Is Not) — Scope Statement

**What it is:**
A free, all-in-one desktop app for **DIY GRBL-based 2D cutting machines** that takes you
from artwork to finished part. The app's real target is *any GRBL/grblHAL 2D cutting
machine*, not just plasma — the toolpath model (trace outlines, move the head along
paths) is shared across machine types. Supported machine types:

- **Plasma** (primary) — kerf compensation, lead-in/out, pierce points, THC, cut ordering.
- **Laser** — beam on/off (M3/M5 or PWM), power %; no kerf/pierce. Simpler than plasma,
  nearly free given the plasma engine.
- **Vinyl / drag-knife** — knife up/down (Z or servo), **blade-offset + overcut
  compensation** for sharp corners; no kerf/pierce/THC. A distinct "drag-knife"
  operation mode.
- **Router/engraver** (bonus) — also GRBL 2D; supported as a natural side effect.

Core workflow (all machine types):
- **Import** vector artwork (SVG, DXF) — multiple files into one project.
- **Arrange & nest** parts on a defined table/sheet.
- **Generate toolpaths** — using the operation mode for the selected machine type.
- **Simulate** the cut before running it.
- **Cut directly** — stream G-code to a supported controller, with jog, home,
  pause/stop/resume, and (later) power-loss recovery.

**Who it's for:**
DIY CNC builders running hobby/shop-built machines — the people buying controllers and
kits from AliExpress, Amazon, and local electronics shops, and **people who build their
own plasma tables, laser cutters, or vinyl cutters** around an open GRBL board.

**Supported hardware — works fully (design → cut):**
- **GRBL** boards (e.g. Arduino Uno + CNC Shield V3 + A4988/DRV8825) — the cheap,
  ubiquitous DIY standard; ideal dev/test and entry-level target. Drives DIY plasma,
  laser, router, and (with drag-knife mode) vinyl machines.
- **grblHAL** 32-bit boards (ESP32/Teensy/STM32) with **THC** support — the
  recommended choice for real plasma production.
- What matters is the **controller, not the machine** — a home-built vinyl cutter or
  laser running GRBL works because it speaks the protocol the app targets.
- Both speak GRBL-style serial over USB.

**Partly supported — CAM only (no integrated cutting):**
- **Mach3 USB motion-control boards** (closed proprietary plugin, e.g. RnRMotion/
  RATTMMOTOR USB type). Use this app to design, nest, and export G-code, then run
  the cut in Mach3. *Some of these boards can be reflashed to grblHAL to gain full support.*
- **Turnkey/proprietary machines** (e.g. AM.co.za vinyl/laser/plasma running VinylCut,
  RDWorks/Ruida, or other closed controllers) — not directly drivable. Usable as a CAM
  front-end only if their controller imports standard G-code/DXF. A DIY owner who swaps
  the control box for a grblHAL board moves into the fully-supported camp.

**Out of scope (at least for now):**
- 3D/multi-axis machining, milling toolpaths, lathe work.
- Drawing/CAD from scratch (it's a CAM + control app, not a CAD program — bring your
  SVG/DXF).
- Proprietary closed controllers that don't expose an open serial protocol.

**It is not** a CAD program, a 3D printer slicer, or a replacement for Mach3/LinuxCNC
on machines that depend on them. It is a focused **GRBL 2D-cutting CAM-and-cut tool**
(plasma, laser, vinyl, router).

---

## 2. Guiding Principles

1. **One app, whole workflow** — import → arrange → CAM → simulate → cut.
2. **Modern, friendly UX** — clean viewport, instant-update settings, sensible defaults.
3. **Safety first** — plasma fires a torch; lasers carry eye/burn risk. E-stop, hard
   limits, and careful resume logic are first-class concerns, not afterthoughts.
4. **Free** — no per-seat licensing wall like SolidWorks/SheetCAM.
5. **Pluggable** — controllers, post-processors, and **machine-type operation modes**
   (plasma / laser / vinyl / router) are swappable modules.

---

## 3. Technology Stack

| Layer | Choice | Why |
|-------|--------|-----|
| Frontend UI | React + shadcn/ui (Tailwind) + Vite | Modern look, fast dev, component ecosystem |
| Viewport | Canvas 2D or Three.js (in React) | Table view, part placement, toolpath sim |
| Backend | C# / ASP.NET Core | Strong serial/hardware support, CAM math, stateful job/resume logic |
| Frontend ↔ Backend | REST (normal calls) + SignalR WebSocket (live cut streaming) | Real-time position/status during a cut |
| Geometry engine | Clipper2 (C#) | Industry-standard polygon offsetting — exactly what kerf comp needs |
| Serial comms | System.IO.Ports (C#) | Mature, well-documented GRBL streaming |
| Packaging | Desktop shell bundling React + C# as one installable app | "All-in-one like xTool" feel |
| Default post-processor | GRBL-style plasma | Open, documented, solo-doable |

**Why desktop, not pure web:** machine control needs direct USB/serial hardware
access, which a browser tab cannot do reliably. This is why OpenBuilds ships two
apps (web CAM + desktop CONTROL). We unify them into one desktop app.

---

## 4. Controller Strategy — RESOLVED

The DIY controller market (AliExpress, etc.) splits into two camps. This decides which
boards get the full all-in-one experience:

### ✅ GRBL / grblHAL boards — FULL support (the target)
- Serial-based, open protocol, typically CH340 USB chip, 12-24VDC.
- Our C# backend streams G-code directly to these. Live control + resume all work.
- Cheap and everywhere on AliExpress. The plasma/THC community already favors grblHAL.
- **This is the primary target. We publish a "known-good boards" buy list** (like the
  slicer world's "supported printers"), steering users toward open hardware — which
  also fits the free/DIY mission.

### ❌ Mach3 USB motion-control boards — CAM only, no integrated cutting
- e.g. the RATTMMOTOR/RnRMotion-style USB boards (STM32-based).
- These use a **closed, proprietary USB protocol** and require Mach3 + a vendor `.dll`
  plugin. There is no public protocol to stream to — only Mach3 can drive them.
- We **cannot** do integrated cutting for these. Reverse-engineering each vendor blob
  is not viable.
- **Fallback:** these users still use our app as a modern CAM tool (import, nest,
  generate G-code) and run the actual cut through Mach3. They get Milestones 1–2,
  just not the integrated-control half.
- Note: some of these boards expose a programming port and **can be reflashed to
  grblHAL** — we can document this as a path into the supported camp.

**Bottom line:** Target GRBL/grblHAL for the all-in-one dream; serve everyone else as a
best-in-class CAM front end. This sharpens the strategy rather than limiting it.

---

## 5. Milestones

Each milestone ships something genuinely usable on its own.

### Milestone 1 — CAM Core *(the real v1)*
A modern, useful plasma CAM tool. Does everything except auto-arrange and live cutting.
Built inside the desktop shell from day one so we're control-ready.

### Milestone 2 — Auto-Nesting
Automatic part arrangement to save material. Lean on existing open-source nesting
(SVGnest / DeepNest core, both JS) rather than writing packing math from scratch.

### Milestone 3 — Machine Control (GRBL)
The "all-in-one" moment. Connect via serial, jog, home, run job, live position view,
pause / stop / resume-from-pause, job log.

### Milestone 4 — Power-Loss Recovery & Job History
The hard, careful one. Persist exact line/position, safe re-home + re-pierce, recovery.
Treated with humility — even commercial controllers struggle here, and getting it
wrong risks ruined parts or safety incidents.

---

## 6. Detailed Feature Spec — Milestone 1

### Project & File Management
- Create / save / load a project (all settings + imported files in one project file)
- Drag-and-drop import of multiple **SVG** and **DXF** files at once
- File list panel: visibility toggle, delete, rename, re-import/refresh
- Recent projects list

### Table & Sheet Setup
- Global table size (width × height = machine bed)
- Units toggle (mm / inch)
- Origin / zero point selection (corner or center)
- Sheet / material thickness

### Canvas / Viewport *(must feel modern)*
- 2D top-down view of the table with all parts placed
- Smooth pan, zoom, fit-to-view
- Move / rotate / duplicate parts
- Snap, align, grid
- Show cut direction + travel (rapid) moves distinctly

### CAM — Toolpath Generation *(OpenBuilds-minimum baseline)*
- Inside / outside / on-line cut selection per path
- **Kerf compensation** (offset by half kerf width via Clipper2) — critical for plasma
- **Lead-in / lead-out** (line or arc)
- **Pierce** point placement + pierce delay
- Cut order / sequencing (inner cuts before outer)
- Feed rate, cut height settings
- Multiple operations per project with independent settings

### G-code Output
- Generate G-code with plasma torch control (M3/M5 or M7/M8)
- **Pluggable post-processor** (GRBL default) — neutral toolpath → controller dialect
- In-app G-code preview
- **Toolpath simulation / playback** (scrub through the cut like a video)
- Export / download .nc / .gcode

### Material Profiles
- Save presets per material + thickness (feed rate, pierce delay, kerf, cut height) —
  like slicer filament profiles

---

## 7. Architecture Sketch

```
┌─────────────────────────────────────────────┐
│  Desktop Shell (one installable app)          │
│                                               │
│  ┌─────────────────────┐   ┌───────────────┐ │
│  │  React + shadcn UI   │   │ C# ASP.NET    │ │
│  │  - viewport          │◄─►│  Core backend │ │
│  │  - settings panels   │REST│               │ │
│  │  - sim playback      │ + │  - CAM engine │ │
│  │  - file mgmt         │ WS │  (Clipper2)   │ │
│  └─────────────────────┘   │  - post-proc  │ │
│                             │  - serial I/O │ │
│                             │  - job/resume │ │
│                             └──────┬────────┘ │
└────────────────────────────────────┼──────────┘
                                      │ USB serial
                                      ▼
                              ┌───────────────┐
                              │  Controller   │
                              │  (GRBL, TBD)  │
                              └───────────────┘
```

**Key design decisions baked in from the start:**
- **Neutral toolpath model**: CAM engine produces controller-agnostic geometry;
  post-processors translate to dialect. Adding a controller = adding a post-processor.
- **Placement system is nesting-ready**: manual drag-place in M1 uses the same
  part-transform model that auto-nesting will drive in M2 — no rework.
- **Job state is persistable from M3 onward**: control layer designed so the
  "current line + position" can be checkpointed for M4 recovery.

---

## 8. Safety Notes (carry through every milestone)

- Hardware E-stop must always override software.
- Soft limits / table bounds checking before any motion.
- Explicit, deliberate confirmation before torch-on operations.
- Resume-after-power-loss must re-home and re-establish position before re-piercing.
- Treat the control + recovery milestones as the ones needing the most testing.

---

## 9. Immediate Next Steps

1. **Adopt GRBL/grblHAL as the supported-control target**; start a "known-good boards" list.
2. Confirm desktop shell choice for packaging the C# + React bundle.
3. Stand up the skeleton: React+shadcn+Vite frontend, ASP.NET Core backend, REST+SignalR wiring.
4. First real feature: SVG/DXF import + table setup + viewport rendering.
5. Then: kerf comp + lead-in/pierce + post-processor + simulation = end of Milestone 1.
