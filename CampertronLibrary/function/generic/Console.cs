using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ConsoleConfig;

namespace CampertronLibrary.function.generic
{
    public static class CampsiteConfig
    {
        public static void ProcessConsoleConfig(List<ConsoleConfig.ConsoleConfigItem> ConfigList, ref ConsoleConfig.ConfigType LastConfigType)
        {
            Console.ResetColor();
            
            foreach (ConsoleConfig.ConsoleConfigItem ThisConfig in ConfigList)
            {
                switch (ThisConfig.ConfigType)
                {
                    case ConsoleConfig.ConfigType.WriteLine:
                        Console.WriteLine(ThisConfig.ConfigValue);
                        break;
                    case ConsoleConfig.ConfigType.Write:
                        Console.Write(ThisConfig.ConfigValue);
                        break;
                    case ConsoleConfig.ConfigType.WriteLineColor:
                        Console.ForegroundColor = ThisConfig.Color;
                        Console.WriteLine(ThisConfig.ConfigValue);
                        Console.ResetColor();
                        break;
                    case ConsoleConfig.ConfigType.WriteColor:
                        Console.ForegroundColor = ThisConfig.Color;
                        Console.Write(ThisConfig.ConfigValue);
                        Console.ResetColor();
                        break;
                    case ConsoleConfig.ConfigType.WriteEmptyLine:
                        if (LastConfigType != ConsoleConfig.ConfigType.WriteEmptyLine)
                        {
                            Console.WriteLine();
                        }
                        break;
                }
                LastConfigType = ThisConfig.ConfigType;
            }
        }
        public static ConsoleConfig.ConsoleConfigItem AddConsoleConfigItem(String WriteLine)
        {
            return new ConsoleConfig.ConsoleConfigItem() { ConfigType = ConsoleConfig.ConfigType.WriteLine, ConfigValue = WriteLine };
        }
        public static ConsoleConfig.ConsoleConfigItem AddConsoleConfigItem(String WriteLine, bool WriteOnly)
        {
            return new ConsoleConfig.ConsoleConfigItem() { ConfigType = ConsoleConfig.ConfigType.Write, ConfigValue = WriteLine };
        }
        public static ConsoleConfig.ConsoleConfigItem AddConsoleConfigItem(String WriteLine, System.ConsoleColor WriteColor)
        {
            return new ConsoleConfig.ConsoleConfigItem() { ConfigType = ConsoleConfig.ConfigType.WriteLineColor, ConfigValue = WriteLine, Color = WriteColor };
        }
        public static ConsoleConfig.ConsoleConfigItem AddConsoleConfigItem(String WriteLine, System.ConsoleColor WriteColor, bool WriteOnly)
        {
            return new ConsoleConfig.ConsoleConfigItem() { ConfigType = ConsoleConfig.ConfigType.WriteColor, ConfigValue = WriteLine, Color = WriteColor };
        }
        public static ConsoleConfig.ConsoleConfigItem AddConsoleConfigItem(bool WriteLine)
        {
            return new ConsoleConfig.ConsoleConfigItem() { ConfigType = ConsoleConfig.ConfigType.WriteLine };
        }
        public static void WriteToConsole(String WriteText, System.ConsoleColor Color)
        {
            Console.ForegroundColor = Color;
            Console.WriteLine(WriteText);
        }
        public static void WriteToConsole(ConsoleConfigItem WriteItem)
        {
            Console.ForegroundColor = WriteItem.Color;
            Console.Write(WriteItem.ConfigValue);
        }
        public static void WriteToConsole(List<ConsoleConfigItem> WriteTextLst)
        {
            foreach (ConsoleConfigItem WriteText in WriteTextLst)
            {
                WriteToConsole(WriteText);              
            }
            Console.WriteLine();
        }        
    }
}