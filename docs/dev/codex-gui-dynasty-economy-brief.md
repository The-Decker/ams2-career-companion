# Codex GUI brief — Dynasty owner economy ("Team Ledger" + bankruptcy screen)

_2026-07-17, from the Head-of-Coding lane (Fable). The whole economy ViewModel surface exists and
is TESTED — this brief is the bind contract. Lane boundary as always: you own
`src/Companion.App/**` (Views/Themes/Converters + render stand-ins); never edit
Core/ViewModels/Data. Design + system doc: `docs/dev/dynasty-tycoon-economy.md`._

## What shipped (bindable, tested)

A Grand Prix Dynasty career created with the economy carries a team ledger: money in
(prize/sponsors/backing), money out (fees/salaries/repairs/development), journaled decisions,
bankruptcy as a real career ending, and six economy news kinds already flowing through the
existing newsroom (no GUI work needed there — the feed just gains stories).

## Surface 1 — the "Team Ledger" hub tab

`HubViewModel.Economy` (`EconomyViewModel`) is registered as tab key
`HubViewModel.EconomyTabKey` (`"economy"`, title "Team Ledger"), present ONLY when
`Economy.HasEconomy` — exactly like the Paddock tab. Bind these EXACT names:

- `EconomyViewModel.Dashboard` (`DynastyEconomyDashboard?`) — the whole projection, replaced
  wholesale on every refresh (after each Apply and each accepted decision). Null only when the
  tab is absent.
- `EconomyViewModel.HasEconomy` (`bool`) — the tab-presence gate.
- `EconomyViewModel.EconomyActionError` (`string`) — the player-facing reason the LAST decision
  was refused; `""` after a success. Show it inline near the action that failed (the
  Dossier SkillActionError pattern); never a modal.
- Commands (all validated by the session — the GUI never pre-computes affordability):
  - `SignSponsorCommand` (param: the offer's `Id` string)
  - `DropSponsorCommand` (param: the contract's `Id` string)
  - `BuyDevelopmentCommand` (no param)
  - `SetStaffCommand` (param: `int` tier, 0 = no staff)
  - `SetSecondSeatCommand` (param: `Companion.Core.Dynasty.SecondSeatDeal` — `Retained`/`PayDriver`)

`DynastyEconomyDashboard` members (all money values are pre-formatted display strings like
`"88,000"` / `"-2,500"`; empty string = not applicable):

1. **Header strip** — `Balance`, `InDeficit` (tint the balance red), `DeficitRounds` of
   `GraceRounds` (show as "DEFICIT ROUND 2 OF 4" only when `InDeficit`), `HardFloor` (the
   overdraft line, small print), `Bankrupt` (the tab still renders after the end — read-only).
2. **Cash-flow statement** — `Statement` (`IReadOnlyList<DynastyLedgerLineModel>`, newest
   first): `Label` ("Round 3 settlement" / "Decision — sign sponsor" / "Season settlement" /
   "BANKRUPTCY"), `Round` (`int?`), `Net` (signed: "+9,300"/"-10,700"), `BalanceAfter`,
   `IsDeficit` (red row).
3. **Sponsor board** — `ActiveSponsors` (`DynastySponsorContractModel`: `Name`, `TierSlot`
   title/major/minor, `SeasonsRemaining`, `PerRace`, `PerSeason`) with a Drop action per row;
   `SponsorBoard` (`DynastySponsorOfferModel`: `Name`, `TierSlot`, `SigningBonus`, `PerRace`,
   `PerSeason`, `ContractSeasons`, `Eligible`, `IneligibleReason`) — render ineligible deals
   dimmed WITH their reason visible (honest availability, not hidden options).
4. **Development** — `DevelopmentLevel` of `DevelopmentMaxLevel` (a stage bar),
   `NextDevelopmentCost` ("" at cap), `DevelopmentAtCap`; the Buy button binds
   `BuyDevelopmentCommand`. Intended feel: an escalating, era-priced upgrade ladder; a bought
   stage is FELT the very next round (it raises the expected finish).
5. **Staff** — `StaffTier` + `StaffOptions` (`Tier`, `UpkeepPerSeason`, `IsCurrent`) as a
   single-selection row bound to `SetStaffCommand`.
6. **Second seat** — `SecondSeat` (`Retained`/`PayDriver`), `SecondSeatSalaryPerSeason`,
   `PayDriverBackingPerSeason`; a two-way toggle bound to `SetSecondSeatCommand`. Copy hint:
   Retained = "pay the salary, keep the second car's prize money"; PayDriver = "their backers
   pay you; their prize money is theirs".
7. **Pending plan** — `PendingDecisions` (`Description`, `Amount` signed or "", `Seq` for
   item identity) + `NextRound` ("locked in when Round {NextRound} runs") +
   `HasPendingDecisions`. Decisions cannot be un-declared in v1 — do not render a remove
   affordance.

Anti-burial note: the hub snaps back to the Race tab after every Apply (by design) — the ledger
must read cleanly on re-entry with no retained transient state.

## Surface 2 — the bankruptcy game-over takeover

The economy's death screen, mirroring the existing terminal binds:

- `HomeViewModel.BankruptcyScreen` (`BankruptcyScreenModel?`) — non-null the moment the fatal
  settlement folds the team (and on reopening a bankrupt career). Members: `DriverName`,
  `TeamName`, `Year`, `Round` (`int?`), `FinalBalance` (negative display string),
  `DeficitRounds`, `GraceRounds`, `Record` (`CareerRecordsBook`), `Seasons`
  (`IReadOnlyList<CareerSeasonCard>`), `RestoreSlots` + `CanRestore` (offer the same restore
  flow the Normal death screen uses).
- `HomeViewModel.IsCareerTerminal` now includes it — the takeover suppression, command
  disabling and reopen routing already work; you add a `BankruptcyView` beside
  `DeathScreenView`/the SMGP career-over view in `HubView.xaml` and (App-owned)
  `MainWindow.xaml.cs`'s global-accelerator suppression pattern-match gains the
  `Home.BankruptcyScreen: not null` case.
- Gallery: `RecentCareer.TerminalBadge` already returns `"BANKRUPT"` — style it beside
  IN MEMORIAM / CAREER OVER.
- Intended feel: an administrator's notice, not a crash screen — the ledger's last page.
  Era-appropriate, sombre, with the career record given the same dignity as the death screen.

## Surface 3 — nothing else to wire

News (six economy kinds + the 039-economy.json pack), history archive, and the availability
gates are already flowing through existing binds. `ICareerSession.EconomyDashboard()` /
`DeclareEconomyDecision(...)` / `BankruptcyScreen()` exist as default interface members, so
render stand-ins compile against fakes unchanged.

## Render stand-ins

Add render-harness stand-ins in `tests/Companion.RenderHarness.Tests` for the Team Ledger tab
and the bankruptcy takeover (your lane), driving them from a `FakeCareerSession` with
`Economy`/`BankruptcyScreen` set — see `tests/Companion.Tests/ViewModels/EconomyViewModelTests.cs`
for a ready-made dashboard fixture shape.
