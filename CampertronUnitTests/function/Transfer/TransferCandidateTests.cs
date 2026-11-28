using System.Text;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.Apply;

namespace CampertronUnitTests.function.Transfer
{
    // The candidate state (D134; v0.4.0-settings-transfer.md §Candidate-state construction, §Receiver-kept keys, §Text-preserving
    // YAML edits, §Validation, §Strict name model): built in memory from the receiver's saved files and the package, this
    // computer's own values put back by text-range edits, nothing deleted, a name only exactly, every written text through the
    // loader and the validation an editor's Save applies; the preview never shows a kept value
    public class TransferCandidateTests : TransferApplyTestBase
    {
        [Test]
        public void ThePreview_WritesNothing_AndReadsOnlyTheSavedFiles()
        {
            Receiver(Searches: new[] { "Mine.yaml" });
            Dictionary<string, string> Before = Snapshot();
            TransferPreview Preview = PreviewOf(ImportPackage(General(PackageGeneral), Email(PackageEmail), Search("Trip.yaml", PackageSearch), SmtpSecret()));
            Assert.That(Preview.CanApply, Is.True, string.Join(" ", Preview.Refusals.Select(R => R.Message)));
            Assert.That(Snapshot(), Is.EqualTo(Before), "a preview writes nothing, not even the lock");
            Assert.That(Steps, Is.Empty, "no step of an apply ran");
            AssertShowsNoSecretOrPath(Preview);
        }

        [Test]
        public void AnEditorsUnsavedChange_IsNeverPartOfTheCandidate()
        {
            Receiver();
            var Session = new ConfigEditSession<EmailConfig>(Folder);
            Session.Set("smtpServer", "unsaved.example.net");
            Session.Set("smtpPassword", "unsaved-password-value");
            Assert.That(Session.IsDirty, Is.True);
            Import(ImportPackage(Email(PackageEmail)));
            string Written = Read_("Email.yaml");
            Assert.That(Written, Does.Contain("smtpPassword: " + ReceiverPasswordCanary), "the saved password, never the unsaved one");
            Assert.That(Written, Does.Not.Contain("unsaved"));
        }

        [Test]
        public void EverySettingsKind_IsCreated_OrReplaced_AndNamedSo()
        {
            Receiver(General: true, Email: false, Ntfy: true);
            TransferPreview Preview = PreviewOf(ImportPackage(General(PackageGeneral), Email(PackageEmail), Ntfy(PackageNtfy), Search("Trip.yaml", PackageSearch)));
            Assert.That(Preview.Files.Select(F => (F.Name, F.Change)), Is.EquivalentTo(new[]
            {
                ("General.yaml", TransferImportFileChange.Replaced), ("Email.yaml", TransferImportFileChange.Created),
                ("Ntfy.yaml", TransferImportFileChange.Replaced), ("Trip.yaml", TransferImportFileChange.Created)
            }));
            Assert.That(Preview.Lines(), Does.Contain("General.yaml will be replaced.").And.Contain("Email.yaml will be created.").And.Contain("Trip.yaml will be created."));
            Applied(Preview);
            Assert.That(Read_("Trip.yaml"), Is.EqualTo(PackageSearch));
        }

        [Test]
        public void ASearchIsAdded_OrReplacedByItsExactName_AndNoOtherIsEverDeleted()
        {
            Receiver(Searches: new[] { "Here.yaml", "Mine.yaml", "Other one.yaml" });
            Dictionary<string, string> Kept = Snapshot(R => R == "Here.yaml" || !R.EndsWith(".yaml", StringComparison.Ordinal) || R == "General.yaml" || R == "Email.yaml" || R == "Ntfy.yaml");
            TransferPreview Preview = PreviewOf(ImportPackage(Search("Here.yaml", PackageSearch), Search("New trip.yaml", PackageSearch)));
            Assert.That(Preview.Preserved, Is.EqualTo(new[] { "Mine.yaml", "Other one.yaml" }));
            Assert.That(Preview.Lines(), Does.Contain(TransferImportWording.OthersNotTouched));
            Applied(Preview);
            Assert.That(Read_("Here.yaml"), Is.EqualTo(PackageSearch));
            Assert.That(Read_("New trip.yaml"), Is.EqualTo(PackageSearch));
            Assert.That(Snapshot(R => R == "Here.yaml" || !R.EndsWith(".yaml", StringComparison.Ordinal) || R == "General.yaml" || R == "Email.yaml" || R == "Ntfy.yaml" || R == "New trip.yaml"),
                Is.EqualTo(Kept), "this computer's other search files are exactly as they were");
            Assert.That(ConfigFiles.SortedSearchFileNames(Folder), Is.EqualTo(new[] { "Here.yaml", "Mine.yaml", "New trip.yaml", "Other one.yaml" }));
        }

