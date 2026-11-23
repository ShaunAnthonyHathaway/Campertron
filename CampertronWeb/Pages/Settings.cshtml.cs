using System.Security.Claims;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CampertronWeb.Pages
{
    // The settings pages of the web UI (v0.3.0's PR 5; business-rules-settings §32): General.yaml, Email.yaml, Ntfy.yaml
    // and Web.yaml, edited through the shared form layer's SettingsViewModel (D91), the form CampertronGUI's
    // Settings > Campertron shows, so both UIs edit the same files with the same validation, rules and words (the Full UI
    // rule, D84). This page model holds no rule of a setting: it hands the browser's entries and button to the form
    // (WebSettingsForm) and shows what the form says. Every change is a POST with the antiforgery token (Razor Pages' own
    // check, ER-38); a GET only looks at the files on disk, as the desktop GUI does when its tab is shown.
    //  - The form of a browser session lives between requests (WebSettingsWorkspaces) and is used by one request at a time:
    //    its lock is held until the page has been written.
    //  - A page sends the revision of the file it showed: entries from a page that is out of date are never applied.
    //  - The test notification (?handler=test, D86, D88) uses the saved file; the secret is read by the library's
    //    TestNotification during the send only, which runs without the form's lock. One at a time in the web UI.
    //  - The SMTP password is write-only (D82): no page holds it; whether CAMPERTRON_EMAIL_PASSWORD and
    //    CAMPERTRON_NTFY_TOKEN are set in this web UI's environment is said as yes or no (D82 point 4), never their values.
    public sealed class SettingsModel : PageModel
    {
        internal const string StaleText = "This page was out of date (the settings changed in another tab or on disk, or the page was sent again), so nothing you entered was applied. The page shows the current settings.";
        internal const string TestBusyText = "Warning: another test is being sent from this web UI. Wait for its result, then try again.";

        private readonly WebHostSettings _Settings;
        private readonly IHostApplicationLifetime _Lifetime;

        public SettingsModel(WebHostSettings Settings, IHostApplicationLifetime Lifetime)
        {
            _Settings = Settings;
            _Lifetime = Lifetime;
        }

        private WebSettingsWorkspaces _Workspaces => HttpContext.RequestServices.GetRequiredService<WebSettingsWorkspaces>();

        // the file shown: general, email, ntfy or web (WebSettingsWorkspaces.FileNames)
        public string Shown { get; private set; } = "general";
        public SettingsViewModel Forms { get; private set; } = null!;
        public SettingsFileViewModel Form { get; private set; } = null!;
        public int Revision { get; private set; }
        // the page's own notice (an out-of-date page), or null
        public string? PageNotice { get; private set; }
        // what has the keyboard focus when the page opens after an action, or null (a page that was asked for)
        public string? FocusId { get; private set; }
        public bool Posted { get; private set; }
        // D82 point 4: whether the variables are set in this web UI's own process (its monitoring would use them), yes or no
        public bool EmailPasswordVariableSet => SmtpPassword.VariableIsSet();
        public bool NtfyTokenVariableSet => NtfyToken.VariableIsSet();

        // A page never shows a path (§31): the editor's state and an action's result can carry an exception's message from the
        // operating system ("Access to the path '…' is denied."), which names the settings folder; the page says "the
        // settings folder" instead. The desktop GUI shows the folder anyway and keeps the message as it is
        public string WithoutFolder(string? Text)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return string.Empty;
            }
            string Folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_Settings.SettingsFolder));
            return Folder.Length == 0 ? Text : Text.Replace(Folder, "the settings folder", StringComparison.Ordinal);
        }

        public static string Label(string File) => File switch
        {
            "general" => ConfigFiles.GeneralFileName,
            "email" => ConfigFiles.EmailFileName,
            "ntfy" => ConfigFiles.NtfyFileName,
            _ => ConfigFiles.WebFileName
        };

        // the errors the page lists at its top: every field and row in error, then the file's own errors
        public List<(string Id, string Text)> Errors()
        {
            var Found = new List<(string, string)>();
            foreach (SettingField Field in Form.FieldList)
            {
                if (Field.IsError)
                {
                    Found.Add((WebSettingsForm.Id(Field), Field.AccessibleName + ": " + Field.Message));
                }
                if (Field is ListSettingField List)
                {
                    for (int i = 0; i < List.Rows.Count; i++)
                    {
                        if (List.Rows[i].IsError)
                        {
                            Found.Add((WebSettingsForm.RowId(List, i), List.Rows[i].AccessibleName + ": " + List.Rows[i].Message));
                        }
                    }
                }
            }
            foreach (NoticeItem Notice in Form.FileNotices.Where(N => N.IsError))
            {
                Found.Add(("notices", Notice.Message));
            }
            return Found;
        }

        public async Task<IActionResult> OnGetAsync(string? file)
        {
            if (file == null)
            {
                return Redirect("/settings/general");
            }
            if (await Open(file) is not WebSettingsWorkspaces.Workspace Space)
            {
                return NotFound();
            }
            //as the desktop GUI does when its tab is shown: each editor looks at its file on disk
            Forms.CheckForExternalChanges();
            Space.FollowGenerations();
            Revision = Space.Revisions[Shown];
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? file)
        {
            if (file == null || await Open(file) is not WebSettingsWorkspaces.Workspace Space)
            {
                return NotFound();
            }
            Posted = true;
            if (Accept(Space))
            {
                FocusId = WebSettingsForm.Run(Form, Request.Form["action"]);
                Space.Changed(Shown);
            }
            Revision = Space.Revisions[Shown];
            FocusId = Focus(FocusId);
            return Page();
        }

        // D86, D88: one test with the saved Email.yaml or Ntfy.yaml
        public async Task<IActionResult> OnPostTestAsync(string? file)
        {
            if (file == null || await Open(file) is not WebSettingsWorkspaces.Workspace Space)
            {
                return NotFound();
            }
            Posted = true;
            TestNotificationForm? Test = (Form as EmailSettingsViewModel)?.Test ?? (Form as NtfySettingsViewModel)?.Test;
            if (Test == null || !Accept(Space))
            {
                Revision = Space.Revisions[Shown];
                FocusId = Focus(null);
                return Page();
            }
            Space.Changed(Shown);
            if (Test.IsSending)
            {
                Test.End(new TestNotificationResult(false, TestBusyText));
            }
            else if (Test.UnavailableReason is string Reason)
            {
                Test.End(new TestNotificationResult(false, "Warning: " + Reason));
            }
            else if (!_Workspaces.TryBeginTest())
            {
                Test.End(new TestNotificationResult(false, TestBusyText));
            }
            else
            {
                TestNotificationResult Result;
                try
                {
                    Test.TryBegin();
                    //the send runs without the form's lock: the page of another request shows it as being sent meanwhile
                    Space.Lock.Release();
                    try
                    {
                        using var Stop = CancellationTokenSource.CreateLinkedTokenSource(_Lifetime.ApplicationStopping);
                        Result = await Task.Run(() => Test.Send(() => DateTime.Now, Stop.Token));
                    }
                    finally
                    {
                        await Space.Lock.WaitAsync();
                    }
                }
                finally
                {
                    _Workspaces.EndTest();
                }
                Test.End(Result);
            }
            Space.Changed(Shown);
            Revision = Space.Revisions[Shown];
            FocusId = "test-result";
            return Page();
        }

        // the workspace and the shown file's form, with the workspace's lock held until the response is written; null for
        // a file that is not one of the four
        private async Task<WebSettingsWorkspaces.Workspace?> Open(string File)
        {
            if (!WebSettingsWorkspaces.FileNames.Contains(File, StringComparer.Ordinal))
            {
                return null;
            }
            string Key = _Settings.Access.PasswordProtection
                ? User.FindFirstValue(CampertronWebHost.SessionClaim) ?? throw new InvalidOperationException("A signed-in request has no session.")
                : WebSettingsWorkspaces.OpenKey;
            WebSettingsWorkspaces.Workspace Space = _Workspaces.For(Key);
            await Space.Lock.WaitAsync(HttpContext.RequestAborted);
            HttpContext.Response.RegisterForDispose(new Release(Space.Lock));
            Shown = File;
            Forms = Space.Settings;
            Form = Space.File(File);
            return Space;
        }

        // the entries of a page that is current are applied to the form, then each editor looks at its file on disk (a
        // change there with the user's edits becomes the form's conflict, as in the desktop GUI); a page that is out of date
        // applies nothing and says so
        private bool Accept(WebSettingsWorkspaces.Workspace Space)
        {
            bool Current = int.TryParse(Request.Form["revision"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Sent)
                && Sent == Space.Revisions[Shown];
            if (Current)
            {
                WebSettingsForm.Apply(Form, Request.Form);
            }
            Forms.CheckForExternalChanges();
            Space.FollowGenerations();
            if (!Current)
            {
                PageNotice = StaleText;
            }
            return Current;
        }

        // what has the focus: the page's notice, the problems, the confirmation, or what the action named
        private string? Focus(string? Named)
        {
            if (PageNotice != null)
            {
                return "page-notice";
            }
            bool HasErrors = Errors().Count > 0;
            switch (Named)
            {
                case "status":
                    //a save that the errors kept from saving leads to them; any other result to its own words
                    return Form.HasStatus ? (Form.StatusIsError && HasErrors ? "error-summary" : "status") : HasErrors ? "error-summary" : null;
                case null:
                    //Check changes: the problems, else the preview of what Save writes
                    return HasErrors ? "error-summary" : Form.HasPreview ? "preview" : null;
                default:
                    return Named;
            }
        }

        private sealed class Release : IDisposable
        {
            private SemaphoreSlim? _Lock;
            internal Release(SemaphoreSlim Lock) => _Lock = Lock;
            public void Dispose() => Interlocked.Exchange(ref _Lock, null)?.Release();
        }
    }
}
