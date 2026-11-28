using System.Globalization;
using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.Transfer.Apply
{
    // The apply's backup set (D132, D134; v0.4.0-settings-transfer.md §Backup set): before the first write to a settings,
    // search or secret file, a folder Campertron.transfer.backup in the settings folder holds every file the apply will
    // replace, as "<name>.bak", and Contents.txt, which lists by name only what the apply replaces and what it creates, with
    // the time and the Campertron version (no value, no path).
    //  - No loader reads it: the search files are the folder's top-level *.yaml files only, and nothing else lists it. It is
    //    never transferred, logged or shown as a path, and no start or program reads it: it is for the user when a rollback
    //    fails or a process dies.
    //  - Owner-only: the folder 0700 and its files 0600, or on Windows the folder's protected one entry for the current user,
    //    which its files inherit (TransferProtection); where that can't be established nothing is written.
    //  - No secret: Email.yaml and Ntfy.yaml are kept with smtpPassword: '' and topic: '' in place of a value (the caller's
    //    secret-free texts, D111 and D112); never a secret file's contents.
    //  - Bounded: one set, the last apply's. A new set is written under a temporary name, read back, and only then takes the old
    //    set's place, before the first settings write; the old set, Campertron's own files only, is then removed.
    internal static class TransferBackupSet
    {
        internal const string FolderName = "Campertron.transfer.backup";
        internal const string ContentsName = "Contents.txt";
        internal const string FileSuffix = ".bak";
        private const string NewSuffix = ".new";
        private const string OldSuffix = ".old";

        // whether a name is one of a set's own files: Contents.txt or "<settings or search file's name>.bak"
        internal static bool IsSetFileName(string Name)
        {
            if (string.Equals(Name, ContentsName, StringComparison.Ordinal))
            {
                return true;
            }
            if (!Name.EndsWith(FileSuffix, StringComparison.Ordinal))
            {
                return false;
            }
            string Of = Name.Substring(0, Name.Length - FileSuffix.Length);
            return Of == ConfigFiles.GeneralFileName || Of == ConfigFiles.EmailFileName || Of == ConfigFiles.NtfyFileName
                || (SearchFileName.Check(Of, out string Checked) == null && string.Equals(Checked, Of, StringComparison.Ordinal));
        }

        // a folder name a set's preparation or replacement uses for a moment: "<FolderName>.<32 hex>.new" or ".old"
        internal static bool IsTransientName(string Name)
        {
            foreach (string Suffix in new[] { NewSuffix, OldSuffix })
            {
                string Prefix = FolderName + ".";
                if (Name.Length == Prefix.Length + 32 + Suffix.Length && Name.StartsWith(Prefix, StringComparison.Ordinal) && Name.EndsWith(Suffix, StringComparison.Ordinal)
                    && Name.Substring(Prefix.Length, 32).All(C => (C >= '0' && C <= '9') || (C >= 'a' && C <= 'f')))
                {
                    return true;
                }
            }
            return false;
        }

        // Contents.txt: names, the time and the version only
        internal static string Contents(IReadOnlyList<string> Replaced, IReadOnlyList<string> Created, string Version, DateTime NowUtc)
        {
            var Text = new StringBuilder();
            Text.Append("Campertron settings transfer backup\n");
            Text.Append("Written by Campertron ").Append(Version).Append(" at ").Append(NowUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Append(", before an import was applied to this settings folder. Nothing reads this folder.\n");
            Text.Append("Its files hold the earlier settings without their plain-text password and topic ('' in their place): enter them again if you restore a file from here by hand.\n");
            Text.Append("Replaced by the import (the earlier version of each is here as <name>.bak):\n");
            foreach (string Name in Replaced)
            {
                Text.Append("  ").Append(Name).Append('\n');
            }
            if (Replaced.Count == 0)
            {
                Text.Append("  (none)\n");
            }
            Text.Append("Created by the import (they did not exist before, so they are not here):\n");
            foreach (string Name in Created)
            {
                Text.Append("  ").Append(Name).Append('\n');
            }
            if (Created.Count == 0)
            {
                Text.Append("  (none)\n");
            }
            return Text.ToString();
        }

        // Writes the new set (Files: each replaced file's name and its secret-free bytes) under a temporary name, reads it back,
        // and puts it in place of the old set. Throws, leaving the old set as it was and no new folder behind, on any failure
        // before the set is in place. Step is the apply's failure-injection seam
        internal static void Install(string ConfigPath, IReadOnlyList<(string Name, byte[] Bytes)> Files, string ContentsText, Action<string, string?> Step)
        {
            string Final = Path.Join(ConfigPath, FolderName);
            RemoveTransients(ConfigPath);
            // the old set must be Campertron's own: a folder, no link, of its own files only
            if (TransferProtection.Exists(Final) && !IsOwnSet(Final))
            {
                throw new IOException("The backup folder holds something Campertron did not write.");
            }
            string New = Path.Join(ConfigPath, FolderName + "." + Guid.NewGuid().ToString("N") + NewSuffix);
            try
            {
                Step("backup-folder", New);
                TransferProtection.CreateOwnerOnlyFolder(New);
                if (!TransferProtection.IsOwnerOnlyFolder(New))
                {
                    throw new IOException("The backup folder could not be made owner-only.");
                }
                var All = Files.Select(F => (Name: F.Name + FileSuffix, F.Bytes)).Append((Name: ContentsName, Bytes: ConfigFileReplace.StrictUtf8.GetBytes(ContentsText))).ToList();
                foreach ((string Name, byte[] Bytes) in All)
                {
                    string Target = Path.Join(New, Name);
                    Step("backup-file:" + Name, Target);
                    TransferProtection.CreateInheritingFile(Target, Bytes);
                    if (!TransferProtection.IsInheritingOwnerOnlyFile(Target) || !File.ReadAllBytes(Target).AsSpan().SequenceEqual(Bytes))
                    {
                        throw new IOException("A backup file did not read back owner-only and as written.");
                    }
                }
                Step("backup-install", Final);
                string? Old = null;
                if (TransferProtection.Exists(Final))
                {
                    Old = Path.Join(ConfigPath, FolderName + "." + Guid.NewGuid().ToString("N") + OldSuffix);
                    Directory.Move(Final, Old);
                }
                try
                {
                    Directory.Move(New, Final);
                }
                catch (Exception ex) when (TransferProtection.IsFileSystemFailure(ex))
                {
                    if (Old != null)
                    {
                        Directory.Move(Old, Final);
                    }
                    throw;
                }
                if (!TransferProtection.IsOwnerOnlyFolder(Final))
                {
                    throw new IOException("The backup folder did not read back owner-only.");
                }
                // the previous set, now replaced: Campertron's own files only, then its folder (left for the next apply when
                // that is not possible: it holds no secret)
                if (Old != null)
                {
                    RemoveOwnFolder(Old);
                }
            }
            finally
            {
                if (TransferProtection.Exists(New))
                {
                    RemoveOwnFolder(New);
                }
            }
        }

        // a folder (no link) of a set's own files only (regular files, no link, with a set's names)
        private static bool IsOwnSet(string Folder)
        {
            var Info = new DirectoryInfo(Folder);
            if (!Info.Exists || Info.LinkTarget != null || (Info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
            foreach (FileSystemInfo Entry in Info.EnumerateFileSystemInfos())
            {
                if (Entry is not FileInfo File_ || File_.LinkTarget != null || (File_.Attributes & FileAttributes.ReparsePoint) != 0 || !IsSetFileName(File_.Name))
                {
                    return false;
                }
            }
            return true;
        }

        // removes a set's folder when it holds only a set's own files; anything else is left as it is
        private static void RemoveOwnFolder(string Folder)
        {
            try
            {
                if (!IsOwnSet(Folder))
                {
                    return;
                }
                foreach (FileInfo File_ in new DirectoryInfo(Folder).EnumerateFiles())
                {
                    File_.Delete();
                }
                Directory.Delete(Folder, recursive: false);
            }
            catch (Exception ex) when (TransferProtection.IsFileSystemFailure(ex))
            {
                // left in place: it holds no secret, and the next apply removes it
            }
        }

        // a set's transient folders an interrupted apply left (under the apply lock no other apply runs)
        private static void RemoveTransients(string ConfigPath)
        {
            foreach (string Entry in Directory.EnumerateDirectories(ConfigPath))
            {
                if (IsTransientName(Path.GetFileName(Entry)))
                {
                    RemoveOwnFolder(Entry);
                }
            }
        }
    }
}
