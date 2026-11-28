using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive.Campgrounds;
using CampertronLibrary.function.Interactive.Monitoring;
using CampertronLibrary.function.Interactive.Searches;
using CampertronLibrary.function.Interactive.Settings;
using GW = CampertronLibrary.function.Forms.GuidedSetupWording;
using IW = CampertronLibrary.function.Interactive.Settings.InteractiveSettingsWording;
using MW = CampertronLibrary.function.Interactive.Monitoring.InteractiveMonitoringWording;
using SW = CampertronLibrary.function.Forms.SearchesWording;
using W = CampertronLibrary.function.Interactive.GuidedSetup.InteractiveGuidedSetupWording;

namespace CampertronLibrary.function.Interactive.GuidedSetup
{
    // The interactive console's guided setup (v0.4.0's phase 12, D128; business-rules §43): the main menu's Guided setup, the
    // console's host of the shared form layer's GuidedSetupFlow (D117), as the web UI's guided setup pages (D100) and the
    // desktop GUI's Monitoring > Guided setup (D119) are. It keeps nothing of its own and decides no step:
    //  - Where the user is comes from the saved files only (GuidedSetupStatus), read when it opens and again before every
    //    screen; each opening resumes where the saved files say (GuidedSetupFlow.ResumeMethod and the method's first step not
    //    done, else step 1). The step and the method shown live in memory for this visit only: nothing is written, recorded or
    //    marked, and leaving forgets them.
    //  - The step order, a step's state, Back, the steps not done and what step 1's choice, Save and continue of the
    //    notification settings and of a search, New search and Turn the monitoring on do are the flow's: each action hands
    //    the flow the forms of the console's own menus (a new SettingsViewModel's Email or Ntfy form, the Searches menu's
    //    SearchesViewModel and a search's form), and the console shows its outcome: the step it leads to, what it says once,
    //    and what needs the user's attention (said last, before the choices).
    //  - Its steps are the console's own form hosts (InteractiveSettingsForm, InteractiveSearchForm) with every question,
    //    check, preview, conflict, Revert, Reload, test and random topic of Notifications and Searches; inside the guided
    //    setup (FormHostChoices) Save and continue stands where Save is, asked once with No by default when it would write,
    //    and Back and Leave guided setup stand where Back is, each after the form's own question when it holds changes. The
    //    search step shows a search's main settings only, as both other UIs do; its days and filters keep their values.
    //  - Turning the monitoring on saves outputTo through the General form (the flow's), after a y, and starts nothing. The
    //    done step offers the console's monitoring's own Start (InteractiveMonitoring's, for the moment the step showed: its
    //    question, its checks and the session's one MonitoringController) on the user's choice only, then shows Monitoring.
    //    Leaving or completing the guided setup never starts, stops or restarts the monitoring.
    //  - The campground data: the search step says whether it is prepared and readable and, when it is not, offers the
    //    Campgrounds menu's own refresh (InteractiveCampgroundRefresh: its confirmation, progress, cancellation, the
    //    monitoring's pause), never by itself; the console reads the new data from its next screen.
    // Nothing here parses, validates, reads or writes a settings or search file, reads a secret, sends, searches, refreshes
    // or starts the monitoring itself. Every text of a file reaches the terminal with its control characters replaced.
    internal sealed class InteractiveGuidedSetup
    {
        private static readonly SettingsMenuWords Words = new SettingsMenuWords(W.Title, W.GuidedSetup, W.LeaveLabel,
            W.NoFolder, W.FolderMissing, W.ContainerFolderMissing);

        private readonly InteractiveSession Session;
        private readonly InteractivePrompts Prompts;
        private readonly string Folder;
        private GuidedSetupStatus Status;
        private GuidedSetupStep Step = GuidedSetupStep.Method;
        private GuidedSetupMethod? Method;
        // what the last action said, once, on the screen it leads to
        private string? Notice;
        // after an action that stayed on a form: what it said and what needs the user's attention, once
        private string? StayNotice;
        private GuidedSetupAttention? StayAttention;

        // since D130 (PT1): each step is a screen of its own (the first one the screen the guided setup opened on)
        private readonly Dictionary<GuidedSetupStep, InteractiveScreen> Screens = new();
        private readonly InteractiveScreen Entry;

