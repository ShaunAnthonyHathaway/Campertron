using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive.Campgrounds;
using CampertronLibrary.function.Interactive.Results;
using CampertronLibrary.function.Interactive.Settings;
using CampertronLibrary.function.RecDotOrg.monitoring;
using W = CampertronLibrary.function.Interactive.Monitoring.InteractiveMonitoringWording;

namespace CampertronLibrary.function.Interactive.Monitoring
{
    // The interactive console's Monitoring (v0.4.0's phase 11, D127; business-rules §43): the desktop GUI's Monitoring tab and
    // the web UI's monitoring page at a terminal, over the console's one monitoring (InteractiveMonitoringHost: the shared
    // MonitoringController, D116, the session's and no menu's). It holds no rule of the monitoring: it shows one immutable
    // view of the controller in the shared words (MonitoringWording, with the console's sentences) and hands it the chosen
    // action with the moment the menu showed (its Token), so a menu that is out of date starts and stops nothing, and one
    // action at a time, a start's checks, a refused start, a failure, a block's hold and the stop are the controller's.
    //  - Entering it starts, stops and changes nothing: it shows the state, the facts once it started, a refused start or a
    //    failure in the shared words, the hold after a block, the last check, and why an action is unavailable; only the
    //    actions available now are offered. Show the latest status reads the view again (it only looks).
    //  - Start and Restart ask first (No by default, the console's rule for an action that starts), Stop does not; each acts
    //    for the moment shown, then the menu waits for the action, at most ActionWait, reading the keys as they are typed (Esc
    //    and Ctrl+C stop the waiting only: the action goes on; the end of the input ends the console, which stops the
    //    monitoring). Nothing is retried; nothing starts again by itself after a failure.
    //  - A Start is refused here before the controller only when the cache folder does not exist (the console never creates
    //    it, D126's rule); every other refusal is the controller's start, in its words.
    //  - Leaving it, or any other menu, never stops, pauses or restarts the monitoring; the console's end stops it
    //    (InteractiveConsole), and Exit while it is on asks first (Exit).
    //  - The last check's new openings are the controller's snapshot (the shared result rows), shown a part at a time with a
    //    campsite's booking link as text (InteractiveResultsView). Every text of a file reaches the terminal with its control
    //    characters replaced.
    internal static class InteractiveMonitoring
    {
        private static readonly SettingsMenuWords Words = new SettingsMenuWords(W.MonitoringTitle, W.Monitoring, W.BackToMonitoring,
            W.NoFolder, W.FolderMissing, W.ContainerFolderMissing);

        internal static InteractiveMonitoringSeams Seams = new InteractiveMonitoringSeams();

        // the main menu's Monitoring
        internal static InteractiveStep Run(InteractiveSession Session)
        {
            InteractivePrompts Prompts = Session.Prompts;
            //since D130 (PT1): a screen of its own, started on a cleared screen on arrival and on each return to it
            InteractiveScreen Here = Prompts.Screen();
            Here.Show();
            Prompts.Heading(W.MonitoringTitle);
            string? Folder = InteractiveSettingsMenu.SettingsFolder(Session, Words);
            if (Folder == null)
            {
                return InteractiveStep.Done;
            }
            InteractiveMonitoringHost Host = HostOf(Session, Folder);
            Prompts.Say(W.Intro);
            string? Notice = null;
            bool First = true;
            while (true)
            {
                if (!First)
                {
                    Here.Show();
                    Prompts.Heading(W.MonitoringTitle);
                }
                First = false;
                MonitoringControlView V = Host.Controller.View();
                ShowStatus(Prompts, V);
                if (Notice != null)
                {
                    Prompts.Say(Safe(Notice));
                    Notice = null;
                }
                var Choices = new List<string>();
                var Actions = new List<Func<InteractiveStep?>>();
                if (V.CanStart)
                {
                    Choices.Add(MonitoringWording.StartLabel);
                    Actions.Add(() => Act(Session, Host, V, MonitoringControlAction.Start, out Notice));
                }
                if (V.CanStop)
                {
                    Choices.Add(MonitoringWording.StopLabel);
                    Actions.Add(() => Act(Session, Host, V, MonitoringControlAction.Stop, out Notice));
                }
                if (V.CanRestart)
                {
                    Choices.Add(MonitoringWording.RestartLabel);
                    Actions.Add(() => Act(Session, Host, V, MonitoringControlAction.Restart, out Notice));
                }
                Choices.Add(W.ShowLatest);
                Actions.Add(() => null);
                if (V.LastCheck is MonitoringControlCheck Check && Check.Searches.Count > 0)
                {
                    Choices.Add(W.ShowLastCheck);
                    Actions.Add(() => ShowLastCheck(Prompts, Check, V) == ListEnd.EndOfInput ? InteractiveStep.EndOfInput : null);
                }
                Choices.Add(InteractiveSettingsWording.BackToMainMenu);
                PromptResult<int> Picked = Prompts.Choose(MonitoringWording.ActionsHeading, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return InteractiveStep.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == Choices.Count - 1)
                {
                    //Back, Esc or Ctrl+C: the main menu again; the monitoring goes on
                    return InteractiveStep.Done;
                }
                InteractiveStep? Step = Actions[Picked.Value]();
                if (Step != null)
                {
                    return Step.Value;
                }
            }
        }

