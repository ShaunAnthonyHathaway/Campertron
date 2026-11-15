using System.Reflection;
using YamlDotNet.Core;

namespace CampertronLibrary.function.Base
{
    // One settings file opened for editing in the GUI (CM5 of GUI configuration management, decision D43; business-rules
    // §17): General.yaml (T = GeneralConfig) or Email.yaml (T = EmailConfig), and since CM6 (decision D44, business-rules
    // §18) a search file (T = CampertronConfig), with Save As, New search (SearchFiles.Create) and a check that its
    // campgroundID is a reservable, enabled campground. It brings together what CM1 to CM3 built:
    //
    //   - reading: the file's exact bytes and their hash (ConfigFileReplace.Read: strict UTF-8, links, size limit), its
    //     structure (YamlDocument.Analyze) and its settings as the loader reads them (Yaml.LoadSettingsText);
    //   - editing: typed values per key, kept as pending edits next to the settings read; nothing is written while editing;
    //   - the change: the pending edits applied to the text by the YAML document editor (YamlDocument.Edit), which keeps
    //     comments, blank lines, order, quoting, indentation, a byte-order mark and the line breaks and verifies every step
    //     against the intended settings; nothing is ever re-serialized or normalized;
    //   - validation: the configuration validation (ConfigValidation) of the changed text, mapped to keys;
    //   - the preview: the changed lines with one line of context, the SMTP password masked;
    //   - saving: ConfigFileReplace.Replace (temporary file, one rolling backup, the file's permissions, the external-change
    //     check), then the file is read again and must be exactly the reviewed text;
    //   - external changes: optimistic, by comparing the file on disk with what was read (CheckForExternalChange): a clean
    //     session shows the new version, an edited one keeps its edits in a conflict until the user resolves it.
    //
    // No edit means no write. No method opens a network connection or searches, and no message, notice or preview line
    // holds a value of the password (messages are fixed text, key and file names and numbers). Search files are written
    // only when the user saves one (D44 narrows D37 to that explicit action); nothing here deletes a file.
    internal sealed class ConfigEditSession<T> where T : class, new()
    {
        internal const string Mask = ConfigEditPreview.Mask;
        internal static readonly UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        private static readonly UnixFileMode OthersCanRead = UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

        private readonly IReadOnlyDictionary<string, PropertyInfo> _Keys;
        private readonly HashSet<string> _ListKeys;
        private readonly Func<DateTime> _Now;
        private readonly Dictionary<string, object?> _Scalars = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ConfigListItem>> _Lists = new(StringComparer.Ordinal);
        // a search file's campground lookup (exact FacilityID) and what it found, per ID
        private readonly Func<string, CampgroundChoice?>? _Campgrounds;
        private readonly Dictionary<string, CampgroundChoice?> _Found = new(StringComparer.Ordinal);
        private OutputType? _SearchOutput;

        private ConfigFileReplace.SettingsFile? _File;
        private YamlDocumentAnalysis? _Analysis;
        private T? _Original;
        private string? _Candidate;
        private T? _Intended;
        private bool _EditRefused;
        private List<ConfigEditNotice> _Notices = new();
        private List<ConfigPreviewLine> _Preview = new();
        private bool _EmailOutput;
        // the other program's version while a conflict is open
        private ConfigFileReplace.SettingsFile? _Theirs;
        private T? _TheirModel;
        private YamlDocumentAnalysis? _TheirAnalysis;

        // General.yaml or Email.yaml in the settings folder
        internal ConfigEditSession(string ConfigPath, Func<DateTime>? Now = null)
            : this(ConfigPath, null, Now, null)
        {
        }

        // A search file (T = CampertronConfig): FileName is the name of a search file directly in ConfigPath (no folder;
        // General.yaml and Email.yaml are not search files). Campgrounds, if given, finds a reservable, enabled
        // campground by its exact FacilityID (the GUI's campground catalog); a campgroundID it does not find is reported
        internal ConfigEditSession(string ConfigPath, string? FileName, Func<DateTime>? Now, Func<string, CampgroundChoice?>? Campgrounds)
        {
            Kind = typeof(T) == typeof(GeneralConfig) ? ConfigFileKind.General
                : typeof(T) == typeof(EmailConfig) ? ConfigFileKind.Email
                : typeof(T) == typeof(CampertronConfig) ? ConfigFileKind.Search
                : throw new ArgumentException("The settings editor edits General.yaml, Email.yaml and search files only.", nameof(T));
            if (Kind == ConfigFileKind.Search)
            {
                if (string.IsNullOrEmpty(FileName) || FileName != Path.GetFileName(FileName) || FileName.IndexOfAny(new[] { '/', '\\' }) >= 0
                    || FileName == "." || FileName == ".." || ConfigFiles.Classify(FileName) != ConfigFileKind.Search)
                {
                    throw new ArgumentException("A search file is named by its file name in the settings folder.", nameof(FileName));
                }
            }
            else if (FileName != null || Campgrounds != null)
            {
                throw new ArgumentException("General.yaml and Email.yaml have fixed names and no campground.", nameof(FileName));
            }
            this.ConfigPath = ConfigPath;
            this.FileName = Kind == ConfigFileKind.General ? ConfigFiles.GeneralFileName : Kind == ConfigFileKind.Email ? ConfigFiles.EmailFileName : FileName!;
            FilePath = Path.Join(ConfigPath, this.FileName);
            _Campgrounds = Campgrounds;
            _Keys = YamlDocument.ModelKeys(typeof(T));
            _ListKeys = new HashSet<string>(_Keys.Where(K => K.Value.PropertyType == typeof(List<string>)).Select(K => K.Key), StringComparer.Ordinal);
            _Now = Now ?? (() => DateTime.Now);
            Load();
        }

