using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.WebAccess
{
    // The web UI's password as it is kept (decisions D81 and D92, business-rules-settings §31): never the password, only
    // a PBKDF2-HMAC-SHA256 hash of it with its salt and iteration count, as one line of text:
    //
    //   $pbkdf2-sha256$i=600000$<salt, base64 without padding>$<hash, base64 without padding>
    //
    //  - 600,000 iterations (OWASP's Password Storage Cheat Sheet's number for PBKDF2-HMAC-SHA256), a 16-byte random salt
    //    for every hash (RandomNumberGenerator) and a 32-byte hash, the whole output of SHA-256. PBKDF2 because it is in
    //    the base class library (Rfc2898DeriveBytes.Pbkdf2), so no package is added.
    //  - The password is hashed as the UTF-8 bytes of its text in Unicode's NFKC form, so the same password typed on
    //    another keyboard or system gives the same bytes; nothing is trimmed.
    //  - A record is read strictly: exactly this layout, 100,000 to 10,000,000 iterations (a record that asks for fewer
    //    is too weak to trust, one that asks for more would stall the sign-in), a salt of 16 to 64 bytes, a hash of 32.
    //    Anything else is no record, and no password matches it.
    //  - A password is compared by hashing it with the record's salt and count and comparing the two hashes in constant
    //    time (CryptographicOperations.FixedTimeEquals).
    //  - No message here holds a password, a salt or a hash.
    internal static class WebPasswordHash
    {
        internal const string Scheme = "pbkdf2-sha256";
        internal const int Iterations = 600_000;
        internal const int MinIterations = 100_000;
        internal const int MaxIterations = 10_000_000;
        internal const int SaltBytes = 16;
        internal const int MaxSaltBytes = 64;
        internal const int HashBytes = 32;
        internal const int MinLength = 10;
        internal const int MaxLength = 128;
        // a record is a short line; a longer text is not one (and is never parsed)
        internal const int MaxRecordLength = 256;

        internal sealed class Record
        {
            internal int Iterations { get; init; }
            internal byte[] Salt { get; init; } = Array.Empty<byte>();
            internal byte[] Hash { get; init; } = Array.Empty<byte>();
        }

        // null when the text can be a new password, else what is wrong with it, in fixed words (never the password)
        internal static string? CheckNew(string? Password, string? Repeated)
        {
            if (string.IsNullOrEmpty(Password))
            {
                return "Enter a password.";
            }
            if (Password.Length < MinLength)
            {
                return $"The password is too short: use at least {MinLength} characters.";
            }
            if (Password.Length > MaxLength)
            {
                return $"The password is too long: use at most {MaxLength} characters.";
            }
            if (string.IsNullOrWhiteSpace(Password))
            {
                return "The password can't be only spaces.";
            }
            if (Bytes(Password) == null)
            {
                return "The password has characters that can't be used; choose another.";
            }
            if (!string.Equals(Password, Repeated, StringComparison.Ordinal))
            {
                return "The two passwords are not the same; enter the new password twice.";
            }
            return null;
        }

        // the password's bytes: UTF-8 of its NFKC form; null for text that is no valid Unicode (a lone surrogate)
        private static byte[]? Bytes(string Password)
        {
            try
            {
                return new UTF8Encoding(false, true).GetBytes(Password.Normalize(NormalizationForm.FormKC));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is EncoderFallbackException)
            {
                return null;
            }
        }

        // a new record for a password (a new random salt every time), with the current iteration count; Count is for tests,
        // which can't spend 600,000 iterations on every case
        internal static string Create(string Password, int Count = Iterations)
        {
            byte[] Text = Bytes(Password) ?? throw new ArgumentException("The password is not valid Unicode text.");
            byte[] Salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] Hash = Rfc2898DeriveBytes.Pbkdf2(Text, Salt, Count, HashAlgorithmName.SHA256, HashBytes);
            try
            {
                return "$" + Scheme + "$i=" + Count.ToString(CultureInfo.InvariantCulture) + "$" + Encode(Salt) + "$" + Encode(Hash);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(Text);
                CryptographicOperations.ZeroMemory(Hash);
            }
        }

        // the record of a line, or null for anything that is not exactly one
        internal static Record? Parse(string? Line)
        {
            if (string.IsNullOrEmpty(Line) || Line.Length > MaxRecordLength)
            {
                return null;
            }
            string[] Parts = Line.Split('$');
            if (Parts.Length != 5 || Parts[0].Length != 0 || !string.Equals(Parts[1], Scheme, StringComparison.Ordinal) || !Parts[2].StartsWith("i=", StringComparison.Ordinal))
            {
                return null;
            }
            string Count = Parts[2].Substring(2);
            if (Count.Length == 0 || Count.Length > 8 || Count[0] == '0' || !Count.All(C => C >= '0' && C <= '9')
                || !int.TryParse(Count, NumberStyles.None, CultureInfo.InvariantCulture, out int Iterations_) || Iterations_ < MinIterations || Iterations_ > MaxIterations)
            {
                return null;
            }
            byte[]? Salt = Decode(Parts[3]);
            byte[]? Hash = Decode(Parts[4]);
            if (Salt == null || Hash == null || Salt.Length < SaltBytes || Salt.Length > MaxSaltBytes || Hash.Length != HashBytes)
            {
                return null;
            }
            return new Record { Iterations = Iterations_, Salt = Salt, Hash = Hash };
        }

        // whether the password is the record's, compared in constant time; false for text that can't be a password
        internal static bool Verify(string? Password, Record Record)
        {
            if (string.IsNullOrEmpty(Password) || Password.Length > MaxLength)
            {
                return false;
            }
            byte[]? Text = Bytes(Password);
            if (Text == null)
            {
                return false;
            }
            byte[] Hash = Rfc2898DeriveBytes.Pbkdf2(Text, Record.Salt, Record.Iterations, HashAlgorithmName.SHA256, HashBytes);
            try
            {
                return CryptographicOperations.FixedTimeEquals(Hash, Record.Hash);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(Text);
                CryptographicOperations.ZeroMemory(Hash);
            }
        }

        private static string Encode(byte[] Bytes_) => System.Convert.ToBase64String(Bytes_).TrimEnd('=');

        // base64 without padding, strictly: only its alphabet, and it must encode back to the same text
        private static byte[]? Decode(string Text)
        {
            if (Text.Length == 0 || Text.Length > 128 || Text.Any(C => !((C >= 'A' && C <= 'Z') || (C >= 'a' && C <= 'z') || (C >= '0' && C <= '9') || C == '+' || C == '/')))
            {
                return null;
            }
            try
            {
                byte[] Bytes_ = System.Convert.FromBase64String(Text + new string('=', (4 - Text.Length % 4) % 4));
                return string.Equals(Encode(Bytes_), Text, StringComparison.Ordinal) ? Bytes_ : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    // what the settings folder holds as the web UI's password
    internal enum WebPasswordState
    {
        // no file: no password was set yet (the setup code sets the first one)
        Missing,
        // a record that can be compared with
        Usable,
        // a file that is not a record, a folder, a link, or a file that can't be read or can't be kept from other users:
        // no password matches, and nothing is written over it
        Unusable
    }

    // The file that keeps the web UI's password hash (D81, D92; business-rules-settings §31): Web.password in the settings
    // folder, one record (WebPasswordHash) and a line break. An instance is one settings folder's; it keeps no state.
    //  - It is written only whole: to a temporary file beside it, created for its owner only (0600 on Unix; on Windows the
    //    settings folder's access rights), flushed to disk and read back, then moved into place. The first password is
    //    created only if no file of that name exists (two setups at once: one wins, the other is told); a change replaces
    //    the file in one rename, so a stop at any moment leaves the old or the new password, never half a file. There is
    //    no backup: an old hash is of no use to its owner.
    //  - It is read strictly: a regular file (never a link or a folder) of at most MaxFileBytes, UTF-8, one record. On
    //    Unix a file other users can read or write is first made owner-only; if that fails it is unusable, because the
    //    hash would let them guess the password offline.
    //  - Nothing here names the folder, and no message holds the file's content.
    internal sealed class WebPasswordFile
    {
        internal const string FileName = "Web.password";
        internal const int MaxFileBytes = 1024;
        private static readonly UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        internal WebPasswordFile(string ConfigPath)
        {
            FilePath = Path.Join(ConfigPath, FileName);
        }

        internal string FilePath { get; }

        // whether a file, a folder or a link (also to nothing) has the name: then no first password can be created
        internal bool Exists() => File.Exists(FilePath) || Directory.Exists(FilePath) || new FileInfo(FilePath).LinkTarget != null;

        // what identifies the file as it is now, for "is this still the password the session signed in with": its length
        // and time of writing; null without a regular file. Never its content
        internal (long Length, DateTime WrittenUtc)? Fingerprint()
        {
            try
            {
                var Info = new FileInfo(FilePath);
                return Info.Exists && Info.LinkTarget == null ? (Info.Length, Info.LastWriteTimeUtc) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        // the record of the file, with its state
        internal WebPasswordState Read(out WebPasswordHash.Record? Record)
        {
            Record = null;
            try
            {
                var Info = new FileInfo(FilePath);
                if (Info.LinkTarget != null || Directory.Exists(FilePath))
                {
                    return WebPasswordState.Unusable;
                }
                if (!Info.Exists)
                {
                    return WebPasswordState.Missing;
                }
                if (Info.Length == 0 || Info.Length > MaxFileBytes)
                {
                    return WebPasswordState.Unusable;
                }
                if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(FilePath) & ~OwnerOnly) != 0)
                {
                    File.SetUnixFileMode(FilePath, OwnerOnly);
                    if ((File.GetUnixFileMode(FilePath) & ~OwnerOnly) != 0)
                    {
                        return WebPasswordState.Unusable;
                    }
                }
                byte[] Bytes = File.ReadAllBytes(FilePath);
                if (Bytes.Length == 0 || Bytes.Length > MaxFileBytes)
                {
                    return WebPasswordState.Unusable;
                }
                string Text;
                try
                {
                    Text = new UTF8Encoding(false, true).GetString(Bytes);
                }
                catch (DecoderFallbackException)
                {
                    return WebPasswordState.Unusable;
                }
                if (Text.EndsWith("\r\n", StringComparison.Ordinal))
                {
                    Text = Text.Substring(0, Text.Length - 2);
                }
                else if (Text.EndsWith('\n'))
                {
                    Text = Text.Substring(0, Text.Length - 1);
                }
                Record = WebPasswordHash.Parse(Text);
                return Record == null ? WebPasswordState.Unusable : WebPasswordState.Usable;
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return WebPasswordState.Missing;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return WebPasswordState.Unusable;
            }
        }

        internal WebPasswordState State() => Read(out _);

        // whether the password is the one the file keeps; false without a usable record
        internal bool Verify(string? Password) => Read(out WebPasswordHash.Record? Record) == WebPasswordState.Usable && WebPasswordHash.Verify(Password, Record!);

        // The first password: creates the file with the password's record if nothing has its name. False, with nothing
        // written, when the name exists (also when another setup created it meanwhile). Throws IOException or
        // UnauthorizedAccessException, naming the file but never its content, when the folder can't take it
        internal bool Create(string Password, int Count = WebPasswordHash.Iterations)
        {
            string Line = WebPasswordHash.Create(Password, Count) + "\n";
            return ConfigFileReplace.Create(FilePath, Line, Written => string.Equals(Written, Line, StringComparison.Ordinal) ? null : "it is not what was written", OwnerOnly);
        }

        // A changed password: replaces a regular file (never a link or a folder, which throw) with the new record, in one
        // rename. Throws IOException or UnauthorizedAccessException when it can't; the old file then stays as it was
        internal void Replace(string Password, int Count = WebPasswordHash.Iterations)
        {
            var Info = new FileInfo(FilePath);
            if (Info.LinkTarget != null || Directory.Exists(FilePath))
            {
                throw new IOException($"{FileName} is not a regular file; it was left as it is.");
            }
            byte[] Bytes = Encoding.UTF8.GetBytes(WebPasswordHash.Create(Password, Count) + "\n");
            string Folder = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
            ConfigFileReplace.RemoveStaleTemporaryFiles(Folder, FileName, null);
            string Temporary = Path.Join(Folder, ConfigFileReplace.TemporaryName(FileName));
            try
            {
                var Options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows())
                {
                    Options.UnixCreateMode = OwnerOnly;
                }
                using (var Stream = new FileStream(Temporary, Options))
                {
                    Stream.Write(Bytes);
                    Stream.Flush(flushToDisk: true);
                }
                if (!File.ReadAllBytes(Temporary).AsSpan().SequenceEqual(Bytes))
                {
                    throw new IOException($"The temporary copy of {FileName} did not read back as written; the password was not changed.");
                }
                if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(Temporary) != OwnerOnly)
                {
                    throw new IOException($"The temporary copy of {FileName} could not be kept from other users; the password was not changed.");
                }
                File.Move(Temporary, FilePath, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(Temporary))
                    {
                        File.Delete(Temporary);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    //the temporary file holds only a hash and is removed as stale by a later write
                }
            }
        }
    }
}
