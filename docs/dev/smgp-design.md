# SUPER MONACO GP replica mode — verified design (2026-07-10)

Every fact below was adversarially verified against primary sources (the US Genesis manual read
page-by-page from segaretro scans, RaceFans, Sega-16, 1UP fiches, GameFAQs guides, speedrun.com).
Full citations in the smgp-replica-research workflow output. Accuracy rule: nothing invented; the
game's own deadpan vocabulary; no cheese.

## Locked design constraints (Mike, 2026-07-10)

- **SMGP-ONLY FOCUS until the mode is DONE** (Mike, evening 2026-07-10): all build effort goes to
  Super Monaco GP until it is finished — no other packs/features until then.
- **Full-race weekend, not the arcade sprint**: the app runs real GP distances, so the weekend is
  a 60-min Warm Up (practice) + 30-min Preliminary Race (qualifying) + Grand Prix. The briefing
  heads the qualifying section "Qualifying (Preliminary Race)" so it reads as qualifying.
- **Rival ladder is two-wins-with-naming** (verified wired; see SmgpBattleFoldDeterminismTests):
  name the rival BEFORE each race, beat him twice without losing, and you take his seat. The
  briefing copy is streak-aware ("you have beaten him once — beat him again this race").
- **The SMGP world has its OWN news outlet** (`data/rules/news/smgp.json`, routed via
  `NewsFacts.PreferredEra="smgp"`) — the SEGA universe, never the historical 1990s corpus.


- **SMGP is a SEPARATE CAREER ENTITY from the semi-historical F1 careers.** It is not just
  another pack in the season-picker gallery — the M4 main-menu landing must present it as its own
  mode ("Modes → Super Monaco GP") distinct from "Historical careers". Its rules (rival ladder,
  seat swaps, Zeroforce game-over, title defense), its D-only starts, and its fictional world
  (no real-season History documents) all set it apart. Keep the two conceptually separate in the
  UI as the front door is built.
- **Senna is always OP — the one to beat, but beatable.** A. Senna (Madonna #1, raceSkill 0.99,
  tied with G. Ceara at the top of the grid, above the McLarens at 0.98) is a permanent BASE
  entry, so he leads every season. He is the benchmark the player climbs toward; he can be beaten,
  but he is never nerfed or dropped. (author_smgp.cs keeps Madonna #1 in the base 24 — do not move
  Senna to the reserves when trimming for the livery cap.)

## The season (SMGP1 World Championship, Mega Drive 1990)

