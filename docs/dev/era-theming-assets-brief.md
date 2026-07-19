# Era-Medium Theming — Visual Assets + Era-Aware Click SFX (work brief)

> Self-contained execution brief. Complete the "period medium" skin so a career's documents, news,
> gallery cards **and interaction sounds** all carry its era medium — **Telegram (≤1979) / Fax
> (1980–1993) / Email (1994+)** — while dense data surfaces stay legible and the audio stays sparse
> and original. Ground everything in real files; do not invent. Repo root `Z:/Claude Code/ams2-career-companion`,
> solution `Companion.slnx` (.NET 10, WPF). Doctrine source: `docs/dev/career-hub-design.md` §6/§10/§11,
> `docs/dev/career-hub-build.md` Increment 1, `src/Companion.App/Assets/Audio/SOUND-DESIGN.md`.

## 0. What already exists (verified state, do not rebuild)

**Visual — the era medium is DATA + partial render:**
- `src/Companion.Core/Career/EraTheme.cs` — `enum EraMedium {Telegram,Fax,Email}`; `record EraTheme
  {Medium,Label,AccentHex,DocumentFontStack,DatelineFlourish}`; `EraThemes.ForYear(year)` (≤1979
  Telegram / 1980–1993 Fax / ≥1994 Email) + `FromText(text)` (regex `\b(19|20)\d{2}\b`). **Pure.**
  Accents: Telegram `#C8922E` ochre, Fax `#5E7A8C` slate, Email `#3B7DD8` blue. Flourish `STOP`
  (Telegram) else `""`.
- `src/Companion.Core/Career/OfferDocument.cs` — composes a `PlayerOffer` into a period document
  (telegram wire / fax memo / email) — pure/deterministic. Rendered **only** on the offers list in
  `SeasonReviewView.xaml` via `OfferLetterViewModel`.
- `EraTheme` is surfaced by `HubViewModel.Era` + `EraThemingEnabled`, and drives gallery-card
  accent/label/photo through `Converters.cs` (`EraAccentBrush`/`EraLabel`/`EraImage`) and
  `EraArtResolver`.
- Assets: `data/ams2/era-art/` has **22 per-year photos** (1967…2020 + `smgp.jpg`), git-tracked, in
  sync with `dist/`.

**Audio — 9 semantic cues, deliberately era-agnostic:**
- `src/Companion.App/Audio/` — `SoundscapeCatalog.cs` (cue→`SoundEffectDefinition{Variants,Cooldown,
  DedupeGroup,DedupeWindow,MusicDuck}`, with a **round-robin `NextEffect` over a Variants list**),
  `AppAudioController.cs` (owns playback; **no ShellViewModel dep — cannot observe navigation/career
  state**), `SoundAssist.cs` (attached `SoundAssist.Cue` on buttons; no global click handler),
  `WpfAudioEngine.cs`.
- Cues: `Navigate, Confirm, SeatConfirm, Back, BucketPickup, BucketPlace, Warning, Destructive,
  SkillUnlock` → 9 WAVs in `Assets/Audio/Sfx/`, **generated** by `tools/generate_sfx.ps1` +
  `Audio/Generation/generate-seat-confirm.ps1` (original synthesis, 48kHz/16-bit/mono PCM).

## 1. The gaps this brief closes

**Visual**
1. No `EraTheme → ResourceDictionary` swap. The "medium chrome" (thermal-paper grain, telegram wire
   frame, email inbox rows) does not exist; skin today = accent color + font-family + text label only.
2. The **newsroom (`NewsView.xaml`) is not era-skinned at all** — design says news chrome should age.
3. Missing generic fallback photos `telegram.jpg` / `fax.jpg` / `email.jpg` (resolver looks for them;
   only per-year files exist, so off-list years fall to the flat placeholder).
4. No period paper/thermal **textures**, no per-medium **letterhead/iconography**, no bundled
   **era-doc fonts** (font stacks are OS fonts that may not exist on the target box).
5. `EraMedium` is never a first-class bindable (only nested `Hub.Era.Medium`), so a WPF swap has
   nothing clean to key on. News/History VMs carry no era skin. `OfferDocument` is wired to one screen.
