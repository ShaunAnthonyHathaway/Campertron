using System.Net;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.WebAccess;

namespace CampertronWeb
{
    // What a web UI is started with (decision D92): the settings folder, where it listens and where its few console lines
    // go. The console fills it from the command line and Web.yaml (WebMode); no request can change any of it, and no
    // request names a folder or a file (D81).
    public sealed class WebHostSettings
    {
        // the settings folder (in a container /config): Web.yaml, Web.password and Web.keys are in it
        public string SettingsFolder { get; set; } = string.Empty;
        // the address to listen on; null with ListenLocalhost false is every network interface
        public IPAddress? Listen { get; set; }
        // this computer only, on its IPv4 and IPv6 loopback addresses
        public bool ListenLocalhost { get; set; }
        public int Port { get; set; } = WebArguments.DefaultPort;
        // Web.yaml's settings (the defaults without the file)
        public WebConfig Access { get; set; } = new WebConfig();

        // the web UI's console lines (D81: only Campertron's own fixed lines; ASP.NET Core's log output is off): a line of
        // the log with its time, a warning and an error on standard error
        public Action<string> Line { get; set; } = ConsoleUi.LoopLine;
        public Action<string> Warning { get; set; } = ConsoleUi.Warning;
        public Action<string, string[]> Error { get; set; } = (Text, Details) => ConsoleUi.Error(Text, Details);

        // ---- seams for tests ----

        internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
        // the iterations of a new password hash (tests can't spend 600,000 on every case)
        internal int PasswordIterations { get; set; } = WebPasswordHash.Iterations;
        // this computer's name for the Host check
        internal string? MachineName { get; set; } = SafeMachineName();
        // the window of the rate limits (one minute)
        internal TimeSpan RateWindow { get; set; } = TimeSpan.FromMinutes(1);
        // a page that throws, so the tests can see what an unexpected failure shows; never mapped in production
        internal bool MapFailingPage { get; set; }

        private static string? SafeMachineName()
        {
            try
            {
                return Environment.MachineName;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
