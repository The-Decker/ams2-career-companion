# Racing Passport pure racing — GUI handoff

_2026-07-18, Claude (Head of Coding), mission `mission/racing-passport-pure-racing`. Lane contract:
the coding mission is DONE and tested; this is everything the GUI lane needs to finish the visible
polish. **The mode works end-to-end today with the existing XAML** — these are refinements, not
blockers. Do not invent product behavior; the bind contract below is the whole of it._

## The route (already functional with existing XAML)

Racing Passport's creation route is `SeasonPick → Verification → SeatPick → Confirm`. The wizard
already traverses it correctly (the Character and Grid steps are skipped in both directions).
The start card is available and routes through the existing `StartCareerModeCommand`.

## New / changed ViewModel surface (`NewCareerWizardViewModel`)

| Member | Type | Purpose |
|---|---|---|
| `IsRacingPassport` / `IsPureRacingMode` | `bool` | The mode key for every Passport-only visibility decision. |
| `HasCharacterStep` | `bool` | False for Passport — hide any per-step indicator dot for Character if one exists. |
| `HasGridStep` | `bool` | False for Passport — the custom grid editor never runs. |
| `ShowsProgressionSummary` | `bool` | False for Passport — hide campaign pacing/mastery/SP summary blocks. |
| `ShowsMortalityChoice` | `bool` | False for Passport — hide the mortality picker (it stays Off). |
| `ShowsDynastyEconomyChoice` | `bool` | False for Passport — hide any economy affordance. |
| `ShowsOwnEntrant` | `bool` | False for Passport — hide the custom-livery (own-entrant) box on SeatPick. |
| `PlayerDisplayName` | `string` (two-way) | The one identity field: optional driver display name on SeatPick. Blank keeps the seat's authored driver name. |
| `PlayerDisplayNameError` | `string` | Validation message (over `MaxPlayerDisplayNameLength` = 40 chars); empty when valid. Blank input is valid. |
| `PassportSeasonSummary` | `string` | `"1991 · FIA Formula One World Championship"`; empty before a pack parses. |
| `PassportSeatSummary` | `string` | `"Brabham-Repco · replacing N. Piquet · #1"`; empty until a seat is chosen. |
| `PassportConfirmLines` | `IReadOnlyList<string>` | The confirm block (series/year/team/seat/driver/format + the explicit no-progression/no-management lines); empty for other routes. |
| `ResolvedPlayerDisplayName` | `string?` (private) | The effective name (Passport input → character name → authored driver). |

## Desired visible behavior (refinements only)

1. **SeatPick (Passport):** show a "PLAYER DRIVER NAME (OPTIONAL)" text input bound to
   `PlayerDisplayName` with `PlayerDisplayNameError` inline. Until it exists, the mode plays
   fine with the authored driver names.
2. **Step chrome:** if the wizard shows a step list/progress dots, render them from
   `HasCharacterStep`/`HasGridStep` so Passport shows exactly its four steps.
3. **Confirm (Passport):** render `PassportConfirmLines` as the summary block (it is honest
   copy: `Progression: None, pure racing`, `Team management: None`). Do NOT render Level 300 /
   mastery / SP / DNA / economy lines for a Passport confirm.
4. **Start card:** already updated in code ("Choose a season. Take a seat. Go racing." /
   PLAYABLE NOW). Nothing to do.
5. **In-career:** a Passport career is the ordinary historical hub. The Driver dossier tab is
   already absent (no character), the XP toast is already null, the Team Ledger tab is already
   absent (no economy). Nothing to bind, and nothing may be added that implies progression or
   ownership.

## Render cases to verify (render harness, your lane)

- Wizard on the Passport route: four steps only; no character step; no grid editor; the name
  input on SeatPick; the confirm block's no-progression lines.
- SeatPick with a typed over-long name shows the error and blocks Next.
- A Passport career hub: no Driver tab, no progression toast after Apply, no Team Ledger tab.
- The other routes (Dynasty / SMGP / legacy generic) render UNCHANGED (their steps, summaries,
  and pickers are untouched by the mode gates).

## Explicit non-goals

- No XP/level/SP/DNA/mastery/perk surface anywhere in the Passport flow or career.
- No economy/sponsor/staff/development/Team Ledger surface.
- No mortality picker, no nationality/age/portrait/trait inputs.
- No thread/portfolio/hub container UI (the retired design; the career gallery IS the collection).
- No `src/Companion.App/**` edits were made by the coding mission; all of the above is App-layer.
