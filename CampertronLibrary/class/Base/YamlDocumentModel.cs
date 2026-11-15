using YamlDotNet.Core;

//The inputs and outputs of the YAML document editor (CampertronLibrary.function.Base.YamlDocument, decision D39). All of it
//is internal: the editor has no production caller yet. Offsets are UTF-16 indexes into the text the caller passed (the
//units of YamlDotNet's Mark.Index); lines and columns are 1-based and are counted after a leading U+FEFF, as Campertron's
//settings loader sees the file. No diagnostic, loss or analysis carries a scalar value of the document or the message of an
//exception, so nothing of Email.yaml (the SMTP password) can reach a message through them; a caller that wants to show
//content reads it from the text at the offsets.

internal enum YamlDocumentCode
{
    //the document is refused (nothing in it can be edited)
    TooLarge,
    InvalidText,
    UnsupportedLineBreak,
    Malformed,
    TooManyEvents,
    TooDeep,
    TooManyAliases,
    NoContent,
    MultipleDocuments,
    RootNotBlockMapping,
    ComplexKey,
    DuplicateKey,
    MergeKey,
    UnsupportedTag,
    UncertainComment,
    UnsupportedLayout,
    //an edit is refused (the document is fine)
    TooManyEdits,
    ConflictingEdits,
    UnknownProperty,
    MissingProperty,
    LastProperty,
    FieldLocked,
    BlockScalar,
    NotAScalar,
    NotASequence,
    UnsupportedSequence,
    IndexOutOfRange,
    InvalidOrder,
    ValueTypeMismatch,
    UnrepresentableValue,
    //the edited text did not pass its verification (no text is returned)
    ReparseFailed,
    PreservationFailed,
    RuntimeLoadFailed,
    ModelMismatch,
    //warnings: the result is still returned
    MixedLineEndings,
    UnknownKeys
}

internal enum YamlDocumentSeverity
{
    Warning,
    Error
}

internal sealed class YamlDocumentDiagnostic
{
    internal YamlDocumentDiagnostic(YamlDocumentCode Code, YamlDocumentSeverity Severity, string? Key, int Line, int Column, string Message)
    {
        this.Code = Code;
        this.Severity = Severity;
        this.Key = Key;
        this.Line = Line;
        this.Column = Column;
        this.Message = Message;
    }
    internal YamlDocumentCode Code { get; }
    internal YamlDocumentSeverity Severity { get; }
    //the top-level key the diagnostic is about, if any
    internal string? Key { get; }
    //0 when the diagnostic has no position
    internal int Line { get; }
    internal int Column { get; }
    //built from fixed text, key names and numbers only
    internal string Message { get; }
    public override string ToString() => $"{Severity} {Code}" + (Line > 0 ? $" (line {Line}, column {Column})" : "") + ": " + Message;
}

//what a property, or a sequence item, holds or blocks
internal enum YamlDocumentShape
{
    Scalar,
    BlockScalar,
    Alias,
    BlockSequence,
    FlowSequence,
    Mapping
}

//why a property can't be edited although the document can be: its value (or key) depends on an anchor, an alias or a
//standard tag such as !!str, which an edit could silently change or break
internal enum YamlDocumentLock
{
    None,
    Anchor,
    Alias,
    Tag
}

internal sealed class YamlDocumentComment
{
    internal YamlDocumentComment(int Start, int End, int Line)
    {
        this.Start = Start;
        this.End = End;
        this.Line = Line;
    }
    //from the '#' to the end of its line, without the line break
    internal int Start { get; }
    internal int End { get; }
    internal int Line { get; }
}

internal sealed class YamlDocumentItem
{
    internal YamlDocumentItem(int Start, int End, ScalarStyle Style, int Line, int UnitStart, int UnitEnd)
    {
        this.Start = Start;
        this.End = End;
        this.Style = Style;
        this.Line = Line;
        this.UnitStart = UnitStart;
        this.UnitEnd = UnitEnd;
    }
    //the item's scalar, quotes included
    internal int Start { get; }
    internal int End { get; }
    internal ScalarStyle Style { get; }
    internal int Line { get; }
    //what moves with the item when the sequence is reordered: in a block sequence the comment lines directly above the
    //item, the item's line and its end-of-line comment (without the line break); in a flow sequence the scalar
    internal int UnitStart { get; }
    internal int UnitEnd { get; }
}

