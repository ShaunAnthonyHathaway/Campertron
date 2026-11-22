//The diagnostics of Campertron's settings files (CampertronLibrary.function.Base.ConfigValidation, decision D41). They
//are independent of any presentation: a console, a log or a later GUI shows them as it likes. A diagnostic names the file
//(its name only, never its folder), the key and the list item it is about and the position, with a message built from
//fixed text, key names and numbers only: never a value of the file and never the message of an exception, so nothing of
//Email.yaml (the SMTP server, the user name, the password, the addresses) can reach one. The codes are stable and
//documented (business-rules.md §15).

internal enum ConfigFileKind
{
    General,
    Email,
    Search,
    //Ntfy.yaml (D67)
    Ntfy
}

internal enum ConfigSeverity
{
    //a fact worth showing, nothing to fix (a disabled search, the test-only output type)
    Info,
    //Campertron uses the file, but probably not as intended
    Warning,
    //Campertron refuses the file (or, for Email.yaml in Email output, refuses to start the Email loop)
    Error
}

internal enum ConfigCode
{
    //the file as a whole
    FileMissing,
    FileTooLarge,
    TooManyEvents,
    TooDeep,
    TooManyAliases,
    NotUtf8,
    InvalidUtf8,
    Empty,
    Malformed,
    MultipleDocuments,
    NotAMapping,
    DuplicateKey,
    UnknownKey,
    //a value the loader can't read into its setting
    InvalidChoice,
    InvalidNumber,
    InvalidBoolean,
    InvalidDate,
    NotAList,
    NotAScalar,
    NotLoadable,
    //General.yaml
    TestOutput,
    RefreshIntervalInvalid,
    RefreshEveryStart,
    LastRefreshMissing,
    LastRefreshInFuture,
    //Email.yaml
    SmtpServerMissing,
    SmtpPortOutOfRange,
    SenderMissing,
    SenderInvalid,
    RecipientsMissing,
    RecipientInvalid,
    CredentialsMissing,
    //the SMTP password's source (D66): smtpPasswordFile written but empty, or not an absolute path; written and usable (a
    //fact); and smtpPassword set although smtpPasswordFile makes it unused
    PasswordFilePathEmpty,
    PasswordFilePathNotAbsolute,
    PasswordFileSet,
    PasswordNotUsed,
    //Ntfy.yaml (D67): ntfy off; the server, the topic, the tags, the timeout and the token file's form. Errors while ntfy is
    //enabled, warnings otherwise; NtfyServerNotHttps is always a warning, NtfyDisabled and NtfyTokenFileSet are facts
    NtfyDisabled,
    NtfyServerMissing,
    NtfyServerInvalid,
    NtfyServerNotHttps,
    NtfyTopicMissing,
    NtfyTopicInvalid,
    NtfyTagInvalid,
    NtfyTagsTooLong,
    NtfyTimeoutOutOfRange,
    NtfyTokenFilePathEmpty,
    NtfyTokenFilePathNotAbsolute,
    NtfyTokenFileSet,
    //a search file
    SearchDisabled,
    DisplayNameMissing,
    CampgroundIdMissing,
    CampgroundIdNotNumeric,
    TotalHumansMissing,
    SearchValueNegative,
    SearchValueZero,
    SearchWindowOutOfRange,
    SearchBeyondTwelveMonths,
    SearchDatesMissing,
    SearchDatesTooFew,
    SearchDatesExtra,
    DateInvalid,
    DateNotIso,
    DateCultureDependent,
    DatesEqual,
    DatesReversed,
    DateInPast,
    NoWeekdays,
    ConsecutiveDaysBelowOne,
    ListItemNull,
    ListItemEmpty,
    //filterMatching is not written Exact or Contains (empty, a number, padded, a combination) or appears twice (D51)
    FilterMatchingInvalid,
    //the settings folder
    NoSearchFiles,
    NoEnabledSearch
}

internal sealed class ConfigDiagnostic
{
    internal ConfigDiagnostic(ConfigCode Code, ConfigSeverity Severity, ConfigFileKind FileKind, string FileName, string? Key, int? Index, int Line, int Column, string Message)
    {
        this.Code = Code;
        this.Severity = Severity;
        this.FileKind = FileKind;
        this.FileName = FileName;
        this.Key = Key;
        this.Index = Index;
        this.Line = Line;
        this.Column = Column;
        this.Message = Message;
    }
    internal ConfigCode Code { get; }
    internal ConfigSeverity Severity { get; }
    internal ConfigFileKind FileKind { get; }
    //the file's name, without its folder
    internal string FileName { get; }
    //the top-level key (camelCase, as written in the file), if the diagnostic is about one
    internal string? Key { get; }
    //the 0-based item of a list setting, if the diagnostic is about one
    internal int? Index { get; }
    //1-based; 0 when the diagnostic has no position
    internal int Line { get; }
    internal int Column { get; }
    //fixed text, key names and numbers only
    internal string Message { get; }
    public override string ToString() =>
        $"{Severity} {Code} {FileName}" + (Key != null ? " " + Key + (Index != null ? $"[{Index}]" : "") : "") +
        (Line > 0 ? $" (line {Line}, column {Column})" : "") + ": " + Message;
}
