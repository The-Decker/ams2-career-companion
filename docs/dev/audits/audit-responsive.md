AMS2 CAREER COMPANION â€” RESPONSIVENESS AUDIT (all XAML under src/Companion.App). Goal: whole app adapts to window size (Mike: 2560x1440 but resizes; MinWidth 920). Dominant anti-pattern found: almost every screen is a `ScrollViewer > StackPanel MaxWidth=<fixed> HorizontalAlignment="Left"` â€” a hard-capped left-hugging column that leaves ~60% of a 2560 window empty and never reflows.

EXISTING RESPONSIVE PRECEDENTS TO COPY
- 4-col responsive card grid: `src/Companion.App/Views/WizardView.xaml` lines 144â€“205 â€” ListBox with `UniformGrid Columns="4"` ItemsPanel; card hero keeps 16:9 via `Height="{Binding ActualWidth, RelativeSource={RelativeSource Self}, Converter={StaticResource AspectHeight}, ConverterParameter=0.5625}"` + `MinHeight="120"` (converter: `AspectRatioHeightConverter`, `src/Companion.App/Converters/Converters.cs` line 463; registered as `AspectHeight` in `Themes/Theme.xaml` line 70).
- Whole-window UI scale: `AppUiScale` LayoutTransform on every window root (`MainWindow.xaml` 16â€“18, `TabWindow.xaml` 22â€“24, `NewsWindow.xaml`, `PhotoWindow.xaml`, `BriefingCompactWindow.xaml`). NOTE: at 130% scale the post-transform layout width shrinks ~23% â€” star grids absorb this automatically, fixed MaxWidths do not.
- Scaling images: Calendar venue photo `Image Stretch="Uniform" StretchDirection="DownOnly"` (`CalendarView.xaml` 126) + resizable `PhotoWindow` (Image `Stretch="Uniform"` fills window).
- Already-correct star grids: ResultEntryView center `*,*,*` 3-column grid (lines 265â€“270), ConfirmView `*,*` panels (33â€“36), SkinsView editor rows `*,*` (170â€“173).
- Suggested NEW shared tool (one small addition, reused everywhere): a `WidthFractionConverter` sibling of `AspectRatioHeightConverter` so panels can do `MaxWidth = ancestor ActualWidth x fraction` â€” turns every hardcoded page cap into a proportional one.

PER-VIEW WORKLIST (ranked by user-facing impact)

1. BriefingView.xaml (race day â€” seen every round) â€” worst offender.
- Line 7: `<StackPanel MaxWidth="860" HorizontalAlignment="Left">` caps the ENTIRE briefing at 860px. Fix: restructure to a two-column star Grid inside the ScrollViewer â€” left column `3*` (checklist Panel line 83 + staging buttons/banners 241â€“388), right column `2*` (circuit panel line 36, difficulty 196, fuel 204, gamble 214, notes 232); collapse to one column is unnecessary above MinWidth 920, but give the left column `MinWidth="520"`. If a lighter touch is wanted: replace MaxWidth=860 with a proportional MaxWidth (WidthFractionConverter, ~0.75) and `HorizontalAlignment="Stretch"`.
- Line 23: track-art Border `MaxWidth="560"` â†’ proportional or drop (Image already `Stretch="Uniform" StretchDirection="DownOnly"`).
- Line 36: circuit panel `MaxWidth="720"` â†’ remove once inside the right star column.
- Line 40: circuit `Viewbox Width="168" Height="168"` fixed â†’ let it scale: `Width` bound to column ActualWidth via AspectHeight-style converter (square: ConverterParameter=1) with Min 140 / Max 280, or wrap map+caption in a shared `*`/`2*` grid.
- Line 140: checklist label column `Width="180"` â†’ `Auto` + `SharedSizeGroup="ChecklistLabel"` (set `Grid.IsSharedSizeScope` on the ItemsControl at line 122) so long labels never clip at 130% scale.

