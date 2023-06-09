using CampertronLibrary.function.generic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class ConsoleConfig
{
    public class ConsoleConfigItem
    {
        public String Name { get; set; }
        public List<ConsoleConfigValue> Values { get; set; }
    }
    public class ConsoleConfigValue
    {
        public ConfigType ConfigType { get; set; }
        public String? ConfigValue { get; set; }
        public System.ConsoleColor Color { get; set; }
    }
    public enum ConfigType
    {
        WriteLine,
        Write,
        WriteLineColor,
        WriteColor,
        WriteEmptyLine
    }
}