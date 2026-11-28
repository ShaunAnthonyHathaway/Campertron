using System.Security.Cryptography;
using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.Transfer
{
    // The kinds of a settings-transfer package's entries (D132; v0.4.0-settings-transfer.md §Entry kinds): an allow-list.
    // An entry of any other kind makes the whole package refused, never skipped.
    internal enum TransferEntryKind
    {
        General,
        Email,
        Ntfy,
        Search,
        SecretSmtpPassword,
        SecretNtfyToken,
        SecretNtfyTopic
    }

    // The three secrets a transfer can carry, each only after its own opt-in (D132 points 1 and 6; §Secret classification)
    internal enum TransferSecretKind
    {
        SmtpPassword,
        NtfyToken,
        NtfyTopic
    }

    // Format 1 of the settings-transfer package (v0.4.0's phase 16a; D132; v0.4.0-settings-transfer.md, the design that is the
    // authority for every name and number here). One UTF-8 JSON document, never compressed and never an archive, of a header
    // and entries; names, never paths. The limits are the reader's policy, not the format (§Format versioning).
    internal static class TransferFormat
    {
        internal const int Version = 1;

        // the document's members, in the writer's order (§Plain envelope, §Encrypted envelope)
        internal const string VersionMember = "campertronSettingsPackage";
        internal const string ProtectionMember = "protection";
        internal const string ExportedByMember = "exportedBy";
        internal const string PayloadMember = "payload";
        internal const string KdfMember = "kdf";
        internal const string CipherMember = "cipher";
        internal const string CiphertextMember = "ciphertext";
        internal const string TagMember = "tag";
        internal const string EntriesMember = "entries";
        internal const string ManifestMember = "manifestSha256";
        internal const string KindMember = "kind";
        internal const string NameMember = "name";
        internal const string TextMember = "text";
        internal const string Sha256Member = "sha256";
        internal const string IterationsMember = "iterations";
        internal const string SaltMember = "salt";
        internal const string NonceMember = "nonce";

        internal const string ProtectionNone = "none";
        internal const string ProtectionPassphrase = "passphrase";

        // the encrypted envelope's fixed parameters (§Integrity and authenticated encryption): a header that asks for any
        // other value is refused before a key is derived
        internal const string KdfName = "pbkdf2-sha256";
        internal const int Iterations = 600_000;
        internal const string CipherName = "aes-256-gcm";
        internal const int SaltBytes = 16;
        internal const int NonceBytes = 12;
        internal const int TagBytes = 16;
        internal const int KeyBytes = 32;
        // the first line of the associated data
        internal const string AssociatedDataLabel = "campertron-settings-package";

        // §Limits: each enforced before the work it bounds
        internal const int MaxPackageBytes = 1_048_576;
        internal const int MaxEntries = 256;
        internal const int MaxTextBytes = 65_536;
        // a secret is one SecretFile would read, so that a secret an import writes is one the monitoring's start reads
        internal const int MaxSecretBytes = SecretFile.MaxFileBytes;
        internal const int MaxExportedByLength = 64;
        internal const int MaxDepth = 4;

        // the kinds' texts in the package, in the writer's order
        internal static readonly IReadOnlyList<(TransferEntryKind Kind, string Text)> Kinds = new[]
        {
            (TransferEntryKind.General, "general"),
            (TransferEntryKind.Email, "email"),
            (TransferEntryKind.Ntfy, "ntfy"),
            (TransferEntryKind.Search, "search"),
            (TransferEntryKind.SecretSmtpPassword, "secret-smtp-password"),
            (TransferEntryKind.SecretNtfyToken, "secret-ntfy-token"),
            (TransferEntryKind.SecretNtfyTopic, "secret-ntfy-topic")
        };

        internal static string KindText(TransferEntryKind Kind) => Kinds.First(K => K.Kind == Kind).Text;

        // the kind of a text, compared exactly; null for any other text
        internal static TransferEntryKind? KindOf(string Text)
        {
            foreach ((TransferEntryKind Kind, string KindText_) in Kinds)
            {
                if (string.Equals(KindText_, Text, StringComparison.Ordinal))
                {
                    return Kind;
                }
            }
            return null;
        }

        // the one name an entry of a kind has; null for a search, whose name is its file's
        internal static string? FixedName(TransferEntryKind Kind) => Kind switch
        {
            TransferEntryKind.General => ConfigFiles.GeneralFileName,
            TransferEntryKind.Email => ConfigFiles.EmailFileName,
            TransferEntryKind.Ntfy => ConfigFiles.NtfyFileName,
            TransferEntryKind.SecretSmtpPassword => "smtpPassword",
            TransferEntryKind.SecretNtfyToken => "ntfyToken",
            TransferEntryKind.SecretNtfyTopic => "ntfyTopic",
            _ => null
        };

        internal static bool IsSecret(TransferEntryKind Kind) =>
            Kind == TransferEntryKind.SecretSmtpPassword || Kind == TransferEntryKind.SecretNtfyToken || Kind == TransferEntryKind.SecretNtfyTopic;

        // the settings file a secret belongs to, which must be in the same package
        internal static TransferEntryKind OwnerOf(TransferSecretKind Secret) => Secret == TransferSecretKind.SmtpPassword ? TransferEntryKind.Email : TransferEntryKind.Ntfy;

        internal static TransferEntryKind EntryKindOf(TransferSecretKind Secret) => Secret switch
        {
            TransferSecretKind.SmtpPassword => TransferEntryKind.SecretSmtpPassword,
            TransferSecretKind.NtfyToken => TransferEntryKind.SecretNtfyToken,
            _ => TransferEntryKind.SecretNtfyTopic
        };

        internal static TransferSecretKind SecretOf(TransferEntryKind Kind) => Kind switch
        {
            TransferEntryKind.SecretSmtpPassword => TransferSecretKind.SmtpPassword,
            TransferEntryKind.SecretNtfyToken => TransferSecretKind.NtfyToken,
            TransferEntryKind.SecretNtfyTopic => TransferSecretKind.NtfyTopic,
            _ => throw new ArgumentException("Not a secret's kind.", nameof(Kind))
        };

        // strict UTF-8: invalid text throws instead of becoming U+FFFD; no byte-order mark is added
        internal static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        // the SHA-256 of a text's UTF-8 bytes as 64 lower-case hexadecimal digits; null for text that is no valid Unicode
        internal static string? Sha256Hex(string Text)
        {
            byte[] Bytes;
            try
            {
                Bytes = StrictUtf8.GetBytes(Text);
            }
            catch (EncoderFallbackException)
            {
                return null;
            }
            return System.Convert.ToHexStringLower(SHA256.HashData(Bytes));
        }

        // the UTF-8 length of a text; null for text that is no valid Unicode
        internal static int? Utf8Length(string Text)
        {
            try
            {
                return StrictUtf8.GetByteCount(Text);
            }
            catch (EncoderFallbackException)
            {
                return null;
            }
        }

        internal static bool IsSha256Hex(string? Text) =>
            Text != null && Text.Length == 64 && Text.All(C => (C >= '0' && C <= '9') || (C >= 'a' && C <= 'f'));

        // §Header fields: 1 to 64 characters, each an ASCII letter, a digit or one of . - +
        internal static bool IsExportedBy(string? Text) =>
            Text != null && Text.Length >= 1 && Text.Length <= MaxExportedByLength
            && Text.All(C => (C >= 'A' && C <= 'Z') || (C >= 'a' && C <= 'z') || (C >= '0' && C <= '9') || C == '.' || C == '-' || C == '+');

        // §Exporting Campertron version: the application's version without its build's commit hash (the part after "+"),
        // as RuntimeInfo reads it; information only, never a reason to accept or refuse a package
        internal static string CurrentExportedBy()
        {
            System.Reflection.Assembly Library = typeof(TransferFormat).Assembly;
            string? Informational = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(Library)?.InformationalVersion;
            string Version_ = Informational == null ? string.Empty : Informational.Split('+')[0];
            if (IsExportedBy(Version_))
            {
                return Version_;
            }
            Version? Numbers = Library.GetName().Version;
            return Numbers == null ? "0.0.0" : Numbers.Major + "." + Numbers.Minor + "." + Math.Max(0, Numbers.Build);
        }

        // §Integrity and authenticated encryption: the manifest is the SHA-256 of these lines, each ended by a line feed: the
        // format version, the number of entries, then each entry's kind, name and sha256 in the package's order
        internal static string ManifestSha256(int FormatVersion, IReadOnlyList<(string Kind, string Name, string Sha256)> Entries)
        {
            var Lines = new StringBuilder();
            Lines.Append(FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            Lines.Append(Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            foreach ((string Kind, string Name, string Sha256) in Entries)
            {
                Lines.Append(Kind).Append('\n').Append(Name).Append('\n').Append(Sha256).Append('\n');
            }
            return System.Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(Lines.ToString())));
        }

        // standard base64 with padding, read strictly: only that alphabet, and the bytes must encode back to the same text
        // (System.Convert.FromBase64String also accepts white space, which is refused here)
        internal static byte[]? FromBase64Strict(string? Text, int MaxBytes)
        {
            if (string.IsNullOrEmpty(Text) || Text.Length % 4 != 0 || Text.Length / 4 * 3 > MaxBytes + 2)
            {
                return null;
            }
            foreach (char C in Text)
            {
                if (!((C >= 'A' && C <= 'Z') || (C >= 'a' && C <= 'z') || (C >= '0' && C <= '9') || C == '+' || C == '/' || C == '='))
                {
                    return null;
                }
            }
            byte[] Bytes;
            try
            {
                Bytes = System.Convert.FromBase64String(Text);
            }
            catch (FormatException)
            {
                return null;
            }
            return Bytes.Length <= MaxBytes && string.Equals(System.Convert.ToBase64String(Bytes), Text, StringComparison.Ordinal) ? Bytes : null;
        }
    }
}
