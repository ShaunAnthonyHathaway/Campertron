using System.Security.Cryptography;
using System.Text;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Transfer;

namespace CampertronUnitTests.function.Transfer
{
    // Export's content at the package's boundary (D132; v0.4.0-settings-transfer.md §Allow-list, §Never-carried files and
    // state, §Secret classification, §Secret opt-in, §Environment-variable handling, §Text-preserving YAML edits): each test
    // has its own temporary settings folder (an explicit fixture: phase 16a never finds a real one), whose every entry is
    // compared before and after, so no test and no code path writes, creates, deletes or changes anything in it
    public class TransferExportTests : TransferTestBase
    {
        private string Folder = string.Empty;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private Dictionary<string, string>? Before;
        // the environment the tests give: CAMPERTRON_EMAIL_PASSWORD and CAMPERTRON_NTFY_TOKEN set to canaries when asked
        private bool SmtpVariableSet;
        private bool NtfyVariableSet;
        private const string EnvironmentCanary = "CANARY-environment-0b7e4d2a9f";

        private string In(string Name) => Path.Join(Folder, Name);

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(Path.GetTempPath(), "CampertronTransferExportTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            SmtpVariableSet = false;
            NtfyVariableSet = false;
            SmtpPassword.GetEnvironment = Name => Name == SmtpPassword.EnvironmentVariable && SmtpVariableSet ? EnvironmentCanary : null;
            NtfyToken.GetEnvironment = Name => Name == NtfyToken.EnvironmentVariable && NtfyVariableSet ? EnvironmentCanary : null;
            Before = null;
        }

        [TearDown]
        public void DeleteFolder()
        {
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            try
            {
                if (Before != null)
                {
                    Assert.That(Snapshot(), Is.EqualTo(Before), "the export changed the settings folder");
                }
            }
            finally
            {
                if (Directory.Exists(Folder))
                {
                    Directory.Delete(Folder, true);
                }
            }
        }

