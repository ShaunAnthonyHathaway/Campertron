using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive.Campgrounds;
using CampertronLibrary.function.Interactive.Settings;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronLibrary.function.RecDotOrg.sqlite;
using RW = CampertronLibrary.function.Forms.ResultsWording;
using W = CampertronLibrary.function.Interactive.Results.InteractiveResultsWording;

namespace CampertronLibrary.function.Interactive.Results
{
    // What the console's Results use (tests only change them): whether the campground data is prepared (the database file
    // exists; never opened) and its campgrounds (the list's names), the clocks, the shared search service's listing reader,
    // plan and run (CampsiteSearch, manual-search semantics) with its options (a stand-in transport), how often the console
    // looks at the keys and the progress, the least time between two lines of counts, and the stop signals
    internal sealed class InteractiveResultsSeams
    {
        internal Func<string, bool> Prepared { get; init; } = Folder => Load.DbExists(new CampertronInternalConfig { ConfigPath = Folder });
        internal Func<string, List<CampgroundPickerEntry>> Campgrounds { get; init; } = Read.CampgroundPickerEntries;
        internal Func<DateTime> Now { get; init; } = () => DateTime.Now;
        internal TimeProvider Time { get; init; } = TimeProvider.System;
        internal Func<string, string, DateTime, SearchPlanEntry> Inspect { get; init; } = CampsiteSearch.LoadEntry;
        internal Func<SearchRunRequest, DateTime, SearchRunPlan> Plan { get; init; } = CampsiteSearch.Plan;
        internal Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult> Run { get; init; } = CampsiteSearch.Run;
        internal SearchHostOptions? Options { get; init; }
        internal TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
        internal TimeSpan CountInterval { get; init; } = TimeSpan.FromSeconds(5);
        // a request to stop the process while a search runs (SIGTERM, SIGHUP), as the refresh registers it (D125)
        internal Func<Action, IDisposable> WhenAskedToStop { get; init; } = InteractiveCampgroundRefresh.RegisterStopSignals;
    }

    // The console's Results of one session (v0.4.0's phase 10, D126): kept between visits of Results, as the desktop GUI's
    // Results tab keeps them while its window is open, and gone when the console ends. The settings folder's list of search
    // files and its choice (the shared ResultsSearchList, its rules: the enabled files that can run chosen at first, a file
    // that can't run never chosen, the user's choice kept), the five minutes after a block (BlockHold, this console's own, as
    // a window's or a web UI's; since v0.4.0's phase 11, D127, the session's, which the console's monitoring shares), the
    // generation of the runs (each accepted Start the next number; only the latest run's end
    // is taken over, so an earlier run's result never replaces a later one's), the last run's results (ResultsOutcome) with
    // when it ended, and its status line. Used on the console's thread only; the run's own thread touches only its
    // SearchOperation.
    internal sealed class InteractiveResultsState
    {
        internal InteractiveResultsState(string ConfigPath, ResultsSearchList List, BlockHold Hold)
        {
            this.ConfigPath = ConfigPath;
            this.List = List;
            this.Hold = Hold;
        }

        internal string ConfigPath { get; }
        internal ResultsSearchList List { get; }
        internal BlockHold Hold { get; }
        // the number of the last run started (0: none yet)
        internal long LastRun { get; private set; }
        internal ResultsOutcome? Outcome { get; private set; }
        internal long OutcomeOf { get; private set; }
        internal long OutcomeEndedAt { get; private set; }
        // the last run's status line ("Finished: …", "Canceled. …"), or null before the first run
        internal string? Status { get; private set; }

        // an accepted Start: the next run's number; the previous run's results give way to it, its files' rows are locked
        // and wait (the shared list's rule while a run goes on)
        internal long Started(IReadOnlyList<string> Files)
        {
            LastRun++;
            Outcome = null;
            OutcomeOf = 0;
            Status = RW.Searching(Files.Count);
            foreach (ResultsSearchRow Row in List.Rows)
            {
                Row.SetLocked(true);
                Row.RunState = Files.Contains(Row.FileName, StringComparer.Ordinal) ? RW.Waiting : null;
            }
            return LastRun;
        }

        // A run has ended (its thread is done). Its block holds Start for five minutes whatever run it was (recreation.gov
        // limits the computer); its result is taken over only when it is the latest run started: false for an earlier run,
        // whose result then changes nothing
        internal bool Ended(SearchOperation Run, SearchRunResult Result, long EndedAt, string Elapsed)
        {
            if (Result.Status == SearchRunStatus.Blocked)
            {
                Hold.Begin();
            }
            if (Run.Id != LastRun)
            {
                return false;
            }
            Outcome = new ResultsOutcome(Result, Run.Files);
            OutcomeOf = Run.Id;
            OutcomeEndedAt = EndedAt;
            Status = RW.Finished(Outcome.Summary, Elapsed);
            foreach (ResultsSearchRow Row in List.Rows)
            {
                Row.SetLocked(false);
                Row.RunState = Outcome.RunStateOf(Row.FileName);
            }
            return true;
        }
    }

