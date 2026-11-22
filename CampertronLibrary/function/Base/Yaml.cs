using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
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
        //the deserializer of every settings loader below; the YAML document editor (YamlDocument) and the validation
        //(ConfigValidation) check with it. It rejects unknown keys (D20): no IgnoreUnmatchedProperties
        internal static IDeserializer SettingsDeserializer()
        {
            return new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        }

        // ---- bounded loading (TD-52, decision D41) ----
        //
        // Every settings file is read and deserialized through these limits, the editor's own (YamlDocument, D39), so the
        // loader, the validation and the editor accept and refuse the same documents: at most ConfigFileReplace.MaxBytes
        // bytes (checked before the file is read) and YamlDocument.MaxDocumentLength characters (a byte-order mark counts
        // as one, as in the editor), MaxParserEvents YAML events, MaxDepth nested collections and MaxAliases aliases. The
        // events are counted by YamlDotNet's own parser before anything is deserialized, and the deserializer then reads
        // exactly those events, so it never sees a document over a limit; an alias is a reference to an object already
        // read, never a copy, so a document within the alias limit can't expand. The text is decoded as before
        // (File.ReadAllText's rules: a byte-order mark selects UTF-8, UTF-16 or UTF-32, else UTF-8 with invalid bytes read
        // as U+FFFD); ConfigValidation reports a file that is not strict UTF-8.

        //a settings file over a limit: the message names the limit, never the content
        //(an IOException: it is raised while a file is read, and the callers that tolerate an unreadable file catch it)
        internal sealed class SettingsLimitException : IOException
        {
            internal SettingsLimitException(ConfigCode Code, int Line, int Column, string Message) : base(Message)
            {
                this.Code = Code;
                this.Line = Line;
                this.Column = Column;
            }
            internal ConfigCode Code { get; }
            internal int Line { get; }
            internal int Column { get; }
        }

        //the text of a settings file as the loader reads it: a leading U+FEFF stands for the byte-order mark the reader
        //removed (as in ConfigFileReplace.Read and the editor); Utf8 is false for a UTF-16 or UTF-32 file, ValidUtf8 false
        //for a UTF-8 file with bytes that were read as U+FFFD
        internal sealed class SettingsText
        {
            internal string Text { get; init; } = string.Empty;
            internal bool Utf8 { get; init; } = true;
            internal bool ValidUtf8 { get; init; } = true;
        }

        internal static SettingsText ReadSettingsText(string FilePath)
        {
            byte[] Bytes;
            using (var Stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (Stream.CanSeek && Stream.Length > ConfigFileReplace.MaxBytes)
                {
                    throw TooLarge();
                }
                //read at most one byte more than allowed, also if the file grows meanwhile or has no length
                var Content = new MemoryStream();
                byte[] Buffer = new byte[65536];
                int Read;
                while ((Read = Stream.Read(Buffer, 0, Buffer.Length)) > 0)
                {
                    Content.Write(Buffer, 0, Read);
                    if (Content.Length > ConfigFileReplace.MaxBytes)
                    {
                        throw TooLarge();
                    }
                }
                Bytes = Content.ToArray();
            }
            string Text;
            Encoding Detected;
            //the reader File.ReadAllText uses, with the same encoding detection
            using (var Reader = new StreamReader(new MemoryStream(Bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                Text = Reader.ReadToEnd();
                Detected = Reader.CurrentEncoding;
            }
            bool Bom = HasByteOrderMark(Bytes);
            bool Utf8 = Detected.CodePage == Encoding.UTF8.CodePage;
            bool Valid = true;
            if (Utf8)
            {
                try
                {
                    ConfigFileReplace.StrictUtf8.GetCharCount(Bytes, Bom ? 3 : 0, Bytes.Length - (Bom ? 3 : 0));
                }
                catch (DecoderFallbackException)
                {
                    Valid = false;
                }
            }
            return new SettingsText { Text = Bom ? (char)0xFEFF + Text : Text, Utf8 = Utf8, ValidUtf8 = Valid };
        }

        private static bool HasByteOrderMark(byte[] B) =>
            B.Length >= 3 && B[0] == 0xEF && B[1] == 0xBB && B[2] == 0xBF ||
            B.Length >= 2 && (B[0] == 0xFF && B[1] == 0xFE || B[0] == 0xFE && B[1] == 0xFF) ||
            B.Length >= 4 && B[0] == 0 && B[1] == 0 && B[2] == 0xFE && B[3] == 0xFF;

        private static SettingsLimitException TooLarge() =>
            new SettingsLimitException(ConfigCode.FileTooLarge, 0, 0,
                $"The file is larger than Campertron reads (at most {YamlDocument.MaxDocumentLength} characters and {ConfigFileReplace.MaxBytes} bytes).");

        //the YAML events of a settings text, counted against the limits by YamlDotNet's parser (the deserializer's own);
        //throws SettingsLimitException over a limit and YamlException for text that is not YAML (as the deserializer did)
        internal static List<ParsingEvent> ScanSettings(string Text)
        {
            if (Text.Length > YamlDocument.MaxDocumentLength)
            {
                throw TooLarge();
            }
            string Body = Text.Length > 0 && Text[0] == (char)0xFEFF ? Text.Substring(1) : Text;
            var Events = new List<ParsingEvent>();
            var Reader = new Parser(new StringReader(Body));
            int Depth = 0;
            int Aliases = 0;
            while (MoveNext(Reader, Events))
            {
                ParsingEvent Event = Reader.Current!;
                if (Events.Count == YamlDocument.MaxParserEvents)
                {
                    throw Limit(ConfigCode.TooManyEvents, Event, $"The file has more than {YamlDocument.MaxParserEvents} YAML events.");
                }
                switch (Event)
                {
                    case MappingStart:
                    case SequenceStart:
                        if (++Depth > YamlDocument.MaxDepth)
                        {
                            throw Limit(ConfigCode.TooDeep, Event, $"The file nests lists or mappings more than {YamlDocument.MaxDepth} levels deep.");
                        }
                        break;
                    case MappingEnd:
                    case SequenceEnd:
                        Depth--;
                        break;
                    case AnchorAlias:
                        if (++Aliases > YamlDocument.MaxAliases)
                        {
                            throw Limit(ConfigCode.TooManyAliases, Event, $"The file has more than {YamlDocument.MaxAliases} aliases.");
                        }
                        break;
                }
                Events.Add(Event);
            }
            return Events;
        }

        //YamlDotNet 16.3.0's scanner throws InvalidOperationException for some malformed text (an unclosed flow list
        //followed by another line); its deserializer reported it as a YamlException ("Exception during deserialization")
        //at the current event, and so does this scan, so the loaders' messages and exception types stay as they were
        private static bool MoveNext(Parser Reader, List<ParsingEvent> Events)
        {
            try
            {
                return Reader.MoveNext();
            }
            catch (InvalidOperationException ex)
            {
                Mark At = Events.Count > 0 ? Events[^1].Start : Mark.Empty;
                Mark End = Events.Count > 0 ? Events[^1].End : Mark.Empty;
                throw new YamlException(At, End, "Exception during deserialization", ex);
            }
        }

        private static SettingsLimitException Limit(ConfigCode Code, ParsingEvent At, string Message) =>
            new SettingsLimitException(Code, (int)At.Start.Line, (int)At.Start.Column, Message);

        //the settings of a text, as every loader reads them: scanned within the limits, then deserialized from exactly
        //those events by SettingsDeserializer (null for an empty or comment-only text)
        internal static T LoadSettingsText<T>(string Text)
        {
            return SettingsDeserializer().Deserialize<T>(new YamlDocument.EventList(ScanSettings(Text)));
        }
        internal static T LoadSettingsFile<T>(string FilePath)
        {
            return LoadSettingsText<T>(ReadSettingsText(FilePath).Text);
        }

        public static EmailConfig EmailConfigConvertFromYaml(String Filepath)
        {
            return LoadSettingsFile<EmailConfig>(Filepath);
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
            if (!TryCreateSettingsFile<T>(configfilepath, stringResult, null))
            {
                throw new ConfigFileReplace.SettingsFileExistsException(System.IO.Path.GetFileName(configfilepath));
            }
        }
        //the same, returning false (writing nothing) if the file exists; Mode: the new file's Unix mode, null for the default
        private static bool TryCreateSettingsFile<T>(string configfilepath, string stringResult, UnixFileMode? Mode) where T : class
        {
            return ConfigFileReplace.Create(configfilepath, stringResult, Written => Loads<T>(Written) ? null : "it does not load as settings", Mode);
        }
        //Creates the sample General.yaml or Email.yaml, with the settings and the serializer text a first start writes, if
        //no file of that name exists: the GUI's Create for a settings file removed after the start (CM5, D43). Never writes
        //over a file: returns false, writing nothing, if one exists (also one created meanwhile). Mode is the new file's Unix
        //mode (the GUI creates Email.yaml 0600); null gives the default permissions of a new file, as the start does
        internal static bool CreateSampleSettingsFile(ConfigFileKind Kind, string ConfigPath, UnixFileMode? Mode)
        {
            var Serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            switch (Kind)
            {
                case ConfigFileKind.General:
                    return TryCreateSettingsFile<GeneralConfig>(System.IO.Path.Join(ConfigPath, ConfigFiles.GeneralFileName), Serializer.Serialize(GeneralSampleConfig()), Mode);
                case ConfigFileKind.Email:
                    return TryCreateSettingsFile<EmailConfig>(System.IO.Path.Join(ConfigPath, ConfigFiles.EmailFileName), Serializer.Serialize(EmailSampleConfig()), Mode);
                default:
                    throw new ArgumentException("Only General.yaml and Email.yaml have a sample the GUI creates.", nameof(Kind));
            }
        }
        //Creates a new search file (the GUI's New search, CM6, decision D44): the example search's settings with autoRun:
        //false (not enabled: the console's Search and email mode run it only once the user enables it; the GUI's Results
        //tab runs it when it is chosen, V011-3), in the serializer's text the console writes for the example, at FilePath
        //(a name SearchFileName accepted), create-only: returns false, writing nothing, if a file of that name exists, also
        //one created meanwhile by another program. Mode as for CreateSampleSettingsFile
        internal static bool CreateSampleSearchFile(string FilePath, UnixFileMode? Mode)
        {
            CampertronConfig Search = SearchSampleConfig();
            Search.AutoRun = false;
            var Serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            return TryCreateSettingsFile<CampertronConfig>(FilePath, Serializer.Serialize(Search), Mode);
        }
        private static bool Loads<T>(string Text) where T : class
        {
            try
            {
                return LoadSettingsText<T>(Text) != null;
            }
            catch (Exception ex) when (ex is YamlException || ex is SettingsLimitException)
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
            var configfilepath = System.IO.Path.Join(ConfigPath, ConfigFiles.GeneralFileName);
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
        //the settings in a file read for editing, as the loader reads them (within its limits; a leading U+FEFF is the
        //byte-order mark the loader's reader drops)
        private static T LoadForEdit<T>(ConfigFileReplace.SettingsFile Current) where T : class
        {
            try
            {
                return LoadSettingsText<T>(Current.Text) ?? throw NoSettings();
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
            return LoadSettingsFile<GeneralConfig>(Filepath);
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
            return LoadSettingsFile<CampertronConfig>(Filepath);
        }
        public static EmailConfig EmailConfigGetConfig(string configpath)
        {
            EmailConfig ReturnConfig = new EmailConfig();;
            String DefaultEmailConfigFile = System.IO.Path.Join(configpath, ConfigFiles.EmailFileName);
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
        //Ntfy.yaml (D67): null when the file does not exist (ntfy is off; Campertron never creates it) or holds no settings.
        //Read with the bounded loader like every settings file; the exception passes through unchanged and NoteFile records
        //the file for the error message, which never quotes Ntfy.yaml (its topic acts as a password)
        public static NtfyConfig? NtfyConfigGetConfig(string configpath)
        {
            String NtfyFile = System.IO.Path.Join(configpath, ConfigFiles.NtfyFileName);
            if (!File.Exists(NtfyFile))
            {
                return null;
            }
            try
            {
                return LoadSettingsFile<NtfyConfig>(NtfyFile);
            }
            catch (Exception ex) when (ConsoleUi.NoteFile(ex, NtfyFile))
            {
                throw;
            }
        }
        public static GeneralConfig GeneralConfigGetConfig(string configpath)
        {
            GeneralConfig ReturnConfig = new GeneralConfig();
            String DefaultGeneralConfigFile = System.IO.Path.Join(configpath, ConfigFiles.GeneralFileName);
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
                //a value that would crash the start or download the RIDB data on every start is refused (D41)
                ConfigValidation.ThrowIfRefused(ConfigValidation.CheckGeneral(ReturnConfig, ConfigFiles.GeneralFileName, DateTime.Now));
            }
            catch (Exception ex) when (ConsoleUi.NoteFile(ex, DefaultGeneralConfigFile))
            {
                throw;
            }

            return ReturnConfig;
        }
        public static List<CampertronConfig> CampertronConfigGetConfigs(string configpath)
        {
            return CampertronConfigGetConfigs(configpath, true, true, out _);
        }
        //the same; SearchFilesRead is how many search files were read (an example search this call creates is not one, a
        //ZionConfig.yaml another program created meanwhile is). WarnIfNoneEnabled false leaves the warning about search files
        //of which none is enabled to the caller: email mode refuses to start then, with an error of its own (D64).
        //CreateExampleIfNone false leaves a folder without a search file exactly as it is (no example search, no line about
        //one): email mode refuses to start then too, and creates nothing (D65); the console's Search still creates it
        internal static List<CampertronConfig> CampertronConfigGetConfigs(string configpath, bool WarnIfNoneEnabled, bool CreateExampleIfNone, out int SearchFilesRead)
        {
            List<CampertronConfig> ReturnConfigLst = new List<CampertronConfig>();
            bool FoundValidConfig = false;
            int SearchFiles = 0;
            SearchFilesRead = 0;
            //the search files: every top-level *.yaml except General.yaml and Email.yaml (ConfigFiles, one rule for all)
            foreach (String ConfigFile in ConfigFiles.SearchFiles(configpath))
            {
                SearchFiles++;
                //the exception passes through unchanged; NoteFile only records the file for the error message
                try
                {
                    string Text = ReadSettingsText(ConfigFile).Text;
                    CampertronConfig ThisConfigFile = LoadSettingsText<CampertronConfig>(Text) ?? throw NoSettings();
                    if (ThisConfigFile.AutoRun)
                    {
                        FoundValidConfig = true;
                        //values that would take the search into a dangerous path are refused before its dates (D41), and
                        //so is a filterMatching that is not written Exact or Contains (D51)
                        ThrowIfSearchRefused(ThisConfigFile, Text, Path.GetFileName(ConfigFile));
                        ThisConfigFile.GenerateSearchData();
                        ReturnConfigLst.Add(ThisConfigFile);
                    }
                }
                catch (Exception ex) when (ConsoleUi.NoteFile(ex, ConfigFile))
                {
                    throw;
                }
            }
            if (SearchFiles == 0 && !CreateExampleIfNone)
            {
                //no search file, and the caller (email mode's start) creates no example: nothing is read, written or listed
            }
            else if (SearchFiles == 0)
            {
                //no search file at all: the example search is created and run by the console's Search, only then (email
                //mode never creates it, D65)
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
                    SearchFilesRead = 1;
                    try
                    {
                        string Text = ReadSettingsText(ConfigFilePath).Text;
                        CampertronConfig ThisConfigFile = LoadSettingsText<CampertronConfig>(Text) ?? throw NoSettings();
                        if (ThisConfigFile.AutoRun)
                        {
                            ThrowIfSearchRefused(ThisConfigFile, Text, Path.GetFileName(ConfigFilePath));
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
            else
            {
                SearchFilesRead = SearchFiles;
                if (!FoundValidConfig && WarnIfNoneEnabled)
                {
                    //search files exist but none is enabled: they are the user's and stay exactly as they are (no example is
                    //written over them, TD-13), and nothing is searched
                    ConsoleUi.Warning("No search file has autoRun: true, so no search runs. Set autoRun: true in a search file to run that search.");
                }
            }
            return ReturnConfigLst;
        }
        //an enabled search as a start uses it: refused (ConfigValidationException) for what CheckSearch refuses at start and
        //for a filterMatching its text writes as anything but Exact or Contains (CheckSearchText, D51); the text is the one
        //the settings were loaded from (read once)
        private static void ThrowIfSearchRefused(CampertronConfig Search, string Text, string FileName)
        {
            ConfigValidation.ThrowIfRefused(ConfigValidation.CheckSearch(Search, FileName, DateTime.Now).Concat(ConfigValidation.CheckSearchText(Text, FileName)));
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
            CampertronConfig ZionConfig = SearchSampleConfig();
            CampertronConfigConvertToYaml(ZionConfig, "ZionConfig", ConfigPath);
            return ZionConfig;
        }
        //the settings of the example search (the console writes them as ZionConfig.yaml when it searches without a search
        //file; the GUI's New search writes them, not enabled, under the name the user gives, CM6)
        internal static CampertronConfig SearchSampleConfig()
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
            //a new search matches its lists exactly (D51), so its values are the campground data's own: Watchman's only
            //group campsite type instead of the part "GROUP" a Contains search uses (the same campsites are left out)
            ZionConfig.FilterMatching = FilterMatchMode.Exact;
            ZionConfig.IncludeEquipment = new List<string>() { "Tent", "SMALL TENT", "LARGE TENT OVER 9X12`" };
            ZionConfig.ExcludeEquipment = new List<string>();
            ZionConfig.IncludeSites = new List<string>();
            ZionConfig.ExcludeSites = new List<string>();
            ZionConfig.ConsecutiveDays = 1;
            ZionConfig.IncludeAttributes = new List<string>() { "Shade:Yes", "Campfire Allowed:Yes" };
            ZionConfig.ExcludeAttributes = new List<string>();
            ZionConfig.IncludeCampsiteType = new List<string>();
            ZionConfig.ExcludeCampsiteType = new List<string>() { "GROUP TENT ONLY AREA NONELECTRIC" };
            return ZionConfig;
        }
        public static GeneralConfig GeneralConfigGenerateSampleConfig(String ConfigPath)
        {
            GeneralConfig config = GeneralSampleConfig();
            GeneralConfigConvertToYaml(config, "General", ConfigPath);
            return config;
        }
        //the settings of the sample General.yaml (the start and the GUI's Create write them)
        internal static GeneralConfig GeneralSampleConfig()
        {
            GeneralConfig config = new GeneralConfig();

            config.LastRidbDataRefresh = DateTime.UtcNow.AddYears(-50);
            config.RefreshRidbDataDayInterval = 30;
            config.OutputTo = OutputType.Console;
            config.AutoRefresh = false;
            return config;
        }
        public static EmailConfig EmailConfigGenerateSampleConfig(String ConfigPath)
        {
            EmailConfig config = EmailSampleConfig();
            EmailConfigConvertToYaml(config, "Email", ConfigPath);
            return config;
        }
        //the settings of the sample Email.yaml (the start and the GUI's Create write them)
        internal static EmailConfig EmailSampleConfig()
        {
            EmailConfig config = new EmailConfig();

            config.SendFromAddress = String.Empty;
            config.SmtpPort = 0;
            config.SmtpUsername = String.Empty;
            config.SmtpPassword = String.Empty;
            config.SendToAddressList = new List<string>();
            config.SmtpServer = String.Empty;
            return config;
        }
    }
}
