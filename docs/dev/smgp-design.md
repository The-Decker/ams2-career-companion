# SUPER MONACO GP replica mode — verified design (2026-07-10)

Every fact below was adversarially verified against primary sources (the US Genesis manual read
page-by-page from segaretro scans, RaceFans, Sega-16, 1UP fiches, GameFAQs guides, speedrun.com).
Full citations in the smgp-replica-research workflow output. Accuracy rule: nothing invented; the
game's own deadpan vocabulary; no cheese.

## The season (SMGP1 World Championship, Mega Drive 1990)

- **16 races**, country-named rounds in the GAME's order (not the real 1989 calendar):
  R1 SAN MARINO, R2 BRAZIL, R3 FRANCE, R4 HUNGARY, R5 WEST GERMANY, R6 U.S.A., R7 CANADA,
  R8 GREAT BRITAIN, R9 ITALY, R10 PORTUGAL, R11 SPAIN, R12 MEXICO, R13 JAPAN, R14 BELGIUM,
  R15 AUSTRALIA, **R16 MONACO (the finale)**. Courses model the 1989 F1 circuits (Imola,
  Jacarepaguá, Paul Ricard, Hungaroring, Hockenheim, Phoenix, Montreal, Silverstone, Monza,
  Estoril, Jerez, Mexico City, Suzuka, Spa, Adelaide, Monte Carlo) — our placeholder/alternate
  track system already covers exactly this era (the 1990 pack's calendar is nearly identical).
- **Points 9-6-4-3-2-1**, top six only, NO dropped scores — raw leader after 16 races wins.
- Races are 5 laps in the game; the app authors laps per its own distance conventions but the
  briefing should note the game ran sprint distances. Weather: always ideal (Clear) — verified.
- Each weekend: optional WARM UP, then a one-lap **"Preliminary Race"** (the game's name for
  qualifying — NEVER call it "Super License" in SMGP1; that's an SMGP II term/music cue only),
  then the race. Field: 16 one-driver teams (player included).

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
    team!" — a SEAT SWAP: you take his seat; he drops to the team one tier below yours; that
    team's driver takes your old seat (verified displacement chain).
  - A rival beats YOU under the same rule → he is offered YOUR seat; you are demoted one tier.
  - Losing a rival battle while at **ZEROFORCE** (nothing below) = **career over** (the game's
    game-over screen — the replica's one hard-fail state).
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
  - `data/ams2/smgp/portraits/<driverId>.jpg` — rival portrait on the briefing dossier card
    (e.g. `driver.gilberto_ceara.jpg`; driver ids from `packs/smgp-1/drivers.json`).
  - `data/ams2/smgp/banners/<teamId>.jpg` — team banner atop the dossier card (`team.madonna.jpg`).
  - `data/ams2/smgp/rounds/<round>.jpg` — round card art under the round header (`1.jpg` … `16.jpg`).
  - `data/ams2/smgp/hero.jpg` — the mode hero image (reserved for the main-menu/mode screens, M4).
- A **mode flag** on the pack manifest (`careerStyle: "smgp"`) gating: rival panel in the
  briefing (pick/decline + forced challenges), rival-battle state in the envelope (new versioned
  fold rows — determinism-gated like the called-shot gamble), seat-swap offers + tier
  displacement at season events, the Ceara title-defense event, the Zeroforce career-over state,
  and the presentation vocabulary above. Normal packs are completely unaffected.
- Season 2+ = the existing carryover machinery + the SMGP reshuffle-by-points rule.