        [Test]
        public void ANameThatDiffersInCaseOnly_RefusesThePackage_AndNothingIsWritten()
        {
            Receiver(Searches: new[] { "trip.yaml" });
            Dictionary<string, string> Before = Snapshot();
            TransferPreview Preview = PreviewOf(ImportPackage(General(PackageGeneral), Search("Trip.yaml", PackageSearch)));
            Assert.That(Preview.CanApply, Is.False);
            Assert.That(Preview.Refusals.Single().Problem, Is.EqualTo(TransferImportProblem.CaseOnlyConflict));
            Assert.That(Preview.Refusals.Single().Message, Is.EqualTo("Trip.yaml differs from a file on this computer only in upper and lower case, so this settings package can't be imported."));
            Assert.That(Applied(Preview).Outcome, Is.EqualTo(TransferApplyOutcome.Refused));
            Assert.That(Snapshot(), Is.EqualTo(Before));
        }

        [Test]
        public void AnInvalidSearch_RefusesThePackage_ByItsName_InTheValidationsWords()
        {
            Receiver();
            TransferPreview Preview = PreviewOf(ImportPackage(General(PackageGeneral), Search("Bad.yaml", "displayName: Bad\nautoRun: true\ncampgroundID: '1'\nsearchBy: DaysOut\nsearchValue: -3\n")));
            Assert.That(Preview.CanApply, Is.False);
            TransferImportFailure Refusal = Preview.Refusals.Single();
            Assert.That(Refusal.Problem, Is.EqualTo(TransferImportProblem.PackageFileRefused));
            Assert.That(Refusal.Name, Is.EqualTo("Bad.yaml"));
            Assert.That(Refusal.Message, Does.StartWith("Bad.yaml can't be imported as it would be: "));
            string Expected = ConfigValidation.ValidateSearch("displayName: Bad\nautoRun: true\ncampgroundID: '1'\nsearchBy: DaysOut\nsearchValue: -3\n", "Bad.yaml", DateTime.Now)
                .First(D => D.Severity == ConfigSeverity.Error).Message;
            Assert.That(Refusal.Message, Does.EndWith(Expected), "the validation's own words");
        }

        [Test]
        public void ASettingsTextTheLoaderRefuses_RefusesThePackage_ByItsName()
        {
            Receiver();
            TransferPreview Preview = PreviewOf(ImportPackage(General("outputTo: Console\ncolour: red\n")));
            Assert.That(Preview.Refusals.Select(R => (R.Problem, R.Name)), Is.EqualTo(new[] { (TransferImportProblem.PackageFileUnloadable, (string?)"General.yaml") }), "an unknown key is never dropped");
        }

        [Test]
        public void AReceiverFileThatCanNotBeRead_OrLoaded_IsNotReplaced()
        {
            Write("Email.yaml", "smtpServer: x\n");
            File.WriteAllBytes(In("Email.yaml"), Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("smtpServer: x\n")).ToArray());
            Write("General.yaml", "outputTo: Console\nsomethingNew: 1\n");
            TransferPreview Preview = PreviewOf(ImportPackage(General(PackageGeneral), Email(PackageEmail)));
            Assert.That(Preview.Refusals.Select(R => (R.Problem, R.Name)), Is.EquivalentTo(new[]
            {
                (TransferImportProblem.ReceiverUnloadable, (string?)"General.yaml"), (TransferImportProblem.ReceiverUnreadable, (string?)"Email.yaml")
            }));
        }

        [Test]
        public void EveryReceiverKeptKey_IsTheReceiversOwn_WhenItHasOne()
        {
            Receiver();
            Write("Email.yaml", ReceiverEmail + "smtpPasswordFile: " + In("my-password.txt") + "\n");
            Write("Ntfy.yaml", ReceiverNtfy + "tokenFile: " + In("my-token.txt") + "\n");
            // a package someone made by hand, holding the other computer's values of those keys
            string Theirs = PackageEmail + "smtpPassword: their-password-value\nsmtpPasswordFile: /their/password\n";
            string TheirNtfy = PackageNtfy + "topic: theirtopic\ntokenFile: /their/token\n";
            string TheirGeneral = PackageGeneral + "lastRidbDataRefresh: 2020-01-01T00:00:00.0000000Z\n";
            TransferPreview Preview = PreviewOf(ImportPackage(General(TheirGeneral), Email(Theirs), Ntfy(TheirNtfy)));
            Assert.That(Preview.Kept, Is.EquivalentTo(new[]
            {
                ("General.yaml", "lastRidbDataRefresh"), ("Email.yaml", "smtpPassword"), ("Email.yaml", "smtpPasswordFile"), ("Ntfy.yaml", "topic"), ("Ntfy.yaml", "tokenFile")
            }));
            Assert.That(Preview.Lines(), Does.Contain("smtpPassword in Email.yaml is kept from this computer."));
            Applied(Preview);
            EmailConfig E = Yaml.LoadSettingsText<EmailConfig>(Read_("Email.yaml"))!;
            Assert.That(E.SmtpPassword, Is.EqualTo(ReceiverPasswordCanary));
            Assert.That(E.SmtpPasswordFile, Is.EqualTo(In("my-password.txt")));
            NtfyConfig N = Yaml.LoadSettingsText<NtfyConfig>(Read_("Ntfy.yaml"))!;
            Assert.That(N.Topic, Is.EqualTo(ReceiverTopicCanary));
            Assert.That(N.TokenFile, Is.EqualTo(In("my-token.txt")));
            GeneralConfig G = Yaml.LoadSettingsText<GeneralConfig>(Read_("General.yaml"))!;
            Assert.That(G.LastRidbDataRefresh, Is.EqualTo(Yaml.LoadSettingsText<GeneralConfig>(ReceiverGeneral)!.LastRidbDataRefresh));
            Assert.That(Read_("Email.yaml") + Read_("Ntfy.yaml") + Read_("General.yaml"), Does.Not.Contain("their-password-value").And.Not.Contain("/their/").And.Not.Contain("theirtopic").And.Not.Contain("2020-01-01"));
        }