        private void Write(string Name, string Text, bool Bom = false) =>
            File.WriteAllBytes(In(Name), (Bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(new UTF8Encoding(false).GetBytes(Text)).ToArray());

        // every entry of the folder (and below it): its kind, length, content's hash, time and mode
        private Dictionary<string, string> Snapshot()
        {
            var Entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string Entry in Directory.EnumerateFileSystemEntries(Folder, "*", SearchOption.AllDirectories))
            {
                var Info = new FileInfo(Entry);
                string Content = File.Exists(Entry) ? System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Entry))) : "folder";
                string Mode = OperatingSystem.IsWindows() ? string.Empty : File.GetUnixFileMode(Entry).ToString();
                Entries[Path.GetRelativePath(Folder, Entry)] = $"{Content} {Info.LastWriteTimeUtc.Ticks} {Mode} {File.GetAttributes(Entry)}";
            }
            return Entries;
        }

        // the folder is fixed from here on: the test's TearDown compares it
        private void Freeze() => Before = Snapshot();

        private const string GeneralText = "# General settings\noutputTo: Email\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-01T10:00:00.0000000Z\nautoRefresh: false # never\n";
        private string EmailText(string? PasswordFile = null) =>
            "smtpServer: mail.example.com # the relay\nsmtpPort: 587\nsmtpUsername: user@example.com\nsmtpPassword: " + SmtpCanary + "\n"
            + (PasswordFile != null ? "smtpPasswordFile: " + PasswordFile + "\n" : string.Empty)
            + "sendToAddressList:\n- me@example.com\nsendFromAddress: user@example.com\n";
        private string NtfyText(string? TokenFile = null) =>
            "enabled: true\nserver: https://ntfy.example.com\ntopic: " + TopicCanary + "\n" + (TokenFile != null ? "tokenFile: " + TokenFile + "\n" : string.Empty) + "priority: high\n";
        private const string SearchText = "# my trip\ndisplayName: Trip\nautoRun: false\ncampgroundID: '232447'\n";

        private void Settings(string? PasswordFile = null, string? TokenFile = null)
        {
            Write("General.yaml", GeneralText);
            Write("Email.yaml", EmailText(PasswordFile));
            Write("Ntfy.yaml", NtfyText(TokenFile));
            Write("Trip.yaml", SearchText);
            Write("Other trip.yaml", "displayName: Other\r\ncampgroundID: '1'\r\n", Bom: true);
        }

        private static TransferExportSelection Everything(bool Password = false, bool Token = false, bool Topic = false) => new TransferExportSelection
        {
            General = true,
            Email = true,
            Ntfy = true,
            Searches = new[] { "Trip.yaml", "Other trip.yaml" },
            SmtpPassword = Password,
            NtfyToken = Token,
            NtfyTopic = Topic
        };

        private TransferExportResult Export(TransferExportSelection Selection, string? Passphrase = null) => TransferExport.Build(Folder, Selection, Passphrase, ExportedBy);

        private static TransferPackage ReadBack(TransferExportResult Result, string? Passphrase = null)
        {
            Assert.That(Result.Failure, Is.Null);
            TransferReadResult Read_ = Read(Result.Package!, Passphrase);
            Assert.That(Read_.Failure, Is.Null);
            return Read_.Package!;
        }

        private static string TextOf(TransferPackage Package_, string Name) => Package_.Entries.Single(E => E.Name == Name).Text;

        [Test]
        public void TheOffer_ListsTheFilesThatExist_AndTheSearchesTheNameRulesAccept()
        {
            Settings();
            Write("con.yaml", "displayName: x\n");
            Write("Web.yaml", "passwordProtection: true\n");
            Freeze();
            TransferExportOffer Offer = TransferExport.Offer(Folder);
            Assert.That((Offer.General, Offer.Email, Offer.Ntfy), Is.EqualTo((true, true, true)));
            Assert.That(Offer.Searches, Is.EqualTo(new[] { "Other trip.yaml", "Trip.yaml" }));
            Assert.That(Offer.SearchesNotOffered, Is.EqualTo(new[] { "con.yaml" }), "Web.yaml is a settings file, never a search");
            foreach (TransferSecretKind Secret in new[] { TransferSecretKind.SmtpPassword, TransferSecretKind.NtfyTopic })
            {
                Assert.That(Offer.SecretNote(Secret, Everything()), Is.Null, Secret.ToString());
            }
            foreach (TransferSecretKind Secret in Enum.GetValues<TransferSecretKind>())
            {
                Assert.That(Offer.SecretNote(Secret, new TransferExportSelection { General = true })!.Problem, Is.EqualTo(TransferProblem.ExportSecretWithoutFile), Secret.ToString());
            }
            Assert.That(Offer.SecretNote(TransferSecretKind.NtfyToken, Everything())!.Problem, Is.EqualTo(TransferProblem.ExportSecretNotConfigured), "no token source");
        }

        [Test]
        public void AnEmptyOrMissingFolder_OffersNothing()
        {
            Freeze();
            TransferExportOffer Offer = TransferExport.Offer(Folder);
            Assert.That((Offer.General, Offer.Email, Offer.Ntfy, Offer.Searches.Count), Is.EqualTo((false, false, false, 0)));
            TransferExportOffer Missing = TransferExport.Offer(In("absent"));
            Assert.That(Missing.Searches, Is.Empty);
            Assert.That(Directory.Exists(In("absent")), Is.False);
            Assert.That(Export(new TransferExportSelection { General = true }).Failure!.Problem, Is.EqualTo(TransferProblem.ExportFileMissing));
        }

        [Test]
        public void ADefaultExport_IsPlain_HoldsNoSecret_AndLeavesOutThisComputersKeys_ByTextEdits()
        {
            string PasswordFile = In("smtp.password");
            string TokenFile = In("ntfy.token");
            File.WriteAllText(PasswordFile, "CANARY-passwordfile-1d3e");
            File.WriteAllText(TokenFile, TokenCanary);
            Settings(PasswordFile, TokenFile);
            Freeze();
            TransferExportResult Result = Export(Everything());
            Assert.That(Result.Encrypted, Is.False);
            Assert.That(Text(Result.Package!), Does.Contain("\"protection\": \"none\""));
            AssertNoCanary("a default export", Result.Package!, SmtpCanary, TokenCanary, TopicCanary, "CANARY-passwordfile-1d3e", EnvironmentCanary);
            Assert.That(Text(Result.Package!), Does.Not.Contain(Folder).And.Not.Contain("smtp.password").And.Not.Contain("ntfy.token"), "no path");
            TransferPackage Read_ = ReadBack(Result);
            Assert.That(Read_.HasSecret, Is.False);
            // the keys removed, and only them: comments, order and the other values kept exactly
            Assert.That(TextOf(Read_, "General.yaml"), Is.EqualTo("# General settings\noutputTo: Email\nrefreshRidbDataDayInterval: 30\nautoRefresh: false # never\n"));
            Assert.That(TextOf(Read_, "Email.yaml"), Is.EqualTo("smtpServer: mail.example.com # the relay\nsmtpPort: 587\nsmtpUsername: user@example.com\nsendToAddressList:\n- me@example.com\nsendFromAddress: user@example.com\n"));
            Assert.That(TextOf(Read_, "Ntfy.yaml"), Is.EqualTo("enabled: true\nserver: https://ntfy.example.com\npriority: high\n"));
            // search files travel unchanged, byte-order mark and line endings included
            Assert.That(TextOf(Read_, "Trip.yaml"), Is.EqualTo(SearchText));
            Assert.That(TextOf(Read_, "Other trip.yaml"), Is.EqualTo("﻿displayName: Other\r\ncampgroundID: '1'\r\n"));
            // each edited text loads as the file's settings without those keys
            Assert.That(Yaml.LoadSettingsText<EmailConfig>(TextOf(Read_, "Email.yaml")).SmtpServer, Is.EqualTo("mail.example.com"));
            Assert.That(Yaml.LoadSettingsText<GeneralConfig>(TextOf(Read_, "General.yaml")).LastRidbDataRefresh, Is.Null);
        }

        [Test]
        public void TheSmtpPassword_FromThePlainText_IsCarriedOnlyWhenChosen_Encrypted()
        {
            Settings();
            Freeze();
            Assert.That(Export(Everything(Password: true)).Failure!.Problem, Is.EqualTo(TransferProblem.PassphraseEmpty));
            TransferExportResult Result = Export(Everything(Password: true), PassphraseCanary);
            Assert.That(Result.Encrypted, Is.True);
            AssertNoCanary("an encrypted export", Result.Package!);
            TransferPackage Read_ = ReadBack(Result, PassphraseCanary);
            Assert.That(Read_.Entries.Where(E => E.IsSecret).Select(E => (E.Kind, E.Text)), Is.EqualTo(new[] { (TransferEntryKind.SecretSmtpPassword, SmtpCanary) }));
            Assert.That(TextOf(Read_, "Email.yaml"), Does.Not.Contain(SmtpCanary).And.Not.Contain("smtpPassword"), "the text never holds it, even encrypted");
            Assert.That(TextOf(Read_, "Ntfy.yaml"), Does.Not.Contain(TopicCanary), "the topic not chosen");
        }

        [Test]
        public void ThePasswordFromItsFile_TheTokenFromItsFile_AndTheTopic_EachOnItsOwnChoice()
        {
            string PasswordFile = In("smtp.password");
            string TokenFile = In("ntfy.token");
            File.WriteAllText(PasswordFile, "CANARY-passwordfile-1d3e\n");
            File.WriteAllText(TokenFile, TokenCanary + "\r\n");
            Settings(PasswordFile, TokenFile);
            Freeze();
            TransferPackage Read_ = ReadBack(Export(Everything(Password: true, Token: true, Topic: true), PassphraseCanary), PassphraseCanary);
            // the file is the password's source (the precedence), never the plain text beside it; one line break removed
            Assert.That(Read_.Entries.Where(E => E.IsSecret).Select(E => (E.Kind, E.Text)), Is.EqualTo(new[]
            {
                (TransferEntryKind.SecretSmtpPassword, "CANARY-passwordfile-1d3e"),
                (TransferEntryKind.SecretNtfyToken, TokenCanary),
                (TransferEntryKind.SecretNtfyTopic, TopicCanary)
            }));
            foreach (TransferSecretKind Only in Enum.GetValues<TransferSecretKind>())
            {
                var Selection = Everything(Password: Only == TransferSecretKind.SmtpPassword, Token: Only == TransferSecretKind.NtfyToken, Topic: Only == TransferSecretKind.NtfyTopic);
                TransferPackage One = ReadBack(Export(Selection, PassphraseCanary), PassphraseCanary);
                Assert.That(One.Entries.Where(E => E.IsSecret).Select(E => TransferFormat.SecretOf(E.Kind)), Is.EqualTo(new[] { Only }));
            }
        }

        [Test]
        public void ASecretOnlyInAnEnvironmentVariable_IsNotOffered_AndItsValueNeverRead()
        {
            Write("Email.yaml", "smtpServer: mail.example.com\nsmtpPassword: " + SmtpCanary + "\n");
            Write("Ntfy.yaml", "enabled: true\ntopic: " + TopicCanary + "\n");
            SmtpVariableSet = true;
            NtfyVariableSet = true;
            Freeze();
            TransferExportOffer Offer = TransferExport.Offer(Folder);
            var Selection = new TransferExportSelection { Email = true, Ntfy = true, SmtpPassword = true, NtfyToken = true };
            TransferFailure Password = Offer.SecretNote(TransferSecretKind.SmtpPassword, Selection)!;
            TransferFailure Token = Offer.SecretNote(TransferSecretKind.NtfyToken, Selection)!;
            Assert.That(Password.Problem, Is.EqualTo(TransferProblem.ExportSecretFromEnvironment));
            Assert.That(Password.Message, Does.Contain("CAMPERTRON_EMAIL_PASSWORD").And.Not.Contain(EnvironmentCanary));
            Assert.That(Token.Problem, Is.EqualTo(TransferProblem.ExportSecretFromEnvironment));
            Assert.That(Token.Message, Does.Contain("CAMPERTRON_NTFY_TOKEN").And.Not.Contain(EnvironmentCanary));
            // chosen anyway: the export stops, and the variable's value is nowhere
            foreach (TransferExportSelection Chosen in new[] { new TransferExportSelection { Email = true, SmtpPassword = true }, new TransferExportSelection { Ntfy = true, NtfyToken = true } })
            {
                TransferExportResult Result = Export(Chosen, PassphraseCanary);
                Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferProblem.ExportSecretFromEnvironment));
                Assert.That(Result.Package, Is.Null);
                Assert.That(Result.Failure.Message, Does.Not.Contain(EnvironmentCanary));
            }
            // the plain-text password is not the source while the variable is set, so it is not exported either
            TransferExportResult Plain_ = Export(new TransferExportSelection { Email = true });
            Assert.That(Text(Plain_.Package!), Does.Not.Contain(EnvironmentCanary));
            AssertNoCanary("the export", Plain_.Package!, SmtpCanary, EnvironmentCanary);
        }

        [Test]
        public void AConfiguredFile_StaysTheSourceWhileTheVariableIsSet()
        {
            string PasswordFile = In("smtp.password");
            File.WriteAllText(PasswordFile, "CANARY-passwordfile-1d3e");
            Write("Email.yaml", "smtpServer: mail.example.com\nsmtpPasswordFile: " + PasswordFile + "\n");
            SmtpVariableSet = true;
            Freeze();
            TransferPackage Read_ = ReadBack(Export(new TransferExportSelection { Email = true, SmtpPassword = true }, PassphraseCanary), PassphraseCanary);
            Assert.That(Read_.Entries.Last().Text, Is.EqualTo("CANARY-passwordfile-1d3e"));
        }

        [Test]
        public void ASecretFileThatCantBeUsed_DisablesItsChoice_ByTheProblemsKind_NeverThePath()
        {
            string Missing = In("absent.password");
            Write("Email.yaml", "smtpServer: mail.example.com\nsmtpPasswordFile: " + Missing + "\n");
            Write("Ntfy.yaml", "enabled: true\ntopic: " + TopicCanary + "\ntokenFile: relative/token\n");
            Freeze();
            TransferExportOffer Offer = TransferExport.Offer(Folder);
            TransferFailure Password = Offer.SecretNote(TransferSecretKind.SmtpPassword)!;
            TransferFailure Token = Offer.SecretNote(TransferSecretKind.NtfyToken)!;
            Assert.That(Password.Problem, Is.EqualTo(TransferProblem.ExportSecretFileUnusable));
            Assert.That(Password.Message, Does.Contain("it does not exist").And.Not.Contain(Missing).And.Not.Contain("absent.password"));
            Assert.That(Token.Message, Does.Contain("not a full path").And.Not.Contain("relative/token"));
            Assert.That(Export(new TransferExportSelection { Email = true, SmtpPassword = true }, PassphraseCanary).Failure!.Problem, Is.EqualTo(TransferProblem.ExportSecretFileUnusable));
        }

        [Test]
        public void ASecretThatIsNoUsableSecret_StopsTheExport_WithItsKindOnly()
        {
            string TokenFile = In("ntfy.token");
            File.WriteAllText(TokenFile, "token with spaces");
            Write("Email.yaml", "smtpServer: mail.example.com\nsmtpPassword: \"two\\nlines " + SmtpCanary + "\"\n");
            Write("Ntfy.yaml", "enabled: true\ntopic: " + TopicCanary + "\ntokenFile: " + TokenFile + "\n");
            Freeze();
            TransferExportResult Password = Export(new TransferExportSelection { Email = true, SmtpPassword = true }, PassphraseCanary);
            Assert.That(Password.Failure!.Problem, Is.EqualTo(TransferProblem.ExportSecretUnusable));
            Assert.That(Password.Failure.Secret, Is.EqualTo(TransferSecretKind.SmtpPassword));
            AssertNoCanary("the refusal", Password.Failure.Message);
            TransferExportResult Token = Export(new TransferExportSelection { Ntfy = true, NtfyToken = true }, PassphraseCanary);
            Assert.That(Token.Failure!.Problem, Is.EqualTo(TransferProblem.ExportSecretFileUnusable));
            Assert.That(Token.Failure.Message, Does.Contain("characters a token can't have"));
        }

        [Test]
        public void NoSecretOrSourceConfigured_DisablesTheChoice()
        {
            Write("Email.yaml", "smtpServer: mail.example.com\nsmtpPassword: ''\n");
            Write("Ntfy.yaml", "enabled: false\n");
            Freeze();
            TransferExportOffer Offer = TransferExport.Offer(Folder);
            foreach (TransferSecretKind Secret in Enum.GetValues<TransferSecretKind>())
            {
                Assert.That(Offer.SecretNote(Secret)!.Problem, Is.EqualTo(TransferProblem.ExportSecretNotConfigured), Secret.ToString());
            }
        }

        [Test]
        public void TheTopicOrPasswordElsewhereInItsFile_KeepsTheFileOut_ByName()
        {
            Write("Ntfy.yaml", "# old topic: " + TopicCanary + "\nenabled: true\ntopic: " + TopicCanary + "\n");
            Write("Email.yaml", "smtpServer: mail.example.com\nsmtpUsername: " + SmtpCanary + "\nsmtpPassword: " + SmtpCanary + "\n");
            Freeze();
            TransferExportResult Ntfy_ = Export(new TransferExportSelection { Ntfy = true });
            Assert.That(Ntfy_.Failure!.Problem, Is.EqualTo(TransferProblem.ExportFileHoldsSecret));
            Assert.That(Ntfy_.Failure.Name, Is.EqualTo("Ntfy.yaml"));
            AssertNoCanary("the refusal", Ntfy_.Failure.Message);
            Assert.That(Export(new TransferExportSelection { Email = true }).Failure!.Problem, Is.EqualTo(TransferProblem.ExportFileHoldsSecret));
        }

        [Test]
        public void AFileTheLoaderRefuses_TheEditorCantEdit_OrOver64KiB_IsNotExported_AndNamed()
        {
            Write("General.yaml", "outputTo: Console\nunknownKey: 1\n");
            Write("Email.yaml", "smtpPassword: &p " + SmtpCanary + "\nsmtpUsername: *p\n");
            Write("Trip.yaml", "displayName: Trip\nnotASearchKey: x\n");
            Write("Big.yaml", "displayName: Big\n# " + new string('x', 65_536) + "\n");
            Freeze();
            Assert.That(Export(new TransferExportSelection { General = true }).Failure!.Message, Is.EqualTo("General.yaml has settings Campertron can't read, so it can't be exported."));
            TransferFailure Email_ = Export(new TransferExportSelection { Email = true }).Failure!;
            Assert.That(Email_.Problem, Is.EqualTo(TransferProblem.ExportFileNotEditable));
            AssertNoCanary("the refusal", Email_.Message);
            Assert.That(Export(new TransferExportSelection { Searches = new[] { "Trip.yaml" } }).Failure!.Problem, Is.EqualTo(TransferProblem.ExportFileRefused));
            TransferFailure Big = Export(new TransferExportSelection { Searches = new[] { "Big.yaml" } }).Failure!;
            Assert.That(Big.Problem, Is.EqualTo(TransferProblem.ExportFileTooLarge));
            Assert.That(Big.Name, Is.EqualTo("Big.yaml"));
        }

        [Test]
        public void TheNeverCarriedFiles_AreNeverReadForAPackage_NorAcceptedAsAChoice()
        {
            Settings();
            foreach (string Name in new[] { "Web.yaml", "Web.password", "Web.keys", "preferences.json", "Camp.History", "RecreationDotOrg.db", "Email.yaml.bak",
                "Trip.yaml.0123456789abcdef0123456789abcdef.tmp", "Campertron.monitoring.lock", "notes.txt" })
            {
                Write(Name, "CANARY-nevercarried-" + Name + "\n");
            }
            Directory.CreateDirectory(In("CampertronCache"));
            File.WriteAllText(Path.Join(In("CampertronCache"), "cache.yaml"), "CANARY-nevercarried-cache\n");
            Freeze();
            TransferExportOffer Offer = TransferExport.Offer(Folder);
            Assert.That(Offer.Searches, Is.EqualTo(new[] { "Other trip.yaml", "Trip.yaml" }));
            TransferExportResult Result = Export(Everything());
            Assert.That(Text(Result.Package!), Does.Not.Contain("CANARY-nevercarried"));
            foreach (string Choice in new[] { "Web.yaml", "web.yaml", "Camp.History", "notes.txt", "../Trip.yaml", "CampertronCache/cache.yaml", "Email.yaml.bak", "Trip" })
            {
                Assert.That(Export(new TransferExportSelection { Searches = new[] { Choice } }).Failure!.Problem, Is.EqualTo(TransferProblem.NameRefused), Choice);
            }
            Assert.That(Export(new TransferExportSelection { Searches = new[] { "Absent.yaml" } }).Failure!.Message, Is.EqualTo("Absent.yaml is not in the settings folder, so it can't be exported."));
            Assert.That(Export(new TransferExportSelection { Searches = new[] { "Trip.yaml", "trip.yaml" } }).Failure!.Problem, Is.EqualTo(TransferProblem.DuplicateName));
        }

        [Test]
        public void TheChoices_AreCheckedBeforeAnythingIsRead()
        {
            Settings();
            Freeze();
            Assert.That(Export(new TransferExportSelection()).Failure!.Problem, Is.EqualTo(TransferProblem.ExportNothingChosen));
            Assert.That(Export(new TransferExportSelection { General = true, SmtpPassword = true }, PassphraseCanary).Failure!.Problem, Is.EqualTo(TransferProblem.ExportSecretWithoutFile));
            Assert.That(Export(new TransferExportSelection { Email = true, SmtpPassword = true }, "short").Failure!.Problem, Is.EqualTo(TransferProblem.PassphraseTooShort));
            Assert.Throws<ArgumentException>(() => Export(new TransferExportSelection { General = true }, PassphraseCanary), "a passphrase only with a secret");
            Assert.That(Derivations, Is.Zero);
        }

        [Test]
        public void TheExportingVersion_IsTheRunningOnesWithoutItsCommitHash()
        {
            Settings();
            Freeze();
            TransferPackage Read_ = ReadBack(TransferExport.Build(Folder, new TransferExportSelection { General = true }, null));
            Assert.That(Read_.ExportedBy, Is.EqualTo(TransferFormat.CurrentExportedBy()));
            Assert.That(TransferFormat.IsExportedBy(Read_.ExportedBy), Is.True);
            Assert.That(Read_.ExportedBy, Does.Not.Contain("+"));
        }

        [Test]
        public void TheSameFolderAndChoices_GiveTheSameBytes()
        {
            Settings();
            Freeze();
            byte[]? First = Export(Everything()).Package;
            byte[]? Second = Export(Everything()).Package;
            Assert.That(First, Is.Not.Null);
            Assert.That(Second, Is.EqualTo(First));
        }
    }
}
