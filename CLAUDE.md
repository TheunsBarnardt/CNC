# CLAUDE.md — Instructions for Claude Code

This file is read automatically. Follow it on every task in this repo.

## What this project is
A free, all-in-one desktop app for **DIY GRBL-based 2D cutting machines** (plasma first;
laser, vinyl/drag-knife, and router later). It takes a project from imported vector art
(SVG/DXF) → arrange/nest → toolpaths → simulate → cut directly via a GRBL serial connection.

**Read `PLASMA_CAM_PLAN.md` for full context before starting any task.** It is the source
of truth for scope, stack, milestones, and architecture.

## Golden rules
1. **Stay in scope.** Build only what the current task asks. Do NOT jump ahead to later
   milestones. If a task seems to need out-of-scope work, stop and flag it instead of
   silently expanding.
2. **Check the plan before adding scope.** If something isn't in `PLASMA_CAM_PLAN.md`,
   ask before adding it.
3. **Safety-critical domain.** This app eventually fires a plasma torch / laser. Any
   machine-control code must be conservative: no motion without explicit user action,
   E-stop always wins, validate against table bounds. Never auto-run a cut.
4. **One feature per task.** Keep PRs/commits focused and reviewable.
5. **Update `TASKS.md`** — tick off the item you completed and note anything deferred.

## Tech stack (do not substitute without asking)
- **Frontend:** AvaloniaUI (native .NET desktop, Fluent theme, MVVM, compiled bindings)
- **Backend:** C# class library (no HTTP/Web APIs — services accessed directly via DI)
- **Frontend ↔ Backend:** Direct DI injection (in-process, single executable)
- **Rendering:** SkiaSharp for viewport/graphics
- **Geometry:** Clipper2 (C#) for offsetting/kerf. Don't hand-roll polygon offsetting.
- **Serial:** System.IO.Ports, behind an interface with a fake implementation for testing
- **Packaging:** Single self-contained `.exe` or platform-specific bundle

## Architecture conventions
- **Neutral toolpath model.** CAM produces controller-agnostic geometry; post-processors
  translate to a dialect (GRBL default). Adding a controller = adding a post-processor,
  not editing the CAM engine.
- **Machine-type operation modes** (plasma / laser / vinyl / router) are pluggable. Don't
  hard-code plasma assumptions into shared code.
- **Placement model is nesting-ready.** Manual drag-place and future auto-nest share the
  same part-transform model.
- **Serial behind an interface.** `IMachineConnection` (or similar) with a `FakeMachine`
  implementation so the whole app is testable without hardware.

## Repo layout
```
/                     repo root
  PLASMA_CAM_PLAN.md  full design doc (source of truth)
  CLAUDE.md           this file
  TASKS.md            milestone task checklist
  README.md           how to run
  /backend            C# class library (CAM engine, machine control, I/O)
  /desktop            AvaloniaUI frontend (native .NET desktop app)
```

## Commands

**Run the app (dev):**
```bash
cd desktop
dotnet run
```

**Build for release:**
```bash
cd desktop
dotnet publish -c Release -o bin/Release
```

**Build backend only (class library):**
```bash
cd backend
dotnet build
```

**Restore tests (when re-added):**
```bash
dotnet test
```

**Notes:**
- No separate terminal for backend — it runs in-process with AvaloniaUI
- No web browser needed — native desktop only
- No HTTP/REST endpoints — services consumed directly via DI

## Coding standards
- **C# (backend + AvaloniaUI):** nullable reference types on, async where I/O is involved
- **AvaloniaUI:** MVVM pattern; view models inherit from `ReactiveObject` or similar; use compiled bindings
- **XAML:** keep simple; complex logic belongs in code-behind or view model
- Keep components/controls small; group related state; lift only when shared
- Comment the *why* for any non-obvious geometry/serial logic or machine-control behavior
- Prefer clarity over cleverness — this is a community tool others will read

## When unsure
Ask, or leave a clearly-marked `// TODO(plan):` and mention it in your summary. Do not
guess on machine-control or safety behavior.
