using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using System;

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
            };
            Deactivated += (_, _) => Settings()?.HideSecrets();
        }

        private SettingsViewModel? Settings() => (DataContext as MainWindowViewModel)?.Settings;
        private SearchesViewModel? Searches() => (DataContext as MainWindowViewModel)?.Searches;
        private ResultsViewModel? Results() => (DataContext as MainWindowViewModel)?.Results;

        // the Settings view model if Settings > Campertron is the tab shown
        private SettingsViewModel? SettingsShown() =>
            MainTabControl.SelectedIndex == SettingsTabIndex && SettingsTabControl.SelectedIndex == CampertronSettingsTabIndex ? Settings() : null;

        // The top-level tabs, in this order (decision D50); Searches is the first and the one shown at the start.
        internal const int SearchesTabIndex = 0;
        internal const int ResultsTabIndex = 1;
        internal const int SettingsTabIndex = 2;
        internal const int CampsitesTabIndex = 3;
        // Settings' own tabs
        internal const int CampertronSettingsTabIndex = 0;
        internal const int SystemSettingsTabIndex = 1;
        // Campsites' own tabs, the campground lookup (By Park is the first)
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
        }

        private MainWindowViewModel? _Observed;

        // The startup creates the tabs' view models when it succeeds; the tab shown then (Searches) is shown to them.
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_Observed != null)
            {
                _Observed.PropertyChanged -= ViewModel_PropertyChanged;
            }
            _Observed = DataContext as MainWindowViewModel;
            if (_Observed != null)
            {
                _Observed.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? Sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.State) && _Observed?.IsReady == true)
            {
                ShownTabChanged();
            }
        }

        // R8: the configuration and the campground database are prepared after the window is shown (they used to be
        // prepared here, in the constructor, before any window existed). See MainWindowViewModel.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            (DataContext as MainWindowViewModel)?.Start();
        }

        // A preparation still running when the window closes must not change it afterwards; a search still running is
        // canceled (GS2), and nothing it reports later changes the window.
        protected override void OnClosed(EventArgs e)
        {
            (DataContext as MainWindowViewModel)?.Detach();
            base.OnClosed(e);
        }

        // Keyboard-trap workaround (TD-39, beta.8, build-and-test.md §P), tested in Campertron. With Avalonia 12.1.6,
        // Campertron's window and a minimal TabControl with two buttons both reproduce this: Tab and Shift+Tab do not
        // leave a TabControl's selected content, and at its last (or first) focusable element focus stays where it is
        // (a window without a TabControl wraps normally). Since D50 two tab rows are nested in the top-level tabs
        // (Settings and Campsites), and there it traps on the nested tab header too. Avalonia's own navigation runs first;
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