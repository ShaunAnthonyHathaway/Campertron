using CampertronLibrary.function.Transfer;

namespace CampertronUnitTests.function.Transfer
{
    // The allow-list of kinds and the strict name model (D132; v0.4.0-settings-transfer.md §Entry kinds, §Strict name model,
    // §Never-carried files and state): names, never paths; settings names exactly; a search name exactly what
    // SearchFileName.Check gives; no two names that differ only in case; an unknown kind refuses the whole package
    public class TransferNamesAndKindsTests : TransferTestBase
    {
        private static TransferProblem? Created(params (TransferEntryKind, string, string)[] Entries)
        {
            TransferPackage.Create(ExportedBy, Entries, out TransferFailure? Failure);
            return Failure?.Problem;
        }

        private static TransferProblem? ReadPlain(params (string Kind, string Name, string Text)[] Entries) => Problem(Bytes(PlainJson(PayloadJson(Entries))));

        [Test]
        public void TheSettingsNames_AreExact_InEveryOtherSpellingRefused()
        {
            Assert.That(Created((TransferEntryKind.General, "General.yaml", "x: 1\n"), (TransferEntryKind.Email, "Email.yaml", "x: 1\n"), (TransferEntryKind.Ntfy, "Ntfy.yaml", "x: 1\n")), Is.Null);
            foreach (string Wrong in new[] { "general.yaml", "GENERAL.YAML", "General.yml", "General", "./General.yaml", "General.yaml ", " General.yaml", "Email.yaml" })
            {
                Assert.That(Created((TransferEntryKind.General, Wrong, "x: 1\n")), Is.EqualTo(TransferProblem.NameRefused), Wrong);
                Assert.That(ReadPlain(("general", Wrong, "x: 1\n")), Is.EqualTo(TransferProblem.NameRefused), Wrong);
            }
            Assert.That(ReadPlain(("email", "General.yaml", "x: 1\n")), Is.EqualTo(TransferProblem.NameRefused));
            Assert.That(ReadPlain(("ntfy", "ntfy.yaml", "x: 1\n")), Is.EqualTo(TransferProblem.NameRefused));
        }

        [Test]
        public void ASecretsName_IsExact()
        {
            Assert.That(Created(Email(), (TransferEntryKind.SecretSmtpPassword, "SmtpPassword", SmtpCanary)), Is.EqualTo(TransferProblem.NameRefused));
            Assert.That(Created(Ntfy(), (TransferEntryKind.SecretNtfyTopic, "topic", TopicCanary)), Is.EqualTo(TransferProblem.NameRefused));
        }

        [TestCase("Trip.yaml")]
        [TestCase("My trip (2) - copy_1.yaml")]
        [TestCase("Caf\u00E9 \u00E9t\u00E9.yaml")]
        [TestCase("a.b.yaml")]
        [TestCase("Camp.History.yaml")]
        public void AValidSearchName_IsCarried(string Name)
        {
            Assert.That(Read(Plain(Package(Search(Name)))).Package!.Entries.Single().Name, Is.EqualTo(Name));
        }

