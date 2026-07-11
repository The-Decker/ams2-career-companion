# Next session — Career Hub design (21 questions)

Paste the block below into a FRESH thread when you wake up. It's self-contained: machine facts
auto-load from memory, the repo docs carry the rest. Everything is committed (last: v0.3.4,
13 packs, character system designed, 1032 tests green).

---

Resume the AMS2 Career Companion in `Z:\Claude Code\ams2-career-companion` (git repo, all
committed). Today's mission: **design the immersive Career Hub with me via a 21-question
conversation**, then produce an updated hub spec + a build plan. Design only this session —
no app code until I approve the direction.

First read, in order: `PIPELINE-0.4.0.md`, `docs/dev/career-hub-vision.md`,
`docs/dev/career-hub-design.md` (the synthesized hub design + its 5 open questions),
`docs/dev/character-system.md` + `data/rules/perks.json` (the NEW driver character layer — 42
balanced perks, stats, levels — that the hub must surface), and skim `PLAN.md` (8 locked
decisions) + `docs/dev/career-sim.md` (the deterministic sim the hub visualizes).

Then run **21 questions** as a real elicitation — grouped into short rounds (~3-4 per
`AskUserQuestion` call, ~6 rounds), each option with your recommendation first so I can just
pick or override. Cover these axes (fold in the already-open taste calls from
career-hub-design.md §Open Questions and character-system.md §Open choices):

1. Hub navigation shape (persistent tab rail vs dashboard vs tear-off "own windows")
2. Tab reveal (all present vs progressive unlock as the career grows)
3. Where the race-day loop lives (always-pinned spine vs a tab)
4. The "Why?" journal inspector — how central / clickable-everywhere
5. Does "Why?" also explain character/perk effects
6. Era presentation depth for v1 (full telegram/fax/email skin now vs later)
7. Whole-UI era re-skin vs just news/offers
8. Character-creation CP budget: 10 vs 12
9. Injury system in v1 or deferred (biggest new sim surface; 7 perks touch it)
10. Talent-stat honesty: pure-expectation vs also-nudge-own-car (+ hardcore toggle)
11. Archetype preset count (7 vs 9) and whether they're editable
12. Respec strictness (milestone token, equal-or-lower cost, vs freer repricing)
13. Character dossier/sheet: its own hub tab, and how deep "full customize" goes
14. First minigame (Setup Gamble vs Media Moment vs Contract Negotiation)
15. Minigame agency/randomness + always-skippable?
16. Contracts/offers as era-documents — negotiation depth for v1
17. News feed prominence + default tone (minimal-narrative on by default?)
18. Career scrapbook / records / history depth for v1
19. Team/finances surfacing in v1 (read-only tier lens vs early ledger)
20. Multi-career + save-slot management UX
21. Art direction / accent identity — any visual references you want me to match

Deliverables this session: an updated `docs/dev/career-hub-design.md` reflecting my answers,
a `docs/dev/career-hub-build.md` (the incremental build plan: v0.4.0 = Hub Increment 1, then
2, then 3, each additive behind the `ICareerSession` seam), and — if there's time — kick off
Hub Increment 1. Keep every hard constraint: sim never decides races, deterministic +
journaled, data-driven, mouse+keyboard parity (decision 8), additive to the shipped loop.
Also: confirm Ctrl+Z-after-mech/acc works in v0.3.4, and (optional) give me the in-game
per-class grid cap.

---

(Bare-minimum fallback if you want to skip the details: "resume the AMS2 Career Companion in
`Z:\Claude Code\ams2-career-companion`, read `NEXT-SESSION-hub-design.md`, and run the
21-question hub design with me.")
