using CampertronLibrary.function.RecDotOrg.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace CampertronLibrary.function.Base
{
    public static class Menu
    {
        static internal MenuLocation _location;
        public static void Init(CampertronInternalConfig config)
        {
            _location = MenuLocation.Search;
            Console.Write("\f\u001bc\x1b[3J");
            Console.CursorVisible = false;
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "🤖 CAMPERTRON 🤖";
            WriteLogo();
            WriteMenu(_location, config);
        }
        internal static void WriteMenu(MenuLocation CurrentLocation, CampertronInternalConfig config)
        {
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != null || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS") != null)
            {
                Console.Write("\f\u001bc\x1b[3J");
                Console.WriteLine("Unsupported output type in container");
                Environment.Exit(1);
            }
            else
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
                            CampertronLibrary.function.RecDotOrg.data.Load.RunConsoleSearch(config);
                            Init(config);
                            break;
                        case MenuLocation.Refresh:
                            RefreshRidbRecreationData.RefreshData(false, config);
                            Init(config);
                            break;
                        case MenuLocation.ViewConfig:
                            TimeSpan? RefreshDaysAgo = DateTime.UtcNow - config.GeneralConfig.LastRidbDataRefresh;
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("Config => ");
                            Console.ResetColor();
                            Console.Write(config.ConfigPath + "\r\n");
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("Cache => ");
                            Console.ResetColor();
                            Console.Write(config.CachePath + "\r\n");
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("RIDB refresh => ");
                            Console.ResetColor();
                            Console.Write(RefreshDaysAgo.Value.TotalDays + " days ago\r\n");
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("OS => ");
                            Console.ResetColor();
                            Console.Write(System.Runtime.InteropServices.RuntimeInformation.OSDescription + " (" + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture + ")\r\n");
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("Framework => ");
                            Console.ResetColor();
                            Console.Write(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " (" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture + ")\r\n");
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("\r\nPress any key to continue...");
                            Console.ResetColor();
                            Console.SetCursorPosition(0, Console.GetCursorPosition().Top - 7);
                            Console.ReadKey();
                            WriteMenu(_location, config);
                            break;
                        case MenuLocation.Exit:
                            Console.Write("\f\u001bc\x1b[3J");
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
            Console.CursorVisible = false;
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            String Title = @"
  ____                                _                   
 / ___|__ _ _ __ ___  _ __   ___ _ __| |_ _ __ ___  _ __  
| |   / _` | '_ ` _ \| '_ \ / _ \ '__| __| '__/ _ \| '_ \ 
| |__| (_| | | | | | | |_) |  __/ |  | |_| | | (_) | | | |
 \____\__,_|_| |_| |_| .__/ \___|_|   \__|_|  \___/|_| |_|
                     |_|                                  
";
            Console.WriteLine(Title);
            Console.ResetColor();
        }
    }
    internal enum MenuLocation { Search, Refresh, ViewConfig, Exit }
}