    // The interactive console's Results (v0.4.0's phase 10, D126; business-rules §43): the manual search of the desktop
    // GUI's Results tab and the web UI's results page at a terminal, on the shared search service (CampsiteSearch, D115: the
    // --interactive console's searches; the start without arguments keeps its own engine until phase 14) and the shared form
    // layer's Results (ResultsSearchList, ResultsStart, ResultsOutcome, ResultsListing, ResultsWording, BlockHold):
    //  - Entering it reads the settings folder's search files as the shared list reads them (CampsiteSearch.LoadEntry: the
    //    bounded reader and the validation, never written) and starts, creates and changes nothing. The list is read again
    //    each time its choices are shown and at Start; the user's choice is kept for this console's session.
    //  - A search starts only from Start search, through ResultsStart's checks and words (the files read again, a chosen
    //    file the disk unchose means nothing starts, at least one chosen file that can be searched), never by itself, not
    //    within the five minutes after a block, and only with the settings and cache folders there (never created here).
    //  - It runs on a thread of its own (InteractiveSearchRun), which the console waits for: Esc and Ctrl+C cancel it through
    //    the shared run's token, so do the end of the input and SIGTERM or SIGHUP, and nothing is asked while it runs.
    //  - Its results are the shared ResultsOutcome, shown with the shared words a part at a time (InteractiveResultsView);
    //    a campsite's booking link only as the shared BookingLink accepts it, shown as text (no browser is opened).
    //  - A manual search: no Camp.History (never read, created, changed, removed or re-armed), no Email.yaml or Ntfy.yaml,
    //    no notification, no monitoring or its lock, no search file written, nothing retried by the console (the shared
    //    run's own bounds: at most three requests a month, none after a block), and no file but the campgrounds' campsite
    //    details the shared run prepares in the cache folder. Every text of a file or of recreation.gov reaches the terminal
    //    with its control characters replaced.
    internal static class InteractiveResults
    {
        private static readonly SettingsMenuWords Words = new SettingsMenuWords(W.ResultsTitle, W.Results, W.BackToResults,
            W.NoFolder, W.FolderMissing, W.ContainerFolderMissing);

        internal static InteractiveResultsSeams Seams = new InteractiveResultsSeams();

        // the main menu's Results
        internal static InteractiveStep Run(InteractiveSession Session)
        {
            InteractivePrompts Prompts = Session.Prompts;
            //since D130 (PT1): a screen of its own, started on a cleared screen on arrival and on each return to it
            InteractiveScreen Here = Prompts.Screen();
            Here.Show();
            Prompts.Heading(W.ResultsTitle);
            string? Folder = InteractiveSettingsMenu.SettingsFolder(Session, Words);
            if (Folder == null)
            {
                return InteractiveStep.Done;
            }
            InteractiveResultsState State = StateOf(Session, Folder);
            bool First = true;
            while (true)
            {
                if (!First)
                {
                    Here.Show();
                    Prompts.Heading(W.ResultsTitle);
                }
                First = false;
                Prompts.Say(W.ResultsNote);
                string? Notice = null;
                State.List.Rescan(false, Text => Notice = Text);
                if (Notice != null)
                {
                    Prompts.Say(Notice);
                }
                if (!Seams.Prepared(Folder))
                {
                    Prompts.Say(W.DataMissing);
                }
                if (State.Hold.Text() is string Held)
                {
                    Prompts.Say(Held);
                }
                if (State.Status != null)
                {
                    Prompts.Say(Safe(State.Status));
                }
                List<ResultsSearchRow> Rows = State.List.Rows.ToList();
                if (Rows.Count == 0)
                {
                    if (Notice == null)
                    {
                        Prompts.Say(W.NoSearchFiles);
                    }
                }
                else
                {
                    Prompts.Say(W.ChooseHow);
                    Prompts.Say(Rows.Any(R => R.IsSelected) ? W.ChosenCount(Rows.Count(R => R.IsSelected), Rows.Count) : W.NoneChosen);
                }
                var Choices = Rows.Select(Label).ToList();
                int StartAt = Choices.Count;
                Choices.Add(RW.StartName);
                Choices.Add(RW.SelectEnabledLabel);
                Choices.Add(RW.SelectAllLabel);
                Choices.Add(RW.ClearSelectionLabel);
                int ShowAt = State.Outcome != null ? Choices.Count : -1;
                if (ShowAt >= 0)
                {
                    Choices.Add(W.ShowResults);
                }
                Choices.Add(InteractiveSettingsWording.BackToMainMenu);
                PromptResult<int> Picked = Prompts.Choose(RW.RunHeading, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return InteractiveStep.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == Choices.Count - 1)
                {
                    //Back, Esc or Ctrl+C: the main menu again
                    return InteractiveStep.Done;
                }
                int At = Picked.Value;
                if (At < StartAt)
                {
                    Toggle(Prompts, Rows[At]);
                }
                else if (At == StartAt)
                {
                    InteractiveStep Step = Start(Session, State, Folder);
                    if (Step != InteractiveStep.Done)
                    {
                        return Step;
                    }
                }
                else if (At == StartAt + 1)
                {
                    State.List.SelectEnabled();
                }
                else if (At == StartAt + 2)
                {
                    State.List.SelectAllRunnable();
                }
                else if (At == StartAt + 3)
                {
                    State.List.ClearSelection();
                }
                else if (At == ShowAt)
                {
                    if (InteractiveResultsView.Show(Prompts, State, false) == ListEnd.EndOfInput)
                    {
                        return InteractiveStep.EndOfInput;
                    }
                }
            }
        }

