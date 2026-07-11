# Wet-race weather research - per-season deep pass (M2)

Data-grounded per-round weather slots for rain-affected RACE DAYS, researched and
adversarially verified (independent second-source check per claim + missed-race sweep)
by the m2-deep-pass-research workflow, 2026-07-09. Applied to packs/f1-<year>/season.json
by tools/author_weather.cs. Slots = equal quarters of the session, AMS2 vocabulary.
Rounds not listed stay at the authored Clear x4 default (verified dry or no wet evidence).

## 1967

Researched + adversarially verified 2026-07-10 (the wet-1967-research workflow — one researcher +
one independent skeptic per round, all 11 championship rounds). Exactly one wet race.

### R8 Canadian Grand Prix (Mosport Park)
- race: Rain / Light Rain / Light Rain / Heavy Rain
- evidence: VERIFIED genuinely wet (real precipitation, not grey skies). The inaugural Canadian
  GP, a canonical wet 1967 race. Wikipedia infobox: "Rainy, temperatures up to 24.4 C".
  motorsport.com and grandprix.com give the same arc: raining at the start ("It was raining when
  the race began"), the track then began to dry through the middle, then rain returned around
  lap 68 (water into Clark's ignition ended his lead) and fell "harder than ever" to the flag —
  "conditions reached their worst" (Motor Sport period report). 90-lap quarters ~1-22 / 23-45 /
  46-67 / 68-90: Q1 raining and intensifying (slot 1 = Rain, since AMS2 cannot start a session
  already-wet); Q2 easing, track drying = Light Rain (lingering damp); Q3 dried then a fine rain
  returns ~lap 58 = Light Rain; Q4 the heaviest rain of the day to the finish = Heavy Rain.
  Qualifying/practice ran on the preceding fine, warm days — no wet quali, so qualifyingSlots [].
- source: https://en.wikipedia.org/wiki/1967_Canadian_Grand_Prix
- source: https://www.grandprix.com/races/canadian-gp-1967.html
- source: https://www.motorsport.com/f1/news/jack-brabham-won-the-first-canadian-gp-on-this-day-in-1967-810124/810124/
- source: https://www.motorsportmagazine.com/archive/article/october-1967/71/weather-affects-first-canadian-grand-prix/

The other ten rounds were all independently verified DRY (empty slots, left untouched):
- R1 South Africa (Kyalami): the tropical storm fell the NIGHT BEFORE and washed the rubber off
  (a Monday-morning re-rubbering session was added); race day was hot (~60 C track), chicanef1
  "Hot". The early "spray" was cement dust after a support-race oil spill, not rain.
- R3 Netherlands (Zandvoort): the researcher first read it wet (a slippery early surface), but the
  **adversarial verify pass CORRECTED it to dry** — Jenkinson's Motor Sport report says race day
  was "grey, but everywhere was dry"; the slick surface was coastal dew / a green track, not
  precipitation. A caught false-positive, kept as the record of the correction.
- R2 Monaco (StatsF1 "Sunny"), R4 Belgium (Autosport "both days on dry roads"; Gurney's 148.85 mph
  lap record impossible wet), R5 France/Le Mans (a race-morning shower dried well before the
  start), R6 Britain (Motor Sport "Warm and dry"; a threatened shower never fell), R7 Germany
  (Nürburgring "Warm, dry and sunny"), R9 Italy (Monza "Sunday warm and dry" — the only rain was
  an isolated Saturday-practice storm that did not set the grid), R10 USA (Watkins Glen "beautiful
  bright sun"; rain confined to Friday practice), R11 Mexico (Autosport "warm and sunny").
- NOTE: the R7 (Germany) *verify* agent returned a malformed placeholder result; the R7 *research*
  agent's dry finding (Wikipedia "Warm, dry and sunny", a well-documented dry race) is authoritative
  and stands — R7 is dry.

## 1969

No rain-affected races (all rounds independently verified dry).

## 1974

### R2 Brazilian Grand Prix
- race: Light Cloud / Light Cloud / Heavy Cloud / Storm
- qualifying: Clear / Clear / Light Cloud / Light Cloud
- evidence: VERIFIED wet (late storm). Period Motor Sport report: the race started hot and sunny; 'with 12 laps apparently to go, ominous black clouds started to scud across the skies... a lap later... suddenly the circuit was drenched in a torrential summer downpour', with aquaplaning and puddles forming; the race was flagged after 32 of the scheduled 40 laps with Fittipaldi the winner. ChicaneF1 weather field: 'Cloudy, Hot, Showers'. Slots corrected: slot 2 downgraded Medium Cloud -> Light Cloud because the period report has it hot and sunny/partially cloudy until ~lap 28 (~70% distance); cloud build belongs in slot 3 only, storm strictly in the final quarter. Practice/qualifying sessions were dry (the ferocious thunderstorms fell between sessions), so qualifyingSlots reflect a clear hot day.
- source: https://www.motorsportmagazine.com/archive/article/march-1974/32/brazilian-grand-prix-2/
- source: https://en.wikipedia.org/wiki/1974_Brazilian_Grand_Prix
- source: https://www.chicanef1.com/racetit.pl?year=1974&gp=Brazilian%20GP

### R4 Spanish Grand Prix
- race: Rain / Medium Cloud / Medium Cloud / Light Cloud
- evidence: VERIFIED wet start drying to slicks. Period Motor Sport report: 'On Sunday the scene was sheer misery, with rain falling steadily'; rain was 'still falling as the cars went out for some warm-up laps', the whole field starting on the deepest full-wet tyres; then approximately 17 laps in 'the rain had stopped and the track was drying incredibly quickly', triggering mass stops for slicks (Ferrari's 35s stop helping Lauda to his first GP win); race ended at the two-hour mark after 84 of 90 laps. ChicaneF1: 'Overcast, Rain'. Slots corrected: slot 2 changed Light Rain -> Medium Cloud because rain stopped at ~20% distance (lap 17 of 84, inside slot 1) and the second quarter was rain-free rapid drying; keeping Light Rain in slot 2 would contradict the documented lap-17 rain stop and lap-20s slick stops.
- source: https://www.motorsportmagazine.com/archive/article/june-1974/34/the-spanish-grand-prix/
- source: https://en.wikipedia.org/wiki/1974_Spanish_Grand_Prix
- source: https://www.chicanef1.com/racetit.pl?year=1974&gp=Spanish%20GP

### R11 German Grand Prix
- race: Overcast / Light Rain / Overcast / Light Rain
- evidence: VERIFIED lightly rain-affected. Period Motor Sport report: before the 1:30pm start 'overhead the sky was ominous, though the ground was dry'; during the race 'it was sprinkling with rain on the far side of the circuit, and though most of the track was damp it was not significant enough for the race to be stopped', the start/finish plateau staying dry. Wikipedia and ChicaneF1 both give 'Overcast, showers', with occasional showers during the race contributing to the accident toll (including Hailwood's career-ending Pflanzgarten crash on the penultimate lap). Regazzoni won all 14 laps; no wet tyres, never full-wet â€” Light Rain is the correct maximum intensity. Slots kept: dry-overcast opening matches the period report exactly, and the Overcast/Light Rain alternation is the faithful 4-slot encoding of intermittent light showers. Note: the claimed 'pre-agreed to stop the race if rain fell after lap 8' detail could not be verified in period sources and was dropped from the evidence.
- source: https://www.motorsportmagazine.com/archive/article/september-1974/21/german-grand-prix-18/
- source: https://en.wikipedia.org/wiki/1974_German_Grand_Prix
- source: https://www.chicanef1.com/racetit.pl?year=1974&gp=German%20GP

## 1978

### R12 Austrian Grand Prix
- race: Overcast / Storm / Light Rain / Medium Cloud
- qualifying: Medium Cloud / Medium Cloud / Medium Cloud / Medium Cloud
- evidence: Wikipedia infobox: Weather 'Wet'. The race started DRY at 2:00pm under threatening skies (grandprix.com: 'The race began with rain threatening'); a heavy rainshower hit on lap 4 (not lap 2 as originally claimed), sending Reutemann and others spinning off and forcing a red flag after lap 7. The 47-lap second part restarted at 3:15pm on a soaked track once the rain relented; the track progressively dried and drivers switched to slicks before the finish. Peterson won from Depailler and Villeneuve. Qualifying was dry (Peterson pole 1:37.71). Slot fix: opening slot corrected from Storm to Overcast because the start was dry with rain only threatening.
- source: https://en.wikipedia.org/wiki/1978_Austrian_Grand_Prix
- source: https://www.grandprix.com/gpe/rr309.html

## 1983

Researched + independently checked 2026-07-11 against all 15 championship weekends. Monaco was
the season's only rain-affected race. Belgium, Detroit, and Germany add materially wet timed
qualifying sessions; the race slots for those rounds preserve their documented dry race-day sky.

### R5 Monaco Grand Prix
- race: Light Rain / Heavy Cloud / Medium Cloud / Light Cloud
- qualifying: Rain / Rain / Heavy Rain / Rain
- evidence: Saturday timed qualifying ran in persistent rain. Heavy Sunday-morning rain left a
  damp grid and light precipitation at the start; most front-runners chose wets while Rosberg
  gambled on slicks. The surface then dried and the rain did not return. Light Rain in slot 1 is
  the minimum AMS2 setting that reproduces the wet-tyre start; the three cloud slots model the
  documented drying race.
- source: https://www.motorsportmagazine.com/archive/article/june-1983/28/monaco-grand-prix-11/

### R6 Belgian Grand Prix
- race: Clear / Light Cloud / Clear / Clear
- qualifying: Clear / Heavy Cloud / Rain / Heavy Rain
- evidence: Friday timed qualifying was warm and dry, while Saturday practice and qualifying ran
  in pouring rain. Race day was warm and dry. The mixed qualifying sequence is a conservative
  one-session composite of the two timed days.
- source: https://www.motorsportmagazine.com/archive/article/june-1983/74/le-grand-prix-de-belgique/

### R7 Detroit Grand Prix
- race: Clear / Clear / Clear / Clear
- qualifying: Rain / Heavy Rain / Heavy Cloud / Clear
- evidence: Friday's running began on wet streets and afternoon qualifying took place in hard
  rain; Saturday qualifying dried from grey conditions into sunshine. Sunday was clear and warm.
  The qualifying sequence represents that wet-to-dry timed-session arc without inventing a wet
  race.
- source: https://www.motorsportmagazine.com/archive/article/july-1983/32/the-detroit-grand-prix/

### R10 German Grand Prix
- race: Overcast / Heavy Cloud / Heavy Cloud / Medium Cloud
- qualifying: Heavy Cloud / Rain / Heavy Rain / Rain
- evidence: Saturday practice and qualifying were wet throughout. Rain continued into Sunday
  morning but stopped before warm-up, allowing the surface to dry for a rain-free race under a
  grey sky. The race slots deliberately contain no precipitation.
- source: https://www.motorsportmagazine.com/archive/article/september-1983/44/the-german-grand-prix-3/

The remaining eleven rounds have no material rain slot. San Marino began with residual dampness
after overnight rain but no session precipitation; Austria's shower arrived after Friday
qualifying; the Netherlands report explicitly describes the trace rain as inconsequential. Those
events stay out rather than manufacturing a wet session. All dates were cross-checked against:
https://www.formula1.com/en/results/1983/races

## 1985

### R2 Portuguese Grand Prix
- race: Rain / Heavy Rain / Heavy Rain / Heavy Rain
- evidence: VERIFIED. Wikipedia race infobox: 'Overcast, Cold, Rain'. Rain was already falling at the start and 'continued and got heavier' throughout; conditions were so bad that leader Senna signalled officials to stop the race, and the race ran to the two-hour limit, cut from 70 to 67 laps. Senna's first GP win, one of F1's canonical wet drives. Claimed slot progression (raining at the start, worsening to heavy for the remainder) matches the documented reality; kept unchanged.
- source: https://en.wikipedia.org/wiki/1985_Portuguese_Grand_Prix

### R13 Belgian Grand Prix
- race: Light Rain / Overcast / Medium Cloud / Light Rain
- evidence: VERIFIED with one correction. Wikipedia race infobox: 'Wet/Dry, drying up in later stages'. Rain fell before the race, so the grid formed on a damp track with everyone on wet tyres; the track dried early and the field pitted for slicks (Senna re-took the lead after the stops and won from Mansell). Rain did return in the second half, but only as a brief light shower: a contemporary race account records 'wet tyres were readied, but the shower passed as quickly as it came, and the track stayed dry' â€” nobody switched back to wets. Slot 4 therefore downgraded from Rain to Light Rain; slot 1 Light Rain retained as the encoding of the damp/wet-tyre start (rain had stopped shortly before the green flag), slots 2-3 encode the drying phase.
- source: https://en.wikipedia.org/wiki/1985_Belgian_Grand_Prix
- source: https://f1since81.wordpress.com/2015/04/01/1985-belgian-grand-prix-2/

## 1986

No rain-affected races (all rounds independently verified dry).

## 1988

### R8 British Grand Prix
- race: Heavy Rain / Rain / Rain / Light Rain
- evidence: VERIFIED: Wikipedia race report states the race 'was held in pouring rain, the first wet race since the 1985 Belgian Grand Prix' and stayed wet throughout the 65 laps; Prost withdrew on lap 24 citing terrible handling in the conditions, and Mansell set fastest lap on lap 48 while 'seeking out the wet parts of the track to cool his tyres', confirming the rain eased/track partially dried late. Qualifying was dry (rain arrived on race day only), so no qualifyingSlots. Heavy Rain opening easing to Light Rain matches the documented arc.
- source: https://en.wikipedia.org/wiki/1988_British_Grand_Prix

### R9 German Grand Prix
- race: Rain / Light Rain / Light Rain / Overcast
- evidence: VERIFIED by two independent sources: Wikipedia â€” weekend thunderstorms left the track soaked, everyone started on wets except Piquet who aquaplaned off at the Ostkurve chicane on lap 1, Alliot spun off after gambling on slicks on lap 9, and Prost had a late spin at the Ostkurve, confirming a damp track to the flag. Motor Sport's contemporary 'Regenmeister' report confirms rain actually fell intermittently DURING the race ('sometimes the rain stopped... occasionally some parts of the circuit almost dried out') and that qualifying was dry (good conditions Friday, ferocious heatwave Saturday) â€” so no qualifyingSlots. First-slot Rain soaks the track (AMS2 cannot start pre-wet), tapering to a no-rain Overcast final slot matching the near-drying finish.
- source: https://en.wikipedia.org/wiki/1988_German_Grand_Prix
- source: https://www.motorsportmagazine.com/archive/article/september-1988/10/regenmeister/

### R15 Japanese Grand Prix
- race: Light Cloud / Light Rain / Overcast / Light Rain
- evidence: VERIFIED: Wikipedia infobox weather is 'Cool and mainly dry, some rain toward the end'; the report states rain began on parts of the circuit on lap 14 of 51 (~27% distance, i.e. early in slot 2), benefiting Senna, and that late in the race, on slicks on a now-wet track, Senna gestured for the race to be stopped. Motor Sport's 75-greatest-GPs entry independently confirms mid/late-race rain decided the title ('When the rain came, Prost hung on... but ultimately ceded the race'). The whole field finished on slicks, so Light Rain (never full Rain) is the correct intensity; dry cool start = Light Cloud, mainly-dry mid-race = Overcast, fine rain over the closing laps = Light Rain final slot.
- source: https://en.wikipedia.org/wiki/1988_Japanese_Grand_Prix
- source: https://www.motorsportmagazine.com/special-article/the-75-greatest-grands-prix/84/30-1988-japanese-gp-senna-storms-from-back-to-claim-first-title/

## 1990

### R5 Canadian Grand Prix
- race: Rain / Light Rain / Medium Cloud / Light Cloud
- qualifying: Rain / Rain / Rain / Rain
- evidence: CONFIRMED wet race day. Officials displayed the 'WET RACE' declaration board; heavy Sunday-morning rain left the Montreal track soaked at the start and the entire field started on wet-weather tyres. The rain stopped almost as the race began and the racing line dried quickly (off-line stayed treacherous, causing numerous spins); slick stops came in the first third â€” Berger stopped first, Nannini briefly led before pitting, and Senna won after Berger's jumped-start one-minute penalty dropped him to fourth. Saturday qualifying was also wet, so Friday's dry times decided the grid. The Rain-opening slot sequence is the correct AMS2 encoding: a soaked start line is only achievable with rain in slot 1, and the taper Rain -> Light Rain -> Medium Cloud -> Light Cloud reproduces the early-race drying that forced the wets-to-slicks stops.
- source: https://en.wikipedia.org/wiki/1990_Canadian_Grand_Prix
- source: https://www.grandprix.com/races/canadian-gp-1990.html
- source: https://www.motorsportmagazine.com/archive/article/july-1990/14/canadian-grand-prix-a-bit-confused/

## 1991

### R2 Brazilian Grand Prix
- race: Medium Cloud / Heavy Cloud / Overcast / Rain
- evidence: VERIFIED WET, sequence corrected. Wikipedia infobox: 'Cloudy at start, rainy later' (humidity 95%). Race ran dry until rain arrived in the closing laps; Senna, gearbox failing, drove the last ~7 laps locked in sixth gear and per grandprix.com 'the leading three struggled on to the line in terrible conditions' with Senna beating Patrese by ~3 seconds. Original slot 1 'Clear' contradicted the documented cloudy start, so the sequence was corrected to a building-cloud progression into the late shower.
- source: https://en.wikipedia.org/wiki/1991_Brazilian_Grand_Prix
- source: https://www.grandprix.com/gpe/rr502.html

### R3 San Marino Grand Prix
- race: Rain / Light Rain / Medium Cloud / Clear
- evidence: VERIFIED WET as claimed. Wikipedia infobox reads exactly 'Wet at start, dry by finish'. Heavy pre-race rain left a soaked track: Prost spun off at Rivazza on the formation lap, stalled and never took the start; Berger spun at the same spot but continued. Track dried through the race with the wet-to-dry transition driving tyre strategy. Claimed wet-start/dry-finish progression matches the documented reality.
- source: https://en.wikipedia.org/wiki/1991_San_Marino_Grand_Prix

### R14 Spanish Grand Prix
- race: Light Rain / Overcast / Medium Cloud / Rain
- evidence: VERIFIED WET as claimed. Wikipedia infobox: 'Warm and overcast, drying'; race report: 'On race morning it was raining, but by start time it had stopped, although the track was still wet' â€” the damp start produced the famous Mansell-Senna wheel-to-wheel duel. Prost then Berger were the first front-runners to pit for dry tyres as it dried, and 'the rain returned and Senna had a dramatic spin at the last corner, dropping from second to fifth'. Slot 1 Light Rain is the correct authoring device for a wet-but-not-raining start; drying middle and returning late rain match the sources.
- source: https://en.wikipedia.org/wiki/1991_Spanish_Grand_Prix

### R16 Australian Grand Prix
- race: Storm / Thunderstorm / Storm / Storm
- evidence: VERIFIED WET as claimed. Wikipedia infobox: 'Torrential rain'. Race stopped after 16 of 81 scheduled laps with results declared from the end of lap 14 â€” the shortest World Championship race ever at the time â€” and half points awarded (first time since Monaco 1984). Senna won and said 'I don't think that was a race, it was just a matter of staying on the circuit.' Downpour intensified during the race, so the all-storm sequence is appropriate (the race effectively ran entirely inside slot 1 anyway).
- source: https://en.wikipedia.org/wiki/1991_Australian_Grand_Prix
- source: https://www.racefans.net/2008/11/25/1991-australian-gp-flashback-video/

## 1992

### R4 Spanish Grand Prix
- race: Light Rain / Rain / Rain / Heavy Rain
- evidence: Independently verified wet from the start and intensifying: StatsF1 marks the race with a rain icon and says rain was still falling on Montmelo on race Sunday; grandprix.com's report states 'at the start all the major runners were on wet tyres'; Wikipedia's narrative describes a damp start with the rain intensifying (Patrese spun into the wall on lap 20, Senna spun off near the end), Mansell winning in full-wet conditions. Mansell's fastest lap came as early as lap 10 â€” the track only got slower/wetter â€” supporting the intensifying progression. Note: a Wikipedia infobox reading of 'dry at start' is contradicted by the wets-at-start fact and two independent sources; resolved in favor of wet-at-start. Light Rain opener (wet track, wets viable from lap 1) building to Heavy Rain is faithful. Saturday qualifying was washed out ('dreadful weather on Saturday') and the grid was set on dry Friday times, so qualifyingSlots are deliberately omitted.
- source: https://en.wikipedia.org/wiki/1992_Spanish_Grand_Prix
- source: https://www.grandprix.com/gpe/rr520.html
- source: https://www.statsf1.com/en/1992/espagne.aspx

### R8 French Grand Prix
- race: Overcast / Heavy Rain / Light Rain / Rain
- evidence: Verified wet, but the researcher's opening slot was corrected: the race began DRY (Wikipedia infobox 'Dry, then raining'; the field started on slicks), so quarter 1 is Overcast, not Light Rain. Rain then fell so heavily that the race was red-flagged (stoppage around laps 18-20 of 69); after the rain decreased the race restarted and was decided on aggregate times, and rain returned in the closing stint (~lap 61) when 'everyone pitted for wets'. Quarters map cleanly onto the 69-lap race: dry opening third, heavy-rain stoppage window, easing/drying restart phase, and the late rain return. The stoppage downpour is modeled as Heavy Rain rather than Storm because the field resumed and raced through it on wets.
- source: https://en.wikipedia.org/wiki/1992_French_Grand_Prix
- source: https://www.grandprix.com/gpe/rr524.html

### R12 Belgian Grand Prix
- race: Light Rain / Rain / Light Rain / Overcast
- evidence: Verified wet-dry classic, with the evidence wording corrected: the race started under overcast skies on a DRY track with the field on slicks (Wikipedia infobox 'Overcast, brief rain mid-race'; grandprix.com: started dry), rain arrived within the opening laps (~lap 3) sending almost everybody to the pits for wets (Senna gambled on staying out and dropped to 12th), the track began drying by lap 28, and Schumacher's perfectly timed lap-30 switch back to slicks won him his maiden GP on a dry-finishing track. The Light Rain opener is retained deliberately: rain arrived ~lap 3 of 44 â€” inside the first weather quarter â€” so an Overcast opener would delay rain past lap ~10 and lose the race-defining early scramble onto wets; Light Rain errs by only a couple of laps in the other direction. One qualifying day was wet (Berger's 160 mph wet crash at the entrance to Eau Rouge), but the grid was set on the dry day, so qualifyingSlots are deliberately omitted.
- source: https://en.wikipedia.org/wiki/1992_Belgian_Grand_Prix
- source: https://www.grandprix.com/gpe/rr528.html

## 1993

### R1 South African Grand Prix
- race: Clear / Clear / Clear / Thunderstorm
- qualifying: Clear / Clear / Clear / Clear
- evidence: CONFIRMED (3 sources). Dry and very hot (33C) for nearly the full 72 laps; with two laps to go 'there was lightning and the sky darkened. Soon it was lashing down with rain at the back half of the circuit' (f1since81), Warwick slid off into the tyre wall on the final lap, and grandprix.com records 'a late race rain storm'. Wikipedia infobox: 'Very hot and humid, with torrential thunderstorms. Air temp: 33 C'. Lightning is explicitly documented, so Thunderstorm (final slot only) is the correct token; the storm arriving with ~2 laps left is later than a 4-slot model can express, but Clear/Clear/Clear/Thunderstorm is the best possible encoding.
- source: https://en.wikipedia.org/wiki/1993_South_African_Grand_Prix
- source: https://www.grandprix.com/gpe/rr533.html
- source: https://f1since81.wordpress.com/2017/12/27/1993-south-african-grand-prix/

### R2 Brazilian Grand Prix
- race: Light Cloud / Heavy Rain / Medium Cloud / Clear
- qualifying: Clear / Clear / Clear / Clear
- evidence: CONFIRMED (2 sources). Wikipedia infobox: 'Dry first then torrential rain for a short period; later drying and staying dry for the rest of the race'. A 'huge rain storm' (grandprix.com) broke around lap 25 of 71; by lap 27 Katayama and Suzuki crashed bringing out the safety car, and on lap 30 Prost, still on slicks, slid into Fittipaldi's wrecked Minardi. The rain then stopped, the safety car came in, and the field returned to slicks on a rapidly drying track. Corrected evidence nit: Wikipedia calls this safety-car deployment 'the second time this had been seen in Formula 1' (the first was the 1973 Canadian GP) - it was the first of the modern official era, not F1's first-ever as the researcher wrote. Rain at ~35-42% distance sits squarely in slot 2, so the sequence stands.
- source: https://en.wikipedia.org/wiki/1993_Brazilian_Grand_Prix
- source: https://www.grandprix.com/gpe/rr534.html

### R3 European Grand Prix
- race: Rain / Medium Cloud / Rain / Light Rain
- qualifying: Clear / Clear / Clear / Clear
- evidence: CONFIRMED (2 sources). 'It was raining at the start as Prost and Hill led away' (grandprix.com) - the whole field on wets for Senna's legendary opening lap - then repeated wet-dry cycles all afternoon (Wikipedia infobox: 'Very cold, rain with dry spells'); Prost made seven pit stops to winner Senna's four. Corrected: grandprix.com records 'the rain came again in the closing laps and Senna decided that this time he had to stop. Hill and Prost followed him in' - the race FINISHED under rain, contradicting the researcher's 'dried again before the finish' wording; the Light Rain final slot is therefore correct and kept. Four slots cannot capture all five-plus transitions; wet / dry-spell / wet / light-wet is the faithful coarse encoding.
- source: https://en.wikipedia.org/wiki/1993_European_Grand_Prix
- source: https://www.grandprix.com/gpe/rr535.html

### R4 San Marino Grand Prix
- race: Rain / Light Rain / Medium Cloud / Clear
- qualifying: Clear / Clear / Clear / Clear
- evidence: CONFIRMED (2 sources). 'It was grey and overcast on Sunday morning and it started to rain at lunch time... the rain then cleared away although the track was still wet at the start' (grandprix.com); the field started on wets, then 'Senna then pitted for slick tyres and was followed by the other leaders' as the track dried fully. Wikipedia infobox: 'Wet at start, dry later'. Note: it was not actively raining at lights-out (wet track only), but AMS2 has no pre-wetted-track option, so Rain in slot 1 tapering through Light Rain to Clear is the correct authoring device to reproduce the historical wet start and full dry-out.
- source: https://en.wikipedia.org/wiki/1993_San_Marino_Grand_Prix
- source: https://www.grandprix.com/gpe/rr536.html

### R15 Japanese Grand Prix
- race: Overcast / Light Rain / Rain / Medium Cloud
- qualifying: Clear / Clear / Clear / Clear
- evidence: CONFIRMED (2 sources). Started dry and cloudy; light rain arrived around the first pit stops ('it was not wet enough for a tyre change' at first - grandprix.com), intensified by lap 21 of 53 when Senna passed Prost at Spoon and the leaders took wets, Senna building a 30s lead by lap 27 in the wet; 'the rain then eased and the track began to dry' and the field was back on slicks by ~lap 42. Wikipedia infobox: 'Dry/wet, warm, cloudy'. Rain onset in slot 2, peak wet in slot 3, drying slot 4 - the sequence maps cleanly onto the documented lap timeline.
- source: https://en.wikipedia.org/wiki/1993_Japanese_Grand_Prix
- source: https://www.grandprix.com/gpe/rr547.html

## 1995

### R3 San Marino Grand Prix
- race: Rain / Light Rain / Medium Cloud / Clear
- qualifying: Clear / Clear / Clear / Clear
- evidence: VERIFIED. Wikipedia infobox: 'Heavy rain before the start, before brightening up.' Steady rain fell all Sunday morning; the track was wet at the start and the six drivers who started on wet tyres gained about five seconds a lap before the circuit dried and they switched back to slicks. Both qualifying sessions were dry (Friday cool and fast, Saturday hotter and slower). Slot sequence kept: Rain in slot 1 is required to render the wet start, drying to Clear matches the documented brightening.
- source: https://en.wikipedia.org/wiki/1995_San_Marino_Grand_Prix

### R11 Belgian Grand Prix
- race: Heavy Cloud / Rain / Medium Cloud / Heavy Rain
- qualifying: Rain / Light Rain / Medium Cloud / Rain
- evidence: VERIFIED, one slot corrected. Wikipedia infobox: 'Cloudy, then heavy rain.' Race started dry on slicks; light rain began spotting the track around lap 20 and Hill pitted for wets on lap 24 while Schumacher stayed on slicks; the rain then STOPPED around lap 25 and Hill's wets tailed off as the track dried (Schumacher's slick gamble paid off); the rain later intensified again, bringing out the safety car, and both leaders finished on wets in heavy rain. Slot 3 changed from Light Rain to Medium Cloud because the documented mid-race phase is rain-stopped/drying with slicks fastest, which continuous Light Rain contradicts. Qualifying was a documented wet-dry mixed session leaving Hill 8th and Schumacher 16th; the wet-dry-wet qualifying sequence is retained as a faithful encoding.
- source: https://en.wikipedia.org/wiki/1995_Belgian_Grand_Prix
- source: https://autoaction.com.au/2025/07/24/1995-belgian-gp-schumacher-stars-hill-rages

### R14 European Grand Prix
- race: Light Rain / Overcast / Medium Cloud / Clear
- qualifying: Overcast / Light Rain / Rain / Rain
- evidence: VERIFIED. Wikipedia infobox: 'Rain, later dried out, air temperature 11 C.' The track surface was damp at the start and dried as the race progressed; most teams started on wet tyres while Ferrari and McLaren gambled on slicks anticipating the dry-out. Both one-hour qualifying sessions were interrupted by rain with little running afterwards; 17 of 24 drivers set their best time in the drier first period, so the qualifying sequence starting Overcast with rain arriving and persisting is faithful. Race slots kept: damp start (Light Rain slot 1) drying through Overcast/Medium Cloud to a fully dry finish.
- source: https://en.wikipedia.org/wiki/1995_European_Grand_Prix

### R16 Japanese Grand Prix
- race: Light Rain / Medium Cloud / Light Rain / Rain
- qualifying: Clear / Clear / Clear / Clear
- evidence: VERIFIED. Wikipedia infobox: 'Rain, later dried out.' The track was damp from morning rain and the whole field started on wet tyres; Alesi switched to slicks around lap 7 as the track dried and others followed; late in the race rain returned, initially only at the Spoon Curve end, where Hill ran off (and later spun off and retired) and Coulthard also went off. Practice and qualifying were dry throughout. Slot sequence kept: damp start, mid-race dry phase, rain building again over the closing stint matches the documented lap-7 dry-out and late localized rain.
- source: https://en.wikipedia.org/wiki/1995_Japanese_Grand_Prix

## 1997

### R5 Monaco Grand Prix
- race: Rain / Heavy Rain / Rain / Rain
- qualifying: Clear / Clear / Clear / Clear
- evidence: Verified wet. Light rain began about 30 minutes before the start (grandprix.com: at the lights it was still 'a flurry of rain drops') and WORSENED during the warm-up lap and opening laps; Williams started on slicks expecting it to clear and paid for it, Schumacher won on intermediates. Wikipedia infobox: 'Overcast, cold and rain, 11 C'. The track never dried - no driver returned to slicks - and the race was flagged at 62 of 78 laps on the two-hour limit. Corrected sequence: wet-but-lighter opening slot building to the documented worst spell in the first third, then sustained rain to the flag (the researcher's Heavy Rain opener inverted the documented intensification). Qualifying was dry (Frentzen pole 1:18.216 on a dry Saturday).
- source: https://en.wikipedia.org/wiki/1997_Monaco_Grand_Prix
- source: https://www.grandprix.com/gpe/rr602.html

### R8 French Grand Prix
- race: Clear / Light Cloud / Heavy Cloud / Light Rain
- qualifying: Clear / Clear / Clear / Clear
- evidence: Verified wet-late. Wikipedia infobox: 'Dry at first, rain in closing stages'. Grandprix.com: rainy morning cleared to bright midday sunshine, clouds returned as the cars went to the grid, and a nearby thunderstorm brought slippery but intermittent drizzle from roughly nine laps out (about lap 63 of 72), easing again before the flag; leaders Schumacher and Frentzen stayed on slicks to finish 1-2 while others gambled on intermediates. Researcher's sequence confirmed as the best 4-slot fit: dry sunny start, cloud building through the race, Light Rain (not Rain) confined to the final slot.
- source: https://en.wikipedia.org/wiki/1997_French_Grand_Prix
- source: https://www.grandprix.com/gpe/rr605.html

### R12 Belgian Grand Prix
- race: Rain / Medium Cloud / Light Cloud / Clear
- qualifying: Clear / Clear / Clear / Clear
- evidence: Verified wet-start. A 'tropical rainstorm' broke about 30 minutes before the start and fell for roughly 20 minutes, leaving rivers and puddles on the track; the race began behind the safety car (first safety-car start in F1 history, 3 laps). Both Wikipedia ('Wet, then drying out, up to 28 C') and grandprix.com ('the deluge was a pre-race phenomenon') confirm NO rain fell during the race itself - the track dried steadily, Schumacher (intermediates from the start) took slicks on lap 14 of 44 and won by 26s. Corrected sequence: the researcher's slot 2 'Light Rain' would keep rain falling to half-distance, contradicting slicks-by-lap-14; one Rain slot soaks the track for the safety-car start, then dry brightening slots reproduce the documented drying under 28 C sunshine.
- source: https://en.wikipedia.org/wiki/1997_Belgian_Grand_Prix
- source: https://www.grandprix.com/gpe/rr609.html

## 2000

### R6 European Grand Prix
- race: Overcast / Rain / Heavy Rain / Heavy Rain
- qualifying: Light Rain / Overcast / Rain / Heavy Rain
- evidence: VERIFIED: Wikipedia race report confirms the race began overcast and dry (track 12C), light rain fell on lap 10 of 67, heavy rain on lap 12 forced the entire field to pit for wet-weather tyres, the rain kept intensifying (no safety car despite worsening conditions) and persisted to the finish with Schumacher aquaplaning on worn tyres in the closing laps. Qualifying was damp from an earlier shower and a heavy rainstorm made the track slippery in the final 25 minutes, fixing the grid. Slot progression (dry overcast opening ~15% of distance, rain building through the second quarter, heavy rain to the flag) matches the documented timeline within AMS2's 4-slot interpolation.
- source: https://en.wikipedia.org/wiki/2000_European_Grand_Prix

### R8 Canadian Grand Prix
- race: Medium Cloud / Light Rain / Rain / Heavy Rain
- evidence: VERIFIED: Wikipedia race report confirms a dry, cloudy start (air 17C/track 21C, 70% rain chance), light rain from lap 23 of 69 slowing lap times, sharp intensification after lap 42 with every driver onto wet tyres (last conversions by lap 46), and rain increasing again around lap 65 through to the finish of Schumacher's win. The four quarters map almost exactly onto the slots: lap 23 = 33% (slot 2 Light Rain), lap 42 = 61% (slot 3 Rain), lap 65 = 94% (slot 4 Heavy Rain). Qualifying was dry and sunny, so no qualifyingSlots â€” correct.
- source: https://en.wikipedia.org/wiki/2000_Canadian_Grand_Prix

### R11 German Grand Prix
- race: Clear / Light Cloud / Overcast / Rain
- qualifying: Light Rain / Overcast / Light Rain / Overcast
- evidence: VERIFIED: Wikipedia race report confirms the race began in dry weather; light rain hit the stadium section on lap 32 of 45 (71% distance, right at the slot-3/4 boundary), spread on lap 33, became a stadium downpour on lap 35 with heavier rain again on lap 44. The rain was localized â€” it never reached the track's outer edge, letting Barrichello win on slick tyres while others took wets â€” so capping slot 4 at Rain rather than Heavy Rain is the right AMS2 compromise. Qualifying is documented as damp weather with intermittent rain (drivers on grooved and wet compounds, initially hesitant to run), supporting the alternating Light Rain/Overcast qualifying slots.
- source: https://en.wikipedia.org/wiki/2000_German_Grand_Prix

### R13 Belgian Grand Prix
- race: Rain / Medium Cloud / Clear / Clear
- evidence: VERIFIED with modeling caveat: Wikipedia race report confirms overnight/morning rain stopped about an hour before the start but left standing water, heavy spray and poor visibility, so the 44-lap race started behind the safety car on full wets; the track dried rapidly (Alesi to slicks lap 4, the Schumachers lap 6, Hakkinen lap 7, everyone by lap 9) and NO rain fell during the race itself, which stayed dry through Hakkinen's famous lap-41 pass. Slot 1 Rain is a deliberate track-soaking device (AMS2 custom weather cannot author a pre-wet track without rain); the wet-start-to-fast-dry-out progression is faithful, though it keeps rain falling slightly longer (to ~25% distance) than history (slicks by 20%).
- source: https://en.wikipedia.org/wiki/2000_Belgian_Grand_Prix

### R15 United States Grand Prix
- race: Light Rain / Overcast / Medium Cloud / Clear
- evidence: VERIFIED with modeling caveat: Wikipedia race report confirms heavy rain fell between the end of warm-up and the race, so every driver except Herbert started on intermediate tyres on a damp, cool circuit (air 12-14C, track 13-14C); no rain fell during the race itself â€” the oval section dried quickly while the infield kept standing water and less grip before drying. Slot 1 Light Rain is the track-soaking device for the damp intermediate-tyre start (Light Rain rather than Rain correctly matches inters, not full wets), drying through slots 2-4.
- source: https://en.wikipedia.org/wiki/2000_United_States_Grand_Prix

### R16 Japanese Grand Prix
- race: Medium Cloud / Overcast / Light Rain / Light Rain
- evidence: VERIFIED: Wikipedia race report confirms the title decider was dry and overcast at the start (air 22C/track 23C, rain forecast); a brief lap-4 drizzle had no effect, then light rain from lap 30 of 53 (57% distance) made the track slippery and intensified slightly around lap 46, but was never heavy enough for wet tyres â€” the entire field finished on dries, with the slippery-phase caution shaping the Schumacher/Hakkinen pit battle. Capping slots 3-4 at Light Rain is exactly right; anything wetter would force wet tyres AMS2-side and contradict history.
- source: https://en.wikipedia.org/wiki/2000_Japanese_Grand_Prix

## 2005

### R16 Belgian Grand Prix
- race: Rain / Light Rain / Overcast / Medium Cloud
- qualifying: Overcast / Overcast / Overcast / Medium Cloud
- evidence: VERIFIED WET RACE (Wikipedia race-detail weather: 'Wet and dry'). Heavy rain fell during Sunday morning warm-up, leaving the track damp/soaked at the start with light drizzle persisting into the formation lap; virtually the whole field started on intermediates. Mid-race dry-tyre gambles failed on the still-greasy surface (Michael Schumacher's slick gamble went unrewarded; Ralf Schumacher pitted for dries and spun at Les Combes ~lap 25). The track dried slowly and incompletely; dry tyres only became viable in the final laps â€” Webber's Williams switched to dries after 38 of 44 laps and he became the fastest man on the circuit, finishing P4, while Button kept worn intermediates to the flag in P3. Raikkonen won on a drying track. Slot rationale: slot-1 Rain is required to reproduce the soaked start (AMS2 has no pre-race soaking), easing to Light Rain keeps the track wet through half-distance where slicks demonstrably failed, then Overcast/Medium Cloud give the documented slow late dry-out. QUALIFYING CORRECTED: the researcher's 'Light Rain' opening slot is contradicted by the evidence â€” Saturday's 13:00 one-lap session ran on a dry racing line (Montoya pole 1:46.391, unambiguous dry pace with race fuel; McLarens split by 0.049s; normal-order grid, no wet scramble; grip improved through the session; no source reports rain falling during qualifying). The wet Saturday-adjacent running was FRIDAY practice 2 (abandoned, no valid times, Liuzzi crash). Qualifying slots therefore rain-free grey-sky cloud only.
- source: https://en.wikipedia.org/wiki/2005_Belgian_Grand_Prix
- source: https://www.autosport.com/general/news/the-2005-belgian-grand-prix-review-5076598/5076598/
- source: https://www.press.bmwgroup.com/canada/article/detail/T0025353EN/belgian-grand-prix-spa-francorchamps-11th-september-2005-summary-gamble-pays-off-as-webber-finishes-fourth-in-spa?language=en
- source: https://www.formula1.com/en/results/2005/races/786/belgium/qualifying/0

## 2006

### R13 Hungarian Grand Prix
- race: Rain / Rain / Light Rain / Medium Cloud
- qualifying: Clear / Clear / Clear / Clear
- evidence: VERIFIED: first wet Hungarian GP in history. Wikipedia infobox weather 'Cool and rainy, up to 20C'. Heavy pre-race rain left the track soaked; all drivers started on intermediates except Barrichello on full wets. The cool, overcast day dried the track only slowly; the switch to dry tyres came in the closing stint (~lap 51 of 70), when Alonso lost a right-rear wheel nut after his dry-tyre stop and crashed, handing Button his maiden win on a late change to dries. Saturday qualifying was independently confirmed DRY (Raikkonen pole 1:19.599; Alonso and Schumacher carried 2-second penalties) â€” qualifyingSlots here are all Clear, i.e. no wet qualifying; only the race slots encode weather deviation.
- source: https://en.wikipedia.org/wiki/2006_Hungarian_Grand_Prix
- source: https://www.autosport.com/f1/news/raikkonen-snatches-last-gasp-hungary-pole-4404390/4404390/
- source: https://www.statsf1.com/en/2006/hongrie.aspx

### R16 Chinese Grand Prix
- race: Rain / Light Rain / Medium Cloud / Light Rain
- qualifying: Rain / Rain / Rain / Rain
- evidence: VERIFIED: heavy rain fell before the start and all 22 cars began on intermediates on a soaked track; no further rain fell for most of the race and the surface gradually dried (dry-tyre stops laps 35-41 of 56, letting Schumacher pass Alonso for his 91st and final F1 win) before light rain returned in the closing laps. Saturday 30 September qualifying was also wet â€” held in overcast, rainy conditions on a slippery track; Alonso's pole of 1:44.360 (confirmed against formula1.com official results) is ~10s off Shanghai dry pace. An initial Wikipedia auto-summary claiming dry qualifying was cross-checked and disproven.
- source: https://en.wikipedia.org/wiki/2006_Chinese_Grand_Prix
- source: https://www.formula1.com/en/results/2006/races/805/china/qualifying
- source: https://www.motorsportmagazine.com/articles/single-seaters/f1/china-2006-the-last-time-michael-schumacher-won-an-f1-race/

## 2008

### R6 Monaco Grand Prix
- race: Rain / Light Rain / Medium Cloud / Clear
- evidence: VERIFIED. Wikipedia infobox: 'Wet, drying later'. Race started fully wet after morning rain resumed before the start â€” nearly all drivers started on standard wets (Piquet on extremes), with aquaplaning in the opening laps. A dry line emerged mid-race and Hamilton switched to dry tyres at his lap-54 stop just as dries became the strongest strategy; no rain returned in the second half. The slow wet pace meant the race ended at the 2-hour limit after 76 of the scheduled 78 laps. Slot progression wet-to-dry matches; final 'Clear' is a modeling choice (sources document only 'drying later', no rain).
- source: https://en.wikipedia.org/wiki/2008_Monaco_Grand_Prix

### R8 French Grand Prix
- race: Medium Cloud / Overcast / Overcast / Light Rain
- evidence: VERIFIED (marginal wet). Wikipedia: 'The conditions on the grid were dry before the race, although the sky was overcast'; 'On lap 55, light rain started to fall' and 'it would continue to rain lightly for the next few laps' but 'was never heavy enough to be a problem to the drivers' â€” no wet-tyre changes. Rain genuinely fell during the race (lap 55 of 70 sits in the fourth slot), so a single cosmetic Light Rain final slot is the correct model; the rest overcast/dry.
- source: https://en.wikipedia.org/wiki/2008_French_Grand_Prix

### R9 British Grand Prix
- race: Rain / Rain / Heavy Rain / Light Rain
- evidence: VERIFIED. Wikipedia: 'persistent rain in the morning, leaving standing water on the track, although it had abated by the time the race began' â€” the entire field started on intermediates (slot 1 'Rain' is required to model the soaked track, and rain continued intermittently early). The rain returned around the lap-21 pit-stop window, then got heavier mid-race with some drivers taking extreme wets (~laps 35-38) before easing as forecast; it stopped before the finish but the track stayed wet. Hamilton won by 68 seconds, the largest margin since the 1995 Australian GP. Sequence matches the documented arc across the 60 laps.
- source: https://en.wikipedia.org/wiki/2008_British_Grand_Prix
- source: https://www.formula1.com/en/latest/article/greatest-races-7-hamilton-reclaims-title-momentum-with-a-sublime-wet-weather.3yJNg1yWzRUdBLOX8MYz9s

### R13 Belgian Grand Prix
- race: Medium Cloud / Medium Cloud / Heavy Cloud / Rain
- evidence: VERIFIED, SLOTS CORRECTED. Wikipedia infobox: 'Cloudy, rain in last 3 laps' â€” the claimed opening 'Clear' contradicted the documented cloudy sky, so slots were corrected to a cloudy build (Medium Cloud, Medium Cloud, Heavy Cloud, Rain). 'Heavy rain began to fall on lap 41' of 44, triggering the Hamilton-Raikkonen wet battle, Raikkonen's barrier crash, and Hamilton's Bus Stop chicane-cut penalty. Final slot kept at 'Rain' rather than 'Heavy Rain' because the fourth slot spans roughly laps 33-44, of which only the last three were wet.
- source: https://en.wikipedia.org/wiki/2008_Belgian_Grand_Prix

### R14 Italian Grand Prix
- race: Heavy Rain / Rain / Light Rain / Overcast
- qualifying: Rain / Rain / Heavy Rain / Heavy Rain
- evidence: VERIFIED. Wikipedia infobox: 'Heavy rain, dry towards the end'. Heavy pre-race rain made the track very slippery; the race began behind the safety car (rolling start) with all drivers on extreme wets, air temp 14C. 'Light rain began to fall on lap 26, though only lasted five minutes'; drivers switched to intermediates from ~lap 28 and 'by lap 36 the majority of the field were running on intermediate tyres'; Raikkonen set fastest lap on the final lap (53) on the dried track. Qualifying was very wet with rain intensifying through the session â€” Vettel took a shock pole (1:37.555) en route to Toro Rosso's maiden win, so the wet qualifyingSlots are also verified.
- source: https://en.wikipedia.org/wiki/2008_Italian_Grand_Prix

### R18 Brazilian Grand Prix
- race: Rain / Medium Cloud / Light Cloud / Rain
- evidence: VERIFIED. Wikipedia infobox: 'Rain at beginning and end, otherwise drying'. Heavy rain hit at 14:56, four minutes before the scheduled 15:00 start, delaying it ten minutes; every team bar BMW fitted intermediates before the formation lap (Kubica started from the pit lane after changing). The track dried rapidly â€” first dry-tyre stop at the end of lap 2, most of the field on dries by lap 11. 'Light rain began to fall on lap 63' and intensified significantly by lap 69 of 71, with Glock sliding on dry tyres as Hamilton passed him at the final corners to take the title. Wet-dry-wet slot arc matches (slot 4 covers ~laps 53-71).
- source: https://en.wikipedia.org/wiki/2008_Brazilian_Grand_Prix

## 2010

Researched 2026-07-11 across all 19 championship weekends. Five races and three qualifying
sessions were materially rain-affected; Belgium appears in both groups. The other 12 races keep
Clear defaults. Practice-only rain was not promoted into race or qualifying data.

### R2 Australian Grand Prix
- race: Light Rain / Overcast / Overcast / Overcast
- evidence: Rain arrived roughly 15 minutes before the start and the field began on
  intermediates. It stopped shortly after the start; Button changed to slicks on lap 7 and most
  rivals followed on laps 8-9. Slot 1 supplies the necessary pre-wet surface, while the overcast
  tail reflects the gloomy but drying remainder.
- source: https://www.formula1.com/en/results/2010/races/861/australia/race-result
- source: https://www.grandprix.com/races/australian-gp-2010-race-report-button-calls-the-shots-in-australia.html
- source: https://www.motorsportmagazine.com/articles/single-seaters/f1/2010-australian-grand-prix-report/

### R3 Malaysian Grand Prix
- race: Clear / Clear / Clear / Clear
- qualifying: Rain / Light Rain / Heavy Rain / Light Rain
- evidence: Rain began before Q1. Drivers moved from intermediates to full wets as it worsened;
  Q2 eased, while Q3 was red-flagged after three minutes in severe rain and resumed as conditions
  improved. Webber's intermediate gamble decided pole. Sunday's race was dry.
- source: https://www.formula1.com/en/results/2010/races/862/malaysia/qualifying
- source: https://www.grandprix.com/races/malaysian-gp-2010-qualifying-report-webber-shows-up-the-clowns.html

### R4 Chinese Grand Prix
- race: Light Rain / Rain / Light Rain / Rain
- evidence: The field started on slicks in light rain. The first shower stopped almost
  immediately, a longer shower around lap 19 required intermediates, and rain intensified again
  near the finish. Saturday qualifying was warm and dry.
- source: https://www.formula1.com/en/results/2010/races/863/china/race-result
- source: https://www.autosport.com/f1/news/the-complete-2010-chinese-gp-review-5080815/5080815/
- source: https://www.motorsportmagazine.com/articles/single-seaters/f1/2010-chinese-grand-prix-report/

### R7 Turkish Grand Prix
- race: Clear / Light Cloud / Heavy Cloud / Light Rain
- evidence: The race began hot and dry as cloud built from the west. Very light drizzle arrived
  late; drivers were warned about reduced grip but remained on slicks. The final Light Rain slot
  models the genuine marginal shower without turning the event into a wet-tyre race.
- source: https://www.formula1.com/en/results/2010/races/866/turkey/race-result
- source: https://www.autosport.com/f1/news/the-2010-formula-1-race-by-race-review-5081849/5081849/

### R13 Belgian Grand Prix
- race: Light Rain / Medium Cloud / Heavy Cloud / Rain
- qualifying: Light Cloud / Light Rain / Medium Cloud / Light Rain
- evidence: Qualifying began dry, but Petrov's red flag let a shower reach the circuit during Q1;
  Q2 dried before spots returned late in Q3. The race had a lap-one shower, a long dry middle,
  then a proper shower from lap 33 of 44 that forced late intermediate/full-wet stops.
- source: https://www.formula1.com/en/results/2010/races/872/belgium/qualifying
- source: https://www.formula1.com/en/results/2010/races/872/belgium/race-result
- source: https://www.motorsportmagazine.com/articles/single-seaters/f1/2010-belgian-grand-prix-report/
- source: https://www.autosport.com/f1/news/the-complete-2010-belgian-gp-review-5081561/5081561/

### R17 Korean Grand Prix
- race: Storm / Rain / Light Rain / Overcast
- evidence: Standing water and near-zero visibility produced a safety-car start and red flag after
  three laps. After a stoppage of more than 45 minutes, the race resumed behind the safety car on
  full wets, moved to intermediates as rain eased, and never became dry enough for slicks before
  darkness. Storm is reserved here for the undriveable opening water level.
- source: https://www.formula1.com/en/results/2010/races/876/south-korea/race-result
- source: https://www.autosport.com/f1/news/the-complete-2010-korean-gp-review-5081744/5081744/
- source: https://www.motorsportmagazine.com/articles/single-seaters/f1/i-was-there-when-2010-korean-gp/

### R18 Brazilian Grand Prix
- race: Clear / Clear / Clear / Clear
- qualifying: Light Rain / Light Rain / Overcast / Medium Cloud
- evidence: Intermittent rain kept the field on intermediates through Q1, Q2, and the opening Q3
  runs. A dry line finally formed and drivers switched to slicks for the last five minutes, when
  Hülkenberg took pole. Sunday's race was dry.
- source: https://www.formula1.com/en/results/2010/races/877/brazil/qualifying
- source: https://www.grandprix.com/races/brazilian-gp-2010-qualifying-report-hulkenberg-takes-sensational-brazil-pole.html
- source: https://www.theguardian.com/sport/2010/nov/06/williams-brazil-grand-prix

False positives checked: German rain was practice-only; Singapore FP1 began on residual moisture;
Abu Dhabi had a pre-FP1 shower; and Japan's washed-out Saturday qualifying produced no timed laps.
The actual Japanese qualifying ran Sunday morning in dry sunshine, followed by a dry race:
https://www.grandprix.com/races/japanese-gp-2010-sunday-qualifying-team-quotes.html.

## 2016

### R6 Monaco Grand Prix
- race: Rain / Light Rain / Overcast / Overcast
- qualifying: Clear / Clear / Clear / Clear
- evidence: VERIFIED. Wikipedia infobox: 'Rainy at start, dry later', 17-18 C air. Rain shortly before the start forced a safety-car start with all cars on full wets (SC in after 7-8 laps); field moved to intermediates from ~lap 7, Hamilton went straight from wets to ultrasoft slicks on lap 31 of 78; skies stayed grey and light rain returned 3 laps from the flag with no driver changing tyres. Slot 4 corrected from 'Medium Cloud' to 'Overcast': no source supports late brightening â€” the afternoon stayed grey with returning drizzle (drizzle too light/brief for a Light Rain slot, which would overweight a 3-lap shower across the final quarter). Saturday qualifying was dry.
- source: https://en.wikipedia.org/wiki/2016_Monaco_Grand_Prix
- source: https://press.pirelli.com/2016-monaco-grand-prix---race/
- source: https://www.planetf1.com/news/fia-explain-delay-wet-monaco-grand-prix

### R10 British Grand Prix
- race: Rain / Light Rain / Medium Cloud / Clear
- qualifying: Clear / Clear / Clear / Clear
- evidence: VERIFIED. A heavy shower shortly before the start soaked Silverstone; the race began behind the safety car with every car on full wets, released at the end of lap 5. Several drivers pitted for intermediates as soon as the SC came in; with sun out and warm conditions the track dried steadily and the field switched to slicks from around one-third distance (~lap 17 of 52), finishing in dry, bright conditions. Claimed sequence matches documented wet-to-dry-sunny progression exactly; slot 1 'Rain' is the correct authoring choice to produce the soaked track at lights-out. Saturday qualifying was dry.
- source: https://en.wikipedia.org/wiki/2016_British_Grand_Prix
- source: https://www.formula1.com/en/latest/article/silverstone-race-report.OZrhYBY8toEfh8RAQvTSl
- source: http://www.f1strategyreport.com/2016/07/15/f1-strategy-report-british-grand-prix-2016/

### R20 Brazilian Grand Prix
- race: Heavy Rain / Storm / Heavy Rain / Rain
- qualifying: Clear / Clear / Clear / Clear
- evidence: VERIFIED. Wikipedia infobox: 'Rain and 16 C'. Track very wet at the start â€” race ran behind the safety car until lap 8; red-flagged after Raikkonen's aquaplaning crash on the pit straight (lap 20-21) with a 35-minute stoppage, then red-flagged again seven laps after the restart as conditions were 'still too dangerous' (25-minute delay); five safety-car periods in total, longest Brazilian GP in history at just over 3 hours, wet to the finish where Verstappen climbed 16th to 3rd in the final 16 laps through heavy spray. Peak intensity (both red flags) sits in the second quarter, matching 'Storm' in slot 2, easing only slightly to 'Rain' at the end â€” sequence kept as claimed. Saturday qualifying was dry.
- source: https://en.wikipedia.org/wiki/2016_Brazilian_Grand_Prix
- source: https://www.racefans.net/2016/11/18/verstappen-dominates-brazil-driver-of-the-weekend/
- source: https://bleacherreport.com/articles/2675840-max-verstappen-channels-ayrton-senna-with-magical-wet-brazilian-gp-performance

## 2020

### R3 Hungarian Grand Prix
- race: Light Rain / Overcast / Medium Cloud / Light Cloud
- evidence: VERIFIED: Wikipedia race report confirms the race started in damp conditions after pre-race rain, with 18 of 20 cars launching on intermediates (Magnussen on full wets; both Haas cars switched to slicks on the formation lap). The track dried rapidly, everyone pitted for slicks within the opening laps, and the forecast further rain never arrived â€” no rain fell during the race itself. Infobox: 'Wet at start, partly cloudy'. Slot 4 corrected from Clear to Light Cloud to match the documented partly-cloudy (not fully clear) finish; slot 1 Light Rain is the authoring device that produces the damp start. Qualifying was effectively dry (only very light rain, all laps on slicks), so no qualifyingSlots â€” correct.
- source: https://en.wikipedia.org/wiki/2020_Hungarian_Grand_Prix
- source: https://press.pirelli.com/2020-hungarian-grand-prix---race/

### R12 Portuguese Grand Prix
- race: Light Rain / Light Cloud / Clear / Clear
- evidence: VERIFIED: Motor Sport Magazine's lap-by-lap independently documents raindrops appearing on lap 1 under grey skies, the drizzle 'moving away from the Portimao circuit' and 'the sun emerged from the grey skies' by lap 8, with dry conditions for the remainder of the 66 laps. The light drizzle on the cold, fresh Portimao surface drove the chaotic opening laps (Hamilton dropping to third, Sainz leading laps 1-5 on softs) though it was never heavy enough for wet tyres â€” the whole field stayed on slicks. Wikipedia infobox says 'Cloudy' but the documented lap-8 sun emergence supports the Clear tail. Sequence confirmed as claimed.
- source: https://www.motorsportmagazine.com/articles/single-seaters/f1/2020-portuguese-grand-prix-as-it-happened-lap-by-lap/
- source: https://en.wikipedia.org/wiki/2020_Portuguese_Grand_Prix
- source: https://www.formula1.com/en/latest/article/hamilton-takes-record-breaking-92nd-win-with-dominant-drive-in-portuguese-gp.5gTAYvkxPrfqhT8ZYZFRt2

### R14 Turkish Grand Prix
- race: Rain / Light Rain / Overcast / Overcast
- qualifying: Heavy Rain / Rain / Rain / Light Rain
- evidence: VERIFIED: Wikipedia, F1.com and Pirelli all confirm rain roughly thirty minutes before the start soaked the slippery resurfaced Istanbul Park (13 degrees C track and air); the entire field started on full wets except the two Williams on intermediates; the wet-to-intermediate crossover came within 10 laps (field complete by lap 13); the track dried only incrementally and NEVER enough for slicks â€” Hamilton won on a ~50-lap set of inters worn to the canvas. AUTHORING NOTE: no rain actually fell during the race itself (a late-race rain threat never materialised); slot 2 Light Rain is retained deliberately as the wet-holding device so the sim reproduces the documented never-slick-ready, inters-only race â€” three consecutive dry slots would dry the track to slicks, a worse historical error. Qualifying verified wet throughout: suspended 45 minutes, red flags, full wets faster than inters for most of the session, Stroll's shock pole (1:47.765) on intermediates as conditions marginally improved â€” the Heavy Rain -> easing progression matches.
- source: https://en.wikipedia.org/wiki/2020_Turkish_Grand_Prix
- source: https://www.formula1.com/en/latest/article/hamilton-seals-historic-7th-title-with-peerless-wet-weather-victory-in.4wK1atemiXDvWXQxOknL4J
- source: https://press.pirelli.com/2020-turkish-grand-prix--race/
- source: https://www.formula1.com/en/latest/article/stroll-takes-scintillating-turkish-gp-pole-in-chaotic-rain-hit-qualifying.5WR4u0y0D9oMU3vYxmtFyj
