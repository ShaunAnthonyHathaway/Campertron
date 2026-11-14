using CampertronLibrary.function.RecDotOrg.api;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core;

namespace CampertronLibrary.function.Base
{
    public static class Menu
    {
        static internal MenuLocation _location;
        public static void Init(CampertronInternalConfig config)
        {
            _location = MenuLocation.Search;
            if (config.GeneralConfig.OutputTo != OutputType.UnitTest)
            {
                //the menu is the only way to use Console and HtmlFile output: it needs a person at a terminal
                bool Container = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != null || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS") != null;
                string? Problem = StartProblem(Container, ConsoleUi.Current.Interactive);
                if (Problem != null)
                {
                    ConsoleUi.Error(Problem);
                    Environment.Exit(1);
                }
                ConsoleUi.UseUtf8Output();
                Draw(config);
            }
        }
        //why the menu cannot start, or null: a container has no menu (email only), and without a terminal on both
        //standard input and output the menu cannot read keys or place the cursor
        public static string? StartProblem(bool Container, bool Interactive)
        {
            if (Container)
            {
                return "Unsupported output type in container. The container sends results by email only: set outputTo: Email in /config/General.yaml.";
            }
            if (!Interactive)
            {
                return "the Campertron menu needs an interactive terminal. For unattended use, set outputTo: Email in General.yaml.";
            }
            return null;
        }
        //clears the visible screen (never the scrollback, and never before unread messages) and draws the logo and the
        //menu with the current selection
        private static void Draw(CampertronInternalConfig config)
        {
            ConsoleUi.AcknowledgePending();
            ConsoleUi.ClearScreen();
            ConsoleUi.HideCursor();
            Console.Title = "🤖 CAMPERTRON 🤖";
            WriteLogo();
            WriteMenu(_location, config);
        }
        internal static void WriteMenu(MenuLocation CurrentLocation, CampertronInternalConfig config)
        {
            WriteOptions(new List<string>() { "Search", "Refresh Ridb Data (slow)", "View Config", "Exit" }, GetLocationInt(CurrentLocation));
            int ClearLineCount = 0;
            while (ClearLineCount < 6)
            {
                Console.SetCursorPosition(0, Console.GetCursorPosition().Top + ClearLineCount);
                ClearCurrentConsoleLine();
                Console.SetCursorPosition(0, Console.GetCursorPosition().Top - ClearLineCount);
                ClearLineCount++;
            }
            while (true)
            {
                Console.SetCursorPosition(0, Console.GetCursorPosition().Top - 4);
                ProcessInput(Console.ReadKey(), config);
            }
        }
        //View Config: where the settings are, where results go, and which Campertron and .NET this is. One line per
        //value, so a long path can wrap without disturbing anything.
        public static List<ConsoleUi.StyledLine> ViewConfigLines(CampertronInternalConfig config, DateTime UtcNow, CultureInfo Culture, System.Reflection.Assembly? App = null)
        {
            List<ConsoleUi.StyledLine> Lines = new List<ConsoleUi.StyledLine>();
            void Add(string Label, string? Value)
            {
                Lines.Add(new ConsoleUi.StyledLine(Label + " => ", ConsoleColor.Green).Add(Value ?? string.Empty));
            }
            //the first line of --version (RuntimeInfo): the program and its version
            Add("Version", RuntimeInfo.Describe(App ?? System.Reflection.Assembly.GetEntryAssembly() ?? typeof(Menu).Assembly).Split(Environment.NewLine)[0]);
            Add("Config", config.ConfigPath);
            Add("Cache", config.CachePath);
            Add("Output", config.GeneralConfig?.OutputTo.ToString());
            int SearchFiles = 0;
            try
            {
                SearchFiles = Directory.GetFiles(config.ConfigPath ?? string.Empty, "*.yaml", SearchOption.TopDirectoryOnly)
                    .Select(p => Path.GetFileName(p).ToLowerInvariant()).Count(p => p != "general.yaml" && p != "email.yaml");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                //the folder cannot be listed: show 0
            }
            Add("Searches", ConsoleUi.Count(SearchFiles, "search file", "search files"));
            DateTime? Refreshed = config.GeneralConfig?.LastRidbDataRefresh;
            string RefreshText = "never (the campground list that came with Campertron is in use)";
            //a new General.yaml carries a date 50 years back, which means "not refreshed yet"
            if (Refreshed.HasValue && (UtcNow - Refreshed.Value).TotalDays < 3650)
            {
                int Days = Math.Max(0, (int)Math.Floor((UtcNow - Refreshed.Value).TotalDays));
                RefreshText = (Days == 0 ? "today" : ConsoleUi.Count(Days, "day", "days") + " ago") + " (" + Refreshed.Value.ToString("d", Culture) + ")";
            }
            Add("RIDB refresh", RefreshText);
            Add("OS", System.Runtime.InteropServices.RuntimeInformation.OSDescription + " (" + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture + ")");
            Add("Framework", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " (" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture + ")");
            return Lines;
        }
        internal static int GetLocationInt(MenuLocation CurrentLocation)
        {
            switch (CurrentLocation)
            {
                case MenuLocation.Search:
                    return 0;
                case MenuLocation.Refresh:
                    return 1;
                case MenuLocation.ViewConfig:
                    return 2;
                case MenuLocation.Exit:
                    return 3;
                default:
                    return 0;
            }
        }
        internal static void ProcessInput(System.ConsoleKeyInfo NewKey, CampertronInternalConfig config)
        {
            switch (NewKey.Key)
            {
                case ConsoleKey.Enter:
                    switch (_location)
                    {
                        case MenuLocation.Search:
                            CampertronLibrary.function.RecDotOrg.data.Load.RunConsoleSearch(config, true);
                            Init(config);
                            break;
                        case MenuLocation.Refresh:
                            ConsoleUi.ClearScreen();
                            RefreshRidbRecreationData.RefreshData(false, config);
                            Init(config);
                            break;
                        case MenuLocation.ViewConfig:
                            //its own screen (no cursor arithmetic), then the menu again with View Config still selected
                            ConsoleUi.ClearScreen();
                            WriteLogo();
                            ConsoleUi.Write(ViewConfigLines(config, DateTime.UtcNow, CultureInfo.CurrentCulture));
                            ConsoleUi.Line();
                            ConsoleUi.WaitForKey("Press any key to return to the menu...");
                            Draw(config);
                            break;
                        case MenuLocation.Exit:
                            ConsoleUi.ClearScreen();
                            ConsoleUi.RestoreTerminal();
                            Environment.Exit(0);
                            break;
                        default:
                            Environment.Exit(0);
                            break;
                    }
                    break;
                case ConsoleKey.UpArrow:
                    switch (_location)
                    {
                        case MenuLocation.Search:
                            WriteMenu(_location, config);
                            break;
                        case MenuLocation.Refresh:
                            _location = MenuLocation.Search;
                            WriteMenu(_location, config);
                            break;
                        case MenuLocation.ViewConfig:
                            _location = MenuLocation.Refresh;
                            WriteMenu(_location, config);
                            break;
                        case MenuLocation.Exit:
                            _location = MenuLocation.ViewConfig;
                            WriteMenu(_location, config);
                            break;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    switch (_location)
                    {
                        case MenuLocation.Search:
                            _location = MenuLocation.Refresh;
                            WriteMenu(_location, config);
                            break;
                        case MenuLocation.Refresh:
                            _location = MenuLocation.ViewConfig;
                            WriteMenu(_location, config);
                            break;
                        case MenuLocation.ViewConfig:
                            _location = MenuLocation.Exit;
                            WriteMenu(_location, config);
                            break;
                        case MenuLocation.Exit:
                            _location = MenuLocation.Exit;
                            WriteMenu(_location, config);
                            break;
                    }
                    break;
                default:
                    WriteMenu(_location, config);
                    break;
            }
        }
        internal static void WriteOptions(List<String> Options, int Selected)
        {
            int CurrentSelected = 0;
            foreach (String Option in Options)
            {
                ClearCurrentConsoleLine();
                Console.SetCursorPosition(0, Console.GetCursorPosition().Top);
                if (CurrentSelected == Selected)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("-> ");
                    Console.ResetColor();
                }
                else
                {
                    Console.Write("   ");
                }
                Console.Write(Option + "\r\n");
                CurrentSelected++;
            }
        }
        public static void ClearCurrentConsoleLine()
        {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, currentLineCursor);
        }
        internal static void WriteLogo()
        {
            ConsoleUi.HideCursor();
            String Title = @"
  ____                                _                   
 / ___|__ _ _ __ ___  _ __   ___ _ __| |_ _ __ ___  _ __  
| |   / _` | '_ ` _ \| '_ \ / _ \ '__| __| '__/ _ \| '_ \ 
| |__| (_| | | | | | | |_) |  __/ |  | |_| | | (_) | | | |
 \____\__,_|_| |_| |_| .__/ \___|_|   \__|_|  \___/|_| |_|
                     |_|                                  
";
            ConsoleUi.Line(Title, ConsoleColor.DarkGreen);
        }
    }
    internal enum MenuLocation { Search, Refresh, ViewConfig, Exit }
}