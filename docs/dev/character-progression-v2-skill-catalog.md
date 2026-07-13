# Character progression v2 — 90-skill catalog

_Draft catalog, 2026-07-12. Companion to `character-progression-v2.md`._

Classification:

- **EXPECTATION** — player-seat AI personality/rating fields. Only `raceSkill` currently feeds Companion's expected-finish calculation; `qualifyingSkill`, aggression, consistency, start reactions, and the other personality fields are staged AI/fiction data unless a separate CAREER lever is explicitly named. None physically improve a human-driven AMS2 car.
- **CAREER** — deterministic Companion mechanics such as reputation, offers, aging, injury, income, or XP.
- **CAR** — real human-car physics through `weight_scalar`, `power_scalar`, or `drag_scalar`; final values clamp to AMS2's 0.900–1.100 range.

All IDs are new v2 mastery IDs and are append-only after release; none collide with the 42 shipped perk IDs. A mastery node may reference an immutable legacy effect set, but its ID, SP price, gates, and ownership are separate. Costs are first-wave targets. Every Requires cell below names exact stable IDs. Prerequisites are branch-local.

Serialized `unlockLevel` gates are tier 1/L1, tier 2/L50, tier 3/L150, tier 4/L275, and tier 5/L400. Each family's two tier-5 endpoints share `<family>.capstone` as `exclusiveGroup`: one may be owned before L475, and the L475 mastery override permits the second.

All XP percentages in this draft apply to per-round XP only. Season-award XP remains unmodified unless a future version explicitly adds and journals a separate season-award rule.

## Pace — 10 skills (33 SP)

| ID / name | Tier | Requires | SP | Effect | Tradeoff / guardrail |
|---|---:|---|---:|---|---|
| `pace_rhythm` — Rhythm Driver | 1 | — | 1 | EXPECTATION raceSkill +0.03, qualifyingSkill -0.02 | Sunday bias; higher expectation self-taxes results. |
| `pace_telemetry_habit` — Telemetry Habit | 1 | — | 1 | CAREER pace-anchor alpha +0.05, marketability -0.03 | Faster calibration, less charisma; alpha clamp 0.15–0.60. |
| `pace_qualifying_sequence` — Qualifying Sequence | 2 | `pace_rhythm` | 2 | EXPECTATION qualifyingSkill +0.06, raceSkill -0.03 | Grid specialist; no CAR effect. |
| `pace_setup_feedback` — Setup Feedback Loop | 2 | `pace_telemetry_habit` | 2 | CAR drag -0.003, weight +0.003; CAREER marketability -0.03 | Approximately neutral car exchange plus visibility tax. |
| `pace_race_program` — Race-Pace Program | 3 | `pace_qualifying_sequence` | 3 | EXPECTATION raceSkill +0.07, qualifyingSkill -0.035; CAREER finish-vs-expected XP -5% | Raises its own Sunday bar; no physical buff. |
| `pace_aero_map` — Aero Balance Map | 3 | `pace_setup_feedback` | 3 | CAR drag -0.005, power -0.005; EXPECTATION tyreManagement -0.02 | Low-drag/low-power exchange. |
| `pace_complete_lap` — Complete-Lap Craft | 4 | `pace_race_program`, `pace_aero_map` | 4 | EXPECTATION race/qualifying +0.05; CAREER finish-vs-expected XP -8% | Stronger bar on both axes with explicit XP tax. |
| `pace_powertrain_exploitation` — Powertrain Exploitation | 4 | `pace_aero_map` | 4 | CAR power +0.006, drag +0.006; EXPECTATION avoidanceOfMistakes -0.03 | Acceleration/drag exchange with knife-edge identity. |
| `pace_perfect_lap` — The Perfect Lap | 5 | `pace_complete_lap` | 6 | EXPECTATION qualifying +0.10, race -0.04, consistency -0.04; CAREER win XP -10% | Spectacular Saturday specialization. |
| `pace_total_performance` — Total Performance | 5 | `pace_complete_lap`, `pace_powertrain_exploitation` | 7 | CAR power +0.008, drag -0.004, weight +0.008; CAREER salary ask +12%, marketability -0.05 | Intentionally net-positive CAR capstone; branch max-path CAR advantage audited. |

