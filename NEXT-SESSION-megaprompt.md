# NEXT SESSION — Content deepening: Calendar · News · Per-season data · SMGP audit

Resume the **AMS2 Career Companion** (`Z:\Claude Code\ams2-career-companion` — WPF / .NET 10, single
self-contained exe). This prompt was authored 2026-07-10 (on the tail of the McLaren-skin work) and
is model-agnostic (may roll from Fable to Opus mid-run).

**READ MEMORY FIRST**: `MEMORY.md` → `ams2-hub-build-progress.md` (TOP block = current) →
`ams2-mclaren-skin-pipeline.md` → this file. Then **verify against the live repo** — treat every
file path / line below as a hint, confirm with `git log`, `git status`, and a quick read before
trusting it. This repo moves fast.

## Orientation
- Branch `hub/increment-4` (head ~`99a63c5`). Solution: `Companion.slnx` (no `.sln`). Build/test:
  `dotnet build` / `dotnet test` from repo root.
- **Discipline every slice** (non-negotiable, from CLAUDE.md + memory):
  - Keep the full suite + RenderHarness tests green; the **f1db oracle (77/77) is NEVER touched**.
  - Sim/fold changes are **envelope-versioned + per-career gated**; replay stays byte-identical.
    Pack/grid/data changes affect **NEW careers only**.
  - Work **in sections / slices** — Mike wants eyes on it; commit + push per slice, show him between.
    (No `gh` CLI on this machine — PRs via the cached git cred + API, helper `Z:\Claude Code\open-pr.ps1`.)
  - `Companion.Core` has no I/O/WPF/DB. Packs serialize with `CoreJson.Options` (camelCase).

There are **four work streams**. Do them section-by-section; ask Mike which to start and how deep.

---

## MISSION A — Season Calendar optimization
**Files:** `src/Companion.Data/ChampionshipCalendar.cs`, `src/Companion.ViewModels/Hub/CalendarViewModel.cs`,
`src/Companion.App/Views/CalendarView.xaml(.cs)`. **Tests:** `tests/Companion.Tests/ViewModels/CalendarViewModelTests.cs`,
`tests/Companion.RenderHarness.Tests/CalendarRenderTests.cs`, `tests/Companion.Tests/Data/MixedCalendar*Tests.cs`.

**Current state:** Calendar hub tab = expandable round cards — real venue + actual AMS2 track + badge
(Real venue / Stand-in / Alternate), f1db circuit map SVG, era-capped fun facts, optional venue photo
(→ resizable PhotoWindow). Auto-levels tilted maps via `CircuitGeometryConverter` (PCA).

**"Optimize" — ASK MIKE which axis first**, then slice:
- **Performance:** profile the calendar build + expand-render (map SVG parse, fun-facts, photo load).
  Lazy-load expanded content, cache map geometry, virtualize the round list if long, defer photo decode.
- **Layout / density:** tighter cards, responsive columns (reuse `WidthFractionConverter` /
  `WidthToColumnsConverter`), a true at-a-glance season overview; center + margins already partly done.
- **Content correctness:** every round's venue / AMS2 track / badge / fun-fact correct AND era-capped
  (no spoilers — same discipline as the history-reveal + fun-facts work); fill gaps.

---

## MISSION B — News articles: deepen content, in sections
**Files:** `src/Companion.Core/News/NewsArticleBank.cs`, `NewsArticleComposer.cs`. **Content:**
`data/rules/news/{1960s,1970s,1980s,1990s,2000s,2010s}.json`. **Views:** `NewsView.xaml`, `NewsWindow.xaml`.

**Current state:** dynamic news = per-decade era corpora + season-in-review articles, composed per career.

**Work additively, ONE decade (or one article-type) per slice — commit each:**
1. Audit each decade JSON for volume + variety (headline templates, body templates, fillable slots).
   Write the thin spots into `docs/dev/audits/audit-news-coverage.md`.
2. Pile on content, decade by decade: more headlines, driver/team storylines, mid-season + silly-season
   beats, technical/regulation flavor, rivalries, championship-permutation pieces. **Era-capped** — no
   anachronisms, no result spoilers (reuse the era-cap machinery already used by fun-facts/history).
3. New article TYPES where they earn their place (qualifying reports, incident/DNF stories, transfer
   rumors) → wire into `NewsArticleComposer`.
