using System.Net.Mail;
using System.Net;
using static ConsoleConfig;
using System.Text;
using System.Diagnostics;
using System.Net.Mime;

namespace CampertronLibrary.function.Base
{
    public static class CampsiteConfig
    {
        //used for writing to the console
        //CachePath (CampertronInternalConfig.CachePath) is where HtmlFile and Email output write the HTML results
        public static void ProcessConsoleConfig(List<ConsoleConfig.ConsoleConfigValue> Config, ref ConsoleConfig.ConfigType LastConfigType, GeneralConfig GenConfig, EmailConfig emailConfig, string? CachePath)
        {
            if (GenConfig.OutputTo == OutputType.Console || GenConfig.OutputTo == OutputType.UnitTest)
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

                //replace the old html file in the cache with the results
                string DbFile = WriteHtmlResults(CachePath, BodyBuilder.ToString());

                //send as email if desired output
                if (GenConfig.OutputTo == OutputType.Email)
                {
                    var message = CreateResultsEmail(emailConfig, BodyBuilder.ToString(), DbFile);

                    using (var client = new SmtpClient())
                    {
                        client.Port = emailConfig.SmtpPort;
                        client.Host = emailConfig.SmtpServer;
                        client.EnableSsl = true;
                        client.DeliveryMethod = SmtpDeliveryMethod.Network;
                        client.UseDefaultCredentials = false;
                        client.Credentials = new NetworkCredential(emailConfig.SmtpUsername, emailConfig.SmtpPassword);
                        client.Send(message);
                        Console.WriteLine("Sent email");
                    }
                }
                else
                {//if outputting to html file open with default browser
                    var p = new Process();
                    p.StartInfo = new ProcessStartInfo(DbFile)
                    {
                        UseShellExecute = true
                    };
                    p.Start();
                }
            }
        }
        //deletes the previous results (*.html directly in CachePath) and writes Html to a new {guid}.html there;
        //returns its path. CachePath is the configured cache directory (CampertronInternalConfig.CachePath, TD-05)
        public static string WriteHtmlResults(string? CachePath, string Html)
        {
            string cachepath = RecDotOrg.sqlite.Clear.CheckCacheDirectory(CachePath);
            if (Directory.Exists(cachepath) == false)
            {
                Directory.CreateDirectory(cachepath);
            }
            else
            {
                foreach(String ThisOldFile in Directory.GetFiles(cachepath, "*.html", SearchOption.TopDirectoryOnly))
                {
                    File.Delete(ThisOldFile);
                }
            }

            //create new html file with results
            string DbFile = Path.Join(cachepath, System.Guid.NewGuid().ToString() + ".html");
            TextWriter TW = new StreamWriter(DbFile);
            TW.Write(Html);
            TW.Close();
            return DbFile;
        }
        //the results email: Body as HTML, with HtmlFile (the file WriteHtmlResults wrote) attached
        public static MailMessage CreateResultsEmail(EmailConfig emailConfig, string Body, string HtmlFile)
        {
            var message = new System.Net.Mail.MailMessage();
            message.From = new MailAddress(emailConfig.SendFromAddress);
            foreach (String ThisToAddress in emailConfig.SendToAddressList)
            {
                message.To.Add(ThisToAddress);
            }
            message.IsBodyHtml = true;
            message.Subject = "Campertron Auto-Email";
            message.Body = Body;

            Attachment data = new Attachment(HtmlFile, MediaTypeNames.Application.Octet);
            message.Attachments.Add(data);
            return message;
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