        // this console's Results for the settings folder (a new one when the folder is another)
        private static InteractiveResultsState StateOf(InteractiveSession Session, string Folder)
        {
            if (Session.ResultsState is InteractiveResultsState Kept && string.Equals(Kept.ConfigPath, Folder, StringComparison.Ordinal))
            {
                return Kept;
            }
            InteractiveResultsSeams With = Seams;
            //the list's campground names from the campground data, read once for this console's Results and only when it is
            //prepared (never opened when missing; without it a row names the campground by its ID, as both other UIs do)
            var Catalog = new CampgroundCatalog(() => With.Prepared(Folder) ? With.Campgrounds(Folder) : new List<CampgroundPickerEntry>());
            //the five minutes after a block are the session's, shared with the console's monitoring (D127)
            var State = new InteractiveResultsState(Folder, new ResultsSearchList(Folder, Catalog, Name => false, With.Inspect, With.Now, () => { }), Session.Hold(With.Time));
            Session.ResultsState = State;
            return State;
        }

        // a search file as a choice: its title, whether it is chosen, its details (its file, its campground, its choice), why
        // it can't be searched and its warnings (the shared row's words), and what the last run did with it
        private static string Label(ResultsSearchRow Row) =>
            Safe(W.Row(Row.Title, Row.IsSelected ? W.Chosen : W.NotChosen, Row.HelpText, Row.RunState));

        // a search file chosen or left out, by the shared row's rule (a file that can't run can't be chosen: its reason)
        private static void Toggle(InteractivePrompts Prompts, ResultsSearchRow Row)
        {
            string Title = Row.Title + " (" + Row.FileName + ")";
            if (!Row.IsRunnable)
            {
                Prompts.Say(Safe(W.CantBeChosen(Title, Row.Reason)));
                return;
            }
            Row.IsSelected = !Row.IsSelected;
            Prompts.Say(Safe(Row.IsSelected ? W.NowChosen(Title) : W.NoLongerChosen(Title)));
        }

        // Start search: the console's folders, then ResultsStart's checks and words, then the run and its results
        private static InteractiveStep Start(InteractiveSession Session, InteractiveResultsState State, string Folder)
        {
            InteractivePrompts Prompts = Session.Prompts;
            if (State.Hold.IsActive)
            {
                Prompts.Say(State.Hold.Text() ?? RW.NotStartedNoneReady);
                return InteractiveStep.Done;
            }
            CampertronInternalConfig? Folders = null;
            try
            {
                Folders = Session.Folders();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException
                || ex is System.Security.SecurityException || ex is InvalidOperationException)
            {
                //no folders (Load.DesktopDataFolder's refusal): the shared words below
            }
            string? Cache = Folders?.CachePath;
            if (Cache == null || !Path.IsPathFullyQualified(Cache) || !Path.IsPathFullyQualified(Folder))
            {
                Prompts.Say(RW.NoCacheFolder);
                return InteractiveStep.Done;
            }
            if (!Directory.Exists(Cache))
            {
                Prompts.Say(Folders!.ContainerMode ? W.ContainerCacheFolderMissing : W.CacheFolderMissing);
                return InteractiveStep.Done;
            }
            ResultsStart Prepared = ResultsStart.Prepare(State.List.Rows, () => State.List.Rescan(true, Text => { }),
                Names => Seams.Plan(new SearchRunRequest(Folder, Cache, Names), Seams.Now()));
            if (Prepared.Plan == null)
            {
                //nothing started: the previous results stay, and the reason is said in the shared words
                if (Prepared.ListNotice != null)
                {
                    Prompts.Say(Safe(Prepared.ListNotice));
                }
                Prompts.Say(Safe(Prepared.Status ?? RW.NotStartedNoneReady));
                return InteractiveStep.Done;
            }
            InteractiveStep Step = InteractiveSearchRun.Execute(Prompts, State, Prepared);
            if (Step != InteractiveStep.Done)
            {
                return Step;
            }
            return InteractiveResultsView.Show(Prompts, State, true) == ListEnd.EndOfInput ? InteractiveStep.EndOfInput : InteractiveStep.Done;
        }

        // a text of a file or of recreation.gov as a terminal may show it: every control character replaced
        internal static string Safe(string? Text) => InteractiveSettingsForm.Visible(Text ?? string.Empty);
    }
}