        [Test]
        public void AReceiverKeptKey_IsLeftOut_WhenTheReceiverHasNone()
        {
            string Theirs = PackageEmail + "smtpPassword: their-password-value\nsmtpPasswordFile: /their/password\n";
            string TheirNtfy = PackageNtfy + "topic: theirtopic\ntokenFile: /their/token\n";
            string TheirGeneral = PackageGeneral + "lastRidbDataRefresh: 2020-01-01T00:00:00.0000000Z\n";
            TransferPreview Preview = PreviewOf(ImportPackage(General(TheirGeneral), Email(Theirs), Ntfy(TheirNtfy)));
            Assert.That(Preview.Kept, Is.Empty, "nothing of this computer's to keep");
            Applied(Preview);
            Assert.That(Read_("Email.yaml"), Is.EqualTo(PackageEmail), "the keys removed by text edits, the rest byte for byte");
            Assert.That(Read_("Ntfy.yaml"), Is.EqualTo(PackageNtfy));
            Assert.That(Read_("General.yaml"), Is.EqualTo(PackageGeneral));
        }

        [Test]
        public void TheTextIsEdited_NeverSerializedAgain_CommentsQuotingAndLineEndingsKept()
        {
            Receiver();
            string Crlf = "# their header\r\noutputTo: 'Console'   # quoted\r\nrefreshRidbDataDayInterval: 30\r\n\r\n# a comment line\r\nautoRefresh: false\r\n";
            Import(ImportPackage(General(Crlf)));
            byte[] Written = BytesOf("General.yaml");
            string Text_ = Encoding.UTF8.GetString(Written);
            Assert.That(Text_, Does.StartWith("# their header\r\noutputTo: 'Console'   # quoted\r\nrefreshRidbDataDayInterval: 30\r\n\r\n# a comment line\r\nautoRefresh: false\r\n"),
                "every line of the package's text as it was");
            Assert.That(Text_, Does.Contain("lastRidbDataRefresh: ").And.EndWith("\r\n"), "this computer's value inserted with the file's line ending");
            Assert.That(Text_.Replace("\r\n", string.Empty).Contains('\n'), Is.False, "no other line ending");
        }

