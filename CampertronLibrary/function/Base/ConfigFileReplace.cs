namespace CampertronLibrary.function.Base
{
    // Replaces a small configuration file so that a process killed at any point leaves either the complete old file
    // or the complete new file, never an empty or partial one (TD-37; used for the RIDB refresh timestamp in
    // General.yaml, business-rules §8). The new content goes to a uniquely named temporary file in the same
    // directory ("<name>.<32 hex digits>.tmp", which no *.yaml loader picks up), is flushed to disk, closed and
    // validated, and only then moved over the destination with File.Move(overwrite: true). The destination is never
    // truncated or deleted first.
    //   - Linux, macOS: .NET 10 implements that move with rename(2). POSIX requires the destination name to refer to
    //     the old or the new file throughout and to be unaffected if rename fails (except EIO). Flush(true) is fsync
    //     (F_FULLFSYNC on macOS). The directory is not fsynced, so after a power loss the old file may reappear.
    //   - Windows: MoveFileExW(MOVEFILE_COPY_ALLOWED | MOVEFILE_REPLACE_EXISTING), without MOVEFILE_WRITE_THROUGH.
    //     Microsoft documents no atomicity for it; within one directory it is a rename. Not tested on Windows.
    public static class ConfigFileReplace
    {
        public const string TemporarySuffix = ".tmp";

        // WriteContents writes the complete new document; IsValid receives the text read back from the closed
        // temporary file. ReplaceDestination moves the temporary file over the destination (File.Move with
        // overwrite; replaceable only to test a failing replacement). Any failure throws and leaves the destination
        // as it was; the temporary file is removed on a best-effort basis without hiding the original exception.
        public static void Replace(string DestinationPath, Action<TextWriter> WriteContents, Func<string, bool> IsValid,
            Action<string, string>? ReplaceDestination = null)
        {
            string Directory_ = Path.GetDirectoryName(Path.GetFullPath(DestinationPath)) ?? ".";
            string TemporaryPath = Path.Join(Directory_, TemporaryName(Path.GetFileName(DestinationPath)));
            try
            {
                using (var Stream = new FileStream(TemporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var Writer = new StreamWriter(Stream))
                {
                    WriteContents(Writer);
                    Writer.Flush();
                    Stream.Flush(flushToDisk: true);
                }
                if (!IsValid(File.ReadAllText(TemporaryPath)))
                {
                    throw new InvalidDataException($"the new content of {DestinationPath} did not validate; {DestinationPath} was not changed");
                }
                (ReplaceDestination ?? ((From, To) => File.Move(From, To, overwrite: true)))(TemporaryPath, DestinationPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(TemporaryPath))
                    {
                        File.Delete(TemporaryPath);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.WriteLine($"Could not remove the temporary file {TemporaryPath}: {ex.Message}");
                }
            }
        }

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

        // Removes temporary files of DestinationFileName left in Directory_ by an interrupted Replace. The caller must
        // make sure no Replace of that file is running (the RIDB refresh holds its refresh lock). Failures are reported.
        public static void RemoveStaleTemporaryFiles(string Directory_, string DestinationFileName)
        {
            foreach (string File_ in Directory.GetFiles(Directory_))
            {
                if (!IsTemporaryName(DestinationFileName, Path.GetFileName(File_)))
                {
                    continue;
                }
                try
                {
                    File.Delete(File_);
                    Console.WriteLine($"Removed {File_}, left by an interrupted update of {DestinationFileName}");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.WriteLine($"Could not remove {File_}: {ex.Message}");
                }
            }
        }
    }
}
