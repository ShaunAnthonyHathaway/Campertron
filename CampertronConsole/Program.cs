using CampertronLibrary.function.Base;
using CampertronLibrary.function.Interactive;
using CampertronLibrary.function.RecDotOrg.data;

class Program
{
    static int Main(string[] args)
    {
        //--version comes first and touches nothing else: its output is a stable contract (RuntimeInfo)
        if (RuntimeInfo.IsVersionRequest(args))
        {
            System.Console.WriteLine(RuntimeInfo.Describe(typeof(Program).Assembly));
            return 0;
        }
        //web mode (--web, with --listen and --port) and its health check (--healthcheck): only when the command line has
        //one of these arguments (D80, D85, D92). Every other start is unchanged: it never looked at its arguments
        if (CampertronWeb.WebMode.IsRequested(args))
        {
            return CampertronWeb.WebMode.Run(args);
        }
        //the console's own arguments (v0.4.0's phase 6; D115, D121): --interactive opens the interactive console and --help
        //says how to start it, each the whole command line (else refused, exit code 1, nothing read or created). Every other
        //argument is still ignored, so every other start is unchanged, and a terminal alone never opens the interactive console
        ConsoleArguments Arguments = ConsoleArguments.Parse(args);
        switch (Arguments.Command)
        {
            case ConsoleCommand.Help:
                ConsoleArguments.WriteHelp();
                return 0;
            case ConsoleCommand.Interactive:
                return InteractiveConsole.RunInTerminal(typeof(Program).Assembly);
            case ConsoleCommand.Refused:
                ConsoleUi.Error(Arguments.Error!, ConsoleArguments.Usage);
                return 1;
        }
        CampertronInternalConfig? Config = null;
        Load.FirstStartNotice = () => ConsoleUi.Line("Preparing campground data (first start)...");
        try
        {
            Config = Load.GetConfig();
            //the menu and email mode run until the process ends; a run that ends by itself gives the exit code: 0, or 1
            //when email mode had no search to check and has said why (D64)
            return Load.RunConsole(Config);
        }
        catch (Exception ex)
        {
            //a failure ends the program with a message on standard error and exit code 1, not as an unhandled exception;
            //every SMTP password the run knows (from smtpPasswordFile, CAMPERTRON_EMAIL_PASSWORD or smtpPassword, D66) and
            //ntfy's token and topic (D67) are removed from it
            return ConsoleUi.ReportFailure(ex, Config);
        }
    }
}
