using CampertronLibrary.function.RecDotOrg.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace CampertronLibrary.function.Base
{
    public static class Menu
    {
        static internal MenuLocation location;
        public static void Init(CampertronInternalConfig config, bool Clear)
        {
            location = MenuLocation.Search;
            Start(location, config);
        }
        internal static void Start(MenuLocation CurrentLocation, CampertronInternalConfig config)
        {
            Console.Write("\f\u001bc\x1b[3J");
            Init(CurrentLocation);
            WriteOptions(new List<string>() { "Search", "Refresh Ridb Data (slow)", "View Config", "Exit" }, GetLocationInt(CurrentLocation));
            ProcessInput(Console.ReadKey(), config);
        }
        internal static int GetLocationInt(MenuLocation CurrentLocation)
        {
            switch(CurrentLocation)
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
                    switch (location)
                    {
                        case MenuLocation.Search:
                            CampertronLibrary.function.RecDotOrg.data.Load.StartConsoleSearch(config);
                            break;
                        case MenuLocation.Refresh:
                            RefreshRidbRecreationData.RefreshData(false, config);
                            Start(location, config);
                            break;
                        case MenuLocation.ViewConfig:
                            Console.Write("\f\u001bc\x1b[3J");
                            WriteAscii();
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
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("\r\nPress any key to continue...");
                            Console.ResetColor();
                            Console.ReadKey();
                            Start(location, config);
                            break;
                        case MenuLocation.Exit:
                            Environment.Exit(0);
                            break;
                            default: 
                            Environment.Exit(0);
                            break;
                    }
                    break;
                case ConsoleKey.UpArrow:
                    switch (location)
                    {
                        case MenuLocation.Search:
                            Start(location, config);
                            break;
                        case MenuLocation.Refresh:
                            location = MenuLocation.Search;
                            Start(location, config);
                            break;
                        case MenuLocation.ViewConfig:
                            location = MenuLocation.Refresh;
                            Start(location, config);
                            break;
                        case MenuLocation.Exit:
                            location = MenuLocation.ViewConfig;
                            Start(location, config);
                            break;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    switch (location)
                    {
                        case MenuLocation.Search:
                            location = MenuLocation.Refresh;
                            Start(location, config);
                            break;
                        case MenuLocation.Refresh:
                            location = MenuLocation.ViewConfig;
                            Start(location, config);
                            break;
                        case MenuLocation.ViewConfig:
                            location = MenuLocation.Exit;
                            Start(location, config);
                            break;
                        case MenuLocation.Exit:
                            location = MenuLocation.Exit;
                            Start(location, config);
                            break;
                    }
                    break;
                default:
                    Start(location, config);
                    break;
            }
        }
        internal static void WriteOptions(List<String> Options, int Selected)
        {
            int CurrentSelected = 0;
            foreach (String Option in Options)
            {
                if (CurrentSelected == Selected)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("=====> ");
                    Console.ResetColor();
                }
                else
                {
                    Console.Write("       ");
                }
                Console.Write(Option + "\r\n");
                CurrentSelected++;
            }

        }
        internal static void Init(MenuLocation CurrentLocation)
        {
            Console.CursorVisible = false;
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "🤖 CAMPERTRON 🤖";
            WriteAscii();
        }
        internal static void WriteAscii()
        {
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
