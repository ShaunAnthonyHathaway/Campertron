using System.Net;
using System.Net.Mail;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;
using CampertronWeb.Pages;

namespace CampertronWebTests
{
    // The guided setup (v0.3.0's PR 10, D80 point 4, D100; business-rules-settings §37) on the real host: the start page
    // leads an unconfigured session to it once; its four steps host the settings and searches pages' own forms (the shared
    // form layer, D91) and write the same files through them; where the user is comes from the saved files only; a GET
    // creates, saves, sends and starts nothing; every change is a POST with the antiforgery token from this web UI and the
    // revision of what it showed; the test email and notification are the settings pages' (explicit, rate-limited, the
    // secret read only during the send, fixed words); turning the monitoring on saves outputTo only once the monitoring's
    // checks of the files pass, and starts nothing; leaving marks nothing. The email's and ntfy's transports are their seams
    // and the campground data is in memory: nothing reaches a server or opens a database.
    public class GuidedSetupPageTests
    {
        internal const string ServerText = "Guided-server-says-S3cret";
        private string Password = string.Empty;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private readonly Dictionary<string, string> Environment_ = new(StringComparer.Ordinal);
        private readonly List<string?> SentWith = new();
        private Exception? SendFailure;

        [SetUp]
        public void Seams()
        {
            Password = "GuidedCnry" + Guid.NewGuid().ToString("N");
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            Environment_.Clear();
            SentWith.Clear();
            SendFailure = null;
            CampsiteConfig.SendResultsEmail = (Settings, Message) =>
            {
                lock (SentWith)
                {
                    SentWith.Add(CampsiteConfig.SmtpCredential(Settings).Password);
                }
                if (SendFailure != null)
                {
                    throw SendFailure;
                }
            };
            SmtpPassword.GetEnvironment = Name => Environment_.TryGetValue(Name, out string? Value) ? Value : null;
            NtfyToken.GetEnvironment = Name => Environment_.TryGetValue(Name, out string? Value) ? Value : null;
        }

        [TearDown]
        public void RestoreSeams()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
        }

        internal const string Topic = "guidedWebTopic0123456789abc";
        internal static string ValidEmail(string Password) => "# my email settings\nsmtpServer: smtp.example.invalid   # the server\nsmtpPort: 587\nsmtpUsername: guided-user\nsmtpPassword: " + Password +
            "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";
        internal const string ValidNtfy = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n";
        internal static string GeneralWith(string Output) => "outputTo: " + Output + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n";

        // a web UI with the campground data in memory (the searches pages' own fixture)
        internal static Task<WebTestHost> Start(bool WithPassword = true, string? WebYaml = null, string? Folder = null, Action<WebHostSettings>? Configure = null) =>
            WebTestHost.Start(WebYaml, WithPassword, Folder, Settings =>
            {
                Settings.Campgrounds = SearchesPageTests.Campgrounds;
                Settings.FilterChoices = SearchesPageTests.Choices;
                Settings.PrepareCampgroundData = () => throw new AssertionException("the campground data is in memory");
                Configure?.Invoke(Settings);
            });

        private static string FileOf(WebTestHost Host, string Name) => Path.Join(Host.Folder, Name);

        // every file of the settings folder with its bytes (the web UI's own keys and password excepted)
        internal static Dictionary<string, string> Snapshot(string Folder) => Directory.GetFiles(Folder, "*", SearchOption.AllDirectories)
            .Where(Entry => !Path.GetRelativePath(Folder, Entry).StartsWith("Web.", StringComparison.Ordinal))
            .ToDictionary(Entry => Path.GetRelativePath(Folder, Entry), Entry => File.ReadAllText(Entry));

        // ---- who may use it, and where it leads ----

