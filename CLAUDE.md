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
- **Frontend:** React + TypeScript + Vite + shadcn/ui (Tailwind). State: keep it simple
  (React state / context first; only add a store if a task calls for it).
- **Backend:** C# / ASP.NET Core (Web API). Real-time: SignalR.
- **Frontend ↔ Backend:** REST for normal calls, SignalR hub for live machine status.
- **Geometry:** Clipper2 (C#) for offsetting/kerf. Don't hand-roll polygon offsetting.
- **Serial:** System.IO.Ports, behind an interface with a fake implementation until M3.
- **Viewport:** Canvas 2D or Three.js in React (pick per task; default Canvas 2D for the
  flat table view).
- **Packaging:** desktop shell bundling frontend + backend (decided later; keep both
  independently runnable for now).

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

## Repo layout (target)
```
/                     repo root
  PLASMA_CAM_PLAN.md  full design doc (source of truth)
  CLAUDE.md           this file
  TASKS.md            milestone task checklist
  README.md           how to run
  /frontend           Vite + React + shadcn/ui
  /backend            ASP.NET Core Web API + SignalR
```

## Commands
> Keep these current as the project grows.
- Backend dev: `cd backend && dotnet run --launch-profile http` → http://localhost:5100
  - Health: `GET http://localhost:5100/api/health`
  - SignalR hub: `http://localhost:5100/hubs/machine` (broadcasts `machineStatus` every 2s)
- Frontend dev: `cd frontend && npm install && npm run dev` → http://localhost:5173
  - Backend URL is read from `frontend/.env` (`VITE_BACKEND_URL`, defaults to :5100)
- Backend build: `cd backend && dotnet build`
- Backend tests: `cd backend.Tests && dotnet test`
- Frontend build/typecheck: `cd frontend && npm run build`
- Heartbeat smoke test (backend must be running): `cd frontend && node scripts/heartbeat-check.mjs`
- (Later) full app / packaging: TBD

## Coding standards
- TypeScript strict mode on. No `any` unless justified in a comment.
- C#: nullable reference types on, async where I/O is involved.
- Keep components small; colocate UI state; lift only when shared.
- Comment the *why* for any non-obvious geometry/serial logic.
- Prefer clarity over cleverness — this is a community tool others will read.

## When unsure
Ask, or leave a clearly-marked `// TODO(plan):` and mention it in your summary. Do not
guess on machine-control or safety behavior.
