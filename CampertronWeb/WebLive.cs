using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace CampertronWeb
{
    // The web UI's automatic progress (D108 for the results page; D109 made it the rule for every page of an operation
    // that goes on, with the system page's refresh of the campground data and the monitoring page; business-rules-settings
    // §34 and §39). While a page shows such an operation, the web UI's own script (Assets/live.js, its only one) asks the
    // page's "?handler=progress" with the key the page names (a GET that only looks) and shows the answer in place: text
    // of the page's own elements by their id and the bar. When the answer's view differs from the page's (a control, a
    // section or the operation's end changed), the script takes the page itself once more ("?handler=view", the page as a
    // GET shows it) and puts its changed parts in place. Every page's operation has its own answer (WebResults.Observe,
    // WebRefresh.Observe, WebMonitoringLive.Observe); this is their shape and what they share.
    //  - State: an operation's own word while it goes on ("running", "canceling", "finishing", "starting", "stopping",
    //    "paused", ...); "ended": the operation the page shows is over (the page itself says how); "replaced": the page
    //    names nothing this session may see as it is now (another run, an earlier start of the web UI, a key that is no
    //    key): it is shown nothing.
    //  - Seq: a higher number for every answer of the browser session (the workspace's, under its lock), so a page never
    //    shows an older answer over a newer one. Rank: for an operation whose states only go forward (a run, a refresh), a
    //    later state has a higher rank, so a page never shows an earlier state again (Cancel and an end win); 0 for the
    //    monitoring, whose states come round again.
    //  - Fields: what the page already shows in words (its own element ids), Bar: the progress element's values. Never a
    //    path, a file's content, a setting, a secret, an exception's message or another session's run.
    internal sealed record WebLiveAnswer(
        [property: JsonPropertyName("format")] int Format,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("key")] string? Key,
        [property: JsonPropertyName("seq")] long Seq,
        [property: JsonPropertyName("rank")] int? Rank = null,
        [property: JsonPropertyName("view")] string? View = null,
        [property: JsonPropertyName("fields")] IReadOnlyDictionary<string, string>? Fields = null,
        [property: JsonPropertyName("bar")] WebLiveBar? Bar = null)
    {
        internal const int FormatVersion = 2;
        internal const string EndedState = "ended";
        internal const string ReplacedState = "replaced";

        // the default encoder escapes <, >, &, ' and every character outside ASCII: a name in the JSON is never markup, even
        // to a reader that would take it for some
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        internal static WebLiveAnswer Ended(string Key, long Seq, int Rank) => new WebLiveAnswer(FormatVersion, EndedState, Key, Seq, Rank);

        internal static WebLiveAnswer Replaced(long Seq) => new WebLiveAnswer(FormatVersion, ReplacedState, null, Seq);

        internal string Json() => JsonSerializer.Serialize(this, Options);
    }

    // the bar as the page shows it: indeterminate until a total is known
    internal sealed record WebLiveBar(
        [property: JsonPropertyName("indeterminate")] bool Indeterminate,
        [property: JsonPropertyName("value")] double Value,
        [property: JsonPropertyName("max")] double Max)
    {
        internal static WebLiveBar Of((bool Indeterminate, double Value, double Maximum) Shown) =>
            new WebLiveBar(Shown.Indeterminate, Shown.Indeterminate ? 0 : Shown.Value, Shown.Indeterminate ? 1 : Shown.Maximum);
    }

    // The words of a page's automatic progress: each operation's own (the search, the refresh, the monitoring), put into
    // the page's markup (#live's attributes; the script has no words of its own). Where a text has {time}, the script
    // says the time it last got an answer, as the browser writes a time.
    internal sealed record WebLiveWords(string Manual, string Updating, string Failing, string Resumed, string EndedNotShown, string SignedOut, string OutOfDate);

    // What a page tells its script (the partial _Live): the operation it shows (Key), its state, rank and view as the page
    // was written, the session's answer number then, how often to ask and how long to wait, and the words
    internal sealed record WebLiveView(string Key, string State, int Rank, long Seq, string View, string Interval, string Timeout, WebLiveWords Words);

    internal static class WebLive
    {
        internal const string ContentType = "application/json; charset=utf-8";
        internal const string TimePlaceholder = "{time}";
        // the handlers of a live page: its answer (JSON) and the page as a GET shows it, for the script
        internal const string ProgressHandler = "progress";
        internal const string ViewHandler = "view";

        // A random name for one start of the web UI (an operation's key begins with it), so a page from before a restart
        // is told so and shown nothing of what runs now
        internal static string NewInstance() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

        // a short name of what a part of a page shows (the page's view, a part's data-part): equal values, equal names.
        // Numbers, flags and the web UI's own words only; never a secret
        internal static string Signature(params object?[] Parts)
        {
            var Text = new StringBuilder();
            foreach (object? Part in Parts)
            {
                string One = Part switch
                {
                    null => "-",
                    bool Flag => Flag ? "1" : "0",
                    DateTime At => At.Ticks.ToString(CultureInfo.InvariantCulture),
                    IFormattable Value => Value.ToString(null, CultureInfo.InvariantCulture),
                    _ => Part.ToString() ?? "-"
                };
                Text.Append(One.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(One).Append('|');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text.ToString()))).Substring(0, 16).ToLowerInvariant();
        }

        // milliseconds as the page writes them for its script
        internal static string Milliseconds(TimeSpan Time) => ((long)Time.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

        // A request of a page's script, which only looks: a GET (or HEAD) of a live page's answer or of the page for its
        // script. It never renews the session's idle time (WebSessions.Check, D109): only what the user does keeps a
        // session alive, so a page left open signs out after the idle time like any other. Razor Pages takes a handler's
        // name from the address only, case-insensitively, so a request can't reach these handlers and avoid this rule
        internal static bool IsObservation(HttpRequest Request)
        {
            if (!HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method))
            {
                return false;
            }
            Microsoft.Extensions.Primitives.StringValues Handler = Request.Query["handler"];
            return Handler.Count > 0 && Handler.Any(Name => string.Equals(Name, ProgressHandler, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Name, ViewHandler, StringComparison.OrdinalIgnoreCase));
        }
    }
}
