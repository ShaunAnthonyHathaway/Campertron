using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Forms
{
    // The guided setup's status (v0.3.0's PR 10, D100; business-rules-settings §37), at the shared form layer's own level:
    // what the saved files of a settings folder say, from nothing to everything (Unconfigured, Partial, Ready, Complete),
    // read by the monitoring start's own checks (MonitoringStartErrors, MonitoringSearchErrors, the secret files' metadata),
    // never from a record of its own. Reading creates, writes and deletes nothing, opens no secret file and reads no
    // environment variable. Temporary folders only; no network, no database.
    public class GuidedSetupFormsTests : ConfigEditTestBase
    {
        private const string Password = "Guided-S3cret-Canary";
        private const string Topic = "guidedCanaryTopic0123456789";
        private const string ValidEmail = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: guided-user\nsmtpPassword: " + Password +
            "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";
        private const string ValidNtfy = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n";

        private static string Search(string Name, bool AutoRun, string CampgroundLine = "campgroundID: '232445'") =>
            "displayName: " + Name + "\nautoRun: " + (AutoRun ? "true" : "false") + "\n" + CampgroundLine + "\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";

        private static string GeneralWith(string Output) => "outputTo: " + Output + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n";

        private void Write(string Name, string Text) => File.WriteAllText(Path.Join(Folder, Name), Text);

        // every file of the folder with its bytes and time of writing: reading the status changes none of them
        private Dictionary<string, (string Text, DateTime Written)> Snapshot() => Directory.GetFileSystemEntries(Folder, "*", SearchOption.AllDirectories)
            .ToDictionary(Entry => Path.GetRelativePath(Folder, Entry), Entry => File.Exists(Entry) ? (File.ReadAllText(Entry), File.GetLastWriteTimeUtc(Entry)) : ("(folder)", Directory.GetLastWriteTimeUtc(Entry)));

        private GuidedSetupStatus Read()
        {
            Dictionary<string, (string, DateTime)> Before = Snapshot();
            GuidedSetupStatus Status = GuidedSetupStatus.Read(Folder, DateTime.Now);
            Assert.That(Snapshot(), Is.EqualTo(Before), "reading the status creates, writes and deletes nothing");
            return Status;
        }

        [Test]
        public void AnEmptyFolder_IsUnconfigured_AndReadingItCreatesNothing()
        {
            GuidedSetupStatus Status = Read();
            Assert.That(Directory.GetFileSystemEntries(Folder), Is.Empty, "no General.yaml, Email.yaml, search file or anything else");
            Assert.That(Status.IsUnconfigured, Is.True);
            Assert.That((Status.Overall, Status.State(GuidedSetupMethod.Email), Status.State(GuidedSetupMethod.Ntfy)),
                Is.EqualTo((GuidedSetupState.Unconfigured, GuidedSetupState.Unconfigured, GuidedSetupState.Unconfigured)));
            Assert.That(Status.SuggestedMethod, Is.Null, "the user chooses");
            Assert.That((Status.SavedOutput, Status.GeneralExists, Status.SearchFileCount), Is.EqualTo(((OutputType?)null, false, 0)));
            Assert.That(Status.NextStep(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupStep.Notifications));
            Assert.That(Status.NextStep(GuidedSetupMethod.Ntfy), Is.EqualTo(GuidedSetupStep.Notifications));
            Assert.That(Status.StartProblems(GuidedSetupMethod.Email), Has.Some.StartWith("Email.yaml: "), "a missing Email.yaml is the start's own error for email");
            Assert.That(Status.StartProblems(GuidedSetupMethod.Ntfy), Has.Some.StartWith("Ntfy.yaml: "), "and a missing Ntfy.yaml for ntfy");
            Assert.That(Status.SearchProblems, Has.Count.EqualTo(1).And.Some.Contain("needs at least one enabled search"));
        }

        [Test]
        public void TheFilesAConsoleStartWrites_AreStillUnconfigured()
        {
            //the samples a first start of the console (or CampertronGUI's Create) writes: outputTo Console, an empty Email.yaml
            Assert.That(Yaml.CreateSampleSettingsFile(ConfigFileKind.General, Folder, null), Is.True);
            Assert.That(Yaml.CreateSampleSettingsFile(ConfigFileKind.Email, Folder, null), Is.True);
            GuidedSetupStatus Status = Read();
            Assert.That(Status.SavedOutput, Is.EqualTo(OutputType.Console));
            Assert.That(Status.GeneralExists, Is.True);
            Assert.That(Status.IsUnconfigured, Is.True, "nothing a notification-only user needs is saved");
            Assert.That(Status.NotificationsReady(GuidedSetupMethod.Email), Is.False);
            Assert.That(Status.StartProblems(GuidedSetupMethod.Email), Has.Some.Contain("smtpServer is empty"), "the start's words for an empty Email.yaml");
        }

        [Test]
        public void AUsableEmailYaml_IsPartial_SuggestsEmail_AndTheSearchIsNext()
        {
            Write("Email.yaml", ValidEmail);
            GuidedSetupStatus Status = Read();
            Assert.That(Status.IsUnconfigured, Is.False);
            Assert.That(Status.NotificationsReady(GuidedSetupMethod.Email), Is.True);
            Assert.That(Status.NotificationsReady(GuidedSetupMethod.Ntfy), Is.False);
            Assert.That(Status.SuggestedMethod, Is.EqualTo(GuidedSetupMethod.Email));
            Assert.That(Status.Overall, Is.EqualTo(GuidedSetupState.Partial));
            Assert.That(Status.NextStep(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupStep.Search));
            Assert.That(string.Join(" ", Status.StartProblems(GuidedSetupMethod.Email).Concat(Status.SearchProblems)), Does.Not.Contain(Password), "no message holds a value");
        }

        [Test]
        public void Ntfy_IsReadyOnlyTurnedOn_AndADisabledOneIsSaidInTheStartsWords()
        {
            Write("Ntfy.yaml", ValidNtfy.Replace("enabled: true", "enabled: false"));
            GuidedSetupStatus Off = Read();
            Assert.That(Off.NotificationsReady(GuidedSetupMethod.Ntfy), Is.False, "outputTo: Ntfy needs ntfy turned on");
            Assert.That(Off.IsUnconfigured, Is.True, "a disabled Ntfy.yaml alone is nothing a monitoring could use");
            Assert.That(Off.StartProblems(GuidedSetupMethod.Ntfy), Has.Some.StartWith("Ntfy.yaml: "));
            Assert.That(Off.StartProblems(GuidedSetupMethod.Email), Is.Empty.Or.All.Not.StartWith("Ntfy.yaml"), "an Ntfy.yaml that is off is not email's concern");

            Write("Ntfy.yaml", ValidNtfy);
            GuidedSetupStatus On = Read();
            Assert.That(On.NotificationsReady(GuidedSetupMethod.Ntfy), Is.True);
            Assert.That(On.SuggestedMethod, Is.EqualTo(GuidedSetupMethod.Ntfy));
            Assert.That(On.NextStep(GuidedSetupMethod.Ntfy), Is.EqualTo(GuidedSetupStep.Search));
            Assert.That(string.Join(" ", On.StartProblems(GuidedSetupMethod.Ntfy)), Does.Not.Contain(Topic), "the topic is never in a message");
        }

        [Test]
        public void ASearchFileAlone_IsNoLongerUnconfigured_ButNeitherMethodIsReady()
        {
            Write("Trip.yaml", Search("Guided Trip", AutoRun: false));
            GuidedSetupStatus Status = Read();
            Assert.That(Status.IsUnconfigured, Is.False, "a search file is something the user (or the console's Search) saved");
            Assert.That(Status.SearchReady, Is.False, "it is not enabled");
            Assert.That(Status.SearchProblems.Single(), Does.Contain("No search is Enabled (autoRun)"));
            Assert.That(Status.Overall, Is.EqualTo(GuidedSetupState.Partial));
        }

        [Test]
        public void TheNotificationsAndAnEnabledSearch_AreReady_UntilOutputToIsTheMethod_ThenComplete()
        {
            Write("Email.yaml", ValidEmail);
            Write("Trip.yaml", Search("Guided Trip", AutoRun: true));
            Write("General.yaml", GeneralWith("Console"));
            GuidedSetupStatus Ready = Read();
            Assert.That(Ready.FilesReady(GuidedSetupMethod.Email), Is.True);
            Assert.That(Ready.State(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupState.Ready));
            Assert.That(Ready.NextStep(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupStep.TurnOn));
            Assert.That(Ready.IsOn(GuidedSetupMethod.Email), Is.False);

            Write("General.yaml", GeneralWith("Email"));
            GuidedSetupStatus Complete = Read();
            Assert.That(Complete.State(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupState.Complete));
            Assert.That((Complete.Overall, Complete.SuggestedMethod), Is.EqualTo((GuidedSetupState.Complete, (GuidedSetupMethod?)GuidedSetupMethod.Email)));
            Assert.That(Complete.NextStep(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupStep.Done));
            Assert.That(Complete.State(GuidedSetupMethod.Ntfy), Is.EqualTo(GuidedSetupState.Partial), "the other method is judged by its own file");
        }

        [Test]
        public void NtfyComplete_NeedsNoEmailYaml()
        {
            Write("Ntfy.yaml", ValidNtfy);
            Write("Trip.yaml", Search("Guided Trip", AutoRun: true));
            Write("General.yaml", GeneralWith("Ntfy"));
            GuidedSetupStatus Status = Read();
            Assert.That(File.Exists(Path.Join(Folder, "Email.yaml")), Is.False);
            Assert.That(Status.State(GuidedSetupMethod.Ntfy), Is.EqualTo(GuidedSetupState.Complete));
            Assert.That(Status.SuggestedMethod, Is.EqualTo(GuidedSetupMethod.Ntfy));
        }

        [Test]
        public void OutputToEmailWithAnEmailYamlTheStartRefuses_IsNeverComplete()
        {
            Write("Email.yaml", ValidEmail.Replace("smtpServer: smtp.example.invalid", "smtpServer: ''"));
            Write("Trip.yaml", Search("Guided Trip", AutoRun: true));
            Write("General.yaml", GeneralWith("Email"));
            GuidedSetupStatus Status = Read();
            Assert.That(Status.IsOn(GuidedSetupMethod.Email), Is.True);
            Assert.That(Status.State(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupState.Partial), "no record hides a configuration the monitoring would refuse");
            Assert.That(Status.NextStep(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupStep.Notifications));
            Assert.That(Status.StartProblems(GuidedSetupMethod.Email), Has.Some.EqualTo("Email.yaml: smtpServer is empty."));

            //for email, an Ntfy.yaml that is turned on must pass too: the review step says so
            Write("Email.yaml", ValidEmail);
            Write("Ntfy.yaml", "enabled: true\nserver: https://ntfy.example.invalid\n");
            GuidedSetupStatus Both = Read();
            Assert.That(Both.NotificationsReady(GuidedSetupMethod.Email), Is.True);
            Assert.That(Both.FilesReady(GuidedSetupMethod.Email), Is.False);
            Assert.That(Both.StartProblems(GuidedSetupMethod.Email), Has.Some.StartWith("Ntfy.yaml: "));
            Assert.That(Both.NextStep(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupStep.TurnOn));
        }

        [Test]
        public void ASearchTheStartWouldStopOn_OrOneWithoutACampground_KeepsTheSearchStepOpen()
        {
            Write("Email.yaml", ValidEmail);
            Write("Trip.yaml", Search("Guided Trip", AutoRun: true, CampgroundLine: "# no campground"));
            GuidedSetupStatus NoCampground = Read();
            Assert.That(NoCampground.SearchReady, Is.False);
            Assert.That(NoCampground.SearchProblems.Single(), Does.Contain("No Enabled (autoRun) search has a campground"));

            Write("Trip.yaml", Search("Guided Trip", AutoRun: true));
            Write("Broken.yaml", "displayName: [Canary\n");
            GuidedSetupStatus Broken = Read();
            Assert.That(Broken.SearchReady, Is.False, "a search file the start stops on");
            Assert.That(Broken.SearchProblems.Single(), Does.Contain("Broken.yaml"));
            Assert.That(Broken.NextStep(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupStep.Search));
        }

        [Test]
        public void TheSecretsSources_AreJudgedAsTheStartJudgesThem_FromTheFilesMetadataOnly_AndNoVariableIsRead()
        {
            Func<string, string?> Original = SmtpPassword.GetEnvironment;
            Func<string, string?> OriginalToken = NtfyToken.GetEnvironment;
            SmtpPassword.GetEnvironment = Name => throw new AssertionException("the status reads no environment variable");
            NtfyToken.GetEnvironment = Name => throw new AssertionException("the status reads no environment variable");
            try
            {
                //no password at all: the start signs in without one (a warning of the form), so the file is ready
                Write("Email.yaml", ValidEmail.Replace("smtpPassword: " + Password + "\n", string.Empty));
                Assert.That(Read().NotificationsReady(GuidedSetupMethod.Email), Is.True);

                //a password file that is not there: the start would refuse it, said in D75's words
                string Missing = Path.Join(Folder, "secrets", "smtp-password");
                Write("Email.yaml", ValidEmail.Replace("smtpPassword: " + Password + "\n", "smtpPasswordFile: " + Missing + "\n"));
                GuidedSetupStatus NoFile = Read();
                Assert.That(NoFile.NotificationsReady(GuidedSetupMethod.Email), Is.False);
                Assert.That(NoFile.SecretFileProblems(GuidedSetupMethod.Email).Single(), Does.StartWith("Email.yaml: smtpPasswordFile"));

                //a password file that exists: judged from its metadata, never opened (it holds a canary nothing may show)
                Directory.CreateDirectory(Path.GetDirectoryName(Missing)!);
                File.WriteAllText(Missing, Password);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(Missing, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                GuidedSetupStatus WithFile = Read();
                Assert.That(WithFile.NotificationsReady(GuidedSetupMethod.Email), Is.True);
                Assert.That(string.Join(" ", WithFile.SecretFileProblems(GuidedSetupMethod.Email).Concat(WithFile.SecretFileWarnings(GuidedSetupMethod.Email))), Does.Not.Contain(Password));
            }
            finally
            {
                SmtpPassword.GetEnvironment = Original;
                NtfyToken.GetEnvironment = OriginalToken;
            }
        }

        [Test]
        public void AGeneralYamlTheStartRefuses_IsNoOutputTo()
        {
            Write("General.yaml", "outputTo: [Email\n");
            GuidedSetupStatus Broken = Read();
            Assert.That((Broken.GeneralExists, Broken.SavedOutput), Is.EqualTo((true, (OutputType?)null)));
            Write("General.yaml", "outputTo: Somewhere\n");
            Assert.That(Read().SavedOutput, Is.Null, "a value the start refuses");
        }

        [Test]
        public void TheStepsWords_SayTheStepAndItsNumber_AndTheTurnOnSaysWhatItSaves()
        {
            Assert.That(GuidedSetupWording.StepOf(GuidedSetupStep.Notifications), Is.EqualTo("Step 2 of 4"));
            Assert.That(GuidedSetupWording.StepOf(GuidedSetupStep.TurnOn), Is.EqualTo("Step 4 of 4"));
            Assert.That(GuidedSetupWording.Steps(GuidedSetupMethod.Ntfy), Is.EqualTo(new[] { "How you are notified", "ntfy settings", "A search to check", "Review and turn on" }));
            Assert.That(GuidedSetupWording.Steps(GuidedSetupMethod.Email)[1], Is.EqualTo("Email settings"));
            Assert.That(GuidedSetupWording.TurnOnLabel(GuidedSetupMethod.Email), Is.EqualTo("Turn the monitoring on (save outputTo: Email in General.yaml)"));
            Assert.That(GuidedSetupWording.TurnOnHelp(GuidedSetupMethod.Ntfy), Does.Contain("saves outputTo: Ntfy in General.yaml and changes nothing else"));
            Assert.That(GuidedSetupWording.TestRecommended, Does.Contain("optional"), "a test is recommended, never required (D86)");
            Assert.That(GuidedSetupWording.SkipHint, Does.Contain("marks nothing"));
            Assert.That(GuidedSetupWording.DoneText(GuidedSetupMethod.Email), Does.Contain("can't promise that a message arrives"));
        }
    }
}