        private InteractiveGuidedSetup(InteractiveSession Session, string Folder, InteractiveScreen Entry)
        {
            this.Session = Session;
            Prompts = Session.Prompts;
            this.Folder = Folder;
            this.Entry = Entry;
            Status = Read();
        }

        // the main menu's Guided setup. Since D130 (PT1) a missing settings folder is offered to be created (SetUpFolder) and
        // the guided setup then goes on with the saved files; the main menu itself never leads here and never creates it
        internal static InteractiveStep Run(InteractiveSession Session)
        {
            InteractivePrompts Prompts = Session.Prompts;
            InteractiveScreen Entry = Prompts.Screen();
            Entry.Show();
            Prompts.Heading(W.Title);
            (string? Path, bool Exists, bool Container) = InteractiveSettingsMenu.FindFolder(Session);
            string? Folder;
            if (Path != null && !Exists && !Container)
            {
                InteractiveStep? End = SetUpFolder(Session, out Folder);
                if (End != null)
                {
                    return End.Value;
                }
            }
            else
            {
                Folder = InteractiveSettingsMenu.SettingsFolder(Session, Words);
            }
            if (Folder == null)
            {
                return InteractiveStep.Done;
            }
            Prompts.Say(GW.Intro);
            Prompts.Say(W.HowItWorks);
            return new InteractiveGuidedSetup(Session, Folder, Entry).Loop();
        }

        // A first setup (D130): the missing settings folder, said with what creating it does, created only after a y (No by
        // default; Esc and Ctrl+C create nothing) by the first start's own preparation (Session.SetUp: Load.GetConfig, as the
        // start without arguments and the desktop app prepare it), never by the console's own code. The folder is looked at
        // again just before (another program may have created it meanwhile: then nothing is created) and after. Nothing is
        // searched, created as a search, downloaded, sent or started. Null with Folder: the guided setup goes on with the
        // saved files; else how the guided setup ends (a problem said in fixed words, the problem's kind only)
        private static InteractiveStep? SetUpFolder(InteractiveSession Session, out string? Folder)
        {
            InteractivePrompts Prompts = Session.Prompts;
            Folder = null;
            Prompts.Say(W.SetUpExplanation);
            PromptResult<bool> Answer = Prompts.Confirm(W.SetUpQuestion, false);
            if (Answer.End == PromptEnd.EndOfInput || Answer.End == PromptEnd.Unavailable)
            {
                return InteractiveStep.EndOfInput;
            }
            if (!Answer.IsAnswered || !Answer.Value)
            {
                Prompts.Say(W.NothingCreated);
                return InteractiveStep.Done;
            }
            (string? Path, bool Exists, bool Container) = InteractiveSettingsMenu.FindFolder(Session);
            if (Path == null || Container)
            {
                Prompts.Say(Path == null ? W.NoFolder : W.ContainerFolderMissing);
                return InteractiveStep.Done;
            }
            if (Exists)
            {
                Prompts.Say(W.FolderAppeared);
                Folder = Path;
                return null;
            }
            Prompts.Say(W.SettingUp);
            try
            {
                Session.SetUp();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Prompts.Say(W.SetUpFailed(ex.GetType().Name));
                return InteractiveStep.Done;
            }
            (Path, Exists, _) = InteractiveSettingsMenu.FindFolder(Session);
            if (Path == null || !Exists)
            {
                Prompts.Say(W.SetUpFailed(nameof(DirectoryNotFoundException)));
                return InteractiveStep.Done;
            }
            Prompts.Say(W.SetUpDone);
            Folder = Path;
            return null;
        }

        // the step's screen: made when the step is first shown in this visit
        private InteractiveScreen ScreenOf(GuidedSetupStep Of)
        {
            if (!Screens.TryGetValue(Of, out InteractiveScreen? Here))
            {
                Here = Screens.Count == 0 ? Entry : Prompts.Screen();
                Screens[Of] = Here;
            }
            return Here;
        }

        // where it resumes (the flow's), then each step until the user leaves or the console ends
        private InteractiveStep Loop()
        {
            Prompts.Say(W.WhereYouAre + ": " + GW.StartState(Status));
            if (GuidedSetupFlow.ResumeMethod(Status) is GuidedSetupMethod Resume)
            {
                Method = Resume;
                Step = Status.NextStep(Resume);
            }
            //the step it resumes at is shown on the screen it opened on
            ScreenOf(Step);
            while (true)
            {
                InteractiveStep? End = Step switch
                {
                    GuidedSetupStep.Method => MethodStep(),
                    GuidedSetupStep.Notifications => NotificationsStep(),
                    GuidedSetupStep.Search => SearchStep(),
                    GuidedSetupStep.TurnOn => TurnOnStep(),
                    _ => DoneStep()
                };
                if (End != null)
                {
                    return End.Value;
                }
            }
        }

