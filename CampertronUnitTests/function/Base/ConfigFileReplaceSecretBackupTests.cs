using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // D111: ConfigFileReplace.Replace with a secret-free backup (business-rules-settings §14): the backup holds the text
    // the caller gives instead of the bytes read, checked before anything is written and again when its temporary file is
    // read back, with the file's mode; a backup that fails its check stops the replacement before anything is written; a
    // failure at any step leaves a whole file and a whole backup, and the bytes that held the secret go into no new file.
    // The class name starts with ConfigFileReplace, so the Windows CI job's filter runs it natively.
    public class ConfigFileReplaceSecretBackupTests
    {
        private const string Secret = "S3cr3t-Pa55-d111";
        private const string Original = "# my settings\nsmtpServer: 'mail.example.org' # host\nsmtpPassword: " + Secret + " # keep it safe\n";
        private const string Edited = "# my settings\nsmtpServer: 'mail.example.org' # host\nsmtpPassword: '' # keep it safe\n";
        private const string Secretless = Edited;
        private string Directory_ = string.Empty;
        private string Settings => Path.Join(Directory_, "Settings.yaml");
        private string Backup => Settings + ".bak";
        private Action<string, string> OriginalStep = null!;
        private readonly List<string> Steps = new();

        [SetUp]
        public void Setup()
        {
            Directory_ = Path.Join(Path.GetTempPath(), "CampertronConfigFileReplaceSecretBackupTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Directory_);
            File.WriteAllText(Settings, Original);
            OriginalStep = ConfigFileReplace.Step;
            Steps.Clear();
            ConfigFileReplace.Step = (Name, Path_) => Steps.Add(Name);
        }

        [TearDown]
        public void TearDown()
        {
            ConfigFileReplace.Step = OriginalStep;
            Directory.Delete(Directory_, true);
        }

        private static string? Accept(string Text) => null;

        private string[] Holding(string Value) =>
            Directory.GetFiles(Directory_).Where(F => File.ReadAllText(F).Contains(Value, StringComparison.Ordinal)).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal).ToArray()!;

        [Test]
        public void TheBackup_HoldsTheGivenText_CheckedBeforeWritingAndWhenReadBack_WithTheFilesMode()
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Settings, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            }
            var Checked = new List<string>();
            var Without = new ConfigFileReplace.SecretFreeBackup(Secretless, Text => { Checked.Add(Text); return null; }, "its plain-text password");

            ConfigFileReplace.Replace(ConfigFileReplace.Read(Settings), Edited, Accept, Without);

            Assert.That(File.ReadAllBytes(Settings), Is.EqualTo(Encoding.UTF8.GetBytes(Edited)));
            Assert.That(File.ReadAllBytes(Backup), Is.EqualTo(Encoding.UTF8.GetBytes(Secretless)), "the given text, not the bytes read");
            Assert.That(Checked, Is.EqualTo(new[] { Secretless, Secretless }), "before anything is written, and the temporary file read back");
            Assert.That(Holding(Secret), Is.Empty);
            Assert.That(Directory.GetFiles(Directory_).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Settings.yaml", "Settings.yaml.bak" }));
            Assert.That(Steps, Is.EqualTo(new[]
            {
                "temporary-writing", "temporary-flushed", "temporary-written", "temporary-verified",
                "temporary-writing", "temporary-flushed", "backup-written", "backup-installed",
                "before-precondition", "before-replace", "after-replace"
            }), "the same order as an exact backup");
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(Backup), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead));
            }
        }

        [Test]
        public void ABackupThatFailsItsCheck_StopsTheReplacement_BeforeAnythingIsWritten()
        {
            File.WriteAllText(Backup, "an older backup\n");
            var Without = new ConfigFileReplace.SecretFreeBackup(Original, Text => Text.Contains(Secret, StringComparison.Ordinal) ? "it holds the secret" : null, "its plain-text password");

            Assert.That(() => ConfigFileReplace.Replace(ConfigFileReplace.Read(Settings), Edited, Accept, Without),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("Campertron could not make the backup of Settings.yaml without its plain-text password; Settings.yaml was left as it is."));

            Assert.That(Steps, Is.Empty, "refused before anything is written");
            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Original));
            Assert.That(File.ReadAllText(Backup), Is.EqualTo("an older backup\n"));
        }

        [TestCase("temporary-writing")]
        [TestCase("temporary-written")]
        [TestCase("backup-written")]
        [TestCase("backup-installed")]
        [TestCase("before-precondition")]
        [TestCase("before-replace")]
        public void AFailureAtAnyStep_LeavesAWholeFileAndAWholeBackup_AndTheSecretInNoNewFile(string StepName)
        {
            File.WriteAllText(Backup, "an older backup\n");
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == StepName)
                {
                    throw new IOException("injected failure at " + StepName);
                }
            };
            var Without = new ConfigFileReplace.SecretFreeBackup(Secretless, Accept, "its plain-text password");

            Assert.That(() => ConfigFileReplace.Replace(ConfigFileReplace.Read(Settings), Edited, Accept, Without), Throws.TypeOf<IOException>());

            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Original), "the file as it was");
            bool Installed = StepName == "backup-installed" || StepName == "before-precondition" || StepName == "before-replace";
            Assert.That(File.ReadAllText(Backup), Is.EqualTo(Installed ? Secretless : "an older backup\n"));
            Assert.That(Holding(Secret), Is.EqualTo(new[] { "Settings.yaml" }), "no temporary file or backup holds it");
            Assert.That(Directory.GetFiles(Directory_).Length, Is.EqualTo(2), "the temporary files are removed");
        }

        [Test]
        public void AReadBackAfterTheRename_ThatFails_SaysWhatTheBackupLeavesOut()
        {
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "after-replace")
                {
                    File.AppendAllText(Path_, "# changed\n");
                }
            };
            var Without = new ConfigFileReplace.SecretFreeBackup(Secretless, Accept, "its plain-text password");

            Assert.That(() => ConfigFileReplace.Replace(ConfigFileReplace.Read(Settings), Edited, Accept, Without),
                Throws.TypeOf<IOException>().With.Message.EqualTo("Settings.yaml was replaced, but reading it back did not give what was written; the previous content, without its plain-text password, is in Settings.yaml.bak."));
            Assert.That(File.ReadAllText(Backup), Is.EqualTo(Secretless));
        }

        [Test]
        public void WithoutASecretFreeBackup_TheBackupIsTheExactBytesRead_AsBefore()
        {
            ConfigFileReplace.Replace(ConfigFileReplace.Read(Settings), Edited, Accept);

            Assert.That(File.ReadAllBytes(Backup), Is.EqualTo(Encoding.UTF8.GetBytes(Original)));
        }
    }
}
