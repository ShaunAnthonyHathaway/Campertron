using System.Text;

namespace CampertronLibrary.function.Base
{
    // why a secret file can't be used (business-rules §27); each secret's own source maps it to its words
    internal enum SecretFileProblem
    {
        PathEmpty,
        PathNotAbsolute,
        Missing,
        IsFolder,
        NotReadable,
        Empty,
        TooLarge,
        HasByteOrderMark,
        NotUtf8,
        HasNul,
        OnlyLineBreak,
        HasLineBreak
    }

    // The one reader of a file that holds a secret (decision D66; the SMTP password's smtpPasswordFile and, since D67,
    // ntfy's tokenFile): the path must be fully qualified (never resolved against the working directory or another
    // folder); it is followed through links to a regular file of 1 to 4,096 bytes. A folder, a missing file, a link to
    // nothing and a file that reports no length (also a FIFO or a device, which are never opened: reading one could block)
    // are problems. Only that file is opened, read-only and shared; its folder is never listed, and nothing is written,
    // copied, moved, deleted or chmodded. The secret is the file's whole text, strict UTF-8, with at most one line break
    // at its end removed (LF, or CR LF): nothing else is trimmed. A byte-order mark, a NUL (UTF-16 text has them), bytes
    // that are not UTF-8, only a line break, or a line break anywhere else are problems. No problem holds the path or
    // the text.
    internal static class SecretFile
    {
        // a secret file is a few bytes; anything larger is the wrong file (and is never read whole)
        internal const int MaxFileBytes = 4096;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        // null and the secret's text, or the problem (Text null)
        internal static SecretFileProblem? Read(string? FilePath, out string? Text)
        {
            Text = null;
            //the file's metadata first, without opening it (since D75 shared with the GUI's check, SecretFileCheck.Locate)
            SecretFileProblem? Located = SecretFileCheck.Locate(FilePath, out _);
            if (Located != null)
            {
                return Located;
            }
            byte[] Buffer = new byte[MaxFileBytes + 1];
            int Count = 0;
            try
            {
                using (var Stream = new FileStream(FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.None))
                {
                    int Read;
                    while (Count < Buffer.Length && (Read = Stream.Read(Buffer, Count, Buffer.Length - Count)) > 0)
                    {
                        Count += Read;
                    }
                }
                if (Count == 0)
                {
                    return SecretFileProblem.Empty;
                }
                if (Count > MaxFileBytes)
                {
                    return SecretFileProblem.TooLarge;
                }
                return FromBytes(Buffer.AsSpan(0, Count), out Text);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return SecretFileProblem.Missing;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                //the exception's message is left out: it names the path
                return SecretFileProblem.NotReadable;
            }
            finally
            {
                Array.Clear(Buffer);
            }
        }

        // the secret of a file's bytes: its whole text, with at most one line break at its end removed
        internal static SecretFileProblem? FromBytes(ReadOnlySpan<byte> Bytes, out string? Text)
        {
            Text = null;
            if (Bytes.Length == 0)
            {
                return SecretFileProblem.Empty;
            }
            if (Bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            {
                return SecretFileProblem.HasByteOrderMark;
            }
            if (Bytes.IndexOf((byte)0) >= 0)
            {
                return SecretFileProblem.HasNul;
            }
            string Decoded;
            try
            {
                Decoded = StrictUtf8.GetString(Bytes);
            }
            catch (DecoderFallbackException)
            {
                return SecretFileProblem.NotUtf8;
            }
            if (Decoded.EndsWith('\n'))
            {
                Decoded = Decoded.EndsWith("\r\n", StringComparison.Ordinal) ? Decoded.Substring(0, Decoded.Length - 2) : Decoded.Substring(0, Decoded.Length - 1);
                if (Decoded.Length == 0)
                {
                    return SecretFileProblem.OnlyLineBreak;
                }
            }
            if (Decoded.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                return SecretFileProblem.HasLineBreak;
            }
            Text = Decoded;
            return null;
        }
    }
}
