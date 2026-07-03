# UX round (locked decision #8 — full input parity + approachable depth)

The bar: a first-time user completes a full race weekend with ONLY the mouse and without
reading docs; a power user still beats 90 seconds on the keyboard. Nothing regresses the
grammar — the two input paths are peers over the same viewmodel state.

## 1. Drag-and-drop result entry (the centerpiece)

Three columns: **Remaining** (unplaced, with car number + name + team), **Finishing order**
(P1..Pn slots), **Out** (two drop zones: DNF, DSQ).

- Drag Remaining → Finishing order: insert at the drop index (insertion indicator line);
  following drivers shift down. Drag onto a filled slot inserts BEFORE it (never replaces).
- Drag within Finishing order: reorder == the grammar's penalty/reposition (same VM mutation,
  same undo stack entry).
- Drag → DNF zone: marks DNF; a small inline reason picker appears on the dropped row
  (Mechanical / Accident / Other — default Other, same one-letter codes as the grammar).
- Drag → DSQ zone: marks DSQ. Drag OUT of any zone back to Remaining: unmarks (undoable).
- Multi-select (Ctrl/Shift-click) + drag to DNF: bulk retirement, mirroring bulk-Enter.
- Every mutation goes through the EXISTING ResultEntryViewModel operations (add
  mouse-oriented methods only where the grammar has no equivalent primitive — e.g.
  InsertAt(driverId, index)); the undo stack, progress counter, timer, and IsComplete rule
  are shared. Drag interactions are therefore testable at the VM level (call the methods),
  with the WPF adorner/behavior layer kept thin.
- Buttons beside the input box: Undo, DNF phase toggle, Clear — plus a per-row context menu
  (right-click a driver anywhere): Place next / Insert at position… / DNF (reason submenu) /
  DSQ / Remove.

## 2. Mouse-parity inventory (every screen)

- Start: everything already clickable; add right-click MRU → Remove from list / Open folder.
- Wizard: fully clickable already; seat pick gets double-click-to-select-and-advance;
  ratings shown as bars with tooltips (raw numbers on hover).
- **Briefing becomes a manual setup CHECKLIST (user correction 2026-07-02):** AMS2's custom
  race settings are arrow-steppers — nothing can be pasted. Redesign the settings panel:
  one CHECK-OFF row per setting (click the row or its checkbox to tick it), values big and
  glanceable, ordered to match the in-game custom-race screen flow (track → class →
  opponents → laps → date → start time → weather slots → time progression → pit rules), a
  "N of M set" progress indicator, and tick-state that resets per round. Remove the per-row
  copy buttons (keep values text-selectable; one optional "copy summary" for sharing outside
  the game). Add a **compact always-on-top mode** (small floating checklist window toggle)
  so the user can tick settings off while clicking through AMS2 in windowed/borderless mode.
  Stage/Force/Restore as buttons with confirmation popovers (force explains the NAMeS marker
  rule in plain words).
- Confirm: Apply/Back buttons exist; movement rows get tooltips ("P5 → P2: +3").
- Standings: sortable columns (click header), column chooser (right-click header: toggle
  gross/counted/dropped/points-per-round), tab order persisted.
- Season review: offer cards clickable (Accept with confirmation), restore button.
- Global: Esc = back wherever non-destructive; alt-text/tooltips on every icon button;
  hover states on all interactive elements; focus visuals for keyboard users preserved.

## 3. Settings screen (depth without clutter)

`%APPDATA%\AMS2CareerCompanion\settings.json` (camelCase, versioned):
- Appearance: accent color (presets + custom hex), font scale (90–130%), theme (dark now;
  the file carries a `theme` key so light can arrive later without migration).
- Racing: default difficulty slider, minimal-narrative toggle (suppresses headlines except
  championship-critical), auto-open briefing on career load.
- Staging (NAMeS-first): prefer-installed-baseline default for new careers, diff-aware
  staging on/off (on = default), restore-on-season-end prompt on/off.
- Data: pack folders list (add/remove custom locations), open-data-folder buttons.
- Settings apply live (no restart); a Reset-to-defaults button; every control tooltipped.

## 4. Discoverability & polish

- First-run coach marks: three dismissable callouts (briefing copy buttons, result-entry
  "type OR drag", standings rules chip). Never shown again once dismissed (settings flag).
- Empty states: no careers yet → arrow to New career; no packs found → explains pack folder
  locations with an Open button.
- Consistent iconography (Segoe MDL2 glyphs), 150ms hover/press transitions, list
  virtualization for big grids, window size/position remembered.

## Verification

- VM tests: InsertAt/MoveTo/mark-unmark parity with grammar mutations sharing one undo
  stack (a mixed keyboard+mouse sequence fully unwinds); multi-select bulk DNF; settings
  round-trip + live-apply notifications; column-chooser state persistence.
- UIA walk on the real machine: complete a full synthetic race weekend MOUSE-ONLY
  (wizard → briefing → drag 12 drivers to a finishing order incl. one reorder, two DNFs
  with reasons, one DSQ → confirm → standings), then the same KEYBOARD-ONLY, asserting
  identical ResultDrafts. Screenshots at each step for the report.
- The keystroke-budget test (<120 for 26 cars) still passes untouched.