- **16 races**, country-named rounds in the GAME's order (not the real 1989 calendar):
  R1 SAN MARINO, R2 BRAZIL, R3 FRANCE, R4 HUNGARY, R5 WEST GERMANY, R6 U.S.A., R7 CANADA,
  R8 GREAT BRITAIN, R9 ITALY, R10 PORTUGAL, R11 SPAIN, R12 MEXICO, R13 JAPAN, R14 BELGIUM,
  R15 AUSTRALIA, **R16 MONACO (the finale)**. Courses model the 1989 F1 circuits (Imola,
  Jacarepaguá, Paul Ricard, Hungaroring, Hockenheim, Phoenix, Montreal, Silverstone, Monza,
  Estoril, Jerez, Mexico City, Suzuka, Spa, Adelaide, Monte Carlo) — our placeholder/alternate
  track system already covers exactly this era (the 1990 pack's calendar is nearly identical).
- **Points 9-6-4-3-2-1**, top six only, NO dropped scores — raw leader after 16 races wins.
- Races are 5 laps in the game; **the app runs FULL GP distances** (real lap counts per its own
  distance conventions), so the weekend is a real F1-style one, not the arcade sprint. Weather:
  always ideal (Clear) — verified.
- Each weekend (Mike's rule, since we run full races): a **60-minute WARM UP** (practice) then a
  **30-minute "Preliminary Race"** — the game's name for **qualifying** (NEVER call it "Super
  License" in SMGP1; that's an SMGP II term/music cue only). The briefing groups it under the
  **"Qualifying (Preliminary Race)"** heading so it reads unmistakably as qualifying, not a second
  race. Then the Grand Prix. Field: 16 one-driver teams (player included).

## The career premise (the part that must be exact)

- The player is ASSIGNED to **MINARAE, Level C** ("The computer has assigned you to the MINARAE
  team. Later in the series, you may be politely asked to change teams if your performance is not
  up to snuff!").
- Four tiers as the game labels them: **LEVEL A** Madonna, Firenze, Millions, Bestowal ·
  **LEVEL B** Blanche, Tyrant, Losel, May · **LEVEL C** Bullets, Dardan, Linden, Minarae(player) ·
  **LEVEL D** Rigel, Comet, Orchis, Zeroforce.
- **Rival system**: before each race the player MAY name a rival from any team ("WILL YOU NAME
  HIM AS YOUR RIVAL? ►YES NO"); sometimes another driver force-challenges the player. Rules:
  - Beat the same rival **twice without losing to him** → "you may get an offer to join his
    team!" — a SEAT SWAP. **CLEAN model (shipped `f277a95`, Mike's anti-chaos rule):** you race as
    your OWN distinct driver (`RoundGridResolver.SyntheticPlayerDriverId`), so you simply MOVE into
    the rival's car; the AI whose car you take BENCHES (and returns the instant you move to another
    car), the car you left reverts to its authored driver, and NOBODY else moves — no cascade,
    `SmgpState.AiSeatOverrides` stays empty. (Superseded the earlier "displacement chain" where the
    rival dropped a tier and that team's driver took your old seat.)
  - **Challenge targeting (Mike's rule):** you may only name a rival in the tier directly ABOVE
    you (the seat you climb toward) or ANY tier below — never two tiers up, never your own tier.
    So D→C only; C→B or D; B→A, C or D; A→B, C or D. (`SmgpRules.CanChallenge`; the briefing
    filters the namable-rival list, but the FORCED title-defense challenger bypasses it.)
  - **Relegation (Mike's rule):** losing to the same rival **twice** while ABOVE LEVEL D → you are
    RELEGATED to the class below, in a **RANDOM team** (picked deterministically from the master
    seed + round + rival, so replay re-derives it). CLEAN model: only YOU move into that team's car
    (its AI benches, your old car reverts to its authored driver); the rival keeps his own car — no
    cascade.
  - **The LEVEL D floor (Mike's rule):** at D there is nowhere below, so a two-loss forfeit does
    NOT relegate — instead every LOST battle (any rival) counts, and the **fourth**
    (`SmgpRules.FloorLossLimit`) ends the career: kicked out of F1 SMGP. Promoting out of D (a
    win up to C) wipes the count. This replaces the old "forfeit at Zeroforce = game over" rule.
- **Title defense**: winning the championship automatically seats you in **MADONNA** for the next
  season. At its start, **G. Ceara** (Brazilian, Bullets, the Senna analogue, near-unbeatable
  pace) declares your days are over and force-challenges you in R1 (San Marino) + R2 (Brazil).
  Win AT LEAST ONE of the two → keep Madonna; lose both → fired to DARDAN, Ceara takes Madonna.
- **Completion**: two championships won = the game is beaten (the app can keep the mode running
  season over season like normal carryover afterward; seats reshuffle by points between seasons).
- Mike's tuning: "move up the field quicker" — the two-wins rule IS the quick ladder (a promotion
  every ~2 races when you deliver); keep the game's rule rather than inventing an accelerator.

## Roster (from the installed SMGP SKINS V1 pack, F-Classic_Gen3, 32 liveries)

The pack is the verified UNION of SMGP1 + SMGP II rosters (both games' driver per team where they
overlap). Season 1 of the mode uses the SMGP1 sixteen (Asselin/Elssler/Alberti/Picos/Herbin/
Hamano/Pacheco/Turner/Miller(Bullets)/Bellini/Moreau/[player at Minarae]/Cotman/Tornio/Tegner/
Klinger); the SMGP II names (Senna, Jones, Germi, Blume, Gould, Dufay, Alfven, Nono, Arai,
Rampal, White, Yepes, Chardin, Delvaux, Sambena*) serve the title-defense/second-season flavor and
extra liveries. Corrections the pack build must apply: **F. Elssler** (pack typo "Elsser"),
**P. Klinger** (pack livery label typo "Kilnger"), B. Miller is BULLETS in season 1 (not Minarae),
*"E. Sambena" is the pack author's invention for the SMGP II player team Serga — non-canon, usable
as the player's own-entrant slot. The pack's CustomAIDrivers ratings already encode the tier
ladder (0.99 → 0.70) and G. Ceara at 0.99.

## Presentation vocabulary (exact, sourced — this is the no-cheese kit)

- Round header: "SAN MARINO · ROUND 1" style (the game's Course Select format).
- Qualifying label: "PRELIMINARY RACE". Rival prompt: "WILL YOU NAME HIM AS YOUR RIVAL?"
- Rival dossier card: team banner + MACHINE block (engine name, max power) + driver portrait slot
  + a one-line deadpan quote ("IT'S INTERESTING."). Rivals have distinct personalities via their
  accept/challenge one-liners.
- Pit-crew advice line before each race ("PASS THE CARS AT THE HAIRPIN TURN!").
- HUD nods: points readout abbreviated "D.P."; dual YOU/RIVAL position readout in race results.
- New-game scene: crew before the team truck under the MINARAE banner — "LET'S TRY HARD TO WIN."
- Music-title flavor available as text (Options B.G.M. by song title: "Extreme Tension", "Theme
  of Monaco"…). Ad/billboard art must be parody-only (the game's own "Marlbobo" lawsuit history;
  Madonna's home-version livery is yellow/red, deliberately NOT Marlboro).
- Asset slots (Mike supplies art): mode hero image, rival portraits, team banners, round cards —
  via the existing user-asset system (new `data/ams2/smgp/` folder family).

## Implementation shape (app side)

- A **season pack** `packs/smgp-1` (ams2Class F-Classic_Gen3, 16 rounds, 16 one-driver teams,
  points 9-6-4-3-2-1 wholeSeason, grids = all 16 every round, skinpack livery bindings, ratings
  from the pack's own CustomAIDrivers XML) — CONFLICTS with the 1990 skinpack on disk, so it
  depends on the Skin Season Manager swapping `formula_classic_g3m*.xml` at career load/stage.
- **User-art slots (SHIPPED, M3 slice 5)** — drop-in images beside the exe, never committed,
  resolved by the shared keyed-asset convention (`.jpg`/`.jpeg`/`.png`, first found wins; absent
  = the UI hides the slot):
  - `data/ams2/portraits/<driverId>.jpg` — DRIVER PORTRAITS (universal, every pack): shown on
    the briefing dossier card AND the wizard's grid-choice cards (e.g.
    `driver.gilberto_ceara.jpg`; driver ids from `packs/smgp-1/drivers.json`).
  - `data/ams2/cars/<driverId>.png` — the rival's CAR photo on the briefing dossier card
    (universal, keyed by the driver whose car it is). **EXTRACTED from the installed livery** by
    `tools/extract_car_previews.cs` (`dotnet run tools/extract_car_previews.cs`): it parses each
    F-Classic_Gen3 model's override XML for the `<PREVIEWIMAGE>` DDS (a pre-rendered car
    thumbnail), decodes it (self-contained BC1/BC3/uncompressed DDS → PNG, ~900px), matches the
    livery to the pack driver, and writes `dist/data/ams2/cars/<driverId>.png`. Re-run when the
    skins change. The tool also emits `dist/data/ams2/portraits/_SMGP-portrait-keys.txt` — the
    exact filename for each driver's hand-supplied PORTRAIT drop. Both are USER ASSETS (skinpack
    renders / your art) under gitignored `dist/`, never committed. Both dossier slots render as
    framed placeholders until art is present, so they are discoverable.
  - `data/ams2/smgp/banners/<teamId>.jpg` — team banner atop the dossier card (`team.madonna.jpg`).
  - `data/ams2/smgp/rounds/<round>.jpg` — round card art under the round header (`1.jpg` … `16.jpg`).
  - `data/ams2/smgp/hero.jpg` — the mode hero image (reserved for the main-menu/mode screens, M4).
- **The McLaren field (Iris & Azalea, Kobra Fleetworks)** — two McLaren MP4/5B teams
  (Iris #33 B. Salgado purple, Azalea #34 M. Larssen pink — the `mclaren_mp45b` mod car; numbers
  33/34 because the 32-car skin universe uses every number 1–32) round the base 24 generic-model
  SMGP cars to 26 (the F-Classic_Gen3 26-livery cap). Since the mod was finalized they are
  PERMANENT base entries (the opt-in `manifest.moddedField` tick era is over). Gated exactly like the alternate
  tracks: a wizard tick verifies the car mod is installed (`mclaren_mp45b` in the content library
  + its `Overrides\mclaren_mp45b\` folder present) and, when it is, the creation-time
  `ModdedFieldTransform` appends the two entries and bumps each round's grid size before pinning —
  so the pinned pack fields 26 and replays byte-identically. Off or mod missing = the base 24-car
  field, no dependency. The Iris/Azalea teams (LEVEL A) + drivers are always in teams.json/
  drivers.json (inert without an entry); Zeroforce stays the ladder floor.
- A **mode flag** on the pack manifest (`careerStyle: "smgp"`) gating: rival panel in the
  briefing (pick/decline + forced challenges), rival-battle state in the envelope (new versioned
  fold rows — determinism-gated like the called-shot gamble), seat-swap offers + tier
  displacement at season events, the Ceara title-defense event, the Zeroforce career-over state,
  and the presentation vocabulary above. Normal packs are completely unaffected.
- Season 2+ = the existing carryover machinery + the SMGP reshuffle-by-points rule.
