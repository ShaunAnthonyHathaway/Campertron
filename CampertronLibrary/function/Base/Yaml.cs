using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CampertronLibrary.function.Base
{
    public static class Yaml
    {
        public static void EmailConfigConvertToYaml(EmailConfig IncomingConfig, String FileName, string ConfigPath)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var stringResult = serializer.Serialize(IncomingConfig);
            var configfilepath = System.IO.Path.Join(ConfigPath, $"{FileName}.yaml");
            CreateSettingsFile<EmailConfig>(configfilepath, stringResult);
        }
        //the deserializer of every settings loader below; the YAML document editor (YamlDocument) checks its results with it
        internal static IDeserializer SettingsDeserializer()
        {
            return new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        }
        public static EmailConfig EmailConfigConvertFromYaml(String Filepath)
        {
            return SettingsDeserializer().Deserialize<EmailConfig>(File.ReadAllText(Filepath));
        }
        public static void GeneralConfigConvertToYaml(GeneralConfig IncomingConfig, String FileName, string ConfigPath)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var stringResult = serializer.Serialize(IncomingConfig);
            var configfilepath = System.IO.Path.Join(ConfigPath, $"{FileName}.yaml");
            CreateSettingsFile<GeneralConfig>(configfilepath, stringResult);
        }
        //Writes the sample settings files (General.yaml, Email.yaml, the example search): atomically and only if no file of
        //that name exists (ConfigFileReplace.Create, D40), so an existing file is never truncated or written over. Throws
        //ConfigFileReplace.SettingsFileExistsException, writing nothing, if one exists, also one created meanwhile by another
        //program. The text is the serializer's, unchanged
        private static void CreateSettingsFile<T>(string configfilepath, string stringResult) where T : class
        {
            if (!ConfigFileReplace.Create(configfilepath, stringResult, Written => Loads<T>(Written) ? null : "it does not load as settings"))
            {
                throw new ConfigFileReplace.SettingsFileExistsException(System.IO.Path.GetFileName(configfilepath));
            }
        }
        private static bool Loads<T>(string Text) where T : class
        {
            try
            {
                return SettingsDeserializer().Deserialize<T>(Text) != null;
            }
            catch (YamlDotNet.Core.YamlException)
            {
                return false;
            }
        }
        //Writes IncomingConfig.LastRidbDataRefresh into General.yaml after a RIDB refresh (D40). Only that value changes: the
        //file as it is now is read (strict UTF-8), the comment-preserving editor (YamlDocument) changes or adds that one value,
        //so comments, formatting and every other setting stay exactly as the file has them, and the result must load as the
        //file's own settings with the new timestamp; it then replaces General.yaml atomically, keeping the previous bytes in
        //General.yaml.bak, unless General.yaml changed meanwhile (ConfigFileReplace.Replace). Nothing is written if the file
        //already holds that timestamp. Other values of IncomingConfig are not written. Throws (IOException,
        //InvalidDataException, UnauthorizedAccessException) and leaves General.yaml as it was if it can't be read, loaded or
        //edited safely, or replaced; no message quotes the file's content
        public static void GeneralConfigReplaceYaml(GeneralConfig IncomingConfig, string ConfigPath)
        {
            var configfilepath = System.IO.Path.Join(ConfigPath, "General.yaml");
            ConfigFileReplace.SettingsFile Current = ConfigFileReplace.Read(configfilepath);
            GeneralConfig Intended = LoadForEdit<GeneralConfig>(Current);
            if (SameTime(Intended.LastRidbDataRefresh, IncomingConfig.LastRidbDataRefresh))
            {
                return;
            }
            Intended.LastRidbDataRefresh = IncomingConfig.LastRidbDataRefresh;
            YamlDocumentEdit[] Edits = { YamlDocumentEdit.SetScalar("lastRidbDataRefresh", IncomingConfig.LastRidbDataRefresh) };
            YamlDocumentResult Edited = YamlDocument.Edit(Current.Text, Edits, Intended);
            if (!Edited.Succeeded)
            {
                throw new InvalidDataException($"General.yaml can't be updated safely ({Describe(Edited)}); it was left as it is.");
            }
            ConfigFileReplace.Replace(Current, Edited.Text!, Candidate =>
            {
                YamlDocumentResult Check = YamlDocument.VerifyCandidate(Current.Text, Edits, Intended, Candidate);
                return Check.Succeeded ? null : Describe(Check);
            });
        }
        //the settings in a file read for editing, as the loader reads them (without the byte-order mark File.ReadAllText drops)
        private static T LoadForEdit<T>(ConfigFileReplace.SettingsFile Current) where T : class
        {
            string Text = Current.Text.Length > 0 && Current.Text[0] == (char)0xFEFF ? Current.Text.Substring(1) : Current.Text;
            try
            {
                return SettingsDeserializer().Deserialize<T>(Text) ?? throw NoSettings();
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                //the loader's message can quote the file: only its position is kept
                throw new InvalidDataException($"{Current.Name} could not be read (line {ex.Start.Line}, column {ex.Start.Column}); it was left as it is.");
            }
        }
        private static bool SameTime(DateTime? A, DateTime? B) =>
            A == null || B == null ? A == null && B == null : A.Value.Ticks == B.Value.Ticks && A.Value.Kind == B.Value.Kind;
        //the editor's reason, without any value of the document
        private static string Describe(YamlDocumentResult Result)
        {
            YamlDocumentDiagnostic? Error = Result.Diagnostics.FirstOrDefault(D => D.Severity == YamlDocumentSeverity.Error);
            return Error == null ? "no reason given" : Error.Code + ": " + Error.Message;
        }
        public static GeneralConfig GeneralConfigConvertFromYaml(String Filepath)
        {
            return SettingsDeserializer().Deserialize<GeneralConfig>(File.ReadAllText(Filepath));
        }
        public static void CampertronConfigConvertToYaml(CampertronConfig IncomingConfig, String FileName, string ConfigPath)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var stringResult = serializer.Serialize(IncomingConfig);
            var configfilepath = System.IO.Path.Join(ConfigPath, $"{FileName}.yaml");
            CreateSettingsFile<CampertronConfig>(configfilepath, stringResult);
        }
        public static CampertronConfig CampertronConfigConvertFromYaml(String Filepath)
        {
            return SettingsDeserializer().Deserialize<CampertronConfig>(File.ReadAllText(Filepath));
        }
        public static EmailConfig EmailConfigGetConfig(string configpath)
        {
            EmailConfig ReturnConfig = new EmailConfig();;
            String DefaultEmailConfigFile = System.IO.Path.Join(configpath, "Email.yaml");
            if (!File.Exists(DefaultEmailConfigFile))
            {
                try
                {
                    return EmailConfigGenerateSampleConfig(configpath);
                }
                catch (ConfigFileReplace.SettingsFileExistsException)
                {
                    //created meanwhile by another program: read like any existing file below
                }
            }
            //the exception passes through unchanged; NoteFile only records the file for the error message
            try
            {
                ReturnConfig = EmailConfigConvertFromYaml(DefaultEmailConfigFile);
            }
            catch (Exception ex) when (ConsoleUi.NoteFile(ex, DefaultEmailConfigFile))
            {
                throw;
            }

            return ReturnConfig;
        }
        public static GeneralConfig GeneralConfigGetConfig(string configpath)
        {
            GeneralConfig ReturnConfig = new GeneralConfig();
            String DefaultGeneralConfigFile = System.IO.Path.Join(configpath, "General.yaml");
            if (!File.Exists(DefaultGeneralConfigFile))
            {
                try
                {
                    return GeneralConfigGenerateSampleConfig(configpath);
                }
                catch (ConfigFileReplace.SettingsFileExistsException)
                {
                    //created meanwhile by another program: read like any existing file below
                }
            }
            try
            {
                ReturnConfig = GeneralConfigConvertFromYaml(DefaultGeneralConfigFile) ?? throw NoSettings();
            }
            catch (Exception ex) when (ConsoleUi.NoteFile(ex, DefaultGeneralConfigFile))
            {
                throw;
            }

            return ReturnConfig;
        }
        public static List<CampertronConfig> CampertronConfigGetConfigs(string configpath)
        {
            List<CampertronConfig> ReturnConfigLst = new List<CampertronConfig>();
            bool FoundValidConfig = false;
            int SearchFiles = 0;
            foreach (String ConfigFile in Directory.GetFiles(configpath, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                String ThisLowerConfigFile = ConfigFile.ToLower().Trim();
                FileInfo Fi = new FileInfo(ThisLowerConfigFile);
                if (Fi.Name != "general.yaml" && Fi.Name != "email.yaml")
                {
                    SearchFiles++;
                    //the exception passes through unchanged; NoteFile only records the file for the error message
                    try
                    {
                        CampertronConfig ThisConfigFile = CampertronConfigConvertFromYaml(ConfigFile) ?? throw NoSettings();
                        if (ThisConfigFile.AutoRun)
                        {
                            FoundValidConfig = true;
                            ThisConfigFile.GenerateSearchData();
                            ReturnConfigLst.Add(ThisConfigFile);
                        }
                    }
                    catch (Exception ex) when (ConsoleUi.NoteFile(ex, ConfigFile))
                    {
                        throw;
                    }
                }
            }
            if (SearchFiles == 0)
            {
                //a first start: the example search is created (and run) only when there is no search file at all
                String ConfigFilePath = System.IO.Path.Join(configpath, "ZionConfig.yaml");
                ConsoleUi.Status($"No search files found; creating the example search {ConfigFilePath}");
                try
                {
                    CampertronConfig SampleConfig = CampertronConfigGenerateSampleConfig(configpath);
                    SampleConfig.GenerateSearchData();
                    ReturnConfigLst.Add(SampleConfig);
                }
                catch (ConfigFileReplace.SettingsFileExistsException)
                {
                    //another program created ZionConfig.yaml meanwhile: it is never written over (TD-13) and is read like a
                    //search file that was there
                    try
                    {
                        CampertronConfig ThisConfigFile = CampertronConfigConvertFromYaml(ConfigFilePath) ?? throw NoSettings();
                        if (ThisConfigFile.AutoRun)
                        {
                            ThisConfigFile.GenerateSearchData();
                            ReturnConfigLst.Add(ThisConfigFile);
                        }
                    }
                    catch (Exception ex) when (ConsoleUi.NoteFile(ex, ConfigFilePath))
                    {
                        throw;
                    }
                }
            }
            else if (!FoundValidConfig)
            {
                //search files exist but none is enabled: they are the user's and stay exactly as they are (no example is
                //written over them, TD-13), and nothing is searched
                ConsoleUi.Warning("No search file has autoRun: true, so no search runs. Set autoRun: true in a search file to run that search.");
            }
            return ReturnConfigLst;
        }
        //an empty settings file, or one with only comments, deserializes to null. It is reported like any other unusable
        //file (NoteFile names it), instead of failing later with a NullReferenceException (TD-37). Email.yaml is not
        //checked: it is needed only for Email output
        private static InvalidDataException NoSettings()
        {
            return new InvalidDataException("The file has no settings: it is empty or contains only comments.");
        }
        public static CampertronConfig CampertronConfigGenerateSampleConfig(String ConfigPath)
        {
            CampertronConfig ZionConfig = new CampertronConfig();
            ZionConfig.DisplayName = "Zion non-group tent friendly sites";
            ZionConfig.AutoRun = true;
            ZionConfig.CampgroundID = "232445";
            ZionConfig.TotalHumans = 2;
            ZionConfig.SearchBy = SearchTypes.DaysOut;
            ZionConfig.SearchValue = 7;
            ZionConfig.SearchValueDates = new List<String>() { };
            ZionConfig.ShowMonday = true;
            ZionConfig.ShowTuesday = true;
            ZionConfig.ShowWednesday = true;
            ZionConfig.ShowThursday = true;
            ZionConfig.ShowFriday = true;
            ZionConfig.ShowSaturday = true;
            ZionConfig.ShowSunday = true;
            ZionConfig.IncludeEquipment = new List<string>() { "Tent", "SMALL TENT", "LARGE TENT OVER 9X12`" };
            ZionConfig.ExcludeEquipment = new List<string>();
            ZionConfig.IncludeSites = new List<string>();
            ZionConfig.ExcludeSites = new List<string>();
            ZionConfig.ConsecutiveDays = 1;
            ZionConfig.IncludeAttributes = new List<string>() { "Shade:Yes", "Campfire Allowed:Yes" };
            ZionConfig.ExcludeAttributes = new List<string>();
            ZionConfig.IncludeCampsiteType = new List<string>();
            ZionConfig.ExcludeCampsiteType = new List<string>() { "GROUP" };
            CampertronConfigConvertToYaml(ZionConfig, "ZionConfig", ConfigPath);
            return ZionConfig;
        }
        public static GeneralConfig GeneralConfigGenerateSampleConfig(String ConfigPath)
        {
            GeneralConfig config = new GeneralConfig();

            config.LastRidbDataRefresh = DateTime.UtcNow.AddYears(-50);
            config.RefreshRidbDataDayInterval = 30;
            config.OutputTo = OutputType.Console;
            config.AutoRefresh = false;
            GeneralConfigConvertToYaml(config, "General", ConfigPath);
            return config;
        }
        public static EmailConfig EmailConfigGenerateSampleConfig(String ConfigPath)
        {
            EmailConfig config = new EmailConfig();

            config.SendFromAddress = String.Empty;
            config.SmtpPort = 0;
            config.SmtpUsername = String.Empty;
            config.SmtpPassword = String.Empty;
            config.SendToAddressList = new List<string>();
            config.SmtpServer = String.Empty;
            EmailConfigConvertToYaml(config, "Email", ConfigPath);
            return config; 
        }
    }
}