4. Tests: composer determinism + a per-decade no-anachronism assertion.

Mike's framing: **open-ended, keep adding** — we grind this in sections over multiple sessions.

---

## MISSION C — Per-season data/memory system ("work on every season possible")
**Goal:** a durable per-season progress ledger so all 20 packs can be brought to full depth without
re-deriving state each session. Packs: `packs/f1-1967 … f1-2020` (19 F1 seasons) + `packs/smgp-1`.

- Create/extend **`docs/dev/season-coverage.md`** — a per-pack matrix of every content dimension:
  ratings · weekend/weather (refuel era) · authored wet races · fuel guidance · fun-facts · history
  docs · news depth · alternate tracks · skins mapped · circuit maps · portraits · era-art. Mark
  **done / partial / todo** per season. This IS the "work every season" worklist.
- Document the **repeatable per-season pipeline** in that doc (tools already exist:
  `tools/import_jusk_ai.cs`, `derive_ratings/form/history/circuits`, `author_weather.cs`,
  `author_smgp.cs`, `extract_tracks.cs`, `author_alternates.cs`) — the order to bring any season to parity.
- **Save a MEMORY per meaningful season milestone** — only the non-obvious (roster quirks, rating
  source, wet-race citations, staged-vs-imported packs, gotchas like the F-Classic_Gen2 = our own
  staged file circular-import trap). Cross-link `[[...]]`. Don't memory-ize what the repo already records.

---

## MISSION D — SMGP correctness audit (teams · numbers · names · skins ALL match)
A **VERIFY pass**. Source of truth = the installed skins; the pack data must match them exactly.

**Data to fix:** `packs/smgp-1/{teams,drivers,entries,season,pack}.json`.
**Skin truth:** `data/ams2/skin-seasons/smgp/formula_classic_g3m{1..4}.xml` (repo mirror of the
installed `SMGP SKINS V1` overrides) ↔ live `Y:\SteamLibrary\...\Automobilista 2\Vehicles\Textures\
CustomLiveries\Overrides\formula_classic_g3m*\formula_classic_g3m*.xml` + the SMGP AI file
`...\UserData\CustomAIDrivers\F-Classic_Gen3.xml`.

**The link field:** `entries.json.ams2LiveryName` must **byte-match** the skin XML `LIVERY_OVERRIDE
NAME`, and `entries.json.number` must match the number embedded in that name. Slot IDs repeat across
the four car models (each has its own 51/52/... namespace) — so team+number is the unique key, not slot.

**GROUND-TRUTH ROSTER — read off the installed skins 2026-07-10 (32-car field, every number 1–32 used):**

*g3m1 (Madonna / Firenze / Bestowal / Blanche):*
| slot | livery NAME (skin) | team | # | driver | ctry |
|------|--------------------|------|---|--------|------|
| 51 | Madonna #1 A. Senna   | Madonna  | 1 | Ayrton Senna     | BRA |
| 52 | Madonna #2 A. Asselin | Madonna  | 2 | Alain Asselin    | FRA |
| 53 | Firenze #3 F. Elsser  | Firenze  | 3 | Felipe Elsser    | AUT |
| 54 | Firenze #4 I. Germi   | Firenze  | 4 | Ivanazzio Germi  | ITA |
| 55 | Bestowal #7 A. Picos  | Bestowal | 7 | Alex Picos       | BRA |
| 56 | Blanche #9 J. Herbin  | Blanche  | 9 | Jean Herbin      | FRA |
| X6 | Bestowal #8 M. Blume  | Bestowal | 8 | Michael Blume    | DEU |

*g3m2 (Orchis / Zeroforce / Comet / Lares / Linden / Rigel / Minarae):*
| 51 | Orchis #31 C. Tegner    | Orchis    | 31 | Christopher Tegner | SWE |
| 52 | Zeroforce #32 P. Kilnger| Zeroforce | 32 | Paul Klinger       | DEU |
| 53 | Comet #29 E. Tornio     | Comet     | 29 | Ethan Tornio       | FIN |
| X3 | Lares #23 P. Arai       | Lares     | 23 | Park Arai          | JPN |
| 54 | Linden #22 M. Moreau    | Linden    | 22 | Marcel Moreau      | FRA |
| 55 | Rigel #26 R. Cotman     | Rigel     | 26 | Ryan Cotman        | GBR |
| X5 | Rigel #27 T. Chardin    | Rigel     | 27 | Tristan Chardin    | FRA |
| 56 | Minarae #20 B. Miller   | Minarae   | 20 | Bernie Miller      | USA |