internal sealed class YamlDocumentProperty
{
    internal YamlDocumentProperty(string Key, int KeyStart, int KeyEnd, int ValueStart, int ValueEnd, int FirstLine, int LastLine,
        int Indentation, YamlDocumentShape Shape, ScalarStyle? Style, YamlDocumentLock Lock, bool IsEditableSequence,
        IReadOnlyList<YamlDocumentItem> Items, YamlDocumentComment? InlineComment)
    {
        this.Key = Key;
        this.KeyStart = KeyStart;
        this.KeyEnd = KeyEnd;
        this.ValueStart = ValueStart;
        this.ValueEnd = ValueEnd;
        this.FirstLine = FirstLine;
        this.LastLine = LastLine;
        this.Indentation = Indentation;
        this.Shape = Shape;
        this.Style = Style;
        this.Lock = Lock;
        this.IsEditableSequence = IsEditableSequence;
        this.Items = Items;
        this.InlineComment = InlineComment;
    }
    internal string Key { get; }
    internal int KeyStart { get; }
    internal int KeyEnd { get; }
    //the value's source range; an empty value ("key:") is a zero-length range right after the colon, a flow sequence ends
    //after its ']', and a block scalar's range is trimmed to its last content character (the parser's own range runs on
    //over its line break and any blank lines that follow)
    internal int ValueStart { get; }
    internal int ValueEnd { get; }
    //the lines the property occupies: the key's line through the line of the value's last character
    internal int FirstLine { get; }
    internal int LastLine { get; }
    internal int Indentation { get; }
    internal YamlDocumentShape Shape { get; }
    internal ScalarStyle? Style { get; }
    internal YamlDocumentLock Lock { get; }
    //a sequence of single-line plain or quoted scalars whose items can be inserted, removed, replaced and reordered
    internal bool IsEditableSequence { get; }
    internal IReadOnlyList<YamlDocumentItem> Items { get; }
    internal YamlDocumentComment? InlineComment { get; }
}

internal sealed class YamlDocumentAnalysis
{
    internal YamlDocumentAnalysis(bool Succeeded, IReadOnlyList<YamlDocumentDiagnostic> Diagnostics, IReadOnlyList<YamlDocumentProperty> Properties,
        IReadOnlyList<YamlDocumentComment> Comments, bool HasByteOrderMark, bool EndsWithLineBreak, int LfCount, int CrLfCount, int CrCount,
        string InsertionLineEnding)
    {
        this.Succeeded = Succeeded;
        this.Diagnostics = Diagnostics;
        this.Properties = Properties;
        this.Comments = Comments;
        this.HasByteOrderMark = HasByteOrderMark;
        this.EndsWithLineBreak = EndsWithLineBreak;
        this.LfCount = LfCount;
        this.CrLfCount = CrLfCount;
        this.CrCount = CrCount;
        this.InsertionLineEnding = InsertionLineEnding;
    }
    //false when the document is refused; its error diagnostic says why
    internal bool Succeeded { get; }
    internal IReadOnlyList<YamlDocumentDiagnostic> Diagnostics { get; }
    internal IReadOnlyList<YamlDocumentProperty> Properties { get; }
    internal IReadOnlyList<YamlDocumentComment> Comments { get; }
    internal bool HasByteOrderMark { get; }
    internal bool EndsWithLineBreak { get; }
    internal int LfCount { get; }
    internal int CrLfCount { get; }
    internal int CrCount { get; }
    //the line break an inserted line gets: the document's most frequent one (ties: LF, CRLF, CR), LF without any
    internal string InsertionLineEnding { get; }
}

internal enum YamlDocumentEditKind
{
    SetScalar,
    RemoveProperty,
    SetSequence,
    SetItem,
    InsertItem,
    RemoveItem,
    ReorderItems
}

