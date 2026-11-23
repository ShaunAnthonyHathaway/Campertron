using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // General.yaml and Email.yaml read as the console's start reads an existing one, without ever creating one
    // (Yaml.GeneralConfigGetExisting and EmailConfigGetExisting; v0.3.0's PR 8, D98: the web UI's monitoring reads its
    // settings with them). A missing file is null and stays missing; an existing one is read by the bounded loader and,
    // for General.yaml, refused for the values a start refuses (D41), with the file noted for the failure's words.
    public class ExistingSettingsTests
    {
        private string Folder = string.Empty;

        [SetUp]
        public void SetUp()
        {
            Folder = Path.Join(Path.GetTempPath(), "CampertronExistingSettingsTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(Folder, true);
        }

        [Test]
        public void AMissingFile_IsNull_AndNothingIsCreated()
        {
            Assert.That(Yaml.GeneralConfigGetExisting(Folder), Is.Null);
            Assert.That(Yaml.EmailConfigGetExisting(Folder), Is.Null);
            Assert.That(Directory.GetFileSystemEntries(Folder), Is.Empty, "no sample file is written (GeneralConfigGetConfig and EmailConfigGetConfig would write one)");
        }

        [Test]
        public void AnExistingFile_IsReadAsTheStartReadsIt()
        {
            File.WriteAllText(Path.Join(Folder, "General.yaml"), "outputTo: Ntfy\nautoRefresh: false\n");
            File.WriteAllText(Path.Join(Folder, "Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 2525\n");
            Assert.That(Yaml.GeneralConfigGetExisting(Folder)!.OutputTo, Is.EqualTo(OutputType.Ntfy));
            EmailConfig Email = Yaml.EmailConfigGetExisting(Folder)!;
            Assert.That((Email.SmtpServer, Email.SmtpPort), Is.EqualTo(("smtp.example.invalid", 2525)));
        }

        [Test]
        public void AFileTheStartRefuses_IsRefusedTheSameWay_WithTheFileNoted()
        {
            string General = Path.Join(Folder, "General.yaml");
            File.WriteAllText(General, "outputTo: Email\nrefreshRidbDataDayInterval: 1e300\nautoRefresh: true\n");
            Exception Refused = Assert.Catch(() => Yaml.GeneralConfigGetExisting(Folder))!;
            Exception Console = Assert.Catch(() => Yaml.GeneralConfigGetConfig(Folder))!;
            Assert.That(Refused.GetType(), Is.EqualTo(Console.GetType()), "what GeneralConfigGetConfig refuses in an existing file");
            Assert.That(Refused.Message, Is.EqualTo(Console.Message));
            Assert.That(Refused.Data[ConsoleUi.FileKey], Is.EqualTo(General));

            File.WriteAllText(General, "outputTo: [not, a, choice\n");
            Assert.That(Assert.Catch(() => Yaml.GeneralConfigGetExisting(Folder))!.Data[ConsoleUi.FileKey], Is.EqualTo(General));
            string Email = Path.Join(Folder, "Email.yaml");
            File.WriteAllText(Email, "smtpPort: [not, a, number\n");
            Assert.That(Assert.Catch(() => Yaml.EmailConfigGetExisting(Folder))!.Data[ConsoleUi.FileKey], Is.EqualTo(Email));
        }
    }
}