        //the search problems name this console's Searches menu and New search file (PT1)
        private GuidedSetupStatus Read() => GuidedSetupStatus.Read(Folder, DateTime.Now, SearchPlace.Menu);

        // ---- every step ----

        // the step's heading, the list of steps with each one's state in words (the flow's), and what the last action said
        private void ShowStep()
        {
            ScreenOf(Step).Show();
            Status = Read();
            Prompts.Heading(Safe(GW.StepHeading(Step, Method, Status)));
            foreach (GuidedSetupStep One in GuidedSetupFlow.Steps)
            {
                Prompts.Say("  " + W.StepLine(One, GW.StepName(One, Method), GW.StepState(GuidedSetupFlow.StateOf(One, Step, Method, Status))), "    ");
            }
            SayNotice();
        }

        private void SayNotice()
        {
            if (Notice != null)
            {
                Prompts.Say(Safe(Notice));
                Notice = null;
            }
        }

        // a step's own choices: the step's actions, then Back (the flow's step before, when there is one) and Leave guided
        // setup. Esc and Ctrl+C leave the guided setup; the end of the input ends the console. Null: the step again
        private InteractiveStep? Choose(string Title, List<(string Label, Func<InteractiveStep?> Run)> Actions)
        {
            if (GuidedSetupFlow.Previous(Step) is GuidedSetupStep Before)
            {
                Actions.Add((GW.BackLabel, () => GoTo(Before)));
            }
            Actions.Add((W.LeaveLabel, Leave));
            PromptResult<int> Picked = Prompts.Choose(Title, Actions.Select(A => A.Label).ToList());
            if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
            {
                return InteractiveStep.EndOfInput;
            }
            if (!Picked.IsAnswered)
            {
                return Leave();
            }
            return Actions[Picked.Value].Run();
        }

        // a step the user opened (Back, a review's or the done step's link): as the saved files are now
        private InteractiveStep? GoTo(GuidedSetupStep Next)
        {
            Step = Next;
            Notice = null;
            return null;
        }

        private InteractiveStep? Leave()
        {
            Prompts.Say(W.Left);
            return InteractiveStep.Done;
        }

        // a form's step: the form's own host with the guided setup's way through it; Back, Leave or the form's end
        private InteractiveStep? AfterForm(ScreenEnd End, FormHostChoices Way, int Back, int Leave_)
        {
            if (End == ScreenEnd.EndOfInput)
            {
                return InteractiveStep.EndOfInput;
            }
            if (Way.Taken == Back && GuidedSetupFlow.Previous(Step) is GuidedSetupStep Before)
            {
                return GoTo(Before);
            }
            if (Way.Taken == Leave_)
            {
                return Leave();
            }
            return null;
        }

        // Save and continue's question, asked only when the form would write (it has changes): No by default. Null: go on to
        // the flow; else the form stays (No, Esc, Ctrl+C) or the console ends
        private ScreenEnd? AskToSave(SettingsFileViewModel Form, out bool Stay)
        {
            Stay = false;
            if (!Form.IsDirty)
            {
                return null;
            }
            PromptResult<bool> Sure = Prompts.Confirm(IW.SaveQuestion(Safe(Form.FileName)), false);
            if (Sure.End == PromptEnd.EndOfInput || Sure.End == PromptEnd.Unavailable)
            {
                return ScreenEnd.EndOfInput;
            }
            if (!Sure.IsAnswered || !Sure.Value)
            {
                if (Sure.IsCancelled)
                {
                    Prompts.Say(InteractiveWording.Cancelled);
                }
                Prompts.Say(IW.Kept);
                Stay = true;
            }
            return null;
        }

