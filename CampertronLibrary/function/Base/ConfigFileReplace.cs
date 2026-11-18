using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace CampertronLibrary.function.Base
{
    // Safe writes of Campertron's settings files (TD-37; decisions D26 and D40; business-rules §8 and §14).
    //
    // Replace: an existing settings file gets new content only through a uniquely named temporary file in the same
    // directory ("<name>.<32 hex digits>.tmp", which no *.yaml loader picks up), created with create-new semantics and
    // owner-only permissions, written, flushed to disk, given the file's permissions, read back and checked, then renamed
    // over the file. The file is never truncated or written in place, so a process killed at any point leaves the complete
    // old or the complete new file. Before the rename the previous bytes go to "<name>.bak" (one rolling backup, written the
    // same way), and the file is read again: if it changed since it was read, nothing is replaced. A symbolic link stays a
    // link: its final target is updated, in the target's directory. After the rename the file is read back and checked.
    //
    // Create: a new file (the samples) is written to a temporary file the same way and moved into place only if no file of
    // that name exists, so an existing file is never truncated or overwritten.
    //
    //   - Linux, macOS: the rename is rename(2); the create-only move is link(2), which fails if the name exists (on a file
    //     system without hard links .NET copies with create-new semantics instead, which an interruption can leave partial).
    //     Flush(true) is fsync (F_FULLFSYNC on macOS). The directory is not fsynced, so after a power loss the old file may
    //     reappear. The owner and any POSIX ACL or extended attribute of the old file are not carried over.
    //   - Windows: MoveFileExW, with MOVEFILE_REPLACE_EXISTING for a replacement and without it for a creation; Microsoft
    //     documents no atomicity for it. A reparse point (symbolic link, junction, cloud placeholder), a read-only file or a
    //     file with an access control list of its own (protected or with explicit entries) is refused. The replacement and
    //     the backup take the access rules their folder gives new files; they must equal the file's (its DACL, compared as
    //     SDDL) before anything is renamed, so a file moved in from a folder with other rules is refused rather than given
    //     different, possibly broader ones. Run natively by the Windows CI job (build-and-test.md §AE).
    public static class ConfigFileReplace
    {
        public const string TemporarySuffix = ".tmp";
        internal const string BackupSuffix = ".bak";

        // A replacement keeps its temporary files for the time it takes to write, flush, read back and check a settings
        // file of at most YamlDocument.MaxDocumentLength characters: well under a second. A temporary file younger than this
        // may belong to a running write (also in another process) and is left alone.
        internal static readonly TimeSpan StaleTemporaryAge = TimeSpan.FromMinutes(10);

        // the largest file read for editing: the editor's limit in UTF-16 units, at most 3 bytes each in UTF-8, plus a BOM
        internal static readonly int MaxBytes = 3 * YamlDocument.MaxDocumentLength + 3;

        private const int MaxLinkHops = 40;

        // the steps of a write, for tests (failure injection) and the interruption harness; does nothing in production
        internal static Action<string, string> Step = (Name, Path_) => { };

        // strict UTF-8: invalid bytes throw instead of becoming U+FFFD; no byte-order mark is added by the encoder
        internal static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
        private static readonly char ByteOrderMark = (char)0xFEFF;

        // the destination of a creation exists already (another process may have created it): nothing was written
        internal sealed class SettingsFileExistsException : IOException
        {
            internal SettingsFileExistsException(string FileName) : base($"{FileName} already exists and was left as it is.") { }
        }

        // A settings file as Read found it: its decoded text (a leading U+FEFF stands for a UTF-8 byte-order mark) and what
        // Replace checks again before it renames: the link chain, the final target, its bytes, their hash, length, time
        // and permissions.
        internal sealed class SettingsFile
        {
            internal string Path { get; init; } = string.Empty;
            internal string TargetPath { get; init; } = string.Empty;
            internal IReadOnlyList<(string Link, string Target)> Links { get; init; } = Array.Empty<(string, string)>();
            internal byte[] Bytes { get; init; } = Array.Empty<byte>();
            internal string Text { get; init; } = string.Empty;
            internal string Sha256 { get; init; } = string.Empty;
            internal long Length { get; init; }
            internal DateTime LastWriteTimeUtc { get; init; }
            internal UnixFileMode? Mode { get; init; }
            internal FileAttributes Attributes { get; init; }
            // Windows: the file's access rules (its DACL as SDDL); null elsewhere
            internal string? AccessRules { get; init; }
            internal string Name => System.IO.Path.GetFileName(Path);
        }

        // ---- reading ----

        // Reads a settings file for editing. Refuses (IOException, InvalidDataException) a missing file, a directory, an
        // empty or non-regular file, a dangling or circular link, a link on Windows, invalid UTF-8, UTF-16 or UTF-32 text,
        // and a file that changes while it is read. Messages name the file, never its content.
        internal static SettingsFile Read(string FilePath)
        {
            string Full = System.IO.Path.GetFullPath(FilePath);
            string Name = System.IO.Path.GetFileName(Full);
            (string Target, List<(string, string)> Links) = ResolveTarget(Full, Name);
            (long Length, DateTime Time, UnixFileMode? Mode, FileAttributes Attributes) Before = Metadata(Target, Name);
            byte[] Bytes = ReadBytes(Target, Name);
            (long Length, DateTime Time, UnixFileMode? Mode, FileAttributes Attributes) After = Metadata(Target, Name);
            if (Before != After || Bytes.LongLength != Before.Length)
            {
                throw new IOException($"{Name} changed while Campertron read it; it was left as it is.");
            }
            string? Rules = null;
            if (OperatingSystem.IsWindows())
            {
                Rules = AccessRulesOf(Target);
            }
            return new SettingsFile
            {
                Path = Full,
                TargetPath = Target,
                Links = Links,
                Bytes = Bytes,
                Text = Decode(Bytes, Name),
                Sha256 = System.Convert.ToHexString(SHA256.HashData(Bytes)),
                Length = Before.Length,
                LastWriteTimeUtc = Before.Time,
                Mode = Before.Mode,
                Attributes = Before.Attributes,
                AccessRules = Rules
            };
        }

        // the text of UTF-8 bytes, strictly; a UTF-8 byte-order mark becomes a leading U+FEFF (as YamlDocument expects it)
        internal static string Decode(byte[] Bytes, string Name)
        {
            if (Bytes.Length >= 2 && ((Bytes[0] == 0xFF && Bytes[1] == 0xFE) || (Bytes[0] == 0xFE && Bytes[1] == 0xFF)) ||
                Bytes.Length >= 4 && Bytes[0] == 0 && Bytes[1] == 0 && Bytes[2] == 0xFE && Bytes[3] == 0xFF)
            {
                throw new InvalidDataException($"{Name} is not UTF-8: it starts with a UTF-16 or UTF-32 byte-order mark. It was left as it is.");
            }
            if (Array.IndexOf(Bytes, (byte)0) >= 0)
            {
                throw new InvalidDataException($"{Name} is not UTF-8 text: it holds NUL bytes (as UTF-16 text does). It was left as it is.");
            }
            bool Bom = Bytes.AsSpan().StartsWith(Utf8Bom);
            try
            {
                string Body = StrictUtf8.GetString(Bytes, Bom ? 3 : 0, Bytes.Length - (Bom ? 3 : 0));
                return Bom ? ByteOrderMark + Body : Body;
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidDataException($"{Name} is not valid UTF-8. It was left as it is.");
            }
        }

        // the UTF-8 bytes of a text; a leading U+FEFF is written as the byte-order mark it stands for
        internal static byte[] Encode(string Text, string Name)
        {
            try
            {
                return StrictUtf8.GetBytes(Text);
            }
            catch (EncoderFallbackException)
            {
                throw new InvalidDataException($"The new content of {Name} is not valid text; {Name} was left as it is.");
            }
        }

        // Unix: follows a chain of symbolic links to its final target (every link and the text it holds are returned, so a
        // change can be noticed); refuses a cycle, a dangling link, a directory and an empty or non-regular file (a FIFO,
        // socket or device reports size 0 and is refused before it is opened, which could block). Windows: refuses any
        // reparse point.
        private static (string Target, List<(string, string)> Links) ResolveTarget(string Full, string Name)
        {
            var Links = new List<(string, string)>();
            var Seen = new HashSet<string>(StringComparer.Ordinal);
            string Current = Full;
            while (true)
            {
                if (OperatingSystem.IsWindows())
                {
                    if (File.Exists(Current) && (File.GetAttributes(Current) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException($"{Name} is a symbolic link, junction or other reparse point, which Campertron does not update on Windows; it was left as it is.");
                    }
                    break;
                }
                string? LinkTarget = new FileInfo(Current).LinkTarget;
                if (LinkTarget == null)
                {
                    break;
                }
                if (Links.Count == MaxLinkHops || !Seen.Add(Current))
                {
                    throw new IOException($"{Name} is a symbolic link that leads into a cycle (or through more than {MaxLinkHops} links); it was left as it is.");
                }
                Links.Add((Current, LinkTarget));
                Current = System.IO.Path.GetFullPath(LinkTarget, System.IO.Path.GetDirectoryName(Current)!);
            }
            if (Directory.Exists(Current))
            {
                throw new IOException($"{Name} is a directory{(Links.Count > 0 ? " (through a symbolic link)" : "")}, not a settings file; it was left as it is.");
            }
            if (!File.Exists(Current))
            {
                throw new FileNotFoundException(Links.Count > 0 ? $"{Name} is a symbolic link to a file that does not exist; it was left as it is." : $"{Name} does not exist.");
            }
            if (new FileInfo(Current).Length == 0)
            {
                throw new InvalidDataException($"{Name} is empty or is not a regular file; it was left as it is.");
            }
            return (Current, Links);
        }

        private static (long, DateTime, UnixFileMode?, FileAttributes) Metadata(string Target, string Name)
        {
            var Info = new FileInfo(Target);
            Info.Refresh();
            if (!Info.Exists)
            {
                throw new IOException($"{Name} disappeared while Campertron read it; nothing was changed.");
            }
            UnixFileMode? Mode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(Target);
            return (Info.Length, Info.LastWriteTimeUtc, Mode, Info.Attributes);
        }

        private static byte[] ReadBytes(string Target, string Name)
        {
            using var Stream = new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Stream.Length > MaxBytes)
            {
                throw new InvalidDataException($"{Name} is larger than the {MaxBytes} bytes Campertron edits; it was left as it is.");
            }
            var Bytes = new byte[Stream.Length];
            Stream.ReadExactly(Bytes);
            if (Stream.ReadByte() != -1)
            {
                throw new IOException($"{Name} grew while Campertron read it; it was left as it is.");
            }
            return Bytes;
        }

        // ---- replacing ----

        // Replaces Original's file with NewText when Verify accepts it (Verify returns null, or a reason without values,
        // and is applied to the text before writing, to the temporary file read back and to the file read back after the
        // rename). Throws (IOException, InvalidDataException, UnauthorizedAccessException) and leaves the file as it was on
        // any failure before the rename; after the rename only the final read-back can fail, which is reported.
        internal static void Replace(SettingsFile Original, string NewText, Func<string, string?> Verify)
        {
            string Name = Original.Name;
            byte[] NewBytes = Encode(NewText, Name);
            if (Decode(NewBytes, Name) != NewText)
            {
                throw new InvalidDataException($"The new content of {Name} does not survive UTF-8 encoding; {Name} was left as it is.");
            }
            Check(Verify(NewText), Name);
            CheckReplaceable(Original);
            string Directory_ = System.IO.Path.GetDirectoryName(Original.TargetPath)!;
            string TargetName = System.IO.Path.GetFileName(Original.TargetPath);
            RemoveStale(Directory_, TargetName, null);
            string Temporary = System.IO.Path.Join(Directory_, TemporaryName(TargetName));
            try
            {
                WriteNew(Temporary, NewBytes, Original.Mode, Original.Attributes);
                CheckSameAccessRules(Temporary, Original);
                Step("temporary-written", Temporary);
                ReadBack(Temporary, NewBytes, Verify, Name);
                Step("temporary-verified", Temporary);
                CheckUnchanged(Original);
                WriteBackup(Original, Directory_, TargetName);
                Step("before-precondition", Original.TargetPath);
                CheckUnchanged(Original);
                Step("before-replace", Temporary);
                File.Move(Temporary, Original.TargetPath, overwrite: true);
                Step("after-replace", Original.TargetPath);
            }
            finally
            {
                RemoveIfOurs(Temporary);
            }
            // the file as a reader sees it (through its link, if it has one)
            byte[] Written = File.ReadAllBytes(Original.Path);
            if (!Written.AsSpan().SequenceEqual(NewBytes) || Verify(Decode(Written, Name)) != null ||
                (Original.Mode != null && !OperatingSystem.IsWindows() && File.GetUnixFileMode(Original.TargetPath) != Original.Mode) ||
                (OperatingSystem.IsWindows() && AccessRulesOf(Original.TargetPath) != Original.AccessRules))
            {
                throw new IOException($"{Name} was replaced, but reading it back did not give what was written; the previous content is in {TargetName}{BackupSuffix}.");
            }
        }

        // The previous bytes go to "<name>.bak" before the file is replaced: written to a temporary file with the file's
        // permissions (so never wider), read back, and renamed over the old backup, which is never written in place
        private static void WriteBackup(SettingsFile Original, string Directory_, string TargetName)
        {
            string Backup = System.IO.Path.Join(Directory_, TargetName + BackupSuffix);
            string Temporary = System.IO.Path.Join(Directory_, TemporaryName(TargetName + BackupSuffix));
            try
            {
                WriteNew(Temporary, Original.Bytes, Original.Mode, Original.Attributes & ~FileAttributes.ReadOnly);
                CheckSameAccessRules(Temporary, Original);
                Step("backup-written", Temporary);
                ReadBack(Temporary, Original.Bytes, null, Original.Name + BackupSuffix);
                File.Move(Temporary, Backup, overwrite: true);
                Step("backup-installed", Backup);
            }
            finally
            {
                RemoveIfOurs(Temporary);
            }
        }

        // the file, its links and its content are still exactly what Read found
        private static void CheckUnchanged(SettingsFile Original)
        {
            SettingsFile Now;
            try
            {
                Now = Read(Original.Path);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                throw Changed(Original.Name);
            }
            if (Now.TargetPath != Original.TargetPath || !Now.Links.SequenceEqual(Original.Links) || Now.Sha256 != Original.Sha256 ||
                Now.Length != Original.Length || Now.LastWriteTimeUtc != Original.LastWriteTimeUtc || Now.Mode != Original.Mode ||
                Now.Attributes != Original.Attributes || Now.AccessRules != Original.AccessRules)
            {
                throw Changed(Original.Name);
            }
        }

        private static IOException Changed(string Name) =>
            new IOException($"{Name} was changed by another program while Campertron was updating it; it was left as it is and can be updated again.");

        // Windows: the replacement takes the access rules its folder gives new files, so a file with rules of its own, or
        // a read-only one, is not replaced (an ordinary file has only the rules it inherits from its folder)
        private static void CheckReplaceable(SettingsFile Original)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            if ((Original.Attributes & FileAttributes.ReadOnly) != 0)
            {
                throw new UnauthorizedAccessException($"{Original.Name} is read-only; it was left as it is.");
            }
            FileSecurity Security = new FileInfo(Original.TargetPath).GetAccessControl(AccessControlSections.Access);
            if (Security.AreAccessRulesProtected || Security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)).Count > 0)
            {
                throw new UnauthorizedAccessException($"{Original.Name} has access rules of its own, which a replacement would not keep; it was left as it is.");
            }
        }

        // Windows: a new file written for the replacement (or the backup) must have exactly the file's access rules; it
        // gets the rules its folder gives new files, which differ from the file's if the file was moved in from a folder
        // with other rules. Nothing has been renamed yet, so the file is left as it is.
        private static void CheckSameAccessRules(string Temporary, SettingsFile Original)
        {
            if (OperatingSystem.IsWindows() && AccessRulesOf(Temporary) != Original.AccessRules)
            {
                throw new UnauthorizedAccessException($"{Original.Name} has access rules that a new file in its folder would not get (was it moved there?); it was left as it is.");
            }
        }

        // the access rules of a file (its DACL, with the inheritance flags) as SDDL, for an exact comparison
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static string AccessRulesOf(string Path_) =>
            new FileInfo(Path_).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        // ---- creating ----

        // Creates FilePath with Text if no file of that name exists (Verify as for Replace). Returns false, writing nothing,
        // if the name exists, also when another process creates it while this one writes. Without Mode the new file gets
        // the permissions a new file gets in that directory (as StreamWriter gave the samples before); with a Mode (Unix
        // only; ignored on Windows, where the file takes its folder's access rules) it is written owner-only and then given
        // exactly that mode before it is moved into place (the GUI creates Email.yaml 0600, D43). There is no backup.
        internal static bool Create(string FilePath, string Text, Func<string, string?> Verify, UnixFileMode? Mode = null)
        {
            string Full = System.IO.Path.GetFullPath(FilePath);
            string Name = System.IO.Path.GetFileName(Full);
            if (Exists(Full))
            {
                return false;
            }
            byte[] Bytes = Encode(Text, Name);
            Check(Verify(Text), Name);
            string Directory_ = System.IO.Path.GetDirectoryName(Full)!;
            RemoveStale(Directory_, Name, null);
            string Temporary = System.IO.Path.Join(Directory_, TemporaryName(Name));
            try
            {
                WriteNew(Temporary, Bytes, OperatingSystem.IsWindows() ? null : Mode, null);
                Step("temporary-written", Temporary);
                ReadBack(Temporary, Bytes, Verify, Name);
                Step("before-create", Full);
                try
                {
                    File.Move(Temporary, Full, overwrite: false);
                }
                catch (IOException) when (Exists(Full))
                {
                    return false;
                }
                Step("after-create", Full);
            }
            finally
            {
                RemoveIfOurs(Temporary);
            }
            if (!File.ReadAllBytes(Full).AsSpan().SequenceEqual(Bytes))
            {
                throw new IOException($"{Name} was created, but reading it back did not give what was written.");
            }
            if (Mode != null && !OperatingSystem.IsWindows() && File.GetUnixFileMode(Full) != Mode.Value)
            {
                throw new IOException($"{Name} was created, but it does not have the permissions it was given.");
            }
            return true;
        }

        // a file, a directory or a link (also a dangling one) of that name
        private static bool Exists(string Full) => File.Exists(Full) || Directory.Exists(Full) || new FileInfo(Full).LinkTarget != null;

        // ---- shared steps ----

        // Writes Bytes to a new file (create-new: an existing name, also a link, is an error) and flushes it to disk. With a
        // Mode (a replacement) it is created owner-only, so its content is never readable by others while it is written,
        // and then given exactly that mode; on Windows Attributes (without ReadOnly) are copied. Without a Mode (a creation)
        // it gets the default permissions of a new file.
        private static void WriteNew(string Path_, byte[] Bytes, UnixFileMode? Mode, FileAttributes? Attributes)
        {
            var Options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (Mode != null && !OperatingSystem.IsWindows())
            {
                Options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using (var Stream = new FileStream(Path_, Options))
            {
                Step("temporary-writing", Path_);
                Stream.Write(Bytes);
                Stream.Flush(flushToDisk: true);
                Step("temporary-flushed", Path_);
            }
            if (Mode != null && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path_, Mode.Value);
                if (File.GetUnixFileMode(Path_) != Mode.Value)
                {
                    throw new IOException($"{System.IO.Path.GetFileName(Path_)} could not be given the permissions of the file it replaces; nothing was replaced.");
                }
            }
            if (Attributes != null && OperatingSystem.IsWindows())
            {
                FileAttributes Copy = Attributes.Value & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.NotContentIndexed);
                if (Copy != 0)
                {
                    File.SetAttributes(Path_, Copy);
                }
            }
        }

        // the temporary file holds exactly the bytes written, and their text passes Verify
        private static void ReadBack(string Path_, byte[] Bytes, Func<string, string?>? Verify, string Name)
        {
            byte[] Back = File.ReadAllBytes(Path_);
            if (!Back.AsSpan().SequenceEqual(Bytes))
            {
                throw new IOException($"The temporary copy of {Name} did not read back as written; nothing was replaced.");
            }
            if (Verify != null)
            {
                Check(Verify(Decode(Back, Name)), Name);
            }
        }

        private static void Check(string? Problem, string Name)
        {
            if (Problem != null)
            {
                throw new InvalidDataException($"The new content of {Name} did not pass its check ({Problem}); {Name} was left as it is.");
            }
        }

        // removes a temporary file this operation created; a failure is reported without hiding the original exception
        private static void RemoveIfOurs(string Temporary)
        {
            try
            {
                if (File.Exists(Temporary) && new FileInfo(Temporary).LinkTarget == null)
                {
                    File.Delete(Temporary);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"Could not remove the temporary file {Temporary}: {ex.Message}");
            }
        }

        // ---- names and stale temporary files ----

        public static string TemporaryName(string DestinationFileName) =>
            DestinationFileName + "." + Guid.NewGuid().ToString("N") + TemporarySuffix;

        // True only for "<DestinationFileName>.<32 lowercase hex digits>.tmp"
        public static bool IsTemporaryName(string DestinationFileName, string Name)
        {
            string Prefix = DestinationFileName + ".";
            if (Name.Length != Prefix.Length + 32 + TemporarySuffix.Length ||
                !Name.StartsWith(Prefix, StringComparison.Ordinal) || !Name.EndsWith(TemporarySuffix, StringComparison.Ordinal))
            {
                return false;
            }
            string Middle = Name.Substring(Prefix.Length, Name.Length - Prefix.Length - TemporarySuffix.Length);
            return Middle.Length == 32 && Middle.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }

        // Removes temporary files of DestinationFileName (and of its backup) left in Directory_ by an interrupted write:
        // only regular files (never a link or a directory) with exactly those names, last written more than
        // StaleTemporaryAge ago. Removals and failures are reported. Called at the start of a RIDB refresh.
        public static void RemoveStaleTemporaryFiles(string Directory_, string DestinationFileName)
        {
            RemoveStale(Directory_, DestinationFileName, Console.WriteLine);
        }

        // the same, with each line going to Report (the RIDB refresh service, V011-1; null: quietly)
        internal static void RemoveStaleTemporaryFiles(string Directory_, string DestinationFileName, Action<string>? Report)
        {
            RemoveStale(Directory_, DestinationFileName, Report);
        }

        // the same, quietly when Report is null (before every write, which can run while Campertron starts)
        private static void RemoveStale(string Directory_, string DestinationFileName, Action<string>? Report)
        {
            DateTime Limit = DateTime.UtcNow - StaleTemporaryAge;
            foreach (string File_ in Directory.GetFiles(Directory_))
            {
                string Name = System.IO.Path.GetFileName(File_);
                if (!IsTemporaryName(DestinationFileName, Name) && !IsTemporaryName(DestinationFileName + BackupSuffix, Name))
                {
                    continue;
                }
                try
                {
                    var Info = new FileInfo(File_);
                    if (Info.LinkTarget != null || (Info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 || Info.LastWriteTimeUtc > Limit)
                    {
                        continue;
                    }
                    File.Delete(File_);
                    Report?.Invoke($"Removed {File_}, left by an interrupted update of {DestinationFileName}");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Report?.Invoke($"Could not remove {File_}: {ex.Message}");
                }
            }
        }
    }
}
