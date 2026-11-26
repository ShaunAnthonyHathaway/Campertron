using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.monitoring;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Campertron.ViewModels
{
    // The Monitoring tab (v0.4.0's phase 4, D115 and D118; business-rules-gui §41): the application's monitoring
    // (DesktopMonitoring, the library's MonitoringController), its state, Start, Stop and Restart with why each is
    // unavailable, why it is not running, and what its last check found, in the shared words (MonitoringWording) with the
    // desktop GUI's own (DesktopMonitoringWording). It holds no rule of the monitoring and owns none of it: it shows one
    // immutable view of the controller (View) and hands it the pressed action with the moment the tab showed (its Token),
    // so a tab that is out of date starts and stops nothing, and one action at a time is the controller's.
    //  - While the tab is shown it reads the controller's view once a second (a timer of the UI thread) and after each
    //    action; hidden, or once the window has closed, the timer is stopped. Reading only looks: it never starts, stops,
    //    repeats, resumes or cancels the monitoring, and the monitoring goes on whether the tab is shown or not.
    //  - Showing another tab never stops it; the window's close asks first (MainWindow) and the application's end stops it
    //    (DesktopMonitoring.BeginShutdown).
    //  - The last check's searches and rows are the controller's snapshot (the shared result rows); its booking page and
    //    copies only on the user's action (CampsiteActions).
    public sealed class MonitoringViewModel : ViewModelBase
    {
        internal static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

        private readonly MonitoringController _Controller;
        private readonly Func<TimeSpan, Action, IDisposable> _Ticker;
        private MonitoringControlView _View;
        private IDisposable? _Timer;
        private object? _TimerOwner;
        private bool _Shown;
        private bool _Detached;
        // the last check the tab shows the searches of (its run and number): a new check chooses its search again
        private (long Generation, int Number)? _ShownCheck;

        internal MonitoringViewModel(MonitoringController Controller, Func<TimeSpan, Action, IDisposable> Ticker)
        {
            _Controller = Controller;
            _Ticker = Ticker;
            StartCommand = new RelayCommand(() => Act(MonitoringControlAction.Start), () => CanStart);
            StopCommand = new RelayCommand(() => Act(MonitoringControlAction.Stop), () => CanStop);
            RestartCommand = new RelayCommand(() => Act(MonitoringControlAction.Restart), () => CanRestart);
            OpenBookingCommand = new RelayCommand(OpenBooking, () => SelectedRow?.HasCampsite == true);
            CopyIdCommand = new RelayCommand(CopyCampsiteId, () => SelectedRow?.HasCampsite == true);
            CopyLinkCommand = new RelayCommand(CopyBookingLink, () => SelectedRow?.HasCampsite == true);
            _View = Controller.View();
            Apply(_View);
        }

        // the moment of the monitoring the tab shows (tests)
        internal MonitoringControlView View => _View;
        internal bool IsShown => _Shown;
        internal bool IsDetached => _Detached;
        // how often the tab read the controller's view (tests: the timer runs only while the tab is shown)
        internal int Reads { get; private set; }
        internal bool TimerRunning => _Timer != null;

        // after an action the user took (the view then moves the focus to the state, as the web UI's page does)
        internal event Action? Acted;

        // ---- the tab is shown, hidden, the window closed ----

        public void Show()
        {
            if (_Detached)
            {
                return;
            }
            _Shown = true;
            Notice = null;
            Update();
            if (_Timer == null)
            {
                object Owner = new object();
                _TimerOwner = Owner;
                _Timer = _Ticker(UpdateInterval, () =>
                {
                    if (ReferenceEquals(Owner, _TimerOwner))
                    {
                        Tick();
                    }
                });
            }
        }

        public void Hide()
        {
            _Shown = false;
            StopTimer();
        }

        // the window closed: nothing changes the tab any more and its timer stops (the monitoring is the application's)
        public void Detach()
        {
            _Detached = true;
            _Shown = false;
            StopTimer();
            RefreshCommands();
        }

        private void StopTimer()
        {
            _TimerOwner = null;
            _Timer?.Dispose();
            _Timer = null;
        }

        internal void Tick()
        {
            if (_Detached || !_Shown)
            {
                StopTimer();
                return;
            }
            Update();
        }

        // the controller's view now (it only looks)
        internal void Update()
        {
            if (_Detached)
            {
                return;
            }
            Reads++;
            Apply(_Controller.View());
        }

        // ---- the actions ----

        public RelayCommand StartCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand RestartCommand { get; }

        public bool CanStart => !_Detached && _View.CanStart;
        public bool CanStop => !_Detached && _View.CanStop;
        public bool CanRestart => !_Detached && _View.CanRestart;

        // the action for the moment the tab showed; what it did when it did nothing is the tab's notice
        internal MonitoringControlOutcome? Act(MonitoringControlAction Action)
        {
            if (_Detached)
            {
                return null;
            }
            MonitoringControlOutcome Outcome = _Controller.Act(Action, _View.Token);
            Notice = Outcome == MonitoringControlOutcome.Accepted ? null : DesktopMonitoringWording.Outcome(Outcome, _Controller.View().Cooldown);
            Update();
            Acted?.Invoke();
            return Outcome;
        }

        private string? _Notice;
        // what the last action did when it did not do what was asked (a warning), or null
        public string? Notice
        {
            get => _Notice;
            private set
            {
                this.RaiseAndSetIfChanged(ref _Notice, value);
                this.RaisePropertyChanged(nameof(HasNotice));
            }
        }
        public bool HasNotice => Notice != null;

        // ---- what the tab shows ----

        private void Apply(MonitoringControlView V)
        {
            _View = V;
            MonitoringStatus S = V.Status;
            StateText = DesktopMonitoringWording.StateText(V);
            Announcement = AnnouncementOf(V);
            ShowsFacts = V.ShowsFacts;
            MonitoringControlStart? Start = V.ThisStart;
            StartedText = V.ShowsFacts ? DesktopMonitoringWording.StartedText(V) : null;
            NotificationsText = V.ShowsFacts && Start != null && Start.Started ? Start.Output : null;
            SearchesText = V.ShowsFacts ? MonitoringWording.SearchesText(S.Searches) : null;
            LastFinishedText = V.ShowsFacts ? MonitoringWording.LastFinishedText(S) : null;
            NextCheckText = V.ShowsFacts && S.NextCheckAt is DateTime Next ? MonitoringWording.When(Next) : null;
            ShowsFilesChanged = S.IsRunning && S.SearchFilesChangedSinceStart;
            FailureText = V.ShowsFailure ? string.Join(Environment.NewLine, V.ThisEnd!.Lines) : null;
            StartRefusedText = V.ShowsStartRefused ? string.Join(Environment.NewLine, Start!.Lines) : null;
            StartRefusedSeverity = V.ShowsStartRefused && Start!.Problem == MonitoringStartProblem.NotMonitoring ? MessageSeverity.Info : MessageSeverity.Error;
            CooldownText = V.Cooldown;
            UnavailableText = DesktopMonitoringWording.Unavailable(V);
            ApplyCheck(V);
            RefreshCommands();
        }

        // the state of a moment whose change is announced: an action or an end under way, running (or a stop asked for),
        // paused for the refresh, or ended (how), with the start it belongs to. A check's own progress is not part of it
        internal static string AnnouncementOf(MonitoringControlView V)
        {
            string State = V.ShuttingDown ? "closing"
                : V.Busy != MonitoringControlBusy.None ? V.Busy.ToString()
                : V.Status.IsRunning ? (V.Status.StopRequested ? "stopping" : "running")
                : V.Paused && V.Resumes ? "paused"
                : V.ShowsFailure ? "failed"
                : V.ShowsStartRefused ? "refused"
                : V.ThisEnd != null ? "stopped"
                : "idle";
            return State + "-" + V.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private string _StateText = string.Empty;
        // the state in words (never by color alone)
        public string StateText
        {
            get => _StateText;
            private set => this.RaiseAndSetIfChanged(ref _StateText, value);
        }

        private string _Announcement = string.Empty;
        public string Announcement
        {
            get => _Announcement;
            private set => this.RaiseAndSetIfChanged(ref _Announcement, value);
        }

        private bool _ShowsFacts;
        public bool ShowsFacts
        {
            get => _ShowsFacts;
            private set => this.RaiseAndSetIfChanged(ref _ShowsFacts, value);
        }

        private string? _StartedText;
        public string? StartedText
        {
            get => _StartedText;
            private set => this.RaiseAndSetIfChanged(ref _StartedText, value);
        }

        private string? _NotificationsText;
        public string? NotificationsText
        {
            get => _NotificationsText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _NotificationsText, value);
                this.RaisePropertyChanged(nameof(HasNotifications));
            }
        }
        public bool HasNotifications => NotificationsText != null;

        private string? _SearchesText;
        public string? SearchesText
        {
            get => _SearchesText;
            private set => this.RaiseAndSetIfChanged(ref _SearchesText, value);
        }

        private string? _LastFinishedText;
        public string? LastFinishedText
        {
            get => _LastFinishedText;
            private set => this.RaiseAndSetIfChanged(ref _LastFinishedText, value);
        }

        private string? _NextCheckText;
        public string? NextCheckText
        {
            get => _NextCheckText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _NextCheckText, value);
                this.RaisePropertyChanged(nameof(HasNextCheck));
            }
        }
        public bool HasNextCheck => NextCheckText != null;

        private bool _ShowsFilesChanged;
        // the search files changed since the monitoring started (a warning)
        public bool ShowsFilesChanged
        {
            get => _ShowsFilesChanged;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ShowsFilesChanged, value);
                this.RaisePropertyChanged(nameof(FilesChangedText));
            }
        }
        public string? FilesChangedText => ShowsFilesChanged ? MonitoringWording.FilesChanged : null;

        private string? _FailureText;
        // why it stopped when a check failed, in fixed words (an error)
        public string? FailureText
        {
            get => _FailureText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _FailureText, value);
                this.RaisePropertyChanged(nameof(ShowsFailure));
            }
        }
        public bool ShowsFailure => FailureText != null;

        private string? _StartRefusedText;
        // why the last start did not start it, in the start's own words (an error; information when no monitoring is
        // turned on in General.yaml)
        public string? StartRefusedText
        {
            get => _StartRefusedText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _StartRefusedText, value);
                this.RaisePropertyChanged(nameof(ShowsStartRefused));
            }
        }
        public bool ShowsStartRefused => StartRefusedText != null;

        private string _StartRefusedSeverity = MessageSeverity.Error;
        public string StartRefusedSeverity
        {
            get => _StartRefusedSeverity;
            private set => this.RaiseAndSetIfChanged(ref _StartRefusedSeverity, value);
        }

        private string? _CooldownText;
        // the five minutes after a block, with the time left
        public string? CooldownText
        {
            get => _CooldownText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _CooldownText, value);
                this.RaisePropertyChanged(nameof(HasCooldown));
            }
        }
        public bool HasCooldown => CooldownText != null;

        private string _UnavailableText = string.Empty;
        // why the actions that can't be used now can't (a disabled button says nothing by itself)
        public string UnavailableText
        {
            get => _UnavailableText;
            private set => this.RaiseAndSetIfChanged(ref _UnavailableText, value);
        }

        // ---- the last check ----

        private void ApplyCheck(MonitoringControlView V)
        {
            MonitoringControlCheck? Check = V.LastCheck;
            CheckSummary = Check == null ? null : MonitoringWording.CheckSummary(Check, !V.Status.IsRunning && V.Busy == MonitoringControlBusy.None);
            if (Check == null)
            {
                _ShownCheck = null;
                CheckSearches = Array.Empty<ResultSearchViewModel>();
                SelectedSearch = null;
                return;
            }
            if (_ShownCheck != (Check.Generation, Check.Number))
            {
                _ShownCheck = (Check.Generation, Check.Number);
                CheckSearches = Check.Searches;
                // the first search with openings, as the web UI's page shows first
                SelectedSearch = Check.Searches.FirstOrDefault(S => S.Openings > 0) ?? Check.Searches.FirstOrDefault();
            }
        }

        private string? _CheckSummary;
        public string? CheckSummary
        {
            get => _CheckSummary;
            private set
            {
                this.RaiseAndSetIfChanged(ref _CheckSummary, value);
                this.RaisePropertyChanged(nameof(HasCheck));
                this.RaisePropertyChanged(nameof(HasNoCheck));
            }
        }
        public bool HasCheck => CheckSummary != null;
        public bool HasNoCheck => CheckSummary == null;

        private IReadOnlyList<ResultSearchViewModel> _CheckSearches = Array.Empty<ResultSearchViewModel>();
        public IReadOnlyList<ResultSearchViewModel> CheckSearches
        {
            get => _CheckSearches;
            private set
            {
                this.RaiseAndSetIfChanged(ref _CheckSearches, value);
                this.RaisePropertyChanged(nameof(HasCheckSearches));
            }
        }
        public bool HasCheckSearches => CheckSearches.Count > 0;

        private ResultSearchViewModel? _SelectedSearch;
        // the search of the last check shown, and its rows (built once by the controller, only read here)
        public ResultSearchViewModel? SelectedSearch
        {
            get => _SelectedSearch;
            set
            {
                if (ReferenceEquals(_SelectedSearch, value))
                {
                    return;
                }
                this.RaiseAndSetIfChanged(ref _SelectedSearch, value);
                SelectedRow = null;
                ActionMessage = null;
                this.RaisePropertyChanged(nameof(SelectedRows));
                this.RaisePropertyChanged(nameof(SelectedCountsText));
                this.RaisePropertyChanged(nameof(SelectedEmptyText));
                this.RaisePropertyChanged(nameof(HasSelectedEmptyText));
                this.RaisePropertyChanged(nameof(HasSelectedRows));
            }
        }
        public IReadOnlyList<ResultRow> SelectedRows => SelectedSearch?.Rows ?? Array.Empty<ResultRow>();
        public bool HasSelectedRows => SelectedRows.Count > 0;
        public string? SelectedCountsText => SelectedSearch != null && SelectedSearch.Openings > 0 ? SelectedSearch.CountsText : null;
        // a search without new openings at this check
        public string? SelectedEmptyText => SelectedSearch != null && SelectedSearch.Openings == 0 ? MonitoringWording.NoNewOpening : null;
        public bool HasSelectedEmptyText => SelectedEmptyText != null;

        // ---- the selected campsite: its booking page and copies (only on the user's action) ----

        public RelayCommand OpenBookingCommand { get; }
        public RelayCommand CopyIdCommand { get; }
        public RelayCommand CopyLinkCommand { get; }
        // set by the view: the platform's launcher (Avalonia's ILauncher, never a shell) and the clipboard
        public Func<Uri, Task<bool>>? LinkLauncher { get; set; }
        public Func<string, Task<bool>>? CopyText { get; set; }

        private ResultRow? _SelectedRow;
        public ResultRow? SelectedRow
        {
            get => _SelectedRow;
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedRow, value);
                ActionMessage = null;
                this.RaisePropertyChanged(nameof(SelectedCampsiteText));
                OpenBookingCommand.Refresh();
                CopyIdCommand.Refresh();
                CopyLinkCommand.Refresh();
            }
        }
        public string SelectedCampsiteText => ResultsWording.SelectedCampsite(SelectedRow);

        private string? _ActionMessage;
        public string? ActionMessage
        {
            get => _ActionMessage;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ActionMessage, value);
                this.RaisePropertyChanged(nameof(HasActionMessage));
            }
        }
        public bool HasActionMessage => ActionMessage != null;
        private bool _ActionIsError;
        public bool ActionIsError
        {
            get => _ActionIsError;
            private set => this.RaiseAndSetIfChanged(ref _ActionIsError, value);
        }

        public async void OpenBooking() => await OpenBookingAsync();
        public async void CopyCampsiteId() => await CopyCampsiteIdAsync();
        public async void CopyBookingLink() => await CopyBookingLinkAsync();

        internal async Task OpenBookingAsync()
        {
            if (SelectedRow is ResultRow Row && Row.CampsiteId != null)
            {
                Report(Row, await CampsiteActions.OpenBookingAsync(Row, LinkLauncher));
            }
        }

        internal async Task CopyCampsiteIdAsync()
        {
            if (SelectedRow is ResultRow Row && Row.CampsiteId != null)
            {
                Report(Row, await CampsiteActions.CopyIdAsync(Row, CopyText));
            }
        }

        internal async Task CopyBookingLinkAsync()
        {
            if (SelectedRow is ResultRow Row && Row.CampsiteId != null)
            {
                Report(Row, await CampsiteActions.CopyLinkAsync(Row, CopyText));
            }
        }

        // a message about the row it was for (the selection may have changed meanwhile)
        private void Report(ResultRow Row, (bool Succeeded, string Message) Done)
        {
            if (!ReferenceEquals(Row, SelectedRow) || _Detached)
            {
                return;
            }
            ActionMessage = Done.Message;
            ActionIsError = !Done.Succeeded;
        }

        private void RefreshCommands()
        {
            StartCommand.Refresh();
            StopCommand.Refresh();
            RestartCommand.Refresh();
            this.RaisePropertyChanged(nameof(CanStart));
            this.RaisePropertyChanged(nameof(CanStop));
            this.RaisePropertyChanged(nameof(CanRestart));
        }
    }
}
