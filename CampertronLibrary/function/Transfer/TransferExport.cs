using CampertronLibrary.function.Base;
using YamlDotNet.Core;

namespace CampertronLibrary.function.Transfer
{
    // An export's choices (v0.4.0-settings-transfer.md §Allow-list, §Secret opt-in): any of the three settings files, any of
    // the search files the folder lists, and each secret only by its own choice, all left out by default. No choice selects
    // the secrets together.
    internal sealed class TransferExportSelection
    {
        internal bool General { get; init; }
        internal bool Email { get; init; }
        internal bool Ntfy { get; init; }
        internal IReadOnlyList<string> Searches { get; init; } = Array.Empty<string>();
        internal bool SmtpPassword { get; init; }
        internal bool NtfyToken { get; init; }
        internal bool NtfyTopic { get; init; }

        internal bool Chose(TransferSecretKind Secret) => Secret switch
        {
            TransferSecretKind.SmtpPassword => SmtpPassword,
            TransferSecretKind.NtfyToken => NtfyToken,
            _ => NtfyTopic
        };

        internal bool ChoseFile(TransferEntryKind Kind) => Kind switch
        {
            TransferEntryKind.General => General,
            TransferEntryKind.Email => Email,
            TransferEntryKind.Ntfy => Ntfy,
            _ => false
        };

        internal IEnumerable<TransferSecretKind> Secrets => Enum.GetValues<TransferSecretKind>().Where(Chose);
    }

    // What a settings folder offers for an export, judged without reading a secret: the settings files that exist, the
    // search files the folder lists (those whose name the name rules refuse apart, named), and for each secret whether it
    // can be chosen or the note that says why not. A secret file is judged here by its metadata only (SecretFileCheck, D75);
    // an environment variable only by whether it is set, yes or no (D82), never by its value.
    internal sealed class TransferExportOffer
    {
        internal TransferExportOffer(bool General, bool Email, bool Ntfy, IReadOnlyList<string> Searches, IReadOnlyList<string> SearchesNotOffered,
            IReadOnlyDictionary<TransferSecretKind, TransferFailure?> Notes)
        {
            this.General = General;
            this.Email = Email;
            this.Ntfy = Ntfy;
            this.Searches = Searches;
            this.SearchesNotOffered = SearchesNotOffered;
            this.Notes = Notes;
        }
        internal bool General { get; }
        internal bool Email { get; }
        internal bool Ntfy { get; }
        internal IReadOnlyList<string> Searches { get; }
        // the listed search files a name rule refuses (a file made by hand): not offered, and said by name
        internal IReadOnlyList<string> SearchesNotOffered { get; }
        private IReadOnlyDictionary<TransferSecretKind, TransferFailure?> Notes { get; }

        internal bool Has(TransferEntryKind Kind) => Kind switch
        {
            TransferEntryKind.General => General,
            TransferEntryKind.Email => Email,
            TransferEntryKind.Ntfy => Ntfy,
            _ => false
        };

        // null when the secret can be chosen with this selection, else why its choice is disabled: its file is not part of
        // the export, or the offer's own note (no value or source, only an environment variable, a file that can't be used)
        internal TransferFailure? SecretNote(TransferSecretKind Secret, TransferExportSelection Selection) =>
            !Selection.ChoseFile(TransferFormat.OwnerOf(Secret)) ? TransferWording.ExportSecret(TransferProblem.ExportSecretWithoutFile, Secret) : Notes[Secret];

        // the offer's own note of a secret, whatever the selection
        internal TransferFailure? SecretNote(TransferSecretKind Secret) => Notes[Secret];
    }

    // what an export made: the package's bytes (with an opted-in secret always the encrypted envelope), or why it stopped
    internal sealed class TransferExportResult
    {
        internal TransferExportResult(byte[]? Package, bool Encrypted, TransferFailure? Failure)
        {
            this.Package = Package;
            this.Encrypted = Encrypted;
            this.Failure = Failure;
        }
        internal byte[]? Package { get; }
        internal bool Encrypted { get; }
        internal TransferFailure? Failure { get; }
        internal bool Succeeded => Package != null;
        public override string ToString() => Succeeded ? $"TransferExportResult: {Package!.Length} bytes" + (Encrypted ? ", encrypted" : string.Empty) : $"TransferExportResult: {Failure}";
    }