//One change to a top-level property. Each key may be the target of one edit per call; the edits of a call are planned
//against the text that was passed in and applied together.
internal sealed class YamlDocumentEdit
{
    private YamlDocumentEdit(YamlDocumentEditKind Kind, string Key)
    {
        ArgumentNullException.ThrowIfNull(Key);
        this.Kind = Kind;
        this.Key = Key;
    }
    internal YamlDocumentEditKind Kind { get; }
    internal string Key { get; }
    //SetScalar: null, a string, a bool, an int, a double, a DateTime or an enum value
    internal object? Value { get; private init; }
    //SetSequence, SetItem and InsertItem (one value)
    internal IReadOnlyList<string> Values { get; private init; } = Array.Empty<string>();
    internal int Index { get; private init; } = -1;
    //ReorderItems: Order[new position] = the item's current index
    internal IReadOnlyList<int> Order { get; private init; } = Array.Empty<int>();

    //replaces the value of a scalar property, or adds the property after the last one
    internal static YamlDocumentEdit SetScalar(string Key, object? Value) => new(YamlDocumentEditKind.SetScalar, Key) { Value = Value };
    //removes the property's own lines (the key's line through its value's last line), with their comments
    internal static YamlDocumentEdit RemoveProperty(string Key) => new(YamlDocumentEditKind.RemoveProperty, Key);
    //replaces every item of a sequence (or a missing or null value) with these strings
    internal static YamlDocumentEdit SetSequence(string Key, IReadOnlyList<string> Values) => new(YamlDocumentEditKind.SetSequence, Key) { Values = Copy(Values) };
    internal static YamlDocumentEdit SetItem(string Key, int Index, string Value) => new(YamlDocumentEditKind.SetItem, Key) { Index = Index, Values = Copy(new[] { Value }) };
    internal static YamlDocumentEdit InsertItem(string Key, int Index, string Value) => new(YamlDocumentEditKind.InsertItem, Key) { Index = Index, Values = Copy(new[] { Value }) };
    internal static YamlDocumentEdit RemoveItem(string Key, int Index) => new(YamlDocumentEditKind.RemoveItem, Key) { Index = Index };
    internal static YamlDocumentEdit ReorderItems(string Key, IReadOnlyList<int> Order)
    {
        ArgumentNullException.ThrowIfNull(Order);
        return new YamlDocumentEdit(YamlDocumentEditKind.ReorderItems, Key) { Order = Order.ToArray() };
    }
    private static string[] Copy(IReadOnlyList<string> Values)
    {
        ArgumentNullException.ThrowIfNull(Values);
        if (Values.Any(Value => Value == null))
        {
            throw new ArgumentException("A sequence item can't be null.", nameof(Values));
        }
        return Values.ToArray();
    }
}

internal enum YamlDocumentLossKind
{
    Property,
    SequenceItem,
    SequenceItems
}

//Content an edit removed from the document: the exact range (in the text that was passed in), its lines and the comments
//in it. Blank lines in the range are removed too; nothing outside it is.
internal sealed class YamlDocumentLoss
{
    internal YamlDocumentLoss(YamlDocumentLossKind Kind, string Key, int Index, int Start, int End, int FirstLine, int LastLine,
        IReadOnlyList<YamlDocumentComment> Comments)
    {
        this.Kind = Kind;
        this.Key = Key;
        this.Index = Index;
        this.Start = Start;
        this.End = End;
        this.FirstLine = FirstLine;
        this.LastLine = LastLine;
        this.Comments = Comments;
    }
    internal YamlDocumentLossKind Kind { get; }
    internal string Key { get; }
    //the removed item's index, or -1
    internal int Index { get; }
    internal int Start { get; }
    internal int End { get; }
    internal int FirstLine { get; }
    internal int LastLine { get; }
    internal IReadOnlyList<YamlDocumentComment> Comments { get; }
}

internal sealed class YamlDocumentResult
{
    internal YamlDocumentResult(bool Succeeded, string? Text, IReadOnlyList<YamlDocumentDiagnostic> Diagnostics, IReadOnlyList<YamlDocumentLoss> Losses)
    {
        this.Succeeded = Succeeded;
        this.Text = Text;
        this.Diagnostics = Diagnostics;
        this.Losses = Losses;
    }
    internal bool Succeeded { get; }
    //the edited document; null whenever Succeeded is false (a refused or unverified edit never returns text)
    internal string? Text { get; }
    internal IReadOnlyList<YamlDocumentDiagnostic> Diagnostics { get; }
    internal IReadOnlyList<YamlDocumentLoss> Losses { get; }
}