        [Test]
        public void ASearchText_TravelsByteForByte_ByteOrderMarkAndLineEndingsIncluded()
        {
            string Text_ = "﻿# trip\r\ndisplayName: Trip\r\nautoRun: false\r\ncampgroundID: '232447'\r\n";
            Import(ImportPackage(Search("Trip.yaml", Text_)));
            Assert.That(BytesOf("Trip.yaml"), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Text_.Substring(1))).ToArray()));
        }

        [Test]
        public void AFileLeftOut_IsNotWritten_AndNothingChosen_CanNotBeApplied()
        {
            Receiver();
            Dictionary<string, string> Before = Snapshot();
            TransferPreview Preview = PreviewOf(ImportPackage(General(PackageGeneral), Email(PackageEmail)), new TransferImportChoices { LeftOut = new[] { "Email.yaml" } });
            Assert.That(Preview.Files.Single(F => F.Name == "Email.yaml").Change, Is.EqualTo(TransferImportFileChange.LeftOut));
            Assert.That(Preview.Lines(), Does.Contain("Email.yaml is left out of the import; this computer's is not changed."));
            Applied(Preview);
            Assert.That(Read_("Email.yaml"), Is.EqualTo(ReceiverEmail));

            TransferPreview None = PreviewOf(ImportPackage(General(PackageGeneral)), new TransferImportChoices { LeftOut = new[] { "General.yaml" } });
            Assert.That(None.Refusals.Single().Problem, Is.EqualTo(TransferImportProblem.NothingChosen));
        }

        [Test]
        public void AFileThatIsTheSame_IsNotWritten_AndAPackageOfSameFiles_IsNothingToImport()
        {
            Receiver(Searches: new[] { "Trip.yaml" });
            Write("Trip.yaml", PackageSearch);
            DateTime Time = File.GetLastWriteTimeUtc(In("Trip.yaml"));
            Import(ImportPackage(General(PackageGeneral), Search("Trip.yaml", PackageSearch)));
            Assert.That(Steps, Does.Not.Contain("write:Trip.yaml"));
            Assert.That(File.GetLastWriteTimeUtc(In("Trip.yaml")), Is.EqualTo(Time));
            Assert.That(File.Exists(In("Trip.yaml.bak")), Is.False, "never replaced, so no rolling backup");

            TransferPreview Same = PreviewOf(ImportPackage(Search("Trip.yaml", PackageSearch)));
            Assert.That(Same.Files.Single().Change, Is.EqualTo(TransferImportFileChange.Unchanged));
            Assert.That(Same.Refusals.Single().Problem, Is.EqualTo(TransferImportProblem.NothingToChange));
            Assert.That(Applied(Same).Failure!.Problem, Is.EqualTo(TransferImportProblem.NotApplicable));
        }

        [Test]
        public void NoExampleSearch_IsCreated_AndNoSearchFileIsMadeButThePackages()
        {
            Import(ImportPackage(General(PackageGeneral)));
            Assert.That(ConfigFiles.SearchFiles(Folder), Is.Empty, "an import never creates the example search (ER-15)");
            Assert.That(File.Exists(In("ZionConfig.yaml")), Is.False);
        }

        [Test]
        public void ASearchFile_IsCarriedAsText_SoTheSearchCorrectionsOfTD65AndTD66_AreNotDecidedHere()
        {
            // the import neither adds nor changes a search setting: a search file is written exactly as the package holds it,
            // whatever keys the corrections of TD-65 and TD-66 give it later, and New search is never run
            string Full = "displayName: Every key\nautoRun: false\ncampgroundID: '232447'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\nshowMonday: true\nshowTuesday: false\nshowWednesday: true\nshowThursday: false\nshowFriday: true\nshowSaturday: true\nshowSunday: false\nfilterMatching: Exact\n";
            Import(ImportPackage(Search("Every key.yaml", Full)));
            Assert.That(Read_("Every key.yaml"), Is.EqualTo(Full));
            CampertronConfig Loaded = Yaml.LoadSettingsText<CampertronConfig>(Read_("Every key.yaml"))!;
            Assert.That(Loaded.AutoRun, Is.False);
            Assert.That(Loaded.ShowTuesday, Is.False, "the days as the package has them, not New search's defaults");
        }

        [Test]
        public void TheFilesAnImportNeverCarries_AreNeverTouched()
        {
            Receiver(Searches: new[] { "Mine.yaml" });
            Write("Web.yaml", "passwordProtection: true\n");
            Write("Web.password", "pbkdf2-record\n");
            Directory.CreateDirectory(In("Web.keys"));
            Write(Path.Join("Web.keys", "key-1.xml"), "<key/>");
            Write("Camp.History", "232447|2026-12-01\n");
            Write("RecreationDotOrg.db", "not a database");
            Write("preferences.json", "{}");
            Write("notes.txt", "mine");
            Write("Email.password", "a user's own password file");
            Write("Ntfy.token", "a user's own token file");
            Write("General.yaml.bak", "an earlier backup");
            Directory.CreateDirectory(In("cache"));
            Write(Path.Join("cache", "232447.json"), "{}");
            string[] Never = { "Web.yaml", "Web.password", Path.Join("Web.keys", "key-1.xml"), "Camp.History", "RecreationDotOrg.db", "preferences.json", "notes.txt", "Email.password", "Ntfy.token",
                Path.Join("cache", "232447.json"), "Mine.yaml" };
            Dictionary<string, string> Before = Snapshot(R => !Never.Contains(R));
            var Times = Never.ToDictionary(N => N, N => File.GetLastWriteTimeUtc(In(N)));
            Import(ImportPackage(General(PackageGeneral), Email(PackageEmail), Ntfy(PackageNtfy), Search("Trip.yaml", PackageSearch), SmtpSecret(), TokenSecret()));
            Assert.That(Snapshot(R => !Never.Contains(R)), Is.EqualTo(Before));
            foreach (string Name in Never)
            {
                Assert.That(File.GetLastWriteTimeUtc(In(Name)), Is.EqualTo(Times[Name]), Name);
            }
            Assert.That(File.Exists(In("Campertron.monitoring.lock")), Is.False, "no monitoring was started (its lock never taken)");
        }
    }
}