    // Export's content (D132; v0.4.0-settings-transfer.md §Allow-list, §Never-carried files and state, §Secret classification,
    // §Secret opt-in, §Environment-variable handling, §Text-preserving YAML edits, §Validation), from a settings folder its
    // caller names: phase 16a has no caller but its tests, so nothing here finds or uses a real settings folder by itself.
    //  - Only the allow-list is ever read: General.yaml, Email.yaml, Ntfy.yaml and the chosen search files the folder lists
    //    (ConfigFiles.SortedSearchFileNames, by names SearchFileName.Check accepts); never Web.yaml, Web.password, Web.keys,
    //    preferences.json, Camp.History, the campground data, a backup, a temporary file or any other file.
    //  - The keys that belong to one computer are removed from a copy of each settings text by the YAML document editor's
    //    text-range edits (YamlDocument.Edit, RemoveProperty), never by serializing a model: lastRidbDataRefresh;
    //    smtpPassword and smtpPasswordFile; topic and tokenFile. The edited text must load as the file's settings without
    //    them (the editor checks it), and must hold the removed password or topic nowhere else, as a secret-free backup
    //    (D111, D112). Search files travel unchanged. A file the loader refuses, one over 64 KiB, or one the editor can't
    //    edit, is not exported and is named.
    //  - A secret is read only when it was chosen, once, when the export is made: a password or token file through SecretFile
    //    (the one reader), the plain-text password or the topic from the file's settings. A secret whose source is an
    //    environment variable is never offered and its value never read. A secret that is no usable secret by SecretFile's
    //    rules stops the export.
    //  - A package with a chosen secret is the encrypted envelope (the passphrase required); one without is the plain one.
    // Nothing is written: the bytes go to the caller.
    internal static class TransferExport
    {
        // the keys Export leaves out of each settings file's text (§Allow-list, §Receiver-kept keys)
        internal static readonly IReadOnlyList<string> GeneralKeysLeftOut = new[] { "lastRidbDataRefresh" };
        internal static readonly IReadOnlyList<string> EmailKeysLeftOut = new[] { Base.SmtpPassword.EmailYamlKey, Base.SmtpPassword.FileKey };
        internal static readonly IReadOnlyList<string> NtfyKeysLeftOut = new[] { "topic", Base.NtfyToken.FileKey };

        internal static TransferExportOffer Offer(string ConfigFolder)
        {
            bool General = File.Exists(Path.Join(ConfigFolder, ConfigFiles.GeneralFileName));
            bool Email = File.Exists(Path.Join(ConfigFolder, ConfigFiles.EmailFileName));
            bool Ntfy = File.Exists(Path.Join(ConfigFolder, ConfigFiles.NtfyFileName));
            List<string> Listed = Directory.Exists(ConfigFolder) ? ConfigFiles.SortedSearchFileNames(ConfigFolder) : new List<string>();
            var Offered = new List<string>();
            var NotOffered = new List<string>();
            foreach (string Name in Listed)
            {
                (IsSearchName(Name) ? Offered : NotOffered).Add(Name);
            }
            EmailConfig? EmailModel = Email ? TryLoad<EmailConfig>(Path.Join(ConfigFolder, ConfigFiles.EmailFileName)) : null;
            NtfyConfig? NtfyModel = Ntfy ? TryLoad<NtfyConfig>(Path.Join(ConfigFolder, ConfigFiles.NtfyFileName)) : null;
            var Notes = new Dictionary<TransferSecretKind, TransferFailure?>
            {
                [TransferSecretKind.SmtpPassword] = EmailModel == null ? Note(TransferProblem.ExportSecretWithoutFile, TransferSecretKind.SmtpPassword) : PasswordNote(EmailModel),
                [TransferSecretKind.NtfyToken] = NtfyModel == null ? Note(TransferProblem.ExportSecretWithoutFile, TransferSecretKind.NtfyToken) : TokenNote(NtfyModel),
                [TransferSecretKind.NtfyTopic] = NtfyModel == null ? Note(TransferProblem.ExportSecretWithoutFile, TransferSecretKind.NtfyTopic)
                    : string.IsNullOrEmpty(NtfyModel.Topic) ? Note(TransferProblem.ExportSecretNotConfigured, TransferSecretKind.NtfyTopic) : null
            };
            return new TransferExportOffer(General, Email, Ntfy, Offered, NotOffered, Notes);
        }