*g3m3 (Millions):*
| 51 | Millions #5 N. Jones  | Millions | 5 | Nigel Jones    | GBR |
| 52 | Millions #6 G. Alberti| Millions | 6 | Giorgio Alberti| ITA |

*g3m4 (Feet / Tyrant / Minarae / Losel / Serga / May / Bullets / Dardan / Cool / Moon / Joke / Blanche):*
| X1 | Feet #24 J. Rampal    | Feet    | 24 | Jean Rampal    | FRA |
| 52 | Tyrant #11 M. Hamano  | Tyrant  | 11 | Miyagi Hamano  | JPN |
| XX | Minarae #21 J. Nono   | Minarae | 21 | Julianno Nono  | ITA |
| 54 | Losel #13 E. Pacheco  | Losel   | 13 | Esteban Pacheco| ESP |
| 55 | Serga #25 E. Sambena  | Serga   | 25 | Eric Sambena   | AND |
| 56 | May #15 G. Turner     | May     | 15 | George Turner  | GBR |
| 57 | Bullets #17 G. Ceara  | Bullets | 17 | Gilberto Ceara | BRA |
| 58 | Dardan #18 E. Bellini | Dardan  | 18 | Eddie Bellini  | ITA |
| 59 | Cool #28 A. Delvaux   | Cool    | 28 | Alef Delvaux   | BEL |
| 60 | Moon #30 K. Yepes     | Moon    | 30 | Kevin Yepes    | ESP |
| X8 | Dardan #19 K. Alfven  | Dardan  | 19 | Keke Alfven    | FIN |
| 51 | Joke #16 L. Dufay     | Joke    | 16 | Luca Dufay     | ITA |
| 53 | Losel #14 W. Dehehe   | Losel   | 14 | Willian Dehehe | BRA |
| X6 | Tyrant #12 G. Gould   | Tyrant  | 12 | Gilles Gould   | CAN |
| X2 | Blanche #10 P. White  | Blanche | 10 | Paul White     | AUT |

*McLaren MP4/5B mod car (`mclaren_mp45b`) — opt-in "Iris & Azalea" extras (KobraFleetworks skin):*
| 51 | Iris #33 B. Salgado   | Iris   | 33 | Bruno Salgado | BRA |
| 52 | Azalea #34 M. Larssen | Azalea | 34 | Mika Larssen  | FIN |
(These moved from #1/#8 → #33/#34 because 1–32 are all taken; see `ams2-mclaren-skin-pipeline.md`.)

**KNOWN discrepancies to reconcile (found in passing — audit for the rest):**
- **Firenze #3:** skin livery says "F. **Elsser**" but `drivers.json` id `driver.felipe_elssler` / name
  "Felipe Elss**l**er" (extra L). The SKIN wins (it's what binds in-game) unless Mike says otherwise.
- **Zeroforce #32:** the skin livery NAME literally reads "P. **Kilnger**" (typo) while the driver is
  "Paul **Klinger**". Decide: match the pack `ams2LiveryName` to the skin's typo so the AI binds, OR
  correct the skin file too. The AI file `livery_name` must equal the override `NAME` exactly.
- Confirm **Millions #5 (Nigel Jones)** exists in `entries.json` (the preview only showed #6).

**Deliverables:**
1. `docs/dev/audits/audit-smgp-roster.md` — a diff of EVERY mismatch (name spelling, number, team,
   missing/extra entry, `ams2LiveryName` ≠ skin `NAME`) across all 34.
2. Fix `packs/smgp-1/*.json` to match the skins (determinism-gated; new careers only).
3. **Add a test** asserting every `entries.json.ams2LiveryName` exists verbatim as a `NAME` in the
   skin override XMLs — this catches roster drift forever.

---

## Deferred (do not lose)
- **M4 beautification** (main-menu landing / "front door", gallery-card parity, themes, MotionAssist,
  user-art panel) — the prior megaprompt (in git history) + `docs/dev/smgp-design.md`.
- Unclosed **M1 adversarial review**.
- **SMGP tail:** CareerOver hard-stop UX, reshuffle-by-points, random AI challenges, per-round
  advice/quote data files, completion celebration.
