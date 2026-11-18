using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Campertron.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Size, minimum size and startup position are set in MainWindow.axaml.
            //CanResize = false;
            AddHandler(KeyDownEvent, RememberFocusBeforeTab, RoutingStrategies.Tunnel);
            AddHandler(KeyDownEvent, WrapTabAtContentEdge, RoutingStrategies.Bubble, handledEventsToo: true);
            // CM5: the settings editors look at their files when Settings > Campertron is shown or the window is activated
            // (another program may have changed them meanwhile), and the revealed password is masked again when the user
            // leaves that tab or the window. CM6 and GS2: the Searches and Results tabs look at the search files when they
            // are shown. D50: either level of tabs can show or hide them.
            MainTabControl.PropertyChanged += Tabs_PropertyChanged;
            SettingsTabControl.PropertyChanged += Tabs_PropertyChanged;
            Activated += (_, _) =>
            {
                SettingsShown()?.CheckForExternalChanges();
                // CM6: the search files too, when the Searches tab is shown
                if (MainTabControl.SelectedIndex == SearchesTabIndex)
                {
                    Searches()?.CheckForExternalChanges();
                }
                // GS2: and when the Results tab is shown (not while a search runs: its choice is fixed)
                if (MainTabControl.SelectedIndex == ResultsTabIndex)
                {
                    Results()?.CheckForExternalChanges();
                }
                // V011-2: Settings > System's current configuration reads again (another program may have changed
                // General.yaml or the search files meanwhile); V011-1: so does the campground data's last refresh
                SystemShown()?.Configuration?.Refresh();
                SystemShown()?.CampgroundData?.Show();
            };
            Deactivated += (_, _) => Settings()?.HideSecrets();
            // D62: the window's normal bounds, kept in memory only, for an automatic restart after a refresh (Avalonia reports
            // no restore bounds of a maximized window); nothing is stored or reused by an ordinary start
            PositionChanged += (_, _) => RememberNormalBoundsSoon();
            Resized += (_, _) => RememberNormalBoundsSoon();
        }

        // ---- the automatic restart after a refresh (decision D62, business-rules-gui.md §26) ----

        private PixelPoint? _NormalPosition;
        private Size? _NormalSize;
        private PlacementResult? _PendingPlacement;

        // the placement a restarted process applied (tests)
        internal PlacementResult? AppliedPlacement { get; private set; }

        // After the move or resize has settled: a maximize also moves and resizes the window before its state reads
        // Maximized, so the bounds are taken only if the window is still normal then.
        private void RememberNormalBoundsSoon()
        {
            Dispatcher.UIThread.Post(RememberNormalBounds, DispatcherPriority.Background);
        }

        internal void RememberNormalBounds()
        {
            if (WindowState == WindowState.Normal && IsVisible)
            {
                _NormalPosition = Position;
                _NormalSize = ClientSize;
            }
        }

        // The window and the tabs as the automatic restart hands them on: the normal bounds (those it had before it was
        // maximized or minimized), whether it is maximized (never minimized or full screen: those restore as a normal window).
        internal RestartCapture? CaptureForRestart()
        {
            RestartWindow? Window = null;
            if (_NormalPosition is PixelPoint At && _NormalSize is Size Size)
            {
                Window = new RestartWindow(At.X, At.Y, Size.Width, Size.Height, WindowState == WindowState.Maximized);
            }
            else if (WindowState == WindowState.Normal)
            {
                Window = new RestartWindow(Position.X, Position.Y, ClientSize.Width, ClientSize.Height, false);
            }
            RestartTopTab Tab = MainTabControl.SelectedIndex switch
            {
                ResultsTabIndex => RestartTopTab.Results,
                SettingsTabIndex => RestartTopTab.Settings,
                CampsitesTabIndex => RestartTopTab.Campgrounds,
                _ => RestartTopTab.Searches,
            };
            RestartSettingsTab Settings = SettingsTabControl.SelectedIndex == SystemSettingsTabIndex ? RestartSettingsTab.System : RestartSettingsTab.Campertron;
            return new RestartCapture(Window, Tab, Settings);
        }

        // A restarted process's window, before it is shown: the old window's normal bounds fitted to the screens there are
        // now (WindowPlacement). Without a usable result the window keeps its ordinary startup placement.
        internal PlacementResult? ApplyRestartPlacement(RestartWindow Stored)
        {
            var Available = new List<PlacementScreen>();
            try
            {
                if (Screens != null)
                {
                    Available.AddRange(Screens.All.Select(S => new PlacementScreen(S.Bounds, S.WorkingArea, S.Scaling, S.IsPrimary)));
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException)
            {
                // no screen information: the size alone is used
            }
            if (WindowPlacement.Fit(Stored, Available, MinWidth, MinHeight) is not PlacementResult Result)
            {
                return null;
            }
            Width = Result.Width;
            Height = Result.Height;
            if (Result.Position is PixelPoint At)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = At;
            }
            _PendingPlacement = Result;
            AppliedPlacement = Result;
            return Result;
        }

        private SettingsViewModel? Settings() => (DataContext as MainWindowViewModel)?.Settings;
        private SearchesViewModel? Searches() => (DataContext as MainWindowViewModel)?.Searches;
        private ResultsViewModel? Results() => (DataContext as MainWindowViewModel)?.Results;

        // the Settings view model if Settings > Campertron is the tab shown
        private SettingsViewModel? SettingsShown() =>
            MainTabControl.SelectedIndex == SettingsTabIndex && SettingsTabControl.SelectedIndex == CampertronSettingsTabIndex ? Settings() : null;

        // the System settings view model if Settings > System is the tab shown (V011-2: its current configuration)
        private SystemSettingsViewModel? SystemShown() =>
            MainTabControl.SelectedIndex == SettingsTabIndex && SettingsTabControl.SelectedIndex == SystemSettingsTabIndex
                ? (DataContext as MainWindowViewModel)?.SystemSettings : null;

        // The top-level tabs, in this order (decision D50); Searches is the first and the one shown at the start.
        internal const int SearchesTabIndex = 0;
        internal const int ResultsTabIndex = 1;
        internal const int SettingsTabIndex = 2;
        internal const int CampsitesTabIndex = 3;
        // Settings' own tabs
        internal const int CampertronSettingsTabIndex = 0;
        internal const int SystemSettingsTabIndex = 1;
        // the Campgrounds tab's own tabs, the campground lookup (By Park is the first; the tab's index, x:Name and automation ID
        // keep the name Campsites, its label until D55)
        internal const int ByParkTabIndex = 0;
        internal const int ByCityTabIndex = 1;
        internal const int ByStateTabIndex = 2;

        private void Tabs_PropertyChanged(object? Sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == SelectingItemsControl.SelectedIndexProperty)
            {
                ShownTabChanged();
            }
        }

        // The tab shown changed (on either level), or the startup made the tabs' view models (the first tab is shown before
        // they exist).
        private void ShownTabChanged()
        {
            if (SettingsShown() is SettingsViewModel ShownSettings)
            {
                ShownSettings.CheckForExternalChanges();
            }
            else
            {
                Settings()?.HideSecrets();
            }
            // CM6: the Searches tab reads the search files and the campground data when it is first shown, and looks at
            // the files on disk again each time it is shown
            if (MainTabControl.SelectedIndex == SearchesTabIndex)
            {
                Searches()?.Show();
            }
            // GS2: the Results tab lists the search files (and whether the Searches tab holds unsaved changes to them) each
            // time it is shown
            if (MainTabControl.SelectedIndex == ResultsTabIndex)
            {
                Results()?.Show();
            }
            // V011-2: Settings > System's current configuration reads General.yaml (read-only) and counts the search files
            // each time it is shown, so a save in another tab shows there; V011-1: the campground data's last refresh too
            SystemShown()?.Configuration?.Refresh();
            SystemShown()?.CampgroundData?.Show();
        }

        private MainWindowViewModel? _Observed;

        // The startup creates the tabs' view models when it succeeds; the tab shown then (Searches) is shown to them.
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_Observed != null)
            {
                _Observed.PropertyChanged -= ViewModel_PropertyChanged;
                _Observed.RestartHooks.Capture = null;
            }
            _Observed = DataContext as MainWindowViewModel;
            if (_Observed != null)
            {
                _Observed.PropertyChanged += ViewModel_PropertyChanged;
                _Observed.RestartHooks.Capture = CaptureForRestart;
            }
        }

        private void ViewModel_PropertyChanged(object? Sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.State) && _Observed?.IsReady == true)
            {
                ShownTabChanged();
                // D62: a process an automatic restart started opens at Settings > System, once its startup is ready (the tabs'
                // own handlers show it: the current configuration and the campground data read again)
                if (_Observed.TakeRestartNavigation())
                {
                    SettingsTabControl.SelectedIndex = SystemSettingsTabIndex;
                    MainTabControl.SelectedIndex = SettingsTabIndex;
                    // the campground data card, with the restart's status and the new date, scrolled into view once laid out
                    Dispatcher.UIThread.Post(() => SystemSettingsContent.FindControl<CampgroundDataView>("CampgroundDataContent")?.BringIntoView(),
                        DispatcherPriority.Loaded);
                }
            }
        }

        // R8: the configuration and the campground database are prepared after the window is shown (they used to be
        // prepared here, in the constructor, before any window existed). See MainWindowViewModel.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            // D62: a window manager may place a new window itself; the restored position is asked for once more, then the
            // window is maximized if the old one was (its normal bounds stay for when it is restored)
            PlacementResult? Placement = _PendingPlacement;
            _PendingPlacement = null;
            if (Placement?.Position is PixelPoint At && Position != At)
            {
                Position = At;
            }
            RememberNormalBounds();
            if (Placement?.Maximized == true)
            {
                WindowState = WindowState.Maximized;
            }
            (DataContext as MainWindowViewModel)?.Start();
        }

        // A preparation still running when the window closes must not change it afterwards; a search still running is
        // canceled (GS2), and nothing it reports later changes the window; a refresh of the campground data that can still
        // stop is canceled (V011-1), and Program.Main waits for it.
        protected override void OnClosed(EventArgs e)
        {
            (DataContext as MainWindowViewModel)?.Detach();
            base.OnClosed(e);
        }

        // Keyboard-trap workaround (TD-39, beta.8, build-and-test.md §P), tested in Campertron. With Avalonia 12.1.6,
        // Campertron's window and a minimal TabControl with two buttons both reproduce this: Tab and Shift+Tab do not
        // leave a TabControl's selected content, and at its last (or first) focusable element focus stays where it is
        // (a window without a TabControl wraps normally). Since D50 two tab rows are nested in the top-level tabs
        // (Settings and Campgrounds), and there it traps on the nested tab header too. Avalonia's own navigation runs first;
        // only a plain Tab or Shift+Tab is corrected, in three cases:
        // - Tab left focus unchanged inside the tabs: focus moves to the selected top-level tab header (the window's first
        //   stop), so Tab cycles through the whole window;
        // - Shift+Tab left focus unchanged: focus moves to the selected header of the innermost tab row whose content
        //   holds it (from a nested header, the top-level header), so Shift+Tab walks back through every header;
        // - Tab from a tab header went into a nested tab row's content past that row's header (Avalonia does this once the
        //   nested row was left by the first case): focus moves to the nested row's selected header.
        // Arrow keys on a header switch that row's tabs, as before. Other keys and modified Tab combinations are left to
        // Avalonia. Reassess after any Avalonia upgrade or an upstream fix for this exact behavior.
        private IInputElement? _FocusBeforeTab;

        private static bool IsPlainTab(KeyEventArgs e)
        {
            return e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None;
        }

        private void RememberFocusBeforeTab(object? Sender, KeyEventArgs e)
        {
            _FocusBeforeTab = IsPlainTab(e) ? FocusManager?.GetFocusedElement() : null;
        }

        private void WrapTabAtContentEdge(object? Sender, KeyEventArgs e)
        {
            IInputElement? Before = _FocusBeforeTab;
            _FocusBeforeTab = null;
            if (!IsPlainTab(e) || Before is not Visual Focused || !MainTabControl.IsVisualAncestorOf(Focused))
            {
                return;
            }
            bool Backward = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            IInputElement? After = FocusManager?.GetFocusedElement();
            TabItem? Target;
            if (ReferenceEquals(After, Before))
            {
                // trapped: Tab wraps to the top-level header; Shift+Tab goes to the header of the row that holds the focus
                Target = Backward ? SelectedHeader(RowHolding(Focused)) : SelectedHeader(MainTabControl);
            }
            else if (!Backward && Focused is TabItem && After is Visual Entered)
            {
                // from a header into its content: the first stop there is a nested row's header
                Target = SelectedHeader(NestedRowEntered(RowOf(Focused), Entered));
            }
            else
            {
                return;
            }
            if (Target != null && !ReferenceEquals(Target, After))
            {
                e.Handled = Target.Focus(NavigationMethod.Tab) || e.Handled;
            }
        }

        // the tab row (TabControl) a header belongs to, or that shows an element in its selected content
        private static TabControl? RowOf(Visual Element)
        {
            return Element.FindAncestorOfType<TabControl>();
        }

        // The innermost tab row whose selected content holds the element: for a header, the row around its own row (a
        // tab's content is shown by its TabControl, not inside its TabItem). Null outside the window's tabs.
        private TabControl? RowHolding(Visual Element)
        {
            TabControl? Row = Element is TabItem ? RowOf(Element) is TabControl Own ? RowOf(Own) : null : RowOf(Element);
            return Row != null && (ReferenceEquals(Row, MainTabControl) || MainTabControl.IsVisualAncestorOf(Row)) ? Row : null;
        }

        // The outermost tab row nested in Row's content that holds Entered, unless Entered is that row's own header.
        private static TabControl? NestedRowEntered(TabControl? Row, Visual Entered)
        {
            if (Row == null)
            {
                return null;
            }
            TabControl? Nested = null;
            for (TabControl? Candidate = RowOf(Entered); Candidate != null && !ReferenceEquals(Candidate, Row); Candidate = RowOf(Candidate))
            {
                Nested = Candidate;
            }
            if (Nested == null || !Row.IsVisualAncestorOf(Nested))
            {
                return null;
            }
            return Entered is TabItem && ReferenceEquals(RowOf(Entered), Nested) ? null : Nested;
        }

        private static TabItem? SelectedHeader(TabControl? Row)
        {
            return Row?.SelectedItem as TabItem;
        }
    }
}