        internal ConfigFileKind Kind { get; }
        internal string ConfigPath { get; }
        internal string FileName { get; }
        internal string FilePath { get; }
        internal ConfigEditState State { get; private set; }
        // why the file is Missing, Unreadable or ReadOnly (fixed text and the file's name)
        internal string? StateReason { get; private set; }
        // the keys of the settings, in the model's order
        internal IReadOnlyList<string> Keys => _Keys.Keys.ToList();
        // keys whose secrets are never shown: Email.yaml's smtpPassword
        internal IReadOnlyCollection<string> SecretKeys => Kind == ConfigFileKind.Email ? new[] { "smtpPassword" } : Array.Empty<string>();
        internal bool IsList(string Key) => _ListKeys.Contains(Key);
        // the SHA-256 of the bytes the session edits (null without a file)
        internal string? Sha256 => _File?.Sha256;
        internal UnixFileMode? FileMode => _File?.Mode;
        internal bool IsDirty => _Scalars.Count > 0 || _Lists.Count > 0;
        internal IReadOnlyCollection<string> ChangedKeys => _Keys.Keys.Where(K => _Scalars.ContainsKey(K) || _Lists.ContainsKey(K)).ToList();
        internal bool IsChanged(string Key) => _Scalars.ContainsKey(Key) || _Lists.ContainsKey(Key);
        internal IReadOnlyList<ConfigEditNotice> Notices => _Notices;
        internal IReadOnlyList<ConfigPreviewLine> Preview => _Preview;
        // the text a save writes (null while nothing is edited or an edit is refused)
        internal string? Candidate => _Candidate;
        internal ConfigConflict? Conflict { get; private set; }
        internal bool HasBlockingErrors => _EditRefused || _Notices.Any(N => N.Severity == ConfigSeverity.Error);
        internal bool CanSave => State == ConfigEditState.Editable && IsDirty && _Candidate != null && Conflict == null && !HasBlockingErrors;

        // a search file only: General.yaml's outputTo (the validation's message about consecutiveDays mentions the Email
        // output); null if unknown
        internal OutputType? SearchOutput
        {
            get => _SearchOutput;
            set
            {
                if (_SearchOutput != value)
                {
                    _SearchOutput = value;
                    Recompute();
                }
            }
        }

        // a search file only: the campground of campgroundID as edited (or as the file has it), if the lookup finds a
        // reservable, enabled campground with exactly that FacilityID
        internal CampgroundChoice? Campground => Kind == ConfigFileKind.Search && Value("campgroundID") is string Id ? FindCampground(Id) : null;

        private CampgroundChoice? FindCampground(string Id)
        {
            if (_Campgrounds == null || string.IsNullOrWhiteSpace(Id))
            {
                return null;
            }
            if (!_Found.TryGetValue(Id, out CampgroundChoice? Choice))
            {
                Choice = _Campgrounds(Id);
                // only the exact FacilityID is an answer, whatever the lookup returned
                _Found[Id] = Choice = Choice != null && string.Equals(Choice.FacilityID, Id, StringComparison.Ordinal) ? Choice : null;
            }
            return Choice;
        }

        // Email.yaml only: whether General.yaml's outputTo is Email; then what sending needs is an error, else a warning
        internal bool EmailOutput
        {
            get => _EmailOutput;
            set
            {
                if (_EmailOutput != value)
                {
                    _EmailOutput = value;
                    Recompute();
                }
            }
        }

        // ---- values ----

        // the value as the file holds it (null without settings)
        internal object? OriginalValue(string Key) => _Original == null ? null : Property(Key).GetValue(_Original);

        // the value as edited, or as the file holds it
        internal object? Value(string Key)
        {
            if (_Scalars.TryGetValue(Key, out object? Edited))
            {
                return Edited;
            }
            if (_Lists.TryGetValue(Key, out List<ConfigListItem>? Rows))
            {
                return Rows.Select(R => R.Value).ToList();
            }
            return OriginalValue(Key);
        }

        // the rows of a list as edited (each with the index of the item it was in the file), or the file's items
        internal IReadOnlyList<ConfigListItem> Items(string Key)
        {
            if (_Lists.TryGetValue(Key, out List<ConfigListItem>? Rows))
            {
                return Rows.ToList();
            }
            var Original = OriginalValue(Key) as List<string>;
            return Original == null ? new List<ConfigListItem>() : Original.Select((V, i) => new ConfigListItem(i, V)).ToList();
        }

        // whether item Index of a list is empty (null) in the file, as "-" or "- ~" (a row for it keeps it as it is until
        // the user enters a value)
        internal bool IsNullItem(string Key, int Index) => OriginalValue(Key) is List<string> Original && Index >= 0 && Index < Original.Count && Original[Index] == null;

        // why a key can't be edited (fixed text), or null
        internal string? LockReason(string Key)
        {
            Property(Key);
            if (State != ConfigEditState.Editable)
            {
                return StateReason;
            }
            YamlDocumentProperty? P = _Analysis!.Properties.FirstOrDefault(Prop => Prop.Key == Key);
            if (P == null)
            {
                return null;
            }
            if (P.Lock != YamlDocumentLock.None)
            {
                return $"{Key} uses an {P.Lock.ToString().ToLowerInvariant()} in {FileName}, which Campertron keeps exactly as it is and does not edit.";
            }
            if (IsList(Key))
            {
                bool IsSequence = P.Shape == YamlDocumentShape.BlockSequence || P.Shape == YamlDocumentShape.FlowSequence;
                if ((IsSequence && !P.IsEditableSequence) || (!IsSequence && P.Shape != YamlDocumentShape.Scalar))
                {
                    return $"{Key} is written in a list layout that Campertron can't edit item by item safely; change it in a text editor.";
                }
                return null;
            }
            return P.Shape == YamlDocumentShape.Scalar ? null
                : $"{Key} is written in a form (a multi-line text, list or mapping) that Campertron does not rewrite; change it in a text editor.";
        }

        // sets a scalar value (string, int, double, bool, DateTime?, OutputType); the value the file holds is no edit
        internal void Set(string Key, object? NewValue)
        {
            PropertyInfo P = Property(Key);
            if (IsList(Key))
            {
                throw new ArgumentException($"{Key} is a list; use SetItems.", nameof(Key));
            }
            RequireEditable();
            if (SameValue(P.PropertyType, NewValue, OriginalValue(Key)))
            {
                _Scalars.Remove(Key);
            }
            else
            {
                _Scalars[Key] = NewValue;
            }
            Recompute();
        }