## Racecraft — 10 skills (33 SP)

| ID / name | Tier | Requires | SP | Effect | Tradeoff / guardrail |
|---|---:|---|---:|---|---|
| `racecraft_clean_overtake` — Clean Overtaker | 1 | — | 1 | EXPECTATION aggression +0.04, avoidance +0.03, defending -0.03 | Attack over defense; no CAR effect. |
| `racecraft_defensive_lines` — Defensive Lines | 1 | — | 1 | EXPECTATION defending +0.06, aggression -0.04, qualifying -0.02 | Sunday defense costs attack/grid identity. |
| `racecraft_first_lap_reader` — First-Lap Reader | 2 | `racecraft_clean_overtake` | 2 | EXPECTATION startReactions +0.07, consistency -0.03; CAREER error-blame scale +0.02 | Launch identity with volatility/blame tax. |
| `racecraft_pressure_absorption` — Pressure Absorption | 2 | `racecraft_defensive_lines` | 2 | CAREER error-blame scale -0.05; EXPECTATION aggression -0.03 | Forgiving evaluation but fewer forced moves; scale clamp 0.75–1.25. |
| `racecraft_switchback_school` — Switchback School | 3 | `racecraft_first_lap_reader` | 3 | EXPECTATION aggression +0.07, defending +0.03, avoidance -0.05; CAREER injury base +0.02 | Assertive identity with real Companion injury risk. |
| `racecraft_damage_limitation` — Damage Limitation | 3 | `racecraft_pressure_absorption` | 3 | CAREER error floor blend +0.20, win XP -5%; CAR power -0.003 | Forgiveness paid by pace and peak XP. |
| `racecraft_traffic_mastery` — Traffic Mastery | 4 | `racecraft_switchback_school`, `racecraft_damage_limitation` | 4 | EXPECTATION aggression/avoidance +0.05, qualifying -0.03; CAREER blame +0.03 | Race-day identity, poorer clear-air bar. |
| `racecraft_closing_laps` — Closing Laps | 4 | `racecraft_first_lap_reader`, `racecraft_pressure_absorption` | 4 | EXPECTATION consistency +0.06, aggression -0.04; CAREER round rep +8%, podium XP -5% | Bankable finishes, fewer hero moves. |
| `racecraft_grandmaster` — Grandmaster of Traffic | 5 | `racecraft_traffic_mastery`, `racecraft_closing_laps` | 6 | EXPECTATION aggression/defending +0.08, avoidance -0.06; CAREER injury +0.03, blame +0.04 | Large personality signature with explicit risk; zero CAR upside. |
| `racecraft_untouchable` — Untouchable | 5 | `racecraft_damage_limitation`, `racecraft_closing_laps` | 7 | CAREER floor blend +0.25, blame scale -0.08, win XP -10%; EXPECTATION avoidance +0.05, aggression -0.06; CAR power -0.006 | Maximum protection paid by conservative identity and real pace loss. |

## Physical — 10 skills (33 SP)

