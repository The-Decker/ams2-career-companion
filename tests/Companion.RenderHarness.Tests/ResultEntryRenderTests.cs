using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.ResultEntry;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Off-screen render tests for the two v0.3.1 DNF result-entry regressions, driven through the
/// REAL ResultEntryView over a REAL ResultEntryViewModel — the VM logic is already covered and
/// correct, so these exercise the view layer that only a live render exposes:
///
/// BUG A — clicking into the DNF custom-cause box or the DSQ reason box must leave the caret
///          there; the old OnPreviewMouseUp yanked focus back to InputBox on every left release.
/// BUG B — after Mech/Acc, a single Ctrl+Z through the view's key path must revert the DNF
///          reason (and the row must reflect it).
///
/// Every test hops onto an STA thread with a live Dispatcher via <see cref="WpfRenderHarness"/>;
/// on a non-Windows / non-STA host they self-skip instead of failing.
/// </summary>
public sealed class ResultEntryRenderTests
{
    private const string PlayerId = "d.amon";

    private static readonly (string Id, string Name, string Number)[] Roster =
    [
        ("d.brabham", "Jack Brabham", "1"),
        ("d.hulme", "Denny Hulme", "2"),
        ("d.clark", "Jim Clark", "3"),
        ("d.ghill", "Graham Hill", "4"),
        (PlayerId, "Chris Amon", "6"),
        ("d.stewart", "Jackie Stewart", "8"),
    ];

    private static readonly PackDriverRatings Ratings = new() { RaceSkill = 0.9, QualifyingSkill = 0.9 };

    private static GridSeat Seat(string id, string name, string number) => new()
    {
        DriverId = id,
        DriverName = name,
        TeamId = "t." + id,
        TeamName = "Team " + name,
        Number = number,
        Ams2LiveryName = name,
        Ratings = Ratings,
        Reliability = 1.0,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
        IsPlayer = id == PlayerId,
    };

    private static IReadOnlyList<GridSeat> Grid() =>
        Roster.Select(r => Seat(r.Id, r.Name, r.Number)).ToArray();

    // ---------- BUG A: the caret must stay in the DNF custom-cause box ----------

    [Fact]
    public void ClickingIntoDnfDetailBox_KeepsFocusThere_AndTypedTextReachesViewModel()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            // Mark a driver DNF as "Other" and open its inline reason picker so the custom-cause
            // box is realised and visible.
            const string id = "d.clark";
            Assert.True(vm.MarkDnf(id)); // default reason "o"
            vm.ReasonPickerDriverId = id;
            host.Layout();

            var detailBox = host.FindDnfDetailBox(id);
            Assert.NotNull(detailBox);

            // Simulate the click landing in the box: give it keyboard focus, then raise the same
            // left PreviewMouseUp the mouse would, with the box as the original source.
            detailBox!.Focus();
            Keyboard.Focus(detailBox);
            host.RaiseLeftMouseUp(detailBox);
            WpfRenderHarness.Pump(); // let the view's deferred FocusInput (if any) run

            // The fix: focus stays in the detail box, NOT snapped back to InputBox.
            Assert.Same(detailBox, Keyboard.FocusedElement);
            Assert.NotSame(host.InputBox, Keyboard.FocusedElement);

            // Typing there and committing (LostFocus) must reach the VM via SetDnfDetail.
            detailBox.Text = "Engine fire";
            host.RaiseLostFocus(detailBox);
            WpfRenderHarness.Pump();

