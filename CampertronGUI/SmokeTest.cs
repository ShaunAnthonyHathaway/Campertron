using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Campertron
{
    // `CampertronGUI --smoke-test` (R8): a release smoke test of the real GUI on a real platform. It starts the normal
    // application and window, lets the normal startup prepare the configuration and database, waits for the window's
    // native handle, the ready state with data and a rendered frame, then closes the window through the normal lifetime.
    // Every step prints one "SMOKE key=value" line to standard output; the exit code is 0 only if every step passed.
    // Failures are explained on standard error. Nothing else differs from a normal start: same data folder, same
    // Load.GetConfig, no extra network access. Since CM5 it also runs the settings editor's probe (SettingsProbe) in a
    // new temporary folder, which it removes, and shows the Settings tab; since CM6 the search editor's probe
    // (SearchProbe, also in a temporary folder, with the installed campground data) and the Searches tab; since GS2 the
    // Results tab and runs of the search service with a fixture campground and a fixture transport (RunProbe, in a
    // temporary folder); since D50 the four top-level tabs and the two nested rows, the theme preference's store and the
    // three themes (ThemeProbe, in a temporary folder) and Settings > System; since V011-2 (D60) its current configuration,
    // whose Copy and Open settings folder it never presses; since V011-1 (D61) its campground data, whose Refresh campground
    // data… it never presses (no refresh, download or confirmation). Nothing is written into the settings folder or the
    // user's theme preference, and nothing reaches recreation.gov.
    public sealed class SmokeTest
    {
        public const string Argument = "--smoke-test";
        public const int PassExitCode = 0;
        public const int FailExitCode = 1;
        public const int TimeoutExitCode = 2;
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        private readonly TextWriter _Out;
        private readonly TextWriter _Err;
        private readonly TimeSpan _Timeout;
        private readonly Action<int> _Abort;
        private readonly Func<TopLevel, IPlatformHandle?> _Handle;
        private readonly Action<TopLevel, Action> _OnRenderedFrame;
        private readonly object _Lock = new object();
        private Timer? _Timer;
        private MainWindow? _Window;
        private MainWindowViewModel? _ViewModel;
        // the UI thread's dispatcher, captured by Attach on the UI thread: Fail posts to it from any thread and never asks
        // for Dispatcher.UIThread itself (TD-56)
        private Dispatcher? _Ui;
        private string _Waiting = "the window to open";
        private bool _Opened;
        private bool _Ready;
        private bool _CloseRequested;
        private bool _Closed;
        private bool _Finished;
        private string? _Failure;

        public static bool IsRequest(string[]? Args)
        {
            return Args != null && Args.Length == 1 && Args[0] == Argument;
        }

        // Production: the process exits with code 2 on timeout, even if the UI thread is blocked.
        public SmokeTest(TextWriter Out, TextWriter Err)
            : this(Out, Err, DefaultTimeout, Code => Environment.Exit(Code), Window => Window.TryGetPlatformHandle(), WaitForRenderedFrame)
        {
        }

        // Tests replace the timeout, the abort, the native handle and the rendered-frame signal.
        public SmokeTest(TextWriter Out, TextWriter Err, TimeSpan Timeout, Action<int> Abort,
            Func<TopLevel, IPlatformHandle?> Handle, Action<TopLevel, Action> OnRenderedFrame)
        {
            _Out = Out;
            _Err = Err;
            _Timeout = Timeout;
            _Abort = Abort;
            _Handle = Handle;
            _OnRenderedFrame = OnRenderedFrame;
        }

        public string? Failure => _Failure;

        // Runs the GUI exactly as Program.Main does, with the smoke test attached to the main window.
        public static int Run(AppBuilder Builder, string[] Args)
        {
            var Smoke = new SmokeTest(Console.Out, Console.Error);
            Smoke.Line("startup", RuntimeInfo.Describe(typeof(SmokeTest).Assembly).Split('\n')[0].Trim());
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Smoke.Fail("unhandled exception: " + e.ExceptionObject);
            int LifetimeExitCode = Builder.StartWithClassicDesktopLifetime(Args, Lifetime =>
            {
                Lifetime.Startup += (_, _) =>
                {
                    if (Lifetime.MainWindow is MainWindow Window)
                    {
                        Smoke.Attach(Window);
                    }
                    else
                    {
                        Smoke.Fail("the application created no main window");
                    }
                };
            });
            (Application.Current as App)?.WaitForPendingPreparation();
            (Application.Current as App)?.WaitForPendingSearch(App.SearchShutdownWait);
            // the smoke test never starts a refresh (V011-1): nothing is pending, nothing is reported
            App.ReportRefreshAfterClose((Application.Current as App)?.WaitForPendingRefresh(), Console.Error);
            return Smoke.Finish(LifetimeExitCode);
        }

        // On the UI thread, before or after the window is shown.
        public void Attach(MainWindow Window)
        {
            _Ui = Dispatcher.UIThread;
            _Window = Window;
            _ViewModel = Window.DataContext as MainWindowViewModel;
            if (_ViewModel == null)
            {
                Fail("the main window has no MainWindowViewModel");
                return;
            }
            _Ui.UnhandledException += (_, e) => Fail("unhandled UI exception: " + e.Exception);
            _ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            Window.Opened += (_, _) => OnOpened();
            Window.Closing += Window_Closing;
            Window.Closed += (_, _) => _Closed = true;
            _Timer = new Timer(_ => OnTimeout(), null, _Timeout, System.Threading.Timeout.InfiniteTimeSpan);
            if (Window.IsVisible)
            {
                OnOpened();
            }
        }

        private void OnOpened()
        {
            if (_Opened || _Finished || _Window == null)
            {
                return;
            }
            _Opened = true;
            Line("window-opened", "yes");
            IPlatformHandle? Handle = _Handle(_Window);
            if (Handle == null || Handle.Handle == IntPtr.Zero)
            {
                Fail("the main window has no native platform handle");
                return;
            }
            Line("native-handle", (Handle.HandleDescriptor ?? "handle") + ":0x" + Handle.Handle.ToString("x"));
            _Waiting = "the startup to prepare the campground data";
            CheckState();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.State))
            {
                CheckState();
            }
        }

        private void CheckState()
        {
            if (!_Opened || _Ready || _Finished || _ViewModel == null || _Window == null)
            {
                return;
            }
            if (_ViewModel.IsFailed)
            {
                Fail("startup failed: " + _ViewModel.ErrorMessage);
                return;
            }
            if (!_ViewModel.IsReady || _ViewModel.ReactiveViewModel == null)
            {
                return;
            }
            _Ready = true;
            Line("ready", "yes");
            Line("config-path", _ViewModel.ConfigPath);
            _Waiting = "a rendered frame";
            _OnRenderedFrame(_Window, OnRenderedFrame);
        }

        // After a rendered frame the ready window has been laid out, so its selectors show the data.
        private void OnRenderedFrame()
        {
            if (_Finished || _Window == null || _ViewModel?.ReactiveViewModel == null || _CloseRequested || _Failure != null)
            {
                return;
            }
            Line("rendered-frame", "yes");
            // D50: four top-level tabs with Searches shown at the start, and the Settings and Campgrounds tabs' own rows (the line's
            // key "campsites" and the control's name keep the tab's label before D55)
            TabControl? Tabs = _Window.FindControl<TabControl>("MainTabControl");
            TabControl? SettingsRow = _Window.FindControl<TabControl>("SettingsTabControl");
            TabControl? CampsitesRow = _Window.FindControl<TabControl>("CampsitesTabControl");
            if (Tabs == null || SettingsRow == null || CampsitesRow == null)
            {
                Fail("the main window has no tabs");
                return;
            }
            string Navigation = "start=" + Header(Tabs.SelectedItem) + " tabs=" + Headers(Tabs) + " settings=" + Headers(SettingsRow)
                + " campsites=" + Headers(CampsitesRow);
            Line("navigation", Navigation);
            if (Navigation != ExpectedNavigation)
            {
                Fail("the tabs are not the expected ones: " + Navigation);
                return;
            }
            // the campground lookup: Campgrounds > By Park, its views created, bound and laid out now
            _Waiting = "the Campgrounds tab";
            Tabs.SelectedIndex = MainWindow.CampsitesTabIndex;
            CampsitesRow.SelectedIndex = MainWindow.ByParkTabIndex;
            _Window.UpdateLayout();
            int Parks = _Window.FindControl<ComboBox>("ParkComboBox")?.ItemCount ?? 0;
            int States = _ViewModel.ReactiveViewModel.StateList.Count;
            Line("parks", Parks.ToString(CultureInfo.InvariantCulture));
            Line("states", States.ToString(CultureInfo.InvariantCulture));
            if (Parks <= 0 || States <= 0)
            {
                Fail("the campground data is empty (parks " + Parks + ", states " + States + ")");
                return;
            }
            Line("window-size", _Window.ClientSize.Width.ToString("0", CultureInfo.InvariantCulture) + "x"
                + _Window.ClientSize.Height.ToString("0", CultureInfo.InvariantCulture));
            Line("render-scaling", _Window.RenderScaling.ToString("0.###", CultureInfo.InvariantCulture));
            Line("theme", Application.Current?.ActualThemeVariant?.ToString() ?? "unknown");
            // D50: the theme preference's store and the three themes on this platform, in a temporary folder (ThemeProbe)
            (bool ThemePassed, string Theme) = ThemeProbe.Run(Application.Current);
            Line("theme-probe", Theme);
            if (!ThemePassed)
            {
                Fail("the theme probe failed: " + Theme);
                return;
            }
            // CM5: the settings editor's writer on this platform, in a temporary folder (SettingsProbe), then the Settings
            // tab with the real settings folder: its views and bindings initialize and both files open for editing
            (bool ProbePassed, string Probe) = SettingsProbe.Run();
            Line("settings-probe", Probe);
            if (!ProbePassed)
            {
                Fail("the settings editor probe failed: " + Probe);
                return;
            }
            // Settings > Campertron: the tab's views are created, bound and laid out now (the frame above already proved
            // rendering)
            _Waiting = "the Settings tab";
            Tabs.SelectedIndex = MainWindow.SettingsTabIndex;
            SettingsRow.SelectedIndex = MainWindow.CampertronSettingsTabIndex;
            _Window.UpdateLayout();
            OnSettingsShown();
        }

        private void OnSettingsShown()
        {
            if (_Finished || _Window == null || _ViewModel == null || _CloseRequested || _Failure != null)
            {
                return;
            }
            SettingsViewModel? Settings = _ViewModel.Settings;
            bool Shown = _Window.FindControl<SettingsView>("SettingsContent") is { IsEffectivelyVisible: true };
            if (!Shown || Settings == null)
            {
                Line("settings-tab", "no");
                Fail("the Settings tab was not shown");
                return;
            }
            // D69: Ntfy.yaml's editor too; on a clean start Ntfy.yaml does not exist, so the editor is a new file (nothing written).
            // v0.3.0's PR 5: Web.yaml's editor too, likewise a new file on a clean start
            Line("settings-tab", "yes general=" + (Settings.General.IsEditable ? "editable" : "not-editable")
                + " email=" + (Settings.Email.IsEditable ? "editable" : "not-editable")
                + " ntfy=" + (Settings.Ntfy.IsNew ? "new" : Settings.Ntfy.IsEditable ? "editable" : "not-editable")
                + " web=" + (Settings.Web.IsNew ? "new" : Settings.Web.IsEditable ? "editable" : "not-editable"));
            // D50: Settings > System with the theme the application started with (on a new computer: System, no file)
            _Waiting = "the System settings tab";
            TabControl? SettingsRow = _Window.FindControl<TabControl>("SettingsTabControl");
            SystemSettingsViewModel SystemTab = _ViewModel.SystemSettings;
            if (SettingsRow == null)
            {
                Line("system-tab", "no");
                Fail("the System settings tab was not shown");
                return;
            }
            SettingsRow.SelectedIndex = MainWindow.SystemSettingsTabIndex;
            _Window.UpdateLayout();
            SystemSettingsView? SystemView = _Window.FindControl<SystemSettingsView>("SystemSettingsContent");
            string[] Choices = { "SystemThemeButton", "LightThemeButton", "DarkThemeButton" };
            RadioButton?[] Buttons = Choices.Select(Name => SystemView?.FindControl<RadioButton>(Name)).ToArray();
            if (SystemView is not { IsEffectivelyVisible: true } || !ReferenceEquals(SystemView.DataContext, SystemTab) || Buttons.Any(b => b == null)
                || Buttons.Count(b => b!.IsChecked == true) != 1 || Buttons[(int)SystemTab.Theme]!.IsChecked != true)
            {
                Line("system-tab", "no");
                Fail("the System settings tab was not shown with the theme choices");
                return;
            }
            // V011-2 (D60): below the theme, the current configuration with the console's eight View Config items, the
            // startup's settings folder and its two actions bound. Neither action is pressed (no clipboard, no file manager),
            // and a status would mean one ran.
            CurrentConfigurationViewModel? Configuration = SystemTab.Configuration;
            CurrentConfigurationView? Card = SystemView.FindControl<CurrentConfigurationView>("CurrentConfigurationContent");
            Button? CopyButton = Card?.FindControl<Button>("CopyConfigurationButton");
            Button? FolderButton = Card?.FindControl<Button>("OpenConfigFolderButton");
            if (Configuration == null || Card is not { IsEffectivelyVisible: true } || !ReferenceEquals(Card.DataContext, Configuration)
                || !Configuration.Items.Select(i => i.Label).SequenceEqual(CurrentConfiguration.Labels)
                || (Card.FindControl<ItemsControl>("ConfigurationItems")?.ItemCount ?? 0) != CurrentConfiguration.Labels.Count)
            {
                Line("system-tab", "no");
                Fail("the System settings tab was not shown with the current configuration");
                return;
            }
            // V011-1 (D61): below it, the campground data with the last refresh and Refresh campground data… bound, its
            // confirmation built but hidden with Start refresh and Back bound, and no refresh started. Nothing is pressed.
            CampgroundDataViewModel? Data = SystemTab.CampgroundData;
            CampgroundDataView? DataCard = SystemView.FindControl<CampgroundDataView>("CampgroundDataContent");
            SelectableTextBlock? LastRefreshed = DataCard?.FindControl<SelectableTextBlock>("LastRefreshedValue");
            Border? Confirmation = DataCard?.FindControl<Border>("RefreshConfirmation");
            Button? StartRefresh = DataCard?.FindControl<Button>("StartRefreshButton");
            Button? Back = DataCard?.FindControl<Button>("RefreshBackButton");
            if (Data == null || DataCard is not { IsEffectivelyVisible: true } || !ReferenceEquals(DataCard.DataContext, Data))
            {
                Line("system-tab", "no");
                Fail("the System settings tab was not shown with the campground data");
                return;
            }
            bool ConfirmationReady = Confirmation != null && !Confirmation.IsVisible && StartRefresh != null && ReferenceEquals(StartRefresh.Command, Data.StartCommand)
                && Back != null && ReferenceEquals(Back.Command, Data.BackCommand);
            bool RefreshIdle = !Data.IsRunning && !Data.IsConfirming && Data.CurrentRefreshId == 0 && Data.LastResult == null && !Data.HasStatus;
            Line("system-tab", "yes choices=" + string.Join(",", Buttons.Select(b => b!.Content as string)) + " selected=" + SystemTab.Theme
                + " preference=" + SystemTab.StartSource.ToString().ToLowerInvariant()
                + " configuration=" + Configuration.Items.Count.ToString(CultureInfo.InvariantCulture)
                + " paths=" + (Configuration.ConfigPath == _ViewModel.ConfigPath && Configuration.Items[1].Value == _ViewModel.ConfigPath ? "startup" : "other")
                + " copy=" + Bound(CopyButton, Configuration.CopyCommand) + " open-folder=" + Bound(FolderButton, Configuration.OpenFolderCommand)
                + " actions=" + (Configuration.HasStatus ? "ran" : "idle")
                + " campground-data=shown"
                + " last-refreshed=" + (LastRefreshed is { IsEffectivelyVisible: true } && !string.IsNullOrWhiteSpace(LastRefreshed.Text) && LastRefreshed.Text == Data.LastRefreshedText ? "shown" : "missing")
                + " refresh=" + Bound(DataCard.FindControl<Button>("RefreshDatabaseButton"), Data.RefreshCommand)
                + " confirmation=" + (ConfirmationReady ? "ready" : "missing")
                + " refresh-state=" + (RefreshIdle ? "idle" : "started")
                // D62: the automatic restart's launcher, resolved from this process's own executable (nothing is started, no
                // handoff is written) with the window's capture bound
                + " restart=" + (_ViewModel.RestartHooks.Capture == null ? "unbound" : RestartTargets.Current()?.Kind ?? "unavailable"));
            // CM6: the search editor's writer, Save As and campground lookup on this platform, in a temporary folder
            // (SearchProbe), then the Searches tab with the real settings folder (read only) and the campground data
            (bool SearchPassed, string Search) = SearchProbe.Run(_ViewModel.ConfigPath);
            Line("search-probe", Search);
            if (!SearchPassed)
            {
                Fail("the search editor probe failed: " + Search);
                return;
            }
            TabControl? Tabs = _Window.FindControl<TabControl>("MainTabControl");
            SearchesViewModel? Searches = _ViewModel.Searches;
            if (Tabs == null || Searches == null)
            {
                Line("searches-tab", "no");
                Fail("the Searches tab was not shown");
                return;
            }
            _Waiting = "the Searches tab";
            Tabs.SelectedIndex = MainWindow.SearchesTabIndex;
            _Window.UpdateLayout();
            bool SearchesShown = _Window.FindControl<SearchesView>("SearchesContent") is { IsEffectivelyVisible: true };
            if (!SearchesShown || !Searches.IsLoaded || Searches.Catalog.LoadError != null || Searches.Catalog.Entries.Count == 0)
            {
                Line("searches-tab", "no");
                Fail("the Searches tab was not shown with the campground data");
                return;
            }
            Line("searches-tab", "yes files=" + Searches.Files.Count.ToString(CultureInfo.InvariantCulture)
                + " campgrounds=" + Searches.Catalog.Entries.Count.ToString(CultureInfo.InvariantCulture));
            // D51: a campground's filter values from the installed data, read on a worker thread by the selectors' source,
            // Campertron's attribute index built by the start, and a selector checking a value (FilterProbe)
            _Waiting = "the filter probe";
            OnFilterProbe(Searches.Catalog.Entries.Select(E => E.FacilityID).ToList());
        }

        private async void OnFilterProbe(System.Collections.Generic.List<string> CampgroundIds)
        {
            if (_Finished || _Window == null || _ViewModel == null || _CloseRequested || _Failure != null)
            {
                return;
            }
            (bool FilterPassed, string Filter) = await FilterProbe.RunAsync(_ViewModel.ConfigPath, CampgroundIds);
            if (_Finished || _CloseRequested || _Failure != null)
            {
                return;
            }
            Line("filter-probe", Filter);
            if (!FilterPassed)
            {
                Fail("the filter selector probe failed: " + Filter);
                return;
            }
            // GS2: the Results tab with the startup's folders, then runs of GS1's search service through the tab's view model
            // and view in a temporary folder, with a fixture transport (RunProbe): never recreation.gov
            _Waiting = "the Results tab";
            OnResultsShown();
        }

        private async void OnResultsShown()
        {
            if (_Finished || _Window == null || _ViewModel == null || _CloseRequested || _Failure != null)
            {
                return;
            }
            (bool RunPassed, string Tab, string Run) = await RunProbe.RunAsync(_Window, _ViewModel);
            if (_Finished || _CloseRequested || _Failure != null)
            {
                return;
            }
            Line("results-tab", Tab);
            Line("run-probe", Run);
            if (!RunPassed)
            {
                Fail("the Results tab probe failed: " + Run);
                return;
            }
            _Waiting = "the window to close";
            _CloseRequested = true;
            Line("close-requested", "yes");
            _Window.Close();
        }

        // the tabs the window must have (D50), as the navigation line reports them
        internal const string ExpectedNavigation = "start=Searches tabs=Searches,Results,Settings,Campgrounds settings=Campertron,System campsites=By Park,By City,By State";

        // a button shown, enabled and bound to the command it should run
        private static string Bound(Button? Button, System.Windows.Input.ICommand Command)
        {
            return Button is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } && ReferenceEquals(Button.Command, Command) ? "bound" : "unbound";
        }

        private static string Header(object? Item)
        {
            return (Item as TabItem)?.Header as string ?? "none";
        }

        private static string Headers(TabControl Row)
        {
            return string.Join(",", Row.Items.Select(Header));
        }

        private void Window_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (_Finished)
            {
                return;
            }
            if (!_CloseRequested)
            {
                Fail("the window was closed before the smoke test finished (" + Classify(e) + ")");
                return;
            }
            Line("closing", Classify(e));
            if (!e.IsProgrammatic)
            {
                Fail("the window closed for another reason than the smoke test's request");
            }
        }

        private static string Classify(WindowClosingEventArgs e)
        {
            return "programmatic=" + (e.IsProgrammatic ? "yes" : "no") + " reason=" + e.CloseReason;
        }

        // After the application lifetime has ended (in tests: after the window closed). Returns the exit code.
        public int Finish(int LifetimeExitCode)
        {
            lock (_Lock)
            {
                if (_Finished)
                {
                    return _Failure == null ? PassExitCode : TimeoutExitCode;
                }
                _Finished = true;
            }
            _Timer?.Dispose();
            if (_Failure == null)
            {
                if (!_CloseRequested || !_Closed)
                {
                    _Failure = "the application ended before the smoke test finished, while waiting for " + _Waiting;
                }
                else if (LifetimeExitCode != 0)
                {
                    _Failure = "the application lifetime returned exit code " + LifetimeExitCode;
                }
                else if (_ViewModel != null && !_ViewModel.PendingPreparation.IsCompleted)
                {
                    _Failure = "the startup preparation was still running after the window closed";
                }
            }
            if (_Failure != null)
            {
                Explain(_Failure);
                Line("result", "fail");
                return FailExitCode;
            }
            Line("shutdown", "clean exit-code=" + LifetimeExitCode.ToString(CultureInfo.InvariantCulture));
            Line("result", "pass");
            return PassExitCode;
        }

        // Records the first failure and closes the window so the lifetime ends; Finish reports it. Any thread (an unhandled
        // exception can be reported from a worker): the close goes to the dispatcher Attach captured; before Attach there is
        // no window to close
        public void Fail(string Reason)
        {
            lock (_Lock)
            {
                if (_Finished || _Failure != null)
                {
                    return;
                }
                _Failure = Reason;
            }
            _Ui?.Post(() =>
            {
                if (_Window != null && !_Closed)
                {
                    _Window.Close();
                }
            });
        }

        private void OnTimeout()
        {
            lock (_Lock)
            {
                if (_Finished)
                {
                    return;
                }
                _Finished = true;
                _Failure ??= "timed out after " + _Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                    + " s waiting for " + _Waiting;
            }
            Explain(_Failure);
            Line("result", "fail");
            _Out.Flush();
            _Err.Flush();
            _Abort(TimeoutExitCode);
        }

        private void Explain(string Failure)
        {
            lock (_Out)
            {
                _Err.WriteLine("CampertronGUI smoke test failed: " + Failure);
                _Err.Flush();
            }
        }

        private void Line(string Key, string Value)
        {
            string Clean = string.Join(" ", (Value ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            lock (_Out)
            {
                _Out.WriteLine("SMOKE " + Key + "=" + Clean);
                _Out.Flush();
            }
        }

        // Avalonia calls an animation-frame callback when it renders the next frame; the second callback comes after
        // the first frame was rendered and presented.
        public static void WaitForRenderedFrame(TopLevel Window, Action Rendered)
        {
            Window.RequestAnimationFrame(_ => Window.RequestAnimationFrame(_ => Rendered()));
        }
    }
}