6. `data/rules/era-themes.json` (documented per-pack override, decade→`{medium,accent,fontStack,
   paperTexture,datelineFormat}`) does not exist, and its schema doesn't match the built record.
7. `ART-INVENTORY.md` is stale (says 20, disk has 22; omits 1983, 2010).

**Audio**
8. Every click sounds identical in every era. There is no era dimension in the catalog/controller,
   and the sound bible currently forbids one (see §3 Decision).

## 2. Invariants — any change must honor these

- **Core stays pure** — era selection is a pure function of the season year; presentation introduces
  **no un-seeded state**; every era-flavored *string* is chosen via a **named PCG32 stream**
  (`career-hub-design.md` §6). No I/O/WPF/DB in `Companion.Core`.
- **Presentation only — no save-format change.** New `EraTheme`/state fields must be defaulted and
  `[JsonIgnore(WhenWritingDefault)]` (never persisted); the **f1db oracle is untouched**; no migration.
- **"Immersive docs, legible tools"** (`career-hub-design.md` decision 7): the swap skins *documents*
  (news articles, offer letters, scrapbook spreads) + chrome + accent; **standings tables and result
  entry keep the legible base face with era accent only.**
- **Period-authentic minimalism** (decision 21): styles + tokens + lean vector/tileable art, **not
  bitmap-heavy themes** — keep the single-exe lean.
- **Lane boundary (strict):** Coding lane (Claude) = `Companion.Core` / `Companion.ViewModels` /
  `tests` / `data/rules`. GUI lane (Codex) = `Companion.App/**` (Themes, Converters, Views, `Audio/`)
  + render stand-ins + the asset/WAV authoring. Coding lane ships the **Slice-0 stub bind contract
  first** so GUI can bind names before logic lands.

## 3. DECISION REQUIRED — era-aware SFX vs. the sound bible

`SOUND-DESIGN.md` locks **Pillar 6: "one original identity across every era"** and **Pillar 3: sound
"does not follow … a career outcome"**, and `AppAudioController` is built to never observe career
state. Era-specific click sounds contradict all three. **Recommended reconciliation (needs Mike's
sign-off before Workstream B ships):**

- **Timbre only, never triggering.** Meanings, trigger rules, mix, ducking, anti-chatter, and the
  silence zones stay **exactly** as today. Era changes only *how a cue is voiced*, never *when/whether*
  it fires. "Silence is part of the design" (Pillar 4) is preserved.
- **Push, don't observe.** The shell hands audio a one-way era-skin token —
  `AppAudioController.SetEraSkin(EraMedium?)` — when a career opens/closes, the same push model as
  settings. `null` = era-neutral base set (menus, gallery, no active career). The controller still
  never watches navigation or outcomes; it is *told* a skin, like a theme.
- **Reuse the Variants seam.** `SoundEffectDefinition.Variants` is already a list with round-robin
  `NextEffect`; extend it to hold **per-era variant sets** and select by the pushed medium (fallback
  to base when a medium has no variant).
- **Original synthesis only** (licensing Pillar, §7): add era-tinted masters via the tracked
  generator — Telegram: relay/telegraph-key tick + small bell; Fax: thermal-print chirp + handshake
  warble; Email: soft FM chime — **never** SEGA/SMGP recordings, F1 broadcast, or team radio.
- **Scope which cues get era color:** the *immersive* cues — **Navigate, Confirm, Back, SeatConfirm**.
  Keep **Warning / Destructive / SkillUnlock era-neutral** (they are cross-era consequence signals),
  and **BucketPickup / BucketPlace neutral** (result-entry tooling).
- **Amend the bible:** rewrite Pillars 3 & 6 to "one identity, *era color per medium*; audio receives
  an era skin, never observes state," and record the decision. This edit is the gate for B.

*(Alternative if Mike rejects the amendment: drop Workstream B, keep one identity, ship only the
visual era chrome. Do not implement era SFX silently against the bible.)*

## 4. Workstream A — visual assets + document/news theming