            var entry = vm.Dnfs.Single(d => d.Seat.DriverId == id);
            Assert.Equal("o", entry.Reason);
            Assert.Equal("Engine fire", entry.Detail);
        });
    }

    // ---------- BUG A: the caret must stay in the DSQ reason box ----------

    [Fact]
    public void ClickingIntoDsqReasonBox_KeepsFocusThere_AndTypedTextReachesViewModel()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.hulme";
            Assert.True(vm.MarkDsq(id));
            host.Layout();

            var reasonBox = host.FindDsqReasonBox(id);
            Assert.NotNull(reasonBox);

            reasonBox!.Focus();
            Keyboard.Focus(reasonBox);
            host.RaiseLeftMouseUp(reasonBox);
            WpfRenderHarness.Pump();

            Assert.Same(reasonBox, Keyboard.FocusedElement);
            Assert.NotSame(host.InputBox, Keyboard.FocusedElement);

            reasonBox.Text = "Underweight";
            host.RaiseLostFocus(reasonBox);
            WpfRenderHarness.Pump();

            Assert.Equal("Underweight", vm.DsqReasonOf(id));
        });
    }

    // ---------- BUG A control: a click on a NON-text surface still pins the grammar input ----------

    [Fact]
    public void ClickingOnANonTextSurface_StillPinsGrammarInput()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);
            host.Layout();

            // Focus somewhere else first, then click a plain (non-text-entry) element.
            var order = host.Find<ListBox>("OrderList");
            Assert.NotNull(order);
            order!.Focus();
            host.RaiseLeftMouseUp(order);
            WpfRenderHarness.Pump();

            // Decision 8: typing the grammar must always work — the pin still fires here.
            Assert.Same(host.InputBox, Keyboard.FocusedElement);
        });
    }

    // ---------- BUG B: after Mech, undo reverts the reason AND the rendered row updates ----------

    /// <summary>The substance of BUG B, end-to-end through the live view: mark DNF "Other", open
    /// the picker, click the REAL "Mech" button (the row's reason label renders "mechanical"),
    /// then invoke the undo command — the exact call the view's Ctrl+Z branch makes
    /// (<c>vm.UndoCommand.Execute(null)</c>) — and confirm the VM reason is back to "o" AND the
    /// rendered reason label on the row has visibly changed back from "mechanical" to "retired".
    /// A single undo, and the row reflects it — which the v0.3.1 build did not deliver.</summary>
    [Fact]
    public void AfterMech_Undo_RevertsReason_AndTheRenderedRowUpdates()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.clark";
            Assert.True(vm.MarkDnf(id)); // reason "o"
            vm.ReasonPickerDriverId = id;
            host.Layout();

            var mech = host.FindReasonButton(id, "m");
            Assert.NotNull(mech);
            host.RaiseButtonClick(mech!); // → vm.SetDnfReason(id,"m"); picker closes; FocusInput runs
            host.Layout();

            Assert.Equal("m", vm.Dnfs.Single(d => d.Seat.DriverId == id).Reason);
            Assert.Equal("mechanical", host.RenderedDnfReasonText(id)); // the row shows it live

            // Exactly what OnPreviewKeyDown does for Ctrl+Z (Key.Z + Control → UndoCommand).
            vm.UndoCommand.Execute(null);
            host.Layout();

            Assert.Equal("o", vm.Dnfs.Single(d => d.Seat.DriverId == id).Reason);
            // The load-bearing render assertion: the row's reason label reverted too.
            Assert.Equal("retired", host.RenderedDnfReasonText(id));
        });
    }

    /// <summary>The KEY-ROUTING half of BUG B: a Ctrl+Z must reach the viewmodel's Undo through the
    /// UserControl's tunneling <c>PreviewKeyDown</c> BEFORE the focused InputBox (a TextBox with its
    /// own built-in Ctrl+Z undo) can swallow it. This asserts the tunnel order deterministically:
    /// the UserControl-level handler fires ahead of the InputBox in the preview route, so once it
    /// marks the event Handled, the TextBox never sees the chord. (The modifier state itself can't
    /// be injected in a headless off-screen host — see WpfRenderHarness remarks — so the chord's
    /// Control flag is covered by the direct handler test below and by code inspection.)</summary>
    [Fact]
    public void CtrlZ_ReachesUserControlBeforeInputBox_InThePreviewTunnel()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);
            host.Layout();
            host.InputBox.Focus();
            Keyboard.Focus(host.InputBox);

            var order = new List<string>();
            host.View.PreviewKeyDown += (_, _) => order.Add("usercontrol");
            host.InputBox.PreviewKeyDown += (_, _) => order.Add("inputbox");

            // A plain Z preview-key-down along the real focus route (InputBox is focused).
            var source = PresentationSource.FromVisual(host.View)!;
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Z)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            host.InputBox.RaiseEvent(args);
            WpfRenderHarness.Pump();

            // Tunnel = root→leaf: the UserControl's handler must precede the InputBox's, so the
            // Ctrl+Z branch (which sets e.Handled) always wins the race against TextBox undo.
            var ui = order.IndexOf("usercontrol");
            var ib = order.IndexOf("inputbox");
            Assert.True(ui >= 0, "UserControl PreviewKeyDown did not fire.");
            Assert.True(ib >= 0, "InputBox PreviewKeyDown did not fire.");
            Assert.True(ui < ib, $"Expected UserControl before InputBox in the tunnel; got {string.Join(",", order)}.");
        });
    }

    /// <summary>The handler's Ctrl+Z is genuinely MODIFIER-GATED: invoking the real
    /// <c>OnPreviewKeyDown</c> with a bare Z (no Control) must neither undo nor mark the event
    /// handled — so a lone "z" typed into the grammar box is never mistaken for undo, and, read the
    /// other way, undo only ever fires under Control. Combined with the tunnel-order test above and
    /// the live undo/rebind test, this pins the chord→Undo mapping. (Driving a genuinely-Control
    /// Z through the handler is impossible in this headless off-screen host: WPF's
    /// Win32KeyboardDevice.Modifiers ignores SetKeyboardState/GetKeyState and only updates from a
    /// real OS keyboard report delivered to a foreground window — see the verification notes.)</summary>
    [Fact]
    public void OnPreviewKeyDown_PlainZ_WithoutControl_DoesNotUndo_NorHandle()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.clark";
            Assert.True(vm.MarkDnf(id));
            vm.ReasonPickerDriverId = id;
            host.Layout();
            host.RaiseButtonClick(host.FindReasonButton(id, "m")!); // reason → "m"
            host.Layout();
            Assert.Equal("m", vm.Dnfs.Single(d => d.Seat.DriverId == id).Reason);
            Assert.False(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); // no Control held here

            bool handled = host.InvokePreviewKeyDown(Key.Z);

            Assert.False(handled, "A bare Z must not be handled by the grammar key router.");
            Assert.Equal("m", vm.Dnfs.Single(d => d.Seat.DriverId == id).Reason); // unchanged: no undo
        });
    }

    /// <summary>The BUG B production fix, verified on the rendered control: the grammar InputBox has
    /// its built-in TextBox undo DISABLED, so there is exactly one Ctrl+Z owner (the grammar's
    /// vm.Undo). With <c>IsUndoEnabled=false</c> the TextBox cannot swallow the chord or silently
    /// revert only its own text regardless of where focus sits after a Mech/Acc click.</summary>
    [Fact]
    public void InputBox_HasBuiltInTextBoxUndoDisabled_SoGrammarOwnsCtrlZ()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);
            host.Layout();

            Assert.False(host.InputBox.IsUndoEnabled,
                "InputBox.IsUndoEnabled must be false so the grammar's Ctrl+Z is the only undo owner.");
        });
    }

    // ---------- an off-screen host for one ResultEntryView over one VM ----------

    private sealed class ViewHost : IDisposable
    {
        private readonly Window _window;
        public ResultEntryView View { get; }
        public TextBox InputBox { get; }

        private ViewHost(Window window, ResultEntryView view, TextBox inputBox)
        {
            _window = window;
            View = view;
            InputBox = inputBox;
        }

        public static ViewHost Show(ResultEntryViewModel vm)
        {
            var view = new ResultEntryView { DataContext = vm };
            var window = new Window
            {
                Content = view,
                Width = 1200,
                Height = 800,
                // Off-screen + chromeless so nothing flashes on a dev machine, but a real HWND so
                // focus / keyboard routing behave exactly as in the app.
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            window.Show();
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Loaded);

            var inputBox = FindByName<TextBox>(view, "InputBox")
                ?? throw new InvalidOperationException("InputBox not found in the rendered view.");
            return new ViewHost(window, view, inputBox);
        }

        public void Layout()
        {
            _window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            WpfRenderHarness.Pump();
        }

        public T? Find<T>(string name) where T : FrameworkElement => FindByName<T>(View, name);

        /// <summary>The DNF custom-cause TextBox on a given driver's row — the one bound to
        /// <c>Detail</c>, width 150.</summary>
        public TextBox? FindDnfDetailBox(string driverId) =>
            Descendants<TextBox>(View)
                .FirstOrDefault(b => DnfEntryOf(b.DataContext)?.Seat.DriverId == driverId);

        /// <summary>The DSQ reason TextBox on a given driver's row (its DataContext is the DSQ
        /// GridSeat).</summary>
        public TextBox? FindDsqReasonBox(string driverId) =>
            Descendants<TextBox>(View)
                .FirstOrDefault(b => (b.DataContext as GridSeat)?.DriverId == driverId
                                     && !ReferenceEquals(b, InputBox));

        /// <summary>The inline reason Button ("m"/"a"/"o") on a given driver's DNF row, matched by
        /// its Tag.</summary>
        public Button? FindReasonButton(string driverId, string tag) =>
            Descendants<Button>(View)
                .FirstOrDefault(b => (b.Tag as string) == tag
                                     && DnfEntryOf(b.DataContext)?.Seat.DriverId == driverId);

        // ---------- event raising through the REAL routed-event plumbing ----------

        public void RaiseLeftMouseUp(UIElement source)
        {
            var args = new MouseButtonEventArgs(
                InputManager.Current.PrimaryMouseDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseUpEvent,
                Source = source,
            };
            source.RaiseEvent(args);
        }

        public void RaiseLostFocus(UIElement source) =>
            source.RaiseEvent(new RoutedEventArgs(FrameworkElement.LostFocusEvent, source));

        public void RaiseButtonClick(Button button) =>
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));

        /// <summary>The reason text actually rendered on a driver's DNF row — read straight out of
        /// the realised visual tree (the <c>DnfReasonConverter</c> output shown to the user), so an
        /// assertion on it proves the ROW updated, not just the viewmodel. Returns e.g. "mechanical"
        /// or "retired" (the word the converter yields for "m" / "o-without-detail").</summary>
        public string? RenderedDnfReasonText(string driverId)
        {
            // The DNF row template puts the reason in a TextBlock built from two <Run>s: " — " and
            // the DnfReasonConverter output. A TextBlock composed of explicit Runs reports Text=""
            // (the Text property only mirrors simple content), so read the Runs directly. Find the
            // row whose DataContext is this driver's DnfEntry and whose inlines start with " — ".
            foreach (var tb in Descendants<System.Windows.Controls.TextBlock>(View))
            {
                if (DnfEntryOf(tb.DataContext)?.Seat.DriverId != driverId)
                    continue;
                var runs = tb.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text).ToArray();
                string joined = string.Concat(runs);
                int dash = joined.IndexOf('—');
                if (dash >= 0)
                    return joined[(dash + 1)..].Trim();
            }
            return null;
        }

        /// <summary>Invoke the view's real <c>OnPreviewKeyDown</c> with a key-down for
        /// <paramref name="key"/> under whatever modifiers are currently held, and report whether the
        /// handler marked it Handled. Used to prove the Ctrl+Z branch is modifier-gated.</summary>
        public bool InvokePreviewKeyDown(Key key)
        {
            var source = PresentationSource.FromVisual(View)
                ?? throw new InvalidOperationException("No PresentationSource for the view.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            // Disambiguate from any base OnPreviewKeyDown(KeyEventArgs) override by pinning the
            // exact two-parameter (object sender, KeyEventArgs e) signature this view declares.
            var method = typeof(ResultEntryView).GetMethod(
                "OnPreviewKeyDown",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                binder: null,
                types: [typeof(object), typeof(KeyEventArgs)],
                modifiers: null)
                ?? throw new InvalidOperationException("OnPreviewKeyDown(object, KeyEventArgs) not found.");
            method.Invoke(View, [View, args]);
            return args.Handled;
        }

        private static DnfEntry? DnfEntryOf(object? dataContext) => dataContext as DnfEntry;

        public void Dispose()
        {
            _window.Close();
            WpfRenderHarness.Pump(DispatcherPriority.Background);
        }

        // ---------- visual-tree helpers ----------

        private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement =>
            Descendants<T>(root).FirstOrDefault(e => e.Name == name);

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (var descendant in Descendants<T>(child))
                    yield return descendant;
            }
        }
    }
}
