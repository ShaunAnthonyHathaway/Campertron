using System.Security.Claims;
using CampertronLibrary.function.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace CampertronWeb.Pages
{
    // The results page of the web UI (v0.3.0's PR 7, D97; business-rules-settings §34): the desktop GUI's Results tab as a
    // page. It runs the saved search files on request through GS1's search service with manual-search semantics (no
    // Camp.History, no notification, no settings file written) and shows the typed results, with the rules and words of the
    // shared form layer (WebResults; the Full UI rule, D84). This page model holds no rule of a run or a result: it hands
    // the page's revision, check boxes and button to the session's WebResults and shows what it says.
    //  - A GET only looks: the run's progress or end, the search files on disk (as the desktop GUI does when its tab is
    //    shown), the results of the session's last run. It starts, stops and writes nothing, sends nothing, and never
    //    takes the monitoring lock. Which search's results and which of their rows it shows is the address's choice
    //    (?run, ?search, ?part: numbers only, of the session's own last run; anything else shows the first page).
    //  - Every change is a POST with the antiforgery token (Razor Pages' own check, ER-38), then a redirect to the page
    //    (so reloading the page shows the progress and never sends an action again): the choice (Select enabled, Select
    //    all that can run, Clear selection, and Enter, which keeps the boxes as they are), "?handler=start" (its own rate
    //    limit, CampertronWebHost.RunsPerClient) and "?handler=cancel". A page sends the revision it showed; an
    //    out-of-date page applies nothing and starts nothing.
    //  - While a run of the session goes on, the page's own script (D108; Assets/results.js) shows its progress as it goes:
    //    it asks "?handler=progress" (a GET that only looks, WebResults.Observe) for the run the page shows and puts the
    //    answer in place, then takes the page's results part once the run has ended. Without the script the page is the
    //    server's, as before, and "Show the latest progress" shows it as it is now.
    [RequestFormLimits(ValueCountLimit = MaxFormValues, KeyLengthLimit = CampertronWebHost.MaxFormKeyLength, ValueLengthLimit = CampertronWebHost.MaxFormValueLength,
        BufferBodyLengthLimit = CampertronWebHost.MaxRequestBytes, MultipartBodyLengthLimit = CampertronWebHost.MaxRequestBytes)]
    public sealed class ResultsModel : PageModel
    {
        // the most entries the page may post: each search file a box and its shown state. The attribute replaces the web
        // UI's form limits for this page, so it names every other one as the host sets it (CampertronWebHost)
        internal const int MaxFormValues = 4096;
        internal const string Address = "/results";
        internal const string StaleText = "This page was out of date (the choice changed in another tab or on disk, a search started or ended, or the page was sent again), so nothing you chose was applied and no search started. The page shows the current choice.";

        private readonly WebHostSettings _Settings;

        public ResultsModel(WebHostSettings Settings)
        {
            _Settings = Settings;
        }

        private WebSettingsWorkspaces _Workspaces => HttpContext.RequestServices.GetRequiredService<WebSettingsWorkspaces>();

        internal WebResults Results { get; private set; } = null!;
        // what the page shows of the last run: the search (its place in the run) and the page of its rows
        internal int ShownIndex { get; private set; } = -1;
        internal ResultsListing? Listing { get; private set; }
        // what has the focus when the page opens after an action, and the page's notice (an out-of-date page), or null
        public string? FocusId { get; private set; }
        public string? PageNotice { get; private set; }

        // the address of the last run's search at Index, on its page Page
        internal string ShowAddress(int Index, int Page = 1) =>
            Address + "?run=" + Results.OutcomeId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "&search=" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (Page > 1 ? "&part=" + Page.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);

        public string WithoutFolder(string? Text) => WebPageText.WithoutFolder(_Settings.SettingsFolder, Text);

        // part: the page of the rows ("page" is Razor Pages' own route value)
        public async Task<IActionResult> OnGetAsync(long? run, int? search, int? part)
        {
            await Open();
            Results.Show();
            (FocusId, PageNotice) = Results.TakeAfterAction();
            ShownIndex = Results.ShownIndex(run, search);
            Listing = Results.Listing(ShownIndex, run, part);
            return Page();
        }

        // the page's automatic progress (D108): the state of the run the page shows (run: its key), as JSON; it only looks.
        // Not the page: an answer for the page's script, no-store as every answer of the web UI
        public async Task<IActionResult> OnGetProgressAsync(string? run)
        {
            await Open();
            return Content(Results.Observe(run).Json(), WebResultsLive.ContentType);
        }

        // how often the page's script asks and how long it waits for an answer, in milliseconds
        internal string LiveInterval => Milliseconds(_Settings.Results.LiveInterval);
        internal string LiveTimeout => Milliseconds(_Settings.Results.LiveTimeout);

        private static string Milliseconds(TimeSpan Time) => ((long)Time.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

        // the choice: Select enabled, Select all that can run, Clear selection; Enter keeps the boxes as they are
        public async Task<IActionResult> OnPostAsync()
        {
            await Open();
            if (Accept())
            {
                switch (Request.Form["action"].ToString())
                {
                    case "enabled":
                        Results.SelectEnabled();
                        break;
                    case "all":
                        Results.SelectAllRunnable();
                        break;
                    case "clear":
                        Results.ClearSelection();
                        break;
                }
            }
            return LocalRedirect(Address);
        }

        // Start: the page's boxes, then a run of the chosen files when one can start
        public async Task<IActionResult> OnPostStartAsync()
        {
            await Open();
            if (Accept())
            {
                Results.Start();
            }
            return LocalRedirect(Address);
        }

        // Cancel: the run the page showed
        public async Task<IActionResult> OnPostCancelAsync()
        {
            await Open();
            Results.Cancel(Request.Form["run"]);
            return LocalRedirect(Address);
        }

        private bool Accept() => Results.Accept(Request.Form["revision"], Request.Form["shown"].Select(Value => Value ?? string.Empty), Request.Form["chosen"].Select(Value => Value ?? string.Empty), StaleText);

        // the workspace of the session (or of the web UI without password protection) with its lock held until the response
        // is written
        private async Task Open()
        {
            string Key = _Settings.Access.PasswordProtection
                ? User.FindFirstValue(CampertronWebHost.SessionClaim) ?? throw new InvalidOperationException("A signed-in request has no session.")
                : WebSettingsWorkspaces.OpenKey;
            WebSettingsWorkspaces.Workspace Space = _Workspaces.For(Key);
            await Space.Lock.WaitAsync(HttpContext.RequestAborted);
            HttpContext.Response.RegisterForDispose(new Release(Space.Lock));
            Results = Space.Results;
        }

        private sealed class Release : IDisposable
        {
            private SemaphoreSlim? _Lock;
            internal Release(SemaphoreSlim Lock) => _Lock = Lock;
            public void Dispose() => Interlocked.Exchange(ref _Lock, null)?.Release();
        }
    }
}
