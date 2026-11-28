using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Unicode;

namespace CampertronLibrary.function.Transfer
{
    // what a read gave: a plain package read completely, an encrypted one whose passphrase is asked next, or a refusal
    internal sealed class TransferReadResult
    {
        private TransferReadResult(TransferPackage? Package, TransferLockedPackage? Locked, TransferFailure? Failure)
        {
            this.Package = Package;
            this.Locked = Locked;
            this.Failure = Failure;
        }
        internal static TransferReadResult Read(TransferPackage Package) => new TransferReadResult(Package, null, null);
        internal static TransferReadResult NeedsPassphrase(TransferLockedPackage Locked) => new TransferReadResult(null, Locked, null);
        internal static TransferReadResult Refused(TransferFailure Failure) => new TransferReadResult(null, null, Failure);
        internal TransferPackage? Package { get; }
        internal TransferLockedPackage? Locked { get; }
        internal TransferFailure? Failure { get; }
        public override string ToString() => Package != null ? Package.ToString() : Locked != null ? "TransferReadResult: needs the passphrase" : $"TransferReadResult: {Failure}";
    }

    // An encrypted package after its structure was read, before its passphrase: what Unlock needs, kept in memory only. Its
    // header values were checked when it was opened, and the result of that check is kept here, hidden: Unlock refuses a
    // header that is not format 1's in the same sentence as a wrong passphrase and without deriving a key, so neither the
    // package nor its answer says which part was wrong (v0.4.0-settings-transfer.md §Integrity and authenticated encryption).
    internal sealed class TransferLockedPackage
    {
        internal TransferLockedPackage(string ExportedBy, string Salt64, string Nonce64, byte[]? Salt, byte[]? Nonce, byte[]? Ciphertext, byte[]? Tag, bool HeaderPassed)
        {
            this.ExportedBy = ExportedBy;
            this.Salt64 = Salt64;
            this.Nonce64 = Nonce64;
            this.Salt = Salt;
            this.Nonce = Nonce;
            this.Ciphertext = Ciphertext;
            this.Tag = Tag;
            this.HeaderPassed = HeaderPassed;
        }
        internal string ExportedBy { get; }
        internal string Salt64 { get; }
        internal string Nonce64 { get; }
        internal byte[]? Salt { get; }
        internal byte[]? Nonce { get; }
        internal byte[]? Ciphertext { get; }
        internal byte[]? Tag { get; }
        internal bool HeaderPassed { get; }
        public override string ToString() => "TransferLockedPackage";
    }

    // The strict reader of a package (v0.4.0-settings-transfer.md §Shared package model, §Header fields, §Format versioning,
    // §Limits, §Integrity and authenticated encryption): the base class library's JSON reader, no package, nothing decompressed,
    // unpacked or written anywhere: everything is built in memory. In the design's order: the length; the bytes within the
    // limit; strict UTF-8 without a byte-order mark; the strict JSON document; the header; for the encrypted form the
    // passphrase, one derivation and the decryption with its tag; the payload's strict JSON; the entry count (while the list
    // is read); each entry's kind, name and length; each text's SHA-256; the manifest's SHA-256; the secrets' rules. A newer
    // format is refused as one; nothing is ever read "as far as it can be". Every refusal is a fixed sentence (TransferWording).
    internal static class TransferPackageReader
    {
        private static readonly JsonReaderOptions Strict = new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = TransferFormat.MaxDepth
        };

