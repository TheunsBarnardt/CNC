# KICKOFF — First message to paste into Claude Code

Open this folder in Claude Code, then paste the block below as your very first message.
After it finishes Task 0, work through `TASKS.md` one task per session.

---

You're helping build a free, all-in-one desktop app for DIY GRBL-based 2D cutting machines
(plasma first; laser, vinyl, router later).

Before doing anything:
1. Read `PLASMA_CAM_PLAN.md` (full design doc — the source of truth).
2. Read `CLAUDE.md` (conventions and guardrails).
3. Read `TASKS.md` (the build sequence).

Then do **Task 0 only** (Project skeleton) from `TASKS.md`:
- `/frontend`: Vite + React + TypeScript + Tailwind + shadcn/ui, with one demo page that
  calls the backend health endpoint and shows the result.
- `/backend`: ASP.NET Core Web API with `GET /api/health` and a SignalR hub at
  `/hubs/machine` that broadcasts a heartbeat every few seconds.
- Add `IMachineConnection` + a `FakeMachineConnection` the hub uses to emit fake status.
- Wire CORS for the frontend dev port.
- Update `README.md` and the Commands section of `CLAUDE.md` with exact run steps.
- Tick Task 0 in `TASKS.md`.

Confirm both apps run and the frontend shows a live heartbeat from the backend, then STOP
and summarize. Do not start Task 1 or build any CAM features. Stay strictly in scope; if
something seems to require out-of-scope work, flag it instead of expanding.
