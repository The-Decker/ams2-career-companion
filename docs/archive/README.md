# Archived docs

Superseded planning documents, kept for history (nothing here is current). The **living** state
of the project lives in three places:

- **`CLAUDE.md`** (repo root) — the project's standing instructions + the locked decisions.
- **`PLAN.md`** (repo root) — the founding product vision (2026-07-02). Still the north star for
  scope; the *implemented* state has moved well beyond it (career hub, character system, SMGP
  replica mode). Kept as origin context.
- **The auto-memory** (`MEMORY.md` → `ams2-hub-build-progress.md`) — the day-by-day build log,
  branch/RC state, and what's next. This is the true progress tracker.
- **`docs/dev/`** — the durable design specs (`smgp-design.md`, `career-hub-design.md`,
  `character-system.md`, the audits, etc.).

## What's here and why it was archived

| File | Was | Archived because |
|---|---|---|
| `HANDOFF.md` | Laptop→desktop handoff, "zero code written yet" (2026-07-02) | 200+ commits since; obsolete. |
| `ROADMAP.md` | Progress-vs-PLAN tracker, "suite 785/785" (2026-07-03) | The auto-memory is now the living progress log. |
| `PIPELINE-0.4.0.md` | v0.4.0 increment ladder | Self-marked SUPERSEDED (2026-07-05) by `docs/dev/career-hub-build.md`. |
| `NEXT-SESSION-continue.md` | A next-session resume prompt (2026-07-05) | The hub build it pointed at is done. |
| `NEXT-SESSION-hub-design.md` | The 21-question hub-design prompt (2026-07-05) | The hub is designed + built. |
| `NEXT-SESSION-megaprompt.md` | "Content deepening" resume prompt (2026-07-10, head `99a63c5`) | Superseded — the current resume prompts are `SMGP-CONTINUE.md` (Claude/SMGP) + `CODEX-1967-BRIEF.md` (Codex/1967). |
| `career-hub-vision.md` | Pre-build hub "vision" seed (2026-07-03, "design round pending") | The hub is designed + built (`career-hub-design.md` / `career-hub-build.md`). |
| `character-system-vision.md` | Pre-build character "vision" seed (2026-07-03) | The character system is built (`character-system.md`). |

The current resume prompts are **`SMGP-CONTINUE.md`** (Claude's SMGP lane) and
**`CODEX-1967-BRIEF.md`** (Codex's 1967 lane), both at the repo root.
