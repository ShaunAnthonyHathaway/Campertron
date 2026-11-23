using System.Net;
using System.Net.Sockets;

namespace CampertronWeb
{
    // CampertronConsole --healthcheck (decisions D85 and D92; business-rules-settings §31): asks a running web UI whether
    // it answers, for a container's health check (Compose's healthcheck; the image itself has none, because its default
    // mode serves nothing). One GET of /healthz on this computer's loopback address (or the address --listen names) and
    // the port: exit code 0 when the answer is 200 with the text "ok", else 1. It needs no password, sends none, follows
    // no redirect, uses no proxy, reads no settings and creates nothing; it says one line.
    internal static class WebHealthCheck
    {
        internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
        internal const string Healthy = "Campertron's web UI answers.";
        internal const string Unhealthy = "Campertron's web UI does not answer.";

        internal static int Run(IPAddress Address, int Port, TimeSpan Limit, TextWriter Output)
        {
            bool Ok = Ask(Address, Port, Limit);
            Output.WriteLine(Ok ? Healthy : Unhealthy);
            return Ok ? 0 : 1;
        }

        internal static bool Ask(IPAddress Address, int Port, TimeSpan Limit)
        {
            string Host = Address.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + Address + "]" : Address.ToString();
            try
            {
                using var Handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = Limit };
                using var Client = new HttpClient(Handler) { Timeout = Limit };
                using HttpResponseMessage Response = Client.GetAsync("http://" + Host + ":" + Port + CampertronWebHost.HealthPath).GetAwaiter().GetResult();
                if (Response.StatusCode != HttpStatusCode.OK || (Response.Content.Headers.ContentLength ?? 0) > 16)
                {
                    return false;
                }
                return string.Equals(Response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), CampertronWebHost.HealthText, StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException || ex is SocketException)
            {
                return false;
            }
        }
    }
}
