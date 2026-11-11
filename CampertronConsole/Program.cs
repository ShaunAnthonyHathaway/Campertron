class Program
{
    static void Main(string[] args)
    {
        if (CampertronLibrary.function.Base.RuntimeInfo.IsVersionRequest(args))
        {
            System.Console.WriteLine(CampertronLibrary.function.Base.RuntimeInfo.Describe(typeof(Program).Assembly));
            return;
        }
        CampertronLibrary.function.RecDotOrg.data.Load.RunConsoleSearch(CampertronLibrary.function.RecDotOrg.data.Load.GetConfig());
    }
}
