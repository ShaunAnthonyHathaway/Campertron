using System.Security.Claims;
using CampertronLibrary.function.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CampertronWeb.Pages
{
    // The guided setup of the web UI (v0.3.0's PR 10, D80 point 4, D100; business-rules-settings §37): a short path for the
    // notification-only user (D79) through the forms the settings and searches pages show, the shared form layer's (D91), so
    // it writes the same files with the same validation, rules and words, and keeps nothing of its own:
    //   1 how you are notified (email or ntfy; nothing saved: the choice is the address),
    //   2 the notification settings (Email.yaml or Ntfy.yaml: the session's own form, the settings page's partial, with
    //     the test email or notification of D86 and D88, recommended and optional),
    //   3 a search to check (New search, and a search's main settings with the session's own Searches form: its
    //     campground, dates, people and Enabled (autoRun); the rest on the searches page),
    //   4 review and turn on (outputTo in General.yaml, through the General form, once the monitoring's checks of the files
    //     pass), then what is done and the monitoring's state.
    // Where the user is comes from the saved files only (GuidedSetupStatus): no progress is kept, so a browser closed, a
    // session ended or web mode restarted loses nothing that was saved, and a step is never "done" unless the files say so.
    //  - A GET only looks: it never creates a file, saves, sends, searches, refreshes or starts the monitoring. The method
    //    chosen on step 1 is a GET form's value, which only leads to the method's address.
    //  - Every change is a POST with the antiforgery token (Razor Pages' own check, ER-38) and the revision of the form it
    //    showed (an out-of-date page applies nothing), then the page or a redirect: the settings pages' and searches pages'
    //    own rules, with the same seams (WebSettingsWorkspaces.AcceptSettings, SendTest; WebSettingsForm.Apply, Run). Its test
    //    and its New search count with theirs (CampertronWebHost.TestsPerClient, FilesPerClient).
    //  - The page model holds no rule of a setting, a search or the monitoring: the forms and the library's checks do.
    //  - Leaving it is always one link away (the menu, and Skip or Leave on every step), and leaving marks nothing.
    // Web-only in v0.3.0 (D79 point 6: CampertronGUI's guided path is later work; the forms and files it uses are both UIs').
    public sealed class GuidedSetupModel : PageModel
    {
        internal const string Root = CampertronWebHost.GuidedSetupPath;

        private readonly WebHostSettings _Settings;
        private readonly IHostApplicationLifetime _Lifetime;

        public GuidedSetupModel(WebHostSettings Settings, IHostApplicationLifetime Lifetime)
        {
            _Settings = Settings;
            _Lifetime = Lifetime;
        }

        private WebSettingsWorkspaces _Workspaces => HttpContext.RequestServices.GetRequiredService<WebSettingsWorkspaces>();
        private WebMonitoring _Monitoring => HttpContext.RequestServices.GetRequiredService<WebMonitoring>();

        public GuidedSetupStatus Status { get; private set; } = null!;
        public GuidedSetupStep Step { get; private set; } = GuidedSetupStep.Method;
        public GuidedSetupMethod? Method { get; private set; }
        // step 1: the method shown checked (the address's choice, else what the saved files suggest), and its problem
        public GuidedSetupMethod? Chosen { get; private set; }
        public string? ChoiceError { get; private set; }
        // step 2: the session's form of the method's file (the settings page's own)
        public SettingsFileViewModel? Form { get; private set; }
        // step 3: the session's Searches form, and the search shown (null: the list)
        public SearchesViewModel? Searches { get; private set; }
        public SearchFileViewModel? Search { get; private set; }
        public string? Created { get; private set; }
        // step 4: the session's General form
        public GeneralSettingsViewModel? General { get; private set; }
        // done: the monitoring as it is now
        internal WebMonitoringView? Monitoring { get; private set; }
        public int Revision { get; private set; }
        public string? PageNotice { get; private set; }
        // the step's own message after an action (its severity's word first), or null
        public string? Notice { get; private set; }
        public string? FocusId { get; private set; }
        public bool Posted { get; private set; }
        public bool EmailPasswordVariableSet => CampertronLibrary.function.Base.SmtpPassword.VariableIsSet();
        public bool NtfyTokenVariableSet => CampertronLibrary.function.Base.NtfyToken.VariableIsSet();

        // ---- addresses: fixed, built here only (never from a request's text) ----

        public static string MethodName(GuidedSetupMethod Method) => Method == GuidedSetupMethod.Email ? "email" : "ntfy";

        private static string? StepName(GuidedSetupStep Step) => Step switch
        {
            GuidedSetupStep.Notifications => "notifications",
            GuidedSetupStep.Search => "search",
            GuidedSetupStep.TurnOn => "turn-on",
            GuidedSetupStep.Done => "done",
            _ => null,
        };

        public static string Address(GuidedSetupMethod? Method, GuidedSetupStep Step, string? File = null)
        {
            if (Method is not GuidedSetupMethod Known || StepName(Step) is not string Name)
            {
                return Root;
            }
            return Root + "/" + MethodName(Known) + "/" + Name + (File == null ? string.Empty : "/" + Uri.EscapeDataString(File));
        }

        // the address of a method that resumes where the saved files say
        public static string Resume(GuidedSetupMethod? Method) => Method is GuidedSetupMethod Known ? Root + "/" + MethodName(Known) : Root;

        private static GuidedSetupMethod? ParseMethod(string? Text) => Text switch
        {
            "email" => GuidedSetupMethod.Email,
            "ntfy" => GuidedSetupMethod.Ntfy,
            _ => null,
        };

        private static GuidedSetupStep? ParseStep(string? Text) => Text switch
        {
            "notifications" => GuidedSetupStep.Notifications,
            "search" => GuidedSetupStep.Search,
            "turn-on" => GuidedSetupStep.TurnOn,
            "done" => GuidedSetupStep.Done,
            _ => null,
        };

        // the form name of the method's notification file in a workspace (WebSettingsWorkspaces.FileNames)
        private static string FileKey(GuidedSetupMethod Method) => Method == GuidedSetupMethod.Email ? "email" : "ntfy";

        public string WithoutFolder(string? Text) => WebPageText.WithoutFolder(_Settings.SettingsFolder, Text);

        // the problems the step's form lists at the page's top
        public List<(string Id, string Text)> Errors() => Form != null ? WebSettingsForm.Errors(Form) : Search != null ? WebSettingsForm.Errors(Search) : new List<(string, string)>();

        // ---- GET: only looks ----

        public async Task<IActionResult> OnGetAsync(string? method, string? step, string? file, string? choice)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            (FocusId, Notice) = Space.TakeGuidedAfter();
            Status = ReadStatus();
            if (method == null)
            {
                Step = GuidedSetupStep.Method;
                Chosen = Status.SuggestedMethod;
                //the step's GET form was sent (its Continue, or a choice): a method leads on, anything else is said
                if (Request.Query.ContainsKey("choice") || Request.Query.ContainsKey("continue"))
                {
                    if (ParseMethod(choice) is GuidedSetupMethod Picked)
                    {
                        //step 1 saves nothing: the choice only leads to the method's next step
                        return LocalRedirect(Resume(Picked));
                    }
                    ChoiceError = GuidedSetupWording.MethodMissing;
                    FocusId = "error-summary";
                }
                return Page();
            }
            if (ParseMethod(method) is not GuidedSetupMethod Known)
            {
                return NotFound();
            }
            Method = Known;
            if (step == null)
            {
                return file == null ? LocalRedirect(Address(Known, Status.NextStep(Known))) : NotFound();
            }
            if (ParseStep(step) is not GuidedSetupStep Asked || (file != null && Asked != GuidedSetupStep.Search))
            {
                return NotFound();
            }
            Step = Asked;
            switch (Asked)
            {
                case GuidedSetupStep.Notifications:
                    Form = Space.File(FileKey(Known));
                    //as the settings page does when it is shown: each editor looks at its file on disk
                    Space.Settings.CheckForExternalChanges();
                    Space.FollowGenerations();
                    Revision = Space.Revisions[FileKey(Known)];
                    break;
                case GuidedSetupStep.Search:
                    OpenSearches(Space);
                    if (file != null)
                    {
                        if (Find(file) is not SearchFileViewModel Found)
                        {
                            return NotFound();
                        }
                        ShowSearch(Found);
                        Space.FollowSearches();
                        Revision = Space.RevisionOf(Found);
                        Created = Space.TakeCreated(Found.FileName);
                        if (Created != null)
                        {
                            FocusId = "created";
                        }
                    }
                    else
                    {
                        Revision = Space.ListRevision;
                    }
                    break;
                case GuidedSetupStep.TurnOn:
                    General = Space.Settings.General;
                    Space.Settings.CheckForExternalChanges();
                    Space.FollowGenerations();
                    Revision = Space.Revisions["general"];
                    break;
                case GuidedSetupStep.Done:
                    Monitoring = _Monitoring.View();
                    break;
            }
            return Page();
        }

        // ---- POST: the step's form ----

        // Step 2's and a search's buttons: the entries into the form when the page is current, the disk looked at, then the
        // button through the form's own command; "continue" saves what changed and goes on once the saved files are ready
        public async Task<IActionResult> OnPostAsync(string? method, string? step, string? file)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            Posted = true;
            if (ParseMethod(method) is not GuidedSetupMethod Known)
            {
                return NotFound();
            }
            Method = Known;
            GuidedSetupStep? Asked = ParseStep(step);
            if (Asked == GuidedSetupStep.Notifications && file == null)
            {
                Step = GuidedSetupStep.Notifications;
                return Notifications(Space, Known);
            }
            if (Asked == GuidedSetupStep.Search && file != null)
            {
                Step = GuidedSetupStep.Search;
                return EditSearch(Space, Known, file);
            }
            return NotFound();
        }

        // Step 2's test email or test notification (D86, D88): the saved file, the settings pages' own rule and send
        public async Task<IActionResult> OnPostTestAsync(string? method, string? step, string? file)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            Posted = true;
            if (ParseMethod(method) is not GuidedSetupMethod Known || ParseStep(step) != GuidedSetupStep.Notifications || file != null)
            {
                return NotFound();
            }
            Method = Known;
            Step = GuidedSetupStep.Notifications;
            string Key = FileKey(Known);
            Form = Space.File(Key);
            TestNotificationForm Test = (Form as EmailSettingsViewModel)?.Test ?? ((NtfySettingsViewModel)Form).Test;
            if (!Space.AcceptSettings(Key, Request.Form))
            {
                PageNotice = SettingsModel.StaleText;
                return Shown(Space, Key, "page-notice");
            }
            Space.Changed(Key);
            await WebTestSend.Send(_Workspaces, Space, Test, _Lifetime.ApplicationStopping);
            Space.Changed(Key);
            return Shown(Space, Key, "test-result");
        }

        // Step 3's New search: the example search, not enabled, named by the user (the searches page's own command); then
        // its guided page
        public async Task<IActionResult> OnPostCreateAsync(string? method, string? step, string? file)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            Posted = true;
            if (ParseMethod(method) is not GuidedSetupMethod Known || ParseStep(step) != GuidedSetupStep.Search || file != null)
            {
                return NotFound();
            }
            Method = Known;
            Step = GuidedSetupStep.Search;
            OpenSearches(Space);
            bool Current = int.TryParse(Request.Form["revision"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Sent)
                && Sent == Space.ListRevision;
            if (Current)
            {
                Searches!.NewSearchName = Request.Form["new.name"].ToString();
            }
            Searches!.Show();
            Space.FollowSearches();
            if (!Current)
            {
                PageNotice = SettingsModel.StaleText;
                FocusId = "page-notice";
            }
            else
            {
                Searches.NewSearchCommand.Execute(null);
                Space.ListChanged();
                if (!Searches.NewSearchIsError && Searches.SelectedFile is SearchFileViewModel New && Searches.NewSearchMessage is string Message)
                {
                    Space.Created(New.FileName, Message);
                    return LocalRedirect(Address(Known, GuidedSetupStep.Search, New.FileName));
                }
                FocusId = "new-search-message";
            }
            Revision = Space.ListRevision;
            Status = ReadStatus();
            return Page();
        }

        // Step 4's Turn the monitoring on: outputTo in General.yaml through the General form, only when the monitoring's
        // checks of the saved files pass for the method and General.yaml has no unsaved change of the user's
        public async Task<IActionResult> OnPostTurnOnAsync(string? method, string? step, string? file)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            Posted = true;
            if (ParseMethod(method) is not GuidedSetupMethod Known || ParseStep(step) != GuidedSetupStep.TurnOn || file != null)
            {
                return NotFound();
            }
            Method = Known;
            Step = GuidedSetupStep.TurnOn;
            General = Space.Settings.General;
            bool Current = int.TryParse(Request.Form["revision"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Sent)
                && Sent == Space.Revisions["general"];
            Space.Settings.CheckForExternalChanges();
            Space.FollowGenerations();
            Status = ReadStatus();
            if (!Current)
            {
                PageNotice = SettingsModel.StaleText;
                FocusId = "page-notice";
            }
            else if (General.IsDirty || General.HasConflict)
            {
                Notice = GuidedSetupWording.GeneralUnsaved;
                FocusId = "notice";
            }
            else if (!Status.FilesReady(Known))
            {
                Notice = GuidedSetupWording.TurnOnNotReady;
                FocusId = "notice";
            }
            else if (Status.IsOn(Known))
            {
                Space.GuidedActed("step-heading", GuidedSetupWording.AlreadyOn);
                return LocalRedirect(Address(Known, GuidedSetupStep.Done));
            }
            else
            {
                string? Problem = TurnOn(General, Known);
                Space.Changed("general");
                Status = ReadStatus();
                if (Problem == null && Status.IsOn(Known))
                {
                    Space.GuidedActed("step-heading", GuidedSetupWording.Saved(General.FileName));
                    return LocalRedirect(Address(Known, GuidedSetupStep.Done));
                }
                Notice = Problem ?? GuidedSetupWording.TurnOnNotReady;
                FocusId = "notice";
            }
            Revision = Space.Revisions["general"];
            return Page();
        }

        // outputTo through the General form (General.yaml created first, with its defaults, when it does not exist), then
        // the form's own Save with its checks (D71, D73); null when it was saved, else the form's words. A save that did not
        // go through leaves no unsaved outputTo behind in the session's form
        private static string? TurnOn(GeneralSettingsViewModel General, GuidedSetupMethod Method)
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

        // ---- the steps' posts ----

        private IActionResult Notifications(WebSettingsWorkspaces.Workspace Space, GuidedSetupMethod Known)
        {
            string Key = FileKey(Known);
            Form = Space.File(Key);
            if (!Space.AcceptSettings(Key, Request.Form))
            {
                PageNotice = SettingsModel.StaleText;
                return Shown(Space, Key, "page-notice");
            }
            string? Action = Request.Form["action"];
            if (string.Equals(Action, "continue", StringComparison.Ordinal))
            {
                //what changed is saved by the form's own Save (with its checks); a value with its own error is never saved
                if (Form.IsDirty || WebSettingsForm.Errors(Form).Count > 0)
                {
                    WebSettingsForm.Run(Form, "save");
                }
                Space.Changed(Key);
                Status = ReadStatus();
                if (!Form.IsDirty && !Form.HasConflict && WebSettingsForm.Errors(Form).Count == 0 && Status.NotificationsReady(Known))
                {
                    Space.GuidedActed("step-heading", Form.HasStatus && !Form.StatusIsError && Form.StatusText != null ? "Info: " + Form.StatusText : null);
                    return LocalRedirect(Address(Known, GuidedSetupStep.Search));
                }
                //what keeps the step from going on: the form's problems, its failed save, else what the monitoring's start
                //would refuse in the saved file (the step's own list of it, which takes the focus)
                return Shown(Space, Key, WebSettingsForm.Errors(Form).Count > 0 ? "error-summary" : Form.HasStatus && Form.StatusIsError ? "status"
                    : Form.HasConflict ? "conflict" : "needs");
            }
            string? Named = WebSettingsForm.Run(Form, Action);
            Space.Changed(Key);
            return Shown(Space, Key, Focus(Named));
        }

        private IActionResult EditSearch(WebSettingsWorkspaces.Workspace Space, GuidedSetupMethod Known, string Name)
        {
            OpenSearches(Space);
            if (Find(Name) is not SearchFileViewModel Found)
            {
                return NotFound();
            }
            ShowSearch(Found);
            bool Current = int.TryParse(Request.Form["revision"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Sent)
                && Sent == Space.RevisionOf(Found);
            if (Current)
            {
                WebSettingsForm.Apply(Found, Request.Form);
            }
            Searches!.CheckForExternalChanges();
            Space.FollowSearches();
            string? Action = Request.Form["action"];
            if (!Current)
            {
                PageNotice = SettingsModel.StaleText;
                FocusId = "page-notice";
            }
            else if (string.Equals(Action, "continue", StringComparison.Ordinal))
            {
                if (Found.IsDirty || WebSettingsForm.Errors(Found).Count > 0)
                {
                    WebSettingsForm.Run(Found, "save");
                }
                Space.SearchChanged(Found);
                Status = ReadStatus();
                if (!Found.IsDirty && !Found.HasConflict && WebSettingsForm.Errors(Found).Count == 0 && Status.SearchReady)
                {
                    Space.GuidedActed("step-heading", Found.HasStatus && !Found.StatusIsError && Found.StatusText != null ? "Info: " + Found.StatusText : null);
                    return LocalRedirect(Address(Known, GuidedSetupStep.TurnOn));
                }
                if (!Found.HasStatus || !Found.StatusIsError)
                {
                    Notice = GuidedSetupWording.SearchNotReady;
                }
                FocusId = WebSettingsForm.Errors(Found).Count > 0 ? "error-summary" : Notice != null ? "notice" : "status";
            }
            else
            {
                FocusId = Focus(WebSettingsForm.Run(Found, Action));
                Space.SearchChanged(Found);
            }
            Revision = Space.RevisionOf(Found);
            Status ??= ReadStatus();
            return Page();
        }

        private IActionResult Shown(WebSettingsWorkspaces.Workspace Space, string Key, string? Focus)
        {
            Revision = Space.Revisions[Key];
            FocusId = Focus;
            Status ??= ReadStatus();
            return Page();
        }

        // what has the focus after a form's action: the problems, the result, the preview, or what the action named
        private string? Focus(string? Named)
        {
            SettingsFileViewModel Editor = (SettingsFileViewModel?)Form ?? Search!;
            bool HasErrors = Errors().Count > 0;
            switch (Named)
            {
                case "status":
                    return Editor.HasStatus ? (Editor.StatusIsError && HasErrors ? "error-summary" : "status") : HasErrors ? "error-summary" : null;
                case null:
                    return HasErrors ? "error-summary" : Editor.HasPreview ? "preview" : null;
                default:
                    return Named;
            }
        }

        // ---- the workspace and the forms ----

        private GuidedSetupStatus ReadStatus() => GuidedSetupStatus.Read(_Settings.SettingsFolder, DateTime.Now);

        // the session's Searches form, following General.yaml's outputTo, its files read (the first time) or compared with
        // the disk, as the searches page does when it is shown
        private void OpenSearches(WebSettingsWorkspaces.Workspace Space)
        {
            Searches = Space.Searches;
            Space.FollowOutput();
            Searches.Show();
            Space.FollowSearches();
        }

        private SearchFileViewModel? Find(string Name) => Searches!.Files.FirstOrDefault(F => string.Equals(F.FileName, Name, StringComparison.Ordinal));

        private void ShowSearch(SearchFileViewModel Found)
        {
            Search = Found;
            Searches!.SelectedFile = Found;
        }

        // the workspace of the session (or of the web UI without password protection), its lock held until the response is
        // written
        private async Task<WebSettingsWorkspaces.Workspace> Open()
        {
            string Key = _Settings.Access.PasswordProtection
                ? User.FindFirstValue(CampertronWebHost.SessionClaim) ?? throw new InvalidOperationException("A signed-in request has no session.")
                : WebSettingsWorkspaces.OpenKey;
            WebSettingsWorkspaces.Workspace Space = _Workspaces.For(Key);
            await Space.Lock.WaitAsync(HttpContext.RequestAborted);
            HttpContext.Response.RegisterForDispose(new Release(Space.Lock));
            return Space;
        }

        private sealed class Release : IDisposable
        {
            private SemaphoreSlim? _Lock;
            internal Release(SemaphoreSlim Lock) => _Lock = Lock;
            public void Dispose() => Interlocked.Exchange(ref _Lock, null)?.Release();
        }
    }
}