| ID / name | Tier | Requires | SP | Effect | Tradeoff / guardrail |
|---|---:|---|---:|---|---|
| `physical_core_strength` — Core Strength | 1 | — | 1 | EXPECTATION stamina +0.06; CAR weight +0.002 | Stamina is fiction/expectation for human; weight cost is physical. |
| `physical_recovery_habits` — Recovery Habits | 1 | — | 1 | CAREER injury durability +0.06, round rep -2% | Safer injury model, slower public momentum. |
| `physical_heat_conditioning` — Heat Conditioning | 2 | `physical_core_strength` | 2 | EXPECTATION stamina +0.08, consistency -0.03 | Endurance identity with fine-control volatility. |
| `physical_rehab_discipline` — Rehab Discipline | 2 | `physical_recovery_habits` | 2 | CAREER injury durability +0.10, marketability -0.04, experience offers -3% | Availability over fame/momentum. |
| `physical_endurance_base` — Endurance Base | 3 | `physical_heat_conditioning` | 3 | EXPECTATION stamina +0.10; CAR drag -0.003, power -0.003 | Net-neutral endurance setup. |
| `physical_career_longevity` — Career Longevity | 3 | `physical_rehab_discipline` | 3 | CAREER decline acceleration -0.15, round rep -2%; CAR weight +0.003 | Longer tail paid by pace/glamour. |
| `physical_peak_preservation` — Peak Preservation | 4 | `physical_endurance_base`, `physical_career_longevity` | 4 | CAREER peak shift +1, stat soft cap -0.03, experience offers -5% | Later decline, lower ultimate rating ceiling. |
| `physical_marathon_engine` — Marathon Engine | 4 | `physical_endurance_base` | 4 | EXPECTATION stamina +0.12, qualifying -0.03; CAR drag -0.005, weight +0.005 | Explicit drag/weight exchange. |
| `physical_ageless_competitor` — Ageless Competitor | 5 | `physical_peak_preservation`, `physical_marathon_engine` | 6 | CAREER peak +2, decline -0.25, stat cap -0.05, season rep -3%; CAR weight +0.008 | Long plateau, lower ceiling/heavier car/slower legacy; decline floor 0.10. |
| `physical_indestructible` — Indestructible | 5 | `physical_rehab_discipline`, `physical_peak_preservation` | 7 | CAREER injury durability +0.25, age-risk offers -0.12, season rep -5%; CAR power -0.006 | Availability/late offers paid by real pace and legacy. |

## Mental — 10 skills (30 SP)

| ID / name | Tier | Requires | SP | Effect | Tradeoff / guardrail |
|---|---:|---|---:|---|---|
| `v2_consistency_king` — Consistency King | 1 | — | 1 | CAREER OPI retention +0.05; EXPECTATION consistency +0.08 | Positive breakthroughs also move OPI more slowly; retention cap 0.90. |
| `v2_streaky` — Streaky | 1 | — | 1 | CAREER OPI retention -0.10 | Good and bad form arrive rapidly; retention floor 0.65. |
| `v2_student_of_the_craft` — Student of the Craft | 2 | `v2_consistency_king` | 2 | CAREER midfield/mechanical-DNF XP +50% | Win/podium XP -40%; not universal acceleration. |
| `mental_momentum_rider` — Momentum Rider | 2 | `v2_streaky` | 2 | CAREER faster gain-side OPI; signed round reputation ±15% | Failures accelerate with successes. |
| `v2_metronome` — Metronome | 3 | `v2_student_of_the_craft` | 3 | CAREER OPI retention +0.06; EXPECTATION consistency +0.08 | Further suppresses positive OPI movement. |
| `mental_early_brilliance` — Early Brilliance | 3 | `mental_momentum_rider` | 3 | EXPECTATION pre-peak race/qualifying +0.05; post-peak qualifying +0.02 and race -0.02 | Permanent era-shaped identity; never becomes unpurchasable. |
| `mental_race_reader` — Race Reader | 4 | `v2_metronome` | 4 | CAREER error floor blend +0.35; EXPECTATION avoidance +0.08, aggression -0.08; win XP -10% | Mistakes judged less harshly; conservative identity. |
| `mental_adversity_engine` — Adversity Engine | 4 | `mental_early_brilliance` | 4 | CAREER finish-vs-expected and mechanical-DNF round XP +25% | Underperformance more readily reduces that round's applied XP to zero; win/podium round XP -15%. Lifetime XP never falls in v2. |
| `mental_zen_master` — Zen Master | 5 | `mental_race_reader` | 5 | CAREER blame scale -0.15, retention +0.04 | All XP -15%; blame floor 0.70, retention cap 0.90. |
| `mental_relentless_mind` — Relentless Mind | 5 | `mental_adversity_engine` | 5 | CAREER all XP +20%, signed round reputation ±15% | Retention -0.05; failures accelerate; aggregate XP cap 1.40. |

