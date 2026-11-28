using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.Transfer
{
    // why a package, an entry, a passphrase or an export was refused (v0.4.0-settings-transfer.md §Safe errors)
    internal enum TransferProblem
    {
        // the package as a whole: these name nothing
        Unreadable,
        TooLarge,
        NotAPackage,
        Damaged,
        NewerFormat,
        TooManyEntries,
        NoEntries,
        UnknownKind,
        NameRefused,
        SecretNotEncrypted,
        // one sentence for a wrong passphrase, a changed header, ciphertext or tag, and an encrypted header that is not
        // format 1's: it never says which
        WrongPassphraseOrChanged,
        EncryptionUnavailable,
        // an entry that passed the name rules: these name it
        DuplicateName,
        EntryTooLarge,
        // a secret entry: these name the secret's kind
        SecretUnusable,
        SecretWithoutItsFile,
        // the passphrase the user typed
        PassphraseEmpty,
        PassphraseTooShort,
        PassphraseTooLong,
        PassphraseOnlySpaces,
        PassphraseInvalid,
        // an export's choices and files: these name a file the name rules accepted, or a secret's kind
        ExportNothingChosen,
        ExportFileMissing,
        ExportFileTooLarge,
        ExportFileUnreadable,
        ExportFileRefused,
        ExportFileNotEditable,
        ExportFileHoldsSecret,
        ExportSecretWithoutFile,
        ExportSecretNotConfigured,
        ExportSecretFromEnvironment,
        ExportSecretFileUnusable,
        ExportSecretUnusable,
        ExportTooLarge,
        ExportTooManyEntries
    }

    // A refusal: its kind and its fixed sentence. The sentence holds at most a settings or search file's name that passed the
    // name rules, a secret's kind, an environment variable's name, a count or a limit: never a value of a settings file, a
    // secret, the passphrase, a path, an exception's or the system's message (§Logging and redaction)
    internal sealed class TransferFailure
    {
        internal TransferFailure(TransferProblem Problem, string Message, string? Name = null, TransferSecretKind? Secret = null)
        {
            this.Problem = Problem;
            this.Message = Message;
            this.Name = Name;
            this.Secret = Secret;
        }
        internal TransferProblem Problem { get; }
        internal string Message { get; }
        // the file the sentence names, if any
        internal string? Name { get; }
        // the secret the sentence names, if any
        internal TransferSecretKind? Secret { get; }
        public override string ToString() => Problem + ": " + Message;
    }

    // The transfer core's fixed sentences (§Safe errors, §Interface parity: an adapter shows them and defines none). Phase 16a
    // settles the sentences of the package, the passphrase and the export; the menus' and pages' words are phases 17 to 19's.
    internal static class TransferWording
    {
        private static string Limit(int Bytes) => Bytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        // the secret's words, as a preview names it by its kind only
        internal static string SecretWords(TransferSecretKind Secret) => Secret switch
        {
            TransferSecretKind.SmtpPassword => "the SMTP password",
            TransferSecretKind.NtfyToken => "ntfy's access token",
            _ => "ntfy's topic"
        };

        private static string Capitalized(string Words) => char.ToUpperInvariant(Words[0]) + Words.Substring(1);

        private static string FileOf(TransferSecretKind Secret) => Secret == TransferSecretKind.SmtpPassword ? ConfigFiles.EmailFileName : ConfigFiles.NtfyFileName;

        internal static TransferFailure Package(TransferProblem Problem) => new TransferFailure(Problem, Problem switch
        {
            TransferProblem.Unreadable => "This file can't be read.",
            TransferProblem.TooLarge => $"This file is larger than a Campertron settings package can be (at most {Limit(TransferFormat.MaxPackageBytes)} bytes), so it was not read.",
            TransferProblem.NotAPackage => "This file is not a Campertron settings package.",
            TransferProblem.Damaged => "This Campertron settings package is damaged and can't be read.",
            TransferProblem.NewerFormat => "This settings package was made by a newer version of Campertron. Update Campertron to read it.",
            TransferProblem.TooManyEntries => $"This settings package holds more than {TransferFormat.MaxEntries} files and secrets, more than Campertron reads.",
            TransferProblem.NoEntries => "This settings package holds no settings or search file.",
            TransferProblem.UnknownKind => "This settings package holds an entry of a kind Campertron does not know, so none of it can be read.",
            TransferProblem.NameRefused => "This settings package holds a name Campertron does not accept for a settings or search file, so none of it can be read.",
            TransferProblem.SecretNotEncrypted => "This settings package holds a secret but is not encrypted, so none of it can be read.",
            TransferProblem.WrongPassphraseOrChanged => "The passphrase is wrong, or the package was changed or damaged.",
            TransferProblem.EncryptionUnavailable => "Encryption is not available on this computer, so Campertron can't make or read an encrypted settings package.",
            _ => throw new ArgumentException("Not a package's problem.", nameof(Problem))
        });

        // an entry that passed the name rules
        internal static TransferFailure Entry(TransferProblem Problem, string Name) => new TransferFailure(Problem, Problem switch
        {
            TransferProblem.DuplicateName => $"This settings package holds {Name} more than once (or two names that differ only in upper and lower case), so none of it can be read.",
            TransferProblem.EntryTooLarge => $"{Name} in this settings package is larger than {Limit(TransferFormat.MaxTextBytes)} bytes, more than Campertron reads, so none of it can be read.",
            _ => throw new ArgumentException("Not an entry's problem.", nameof(Problem))
        }, Name);

        internal static TransferFailure Secret(TransferProblem Problem, TransferSecretKind Secret) => new TransferFailure(Problem, Problem switch
        {
            TransferProblem.SecretUnusable => $"{Capitalized(SecretWords(Secret))} in this settings package can't be used (a secret is 1 to {Limit(TransferFormat.MaxSecretBytes)} bytes of text on one line), so none of it can be read.",
            TransferProblem.SecretWithoutItsFile => $"This settings package holds {SecretWords(Secret)} without {FileOf(Secret)}, so none of it can be read.",
            _ => throw new ArgumentException("Not a secret's problem.", nameof(Problem))
        }, null, Secret);

        internal static TransferFailure Passphrase(TransferProblem Problem) => new TransferFailure(Problem, Problem switch
        {
            TransferProblem.PassphraseEmpty => "Enter the passphrase.",
            TransferProblem.PassphraseTooShort => $"The passphrase is too short: use at least {TransferPassphrase.MinLength} characters.",
            TransferProblem.PassphraseTooLong => $"The passphrase is too long: use at most {TransferPassphrase.MaxLength} characters.",
            TransferProblem.PassphraseOnlySpaces => "The passphrase can't be only spaces.",
            TransferProblem.PassphraseInvalid => "The passphrase has characters that can't be used; choose another.",
            _ => throw new ArgumentException("Not a passphrase's problem.", nameof(Problem))
        });

        // an export's file, by a name the name rules accepted (a settings file's, or a search file's from the listing)
        internal static TransferFailure ExportFile(TransferProblem Problem, string Name) => new TransferFailure(Problem, Problem switch
        {
            TransferProblem.ExportFileMissing => $"{Name} is not in the settings folder, so it can't be exported.",
            TransferProblem.ExportFileTooLarge => $"{Name} is larger than {Limit(TransferFormat.MaxTextBytes)} bytes, so it can't be exported.",
            TransferProblem.ExportFileUnreadable => $"{Name} can't be read, so it can't be exported.",
            TransferProblem.ExportFileRefused => $"{Name} has settings Campertron can't read, so it can't be exported.",
            TransferProblem.ExportFileNotEditable => $"Campertron can't leave this computer's own settings out of {Name} without changing the rest of it, so it can't be exported.",
            TransferProblem.ExportFileHoldsSecret => $"{Name} holds its password or topic outside that setting too (for example in a comment), so it can't be exported.",
            TransferProblem.DuplicateName => $"{Name} is chosen more than once (or with a name that differs only in upper and lower case).",
            _ => throw new ArgumentException("Not an export file's problem.", nameof(Problem))
        }, Name);

        // an export's choice of a secret that can't be made: why it is disabled, or why the export stopped
        internal static TransferFailure ExportSecret(TransferProblem Problem, TransferSecretKind Secret, string? Reason = null) => new TransferFailure(Problem, Problem switch
        {
            TransferProblem.ExportSecretWithoutFile => $"{Capitalized(SecretWords(Secret))} can be exported only with {FileOf(Secret)}.",
            TransferProblem.ExportSecretNotConfigured => Secret switch
            {
                TransferSecretKind.SmtpPassword => "There is no SMTP password in Email.yaml or in a password file to export.",
                TransferSecretKind.NtfyToken => "There is no ntfy access token file to export.",
                _ => "There is no ntfy topic in Ntfy.yaml to export."
            },
            TransferProblem.ExportSecretFromEnvironment => $"{Capitalized(SecretWords(Secret))} comes only from the environment variable {(Secret == TransferSecretKind.SmtpPassword ? SmtpPassword.EnvironmentVariable : NtfyToken.EnvironmentVariable)}, which Campertron never exports; set it on the other computer too.",
            TransferProblem.ExportSecretFileUnusable => $"{Capitalized(SecretWords(Secret))} can't be exported: the file that holds it can't be used ({Reason ?? "it can't be read"}).",
            TransferProblem.ExportSecretUnusable => $"{Capitalized(SecretWords(Secret))} can't be exported: a secret is 1 to {Limit(TransferFormat.MaxSecretBytes)} bytes of text on one line.",
            _ => throw new ArgumentException("Not an export secret's problem.", nameof(Problem))
        }, null, Secret);

        internal static TransferFailure Export(TransferProblem Problem) => new TransferFailure(Problem, Problem switch
        {
            TransferProblem.ExportNothingChosen => "Choose at least one settings or search file to export.",
            TransferProblem.ExportTooLarge => $"The chosen files would make a package larger than {Limit(TransferFormat.MaxPackageBytes)} bytes; choose fewer files.",
            TransferProblem.ExportTooManyEntries => $"The chosen files and secrets are more than the {TransferFormat.MaxEntries} a package holds; choose fewer files.",
            TransferProblem.NameRefused => "A chosen search file's name is not one Campertron accepts, so it can't be exported.",
            _ => throw new ArgumentException("Not an export's problem.", nameof(Problem))
        });

        // a secret file's problem, by its kind only (never the path)
        internal static string SecretFileReason(SecretFileProblem Problem) => Problem switch
        {
            SecretFileProblem.PathEmpty => "its path is empty",
            SecretFileProblem.PathNotAbsolute => "its path is not a full path",
            SecretFileProblem.Missing => "it does not exist",
            SecretFileProblem.IsFolder => "it is a folder",
            SecretFileProblem.NotReadable => "it can't be read",
            SecretFileProblem.Empty => "it is empty",
            SecretFileProblem.TooLarge => $"it is larger than {Limit(SecretFile.MaxFileBytes)} bytes",
            SecretFileProblem.HasByteOrderMark => "it starts with a byte-order mark",
            SecretFileProblem.NotUtf8 => "it is not UTF-8 text",
            SecretFileProblem.HasNul => "it holds a NUL character",
            SecretFileProblem.OnlyLineBreak => "it holds only a line break",
            _ => "it holds more than one line"
        };

        // ntfy's token file holds characters an HTTP header can't carry (NtfyToken.IsValidToken)
        internal const string TokenCharactersReason = "it holds characters a token can't have";
    }
}
