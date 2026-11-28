using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.Apply;

namespace CampertronUnitTests.function.Transfer
{
    // Shared by the import's tests (v0.4.0's phase 16b, D134; v0.4.0-settings-transfer.md §Test obligations): each test has its
    // own temporary settings folder (an explicit fixture: the transfer core never finds a real one), the receiver's own secrets
    // as canaries of their own besides the package's, every seam restored, and nothing reads an environment variable (a receiver
    // never does). No test reaches the network, LocalApplicationData or the embedded database.
    public abstract class TransferApplyTestBase : TransferTestBase
    {
        // the receiver's own secrets: they stay where they are and appear nowhere else
        internal const string ReceiverPasswordCanary = "CANARY-receiverpassword-7c1e";
        internal const string ReceiverTopicCanary = "receivertopic5k2m8p3q";
        internal const string ReceiverTokenCanary = "CANARY-receivertoken-9d4f";

        protected string Folder = string.Empty;
        private Action<string, string?> OriginalStep = null!;
        private Action<string, string> OriginalReplaceStep = null!;
        private Func<string> OriginalTransactionId = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        protected bool EnvironmentRead;
        // the steps of the apply and of the safe writer, as they ran
        protected readonly List<string> Steps = new();

        protected string In(string Name) => Path.Join(Folder, Name);