## Business — 10 skills (30 SP)

| ID / name | Tier | Requires | SP | Effect | Tradeoff / guardrail |
|---|---:|---|---:|---|---|
| `v2_sponsor_magnet` — Sponsor Magnet | 1 | — | 1 | CAREER portable pay budget +2 BU | Works-team marketability/salary stigma remains. |
| `v2_cheap_contract` — Cheap Contract | 1 | — | 1 | CAREER salary-ask offer weight -0.40 | Salary offer -40%, marketability -0.05. |
| `business_mercenary` — Mercenary | 2 | `v2_sponsor_magnet` | 2 | CAREER faster gain-side OPI, salary +10% | Experience offers -20%, season rep -15%. |
| `v2_company_man` — Company Man | 2 | `v2_cheap_contract` | 2 | CAREER experience offers +30%, season rep +15% | Salary -15%, salary-ask evaluation +10%. |
| `v2_prima_donna` — Prima Donna | 3 | `business_mercenary` | 3 | CAREER salary +25%, marketability +0.20 | Salary ask +40%; EXPECTATION consistency -0.05. |
| `business_contract_negotiator` — Contract Negotiator | 3 | `v2_company_man` | 3 | CAREER salary offer +20% | Salary ask +25%; richer demands narrow the viable team pool. |
| `business_brand_empire` — Brand Empire | 4 | `v2_prima_donna` | 4 | CAREER pay budget +2 BU, marketability +0.10 | Salary ask +25%; works-team marketability -0.10; sponsor cap +5 BU. |
| `business_paddock_fixture` — Paddock Fixture | 4 | `business_contract_negotiator` | 4 | CAREER experience offers +30%, age-risk -15% | Salary -20%, season rep -10%. |
| `business_commercial_titan` — Commercial Titan | 5 | `business_brand_empire` | 5 | CAREER pay budget +3 BU, rep-floor relax one tier | Salary ask +40%, works-team marketability -15%; hard caps apply. |
| `business_boardroom_favorite` — Boardroom Favorite | 5 | `business_paddock_fixture` | 5 | CAREER experience offers +40%, age-risk -25%, rep-floor relax one tier | Salary -25%, marketability -0.10; secures seats rather than wealth/fame. |

## Weather — 10 skills (30 SP)

Conditional CAR paths require calendar-frequency and full-path audits.

