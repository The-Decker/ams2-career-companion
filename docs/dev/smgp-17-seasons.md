# SMGP: the 17-Season Campaign + the locked finale (`special.jpg`)

**Status:** design / working prompt (Claude's mechanical piece) · **Date:** 2026-07-12

Mike: *"the continuation of seasons in SMGP — 17 seasons total before it gives you a final final screen
with a special image that has its own name, `special.jpg`, and it's so special that no one can access it
until you beat all 17 seasons."*

## The vision

SMGP becomes a **17-season grand campaign**. You climb the ladder (Zeroforce D → Madonna A), win titles,
defend them — season after season, seventeen in all. Beat all seventeen and the game gives you a **final
final screen** built around one **secret image, `special.jpg`** — an asset that is *inaccessible and
unviewable* anywhere in the app until the campaign is truly beaten. It's the reward that proves total
mastery of the SEGA world.

## What already exists (build ON this, don't reinvent)

- **`SmgpState.Titles`** — a folded, per-career counter of championships won, carried across seasons via
  record `with` (`SmgpState.cs:26`). Determinism-safe (byte-compared on replay).
- **Completion today:** `SmgpRules.IsComplete(titles) => titles >= 2` (`SmgpRules.cs:153`) — 2 titles =
  "the replica is beaten," but the mode keeps running as carryover. **This becomes a milestone, not the
  end; the 17-season finale is the new summit.**
- **Season rollover:** `SmgpState.WithSeasonReset()` clears per-season scratch (streaks/defense/floor),
  carries seats + titles + career-over (`SmgpState.cs:90`). Season 2+ gets `SmgpSeasonVariety.ForSeason`
  (shuffled calendar/weather, display-only). Season records live in the career DB (`CareerStore.ReadSeasons`).
- **Title defense** (`SmgpRules.TitleDefense`) — reigning champion starts next season in Madonna, Ceara
  force-challenges rounds 1+2. Lose both → fired to Dardan.
- **Hard fail:** `CareerOver` at `FloorLossLimit` (4) losses on the Level-D floor.

## The design

### 1. Campaign length = 17 seasons
The SMGP campaign is a **fixed 17-season arc**. Track the season ordinal (already derivable from
`CareerStore.ReadSeasons().Count`). Surface progress everywhere it matters ("SEASON 6 / 17").

### 2. What "beating a season" means — **DECISION FOR MIKE** (I'll default to option A)
- **A (default, "flawless emperor"):** beat season N = **win its championship** (a Title). Beat all 17 =
  **17 titles across the 17-season campaign** (win every season). `special.jpg` unlocks the moment the
  17th title lands. Brutally hard — a single lost season means the perfect run is gone (the campaign can
  continue, but the finale needs all 17). This is the most "special."
- **B (softer):** beat all 17 = **survive 17 seasons AND be the reigning champion at the end of season 17**
  (you can drop a title mid-run as long as you reclaim the crown by the finale).
- **C (accumulator):** `special.jpg` unlocks at **17 career titles**, however many seasons it takes (the
  "17 seasons" is then a soft target, not a hard cap).

I'll implement **A** unless Mike says otherwise — it best matches "beat all 17 seasons." The unlock
predicate is a one-liner either way; the choice is a rules tweak.

### 3. The finale — the locked `special.jpg` screen
- A new **full-screen "final final" view** (reuse the `PromotionView` full-immersion pattern:
  `HomeViewModel` shows a gated content VM; App.xaml DataTemplate) shown **once** when the campaign is
  beaten — a hero built around **`special.jpg`** + a triumphant headline + the 17-season record.
- **`special.jpg` is a locked secret.** It lives at a path the app only ever loads on this screen after
  the unlock condition is met — never on any gallery/placeholder/inspector, never keyed by a converter a
  curious player could trigger early. Treat it like a sealed achievement image: the code refuses to
  surface it unless `campaignBeaten == true`. (If the file is absent, the screen still renders with a
  placeholder + the record — the *unlock* is the achievement, the image is the payoff.)

### 4. Mechanics to build
- **Progress state:** expose `SeasonOrdinal` / `SeasonsTotal (=17)` + a `CampaignBeaten` predicate
  (`Titles`-based per §2) on the session projection. Show "SEASON n / 17" on the hub/briefing.
- **Rollover to 17:** confirm the season-end → next-season flow runs cleanly for a long career (the
  `WithSeasonReset` + variety + re-pin path). This is where the noted **multi-season follow-ups** land and
  become required: (a) **DNQ re-roll per season** (season 1 is seeded; seasons 2-17 need their own seeded
  roll), (b) **multi-season stat/point accrual** (career totals must span all seasons, not just the
  current one). Both are display/pinned-data, replay-safe.
- **The unlock + screen:** a session method `SmgpFinale()` → the final model (record + `special.jpg` key)
  or null until beaten; `HomeViewModel` shows it after the season-17 (or 17th-title) fold, once.
- **End-of-campaign behaviour:** after the finale, what next? (Freeze? "New Game+"? Keep carrying on?) —
  minor, decide with the screen.

### 5. Determinism / persistence
- `Titles` is already folded + byte-compared — the unlock predicate is a pure read, **no fold change**.
- The finale screen + progress are **display-only** projections (like the promotion screen). Safe.
- The multi-season DNQ re-roll (§4) is a **pinned, per-season seeded** transform (same safe pattern as
  the season-1 DNQ) — no fold change, replay-identical.
- Any new `SmgpState` field (unlikely needed — derive from `Titles` + season count) must be
  `WhenWritingDefault`-gated for byte-compat, per the existing pattern.

## Open decisions for Mike
1. **The "beat all 17" rule** — A / B / C above (I default to A).
2. **A lost season** — does it end the campaign, or can you keep grinding toward a later perfect stretch?
3. **After the finale** — freeze the career, offer New Game+, or continue?
4. **`special.jpg`** — Mike supplies the image; where should it drop, and any special reveal (fade,
   fanfare, its own screen chrome)?

## Build order (when I pick this up)
1. Season-ordinal + `CampaignBeaten` projection + "SEASON n / 17" display.
2. Multi-season DNQ re-roll + cross-season stat/point accrual (the prerequisites a real 17-season arc
   needs).
3. The locked finale screen + `special.jpg` gating.
4. Determinism tests: a career that wins 17 → the finale unlocks + replays byte-identical; a career short
   of 17 → no finale, `special.jpg` never surfaces.