        [SetUp]
        public void CreateApplyFolder()
        {
            Folder = Path.Join(Path.GetTempPath(), "CampertronTransferApplyTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            OriginalStep = TransferApply.Step;
            OriginalReplaceStep = ConfigFileReplace.Step;
            OriginalTransactionId = TransferImport.NewTransactionId;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            EnvironmentRead = false;
            SmtpPassword.GetEnvironment = Name => { EnvironmentRead = true; return null; };
            NtfyToken.GetEnvironment = Name => { EnvironmentRead = true; return null; };
            Steps.Clear();
            TransferApply.Step = (Name, Path_) => Steps.Add(Name);
            ConfigFileReplace.Step = (Name, Path_) => Steps.Add("replace:" + Name);
        }

        [TearDown]
        public void DeleteApplyFolder()
        {
            TransferApply.Step = OriginalStep;
            ConfigFileReplace.Step = OriginalReplaceStep;
            TransferImport.NewTransactionId = OriginalTransactionId;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            try
            {
                Assert.That(EnvironmentRead, Is.False, "the import read an environment variable");
            }
            finally
            {
                if (Directory.Exists(Folder))
                {
                    MakeRemovable(Folder);
                    Directory.Delete(Folder, true);
                }
            }
        }

        private static void MakeRemovable(string Root)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            foreach (string Entry in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories).Prepend(Root))
            {
                try
                {
                    File.SetUnixFileMode(Entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch (Exception)
                {
                    // removed below if it can be
                }
            }
        }

        // ---- the receiver ----

        protected void Write(string Name, string Text, bool Bom = false) =>
            File.WriteAllBytes(In(Name), (Bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(new UTF8Encoding(false).GetBytes(Text)).ToArray());

        protected string Read_(string Name) => File.ReadAllText(In(Name));

        protected byte[] BytesOf(string Name) => File.ReadAllBytes(In(Name));

        internal const string ReceiverGeneral = "# this computer's general settings\noutputTo: Console\nrefreshRidbDataDayInterval: 14\nlastRidbDataRefresh: 2026-10-01T08:00:00.0000000Z # refreshed here\nautoRefresh: true\n";
        internal const string ReceiverEmail = "# this computer's email\nsmtpServer: old.example.org\nsmtpPort: 25\nsmtpUsername: here@example.org\nsmtpPassword: " + ReceiverPasswordCanary + "\nsendToAddressList:\n- here@example.org\nsendFromAddress: here@example.org\n";
        internal const string ReceiverNtfy = "enabled: false # off here\nserver: https://ntfy.example.org\ntopic: " + ReceiverTopicCanary + "\npriority: low\n";
        internal const string ReceiverSearch = "# kept here\ndisplayName: Here\nautoRun: false\ncampgroundID: '1'\n";

        protected void Receiver(bool General = true, bool Email = true, bool Ntfy = true, params string[] Searches)
        {
            if (General)
            {
                Write(ConfigFiles.GeneralFileName, ReceiverGeneral);
            }
            if (Email)
            {
                Write(ConfigFiles.EmailFileName, ReceiverEmail);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(In(ConfigFiles.EmailFileName), UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            if (Ntfy)
            {
                Write(ConfigFiles.NtfyFileName, ReceiverNtfy);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(In(ConfigFiles.NtfyFileName), UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            foreach (string Search_ in Searches)
            {
                Write(Search_, ReceiverSearch);
            }
        }

        // ---- the package (as Export leaves this computer's keys out) ----

        internal const string PackageGeneral = "# the other computer's general settings\noutputTo: Console\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n";
        internal const string PackageEmail = "# from the other computer\nsmtpServer: mail.example.com # the relay\nsmtpPort: 587\nsmtpUsername: there@example.com\nsendToAddressList:\n- there@example.com\nsendFromAddress: there@example.com\n";
        internal const string PackageNtfy = "enabled: true\nserver: https://ntfy.example.com # theirs\npriority: high\n";
        internal const string PackageSearch = "# a trip\ndisplayName: Trip\nautoRun: true\ncampgroundID: '232447'\nsearchBy: DaysOut\nsearchValue: 7\n";

        internal static TransferPackage ImportPackage(params (TransferEntryKind Kind, string Name, string Text)[] Entries) => Package(Entries);

        // ---- the import ----

        internal static readonly TransferApplyHost Idle = new TransferApplyHost(() => false, () => false, () => false);

        internal TransferPreview PreviewOf(TransferPackage Package_, TransferImportChoices? Choices = null) => TransferImport.Preview(Folder, Package_, Choices);

        internal TransferApplyResult Applied(TransferPreview Preview) => TransferApply.Apply(Preview, Preview.Revision, Idle);

        // a preview that must be applicable, and its apply, which must succeed
        internal TransferApplyResult Import(TransferPackage Package_, TransferImportChoices? Choices = null)
        {
            TransferPreview Preview = PreviewOf(Package_, Choices);
            Assert.That(Preview.Refusals.Select(R => R.Message), Is.Empty, "the preview can be applied");
            TransferApplyResult Result = Applied(Preview);
            Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.Applied), Result.Message);
            return Result;
        }

        // a step of the apply or of the safe writer, at its nth occurrence, throws
        protected void FailAt(string StepName, int Occurrence = 1, Action? Before = null)
        {
            int Seen = 0;
            void Check(string Name)
            {
                Steps.Add(Name);
                if (Name == StepName && ++Seen == Occurrence)
                {
                    Before?.Invoke();
                    throw new IOException("injected at " + StepName);
                }
            }
            TransferApply.Step = (Name, Path_) => Check(Name);
            ConfigFileReplace.Step = (Name, Path_) => Check("replace:" + Name);
        }

        // ---- the folder ----

        // every entry of the folder (and below it): its kind, length, content's hash and mode
        protected Dictionary<string, string> Snapshot(Func<string, bool>? Leave = null)
        {
            var Entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string Entry in Directory.EnumerateFileSystemEntries(Folder, "*", SearchOption.AllDirectories))
            {
                string Relative = Path.GetRelativePath(Folder, Entry);
                if (Leave != null && Leave(Relative))
                {
                    continue;
                }
                string Content;
                try
                {
                    Content = File.Exists(Entry) ? System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Entry))) : "folder";
                }
                catch (IOException)
                {
                    // a file another handle holds without sharing (the apply's lock, held by the test)
                    Content = "held " + new FileInfo(Entry).Length;
                }
                string Mode = OperatingSystem.IsWindows() ? string.Empty : File.GetUnixFileMode(Entry).ToString();
                Entries[Relative] = $"{Content} {Mode}";
            }
            return Entries;
        }