        [Test]
        public async Task TheGuidedSetup_NeedsTheOwner_AnswersOnlyItsOwnAddresses_AndAGetChangesNothing()
        {
            await using WebTestHost Host = await Start();
            using Browser Visitor = Host.Browser();
            WebPage Refused = await Visitor.Get("/guided-setup/email/notifications");
            Assert.That(Refused.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Refused.Location, Is.EqualTo("/signin?returnUrl=%2Fguided-setup%2Femail%2Fnotifications"));
            Assert.That((await Visitor.Post("/guided-setup/email/turn-on?handler=turnon")).Status, Is.Not.EqualTo(HttpStatusCode.OK), "no change without the owner");
            Assert.That((await Visitor.Get("/healthz")).Body, Is.EqualTo("ok"), "the health answer says nothing of the setup");

            using Browser Owner = await Host.SignedIn();
            Dictionary<string, string> Before = Snapshot(Host.Folder);
            Assert.That((await Owner.Get("/guided-setup/email")).Location, Is.EqualTo("/guided-setup/email/notifications"), "a method resumes at its first step that is not done");
            Assert.That((await Owner.Get("/guided-setup/ntfy")).Location, Is.EqualTo("/guided-setup/ntfy/notifications"));
            foreach (string Address in new[] { "/guided-setup", "/guided-setup/email/notifications", "/guided-setup/ntfy/notifications", "/guided-setup/email/search", "/guided-setup/email/turn-on",
                "/guided-setup/email/done", "/guided-setup/ntfy/done" })
            {
                WebPage Page = await Owner.Get(Address);
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), Address);
                Assert.That(Page.Header("Cache-Control"), Does.Contain("no-store"), Address);
                Assert.That(Page.Header("Content-Security-Policy"), Does.Contain("script-src 'self'").And.Contain("form-action 'self'"), Address);
                Assert.That(Page.Body, Does.Not.Contain(Host.Folder), Address + ": no page shows the settings folder");
                Assert.That(Page.Body, Does.Contain("href=\"/searches\"").And.Contain("href=\"/settings/general\"").And.Contain("href=\"/monitoring\""), Address + ": the full web UI's menu stays");
            }
            foreach (string Other in new[] { "/guided-setup/Email", "/guided-setup/webhook", "/guided-setup/email/method", "/guided-setup/email/everything", "/guided-setup/email/notifications/Email.yaml",
                "/guided-setup/email/search/Missing.yaml", "/guided-setup/email/search/..%2FGeneral.yaml", "/guided-setup/email/done/x" })
            {
                Assert.That((await Owner.Get(Other)).Status, Is.EqualTo(HttpStatusCode.NotFound), Other);
            }
            Assert.That(Snapshot(Host.Folder), Is.EqualTo(Before), "a GET of every step created and changed nothing: no General.yaml, Email.yaml, Ntfy.yaml or search file");
            Assert.That(Directory.GetFiles(Host.Folder).Select(Path.GetFileName), Has.None.EqualTo("Camp.History").And.None.EqualTo("Campertron.monitoring.lock"));
            Assert.That(SentWith, Is.Empty, "nothing sent");
            Assert.That(Host.Service<WebMonitoring>().View().Status.IsRunning, Is.False);
        }

        [Test]
        public async Task TheStartPage_LeadsAnUnconfiguredSessionToTheGuidedSetupOnce_AfterThePasswordsSetup_AndAConfiguredOneNever()
        {
            await using WebTestHost Host = await Start(WithPassword: false);
            using Browser First = Host.Browser();
            Assert.That((await First.Get("/")).Location, Is.EqualTo("/setup"), "the password's setup comes first (D81)");
            Assert.That((await First.Get("/guided-setup")).Location, Is.EqualTo("/setup"));
            Assert.That(new WebPasswordFile(Host.Folder).Create(WebTestHost.Password, WebTestHost.Iterations), Is.True);

            using Browser Owner = await Host.SignedIn();
            Assert.That((await Owner.Get("/")).Location, Is.EqualTo("/guided-setup"), "nothing is set up: the guided setup comes first");
            WebPage Home = await Owner.Get("/");
            Assert.That(Home.Status, Is.EqualTo(HttpStatusCode.OK), "once a session: then the start page, so nothing traps the user");
            Assert.That(Home.Text(), Does.Contain(GuidedSetupWording.StartUnconfigured));
            Assert.That(SettingsForm.Element(Home, "guided-heading"), Is.Not.Null);
            Assert.That(Home.Body, Does.Contain("<a href=\"/guided-setup\">" + GuidedSetupWording.StartLink + "</a>"));
            using Browser Again = await Host.SignedIn();
            Assert.That((await Again.Get("/")).Location, Is.EqualTo("/guided-setup"), "a new session is led there again while nothing is set up");

            //something saved: no lead, a restrained line on the start page only, and the link resumes the method
            File.WriteAllText(FileOf(Host, "Email.yaml"), ValidEmail(Password));
            using Browser Later = await Host.SignedIn();
            WebPage Partial = await Later.Get("/");
            Assert.That(Partial.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Partial.Text(), Does.Contain(GuidedSetupWording.StartPartial));
            Assert.That(Partial.Body, Does.Contain("<a href=\"/guided-setup/email\">" + GuidedSetupWording.ContinueLink + "</a>"));
            Assert.That((await Later.Get("/searches")).Body, Does.Not.Contain(GuidedSetupWording.StartPartial), "no other page nags");

            //set up: the start page says so, and the guided setup can be opened again
            File.WriteAllText(FileOf(Host, "Trip.yaml"), SearchesPageTests.Search("Guided trip"));
            File.WriteAllText(FileOf(Host, "General.yaml"), GeneralWith("Email"));
            using Browser Configured = await Host.SignedIn();
            WebPage Done = await Configured.Get("/");
            Assert.That(Done.Status, Is.EqualTo(HttpStatusCode.OK), "a configured installation lands on the start page");
            Assert.That(Done.Text(), Does.Contain(GuidedSetupWording.StartComplete(GuidedSetupMethod.Email)));
            Assert.That(Done.Body, Does.Contain("<a href=\"/guided-setup\">" + GuidedSetupWording.ReopenLink + "</a>"));
            WebPage Reopened = await Configured.Get("/guided-setup");
            Assert.That(SettingsForm.Element(Reopened, "method-email")!.Attribute("checked")?.Value, Is.EqualTo("checked"), "the saved method is offered");

            await using WebTestHost Open = await Start(WithPassword: false, WebYaml: "passwordProtection: false\n");
            using Browser Anyone = Open.Browser();
            Assert.That((await Anyone.Get("/")).Location, Is.EqualTo("/guided-setup"), "without password protection: once for the web UI (one owner, D81)");
            Assert.That((await Anyone.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK));
            using Browser Other = Open.Browser();
            Assert.That((await Other.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task StepOne_IsAGetThatSavesNothing_LeadsToTheMethod_AndSaysWhenNothingWasChosen()
        {
            await using WebTestHost Host = await Start();
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/guided-setup");
            XElement Form = Page.Xml().Descendants("form").Single(F => (string?)F.Attribute("action") == "/guided-setup");
            Assert.That((string?)Form.Attribute("method"), Is.EqualTo("get"));
            Assert.That(Form.Descendants("input").Where(I => (string?)I.Attribute("type") == "radio").Select(I => (string?)I.Attribute("value")), Is.EqualTo(new[] { "email", "ntfy" }));
            Assert.That(Form.Descendants("input").Where(I => (string?)I.Attribute("checked") != null), Is.Empty, "nothing is saved yet, so nothing is chosen for the user");
            Assert.That(Page.Text(), Does.Contain(GuidedSetupWording.StepOf(GuidedSetupStep.Method)).And.Contain(GuidedSetupWording.SkipLabel));
            Assert.That(Page.Xml().Descendants("a").Single(A => A.Value == GuidedSetupWording.SkipLabel).Attribute("href")?.Value, Is.EqualTo("/"), "skipping is a link to the full web UI: nothing is posted or kept");

            WebPage Missing = await Owner.Get("/guided-setup?continue=1");
            Assert.That(Missing.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(SettingsForm.Element(Missing, "error-summary")!.Value, Does.Contain(GuidedSetupWording.MethodMissing));
            Assert.That(Missing.Xml().Descendants("title").Single().Value, Does.StartWith("Error: "));
            Assert.That((await Owner.Get("/guided-setup?choice=webhook&continue=1")).Body, Does.Contain(GuidedSetupWording.MethodMissing), "only the two methods");
            Assert.That((await Owner.Get("/guided-setup?choice=ntfy&continue=1")).Location, Is.EqualTo("/guided-setup/ntfy"));
            Assert.That((await Owner.Get("/guided-setup?choice=email")).Location, Is.EqualTo("/guided-setup/email"));
            Assert.That((await Owner.Get("/guided-setup?choice=https%3A%2F%2Fevil.example")).Location, Is.Null, "no redirect anywhere else: the addresses are fixed");
            Assert.That(Directory.GetFiles(Host.Folder).Select(Path.GetFileName).Where(Name => !Name!.StartsWith("Web.", StringComparison.Ordinal)), Is.Empty);
        }

        // ---- step 2: the notification settings ----

        [Test]
        public async Task TheEmailStep_IsTheSettingsPagesForm_ItSavesThroughIt_AndGoesOnOnlyOnceTheSavedFileIsWhatTheMonitoringNeeds()
        {
            await using WebTestHost Host = await Start();
            using Browser Owner = await Host.SignedIn();
            const string Address = "/guided-setup/email/notifications";
            WebPage Page = await Owner.Get(Address);
            Assert.That(Page.Text(), Does.Contain("Step 2 of 4: Email settings").And.Contain("Email.yaml does not exist."));
            Assert.That(SettingsForm.Element(Page, "needs")!.Value, Does.Contain("Email.yaml: The file does not exist."), "what the monitoring's start would refuse, in its words");

            //the settings page's own Create: the sample a first start writes
            WebPage Created = await Owner.Post(Address, GuidedForm.Fields(Page, Address, "create").ToArray());
            Assert.That(File.Exists(FileOf(Host, "Email.yaml")), Is.True);
            Assert.That(Created.Text(), Does.Contain("Created Email.yaml with the default settings."));

            //a value with its own error is never saved, and Save and continue stays
            WebPage Refused = await Owner.Post(Address, GuidedForm.Fields(Created, Address, "continue", ("f.smtpPort", "not a port")).ToArray());
            Assert.That(Refused.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(SettingsForm.Element(Refused, "error-summary"), Is.Not.Null);
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Does.Not.Contain("not a port"));

            //saved without what the monitoring needs (no recipient): the form saves it (a warning while outputTo is not Email),
            //the step says what is missing in the start's words and does not go on
            (string, string)[] Typed = { ("f.smtpServer", "smtp.example.invalid"), ("f.smtpPort", "587"), ("f.smtpUsername", "guided-user"), ("secret.smtpPassword", Password),
                ("f.sendFromAddress", "from@example.invalid") };
            WebPage NoRecipient = await Owner.Post(Address, GuidedForm.Fields(Refused, Address, "continue", Typed).ToArray());
            Assert.That(NoRecipient.Status, Is.EqualTo(HttpStatusCode.OK), "not ready: no redirect");
            XElement Needs = SettingsForm.Element(NoRecipient, "needs")!;
            Assert.That(Needs.Value.Trim(), Does.StartWith(GuidedSetupWording.NotReady(GuidedSetupMethod.Email)).And.Contain("sendToAddressList has no address"));
            Assert.That(Needs.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"), "what is missing takes the focus");
            Assert.That(SettingsForm.Element(NoRecipient, "notice"), Is.Null, "said once");
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Does.Contain("smtp.example.invalid").And.Contain(Password), "the form saved what it accepted");
            Assert.That(NoRecipient.Body, Does.Not.Contain(Password), "the SMTP password is write-only (D82): never in a page");
            Assert.That(NoRecipient.Xml().Descendants("input").Where(I => (string?)I.Attribute("type") == "hidden").Select(I => (string?)I.Attribute("value") ?? ""), Has.None.Contain(Password));
            Assert.That(SettingsForm.Element(NoRecipient, "f-smtpPassword")!.Attribute("value"), Is.Null, "its box stays empty");

            //the recipient: saved and on to the search
            WebPage Added = await Owner.Post(Address, GuidedForm.Fields(NoRecipient, Address, "row-add.sendToAddressList").ToArray());
            WebPage Ready = await Owner.Post(Address, GuidedForm.Fields(Added, Address, "continue", ("f.sendToAddressList.0", "to@example.invalid")).ToArray());
            Assert.That(Ready.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Ready.Location, Is.EqualTo("/guided-setup/email/search"));
            WebPage Next = await Owner.Get(Ready.Location!);
            Assert.That(SettingsForm.Element(Next, "notice")!.Value, Does.StartWith("Info: Saved Email.yaml"), "the save's result, said once on the next step");
            Assert.That((await Owner.Get(Ready.Location!)).Body, Does.Not.Contain("Saved Email.yaml"));
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Does.Contain("to@example.invalid"));
            Assert.That(GuidedSetupStatus.Read(Host.Folder, DateTime.Now).NotificationsReady(GuidedSetupMethod.Email), Is.True);
            Assert.That(SentWith, Is.Empty, "no step sends anything but its test");

            //a file that is ready: Save and continue goes on without writing
            string Saved = File.ReadAllText(FileOf(Host, "Email.yaml"));
            WebPage Again = await Owner.Get(Address);
            Assert.That((await Owner.Post(Address, GuidedForm.Fields(Again, Address, "continue").ToArray())).Location, Is.EqualTo("/guided-setup/email/search"));
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Is.EqualTo(Saved));
            Assert.That(File.Exists(FileOf(Host, "General.yaml")), Is.False, "the step writes its own file only");
        }

        [Test]
        public async Task TheEmailStep_KeepsComments_SaysTheVariableAsYesOrNo_AndAnOutOfDatePageAppliesNothing()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Email.yaml"), ValidEmail(Password));
            const string Variable = "Guided-variable-value-S3cret";
            Environment_["CAMPERTRON_EMAIL_PASSWORD"] = Variable;
            using Browser Owner = await Host.SignedIn();
            const string Address = "/guided-setup/email/notifications";
            WebPage Page = await Owner.Get(Address);
            Assert.That(SettingsForm.Element(Page, "email-variable")!.Value, Does.Contain("Yes"));
            Assert.That(Page.Body, Does.Not.Contain(Variable).And.Not.Contain(Password));
            Assert.That(SettingsForm.Element(Page, "needs"), Is.Null, "a ready file has nothing to fix");

            //another tab saves meanwhile: this page's entries are never applied
            WebPage Other = await Owner.Get("/settings/email");
            Assert.That((await Owner.Post("/settings/email", SettingsForm.Fields(Other, "save", ("f.smtpUsername", "other-tab")).ToArray())).Status, Is.EqualTo(HttpStatusCode.OK));
            WebPage Stale = await Owner.Post(Address, GuidedForm.Fields(Page, Address, "continue", ("f.smtpUsername", "stale-page")).ToArray());
            Assert.That(Stale.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(SettingsForm.Element(Stale, "page-notice")!.Value, Does.Contain("was out of date"));
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Does.Contain("other-tab").And.Not.Contain("stale-page"));

            //a current page saves through the session (one rolling backup, the comments and the rest kept)
            WebPage Current = await Owner.Get(Address);
            Assert.That((await Owner.Post(Address, GuidedForm.Fields(Current, Address, "continue", ("f.smtpUsername", "guided-two")).ToArray())).Location, Is.EqualTo("/guided-setup/email/search"));
            string Text = File.ReadAllText(FileOf(Host, "Email.yaml"));
            Assert.That(Text, Does.StartWith("# my email settings\nsmtpServer: smtp.example.invalid   # the server\n").And.Contain("smtpUsername: guided-two"));
            Assert.That(File.Exists(FileOf(Host, "Email.yaml.bak")), Is.True);
            //a change on disk under unsaved edits is the form's conflict, never an overwrite
            WebPage Edited = await Owner.Post(Address, GuidedForm.Fields(await Owner.Get(Address), Address, "check", ("f.smtpUsername", "mine")).ToArray());
            File.WriteAllText(FileOf(Host, "Email.yaml"), Text.Replace("guided-two", "theirs"));
            WebPage Conflict = await Owner.Post(Address, GuidedForm.Fields(Edited, Address, "continue").ToArray());
            Assert.That(SettingsForm.Element(Conflict, "conflict"), Is.Not.Null);
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Does.Contain("theirs").And.Not.Contain("smtpUsername: mine"));
            Environment_.Remove("CAMPERTRON_EMAIL_PASSWORD");
            Assert.That(SettingsForm.Element(await Owner.Get(Address), "email-variable")!.Value, Does.Contain("No"));
        }

        // D111: the email step saves through the settings page's session, so a password it replaces or removes is left out
        // of Email.yaml.bak too
        [Test]
        public async Task TheEmailStep_LeavesAReplacedOrRemovedPasswordOutOfTheBackup()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Email.yaml"), ValidEmail(Password));
            string Replacement = "GuidedReplacement" + Guid.NewGuid().ToString("N");
            using Browser Owner = await Host.SignedIn();
            const string Address = "/guided-setup/email/notifications";
            string[] Holding(string Value) => Directory.GetFiles(Host.Folder).Where(F => File.ReadAllText(F).Contains(Value, StringComparison.Ordinal)).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal).ToArray()!;

            WebPage Page = await Owner.Get(Address);
            Assert.That((await Owner.Post(Address, GuidedForm.Fields(Page, Address, "continue", ("secret.smtpPassword", Replacement)).ToArray())).Location, Is.EqualTo("/guided-setup/email/search"));
            Assert.That(Holding(Replacement), Is.EqualTo(new[] { "Email.yaml" }));
            Assert.That(Holding(Password), Is.Empty, "the replaced password is in no file of the folder");
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml.bak")), Is.EqualTo(ValidEmail("''")));

            WebPage Removed = await Owner.Post(Address, GuidedForm.Fields(await Owner.Get(Address), Address, "remove-password").ToArray());
            Assert.That((await Owner.Post(Address, GuidedForm.Fields(Removed, Address, "continue").ToArray())).Location, Is.EqualTo("/guided-setup/email/search"));
            Assert.That(Holding(Replacement), Is.Empty);
            Assert.That(Removed.Body, Does.Not.Contain(Replacement).And.Not.Contain(Password));
        }

        [Test]
        public async Task TheStepsTest_IsTheSettingsPagesTest_SentOnlyOnRequest_WithTheSavedFile_AndChangesNothingElse()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Email.yaml"), ValidEmail(Password));
            using Browser Owner = await Host.SignedIn();
            const string Address = "/guided-setup/email/notifications";
            WebPage Page = await Owner.Get(Address);
            Assert.That(SentWith, Is.Empty, "no send on a GET");
            Assert.That((await Owner.Post(Address, GuidedForm.Fields(Page, Address, "continue").ToArray())).Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(SentWith, Is.Empty, "nor on Save and continue");
            Dictionary<string, string> Before = Snapshot(Host.Folder);

            Page = await Owner.Get(Address);
            WebPage Tested = await Owner.Post(Address + "?handler=test", GuidedForm.Fields(Page, Address, null).ToArray());
            Assert.That(SettingsForm.Element(Tested, "test-result")!.Value, Does.StartWith("Info: the SMTP server accepted the test email for 1 recipient"));
            Assert.That(SentWith, Is.EqualTo(new[] { Password }), "one send, with the saved file's password read for it");
            Assert.That(Tested.Body, Does.Not.Contain(Password));
            Assert.That(Snapshot(Host.Folder), Is.EqualTo(Before), "a test writes nothing: no Camp.History, no lock, no settings file");
            Assert.That(Host.Service<WebMonitoring>().View().Status.IsRunning, Is.False, "and starts no monitoring");

            //a failed send: fixed words, never the server's text
            SendFailure = new SmtpException(SmtpStatusCode.MailboxUnavailable, ServerText);
            WebPage Failed = await Owner.Post(Address + "?handler=test", GuidedForm.Fields(Tested, Address, null).ToArray());
            Assert.That(SettingsForm.Element(Failed, "test-result")!.Value, Does.StartWith("Error: the test email could not be sent: ").And.Contain("(SMTP 550)"));
            Assert.That(Failed.Body, Does.Not.Contain(ServerText).And.Not.Contain(Password));
            Assert.That(Snapshot(Host.Folder), Is.EqualTo(Before));

            //unsaved changes: the test uses the saved file, so it is not offered (the form's own rule)
            WebPage Changed = await Owner.Post(Address, GuidedForm.Fields(Failed, Address, "check", ("f.smtpUsername", "changed")).ToArray());
            int Sends = SentWith.Count;
            WebPage NotNow = await Owner.Post(Address + "?handler=test", GuidedForm.Fields(Changed, Address, null).ToArray());
            Assert.That(SettingsForm.Element(NotNow, "test-result")!.Value, Does.StartWith("Warning: The test uses Email.yaml as it is saved"));
            Assert.That(SentWith, Has.Count.EqualTo(Sends));

            //the tests of this step and of the settings pages are one limit per client: five a minute
            SendFailure = null;
            WebPage Clean = await Owner.Post(Address, GuidedForm.Fields(NotNow, Address, "revert").ToArray());
            int Allowed = 0;
            for (int i = 0; i < 6; i++)
            {
                WebPage Each = await Owner.Post((i % 2 == 0 ? Address : "/settings/email") + "?handler=test", GuidedForm.Fields(Clean, Address, null).ToArray());
                if (Each.Status == HttpStatusCode.TooManyRequests)
                {
                    break;
                }
                Allowed++;
            }
            Assert.That(Allowed, Is.LessThan(6), "the test's rate limit holds across both pages");
        }

        [Test]
        public async Task TheNtfyStep_IsNtfysForm_ATopicIsAnUnsavedEdit_AndItsTestUsesTheSavedFile()
        {
            await using WebTestHost Host = await Start();
            var Handler = new Publishes();
            NtfyPublisher.CreateHandler = () => Handler;
            using Browser Owner = await Host.SignedIn();
            const string Address = "/guided-setup/ntfy/notifications";
            WebPage Page = await Owner.Get(Address);
            Assert.That(Page.Text(), Does.Contain("Step 2 of 4: ntfy settings").And.Contain(GuidedSetupWording.NtfyHelp));
            Assert.That(File.Exists(FileOf(Host, "Ntfy.yaml")), Is.False, "a new Ntfy.yaml is only edited in memory until Save");

            WebPage Generated = await Owner.Post(Address, GuidedForm.Fields(Page, Address, "generate-topic").ToArray());
            string Generated_ = SettingsForm.Element(Generated, "f-topic")!.Attribute("value")!.Value;
            Assert.That(Generated_, Does.Match("^campertron-[a-z0-9]{26}$"));
            Assert.That(File.Exists(FileOf(Host, "Ntfy.yaml")), Is.False, "a generated topic is an unsaved edit");

            //saved off: the step says ntfy must be on, in the start's words, and stays
            WebPage Off = await Owner.Post(Address, GuidedForm.Fields(Generated, Address, "continue", ("f.server", "https://ntfy.example.invalid")).ToArray());
            Assert.That(Off.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(File.ReadAllText(FileOf(Host, "Ntfy.yaml")), Does.Contain(Generated_).And.Contain("enabled: false"));
            Assert.That(SettingsForm.Element(Off, "needs")!.Value, Does.Contain("with ntfy turned on (Enabled)"));

            WebPage On = await Owner.Post(Address, GuidedForm.Fields(Off, Address, "continue", ("f.enabled", "true")).ToArray());
            Assert.That(On.Location, Is.EqualTo("/guided-setup/ntfy/search"));
            Assert.That(Handler.Count, Is.Zero, "nothing published without the test");

            WebPage Back = await Owner.Get(Address);
            WebPage Tested = await Owner.Post(Address + "?handler=test", GuidedForm.Fields(Back, Address, null).ToArray());
            Assert.That(SettingsForm.Element(Tested, "test-result")!.Value, Does.StartWith("Info: the ntfy server accepted the test notification at "));
            Assert.That(Handler.Count, Is.EqualTo(1));

            Handler.Failing = true;
            WebPage Failed = await Owner.Post(Address + "?handler=test", GuidedForm.Fields(Tested, Address, null).ToArray());
            Assert.That(SettingsForm.Element(Failed, "test-result")!.Value, Does.StartWith("Error: "));
            Assert.That(Failed.Body, Does.Not.Contain(ServerText));
            Assert.That((await Owner.Get("/guided-setup/ntfy/turn-on")).Body, Does.Not.Contain(Generated_), "the topic is shown only in Ntfy.yaml's editor (D82)");
        }

        // ---- step 3: a search ----

        [Test]
        public async Task TheSearchStep_CreatesASearchWithNewSearch_EditsItsMainSettings_AndGoesOnOnceOneIsEnabled()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Email.yaml"), ValidEmail(Password));
            using Browser Owner = await Host.SignedIn();
            const string List = "/guided-setup/email/search";
            WebPage Page = await Owner.Get(List);
            Assert.That(Page.Text(), Does.Contain("Step 3 of 4: A search to check").And.Contain(GuidedSetupWording.NoSearchFiles));
            Assert.That(SettingsForm.Element(Page, "search-state")!.Value, Does.Contain("Campertron needs at least one enabled search"));
            Assert.That(Page.Xml().Descendants("a").Where(A => (string?)A.Attribute("class") == "button-link"), Is.Empty, "no Continue before a search is ready (the list of steps still links every step)");

            //a name the library refuses: nothing created
            WebPage Refused = await Owner.Post(List + "?handler=create", GuidedForm.Fields(Page, List, null, ("new.name", "../General")).ToArray());
            Assert.That(SettingsForm.Element(Refused, "new-search-message")!.Value, Does.StartWith("Error: "));
            Assert.That(Directory.GetFiles(Host.Folder, "*.yaml").Select(Path.GetFileName), Is.EqualTo(new[] { "Email.yaml" }));

            WebPage Created = await Owner.Post(List + "?handler=create", GuidedForm.Fields(Refused, List, null, ("new.name", "Lake trip")).ToArray());
            Assert.That(Created.Location, Is.EqualTo("/guided-setup/email/search/Lake%20trip.yaml"));
            string Example = File.ReadAllText(FileOf(Host, "Lake trip.yaml"));
            Assert.That(Example, Does.Contain("autoRun: false"), "the example search, not enabled (the searches page's own New search)");
            string Address = Created.Location!;
            WebPage Editor = await Owner.Get(Address);
            Assert.That(SettingsForm.Element(Editor, "created")!.Value, Does.Contain("Created Lake trip.yaml"));
            Assert.That(SettingsForm.Element(Editor, "f-autoRun"), Is.Not.Null);
            Assert.That(SettingsForm.Element(Editor, "f-campgroundID"), Is.Not.Null);
            Assert.That(SettingsForm.Element(Editor, "f-showMonday"), Is.Null, "the days and the filters stay on the searches page");
            Assert.That(Editor.Body, Does.Contain("href=\"/searches/Lake%20trip.yaml\""));

            //a campground by its exact ID, the people, enabled: saved and on to the last step
            WebPage Used = await Owner.Post(Address, GuidedForm.Fields(Editor, Address, "use-campground-id", ("campground.id", SearchesPageTests.Campground)).ToArray());
            Assert.That(SettingsForm.Element(Used, WebSettingsForm.CampgroundCurrentId)!.Value, Does.Contain("UPPER PINES"));
            Assert.That(File.ReadAllText(FileOf(Host, "Lake trip.yaml")), Is.EqualTo(Example), "nothing written before Save");
            WebPage NotEnabled = await Owner.Post(Address, GuidedForm.Fields(Used, Address, "continue", ("f.totalHumans", "4")).ToArray());
            Assert.That(NotEnabled.Status, Is.EqualTo(HttpStatusCode.OK), "saved, but no search is enabled yet");
            Assert.That(SettingsForm.Element(NotEnabled, "notice")!.Value, Does.Contain("at least one search must be Enabled (autoRun)"));
            WebPage Ready = await Owner.Post(Address, GuidedForm.Fields(NotEnabled, Address, "continue", ("f.autoRun", "true")).ToArray());
            Assert.That(Ready.Location, Is.EqualTo("/guided-setup/email/turn-on"));
            string Saved = File.ReadAllText(FileOf(Host, "Lake trip.yaml"));
            Assert.That(Saved, Does.Contain("autoRun: true").And.Contain("campgroundID: " + SearchesPageTests.Campground + "\n").And.Contain("totalHumans: 4"));
            Assert.That(Saved, Does.Contain("showMonday: true").And.Contain("filterMatching: Exact"), "the settings not shown keep their values");
            Assert.That(GuidedSetupStatus.Read(Host.Folder, DateTime.Now).SearchReady, Is.True);
            Assert.That(SettingsForm.Element(await Owner.Get(List), "search-state")!.Value, Does.StartWith("Info: "));
            Assert.That(Host.Service<WebMonitoring>().View().Status.IsRunning, Is.False, "nothing ran a search or started the monitoring");
            Assert.That(Directory.GetFiles(Host.Folder).Select(Path.GetFileName), Has.None.EqualTo("Camp.History"));
        }

        [Test]
        public async Task ASearchsPage_KeepsCommentsAndUnshownSettings_WarnsOfTheSearchesPagesUnsavedChanges_AndAnOutOfDatePageAppliesNothing()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Trip.yaml"), SearchesPageTests.Search("First trip", AutoRun: false, Extra: "includeEquipment:\n- Tent\n"));
            using Browser Owner = await Host.SignedIn();
            const string Address = "/guided-setup/email/search/Trip.yaml";
            WebPage Page = await Owner.Get(Address);

            //an unsaved change on the searches page in a setting this step does not show: said, with a link there
            WebPage Full = await Owner.Get("/searches/Trip.yaml");
            await Owner.Post("/searches/Trip.yaml", SearchForm.Fields(Full, "check", ("present.showMonday", "1")).Where(F => F.Name != "f.showMonday").ToArray());
            WebPage Warned = await Owner.Get(Address);
            Assert.That(SettingsForm.Element(Warned, "other-changes")!.Value, Does.Contain("unsaved changes on its searches page"));

            //the page asked for before that change is out of date: nothing applied
            WebPage Stale = await Owner.Post(Address, GuidedForm.Fields(Page, Address, "continue", ("f.autoRun", "true")).ToArray());
            Assert.That(SettingsForm.Element(Stale, "page-notice")!.Value, Does.Contain("was out of date"));
            Assert.That(File.ReadAllText(FileOf(Host, "Trip.yaml")), Does.Contain("autoRun: false"));

            WebPage Reverted = await Owner.Post("/searches/Trip.yaml", SearchForm.Fields(await Owner.Get("/searches/Trip.yaml"), "revert").ToArray());
            Assert.That(Reverted.Status, Is.EqualTo(HttpStatusCode.OK));
            WebPage Current = await Owner.Get(Address);
            Assert.That(SettingsForm.Element(Current, "other-changes"), Is.Null);
            Assert.That((await Owner.Post(Address, GuidedForm.Fields(Current, Address, "continue", ("f.autoRun", "true")).ToArray())).Location, Is.EqualTo("/guided-setup/email/turn-on"));
            string Saved = File.ReadAllText(FileOf(Host, "Trip.yaml"));
            Assert.That(Saved, Does.StartWith("# my search\ndisplayName: First trip   # its name\nautoRun: true\n"), "the comments and layout kept");
            Assert.That(Saved, Does.Contain("includeEquipment:\n- Tent\n").And.Contain("showMonday: true"));
            //sent twice: the second is out of date and changes nothing
            WebPage Twice = await Owner.Post(Address, GuidedForm.Fields(Current, Address, "continue", ("f.totalHumans", "9")).ToArray());
            Assert.That(SettingsForm.Element(Twice, "page-notice"), Is.Not.Null);
            Assert.That(File.ReadAllText(FileOf(Host, "Trip.yaml")), Is.EqualTo(Saved));
            //a search is ready, but an entry the form refuses is never left behind unsaved: the step stays and says so
            WebPage Refused = await Owner.Post(Address, GuidedForm.Fields(await Owner.Get(Address), Address, "continue", ("f.totalHumans", "many")).ToArray());
            Assert.That(Refused.Status, Is.EqualTo(HttpStatusCode.OK), "no Continue past a refused entry, even with a search ready");
            Assert.That(SettingsForm.Element(Refused, "error-summary"), Is.Not.Null);
            Assert.That(File.ReadAllText(FileOf(Host, "Trip.yaml")), Is.EqualTo(Saved));
        }

        [Test]
        public async Task CampgroundDataThatCantBeRead_LeadsToTheSystemPage_AndNoStepPreparesOrRefreshesIt()
        {
            await using WebTestHost Host = await Start(Configure: Settings => Settings.Campgrounds = () => throw new InvalidOperationException(ServerText));
            File.WriteAllText(FileOf(Host, "Trip.yaml"), SearchesPageTests.Search("First trip", AutoRun: false));
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/guided-setup/email/search/Trip.yaml");
            Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK));
            XElement Problem = SettingsForm.Element(Page, "campground-data")!;
            Assert.That(Problem.Value, Does.StartWith("Warning: ").And.Contain("Settings > System"));
            Assert.That(Problem.Descendants("a").Single().Attribute("href")?.Value, Is.EqualTo("/settings/system"), "the existing refresh, never one of the guided setup's own");
            Assert.That(Page.Body, Does.Not.Contain(ServerText));
            Assert.That(Host.Service<WebRefresh>().View().IsRunning, Is.False, "nothing refreshes by itself");
        }

        // ---- step 4: review and turn on; done ----

        [Test]
        public async Task TurningOn_SavesOutputToOnly_OnlyOnceTheFilesPass_StartsNothing_AndTheDonePageSaysWhatIsTrue()
        {
            await using WebTestHost Host = await Start();
            using Browser Owner = await Host.SignedIn();
            const string Address = "/guided-setup/email/turn-on";
            WebPage NotReady = await Owner.Get(Address);
            XElement Button = NotReady.Xml().Descendants("button").Single(B => B.Value.StartsWith("Turn the monitoring on", StringComparison.Ordinal));
            Assert.That(Button.Attribute("disabled")?.Value, Is.EqualTo("disabled"));
            Assert.That(SettingsForm.Element(NotReady, "problems")!.Value, Does.Contain("Email.yaml").And.Contain("needs at least one enabled search"));
            WebPage Refused = await Owner.Post(Address + "?handler=turnon", GuidedForm.Fields(NotReady, Address, null).ToArray());
            Assert.That(SettingsForm.Element(Refused, "notice")!.Value, Is.EqualTo(GuidedSetupWording.TurnOnNotReady));
            Assert.That(File.Exists(FileOf(Host, "General.yaml")), Is.False, "nothing saved, not even a sample");
            WebPage NotDone = await Owner.Get("/guided-setup/email/done");
            Assert.That(NotDone.Text(), Does.Contain(GuidedSetupWording.NotDoneHeading), "the done page never claims what the files don't say");

            File.WriteAllText(FileOf(Host, "Email.yaml"), ValidEmail(Password));
            File.WriteAllText(FileOf(Host, "Trip.yaml"), SearchesPageTests.Search("First trip"));
            WebPage Ready = await Owner.Get(Address);
            Assert.That(Ready.Text(), Does.Contain(GuidedSetupWording.NoGeneralYet).And.Contain("outputTo in General.yaml: not set yet"));

            //General.yaml with the user's unsaved changes on its settings page: never saved along
            WebPage General = await Owner.Get("/settings/general");
            await Owner.Post("/settings/general", SettingsForm.Fields(General, "create").ToArray());
            WebPage Edited = await Owner.Post("/settings/general", SettingsForm.Fields(await Owner.Get("/settings/general"), "check", ("f.refreshRidbDataDayInterval", "12")).ToArray());
            Assert.That(Edited.Status, Is.EqualTo(HttpStatusCode.OK));
            string Sample = File.ReadAllText(FileOf(Host, "General.yaml"));
            WebPage Held = await Owner.Post(Address + "?handler=turnon", GuidedForm.Fields(await Owner.Get(Address), Address, null).ToArray());
            Assert.That(SettingsForm.Element(Held, "notice")!.Value, Is.EqualTo(GuidedSetupWording.GeneralUnsaved));
            Assert.That(File.ReadAllText(FileOf(Host, "General.yaml")), Is.EqualTo(Sample));
            await Owner.Post("/settings/general", SettingsForm.Fields(await Owner.Get("/settings/general"), "revert").ToArray());

            WebPage Page = await Owner.Get(Address);
            WebPage Stale = await Owner.Post(Address + "?handler=turnon", GuidedForm.Fields(NotReady, Address, null).ToArray());
            Assert.That(SettingsForm.Element(Stale, "page-notice"), Is.Not.Null, "an out-of-date page turns nothing on");
            WebPage TurnedOn = await Owner.Post(Address + "?handler=turnon", GuidedForm.Fields(Page, Address, null).ToArray());
            Assert.That(TurnedOn.Location, Is.EqualTo("/guided-setup/email/done"));
            string After = File.ReadAllText(FileOf(Host, "General.yaml"));
            Assert.That(After, Is.EqualTo(Sample.Replace("outputTo: Console", "outputTo: Email")), "outputTo and nothing else");
            WebPage Twice = await Owner.Post(Address + "?handler=turnon", GuidedForm.Fields(Page, Address, null).ToArray());
            Assert.That(SettingsForm.Element(Twice, "page-notice"), Is.Not.Null, "sent again: out of date, nothing done");
            Assert.That(File.ReadAllText(FileOf(Host, "General.yaml")), Is.EqualTo(After));

            //done: the monitoring is not running (web mode starts it when it starts, or Start does), and nothing pretends it is
            WebMonitoringView View = Host.Service<WebMonitoring>().View();
            Assert.That(View.Status.IsRunning, Is.False, "turning it on starts nothing");
            Assert.That(SentWith, Is.Empty);
            Assert.That(Directory.GetFiles(Host.Folder).Select(Path.GetFileName), Has.None.EqualTo("Camp.History").And.None.EqualTo("Campertron.monitoring.lock"));
            WebPage Done = await Owner.Get("/guided-setup/email/done");
            Assert.That(Done.Text(), Does.Contain(GuidedSetupWording.DoneHeading(GuidedSetupMethod.Email)).And.Contain(GuidedSetupWording.MonitoringNotRunning));
            Assert.That(SettingsForm.Element(Done, "monitoring-state")!.Value, Does.StartWith("Info: Not running"));
            XElement StartForm = Done.Xml().Descendants("form").Single(F => (string?)F.Attribute("action") == "/monitoring?handler=start");
            Assert.That(StartForm.Descendants("input").Single(I => (string?)I.Attribute("name") == "state").Attribute("value")?.Value, Is.EqualTo(View.Token),
                "Start is the monitoring page's own action, for the moment shown");
            Assert.That(Done.Body, Does.Not.Contain(Password).And.Not.Contain("example.invalid").And.Not.Contain(Host.Folder), "no secret, destination or path in the summary");
            Assert.That(GuidedSetupStatus.Read(Host.Folder, DateTime.Now).State(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupState.Complete));

            //switching to ntfy keeps Email.yaml: nothing of the other method is deleted
            File.WriteAllText(FileOf(Host, "Ntfy.yaml"), ValidNtfy);
            WebPage Ntfy = await Owner.Get("/guided-setup/ntfy/turn-on");
            Assert.That((await Owner.Post("/guided-setup/ntfy/turn-on?handler=turnon", GuidedForm.Fields(Ntfy, "/guided-setup/ntfy/turn-on", null).ToArray())).Location, Is.EqualTo("/guided-setup/ntfy/done"));
            Assert.That(File.ReadAllText(FileOf(Host, "General.yaml")), Does.Contain("outputTo: Ntfy"));
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Is.EqualTo(ValidEmail(Password)));
        }

        // ---- resuming, leaving ----

        [Test]
        public async Task ResumingAfterASignOutOrARestart_StartsWhereTheSavedFilesSay_AndLeavingMarksNothing()
        {
            string Folder = WebTestHost.NewFolder();
            try
            {
                await using (WebTestHost Host = await Start(Folder: Folder))
                {
                    using Browser Owner = await Host.SignedIn();
                    const string Address = "/guided-setup/email/notifications";
                    WebPage Page = await Owner.Post(Address, GuidedForm.Fields(await Owner.Get(Address), Address, "create").ToArray());
                    Page = await Owner.Post(Address, GuidedForm.Fields(Page, Address, "row-add.sendToAddressList").ToArray());
                    Assert.That((await Owner.Post(Address, GuidedForm.Fields(Page, Address, "continue", ("f.smtpServer", "smtp.example.invalid"), ("f.smtpPort", "587"),
                        ("f.sendFromAddress", "from@example.invalid"), ("f.sendToAddressList.0", "to@example.invalid")).ToArray())).Location, Is.EqualTo("/guided-setup/email/search"));
                    //leaving: a link to the start page; nothing is posted, kept or marked
                    WebPage Search = await Owner.Get("/guided-setup/email/search");
                    Assert.That(Search.Xml().Descendants("a").Single(A => A.Value == GuidedSetupWording.LeaveLabel).Attribute("href")?.Value, Is.EqualTo("/"));
                    Dictionary<string, string> Before = Snapshot(Folder);
                    Assert.That((await Owner.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(Snapshot(Folder), Is.EqualTo(Before));
                    await Owner.Post("/signout");
                    using Browser Again = await Host.SignedIn();
                    WebPage Home = await Again.Get("/");
                    Assert.That(Home.Status, Is.EqualTo(HttpStatusCode.OK), "something is saved: no lead");
                    Assert.That(Home.Body, Does.Contain("href=\"/guided-setup/email\""));
                    Assert.That((await Again.Get("/guided-setup/email")).Location, Is.EqualTo("/guided-setup/email/search"), "a new session resumes where the files say");
                }
                await using (WebTestHost Restarted = await Start(WithPassword: false, Folder: Folder))
                {
                    using Browser Owner = await Restarted.SignedIn();
                    Assert.That((await Owner.Get("/guided-setup/email")).Location, Is.EqualTo("/guided-setup/email/search"), "and so does a web mode started again");
                    Assert.That((await Owner.Get("/guided-setup")).Body, Does.Contain("value=\"email\" checked=\"checked\""));
                }
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        // ---- what the code reaches ----

        [Test]
        public async Task EveryGuidedChange_IsAPostWithTheFormsTokenFromThisWebUI()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Email.yaml"), ValidEmail(Password));
            File.WriteAllText(FileOf(Host, "Trip.yaml"), SearchesPageTests.Search("First trip"));
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/guided-setup/email/turn-on");
            Dictionary<string, string> Before = Snapshot(Host.Folder);
            Assert.That((await Owner.Post("/guided-setup/email/turn-on?handler=turnon", GuidedForm.Fields(Page, "/guided-setup/email/turn-on", null, ("__RequestVerificationToken", "forged")).ToArray())).Status,
                Is.EqualTo(HttpStatusCode.BadRequest));
            Owner.Origin = "http://evil.example";
            Assert.That((await Owner.Post("/guided-setup/email/turn-on?handler=turnon", GuidedForm.Fields(Page, "/guided-setup/email/turn-on", null).ToArray())).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Owner.Origin = Owner.Root.GetLeftPart(UriPartial.Authority);
            Assert.That((await Owner.Post("/guided-setup", ("action", "continue"))).Status, Is.EqualTo(HttpStatusCode.NotFound), "step 1 posts nothing");
            Assert.That((await Owner.Post("/guided-setup/email/done", ("action", "continue"))).Status, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await Owner.Post("/guided-setup/email/turn-on", ("action", "continue"))).Status, Is.EqualTo(HttpStatusCode.NotFound), "turning on is its own handler");
            Assert.That(Snapshot(Host.Folder), Is.EqualTo(Before));
        }

        [Test]
        public void TheGuidedSetupsCode_ReachesNoMonitoringStartSearchRunRefreshOrDeletion()
        {
            Type[] Types = typeof(GuidedSetupModel).Assembly.GetTypes()
                .Where(T => T == typeof(GuidedSetupModel) || (T.FullName ?? "").Contains("GuidedSetup", StringComparison.Ordinal) || T == typeof(IndexModel) || (T.FullName ?? "").Contains("Pages_Index", StringComparison.Ordinal))
                .ToArray();
            Assert.That(Types.Select(T => T.Name), Does.Contain("Pages_GuidedSetup").And.Contain("Pages_Index"), "the compiled pages are scanned too");
            List<string> Called = IlReferences.ReferencedMembers(Types).Select(C => C.Member).Distinct().ToList();
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.RecDotOrg.api.", "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch",
                "CampertronLibrary.function.RecDotOrg.data.", "CampertronLibrary.function.Base.Notifications", "CampertronLibrary.function.Base.SmtpPassword::Resolve",
                "CampertronLibrary.function.Base.NtfyToken::Resolve", "CampsiteConfig::", "System.Net.Http.", "System.Net.Mail.", "System.Diagnostics.Process",
                "System.IO.File::", "System.IO.Directory::", "System.IO.FileInfo::Delete", "System.Console::", "CampertronWeb.WebMonitoring::Act", "CampertronWeb.WebMonitoring::Start",
                "CampertronWeb.WebRefresh::Begin", "CampertronWeb.WebResults", "Microsoft.AspNetCore.Http.ISession",
            };
            Assert.That(Called.Where(M => Forbidden.Any(F => M.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Forms.GuidedSetupStatus::Read"), "where the user is comes from the saved files");
            Assert.That(Called, Does.Contain("CampertronWeb.WebSettingsForm::Apply").And.Contain("CampertronWeb.WebSettingsForm::Run"), "the settings and searches pages' own adapter");
            Assert.That(Called, Does.Contain("CampertronWeb.WebTestSend::Send"), "the settings pages' own test");
        }

        private sealed class Publishes : HttpMessageHandler
        {
            internal int Count;
            internal bool Failing;
            protected override HttpResponseMessage Send(HttpRequestMessage Request, CancellationToken Cancel)
            {
                Interlocked.Increment(ref Count);
                return Failing
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(ServerText) }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"guided1\"}") };
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Cancel) => Task.FromResult(Send(Request, Cancel));
        }
    }

    // the guided setup's forms as a browser sends them: every input of the form that posts to the address (a check box or a
    // radio button only when it is checked), then the changes and the button's action
    internal static class GuidedForm
    {
        internal static List<(string Name, string Value)> Fields(WebPage Page, string Address, string? Action, params (string Name, string Value)[] Changes)
        {
            XElement Form = Page.Xml().Descendants("form").First(F => ((string?)F.Attribute("action") ?? "").Split('?')[0] == Address && (string?)F.Attribute("method") == "post");
            var Fields = new List<(string, string)>();
            foreach (XElement Input in Form.Descendants("input"))
            {
                string? Name = (string?)Input.Attribute("name");
                string Type = (string?)Input.Attribute("type") ?? "text";
                if (Name == null || Input.Attribute("disabled") != null || Name == "__RequestVerificationToken")
                {
                    continue;
                }
                if ((Type == "checkbox" || Type == "radio") && Input.Attribute("checked") == null)
                {
                    continue;
                }
                Fields.Add((Name, (string?)Input.Attribute("value") ?? string.Empty));
            }
            foreach ((string Name, string Value) in Changes)
            {
                Fields.RemoveAll(F => F.Item1 == Name);
                Fields.Add((Name, Value));
            }
            if (Action != null)
            {
                Fields.Add(("action", Action));
            }
            return Fields;
        }
    }
}