        // the SMTP password's note by its source's precedence (SmtpPassword.SelectedSource: the file, then the variable,
        // then the plain text), without reading the file or the variable's value
        private static TransferFailure? PasswordNote(EmailConfig Email) => Base.SmtpPassword.SelectedSource(Email) switch
        {
            SmtpPasswordSource.File => SecretFileCheck.Locate(Email.SmtpPasswordFile, out _) is SecretFileProblem Problem
                ? Note(TransferProblem.ExportSecretFileUnusable, TransferSecretKind.SmtpPassword, TransferWording.SecretFileReason(Problem)) : null,
            SmtpPasswordSource.Environment => Note(TransferProblem.ExportSecretFromEnvironment, TransferSecretKind.SmtpPassword),
            _ => string.IsNullOrEmpty(Email.SmtpPassword) ? Note(TransferProblem.ExportSecretNotConfigured, TransferSecretKind.SmtpPassword) : null
        };

        // ntfy's token's note by its precedence (NtfyToken.SelectedSource: the file, then the variable, else none)
        private static TransferFailure? TokenNote(NtfyConfig Ntfy) => Base.NtfyToken.SelectedSource(Ntfy) switch
        {
            NtfyTokenSource.File => SecretFileCheck.Locate(Ntfy.TokenFile, out _) is SecretFileProblem Problem
                ? Note(TransferProblem.ExportSecretFileUnusable, TransferSecretKind.NtfyToken, TransferWording.SecretFileReason(Problem)) : null,
            NtfyTokenSource.Environment => Note(TransferProblem.ExportSecretFromEnvironment, TransferSecretKind.NtfyToken),
            _ => Note(TransferProblem.ExportSecretNotConfigured, TransferSecretKind.NtfyToken)
        };

        private static TransferFailure Note(TransferProblem Problem, TransferSecretKind Secret, string? Reason = null) => TransferWording.ExportSecret(Problem, Secret, Reason);

