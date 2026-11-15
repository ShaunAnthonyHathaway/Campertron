//The inputs and outputs of the settings editing session (CampertronLibrary.function.Base.ConfigEditSession, CM5 of GUI
//configuration management, decision D43). Internal: the GUI's settings editor is its only caller. No message, notice or
//preview line carries a value of Email.yaml's smtpPassword: messages are fixed text, key names and numbers (the
//validation's, the editor's and the writer's, which never quote a value), and the preview masks the password.

internal enum ConfigEditState
{
    //the file does not exist; the GUI can create the sample a first start writes
    Missing,
    //the file can't be read for editing (not strict UTF-8, too large, empty, a link that can't be followed, ...)
    Unreadable,
    //the file is read but can't be edited safely: the YAML document editor refuses it, or the loader can't read its
    //settings (for example an unknown key); nothing in it is changed
    ReadOnly,
    Editable
}

internal enum ConfigEditSource
{
    //the configuration validation (ConfigValidation, D41)
    Validation,
    //the YAML document editor (YamlDocument, D39): a refused edit, a removed comment, mixed line breaks
    Editor,
    //the session itself (permissions, external changes)
    Session
}

internal sealed class ConfigEditNotice
{
    internal ConfigEditNotice(ConfigSeverity Severity, ConfigEditSource Source, string Code, string? Key, int? Index, string Message)
    {
        this.Severity = Severity;
        this.Source = Source;
        this.Code = Code;
        this.Key = Key;
        this.Index = Index;
        this.Message = Message;
    }
    internal ConfigSeverity Severity { get; }
    internal ConfigEditSource Source { get; }
    internal string Code { get; }
    //the top-level key (camelCase) the notice is about, or null for the whole file
    internal string? Key { get; }
    //the 0-based list item, if any
    internal int? Index { get; }
    //fixed text, key names and numbers only
    internal string Message { get; }
    public override string ToString() => $"{Severity} {Source} {Code}" + (Key != null ? " " + Key + (Index != null ? $"[{Index}]" : "") : "") + ": " + Message;
}

internal enum ConfigPreviewKind
{
    Context,
    Removed,
    Added,
    //unchanged lines left out between two changes
    Gap
}

//one line of the preview of a save: the line as it is (Removed, Context) or as it will be (Added), without its line break;
//the password's value is masked
internal sealed class ConfigPreviewLine
{
    internal ConfigPreviewLine(ConfigPreviewKind Kind, int? OldLine, int? NewLine, string Text)
    {
        this.Kind = Kind;
        this.OldLine = OldLine;
        this.NewLine = NewLine;
        this.Text = Text;
    }
    internal ConfigPreviewKind Kind { get; }
    //1-based line numbers in the file as it is and as it will be
    internal int? OldLine { get; }
    internal int? NewLine { get; }
    internal string Text { get; }
}

//what CheckForExternalChange found
internal enum ConfigExternalChange
{
    None,
    //the file changed while nothing was edited: the session now shows the new version
    Reloaded,
    //only the file's permissions, time or links changed, not its content: kept as the new state of the file
    MetadataChanged,
    //the file disappeared while nothing was edited: the session is Missing
    Deleted,
    //the file changed or disappeared while it was edited: the edits are kept and must be resolved (Conflict)
    Conflict
}

internal enum ConfigConflictKind
{
    Changed,
    Deleted
}

//The file changed (or disappeared) on disk while it had unsaved edits. Nothing is written until it is resolved, and
//neither version is lost: the edits stay in the session and the other program's version stays on disk.
internal sealed class ConfigConflict
{
    internal ConfigConflict(ConfigConflictKind Kind, IReadOnlyList<string> OverlappingKeys, bool CanApplyEdits, string Message)
    {
        this.Kind = Kind;
        this.OverlappingKeys = OverlappingKeys;
        this.CanApplyEdits = CanApplyEdits;
        this.Message = Message;
    }
    internal ConfigConflictKind Kind { get; }
    //the edited keys the other program changed too (names only)
    internal IReadOnlyList<string> OverlappingKeys { get; }
    //the edits can be applied to the new version by the YAML document editor (for overlapping keys only with an explicit
    //choice to keep the edited values)
    internal bool CanApplyEdits { get; }
    internal string Message { get; }
}

//one row of an edited list: the index of the item it was in the file (null for a new item) and its value
internal sealed class ConfigListItem
{
    internal ConfigListItem(int? OriginalIndex, string Value)
    {
        this.OriginalIndex = OriginalIndex;
        this.Value = Value;
    }
    internal int? OriginalIndex { get; }
    internal string Value { get; }
}

internal sealed class ConfigEditResult
{
    internal ConfigEditResult(bool Succeeded, string Message)
    {
        this.Succeeded = Succeeded;
        this.Message = Message;
    }
    internal bool Succeeded { get; }
    //fixed text and file names only
    internal string Message { get; }
}
