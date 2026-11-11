using System.Reflection;
using System.Runtime.InteropServices;

namespace CampertronLibrary.function.Base
{
    // "--version" support for CampertronConsole and CampertronGUI. It reports the application version and
    // the .NET runtime the process actually runs on, without reading or creating any configuration or data,
    // so it can identify an unknown copy of the program and is used by the release smoke tests.
    public static class RuntimeInfo
    {
        public const string VersionArgument = "--version";

        // True only for exactly one argument, "--version". Any other arguments keep the normal startup.
        public static bool IsVersionRequest(string[]? Args)
        {
            return Args != null && Args.Length == 1 && Args[0] == VersionArgument;
        }

        // Two lines: "<AssemblyName> <InformationalVersion>" and
        // "Framework => <RuntimeInformation.FrameworkDescription> (<ProcessArchitecture>)", the same text as View Config.
        public static string Describe(Assembly App)
        {
            string Name = App.GetName().Name ?? "Campertron";
            string Version = App.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? App.GetName().Version?.ToString()
                ?? "unknown";
            return Name + " " + Version + Environment.NewLine
                + "Framework => " + RuntimeInformation.FrameworkDescription + " (" + RuntimeInformation.ProcessArchitecture + ")";
        }
    }
}
