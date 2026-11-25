using System.Net;
using System.Net.Mail;
using System.Xml.Linq;
using CampertronLibrary.function.Base;

namespace CampertronWebTests
{
    // The web UI's settings pages (v0.3.0's PR 5; business-rules-settings §32): General.yaml, Email.yaml, Ntfy.yaml and
    // Web.yaml, edited through the shared form layer (the desktop GUI's form, D91), on the real host. Every change is a POST
    // with the antiforgery token from this web UI; a value the form refuses is shown at its field and in the summary and
    // never saved; a page that is out of date applies nothing; a change on disk under the user's edits is a conflict; the
    // SMTP password is write-only; Web.yaml's one confirmation (D81) asks once; whether the two variables are set is yes or
    // no; the test email and notification (D86, D88) use the saved file, one at a time, rate-limited, with their secret
    // read only during the send; a random topic is an unsaved edit; a session's settings form ends with it. The email's and
    // ntfy's transports are their seams; nothing reaches a server.
    public class SettingsPageTests
    {
        private const string EmailText = "# my email settings\nsmtpServer: smtp.example.invalid   # the server\nsmtpPort: 587\nsmtpUsername: web-user\n" +
            "sendToAddressList:\n- one@example.invalid\nsendFromAddress: from@example.invalid\n";
        private string Password = string.Empty;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private readonly Dictionary<string, string> Environment_ = new(StringComparer.Ordinal);
        private readonly List<string?> SentWith = new();
        private int EnvironmentReads;

        [SetUp]
        public void Seams()
        {
            Password = "WebCnry" + Guid.NewGuid().ToString("N");
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            Environment_.Clear();
            SentWith.Clear();
            EnvironmentReads = 0;
            CampsiteConfig.SendResultsEmail = (Settings, Message) => { lock (SentWith) { SentWith.Add(CampsiteConfig.SmtpCredential(Settings).Password); } };
            SmtpPassword.GetEnvironment = Name => { Interlocked.Increment(ref EnvironmentReads); return Environment_.TryGetValue(Name, out string? Value) ? Value : null; };
            NtfyToken.GetEnvironment = Name => { Interlocked.Increment(ref EnvironmentReads); return Environment_.TryGetValue(Name, out string? Value) ? Value : null; };
        }

        [TearDown]
        public void RestoreSeams()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
        }

        private static string FileOf(WebTestHost Host, string Name) => Path.Join(Host.Folder, Name);

        // ---- the pages and who may use them ----

        [Test]
        public async Task TheSettingsPages_NeedTheOwner_AndNameOnlyTheirFourFiles()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            WebPage Refused = await Visitor.Get("/settings/email");
            Assert.That(Refused.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Refused.Location, Is.EqualTo("/signin?returnUrl=%2Fsettings%2Femail"));
            Assert.That((await Visitor.Post("/settings/email", ("action", "save"))).Status, Is.Not.EqualTo(HttpStatusCode.OK), "no change without the owner");

