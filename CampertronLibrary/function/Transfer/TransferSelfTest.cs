using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CampertronLibrary.function.Transfer
{
    // The settings-transfer package's compatibility self-test (v0.4.0's phase 16a; v0.4.0-settings-transfer.md §Phase 16a
    // boundary): whether this platform's .NET cryptography does what the encrypted envelope needs, for a test or, later, a
    // smoke host to run. Everything is in memory: it takes no folder, opens no file and writes nothing. Its passphrases and
    // secrets are random for every run (never a fixed value), the salts and nonces are the production writer's own fresh ones,
    // and its lines hold the checks' names, the running platform and ok or failed only, never a value.
    // Where AES-GCM is not supported, the cryptographic checks fail and the check that encryption is refused there passes:
    // the self-test then fails, as the release target would (fail closed). A line says what ran where it ran; it is never a
    // claim about another platform.
    internal static class TransferSelfTest
    {
        internal sealed record Check(string Name, bool Passed);

        internal sealed class Report
        {
            internal Report(string Runtime, IReadOnlyList<Check> Checks)
            {
                this.Runtime = Runtime;
                this.Checks = Checks;
            }
            // the running process's runtime identifier and architecture, as .NET reports them now
            internal string Runtime { get; }
            internal IReadOnlyList<Check> Checks { get; }
            internal bool Passed => Checks.Count > 0 && Checks.All(C => C.Passed);
            internal IEnumerable<string> Lines =>
                new[] { "transfer-selftest runtime=" + Runtime }
                .Concat(Checks.Select(C => "transfer-selftest " + C.Name + "=" + (C.Passed ? "ok" : "failed")))
                .Append("transfer-selftest result=" + (Passed ? "ok" : "failed"));
        }

        internal static Report Run()
        {
            string Runtime = RuntimeInformation.RuntimeIdentifier + " " + RuntimeInformation.ProcessArchitecture;
            var Checks = new List<Check>();
            void Add(string Name, Func<bool> Test)
            {
                bool Passed;
                try
                {
                    Passed = Test();
                }
                catch (Exception ex) when (ex is CryptographicException || ex is PlatformNotSupportedException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    Passed = false;
                }
                Checks.Add(new Check(Name, Passed));
            }
            Add("aes-gcm-supported", () => TransferCrypto.IsSupported);
            if (!TransferCrypto.IsSupported)
            {
                // fail closed: nothing is encrypted without AES-GCM, and no other cipher is tried
                Add("encryption-refused-without-aes-gcm", () => TransferPackageWriter.WriteEncrypted(Sample(Random(24), out _), Random(20)).Failure?.Problem == TransferProblem.EncryptionUnavailable);
                return new Report(Runtime, Checks);
            }
            string Passphrase = Random(20);
            string OtherPassphrase = Random(20);
            byte[] PassphraseBytes = Encoding.UTF8.GetBytes(Passphrase);
            byte[] Key = Array.Empty<byte>();
            try
            {
                // PBKDF2-HMAC-SHA256 with format 1's count gives a 32-byte key
                Add("pbkdf2-key-length", () =>
                {
                    Key = TransferCrypto.DeriveKey(PassphraseBytes, TransferCrypto.NewSalt());
                    return Key.Length == TransferFormat.KeyBytes;
                });
                // AES-GCM with that key, a fresh nonce and associated data: the round trip, and each changed part refused
                byte[] Nonce = TransferCrypto.NewNonce();
                byte[] Plain = RandomNumberGenerator.GetBytes(64);
                byte[] Associated = TransferCrypto.AssociatedData(TransferFormat.Version, "0.0.0", TransferFormat.Iterations, Convert.ToBase64String(TransferCrypto.NewSalt()), Convert.ToBase64String(Nonce));
                (byte[] Ciphertext, byte[] Tag) Sealed = (Array.Empty<byte>(), Array.Empty<byte>());
                Add("aes-gcm-round-trip", () =>
                {
                    Sealed = TransferCrypto.Seal(Key, Nonce, Plain, Associated);
                    byte[]? Opened = TransferCrypto.Open(Key, Nonce, Sealed.Ciphertext, Sealed.Tag, Associated);
                    return Opened != null && Opened.AsSpan().SequenceEqual(Plain) && !Sealed.Ciphertext.AsSpan().SequenceEqual(Plain);
                });
                Add("associated-data-enforced", () => TransferCrypto.Open(Key, Nonce, Sealed.Ciphertext, Sealed.Tag, Flipped(Associated)) == null);
                Add("ciphertext-tamper-refused", () => TransferCrypto.Open(Key, Nonce, Flipped(Sealed.Ciphertext), Sealed.Tag, Associated) == null);
                Add("tag-tamper-refused", () => TransferCrypto.Open(Key, Nonce, Sealed.Ciphertext, Flipped(Sealed.Tag), Associated) == null);
                // a package with all three secrets through the production writer and reader
                TransferPackage Package = Sample(Random(24), out string[] Secrets);
                byte[]? First = null;
                byte[]? Second = null;
                Add("secret-package-round-trip", () =>
                {
                    First = TransferPackageWriter.WriteEncrypted(Package, Passphrase).Bytes;
                    TransferReadResult Opened = First == null ? TransferReadResult.Refused(TransferWording.Package(TransferProblem.Damaged)) : TransferPackageReader.Open(First);
                    TransferPackage? Read = Opened.Locked == null ? null : TransferPackageReader.Unlock(Opened.Locked, Passphrase).Package;
                    return Read != null && Read.Encrypted && Read.Entries.Select(E => (E.Kind, E.Name, E.Text)).SequenceEqual(Package.Entries.Select(E => (E.Kind, E.Name, E.Text)));
                });
                Add("secrets-absent-from-envelope", () => First != null && Secrets.Append(Passphrase).All(Value => !Holds(First, Value)));
                Add("wrong-passphrase-refused", () => First != null && Unlocked(First, OtherPassphrase) == TransferProblem.WrongPassphraseOrChanged);
                Add("header-tamper-refused", () => First != null && Unlocked(ChangedExportedBy(First), Passphrase) == TransferProblem.WrongPassphraseOrChanged);
                Add("fresh-salt-and-nonce", () =>
                {
                    Second = TransferPackageWriter.WriteEncrypted(Package, Passphrase).Bytes;
                    return First != null && Second != null
                        && Member(First, TransferFormat.SaltMember) is string S1 && Member(Second, TransferFormat.SaltMember) is string S2 && S1 != S2
                        && Member(First, TransferFormat.NonceMember) is string N1 && Member(Second, TransferFormat.NonceMember) is string N2 && N1 != N2;
                });
            }
            finally
            {
                CryptographicOperations.ZeroMemory(PassphraseBytes);
                CryptographicOperations.ZeroMemory(Key);
            }
            return new Report(Runtime, Checks);
        }

        // random letters and digits (never shown)
        private static string Random(int Length) => RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", Length);

        // a small package with the three settings kinds and the three secrets
        private static TransferPackage Sample(string Seed, out string[] Secrets)
        {
            Secrets = new[] { "p-" + Seed, "t-" + Seed, "campertron-" + Seed.ToLowerInvariant() };
            return TransferPackage.Create("0.0.0", new[]
            {
                (TransferEntryKind.General, "General.yaml", "outputTo: Console\n"),
                (TransferEntryKind.Email, "Email.yaml", "smtpServer: mail.example.com\n"),
                (TransferEntryKind.Ntfy, "Ntfy.yaml", "enabled: false\n"),
                (TransferEntryKind.SecretSmtpPassword, "smtpPassword", Secrets[0]),
                (TransferEntryKind.SecretNtfyToken, "ntfyToken", Secrets[1]),
                (TransferEntryKind.SecretNtfyTopic, "ntfyTopic", Secrets[2])
            }, out _)!;
        }

        private static byte[] Flipped(byte[] Bytes)
        {
            byte[] Copy = (byte[])Bytes.Clone();
            Copy[Copy.Length / 2] ^= 0x01;
            return Copy;
        }

        private static TransferProblem? Unlocked(byte[] Bytes, string Passphrase)
        {
            TransferReadResult Opened = TransferPackageReader.Open(Bytes);
            return Opened.Locked == null ? Opened.Failure?.Problem : TransferPackageReader.Unlock(Opened.Locked, Passphrase).Failure?.Problem;
        }

        // the package with its exportedBy changed to another valid value: only the tag can refuse it
        private static byte[] ChangedExportedBy(byte[] Bytes)
        {
            string Text = Encoding.UTF8.GetString(Bytes);
            return Encoding.UTF8.GetBytes(Text.Replace("\"exportedBy\": \"0.0.0\"", "\"exportedBy\": \"0.0.1\"", StringComparison.Ordinal));
        }

        // a string member's value anywhere in the package's text (salt, nonce)
        private static string? Member(byte[] Bytes, string Name)
        {
            string Text = Encoding.UTF8.GetString(Bytes);
            string Start = "\"" + Name + "\": \"";
            int At = Text.IndexOf(Start, StringComparison.Ordinal);
            if (At < 0)
            {
                return null;
            }
            int From = At + Start.Length;
            int End = Text.IndexOf('"', From);
            return End < 0 ? null : Text.Substring(From, End - From);
        }

        // whether the bytes hold the value: as text, as JSON's escaped text, or as base64 at any of the three alignments (the
        // part of each encoding that depends on the value alone: without the first group where other bytes come before it,
        // and without the last group, which may hold padding)
        internal static bool Holds(byte[] Bytes, string Value)
        {
            string Text = Encoding.UTF8.GetString(Bytes);
            if (Text.Contains(Value, StringComparison.Ordinal) || Text.Contains(System.Text.Json.JsonEncodedText.Encode(Value).ToString(), StringComparison.Ordinal))
            {
                return true;
            }
            byte[] Raw = Encoding.UTF8.GetBytes(Value);
            for (int Shift = 0; Shift < 3; Shift++)
            {
                string Encoded = Convert.ToBase64String(new byte[Shift].Concat(Raw).ToArray());
                int From = Shift == 0 ? 0 : 4;
                int Length = Encoded.Length - 4 - From;
                if (Length >= 8 && Text.Contains(Encoded.Substring(From, Length), StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
