using CampertronLibrary.function.Forms;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Campertron.ViewModels
{
    // where the guided setup sends the user in the window (the window's own tabs)
    public enum GuidedSetupPlace
    {
        MonitoringStatus,
        SettingsSystem,
        Searches,
    }

    // one step in the list of steps: its name, its state in words and a button that opens it
    public sealed class GuidedStepItem
    {
        internal GuidedStepItem(GuidedSetupStep Step, string Name, string StateText, bool IsCurrent, bool CanOpen, Action Open)
        {
            this.Step = Step;
            this.Name = Name;
            this.StateText = StateText;
            this.IsCurrent = IsCurrent;
            Label = GuidedSetupWording.StepNumber(Step).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " + Name;
            StateLabel = "(" + StateText + ")";
            AccessibleName = GuidedSetupWording.StepOf(Step) + ": " + Name + ", " + StateText;
            OpenCommand = new RelayCommand(Open, () => CanOpen);
        }

        public GuidedSetupStep Step { get; }
        public string Name { get; }
        public string StateText { get; }
        public bool IsCurrent { get; }
        public string Label { get; }
        public string StateLabel { get; }
        public string AccessibleName { get; }
        public RelayCommand OpenCommand { get; }
    }

    // a search file of the search step, with its button that sets it up here
    public sealed class GuidedSearchItem
    {
        internal GuidedSearchItem(SearchFileViewModel File, Action SetUp)
        {
            this.File = File;
            ActionName = GuidedSetupWording.SetUpThisSearch + ": " + File.FileName;
            SetUpCommand = new RelayCommand(SetUp);
        }

        public SearchFileViewModel File { get; }
        public string ActionName { get; }
        public RelayCommand SetUpCommand { get; }
    }

    // The desktop GUI's guided setup (v0.4.0's phase 5; business-rules-gui §42), in the Monitoring tab beside its Status: the
    // host of the shared form layer's GuidedSetupFlow, as the web UI's guided setup page is (D100, D117). It keeps nothing of
    // its own and decides no step:
    //  - Where the user is comes from the saved files only (GuidedSetupStatus), read again whenever the guided setup is shown,
    //    the window is activated while it is shown, and after each action (the flow's outcome carries what it read). The step
    //    shown is kept in memory only, as a browser keeps the address it shows: nothing is written, recorded or marked.
    //  - The step order, a step's state, Back, the steps not done, where it resumes and what step 1's choice, Save and continue
    //    of the notification settings and of a search, New search and Turn the monitoring on do are the flow's: each action
    //    hands the flow the window's own forms (the Settings tab's SettingsViewModel, the Searches tab's SearchesViewModel,
    //    never a second session of a file) and shows its GuidedSetupOutcome: the step it leads to, what it says, what needs
    //    the user's attention (the view gives it the focus).
    //  - Its steps show the Settings and Searches tabs' own editors of those forms (GuidedEditor), with their validation,
    //    preview, conflict, Revert, Reload, test email and notification and random topic; their Save is the guided setup's
    //    Save and continue, which saves only through the form's own Save. An unsaved change of a form is the same in both
    //    places; leaving the guided setup saves, discards and marks nothing.
    //  - Turning the monitoring on saves outputTo through the General form and starts nothing; the done step's Start the
    //    monitoring is the application's monitoring's own Start (DesktopMonitoring's MonitoringController, for the moment the
    //    step showed), on the user's choice only (D115). Leaving or completing the guided setup never starts or stops it.
    //  - The campground data: a search step that can't read it says so and leads to Settings > System, whose card refreshes
    //    it (V011-1, D61, D62); the guided setup never refreshes, searches or sends anything itself.
    // Its desktop words are DesktopGuidedSetupWording's (Host: its sentence of the orchestration).
    public sealed class GuidedSetupViewModel : ViewModelBase
    {
        private readonly string _ConfigPath;
        private readonly SettingsViewModel _Settings;
        private readonly SearchesViewModel _Searches;
        private readonly MonitoringController _Monitoring;
        private bool _Resumed;
        private bool _Detached;
        private SettingsFileViewModel? _Observed;
        private MonitoringControlView _MonitoringView;

        internal GuidedSetupViewModel(string ConfigPath, SettingsViewModel Settings, SearchesViewModel Searches, MonitoringController Monitoring)
        {
            _ConfigPath = ConfigPath;
            _Settings = Settings;
            _Searches = Searches;
            _Monitoring = Monitoring;
            _Status = Read();
            _MonitoringView = Monitoring.View();
            ContinueCommand = new RelayCommand(Continue, () => !_Detached && Step == GuidedSetupStep.Method);
            BackCommand = new RelayCommand(Back, () => !_Detached && GuidedSetupFlow.Previous(Step) != null);
            SaveNotificationsCommand = new RelayCommand(SaveNotifications, () => !_Detached && Step == GuidedSetupStep.Notifications && Method != null);
            NewSearchCommand = new RelayCommand(NewSearch, () => !_Detached && Step == GuidedSetupStep.Search && Search == null);
            SaveSearchCommand = new RelayCommand(SaveSearch, () => !_Detached && Step == GuidedSetupStep.Search && Search != null && Search.IsEditable);
            BackToSearchesCommand = new RelayCommand(BackToSearches, () => !_Detached && Step == GuidedSetupStep.Search && Search != null);
            SearchContinueCommand = new RelayCommand(() => GoTo(GuidedSetupStep.TurnOn), () => !_Detached && Step == GuidedSetupStep.Search && Search == null && Status.SearchReady);
            TurnOnCommand = new RelayCommand(TurnOn, () => !_Detached && Step == GuidedSetupStep.TurnOn && Method is GuidedSetupMethod M && Status.FilesReady(M));
            DoneContinueCommand = new RelayCommand(() => GoTo(GuidedSetupStep.Done), () => !_Detached && Step == GuidedSetupStep.TurnOn && ShowsDoneContinue);
            StartMonitoringCommand = new RelayCommand(StartMonitoring, () => !_Detached && Step == GuidedSetupStep.Done && IsComplete && _MonitoringView.CanStart);
            ShowStatusCommand = new RelayCommand(() => Navigate?.Invoke(GuidedSetupPlace.MonitoringStatus), () => !_Detached);
            ShowSearchesCommand = new RelayCommand(ShowSearches, () => !_Detached && Search != null);
            ShowSystemCommand = new RelayCommand(() => Navigate?.Invoke(GuidedSetupPlace.SettingsSystem), () => !_Detached);
            GoToNotificationsCommand = new RelayCommand(() => GoTo(GuidedSetupStep.Notifications), () => !_Detached && Method != null);
            GoToSearchCommand = new RelayCommand(() => GoTo(GuidedSetupStep.Search), () => !_Detached && Method != null);
            Apply();
        }

        // ---- the window ----

        // the guided setup asks the window to show one of its tabs
        internal event Action<GuidedSetupPlace>? Navigate;
        // after an action: what needs the user's attention (the view gives that element the focus)
        internal event Action<GuidedSetupAttention>? AttentionRequested;

        internal bool IsShown { get; private set; }
        internal bool IsDetached => _Detached;

        // The guided setup is shown (its tab, on either level): the first time it resumes where the saved files say (the
        // web UI's start page's link: GuidedSetupFlow.ResumeMethod and the method's first step not done, else step 1), later
        // it shows the step it showed, as it is now (the saved files read again, the step's forms compared with the disk, as
        // the Settings and Searches tabs do when they are shown). What an earlier action said is not said again
        public void Show()
        {
            if (_Detached)
            {
                return;
            }
            IsShown = true;
            Notice = null;
            CreatedText = null;
            MonitoringNotice = null;
            if (!_Resumed)
            {
                _Resumed = true;
                Status = Read();
                if (GuidedSetupFlow.ResumeMethod(Status) is GuidedSetupMethod Resume)
                {
                    Method = Resume;
                    Enter(Status.NextStep(Resume));
                    return;
                }
                Enter(GuidedSetupStep.Method);
                return;
            }
            Refresh();
        }

        // another tab is shown: a revealed password is masked again (CM5's rule), nothing else changes
        public void Hide()
        {
            if (!IsShown)
            {
                return;
            }
            IsShown = false;
            _Settings.HideSecrets();
        }

        // the window was activated while the guided setup is shown: the files read again (another program may have changed
        // them), without what an action said being taken away
        public void Activated()
        {
            if (IsShown && !_Detached)
            {
                Refresh();
            }
        }

        // the window closed: nothing changes the guided setup any more
        public void Detach()
        {
            _Detached = true;
            IsShown = false;
            Observe(null);
            RefreshCommands();
        }

        // the saved files as the step shows them now, and the step's preparation (what the web UI's GET of the step does)
        private void Refresh()
        {
            Prepare();
            Status = Read();
            Apply();
        }

        // ---- where the user is ----

        private GuidedSetupStatus _Status;
        public GuidedSetupStatus Status
        {
            get => _Status;
            private set => this.RaiseAndSetIfChanged(ref _Status, value);
        }

        private GuidedSetupStep _Step = GuidedSetupStep.Method;
        public GuidedSetupStep Step
        {
            get => _Step;
            private set => this.RaiseAndSetIfChanged(ref _Step, value);
        }

        private GuidedSetupMethod? _Method;
        // the method the user chose on step 1 (or the guided setup resumed with); null until then
        public GuidedSetupMethod? Method
        {
            get => _Method;
            private set => this.RaiseAndSetIfChanged(ref _Method, value);
        }

        // whether nothing of the guided setup is saved (the window's lead, once per start)
        internal bool IsUnconfigured => Status.IsUnconfigured;

        private string _Heading = string.Empty;
        // "Step N of 4: its name", or the done step's heading (the shared words)
        public string Heading
        {
            get => _Heading;
            private set => this.RaiseAndSetIfChanged(ref _Heading, value);
        }

        private string _ReadinessText = string.Empty;
        // what the saved files say of the guided setup, as the web UI's start page says it (the shared words)
        public string ReadinessText
        {
            get => _ReadinessText;
            private set => this.RaiseAndSetIfChanged(ref _ReadinessText, value);
        }

        private IReadOnlyList<GuidedStepItem> _Steps = Array.Empty<GuidedStepItem>();
        // the steps in their order, each with its state in words (GuidedSetupFlow.Steps, StateOf)
        public IReadOnlyList<GuidedStepItem> Steps
        {
            get => _Steps;
            private set => this.RaiseAndSetIfChanged(ref _Steps, value);
        }

        public bool IsMethodStep => Step == GuidedSetupStep.Method;
        public bool IsNotificationsStep => Step == GuidedSetupStep.Notifications;
        public bool IsSearchListStep => Step == GuidedSetupStep.Search && Search == null;
        public bool IsSearchStep => Step == GuidedSetupStep.Search && Search != null;
        public bool IsTurnOnStep => Step == GuidedSetupStep.TurnOn;
        public bool IsDoneStep => Step == GuidedSetupStep.Done;
        public bool HasBack => GuidedSetupFlow.Previous(Step) != null;

        private string? _Notice;
        // what the last action said (its severity's word first), or null
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

        // ---- step 1: how you are notified ----

        private bool _IsEmailChosen;
        public bool IsEmailChosen
        {
            get => _IsEmailChosen;
            set
            {
                this.RaiseAndSetIfChanged(ref _IsEmailChosen, value);
                if (value)
                {
                    IsNtfyChosen = false;
                    ChoiceError = null;
                }
            }
        }

        private bool _IsNtfyChosen;
        public bool IsNtfyChosen
        {
            get => _IsNtfyChosen;
            set
            {
                this.RaiseAndSetIfChanged(ref _IsNtfyChosen, value);
                if (value)
                {
                    IsEmailChosen = false;
                    ChoiceError = null;
                }
            }
        }

        private GuidedSetupMethod? Picked => IsEmailChosen ? GuidedSetupMethod.Email : IsNtfyChosen ? GuidedSetupMethod.Ntfy : null;

        private string? _ChoiceError;
        // nothing chosen, in the shared words
        public string? ChoiceError
        {
            get => _ChoiceError;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ChoiceError, value);
                this.RaisePropertyChanged(nameof(HasChoiceError));
            }
        }
        public bool HasChoiceError => ChoiceError != null;

        public RelayCommand ContinueCommand { get; }

        // Continue: the flow's choice (it saves nothing: the method chosen only leads to its first step not done)
        private void Continue()
        {
            GuidedSetupOutcome Outcome = GuidedSetupFlow.Choose(Picked, Read());
            if (Outcome.Next != null && Picked is GuidedSetupMethod Chose)
            {
                Method = Chose;
                Show(Outcome);
                return;
            }
            // nothing chosen: the step says so at the choice (the web UI's step 1 says it in its error summary)
            Status = Outcome.Status ?? Status;
            ChoiceError = Outcome.Notice;
            Apply();
            AttentionRequested?.Invoke(Outcome.Attention);
        }

        // ---- step 2: the notification settings ----

        // the window's own forms of Email.yaml and Ntfy.yaml (the Settings tab's)
        public EmailSettingsViewModel Email => _Settings.Email;
        public NtfySettingsViewModel Ntfy => _Settings.Ntfy;
        public bool IsEmailMethod => Method == GuidedSetupMethod.Email;
        public bool IsNtfyMethod => Method == GuidedSetupMethod.Ntfy;
        // the form of the method's file
        internal SettingsFileViewModel? Form => Step == GuidedSetupStep.Notifications ? Method switch
        {
            GuidedSetupMethod.Email => _Settings.Email,
            GuidedSetupMethod.Ntfy => _Settings.Ntfy,
            _ => null
        } : null;

        public string StepHelp => Method == GuidedSetupMethod.Ntfy ? GuidedSetupWording.NtfyHelp : GuidedSetupWording.EmailHelp;

        private string? _NeedsText;
        // what the monitoring's start would refuse in the method's saved file, or null once it is ready
        public string? NeedsText
        {
            get => _NeedsText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _NeedsText, value);
                this.RaisePropertyChanged(nameof(HasNeeds));
            }
        }
        public bool HasNeeds => NeedsText != null;

        public RelayCommand SaveNotificationsCommand { get; }

        // Save and continue: the flow saves what changed through the form's own Save and goes on once the saved file is ready
        private void SaveNotifications()
        {
            if (Form is not SettingsFileViewModel Editor || Method is not GuidedSetupMethod Known)
            {
                return;
            }
            Show(GuidedSetupFlow.ContinueNotifications(Editor, Known, _ConfigPath));
        }

        // ---- the form's problems (step 2's form or step 3's search) ----

        private string? _ProblemsText;
        // every problem of the step's form, in the form's order and words (FormProblems), one a line; null when none
        public string? ProblemsText
        {
            get => _ProblemsText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ProblemsText, value);
                this.RaisePropertyChanged(nameof(HasProblems));
            }
        }
        public bool HasProblems => ProblemsText != null;

        // ---- step 3: a search ----

        private IReadOnlyList<GuidedSearchItem> _SearchItems = Array.Empty<GuidedSearchItem>();
        // the search files with their state, each with Set up this search here (the Searches tab's own list)
        public IReadOnlyList<GuidedSearchItem> SearchItems
        {
            get => _SearchItems;
            private set
            {
                this.RaiseAndSetIfChanged(ref _SearchItems, value);
                this.RaisePropertyChanged(nameof(HasSearchItems));
                this.RaisePropertyChanged(nameof(HasNoSearchFiles));
            }
        }
        public bool HasSearchItems => SearchItems.Count > 0;
        // at least one enabled search can be checked (Continue goes on to the review)
        public bool SearchReady => Status.SearchReady;
        public bool HasNoSearchFiles => SearchItems.Count == 0;

        // the window's own Searches form: New search's name and message are the Searches tab's
        public SearchesViewModel Searches => _Searches;

        private string _SearchStateText = string.Empty;
        // whether a search is ready, and what the monitoring's start would find in the search files
        public string SearchStateText
        {
            get => _SearchStateText;
            private set => this.RaiseAndSetIfChanged(ref _SearchStateText, value);
        }

        private SearchFileViewModel? _Search;
        // the search whose main settings the step shows (null: the list)
        public SearchFileViewModel? Search
        {
            get => _Search;
            private set
            {
                if (ReferenceEquals(_Search, value))
                {
                    return;
                }
                this.RaiseAndSetIfChanged(ref _Search, value);
                this.RaisePropertyChanged(nameof(HasSearch));
            }
        }
        public bool HasSearch => Search != null;

        private string? _CreatedText;
        // New search's message on the new search's own page, once
        public string? CreatedText
        {
            get => _CreatedText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _CreatedText, value);
                this.RaisePropertyChanged(nameof(HasCreated));
            }
        }
        public bool HasCreated => CreatedText != null;

        private bool _HasOtherChanges;
        // the search also has unsaved changes of settings the step does not show (the Searches tab's)
        public bool HasOtherChanges
        {
            get => _HasOtherChanges;
            private set => this.RaiseAndSetIfChanged(ref _HasOtherChanges, value);
        }

        private bool _HasCampgroundDataProblem;
        // the campground data can't be read: Settings > System refreshes it
        public bool HasCampgroundDataProblem
        {
            get => _HasCampgroundDataProblem;
            private set => this.RaiseAndSetIfChanged(ref _HasCampgroundDataProblem, value);
        }

        public RelayCommand NewSearchCommand { get; }
        public RelayCommand SaveSearchCommand { get; }
        public RelayCommand BackToSearchesCommand { get; }
        public RelayCommand SearchContinueCommand { get; }
        public RelayCommand ShowSearchesCommand { get; }
        public RelayCommand ShowSystemCommand { get; }

        // New search: the Searches tab's own command, through the flow; a new file opens its own page of the step
        private void NewSearch()
        {
            Show(GuidedSetupFlow.NewSearch(_Searches));
        }

        // Set up this search here: its main settings (the Searches tab's editor of it shows the same form)
        private void SetUpSearch(SearchFileViewModel File)
        {
            if (_Detached || Step != GuidedSetupStep.Search)
            {
                return;
            }
            Notice = null;
            CreatedText = null;
            Search = File;
            _Searches.SelectedFile = File;
            Status = Read();
            Apply();
            AttentionRequested?.Invoke(GuidedSetupAttention.Step);
        }

        // Save and continue of the search: the flow saves through the form's own Save and goes on once a search is ready
        private void SaveSearch()
        {
            if (Search is SearchFileViewModel File)
            {
                Show(GuidedSetupFlow.ContinueSearch(File, _ConfigPath));
            }
        }

        private void BackToSearches()
        {
            Notice = null;
            CreatedText = null;
            Search = null;
            Refresh();
            AttentionRequested?.Invoke(GuidedSetupAttention.Step);
        }

        // the search's other settings: the Searches tab, with this search chosen
        private void ShowSearches()
        {
            if (Search is SearchFileViewModel File)
            {
                _Searches.SelectedFile = File;
                Navigate?.Invoke(GuidedSetupPlace.Searches);
            }
        }

        // ---- step 4: review and turn on ----

        public string TurnOnHelp => Method is GuidedSetupMethod M ? DesktopGuidedSetupWording.TurnOnHelp(M) : string.Empty;
        public string TurnOnLabel => Method is GuidedSetupMethod M ? GuidedSetupWording.TurnOnLabel(M) : string.Empty;
        public string NotificationsItemText => Method is GuidedSetupMethod M ? GuidedSetupWording.NotificationsItem(M, Status.NotificationsReady(M)) : string.Empty;
        public bool NotificationsNotReady => Method is GuidedSetupMethod M && !Status.NotificationsReady(M);
        public string NotificationsStepName => GuidedSetupWording.StepName(GuidedSetupStep.Notifications, Method);
        public string SearchItemText => GuidedSetupWording.SearchItem(Status.SearchReady);
        public bool SearchNotReady => !Status.SearchReady;
        public string SearchStepName => GuidedSetupWording.StepName(GuidedSetupStep.Search, Method);
        public string OutputItemText => Method is GuidedSetupMethod M ? GuidedSetupWording.OutputItem(M, Status.SavedOutput) : string.Empty;
        // everything the monitoring's start would refuse in the saved files, under the shared heading; null when nothing
        public string? RefusedText => Method is GuidedSetupMethod M && Status.Problems(M).Count > 0
            ? GuidedSetupWording.FilesRefused + Environment.NewLine + string.Join(Environment.NewLine, Status.Problems(M)) : null;
        public bool HasRefused => RefusedText != null;
        public string? SecretWarningsText => Method is GuidedSetupMethod M && Status.SecretFileWarnings(M).Count > 0
            ? string.Join(Environment.NewLine, Status.SecretFileWarnings(M).Select(W => "Warning: " + W)) : null;
        public bool HasSecretWarnings => SecretWarningsText != null;
        public bool FilesReady => Method is GuidedSetupMethod M && Status.FilesReady(M);
        // outputTo is already the method and the files pass: Continue to the done step instead of Turn on
        public bool ShowsDoneContinue => Method is GuidedSetupMethod M && Status.IsOn(M) && Status.FilesReady(M);
        public bool ShowsTurnOn => !ShowsDoneContinue;
        public bool ShowsNoGeneralYet => ShowsTurnOn && !Status.GeneralExists;
        public string? TurnOnUnavailableText => ShowsTurnOn && !FilesReady ? GuidedSetupWording.TurnOnNotReady.Substring("Error: ".Length) : null;
        public bool HasTurnOnUnavailable => TurnOnUnavailableText != null;

        public RelayCommand TurnOnCommand { get; }
        public RelayCommand DoneContinueCommand { get; }
        public RelayCommand GoToNotificationsCommand { get; }
        public RelayCommand GoToSearchCommand { get; }

        // Turn the monitoring on: the General form looks at its file first (as the Settings tab does when it is shown), then
        // the flow saves outputTo through the General form, only when the saved files pass and General.yaml has no unsaved
        // change, and starts nothing
        private void TurnOn()
        {
            if (Method is not GuidedSetupMethod Known)
            {
                return;
            }
            _Settings.CheckForExternalChanges();
            GuidedSetupStatus Now = Read();
            Status = Now;
            Show(GuidedSetupFlow.TurnOn(_Settings.General, Known, Now, _ConfigPath, DesktopGuidedSetupWording.Host));
        }

        // ---- done ----

        public bool IsComplete => Method is GuidedSetupMethod M && Status.State(M) == GuidedSetupState.Complete;
        public bool IsNotComplete => !IsComplete;
        public string DoneText => Method is GuidedSetupMethod M ? DesktopGuidedSetupWording.DoneText(M) : string.Empty;

        private string _MonitoringStateText = string.Empty;
        // the application's monitoring as the step read it, in the Monitoring tab's words
        public string MonitoringStateText
        {
            get => _MonitoringStateText;
            private set => this.RaiseAndSetIfChanged(ref _MonitoringStateText, value);
        }

        public bool ShowsMonitoringChanged => _MonitoringView.Status.IsRunning && _MonitoringView.Status.SearchFilesChangedSinceStart;
        public bool CanStartMonitoring => IsComplete && _MonitoringView.CanStart && !_Detached;

        private string? _MonitoringNotice;
        // what Start the monitoring did when it did not start it (the Monitoring tab's words), or null
        public string? MonitoringNotice
        {
            get => _MonitoringNotice;
            private set
            {
                this.RaiseAndSetIfChanged(ref _MonitoringNotice, value);
                this.RaisePropertyChanged(nameof(HasMonitoringNotice));
            }
        }
        public bool HasMonitoringNotice => MonitoringNotice != null;

        private IReadOnlyList<GuidedStepItem> _ToDoItems = Array.Empty<GuidedStepItem>();
        // the steps not done for the method, while the guided setup is not finished (GuidedSetupFlow.ToDo)
        public IReadOnlyList<GuidedStepItem> ToDoItems
        {
            get => _ToDoItems;
            private set => this.RaiseAndSetIfChanged(ref _ToDoItems, value);
        }

        public RelayCommand StartMonitoringCommand { get; }
        public RelayCommand ShowStatusCommand { get; }

        // the moment of the monitoring the done step shows (tests)
        internal MonitoringControlView MonitoringView => _MonitoringView;

        // Start the monitoring: the application's monitoring's own Start, for the moment the step showed (D115: only on the
        // user's choice). Started, the monitoring's Status shows how it goes, as the web UI's done page leads to its monitoring
        // page; a start the controller did not take says why here
        private void StartMonitoring()
        {
            MonitoringControlOutcome Outcome = _Monitoring.Act(MonitoringControlAction.Start, _MonitoringView.Token);
            _MonitoringView = _Monitoring.View();
            if (Outcome == MonitoringControlOutcome.Accepted)
            {
                MonitoringNotice = null;
                Apply();
                Navigate?.Invoke(GuidedSetupPlace.MonitoringStatus);
                return;
            }
            MonitoringNotice = DesktopMonitoringWording.Outcome(Outcome, _MonitoringView.Cooldown);
            Apply();
            AttentionRequested?.Invoke(GuidedSetupAttention.Notice);
        }

        // ---- moving between the steps ----

        public RelayCommand BackCommand { get; }

        // Back: the flow's step before this one; nothing is saved or discarded
        private void Back()
        {
            if (GuidedSetupFlow.Previous(Step) is GuidedSetupStep Previous)
            {
                GoTo(Previous);
            }
        }

        // a step the user opened (the list of steps, Back, a review's link): as it is now, with the step's heading in focus
        private void GoTo(GuidedSetupStep Next)
        {
            if (_Detached || (Next != GuidedSetupStep.Method && Method == null))
            {
                return;
            }
            Notice = null;
            CreatedText = null;
            MonitoringNotice = null;
            Status = Read();
            Enter(Next);
            AttentionRequested?.Invoke(GuidedSetupAttention.Step);
        }

        // a step is shown: what it shows is prepared as the web UI's GET of it prepares it
        private void Enter(GuidedSetupStep Next)
        {
            if (Step == GuidedSetupStep.Notifications && Next != GuidedSetupStep.Notifications)
            {
                // a revealed password is masked again when its editor is left (CM5's rule)
                _Settings.HideSecrets();
            }
            Step = Next;
            if (Next != GuidedSetupStep.Search)
            {
                Search = null;
            }
            if (Next == GuidedSetupStep.Method)
            {
                ChoiceError = null;
                GuidedSetupMethod? Checked = Method ?? Status.SuggestedMethod;
                IsEmailChosen = Checked == GuidedSetupMethod.Email;
                IsNtfyChosen = Checked == GuidedSetupMethod.Ntfy;
            }
            Prepare();
            Apply();
        }

        // what the step's forms need before it is shown: the Settings tab's forms look at their files (step 2, step 4), the
        // Searches tab's form reads its files and the campground data or compares them with the disk (step 3), and a search
        // shown is the one the Searches form has chosen (only the chosen file reads its campground's values); a search file
        // that is gone leaves the step's list
        private void Prepare()
        {
            switch (Step)
            {
                case GuidedSetupStep.Notifications:
                case GuidedSetupStep.TurnOn:
                    _Settings.CheckForExternalChanges();
                    break;
                case GuidedSetupStep.Search:
                    _Searches.Show();
                    if (Search is SearchFileViewModel Shown)
                    {
                        if (!_Searches.Files.Contains(Shown))
                        {
                            Search = null;
                        }
                        else
                        {
                            _Searches.SelectedFile = Shown;
                        }
                    }
                    break;
                case GuidedSetupStep.Done:
                    _MonitoringView = _Monitoring.View();
                    break;
            }
        }

        // an action's outcome: the status it read, the step it leads to (New search's to its new search's page), what it says
        // and what needs the user's attention
        private void Show(GuidedSetupOutcome Outcome)
        {
            if (Outcome.Status != null)
            {
                Status = Outcome.Status;
            }
            if (Outcome.CreatedFile is string Created)
            {
                Search = _Searches.Files.FirstOrDefault(F => string.Equals(F.FileName, Created, StringComparison.Ordinal));
                Status = Read();
                Notice = null;
                CreatedText = Outcome.Notice;
                Enter(GuidedSetupStep.Search);
                AttentionRequested?.Invoke(GuidedSetupAttention.NewSearch);
                return;
            }
            if (Outcome.Next is GuidedSetupStep Next)
            {
                CreatedText = null;
                MonitoringNotice = null;
                Enter(Next);
            }
            else
            {
                Apply();
            }
            Notice = Outcome.Notice;
            AttentionRequested?.Invoke(Outcome.Attention);
        }

        // ---- what the step shows ----

        private GuidedSetupStatus Read() => GuidedSetupStatus.Read(_ConfigPath, DateTime.Now);

        private void Apply()
        {
            GuidedSetupStatus S = Status;
            Heading = GuidedSetupWording.StepHeading(Step, Method, S);
            ReadinessText = GuidedSetupWording.StartState(S);
            Steps = GuidedSetupFlow.Steps.Select(One => new GuidedStepItem(One, GuidedSetupWording.StepName(One, Method),
                GuidedSetupWording.StepState(GuidedSetupFlow.StateOf(One, Step, Method, S)), One == Step,
                One != Step && (One == GuidedSetupStep.Method || Method != null), () => GoTo(One))).ToList();
            NeedsText = Step == GuidedSetupStep.Notifications && Method is GuidedSetupMethod M && !S.NotificationsReady(M)
                ? GuidedSetupWording.NotReady(M) + Environment.NewLine + string.Join(Environment.NewLine, S.NotificationProblems(M)) : null;
            SearchStateText = S.SearchReady ? GuidedSetupWording.SearchReadyText
                : GuidedSetupWording.SearchNotReady + (S.SearchProblems.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, S.SearchProblems) : string.Empty);
            SearchItems = Step == GuidedSetupStep.Search && Search == null
                ? _Searches.Files.Select(F => new GuidedSearchItem(F, () => SetUpSearch(F))).ToList() : Array.Empty<GuidedSearchItem>();
            HasCampgroundDataProblem = Step == GuidedSetupStep.Search && _Searches.Catalog.LoadError != null;
            MonitoringStateText = DesktopMonitoringWording.StateText(_MonitoringView);
            ToDoItems = Step == GuidedSetupStep.Done && Method is GuidedSetupMethod Known && !IsComplete
                ? GuidedSetupFlow.ToDo(Known, S).Select(One => new GuidedStepItem(One, GuidedSetupWording.StepName(One, Known), GuidedSetupWording.StepToDo, false, true, () => GoTo(One))).ToList()
                : Array.Empty<GuidedStepItem>();
            Observe(Step == GuidedSetupStep.Notifications ? Form : Step == GuidedSetupStep.Search ? Search : null);
            ApplyForm();
            foreach (string Name in Derived)
            {
                this.RaisePropertyChanged(Name);
            }
            RefreshCommands();
        }

        // the step's form's problems and the search's unsaved changes not shown here, as the form is now
        private void ApplyForm()
        {
            SettingsFileViewModel? Editor = _Observed;
            IReadOnlyList<FormProblem> Problems = Editor == null ? Array.Empty<FormProblem>() : FormProblems.Of(Editor);
            ProblemsText = Problems.Count > 0 ? string.Join(Environment.NewLine, Problems.Select(P => P.Text)) : null;
            HasOtherChanges = Editor is SearchFileViewModel File && File.FieldList.Any(Field => !MainSettings(File).Contains(Field) && Field.IsChanged);
            SaveSearchCommand.Refresh();
        }

        // the search's settings the step shows (the web UI's guided search page shows the same)
        private static HashSet<SettingField> MainSettings(SearchFileViewModel File) => new HashSet<SettingField>
        {
            File.AutoRun, File.DisplayName, File.Campground, File.SearchBy, File.SearchValue, File.SearchValueDates, File.ConsecutiveDays, File.TotalHumans
        };

        // the step's form is followed while it is shown: its problems and the search's other changes follow every edit
        private void Observe(SettingsFileViewModel? Editor)
        {
            if (ReferenceEquals(Editor, _Observed))
            {
                return;
            }
            if (_Observed != null)
            {
                _Observed.PropertyChanged -= Form_PropertyChanged;
            }
            _Observed = Editor;
            if (_Observed != null)
            {
                _Observed.PropertyChanged += Form_PropertyChanged;
            }
        }

        private void Form_PropertyChanged(object? Sender, PropertyChangedEventArgs e)
        {
            if (!_Detached && ReferenceEquals(Sender, _Observed))
            {
                ApplyForm();
            }
        }

        // the properties computed from the step, the method and the status
        private static readonly string[] Derived =
        {
            nameof(IsMethodStep), nameof(IsNotificationsStep), nameof(IsSearchListStep), nameof(IsSearchStep), nameof(IsTurnOnStep), nameof(IsDoneStep), nameof(HasBack),
            nameof(IsEmailMethod), nameof(IsNtfyMethod), nameof(StepHelp), nameof(SearchReady),
            nameof(TurnOnHelp), nameof(TurnOnLabel), nameof(NotificationsItemText), nameof(NotificationsNotReady), nameof(NotificationsStepName), nameof(SearchItemText),
            nameof(SearchNotReady), nameof(SearchStepName), nameof(OutputItemText), nameof(RefusedText), nameof(HasRefused), nameof(SecretWarningsText),
            nameof(HasSecretWarnings), nameof(FilesReady), nameof(ShowsDoneContinue), nameof(ShowsTurnOn), nameof(ShowsNoGeneralYet), nameof(TurnOnUnavailableText),
            nameof(HasTurnOnUnavailable), nameof(IsComplete), nameof(IsNotComplete), nameof(DoneText), nameof(ShowsMonitoringChanged), nameof(CanStartMonitoring),
        };

        private void RefreshCommands()
        {
            foreach (RelayCommand Command in new[] { ContinueCommand, BackCommand, SaveNotificationsCommand, NewSearchCommand, SaveSearchCommand, BackToSearchesCommand,
                SearchContinueCommand, TurnOnCommand, DoneContinueCommand, StartMonitoringCommand, ShowStatusCommand, ShowSearchesCommand, ShowSystemCommand,
                GoToNotificationsCommand, GoToSearchCommand })
            {
                Command.Refresh();
            }
        }
    }
}
