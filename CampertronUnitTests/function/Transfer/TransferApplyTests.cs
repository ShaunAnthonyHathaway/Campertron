using CampertronLibrary.function.Base;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.Apply;

namespace CampertronUnitTests.function.Transfer
{
    // The apply's order, writes and read-back (D134; v0.4.0-settings-transfer.md §Write order, §Atomic replacement, §Read-back
    // verification, §Restart requirements, §Monitoring, refresh, search and editor coordination): every file kind created or
    // replaced through the safe writers in the fixed order with General.yaml last, a file whose text is the same not written,
    // the receiver's other files never touched, and nothing searched, refreshed, monitored or recorded in Camp.History
    public class TransferApplyTests : TransferApplyTestBase
    {
        private TransferPackage Everything(bool Secrets = false) => Secrets
            ? ImportPackage(General(PackageGeneral), Email(PackageEmail), Ntfy(PackageNtfy), Search("Trip.yaml", PackageSearch), Search("Here.yaml", PackageSearch), SmtpSecret(), TokenSecret(), TopicSecret())
            : ImportPackage(General(PackageGeneral), Email(PackageEmail), Ntfy(PackageNtfy), Search("Trip.yaml", PackageSearch), Search("Here.yaml", PackageSearch));

        [Test]
        public void AnEmptyReceiver_GetsEveryFile_Created_InTheOrder_GeneralLast()
        {
            TransferApplyResult Result = Import(Everything());
            Assert.That(Result.Message, Is.EqualTo(TransferImportWording.Applied));
            Assert.That(Read_("General.yaml"), Is.EqualTo(PackageGeneral), "nothing of this computer's to put back: the package's text");
            Assert.That(Read_("Email.yaml"), Is.EqualTo(PackageEmail));
            Assert.That(Read_("Ntfy.yaml"), Is.EqualTo(PackageNtfy));
            Assert.That(Read_("Trip.yaml"), Is.EqualTo(PackageSearch));
            Assert.That(Read_("Here.yaml"), Is.EqualTo(PackageSearch));
            List<string> Writes = Steps.Where(S => S.StartsWith("write:", StringComparison.Ordinal)).ToList();
            Assert.That(Writes, Is.EqualTo(new[] { "write:Here.yaml", "write:Trip.yaml", "write:Ntfy.yaml", "write:Email.yaml", "write:General.yaml" }));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(ModeOf(In("Email.yaml")), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite), "a new Email.yaml is owner-only, as the editors create it");
                Assert.That(ModeOf(In("Ntfy.yaml")), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite), "a new Ntfy.yaml is owner-only");
            }
        }

        [Test]
        public void EveryWrite_GoesThroughTheSafeWriters_AndIsReadBack()
        {
            Receiver(Searches: new[] { "Here.yaml" });
            Import(Everything(Secrets: true));
            foreach (string Name in new[] { "Here.yaml", "Ntfy.yaml", "Email.yaml", "General.yaml" })
            {
                List<string> Of = Steps.SkipWhile(S => S != "write:" + Name).Skip(1).TakeWhile(S => S != "readback:" + Name).ToList();
                Assert.That(Of, Does.Contain("replace:before-precondition").And.Contain("replace:before-replace").And.Contain("replace:backup-installed"), Name + ": ConfigFileReplace.Replace, its re-read and its rolling backup");
            }
            List<string> Trip = Steps.SkipWhile(S => S != "write:Trip.yaml").Skip(1).TakeWhile(S => S != "readback:Trip.yaml").ToList();
            Assert.That(Trip, Does.Contain("replace:before-create").And.Contain("replace:after-create"), "a new file through ConfigFileReplace.Create: create-only");
            Assert.That(Steps, Does.Contain("verify").And.Contain("compare").And.Contain("secret-reference").And.Contain("commit"));
            Assert.That(File.Exists(In("Trip.yaml.bak")), Is.False, "a created file has no rolling backup");
        }

        [Test]
        public void AnExistingReceiver_KeepsItsOwnValues_AndItsOtherSearches()
        {
            Receiver(Searches: new[] { "Mine.yaml", "Here.yaml" });
            string Mine = Read_("Mine.yaml");
            TransferApplyResult Result = Import(Everything());
            string General_ = Read_("General.yaml");
            Assert.That(General_, Does.Contain("lastRidbDataRefresh: 2026-10-01T08:00:00"), "this computer's refresh time");
            Assert.That(General_, Does.Contain("refreshRidbDataDayInterval: 30").And.Contain("# the other computer's general settings"), "the package's settings and comments");
            string Email_ = Read_("Email.yaml");
            Assert.That(Email_, Does.Contain("smtpPassword: " + ReceiverPasswordCanary), "this computer's own plain-text password stays");
            Assert.That(Email_, Does.Contain("smtpServer: mail.example.com # the relay"));
            string Ntfy_ = Read_("Ntfy.yaml");
            Assert.That(Ntfy_, Does.Contain("topic: " + ReceiverTopicCanary), "this computer's topic stays when none is imported");
            Assert.That(Read_("Mine.yaml"), Is.EqualTo(Mine), "a search the package does not hold is not touched");
            Assert.That(Read_("Here.yaml"), Is.EqualTo(PackageSearch), "a search of the same name is replaced");
            Assert.That(Result.RestartWords, Is.Not.Empty);
        }
    }
}