        // sets a list's rows; rows that give exactly the file's items are no edit
        internal void SetItems(string Key, IReadOnlyList<ConfigListItem> Rows)
        {
            Property(Key);
            if (!IsList(Key))
            {
                throw new ArgumentException($"{Key} is not a list.", nameof(Key));
            }
            RequireEditable();
            var Original = OriginalValue(Key) as List<string>;
            int Count = Original?.Count ?? 0;
            if (Rows.Any(R => R == null) || Rows.Where(R => R.OriginalIndex != null).Select(R => R.OriginalIndex!.Value).Any(i => i < 0 || i >= Count)
                || Rows.Where(R => R.OriginalIndex != null).GroupBy(R => R.OriginalIndex).Any(G => G.Count() > 1))
            {
                throw new ArgumentException("Each row refers to a different item of the list, or to none.", nameof(Rows));
            }
            // an empty (null) item stays only as the file has it: a row of an empty item, unchanged
            if (Rows.Any(R => R.Value == null && (R.OriginalIndex == null || Original![R.OriginalIndex.Value] != null)))
            {
                throw new ArgumentException("Only an item that is empty in the file can stay empty.", nameof(Rows));
            }
            if (Rows.Select(R => R.Value).SequenceEqual(Original ?? new List<string>(), StringComparer.Ordinal))
            {
                _Lists.Remove(Key);
            }
            else
            {
                _Lists[Key] = Rows.ToList();
            }
            Recompute();
        }

        internal void Revert(string Key)
        {
            Property(Key);
            _Scalars.Remove(Key);
            _Lists.Remove(Key);
            Recompute();
        }

        internal void RevertAll()
        {
            _Scalars.Clear();
            _Lists.Clear();
            Recompute();
        }

        // ---- reading ----

        // Discards every edit and any conflict, and reads the file again
        internal void Reload()
        {
            _Scalars.Clear();
            _Lists.Clear();
            ClearConflict();
            Load();
        }

        private void Load()
        {
            _File = null;
            _Analysis = null;
            _Original = null;
            StateReason = null;
            try
            {
                _File = ConfigFileReplace.Read(FilePath);
            }
            catch (Exception ex) when ((ex is FileNotFoundException || ex is DirectoryNotFoundException) && !IsLink(FilePath))
            {
                State = ConfigEditState.Missing;
                StateReason = $"{FileName} does not exist.";
                Recompute();
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                // ConfigFileReplace's messages name the file and the problem, never its content
                State = ConfigEditState.Unreadable;
                StateReason = ex.Message;
                Recompute();
                return;
            }
            _Analysis = YamlDocument.Analyze(_File.Text);
            _Original = TryLoad(_File.Text);
            if (!_Analysis.Succeeded)
            {
                YamlDocumentDiagnostic? Why = _Analysis.Diagnostics.FirstOrDefault(D => D.Severity == YamlDocumentSeverity.Error);
                State = ConfigEditState.ReadOnly;
                StateReason = $"{FileName} can't be edited safely here: {Why?.Message ?? "its layout is not supported."} It was left as it is.";
            }
            else if (_Original == null)
            {
                State = ConfigEditState.ReadOnly;
                StateReason = $"{FileName} can't be edited here because Campertron can't read its settings (see the problems below). Correct it in a text editor, then reload it.";
            }
            else
            {
                State = ConfigEditState.Editable;
            }
            Recompute();
        }

        private static bool IsLink(string Path_) => new FileInfo(Path_).LinkTarget != null;

        // the settings of a text as the loader reads them, or null if it can't (the reason is in the validation's notices)
        private static T? TryLoad(string Text)
        {
            try
            {
                return Yaml.LoadSettingsText<T>(Text);
            }
            catch (Exception ex) when (ex is YamlException || ex is Yaml.SettingsLimitException)
            {
                return null;
            }
        }

        // ---- the change, its validation and its preview ----

        private void Recompute()
        {
            var Notices = new List<ConfigEditNotice>();
            _Candidate = null;
            _Intended = null;
            _EditRefused = false;
            _Preview = new List<ConfigPreviewLine>();
            if (_File != null && State != ConfigEditState.Missing && State != ConfigEditState.Unreadable)
            {
                string Base = _File.Text;
                if (State == ConfigEditState.Editable && IsDirty)
                {
                    (string? Text, T? Intended, List<YamlDocumentLoss> Losses) = Build(Base, _Original!, _Analysis!, Notices);
                    if (Text != null)
                    {
                        _Candidate = Text;
                        _Intended = Intended;
                        Notices.AddRange(LossNotices(Losses));
                        Notices.AddRange(Validate(Text));
                        Notices.AddRange(CampgroundNotices());
                        _Preview = ConfigEditPreview.Build(Base, Text, SecretRanges(_Analysis!), SecretRanges(YamlDocument.Analyze(Text)), SecretValues());
                    }
                    else
                    {
                        _EditRefused = true;
                        Notices.AddRange(Validate(Base).Where(N => N.Key == null || !IsChanged(N.Key)));
                    }
                }
                else
                {
                    Notices.AddRange(Validate(Base));
                    Notices.AddRange(CampgroundNotices());
                }
                if (Kind == ConfigFileKind.Email && _File.Mode is UnixFileMode Mode && (Mode & OthersCanRead) != 0)
                {
                    Notices.Add(new ConfigEditNotice(ConfigSeverity.Info, ConfigEditSource.Session, "EmailReadableByOthers", null, null,
                        $"Other users of this computer may be able to read {FileName} (its permissions allow it), and it stores the SMTP password as plain text."));
                }
            }
            _Notices = Notices;
        }

        private List<ConfigEditNotice> Validate(string Text)
        {
            List<ConfigDiagnostic> Found = Kind == ConfigFileKind.General ? ConfigValidation.ValidateGeneral(Text, _Now(), FileName)
                : Kind == ConfigFileKind.Email ? ConfigValidation.ValidateEmail(Text, _EmailOutput, FileName)
                : ConfigValidation.ValidateSearch(Text, FileName, _Now(), _SearchOutput);
            return Found.Select(D => new ConfigEditNotice(D.Severity, ConfigEditSource.Validation, D.Code.ToString(), D.Key, D.Index, D.Message)).ToList();
        }