        [TestCase("Trip", Description = "without .yaml: Check would add it, so it is not the name Check gives")]
        [TestCase("Trip.YAML", Description = "Check gives Trip.yaml")]
        [TestCase("Trip.yml")]
        [TestCase("../Trip.yaml")]
        [TestCase("..\\Trip.yaml")]
        [TestCase("a/../../etc/passwd.yaml")]
        [TestCase("/etc/Trip.yaml")]
        [TestCase("C:\\Users\\Trip.yaml")]
        [TestCase("C:Trip.yaml")]
        [TestCase("sub/Trip.yaml")]
        [TestCase("sub\\Trip.yaml")]
        [TestCase("..yaml")]
        [TestCase(".hidden.yaml")]
        [TestCase("Trip..yaml")]
        [TestCase(" Trip.yaml")]
        [TestCase("Trip .yaml")]
        [TestCase("Trip*.yaml")]
        [TestCase("Trip:Stream.yaml")]
        [TestCase("Trip\0.yaml")]
        [TestCase("Trip\n.yaml")]
        [TestCase("CON.yaml")]
        [TestCase("nul.yaml")]
        [TestCase("COM1.yaml")]
        [TestCase("LPT9.backup.yaml")]
        [TestCase("General.yaml")]
        [TestCase("general.yaml")]
        [TestCase("EMAIL.yaml")]
        [TestCase("Ntfy.yaml")]
        [TestCase("Web.yaml")]
        [TestCase("web.YAML")]
        [TestCase("")]
        public void AnInvalidSearchName_RefusesTheWholePackage_AndIsNeverNamed(string Name)
        {
            Assert.That(Created(General(), Search(Name)), Is.EqualTo(TransferProblem.NameRefused), Name);
            TransferReadResult Result = Read(Bytes(PlainJson(PayloadJson(new[] { ("general", "General.yaml", "x: 1\n"), ("search", Name, "x: 1\n") }))));
            Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferProblem.NameRefused), Name);
            Assert.That(Result.Failure.Name, Is.Null);
            if (Name.Length > 0)
            {
                Assert.That(Result.Failure.Message, Does.Not.Contain(Name));
            }
        }

        [Test]
        public void ALongSearchName_UpToTheLimit()
        {
            string Longest = new string('a', 100) + ".yaml";
            Assert.That(Created(Search(Longest)), Is.Null);
            Assert.That(Created(Search(new string('a', 101) + ".yaml")), Is.EqualTo(TransferProblem.NameRefused));
        }

        [Test]
        public void TheNeverCarriedFiles_AreRefusedByName_InEveryKind()
        {
            foreach (string Name in new[] { "Web.yaml", "Web.password", "Web.keys", "Camp.History", "preferences.json", "RecreationDotOrg.db", "RecreationDotOrg.db.bak",
                "Email.yaml.bak", "Ntfy.yaml.0123456789abcdef0123456789abcdef.tmp", "Campertron.monitoring.lock", "restart.json", "notes.txt", "Trip.yaml.bak" })
            {
                foreach (string Kind in new[] { "search", "general", "email", "ntfy" })
                {
                    Assert.That(ReadPlain((Kind, Name, "x: 1\n")), Is.EqualTo(TransferProblem.NameRefused), Kind + " " + Name);
                }
                Assert.That(Created(Search(Name)), Is.EqualTo(TransferProblem.NameRefused), Name);
            }
        }

        [Test]
        public void ADuplicateName_AndNamesThatDifferOnlyInCase_AreRefused_AndNamed()
        {
            Assert.That(Created(Search("Trip.yaml"), Search("Trip.yaml")), Is.EqualTo(TransferProblem.DuplicateName));
            Assert.That(Created(Search("Trip.yaml"), Search("TRIP.yaml")), Is.EqualTo(TransferProblem.DuplicateName));
            Assert.That(Created(General(), General()), Is.EqualTo(TransferProblem.DuplicateName), "one entry of a kind but search");
            Assert.That(Created(Email(), SmtpSecret(), SmtpSecret("other")), Is.EqualTo(TransferProblem.DuplicateName));
            TransferReadResult Result = Read(Bytes(PlainJson(PayloadJson(new[] { ("search", "Trip.yaml", "a: 1\n"), ("search", "trip.yaml", "b: 2\n") }))));
            Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferProblem.DuplicateName));
            Assert.That(Result.Failure.Name, Is.EqualTo("trip.yaml"));
            Assert.That(Result.Failure.Message, Does.Contain("trip.yaml").And.Not.Contain("a: 1").And.Not.Contain("b: 2"));
            Assert.That(ReadPlain(("general", "General.yaml", "a: 1\n"), ("general", "General.yaml", "a: 1\n")), Is.EqualTo(TransferProblem.DuplicateName));
        }

        [TestCase("Search")]
        [TestCase("web")]
        [TestCase("history")]
        [TestCase("secret")]
        [TestCase("secret-web-password")]
        [TestCase("general ")]
        [TestCase("")]
        public void AnUnknownKind_RefusesTheWholePackage_NeverSkipped(string Kind)
        {
            TransferReadResult Result = Read(Bytes(PlainJson(PayloadJson(new[] { ("general", "General.yaml", "x: 1\n"), (Kind, "Trip.yaml", "x: 1\n") }))));
            Assert.That(Result.Package, Is.Null);
            Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferProblem.UnknownKind));
            Assert.That(Result.Failure.Name, Is.Null);
        }

        [Test]
        public void TheAllowList_IsExactlyTheDesignsSevenKinds()
        {
            Assert.That(TransferFormat.Kinds.Select(K => K.Text), Is.EqualTo(new[] { "general", "email", "ntfy", "search", "secret-smtp-password", "secret-ntfy-token", "secret-ntfy-topic" }));
            Assert.That(Enum.GetValues<TransferEntryKind>(), Has.Length.EqualTo(7));
            Assert.That(TransferFormat.Kinds.Where(K => TransferFormat.IsSecret(K.Kind)).Select(K => TransferFormat.FixedName(K.Kind)), Is.EqualTo(new[] { "smtpPassword", "ntfyToken", "ntfyTopic" }));
        }
    }
}