        // The package of a selection: every choice checked against the folder as it is now, each chosen file read once, each
        // chosen secret read once; the passphrase only with a chosen secret (then required), never otherwise. ExportedBy is
        // the exporting version (the running Campertron's when null).
        internal static TransferExportResult Build(string ConfigFolder, TransferExportSelection Selection, string? Passphrase, string? ExportedBy = null)
        {
            ArgumentNullException.ThrowIfNull(ConfigFolder);
            ArgumentNullException.ThrowIfNull(Selection);
            bool Encrypt = Selection.Secrets.Any();
            if (!Encrypt && Passphrase != null)
            {
                throw new ArgumentException("A passphrase is asked only when a secret is chosen.", nameof(Passphrase));
            }
            TransferExportOffer Offered = Offer(ConfigFolder);
            // the choices
            var Files = new List<(TransferEntryKind Kind, string Name)>();
            foreach (TransferEntryKind Kind in new[] { TransferEntryKind.General, TransferEntryKind.Email, TransferEntryKind.Ntfy })
            {
                if (Selection.ChoseFile(Kind))
                {
                    string Name = TransferFormat.FixedName(Kind)!;
                    if (!Offered.Has(Kind))
                    {
                        return Failed(TransferWording.ExportFile(TransferProblem.ExportFileMissing, Name));
                    }
                    Files.Add((Kind, Name));
                }
            }
            var Chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string Name in Selection.Searches)
            {
                if (Name == null || !IsSearchName(Name))
                {
                    return Failed(TransferWording.Export(TransferProblem.NameRefused));
                }
                if (!Chosen.Add(Name))
                {
                    return Failed(TransferWording.ExportFile(TransferProblem.DuplicateName, Name));
                }
                if (!Offered.Searches.Contains(Name, StringComparer.Ordinal))
                {
                    return Failed(TransferWording.ExportFile(TransferProblem.ExportFileMissing, Name));
                }
                Files.Add((TransferEntryKind.Search, Name));
            }
            if (Files.Count == 0)
            {
                return Failed(TransferWording.Export(TransferProblem.ExportNothingChosen));
            }
            foreach (TransferSecretKind Secret in Selection.Secrets)
            {
                if (Offered.SecretNote(Secret, Selection) is TransferFailure Note_)
                {
                    return Failed(Note_);
                }
            }
            if (Files.Count + Selection.Secrets.Count() > TransferFormat.MaxEntries)
            {
                return Failed(TransferWording.Export(TransferProblem.ExportTooManyEntries));
            }
            if (Encrypt)
            {
                if (!TransferCrypto.IsSupported)
                {
                    return Failed(TransferWording.Package(TransferProblem.EncryptionUnavailable));
                }
                if (TransferPassphrase.Check(Passphrase) is TransferFailure Problem)
                {
                    return Failed(Problem);
                }
            }
            // the texts
            var Entries = new List<(TransferEntryKind Kind, string Name, string Text)>();
            EmailConfig? EmailModel = null;
            NtfyConfig? NtfyModel = null;
            foreach ((TransferEntryKind Kind, string Name) in Files)
            {
                string? Text = ReadText(Path.Join(ConfigFolder, Name), Name, out TransferFailure? Failure);
                if (Text == null)
                {
                    return Failed(Failure!);
                }
                switch (Kind)
                {
                    case TransferEntryKind.General:
                        Text = Filtered<GeneralConfig>(Text, Name, GeneralKeysLeftOut, null, out _, out Failure);
                        break;
                    case TransferEntryKind.Email:
                        Text = Filtered(Text, Name, EmailKeysLeftOut, Base.SmtpPassword.EmailYamlKey, out EmailModel, out Failure);
                        break;
                    case TransferEntryKind.Ntfy:
                        Text = Filtered(Text, Name, NtfyKeysLeftOut, "topic", out NtfyModel, out Failure);
                        break;
                    default:
                        if (Load<CampertronConfig>(Text) == null)
                        {
                            Text = null;
                            Failure = TransferWording.ExportFile(TransferProblem.ExportFileRefused, Name);
                        }
                        break;
                }
                if (Text == null)
                {
                    return Failed(Failure!);
                }
                Entries.Add((Kind, Name, Text));
            }
            // the secrets, read now and only now, once each
            foreach (TransferSecretKind Secret in Selection.Secrets)
            {
                TransferFailure? Failure;
                string? Value;
                if (Secret == TransferSecretKind.SmtpPassword)
                {
                    Value = ReadPassword(EmailModel!, out Failure);
                }
                else if (Secret == TransferSecretKind.NtfyToken)
                {
                    Value = ReadToken(NtfyModel!, out Failure);
                }
                else
                {
                    Value = string.IsNullOrEmpty(NtfyModel!.Topic) ? null : NtfyModel.Topic;
                    Failure = Value == null ? Note(TransferProblem.ExportSecretNotConfigured, Secret) : null;
                }
                if (Value == null)
                {
                    return Failed(Failure!);
                }
                if (!TransferPackage.IsUsableSecret(Value))
                {
                    return Failed(Note(TransferProblem.ExportSecretUnusable, Secret));
                }
                Entries.Add((TransferFormat.EntryKindOf(Secret), TransferFormat.FixedName(TransferFormat.EntryKindOf(Secret))!, Value));
            }
            // the package
            TransferPackage? Package = TransferPackage.Create(ExportedBy ?? TransferFormat.CurrentExportedBy(), Entries, out TransferFailure? Refused);
            if (Package == null)
            {
                return Failed(Refused!.Problem == TransferProblem.TooManyEntries ? TransferWording.Export(TransferProblem.ExportTooManyEntries) : Refused);
            }
            TransferWriteResult Written = Encrypt ? TransferPackageWriter.WriteEncrypted(Package, Passphrase) : TransferPackageWriter.WritePlain(Package);
            if (!Written.Succeeded)
            {
                return Failed(Written.Failure!.Problem == TransferProblem.TooLarge ? TransferWording.Export(TransferProblem.ExportTooLarge) : Written.Failure);
            }
            return new TransferExportResult(Written.Bytes, Encrypt, null);
        }

