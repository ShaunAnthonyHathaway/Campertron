using System.Collections.Concurrent;
using System.Net;
using CampertronLibrary.function.RecDotOrg.api;

// The required test suite never reaches the network (GS1, decision D45; TD-01): every test answers recreation.gov through
// a seam (the console's AvailabilityApi.ConsoleDownload, the interactive search's SearchTransport) or a stand-in on the
// loopback address. This assembly-wide fixture enforces it. Every HttpClient of the test process gets a proxy that lets
// only loopback addresses through and records anything else (sending it to a closed local port, so it fails at once),
// and the console's download seam records any URL that is not a loopback one (answering with an empty month, so the
// console's unbounded retry never hangs a run). Any record fails the run when the fixture ends.
[SetUpFixture]
public class NoLiveNetwork
{
    internal const string EmptyMonth = "{\"campsites\":{},\"count\":0}";
    internal static readonly ConcurrentQueue<string> Reached = new ConcurrentQueue<string>();
    private IWebProxy? OriginalProxy;
    private Func<string, string>? OriginalDownload;

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
    }

    [OneTimeTearDown]
    public void Check()
    {
        HttpClient.DefaultProxy = OriginalProxy!;
        AvailabilityApi.ConsoleDownload = OriginalDownload!;
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