        // after a form's action that stayed: what it said, then what needs the user's attention (the form's problems in its
        // own words), once
        private void SayStay(SettingsFileViewModel Form, Func<FormProblem, bool>? Hidden = null)
        {
            if (StayNotice != null)
            {
                Prompts.Say(Safe(StayNotice));
            }
            if (StayAttention == GuidedSetupAttention.Problems)
            {
                IReadOnlyList<FormProblem> Problems = FormProblems.Of(Form);
                if (Problems.Count > 0)
                {
                    Prompts.Say(W.ProblemsHeading + ":");
                    foreach (FormProblem Problem in Problems)
                    {
                        Prompts.Say("  " + Safe(Problem.Text), "    ");
                    }
                    if (Hidden != null && Problems.Any(Hidden))
                    {
                        Prompts.Say(W.OtherSettingsProblem);
                    }
                }
            }
            StayNotice = null;
            StayAttention = null;
        }

        // ---- step 1: how you are notified ----

        private InteractiveStep? MethodStep()
        {
            ShowStep();
            Prompts.Say(W.MethodHint);
            if ((Method ?? Status.SuggestedMethod) is GuidedSetupMethod Now)
            {
                Prompts.Say(W.ChosenNow(Now));
            }
            return Choose(GW.MethodLegend, new List<(string, Func<InteractiveStep?>)>
            {
                (GW.EmailChoice, () => Choose(GuidedSetupMethod.Email)),
                (GW.NtfyChoice, () => Choose(GuidedSetupMethod.Ntfy))
            });
        }

        // the flow's choice: it saves nothing, and leads to the method's first step that is not done
        private InteractiveStep? Choose(GuidedSetupMethod Picked)
        {
            GuidedSetupOutcome Outcome = GuidedSetupFlow.Choose(Picked, Read());
            Status = Outcome.Status ?? Status;
            Notice = Outcome.Notice;
            if (Outcome.Next is GuidedSetupStep Next)
            {
                Method = Picked;
                Step = Next;
            }
            return null;
        }

        // ---- step 2: the notification settings ----

        private InteractiveStep? NotificationsStep()
        {
            if (Method is not GuidedSetupMethod Known)
            {
                return GoTo(GuidedSetupStep.Method);
            }
            ShowStep();
            Prompts.Say(Known == GuidedSetupMethod.Ntfy ? GW.NtfyHelp : GW.EmailHelp);
            Prompts.Say(GW.TestRecommended);
            Prompts.Say(W.SaveAndContinueHint);
            //the Notifications menu's form, new each time the step is shown: it reads the file as it is now
            var Forms = new SettingsViewModel(Folder);
            SettingsFileViewModel Form = Known == GuidedSetupMethod.Email ? Forms.Email : Forms.Ntfy;
            var Way = new FormHostChoices
            {
                //the form below the step's heading, on the step's screen
                Screen = ScreenOf(Step),
                ContinueLabel = GW.SaveAndContinueLabel,
                Continue = () => ContinueNotifications(Form, Known),
                Exits = new[] { GW.BackLabel, W.LeaveLabel },
                CancelExit = 1,
                AfterState = () => NotificationsState(Form, Known)
            };
            ScreenEnd End = InteractiveSettingsForm.Run(Prompts, Form, W.LeaveLabel, () => DateTime.Now, SmtpPassword.VariableIsSet, NtfyToken.VariableIsSet, Way);
            return AfterForm(End, Way, 0, 1);
        }

        // Save and continue: the flow saves through the form's own Save (after a y when it would write) and goes on once the
        // saved file is ready; else the form stays, with what needs the user's attention
        private ScreenEnd? ContinueNotifications(SettingsFileViewModel Form, GuidedSetupMethod Known)
        {
            ScreenEnd? Ended = AskToSave(Form, out bool Stay);
            if (Ended != null || Stay)
            {
                return Ended;
            }
            GuidedSetupOutcome Outcome = GuidedSetupFlow.ContinueNotifications(Form, Known, Folder);
            Status = Outcome.Status ?? Read();
            if (Outcome.Next is GuidedSetupStep Next)
            {
                Step = Next;
                Notice = Outcome.Notice;
                return ScreenEnd.Back;
            }
            StayNotice = Outcome.Notice;
            StayAttention = Outcome.Attention;
            return null;
        }

        // after the form's state: what the monitoring's start would refuse in the method's saved file, and an action's result
        private void NotificationsState(SettingsFileViewModel Form, GuidedSetupMethod Known)
        {
            GuidedSetupStatus Now = Read();
            if (!Now.NotificationsReady(Known))
            {
                Prompts.Say(GW.NotReady(Known));
                foreach (string Problem in Now.NotificationProblems(Known))
                {
                    Prompts.Say("  " + Safe(Problem), "    ");
                }
            }
            SayStay(Form);
        }

