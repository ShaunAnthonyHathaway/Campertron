using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // D75: SecretFileCheck, the metadata of a secret file (smtpPasswordFile, tokenFile) without opening it: every state the
    // GUI tells (missing, a folder, unreadable, empty, too large, readable by others, owner-only), links followed, the
    // Windows access rules (on Windows, in the CI's Windows job), the words, the runtime's reader still deciding the same
    // problems first (SecretFile.Read starts with Locate), and that the file is never opened: inotify on Linux sees no open
    // of any file in the folder while it is checked (and does see the test's own open afterwards), and the code refers to
    // nothing that could open or read a file
    public class SecretFileCheckTests
    {
        private string Folder = string.Empty;
        private Func<string, bool?> OriginalCanRead = null!;

        [SetUp]
        public void Setup()
        {
            Folder = Path.Join(Path.GetTempPath(), "CampertronSecretFileCheckTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            OriginalCanRead = SecretFileCheck.CanRead;
        }

        [TearDown]
        public void TearDown()
        {
            SecretFileCheck.CanRead = OriginalCanRead;
            Directory.Delete(Folder, true);
        }

        private string Write(string Name, string Text, UnixFileMode? Mode = null)
        {
            string Path_ = Path.Join(Folder, Name);
            File.WriteAllText(Path_, Text);
            if (Mode != null && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path_, Mode.Value);
            }
            return Path_;
        }

        private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        private static bool IsRoot => !OperatingSystem.IsWindows() && Environment.UserName == "root";

        [Test]
        public void EveryState_IsTold_FromTheMetadata()
        {
            Assert.That(SecretFileCheck.Inspect(Path.Join(Folder, "missing")), Is.EqualTo(new SecretFileStatus(SecretFileProblem.Missing, false)));
            Directory.CreateDirectory(Path.Join(Folder, "a folder"));
            Assert.That(SecretFileCheck.Inspect(Path.Join(Folder, "a folder")).Problem, Is.EqualTo(SecretFileProblem.IsFolder));
            Assert.That(SecretFileCheck.Inspect(Write("empty", "", OwnerOnly)).Problem, Is.EqualTo(SecretFileProblem.Empty));
            Assert.That(SecretFileCheck.Inspect(Write("large", new string('x', 4097), OwnerOnly)).Problem, Is.EqualTo(SecretFileProblem.TooLarge));
            Assert.That(SecretFileCheck.Inspect(Write("ok", "secret\n", OwnerOnly)), Is.EqualTo(new SecretFileStatus(null, false)), "owner-only: nothing to say");
            Assert.That(SecretFileCheck.Inspect("relative/secret").Problem, Is.EqualTo(SecretFileProblem.PathNotAbsolute));
            Assert.That(SecretFileCheck.Inspect("  ").Problem, Is.EqualTo(SecretFileProblem.PathEmpty));
        }

        [Test]
        public void OnLinuxAndMacOs_GroupOrOthersRead_IsTold_AndAnUnreadableFile()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions");
                return;
            }
            Assert.That(SecretFileCheck.Inspect(Write("others", "secret\n", OwnerOnly | UnixFileMode.OtherRead)), Is.EqualTo(new SecretFileStatus(null, true)));
            Assert.That(SecretFileCheck.Inspect(Write("group", "secret\n", OwnerOnly | UnixFileMode.GroupRead)), Is.EqualTo(new SecretFileStatus(null, true)));
            Assert.That(SecretFileCheck.Inspect(Write("default", "secret\n", OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead)).OthersCanRead, Is.True);
            Assert.That(SecretFileCheck.Inspect(Write("600", "secret\n", OwnerOnly)).OthersCanRead, Is.False);
            if (IsRoot)
            {
                Assert.Ignore("root may read any file");
            }
            Assert.That(SecretFileCheck.Inspect(Write("unreadable", "secret\n", UnixFileMode.UserWrite)), Is.EqualTo(new SecretFileStatus(SecretFileProblem.NotReadable, false)));
            Assert.That(SecretFileCheck.Inspect(Write("none", "secret\n", UnixFileMode.None)).Problem, Is.EqualTo(SecretFileProblem.NotReadable));
            Assert.That(SecretFile.Read(Path.Join(Folder, "unreadable"), out _), Is.EqualTo(SecretFileProblem.NotReadable), "the runtime's reader agrees");
        }

        [Test]
        public void Links_AreFollowed_AsTheReaderFollowsThem()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("symbolic links need a privilege on Windows");
                return;
            }
            string Target = Write("target", "secret\n", OwnerOnly);
            File.CreateSymbolicLink(Path.Join(Folder, "link"), Target);
            Assert.That(SecretFileCheck.Inspect(Path.Join(Folder, "link")), Is.EqualTo(new SecretFileStatus(null, false)), "the link's own mode doesn't count, the file's does");
            File.SetUnixFileMode(Target, OwnerOnly | UnixFileMode.OtherRead);
            Assert.That(SecretFileCheck.Inspect(Path.Join(Folder, "link")).OthersCanRead, Is.True);
            File.CreateSymbolicLink(Path.Join(Folder, "dangling"), Path.Join(Folder, "nothing"));
            Assert.That(SecretFileCheck.Inspect(Path.Join(Folder, "dangling")).Problem, Is.EqualTo(SecretFileProblem.Missing));
            Directory.CreateDirectory(Path.Join(Folder, "dir"));
            File.CreateSymbolicLink(Path.Join(Folder, "to-dir"), Path.Join(Folder, "dir"));
            Assert.That(SecretFileCheck.Inspect(Path.Join(Folder, "to-dir")).Problem, Is.EqualTo(SecretFileProblem.IsFolder));
        }

        // the runtime's reader decides the same metadata problems the same way (it starts with SecretFileCheck.Locate)
        [Test]
        public void TheReader_AndTheCheck_AgreeOnEveryMetadataProblem()
        {
            Directory.CreateDirectory(Path.Join(Folder, "dir"));
            foreach (string Path_ in new[] { Path.Join(Folder, "missing"), Path.Join(Folder, "dir"), Write("empty", "", OwnerOnly), Write("large", new string('x', 5000), OwnerOnly), "relative", "" })
            {
                Assert.That(SecretFile.Read(Path_, out string? Text), Is.EqualTo(SecretFileCheck.Inspect(Path_).Problem), Path_);
                Assert.That(Text, Is.Null);
            }
            Assert.That(SecretFile.Read(Write("ok", "secret\n", OwnerOnly), out string? Secret), Is.Null);
            Assert.That(Secret, Is.EqualTo("secret"));
        }

        // Windows: the access rules decide readability (the check asks the system with the file's security descriptor);
        // others' read permission doesn't apply. In the CI's Windows job
        [Test]
        public void OnWindows_TheAccessRulesDecide_AndNoPermissionWarning()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("Windows only");
                return;
            }
            string Path_ = Write("secret", "secret\n");
            Assert.That(SecretFileCheck.Inspect(Path_), Is.EqualTo(new SecretFileStatus(null, false)));
            var Info = new FileInfo(Path_);
            FileSecurity Security = Info.GetAccessControl();
            var Me = WindowsIdentity.GetCurrent().User!;
            var Deny = new FileSystemAccessRule(Me, FileSystemRights.ReadData, AccessControlType.Deny);
            Security.AddAccessRule(Deny);
            Info.SetAccessControl(Security);
            try
            {
                Assert.That(SecretFileCheck.Inspect(Path_), Is.EqualTo(new SecretFileStatus(SecretFileProblem.NotReadable, false)));
                Assert.That(SecretFile.Read(Path_, out _), Is.EqualTo(SecretFileProblem.NotReadable), "the runtime's reader agrees");
            }
            finally
            {
                Security.RemoveAccessRule(Deny);
                Info.SetAccessControl(Security);
            }
            Assert.That(SecretFileCheck.Inspect(Path_).Problem, Is.Null);
        }

        // when the system can't say whether the file is readable, nothing is claimed
        [Test]
        public void AnUnknownAnswer_ClaimsNothing()
        {
            SecretFileCheck.CanRead = _ => null;
            Assert.That(SecretFileCheck.Inspect(Write("ok", "secret\n", OwnerOnly)).Problem, Is.Null);
            SecretFileCheck.CanRead = _ => false;
            Assert.That(SecretFileCheck.Inspect(Write("no", "secret\n", OwnerOnly)).Problem, Is.EqualTo(SecretFileProblem.NotReadable));
        }

        [TestCase("smtpPasswordFile", "password", true, "smtpPasswordFile: smtp_password doesn't exist on this computer, so email mode refuses to start on this computer. If Campertron runs somewhere else, such as in a container, the file only has to exist there.")]
        [TestCase("smtpPasswordFile", "password", false, "smtpPasswordFile: smtp_password doesn't exist on this computer, so email mode would refuse to start on this computer. If Campertron runs somewhere else, such as in a container, the file only has to exist there.")]
        [TestCase("tokenFile", "token", true, "tokenFile: smtp_password doesn't exist on this computer, so the monitoring with ntfy on refuses to start on this computer. If Campertron runs somewhere else, such as in a container, the file only has to exist there.")]
        public void TheWords_NameTheSettingAndTheFileOnly(string Key, string Kind, bool InUse, string Expected)
        {
            string Path_ = Path.Join(Folder, "smtp_password");
            var Said = SecretFileCheck.Describe(Key, Path_, SecretFileCheck.Inspect(Path_), Kind, InUse);
            Assert.That(Said, Is.EqualTo((Expected, true)));
            Assert.That(Said!.Value.Message, Does.Not.Contain(Folder));
        }

        [Test]
        public void TheWords_OfEveryState()
        {
            Directory.CreateDirectory(Path.Join(Folder, "dir"));
            string Said(string Path_) => SecretFileCheck.Describe("tokenFile", Path_, SecretFileCheck.Inspect(Path_), "token", true)?.Message ?? "(nothing)";
            Assert.That(Said(Path.Join(Folder, "dir")), Is.EqualTo("tokenFile: dir is a folder, not a file, so the monitoring with ntfy on refuses to start on this computer."));
            Assert.That(Said(Write("empty", "", OwnerOnly)), Is.EqualTo("tokenFile: empty is empty, so the monitoring with ntfy on refuses to start on this computer."));
            Assert.That(Said(Write("big", new string('x', 4097), OwnerOnly)), Is.EqualTo("tokenFile: big is larger than 4,096 bytes, too large for a token file, so the monitoring with ntfy on refuses to start on this computer."));
            Assert.That(Said(Write("ok", "t\n", OwnerOnly)), Is.EqualTo("(nothing)"));
            SecretFileCheck.CanRead = _ => false;
            Assert.That(Said(Write("locked", "t\n", OwnerOnly)), Is.EqualTo("tokenFile: you can't read locked on this computer, so the monitoring with ntfy on refuses to start on this computer. Check the file's owner and permissions."));
            SecretFileCheck.CanRead = OriginalCanRead;
            if (!OperatingSystem.IsWindows())
            {
                string Open = Write("open", "t\n", OwnerOnly | UnixFileMode.OtherRead);
                Assert.That(SecretFileCheck.Describe("tokenFile", Open, SecretFileCheck.Inspect(Open), "token", true),
                    Is.EqualTo(("tokenFile: other users on this computer can read open. Make it readable only by you, for example with chmod 600 open.", false)));
            }
        }

        // ---- never opened ----

        // inotify (Linux) reports every open and read of a file in the watched folder, the test's own included: while the
        // check looks at a readable, an others-readable, an unreadable, an empty file and a FIFO, nothing is opened; the
        // test's own read afterwards is seen, so the watch works
        [Test]
        public void TheCheck_NeverOpensTheFile_AsInotifySees()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("inotify is Linux's");
                return;
            }
            string[] Files =
            {
                Write("readable", "secret\n", OwnerOnly), Write("open", "secret\n", OwnerOnly | UnixFileMode.OtherRead),
                Write("unreadable", "secret\n", UnixFileMode.UserWrite), Write("empty", "", OwnerOnly)
            };
            string Fifo = Path.Join(Folder, "fifo");
            Assert.That(Inotify.MakeFifo(Fifo), Is.Zero, "a FIFO: opening it to read would block");
            using var Watch = new Inotify(Folder);
            foreach (string File_ in Files.Append(Fifo))
            {
                SecretFileCheck.Inspect(File_);
                SecretFileCheck.Describe("smtpPasswordFile", File_, SecretFileCheck.Inspect(File_), "password", true);
            }
            Assert.That(Watch.Events(), Is.Empty, "no open, read or close of any file while it was checked");
            File.ReadAllBytes(Files[0]);
            Assert.That(Watch.Events(), Does.Contain("readable: open"), "the watch sees an open: the positive control");
        }

        // SecretFileCheck refers to nothing that opens, reads or copies a file, or reads the environment; the reader that
        // does (SecretFile.Read) is not used by it
        [Test]
        public void TheCheck_RefersToNothingThatOpensAFile()
        {
            const System.Reflection.BindingFlags All = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
            List<string> Called = typeof(SecretFileCheck).GetMethods(All).Cast<System.Reflection.MethodBase>()
                .SelectMany(M => YamlDocumentPreservationTests.ReferencedMembers(M)).Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).Distinct().ToList();
            Assert.That(Called, Does.Contain("System.IO.File::GetUnixFileMode"));
            string[] Forbidden =
            {
                "System.IO.FileStream", "System.IO.File::Open", "System.IO.File::Read", "System.IO.File::Copy", "System.IO.File::Write", "System.IO.File::Move",
                "System.IO.File::Delete", "System.IO.File::Set", "System.IO.FileInfo::Open", "System.IO.FileInfo::CopyTo", "System.IO.StreamReader",
                "Microsoft.Win32.SafeHandles.SafeFileHandle", "System.IO.RandomAccess", "System.IO.Directory::Get", "System.IO.Directory::Enumerate",
                "System.Environment::GetEnvironmentVariable", "CampertronLibrary.function.Base.SecretFile::Read", "CampertronLibrary.function.Base.SecretFile::FromBytes",
                "CampertronLibrary.function.Base.SmtpPassword", "CampertronLibrary.function.Base.NtfyToken", "System.Security.Cryptography"
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
        }

        // the inotify watch of one folder (Linux): its files' opens, reads and closes, as "name: open" lines
        internal sealed class Inotify : IDisposable
        {
            private const int NonBlock = 0x800;
            private const uint Access = 0x1, CloseNoWrite = 0x10, Open = 0x20;
            private readonly int _Handle;

            internal Inotify(string Folder)
            {
                _Handle = inotify_init1(NonBlock);
                Assert.That(_Handle, Is.GreaterThanOrEqualTo(0), "inotify_init1");
                Assert.That(inotify_add_watch(_Handle, Folder, Access | Open | CloseNoWrite), Is.GreaterThanOrEqualTo(0), "inotify_add_watch");
            }

            internal List<string> Events()
            {
                var Out = new List<string>();
                byte[] Buffer = new byte[64 * 1024];
                while (true)
                {
                    nint Count = read(_Handle, Buffer, Buffer.Length);
                    if (Count <= 0)
                    {
                        return Out;
                    }
                    int At = 0;
                    while (At + 16 <= Count)
                    {
                        uint Mask = BitConverter.ToUInt32(Buffer, At + 4);
                        int Length = BitConverter.ToInt32(Buffer, At + 12);
                        string Name = System.Text.Encoding.UTF8.GetString(Buffer, At + 16, Length).TrimEnd('\0');
                        if ((Mask & Open) != 0) { Out.Add(Name + ": open"); }
                        if ((Mask & Access) != 0) { Out.Add(Name + ": read"); }
                        if ((Mask & CloseNoWrite) != 0) { Out.Add(Name + ": close"); }
                        At += 16 + Length;
                    }
                }
            }

            internal static int MakeFifo(string Path_) => mkfifo(Path_, 0x180);

            public void Dispose() => close(_Handle);

            [DllImport("libc", SetLastError = true)] private static extern int inotify_init1(int Flags);
            [DllImport("libc", SetLastError = true)] private static extern int inotify_add_watch(int Handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string Path_, uint Mask);
            [DllImport("libc", SetLastError = true)] private static extern nint read(int Handle, byte[] Buffer, nint Count);
            [DllImport("libc")] private static extern int close(int Handle);
            [DllImport("libc", SetLastError = true)] private static extern int mkfifo([MarshalAs(UnmanagedType.LPUTF8Str)] string Path_, uint Mode);
        }
    }
}
