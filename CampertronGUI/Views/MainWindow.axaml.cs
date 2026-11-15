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
            // CM5: the settings editors look at their files when the Settings tab is shown or the window is activated
            // (another program may have changed them meanwhile), and the revealed password is masked again when the user
            // leaves the tab or the window
            MainTabControl.PropertyChanged += MainTabControl_PropertyChanged;
            Activated += (_, _) =>
            {
                SettingsShown()?.CheckForExternalChanges();
                // CM6: the search files too, when the Searches tab is shown
                if (MainTabControl.SelectedIndex == SearchesTabIndex)
                {
                    Searches()?.CheckForExternalChanges();
                }
            };
            Deactivated += (_, _) => Settings()?.HideSecrets();
        }

        private SettingsViewModel? Settings() => (DataContext as MainWindowViewModel)?.Settings;
        private SearchesViewModel? Searches() => (DataContext as MainWindowViewModel)?.Searches;

        // the Settings view model if its tab is the one shown
        private SettingsViewModel? SettingsShown() => MainTabControl.SelectedIndex == SettingsTabIndex ? Settings() : null;

        internal const int SettingsTabIndex = 3;
        internal const int SearchesTabIndex = 4;

        private void MainTabControl_PropertyChanged(object? Sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != SelectingItemsControl.SelectedIndexProperty)
            {
                return;
            }
            if (e.NewValue is int Index && Index == SettingsTabIndex)
            {
                Settings()?.CheckForExternalChanges();
            }
            else
            {
                Settings()?.HideSecrets();
            }
            // CM6: the Searches tab reads the search files and the campground data when it is first shown, and looks at
            // the files on disk again each time it is shown
            if (e.NewValue is int Shown && Shown == SearchesTabIndex)
            {
                Searches()?.Show();
            }
        }

        // R8: the configuration and the campground database are prepared after the window is shown (they used to be
        // prepared here, in the constructor, before any window existed). See MainWindowViewModel.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            (DataContext as MainWindowViewModel)?.Start();
        }

        // A preparation still running when the window closes must not change it afterwards.
        protected override void OnClosed(EventArgs e)
        {
            (DataContext as MainWindowViewModel)?.Detach();
            base.OnClosed(e);
        }

        // Keyboard-trap workaround (TD-39, beta.8, build-and-test.md §P), tested in Campertron. With Avalonia 12.1.6,
        // Campertron's window and a minimal TabControl with two buttons both reproduce this: Tab and Shift+Tab do not
        // leave a TabControl's selected content, and at its last (or first) focusable element focus stays where it is
        // (a window without a TabControl wraps normally). Avalonia's own navigation runs first; only when a plain Tab or
        // Shift+Tab left focus unchanged inside the tab content does focus move to the selected tab header, where the
        // arrow keys switch tabs and Tab enters the content again. Reassess after any Avalonia upgrade or an upstream
        // fix for this exact behavior.
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
            if (!IsPlainTab(e) || Before is not Visual Focused || Focused is TabItem
                || !ReferenceEquals(FocusManager?.GetFocusedElement(), Before)
                || MainTabControl.SelectedItem is not TabItem SelectedTab || !MainTabControl.IsVisualAncestorOf(Focused))
            {
                return;
            }
            e.Handled = SelectedTab.Focus(NavigationMethod.Tab) || e.Handled;
        }
    }
}