        private static TransferExportResult Failed(TransferFailure Failure) => new TransferExportResult(null, false, Failure);

        // a search name the name rules accept: exactly what SearchFileName.Check gives for it
        private static bool IsSearchName(string Name) => SearchFileName.Check(Name, out string Checked) == null && string.Equals(Checked, Name, StringComparison.Ordinal);

        // A settings or search file's text as the safe writers read it (ConfigFileReplace.Read: strict UTF-8, a byte-order mark
        // kept as its one character, the line endings as they are), at most 64 KiB of UTF-8: a larger file is refused from its
        // length before it is read. The reader's message (it names the file's path) is left out.
        private static string? ReadText(string FilePath, string Name, out TransferFailure? Failure)
        {
            Failure = null;
            try
            {
                if (new FileInfo(FilePath).Length > TransferFormat.MaxTextBytes)
                {
                    Failure = TransferWording.ExportFile(TransferProblem.ExportFileTooLarge, Name);
                    return null;
                }
                string Text = ConfigFileReplace.Read(FilePath).Text;
                if ((TransferFormat.Utf8Length(Text) ?? int.MaxValue) > TransferFormat.MaxTextBytes)
                {
                    Failure = TransferWording.ExportFile(TransferProblem.ExportFileTooLarge, Name);
                    return null;
                }
                return Text;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                Failure = TransferWording.ExportFile(TransferProblem.ExportFileUnreadable, Name);
                return null;
            }
        }

        // the settings of a text as every loader reads them (the bounded loader), or null where it refuses them
        private static T? Load<T>(string Text) where T : class
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

