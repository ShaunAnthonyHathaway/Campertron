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
    // Since D110 the theme too: System, Light or Dark, as the desktop GUI's Theme card has them, kept by this browser (its
    // theme cookie, WebTheme) where the desktop GUI keeps its own in preferences.json (D84: the theme's storage is
    // desktop-only, not the theme). Copy, Open settings folder and the automatic restart are the desktop GUI's own
    // (opening folders and the automatic restart are desktop-only; a page's text is selected and copied in the browser).
    // Nothing here edits a setting: refreshRidbDataDayInterval and autoRefresh are General.yaml's, on its settings page.
    // This page model holds no rule of the refresh: it hands the pressed button and the moment of the refresh the page
    // showed to WebRefresh and shows what it says.
    //  - A GET only looks: the configuration and General.yaml's last refresh read now, read-only (nothing created), and one
    //    immutable view of the refresh. "?confirm=refresh" shows the confirmation; nothing starts without its POST.
    //  - Every action is a POST with the antiforgery token (Razor Pages' own check, ER-38) and the moment the page showed
    //    ("state"), then a redirect to the page, so a reload never sends an action again: "?handler=start" and
    //    "?handler=cancel" only (a POST without one of them is refused), limited together per client
    //    (CampertronWebHost.RefreshActionsPerClient). A page that is out of date starts and cancels nothing.
    //  - "?handler=theme" (D110) is the theme's POST: one of the three values or a refusal (400), this browser's theme
    //    cookie set (Light, Dark) or removed (System), nothing else changed (no file, no session, no refresh), then the page
    //    again with the focus on the line that says which theme it is shown in (the confirmation, if it was open, stays).
    //    Limited per client on its own (CampertronWebHost.ThemeChoicesPerClient), not with the refresh's actions.
    //  - While a refresh runs, the web UI's own script (D109; Assets/live.js, WebLive) shows its progress as it goes: it asks
    //    "?handler=progress" (a GET that only looks, WebRefresh.Observe) for the refresh the page shows and puts the
    //    answer in place, then, when Cancel was asked for, the cache step was reached or the refresh has ended, takes the
    //    page again ("?handler=view": the page as a GET shows it, without the notice of an action) and puts its changed parts
    //    in place, the campground data's new date and the configuration's refresh line included. Neither starts, cancels
    //    or repeats anything, and neither renews the session's idle time (WebLive.IsObservation). Without the script the
    //    page is the server's, and "Show the latest progress" shows it as it is now.
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
        // this browser's theme (D110)
        internal WebThemeChoice Theme { get; private set; }
        internal const string ThemeShownId = "theme-shown";

        public Task<IActionResult> OnGetAsync(string? confirm) => Show(confirm, true);

        // the page for its script (D109): as a GET shows it, without the notice of an action (that is the next page's) and
        // never the confirmation (the script follows a refresh; only the user asks for a refresh)
        public Task<IActionResult> OnGetViewAsync() => Show(null, false);

        // the page's automatic progress (D109): the state of the refresh the page shows (key: its key), as JSON; it only
        // looks. Not the page: an answer for the page's script, no-store as every answer of the web UI
        public async Task<IActionResult> OnGetProgressAsync(string? key)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            return Content(_Refresh.Observe(key, Space.Observe()).Json(), WebLive.ContentType);
        }

        // what the page tells its script while a refresh runs, or null
        internal WebLiveView? Live { get; private set; }

        private async Task<IActionResult> Show(string? confirm, bool TakeNotice)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            if (TakeNotice)
            {
                (FocusId, PageNotice) = Space.TakeSystemAfter();
            }
            View = _Refresh.View();
            Theme = HttpContext.RequestServices.GetRequiredService<WebTheme>().Of(Request);
            Live = View.Running is WebRefreshOperation Running
                ? new WebLiveView(_Refresh.KeyOf(Running), View.LiveState, View.LiveRank, Space.Observation, View.LiveView, WebLive.Milliseconds(_Settings.LiveInterval),
                    WebLive.Milliseconds(_Settings.LiveTimeout), WebSystemWording.Live)
                : null;
            Configuration = CurrentConfiguration.ReadWithoutPaths(_Settings.SettingsFolder, _Settings.CachePath, DateTime.UtcNow, CultureInfo.CurrentCulture,
                System.Reflection.Assembly.GetEntryAssembly() ?? typeof(SystemModel).Assembly);
            Confirming = string.Equals(confirm, ConfirmValue, StringComparison.Ordinal) && !View.IsRunning;
            if (Confirming && FocusId == null)
            {
                FocusId = "confirm-heading";
            }
            return Page();
        }

        // the theme's POST (D110): this browser's choice, then the page again
        public async Task<IActionResult> OnPostThemeAsync()
        {
            if (!WebTheme.TryParse(Request.Form[WebTheme.Field], out WebThemeChoice Choice))
            {
                return BadRequest();
            }
            WebSettingsWorkspaces.Workspace Space = await Open();
            HttpContext.RequestServices.GetRequiredService<WebTheme>().Save(HttpContext, Choice);
            Space.SystemActed(ThemeShownId, null);
            return LocalRedirect(string.Equals(Request.Form["confirm"], ConfirmValue, StringComparison.Ordinal) ? Address + "?confirm=" + ConfirmValue : Address);
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
