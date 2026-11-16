using Avalonia.Threading;
using CampertronLibrary.function.RecDotOrg.data;
using ReactiveUI.Reactive;
using System;
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
        private int _Attempt;
        private bool _Detached;
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
        {
            _LoadConfig = LoadConfig;
            _ResultsSeams = ResultsSeams;
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

        // The window closed: a later completion must not change anything, and a search still running is canceled (GS2).
        public void Detach()
        {
            _Detached = true;
            Results?.Detach();
        }

        // The latest search of the Results tab (GS2); Program.Main waits a bounded time for it after the window closed.
        public Task PendingSearch => Results?.PendingRun ?? Task.CompletedTask;

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
            Work.ContinueWith(Done => Dispatcher.UIThread.Post(() => Complete(Attempt, Done.Result.Config, Done.Result.Error)),
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
            Settings = new SettingsViewModel(LoadedConfigPath);
            Searches = new SearchesViewModel(LoadedConfigPath);
            // the search validation's messages follow the outputTo the General editor shows, as Email.yaml's do
            SettingsViewModel CurrentSettings = Settings;
            SearchesViewModel CurrentSearches = Searches;
            CurrentSearches.SetOutput(CurrentSettings.General.OutputToValue);
            CurrentSettings.General.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GeneralSettingsViewModel.IsEmailOutput))
                {
                    CurrentSearches.SetOutput(CurrentSettings.General.OutputToValue);
                }
            };
            // GS2: the Results tab searches the saved files with the startup's folders, shares the Searches tab's campground
            // data and doesn't search a file the Searches tab holds unsaved changes to
            Results = new ResultsViewModel(LoadedConfigPath, Config.CachePath, CurrentSearches.Catalog, CurrentSearches.IsUnsaved, _ResultsSeams ?? new ResultsSeams());
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