2. ResultEntryView.xaml (the loop's work screen) â€” structurally good, targeted fixes.
- Lines 265â€“270: center `*,*,*` grid is the model â€” keep. Add `MinWidth="220"`-ish per column so drag zones stay usable at 920px.
- Footer lines 221â€“262: DockPanel with fixed `Slider Width="130"` (240) + Wet checkbox + hints; at narrow/scaled widths the docked-right chain (Confirm, slider block, checkbox) can crowd out the progress text. Fix: make the footer a Grid (`Auto` progress | `*` hint | `Auto` wet | `Auto` slider | `Auto` confirm) and let the hint TextBlock (257) collapse below a width threshold (DataTrigger on ActualWidth or simply `TextTrimming`); widen slider to `MinWidth=130 MaxWidth=220 Width=Auto`.
- Line 94: circuit-strip text `MaxWidth="480"` â†’ fine, but strip Border (82) is `HorizontalAlignment="Left"` with no growth â€” acceptable; optionally proportional.
- Candidates ListBox `MaxHeight="230"` (204): make proportional to viewport height (converter or leave â€” low).

3. StandingsView.xaml (most-viewed lens + tear-off TabWindow at 560px AND hub at 2560).
- Lines 148 and 160: `DockPanel MaxWidth="760" HorizontalAlignment="Left"` cap the Drivers/Constructors tables. Fix: raise to a proportional MaxWidth (~0.65 of host) or drop to `MaxWidth="1100"`; keep left alignment.
- Header/row fixed pixel columns: header buttons Width 76/86/86/104/44 (lines 33â€“55) duplicated in `StandingsRowTemplate` widths (65â€“84). Fix: convert both templates to a shared Grid with `Auto` numeric columns + `SharedSizeGroup` (Pos/Name/Dropped/Gross/PerRound/Counted) and Name as `*` â€” removes the double-maintenance and clips at 130% scale.
- Round matrix (169â€“241): fixed `Width="190"` driver col (178, 198) + `Width="48"` cells (187, 211) inside an H+V ScrollViewer â€” acceptable, but bump cell width to `MinWidth=48` Auto so big point values (25+FL) never clip.
- Inspector overlay line 253: `MaxWidth="520" MaxHeight="440"` â€” cramped on 1440p. Fix: `MaxWidth` 0.5 / `MaxHeight` 0.7 of window (WidthFractionConverter) with floors 520/440. Same fix in HistoryView (identical copy, line 342).

4. HistoryView.xaml (scrapbook lens; also torn off).
- Line 246: `StackPanel MaxWidth="820" HorizontalAlignment="Left"` â€” cap the whole scrapbook. Fix: proportional MaxWidth (~0.7) or 1100; season cards + timeline already stretch.
- Line 44: per-round circuit `Viewbox 96x96` and line 256: NEXT-RACE `Viewbox 180x180` fixed â†’ scale with available width (same treatment as BriefingView item; square AspectHeight binding, Min 96/Max 260).
- Line 173: season story image `MaxWidth="620"` â†’ proportional; Image already Uniform/DownOnly.
- Line 342: inspector overlay â€” same fix as StandingsView.
- Records WrapPanel (280â€“285) is already responsive â€” leave.

5. CalendarView.xaml (newest â€” half-done responsiveness).
- Line 6: `StackPanel MaxWidth="900" HorizontalAlignment="Left"` â€” same cap; fix proportionally (~0.7). Expanded card content then breathes at 2560.
- Line 99: `Viewbox Width="168" Height="168"` fixed circuit map â†’ scale (same square-converter treatment; this is THE hero visual of the expandable cards).
- Line 122: venue photo `MaxWidth="720"` â†’ proportional (e.g. 0.6 of card width); keep Uniform/DownOnly + PhotoWindow click-through.

6. SkinsView.xaml.
- Line 40: `StackPanel MaxWidth="780" Margin="40,32" HorizontalAlignment="Left"` â€” cap. Fix proportional (~0.65); car rows and editor grid (`*,*` at 170â€“173) already stretch. Consider two-up card WrapPanel/UniformGrid for "This round's cars" (line 93 ItemsControl) at wide widths â€” copy the WizardView UniformGrid precedent with `Columns=2`.

7. ConfirmView.xaml (post-race).
- Line 5: `DockPanel MaxWidth="960" HorizontalAlignment="Left"` â€” the two star panels (33â€“36) would use the width perfectly if uncapped; raise proportionally (~0.8) or drop entirely (this screen is two virtualized lists; full width is safe).

8. StartView.xaml (first screen; gallery is the hero).
- Line 47: `StackPanel MaxWidth="680" HorizontalAlignment="Center"` strangles the career gallery â€” 288px cards in a WrapPanel (124â€“127) inside a 680 cap = only 2 cards/row on a 2560 monitor. Fix: keep the intro text block capped, but move the "Recent careers" Border out of the 680 column (restructure to Grid rows) with proportional MaxWidth (~0.8) so WrapPanel flows 4â€“6 cards; card `Width="288"` (139) + hero `Height="162"` (157) can stay fixed (WrapPanel supplies the responsiveness) â€” or adopt the WizardView UniformGrid+AspectHeight card for parity.
- Line 122: ListBox `MaxHeight="640"` â†’ proportional to viewport height or remove (outer ScrollViewer already scrolls).

9. WizardView.xaml (already the precedent, small gaps).
- Line 152: `UniformGrid Columns="4"` is fixed-4: at 920px cards are ~210px (cramped), at 3440 they'd be huge. Fix: bind `Columns` via a small `WidthToColumnsConverter` (ActualWidth / ~360, clamp 2â€“6) â€” the one genuinely adaptive-columns item in the app.
- Line 465 (Grid step) and line 500 (Confirm step): `MaxWidth="640" HorizontalAlignment="Left"` â€” forms may stay capped, but center them (`HorizontalAlignment="Center"`) or raise to proportional so they don't hug the far left of a 2560 window. Same for seat-pick ratings block `Width="190"` (424) â†’ `MinWidth=190` + star-ish share.

10. CharacterView.xaml (wizard step 5 + reused).
- Line 53: archetype `ColumnDefinition Width="270"` fixed vs `*` stats column. Fix: `Width="1*" MinWidth="240" MaxWidth="400"` / stats `2*` so both flex.

11. HubView.xaml / HomeView.xaml (shell â€” mostly fine).
- HubView line 78: rail `Width="140"` fixed: OK by design, but set `Width="Auto" MinWidth="140"` so 130% UI scale + long tab names never clip.
- HomeView line 23: content `Margin="20,16"` fixed â€” fine.

12. SeasonReviewView.xaml.
- Line 6: `StackPanel MaxWidth="1000" HorizontalAlignment="Left"` â€” proportional (~0.75). Offer letters/dev rows already stretch. FinalStandings ContentControl (247) inherits StandingsView's 760 cap â€” fixing #3 fixes this.

13. SettingsView.xaml / DossierView.xaml / NewsView.xaml (low).
- SettingsView line 38 `MaxWidth="760"`: conventional for settings â€” optionally center. Label columns `Width="170"` (49, 112, 157) â†’ `Auto` + SharedSizeGroup.
- DossierView line 8: `MaxWidth="720" Margin="40,32" HorizontalAlignment="Left"` â†’ proportional (~0.6) or center; stat ProgressBars already stretch.
- NewsView: fully fluid already â€” no changes.

14. Windows.
- MainWindow.xaml line 6: fixed startup `Width="1120" Height="740"` â€” consider persisting last window size/state (maximized) in settings; on 2560x1440 the app always opens small. Not XAML-only but the single biggest "feels unresponsive" first impression.
- BriefingCompactWindow.xaml line 64: label `Width="128"` â†’ `Auto`+SharedSizeGroup or MinWidth (window is user-resizable to 300 wide; values wrap but labels ellipsize).
- TabWindow/NewsWindow/PhotoWindow/RenameCareerDialog: fine (resizable + Uniform image / SizeToContent).

CROSS-CUTTING NOTES FOR THE MEGAPROMPT
- One shared fix powers items 1,3â€“8,12,13: add `WidthFractionConverter` (multiply bound ActualWidth by ConverterParameter) next to `AspectRatioHeightConverter` in `src/Companion.App/Converters/Converters.cs`, register in `Themes/Theme.xaml` beside `AspectHeight` (line 70); then every `MaxWidth="NNN"` page column becomes `MaxWidth="{Binding ActualWidth, RelativeSource={RelativeSource AncestorType=ScrollViewer}, Converter={StaticResource WidthFraction}, ConverterParameter=0.7}"` â€” keeps readable line lengths at 2560 while filling small windows.
- Fixed square Viewboxes for circuit maps appear 5x (BriefingView 40, ResultEntryView 87 [60x60, fine], HistoryView 44 + 256, CalendarView 99) â€” one shared pattern (square AspectHeight binding with Min/Max) fixes all.
- The inspector overlay (Standings 253 / History 342) is a duplicated fixed-size modal â€” fix once, copy twice (or extract).
- Test at the extremes: 920x620 (MinWidth/MinHeight) at 130% font scale, and 2560x1440 at 100%; render tests live in the existing render harness (35 tests) â€” add a wide/narrow snapshot per reworked view if the harness supports sizing.