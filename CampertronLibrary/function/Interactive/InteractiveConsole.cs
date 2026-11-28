using System.Globalization;
using System.Reflection;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronLibrary.function.Interactive
{
    // what a choice of the main menu asks the console to do next
    public enum InteractiveStep
    {
        // done: the main menu again
        Done,
        // the user cancelled one of its questions (Esc or Ctrl+C): "Cancelled.", then the main menu again
        Cancelled,
        // the input ended: the console ends (exit code 0)
        EndOfInput,
        // Exit: the console ends (exit code 0)
        Exit,
        // since v0.4.0's phase 9 (D125): the process was asked to stop (SIGTERM, SIGHUP) while an operation ran, which was
        // waited for; the console ends (exit code 0, as web mode's stop)
        Stopped
    }

    // one choice of the main menu: its label and what it does. A later phase adds its own before Help
    public sealed class InteractiveMenuItem
    {
        public string Label { get; }
        public Func<InteractiveSession, InteractiveStep> Run { get; }

        public InteractiveMenuItem(string Label, Func<InteractiveSession, InteractiveStep> Run)
        {
            this.Label = Label ?? throw new ArgumentNullException(nameof(Label));
            this.Run = Run ?? throw new ArgumentNullException(nameof(Run));
        }
    }

    // where the console's choices find what they need: its terminal and questions, the program (its version), the clock,
    // and where the settings and cache folders are (Load.FolderLocations: found, never created)
    public sealed class InteractiveSession
    {
        public InteractivePrompts Prompts { get; }
        public InteractiveTerminal Terminal => Prompts.Terminal;
        public Assembly App { get; }
        public Func<DateTime> UtcNow { get; }
        public Func<CampertronInternalConfig> Folders { get; }

        // SetUp (since D130): the first start's own preparation, which creates the settings and cache folders, the starting
        // General.yaml and Email.yaml and the bundled campground data (Load.GetConfig, as the start without arguments and the
        // desktop app prepare a first start); called only by the guided setup, after the user's y, for a missing folder
        public Func<CampertronInternalConfig> SetUp { get; }

        public InteractiveSession(InteractiveTerminal Terminal, Assembly App, Func<DateTime> UtcNow, Func<CampertronInternalConfig> Folders,
            Func<CampertronInternalConfig>? SetUp = null)
        {
            Prompts = new InteractivePrompts(Terminal);
            this.App = App ?? throw new ArgumentNullException(nameof(App));
            this.UtcNow = UtcNow ?? throw new ArgumentNullException(nameof(UtcNow));
            this.Folders = Folders ?? throw new ArgumentNullException(nameof(Folders));
            this.SetUp = SetUp ?? Load.GetConfig;
        }

        // since v0.4.0's phase 10 (D126): this session's Results (the list's choice, the last run's results, the five minutes
        // after a block), kept between visits of Results and gone when the console ends; null until Results is first opened
        internal Results.InteractiveResultsState? ResultsState { get; set; }

        // since v0.4.0's phase 11 (D127): this console's monitoring (the shared MonitoringController), the application's and no
        // menu's: made when Monitoring is first opened, stopped at the console's every end (EndMonitoring); null until then
        internal Monitoring.InteractiveMonitoringHost? Monitoring { get; set; }

        // since v0.4.0's phase 11 (D127): the five minutes after a block this console's Results and its monitoring share (as the
        // desktop GUI's window and the web UI each share one), made at its first use with that user's clock
        private BlockHold? _Hold;
        internal BlockHold Hold(TimeProvider Time) => _Hold ??= new BlockHold(Time);

        // The console ends (any end): its monitoring, if it has one, is stopped and waited for (bounded), said in lines when
        // Speak and there was a monitoring to stop. Once; never throws for a terminal that is gone
        internal void EndMonitoring(bool Speak)
        {
            Monitoring?.End(Speak ? Text =>
            {
                try
                {
                    Prompts.Say(Text);
                }
                catch (IOException)
                {
                    //the terminal is gone: nothing more is said
                }
            } : null);
        }
    }

    // CampertronConsole --interactive (v0.4.0's phase 6; D115, D121; business-rules §11): the interactive console, opened
    // only by that argument (ConsoleArguments), never because a terminal is attached; the start without arguments, its menu
    // and the monitoring are unchanged beside it.
    //
    // It needs a person at a terminal (standard input and output both a terminal, as the no-argument menu does) and
    // otherwise says so and ends with exit code 1, having read and created nothing. It reads no settings to start and needs
    // none to be valid: the main menu shows at once, line by line (no cursor movement; since D130 each new screen starts on a
    // cleared screen where the terminal can, and the main menu shows the Campertron logo at its top), its choices numbered: Status (read only), Notifications (since phase 7, D122: Email.yaml and Ntfy.yaml in
    // their shared forms, InteractiveNotifications), Settings (since phase 7a, D123: General.yaml and Web.yaml in their
    // shared forms, InteractiveSettings), Searches (since phase 8, D124: the search files in their shared forms,
    // InteractiveSearches), Campgrounds (since phase 9, D125: the campground lookup, the campground data's status and its
    // refresh, InteractiveCampgrounds), Results (since phase 10, D126: the saved searches run now through the shared search
    // service and their results, InteractiveResults), Monitoring (since phase 11, D127: the unattended monitoring through the
    // shared MonitoringController, the console's and no menu's, InteractiveMonitoring), Guided setup (since phase 12, D128:
    // the shared guided setup's steps over the console's own forms, InteractiveGuidedSetup), Help and Exit. A later phase
    // adds its choices to MainMenu. Nothing starts or searches by itself; a file is saved, created, a test sent, the campground data
    // refreshed, a search run or the monitoring started, stopped or restarted only when the user chooses it (and, but for a
    // search's Start and the monitoring's Stop, confirms it).
    //
    // Its ends and exit codes: Exit, and the end of the input (Ctrl+D on an empty line, on Windows also Ctrl+Z, or a terminal
    // that is gone, which the terminal's IOException says), end it with 0; Ctrl+C at the main menu ends it with "Stopped." and 130, the code a Ctrl+C gives
    // CampertronConsole's other starts on Linux and macOS; a refused start and an unexpected problem end it with 1, in fixed
    // words, never an exception's text or a stack trace. Esc at the main menu has nothing to cancel and the menu is shown
    // again; below the main menu Esc and the first Ctrl+C cancel the question and return to the main menu. A Ctrl+C while
    // the console is not waiting for an answer (nor, since D125, running the refresh, which reads it as a key, nor, since
    // D126, a search of Results, which does too) is ConsoleUi's, as in every start: "Stopped." and the process ends.
    //
    // Since phase 11 (D127) a monitoring the user started goes on while the console runs, whatever menu is shown, and the
    // console's every end stops it first, orderly and bounded (InteractiveMonitoringHost.End), saying so: Exit while it is on
    // asks first (Keep Campertron open, the default, or Stop the monitoring and exit), and Ctrl+C at the main menu, the end
    // of the input, a terminal that is gone and an unexpected problem stop it before the console ends with its usual code; a
    // signal that ends the process (SIGTERM, SIGHUP, a Ctrl+C outside a question) stops it before the signal's own handling.
    // Nothing is kept, so a later start of the console never starts it again.
    public static class InteractiveConsole
    {
        public const int ExitCodeDone = 0;
        public const int ExitCodeRefused = 1;
        public const int ExitCodeStopped = 130;

        // Program.Main's --interactive: the console on this process's terminal
        public static int RunInTerminal(Assembly App)
        {
            try
            {
                ConsoleUi.Capabilities Caps = ConsoleUi.Current;
                if (!Caps.Interactive)
                {
                    ConsoleUi.Error(InteractiveWording.NeedsTerminal);
                    return ExitCodeRefused;
                }
                ConsoleUi.UseUtf8Output();
                ConsoleUi.InstallTerminalHandlers();
                return Run(new InteractiveSession(new ConsoleTerminal(Caps, WindowWidth: () => Console.WindowWidth), App, () => DateTime.UtcNow, Load.FolderLocations));
            }
            catch (Exception ex)
            {
                ConsoleUi.RestoreTerminal();
                try
                {
                    ConsoleUi.Error(InteractiveWording.ConsoleFailed(ex.GetType().Name));
                }
                catch (IOException)
                {
                    //nothing to report to
                }
                return ExitCodeRefused;
            }
        }

        // the main menu's choices, in order; a later phase adds its own before Help
        public static List<InteractiveMenuItem> MainMenu()
        {
            return new List<InteractiveMenuItem>
            {
                new InteractiveMenuItem(InteractiveWording.Status, ShowStatus),
                //v0.4.0's phase 7 (D122): Email.yaml and Ntfy.yaml in their shared forms, with their tests and the random topic
                new InteractiveMenuItem(Settings.InteractiveSettingsWording.Notifications, Settings.InteractiveNotifications.Run),
                //v0.4.0's phase 7a (D123): General.yaml and Web.yaml in their shared forms
                new InteractiveMenuItem(Settings.InteractiveSettingsWording.Settings, Settings.InteractiveSettings.Run),
                //v0.4.0's phase 8 (D124): the search files in their shared forms
                new InteractiveMenuItem(Searches.InteractiveSearchesWording.Searches, Searches.InteractiveSearches.Run),
                //v0.4.0's phase 9 (D125): the campground lookup, the campground data's status and its refresh
                new InteractiveMenuItem(Campgrounds.InteractiveCampgroundsWording.Campgrounds, Campgrounds.InteractiveCampgrounds.Run),
                //v0.4.0's phase 10 (D126): the saved searches run now through the shared search service, and their results
                new InteractiveMenuItem(Results.InteractiveResultsWording.Results, Results.InteractiveResults.Run),
                //v0.4.0's phase 11 (D127): the unattended monitoring through the shared monitoring controller
                new InteractiveMenuItem(Monitoring.InteractiveMonitoringWording.Monitoring, Monitoring.InteractiveMonitoring.Run),
                //v0.4.0's phase 12 (D128): the guided setup through the shared guided setup's orchestration
                new InteractiveMenuItem(GuidedSetup.InteractiveGuidedSetupWording.GuidedSetup, GuidedSetup.InteractiveGuidedSetup.Run),
                new InteractiveMenuItem(InteractiveWording.Help, ShowHelp),
                //since phase 11: while the monitoring is on, Exit asks first
                new InteractiveMenuItem(InteractiveWording.Exit, Monitoring.InteractiveMonitoring.Exit)
            };
        }

        // the console in a session: the title and the main menu until Exit, Ctrl+C at the menu or the end of the input
        public static int Run(InteractiveSession Session, IReadOnlyList<InteractiveMenuItem>? Items = null)
        {
            IReadOnlyList<InteractiveMenuItem> Menu = Items ?? MainMenu();
            InteractivePrompts Prompts = Session.Prompts;
            try
            {
                List<string> Labels = Menu.Select(Item => Item.Label).ToList();
                //since D130 (PT1): the main menu is a screen of its own, with the logo at its top, started on a cleared screen
                //at the start and whenever the user comes back to it; the title and the introduction once, at the start
                InteractiveScreen MainScreen = Prompts.Screen(Main: true);
                bool First = true;
                while (true)
                {
                    MainScreen.Show();
                    if (First)
                    {
                        Prompts.Heading(InteractiveWording.Title);
                        Prompts.Say(InteractiveWording.Introduction);
                        First = false;
                    }
                    PromptResult<int> Choice = Prompts.Choose(InteractiveWording.MainMenu, Labels, null);
                    switch (Choice.End)
                    {
                        case PromptEnd.Answered:
                            InteractiveStep Step = RunChoice(Session, Menu[Choice.Value]);
                            if (Step == InteractiveStep.Exit)
                            {
                                Session.EndMonitoring(true);
                                return ExitCodeDone;
                            }
                            if (Step == InteractiveStep.EndOfInput)
                            {
                                Session.EndMonitoring(true);
                                Prompts.Say(InteractiveWording.InputEnded);
                                return ExitCodeDone;
                            }
                            if (Step == InteractiveStep.Stopped)
                            {
                                Session.EndMonitoring(true);
                                Prompts.Say(InteractiveWording.AskedToStop);
                                return ExitCodeDone;
                            }
                            if (Step == InteractiveStep.Cancelled)
                            {
                                //said at the top of the main menu drawn again
                                Prompts.Carry(InteractiveWording.Cancelled);
                            }
                            break;
                        case PromptEnd.Interrupted:
                            Session.EndMonitoring(true);
                            Session.Terminal.WriteStopped();
                            return ExitCodeStopped;
                        case PromptEnd.Cancelled:
                            Prompts.Say(InteractiveWording.NothingToCancel);
                            break;
                        default:
                            //the end of the input (a terminal never answers Unavailable to a line)
                            Session.EndMonitoring(true);
                            Prompts.Say(InteractiveWording.InputEnded);
                            return ExitCodeDone;
                    }
                }
            }
            catch (IOException)
            {
                //the terminal is gone (a closed window whose hang-up signal was ignored): nothing can be read or shown any more,
                //which is the end of the input
                Session.EndMonitoring(false);
                return ExitCodeDone;
            }
            catch (Exception ex)
            {
                try
                {
                    Session.Terminal.WriteError(InteractiveWording.ConsoleFailed(ex.GetType().Name));
                }
                catch (IOException)
                {
                    //nothing to report to
                }
                Session.EndMonitoring(true);
                return ExitCodeRefused;
            }
            finally
            {
                //whatever ended the console, its monitoring never outlives it (once: an end above already stopped it)
                Session.EndMonitoring(false);
            }
        }

        // a choice's problem is said in fixed words (its kind only) and the main menu comes again
        private static InteractiveStep RunChoice(InteractiveSession Session, InteractiveMenuItem Item)
        {
            try
            {
                return Item.Run(Session);
            }
            catch (Exception ex)
            {
                Session.Prompts.Error(InteractiveWording.ChoiceFailed(ex.GetType().Name));
                return InteractiveStep.Done;
            }
        }

        private static InteractiveStep ShowHelp(InteractiveSession Session)
        {
            Session.Prompts.Screen().Show();
            Session.Prompts.Heading(InteractiveWording.Help);
            foreach (string Line in InteractiveWording.HelpLines)
            {
                Session.Prompts.Say("  " + Line, "    ");
            }
            return InteractiveStep.Done;
        }

        private static InteractiveStep ShowStatus(InteractiveSession Session)
        {
            Session.Prompts.Screen().Show();
            Session.Prompts.Heading(InteractiveWording.StatusTitle);
            foreach ((string Label, string Value) in Status(Session))
            {
                Session.Prompts.Say("  " + Label + ": " + Value, "    ");
            }
            return InteractiveStep.Done;
        }

        // The status: what is set up, read only and redacted. The version; the settings folder (its path, as View Config
        // shows it); for General.yaml whether it can be used and its outputTo (read through CurrentConfiguration.ReadGeneral,
        // which never creates it); for Email.yaml, Ntfy.yaml and Web.yaml only whether they exist (never opened); how many
        // search files there are (their names only, as View Config counts them); and whether the campground data is
        // prepared (the database file exists; never opened). No other value of a file, no secret, no environment variable;
        // nothing is created, written, started or sent. A fact that can't be found is "unknown".
        internal static List<(string Label, string Value)> Status(InteractiveSession Session)
        {
            var Facts = new List<(string, string)>
            {
                (InteractiveWording.VersionLabel, Fact(() => RuntimeInfo.Describe(Session.App).Split(Environment.NewLine)[0]))
            };
            CampertronInternalConfig? Folders = null;
            try
            {
                Folders = Session.Folders();
            }
            catch (Exception ex) when (IsReadProblem(ex) || ex is InvalidOperationException)
            {
                //no folder for the settings (Load.DesktopDataFolder's refusal): nothing else can be found
            }
            if (Folders?.ConfigPath == null)
            {
                Facts.Add((InteractiveWording.FolderLabel, InteractiveWording.NoFolder));
                return Facts;
            }
            string Settings = Folders.ConfigPath;
            bool FolderExists = Fact(() => Directory.Exists(Settings), false);
            Facts.Add((InteractiveWording.FolderLabel, Settings + (FolderExists ? string.Empty
                : Folders.ContainerMode ? InteractiveWording.ContainerFolderMissing : InteractiveWording.FolderMissing)));
            Facts.Add((ConfigFiles.GeneralFileName, Fact(() =>
            {
                if (!File.Exists(Path.Join(Settings, ConfigFiles.GeneralFileName)))
                {
                    return InteractiveWording.GeneralMissing;
                }
                GeneralConfig? General = CurrentConfiguration.ReadGeneral(Settings, Session.UtcNow());
                return General == null ? InteractiveWording.GeneralUnusable : InteractiveWording.GeneralFound(General.OutputTo.ToString());
            })));
            foreach (string Name in new[] { ConfigFiles.EmailFileName, ConfigFiles.NtfyFileName, ConfigFiles.WebFileName })
            {
                Facts.Add((Name, Fact(() => File.Exists(Path.Join(Settings, Name)) ? InteractiveWording.Found : InteractiveWording.NotFound)));
            }
            Facts.Add((InteractiveWording.SearchFilesLabel, Fact(() => FolderExists
                ? CurrentConfiguration.SearchFileCount(Settings).ToString("N0", CultureInfo.CurrentCulture)
                : 0.ToString(CultureInfo.CurrentCulture))));
            Facts.Add((InteractiveWording.CampgroundDataLabel, Fact(() => Load.DbExists(new CampertronInternalConfig { ConfigPath = Settings })
                ? InteractiveWording.DataPrepared : InteractiveWording.DataMissing)));
            return Facts;
        }

        private static bool IsReadProblem(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException;

        private static string Fact(Func<string> Find) => Fact(Find, InteractiveWording.Unknown);

        private static T Fact<T>(Func<T> Find, T Otherwise)
        {
            try
            {
                return Find();
            }
            catch (Exception ex) when (IsReadProblem(ex))
            {
                return Otherwise;
            }
        }
    }
}