        // the console's one monitoring: made when Monitoring is first opened (since phase 12, D128, or the guided setup's done
        // step is shown), then this console's until it ends (never a menu's, never made again)
        internal static InteractiveMonitoringHost HostOf(InteractiveSession Session, string Folder)
        {
            if (Session.Monitoring is InteractiveMonitoringHost Kept)
            {
                return Kept;
            }
            InteractiveMonitoringSeams With = Seams;
            var Host = new InteractiveMonitoringHost(Folder, CacheOf(Session)?.Path, Session.Hold(With.Time), With, Text =>
            {
                try
                {
                    Session.Prompts.Say(Text);
                }
                catch (IOException)
                {
                    //the terminal is gone
                }
            });
            Session.Monitoring = Host;
            return Host;
        }

        // the cache folder the console found (never created), and whether it runs in a container
        private static (string? Path, bool Container)? CacheOf(InteractiveSession Session)
        {
            try
            {
                CampertronInternalConfig? Folders = Session.Folders();
                return Folders == null ? null : (Folders.CachePath, Folders.ContainerMode);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException
                || ex is System.Security.SecurityException || ex is InvalidOperationException)
            {
                //no folders (Load.DesktopDataFolder's refusal): the controller's start says so in the shared words
                return null;
            }
        }

        // ---- the state, in the shared words ----

        private static void ShowStatus(InteractivePrompts Prompts, MonitoringControlView V)
        {
            MonitoringStatus S = V.Status;
            Fact(Prompts, MonitoringWording.StatusHeading, W.StateText(V));
            if (V.ShowsFacts)
            {
                MonitoringControlStart? Start = V.ThisStart;
                Fact(Prompts, MonitoringWording.StartedLabel, W.StartedText(V));
                if (Start != null && Start.Started && Start.Output != null)
                {
                    Fact(Prompts, MonitoringWording.NotificationsLabel, Start.Output);
                }
                Fact(Prompts, MonitoringWording.SearchesLabel, MonitoringWording.SearchesText(S.Searches));
                Fact(Prompts, MonitoringWording.LastFinishedLabel, MonitoringWording.LastFinishedText(S));
                if (S.NextCheckAt is DateTime Next)
                {
                    Fact(Prompts, MonitoringWording.NextCheckLabel, MonitoringWording.When(Next));
                }
            }
            if (S.IsRunning && S.SearchFilesChangedSinceStart)
            {
                Prompts.Say(MessageSeverity.AccessibleText(MonitoringWording.FilesChanged, MessageSeverity.Warning));
            }
            if (V.ShowsFailure)
            {
                Problem(Prompts, V.ThisEnd!.Lines, MessageSeverity.Error);
            }
            if (V.ShowsStartRefused)
            {
                MonitoringControlStart Refused = V.ThisStart!;
                Problem(Prompts, Refused.Lines, Refused.Problem == MonitoringStartProblem.NotMonitoring ? MessageSeverity.Info : MessageSeverity.Error);
            }
            if (V.Cooldown != null)
            {
                Prompts.Say(MessageSeverity.AccessibleText(V.Cooldown, MessageSeverity.Warning));
            }
            if (V.LastCheck is MonitoringControlCheck Check)
            {
                Fact(Prompts, MonitoringWording.LastCheckHeading, MonitoringWording.CheckSummary(Check, Ended(V)));
            }
            else if (V.ShowsFacts)
            {
                Fact(Prompts, MonitoringWording.LastCheckHeading, MonitoringWording.NoCheckYet);
            }
            Prompts.Say(W.Unavailable(V));
        }

