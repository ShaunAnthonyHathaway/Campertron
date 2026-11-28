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
        //why the menu cannot start, or null: a container has no menu (email or, since D67, ntfy only), and without a terminal on both
        //standard input and output the menu cannot read keys or place the cursor
        public static string? StartProblem(bool Container, bool Interactive)
        {
            if (Container)
            {
                return "Unsupported output type in container. The container sends results by email or ntfy only: set outputTo: Email or Ntfy in /config/General.yaml.";
            }
            if (!Interactive)
            {
                return "the Campertron menu needs an interactive terminal. For unattended use, set outputTo: Email or Ntfy in General.yaml.";
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
        //draws the options and reads keys, forever. A key that ProcessInput returns from (an arrow, or a key the menu
        //does not use) is followed by the next drawing in this same loop. Until TD-12 was fixed, ProcessInput called
        //WriteMenu again for every key, so the stack grew with each key press until the process ended with a stack
        //overflow. The console calls and their order are the same as before, so the terminal gets the same bytes.
        internal static void WriteMenu(MenuLocation CurrentLocation, CampertronInternalConfig config)
        {
            while (true)
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
                Console.SetCursorPosition(0, Console.GetCursorPosition().Top - 4);
                ProcessInput(Console.ReadKey(), config);
                CurrentLocation = _location;
            }
        }
        //View Config: where the settings are, where results go, and which Campertron and .NET this is. One line per
        //value, so a long path can wrap without disturbing anything. The items are CurrentConfiguration's (decision D60),
        //which the GUI's Settings > System > Current configuration shows too; each line is "Label => value" with the label
        //colored, byte for byte as before D60.
        public static List<ConsoleUi.StyledLine> ViewConfigLines(CampertronInternalConfig config, DateTime UtcNow, CultureInfo Culture, System.Reflection.Assembly? App = null)
        {
            return CurrentConfiguration.Describe(config, UtcNow, Culture, App ?? System.Reflection.Assembly.GetEntryAssembly() ?? typeof(Menu).Assembly).Items
                .Select(Item => new ConsoleUi.StyledLine(Item.Label + ConfigurationSummary.Separator, ConsoleColor.Green).Add(Item.Value))
                .ToList();
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
        //one key of the menu. Enter runs the selected option (which draws the menu again when it is done); an arrow
        //moves the selection and returns; any other key returns. It never calls WriteMenu: the loop there draws (TD-12).
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
                            break;
                        case MenuLocation.Refresh:
                            _location = MenuLocation.Search;
                            break;
                        case MenuLocation.ViewConfig:
                            _location = MenuLocation.Refresh;
                            break;
                        case MenuLocation.Exit:
                            _location = MenuLocation.ViewConfig;
                            break;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    switch (_location)
                    {
                        case MenuLocation.Search:
                            _location = MenuLocation.Refresh;
                            break;
                        case MenuLocation.Refresh:
                            _location = MenuLocation.ViewConfig;
                            break;
                        case MenuLocation.ViewConfig:
                            _location = MenuLocation.Exit;
                            break;
                        case MenuLocation.Exit:
                            _location = MenuLocation.Exit;
                            break;
                    }
                    break;
                default:
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
            ConsoleUi.Line(LogoText, ConsoleColor.DarkGreen);
        }
        // Campertron's console logo, drawn by the no-argument menu as it always was (an empty line before and after it) and,
        // since D130, at the top of the interactive console's main menu where the terminal is wide enough (LogoLines)
        internal const string LogoText = @"
  ____                                _                   
 / ___|__ _ _ __ ___  _ __   ___ _ __| |_ _ __ ___  _ __  
| |   / _` | '_ ` _ \| '_ \ / _ \ '__| __| '__/ _ \| '_ \ 
| |__| (_| | | | | | | |_) |  __/ |  | |_| | | (_) | | | |
 \____\__,_|_| |_| |_| .__/ \___|_|   \__|_|  \___/|_| |_|
                     |_|                                  
";
        // the logo's own lines: without the empty lines around it and the spaces at the ends of its lines
        internal static readonly IReadOnlyList<string> LogoLines = Array.AsReadOnly(LogoText.Replace("\r", string.Empty).Split('\n')
            .Select(Line => Line.TrimEnd()).SkipWhile(Line => Line.Length == 0).Reverse().SkipWhile(Line => Line.Length == 0).Reverse().ToArray());
    }
    internal enum MenuLocation { Search, Refresh, ViewConfig, Exit }
}