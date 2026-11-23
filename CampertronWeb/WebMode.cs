using System.Net;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.WebAccess;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CampertronWeb
{
    // CampertronConsole's web mode (decisions D80, D85 and D92; business-rules-settings §31): the console is the web UI's
    // host. Program.Main comes here only when the command line has one of web mode's arguments (WebArguments), so every
    // other start is unchanged.
    //
    // The start, in this order, each refusal one error on standard error and exit code 1 (the code of every handled
    // failure), with nothing started:
    //  1. the command line (WebArguments: fixed words and the usage line);
    //  2. the settings folder (Load.GetFolders: in a container /config and /cache must exist), with no settings file read
    //     or created and no database extracted: this version's web UI needs none of them;
    //  3. Web.yaml, if it exists: an error of its validation refuses the start (a name or a proxy that is silently left
    //     out would change who can reach the web UI); without the file the defaults apply (D81) and nothing is created;
    //  4. the password file: one that exists but can't be used refuses the start while a password is required (the web UI
    //     never runs open because its password got lost, and never writes over the file);
    //  5. the keys folder (WebKeyRepository.Prepare) and a first key;
    //  6. the listening itself (an address or port that can't be used).
    // Then the web UI says where it listens and, while a password is required and none is set, the one-time setup code;
    // it runs until the process is told to stop (Ctrl+C, SIGTERM, a container's stop) and returns 0.
    public static class WebMode
    {
        public static bool IsRequested(string[]? Args) => WebArguments.IsRequested(Args);

        public static int Run(string[] Args)
        {
            WebArguments Arguments = WebArguments.Parse(Args);
            if (Arguments.Error != null)
            {
                ConsoleUi.Error(Arguments.Error, WebArguments.Usage);
                return 1;
            }
            if (Arguments.Command == WebCommand.HealthCheck)
            {
                return WebHealthCheck.Run(Arguments.HealthCheckAddress(), Arguments.Port, WebHealthCheck.Timeout, System.Console.Out);
            }
            try
            {
                CampertronInternalConfig Folders = Load.GetFolders();
                var Settings = new WebHostSettings { SettingsFolder = Folders.ConfigPath!, Port = Arguments.Port };
                return Run(Settings, Arguments, CancellationToken.None);
            }
            catch (Exception ex)
            {
                return ConsoleUi.ReportFailure(ex, (string?)null);
            }
        }

        // the same for given settings (the tests' settings folder and console); Stop ends it as a signal does
        internal static int Run(WebHostSettings Settings, WebArguments Arguments, CancellationToken Stop, Action<WebApplication>? Started = null)
        {
            var Lines = new WebConsoleLines(Settings);
            ConsoleUi.UseUtf8Output();
            Lines.Started(Settings.SettingsFolder);
            if (Prepare(Settings, Arguments) is string[] Problem)
            {
                Settings.Error(Problem[0], Problem.Skip(1).ToArray());
                return 1;
            }
            WebApplication App = CampertronWebHost.Build(Settings);
            try
            {
                try
                {
                    //a first key now, so that keys that can't be written stop the start and not the first sign-in
                    App.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Campertron.Start").Protect(new byte[] { 1 });
                }
                catch (Exception)
                {
                    Settings.Error("web mode can't start: the web UI's keys can't be created in " + WebKeyRepository.FolderName + " in the settings folder.",
                        new[] { KeysHelp, Nothing });
                    return 1;
                }
                try
                {
                    App.StartAsync(Stop).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is IOException || ex is System.Net.Sockets.SocketException)
                {
                    //a port in use (Kestrel's IOException), an address this computer does not have or a port it may not take
                    //(the socket's own error): the same fixed words, never the exception's
                    Settings.Error("web mode can't start: it can't listen on port " + Settings.Port + " at the address it was given.",
                        new[] { "Another program may be using the port, this computer may have no such address, or the port needs more rights (a port below 1024). Choose another with --port or --listen.", Nothing });
                    return 1;
                }
                Lines.Listening(CampertronWebHost.EndPoints(App), Settings.Access.PasswordProtection);
                if (Settings.Access.PasswordProtection && App.Services.GetRequiredService<WebPasswordFile>().State() == WebPasswordState.Missing)
                {
                    Lines.SetupCode(App.Services.GetRequiredService<WebSetupCode>().New());
                }
                Started?.Invoke(App);
                App.WaitForShutdownAsync(Stop).GetAwaiter().GetResult();
                Lines.Stopped();
                return 0;
            }
            finally
            {
                App.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        private const string Nothing = "The web UI was not started.";
        private const string KeysHelp = "The web UI keeps the keys of its sign-in there, for its owner only. Let the user that runs Campertron write to the settings folder.";

        // Steps 3 to 5 of the start: reads Web.yaml into Settings and decides where to listen. Null when the web UI can
        // start, else the error's lines (the problem first). Fixed words, the settings' names and numbers only: no value of
        // a file
        internal static string[]? Prepare(WebHostSettings Settings, WebArguments Arguments)
        {
            string WebFile = Path.Join(Settings.SettingsFolder, ConfigFiles.WebFileName);
            if (File.Exists(WebFile) || new FileInfo(WebFile).LinkTarget != null)
            {
                List<ConfigDiagnostic> Errors = ConfigValidation.ValidateFile(WebFile, DateTime.Now).Where(D => D.Severity == ConfigSeverity.Error).ToList();
                if (Errors.Count > 0)
                {
                    var Problem = new List<string> { "web mode can't start: " + ConfigFiles.WebFileName + " can't be used." };
                    Problem.AddRange(Errors.Select(D => D.Line > 0 ? D.Message + " (line " + D.Line + ")" : D.Message));
                    Problem.Add("File: " + WebFile);
                    Problem.Add("Fix the file in a text editor, or move it out of the settings folder: the web UI then uses its defaults (a password required, reachable from the network).");
                    Problem.Add(Nothing);
                    return Problem.ToArray();
                }
                Settings.Access = Yaml.WebConfigGetConfig(Settings.SettingsFolder) ?? new WebConfig();
            }
            else
            {
                Settings.Access = new WebConfig();
            }
            //--listen decides for this process; without it Web.yaml's networkListening does
            Settings.Listen = Arguments.Listen;
            Settings.ListenLocalhost = Arguments.ListenLocalhost || (!Arguments.ListenGiven && !Settings.Access.NetworkListening);
            if (Settings.Access.PasswordProtection && new WebPasswordFile(Settings.SettingsFolder).State() == WebPasswordState.Unusable)
            {
                return new[]
                {
                    "web mode can't start: the web UI's password file can't be used.",
                    WebPasswordFile.FileName + " in the settings folder is not a password record that Campertron wrote, or it can't be read, or it can't be kept from other users.",
                    "To set a new password, delete the file and start web mode again: it then shows a one-time setup code.",
                    Nothing
                };
            }
            if (new WebKeyRepository(Settings.SettingsFolder).Prepare() is string Keys)
            {
                return new[] { "web mode can't start: " + Keys, KeysHelp, Nothing };
            }
            return null;
        }

        // whether this process listens beyond this computer because of --listen (WebAccessRule: then the file's
        // networkListening does not decide); null when --listen was not given
        internal static bool? ListensOnNetwork(WebArguments Arguments) =>
            Arguments.ListenLocalhost ? false : Arguments.Listen == null ? null : !IPAddress.IsLoopback(Arguments.Listen);
    }
}
