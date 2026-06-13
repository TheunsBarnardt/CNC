# DIY GRBL Cutting CAM — All-in-One Desktop App

A free, modern, all-in-one **native desktop app** for **DIY GRBL-based 2D cutting machines** —
plasma (primary), laser, vinyl/drag-knife, and router support. Import vector art (SVG, DXF) →
arrange & nest → generate toolpaths → simulate → cut directly via GRBL serial connection.

> Think "the free xTool for DIY CNC builders." Native desktop. No web dependency. Works offline.

## Status

**Backend (CAM + machine control):** ✅ **Complete** — Milestones 1–4 implemented.
**Frontend (AvaloniaUI):** 🏗 **In progress** — native desktop app replacing web UI.

Fully functional features (backend ready):
- **Plasma CAM engine** (Clipper2): kerf compensation, lead-in/out, pierce, cut ordering
- **Machine control (GRBL):** jog, home, run, pause/resume, soft stop, E-stop, power-loss recovery
- **Post-processors:** plasma, laser, vinyl/drag-knife (pluggable architecture)
- **Geometry tools:** SVG/DXF import, bitmap trace, shape/pen/text creation, node editing, boolean operations
- **Nesting:** auto-arrange parts on sheet, manual placement with snap/grid/align
- **Material profiles:** save/share presets per material + thickness
- **Templates & library:** reusable project templates and element collections
- **Per-layer modes:** Cut/Score/Engrave with feed and laser power overrides
- **xTool-style workspace:** left creation rail, floating edit toolbar, right panels, canvas controls

## Tech Stack

- **Frontend:** AvaloniaUI (native .NET desktop, Fluent design)
- **Backend:** C# class library (no HTTP — services accessed directly via DI)
- **Geometry:** Clipper2 (C#), SkiaSharp (rendering)
- **Serial I/O:** System.IO.Ports
- **Data persistence:** JSON to `%LOCALAPPDATA%/diy-grbl-cam/`

## Quick Start

### Running (Development)

```bash
cd desktop
dotnet run
```

Launches the AvaloniaUI app with full CAM and machine control features.

**Single executable — backend and frontend run in one process. No web browser needed.**

### Building Release

```bash
cd desktop
dotnet publish -c Release -o bin/Release
```

Creates a self-contained executable ready for distribution.

## Supported Hardware

**Full support (design → cut):**
- GRBL boards (e.g. Arduino Uno + CNC Shield V3 + A4988/DRV8825)
- grblHAL 32-bit boards (ESP32/Teensy/STM32) with THC for plasma

Any machine running GRBL/grblHAL over USB serial is fully supported.

**CAM-only (design + export G-code):**
- Mach3 USB motion-control boards (proprietary protocol; use this app as CAM, run cuts in Mach3)
- Proprietary turnkey machines (export `.nc` or `.gcode` for their control software)

## Safety

This software controls plasma torches and lasers. Machine control is conservative by design:
- Explicit user action required to start any motion
- E-stop always wins
- Bounds checking and soft limits enforced
- Never auto-runs a cut
- Power-loss recovery safely re-homes and re-establishes position before resuming

See full safety notes in `PLASMA_CAM_PLAN.md`.

## Architecture

- **Neutral toolpath model:** CAM engine produces controller-agnostic geometry; post-processors translate to G-code dialects
- **Pluggable machine modes:** Plasma/Laser/Vinyl are swappable operation modes; add a new one = new CAM logic + new post-processor
- **Persistent state:** Projects, materials, templates, job checkpoints all saved to disk
- **Direct DI:** AvaloniaUI accesses backend services directly (no network overhead)

## Development Workflow

For Claude Code users working on this project:
1. Read `PLASMA_CAM_PLAN.md` for full vision, scope, architecture
2. Read `CLAUDE.md` for conventions and guardrails
3. Check `TASKS.md` for the feature build sequence
4. One focused task per session, top to bottom

## Next Steps

- Complete AvaloniaUI viewport painting and interaction (in progress)
- Restore unit tests for CAM engine and machine control
- Create installer/packaging (NSIS or WiX)
- Expand xTool Studio parity (efficiency tools, smart fill, etc.)

## License

TBD (intended to be free and open for the DIY community).
