using System.Buffers;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CampertronLibrary.function.Transfer
{
    // the bytes of a package, or why it was not written
    internal sealed class TransferWriteResult
    {
        internal TransferWriteResult(byte[]? Bytes, TransferFailure? Failure)
        {
            this.Bytes = Bytes;
            this.Failure = Failure;
        }
        internal byte[]? Bytes { get; }
        internal TransferFailure? Failure { get; }
        internal bool Succeeded => Bytes != null;
        public override string ToString() => Succeeded ? $"TransferWriteResult: {Bytes!.Length} bytes" : $"TransferWriteResult: {Failure}";
    }

    // The writer of a package (v0.4.0-settings-transfer.md §Shared package model, §Plain envelope, §Encrypted envelope): the
    // base class library's JSON writer, no package; the members in the design's order, the entries in the package's (Create's
    // writer order), two spaces of indentation and a line feed on every platform, every character outside ASCII escaped by the
    // default encoder, so the same package always gives the same bytes but for the encrypted envelope's random salt and nonce.
    // It returns bytes and writes no file: where a package goes is its adapter's (phases 17 to 19).
    internal static class TransferPackageWriter
    {
        internal static readonly JsonWriterOptions Options = new JsonWriterOptions
        {
            Indented = true,
            IndentCharacter = ' ',
            IndentSize = 2,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.Default,
            MaxDepth = TransferFormat.MaxDepth
        };

        // the plain envelope; refused for a package with a secret (D132 point 6) and above the package limit
        internal static TransferWriteResult WritePlain(TransferPackage Package)
        {
            if (Package.HasSecret)
            {
                return new TransferWriteResult(null, TransferWording.Package(TransferProblem.SecretNotEncrypted));
            }
            var Buffer = new ArrayBufferWriter<byte>(4096);
            using (var Json = new Utf8JsonWriter(Buffer, Options))
            {
                Json.WriteStartObject();
                Json.WriteNumber(TransferFormat.VersionMember, Package.FormatVersion);
                Json.WriteString(TransferFormat.ProtectionMember, TransferFormat.ProtectionNone);
                Json.WriteString(TransferFormat.ExportedByMember, Package.ExportedBy);
                Json.WritePropertyName(TransferFormat.PayloadMember);
                WritePayload(Json, Package);
                Json.WriteEndObject();
            }
            return Bounded(Buffer.WrittenSpan.ToArray());
        }

        // The encrypted envelope, for a package with or without a secret: a new random salt and nonce, the key derived once from
        // the passphrase and used once, the complete header as the associated data. Refused where the platform has no AES-GCM,
        // for a passphrase the rules refuse, and above the package limit (checked from the payload's length before the key is
        // derived, and again for the bytes).
        internal static TransferWriteResult WriteEncrypted(TransferPackage Package, string? Passphrase)
        {
            if (!TransferCrypto.IsSupported)
            {
                return new TransferWriteResult(null, TransferWording.Package(TransferProblem.EncryptionUnavailable));
            }
            byte[]? Secret = TransferPassphrase.Bytes(Passphrase, out TransferFailure? Problem);
            if (Secret == null)
            {
                return new TransferWriteResult(null, Problem);
            }
            // sized so that it never grows (a grown buffer leaves an uncleared copy of the secrets behind): at most six bytes for
            // every character of a name or text (a \uXXXX escape) and a few hundred for each entry's members
            long Capacity = 1024 + Package.Entries.Sum(E => 6L * (E.Name.Length + E.Text.Length) + 256);
            var Plain = new ArrayBufferWriter<byte>((int)Math.Min(Capacity, 8L * TransferFormat.MaxPackageBytes));
            byte[]? Key = null;
            try
            {
                using (var Json = new Utf8JsonWriter(Plain, Options))
                {
                    WritePayload(Json, Package);
                }
                // the ciphertext's base64 alone over the limit: refused before any key is derived
                if ((long)(Plain.WrittenCount + 2) / 3 * 4 > TransferFormat.MaxPackageBytes)
                {
                    return new TransferWriteResult(null, TransferWording.Package(TransferProblem.TooLarge));
                }
                byte[] Salt = TransferCrypto.NewSalt();
                byte[] Nonce = TransferCrypto.NewNonce();
                string Salt64 = Convert.ToBase64String(Salt);
                string Nonce64 = Convert.ToBase64String(Nonce);
                Key = TransferCrypto.DeriveKey(Secret, Salt);
                byte[] Associated = TransferCrypto.AssociatedData(Package.FormatVersion, Package.ExportedBy, TransferFormat.Iterations, Salt64, Nonce64);
                (byte[] Ciphertext, byte[] Tag) = TransferCrypto.Seal(Key, Nonce, Plain.WrittenSpan, Associated);
                var Buffer = new ArrayBufferWriter<byte>(Ciphertext.Length / 3 * 4 + 1024);
                using (var Json = new Utf8JsonWriter(Buffer, Options))
                {
                    Json.WriteStartObject();
                    Json.WriteNumber(TransferFormat.VersionMember, Package.FormatVersion);
                    Json.WriteString(TransferFormat.ProtectionMember, TransferFormat.ProtectionPassphrase);
                    Json.WriteString(TransferFormat.ExportedByMember, Package.ExportedBy);
                    Json.WriteStartObject(TransferFormat.KdfMember);
                    Json.WriteString(TransferFormat.NameMember, TransferFormat.KdfName);
                    Json.WriteNumber(TransferFormat.IterationsMember, TransferFormat.Iterations);
                    Json.WriteString(TransferFormat.SaltMember, Salt64);
                    Json.WriteEndObject();
                    Json.WriteStartObject(TransferFormat.CipherMember);
                    Json.WriteString(TransferFormat.NameMember, TransferFormat.CipherName);
                    Json.WriteString(TransferFormat.NonceMember, Nonce64);
                    Json.WriteEndObject();
                    Json.WriteString(TransferFormat.CiphertextMember, Convert.ToBase64String(Ciphertext));
                    Json.WriteString(TransferFormat.TagMember, Convert.ToBase64String(Tag));
                    Json.WriteEndObject();
                }
                return Bounded(Buffer.WrittenSpan.ToArray());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(Secret);
                if (Key != null)
                {
                    CryptographicOperations.ZeroMemory(Key);
                }
                // the plaintext payload holds the secrets: cleared as far as managed memory allows
                Plain.Clear();
            }
        }

        // the payload object: the entries with their four members, then the manifest
        private static void WritePayload(Utf8JsonWriter Json, TransferPackage Package)
        {
            Json.WriteStartObject();
            Json.WriteStartArray(TransferFormat.EntriesMember);
            foreach (TransferEntry Entry in Package.Entries)
            {
                Json.WriteStartObject();
                Json.WriteString(TransferFormat.KindMember, TransferFormat.KindText(Entry.Kind));
                Json.WriteString(TransferFormat.NameMember, Entry.Name);
                Json.WriteString(TransferFormat.TextMember, Entry.Text);
                Json.WriteString(TransferFormat.Sha256Member, Entry.Sha256);
                Json.WriteEndObject();
            }
            Json.WriteEndArray();
            Json.WriteString(TransferFormat.ManifestMember, TransferFormat.ManifestSha256(Package.FormatVersion,
                Package.Entries.Select(E => (TransferFormat.KindText(E.Kind), E.Name, E.Sha256)).ToList()));
            Json.WriteEndObject();
        }

        private static TransferWriteResult Bounded(byte[] Bytes) => Bytes.Length > TransferFormat.MaxPackageBytes
            ? new TransferWriteResult(null, TransferWording.Package(TransferProblem.TooLarge))
            : new TransferWriteResult(Bytes, null);
    }
}
