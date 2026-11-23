using System.Globalization;
using System.Security.Claims;
using CampertronLibrary.function.Base;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace CampertronWeb.Pages
{
    // The system page of the web UI (v0.3.0's PR 9, D99; business-rules-settings §36): CampertronGUI's Settings > System as
    // a page, with its rules and words (the Full UI rule, D84): the current configuration (the console's View Config items,
    // CurrentConfiguration, with the two folders in words: a page never shows a path) and the campground data's card (the
    // last refresh, and the refresh of the library's RidbRefreshService with RefreshRules and RefreshWording, WebRefresh).
    // The theme, Copy, Open settings folder and the automatic restart are the desktop GUI's own (D84: the theme's storage,
    // opening folders and the automatic restart are desktop-only; a page's text is selected and copied in the browser).
    // Nothing here edits a setting: refreshRidbDataDayInterval and autoRefresh are General.yaml's, on its settings page.
    // This page model holds no rule of the refresh: it hands the pressed button and the moment of the refresh the page
    // showed to WebRefresh and shows what it says.
    //  - A GET only looks: the configuration and General.yaml's last refresh read now, read-only (nothing created), and one
    //    immutable view of the refresh. "?confirm=refresh" shows the confirmation; nothing starts without its POST.
    //  - Every action is a POST with the antiforgery token (Razor Pages' own check, ER-38) and the moment the page showed
    //    ("state"), then a redirect to the page, so a reload never sends an action again: "?handler=start" and
    //    "?handler=cancel" only (a POST without one of them is refused), limited together per client
    //    (CampertronWebHost.RefreshActionsPerClient). A page that is out of date starts and cancels nothing.
    //  - A page has no script: it does not update by itself, and says so.
    public sealed class SystemModel : PageModel
    {
        internal const string Address = "/settings/system";
        internal const string ConfirmValue = "refresh";

        private readonly WebHostSettings _Settings;

        public SystemModel(WebHostSettings Settings)
        {
            _Settings = Settings;
        }

        private WebSettingsWorkspaces _Workspaces => HttpContext.RequestServices.GetRequiredService<WebSettingsWorkspaces>();
        private WebRefresh _Refresh => HttpContext.RequestServices.GetRequiredService<WebRefresh>();

        internal ConfigurationSummary Configuration { get; private set; } = null!;
        internal WebRefreshView View { get; private set; } = null!;
        // the confirmation is shown (asked for, and a refresh can be asked for now)
        internal bool Confirming { get; private set; }
        public string? FocusId { get; private set; }
        public string? PageNotice { get; private set; }

        public async Task<IActionResult> OnGetAsync(string? confirm)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            (FocusId, PageNotice) = Space.TakeSystemAfter();
            View = _Refresh.View();
            Configuration = CurrentConfiguration.ReadWithoutPaths(_Settings.SettingsFolder, _Settings.CachePath, DateTime.UtcNow, CultureInfo.CurrentCulture,
                System.Reflection.Assembly.GetEntryAssembly() ?? typeof(SystemModel).Assembly);
            Confirming = string.Equals(confirm, ConfirmValue, StringComparison.Ordinal) && !View.IsRunning;
            if (Confirming && FocusId == null)
            {
                FocusId = "confirm-heading";
            }
            return Page();
        }

        public Task<IActionResult> OnPostStartAsync() => Act(true);
        public Task<IActionResult> OnPostCancelAsync() => Act(false);

        private async Task<IActionResult> Act(bool Start)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            string? State = Request.Form["state"];
            WebRefreshOutcome Outcome = Start ? _Refresh.Begin(State) : _Refresh.Cancel(State);
            string? Notice = WebSystemWording.Outcome(Outcome);
            Space.SystemActed(Notice != null ? "page-notice" : "status", Notice);
            return LocalRedirect(Address);
        }

        // a text without the settings folder's path, should an operating system's message ever reach the page
        public string WithoutFolder(string? Text) => WebPageText.WithoutFolder(_Settings.SettingsFolder, Text);

        // a number as the progress element takes it
        internal static string Number(double Value) => Value.ToString("0.###", CultureInfo.InvariantCulture);

        // the workspace of the session (or of the web UI without password protection), for what the page says once after
        // an action; its lock is held until the response is written. The refresh's own lock is never held then
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