        // ---- step 3: a search ----

        private InteractiveStep? SearchStep()
        {
            while (true)
            {
                if (Method == null)
                {
                    return GoTo(GuidedSetupStep.Method);
                }
                ShowStep();
                Prompts.Say(W.SearchHelp);
                Prompts.Say(Status.SearchReady ? GW.SearchReadyText : GW.SearchNotReady);
                foreach (string Problem in Status.SearchProblems)
                {
                    Prompts.Say("  " + Safe(Problem), "    ");
                }
                //the Searches menu's form, new each time the list is shown: it reads the folder, every file and the data now
                var Data = new InteractiveSearches.CampgroundData(Folder);
                SearchesViewModel Searches = InteractiveSearches.Compose(Session, Folder, Data);
                bool DataProblem = false;
                if (!Data.Prepared)
                {
                    Prompts.Say(W.DataMissing);
                    DataProblem = true;
                }
                else if (Searches.Catalog.LoadError is string Unreadable)
                {
                    Prompts.Say(MessageSeverity.AccessibleText(Unreadable, MessageSeverity.Warning) + " " + W.DataUnreadableHow);
                    DataProblem = true;
                }
                List<SearchFileViewModel> Files = Searches.Files.ToList();
                if (Files.Count == 0)
                {
                    Prompts.Say(GW.NoSearchFiles);
                }
                SearchFileViewModel? Open = null;
                bool Created = false;
                bool Ended = false;
                bool Again = false;
                var Actions = new List<(string Label, Func<InteractiveStep?> Run)>();
                foreach (SearchFileViewModel File in Files)
                {
                    Actions.Add((W.SetUp(Safe(InteractiveSearchesWording.File(File.FileName, File.ListDetails, File.ListStatus))), () =>
                    {
                        Open = File;
                        return null;
                    }));
                }
                Actions.Add((SW.Plain(SW.NewSearchLabel), () =>
                {
                    Open = NewSearch(Searches, out Ended);
                    Created = Open != null;
                    Again = Open == null;
                    return null;
                }));
                if (Status.SearchReady)
                {
                    Actions.Add((GW.ContinueLabel, () => GoTo(GuidedSetupStep.TurnOn)));
                }
                if (DataProblem)
                {
                    Actions.Add((W.RefreshLabel, () =>
                    {
                        InteractiveStep After = InteractiveCampgroundRefresh.Run(Session, Folder);
                        Again = true;
                        return After == InteractiveStep.Done ? null : After;
                    }));
                }
                GuidedSetupStep Before = Step;
                InteractiveStep? End = Choose(GW.SearchesHeading, Actions);
                if (End != null || Ended)
                {
                    return End ?? InteractiveStep.EndOfInput;
                }
                if (Step != Before)
                {
                    return null;
                }
                if (Again || Open == null)
                {
                    continue;
                }
                InteractiveStep? Page = SearchPage(Searches, Open, Data, Created);
                if (Page != null || Step != Before)
                {
                    return Page;
                }
            }
        }

        // New search: the console's own questions, then the flow's New search (the form's command, its name rules, the
        // example search not enabled, create-only); the new file, or null when nothing was created (its words said)
        private SearchFileViewModel? NewSearch(SearchesViewModel Searches, out bool Ended)
        {
            string? Name = InteractiveSearches.NewSearchName(Prompts, out Ended);
            if (Name == null)
            {
                return null;
            }
            Searches.NewSearchName = Name;
            GuidedSetupOutcome Outcome = GuidedSetupFlow.NewSearch(Searches);
            if (Outcome.CreatedFile is string File)
            {
                Notice = Outcome.Notice is string Message ? MessageSeverity.AccessibleText(Message, MessageSeverity.Info) : null;
                return Searches.Files.FirstOrDefault(F => string.Equals(F.FileName, File, StringComparison.Ordinal));
            }
            InteractiveSearches.SayNewSearchResult(Prompts, Searches);
            return null;
        }

