using System.Globalization;

namespace CampertronLibrary.function.Interactive
{
    // The interactive console's own words (v0.4.0's phase 6, D121; business-rules §11): its questions, its main menu, its
    // help and its status. They are the console's alone: neither the desktop GUI nor the web UI shows them (a word both UIs
    // show stays the shared form layer's, ER-37). Fixed words: no settings value, no secret and no exception's text.
    public static class InteractiveWording
    {
        // ---- the start ----

        // --interactive without a terminal on standard input and output (redirected, or no terminal at all): exit code 1
        public const string NeedsTerminal = "the interactive console needs an interactive terminal. Start CampertronConsole --interactive in a terminal, without redirecting its input or output.";

        public const string Title = "Campertron interactive console";
        public const string Introduction = "Type the number of a choice and press Enter. Esc or Ctrl+C cancels a question; Help says more.";

        // ---- the main menu ----

        public const string MainMenu = "Main menu";
        public const string Status = "Status";
        public const string Help = "Help";
        public const string Exit = "Exit";
        // Esc at the main menu: there is no question to cancel
        public const string NothingToCancel = "There is nothing to cancel here. Choose Exit to leave.";
        // a question below the main menu was cancelled: back to the main menu
        public const string Cancelled = "Cancelled.";
        // the end of the input: the console ends (exit code 0)
        public const string InputEnded = "The input ended, so the interactive console ended.";
        // an unexpected problem of a choice: never the exception's text, only its kind
        public static string ChoiceFailed(string Kind) => "That could not be done because of an unexpected problem (" + Kind + ").";
        public static string ConsoleFailed(string Kind) => "the interactive console stopped because of an unexpected problem (" + Kind + ").";

        // ---- questions ----

        public static string ChoosePrompt(int Count) => Count == 1 ? "Type 1 and press Enter: " : "Choose a number from 1 to " + Number(Count) + ": ";
        public static string NoChoice(int Count) => "Type the number of a choice, from 1 to " + Number(Count) + ", then press Enter.";
        public static string NotAChoice(int Count) => "That is not one of the choices. Type a number from 1 to " + Number(Count) + ".";
        public const string YesOrNo = "Type y for yes or n for no.";
        public const string OptionalSuffix = " (optional, Enter leaves it empty): ";
        public const string Required = "An answer is needed here. Type it, or press Esc to cancel.";
        public const string TooManyAttempts = "No usable answer after several tries, so the question was cancelled.";
        public const string SecretSuffix = " (not shown as you type): ";
        public const string SecretEmpty = "Nothing was typed. Type it, or press Esc to cancel.";
        public const string SecretNeedsTerminal = "This needs a terminal that can hide what you type, and this one can't, so nothing was read.";

        private static string Number(int Value) => Value.ToString(CultureInfo.InvariantCulture);

        // ---- help ----

        public static readonly IReadOnlyList<string> HelpLines = Array.AsReadOnly(new[]
        {
            "This is Campertron's interactive console, opened by CampertronConsole --interactive.",
            "Choices: type the number of a choice and press Enter.",
            "Questions: type the answer and press Enter. Where a question has a default, its letter is a capital, as in [y/N], and Enter alone chooses it.",
            "Cancel: Esc or Ctrl+C cancels the question you are answering and returns to the main menu.",
            "Exit: choose Exit. Ctrl+C at the main menu stops the console too, and so does Ctrl+D (on Windows also Ctrl+Z) on an empty line.",
            "Without --interactive, CampertronConsole starts as it always has: General.yaml's outputTo decides (the menu for Console or HtmlFile, the monitoring for Email or Ntfy). CampertronConsole --help lists every way to start it.",
            "Missing or incomplete settings can limit what Campertron can do. Status shows which settings files exist and whether the campground data is prepared, and changes nothing."
        });

        // ---- status ----

        public const string StatusTitle = "Status (read only: nothing is changed)";
        public const string VersionLabel = "Version";
        public const string FolderLabel = "Settings folder";
        public const string SearchFilesLabel = "Search files";
        public const string CampgroundDataLabel = "Campground data";
        public const string Unknown = "unknown";
        public const string NoFolder = "unknown: this computer gives Campertron no folder for its settings";
        public const string FolderMissing = " (not created yet: a start without arguments, or the desktop app, creates it)";
        public const string ContainerFolderMissing = " (not found: in a container, mount the settings folder at /config)";
        public const string Found = "found";
        public const string NotFound = "not found";
        public static string GeneralFound(string Output) => "found; outputTo: " + Output;
        public const string GeneralUnusable = "found, but it can't be used as it is: a start stops with its problem";
        public const string GeneralMissing = "not found: a start without arguments, or the desktop app, creates it (outputTo: Console)";
        public const string DataPrepared = "prepared";
        public const string DataMissing = "not prepared yet: a start without arguments, or the desktop app, prepares it";
    }
}