        private static bool Ended(MonitoringControlView V) => !V.Status.IsRunning && V.Busy == MonitoringControlBusy.None;

        private static void Fact(InteractivePrompts Prompts, string Label, string Value) => Prompts.Say("  " + Label + ": " + Safe(Value), "    ");

        // a refused start's or a failure's lines: its severity before the first, the others below it
        private static void Problem(InteractivePrompts Prompts, IReadOnlyList<string> Lines, string Severity)
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                Prompts.Say(i == 0 ? Safe(MessageSeverity.AccessibleText(Lines[i], Severity)) : "  " + Safe(Lines[i]), "    ");
            }
        }

        // ---- an action ----

        // Start, Stop or Restart for the moment the menu showed (Shown): Start and Restart after a y (No by default); then the
        // wait for it. Notice: what the menu says next (why nothing was done; null when the controller accepted it). Null: the
        // menu again; else the console's step. Since phase 12 (D128) also the guided setup's done step's Start
        internal static InteractiveStep? Act(InteractiveSession Session, InteractiveMonitoringHost Host, MonitoringControlView Shown,
            MonitoringControlAction Action, out string? Notice)
        {
            InteractivePrompts Prompts = Session.Prompts;
            Notice = null;
            if (Action != MonitoringControlAction.Stop)
            {
                Prompts.Say(Action == MonitoringControlAction.Start ? MonitoringWording.StartHelp : MonitoringWording.RestartHelp);
                PromptResult<bool> Sure = Prompts.Confirm(Action == MonitoringControlAction.Start ? W.StartQuestion : W.RestartQuestion, false);
                if (Sure.End == PromptEnd.EndOfInput || Sure.End == PromptEnd.Unavailable)
                {
                    return InteractiveStep.EndOfInput;
                }
                if (!Sure.IsAnswered || !Sure.Value)
                {
                    Notice = Action == MonitoringControlAction.Start ? W.NotStarted : W.NotRestarted;
                    return null;
                }
                //the console never creates the cache folder (D126's rule): a start without it starts nothing
                if (CacheOf(Session) is (string Cache, bool Container) && Path.IsPathFullyQualified(Cache) && !Directory.Exists(Cache))
                {
                    Notice = Container ? W.ContainerCacheFolderMissing : W.CacheFolderMissing;
                    return null;
                }
            }
            else
            {
                Prompts.Say(W.StopHelp);
            }
            MonitoringControlOutcome Outcome = Host.Controller.Act(Action, Shown.Token);
            if (Outcome != MonitoringControlOutcome.Accepted)
            {
                Notice = W.Outcome(Outcome, Host.Controller.View().Cooldown);
                return null;
            }
            return WaitFor(Prompts, Host);
        }

        // The action the controller accepted, until it has ended (the controller is no longer busy) or ActionWait has passed,
        // the console's thread reading the keys as they are typed: Esc and Ctrl+C stop the waiting (the action goes on), the
        // end of the input ends the console. Asks nothing meanwhile. Null: the menu again (it shows how the action went)
        private static InteractiveStep? WaitFor(InteractivePrompts Prompts, InteractiveMonitoringHost Host)
        {
            InteractiveMonitoringSeams With = Seams;
            Prompts.Say(W.WaitHow);
            long Began = With.Time.GetTimestamp();
            using (Prompts.Terminal.ReadKeysAsTheyAreTyped())
            {
                while (Host.Controller.View().Phase == MonitoringControlPhase.Busy)
                {
                    TimeSpan Waited = With.Time.GetElapsedTime(Began);
                    if (Waited >= With.ActionWait)
                    {
                        Prompts.Say(W.StillBusy(With.ActionWait));
                        return null;
                    }
                    while (Prompts.Terminal.NextKeyTyped() is TerminalInputKind Key)
                    {
                        if (Key == TerminalInputKind.Escape || Key == TerminalInputKind.Interrupt)
                        {
                            Prompts.Say(W.StoppedWaiting);
                            return null;
                        }
                        if (Key == TerminalInputKind.EndOfInput)
                        {
                            Prompts.Say(W.InputEndedWhileWaiting);
                            return InteractiveStep.EndOfInput;
                        }
                    }
                    Thread.Sleep(With.PollInterval);
                }
            }
            return null;
        }

        // ---- the last check ----

        // the last check's searches (the controller's snapshot of the check the menu showed), each with its new openings a part
        // at a time, until Back, Esc, Ctrl+C or the end of the input
        private static ListEnd ShowLastCheck(InteractivePrompts Prompts, MonitoringControlCheck Check, MonitoringControlView Shown)
        {
            InteractiveScreen Here = Prompts.Screen();
            while (true)
            {
                Here.Show();
                Prompts.Heading(MonitoringWording.LastCheckHeading);
                Prompts.Say(Safe(MonitoringWording.CheckSummary(Check, Ended(Shown))));
                Prompts.Say(W.LastCheckNote);
                if (Check.Searches.Count == 1)
                {
                    return InteractiveResultsView.ShowSearch(Prompts, Check.Searches[0], W.BackToMonitoring, LinesOf(Check.Searches[0]));
                }
                Prompts.Say(W.ChooseSearch);
                var Choices = Check.Searches.Select(S => Safe(MonitoringWording.SearchLabel(S))).Append(W.BackToMonitoring).ToList();
                PromptResult<int> Picked = Prompts.Choose(MonitoringWording.ResultsOfName, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return ListEnd.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == Choices.Count - 1)
                {
                    return ListEnd.Back;
                }
                ResultSearchViewModel Search = Check.Searches[Picked.Value];
                if (InteractiveResultsView.ShowSearch(Prompts, Search, W.BackToLastCheck, LinesOf(Search)) == ListEnd.EndOfInput)
                {
                    return ListEnd.EndOfInput;
                }
            }
        }

        // what the web UI's monitoring page says of a search of the last check above its rows: its counts when it found new
        // openings, its campground, and that it found none
        private static IReadOnlyList<string> LinesOf(ResultSearchViewModel Search)
        {
            var Lines = new List<string>();
            if (Search.Openings > 0)
            {
                Lines.Add(Search.CountsText);
            }
            Lines.Add(Search.CampgroundText);
            if (Search.Openings == 0)
            {
                Lines.Add(MonitoringWording.NoNewOpening);
            }
            return Lines;
        }

        // ---- the console's end while the monitoring is on ----

        // The main menu's Exit (D115's rule for an application's end, at a terminal): while the monitoring is on (running and
        // not asked to stop, starting or restarting) it asks first: Keep Campertron open (the default: Esc, Ctrl+C or no usable
        // answer keep it open too) or Stop the monitoring and exit (the console's end then stops it, InteractiveConsole). The
        // end of the input ends the console, which stops it. Without a monitoring that is on, Exit ends the console at once
        internal static InteractiveStep Exit(InteractiveSession Session)
        {
            if (Session.Monitoring is not InteractiveMonitoringHost Host || !Host.Controller.View().IsActive)
            {
                return InteractiveStep.Exit;
            }
            InteractivePrompts Prompts = Session.Prompts;
            Prompts.Heading(W.ExitHeading);
            Prompts.Say(W.ExitText);
            PromptResult<int> Picked = Prompts.Choose(W.ExitQuestion, new[] { W.KeepOpen, W.StopAndExit });
            if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
            {
                return InteractiveStep.EndOfInput;
            }
            if (Picked.IsAnswered && Picked.Value == 1)
            {
                return InteractiveStep.Exit;
            }
            Prompts.Say(W.KeptOpen);
            return InteractiveStep.Done;
        }

        // a text of a file as a terminal may show it: every control character replaced
        private static string Safe(string? Text) => InteractiveResults.Safe(Text);
    }
}
