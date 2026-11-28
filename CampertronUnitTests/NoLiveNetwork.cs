using System.Collections.Concurrent;
using System.Net;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.search;

// The required test suite never reaches the network (GS1, decision D45; TD-01): every test answers recreation.gov through
// a seam (the console's AvailabilityApi.ConsoleDownload, the interactive search's SearchTransport) or a stand-in on the
// loopback address. This assembly-wide fixture enforces it. Every HttpClient of the test process gets a proxy that lets
// only loopback addresses through and records anything else (sending it to a closed local port, so it fails at once),
// and the console's download seam records any URL that is not a loopback one (answering with an empty month, so the
// console's unbounded retry never hangs a run). Since v0.4.0's phase 14 the no-argument console's Search requests through
// the shared search's transport (ConsoleSearch.RunOptions): a test that gives none gets one that records every request
// and answers with an empty month. Any record fails the run when the fixture ends.
[SetUpFixture]
public class NoLiveNetwork
{
    internal const string EmptyMonth = "{\"campsites\":{},\"count\":0}";
    internal static readonly ConcurrentQueue<string> Reached = new ConcurrentQueue<string>();
    private IWebProxy? OriginalProxy;
    private Func<string, string>? OriginalDownload;
    private Func<SearchHostOptions>? OriginalSearchOptions;
    // the console Search's run options as production has them, before any test replaced them
    internal static Func<SearchHostOptions>? ProductionSearchOptions { get; private set; }

    [OneTimeSetUp]
    public void Guard()
    {
        OriginalProxy = HttpClient.DefaultProxy;
        HttpClient.DefaultProxy = new LoopbackOnly();
        OriginalDownload = AvailabilityApi.ConsoleDownload;
        Func<string, string> Download = OriginalDownload;
        AvailabilityApi.ConsoleDownload = Url =>
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out Uri? Address) && Address.IsLoopback)
            {
                return Download(Url);
            }
            Reached.Enqueue(Url);
            return EmptyMonth;
        };
        OriginalSearchOptions = ConsoleSearch.RunOptions;
        ProductionSearchOptions = OriginalSearchOptions;
        ConsoleSearch.RunOptions = () => new SearchHostOptions
        {
            KeepOutputValues = true,
            Transport = (Request, Token) =>
            {
                Reached.Enqueue(Request.Url);
                return new SearchResponse(200, EmptyMonth);
            }
        };
    }

    [OneTimeTearDown]
    public void Check()
    {
        HttpClient.DefaultProxy = OriginalProxy!;
        AvailabilityApi.ConsoleDownload = OriginalDownload!;
        ConsoleSearch.RunOptions = OriginalSearchOptions!;
        Assert.That(Reached, Is.Empty, "a test reached for the network instead of a seam or a loopback stand-in: " + string.Join(", ", Reached));
    }

    private sealed class LoopbackOnly : IWebProxy
    {
        public ICredentials? Credentials { get; set; }
        // port 9 (discard) on the loopback address: nothing listens, so the request fails at once
        public Uri GetProxy(Uri Destination) => new Uri("http://127.0.0.1:9/");
        public bool IsBypassed(Uri Host)
        {
            if (Host.IsLoopback)
            {
                return true;
            }
            Reached.Enqueue(Host.ToString());
            return false;
        }
    }
}