        // a search's page: its main settings in the Searches menu's form, with Save and continue, All search files of this
        // step, Back and Leave guided setup. Null: the list again, or the step it went on to
        private InteractiveStep? SearchPage(SearchesViewModel Searches, SearchFileViewModel File, InteractiveSearches.CampgroundData Data, bool Created)
        {
            if (!Created)
            {
                Notice = null;
            }
            SayNotice();
            Prompts.Say(W.SearchPageHelp);
            Searches.SelectedFile = File;
            HashSet<SettingField> Main = MainSettings(File);
            var Way = new FormHostChoices
            {
                //the form below the step's heading, on the step's screen
                Screen = ScreenOf(Step),
                ContinueLabel = GW.SaveAndContinueLabel,
                Continue = () => ContinueSearch(File),
                Exits = new[] { GW.BackToSearches, GW.BackLabel, W.LeaveLabel },
                CancelExit = 0,
                Shows = Main.Contains,
                AfterState = () => SayStay(File, Problem => Problem.Field == null || !Main.Contains(Problem.Field))
            };
            (ScreenEnd End, _) = InteractiveSearchForm.Run(Prompts, Searches, File, () => Data.Prepared, Way);
            return AfterForm(End, Way, 1, 2);
        }

        // the search's settings the step shows (the desktop GUI's and the web UI's guided search page show the same)
        private static HashSet<SettingField> MainSettings(SearchFileViewModel File) => new HashSet<SettingField>
        {
            File.AutoRun, File.DisplayName, File.Campground, File.SearchBy, File.SearchValue, File.SearchValueDates, File.ConsecutiveDays, File.TotalHumans
        };

        // Save and continue of a search: the flow saves through the form's own Save (after a y when it would write) and goes
        // on once an enabled search can be checked; else the form stays, with what needs the user's attention
        private ScreenEnd? ContinueSearch(SearchFileViewModel File)
        {
            ScreenEnd? Ended = AskToSave(File, out bool Stay);
            if (Ended != null || Stay)
            {
                return Ended;
            }
            GuidedSetupOutcome Outcome = GuidedSetupFlow.ContinueSearch(File, Folder);
            Status = Outcome.Status ?? Read();
            if (Outcome.Next is GuidedSetupStep Next)
            {
                Step = Next;
                Notice = Outcome.Notice;
                return ScreenEnd.Back;
            }
            StayNotice = Outcome.Notice;
            StayAttention = Outcome.Attention;
            return null;
        }

        // ---- step 4: review and turn on ----

        private InteractiveStep? TurnOnStep()
        {
            if (Method is not GuidedSetupMethod Known)
            {
                return GoTo(GuidedSetupStep.Method);
            }
            ShowStep();
            GuidedSetupStatus S = Status;
            Prompts.Say(W.TurnOnHelp(Known));
            Prompts.Say(GW.ReviewHeading + ":");
            Prompts.Say("  " + GW.NotificationsItem(Known, S.NotificationsReady(Known)), "    ");
            Prompts.Say("  " + GW.SearchItem(S.SearchReady), "    ");
            Prompts.Say("  " + GW.OutputItem(Known, S.SavedOutput), "    ");
            if (S.Problems(Known).Count > 0)
            {
                Prompts.Say(GW.FilesRefused);
                foreach (string Problem in S.Problems(Known))
                {
                    Prompts.Say("  " + Safe(Problem), "    ");
                }
            }
            foreach (string Warning in S.SecretFileWarnings(Known))
            {
                Prompts.Say(Safe(MessageSeverity.AccessibleText(Warning, MessageSeverity.Warning)));
            }
            bool On = S.IsOn(Known) && S.FilesReady(Known);
            var Actions = new List<(string, Func<InteractiveStep?>)>();
            if (On)
            {
                Actions.Add((GW.ContinueLabel, () => GoTo(GuidedSetupStep.Done)));
            }
            else if (S.FilesReady(Known))
            {
                if (!S.GeneralExists)
                {
                    Prompts.Say(GW.NoGeneralYet);
                }
                Actions.Add((GW.TurnOnLabel(Known), () => TurnOn(Known)));
            }
            else
            {
                Prompts.Say(W.TurnOnUnavailable);
            }
            if (!S.NotificationsReady(Known))
            {
                Actions.Add((GW.StepName(GuidedSetupStep.Notifications, Known), () => GoTo(GuidedSetupStep.Notifications)));
            }
            if (!S.SearchReady)
            {
                Actions.Add((GW.StepName(GuidedSetupStep.Search, Known), () => GoTo(GuidedSetupStep.Search)));
            }
            return Choose(GW.StepName(GuidedSetupStep.TurnOn, Known), Actions);
        }

