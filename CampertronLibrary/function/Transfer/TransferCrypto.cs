using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CampertronLibrary.function.Transfer
{
    // The encrypted envelope's cryptography (D132 point 6; v0.4.0-settings-transfer.md §Integrity and authenticated
    // encryption), .NET's own only, no package:
    //  - the key: PBKDF2-HMAC-SHA256 (Rfc2898DeriveBytes.Pbkdf2, as WebPasswordHash uses it) over the passphrase's bytes and
    //    the header's 16-byte salt, exactly 600,000 iterations, 32 bytes of output, used as the AES-256 key;
    //  - the cipher: AES-GCM (AesGcm) with the header's 12-byte nonce and a 16-byte tag;
    //  - the associated data: the complete header as fixed lines of the values read (never the file's bytes), so any changed
    //    header value fails the tag;
    //  - a salt and a nonce of new random bytes (RandomNumberGenerator) for every package; a key is derived for one package
    //    and encrypts once, so a key and nonce pair is never used twice;
    //  - a failed decryption is one answer (null), whichever part was wrong;
    //  - where the platform has no AES-GCM (AesGcm.IsSupported), nothing is encrypted or decrypted: there is never another
    //    cipher and never a fallback.
    // The callers clear the key and the passphrase's bytes after use (CryptographicOperations.ZeroMemory), as far as managed
    // memory allows; nothing here claims more.
    internal static class TransferCrypto
    {
        internal static bool IsSupported => Availability();

        // whether this platform has AES-GCM: .NET's own answer in production; the tests answer no to prove that every path then
        // refuses (fail closed)
        internal static Func<bool> Availability = () => AesGcm.IsSupported;

        // called once at every key derivation, before it runs (the tests count derivations: a refused header derives none);
        // does nothing in production
        internal static Action Derived = () => { };

        internal static byte[] NewSalt() => RandomNumberGenerator.GetBytes(TransferFormat.SaltBytes);

        internal static byte[] NewNonce() => RandomNumberGenerator.GetBytes(TransferFormat.NonceBytes);

        // the AES-256 key of a passphrase's bytes and a salt, with format 1's fixed iteration count
        internal static byte[] DeriveKey(byte[] Passphrase, byte[] Salt)
        {
            if (Salt.Length != TransferFormat.SaltBytes)
            {
                throw new ArgumentException("The salt is not 16 bytes.", nameof(Salt));
            }
            Derived();
            return Rfc2898DeriveBytes.Pbkdf2(Passphrase, Salt, TransferFormat.Iterations, HashAlgorithmName.SHA256, TransferFormat.KeyBytes);
        }

        // the associated data: the UTF-8 bytes of these lines, each ended by a line feed (§Integrity and authenticated
        // encryption): the label, the format version, "passphrase", exportedBy, the KDF's name, the iteration count, the salt
        // as base64, the cipher's name and the nonce as base64
        internal static byte[] AssociatedData(int FormatVersion, string ExportedBy, int Iterations, string Salt, string Nonce)
        {
            var Lines = new StringBuilder();
            foreach (string Line in new[]
            {
                TransferFormat.AssociatedDataLabel,
                FormatVersion.ToString(CultureInfo.InvariantCulture),
                TransferFormat.ProtectionPassphrase,
                ExportedBy,
                TransferFormat.KdfName,
                Iterations.ToString(CultureInfo.InvariantCulture),
                Salt,
                TransferFormat.CipherName,
                Nonce
            })
            {
                Lines.Append(Line).Append('\n');
            }
            return TransferFormat.StrictUtf8.GetBytes(Lines.ToString());
        }

        // encrypts once with a key used for nothing else
        internal static (byte[] Ciphertext, byte[] Tag) Seal(byte[] Key, byte[] Nonce, ReadOnlySpan<byte> Plaintext, byte[] AssociatedData)
        {
            byte[] Ciphertext = new byte[Plaintext.Length];
            byte[] Tag = new byte[TransferFormat.TagBytes];
            using (var Cipher = new AesGcm(Key, TransferFormat.TagBytes))
            {
                Cipher.Encrypt(Nonce, Plaintext, Ciphertext, Tag, AssociatedData);
            }
            return (Ciphertext, Tag);
        }

        // the plaintext, or null when the key, the nonce, the ciphertext, the tag or the associated data is not the one sealed
        // (one answer for all of them)
        internal static byte[]? Open(byte[] Key, byte[] Nonce, byte[] Ciphertext, byte[] Tag, byte[] AssociatedData)
        {
            byte[] Plaintext = new byte[Ciphertext.Length];
            try
            {
                using (var Cipher = new AesGcm(Key, TransferFormat.TagBytes))
                {
                    Cipher.Decrypt(Nonce, Ciphertext, Tag, Plaintext, AssociatedData);
                }
                return Plaintext;
            }
            catch (CryptographicException)
            {
                // AuthenticationTagMismatchException is one; the plaintext buffer was cleared by Decrypt
                CryptographicOperations.ZeroMemory(Plaintext);
                return null;
            }
        }
    }
}
