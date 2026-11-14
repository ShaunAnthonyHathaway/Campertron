using CampertronLibrary.function.Base;
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
        CampertronInternalConfig? Config = null;
        Load.FirstStartNotice = () => ConsoleUi.Line("Preparing campground data (first start)...");
        try
        {
            Config = Load.GetConfig();
            Load.RunConsoleSearch(Config);
            return 0;
        }
        catch (Exception ex)
        {
            //a failure ends the program with a message on standard error and exit code 1, not as an unhandled exception
            return ConsoleUi.ReportFailure(ex, Config?.EmailConfig?.SmtpPassword);
        }
    }
}
