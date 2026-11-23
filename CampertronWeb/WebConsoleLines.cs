using System.Net;
using System.Net.Sockets;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.WebAccess;

namespace CampertronWeb
{
    // Everything web mode writes to the console (decision D81 point 7, D92; business-rules-settings §31). ASP.NET Core's
    // own log output is off, so these fixed lines are all a console or a container's log shows: where the web UI listens,
    // the one-time setup code, failed sign-ins with the client's address, an unexpected failure by its type's name, and
    // that it stopped. Never a request's path, query, header, cookie, form value or body, never a password, a hash or a
    // key, and never the message of an exception (it could name a path or a value).
    internal sealed class WebConsoleLines
    {
        private readonly WebHostSettings _Settings;
        private readonly object _Lock = new object();
        private readonly Dictionary<string, (int Count, DateTime Said)> _Failed = new(StringComparer.Ordinal);
        private DateTime _FailureSaid = DateTime.MinValue;
        internal const int MaxAddresses = 256;
        internal static readonly TimeSpan Quiet = TimeSpan.FromMinutes(1);

        internal WebConsoleLines(WebHostSettings Settings)
        {
            _Settings = Settings;
        }

        internal void Started(string SettingsFolder)
        {
            _Settings.Line(ConsoleUi.ProgramIdentity() + " | settings " + SettingsFolder + " | web UI");
        }

        // where the web UI listens, and how it is opened
        internal void Listening(IReadOnlyCollection<IPEndPoint> EndPoints, bool PasswordProtection)
        {
            foreach (IPEndPoint EndPoint in EndPoints)
            {
                bool Any = EndPoint.Address.Equals(IPAddress.Any) || EndPoint.Address.Equals(IPAddress.IPv6Any);
                string Shown = EndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + EndPoint.Address + "]" : EndPoint.Address.ToString();
                string Scope = Any ? "every network interface of this computer"
                    : IPAddress.IsLoopback(EndPoint.Address) ? "this computer only"
                    : "that address only";
                string Open = Any ? "open http://<this computer's address>:" + EndPoint.Port + "/ in a browser"
                    : "open http://" + Shown + ":" + EndPoint.Port + "/ in a browser";
                _Settings.Line("Web UI: listening on http://" + Shown + ":" + EndPoint.Port + " (" + Scope + "); " + Open + ". "
                    + (PasswordProtection ? "A password is required." : "No password is required (passwordProtection: false in " + WebFile + ")."));
            }
        }

        private const string WebFile = "Web.yaml";

        // the one-time setup code: on a line of its own, between two fixed marks, so it is found in a container's log
        internal void SetupCode(string Code)
        {
            _Settings.Line("Web UI: no password is set yet. Enter this one-time setup code in the browser to set it (valid for "
                + (int)WebSetupCode.Lifetime.TotalMinutes + " minutes, for one use):");
            _Settings.Line("Web UI setup code: >>> " + Code + " <<<");
        }

        // a failed sign-in (also a wrong setup code or a wrong current password): the client's address and how many came
        // from it since the web UI started. At most one line a minute for an address; the count carries the rest. The
        // address is the connection's, or the one a trusted proxy names: text of an IP address, never of a request
        internal void FailedSignIn(IPAddress? Client)
        {
            string Address = Client == null ? "an unknown address" : (Client.IsIPv4MappedToIPv6 ? Client.MapToIPv4() : Client).ToString();
            DateTime Now = _Settings.UtcNow();
            int Count;
            lock (_Lock)
            {
                if (!_Failed.ContainsKey(Address) && _Failed.Count >= MaxAddresses)
                {
                    Address = "other addresses";
                }
                (int Count, DateTime Said) Known = _Failed.GetValueOrDefault(Address, (0, DateTime.MinValue));
                Count = Known.Count + 1;
                bool Say = Now - Known.Said >= Quiet;
                _Failed[Address] = (Count, Say ? Now : Known.Said);
                if (!Say)
                {
                    return;
                }
            }
            _Settings.Line("Web UI: failed sign-in from " + Address + " (" + ConsoleUi.Count(Count, "failed sign-in", "failed sign-ins") + " from it since the web UI started).");
        }

        // an unexpected failure of a request: its exception type's name only, at most one line a minute
        internal void RequestFailed(Exception Failure)
        {
            DateTime Now = _Settings.UtcNow();
            lock (_Lock)
            {
                if (Now - _FailureSaid < Quiet)
                {
                    return;
                }
                _FailureSaid = Now;
            }
            _Settings.Warning("Web UI: a request failed unexpectedly (" + Failure.GetType().Name + "); the browser was shown an error page.");
        }

        internal void Stopped()
        {
            _Settings.Line("Web UI: stopped.");
        }
    }
}