        // A search file's campgroundID that is not a reservable, enabled campground of the campground data (exact
        // FacilityID): an error when the user entered it (the GUI offers only IDs it found), a warning when the file has it
        // (the campground data may be older than the file; the other settings stay editable). Nothing without a lookup
        private IEnumerable<ConfigEditNotice> CampgroundNotices()
        {
            if (Kind != ConfigFileKind.Search || _Campgrounds == null || Value("campgroundID") is not string Id || string.IsNullOrWhiteSpace(Id) || FindCampground(Id) != null)
            {
                yield break;
            }
            yield return IsChanged("campgroundID")
                ? new ConfigEditNotice(ConfigSeverity.Error, ConfigEditSource.Session, "CampgroundNotFound", "campgroundID", null,
                    "campgroundID is not a reservable, enabled campground in Campertron's campground data; choose a campground from the list or enter an ID it has.")
                : new ConfigEditNotice(ConfigSeverity.Warning, ConfigEditSource.Session, "CampgroundNotFound", "campgroundID", null,
                    "campgroundID is not a reservable, enabled campground in Campertron's campground data, so the search may find nothing.");
        }

        // a search file only: whether the start runs it as it is on disk (autoRun: true and no error), as the settings
        // folder's validation counts it (ConfigValidation.RunsAtStart)
        internal bool RunsAtStart => Kind == ConfigFileKind.Search && ConfigValidation.RunsAtStart(FilePath, _Now(), _SearchOutput);

        // The pending edits applied to Base by the YAML document editor, step by step, each step verified against the
        // settings it should give: first every scalar in one step, then each list operation (the editor takes one edit per
        // key per step). Returns no text if a step is refused; the editor's reason is added to Notices.
        private (string? Text, T? Intended, List<YamlDocumentLoss> Losses) Build(string Base, T Original, YamlDocumentAnalysis Analysis, List<ConfigEditNotice> Notices)
        {
            T Model = Clone(Original);
            string Text = Base;
            var Losses = new List<YamlDocumentLoss>();
            var Warnings = new HashSet<YamlDocumentCode>();
            var Scalars = new List<YamlDocumentEdit>();
            foreach (string Key in _Keys.Keys.Where(_Scalars.ContainsKey))
            {
                Scalars.Add(YamlDocumentEdit.SetScalar(Key, _Scalars[Key]));
                Property(Key).SetValue(Model, _Scalars[Key]);
            }
            if (Scalars.Count > 0 && !Step(ref Text, Scalars, Model, Losses, Warnings, Notices))
            {
                return (null, null, Losses);
            }
            foreach (string Key in _Keys.Keys.Where(_Lists.ContainsKey))
            {
                foreach ((YamlDocumentEdit Edit, List<string>? After) in PlanList(Key, Property(Key).GetValue(Original) as List<string>, _Lists[Key]))
                {
                    Property(Key).SetValue(Model, After);
                    if (!Step(ref Text, new[] { Edit }, Model, Losses, Warnings, Notices))
                    {
                        return (null, null, Losses);
                    }
                }
            }
            return (Text, Model, Losses);
        }

        private bool Step(ref string Text, IReadOnlyList<YamlDocumentEdit> Edits, T Model, List<YamlDocumentLoss> Losses, HashSet<YamlDocumentCode> Warnings, List<ConfigEditNotice> Notices)
        {
            YamlDocumentResult Result = YamlDocument.Edit(Text, Edits, Clone(Model));
            foreach (YamlDocumentDiagnostic D in Result.Diagnostics)
            {
                if (D.Severity == YamlDocumentSeverity.Error)
                {
                    Notices.Add(new ConfigEditNotice(ConfigSeverity.Error, ConfigEditSource.Editor, D.Code.ToString(), D.Key ?? Edits[0].Key, null,
                        $"Campertron can't make this change safely: {D.Message}"));
                }
                else if (Warnings.Add(D.Code))
                {
                    Notices.Add(new ConfigEditNotice(ConfigSeverity.Warning, ConfigEditSource.Editor, D.Code.ToString(), D.Key, null, D.Message));
                }
            }
            if (!Result.Succeeded)
            {
                return false;
            }
            Text = Result.Text!;
            Losses.AddRange(Result.Losses);
            return true;
        }

        // The editor's operations that turn a list's items into the rows: removals (from the last), one reordering, changed
        // values, then insertions in their final order, each with the list it leaves. A list that is missing or null gets
        // its items in one SetSequence.
        private static IEnumerable<(YamlDocumentEdit Edit, List<string>? After)> PlanList(string Key, List<string>? Before, List<ConfigListItem> Rows)
        {
            if (Before == null)
            {
                if (Rows.Count > 0)
                {
                    // every row is new here, and a new row has a value (SetItems)
                    List<string> Values = Rows.Select(R => R.Value!).ToList();
                    yield return (YamlDocumentEdit.SetSequence(Key, Values), Values);
                }
                yield break;
            }
            List<int> Kept = Rows.Where(R => R.OriginalIndex != null).Select(R => R.OriginalIndex!.Value).ToList();
            var Current = Before.ToList();
            var Origin = Enumerable.Range(0, Before.Count).ToList();
            foreach (int Removed in Enumerable.Range(0, Before.Count).Where(i => !Kept.Contains(i)).OrderByDescending(i => i))
            {
                int At = Origin.IndexOf(Removed);
                Current.RemoveAt(At);
                Origin.RemoveAt(At);
                yield return (YamlDocumentEdit.RemoveItem(Key, At), Current.ToList());
            }
            if (!Origin.SequenceEqual(Kept))
            {
                List<int> Order = Kept.Select(o => Origin.IndexOf(o)).ToList();
                Current = Order.Select(i => Current[i]).ToList();
                Origin = Kept.ToList();
                yield return (YamlDocumentEdit.ReorderItems(Key, Order), Current.ToList());
            }
            List<ConfigListItem> KeptRows = Rows.Where(R => R.OriginalIndex != null).ToList();
            for (int At = 0; At < KeptRows.Count; At++)
            {
                // a row that differs from its item has a value: an empty (null) row is only an item that is empty in the file
                if (!string.Equals(KeptRows[At].Value, Current[At], StringComparison.Ordinal))
                {
                    Current[At] = KeptRows[At].Value!;
                    yield return (YamlDocumentEdit.SetItem(Key, At, KeptRows[At].Value!), Current.ToList());
                }
            }
            for (int At = 0; At < Rows.Count; At++)
            {
                if (Rows[At].OriginalIndex == null)
                {
                    Current.Insert(At, Rows[At].Value!);
                    yield return (YamlDocumentEdit.InsertItem(Key, At, Rows[At].Value!), Current.ToList());
                }
            }
        }