| ID / name | Tier | Requires | SP | Effect | Tradeoff / guardrail |
|---|---:|---|---:|---|---|
| `v2_rain_man` — Rain Man | 1 | — | 1 | Wet CAR power +0.020; EXPECTATION wetSkill +0.30 | Dry CAR power -0.008, tyreManagement -0.05. |
| `v2_sunshine_specialist` — Sunshine Specialist | 1 | — | 1 | Dry CAR power +0.010 | Wet CAR power -0.040, EXPECTATION wetSkill -0.20. |
| `v2_tyre_whisperer` — Tyre Whisperer | 2 | `v2_rain_man` | 2 | Long-race CAR drag -0.012; EXPECTATION tyreManagement +0.12 | Short-race CAR drag +0.010. |
| `v2_fuel_saver` — Fuel Saver | 2 | `v2_sunshine_specialist` | 2 | Long-race CAR weight -0.008; EXPECTATION fuelManagement +0.12 | Short-race CAR power -0.012. |
| `weather_reader` — Weather Reader | 3 | `v2_rain_man`, `v2_sunshine_specialist` | 3 | CAREER pace-anchor alpha +0.15 | Anomalous conditions can whipsaw calibration; alpha clamp 0.15–0.60. |
| `weather_thermal_window` — Thermal Window | 3 | `v2_tyre_whisperer`, `v2_fuel_saver` | 3 | EXPECTATION tyre +0.12/fuel +0.08; long CAR drag -0.006 | Short CAR drag +0.006. |
| `storm_chaser` — Storm Chaser | 4 | `weather_reader`, `v2_tyre_whisperer` | 4 | Wet CAR drag -0.010; EXPECTATION wetSkill +0.05 | Dry CAR drag +0.004; wet path aggregate envelope. |
| `weather_endurance_alchemist` — Endurance Alchemist | 4 | `weather_thermal_window`, `v2_fuel_saver` | 4 | Long CAR power +0.008; EXPECTATION stamina/fuel +0.05 | Short CAR power -0.012. |
| `weather_master_of_elements` — Master of Elements | 5 | `storm_chaser` | 5 | Wet CAREER signed round rep +25%, finish-vs-expected XP +20% | Dry rep -8%, XP -5%; no added CAR scalar. |
| `weather_race_economist` — Race Economist | 5 | `weather_endurance_alchemist` | 5 | Long CAREER rep +15%, finish-vs-expected XP +25% | Short rep -10%, XP -20%; no added CAR scalar. |

## Team — 10 skills (31 SP)

| ID / name | Tier | Requires | SP | Effect | Tradeoff / guardrail |
|---|---:|---|---:|---|---|
| `v2_team_player` — Team Player | 1 | — | 1 | CAR power +0.006; CAREER experience-offer weight +0.15 | EXPECTATION aggression -0.06; signed round reputation ×0.90. |
| `v2_underdog_hero` — Underdog Hero | 1 | — | 1 | CAREER low-tier offer bonus +0.25 for pinned budget tier ≤2 | Top-tier reputation ×0.85 for pinned budget tier ≥4; no live team-name inference. |
| `v2_journeyman` — Journeyman | 2 | `v2_team_player` | 2 | CAREER experience-offer weight ×1.50; reputation-floor relaxation by one tier | Age-risk multiplier ×2; floor relaxation hard-capped at one tier. |
| `v2_works_prodigy` — Works Prodigy | 2 | `v2_underdog_hero` | 3 | EXPECTATION raceSkill +0.08, qualifyingSkill +0.05 | Signed round/season reputation ×0.85; raceSkill raises the expected-finish bar, qualifyingSkill is fiction-only for the human. |
| `team_garage_interpreter` — Garage Interpreter | 3 | `v2_journeyman` | 3 | CAREER pace-anchor alpha +0.10; experience-offer weight +0.20 | Marketability -0.10; round reputation ×0.95; alpha clamp applies. |
| `team_trusted_lieutenant` — Trusted Lieutenant | 3 | `v2_works_prodigy` | 3 | CAREER experience-offer weight +0.30; season reputation ×1.20 | Salary offer ×0.80; EXPECTATION aggression -0.05. |
| `team_development_lead` — Development Lead | 4 | `team_garage_interpreter` | 4 | CAR drag -0.008; CAREER pace-anchor alpha +0.05 | CAR weight +0.006; marketability -0.10; net authored car delta +0.002. |
| `team_number_one_status` — Number-One Status | 4 | `team_trusted_lieutenant` | 4 | CAR power +0.008; EXPECTATION startReactions +0.05 | Salary ask ×1.35; round reputation ×0.90; start reactions are fiction-only for the human. |
| `team_standard` — Team Standard | 5 | `team_development_lead` | 5 | CAREER experience-offer weight +0.40; season reputation ×1.25; age risk ×0.80 | Salary offer ×0.75; marketability -0.10; ordinary-progression exclusive with Franchise Driver. |
| `team_franchise_driver` — Franchise Driver | 5 | `team_number_one_status` | 5 | CAREER reputation-floor relaxation one tier; experience-offer weight +0.30; round/season reputation ×1.15; salary offer ×1.15 | Salary ask ×1.40; marketability -0.10; ordinary-progression exclusive with Team Standard. |

