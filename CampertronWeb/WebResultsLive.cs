using System.Text.Json;
using System.Text.Json.Serialization;

namespace CampertronWeb
{
    // The results page's automatic progress (decision D108; business-rules-settings §34). While a search of the browser
    // session runs, the page's own script (Assets/results.js, served by the web UI itself) asks for the run's state with a
    // GET ("/results?handler=progress&run=<the run's key>") and shows it in place: the status, the stage, the counts and the
    // elapsed time, the bar and each chosen file's state. When the run has ended, the script takes the page's results part
    // from the page itself (a GET of "/results") once, and stops. The answer is this record as JSON: what the page already
    // shows in words, for the session's own run only, and nothing else (no path, no file's content, no setting, no
    // exception, no other session's run). It only looks (WebResults.Observe): an answer never starts, repeats or cancels a
    // search.
    //  - State: "running" or "canceling" with the run's state; "ended": the run the page shows has ended (its results are on
    //    the page itself); "replaced": the page names no run of this session as it is now (another run started in another
    //    tab of the same session, the web UI started again, a page of no run): it is shown nothing of a run.
    //  - Seq: a higher number for every answer of the session, so a page never shows an older answer over a newer one.
    internal sealed record WebResultsLive(
        [property: JsonPropertyName("format")] int Format,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("run")] string? Run,
        [property: JsonPropertyName("seq")] long Seq,
        [property: JsonPropertyName("status")] string? Status = null,
        [property: JsonPropertyName("details")] string? Details = null,
        [property: JsonPropertyName("bar")] WebResultsLiveBar? Bar = null,
        [property: JsonPropertyName("rows")] IReadOnlyList<WebResultsLiveRow>? Rows = null)
    {
        internal const int FormatVersion = 1;
        internal const string Running = "running";
        internal const string Canceling = "canceling";
        internal const string EndedState = "ended";
        internal const string ReplacedState = "replaced";
        internal const string ContentType = "application/json; charset=utf-8";

        // the default encoder escapes <, >, &, ' and every character outside ASCII: a name in the JSON is never markup, even
        // to a reader that would take it for some
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        internal static WebResultsLive Ended(string Run, long Seq) => new WebResultsLive(FormatVersion, EndedState, Run, Seq);

        internal static WebResultsLive Replaced(long Seq) => new WebResultsLive(FormatVersion, ReplacedState, null, Seq);

        internal string Json() => JsonSerializer.Serialize(this, Options);
    }

    // the bar as the page shows it: indeterminate until a total is known (ResultsWording.Bar)
    internal sealed record WebResultsLiveBar(
        [property: JsonPropertyName("indeterminate")] bool Indeterminate,
        [property: JsonPropertyName("value")] double Value,
        [property: JsonPropertyName("max")] double Max);

    // a chosen file's state at its row, by the row's place in the page's list (its element "file-<row>-state")
    internal sealed record WebResultsLiveRow(
        [property: JsonPropertyName("row")] int Row,
        [property: JsonPropertyName("state")] string State);

    // The words of the page's automatic progress (D108). They are the web UI's own: the desktop GUI's Results tab shows a
    // run's progress as it goes by itself. The page puts them into its markup (the script has no words of its own), and the
    // script says the time of the last update where a text has {time}, as the browser writes a time.
    internal static class WebResultsLiveWording
    {
        internal const string TimePlaceholder = "{time}";

        // the intro's sentence about the progress
        internal const string Intro = "While a search runs, this page shows its progress as it goes, in a browser that runs the web UI's own script; Show the latest progress shows the page as it is now.";
        // what the page says until its script runs (and in a browser that runs no script): the server's page as it was sent
        internal const string Manual = "Select Show the latest progress to see how the search is going.";
        // the page gets the run's state
        internal const string Updating = "This page updates by itself while the search runs. Last update: {time}.";
        // the page gets no answer, or one it can't use (the network, the web UI, a proxy): the search itself is not affected
        internal const string Failing = "This page can't get the search's progress right now (last update: {time}). That does not stop the search, and the page keeps trying. Show the latest progress shows the page as it is now.";
        // said once when answers come again after a failure
        internal const string Resumed = "This page gets the search's progress again.";
        // the run has ended, but the page can't get its results part
        internal const string EndedNotShown = "The search has ended, but this page can't show its results right now, and it keeps trying. Show the latest progress shows them.";
        // the web UI sent the page to the sign-in: the session ended, and its run with it
        internal const string SignedOut = "This page stopped updating: this browser is no longer signed in, and a search ends with its browser session. Sign in again to see the results page.";
        // the page names no run of the session as it is now
        internal const string OutOfDate = "This page stopped updating: it no longer shows this browser's current search (another search started in this browser, or the web UI started again). Show the latest progress shows the page as it is now.";
    }
}
