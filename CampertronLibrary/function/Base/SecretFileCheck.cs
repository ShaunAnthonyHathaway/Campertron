using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace CampertronLibrary.function.Base
{
    // what a secret file's metadata says, without opening it (D75): a problem the monitoring's start would refuse (a missing
    // file, a folder, a file the user can't read, an empty or too large one), and on Linux and macOS whether others can read
    // it (group or other read permission)
    internal readonly record struct SecretFileStatus(SecretFileProblem? Problem, bool OthersCanRead);

    // The metadata of a file that holds a secret (decision D75; smtpPasswordFile and tokenFile, business-rules-settings §27,
    // §28). Locate is the start of SecretFile.Read, shared: the path must be fully qualified, links are followed, and a folder,
    // a missing file, a link to nothing, an empty file (also a FIFO or a device, which report no length) and a file over
    // 4,096 bytes are problems. Inspect, for the GUI, adds what the reader learns only by opening the file, asked of the
    // system instead: whether the current user may read it (access(2) on Linux and macOS, the file's security descriptor
    // checked against the user's token on Windows), and on Linux and macOS whether its group or others may read it. Nothing
    // here opens, reads, hashes, copies or changes the file, lists its folder, or reads an environment variable; the
    // file's text is only ever SecretFile.Read's, at the monitoring's start
    internal static class SecretFileCheck
    {
        // null when the path names a regular file of 1 to 4,096 bytes (Target: the file, after its links), else the problem
        internal static SecretFileProblem? Locate(string? FilePath, out FileInfo? Target)
        {
            Target = null;
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                return SecretFileProblem.PathEmpty;
            }
            if (!Path.IsPathFullyQualified(FilePath))
            {
                return SecretFileProblem.PathNotAbsolute;
            }
            try
            {
                if (Directory.Exists(FilePath))
                {
                    return SecretFileProblem.IsFolder;
                }
                FileInfo Info = new FileInfo(FilePath);
                if (Info.LinkTarget != null)
                {
                    FileSystemInfo? Resolved = Info.ResolveLinkTarget(true);
                    if (Resolved == null || Directory.Exists(Resolved.FullName))
                    {
                        return Resolved == null ? SecretFileProblem.Missing : SecretFileProblem.IsFolder;
                    }
                    Info = new FileInfo(Resolved.FullName);
                }
                if (!Info.Exists)
                {
                    return SecretFileProblem.Missing;
                }
                if (Info.Length == 0)
                {
                    return SecretFileProblem.Empty;
                }
                if (Info.Length > SecretFile.MaxFileBytes)
                {
                    return SecretFileProblem.TooLarge;
                }
                Target = Info;
                return null;
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return SecretFileProblem.Missing;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                return SecretFileProblem.NotReadable;
            }
        }

        // the file's metadata for the GUI: Locate's problems, then whether the user may read it and whether others may
        internal static SecretFileStatus Inspect(string? FilePath)
        {
            SecretFileProblem? Problem = Locate(FilePath, out FileInfo? Target);
            if (Problem != null || Target == null)
            {
                return new SecretFileStatus(Problem, false);
            }
            if (CanRead(Target.FullName) == false)
            {
                return new SecretFileStatus(SecretFileProblem.NotReadable, false);
            }
            return new SecretFileStatus(null, OthersCanRead(Target.FullName));
        }

        // whether the current user may read the file, asked of the system without opening it; null when it can't say.
        // Replaceable so tests can make the answer fail
        internal static Func<string, bool?> CanRead { get; set; } = SystemCanRead;

        private static bool? SystemCanRead(string FilePath)
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsCanRead(FilePath);
            }
            try
            {
                if (UnixAccess(FilePath, ReadOk) == 0)
                {
                    return true;
                }
                return Marshal.GetLastPInvokeError() == AccessDenied ? false : null;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                return null;
            }
        }

        // the group's or others' read permission (Linux and macOS); never on Windows, where the access rules decide
        private static bool OthersCanRead(string FilePath)
        {
            if (OperatingSystem.IsWindows())
            {
                return false;
            }
            try
            {
                return (File.GetUnixFileMode(FilePath) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                return false;
            }
        }

        // access(2): whether the process may read the file, without opening it
        private const int ReadOk = 4;
        private const int AccessDenied = 13;

        [DllImport("libc", EntryPoint = "access", SetLastError = true)]
        private static extern int UnixAccess([MarshalAs(UnmanagedType.LPUTF8Str)] string Path, int Mode);

        // Windows: the file's security descriptor (owner, group, access rules; metadata, not its contents) checked against
        // the current user's token for the rights a reading open asks for (FILE_GENERIC_READ)
        [SupportedOSPlatform("windows")]
        private static bool? WindowsCanRead(string FilePath)
        {
            try
            {
                byte[] Descriptor = new FileInfo(FilePath).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access)
                    .GetSecurityDescriptorBinaryForm();
                using WindowsIdentity Identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.Duplicate);
                if (!DuplicateToken(Identity.AccessToken, SecurityImpersonation, out SafeAccessTokenHandle Token))
                {
                    return null;
                }
                using (Token)
                {
                    var Mapping = new GenericMapping { GenericRead = FileGenericRead, GenericWrite = 0x120116, GenericExecute = 0x1200A0, GenericAll = 0x1F01FF };
                    byte[] Privileges = new byte[256];
                    uint PrivilegesLength = (uint)Privileges.Length;
                    if (!AccessCheck(Descriptor, Token, FileGenericRead, ref Mapping, Privileges, ref PrivilegesLength, out _, out bool Granted))
                    {
                        return null;
                    }
                    return Granted;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                //the security descriptor or the token can't be read: the system can't say (the start will)
                return null;
            }
        }

        private const int SecurityImpersonation = 2;
        private const uint FileGenericRead = 0x120089;

        [StructLayout(LayoutKind.Sequential)]
        private struct GenericMapping
        {
            public uint GenericRead;
            public uint GenericWrite;
            public uint GenericExecute;
            public uint GenericAll;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateToken(SafeAccessTokenHandle ExistingToken, int ImpersonationLevel, out SafeAccessTokenHandle DuplicateTokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AccessCheck(byte[] SecurityDescriptor, SafeAccessTokenHandle ClientToken, uint DesiredAccess, ref GenericMapping Mapping,
            byte[] PrivilegeSet, ref uint PrivilegeSetLength, out uint GrantedAccess, out bool AccessStatus);

        // ---- the GUI's words (the setting's name and the file's name at most; never the path's folder or a secret) ----

        // the message about a secret file, or null; Kind: "password" or "token". InUse: the provider uses it now (an error);
        // otherwise a warning
        internal static (string Message, bool IsProblem)? Describe(string Key, string? FilePath, SecretFileStatus Status, string Kind, bool InUse)
        {
            string Name = Path.GetFileName(FilePath ?? string.Empty);
            string Mode = Kind == "password" ? "email mode" : "the monitoring with ntfy on";
            string Consequence = InUse ? "so " + Mode + " refuses to start on this computer." : "so " + Mode + " would refuse to start on this computer.";
            string? Problem = Status.Problem switch
            {
                SecretFileProblem.Missing => Key + ": " + Name + " doesn't exist on this computer, " + Consequence + " If Campertron runs somewhere else, such as in a container, the file only has to exist there.",
                SecretFileProblem.IsFolder => Key + ": " + Name + " is a folder, not a file, " + Consequence,
                SecretFileProblem.NotReadable => Key + ": you can't read " + Name + " on this computer, " + Consequence + " Check the file's owner and permissions.",
                SecretFileProblem.Empty => Key + ": " + Name + " is empty, " + Consequence,
                SecretFileProblem.TooLarge => Key + ": " + Name + " is larger than 4,096 bytes, too large for a " + Kind + " file, " + Consequence,
                _ => null
            };
            if (Problem != null)
            {
                return (Problem, true);
            }
            if (Status.OthersCanRead)
            {
                return (Key + ": other users on this computer can read " + Name + ". Make it readable only by you, for example with chmod 600 " + Name + ".", false);
            }
            return null;
        }
    }
}
