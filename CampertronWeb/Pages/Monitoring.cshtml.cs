using System.Security.Claims;
using CampertronLibrary.function.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace CampertronWeb.Pages
{
    // The monitoring page of the web UI (v0.3.0's PR 8, D83 and D98; business-rules-settings §35): the web UI's unattended
    // monitoring (WebMonitoring: since v0.4.0's phase 2 the library's MonitoringController over its MonitoringService, the
    // one email and ntfy mode run), its state, its Start, Stop and Restart, why it is not running, and what its last check
    // found. The monitoring's control is web-only (the Full UI rule's exception, D84). This page model holds no rule of the
    // monitoring: it hands the pressed button and the moment of the monitoring the page showed to the controller and shows
    // what it says, in the shared words (MonitoringWording) with the web UI's own (WebMonitoringWording).
    //  - A GET only looks: one immutable view of the monitoring, written without any lock the monitoring needs. It starts,
    //    stops, reads and writes nothing else (which search of the last check and which of its rows it shows is the
    //    address's choice: ?run, ?check, ?search, ?part, numbers of the current monitoring and check only; anything else
    //    shows the first search with openings and its first rows).
    //  - Every action is a POST with the antiforgery token (Razor Pages' own check, ER-38) and the moment the page showed
    //    ("state"), then a redirect to the page, so a reload never sends an action again: "?handler=start", "?handler=stop"
    //    and "?handler=restart" only (a POST without one of them is refused), limited together per client
    //    (CampertronWebHost.MonitoringActionsPerClient). A page that is out of date starts and stops nothing.
    //  - While the monitoring goes on, the web UI's own script (D109; Assets/live.js, WebLive, WebMonitoringLive) shows it as
    //    it goes: it asks "?handler=progress" (a GET that only looks) and puts the answer in place, then, when a part of the
    //    page changed (a check began or ended, a start, a stop, the end), takes the page again ("?handler=view": the page as
    //    a GET shows it, without the notice of an action, the address's choice of search kept) and puts the changed parts
    //    in place. Neither starts or stops anything, and neither renews the session's idle time (WebLive.IsObservation).
    //    Without the script the page is the server's, and "Show the latest status" shows it as it is now.
    public sealed class MonitoringModel : PageModel
    {
        internal const string Address = "/monitoring";

        private readonly WebHostSettings _Settings;

        public MonitoringModel(WebHostSettings Settings)
        {
            _Settings = Settings;
        }

        private WebSettingsWorkspaces _Workspaces => HttpContext.RequestServices.GetRequiredService<WebSettingsWorkspaces>();
        private WebMonitoring _Monitoring => HttpContext.RequestServices.GetRequiredService<WebMonitoring>();

        internal MonitoringControlView View { get; private set; } = null!;
        // the search of the last check shown (its place in the check) and the page of its rows
        internal int ShownIndex { get; private set; } = -1;
        internal ResultsListing? Listing { get; private set; }
        // what has the focus when the page opens after an action, and the page's notice, or null
        public string? FocusId { get; private set; }
        public string? PageNotice { get; private set; }

        // the address of the last check's search at Index, on its page Page
        internal string ShowAddress(int Index, int Page = 1)
        {
            MonitoringControlCheck Check = View.LastCheck!;
            System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;
            return Address + "?run=" + Check.Generation.ToString(Invariant) + "&check=" + Check.Number.ToString(Invariant)
                + "&search=" + Index.ToString(Invariant) + (Page > 1 ? "&part=" + Page.ToString(Invariant) : string.Empty);
        }

        // a start's or a failure's words never name a path; should an operating system's message ever reach them, the
        // settings folder is said in words (WebPageText.WithoutFolder)
        public string WithoutFolder(string? Text) => WebPageText.WithoutFolder(_Settings.SettingsFolder, Text);

        // part: the page of the rows ("page" is Razor Pages' own route value)
        public Task<IActionResult> OnGetAsync(long? run, int? check, int? search, int? part) => Show(run, check, search, part, true);

        // the page for its script (D109): as a GET shows it, without the notice of an action (that is the next page's)
        public Task<IActionResult> OnGetViewAsync(long? run, int? check, int? search, int? part) => Show(run, check, search, part, false);

        // the page's automatic progress (D109): the monitoring's state (key: the one the page names), as JSON; it only looks.
        // Not the page: an answer for the page's script, no-store as every answer of the web UI
        public async Task<IActionResult> OnGetProgressAsync(string? key)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            return Content(WebMonitoringLive.Observe(_Monitoring.Controller.View(), _Monitoring.Instance, key, Space.Observe()).Json(), WebLive.ContentType);
        }

        // what the page tells its script while the monitoring goes on, or null
        internal WebLiveView? Live { get; private set; }

        private async Task<IActionResult> Show(long? run, int? check, int? search, int? part, bool TakeNotice)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            if (TakeNotice)
            {
                (FocusId, PageNotice) = Space.TakeMonitoringAfter();
            }
            View = _Monitoring.Controller.View();
            Live = WebMonitoringLive.Active(View)
                ? new WebLiveView(_Monitoring.Instance, WebMonitoringLive.State(View), 0, Space.Observation, WebMonitoringLive.View(View), WebLive.Milliseconds(_Settings.LiveInterval),
                    WebLive.Milliseconds(_Settings.LiveTimeout), WebMonitoringWording.Live)
                : null;
            _Settings.Monitoring.Rendering?.Invoke();
            if (View.LastCheck is MonitoringControlCheck Last && Last.Searches.Count > 0)
            {
                bool Asked = run == Last.Generation && check == Last.Number;
                ShownIndex = Asked && search is int Index && Index >= 0 && Index < Last.Searches.Count ? Index : FirstWithOpenings(Last);
                Listing = ResultsListing.Of(Last.Searches[ShownIndex], Asked ? part ?? 1 : 1);
            }
            return Page();
        }

        public Task<IActionResult> OnPostStartAsync() => Act(MonitoringControlAction.Start);
        public Task<IActionResult> OnPostStopAsync() => Act(MonitoringControlAction.Stop);
        public Task<IActionResult> OnPostRestartAsync() => Act(MonitoringControlAction.Restart);

        private async Task<IActionResult> Act(MonitoringControlAction Action)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            MonitoringControlOutcome Outcome = _Monitoring.Controller.Act(Action, Request.Form["state"]);
            string? Notice = Outcome == MonitoringControlOutcome.Accepted ? null : WebMonitoringWording.Outcome(Outcome, _Monitoring.Controller.View().Cooldown);
            Space.MonitoringActed(Notice != null ? "page-notice" : "status", Notice);
            return LocalRedirect(Address);
        }

        private static int FirstWithOpenings(MonitoringControlCheck Check)
        {
            for (int i = 0; i < Check.Searches.Count; i++)
            {
                if (Check.Searches[i].Openings > 0)
                {
                    return i;
                }
            }
            return 0;
        }

        // the workspace of the session (or of the web UI without password protection), for what the page says once after
        // an action; its lock is held until the response is written. The monitoring's own lock is never held then
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