- **A0 — Coding lane (Slice-0 stub first).** Define one shared era-skin bind contract, e.g.
  `IEraSkin { EraMedium Medium; string Label; string AccentHex; string DocumentFontStack; string
  DatelineFlourish; string PaperTextureKey; }`, and surface it as a **first-class property** on the
  Hub / Start(gallery) / News / History / Offers VMs (News & History currently expose none). Ship it
  returning today's values/defaults immediately so GUI can bind. Flatten `EraMedium` to a top-level
  bindable for DataTrigger keying.
- **A1 — Core.** Add `PaperTextureKey` to `EraTheme` (defaulted, non-persisted). Build the
  `data/rules/era-themes.json` loader: reconcile the documented schema
  `decade→{medium,accent,fontStack,paperTexture,datelineFormat}` with the built record, keep the
  hard-coded table as the fallback, keep resolution pure. Add loader + contract tests. Keep
  `EraThemesTests` / `OfferDocumentTests` green.
- **A2 — GUI.** Author `Themes/Era.Telegram.xaml` / `Era.Fax.xaml` / `Era.Email.xaml` (typography,
  paper/thermal texture brushes, chrome framing, dateline format) and a **document-surface-scoped**
  swap keyed on `EraMedium` (offer letters, news article/expanded story, scrapbook) — **not** the
  whole shell. Skin the **newsroom** (`NewsView.xaml`) to the era. Standings/result-entry stay base
  face + accent only. Gate all of it behind `EraThemingEnabled` (today the toggle only hides labels).
- **A3 — Assets (GUI lane content).**
  - Add the 3 fallback photos `telegram.jpg` / `fax.jpg` / `email.jpg` (~1280×720, 16:9, subject
    centered — card crops 288×88 `UniformToFill`) to `data/ams2/era-art/`, sync to `dist/`, and fix
    `ART-INVENTORY.md` (22 not 20; add 1983, 2010; track the 3 medium fallbacks as expected).
  - Period **paper/thermal textures** (ochre wire, fax thermal grain) as lean tileable/vector PNG.
  - **Letterhead/iconography** per medium (telegram wire header, fax sender/date band, email inbox
    row) as vector.
  - Bundle era-doc **fonts** (or explicitly confirm OS fallback is acceptable) so period typography
    doesn't silently degrade — `src/Companion.App/Fonts/` has none that match the stacks.
- **A4 — Offers everywhere.** Make `OfferDocument` a reusable era `DataTemplate` used wherever offers
  appear (live offers panel, contract news), not just `SeasonReviewView`.

## 5. Workstream B — era-aware interaction SFX (gated on §3 sign-off)

- **Coding lane:** expose the active career's current `EraMedium` for the shell to push (a small
  read-only accessor / event the App can call `SetEraSkin` from). No audio code in Core/VM.
- **GUI lane:** implement `SetEraSkin(EraMedium?)` on `AppAudioController`; extend
  `SoundEffectDefinition.Variants` to per-era sets + era-aware selection with base fallback; author
  era-tinted WAV masters through the tracked generator for **Navigate/Confirm/Back/SeatConfirm**
  (48kHz/16-bit/mono PCM, measured against the existing masters' 90–900 ms / −13…−9.5 dBFS range);
  keep Warning/Destructive/SkillUnlock/Bucket* neutral. Respect master/effects/focus-mute; do not
  touch the locked manual-music contract. Update `SOUND-DESIGN.md`, `README.md`, `LICENSES.md`.

## 6. Acceptance / tests

- Coding lane: `EraThemesTests` + `OfferDocumentTests` stay green; add `era-themes.json` loader tests
  and `IEraSkin` contract tests; determinism/oracle untouched; no save migration.
- Render harness (GUI lane): per-medium document snapshot (telegram/fax/email) for offer + news;
  newsroom era-skinned; **standings + result-entry pixel-unchanged**; `EraThemingEnabled=false`
  fully neutralizes era chrome.
- Audio (GUI lane): catalog/generator tests for the new era variants; controller test that
  `SetEraSkin(null)` → base set and that anti-chatter/dedupe/ducking are unchanged; asset/playback
  failure still degrades to silence.
- `dotnet build` / `dotnet test Companion.slnx` green.

## 7. Out of scope

- The rich generative news grammar ("thousands of dispatches") — deferred per `career-hub-build.md`.
- Any music scene-tagging / autoplay — the manual top-bar player contract is locked.
- Persisting any era state — this is presentation only.
