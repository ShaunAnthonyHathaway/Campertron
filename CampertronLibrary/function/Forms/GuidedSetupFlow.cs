using CampertronLibrary.function.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CampertronLibrary.function.Forms
{
    // The guided setup's orchestration (v0.4.0's phase 3, D117; business-rules-settings §37): the order of its steps, a
    // step's state in the list of steps, Back, and what each step's action does and where it leads. Until phase 3 it was the
    // web UI's page (GuidedSetupModel and its view); it moved here unchanged, so that every host of the guided setup takes
    // the same steps with the same rules, and the web UI is its only host so far (the desktop GUI's guided setup is phase 5,
    // the console's phase 12).
    //  - Where the user is comes from the saved files only (GuidedSetupStatus, read again after each save): nothing is kept
    //    here, no progress and no record.
    //  - Every change goes through the session's own forms of the settings and searches pages (the layer's
    //    SettingsFileViewModel, SearchesViewModel, GeneralSettingsViewModel): their Save with its checks (D71, D73), their
    //    New search, their backups and secret rules (ER-18, D111, D112). Nothing here writes, reads or judges a file itself,
    //    reads a secret, sends anything, searches, refreshes or starts the monitoring.
    //  - A host keeps its own: how it shows a step and its addresses, how a user's entries reach the forms, its revisions of
    //    what it showed (an out-of-date page applies nothing: the web UI's), its focus (each outcome says what needs the
    //    user's attention; the host gives it to its own element), its test of the notification file and its own sentences
    //    (GuidedSetupHostWords).
    // No UI toolkit and no static state that changes (ER-37); a form is used by one thread at a time.

    // a step's state in the list of steps, in words (GuidedSetupWording.StepState), never by color alone
    public enum GuidedSetupStepState
    {
        Done,
        Current,
        ToDo,
    }

    // what an action's outcome asks the host to draw the user's attention to (the host's own element for it): the step it
    // leads to (its heading), the form's problems, the form's result (a save that did not go through), the form's conflict
    // with the file on disk, what the monitoring's start would refuse in the saved file, the step's own message (Notice) or
    // New search's message
    public enum GuidedSetupAttention
    {
        Step,
        Problems,
        Status,
        Conflict,
        Needs,
        Notice,
        NewSearch,
    }

    // The sentences of the guided setup's orchestration that only a host knows (as MonitoringHostWords are the monitoring's)
    public sealed class GuidedSetupHostWords
    {
        // turning the monitoring on refused because General.yaml has unsaved changes in the host's own editor of it
        public required string GeneralUnsaved { get; init; }
    }

    // What one action of a step did, for its host to show. Immutable
    public sealed class GuidedSetupOutcome
    {
        private GuidedSetupOutcome(GuidedSetupStep? Next, string? Notice, GuidedSetupAttention Attention, GuidedSetupStatus? Status, string? CreatedFile, bool FormActed)
        {
            this.Next = Next;
            this.Notice = Notice;
            this.Attention = Attention;
            this.Status = Status;
            this.CreatedFile = CreatedFile;
            this.FormActed = FormActed;
        }

        // the step the user goes on to, or null: the user stays on the step
        public GuidedSetupStep? Next { get; }
        // what is said, once on the step it leads to or on the step itself, its severity's word first ("Info: ",
        // "Warning: ", "Error: "); null when the step says nothing more
        public string? Notice { get; }
        public GuidedSetupAttention Attention { get; }
        // the saved files as the action read them last (null when it read none)
        public GuidedSetupStatus? Status { get; }
        // New search's new file, whose own page of the search step is where it leads (null: none was created)
        public string? CreatedFile { get; }
        // the action ran a form's create or save (a host that keeps revisions of what it showed changes them)
        public bool FormActed { get; }

        internal static GuidedSetupOutcome GoTo(GuidedSetupStep Next, string? Notice, GuidedSetupStatus? Status, bool FormActed = false) =>
            new GuidedSetupOutcome(Next, Notice, GuidedSetupAttention.Step, Status, null, FormActed);

        internal static GuidedSetupOutcome Stay(string? Notice, GuidedSetupAttention Attention, GuidedSetupStatus? Status, bool FormActed = false) =>
            new GuidedSetupOutcome(null, Notice, Attention, Status, null, FormActed);

        internal static GuidedSetupOutcome Created(string File, string Message) =>
            new GuidedSetupOutcome(GuidedSetupStep.Search, Message, GuidedSetupAttention.Notice, null, File, false);
    }

    public static class GuidedSetupFlow
    {
        // ---- the steps ----

        // the steps in their order, as the list of steps shows them (Done is the result, not a step)
        public static IReadOnlyList<GuidedSetupStep> Steps => new[] { GuidedSetupStep.Method, GuidedSetupStep.Notifications, GuidedSetupStep.Search, GuidedSetupStep.TurnOn };

        // Back: the step before this one (none before the first); the done page's is the last step
        public static GuidedSetupStep? Previous(GuidedSetupStep Step) => Step switch
        {
            GuidedSetupStep.Notifications => GuidedSetupStep.Method,
            GuidedSetupStep.Search => GuidedSetupStep.Notifications,
            GuidedSetupStep.TurnOn => GuidedSetupStep.Search,
            GuidedSetupStep.Done => GuidedSetupStep.TurnOn,
            _ => null,
        };

        // a step's state in the list of steps while Current is shown: the first step is done once a method is chosen, any
        // other once the saved files say so for the method (never without one)
        public static GuidedSetupStepState StateOf(GuidedSetupStep One, GuidedSetupStep Current, GuidedSetupMethod? Method, GuidedSetupStatus Status) =>
            One == Current ? GuidedSetupStepState.Current
            : One == GuidedSetupStep.Method ? (Method != null ? GuidedSetupStepState.Done : GuidedSetupStepState.ToDo)
            : Method is GuidedSetupMethod Known && Status.IsDone(Known, One) ? GuidedSetupStepState.Done : GuidedSetupStepState.ToDo;

        // the steps that are not done for the method, in their order (what the done page lists while the guided setup is
        // not finished)
        public static IReadOnlyList<GuidedSetupStep> ToDo(GuidedSetupMethod Method, GuidedSetupStatus Status) =>
            new[] { GuidedSetupStep.Notifications, GuidedSetupStep.Search, GuidedSetupStep.TurnOn }.Where(One => !Status.IsDone(Method, One)).ToList();

        // the method the guided setup resumes from where the saved files leave it (the start page's link): none once it is
        // complete (it opens again at its first step), else the method the files point to (none: the user chooses)
        public static GuidedSetupMethod? ResumeMethod(GuidedSetupStatus Status) => Status.Overall == GuidedSetupState.Complete ? null : Status.SuggestedMethod;

        // ---- the steps' actions ----

        // Step 1: the method chosen leads on, to its first step that is not done (nothing is saved: the choice is only where
        // it leads); nothing chosen, or anything else, is said
        public static GuidedSetupOutcome Choose(GuidedSetupMethod? Picked, GuidedSetupStatus Status) => Picked is GuidedSetupMethod Known
            ? GuidedSetupOutcome.GoTo(Status.NextStep(Known), null, Status)
            : GuidedSetupOutcome.Stay(GuidedSetupWording.MethodMissing, GuidedSetupAttention.Problems, Status);

        // Step 2's Save and continue, with the user's entries already in the method's form (Email.yaml's or Ntfy.yaml's):
        // what changed is saved by the form's own Save, with its checks; a value with its own error is never saved. It goes
        // on to the search once the form has nothing unsaved, no conflict and no problem, and the saved file is what the
        // monitoring needs; else what keeps it from going on has the user's attention: the form's problems, its failed save,
        // its conflict, else what the monitoring's start would refuse in the saved file
        public static GuidedSetupOutcome ContinueNotifications(SettingsFileViewModel Form, GuidedSetupMethod Method, string SettingsFolder)
        {
            if (Form.IsDirty || FormProblems.Any(Form))
            {
                Form.Save();
            }
            GuidedSetupStatus Status = Read(SettingsFolder);
            bool HasProblems = FormProblems.Any(Form);
            if (!Form.IsDirty && !Form.HasConflict && !HasProblems && Status.NotificationsReady(Method))
            {
                return GuidedSetupOutcome.GoTo(GuidedSetupStep.Search, SavedResult(Form), Status);
            }
            return GuidedSetupOutcome.Stay(null, HasProblems ? GuidedSetupAttention.Problems : Form.HasStatus && Form.StatusIsError ? GuidedSetupAttention.Status
                : Form.HasConflict ? GuidedSetupAttention.Conflict : GuidedSetupAttention.Needs, Status);
        }

        // Step 3's New search: the searches page's own command, with the name already in the form (the example search, not
        // enabled, named by the user). A new file leads to its own page of the step, which says the form's message once;
        // a name the form refuses stays, with the form's message
        public static GuidedSetupOutcome NewSearch(SearchesViewModel Searches)
        {
            Searches.NewSearchCommand.Execute(null);
            if (!Searches.NewSearchIsError && Searches.SelectedFile is SearchFileViewModel New && Searches.NewSearchMessage is string Message)
            {
                return GuidedSetupOutcome.Created(New.FileName, Message);
            }
            return GuidedSetupOutcome.Stay(null, GuidedSetupAttention.NewSearch, null);
        }

        // Step 3's Save and continue of a search, with the user's entries already in its form: saved as step 2 saves; it
        // goes on to the review once the form has nothing unsaved, no conflict and no problem, and at least one enabled
        // search can be checked; else the step says so (unless the form's save itself failed, which says why)
        public static GuidedSetupOutcome ContinueSearch(SearchFileViewModel Search, string SettingsFolder)
        {
            if (Search.IsDirty || FormProblems.Any(Search))
            {
                Search.Save();
            }
            GuidedSetupStatus Status = Read(SettingsFolder);
            bool HasProblems = FormProblems.Any(Search);
            if (!Search.IsDirty && !Search.HasConflict && !HasProblems && Status.SearchReady)
            {
                return GuidedSetupOutcome.GoTo(GuidedSetupStep.TurnOn, SavedResult(Search), Status);
            }
            string? Notice = !Search.HasStatus || !Search.StatusIsError ? GuidedSetupWording.SearchNotReady : null;
            return GuidedSetupOutcome.Stay(Notice, HasProblems ? GuidedSetupAttention.Problems : Notice != null ? GuidedSetupAttention.Notice : GuidedSetupAttention.Status, Status);
        }

        // Step 4's Turn the monitoring on, with the saved files as the host read them after looking at the disk: outputTo in
        // General.yaml through the General form, and nothing else, only when General.yaml has no unsaved change of the
        // user's and the monitoring's checks of the saved files pass for the method. It starts nothing: the host's monitoring
        // starts with the host, or by its own Start
        public static GuidedSetupOutcome TurnOn(GeneralSettingsViewModel General, GuidedSetupMethod Method, GuidedSetupStatus Status, string SettingsFolder, GuidedSetupHostWords Host)
        {
            if (General.IsDirty || General.HasConflict)
            {
                return GuidedSetupOutcome.Stay(Host.GeneralUnsaved, GuidedSetupAttention.Notice, Status);
            }
            if (!Status.FilesReady(Method))
            {
                return GuidedSetupOutcome.Stay(GuidedSetupWording.TurnOnNotReady, GuidedSetupAttention.Notice, Status);
            }
            if (Status.IsOn(Method))
            {
                return GuidedSetupOutcome.GoTo(GuidedSetupStep.Done, GuidedSetupWording.AlreadyOn, Status);
            }
            string? Problem = SaveOutputTo(General, Method);
            GuidedSetupStatus After = Read(SettingsFolder);
            if (Problem == null && After.IsOn(Method))
            {
                return GuidedSetupOutcome.GoTo(GuidedSetupStep.Done, GuidedSetupWording.Saved(General.FileName), After, FormActed: true);
            }
            return GuidedSetupOutcome.Stay(Problem ?? GuidedSetupWording.TurnOnNotReady, GuidedSetupAttention.Notice, After, FormActed: true);
        }

        // ---- inside ----

        private static GuidedSetupStatus Read(string SettingsFolder) => GuidedSetupStatus.Read(SettingsFolder, DateTime.Now);

        // the form's result after a save that went through, said once on the next step
        private static string? SavedResult(SettingsFileViewModel Form) => Form.HasStatus && !Form.StatusIsError && Form.StatusText != null ? "Info: " + Form.StatusText : null;

        // outputTo through the General form (General.yaml created first, with its defaults, when it does not exist), then
        // the form's own Save with its checks (D71, D73); null when it was saved, else the form's words. A save that did not
        // go through leaves no unsaved outputTo behind in the form
        private static string? SaveOutputTo(GeneralSettingsViewModel General, GuidedSetupMethod Method)
        {
            if (General.IsMissing)
            {
                General.CreateCommand.Execute(null);
                if (!General.IsEditable)
                {
                    return "Error: " + (General.StatusText ?? General.StateText ?? General.FileName + " can't be edited.");
                }
            }
            if (!General.IsEditable)
            {
                return "Error: " + (General.StateText ?? General.FileName + " can't be edited.");
            }
            OutputType Output = GuidedSetupStatus.OutputOf(Method);
            if (General.OutputTo.Choices.FirstOrDefault(Choice => Choice.Value is OutputType Value && Value == Output) is not SettingChoice Offered)
            {
                return GuidedSetupWording.TurnOnNotReady;
            }
            General.OutputTo.Selected = Offered;
            General.Save();
            if (General.IsDirty)
            {
                string Said = General.StatusText ?? GuidedSetupWording.TurnOnNotReady;
                General.Revert();
                return Said.StartsWith("Error: ", StringComparison.Ordinal) ? Said : "Error: " + Said;
            }
            return null;
        }
    }
}
