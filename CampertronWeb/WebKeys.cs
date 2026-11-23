using System.Xml.Linq;
using CampertronLibrary.function.Base;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace CampertronWeb
{
    // The keys ASP.NET Core's Data Protection uses for the web UI's session cookie and its forms' antiforgery tokens
    // (decisions D81 and D92, business-rules-settings §31): in the folder Web.keys of the settings folder, never in
    // ~/.aspnet (the framework's default, outside Campertron's folders and lost with a container) and never in the
    // program's own folder.
    //  - The folder is for its owner only (0700 on Unix; on Windows it has the settings folder's access rights), and so is
    //    every key file (0600). An existing folder that others can enter is made owner-only first; a folder that can't be
    //    made so, a link, or a folder that can't be written is refused: the web UI then does not start (WebStartProblem),
    //    and never falls back to keys in memory or to a fixed key.
    //  - A key file is written whole: to a temporary file created for its owner only, flushed, then moved into place
    //    without replacing anything. Only regular files named *.xml directly in the folder are read.
    //  - The keys are not encrypted at rest: on Linux and macOS the framework has no encryptor without a certificate, and
    //    one in the same folder would protect nothing. Who can read the settings folder can read them, as they can read
    //    Email.yaml.
    internal sealed class WebKeyRepository : IXmlRepository
    {
        internal const string FolderName = "Web.keys";
        private static readonly UnixFileMode FolderOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        private static readonly UnixFileMode FileOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        internal WebKeyRepository(string SettingsFolder)
        {
            Folder = Path.Join(SettingsFolder, FolderName);
        }

        internal string Folder { get; }

        // Makes the folder ready: created owner-only if it is missing, made owner-only if it is not, and written to once.
        // Null when it can keep the keys, else what is wrong, in fixed words (no path)
        internal string? Prepare()
        {
            try
            {
                var Info = new DirectoryInfo(Folder);
                if (Info.LinkTarget != null || File.Exists(Folder))
                {
                    return FolderName + " in the settings folder is a link or a file, not a folder.";
                }
                if (!Info.Exists)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Directory.CreateDirectory(Folder);
                    }
                    else
                    {
                        Directory.CreateDirectory(Folder, FolderOnly);
                    }
                }
                if (!OperatingSystem.IsWindows())
                {
                    if (File.GetUnixFileMode(Folder) != FolderOnly)
                    {
                        File.SetUnixFileMode(Folder, FolderOnly);
                    }
                    if (File.GetUnixFileMode(Folder) != FolderOnly)
                    {
                        return FolderName + " in the settings folder can't be kept from other users (its permissions can't be set to its owner only).";
                    }
                }
                //one write, so a folder that can't take a key is found now and not at the first sign-in
                string Probe = Path.Join(Folder, ConfigFileReplace.TemporaryName("probe"));
                using (new FileStream(Probe, Options()))
                {
                }
                File.Delete(Probe);
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return FolderName + " can't be created or written in the settings folder.";
            }
        }

        private static FileStreamOptions Options()
        {
            var Options_ = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                Options_.UnixCreateMode = FileOnly;
            }
            return Options_;
        }

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            var Found = new List<XElement>();
            foreach (string KeyFile in Directory.GetFiles(Folder, "*.xml", SearchOption.TopDirectoryOnly).OrderBy(Name => Name, StringComparer.Ordinal))
            {
                var Info = new FileInfo(KeyFile);
                if (Info.LinkTarget != null || (Info.Attributes & FileAttributes.Directory) != 0 || Info.Length > 64 * 1024)
                {
                    continue;
                }
                using FileStream Stream = new FileStream(KeyFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                Found.Add(XElement.Load(Stream));
            }
            return Found;
        }

        public void StoreElement(XElement Element, string FriendlyName)
        {
            //the framework names a key "key-<guid>"; anything else gets a name of this repository's own
            string Name = !string.IsNullOrEmpty(FriendlyName) && FriendlyName.Length <= 64 && FriendlyName.All(C => char.IsAsciiLetterOrDigit(C) || C == '-')
                ? FriendlyName : "key-" + Guid.NewGuid().ToString("D");
            string Final = Path.Join(Folder, Name + ".xml");
            string Temporary = Path.Join(Folder, ConfigFileReplace.TemporaryName(Name + ".xml"));
            try
            {
                using (var Stream = new FileStream(Temporary, Options()))
                {
                    Element.Save(Stream);
                    Stream.Flush(flushToDisk: true);
                }
                File.Move(Temporary, Final, overwrite: false);
            }
            finally
            {
                if (File.Exists(Temporary))
                {
                    File.Delete(Temporary);
                }
            }
        }
    }
}
