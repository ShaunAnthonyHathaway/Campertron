using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace CampertronLibrary.function.WebAccess
{
    // what the command line asks of web mode
    public enum WebCommand
    {
        // none of web mode's arguments was given: the console starts as it always did
        None,
        // --web: the web UI
        Web,
        // --healthcheck: asks a running web UI whether it answers (D85)
        HealthCheck
    }

    // Web mode's command line (decisions D80, D85 and D92; business-rules-settings §31):
    //
    //   CampertronConsole --web [--listen <address>] [--port <port>]
    //   CampertronConsole --healthcheck [--listen <address>] [--port <port>]
    //
    // Only a command line with one of these four arguments is read here; every other start is the console's, unchanged
    // (it has never looked at its arguments, apart from the single --version). Once one of them is given the whole line
    // must be right: an unknown argument, a missing or wrong value, an argument given twice, --listen or --port without
    // --web or --healthcheck, and --web together with --healthcheck are refused with fixed words (the argument's value is
    // never repeated), before anything is read or created. A value follows its argument ("--port 8790") or an equals sign
    // ("--port=8790").
    //
    // --listen takes one IP address (IPv4 as four numbers, or IPv6) or the word localhost: never another name, which would
    // have to be resolved, and nothing shortened. --port takes a whole number from 1 to 65535.
    public sealed class WebArguments
    {
        public const string WebArgument = "--web";
        public const string HealthCheckArgument = "--healthcheck";
        public const string ListenArgument = "--listen";
        public const string PortArgument = "--port";
        // the web UI's port unless --port names another (D85)
        public const int DefaultPort = 8790;
        public const string Usage = "Usage: CampertronConsole --web [--listen <address>] [--port <port>]   (the web UI; --healthcheck with the same options asks a running one)";

        public WebCommand Command { get; private set; }
        // --listen's address; null when it was not given or names localhost
        public IPAddress? Listen { get; private set; }
        // --listen localhost: this computer only, on its IPv4 and IPv6 loopback addresses
        public bool ListenLocalhost { get; private set; }
        public bool ListenGiven => Listen != null || ListenLocalhost;
        public int Port { get; private set; } = DefaultPort;
        public bool PortGiven { get; private set; }
        // the problem of a command line that is refused, in fixed words; null when it can be used
        public string? Error { get; private set; }

        // whether the command line is web mode's to read: it has one of the four arguments (also written with "=value")
        public static bool IsRequested(string[]? Args) =>
            Args != null && Args.Any(Arg => Arg != null && (Arg == WebArgument || Arg == HealthCheckArgument || Named(Arg, ListenArgument) || Named(Arg, PortArgument)));

        private static bool Named(string Arg, string Name) => Arg == Name || Arg.StartsWith(Name + "=", StringComparison.Ordinal);

        public static WebArguments Parse(string[]? Args)
        {
            var Result = new WebArguments();
            if (!IsRequested(Args))
            {
                return Result;
            }
            bool Web = false, Health = false;
            string? ListenText = null, PortText = null;
            bool ListenSeen = false, PortSeen = false;
            for (int i = 0; i < Args!.Length; i++)
            {
                string Arg = Args[i] ?? string.Empty;
                if (Arg == WebArgument)
                {
                    if (Web)
                    {
                        return Result.Refuse("--web was given more than once.");
                    }
                    Web = true;
                }
                else if (Arg == HealthCheckArgument)
                {
                    if (Health)
                    {
                        return Result.Refuse("--healthcheck was given more than once.");
                    }
                    Health = true;
                }
                else if (Named(Arg, ListenArgument))
                {
                    if (ListenSeen)
                    {
                        return Result.Refuse("--listen was given more than once.");
                    }
                    ListenSeen = true;
                    if (!TakeValue(Args, ref i, ListenArgument, out ListenText))
                    {
                        return Result.Refuse(ListenProblem);
                    }
                }
                else if (Named(Arg, PortArgument))
                {
                    if (PortSeen)
                    {
                        return Result.Refuse("--port was given more than once.");
                    }
                    PortSeen = true;
                    if (!TakeValue(Args, ref i, PortArgument, out PortText))
                    {
                        return Result.Refuse(PortProblem);
                    }
                }
                else
                {
                    return Result.Refuse("an argument was given that web mode does not know: it knows --web, --healthcheck, --listen and --port.");
                }
            }
            if (Web && Health)
            {
                return Result.Refuse("--web starts the web UI and --healthcheck asks a running one; use one of them.");
            }
            if (!Web && !Health)
            {
                return Result.Refuse("--listen and --port are options of --web (the web UI) and of --healthcheck; without one of them nothing was started.");
            }
            if (ListenSeen)
            {
                if (string.Equals(ListenText, "localhost", StringComparison.Ordinal))
                {
                    Result.ListenLocalhost = true;
                }
                else if (ListenText!.Length > 0 && ListenText[0] != '[' && !ListenText.Contains('%') && WebHosts.TryAddress(ListenText, out IPAddress? Address))
                {
                    Result.Listen = Address;
                }
                else
                {
                    return Result.Refuse(ListenProblem);
                }
            }
            if (PortSeen)
            {
                if (PortText!.Length == 0 || PortText.Length > 5 || !PortText.All(C => C >= '0' && C <= '9') || (PortText.Length > 1 && PortText[0] == '0')
                    || !int.TryParse(PortText, NumberStyles.None, CultureInfo.InvariantCulture, out int Port) || Port < 1 || Port > 65535)
                {
                    return Result.Refuse(PortProblem);
                }
                Result.Port = Port;
                Result.PortGiven = true;
            }
            Result.Command = Web ? WebCommand.Web : WebCommand.HealthCheck;
            return Result;
        }

        private const string ListenProblem = "--listen needs one IP address, such as 127.0.0.1 (this computer only) or 0.0.0.0 (every network interface), or the word localhost.";
        private const string PortProblem = "--port needs a whole number from 1 to 65535.";

        // the value of an argument: after its equals sign, else the next argument (which must not be another option)
        private static bool TakeValue(string[] Args, ref int Index, string Name, out string? Value)
        {
            string Arg = Args[Index];
            if (Arg.Length > Name.Length)
            {
                Value = Arg.Substring(Name.Length + 1);
                return true;
            }
            Value = null;
            if (Index + 1 >= Args.Length || Args[Index + 1] == null || Args[Index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }
            Value = Args[++Index];
            return true;
        }

        private WebArguments Refuse(string Problem)
        {
            Command = WebCommand.None;
            Error = Problem;
            return this;
        }

        // whether an address is this computer only: a loopback address (127.0.0.0/8, ::1)
        public static bool IsLoopback(IPAddress Address) => IPAddress.IsLoopback(Address);

        // the address --healthcheck asks: the one --listen names, or this computer's loopback address when the web UI
        // listens on every interface (no --listen, 0.0.0.0, ::) or on localhost
        public IPAddress HealthCheckAddress()
        {
            if (Listen == null || Listen.Equals(IPAddress.Any))
            {
                return IPAddress.Loopback;
            }
            if (Listen.Equals(IPAddress.IPv6Any))
            {
                return IPAddress.IPv6Loopback;
            }
            return Listen;
        }
    }
}