        // Turn the monitoring on: after a y (No by default), the flow saves outputTo through the General form (the file
        // looked at first, as every screen of the console looks at it) only when the saved files pass, and starts nothing
        private InteractiveStep? TurnOn(GuidedSetupMethod Known)
        {
            PromptResult<bool> Sure = Prompts.Confirm(W.TurnOnQuestion(Known), false);
            if (Sure.End == PromptEnd.EndOfInput || Sure.End == PromptEnd.Unavailable)
            {
                return InteractiveStep.EndOfInput;
            }
            if (!Sure.IsAnswered || !Sure.Value)
            {
                Notice = Sure.IsCancelled ? InteractiveWording.Cancelled + " " + W.NothingSaved : W.NothingSaved;
                return null;
            }
            var Forms = new SettingsViewModel(Folder);
            Forms.General.UseSearchPlace(SearchPlace.Menu);
            Forms.CheckForExternalChanges();
            GuidedSetupStatus Now = Read();
            GuidedSetupOutcome Outcome = GuidedSetupFlow.TurnOn(Forms.General, Known, Now, Folder, W.Host);
            Status = Outcome.Status ?? Read();
            Notice = Outcome.Notice;
            if (Outcome.Next is GuidedSetupStep Next)
            {
                Step = Next;
            }
            return null;
        }

        // ---- done ----

        private InteractiveStep? DoneStep()
        {
            if (Method is not GuidedSetupMethod Known)
            {
                return GoTo(GuidedSetupStep.Method);
            }
            ShowStep();
            GuidedSetupStatus S = Status;
            bool Complete = S.State(Known) == GuidedSetupState.Complete;
            var Actions = new List<(string, Func<InteractiveStep?>)>();
            if (Complete)
            {
                Prompts.Say(W.DoneText(Known));
            }
            else
            {
                Prompts.Say(GW.NotDoneText);
                foreach (GuidedSetupStep ToDo in GuidedSetupFlow.ToDo(Known, S))
                {
                    string Name = GW.StepName(ToDo, Known);
                    Prompts.Say("  " + W.StepLine(ToDo, Name, GW.StepToDo), "    ");
                    Actions.Add((Name, () => GoTo(ToDo)));
                }
            }
            //the console's one monitoring, the session's (made here when Monitoring was not opened yet; never another)
            InteractiveMonitoringHost Host = InteractiveMonitoring.HostOf(Session, Folder);
            MonitoringControlView V = Host.Controller.View();
            Prompts.Say(GW.MonitoringHeading + ":");
            Prompts.Say("  " + MonitoringWording.StatusHeading + ": " + Safe(MW.StateText(V)), "    ");
            if (V.Status.IsRunning && V.Status.SearchFilesChangedSinceStart)
            {
                Prompts.Say(MessageSeverity.AccessibleText(MonitoringWording.FilesChanged, MessageSeverity.Warning));
                Prompts.Say(W.MonitoringRunningChanged);
            }
            else if (Complete && V.CanStart)
            {
                Prompts.Say(W.MonitoringNotRunning);
            }
            if (Complete)
            {
                Prompts.Say(W.ElsewhereNote);
            }
            if (Complete && V.CanStart)
            {
                Actions.Insert(0, (GW.StartMonitoringLabel, () => StartMonitoring(Host, V)));
            }
            return Choose(W.DoneChoices, Actions);
        }

        // Start the monitoring: the console's monitoring's own Start (Monitoring's: its words, its question, its checks), for
        // the moment the step showed, on the user's choice only (D115); started, Monitoring shows how it goes. A start the
        // controller did not take says why on the step
        private InteractiveStep? StartMonitoring(InteractiveMonitoringHost Host, MonitoringControlView Shown)
        {
            InteractiveStep? End = InteractiveMonitoring.Act(Session, Host, Shown, MonitoringControlAction.Start, out string? Said);
            if (End != null)
            {
                return End;
            }
            if (Said != null)
            {
                Notice = Said;
                return null;
            }
            return InteractiveMonitoring.Run(Session);
        }

        // a text of a file or of the shared layer as a terminal may show it: every control character replaced
        private static string Safe(string? Text) => InteractiveSettingsForm.Visible(Text ?? string.Empty);
    }
}
