﻿using System.Net.Mail;
using System.Net;
using static ConsoleConfig;
using System.Text;

namespace CampertronLibrary.function.Base
{
    public static class CampsiteConfig
    {
        //used for writing to the console
        public static void ProcessConsoleConfig(List<ConsoleConfig.ConsoleConfigValue> Config, ref ConsoleConfig.ConfigType LastConfigType, GeneralConfig GenConfig, EmailConfig emailConfig)
        {
            if (GenConfig.OutputTo == OutputType.Console)
            {
                Console.ResetColor();

                foreach (ConsoleConfig.ConsoleConfigValue ThisConfig in Config)
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
            else
            {
                var message = new System.Net.Mail.MailMessage();
                message.From = new MailAddress(emailConfig.SendFromAddress);
                foreach (String ThisToAddress in emailConfig.SendToAddressList)
                {
                    message.To.Add(ThisToAddress);
                }
                message.IsBodyHtml = true;
                message.Subject = "Campertron Auto-Email";                
                StringBuilder BodyBuilder = new StringBuilder();
                foreach (ConsoleConfig.ConsoleConfigValue ThisConfig in Config)
                {
                    switch (ThisConfig.ConfigType)
                    {
                        case ConsoleConfig.ConfigType.WriteLine:
                            BodyBuilder.Append(ThisConfig.ConfigValue + "<br />");
                            break;
                        case ConsoleConfig.ConfigType.Write:
                            BodyBuilder.Append(ThisConfig.ConfigValue);
                            break;
                        case ConsoleConfig.ConfigType.WriteLineColor:
                            BodyBuilder.Append(ThisConfig.ConfigValue + "<br />");
                            break;
                        case ConsoleConfig.ConfigType.WriteColor:
                            BodyBuilder.Append(ThisConfig.ConfigValue);
                            break;
                        case ConsoleConfig.ConfigType.WriteEmptyLine:
                            if (LastConfigType != ConsoleConfig.ConfigType.WriteEmptyLine)
                            {
                                BodyBuilder.Append("<br />");
                            }
                            break;
                    }
                    LastConfigType = ThisConfig.ConfigType;
                }

                message.Body = BodyBuilder.ToString();

                using (var client = new SmtpClient())
                {
                    client.Port = emailConfig.SmtpPort;
                    client.Host = emailConfig.SmtpServer;
                    client.EnableSsl = true;
                    client.DeliveryMethod = SmtpDeliveryMethod.Network;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(emailConfig.SmtpUsername, emailConfig.SmtpPassword);
                    client.Send(message);
                }
            }
        }
        public static ConsoleConfig.ConsoleConfigValue AddConsoleConfigItem(String WriteLine)
        {
            return new ConsoleConfig.ConsoleConfigValue() { ConfigType = ConsoleConfig.ConfigType.WriteLine, ConfigValue = WriteLine };
        }
        public static ConsoleConfig.ConsoleConfigValue AddConsoleConfigItem(String WriteLine, bool WriteOnly)
        {
            return new ConsoleConfig.ConsoleConfigValue() { ConfigType = ConsoleConfig.ConfigType.Write, ConfigValue = WriteLine };
        }
        public static ConsoleConfig.ConsoleConfigValue AddConsoleConfigItem(String WriteLine, System.ConsoleColor WriteColor)
        {
            return new ConsoleConfig.ConsoleConfigValue() { ConfigType = ConsoleConfig.ConfigType.WriteLineColor, ConfigValue = WriteLine, Color = WriteColor };
        }
        public static ConsoleConfig.ConsoleConfigValue AddConsoleConfigItem(String WriteLine, System.ConsoleColor WriteColor, bool WriteOnly)
        {
            return new ConsoleConfig.ConsoleConfigValue() { ConfigType = ConsoleConfig.ConfigType.WriteColor, ConfigValue = WriteLine, Color = WriteColor };
        }
        public static ConsoleConfig.ConsoleConfigValue AddConsoleConfigItem(bool WriteLine)
        {
            return new ConsoleConfig.ConsoleConfigValue() { ConfigType = ConsoleConfig.ConfigType.WriteLine };
        }
        public static void WriteToConsole(String WriteText, System.ConsoleColor Color)
        {
            Console.ForegroundColor = Color;
            Console.WriteLine(WriteText);
        }
        public static void WriteToConsole(ConsoleConfigValue WriteItem)
        {
            Console.ForegroundColor = WriteItem.Color;
            Console.Write(WriteItem.ConfigValue);
        }
        public static void WriteToConsole(List<ConsoleConfigValue> WriteTextLst)
        {
            foreach (ConsoleConfigValue WriteText in WriteTextLst)
            {
                WriteToConsole(WriteText);
            }
            Console.WriteLine();
        }
    }
}