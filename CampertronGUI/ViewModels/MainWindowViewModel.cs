using Avalonia;
using Avalonia.Threading;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.data;
using ReactiveUI.Reactive;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Campertron.ViewModels
{
    public enum StartupState { Preparing, Ready, Failed }

    // Owns the GUI startup (R8). The window is shown first; Load.GetConfig (folders, settings files, extraction, repair
    // or refresh of the campground database) then runs on a background thread. The database-backed ReactiveViewModel
    // is created only after it succeeds, so no list or details getter can query a database that is not ready yet. A
    // failure is shown in the window with Try again; nothing here changes what Load.GetConfig does.
    public class MainWindowViewModel : ViewModelBase
    {
        private const int MaxErrorLength = 500;

        private readonly Func<CampertronInternalConfig> _LoadConfig;
        private readonly ResultsSeams? _ResultsSeams;
        private readonly RefreshSeams? _RefreshSeams;
        private readonly DesktopMonitoringSeams _MonitoringSeams;
        private int _Attempt;
        private bool _Detached;
        // the UI thread's dispatcher, captured by Begin on the UI thread; the monitoring's posts (v0.4.0's phase 4) use it too
        private Dispatcher? _Ui;
        private Task _PendingPreparation = Task.CompletedTask;

        public MainWindowViewModel() : this(Load.GetConfig)
        {
        }

        // The loader is the only seam (tests pass a blocking or throwing one); production uses Load.GetConfig.
        public MainWindowViewModel(Func<CampertronInternalConfig> LoadConfig) : this(LoadConfig, null)
        {
        }

        // Tests also replace the Results tab's search and time seams (GS2); production uses GS1's CampsiteSearch.
        internal MainWindowViewModel(Func<CampertronInternalConfig> LoadConfig, ResultsSeams? ResultsSeams)
            : this(LoadConfig, ResultsSeams, null)
        {
        }

        // Tests also pass the System tab's view model with a theme store of their own (D50); otherwise it is the
        // application's, which App prepared with the saved theme before the window was created. They also replace the
        // campground data refresh's seams (V011-1); production uses the library's RidbRefreshService. And the monitoring's
        // (v0.4.0's phase 4): its clock, its wait, the campground data's preparation and the Monitoring tab's timer.
        internal MainWindowViewModel(Func<CampertronInternalConfig> LoadConfig, ResultsSeams? ResultsSeams, SystemSettingsViewModel? SystemSettings,
            RefreshSeams? RefreshSeams = null, DesktopMonitoringSeams? MonitoringSeams = null)
        {
            _LoadConfig = LoadConfig;
            _ResultsSeams = ResultsSeams;
            _RefreshSeams = RefreshSeams;
            _MonitoringSeams = MonitoringSeams ?? new DesktopMonitoringSeams();
            KeepOpenCommand = new RelayCommand(KeepOpen, () => IsConfirmingClose && !IsStoppingForClose);
            StopAndCloseCommand = new RelayCommand(StopAndClose, () => IsConfirmingClose && !IsStoppingForClose);
            this.SystemSettings = SystemSettings ?? ApplicationSystemSettings();
            // D62: an automatic restart would lose unsaved edits of the settings or search files, so it doesn't happen then
            RestartHooks = new RestartHooks
            {
                HasUnsavedChanges = () => (Settings?.HasUnsavedChanges ?? false) || (Searches?.Files.Any(File => File.IsDirty) ?? false),
            };
        }

        // The automatic restart after a refresh (D62): what it learns from this window (MainWindow sets the capture).
        internal RestartHooks RestartHooks { get; }

        // This process was started by an automatic restart (D62) with this handoff, already claimed and validated in
        // Program.Main; null for every ordinary start. Its outcome gives the campground data's one-time status, and the
        // window opens at Settings > System once the startup is ready (TakeRestartNavigation).
        internal RestartHandoff? Arrival { get; init; }
        private bool _ArrivalStatusUsed;
        private bool _ArrivalNavigationUsed;

        // true once, after a successful startup of a restarted process: MainWindow then shows Settings > System
        internal bool TakeRestartNavigation()
        {
            if (Arrival == null || _ArrivalNavigationUsed || State != StartupState.Ready)
            {
                return false;
            }
            _ArrivalNavigationUsed = true;
            return true;
        }

        // One data operation at a time in this window (V011-1): the Results tab's search or the campground data refresh.
        internal DataOperationGate DataOperations { get; } = new DataOperationGate();

        // ---- the application's monitoring (v0.4.0's phase 4, D115 and D118; business-rules-gui §41) ----

        // The monitoring of this process (DesktopMonitoring: the library's MonitoringController), made once the startup has
        // prepared the settings and cache folders, and the Monitoring tab that shows it. The monitoring is the application's,
        // never the tab's: the tab only reads it and acts on it, showing another tab changes nothing, and it starts only on
        // the user's Start (or, after D62's restart, again when the old process's refresh paused it). Null until the startup
        // succeeded.
        private DesktopMonitoring? _Monitoring;
        internal DesktopMonitoring? Monitoring => _Monitoring;

        private MonitoringViewModel? _MonitoringTab;
        public MonitoringViewModel? MonitoringTab
        {
            get => _MonitoringTab;
            private set => this.RaiseAndSetIfChanged(ref _MonitoringTab, value);
        }

        private bool _ArrivalMonitoringUsed;

        // the stop the window's close began (Program.Main waits for it), or null
        internal Task? PendingMonitoringStop => _Monitoring?.Stopping;

        // ---- the window's close while the monitoring is on (D115) ----

        private bool _CloseAllowed;
        // the close may go on (the user chose to stop the monitoring and close, and it has stopped)
        internal bool CloseIsAllowed => _CloseAllowed;
        // the monitoring has stopped for the close the user chose: the window closes now
        internal event Action? CloseAllowed;

        // Whether the window's close is held back (called on the UI thread by the window's Closing): a close by the user
        // (ByTheUser: the window's own close, not Campertron's programmatic one or the operating system's shutdown) while
        // the monitoring is on asks first, in the window (IsConfirmingClose; asked again, it stays the one question), and a
        // close while that stop goes on waits for it. Never while the automatic restart after a refresh ends this process
        // (D62: the new process starts the monitoring again when the refresh paused it), and never once the close was
        // allowed
        internal bool HoldsClose(bool ByTheUser)
        {
            if (_CloseAllowed || !ByTheUser)
            {
                return false;
            }
            if (IsStoppingForClose)
            {
                return true;
            }
            if (_Monitoring == null || SystemSettings.CampgroundData?.IsRestarting == true || !_Monitoring.Controller.View().IsActive)
            {
                return false;
            }
            CloseStateText = DesktopMonitoringWording.StateText(_Monitoring.Controller.View());
            IsConfirmingClose = true;
            return true;
        }

        private bool _IsConfirmingClose;
        public bool IsConfirmingClose
        {
            get => _IsConfirmingClose;
            private set
            {
                this.RaiseAndSetIfChanged(ref _IsConfirmingClose, value);
                RefreshCloseCommands();
            }
        }

        private bool _IsStoppingForClose;
        // the monitoring is being stopped for the close: the window shows it and closes when it has stopped
        public bool IsStoppingForClose
        {
            get => _IsStoppingForClose;
            private set
            {
                this.RaiseAndSetIfChanged(ref _IsStoppingForClose, value);
                RefreshCloseCommands();
            }
        }

        private string? _CloseStateText;
        // the monitoring's state when the question was asked
        public string? CloseStateText
        {
            get => _CloseStateText;
            private set => this.RaiseAndSetIfChanged(ref _CloseStateText, value);
        }

        public RelayCommand KeepOpenCommand { get; }
        public RelayCommand StopAndCloseCommand { get; }

        // Keep Campertron open (or Escape): the question closes, nothing else changes; the monitoring goes on
        public void KeepOpen()
        {
            if (!IsConfirmingClose || IsStoppingForClose)
            {
                return;
            }
            IsConfirmingClose = false;
        }

        // Stop the monitoring and close: the application's stop of the monitoring (bounded: the controller's StopWait), on a
        // thread of its own while the window shows it, then the window closes. Once; a second request changes nothing
        public void StopAndClose()
        {
            if (!IsConfirmingClose || IsStoppingForClose || _Monitoring == null)
            {
                return;
            }
            IsStoppingForClose = true;
            Dispatcher Ui = _Ui!;
            _Monitoring.BeginShutdown().ContinueWith(_ => Ui.Post(() =>
            {
                _CloseAllowed = true;
                IsConfirmingClose = false;
                IsStoppingForClose = false;
                CloseAllowed?.Invoke();
            }), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        private void RefreshCloseCommands()
        {
            KeepOpenCommand.Refresh();
            StopAndCloseCommand.Refresh();
        }

        // Settings > System (decision D50): the GUI's theme. Unlike the other tabs it reads no settings file and no
        // database, so it exists from the start.
        public SystemSettingsViewModel SystemSettings { get; }

        private static SystemSettingsViewModel ApplicationSystemSettings()
        {
            Application? Current = Application.Current;
            if (Current is App CampertronApp)
            {
                return CampertronApp.SystemSettings ?? CampertronApp.PrepareTheme(ThemePreferenceStore.ForCurrentUser());
            }
            // the designer (no Campertron application): the store is read, nothing is applied
            ThemePreferenceStore Store = ThemePreferenceStore.ForCurrentUser();
            return new SystemSettingsViewModel(Store, Store.Load(), _ => { }, () => Current?.ActualThemeVariant);
        }

        private StartupState _State = StartupState.Preparing;
        public StartupState State
        {
            get
            {
                return _State;
            }
            private set
            {
                if (_State != value)
                {
                    this.RaiseAndSetIfChanged(ref _State, value);
                    this.RaisePropertyChanged(nameof(IsPreparing));
                    this.RaisePropertyChanged(nameof(IsReady));
                    this.RaisePropertyChanged(nameof(IsFailed));
                }
            }
        }
        public bool IsPreparing => State == StartupState.Preparing;
        public bool IsReady => State == StartupState.Ready;
        public bool IsFailed => State == StartupState.Failed;

        // Null until the startup succeeds.
        private ReactiveViewModel? _ReactiveViewModel;
        public ReactiveViewModel? ReactiveViewModel
        {
            get
            {
                return _ReactiveViewModel;
            }
            private set
            {
                this.RaiseAndSetIfChanged(ref _ReactiveViewModel, value);
            }
        }

        // The Settings tab (CM5): null until the startup succeeds, as the lookup tabs' view model.
        private SettingsViewModel? _Settings;
        public SettingsViewModel? Settings
        {
            get
            {
                return _Settings;
            }
            private set
            {
                this.RaiseAndSetIfChanged(ref _Settings, value);
            }
        }

        // The Searches tab (CM6): null until the startup succeeds; it reads the search files and the campground data
        // when the tab is first shown.
        private SearchesViewModel? _Searches;
        public SearchesViewModel? Searches
        {
            get
            {
                return _Searches;
            }
            private set
            {
                this.RaiseAndSetIfChanged(ref _Searches, value);
            }
        }

        // The Results tab (GS2): null until the startup succeeds; it searches with the folders the startup prepared.
        private ResultsViewModel? _Results;
        public ResultsViewModel? Results
        {
            get
            {
                return _Results;
            }
            private set
            {
                this.RaiseAndSetIfChanged(ref _Results, value);
            }
        }

        private string? _ErrorMessage;
        public string? ErrorMessage
        {
            get
            {
                return _ErrorMessage;
            }
            private set
            {
                this.RaiseAndSetIfChanged(ref _ErrorMessage, value);
            }
        }

        // The folder that holds General.yaml, Email.yaml and the database: Load.GetConfig's own path after a successful
        // start. Load.GetConfig returns no path when it fails, so until then this is the GUI's existing default rule.
        private string _ConfigPath = ReactiveViewModel.DefaultConfigPath;
        public string ConfigPath
        {
            get
            {
                return _ConfigPath;
            }
            private set
            {
                this.RaiseAndSetIfChanged(ref _ConfigPath, value);
            }
        }

        // The cache folder Load.GetConfig prepared (GS2: the search's campsite details), null until the startup succeeded.
        private string? _CachePath;
        public string? CachePath
        {
            get
            {
                return _CachePath;
            }
            private set
            {
                this.RaiseAndSetIfChanged(ref _CachePath, value);
            }
        }

        // Completes when the latest background attempt has finished; it never faults (the loader's exception becomes the
        // Failed state). Program.Main waits for it after the window has closed, so an extraction, repair or refresh that
        // was still running finishes as it would have before R8, when it ran before the window existed.
        public Task PendingPreparation => _PendingPreparation;

        public bool HasStarted => _Attempt > 0;

        // Called on the UI thread when the window has opened. Starts the first attempt once.
        public void Start()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_Attempt > 0 || _Detached)
            {
                return;
            }
            Begin();
        }

        // Try again after a failure. Ignored while an attempt is running, after success and after the window closed,
        // so attempts never overlap.
        public bool Retry()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_Detached || State != StartupState.Failed)
            {
                return false;
            }
            Begin();
            return true;
        }

        // The window closed: a later completion must not change anything, a search still running is canceled (GS2), and a
        // refresh of the campground data that can still stop is canceled (V011-1; one that is installing finishes).
        // Since v0.4.0's phase 4 the Monitoring tab stops reading, and the application's monitoring is stopped on a thread of
        // its own (no work of it goes on after the application, D115); Program.Main waits for that stop.
        public void Detach()
        {
            _Detached = true;
            Results?.Detach();
            SystemSettings.CampgroundData?.Detach();
            MonitoringTab?.Detach();
            _Monitoring?.BeginShutdown();
        }

        // The latest search of the Results tab (GS2); Program.Main waits a bounded time for it after the window closed.
        public Task PendingSearch => Results?.PendingRun ?? Task.CompletedTask;

        // The latest refresh of the campground data (V011-1); Program.Main waits for it after the window closed, until it
        // has stopped at a checkpoint or finished its installation. It never faults.
        internal Task<RidbRefreshResult?> PendingRefresh =>
            SystemSettings.CampgroundData?.PendingRefresh ?? Task.FromResult<RidbRefreshResult?>(null);

        private void Begin()
        {
            int Attempt = ++_Attempt;
            ErrorMessage = null;
            State = StartupState.Preparing;
            Func<CampertronInternalConfig> Loader = _LoadConfig;
            Task<(CampertronInternalConfig? Config, Exception? Error)> Work = Task.Run<(CampertronInternalConfig?, Exception?)>(() =>
            {
                try
                {
                    return (Loader(), null);
                }
                catch (Exception Ex)
                {
                    return (null, Ex);
                }
            });
            _PendingPreparation = Work;
            // The completion goes to the dispatcher of the thread that began the attempt (the UI thread: Start and Retry
            // verify it). The worker thread never asks for Dispatcher.UIThread: where no UI dispatcher exists at that moment
            // (between two headless tests, after the previous test's application was reset), the first thread that asks
            // becomes the UI thread, and the next application then can't start on its own thread.
            Dispatcher Ui = Dispatcher.UIThread;
            _Ui = Ui;
            Work.ContinueWith(Done => Ui.Post(() => Complete(Attempt, Done.Result.Config, Done.Result.Error)),
                TaskScheduler.Default);
        }

        private void Complete(int Attempt, CampertronInternalConfig? Config, Exception? Error)
        {
            if (_Detached || Attempt != _Attempt)
            {
                return;
            }
            if (Error != null || Config == null)
            {
                ErrorMessage = Describe(Error);
                State = StartupState.Failed;
                return;
            }
            // Load.GetConfig always sets ConfigPath; the property is declared nullable.
            string LoadedConfigPath = Config.ConfigPath ?? ReactiveViewModel.DefaultConfigPath;
            ConfigPath = LoadedConfigPath;
            CachePath = Config.CachePath;
            ReactiveViewModel = new ReactiveViewModel(LoadedConfigPath);
            // the test email and the test notification (D86, D88) run on a worker thread and end on the UI thread
            Settings = new SettingsViewModel(LoadedConfigPath, GuiHost.Avalonia);
            Searches = new SearchesViewModel(LoadedConfigPath, GuiHost.Avalonia);
            // the search validation's messages follow the outputTo the General editor shows, as Email.yaml's do
            SettingsViewModel CurrentSettings = Settings;
            SearchesViewModel CurrentSearches = Searches;
            // and, since D73, the saved one too: with either one Email or Ntfy, the Searches tab says when the monitoring's start
            // would have no search to check (the General form checks the search files whenever the Settings tab is shown)
            CurrentSearches.SetOutput(CurrentSettings.General.OutputToValue, CurrentSettings.General.SavedOutputToValue);
            CurrentSettings.General.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GeneralSettingsViewModel.IsEmailOutput))
                {
                    CurrentSearches.SetOutput(CurrentSettings.General.OutputToValue, CurrentSettings.General.SavedOutputToValue);
                }
            };
            // GS2: the Results tab searches the saved files with the startup's folders, shares the Searches tab's campground
            // data and doesn't search a file the Searches tab holds unsaved changes to. Since v0.4.0's phase 4 it shares the
            // five minutes after a block with the monitoring (the window's hold, as the web UI's results page and monitoring
            // share theirs)
            ResultsSeams CurrentResultsSeams = _ResultsSeams ?? new ResultsSeams();
            var Hold = new BlockHold(CurrentResultsSeams.Time);
            Results = new ResultsViewModel(LoadedConfigPath, Config.CachePath, CurrentSearches.Catalog, CurrentSearches.IsUnsaved, CurrentResultsSeams,
                DataOperations, Hold);
            // v0.4.0's phase 4 (D115, D118): the application's monitoring with the startup's folders, and the Monitoring tab. A
            // block of the monitoring holds the Results tab's Start too: the monitoring's thread posts that to this, the UI,
            // thread (captured here, never asked for on another thread)
            ResultsViewModel CurrentResults = Results;
            Dispatcher Ui = _Ui!;
            _Monitoring = new DesktopMonitoring(LoadedConfigPath, Config.CachePath, Hold, () => Ui.Post(CurrentResults.HoldStarted, DispatcherPriority.Background),
                _MonitoringSeams);
            MonitoringTab = new MonitoringViewModel(_Monitoring.Controller, _MonitoringSeams.Ticker);
            // V011-2 (D60): Settings > System's current configuration, with the startup's folders (it reads General.yaml
            // read-only and lists the settings folder; no database)
            SystemSettings.Configuration = new CurrentConfigurationViewModel(LoadedConfigPath, Config.CachePath);
            // V011-1 (D61): Settings > System's campground data, with the startup's folders. After a refresh that installed
            // new data, the current configuration shows the new date and the General editor looks at General.yaml (a clean
            // editor reloads; one with unsaved changes shows a conflict). Nothing else reloads: since D62 Campertron restarts
            // itself, and a restarted process's card shows its one-time status (once: a later Try again has none).
            CurrentConfigurationViewModel Configuration = SystemSettings.Configuration;
            RestartOutcome? ArrivalOutcome = Arrival != null && !_ArrivalStatusUsed ? Arrival.Outcome : null;
            _ArrivalStatusUsed = true;
            // Since v0.4.0's phase 4 a refresh pauses the monitoring (D83 point 6, the controller's rule) and starts it again
            // afterwards, in this process or, after D62's restart, in the new one (the handoff says whether it ran)
            bool ArrivalResumes = ArrivalOutcome != null && Arrival!.Monitoring && !_ArrivalMonitoringUsed;
            MonitoringController Controller = _Monitoring.Controller;
            var Pause = new RefreshMonitoringHooks
            {
                IsActive = () => Controller.View().IsActive,
                Pause = Controller.PauseForRefresh,
                Resume = Controller.ResumeAfterRefresh
            };
            SystemSettings.CampgroundData = new CampgroundDataViewModel(LoadedConfigPath, Config.CachePath, DataOperations, () =>
            {
                Configuration.Refresh();
                CurrentSettings.General.CheckForExternalChanges();
            }, _RefreshSeams ?? new RefreshSeams(), RestartHooks, ArrivalOutcome, Pause, ArrivalResumes);
            // D62 and D115: the process the automatic restart after a refresh started resumes the monitoring the old process's
            // refresh paused, once, as the controller resumes it after a refresh (a new start). Never on an ordinary start:
            // only a claimed and validated handoff says so, and it is never kept
            if (ArrivalResumes)
            {
                _ArrivalMonitoringUsed = true;
                Controller.ResumeAfterRefresh(true);
            }
            State = StartupState.Ready;
        }

        // The message only (with the line and column of a YAML error): no stack trace, no file contents. Long messages
        // are shortened.
        public static string Describe(Exception? Error)
        {
            Exception? Shown = Error;
            while (Shown is AggregateException || Shown is System.Reflection.TargetInvocationException)
            {
                if (Shown.InnerException == null)
                {
                    break;
                }
                Shown = Shown.InnerException;
            }
            string Message = Shown == null ? "Unknown error." : Shown.Message;
            Message = string.Join(" ", Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (Message.Length == 0)
            {
                Message = Shown?.GetType().Name ?? "Unknown error.";
            }
            // A settings file that YamlDotNet can't read: its message has no position, so add the line and column.
            if (Shown is YamlDotNet.Core.YamlException Yaml && Yaml.Start.Line > 0)
            {
                Message += " (line " + Yaml.Start.Line + ", column " + Yaml.Start.Column + ")";
            }
            return Message.Length > MaxErrorLength ? Message.Substring(0, MaxErrorLength - 1) + "…" : Message;
        }
    }
}