        // the version scan reads any JSON document within the package limit, to tell a newer format from a damaged one
        private static readonly JsonReaderOptions Scanning = new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = 64
        };

        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
        private static readonly byte[] VersionMemberBytes = TransferFormat.StrictUtf8.GetBytes("\"" + TransferFormat.VersionMember + "\"");

        // a refusal while the document is read, carried to Open's or Unlock's end; it holds no text of the package
        private sealed class Refusal : Exception
        {
            internal Refusal(TransferProblem Problem) : base(Problem.ToString())
            {
                this.Problem = Problem;
            }
            internal TransferProblem Problem { get; }
        }

        // ---- the bytes ----

        // At most the package limit and one byte of a stream, never more: a stream whose length is known and above the limit
        // is refused before anything is read. Null and the failure for a stream over the limit or one that can't be read.
        internal static byte[]? ReadBounded(Stream Input, out TransferFailure? Failure)
        {
            Failure = null;
            try
            {
                byte[]? Buffer = NewBuffer(Input, out Failure);
                if (Buffer == null)
                {
                    return null;
                }
                int Count = 0;
                int Read;
                while (Count < Buffer.Length && (Read = Input.Read(Buffer, Count, Buffer.Length - Count)) > 0)
                {
                    Count += Read;
                }
                return Bounded(Buffer, Count, out Failure);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ObjectDisposedException)
            {
                Failure = TransferWording.Package(TransferProblem.Unreadable);
                return null;
            }
        }

        // the same for a stream read asynchronously (an upload)
        internal static async Task<(byte[]? Bytes, TransferFailure? Failure)> ReadBoundedAsync(Stream Input, CancellationToken Cancel)
        {
            try
            {
                byte[]? Buffer = NewBuffer(Input, out TransferFailure? Failure);
                if (Buffer == null)
                {
                    return (null, Failure);
                }
                int Count = 0;
                int Read;
                while (Count < Buffer.Length && (Read = await Input.ReadAsync(Buffer.AsMemory(Count, Buffer.Length - Count), Cancel).ConfigureAwait(false)) > 0)
                {
                    Count += Read;
                }
                byte[]? Bytes = Bounded(Buffer, Count, out Failure);
                return (Bytes, Failure);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ObjectDisposedException)
            {
                return (null, TransferWording.Package(TransferProblem.Unreadable));
            }
        }

        // a stream that says it holds more than the limit is refused before it is read; any other is read to the limit and one
        // byte (also one that grows while it is read, whatever length it said)
        private static byte[]? NewBuffer(Stream Input, out TransferFailure? Failure)
        {
            Failure = null;
            if (Input.CanSeek && Input.Length - Input.Position > TransferFormat.MaxPackageBytes)
            {
                Failure = TransferWording.Package(TransferProblem.TooLarge);
                return null;
            }
            return new byte[TransferFormat.MaxPackageBytes + 1];
        }

        private static byte[]? Bounded(byte[] Buffer, int Count, out TransferFailure? Failure)
        {
            Failure = null;
            if (Count > TransferFormat.MaxPackageBytes)
            {
                Failure = TransferWording.Package(TransferProblem.TooLarge);
                return null;
            }
            return Buffer.AsSpan(0, Count).ToArray();
        }

        // ---- the package ----

        // A package's bytes: a plain package read and checked completely, an encrypted one ready for its passphrase, or the
        // refusal. No key is derived here.
        internal static TransferReadResult Open(ReadOnlySpan<byte> Bytes)
        {
            if (Bytes.Length > TransferFormat.MaxPackageBytes)
            {
                return Refused(TransferProblem.TooLarge);
            }
            bool Claims = ClaimsToBePackage(Bytes);
            if (Bytes.Length == 0 || Bytes.StartsWith(Utf8Bom) || !Utf8.IsValid(Bytes))
            {
                return Refused(Claims ? TransferProblem.Damaged : TransferProblem.NotAPackage);
            }
            switch (ScanVersion(Bytes))
            {
                case VersionScan.Syntax:
                    return Refused(Claims ? TransferProblem.Damaged : TransferProblem.NotAPackage);
                case VersionScan.None:
                    return Refused(TransferProblem.NotAPackage);
                case VersionScan.Invalid:
                    return Refused(TransferProblem.Damaged);
                case VersionScan.Newer:
                    return Refused(TransferProblem.NewerFormat);
            }
            Envelope E;
            try
            {
                E = ReadEnvelope(Bytes);
            }
            catch (Refusal ex)
            {
                return Refused(ex.Problem);
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
            {
                // the reader's message holds a position and a token: left out
                return Refused(TransferProblem.Damaged);
            }
            if (E.Protection == TransferFormat.ProtectionNone && E.Has(PlainMembers) && !E.HasAny(EncryptedOnlyMembers))
            {
                if (!TransferFormat.IsExportedBy(E.ExportedBy))
                {
                    return Refused(TransferProblem.Damaged);
                }
                List<TransferEntry>? Entries = TransferPackage.CheckEntries(TransferFormat.Version, E.Entries!, false, E.Manifest, out TransferFailure? Failure);
                return Entries == null
                    ? TransferReadResult.Refused(Failure!)
                    : TransferReadResult.Read(TransferPackage.FromRead(TransferFormat.Version, E.ExportedBy!, false, Entries));
            }
            if (E.Protection == TransferFormat.ProtectionPassphrase && E.Has(EncryptedMembers) && !E.HasAny(PlainOnlyMembers))
            {
                if (!TransferCrypto.IsSupported)
                {
                    return Refused(TransferProblem.EncryptionUnavailable);
                }
                return TransferReadResult.NeedsPassphrase(Locked(E));
            }
            return Refused(TransferProblem.Damaged);
        }

        // The plaintext of an encrypted package with this passphrase, read and checked like a plain package's payload; one
        // derivation for each call, none for a passphrase the rules refuse or a header that is not format 1's. A wrong
        // passphrase, a changed header, ciphertext or tag all give the same sentence.
        internal static TransferReadResult Unlock(TransferLockedPackage Package, string? Passphrase)
        {
            byte[]? Secret = TransferPassphrase.Bytes(Passphrase, out TransferFailure? Problem);
            if (Secret == null)
            {
                return TransferReadResult.Refused(Problem!);
            }
            byte[]? Key = null;
            byte[]? Plaintext = null;
            try
            {
                if (!TransferCrypto.IsSupported)
                {
                    return Refused(TransferProblem.EncryptionUnavailable);
                }
                if (!Package.HeaderPassed)
                {
                    return Refused(TransferProblem.WrongPassphraseOrChanged);
                }
                Key = TransferCrypto.DeriveKey(Secret, Package.Salt!);
                // the values read: exportedBy, the salt and the nonce as their texts, and the version and iteration count,
                // which the header's check required to be exactly format 1's (1 and 600000)
                byte[] Associated = TransferCrypto.AssociatedData(TransferFormat.Version, Package.ExportedBy, TransferFormat.Iterations, Package.Salt64, Package.Nonce64);
                Plaintext = TransferCrypto.Open(Key, Package.Nonce!, Package.Ciphertext!, Package.Tag!, Associated);
                if (Plaintext == null)
                {
                    return Refused(TransferProblem.WrongPassphraseOrChanged);
                }
                // authenticated: what follows checks a payload its passphrase's holder wrote
                if (Plaintext.AsSpan().StartsWith(Utf8Bom) || !Utf8.IsValid(Plaintext))
                {
                    return Refused(TransferProblem.Damaged);
                }
                (List<TransferPackage.RawEntry> Raw, string Manifest) Payload;
                try
                {
                    var R = new Utf8JsonReader(Plaintext, Strict);
                    Payload = ReadPayload(ref R);
                    if (R.Read())
                    {
                        return Refused(TransferProblem.Damaged);
                    }
                }
                catch (Refusal ex)
                {
                    return Refused(ex.Problem);
                }
                catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
                {
                    return Refused(TransferProblem.Damaged);
                }
                List<TransferEntry>? Entries = TransferPackage.CheckEntries(TransferFormat.Version, Payload.Raw, true, Payload.Manifest, out TransferFailure? Failure);
                return Entries == null
                    ? TransferReadResult.Refused(Failure!)
                    : TransferReadResult.Read(TransferPackage.FromRead(TransferFormat.Version, Package.ExportedBy, true, Entries));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(Secret);
                if (Key != null)
                {
                    CryptographicOperations.ZeroMemory(Key);
                }
                if (Plaintext != null)
                {
                    CryptographicOperations.ZeroMemory(Plaintext);
                }
            }
        }

        private static TransferReadResult Refused(TransferProblem Problem) => TransferReadResult.Refused(TransferWording.Package(Problem));

        // the encrypted header's values (§Header fields), all checked before Unlock may derive a key: exportedBy, the KDF's name
        // and iteration count, the cipher's name, the salt (16 bytes), the nonce (12), the tag (16) and the ciphertext, each
        // strict base64. The result is kept hidden in the locked package
        private static TransferLockedPackage Locked(Envelope E)
        {
            byte[]? Salt = TransferFormat.FromBase64Strict(E.Salt, TransferFormat.SaltBytes);
            byte[]? Nonce = TransferFormat.FromBase64Strict(E.Nonce, TransferFormat.NonceBytes);
            byte[]? Tag = TransferFormat.FromBase64Strict(E.Tag, TransferFormat.TagBytes);
            byte[]? Ciphertext = TransferFormat.FromBase64Strict(E.Ciphertext, TransferFormat.MaxPackageBytes);
            bool Passed = TransferFormat.IsExportedBy(E.ExportedBy)
                && string.Equals(E.KdfName, TransferFormat.KdfName, StringComparison.Ordinal)
                && string.Equals(E.Iterations, TransferFormat.Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                && string.Equals(E.CipherName, TransferFormat.CipherName, StringComparison.Ordinal)
                && Salt != null && Salt.Length == TransferFormat.SaltBytes
                && Nonce != null && Nonce.Length == TransferFormat.NonceBytes
                && Tag != null && Tag.Length == TransferFormat.TagBytes
                && Ciphertext != null && Ciphertext.Length > 0;
            return Passed
                ? new TransferLockedPackage(E.ExportedBy!, E.Salt!, E.Nonce!, Salt, Nonce, Ciphertext, Tag, true)
                : new TransferLockedPackage(string.Empty, string.Empty, string.Empty, null, null, null, null, false);
        }

        // whether the bytes look like a package Campertron wrote (an object whose text names the format's member): a refusal
        // before the document could be read then says it is damaged rather than that it is no package
        private static bool ClaimsToBePackage(ReadOnlySpan<byte> Bytes)
        {
            ReadOnlySpan<byte> Body = Bytes.StartsWith(Utf8Bom) ? Bytes.Slice(3) : Bytes;
            int First = 0;
            while (First < Body.Length && (Body[First] == (byte)' ' || Body[First] == (byte)'\t' || Body[First] == (byte)'\r' || Body[First] == (byte)'\n'))
            {
                First++;
            }
            return First < Body.Length && Body[First] == (byte)'{' && Body.IndexOf(VersionMemberBytes) >= 0;
        }

        // ---- the format version, before the strict read ----

        private enum VersionScan
        {
            Version1,
            Newer,
            None,
            Invalid,
            Syntax
        }

        // the root object's campertronSettingsPackage, whatever else the document holds (a newer format may hold other
        // members): a whole number of 1 or more, once. The scan also reads the whole document, so text after it is refused
        private static VersionScan ScanVersion(ReadOnlySpan<byte> Bytes)
        {
            var R = new Utf8JsonReader(Bytes, Scanning);
            VersionScan? Found = null;
            try
            {
                if (!R.Read() || R.TokenType != JsonTokenType.StartObject)
                {
                    R.Skip();
                    while (R.Read())
                    {
                    }
                    return VersionScan.None;
                }
                while (R.Read() && R.TokenType == JsonTokenType.PropertyName)
                {
                    bool IsVersion = R.ValueTextEquals(TransferFormat.VersionMember);
                    R.Read();
                    if (IsVersion)
                    {
                        Found = Found != null ? VersionScan.Invalid : ClassifyVersion(ref R);
                    }
                    R.Skip();
                }
                while (R.Read())
                {
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
            {
                return VersionScan.Syntax;
            }
            return Found ?? VersionScan.None;
        }

        private static VersionScan ClassifyVersion(ref Utf8JsonReader R)
        {
            if (R.TokenType != JsonTokenType.Number || R.HasValueSequence)
            {
                return VersionScan.Invalid;
            }
            ReadOnlySpan<byte> Digits = R.ValueSpan;
            foreach (byte B in Digits)
            {
                if (B < (byte)'0' || B > (byte)'9')
                {
                    return VersionScan.Invalid;
                }
            }
            if (Digits.Length == 1 && Digits[0] == (byte)'0')
            {
                return VersionScan.Invalid;
            }
            return Digits.Length == 1 && Digits[0] == (byte)'1' ? VersionScan.Version1 : VersionScan.Newer;
        }

        // ---- format 1's strict document ----

        private const string Version = TransferFormat.VersionMember;
        private const string Protection = TransferFormat.ProtectionMember;
        private const string ExportedBy = TransferFormat.ExportedByMember;
        private const string Payload = TransferFormat.PayloadMember;
        private const string Kdf = TransferFormat.KdfMember;
        private const string Cipher = TransferFormat.CipherMember;
        private const string Ciphertext = TransferFormat.CiphertextMember;
        private const string Tag = TransferFormat.TagMember;
        private static readonly string[] PlainMembers = { Version, Protection, ExportedBy, Payload };
        private static readonly string[] EncryptedMembers = { Version, Protection, ExportedBy, Kdf, Cipher, Ciphertext, Tag };
        private static readonly string[] EncryptedOnlyMembers = { Kdf, Cipher, Ciphertext, Tag };
        private static readonly string[] PlainOnlyMembers = { Payload };

        // the members a document held, as read: types checked, values not yet
        private sealed class Envelope
        {
            internal readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
            internal string? Protection;
            internal string? ExportedBy;
            internal List<TransferPackage.RawEntry>? Entries;
            internal string? Manifest;
            internal string? KdfName;
            internal string? Iterations;
            internal string? Salt;
            internal string? CipherName;
            internal string? Nonce;
            internal string? Ciphertext;
            internal string? Tag;
            internal bool Has(string[] Members) => Members.All(Seen.Contains);
            internal bool HasAny(string[] Members) => Members.Any(Seen.Contains);
        }

        // the document's members, each once, of the type its form gives it; an unknown member refuses the package
        private static Envelope ReadEnvelope(ReadOnlySpan<byte> Bytes)
        {
            var R = new Utf8JsonReader(Bytes, Strict);
            var E = new Envelope();
            Next(ref R, JsonTokenType.StartObject);
            while (Member(ref R, E.Seen) is string Name)
            {
                switch (Name)
                {
                    case Version:
                        Next(ref R, JsonTokenType.Number);
                        break;
                    case Protection:
                        E.Protection = String(ref R);
                        break;
                    case ExportedBy:
                        E.ExportedBy = String(ref R);
                        break;
                    case Payload:
                        (E.Entries, E.Manifest) = ReadPayload(ref R);
                        break;
                    case Kdf:
                        ReadKdf(ref R, E);
                        break;
                    case Cipher:
                        ReadCipher(ref R, E);
                        break;
                    case Ciphertext:
                        E.Ciphertext = String(ref R);
                        break;
                    case Tag:
                        E.Tag = String(ref R);
                        break;
                    default:
                        throw new Refusal(TransferProblem.Damaged);
                }
            }
            if (R.Read())
            {
                throw new Refusal(TransferProblem.Damaged);
            }
            return E;
        }

        private static void ReadKdf(ref Utf8JsonReader R, Envelope E)
        {
            Next(ref R, JsonTokenType.StartObject);
            var Seen = new HashSet<string>(StringComparer.Ordinal);
            while (Member(ref R, Seen) is string Name)
            {
                switch (Name)
                {
                    case TransferFormat.NameMember:
                        E.KdfName = String(ref R);
                        break;
                    case TransferFormat.IterationsMember:
                        Next(ref R, JsonTokenType.Number);
                        // the number exactly as written: format 1 has one value for it, compared as text (no conversion)
                        E.Iterations = R.HasValueSequence ? null : TransferFormat.StrictUtf8.GetString(R.ValueSpan);
                        break;
                    case TransferFormat.SaltMember:
                        E.Salt = String(ref R);
                        break;
                    default:
                        throw new Refusal(TransferProblem.Damaged);
                }
            }
            Require(Seen, TransferFormat.NameMember, TransferFormat.IterationsMember, TransferFormat.SaltMember);
        }

        private static void ReadCipher(ref Utf8JsonReader R, Envelope E)
        {
            Next(ref R, JsonTokenType.StartObject);
            var Seen = new HashSet<string>(StringComparer.Ordinal);
            while (Member(ref R, Seen) is string Name)
            {
                switch (Name)
                {
                    case TransferFormat.NameMember:
                        E.CipherName = String(ref R);
                        break;
                    case TransferFormat.NonceMember:
                        E.Nonce = String(ref R);
                        break;
                    default:
                        throw new Refusal(TransferProblem.Damaged);
                }
            }
            Require(Seen, TransferFormat.NameMember, TransferFormat.NonceMember);
        }

        // the payload object: entries (counted while the list is read: the 257th refuses the package before any entry is
        // hashed) and manifestSha256, each once
        private static (List<TransferPackage.RawEntry> Entries, string Manifest) ReadPayload(ref Utf8JsonReader R)
        {
            Next(ref R, JsonTokenType.StartObject);
            var Seen = new HashSet<string>(StringComparer.Ordinal);
            List<TransferPackage.RawEntry>? Entries = null;
            string? Manifest = null;
            while (Member(ref R, Seen) is string Name)
            {
                switch (Name)
                {
                    case TransferFormat.EntriesMember:
                        Next(ref R, JsonTokenType.StartArray);
                        Entries = new List<TransferPackage.RawEntry>();
                        while (true)
                        {
                            if (!R.Read())
                            {
                                throw new Refusal(TransferProblem.Damaged);
                            }
                            if (R.TokenType == JsonTokenType.EndArray)
                            {
                                break;
                            }
                            if (R.TokenType != JsonTokenType.StartObject)
                            {
                                throw new Refusal(TransferProblem.Damaged);
                            }
                            if (Entries.Count == TransferFormat.MaxEntries)
                            {
                                throw new Refusal(TransferProblem.TooManyEntries);
                            }
                            Entries.Add(ReadEntry(ref R));
                        }
                        break;
                    case TransferFormat.ManifestMember:
                        Manifest = String(ref R);
                        break;
                    default:
                        throw new Refusal(TransferProblem.Damaged);
                }
            }
            Require(Seen, TransferFormat.EntriesMember, TransferFormat.ManifestMember);
            return (Entries!, Manifest!);
        }

        // one entry's four members, each once (the reader is at its StartObject)
        private static TransferPackage.RawEntry ReadEntry(ref Utf8JsonReader R)
        {
            var Seen = new HashSet<string>(StringComparer.Ordinal);
            string? Kind = null, Name_ = null, Text = null, Sha256 = null;
            while (Member(ref R, Seen) is string Name)
            {
                switch (Name)
                {
                    case TransferFormat.KindMember:
                        Kind = String(ref R);
                        break;
                    case TransferFormat.NameMember:
                        Name_ = String(ref R);
                        break;
                    case TransferFormat.TextMember:
                        Text = String(ref R);
                        break;
                    case TransferFormat.Sha256Member:
                        Sha256 = String(ref R);
                        break;
                    default:
                        throw new Refusal(TransferProblem.Damaged);
                }
            }
            Require(Seen, TransferFormat.KindMember, TransferFormat.NameMember, TransferFormat.TextMember, TransferFormat.Sha256Member);
            return new TransferPackage.RawEntry(Kind!, Name_!, Text!, Sha256!);
        }

        // the next member's name, or null at the object's end; a member named twice refuses the package
        private static string? Member(ref Utf8JsonReader R, HashSet<string> Seen)
        {
            if (!R.Read())
            {
                throw new Refusal(TransferProblem.Damaged);
            }
            if (R.TokenType == JsonTokenType.EndObject)
            {
                return null;
            }
            if (R.TokenType != JsonTokenType.PropertyName)
            {
                throw new Refusal(TransferProblem.Damaged);
            }
            string Name = R.GetString()!;
            if (!Seen.Add(Name))
            {
                throw new Refusal(TransferProblem.Damaged);
            }
            return Name;
        }

        private static void Next(ref Utf8JsonReader R, JsonTokenType Type)
        {
            if (!R.Read() || R.TokenType != Type)
            {
                throw new Refusal(TransferProblem.Damaged);
            }
        }

        private static string String(ref Utf8JsonReader R)
        {
            Next(ref R, JsonTokenType.String);
            return R.GetString()!;
        }

        private static void Require(HashSet<string> Seen, params string[] Members)
        {
            if (!Members.All(Seen.Contains))
            {
                throw new Refusal(TransferProblem.Damaged);
            }
        }
    }
}