Team guardrail: every root-to-capstone CAR path is limited to +0.010 authored net delta, and unconditional cross-branch CAR delta is limited to +0.015 before the global scalar/legal-build audit.

## Media — 10 skills (30 SP)

| ID / name | Tier | Requires | SP | Effect | Tradeoff / guardrail |
|---|---:|---|---:|---|---|
| `v2_media_darling` — Media Darling | 1 | — | 1 | CAREER marketability +0.15; round/season reputation ×1.10 | Salary ask ×1.15. |
| `v2_quiet_professional` — Quiet Professional | 1 | — | 1 | CAREER salary ask ×0.85; EXPECTATION consistency +0.04 | Marketability -0.15; salary offer ×0.95; consistency is fiction-only for the human. |
| `v2_superstition` — Superstition | 2 | `v2_media_darling` | 2 | CAREER marketability +0.10 | OPI retention -0.04; deterministic recency identity, never an RNG modifier. |
| `media_press_trained` — Press Trained | 2 | `v2_quiet_professional` | 2 | CAREER marketability +0.05; signed round reputation ×1.15 | Salary ask ×1.10. |
| `media_headline_act` — Headline Act | 3 | `v2_superstition` | 3 | CAREER marketability +0.15; salary offer ×1.10; round reputation ×1.15 | Salary ask ×1.30; OPI retention -0.04. |
| `media_crisis_manager` — Crisis Manager | 3 | `media_press_trained` | 3 | CAREER error-floor blend +0.25 | Marketability -0.10; win round-XP ×0.85; aggregate floor blend capped at 0.60. |
| `media_sponsor_spokesperson` — Sponsor Spokesperson | 4 | `media_headline_act` | 4 | CAREER portable pay budget +1.5 BU; marketability +0.15 | Salary ask ×1.25; EXPECTATION consistency -0.04; portable budget bonus capped at +5 BU. |
| `media_respected_voice` — Respected Voice | 4 | `media_crisis_manager` | 4 | CAREER experience-offer weight +0.25; age risk ×0.85; season reputation ×1.15 | Salary offer ×0.80; marketability -0.10. |
| `media_global_icon` — Global Icon | 5 | `media_sponsor_spokesperson` | 5 | CAREER marketability +0.20; round/season reputation ×1.15; salary offer ×1.20 | Salary ask ×1.40; OPI retention -0.04; ordinary-progression exclusive with Paddock Authority. |
| `media_paddock_authority` — Paddock Authority | 5 | `media_respected_voice` | 5 | CAREER experience-offer weight +0.40; age risk ×0.75; season reputation ×1.25 | Salary offer ×0.75; marketability -0.15; ordinary-progression exclusive with Global Icon. |

Media guardrails: aggregate marketability 0.00–1.00, OPI retention 0.65–0.90, reputation multipliers 0.60–1.40, and salary multipliers 0.50–1.75. Article/headline generation is display-only and cannot affect replay state.

## Era Flavor — 10 skills (30 SP)