            using Browser Owner = await Host.SignedIn();
            Assert.That((await Owner.Get("/settings")).Location, Is.EqualTo("/settings/general"));
            foreach (string File_ in new[] { "general", "email", "ntfy", "web" })
            {
                WebPage Page = await Owner.Get("/settings/" + File_);
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), File_);
                Assert.That(Page.Header("Cache-Control"), Does.Contain("no-store"), File_);
                Assert.That(Page.Header("Content-Security-Policy"), Does.Contain("script-src 'self'").And.Contain("form-action 'self'"), File_);
                Assert.That(Page.Header("X-Frame-Options"), Is.EqualTo("DENY"), File_);
            }
            foreach (string Other in new[] { "/settings/Email", "/settings/General.yaml", "/settings/search", "/settings/..%2FEmail.yaml", "/settings/web/extra" })
            {
                Assert.That((await Owner.Get(Other)).Status, Is.EqualTo(HttpStatusCode.NotFound), Other);
            }
            Assert.That((await Owner.StartPage()).Text(), Does.Contain("settings"));

            await using WebTestHost Open = await WebTestHost.Start("passwordProtection: false\n");
            using Browser Anyone = Open.Browser();
            Assert.That((await Anyone.Get("/settings/web")).Status, Is.EqualTo(HttpStatusCode.OK), "without password protection everyone is the owner (D81)");
        }

        [Test]
        public async Task EveryChange_IsAPostWithTheFormsTokenFromThisWebUI_AndAGetChangesNothing()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            File.WriteAllText(FileOf(Host, "Email.yaml"), EmailText);
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/settings/email");
            List<(string, string)> Fields = SettingsForm.Fields(Page, "save", ("f.smtpServer", "changed.example.invalid"));

            Assert.That((await Owner.Get("/settings/email?action=save&f.smtpServer=changed.example.invalid")).Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await Owner.Post("/settings/email", Fields.Append(("__RequestVerificationToken", "forged")).ToArray())).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Owner.Origin = "http://evil.example";
            Assert.That((await Owner.Post("/settings/email", Fields.ToArray())).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Owner.Origin = Host.Root.GetLeftPart(UriPartial.Authority);
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Is.EqualTo(EmailText), "nothing changed");

            WebPage Saved = await Owner.Post("/settings/email", Fields.ToArray());
            Assert.That(Saved.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Is.EqualTo(EmailText.Replace("smtp.example.invalid   #", "changed.example.invalid   #")), "one value changed, the comments kept");
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml.bak")), Is.EqualTo(EmailText), "the backup of the desktop GUI's writer");
            Assert.That(Saved.Text(), Does.Contain("Saved Email.yaml. The previous version is in Email.yaml.bak."));
            Assert.That(SettingsForm.Element(Saved, "status")!.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"), "the result takes the focus");
        }

        [Test]
        public async Task AValueTheFormRefuses_IsShownAtItsFieldAndAtTheTop_KeepsWhatWasTyped_AndIsNeverSaved()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            File.WriteAllText(FileOf(Host, "Email.yaml"), EmailText);
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/settings/email");
            WebPage Refused = await Owner.Post("/settings/email", SettingsForm.Fields(Page, "save", ("f.smtpPort", "five-eight-seven"), ("f.smtpUsername", "typed-user")).ToArray());

            Assert.That(Refused.Status, Is.EqualTo(HttpStatusCode.OK));
            XElement Port = SettingsForm.Element(Refused, "f-smtpPort")!;
            Assert.That(Port.Attribute("aria-invalid")?.Value, Is.EqualTo("true"));
            Assert.That(Port.Attribute("value")?.Value, Is.EqualTo("five-eight-seven"), "what was typed stays");
            Assert.That(SettingsForm.Element(Refused, "f-smtpUsername")!.Attribute("value")?.Value, Is.EqualTo("typed-user"));
            Assert.That(SettingsForm.Element(Refused, "f-smtpPort-msg")!.Value, Is.EqualTo("Error: Enter a port number, such as 587."), "the form's own words");
            XElement Summary = SettingsForm.Element(Refused, "error-summary")!;
            Assert.That(Summary.Attribute("role")?.Value, Is.EqualTo("alert"));
            Assert.That(Summary.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));
            Assert.That(Summary.Descendants("a").Any(A => A.Attribute("href")?.Value == "#f-smtpPort"), Is.True);
            Assert.That(Refused.Xml().Descendants("title").Single().Value, Does.StartWith("Error: "));
            Assert.That(Refused.Text(), Does.Contain("Nothing was saved: fix the errors shown first."));
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Is.EqualTo(EmailText));
            Assert.That(File.Exists(FileOf(Host, "Email.yaml.bak")), Is.False);

            //Revert: the file's values again
            WebPage Reverted = await Owner.Post("/settings/email", SettingsForm.Fields(Refused, "revert").ToArray());
            Assert.That(SettingsForm.Element(Reverted, "f-smtpPort")!.Attribute("value")?.Value, Is.EqualTo("587"));
            Assert.That(Reverted.Text(), Does.Contain("Your changes to Email.yaml were reverted."));

            //a refused value alone never reaches the file's session, and Revert takes it back too (found by the browser check)
            WebPage Alone = await Owner.Post("/settings/email", SettingsForm.Fields(Reverted, "check", ("f.smtpPort", "x")).ToArray());
            Assert.That(SettingsForm.Element(Alone, "f-smtpPort")!.Attribute("aria-invalid")?.Value, Is.EqualTo("true"));
            WebPage Back = await Owner.Post("/settings/email", SettingsForm.Fields(Alone, "revert").ToArray());
            Assert.That(SettingsForm.Element(Back, "f-smtpPort")!.Attribute("value")?.Value, Is.EqualTo("587"));
            Assert.That(SettingsForm.Element(Back, "error-summary"), Is.Null);
        }

        [Test]
        public async Task TheSmtpPassword_IsWriteOnly_ReplacedAndRemovedButNeverShown()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            File.WriteAllText(FileOf(Host, "Email.yaml"), EmailText + "smtpPassword: " + Password + "\n");
            using Browser Owner = await Host.SignedIn();
            var Bodies = new List<string>();
            WebPage Page = await Owner.Get("/settings/email");
            Bodies.Add(Page.Body);
            Assert.That(SettingsForm.Element(Page, "f-smtpPassword-state")!.Value, Is.EqualTo("A plain-text password is set in Email.yaml. It is never shown here."));
            XElement Box = SettingsForm.Element(Page, "f-smtpPassword")!;
            Assert.That((Box.Attribute("type")?.Value, Box.Attribute("value"), Box.Attribute("autocomplete")?.Value), Is.EqualTo(("password", (XAttribute?)null, "new-password")));

            string NewPassword = Password + "-new";
            WebPage Checked = await Owner.Post("/settings/email", SettingsForm.Fields(Page, "check", ("secret.smtpPassword", NewPassword)).ToArray());
            Bodies.Add(Checked.Body);
            Assert.That(SettingsForm.Element(Checked, "f-smtpPassword-state")!.Value, Does.StartWith("A new plain-text password was entered"));
            Assert.That(SettingsForm.Element(Checked, "f-smtpPassword")!.Attribute("value"), Is.Null, "never put back into the page");
            Assert.That(Checked.Text(), Does.Contain("smtpPassword (hidden)"));

            //an empty box keeps the entered password
            WebPage Saved = await Owner.Post("/settings/email", SettingsForm.Fields(Checked, "save").ToArray());
            Bodies.Add(Saved.Body);
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Does.Contain("smtpPassword: " + NewPassword));

            WebPage Removed = await Owner.Post("/settings/email", SettingsForm.Fields(Saved, "remove-password").ToArray());
            Bodies.Add(Removed.Body);
            Assert.That(SettingsForm.Element(Removed, "f-smtpPassword-state")!.Value, Is.EqualTo("The plain-text password will be removed from Email.yaml when you save."));
            Bodies.Add((await Owner.Post("/settings/email", SettingsForm.Fields(Removed, "save").ToArray())).Body);
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Does.Not.Contain(NewPassword));
            foreach (string Body in Bodies)
            {
                Assert.That(Body, Does.Not.Contain(Password), "no page holds the password, the old or the new one");
            }
        }

        // D111: replacing or removing the plain-text password on the web page leaves it out of Email.yaml.bak, which keeps
        // the previous settings; the page says so, and no page, file of the folder or backup holds a password Email.yaml no
        // longer has
        [Test]
        public async Task AReplacedOrRemovedPassword_IsNeverLeftInTheBackup()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            string Original = EmailText + "smtpPassword: " + Password + "\n";
            File.WriteAllText(FileOf(Host, "Email.yaml"), Original);
            string Replacement = "WebReplacement" + Guid.NewGuid().ToString("N");
            using Browser Owner = await Host.SignedIn();
            var Bodies = new List<string>();
            string[] Holding(string Value) => Directory.GetFiles(Host.Folder).Where(F => File.ReadAllText(F).Contains(Value, StringComparison.Ordinal)).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal).ToArray()!;

            WebPage Page = await Owner.Get("/settings/email");
            WebPage Replaced = await Owner.Post("/settings/email", SettingsForm.Fields(Page, "save", ("secret.smtpPassword", Replacement), ("f.smtpServer", "smtp2.example.invalid")).ToArray());
            Bodies.Add(Replaced.Body);
            Assert.That(SettingsForm.Element(Replaced, "status")!.Value, Does.Contain("Saved Email.yaml. The previous version is in Email.yaml.bak, without its plain-text password."));
            Assert.That(Holding(Password), Is.Empty, "the replaced password is in no file: the file, its backup or a temporary file");
            Assert.That(Holding(Replacement), Is.EqualTo(new[] { "Email.yaml" }));
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml.bak")), Is.EqualTo(EmailText + "smtpPassword: ''\n"), "the previous settings (the old server), no password");

            WebPage Removed = await Owner.Post("/settings/email", SettingsForm.Fields(Replaced, "remove-password").ToArray());
            WebPage Saved = await Owner.Post("/settings/email", SettingsForm.Fields(Removed, "save").ToArray());
            Bodies.Add(Removed.Body);
            Bodies.Add(Saved.Body);
            Assert.That(SettingsForm.Element(Saved, "status")!.Value, Does.Contain("without its plain-text password"));
            Assert.That(Holding(Replacement), Is.Empty);
            Assert.That(Holding(Password), Is.Empty);
            Assert.That(SettingsForm.Element(Saved, "f-smtpPassword-state")!.Value, Is.EqualTo("No plain-text password is set in Email.yaml."));
            foreach (string Body in Bodies)
            {
                Assert.That(Body, Does.Not.Contain(Password).And.Not.Contain(Replacement).And.Not.Contain(Host.Folder));
            }
        }

        [Test]
        public async Task AnOutOfDatePage_AppliesNothing_AndAChangeOnDiskUnderTheEditsIsAConflict()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            string EmailFile = FileOf(Host, "Email.yaml");
            File.WriteAllText(EmailFile, EmailText);
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/settings/email");
            List<(string, string)> First = SettingsForm.Fields(Page, "save", ("f.smtpUsername", "first-user"));
            await Owner.Post("/settings/email", First.ToArray());
            Assert.That(File.ReadAllText(EmailFile), Does.Contain("smtpUsername: first-user"));

            //the same page sent again (the back button, a second tab): nothing applied
            WebPage Again = await Owner.Post("/settings/email", SettingsForm.Fields(Page, "save", ("f.smtpUsername", "stale-user")).ToArray());
            Assert.That(SettingsForm.Element(Again, "page-notice")!.Value, Does.StartWith("Warning: This page was out of date"));
            Assert.That(File.ReadAllText(EmailFile), Does.Contain("smtpUsername: first-user").And.Not.Contain("stale-user"));

            //another program changes the file while this page has an edit: a conflict, nothing overwritten
            File.WriteAllText(EmailFile, File.ReadAllText(EmailFile).Replace("smtpPort: 587", "smtpPort: 465"));
            WebPage Conflict = await Owner.Post("/settings/email", SettingsForm.Fields(Again, "save", ("f.smtpServer", "mine.example.invalid")).ToArray());
            Assert.That(SettingsForm.Element(Conflict, "conflict"), Is.Not.Null);
            Assert.That(File.ReadAllText(EmailFile), Does.Contain("smtpPort: 465").And.Not.Contain("mine.example.invalid"), "the other program's version stays");
            WebPage Mine = await Owner.Post("/settings/email", SettingsForm.Fields(Conflict, "apply-mine").ToArray());
            await Owner.Post("/settings/email", SettingsForm.Fields(Mine, "save").ToArray());
            Assert.That(File.ReadAllText(EmailFile), Does.Contain("smtpPort: 465").And.Contain("mine.example.invalid"), "both kept, nothing lost");
        }

        [Test]
        public async Task WebYaml_AsksOnceForTheOpenState_ThenNeverAgain_AndTheRunningWebUIKeepsItsPassword()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            //a web UI started without --listen: Web.yaml's networkListening decides where it listens (the test host's own
            //--listen 127.0.0.1 would decide instead: see the next test)
            Host.Settings.ListensOnNetwork = null;
            string WebFile = FileOf(Host, "Web.yaml");
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/settings/web");
            Assert.That(Page.Text(), Does.Contain("Web.yaml does not exist yet."));

            //unchecked: the box is left out, as a browser leaves it
            List<(string, string)> Off = SettingsForm.Fields(Page, "save").Where(F => F.Item1 != "f.passwordProtection").ToList();
            WebPage Asked = await Owner.Post("/settings/web", Off.ToArray());
            XElement Question = SettingsForm.Element(Asked, "confirmation")!;
            Assert.That(Question.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));
            Assert.That(Question.Value, Does.Contain("Without a password, everyone who can reach this computer on the network can open Campertron's web UI"));
            Assert.That(File.Exists(WebFile), Is.False, "nothing written before the confirmation");

            WebPage Confirmed = await Owner.Post("/settings/web", SettingsForm.Fields(Asked, "confirm-save").Where(F => F.Item1 != "f.passwordProtection").ToArray());
            Assert.That(SettingsForm.Element(Confirmed, "confirmation"), Is.Null);
            Assert.That(File.ReadAllText(WebFile), Does.Contain("passwordProtection: false").And.Contain("networkListening: true"));

            WebPage Next = await Owner.Post("/settings/web", SettingsForm.Fields(Confirmed, "row-add.allowedHosts").ToArray());
            Assert.That(SettingsForm.Element(Next, "f-allowedHosts-0")!.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"), "the new row takes the focus");
            WebPage Saved = await Owner.Post("/settings/web", SettingsForm.Fields(Next, "save", ("f.allowedHosts.0", "campbox.home.example")).ToArray());
            Assert.That(SettingsForm.Element(Saved, "confirmation"), Is.Null, "the accepted state never asks again");
            Assert.That(File.ReadAllText(WebFile), Does.Contain("campbox.home.example"));

            //the running web UI read Web.yaml when it started: it still requires its password until it starts again
            using Browser Stranger = Host.Browser();
            Assert.That((await Stranger.Get("/settings/web")).Status, Is.EqualTo(HttpStatusCode.Redirect));
        }

        [Test]
        public async Task ThisWebUIsListen_DecidesForIt_SoOnThisComputerOnlyNothingIsOpenAndNothingAsks()
        {
            //the test host was started with --listen 127.0.0.1: whatever networkListening says, it is not on the network
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            Assert.That(Host.Settings.ListensOnNetwork, Is.False);
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/settings/web");
            WebPage Saved = await Owner.Post("/settings/web", SettingsForm.Fields(Page, "save").Where(F => F.Item1 != "f.passwordProtection").ToArray());
            Assert.That(SettingsForm.Element(Saved, "confirmation"), Is.Null);
            Assert.That(File.ReadAllText(FileOf(Host, "Web.yaml")), Does.Contain("passwordProtection: false").And.Contain("networkListening: true"));
        }

        [Test]
        public async Task AFileTheWebUICantRead_IsSaid_WithoutTheSettingsFolder()
        {
            if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            {
                Assert.Ignore("needs Unix permissions that bind the test's own user");
            }
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            string EmailFile = FileOf(Host, "Email.yaml");
            File.WriteAllText(EmailFile, EmailText);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(EmailFile, UnixFileMode.None);
            }
            try
            {
                using Browser Owner = await Host.SignedIn();
                WebPage Page = await Owner.Get("/settings/email");
                Assert.That(SettingsForm.Element(Page, "state")!.Value, Does.Contain("Email.yaml").Or.Contain("the settings folder"));
                Assert.That(Page.Body, Does.Not.Contain(Host.Folder), "a page never shows a path (§31)");
            }
            finally
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(EmailFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
        }

        [Test]
        public async Task AHandlerNamedInTheFormsBody_IsNoTest_SoTheTestsLimitCantBeAvoided()
        {
            //Razor Pages takes a handler's name from the address only: a form field "handler" runs the page's own handler
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            File.WriteAllText(FileOf(Host, "Email.yaml"), EmailText + "smtpPassword: " + Password + "\n");
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/settings/email");
            WebPage Posted = await Owner.Post("/settings/email", SettingsForm.Fields(Page, "check", ("handler", "test")).ToArray());
            Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(SettingsForm.Element(Posted, "test-result"), Is.Null);
            Assert.That(SentWith, Is.Empty, "nothing sent");
        }

        [Test]
        public async Task TheVariables_AreSaidAsYesOrNo_NeverTheirValues()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = await Host.SignedIn();
            Assert.That(SettingsForm.Element(await Owner.Get("/settings/email"), "email-variable")!.Element("dd")!.Value, Is.EqualTo("No"));
            Environment_["CAMPERTRON_EMAIL_PASSWORD"] = Password + "-variable";
            Environment_["CAMPERTRON_NTFY_TOKEN"] = Password + "-token";
            WebPage Email = await Owner.Get("/settings/email");
            WebPage Ntfy = await Owner.Get("/settings/ntfy");
            Assert.That(SettingsForm.Element(Email, "email-variable")!.Element("dd")!.Value, Is.EqualTo("Yes"));
            Assert.That(SettingsForm.Element(Ntfy, "ntfy-variable")!.Element("dd")!.Value, Is.EqualTo("Yes"));
            Assert.That(Email.Body + Ntfy.Body, Does.Not.Contain(Password));
        }

        // ---- the test email and notification (D86, D88) ----

        [Test]
        public async Task ATestEmail_UsesTheSavedFile_ReadsItsSecretOnlyThen_AndSaysItsResult()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            string Secret = Path.Join(Host.Folder, "..", "WebTestSecret-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(Secret, Password);
            try
            {
                File.WriteAllText(FileOf(Host, "Email.yaml"), EmailText + "smtpPasswordFile: " + Path.GetFullPath(Secret) + "\n");
                using Browser Owner = await Host.SignedIn();
                WebPage Page = await Owner.Get("/settings/email");
                //the page's yes-or-no line asks whether the variable is set (D82); the password file is opened only by the send,
                //which the unit and GUI tests count
                Assert.That(SentWith, Is.Empty, "nothing is sent by showing the page");
                Dictionary<string, long> Before = Directory.GetFiles(Host.Folder).ToDictionary(F => F, F => new FileInfo(F).Length);

                WebPage Tested = await Owner.Post("/settings/email?handler=test", SettingsForm.Fields(Page, null).ToArray());
                Assert.That(Tested.Status, Is.EqualTo(HttpStatusCode.OK));
                XElement Result = SettingsForm.Element(Tested, "test-result")!;
                Assert.That(Result.Value, Does.StartWith("Info: the SMTP server accepted the test email for 1 recipient at "));
                Assert.That(Result.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));
                Assert.That(SentWith, Is.EqualTo(new[] { Password }), "the password file's password, read for this send");
                Assert.That(Tested.Body, Does.Not.Contain(Password));
                Assert.That(Directory.GetFiles(Host.Folder).ToDictionary(F => F, F => new FileInfo(F).Length).Where(F => !F.Key.Contains("Web.keys")), Is.EquivalentTo(Before.Where(F => !F.Key.Contains("Web.keys"))),
                    "nothing written: no file, no Camp.History, no monitoring lock");
                Assert.That(Directory.Exists(FileOf(Host, "Camp.History")) || File.Exists(FileOf(Host, "Campertron.monitoring.lock")), Is.False);

                //with an unsaved change the test is refused, and the change stays
                WebPage Changed = await Owner.Post("/settings/email?handler=test", SettingsForm.Fields(Tested, null, ("f.smtpServer", "unsaved.example.invalid")).ToArray());
                Assert.That(SettingsForm.Element(Changed, "test-result")!.Value, Is.EqualTo("Warning: The test uses Email.yaml as it is saved: save or revert your changes first."));
                Assert.That(SettingsForm.Element(Changed, "f-smtpServer")!.Attribute("value")?.Value, Is.EqualTo("unsaved.example.invalid"));
                Assert.That(SentWith, Has.Count.EqualTo(1));
            }
            finally
            {
                File.Delete(Secret);
            }
        }

        [Test]
        public async Task ATest_IsOneAtATimeInTheWebUI_AndRateLimited()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            File.WriteAllText(FileOf(Host, "Email.yaml"), EmailText + "smtpPassword: " + Password + "\n");
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/settings/email");

            WebSettingsWorkspaces Workspaces = Host.Service<WebSettingsWorkspaces>();
            Assert.That(Workspaces.TryBeginTest(), Is.True, "another test holds the web UI's one test");
            WebPage Busy = await Owner.Post("/settings/email?handler=test", SettingsForm.Fields(Page, null).ToArray());
            Assert.That(SettingsForm.Element(Busy, "test-result")!.Value, Is.EqualTo("Warning: another test is being sent from this web UI. Wait for its result, then try again."));
            Assert.That(SentWith, Is.Empty);
            Workspaces.EndTest();

            WebPage Last = Busy;
            for (int i = 0; i < 4; i++)
            {
                Last = await Owner.Post("/settings/email?handler=test", SettingsForm.Fields(Last, null).ToArray());
                Assert.That(Last.Status, Is.EqualTo(HttpStatusCode.OK));
            }
            Assert.That(SentWith, Has.Count.EqualTo(4));
            WebPage Limited = await Owner.Post("/settings/email?handler=test", SettingsForm.Fields(Last, null).ToArray());
            Assert.That(Limited.Status, Is.EqualTo(HttpStatusCode.TooManyRequests), "five test requests a minute from a client");
            Assert.That(SentWith, Has.Count.EqualTo(4));
            Assert.That((await Owner.Post("/settings/email", SettingsForm.Fields(Last, "check").ToArray())).Status, Is.EqualTo(HttpStatusCode.OK), "editing is not limited by it");
        }

        [Test]
        public async Task ATestNotification_PublishesWithTheSavedNtfyYaml_AndARandomTopicIsAnUnsavedEdit()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            string NtfyFile = FileOf(Host, "Ntfy.yaml");
            string Saved = "enabled: false\nserver: https://ntfy.example.invalid\ntopic: webOldTopic0123456789abc\n";
            File.WriteAllText(NtfyFile, Saved);
            var Handler = new Publishes();
            NtfyPublisher.CreateHandler = () => Handler;
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/settings/ntfy");

            WebPage Tested = await Owner.Post("/settings/ntfy?handler=test", SettingsForm.Fields(Page, null).ToArray());
            Assert.That(SettingsForm.Element(Tested, "test-result")!.Value, Does.StartWith("Info: the ntfy server accepted the test notification at "));
            Assert.That(Handler.Count, Is.EqualTo(1));

            WebPage Generated = await Owner.Post("/settings/ntfy", SettingsForm.Fields(Tested, "generate-topic").ToArray());
            XElement Topic = SettingsForm.Element(Generated, "f-topic")!;
            Assert.That(Topic.Attribute("value")?.Value, Does.Match("^campertron-[a-z0-9]{26}$"));
            Assert.That(Topic.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));
            Assert.That(Generated.Text(), Does.Contain("A new random topic is in Topic."));
            Assert.That(File.ReadAllText(NtfyFile), Is.EqualTo(Saved), "nothing written");
            Assert.That(SettingsForm.Element(Generated, "test-unavailable")!.Value, Does.StartWith("The test uses Ntfy.yaml as it is saved"));
            WebPage Reverted = await Owner.Post("/settings/ntfy", SettingsForm.Fields(Generated, "revert").ToArray());
            Assert.That(SettingsForm.Element(Reverted, "f-topic")!.Attribute("value")?.Value, Is.EqualTo("webOldTopic0123456789abc"));
        }

        // D112: a topic replaced on the web page, typed or generated, is left out of Ntfy.yaml.bak, which keeps the previous
        // settings; the page says so; no file of the folder holds a topic Ntfy.yaml no longer has, and the test notification
        // publishes to the saved topic only
        [Test]
        public async Task AReplacedTopic_TypedOrGenerated_IsNeverLeftInTheBackup()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            string NtfyFile = FileOf(Host, "Ntfy.yaml");
            const string Old = "webOldTopicCnry0123456789";
            const string Typed = "webTypedTopicCnry987654321";
            string Saved = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Old + "\ntimeoutSeconds: 20\n";
            File.WriteAllText(NtfyFile, Saved);
            var Handler = new Publishes();
            NtfyPublisher.CreateHandler = () => Handler;
            using Browser Owner = await Host.SignedIn();
            string[] Holding(string Value) => Directory.GetFiles(Host.Folder).Where(F => File.ReadAllText(F).Contains(Value, StringComparison.Ordinal)).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal).ToArray()!;

            WebPage Page = await Owner.Get("/settings/ntfy");
            WebPage Replaced = await Owner.Post("/settings/ntfy", SettingsForm.Fields(Page, "save", ("f.topic", Typed), ("f.timeoutSeconds", "45")).ToArray());
            Assert.That(SettingsForm.Element(Replaced, "status")!.Value, Does.Contain("Saved Ntfy.yaml. The previous version is in Ntfy.yaml.bak, without its topic."));
            Assert.That(File.ReadAllText(NtfyFile + ".bak"), Is.EqualTo(Saved.Replace("topic: " + Old, "topic: ''")), "the previous settings (timeout 20), no topic");
            Assert.That(Holding(Old), Is.Empty);
            Assert.That(Holding(Typed), Is.EqualTo(new[] { "Ntfy.yaml" }));
            Assert.That(Replaced.Body, Does.Not.Contain(Old));

            WebPage Generated = await Owner.Post("/settings/ntfy", SettingsForm.Fields(Replaced, "generate-topic").ToArray());
            string Random = SettingsForm.Element(Generated, "f-topic")!.Attribute("value")!.Value;
            WebPage Again = await Owner.Post("/settings/ntfy", SettingsForm.Fields(Generated, "save").ToArray());
            Assert.That(SettingsForm.Element(Again, "status")!.Value, Does.Contain("without its topic"));
            Assert.That(Holding(Typed), Is.Empty, "the replaced typed topic is in no file");
            Assert.That(Holding(Random), Is.EqualTo(new[] { "Ntfy.yaml" }));

            WebPage Tested = await Owner.Post("/settings/ntfy?handler=test", SettingsForm.Fields(Again, null).ToArray());
            Assert.That(SettingsForm.Element(Tested, "test-result")!.Value, Does.StartWith("Info: the ntfy server accepted the test notification at "));
            Assert.That(Handler.Topics, Is.EqualTo(new[] { Random }), "the saved topic only");
        }

        private sealed class Publishes : HttpMessageHandler
        {
            internal int Count;
            internal readonly List<string?> Topics = new();
            protected override HttpResponseMessage Send(HttpRequestMessage Request, CancellationToken Cancel)
            {
                if (Request.Content != null && System.Text.Json.JsonDocument.Parse(Request.Content.ReadAsStringAsync(Cancel).GetAwaiter().GetResult()).RootElement.TryGetProperty("topic", out System.Text.Json.JsonElement T))
                {
                    lock (Topics)
                    {
                        Topics.Add(T.GetString());
                    }
                }
                Interlocked.Increment(ref Count);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"web1\"}") };
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Cancel) => Task.FromResult(Send(Request, Cancel));
        }

        // ---- a session's settings form ----

        [Test]
        public async Task ASessionsSettingsForm_EndsWithTheSession()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            File.WriteAllText(FileOf(Host, "Email.yaml"), EmailText);
            WebSettingsWorkspaces Workspaces = Host.Service<WebSettingsWorkspaces>();
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/settings/email");
            await Owner.Post("/settings/email", SettingsForm.Fields(Page, "check", ("secret.smtpPassword", Password)).ToArray());
            Assert.That(Workspaces.Count, Is.EqualTo(1));
            await Owner.Post("/signout");
            Assert.That(Workspaces.Count, Is.Zero, "a sign-out ends the form, with the password entered and not saved");

            using Browser Again = await Host.SignedIn();
            await Again.Get("/settings/email");
            using Browser Other = await Host.SignedIn();
            await Other.Get("/settings/general");
            Assert.That(Workspaces.Count, Is.EqualTo(2), "each session its own");
            Host.Service<WebSessions>().Clear();
            Assert.That(Workspaces.Count, Is.Zero, "a password change ends every session's form");
        }
    }

    // A settings page's form as a browser sends it: every field the page shows with its value (a box's text, a checked box
    // or radio button), the hidden fields, and the button that was pressed (null: none, as Enter or the formaction button)
    internal static class SettingsForm
    {
        internal static XElement? Element(WebPage Page, string Id) => Page.Xml().Descendants().FirstOrDefault(E => (string?)E.Attribute("id") == Id);

        internal static List<(string Name, string Value)> Fields(WebPage Page, string? Action, params (string Name, string Value)[] Changes)
        {
            XElement Form = Page.Xml().Descendants("form").Single(F => ((string?)F.Attribute("action") ?? "").StartsWith("/settings/", StringComparison.Ordinal));
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