        // a removal that takes comments with it is announced before the save (the preview shows the lines too); the notice
        // is about the key (its message names the item as it is in the file), not about one of the edited rows
        private static IEnumerable<ConfigEditNotice> LossNotices(List<YamlDocumentLoss> Losses)
        {
            foreach (YamlDocumentLoss Loss in Losses.Where(L => L.Comments.Count > 0))
            {
                string What = Loss.Index >= 0 ? $"item {Loss.Index + 1} of {Loss.Key}" : Loss.Key;
                string Lines = Loss.Comments.Count == 1 ? "1 comment" : $"{Loss.Comments.Count} comments";
                yield return new ConfigEditNotice(ConfigSeverity.Warning, ConfigEditSource.Editor, "CommentRemoved", Loss.Key, null,
                    $"Removing {What} also removes {Lines} that belong to it.");
            }
        }

        private IReadOnlyList<(int Start, int End)> SecretRanges(YamlDocumentAnalysis Analysis) =>
            Analysis.Properties.Where(P => SecretKeys.Contains(P.Key)).Select(P => (P.ValueStart, P.ValueEnd)).ToList();

        // the password as the file holds it and as edited, for the preview's masking
        private IReadOnlyList<string> SecretValues() =>
            SecretKeys.SelectMany(K => new[] { OriginalValue(K) as string, Value(K) as string }).Where(V => !string.IsNullOrEmpty(V)).Select(V => V!).Distinct().ToList();

        // ---- saving ----

        // Writes the reviewed change: only when something is edited, the editor verified it, the validation found no error
        // and the file is still what was read. The file is replaced through ConfigFileReplace (temporary file, backup, the
        // file's permissions) and read again; the edits are cleared only when it is exactly the reviewed text.
        internal ConfigEditResult Save()
        {
            if (!IsDirty)
            {
                return new ConfigEditResult(false, $"Nothing to save: {FileName} has no changes.");
            }
            if (!CanSave)
            {
                return new ConfigEditResult(false, Conflict != null
                    ? $"{FileName} was changed by another program; resolve that first. Nothing was written."
                    : $"{FileName} has problems that must be fixed before it can be saved. Nothing was written.");
            }
            CheckForExternalChange();
            if (Conflict != null || !CanSave)
            {
                return new ConfigEditResult(false, $"{FileName} was changed by another program while you were editing it. Nothing was written.");
            }
            string Expected = _Candidate!;
            T Intended = _Intended!;
            try
            {
                ConfigFileReplace.Replace(_File!, Expected, Written => Written == Expected && SameModel(TryLoad(Written), Intended) ? null : "it is not the reviewed change");
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                // the writer's messages name the file and the problem, never a value; a change by another program (or a
                // replacement whose read-back failed) shows up as a conflict, the edits stay
                CheckForExternalChange();
                return new ConfigEditResult(false, ex.Message);
            }
            ConfigFileReplace.SettingsFile Written;
            try
            {
                Written = ConfigFileReplace.Read(FilePath);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                CheckForExternalChange();
                return new ConfigEditResult(false, $"{FileName} was saved, but it could not be read again: {ex.Message}");
            }
            if (Written.Text != Expected)
            {
                CheckForExternalChange();
                return new ConfigEditResult(false, $"{FileName} was saved, but reading it again gave something else (another program may have changed it). The previous version is in {FileName}{ConfigFileReplace.BackupSuffix}.");
            }
            _Scalars.Clear();
            _Lists.Clear();
            ClearConflict();
            Load();
            return new ConfigEditResult(true, $"Saved {FileName}. The previous version is in {FileName}{ConfigFileReplace.BackupSuffix}.");
        }

        // ---- Save As (a search file) ----