        private static T? TryLoad<T>(string FilePath) where T : class
        {
            try
            {
                return Load<T>(ConfigFileReplace.Read(FilePath).Text);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        // A settings text without the keys that belong to this computer: each present key removed by a text-range edit, the
        // result loaded and compared with the file's settings without those keys (YamlDocument.Edit), and the removed secret's
        // value (SecretKey: the plain-text password, the topic) found nowhere else in it. Model is the file's settings as read.
        private static string? Filtered<T>(string Text, string Name, IReadOnlyList<string> Keys, string? SecretKey, out T? Model, out TransferFailure? Failure) where T : class
        {
            Failure = null;
            Model = Load<T>(Text);
            T? Intended = Load<T>(Text);
            if (Model == null || Intended == null)
            {
                Failure = TransferWording.ExportFile(TransferProblem.ExportFileRefused, Name);
                return null;
            }
            IReadOnlyDictionary<string, System.Reflection.PropertyInfo> Properties = YamlDocument.ModelKeys(typeof(T));
            YamlDocumentAnalysis Analysis = YamlDocument.Analyze(Text);
            List<string> Present = Keys.Where(Key => Analysis.Properties.Any(P => string.Equals(P.Key, Key, StringComparison.Ordinal))).ToList();
            string Result = Text;
            if (Present.Count > 0)
            {
                foreach (string Key in Present)
                {
                    Properties[Key].SetValue(Intended, null);
                }
                YamlDocumentResult Edited = YamlDocument.Edit(Text, Present.Select(YamlDocumentEdit.RemoveProperty).ToList(), Intended);
                if (!Edited.Succeeded || Edited.Text == null)
                {
                    Failure = TransferWording.ExportFile(TransferProblem.ExportFileNotEditable, Name);
                    return null;
                }
                Result = Edited.Text;
            }
            if (SecretKey != null && Properties[SecretKey].GetValue(Model) is string Secret && Secret.Length > 0 && Result.Contains(Secret, StringComparison.Ordinal))
            {
                Failure = TransferWording.ExportFile(TransferProblem.ExportFileHoldsSecret, Name);
                return null;
            }
            return Result;
        }

        // the SMTP password from the source its precedence selects: the file through SecretFile (SmtpPassword.ReadFile), or
        // the plain text; never the environment variable
        private static string? ReadPassword(EmailConfig Email, out TransferFailure? Failure)
        {
            Failure = null;
            switch (Base.SmtpPassword.SelectedSource(Email))
            {
                case SmtpPasswordSource.File:
                    SmtpPasswordResult Read = Base.SmtpPassword.ReadFile(Email.SmtpPasswordFile);
                    if (!Read.Succeeded || string.IsNullOrEmpty(Read.Password))
                    {
                        Failure = Note(TransferProblem.ExportSecretFileUnusable, TransferSecretKind.SmtpPassword, PasswordReason(Read.Problem));
                        return null;
                    }
                    return Read.Password;
                case SmtpPasswordSource.Environment:
                    Failure = Note(TransferProblem.ExportSecretFromEnvironment, TransferSecretKind.SmtpPassword);
                    return null;
                default:
                    if (string.IsNullOrEmpty(Email.SmtpPassword))
                    {
                        Failure = Note(TransferProblem.ExportSecretNotConfigured, TransferSecretKind.SmtpPassword);
                        return null;
                    }
                    return Email.SmtpPassword;
            }
        }

        // ntfy's token from its file through SecretFile, a token NtfyToken accepts; never the environment variable
        private static string? ReadToken(NtfyConfig Ntfy, out TransferFailure? Failure)
        {
            Failure = null;
            switch (Base.NtfyToken.SelectedSource(Ntfy))
            {
                case NtfyTokenSource.File:
                    if (SecretFile.Read(Ntfy.TokenFile, out string? Token) is SecretFileProblem Problem)
                    {
                        Failure = Note(TransferProblem.ExportSecretFileUnusable, TransferSecretKind.NtfyToken, TransferWording.SecretFileReason(Problem));
                        return null;
                    }
                    if (Token == null || !Base.NtfyToken.IsValidToken(Token))
                    {
                        Failure = Note(TransferProblem.ExportSecretFileUnusable, TransferSecretKind.NtfyToken, TransferWording.TokenCharactersReason);
                        return null;
                    }
                    return Token;
                case NtfyTokenSource.Environment:
                    Failure = Note(TransferProblem.ExportSecretFromEnvironment, TransferSecretKind.NtfyToken);
                    return null;
                default:
                    Failure = Note(TransferProblem.ExportSecretNotConfigured, TransferSecretKind.NtfyToken);
                    return null;
            }
        }

        private static string PasswordReason(SmtpPasswordProblem? Problem) => TransferWording.SecretFileReason(Problem switch
        {
            SmtpPasswordProblem.FilePathEmpty => SecretFileProblem.PathEmpty,
            SmtpPasswordProblem.FilePathNotAbsolute => SecretFileProblem.PathNotAbsolute,
            SmtpPasswordProblem.FileMissing => SecretFileProblem.Missing,
            SmtpPasswordProblem.FileIsFolder => SecretFileProblem.IsFolder,
            SmtpPasswordProblem.FileEmpty => SecretFileProblem.Empty,
            SmtpPasswordProblem.FileTooLarge => SecretFileProblem.TooLarge,
            SmtpPasswordProblem.FileHasByteOrderMark => SecretFileProblem.HasByteOrderMark,
            SmtpPasswordProblem.FileNotUtf8 => SecretFileProblem.NotUtf8,
            SmtpPasswordProblem.FileHasNul => SecretFileProblem.HasNul,
            SmtpPasswordProblem.FileOnlyLineBreak => SecretFileProblem.OnlyLineBreak,
            SmtpPasswordProblem.FileHasLineBreak => SecretFileProblem.HasLineBreak,
            _ => SecretFileProblem.NotReadable
        });
    }
}
