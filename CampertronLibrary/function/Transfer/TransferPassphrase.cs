using System.Text;

namespace CampertronLibrary.function.Transfer
{
    // The passphrase of an encrypted package (D132 point 6; v0.4.0-settings-transfer.md §Passphrase rules): the rules of the
    // web UI's password (WebPasswordHash) where they fit, with the Unicode NFKC form authoritative, as the maintainer's
    // approval of phase 16a requires (2026-11-28):
    //  - its bytes are the UTF-8 of its NFKC form, so the same passphrase typed on another keyboard or system gives the same
    //    key; nothing is trimmed;
    //  - that form has 10 to 128 characters (counted as .NET counts a string's length, as WebPasswordHash counts them), is not
    //    only spaces and is valid Unicode;
    //  - a typed text longer than MaxInputLength is refused as too long before it is normalized (NFKC makes a text at most
    //    about four times shorter, so no passphrase of 128 characters is refused by it), which bounds the work;
    //  - it is chosen by the user, never generated, kept, written, logged, echoed or shown here; no sentence holds it.
    // Asking for it twice at export and refusing two that differ is the adapters' (phases 17 to 19), not the package's.
    internal static class TransferPassphrase
    {
        internal const int MinLength = 10;
        internal const int MaxLength = 128;
        internal const int MaxInputLength = 1024;

        // null when the passphrase can be used, else what is wrong with it, in fixed words (never the passphrase)
        internal static TransferFailure? Check(string? Passphrase) => Bytes(Passphrase, out TransferFailure? Problem) is byte[] Valid ? Cleared(Valid) : Problem;

        // the passphrase's bytes (the caller clears them after use), or null and its problem
        internal static byte[]? Bytes(string? Passphrase, out TransferFailure? Problem)
        {
            Problem = null;
            if (string.IsNullOrEmpty(Passphrase))
            {
                Problem = TransferWording.Passphrase(TransferProblem.PassphraseEmpty);
                return null;
            }
            if (Passphrase.Length > MaxInputLength)
            {
                Problem = TransferWording.Passphrase(TransferProblem.PassphraseTooLong);
                return null;
            }
            string Normalized;
            try
            {
                Normalized = Passphrase.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                Problem = TransferWording.Passphrase(TransferProblem.PassphraseInvalid);
                return null;
            }
            if (Normalized.Length < MinLength)
            {
                Problem = TransferWording.Passphrase(TransferProblem.PassphraseTooShort);
                return null;
            }
            if (Normalized.Length > MaxLength)
            {
                Problem = TransferWording.Passphrase(TransferProblem.PassphraseTooLong);
                return null;
            }
            if (string.IsNullOrWhiteSpace(Normalized))
            {
                Problem = TransferWording.Passphrase(TransferProblem.PassphraseOnlySpaces);
                return null;
            }
            try
            {
                return TransferFormat.StrictUtf8.GetBytes(Normalized);
            }
            catch (EncoderFallbackException)
            {
                Problem = TransferWording.Passphrase(TransferProblem.PassphraseInvalid);
                return null;
            }
        }

        private static TransferFailure? Cleared(byte[] Bytes_)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(Bytes_);
            return null;
        }
    }
}
