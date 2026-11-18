using System.IO.Compression;

namespace CampertronLibrary.function.Base
{
    public static class Compression
    {
        //the RIDB export is extracted into a folder of Campertron's own in LocalApplicationData, next to CampertronConfig
        //and CampertronCache. Only that folder is cleared here and deleted after the import (PopulateDbFromRIDBFiles); the
        //generic "Extract" used before could be another program's folder, and it was deleted recursively (TD-48)
        public const string ExtractFolderName = "CampertronExtract";
        public static String Unzip(String Source)
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            String ExtractPath = System.IO.Path.Join(path, ExtractFolderName);
            Extract(Source, ExtractPath, CancellationToken.None, null);
            if (File.Exists(Source))
            {
                File.Delete(Source);
            }
            return ExtractPath;
        }

        // Extracts the archive Source into ExtractPath (Campertron's own extract folder): an existing folder of that name is
        // deleted first (a link with that name is removed as a link, never followed), then the entries are written one at a
        // time. The token is checked before each entry and while an entry is copied (V011-1: a refresh is canceled between
        // and within entries); Progress gets the entries done and their number. An entry that would land outside
        // ExtractPath (an absolute or "../" name) is refused with InvalidDataException before it is written, as is an
        // archive that is not a ZIP. Source is only read.
        internal static void Extract(string Source, string ExtractPath, CancellationToken Token, Action<int, int>? Progress)
        {
            if (Directory.Exists(ExtractPath))
            {
                Directory.Delete(ExtractPath, true);
            }
            using ZipArchive Archive = ZipFile.OpenRead(Source);
            IReadOnlyCollection<ZipArchiveEntry> Entries = Archive.Entries;
            Directory.CreateDirectory(ExtractPath);
            string Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ExtractPath)) + Path.DirectorySeparatorChar;
            StringComparison Comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int Done = 0;
            Progress?.Invoke(0, Entries.Count);
            byte[] Buffer = new byte[81920];
            foreach (ZipArchiveEntry Entry in Entries)
            {
                Token.ThrowIfCancellationRequested();
                string Target = Path.GetFullPath(Path.Join(Root, Entry.FullName));
                if (!Target.StartsWith(Root, Comparison) || Target.Length == Root.Length)
                {
                    throw new InvalidDataException("The archive has an entry outside the folder it is extracted into.");
                }
                if (Entry.FullName.EndsWith('/') || Entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(Target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
                    using Stream Input = Entry.Open();
                    using FileStream Output = new FileStream(Target, FileMode.Create, FileAccess.Write, FileShare.None);
                    int Count;
                    while ((Count = Input.Read(Buffer, 0, Buffer.Length)) > 0)
                    {
                        Token.ThrowIfCancellationRequested();
                        Output.Write(Buffer, 0, Count);
                    }
                }
                Done++;
                Progress?.Invoke(Done, Entries.Count);
            }
        }
    }
}
