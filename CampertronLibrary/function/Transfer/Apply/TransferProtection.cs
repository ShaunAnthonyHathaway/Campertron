using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.Transfer.Apply
{
    // Owner-only files and folders of Campertron's own (D132, D134; v0.4.0-settings-transfer.md §Unix permissions, §Binding
    // Windows owner-only ACL rule): an imported secret's new file and the backup set's folder and files.
    //  - Linux, macOS: a file is created with mode 0600 at its creation (never wider and narrowed afterwards), then given
    //    exactly 0600 so the umask can't leave it without the owner's write; a folder 0700 likewise. Each must read back so,
    //    as a regular file or folder and no link, before it is used.
    //  - Windows: a new secret file and the backup folder are created with their access control already in place (never with
    //    the folder's inherited rules and changed afterwards): a protected list (inheritance disabled) of exactly one entry
    //    that allows full control to the current user, and that user as the owner; no entry for Users, Authenticated Users,
    //    Everyone, the system account, the Administrators group or any other identity. Read back so before use. The backup
    //    set's files take their rules from that folder only. Email.yaml, Ntfy.yaml, General.yaml and the search files never
    //    get rules of their own (ConfigFileReplace refuses to replace such a file); nothing here touches them.
    //  - The topic's rule (§Unix permissions, §Binding Windows owner-only ACL rule): the resulting Ntfy.yaml may be read by
    //    nobody but its owner on Linux and macOS (no group or other permission); on Windows its inherited rules may allow
    //    nobody but the current user, the system account and the Administrators group.
    // An existing file's mode or access rules are never changed. Messages are fixed text without a path.
    internal static class TransferProtection
    {
        internal const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        internal const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        private const UnixFileMode GroupOrOthers = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        // the system account and the Administrators group, which read every file of a Windows computer in any case
        [SupportedOSPlatform("windows")]
        private static SecurityIdentifier LocalSystem => new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        [SupportedOSPlatform("windows")]
        private static SecurityIdentifier Administrators => new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        // an inheritable entry for the creator of a new file names its owner: the current user, who creates it
        [SupportedOSPlatform("windows")]
        private static SecurityIdentifier CreatorOwner => new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null);

        [SupportedOSPlatform("windows")]
        private static SecurityIdentifier CurrentUser()
        {
            using WindowsIdentity Identity = WindowsIdentity.GetCurrent();
            return Identity.User ?? throw new IOException("The current Windows user has no security identifier.");
        }

        // ---- creating ----

        // A new file with these bytes (create-new: an existing name, also a link, fails), owner-only from its creation, flushed
        // to disk; the caller verifies it (IsOwnerOnlyFile) before it uses the file
        internal static void CreateOwnerOnlyFile(string FilePath, ReadOnlySpan<byte> Bytes)
        {
            if (OperatingSystem.IsWindows())
            {
                FileSecurity Security = WindowsOwnerOnlyFile();
                using FileStream Stream = new FileInfo(FilePath).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, Security);
                Stream.Write(Bytes);
                Stream.Flush(flushToDisk: true);
                return;
            }
            var Options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, UnixCreateMode = OwnerOnlyFile };
            using (var Stream = new FileStream(FilePath, Options))
            {
                Stream.Write(Bytes);
                Stream.Flush(flushToDisk: true);
            }
            // only the owner's bits can be added here: the umask never widens a mode
            File.SetUnixFileMode(FilePath, OwnerOnlyFile);
        }

        // A new file in the backup set's folder: on Windows it takes the folder's one rule; on Linux and macOS 0600 as above
        internal static void CreateInheritingFile(string FilePath, ReadOnlySpan<byte> Bytes)
        {
            if (OperatingSystem.IsWindows())
            {
                using var Stream = new FileStream(FilePath, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None });
                Stream.Write(Bytes);
                Stream.Flush(flushToDisk: true);
                return;
            }
            CreateOwnerOnlyFile(FilePath, Bytes);
        }

        // A new folder, owner-only from its creation; an existing name fails. The caller verifies it (IsOwnerOnlyFolder)
        internal static void CreateOwnerOnlyFolder(string FolderPath)
        {
            if (Exists(FolderPath))
            {
                throw new IOException("The backup set's new folder exists already; nothing was written.");
            }
            if (OperatingSystem.IsWindows())
            {
                new DirectoryInfo(FolderPath).Create(WindowsOwnerOnlyFolder());
                return;
            }
            Directory.CreateDirectory(FolderPath, OwnerOnlyFolder);
            File.SetUnixFileMode(FolderPath, OwnerOnlyFolder);
        }

        [SupportedOSPlatform("windows")]
        private static FileSecurity WindowsOwnerOnlyFile()
        {
            SecurityIdentifier User = CurrentUser();
            var Security = new FileSecurity();
            Security.SetOwner(User);
            Security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            Security.AddAccessRule(new FileSystemAccessRule(User, FileSystemRights.FullControl, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            // NEGATIVE CONTROL W1 (never for merge): a broad principal allowed on a new secret file
            Security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            return Security;
        }

        [SupportedOSPlatform("windows")]
        private static DirectorySecurity WindowsOwnerOnlyFolder()
        {
            SecurityIdentifier User = CurrentUser();
            var Security = new DirectorySecurity();
            Security.SetOwner(User);
            Security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            Security.AddAccessRule(new FileSystemAccessRule(User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            // NEGATIVE CONTROL W1 (never for merge): a broad principal allowed on the backup set's folder
            Security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            return Security;
        }

        // ---- verifying ----

        // a regular file, no link or other reparse point, that only its owner may use: exactly 0600, or on Windows the protected
        // list of the one entry for the current user, who owns it
        internal static bool IsOwnerOnlyFile(string FilePath)
        {
            try
            {
                var Info = new FileInfo(FilePath);
                if (!Info.Exists || Info.LinkTarget != null || (Info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                {
                    return false;
                }
                return OperatingSystem.IsWindows() ? WindowsIsOwnerOnly(Info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner), false)
                    : File.GetUnixFileMode(FilePath) == OwnerOnlyFile;
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                return false;
            }
        }

        // the backup set's folder: a folder, no link, exactly 0700, or on Windows the protected one entry (inherited by its
        // files and subfolders) for the current user, who owns it
        internal static bool IsOwnerOnlyFolder(string FolderPath)
        {
            try
            {
                var Info = new DirectoryInfo(FolderPath);
                if (!Info.Exists || Info.LinkTarget != null || (Info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
                return OperatingSystem.IsWindows() ? WindowsIsOwnerOnly(Info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner), true)
                    : File.GetUnixFileMode(FolderPath) == OwnerOnlyFolder;
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                return false;
            }
        }

        // a file of the backup set: regular, no link, 0600; on Windows exactly the one entry for the current user, inherited
        // from the set's folder
        internal static bool IsInheritingOwnerOnlyFile(string FilePath)
        {
            if (!OperatingSystem.IsWindows())
            {
                return IsOwnerOnlyFile(FilePath);
            }
            try
            {
                var Info = new FileInfo(FilePath);
                if (!Info.Exists || (Info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                {
                    return false;
                }
                AuthorizationRuleCollection Rules = Info.GetAccessControl(AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier));
                return Rules.Cast<FileSystemAccessRule>().FirstOrDefault(R => R.IdentityReference.Equals(CurrentUser())) is FileSystemAccessRule Rule && Rule.AccessControlType == AccessControlType.Allow
                    && Rule.IdentityReference.Equals(CurrentUser()) && (Rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl;
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                return false;
            }
        }

        [SupportedOSPlatform("windows")]
        private static bool WindowsIsOwnerOnly(FileSystemSecurity Security, bool Folder)
        {
            SecurityIdentifier User = CurrentUser();
            if (!Security.AreAccessRulesProtected || Security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier Owner || !Owner.Equals(User))
            {
                return false;
            }
            AuthorizationRuleCollection Rules = Security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
            if (Rules.Cast<FileSystemAccessRule>().FirstOrDefault(R => R.IdentityReference.Equals(User)) is not FileSystemAccessRule Rule)
            {
                return false;
            }
            InheritanceFlags Expected = Folder ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
            return Rule.AccessControlType == AccessControlType.Allow && !Rule.IsInherited && Rule.IdentityReference.Equals(User)
                && (Rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl && Rule.InheritanceFlags == Expected
                && Rule.PropagationFlags == PropagationFlags.None;
        }

        // ---- the topic's rule ----

        // Whether the topic may be written into Ntfy.yaml: an existing Ntfy.yaml (Mode: its mode as ConfigFileReplace read it,
        // the link's target's) keeps its permissions, so it must already let nobody but its owner read it; a new one is created
        // 0600. On Windows the file's rules, or a new file's rules from the folder, may allow nobody but the current user, the
        // system account and the Administrators group. Unknown is a refusal
        internal static bool TopicAllowed(string ConfigPath, bool NtfyExists, UnixFileMode? Mode)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return NtfyExists ? WindowsFileAllowsOnlyOwnerAndSystem(Path.Join(ConfigPath, ConfigFiles.NtfyFileName)) : WindowsNewFileAllowsOnlyOwnerAndSystem(ConfigPath);
                }
                return !NtfyExists || (Mode is UnixFileMode Existing && (Existing & GroupOrOthers) == 0);
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                return false;
            }
        }

        // the permissions the topic's rule judges, for the preview's revision: the existing file's mode or rules, or on Windows
        // the folder's rules a new file would inherit (rules are no secret: identities and rights only)
        internal static string TopicFacts(string ConfigPath, bool NtfyExists, UnixFileMode? Mode)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return NtfyExists
                        ? "file " + new FileInfo(Path.Join(ConfigPath, ConfigFiles.NtfyFileName)).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access)
                        : "folder " + new DirectoryInfo(ConfigPath).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
                }
                return NtfyExists ? "mode " + (Mode?.ToString() ?? "unknown") : "new";
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                return "unknown";
            }
        }

        [SupportedOSPlatform("windows")]
        private static bool WindowsFileAllowsOnlyOwnerAndSystem(string FilePath)
        {
            AuthorizationRuleCollection Rules = new FileInfo(FilePath).GetAccessControl(AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier));
            SecurityIdentifier User = CurrentUser();
            return Rules.Cast<FileSystemAccessRule>().Where(R => R.AccessControlType == AccessControlType.Allow)
                .All(R => R.IdentityReference is SecurityIdentifier Sid && (Sid.Equals(User) || Sid.Equals(LocalSystem) || Sid.Equals(Administrators)));
        }

        [SupportedOSPlatform("windows")]
        private static bool WindowsNewFileAllowsOnlyOwnerAndSystem(string FolderPath)
        {
            AuthorizationRuleCollection Rules = new DirectoryInfo(FolderPath).GetAccessControl(AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier));
            SecurityIdentifier User = CurrentUser();
            // the rules a new file inherits: those that apply to files (ObjectInherit)
            return Rules.Cast<FileSystemAccessRule>().Where(R => R.AccessControlType == AccessControlType.Allow && (R.InheritanceFlags & InheritanceFlags.ObjectInherit) != 0)
                .All(R => R.IdentityReference is SecurityIdentifier Sid && (Sid.Equals(User) || Sid.Equals(LocalSystem) || Sid.Equals(Administrators) || Sid.Equals(CreatorOwner)));
        }

        // ---- facts ----

        // a file's metadata, never its content: for an earlier secret file of Campertron's whose state the cleanup compares
        internal static string MetadataFacts(string FilePath)
        {
            try
            {
                var Info = new FileInfo(FilePath);
                if (Directory.Exists(FilePath))
                {
                    return "folder";
                }
                if (!Info.Exists)
                {
                    return Info.LinkTarget != null ? "link" : "absent";
                }
                string Protection = OperatingSystem.IsWindows()
                    ? Info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner).GetSecurityDescriptorSddlForm(AccessControlSections.Access | AccessControlSections.Owner)
                    : File.GetUnixFileMode(FilePath).ToString();
                return (Info.LinkTarget != null ? "link " : "file ") + Info.Length + " " + Info.LastWriteTimeUtc.Ticks + " " + Info.Attributes + " " + Protection;
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                return "unknown";
            }
        }

        // what the file system, its access rules and the identities can throw for one file: never a fault of the program
        internal static bool IsFileSystemFailure(Exception Error) =>
            Error is IOException || Error is UnauthorizedAccessException || Error is InvalidOperationException || Error is NotSupportedException
            || Error is ArgumentException || Error is IdentityNotMappedException || Error is System.ComponentModel.Win32Exception;

        // a file, a folder or a link (also a dangling one) of that name
        internal static bool Exists(string FullPath) => File.Exists(FullPath) || Directory.Exists(FullPath) || new FileInfo(FullPath).LinkTarget != null;
    }
}