        // the settings, search and secret files as a user sees them: without what an apply may leave beside them (its lock,
        // its backup set, the rolling backups, which hold no secret)
        protected Dictionary<string, string> Settings() => Snapshot(Relative =>
            Relative == TransferApplyLock.FileName || Relative == TransferBackupSet.FolderName || Relative.StartsWith(TransferBackupSet.FolderName + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Relative.EndsWith(".bak", StringComparison.Ordinal));

        protected IEnumerable<string> FilesBelow(string Relative = "") =>
            Directory.Exists(Path.Join(Folder, Relative)) ? Directory.EnumerateFiles(Path.Join(Folder, Relative), "*", SearchOption.AllDirectories) : Array.Empty<string>();

        protected string? SecretFileOf(string Prefix) => Directory.EnumerateFiles(Folder).Select(F => Path.GetFileName(F)).SingleOrDefault(N => N.StartsWith(Prefix, StringComparison.Ordinal));

        internal static readonly string[] AllCanaries = { SmtpCanary, TokenCanary, TopicCanary, PassphraseCanary, ReceiverPasswordCanary, ReceiverTopicCanary, ReceiverTokenCanary };

        // a canary appears only where it belongs: Allowed maps a file's name (relative) to the canaries it may hold
        protected void AssertCanariesOnlyIn(IReadOnlyDictionary<string, string[]> Allowed)
        {
            foreach (string File_ in FilesBelow())
            {
                string Relative = Path.GetRelativePath(Folder, File_);
                byte[] Bytes_ = File.ReadAllBytes(File_);
                string[] May = Allowed.TryGetValue(Relative, out string[]? Listed) ? Listed : Array.Empty<string>();
                foreach (string Canary in AllCanaries.Where(C => !May.Contains(C)))
                {
                    Assert.That(TransferSelfTest.Holds(Bytes_, Canary), Is.False, $"{Relative} holds a canary it must not ({Canary.Substring(0, 12)}…)");
                }
            }
        }

        // nothing a host may show holds a canary, a path or the settings folder
        internal void AssertShowsNoSecretOrPath(TransferPreview Preview, TransferApplyResult? Result = null)
        {
            var Shown = new List<string>(Preview.Lines());
            Shown.Add(Preview.ToString());
            Shown.AddRange(Preview.Files.SelectMany(F => F.Lines.Select(L => L.Text)));
            Shown.AddRange(Preview.Files.Select(F => F.ToString()));
            Shown.AddRange(Preview.Secrets.Select(S => S.ToString()));
            Shown.Add(Preview.OutputTo.ToString());
            Shown.Add(Preview.Plan.ToString());
            Shown.AddRange(Preview.Plan.Writes.Select(W => W.ToString()));
            Shown.AddRange(Preview.Plan.Secrets.Select(S => S.ToString()));
            if (Result != null)
            {
                Shown.Add(Result.Message);
                Shown.Add(Result.ToString());
                Shown.AddRange(Result.Warnings);
                Shown.AddRange(Result.Restored);
                Shown.AddRange(Result.NotRestored);
                Shown.AddRange(Result.RestartWords);
            }
            foreach (string Text_ in Shown)
            {
                foreach (string Canary in AllCanaries)
                {
                    Assert.That(Text_, Does.Not.Contain(Canary), "a shown text holds a canary");
                }
                Assert.That(Text_, Does.Not.Contain(Folder), "a shown text holds the settings folder's path");
                Assert.That(Text_, Does.Not.Contain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)), "a shown text holds a path");
                Assert.That(Text_, Does.Not.Contain(TransferApplyLock.FileName), "a shown text names the lock's file");
                Assert.That(Text_, Does.Not.Match(@"[0-9a-f]{32}"), "a shown text holds a transaction's identifier");
            }
        }

        // ---- platforms ----

        [SupportedOSPlatformGuard("windows")]
        protected static bool OnWindows()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("Windows only: run natively when the Windows job's class list names the transfer's tests (a separately approved workflow change)");
            }
            return true;
        }

        [UnsupportedOSPlatformGuard("windows")]
        protected static bool OnUnix()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Linux and macOS only: Windows has access rules instead of modes");
            }
            return true;
        }

        protected static UnixFileMode ModeOf(string FullPath) => OperatingSystem.IsWindows() ? 0 : File.GetUnixFileMode(FullPath);
    }
}
