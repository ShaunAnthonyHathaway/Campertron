using System.Globalization;
using System.Net.Mail;
using System.Reflection;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace CampertronLibrary.function.Base
{
    // a settings file Campertron refuses because of what the validation found (D41). Its message is the errors' messages,
    // which hold key names and numbers only, never a value of the file
    internal sealed class ConfigValidationException : Exception
    {
        internal ConfigValidationException(string Message, IReadOnlyList<ConfigDiagnostic> Diagnostics) : base(Message)
        {
            this.Diagnostics = Diagnostics;
        }
        internal IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }
    }

    // Validation of Campertron's settings files (CM3, decision D41; business-rules.md §15). It reads a file exactly as the
    // loaders do (Yaml.ReadSettingsText and Yaml.ScanSettings: the same limits; Yaml.SettingsDeserializer on the same
    // events: the same naming, enums and unknown-key rejection, D20), so an Error about the YAML means the loader refuses
    // the file too, and then checks what the loaded settings mean. Diagnostics name the file, key, list item and position,
    // never a value (ConfigDiagnostic). Severity: Error, Campertron refuses the file (or, for Email.yaml with outputTo:
    // Email, refuses to start the Email loop); Warning, it uses the file but probably not as intended; Info, a fact.
    //
    // At run time the loaders refuse, through ThrowIfRefused, only what would otherwise take a dangerous path (a search
    // window that runs toward DateTime.MaxValue, a filter item that stops the search, an undefined numeric choice, a
    // refresh interval that stops the start or refreshes on every start); errors the loaders already refused before D41
    // (YAML errors, too few or unreadable dates, equal dates) keep their own messages. Warnings are not printed at run
    // time; they are for a caller that shows settings (the later GUI). Nothing here opens a network connection, sends,
    // writes or deletes a file.
    internal static class ConfigValidation
    {
        // the errors the run-time loaders refuse through this validation
        private static readonly HashSet<ConfigCode> RefusedByCheck = new()
        {
            ConfigCode.InvalidChoice,
            ConfigCode.RefreshIntervalInvalid,
            ConfigCode.SearchValueNegative,
            ConfigCode.SearchWindowOutOfRange,
            ConfigCode.ListItemNull,
            ConfigCode.FilterMatchingInvalid
        };

        // the filter lists whose empty (null) item stops a search (FilterData calls Contains on it)
        private static readonly string[] ItemNullStops = { "includeEquipment", "excludeEquipment", "includeAttributes", "excludeAttributes", "includeCampsiteType", "excludeCampsiteType" };

        private static readonly Regex IsoDate = new Regex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant);

        // a search's filterMatching (D51) and the names it may be written as (in any case, as every Campertron choice)
        internal const string FilterMatchingKey = "filterMatching";
        private static readonly string[] FilterMatchingNames = Enum.GetNames<FilterMatchMode>();
        private const string FilterMatchingMessage = "filterMatching must be Exact or Contains; a search file without filterMatching uses Contains.";

        // ---- run time ----

        // throws, for a loader, if the diagnostics hold an error it refuses through this validation
        internal static void ThrowIfRefused(IEnumerable<ConfigDiagnostic> Diagnostics)
        {
            List<ConfigDiagnostic> Refused = Diagnostics.Where(D => D.Severity == ConfigSeverity.Error && RefusedByCheck.Contains(D.Code)).ToList();
            if (Refused.Count > 0)
            {
                throw new ConfigValidationException(string.Join(" ", Refused.Select(D => D.Message)), Refused);
            }
        }

        // before the Email loop searches anything (Load.RunConsoleSearch): Email.yaml must hold what sending needs. Throws
        // ConfigValidationException naming the settings, never their values. No connection is made and nothing is sent
        internal static void ThrowIfEmailOutputRefused(EmailConfig? Email)
        {
            List<ConfigDiagnostic> Errors = CheckEmail(Email, true, ConfigFiles.EmailFileName, null).Where(D => D.Severity == ConfigSeverity.Error).ToList();
            if (Errors.Count > 0)
            {
                throw new ConfigValidationException("the Email output can't start: " + string.Join(" ", Errors.Select(D => D.Message)), Errors);
            }
        }

        // ---- a text ----

        internal static List<ConfigDiagnostic> ValidateGeneral(string Text, DateTime Now, string FileName = ConfigFiles.GeneralFileName) =>
            ValidateText(Text, ConfigFileKind.General, FileName, Now, null, out _);

        internal static List<ConfigDiagnostic> ValidateEmail(string Text, bool EmailOutput, string FileName = ConfigFiles.EmailFileName) =>
            ValidateText(Text, ConfigFileKind.Email, FileName, DateTime.Now, EmailOutput ? OutputType.Email : null, out _);

        internal static List<ConfigDiagnostic> ValidateSearch(string Text, string FileName, DateTime Now, OutputType? Output = null) =>
            ValidateText(Text, ConfigFileKind.Search, FileName, Now, Output, out _);

        private static List<ConfigDiagnostic> ValidateText(string Text, ConfigFileKind Kind, string FileName, DateTime Now, OutputType? Output, out object? Settings)
        {
            var Out = new List<ConfigDiagnostic>();
            Settings = null;
            switch (Kind)
            {
                case ConfigFileKind.General:
                    {
                        GeneralConfig? G = Analyze<GeneralConfig>(Text, Kind, FileName, Out, out Layout Where, out bool Empty);
                        if (Empty)
                        {
                            Out.Add(Make(ConfigCode.Empty, ConfigSeverity.Error, Kind, FileName, null, null, null, "The file has no settings: it is empty or contains only comments."));
                        }
                        if (G != null)
                        {
                            Out.AddRange(CheckGeneral(G, FileName, Now, Where));
                            Settings = G;
                        }
                        break;
                    }
                case ConfigFileKind.Email:
                    {
                        bool EmailOutput = Output == OutputType.Email;
                        EmailConfig? E = Analyze<EmailConfig>(Text, Kind, FileName, Out, out Layout Where, out bool Empty);
                        if (E != null || Empty)
                        {
                            Out.AddRange(CheckEmail(E, EmailOutput, FileName, Where));
                            Settings = E;
                        }
                        break;
                    }
                default:
                    {
                        CampertronConfig? C = Analyze<CampertronConfig>(Text, Kind, FileName, Out, out Layout Where, out bool Empty);
                        if (Empty)
                        {
                            Out.Add(Make(ConfigCode.Empty, ConfigSeverity.Error, Kind, FileName, null, null, null, "The file has no settings: it is empty or contains only comments."));
                        }
                        if (C != null)
                        {
                            Out.AddRange(CheckSearch(C, FileName, Now, Output, Where));
                            Settings = C;
                        }
                        //filterMatching as written (D51), unless the loader already refused it
                        if (!Out.Any(D => D.Key == FilterMatchingKey && D.Severity == ConfigSeverity.Error))
                        {
                            Out.AddRange(FilterMatchingProblems(Where, FileName));
                        }
                        break;
                    }
            }
            return Out;
        }

        // ---- files and the settings folder ----

        // a settings file, read as the loaders read it; its kind comes from its name (ConfigFiles)
        internal static List<ConfigDiagnostic> ValidateFile(string FilePath, DateTime Now, OutputType? Output = null) =>
            ValidateFile(FilePath, Now, Output, out _);

        private static List<ConfigDiagnostic> ValidateFile(string FilePath, DateTime Now, OutputType? Output, out object? Settings)
        {
            Settings = null;
            string Name = Path.GetFileName(FilePath);
            ConfigFileKind Kind = ConfigFiles.Classify(Name);
            Yaml.SettingsText Read;
            try
            {
                Read = Yaml.ReadSettingsText(FilePath);
            }
            catch (Yaml.SettingsLimitException Limit)
            {
                return new List<ConfigDiagnostic> { Make(Limit.Code, ConfigSeverity.Error, Kind, Name, null, null, null, Limit.Message) };
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return new List<ConfigDiagnostic> { Make(ConfigCode.FileMissing, ConfigSeverity.Error, Kind, Name, null, null, null, "The file does not exist.") };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                //the exception's message is left out: it names nothing a user can act on beyond this
                return new List<ConfigDiagnostic> { Make(ConfigCode.NotLoadable, ConfigSeverity.Error, Kind, Name, null, null, null, "The file can't be read (it may be open in another program or not readable).") };
            }
            var Out = new List<ConfigDiagnostic>();
            string Consequence = Kind == ConfigFileKind.General
                ? "Campertron reads it, but can't update its refresh timestamp or edit it, which needs UTF-8."
                : "Campertron reads it, but can't edit it, which needs UTF-8.";
            if (!Read.Utf8)
            {
                Out.Add(Make(ConfigCode.NotUtf8, ConfigSeverity.Warning, Kind, Name, null, null, null, "The file is UTF-16 or UTF-32 text. " + Consequence));
            }
            else if (!Read.ValidUtf8)
            {
                Out.Add(Make(ConfigCode.InvalidUtf8, ConfigSeverity.Warning, Kind, Name, null, null, null, "The file has bytes that are not valid UTF-8, which are read as the replacement character. " + Consequence));
            }
            Out.AddRange(ValidateText(Read.Text, Kind, Name, Now, Output, out Settings));
            return Out;
        }

        // every settings file of a settings folder, as a start would use them: General.yaml (its outputTo decides how
        // Email.yaml is judged), Email.yaml and each search file, and whether any search runs. Open question #40 stays
        // open: with no enabled search the Email loop keeps checking 0 searches, which is reported, not changed
        internal static List<ConfigDiagnostic> ValidateFolder(string ConfigPath, DateTime Now)
        {
            var Out = new List<ConfigDiagnostic>();
            string GeneralPath = Path.Join(ConfigPath, ConfigFiles.GeneralFileName);
            OutputType? Output = null;
            if (File.Exists(GeneralPath))
            {
                Out.AddRange(ValidateFile(GeneralPath, Now, null, out object? General));
                Output = (General as GeneralConfig)?.OutputTo;
            }
            else
            {
                Out.Add(Make(ConfigCode.FileMissing, ConfigSeverity.Info, ConfigFileKind.General, ConfigFiles.GeneralFileName, null, null, null,
                    "The file does not exist; Campertron creates it with its default settings at its next start."));
            }
            string EmailPath = Path.Join(ConfigPath, ConfigFiles.EmailFileName);
            if (File.Exists(EmailPath))
            {
                Out.AddRange(ValidateFile(EmailPath, Now, Output));
            }
            else
            {
                Out.Add(Output == OutputType.Email
                    ? Make(ConfigCode.FileMissing, ConfigSeverity.Error, ConfigFileKind.Email, ConfigFiles.EmailFileName, null, null, null,
                        "The file does not exist; Campertron creates an empty one at its next start, and the Email output needs it filled in.")
                    : Make(ConfigCode.FileMissing, ConfigSeverity.Info, ConfigFileKind.Email, ConfigFiles.EmailFileName, null, null, null,
                        "The file does not exist; Campertron creates an empty one at its next start."));
            }
            List<string> Searches = ConfigFiles.SearchFiles(ConfigPath);
            bool AnyEnabled = false;
            foreach (string SearchPath in Searches)
            {
                List<ConfigDiagnostic> Found = ValidateFile(SearchPath, Now, Output, out object? Search);
                Out.AddRange(Found);
                AnyEnabled |= Runs(Search, Found);
            }
            ConfigDiagnostic? Folder = SearchFolderDiagnostic(Searches.Count, AnyEnabled, Output);
            if (Folder != null)
            {
                Out.Add(Folder);
            }
            return Out;
        }

        // whether a search file runs at a start as ValidateFolder counts it: autoRun: true and no error (CM6: the GUI's
        // Searches tab counts the files on disk the same way)
        internal static bool RunsAtStart(string FilePath, DateTime Now, OutputType? Output)
        {
            List<ConfigDiagnostic> Found = ValidateFile(FilePath, Now, Output, out object? Search);
            return Runs(Search, Found);
        }

        private static bool Runs(object? Search, List<ConfigDiagnostic> Found) =>
            Search is CampertronConfig C && C.AutoRun && Found.All(D => D.Severity != ConfigSeverity.Error);

        // What the settings folder's search files mean for a start (ValidateFolder; the GUI's Searches tab shows the same,
        // CM6): no search file (the start creates and runs its example) or no usable enabled one (nothing is searched; the
        // Email loop keeps checking 0 searches, open question #40), else null. AnyEnabled: a search file with autoRun: true
        // whose validation found no error
        internal static ConfigDiagnostic? SearchFolderDiagnostic(int SearchFiles, bool AnyEnabled, OutputType? Output)
        {
            if (SearchFiles == 0)
            {
                return Make(ConfigCode.NoSearchFiles, ConfigSeverity.Info, ConfigFileKind.Search, ConfigFiles.SearchPattern, null, null, null,
                    "There is no search file; Campertron creates and runs its example search at its next start.");
            }
            if (!AnyEnabled)
            {
                return Make(ConfigCode.NoEnabledSearch, ConfigSeverity.Warning, ConfigFileKind.Search, ConfigFiles.SearchPattern, null, null, null,
                    Output == OutputType.Email
                        ? "No usable search file has autoRun: true, so nothing is searched: the Email loop checks 0 searches every minute until a search is enabled and Campertron is started again."
                        : "No usable search file has autoRun: true, so nothing is searched.");
            }
            return null;
        }

        // ---- the YAML: the loader's verdict, with positions ----

        // where the top-level keys and the items of their lists are, for the diagnostics' positions, and the value of every
        // occurrence of a key (a duplicate key has several)
        private sealed class Layout
        {
            internal readonly Dictionary<string, (int Line, int Column)> Keys = new(StringComparer.Ordinal);
            internal readonly Dictionary<(string Key, int Index), (int Line, int Column)> Items = new();
            internal readonly Dictionary<string, List<ParsingEvent>> Values = new(StringComparer.Ordinal);
        }

        private sealed class Property
        {
            internal string? Key;
            internal int KeyAt;
            internal int ValueAt;
            internal int End;
        }

        // the settings of a text as the loader reads them (null if it can't, or if the text has none: Empty), with a
        // diagnostic for each limit, YAML or key the loader refuses, and duplicate keys (which the loader accepts: the
        // last wins)
        private static T? Analyze<T>(string Text, ConfigFileKind Kind, string FileName, List<ConfigDiagnostic> Out, out Layout Where, out bool Empty) where T : class
        {
            Where = new Layout();
            Empty = false;
            List<ParsingEvent> Events;
            try
            {
                Events = Yaml.ScanSettings(Text);
            }
            catch (Yaml.SettingsLimitException Limit)
            {
                Out.Add(Make(Limit.Code, ConfigSeverity.Error, Kind, FileName, null, null, null, Limit.Message, Limit.Line, Limit.Column));
                return null;
            }
            catch (YamlException Ex)
            {
                //the parser's message can quote the file: only the position is kept
                Out.Add(Make(ConfigCode.Malformed, ConfigSeverity.Error, Kind, FileName, null, null, null, "The file is not valid YAML.", (int)Ex.Start.Line, (int)Ex.Start.Column));
                return null;
            }
            List<int> Documents = Events.Select((E, I) => (E, I)).Where(P => P.E is DocumentStart).Select(P => P.I).ToList();
            if (Documents.Count > 1)
            {
                ParsingEvent Second = Events[Documents[1]];
                Out.Add(Make(ConfigCode.MultipleDocuments, ConfigSeverity.Error, Kind, FileName, null, null, null,
                    "The file holds more than one YAML document; Campertron reads exactly one.", (int)Second.Start.Line, (int)Second.Start.Column));
                return null;
            }
            ParsingEvent? Root = Documents.Count == 1 ? Events[Documents[0] + 1] : null;
            if (Root == null || Root is Scalar Null && IsNull(Null))
            {
                Empty = true;
                return null;
            }
            if (Root is not MappingStart)
            {
                Out.Add(Make(ConfigCode.NotAMapping, ConfigSeverity.Error, Kind, FileName, null, null, null,
                    "The file does not hold settings as key: value lines.", (int)Root.Start.Line, (int)Root.Start.Column));
                return null;
            }
            int RootAt = Documents[0] + 1;
            List<Property> Properties = ReadProperties(Events, RootAt, Where, Kind, FileName, Out);
            try
            {
                //the loader's own verdict: its deserializer on the same events
                T? Loaded = Yaml.SettingsDeserializer().Deserialize<T>(new YamlDocument.EventList(Events));
                AliasesToCollections(Events, Properties, Kind, FileName, Out);
                return Loaded;
            }
            catch (YamlException Whole)
            {
                IReadOnlyDictionary<string, PropertyInfo> Known = YamlDocument.ModelKeys(typeof(T));
                int Found = 0;
                //each property alone, to report every one the loader refuses; an alias may refer to another property, so a
                //document with aliases gets only the loader's first error
                if (!Events.Any(E => E is AnchorAlias))
                {
                    foreach (Property P in Properties)
                    {
                        var Alone = new List<ParsingEvent>();
                        Alone.AddRange(Events.Take(RootAt + 1));
                        Alone.AddRange(Events.Skip(P.KeyAt).Take(P.End - P.KeyAt));
                        int RootEnd = SkipNode(Events, RootAt);
                        Alone.AddRange(Events.Skip(RootEnd - 1));
                        try
                        {
                            Yaml.SettingsDeserializer().Deserialize<T>(new YamlDocument.EventList(Alone));
                        }
                        catch (YamlException One)
                        {
                            Out.Add(KeyProblem(P, Events, Known, One, Kind, FileName, Where));
                            Found++;
                        }
                    }
                }
                if (Found == 0)
                {
                    Out.Add(Make(ConfigCode.NotLoadable, ConfigSeverity.Error, Kind, FileName, null, null, null,
                        "Campertron's settings loader can't read the file.", (int)Whole.Start.Line, (int)Whole.Start.Column));
                }
                return null;
            }
        }

        // YamlDotNet reads an alias to a list or mapping, used where a text is expected (a setting's value or a list item),
        // as the text "(Collection)" without an error: the alias limit bounds it, nothing is expanded, but the setting
        // does not hold what the file seems to say
        private static void AliasesToCollections(List<ParsingEvent> Events, List<Property> Properties, ConfigFileKind Kind, string FileName, List<ConfigDiagnostic> Out)
        {
            var Collections = new HashSet<string>(StringComparer.Ordinal);
            foreach (ParsingEvent Event in Events)
            {
                if (Event is NodeEvent Node && !Node.Anchor.IsEmpty && Event is MappingStart or SequenceStart)
                {
                    Collections.Add(Node.Anchor.Value);
                }
            }
            if (Collections.Count == 0)
            {
                return;
            }
            bool ToCollection(ParsingEvent E) => E is AnchorAlias A && Collections.Contains(A.Value.Value);
            foreach (Property P in Properties.Where(P => P.Key != null))
            {
                string Key = Kind == ConfigFileKind.Email ? "a setting" : P.Key!;
                if (ToCollection(Events[P.ValueAt]))
                {
                    Out.Add(Make(ConfigCode.NotAScalar, ConfigSeverity.Warning, Kind, FileName, Kind == ConfigFileKind.Email ? null : P.Key, null, null,
                        Key + " refers to a list or mapping, which Campertron reads as the text (Collection).", (int)Events[P.ValueAt].Start.Line, (int)Events[P.ValueAt].Start.Column));
                }
                else if (Events[P.ValueAt] is SequenceStart)
                {
                    int Item = P.ValueAt + 1;
                    for (int Index = 0; Events[Item] is not SequenceEnd; Index++)
                    {
                        if (ToCollection(Events[Item]))
                        {
                            Out.Add(Make(ConfigCode.NotAScalar, ConfigSeverity.Warning, Kind, FileName, Kind == ConfigFileKind.Email ? null : P.Key, Index, null,
                                $"{Key} item {Index + 1} refers to a list or mapping, which Campertron reads as the text (Collection).", (int)Events[Item].Start.Line, (int)Events[Item].Start.Column));
                        }
                        Item = SkipNode(Events, Item);
                    }
                }
            }
        }

        private static bool IsNull(Scalar S) =>
            S.Style == ScalarStyle.Plain && (S.Value.Length == 0 || S.Value is "~" or "null" or "Null" or "NULL");

        // the top-level properties of the root mapping at RootAt, their positions, and duplicate keys
        private static List<Property> ReadProperties(List<ParsingEvent> Events, int RootAt, Layout Where, ConfigFileKind Kind, string FileName, List<ConfigDiagnostic> Out)
        {
            var Properties = new List<Property>();
            var Seen = new HashSet<string>(StringComparer.Ordinal);
            int At = RootAt + 1;
            while (Events[At] is not MappingEnd)
            {
                ParsingEvent KeyEvent = Events[At];
                int ValueAt = SkipNode(Events, At);
                int End = SkipNode(Events, ValueAt);
                string? Key = KeyEvent is Scalar K ? K.Value : null;
                Properties.Add(new Property { Key = Key, KeyAt = At, ValueAt = ValueAt, End = End });
                if (Key != null)
                {
                    if (!Seen.Add(Key))
                    {
                        Out.Add(Make(ConfigCode.DuplicateKey, ConfigSeverity.Warning, Kind, FileName, Kind == ConfigFileKind.Email ? null : Key, null, null,
                            (Kind == ConfigFileKind.Email ? "A key" : Key) + " appears more than once; Campertron uses the last one, and its editor refuses to change the file.",
                            (int)KeyEvent.Start.Line, (int)KeyEvent.Start.Column));
                    }
                    Where.Keys[Key] = ((int)KeyEvent.Start.Line, (int)KeyEvent.Start.Column);
                    if (!Where.Values.TryGetValue(Key, out List<ParsingEvent>? Occurrences))
                    {
                        Where.Values[Key] = Occurrences = new List<ParsingEvent>();
                    }
                    Occurrences.Add(Events[ValueAt]);
                    if (Events[ValueAt] is SequenceStart)
                    {
                        int Item = ValueAt + 1;
                        int Index = 0;
                        while (Events[Item] is not SequenceEnd)
                        {
                            Where.Items[(Key, Index++)] = ((int)Events[Item].Start.Line, (int)Events[Item].Start.Column);
                            Item = SkipNode(Events, Item);
                        }
                    }
                }
                At = End;
            }
            return Properties;
        }

        // the index after the node that starts at At
        private static int SkipNode(List<ParsingEvent> Events, int At)
        {
            if (Events[At] is not (MappingStart or SequenceStart))
            {
                return At + 1;
            }
            int Depth = 0;
            do
            {
                if (Events[At] is MappingStart or SequenceStart)
                {
                    Depth++;
                }
                else if (Events[At] is MappingEnd or SequenceEnd)
                {
                    Depth--;
                }
                At++;
            }
            while (Depth > 0);
            return At;
        }

        // what the loader refuses in one property, from its setting's type and the shape of its value; never the value or
        // the exception's message
        private static ConfigDiagnostic KeyProblem(Property P, List<ParsingEvent> Events, IReadOnlyDictionary<string, PropertyInfo> Known, YamlException One,
            ConfigFileKind Kind, string FileName, Layout Where)
        {
            int Line = (int)One.Start.Line;
            int Column = (int)One.Start.Column;
            if (P.Key == null)
            {
                return Make(ConfigCode.NotLoadable, ConfigSeverity.Error, Kind, FileName, null, null, null, "A key is not a plain name.", Line, Column);
            }
            if (!Known.TryGetValue(P.Key, out PropertyInfo? Setting))
            {
                //an unknown key of Email.yaml is not named: it could be a mistyped value
                return Kind == ConfigFileKind.Email
                    ? Make(ConfigCode.UnknownKey, ConfigSeverity.Error, Kind, FileName, null, null, null,
                        "The file has a key Campertron does not know; Campertron can't use the file until the key is removed or corrected.", Where.Keys.GetValueOrDefault(P.Key).Line, Where.Keys.GetValueOrDefault(P.Key).Column)
                    : Make(ConfigCode.UnknownKey, ConfigSeverity.Error, Kind, FileName, P.Key, null, null,
                        P.Key + " is not a setting Campertron knows; Campertron can't use the file until the key is removed or corrected.", Where.Keys.GetValueOrDefault(P.Key).Line, Where.Keys.GetValueOrDefault(P.Key).Column);
            }
            Type Type = Nullable.GetUnderlyingType(Setting.PropertyType) ?? Setting.PropertyType;
            ParsingEvent Value = Events[P.ValueAt];
            bool Collection = Value is MappingStart or SequenceStart;
            if (Type == typeof(List<string>))
            {
                if (Value is not SequenceStart)
                {
                    return Make(ConfigCode.NotAList, ConfigSeverity.Error, Kind, FileName, P.Key, null, null, P.Key + " must be a list.", Line, Column);
                }
                int? Index = Where.Items.Where(I => I.Key.Key == P.Key && (I.Value.Line < Line || I.Value.Line == Line && I.Value.Column <= Column))
                    .Select(I => (int?)I.Key.Index).DefaultIfEmpty(null).Max();
                return Make(ConfigCode.NotAScalar, ConfigSeverity.Error, Kind, FileName, P.Key, Index, null, "An item of " + P.Key + " is not a single value.", Line, Column);
            }
            if (Collection)
            {
                return Make(ConfigCode.NotAScalar, ConfigSeverity.Error, Kind, FileName, P.Key, null, null, P.Key + " must be a single value, not a list or a mapping.", Line, Column);
            }
            if (Type.IsEnum)
            {
                return Make(ConfigCode.InvalidChoice, ConfigSeverity.Error, Kind, FileName, P.Key, null, null, P.Key + " must be one of " + Choices(Type) + ".", Line, Column);
            }
            if (Type == typeof(bool))
            {
                return Make(ConfigCode.InvalidBoolean, ConfigSeverity.Error, Kind, FileName, P.Key, null, null, P.Key + " must be true or false.", Line, Column);
            }
            if (Type == typeof(int))
            {
                return Make(ConfigCode.InvalidNumber, ConfigSeverity.Error, Kind, FileName, P.Key, null, null, P.Key + $" must be a whole number from {int.MinValue} to {int.MaxValue}.", Line, Column);
            }
            if (Type == typeof(double))
            {
                return Make(ConfigCode.InvalidNumber, ConfigSeverity.Error, Kind, FileName, P.Key, null, null, P.Key + " must be a number.", Line, Column);
            }
            if (Type == typeof(DateTime))
            {
                return Make(ConfigCode.InvalidDate, ConfigSeverity.Error, Kind, FileName, P.Key, null, null, P.Key + " must be a date and time, such as 2026-01-31T00:00:00Z.", Line, Column);
            }
            return Make(ConfigCode.NotLoadable, ConfigSeverity.Error, Kind, FileName, P.Key, null, null, "Campertron's settings loader can't read " + P.Key + ".", Line, Column);
        }

        // the names a user writes (UnitTest is for Campertron's tests)
        private static string Choices(Type Enum_)
        {
            List<string> Names = Enum.GetNames(Enum_).Where(N => N != nameof(OutputType.UnitTest)).ToList();
            return Names.Count < 2 ? string.Join("", Names) : string.Join(", ", Names.Take(Names.Count - 1)) + " or " + Names[^1];
        }

        // ---- what the settings mean ----

        internal static List<ConfigDiagnostic> CheckGeneral(GeneralConfig G, string FileName, DateTime Now) => CheckGeneral(G, FileName, Now, null);

        private static List<ConfigDiagnostic> CheckGeneral(GeneralConfig G, string FileName, DateTime Now, Layout? Where)
        {
            var Out = new List<ConfigDiagnostic>();
            void Add(ConfigCode Code, ConfigSeverity Severity, string Key, string Message) =>
                Out.Add(Make(Code, Severity, ConfigFileKind.General, FileName, Key, null, Where, Message));
            if (!Enum.IsDefined(G.OutputTo))
            {
                Add(ConfigCode.InvalidChoice, ConfigSeverity.Error, "outputTo", "outputTo must be one of " + Choices(typeof(OutputType)) + ".");
            }
            else if (G.OutputTo == OutputType.UnitTest)
            {
                Add(ConfigCode.TestOutput, ConfigSeverity.Info, "outputTo", "outputTo: UnitTest is for Campertron's tests: a start with it shows and searches nothing.");
            }
            //the automatic refresh compares UtcNow.AddDays(-refreshRidbDataDayInterval) with lastRidbDataRefresh (Load.DbExistsCheck)
            DateTime UtcNow = Now.ToUniversalTime();
            double Interval = G.RefreshRidbDataDayInterval;
            if (!double.IsFinite(Interval) || !CanAdd(() => UtcNow.AddDays(-Interval)))
            {
                Add(ConfigCode.RefreshIntervalInvalid, G.AutoRefresh ? ConfigSeverity.Error : ConfigSeverity.Warning, "refreshRidbDataDayInterval", G.AutoRefresh
                    ? "refreshRidbDataDayInterval must be a number of days Campertron can count back from today; with autoRefresh: true this one would stop the start or refresh the campground data on every start."
                    : "refreshRidbDataDayInterval is not a usable number of days (it is not used while autoRefresh is false).");
            }
            else if (G.AutoRefresh && Interval <= 0)
            {
                Add(ConfigCode.RefreshEveryStart, ConfigSeverity.Warning, "refreshRidbDataDayInterval",
                    "refreshRidbDataDayInterval is 0 or less: with autoRefresh: true Campertron downloads and rebuilds the campground data on every start.");
            }
            if (G.AutoRefresh && G.LastRidbDataRefresh == null)
            {
                Add(ConfigCode.LastRefreshMissing, ConfigSeverity.Warning, "lastRidbDataRefresh",
                    "lastRidbDataRefresh is missing: with autoRefresh: true the campground data is never refreshed automatically.");
            }
            else if (G.AutoRefresh && G.LastRidbDataRefresh > UtcNow)
            {
                Add(ConfigCode.LastRefreshInFuture, ConfigSeverity.Warning, "lastRidbDataRefresh",
                    "lastRidbDataRefresh is in the future: the automatic refresh waits until the interval after it has passed.");
            }
            return Out;
        }

        internal static List<ConfigDiagnostic> CheckEmail(EmailConfig? E, bool EmailOutput, string FileName) => CheckEmail(E, EmailOutput, FileName, null);

        // what sending needs (CampsiteConfig.SendWithSmtp, CreateResultsEmail): errors with outputTo: Email, warnings
        // otherwise. Addresses are parsed as sending parses them (MailAddress for the sender, MailAddressCollection.Add,
        // which takes a comma-separated list, for each recipient entry). No connection, no sign-in, no email
        private static List<ConfigDiagnostic> CheckEmail(EmailConfig? E, bool EmailOutput, string FileName, Layout? Where)
        {
            var Out = new List<ConfigDiagnostic>();
            ConfigSeverity Needed = EmailOutput ? ConfigSeverity.Error : ConfigSeverity.Warning;
            void Add(ConfigCode Code, ConfigSeverity Severity, string? Key, int? Index, string Message) =>
                Out.Add(Make(Code, Severity, ConfigFileKind.Email, FileName, Key, Index, Where, Message));
            if (E == null)
            {
                Add(ConfigCode.Empty, Needed, null, null, EmailOutput
                    ? "The file has no settings, and the Email output needs the SMTP server, port and addresses."
                    : "The file has no settings: it is empty or contains only comments (it is needed only for outputTo: Email).");
                return Out;
            }
            if (string.IsNullOrWhiteSpace(E.SmtpServer))
            {
                Add(ConfigCode.SmtpServerMissing, Needed, "smtpServer", null, "smtpServer is empty.");
            }
            if (E.SmtpPort < 1 || E.SmtpPort > 65535)
            {
                Add(ConfigCode.SmtpPortOutOfRange, Needed, "smtpPort", null, "smtpPort is not between 1 and 65535.");
            }
            if (string.IsNullOrWhiteSpace(E.SendFromAddress))
            {
                Add(ConfigCode.SenderMissing, Needed, "sendFromAddress", null, "sendFromAddress is empty.");
            }
            else if (!MailAddress.TryCreate(E.SendFromAddress, out _))
            {
                Add(ConfigCode.SenderInvalid, Needed, "sendFromAddress", null, "sendFromAddress is not a valid email address.");
            }
            if (E.SendToAddressList == null || E.SendToAddressList.Count == 0)
            {
                Add(ConfigCode.RecipientsMissing, Needed, "sendToAddressList", null, "sendToAddressList has no address.");
            }
            else
            {
                for (int i = 0; i < E.SendToAddressList.Count; i++)
                {
                    if (!IsRecipient(E.SendToAddressList[i]))
                    {
                        Add(ConfigCode.RecipientInvalid, Needed, "sendToAddressList", i, $"sendToAddressList item {i + 1} is not a valid email address.");
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(E.SmtpUsername) || string.IsNullOrEmpty(E.SmtpPassword))
            {
                Add(ConfigCode.CredentialsMissing, ConfigSeverity.Warning, string.IsNullOrWhiteSpace(E.SmtpUsername) ? "smtpUsername" : "smtpPassword", null,
                    "smtpUsername or smtpPassword is empty: Campertron then signs in without them, which most SMTP servers refuse.");
            }
            return Out;
        }

        private static bool IsRecipient(string? Entry)
        {
            if (string.IsNullOrWhiteSpace(Entry))
            {
                return false;
            }
            try
            {
                new MailAddressCollection().Add(Entry);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        internal static List<ConfigDiagnostic> CheckSearch(CampertronConfig C, string FileName, DateTime Now, OutputType? Output = null) =>
            CheckSearch(C, FileName, Now, Output, null);

        // A search file's text as the loader reads it, for what its settings alone can't show (D51): filterMatching written
        // as anything but Exact or Contains, or more than once. YamlDotNet reads an empty filterMatching as none (Contains), a
        // number as the choice with that number, " Exact " as Exact and "Exact, Contains" as their combination; each is a
        // guess, so the file is refused (FilterMatchingInvalid, an Error, refused at start for an enabled search). The
        // search loader calls it with the text it loaded; the text validation (ValidateSearch) includes it
        internal static List<ConfigDiagnostic> CheckSearchText(string Text, string FileName)
        {
            List<ParsingEvent> Events;
            try
            {
                Events = Yaml.ScanSettings(Text);
            }
            catch (Exception ex) when (ex is YamlException || ex is Yaml.SettingsLimitException)
            {
                //the loader reports the text itself
                return new List<ConfigDiagnostic>();
            }
            int Root = Events.FindIndex(E => E is DocumentStart) + 1;
            if (Root <= 0 || Root >= Events.Count || Events[Root] is not MappingStart)
            {
                return new List<ConfigDiagnostic>();
            }
            var Where = new Layout();
            ReadProperties(Events, Root, Where, ConfigFileKind.Search, FileName, new List<ConfigDiagnostic>());
            return FilterMatchingProblems(Where, FileName);
        }

        // filterMatching (D51) must be written once, as Exact or Contains in any case (plain or quoted); anything else,
        // an empty value included, is an Error whatever autoRun says, like a value the loader can't read. Missing is fine
        // (Contains). Positions only, never the value
        private static List<ConfigDiagnostic> FilterMatchingProblems(Layout Where, string FileName)
        {
            var Out = new List<ConfigDiagnostic>();
            if (!Where.Values.TryGetValue(FilterMatchingKey, out List<ParsingEvent>? Values))
            {
                return Out;
            }
            if (Values.Count > 1)
            {
                Out.Add(Make(ConfigCode.FilterMatchingInvalid, ConfigSeverity.Error, ConfigFileKind.Search, FileName, FilterMatchingKey, null, null,
                    "filterMatching appears more than once; write it once, as Exact or Contains.", (int)Values[1].Start.Line, (int)Values[1].Start.Column));
            }
            foreach (ParsingEvent Value in Values)
            {
                bool Named = Value is Scalar S && !IsNull(S) && FilterMatchingNames.Contains(S.Value, StringComparer.OrdinalIgnoreCase);
                if (!Named)
                {
                    Out.Add(Make(ConfigCode.FilterMatchingInvalid, ConfigSeverity.Error, ConfigFileKind.Search, FileName, FilterMatchingKey, null, null,
                        FilterMatchingMessage, (int)Value.Start.Line, (int)Value.Start.Column));
                    break;
                }
            }
            return Out;
        }

        // a search as GenerateSearchDates and FilterData use it (business-rules §1 to §4). A problem that would stop or
        // derail an enabled search is an error; in a disabled search (never run) the same problem is a warning
        private static List<ConfigDiagnostic> CheckSearch(CampertronConfig C, string FileName, DateTime Now, OutputType? Output, Layout? Where)
        {
            var Out = new List<ConfigDiagnostic>();
            ConfigSeverity Stops = C.AutoRun ? ConfigSeverity.Error : ConfigSeverity.Warning;
            void Add(ConfigCode Code, ConfigSeverity Severity, string? Key, int? Index, string Message) =>
                Out.Add(Make(Code, Severity, ConfigFileKind.Search, FileName, Key, Index, Where, Message));
            if (!C.AutoRun)
            {
                Add(ConfigCode.SearchDisabled, ConfigSeverity.Info, "autoRun", null, "autoRun is false: this search does not run.");
            }
            if (string.IsNullOrWhiteSpace(C.DisplayName))
            {
                Add(ConfigCode.DisplayNameMissing, ConfigSeverity.Warning, "displayName", null, "displayName is empty: the search's results have no name.");
            }
            if (string.IsNullOrWhiteSpace(C.CampgroundID))
            {
                Add(ConfigCode.CampgroundIdMissing, ConfigSeverity.Warning, "campgroundID", null, "campgroundID is empty: the search is skipped.");
            }
            else if (!C.CampgroundID.Trim().All(char.IsAsciiDigit))
            {
                //whether the ID is a campground of the campground data is checked when the search runs (its scope lines)
                Add(ConfigCode.CampgroundIdNotNumeric, ConfigSeverity.Warning, "campgroundID", null, "campgroundID is not a number, and campground IDs are numbers: the search finds nothing.");
            }
            if (C.TotalHumans == null || C.TotalHumans <= 0)
            {
                Add(ConfigCode.TotalHumansMissing, ConfigSeverity.Warning, "totalHumans", null, "totalHumans is not a positive number: the search finds no campsite.");
            }
            if (!(C.ShowMonday || C.ShowTuesday || C.ShowWednesday || C.ShowThursday || C.ShowFriday || C.ShowSaturday || C.ShowSunday))
            {
                Add(ConfigCode.NoWeekdays, ConfigSeverity.Warning, null, null, "No day of the week is enabled (showMonday to showSunday): the search finds nothing.");
            }
            //settings without their text (a caller's own settings): a filterMatching that is no choice; the text's spelling is
            //checked by FilterMatchingProblems
            if (Where == null && C.FilterMatching is FilterMatchMode Matching && !Enum.IsDefined(Matching))
            {
                Add(ConfigCode.FilterMatchingInvalid, ConfigSeverity.Error, FilterMatchingKey, null, FilterMatchingMessage);
            }
            if (C.ConsecutiveDays < 1)
            {
                Add(ConfigCode.ConsecutiveDaysBelowOne, ConfigSeverity.Warning, "consecutiveDays", null, Output == OutputType.Email
                    ? "consecutiveDays is below 1: no result is shown, and with outputTo: Email the openings it finds are recorded as reported although no email shows them."
                    : "consecutiveDays is below 1: no result is shown.");
            }
            foreach ((string Key, List<string>? Items) in new (string, List<string>?)[]
            {
                ("includeEquipment", C.IncludeEquipment), ("excludeEquipment", C.ExcludeEquipment), ("includeSites", C.IncludeSites), ("excludeSites", C.ExcludeSites),
                ("includeAttributes", C.IncludeAttributes), ("excludeAttributes", C.ExcludeAttributes), ("includeCampsiteType", C.IncludeCampsiteType), ("excludeCampsiteType", C.ExcludeCampsiteType)
            })
            {
                for (int i = 0; Items != null && i < Items.Count; i++)
                {
                    if (Items[i] == null)
                    {
                        bool Stop = ItemNullStops.Contains(Key);
                        Add(ConfigCode.ListItemNull, Stop ? Stops : ConfigSeverity.Warning, Key, i, Stop
                            ? $"{Key} item {i + 1} is empty, which stops the search when a campsite is checked."
                            : $"{Key} item {i + 1} is empty and matches no campsite.");
                    }
                    else if (string.IsNullOrWhiteSpace(Items[i]))
                    {
                        Add(ConfigCode.ListItemEmpty, ConfigSeverity.Warning, Key, i, $"{Key} item {i + 1} is blank, which matches every or no campsite rather than one value.");
                    }
                }
            }
            CheckDates(C, Now, Stops, Add);
            return Out;
        }

        // the dates GenerateSearchDates produces (business-rules §2), without generating them
        private static void CheckDates(CampertronConfig C, DateTime Now, ConfigSeverity Stops, Action<ConfigCode, ConfigSeverity, string?, int?, string> Add)
        {
            List<string>? Dates = C.SearchValueDates;
            DateTime? Last = null;
            switch (C.SearchBy)
            {
                case SearchTypes.DaysOut:
                case SearchTypes.MonthsOut:
                    {
                        string Unit = C.SearchBy == SearchTypes.DaysOut ? "days" : "months";
                        if (C.SearchValue < 0)
                        {
                            Add(ConfigCode.SearchValueNegative, Stops, "searchValue", null,
                                $"searchValue is negative: searchBy {C.SearchBy} needs a number of {Unit} from today, and a negative one makes the search's dates run toward the end of the calendar.");
                            return;
                        }
                        DateTime End = default;
                        if (!CanAdd(() => End = C.SearchBy == SearchTypes.DaysOut ? Now.AddDays(C.SearchValue) : Now.AddMonths(C.SearchValue)))
                        {
                            Add(ConfigCode.SearchWindowOutOfRange, Stops, "searchValue", null, $"searchValue is too large: the search would end after the last date of the calendar.");
                            return;
                        }
                        if (C.SearchValue == 0)
                        {
                            Add(ConfigCode.SearchValueZero, ConfigSeverity.Warning, "searchValue", null, "searchValue is 0: the search looks at today only, and today is never matched.");
                        }
                        Last = End;
                        break;
                    }
                case SearchTypes.Until:
                    {
                        if (Dates == null)
                        {
                            Add(ConfigCode.SearchDatesMissing, ConfigSeverity.Warning, "searchValueDates", null, "searchValueDates is missing: searchBy Until has no end date, so the search has no dates.");
                            return;
                        }
                        if (Dates.Count == 0)
                        {
                            Add(ConfigCode.SearchDatesTooFew, Stops, "searchValueDates", null, "searchValueDates has no date: searchBy Until needs its end date.");
                            return;
                        }
                        if (Dates.Count > 1)
                        {
                            Add(ConfigCode.SearchDatesExtra, ConfigSeverity.Warning, "searchValueDates", 1, "searchValueDates has more than one date: searchBy Until uses only the first.");
                        }
                        if (Dates[0] == null)
                        {
                            Add(ConfigCode.SearchDatesMissing, ConfigSeverity.Warning, "searchValueDates", 0, "searchValueDates item 1 is empty: the search has no dates.");
                            return;
                        }
                        DateTime? Until = ReadDate(Dates, 0, Stops, Add);
                        if (Until == null)
                        {
                            return;
                        }
                        if (Until <= Now)
                        {
                            Add(ConfigCode.DateInPast, ConfigSeverity.Warning, "searchValueDates", 0, "searchValueDates item 1 has passed: the search looks at today only, and today is never matched.");
                            return;
                        }
                        Last = Until;
                        break;
                    }
                case SearchTypes.StartEndDate:
                    {
                        if (Dates == null)
                        {
                            Add(ConfigCode.SearchDatesMissing, ConfigSeverity.Warning, "searchValueDates", null, "searchValueDates is missing: searchBy StartEndDate has no dates, so the search has none.");
                            return;
                        }
                        //GenerateSearchDates reads items 1 and 2; fewer stop the search, unless the first is empty
                        if (Dates.Count == 0 || Dates.Count == 1 && Dates[0] != null)
                        {
                            Add(ConfigCode.SearchDatesTooFew, Stops, "searchValueDates", null, "searchValueDates needs two dates for searchBy StartEndDate: the start and the end.");
                            return;
                        }
                        if (Dates.Count > 2)
                        {
                            Add(ConfigCode.SearchDatesExtra, ConfigSeverity.Warning, "searchValueDates", 2, "searchValueDates has more than two dates: searchBy StartEndDate uses only the first two.");
                        }
                        if (Dates[0] == null || Dates[1] == null)
                        {
                            Add(ConfigCode.SearchDatesMissing, ConfigSeverity.Warning, "searchValueDates", Dates[0] == null ? 0 : 1, "A date of searchValueDates is empty: the search has no dates.");
                            return;
                        }
                        DateTime? Start = ReadDate(Dates, 0, Stops, Add);
                        DateTime? EndDate = ReadDate(Dates, 1, Stops, Add);
                        if (Start == null || EndDate == null)
                        {
                            return;
                        }
                        if (Start == EndDate)
                        {
                            Add(ConfigCode.DatesEqual, Stops, "searchValueDates", 1, "The start and end dates of searchValueDates are the same; searchBy StartEndDate needs two different dates.");
                            return;
                        }
                        if (Start > EndDate)
                        {
                            Add(ConfigCode.DatesReversed, ConfigSeverity.Warning, "searchValueDates", 1, "The start date of searchValueDates is after the end date: the search has no dates.");
                            return;
                        }
                        if (EndDate.Value.Date < Now.Date)
                        {
                            Add(ConfigCode.DateInPast, ConfigSeverity.Warning, "searchValueDates", 1, "The dates of searchValueDates have passed: the search finds nothing.");
                            return;
                        }
                        Last = EndDate;
                        break;
                    }
                case SearchTypes.SpecificDates:
                    {
                        if (Dates == null)
                        {
                            Add(ConfigCode.SearchDatesMissing, ConfigSeverity.Warning, "searchValueDates", null, "searchValueDates is missing: the search has no dates.");
                            return;
                        }
                        if (Dates.Count == 0)
                        {
                            Add(ConfigCode.SearchDatesMissing, ConfigSeverity.Warning, "searchValueDates", null, "searchValueDates is empty: the search looks at today only, and today is never matched.");
                            return;
                        }
                        //an unreadable date is ignored (with a warning when the search is loaded), so it never stops the search
                        for (int i = 0; i < Dates.Count; i++)
                        {
                            DateTime? Date = ReadDate(Dates, i, ConfigSeverity.Warning, Add);
                            if (Date != null && (Last == null || Date > Last))
                            {
                                Last = Date;
                            }
                        }
                        break;
                    }
                default:
                    Add(ConfigCode.InvalidChoice, Stops, "searchBy", null, "searchBy must be one of " + Choices(typeof(SearchTypes)) + ".");
                    return;
            }
            //GetMonthsOut stops at 12 months and warns when the search is loaded
            if (Last != null && (Last.Value.Year - Now.Year) * 12 + Last.Value.Month - Now.Month >= 12)
            {
                Add(ConfigCode.SearchBeyondTwelveMonths, ConfigSeverity.Warning, C.SearchBy is SearchTypes.DaysOut or SearchTypes.MonthsOut ? "searchValue" : "searchValueDates", null,
                    "The search reaches 12 months or more ahead: its results are limited to 12 months out.");
            }
        }

        // a date of searchValueDates as GenerateSearchDates reads it (Convert.ToDateTime with the current culture), with a
        // warning when that reading depends on the culture
        private static DateTime? ReadDate(List<string> Dates, int Index, ConfigSeverity Unreadable, Action<ConfigCode, ConfigSeverity, string?, int?, string> Add)
        {
            string? Text = Dates[Index];
            if (Text == null)
            {
                Add(ConfigCode.DateInvalid, Unreadable, "searchValueDates", Index, $"searchValueDates item {Index + 1} is empty.");
                return null;
            }
            DateTime Date;
            try
            {
                Date = System.Convert.ToDateTime(Text);
            }
            catch (FormatException)
            {
                Add(ConfigCode.DateInvalid, Unreadable, "searchValueDates", Index, $"searchValueDates item {Index + 1} is not a date.");
                return null;
            }
            if (!IsoDate.IsMatch(Text))
            {
                Add(ConfigCode.DateNotIso, ConfigSeverity.Warning, "searchValueDates", Index,
                    $"searchValueDates item {Index + 1} is not written as yyyy-MM-dd: it is read with this computer's date format, which another computer may read differently.");
            }
            else if (!DateTime.TryParseExact(Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime Iso) || Iso != Date)
            {
                Add(ConfigCode.DateCultureDependent, ConfigSeverity.Warning, "searchValueDates", Index,
                    $"searchValueDates item {Index + 1} is read as a different day with this computer's calendar or date format than its yyyy-MM-dd text says.");
            }
            return Date;
        }

        private static bool CanAdd(Action Add)
        {
            try
            {
                Add();
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static ConfigDiagnostic Make(ConfigCode Code, ConfigSeverity Severity, ConfigFileKind Kind, string FileName, string? Key, int? Index, Layout? Where, string Message,
            int Line = 0, int Column = 0)
        {
            if (Line == 0 && Where != null && Key != null)
            {
                (Line, Column) = Index != null && Where.Items.TryGetValue((Key, Index.Value), out var Item) ? Item : Where.Keys.GetValueOrDefault(Key);
            }
            return new ConfigDiagnostic(Code, Severity, Kind, FileName, Key, Index, Line, Column, Message);
        }
    }
}