        // Writes this search file as it is with the edits (the reviewed text; without edits, the file as read) to a new
        // search file in the same folder (CM6, decision D44): a name SearchFileName accepts that no file, folder or link has
        // in any case; create-only (ConfigFileReplace.Create: a file created meanwhile by another program is never written
        // over, and a first creation has no backup), with this file's mode on Unix (on Windows the folder's inherited
        // access rules). This file is not changed and keeps its edits; the caller opens the new file. Allowed with an open
        // conflict too: it writes nothing over either version.
        internal ConfigEditResult SaveAs(string? NewName, out string? NewFileName)
        {
            NewFileName = null;
            if (Kind != ConfigFileKind.Search)
            {
                return new ConfigEditResult(false, $"{FileName} can't be saved under another name.");
            }
            if (State != ConfigEditState.Editable || _File == null || (IsDirty && _Candidate == null) || HasBlockingErrors)
            {
                return new ConfigEditResult(false, $"{FileName} has problems that must be fixed before it can be saved under another name. Nothing was written.");
            }
            string? Problem = SearchFileName.Check(NewName, out string Name);
            if (Problem == null)
            {
                Problem = SearchFileName.CheckFree(ConfigPath, Name);
            }
            if (Problem != null)
            {
                return new ConfigEditResult(false, Problem + " Nothing was written.");
            }
            string Target = Path.Join(ConfigPath, Name);
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(Target)), Path.GetFullPath(ConfigPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.Ordinal))
            {
                return new ConfigEditResult(false, "A search file is saved directly in the settings folder. Nothing was written.");
            }
            string Expected = IsDirty ? _Candidate! : _File.Text;
            T Intended = IsDirty ? _Intended! : _Original!;
            try
            {
                if (!ConfigFileReplace.Create(Target, Expected, Written => Written == Expected && SameModel(TryLoad(Written), Intended) ? null : "it is not the reviewed change",
                    OperatingSystem.IsWindows() ? null : _File.Mode))
                {
                    return new ConfigEditResult(false, $"{Name} was created by another program meanwhile; it was not written over. Nothing was written.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                return new ConfigEditResult(false, ex.Message);
            }
            NewFileName = Name;
            return new ConfigEditResult(true, $"Saved as {Name}. {FileName} was not changed.");
        }

        // ---- external changes ----

        // Compares the file on disk with what the session read. Nothing changed: None. Only permissions, time or links
        // changed: they become the file's state (MetadataChanged). The content changed or the file disappeared: without
        // edits the session shows the new state (Reloaded, Deleted); with edits they are kept and a Conflict is opened.
        internal ConfigExternalChange CheckForExternalChange()
        {
            if (_File == null)
            {
                ConfigEditState Before = State;
                string? BeforeReason = StateReason;
                Load();
                return State != Before || StateReason != BeforeReason ? ConfigExternalChange.Reloaded : ConfigExternalChange.None;
            }
            ConfigFileReplace.SettingsFile? Now = null;
            bool Gone = false;
            Exception? Problem = null;
            try
            {
                Now = ConfigFileReplace.Read(FilePath);
            }
            catch (Exception ex) when ((ex is FileNotFoundException || ex is DirectoryNotFoundException) && !IsLink(FilePath))
            {
                Gone = true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                Problem = ex;
            }
            if (Now != null && Now.Sha256 == _File.Sha256)
            {
                if (Conflict != null)
                {
                    // the other program's change was undone: the file is again what the edits were made on
                    ClearConflict();
                    Recompute();
                }
                if (Now.TargetPath == _File.TargetPath && Now.Links.SequenceEqual(_File.Links) && Now.Mode == _File.Mode && Now.Attributes == _File.Attributes
                    && Now.LastWriteTimeUtc == _File.LastWriteTimeUtc && Now.AccessRules == _File.AccessRules)
                {
                    return ConfigExternalChange.None;
                }
                _File = Now;
                Recompute();
                return ConfigExternalChange.MetadataChanged;
            }
            if (!IsDirty)
            {
                Load();
                return Gone ? ConfigExternalChange.Deleted : ConfigExternalChange.Reloaded;
            }
            if (Gone)
            {
                _Theirs = null;
                _TheirModel = null;
                _TheirAnalysis = null;
                Conflict = new ConfigConflict(ConfigConflictKind.Deleted, Array.Empty<string>(), false,
                    $"{FileName} was deleted by another program while you were editing it. Your changes are kept here; nothing was written.");
                Recompute();
                return ConfigExternalChange.Conflict;
            }
            _Theirs = Now;
            _TheirModel = Now == null ? null : TryLoad(Now.Text);
            _TheirAnalysis = Now == null ? null : YamlDocument.Analyze(Now.Text);
            List<string> Overlapping = ChangedKeys.Where(K => _TheirModel == null || !SameValue(Property(K).PropertyType, Property(K).GetValue(_Original!), Property(K).GetValue(_TheirModel))).ToList();
            bool CanApply = _TheirModel != null && _TheirAnalysis!.Succeeded && Build(Now!.Text, _TheirModel, _TheirAnalysis, new List<ConfigEditNotice>()).Text != null;
            string Message = Problem != null
                ? $"{FileName} was changed by another program and can't be read now ({Problem.Message}) Your changes are kept here; nothing was written."
                : _TheirModel == null || !_TheirAnalysis!.Succeeded
                    ? $"{FileName} was changed by another program, and the new version can't be edited here. Your changes are kept here; nothing was written."
                    : Overlapping.Count > 0
                        ? $"{FileName} was changed by another program while you were editing it. Both changed: {string.Join(", ", Overlapping)}. Your changes are kept here; nothing was written."
                        : $"{FileName} was changed by another program while you were editing it, in other settings than yours. Your changes are kept here; nothing was written.";
            Conflict = new ConfigConflict(ConfigConflictKind.Changed, _TheirModel == null ? ChangedKeys.ToList() : Overlapping, CanApply, Message);
            Recompute();
            return ConfigExternalChange.Conflict;
        }

        // Resolves a Changed conflict by applying the edits to the other program's version: always allowed for edits of
        // other keys (the editor proves they apply); for keys both changed only with KeepMine, the user's explicit choice
        // that the edited values replace the other program's. The edits stay unsaved for review.
        internal ConfigEditResult ApplyEditsToExternalVersion(bool KeepMine)
        {
            if (Conflict == null || Conflict.Kind != ConfigConflictKind.Changed || _Theirs == null || _TheirModel == null || _TheirAnalysis == null || !_TheirAnalysis.Succeeded)
            {
                return new ConfigEditResult(false, $"Your changes can't be applied to the new version of {FileName}.");
            }
            if (Conflict.OverlappingKeys.Count > 0 && !KeepMine)
            {
                return new ConfigEditResult(false, $"Both versions of {FileName} changed {string.Join(", ", Conflict.OverlappingKeys)}; choose which to keep.");
            }
            T OldOriginal = _Original!;
            _File = _Theirs;
            _Analysis = _TheirAnalysis;
            _Original = _TheirModel;
            foreach (string Key in _Scalars.Keys.ToList())
            {
                if (SameValue(Property(Key).PropertyType, _Scalars[Key], Property(Key).GetValue(_Original)))
                {
                    _Scalars.Remove(Key);
                }
            }
            foreach (string Key in _Lists.Keys.ToList())
            {
                var Old = Property(Key).GetValue(OldOriginal) as List<string>;
                var New = Property(Key).GetValue(_Original) as List<string>;
                if (!SameValue(typeof(List<string>), Old, New))
                {
                    // the other program changed this list too (KeepMine): the rows are matched to its items by value, so
                    // an item that is in both keeps its comments
                    var Used = new HashSet<int>();
                    List<ConfigListItem> Rows = _Lists[Key].Select(R =>
                    {
                        int Match = New == null ? -1 : Enumerable.Range(0, New.Count).FirstOrDefault(j => !Used.Contains(j) && New[j] == R.Value, -1);
                        if (Match >= 0)
                        {
                            Used.Add(Match);
                        }
                        return new ConfigListItem(Match >= 0 ? Match : null, R.Value);
                    }).ToList();
                    _Lists[Key] = Rows;
                }
                if (_Lists[Key].Select(R => R.Value).SequenceEqual(New ?? new List<string>(), StringComparer.Ordinal))
                {
                    _Lists.Remove(Key);
                }
            }
            ClearConflict();
            State = ConfigEditState.Editable;
            StateReason = null;
            Recompute();
            return _Candidate != null || !IsDirty
                ? new ConfigEditResult(true, $"Your changes now apply to the new version of {FileName}. Review them and save.")
                : new ConfigEditResult(false, $"Your changes can't be applied to the new version of {FileName} safely; see the problems below.");
        }

        // Resolves a conflict by discarding the edits and showing the file as it is now
        internal void UseExternalVersion()
        {
            Reload();
        }

        // Resolves a Deleted conflict by creating the file again with the edits applied to the version that was read.
        // Create-only: a file created meanwhile by another program is never written over (that becomes a conflict).
        internal ConfigEditResult RecreateWithEdits()
        {
            if (Conflict == null || Conflict.Kind != ConfigConflictKind.Deleted || _Candidate == null || HasBlockingErrors)
            {
                return new ConfigEditResult(false, $"{FileName} can't be created with your changes.");
            }
            string Expected = _Candidate;
            T Intended = _Intended!;
            UnixFileMode? Mode = Kind == ConfigFileKind.Email ? OwnerOnly : _File?.Mode;
            try
            {
                if (!ConfigFileReplace.Create(FilePath, Expected, Written => Written == Expected && SameModel(TryLoad(Written), Intended) ? null : "it is not the reviewed change", Mode))
                {
                    ClearConflict();
                    CheckForExternalChange();
                    return new ConfigEditResult(false, $"{FileName} was created by another program meanwhile; it was not written over.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                return new ConfigEditResult(false, ex.Message);
            }
            _Scalars.Clear();
            _Lists.Clear();
            ClearConflict();
            Load();
            return new ConfigEditResult(true, $"Created {FileName} with your changes.");
        }

        // A missing General.yaml or Email.yaml: creates the sample a first start writes (create-only; Email.yaml owner-only
        // on Unix) and opens it. A file created meanwhile by another program is opened as it is.
        internal ConfigEditResult Create()
        {
            if (State != ConfigEditState.Missing)
            {
                return new ConfigEditResult(false, $"{FileName} exists.");
            }
            if (Kind == ConfigFileKind.Search)
            {
                return new ConfigEditResult(false, "A search file is created with New search; nothing was written.");
            }
            bool Created;
            try
            {
                Created = Yaml.CreateSampleSettingsFile(Kind, ConfigPath, Kind == ConfigFileKind.Email ? OwnerOnly : null);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                return new ConfigEditResult(false, ex.Message);
            }
            Load();
            return Created
                ? new ConfigEditResult(true, $"Created {FileName} with the default settings.")
                : new ConfigEditResult(false, $"{FileName} was created by another program meanwhile; it was opened as it is.");
        }

        // ---- helpers ----

        private void ClearConflict()
        {
            Conflict = null;
            _Theirs = null;
            _TheirModel = null;
            _TheirAnalysis = null;
        }

        private void RequireEditable()
        {
            if (State != ConfigEditState.Editable)
            {
                throw new InvalidOperationException($"{FileName} can't be edited: {StateReason}");
            }
        }

        private PropertyInfo Property(string Key) =>
            _Keys.TryGetValue(Key, out PropertyInfo? P) ? P : throw new ArgumentException($"{Key} is not a setting of {FileName}.", nameof(Key));

        private T Clone(T Source)
        {
            var Copy = new T();
            foreach (PropertyInfo P in _Keys.Values)
            {
                object? V = P.GetValue(Source);
                P.SetValue(Copy, V is List<string> L ? L.ToList() : V);
            }
            return Copy;
        }

        private bool SameModel(T? A, T? B) =>
            A != null && B != null && _Keys.Values.All(P => SameValue(P.PropertyType, P.GetValue(A), P.GetValue(B)));

        // the editor's comparison: strings ordinal, doubles bit for bit, dates with their kind, lists item by item, null not
        // equal to an empty list
        internal static bool SameValue(Type Type, object? A, object? B)
        {
            if (A == null || B == null)
            {
                return A == null && B == null;
            }
            Type Underlying = Nullable.GetUnderlyingType(Type) ?? Type;
            if (Underlying == typeof(double))
            {
                return BitConverter.DoubleToInt64Bits((double)A) == BitConverter.DoubleToInt64Bits((double)B);
            }
            if (Underlying == typeof(DateTime))
            {
                return ((DateTime)A).Ticks == ((DateTime)B).Ticks && ((DateTime)A).Kind == ((DateTime)B).Kind;
            }
            if (Underlying == typeof(List<string>))
            {
                return ((List<string>)A).SequenceEqual((List<string>)B, StringComparer.Ordinal);
            }
            if (Underlying == typeof(string))
            {
                return string.Equals((string)A, (string)B, StringComparison.Ordinal);
            }
            return A.Equals(B);
        }
    }

    // New search (CM6, decision D44): a search file the user names, created in the settings folder with the example
    // search's settings, disabled (Yaml.CreateSampleSearchFile), create-only. Nothing is searched or run.
    internal static class SearchFiles
    {
        internal static ConfigEditResult Create(string ConfigPath, string? Input, out string? FileName)
        {
            FileName = null;
            string? Problem = SearchFileName.Check(Input, out string Name);
            if (Problem == null)
            {
                Problem = SearchFileName.CheckFree(ConfigPath, Name);
            }
            if (Problem != null)
            {
                return new ConfigEditResult(false, Problem + " Nothing was written.");
            }
            try
            {
                if (!Yaml.CreateSampleSearchFile(Path.Join(ConfigPath, Name), null))
                {
                    return new ConfigEditResult(false, $"{Name} was created by another program meanwhile; it was not written over.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                return new ConfigEditResult(false, ex.Message);
            }
            FileName = Name;
            return new ConfigEditResult(true, $"Created {Name} with the example search's settings. It does not run until you enable it (autoRun) and save.");
        }
    }

    // The preview of a save: the lines that change, with one line of context and a gap marker between distant changes (a
    // single unchanged line between two shown ones is shown instead).
    // Secret values are masked: the password's value range in the file as it is and as it will be, and any other
    // occurrence of the password (a whitespace-only password is masked by its range only).
    internal static class ConfigEditPreview
    {
        // what replaces a secret in the preview, always the same length so it tells nothing about the password
        internal const string Mask = "\u2022\u2022\u2022\u2022\u2022\u2022";
        private const int ContextLines = 1;
        // above this many changed lines on a side, the middle is shown as removed and added without a line-by-line diff
        private const int MaxDiffLines = 2000;

        internal static List<ConfigPreviewLine> Build(string Old, string New, IReadOnlyList<(int Start, int End)> OldSecrets,
            IReadOnlyList<(int Start, int End)> NewSecrets, IReadOnlyList<string> SecretValues)
        {
            List<(int Start, string Text)> A = Lines(Old);
            List<(int Start, string Text)> B = Lines(New);
            // the operations: (kind, index in A, index in B)
            var Ops = new List<(ConfigPreviewKind Kind, int I, int J)>();
            int Prefix = 0;
            while (Prefix < A.Count && Prefix < B.Count && A[Prefix].Text == B[Prefix].Text)
            {
                Ops.Add((ConfigPreviewKind.Context, Prefix, Prefix));
                Prefix++;
            }
            int Suffix = 0;
            while (Suffix < A.Count - Prefix && Suffix < B.Count - Prefix && A[A.Count - 1 - Suffix].Text == B[B.Count - 1 - Suffix].Text)
            {
                Suffix++;
            }
            int AEnd = A.Count - Suffix;
            int BEnd = B.Count - Suffix;
            Ops.AddRange(Middle(A, B, Prefix, AEnd, Prefix, BEnd));
            for (int k = 0; k < Suffix; k++)
            {
                Ops.Add((ConfigPreviewKind.Context, AEnd + k, BEnd + k));
            }
            var Shown = new bool[Ops.Count];
            for (int k = 0; k < Ops.Count; k++)
            {
                if (Ops[k].Kind != ConfigPreviewKind.Context)
                {
                    for (int c = Math.Max(0, k - ContextLines); c <= Math.Min(Ops.Count - 1, k + ContextLines); c++)
                    {
                        Shown[c] = true;
                    }
                }
            }
            // a gap of one line is shown instead of the gap marker
            for (int k = 1; k < Ops.Count - 1; k++)
            {
                if (!Shown[k] && Shown[k - 1] && Shown[k + 1])
                {
                    Shown[k] = true;
                }
            }
            var Preview = new List<ConfigPreviewLine>();
            bool Gap = false;
            for (int k = 0; k < Ops.Count; k++)
            {
                if (!Shown[k])
                {
                    Gap = Preview.Count > 0;
                    continue;
                }
                if (Gap)
                {
                    Preview.Add(new ConfigPreviewLine(ConfigPreviewKind.Gap, null, null, "\u2026"));
                    Gap = false;
                }
                (ConfigPreviewKind Kind, int I, int J) = Ops[k];
                Preview.Add(Kind switch
                {
                    ConfigPreviewKind.Removed => new ConfigPreviewLine(Kind, I + 1, null, Masked(A[I], OldSecrets, SecretValues)),
                    ConfigPreviewKind.Added => new ConfigPreviewLine(Kind, null, J + 1, Masked(B[J], NewSecrets, SecretValues)),
                    _ => new ConfigPreviewLine(Kind, I + 1, J + 1, Masked(B[J], NewSecrets, SecretValues))
                });
            }
            return Preview;
        }

        // a line-by-line diff of the changed middle (longest common subsequence), or all removed then all added
        private static IEnumerable<(ConfigPreviewKind, int, int)> Middle(List<(int, string Text)> A, List<(int, string Text)> B, int A0, int A1, int B0, int B1)
        {
            int N = A1 - A0;
            int M = B1 - B0;
            if (N > MaxDiffLines || M > MaxDiffLines || N == 0 || M == 0)
            {
                for (int i = A0; i < A1; i++)
                {
                    yield return (ConfigPreviewKind.Removed, i, -1);
                }
                for (int j = B0; j < B1; j++)
                {
                    yield return (ConfigPreviewKind.Added, -1, j);
                }
                yield break;
            }
            var L = new int[N + 1, M + 1];
            for (int i = N - 1; i >= 0; i--)
            {
                for (int j = M - 1; j >= 0; j--)
                {
                    L[i, j] = A[A0 + i].Text == B[B0 + j].Text ? L[i + 1, j + 1] + 1 : Math.Max(L[i + 1, j], L[i, j + 1]);
                }
            }
            int x = 0;
            int y = 0;
            while (x < N && y < M)
            {
                if (A[A0 + x].Text == B[B0 + y].Text)
                {
                    yield return (ConfigPreviewKind.Context, A0 + x, B0 + y);
                    x++;
                    y++;
                }
                else if (L[x + 1, y] >= L[x, y + 1])
                {
                    yield return (ConfigPreviewKind.Removed, A0 + x, -1);
                    x++;
                }
                else
                {
                    yield return (ConfigPreviewKind.Added, -1, B0 + y);
                    y++;
                }
            }
            for (; x < N; x++)
            {
                yield return (ConfigPreviewKind.Removed, A0 + x, -1);
            }
            for (; y < M; y++)
            {
                yield return (ConfigPreviewKind.Added, -1, B0 + y);
            }
        }

        // the lines of a text without their line breaks (LF, CRLF or CR), each with its offset; a leading U+FEFF (the
        // byte-order mark) is not shown
        internal static List<(int Start, string Text)> Lines(string Text)
        {
            var Lines = new List<(int, string)>();
            int Start = Text.Length > 0 && Text[0] == (char)0xFEFF ? 1 : 0;
            int i = Start;
            while (i < Text.Length)
            {
                if (Text[i] == '\r' || Text[i] == '\n')
                {
                    Lines.Add((Start, Text.Substring(Start, i - Start)));
                    i += Text[i] == '\r' && i + 1 < Text.Length && Text[i + 1] == '\n' ? 2 : 1;
                    Start = i;
                }
                else
                {
                    i++;
                }
            }
            if (Start < Text.Length)
            {
                Lines.Add((Start, Text.Substring(Start)));
            }
            return Lines;
        }

        private static string Masked((int Start, string Text) Line, IReadOnlyList<(int Start, int End)> Ranges, IReadOnlyList<string> Values)
        {
            string Text = Line.Text;
            int LineEnd = Line.Start + Text.Length;
            foreach ((int Start, int End) in Ranges.Where(R => R.End > R.Start && R.Start < LineEnd && R.End > Line.Start).OrderByDescending(R => R.Start))
            {
                int From = Math.Max(Start, Line.Start) - Line.Start;
                int To = Math.Min(End, LineEnd) - Line.Start;
                Text = Text.Substring(0, From) + Mask + Text.Substring(To);
            }
            foreach (string Value in Values.Where(V => !string.IsNullOrWhiteSpace(V)).OrderByDescending(V => V.Length))
            {
                Text = Text.Replace(Value, Mask, StringComparison.Ordinal);
            }
            return Text;
        }
    }
}