| ID / name | Tier | Requires | SP | Effect | Tradeoff / guardrail |
|---|---:|---|---:|---|---|
| `v2_wonderkid` — Wonderkid | 1 | — | 1 | CAREER pre-peak per-round XP ×1.40 | Post-peak per-round XP ×0.75. |
| `v2_late_bloomer` — Late Bloomer | 1 | — | 1 | CAREER peak-age shift +3; post-peak per-round XP ×1.25 | Pre-peak per-round XP ×0.70. |
| `signature_focus` — Signature Focus | 2 | `v2_wonderkid` | 2 | EXPECTATION chosen-flavor rating +0.08 | CAREER stat soft cap -0.05; authored flavor choice is persisted. |
| `v2_adaptable` — Adaptable | 2 | `v2_late_bloomer` | 2 | CAREER decline acceleration -0.50; first post-transition EXPECTATION raceSkill +0.03 | Per-round XP ×0.85; transition is journaled and pinned. |
| `era_mechanical_dialect` — Mechanical Dialect | 3 | `signature_focus` | 3 | CAR power +0.006, drag +0.006 | Net-neutral authored exchange; no class-name inference. |
| `era_long_memory` — Long Memory | 3 | `v2_adaptable` | 3 | CAREER experience-offer weight +0.25; season reputation ×1.10 | Pace-anchor alpha -0.05; salary offer ×0.90. |
| `era_period_specialist` — Period Specialist | 4 | `era_mechanical_dialect` | 4 | EXPECTATION chosen-flavor rating +0.08; CAREER round reputation ×1.10 | Stat soft cap -0.05; per-round XP ×0.90. |
| `era_reinvention` — Reinvention | 4 | `era_long_memory` | 4 | CAREER pace-anchor alpha +0.15; decline acceleration -0.10 | OPI retention -0.05; per-round XP ×0.90; aggregate clamps apply. |
| `era_defining_style` — Era-Defining Style | 5 | `era_period_specialist` | 5 | EXPECTATION chosen-flavor rating +0.10; CAREER round/season reputation ×1.15 | Per-round XP ×0.80; salary ask ×1.20; ordinary-progression exclusive with Timeless Competitor. |
| `era_timeless_competitor` — Timeless Competitor | 5 | `era_reinvention` | 5 | CAREER peak shift +1; decline acceleration -0.05; age risk ×0.80 | Per-round XP ×0.80; salary offer ×0.85; ordinary-progression exclusive with Era-Defining Style. |

Era conditions may depend only on journaled age, the pinned `AgingCurveSet` selected by pack year, and explicit transition rows/pinned pack identity. They may not inspect live strings or the current date. `v2_adaptable` remains dormant until v2 adds a persisted transition flag and versioned round/season/transition condition evaluators.

## Catalog-wide budget and compatibility guardrails

- Exactly **90 mastery nodes** ship in wave 1: 10 in each of nine families.
- Draft skill cost is **280 SP**: Pace 33 + Racecraft 33 + Physical 33 + Mental 30 + Business 30 + Weather 30 + Team 31 + Media 30 + Era Flavor 30.
- The worst seven-attribute rail path costs 119 SP, so the current maximum all-in draft is **399 SP**, leaving 100 of the L500 pool's 499 SP as a future-balance buffer.
- Later tuning should move the complete build toward 430–475 SP without exceeding 499. A version-selected load-time audit enforces the upper bound.
- V2 prices live on new mastery IDs in a progression-version-2 catalog/wrapper. Existing creation CP prices, effects, and streams remain immutable: for example legacy `team_player`, `quiet_professional`, and `wonderkid` stay zero-cost creation traits, while legacy `superstition` and `one_trick` stay -1 CP. Their similarly named v2 mastery nodes use distinct `v2_*` IDs.
- Creation-only `one_trick` is not a mastery node. `signature_focus` is the compatible v2 tree node; it preserves specialization without blocking the level-500 “master everything” promise.
- Final composed CAR scalars clamp to 0.900–1.100. Tests enumerate every legal v2 build and active condition, including the legacy character effects that can already stack roughly +0.032 unconditional net.
- Aggregate clamps are mandatory for OPI retention, pace-anchor alpha, marketability, salary, age/decline, and error blame/floor-blend levers so combinations cannot invert mechanics.
- Conditional effects require explicit, progression-versioned round, season, and transition evaluators. A tier/condition discovered after grid construction must never be used retroactively for EXPECTATION or CAR composition.
