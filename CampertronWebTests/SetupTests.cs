using System.Net;
using CampertronWeb.Pages;

namespace CampertronWebTests
{
    // The first start: a password is required and none is set, so only the setup answers, and only the one-time setup code
    // from the console sets the password (D81, D92)
    public class SetupTests
    {
        private static string RecordOf(WebTestHost Host) => File.ReadAllText(Path.Join(Host.Folder, "Web.password"));

        [Test]
        public async Task WithoutAPassword_EveryPageGoesToTheSetup_AndNothingElseAnswers()
        {
            await using WebTestHost Host = await WebTestHost.Start();
            using Browser Visitor = Host.Browser();
            foreach (string Path_ in new[] { "/", "/signin", "/account/password" })
            {
                WebPage Page = await Visitor.Get(Path_);
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.Redirect), Path_);
                Assert.That(Page.Location, Is.EqualTo("/setup"), Path_);
            }
            WebPage Setup = await Visitor.Get("/setup");
            Assert.That(Setup.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Setup.Text(), Does.Contain("No password is set yet"));
            Assert.That(File.Exists(Path.Join(Host.Folder, "Web.password")), Is.False, "looking at the setup creates nothing");
        }

        [Test]
        public async Task TheRightCode_SetsThePassword_AsAHashInAnOwnerOnlyFile_AndSignsNobodyIn()
        {
            await using WebTestHost Host = await WebTestHost.Start();
            using Browser Owner = Host.Browser();
            string Code = Host.NewSetupCode();
            await Owner.Get("/setup");
            WebPage Done = await Owner.Post("/setup", ("Code", Code), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password));
            Assert.That(Done.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Done.Location, Is.EqualTo("/signin?notice=password-set"));
            Assert.That(Done.SetCookies.Any(Cookie => Cookie.StartsWith("campertron-session", StringComparison.Ordinal)), Is.False, "the setup signs nobody in");

            string Record = RecordOf(Host);
            Assert.That(Record, Does.Match(@"^\$pbkdf2-sha256\$i=100000\$[A-Za-z0-9+/]{22}\$[A-Za-z0-9+/]{43}\n$"));
            Assert.That(Record, Does.Not.Contain(WebTestHost.Password));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(Path.Join(Host.Folder, "Web.password")), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
            Assert.That(Directory.GetFiles(Host.Folder).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Web.password" }), "no temporary file, no backup, no Web.yaml");
            Assert.That(new WebPasswordFile(Host.Folder).Verify(WebTestHost.Password), Is.True);

            //the setup is over: its page goes to the sign-in, and the code opens nothing again
            Assert.That((await Owner.Get("/setup")).Location, Is.EqualTo("/signin"));
            WebPage Again = await Owner.Post("/setup", ("Code", Code), ("Password", "another password 1"), ("Repeat", "another password 1"));
            Assert.That(Again.Location, Is.EqualTo("/signin"));
            Assert.That(new WebPasswordFile(Host.Folder).Verify(WebTestHost.Password), Is.True, "the password is still the first one");
            WebPage SignIn = await Owner.Get("/signin?notice=password-set");
            Assert.That(SignIn.Text(), Does.Contain("The password is set. Sign in with it."));
        }

        [Test]
        public async Task AWrongCode_ChangesNothing_IsCountedOnTheConsole_AndLeavesTheCodeUsable()
        {
            await using WebTestHost Host = await WebTestHost.Start();
            using Browser Visitor = Host.Browser();
            string Code = Host.NewSetupCode();
            await Visitor.Get("/setup");
            foreach (string Wrong in new[] { "AAAA-AAAA-AAAA", "", "abc", new string('A', 31), "ABCD-EFGH-JKM!", "<script>alert(1)</script>" })
            {
                WebPage Refused = await Visitor.Post("/setup", ("Code", Wrong), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password));
                if (Refused.Status == HttpStatusCode.TooManyRequests)
                {
                    break;
                }
                Assert.That(Refused.Status, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(Refused.Text(), Does.Contain(SetupModel.WrongCode));
                Assert.That(Refused.Body, Does.Not.Contain(WebTestHost.Password).And.Not.Contain(Code).And.Not.Contain("alert(1)"), "nothing entered and no code is on the page");
            }
            Assert.That(File.Exists(Path.Join(Host.Folder, "Web.password")), Is.False);
            Assert.That(Host.Lines.Count(Line => Line.StartsWith("Web UI: failed sign-in from 127.0.0.1", StringComparison.Ordinal)), Is.EqualTo(1), "one line a minute for an address");
            Assert.That(string.Join("\n", Host.Lines), Does.Not.Contain("AAAA").And.Not.Contain("script"), "nothing that was entered reaches the console");
            Assert.That(Host.Service<WebSetupCode>().Check(Code), Is.EqualTo(WebSetupCodeResult.Accepted), "a wrong entry does not use the code up");
        }

        [Test]
        public async Task ARefusedPassword_DoesNotUseTheCodeUp()
        {
            await using WebTestHost Host = await WebTestHost.Start();
            using Browser Owner = Host.Browser();
            string Code = Host.NewSetupCode();
            await Owner.Get("/setup");
            WebPage Short = await Owner.Post("/setup", ("Code", Code), ("Password", "short"), ("Repeat", "short"));
            Assert.That(Short.Text(), Does.Contain("The password is too short: use at least 10 characters."));
            WebPage Different = await Owner.Post("/setup", ("Code", Code), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password + "x"));
            Assert.That(Different.Text(), Does.Contain("The two passwords are not the same"));
            Assert.That(File.Exists(Path.Join(Host.Folder, "Web.password")), Is.False);
            //lower case, without hyphens: the same code
            WebPage Done = await Owner.Post("/setup", ("Code", Code.Replace("-", " ").ToLowerInvariant()), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password));
            Assert.That(Done.Location, Is.EqualTo("/signin?notice=password-set"));
        }

        [Test]
        public async Task ACodeOlderThanThirtyMinutes_IsRefused_AndANewOneIsWrittenToTheConsoleOnRequest()
        {
            await using WebTestHost Host = await WebTestHost.Start();
            using Browser Owner = Host.Browser();
            string Code = Host.NewSetupCode();
            await Owner.Get("/setup");

            //while the code is valid nobody can replace it
            WebPage Still = await Owner.Post("/setup?handler=NewCode");
            Assert.That(Still.Text(), Does.Contain(SetupModel.CodeStillValid));
            Assert.That(Host.Lines, Has.None.Contains("setup code"));
            Assert.That(Host.Service<WebSetupCode>().Check(Code), Is.EqualTo(WebSetupCodeResult.Accepted));

            Host.Now = Host.Now.AddMinutes(30);
            WebPage Expired = await Owner.Post("/setup", ("Code", Code), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password));
            Assert.That(Expired.Text(), Does.Contain(SetupModel.ExpiredCode));
            Assert.That(File.Exists(Path.Join(Host.Folder, "Web.password")), Is.False);

            WebPage Fresh = await Owner.Post("/setup?handler=NewCode");
            Assert.That(Fresh.Text(), Does.Contain(SetupModel.NewCodeWritten));
            //the page after it still sets the password: its form posts to the setup, not to the address of the other form
            Assert.That(Fresh.Xml().Descendants("form").Select(Form => (string?)Form.Attribute("action")), Is.EqualTo(new[] { "/setup", "/setup?handler=NewCode" }));
            string Line = Host.Lines.Single(Text => Text.StartsWith("Web UI setup code: >>> ", StringComparison.Ordinal));
            Assert.That(Line, Does.Match(@"^Web UI setup code: >>> [A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4} <<<$"));
            string NewCode = Line.Substring("Web UI setup code: >>> ".Length, 14);
            Assert.That(Fresh.Body, Does.Not.Contain(NewCode), "the code is on the console only, never on a page");
            Assert.That(NewCode, Is.Not.EqualTo(Code));
            WebPage Done = await Owner.Post("/setup", ("Code", NewCode), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password));
            Assert.That(Done.Location, Is.EqualTo("/signin?notice=password-set"));
        }

        [Test]
        public async Task TwoSetupsAtOnce_SetOnePassword()
        {
            await using WebTestHost Host = await WebTestHost.Start();
            string Code = Host.NewSetupCode();
            using Browser One = Host.Browser();
            using Browser Two = Host.Browser();
            await One.Get("/setup");
            await Two.Get("/setup");
            Task<WebPage> First = One.Post("/setup", ("Code", Code), ("Password", "the first password"), ("Repeat", "the first password"));
            Task<WebPage> Second = Two.Post("/setup", ("Code", Code), ("Password", "the second password"), ("Repeat", "the second password"));
            WebPage[] Both = await Task.WhenAll(First, Second);
            Assert.That(Both.Count(Page => Page.Location == "/signin?notice=password-set"), Is.EqualTo(1), "the code sets the password once");
            var Passwords = new WebPasswordFile(Host.Folder);
            Assert.That(Passwords.State(), Is.EqualTo(WebPasswordState.Usable));
            Assert.That(new[] { Passwords.Verify("the first password"), Passwords.Verify("the second password") }.Count(Ok => Ok), Is.EqualTo(1));
            Assert.That(Directory.GetFiles(Host.Folder).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Web.password" }));
        }

        [Test]
        public async Task ARestart_MakesTheOldCodeWorthless()
        {
            string Folder = WebTestHost.NewFolder();
            try
            {
                string Code;
                await using (WebTestHost Before = await WebTestHost.Start(Folder: Folder))
                {
                    Code = Before.NewSetupCode();
                }
                await using WebTestHost After = await WebTestHost.Start(Folder: Folder);
                using Browser Owner = After.Browser();
                await Owner.Get("/setup");
                WebPage Refused = await Owner.Post("/setup", ("Code", Code), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password));
                Assert.That(Refused.Text(), Does.Contain(SetupModel.ExpiredCode), "the new process has no code until it makes one");
                After.NewSetupCode();
                Refused = await Owner.Post("/setup", ("Code", Code), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password));
                Assert.That(Refused.Text(), Does.Contain(SetupModel.WrongCode));
                Assert.That(File.Exists(Path.Join(Folder, "Web.password")), Is.False);
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        [Test]
        public async Task ASettingsFolderThatCantBeWritten_SaysSo_AndWritesANewCodeToTheConsole()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("needs Unix permissions");
                return;
            }
            if (Environment.UserName == "root")
            {
                Assert.Ignore("root writes to every folder");
            }
            await using WebTestHost Host = await WebTestHost.Start();
            using Browser Owner = Host.Browser();
            string Code = Host.NewSetupCode();
            await Owner.Get("/setup");
            UnixFileMode Before = File.GetUnixFileMode(Host.Folder);
            File.SetUnixFileMode(Host.Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                WebPage Failed = await Owner.Post("/setup", ("Code", Code), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password));
                Assert.That(Failed.Status, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(Failed.Text(), Does.Contain(SetupModel.NotSaved));
                Assert.That(Failed.Body, Does.Not.Contain(Host.Folder), "no path on the page");
                Assert.That(Host.Lines.Count(Text => Text.StartsWith("Web UI setup code: >>> ", StringComparison.Ordinal)), Is.EqualTo(1), "the used code is replaced");
            }
            finally
            {
                File.SetUnixFileMode(Host.Folder, Before);
            }
            Assert.That(File.Exists(Path.Join(Host.Folder, "Web.password")), Is.False);
        }
    }
}
