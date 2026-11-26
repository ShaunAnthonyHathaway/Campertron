using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CampertronWebTests
{
    // The guided setup's flow as the web UI shows it, step by step (v0.4.0's phase 3: its orchestration moves into the
    // library, and every decision a page shows must stay the same): for each request of three walks through it, the answer
    // (a page, a redirect to which fixed address, a refusal), the step's heading and title, the list of steps with each
    // state, Back and Leave, the element that takes the focus, what the step says after an action (its notice, its
    // problems, what the monitoring's start would refuse, its review), the done page and the start page's section.
    // A walk's observations are compared with the expected lines below, which hold only what a browser sees: no revision,
    // token, folder or time. The forms, the files and the transports are the guided setup's own tests' (GuidedSetupPageTests):
    // nothing reaches a server or opens a database.
    public class GuidedSetupFlowContractTests
    {
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);

        private static string Normal(string Text) => Spaces.Replace(Text, " ").Trim();

        // what a browser sees of an answer, in one line
        private static string Observe(string What, WebPage Page, string Folder)
        {
            var Parts = new List<string> { What + " -> " + (int)Page.Status };
            if (Page.Location != null)
            {
                Parts.Add("location=" + Page.Location);
            }
            if (Page.Status != HttpStatusCode.OK)
            {
                return string.Join(" | ", Parts);
            }
            XDocument Xml = Page.Xml();
            XElement? Element(string Id) => Xml.Descendants().FirstOrDefault(E => (string?)E.Attribute("id") == Id);
            string Items(XElement Container) => "[" + string.Join("; ", Container.Descendants("li").Select(Li => Normal(Li.Value))) + "]";
            Parts.Add("title=" + Normal(Xml.Descendants("title").Single().Value));
            if (Element("step-heading") is XElement Heading)
            {
                Parts.Add("h2=" + Normal(Heading.Value));
            }
            if (Xml.Descendants("nav").FirstOrDefault(N => (string?)N.Attribute("aria-label") == "Guided setup steps") is XElement Steps)
            {
                Parts.Add("steps=[" + string.Join("; ", Steps.Descendants("li").Select(Li => Normal(Li.Value) + ((string?)Li.Attribute("aria-current") == "step" ? "*" : "")
                    + (Li.Element("a") is XElement A ? "@" + (string?)A.Attribute("href") : ""))) + "]");
            }
            if (Xml.Descendants("nav").FirstOrDefault(N => (string?)N.Attribute("class") == "guided-nav") is XElement Nav)
            {
                Parts.Add("nav=[" + string.Join("; ", Nav.Descendants("a").Select(A => Normal(A.Value) + "@" + (string?)A.Attribute("href"))) + "]");
            }
            Parts.Add("focus=" + string.Join(",", Xml.Descendants().Where(E => E.Attribute("autofocus") != null).Select(E => (string?)E.Attribute("id") ?? E.Name.LocalName)));
            foreach (string Id in new[] { "page-notice", "notice", "created", "status", "conflict", "search-state", "new-search-message", "monitoring-state", "turn-on-unavailable", "campground-data", "other-changes" })
            {
                if (Element(Id) is XElement Found)
                {
                    Parts.Add(Id + "=" + Normal(Found.Value));
                }
            }
            foreach (string Id in new[] { "error-summary", "needs", "problems" })
            {
                if (Element(Id) is XElement Found)
                {
                    Parts.Add(Id + "=" + Items(Found));
                }
            }
            if (Xml.Descendants("ul").FirstOrDefault(U => (string?)U.Attribute("class") == "checklist") is XElement Checklist)
            {
                Parts.Add("checklist=" + Items(Checklist));
            }
            foreach (XElement Button in Xml.Descendants("button").Where(B => B.Value.StartsWith("Turn the monitoring on", StringComparison.Ordinal)))
            {
                Parts.Add("turn-on=" + Normal(Button.Value) + (Button.Attribute("disabled") != null ? " (disabled)" : ""));
            }
            foreach (XElement Link in Xml.Descendants("a").Where(A => (string?)A.Attribute("class") == "button-link"))
            {
                Parts.Add("continue=" + Normal(Link.Value) + "@" + (string?)Link.Attribute("href"));
            }
            if (Xml.Descendants("form").Any(F => (string?)F.Attribute("action") == "/monitoring?handler=start"))
            {
                Parts.Add("start-form");
            }
            foreach (XElement Choice in Xml.Descendants("input").Where(I => (string?)I.Attribute("name") == "choice" && I.Attribute("checked") != null))
            {
                Parts.Add("chosen=" + (string?)Choice.Attribute("value"));
            }
            if (Xml.Descendants("section").FirstOrDefault(S => (string?)S.Attribute("class") == "guided") is XElement Section)
            {
                Parts.Add("start-page=[" + string.Join("; ", Section.Elements("p").Select(P => Normal(P.Value) + (P.Element("a") is XElement A ? "@" + (string?)A.Attribute("href") : ""))) + "]");
            }
            return string.Join(" | ", Parts).Replace(Folder, "<folder>", StringComparison.Ordinal);
        }

        // the SMTP password the walks enter or write: write-only (D82), never in a page
        private const string Secret = "Flow-S3cret";

        private sealed class Walk
        {
            internal readonly List<string> Seen = new();
            internal readonly List<string> Bodies = new();
            private readonly WebTestHost Host;
            internal readonly Browser Owner;
            internal Walk(WebTestHost Host, Browser Owner)
            {
                this.Host = Host;
                this.Owner = Owner;
            }
            internal async Task<WebPage> Get(string Address)
            {
                WebPage Page = await Owner.Get(Address);
                Seen.Add(Observe("GET " + Address, Page, Host.Folder));
                Bodies.Add(Page.Body);
                return Page;
            }
            internal async Task<WebPage> Post(string What, string Address, params (string Name, string Value)[] Fields)
            {
                WebPage Page = await Owner.Post(Address, Fields);
                Seen.Add(Observe("POST " + Address + " " + What, Page, Host.Folder));
                Bodies.Add(Page.Body);
                return Page;
            }
        }

        private static string FileOf(WebTestHost Host, string Name) => Path.Join(Host.Folder, Name);

        // ---- the walks ----

        // email from nothing to done: every step's answers, each refusal and each way on
        private static async Task<List<string>> EmailWalk()
        {
            await using WebTestHost Host = await GuidedSetupPageTests.Start();
            using Browser Owner = await Host.SignedIn();
            var W = new Walk(Host, Owner);
            const string Mail = "/guided-setup/email/notifications";
            const string List = "/guided-setup/email/search";
            const string Review = "/guided-setup/email/turn-on";
            await W.Get("/");
            await W.Get("/");
            await W.Get("/guided-setup");
            await W.Get("/guided-setup?continue=1");
            await W.Get("/guided-setup?choice=webhook&continue=1");
            await W.Get("/guided-setup?choice=email&continue=1");
            await W.Get("/guided-setup/email");
            WebPage Page = await W.Get(Mail);
            Page = await W.Post("create", Mail, GuidedForm.Fields(Page, Mail, "create").ToArray());
            Page = await W.Post("continue (a port refused)", Mail, GuidedForm.Fields(Page, Mail, "continue", ("f.smtpPort", "not a port")).ToArray());
            (string, string)[] Typed = { ("f.smtpServer", "smtp.example.invalid"), ("f.smtpPort", "587"), ("f.smtpUsername", "guided-user"), ("secret.smtpPassword", Secret),
                ("f.sendFromAddress", "from@example.invalid") };
            Page = await W.Post("continue (no recipient)", Mail, GuidedForm.Fields(Page, Mail, "continue", Typed).ToArray());
            Page = await W.Post("row-add", Mail, GuidedForm.Fields(Page, Mail, "row-add.sendToAddressList").ToArray());
            await W.Post("continue (ready)", Mail, GuidedForm.Fields(Page, Mail, "continue", ("f.sendToAddressList.0", "to@example.invalid")).ToArray());
            Page = await W.Get(List);
            Page = await W.Get(List);
            Page = await W.Post("create (a name refused)", List + "?handler=create", GuidedForm.Fields(Page, List, null, ("new.name", "../General")).ToArray());
            WebPage Created = await W.Post("create", List + "?handler=create", GuidedForm.Fields(Page, List, null, ("new.name", "Lake trip")).ToArray());
            string Trip = Created.Location!;
            Page = await W.Get(Trip);
            Page = await W.Post("use-campground-id", Trip, GuidedForm.Fields(Page, Trip, "use-campground-id", ("campground.id", SearchesPageTests.Campground)).ToArray());
            Page = await W.Post("continue (not enabled)", Trip, GuidedForm.Fields(Page, Trip, "continue", ("f.totalHumans", "4")).ToArray());
            Page = await W.Post("continue (people refused)", Trip, GuidedForm.Fields(Page, Trip, "continue", ("f.totalHumans", "many")).ToArray());
            Page = await W.Get(Trip);
            await W.Post("continue (ready)", Trip, GuidedForm.Fields(Page, Trip, "continue", ("f.totalHumans", "4"), ("f.autoRun", "true")).ToArray());
            Page = await W.Get(Review);
            Page = await W.Get(Review);
            await W.Get(List);
            //General.yaml with the user's unsaved changes on its settings page
            await Owner.Post("/settings/general", SettingsForm.Fields(await Owner.Get("/settings/general"), "create").ToArray());
            await Owner.Post("/settings/general", SettingsForm.Fields(await Owner.Get("/settings/general"), "check", ("f.refreshRidbDataDayInterval", "12")).ToArray());
            WebPage Held = await W.Get(Review);
            await W.Post("turn on (General.yaml unsaved)", Review + "?handler=turnon", GuidedForm.Fields(Held, Review, null).ToArray());
            await Owner.Post("/settings/general", SettingsForm.Fields(await Owner.Get("/settings/general"), "revert").ToArray());
            await W.Post("turn on (out of date)", Review + "?handler=turnon", GuidedForm.Fields(Held, Review, null).ToArray());
            Page = await W.Get(Review);
            await W.Post("turn on", Review + "?handler=turnon", GuidedForm.Fields(Page, Review, null).ToArray());
            await W.Get("/guided-setup/email/done");
            await W.Get("/guided-setup/email/done");
            await W.Get(Review);
            //turned on already: the review offers Continue only, so the current revision of General.yaml's form comes from its
            //settings page (another tab)
            string Current = (string)(await Owner.Get("/settings/general")).Xml().Descendants("input").First(I => (string?)I.Attribute("name") == "revision").Attribute("value")!;
            await W.Post("turn on (already on)", Review + "?handler=turnon", ("revision", Current));
            await W.Get("/guided-setup/email/done");
            await W.Get("/guided-setup/email");
            await W.Get("/guided-setup");
            await W.Get("/");
            Assert.That(W.Bodies, Has.None.Contain(Secret), "the SMTP password is never in a page");
            return W.Seen;
        }

        // ntfy, the refusals that need a file on disk (a save the form refuses, a conflict), the done page while not
        // finished, and the addresses the guided setup does not answer
        private static async Task<List<string>> NtfyAndRefusalsWalk()
        {
            await using WebTestHost Host = await GuidedSetupPageTests.Start();
            using Browser Owner = await Host.SignedIn();
            var W = new Walk(Host, Owner);
            const string Mail = "/guided-setup/email/notifications";
            const string Ntfy = "/guided-setup/ntfy/notifications";
            await W.Post("turn on (nothing ready)", "/guided-setup/email/turn-on?handler=turnon", GuidedForm.Fields(await Owner.Get("/guided-setup/email/turn-on"), "/guided-setup/email/turn-on", null).ToArray());
            await W.Get("/guided-setup/email/done");
            await W.Get("/guided-setup/ntfy/done");
            await W.Get("/guided-setup/ntfy");
            WebPage Page = await W.Get(Ntfy);
            Page = await W.Post("create", Ntfy, GuidedForm.Fields(Page, Ntfy, "create").ToArray());
            Page = await W.Post("continue (not turned on)", Ntfy, GuidedForm.Fields(Page, Ntfy, "continue", ("f.server", "https://ntfy.example.invalid"), ("f.topic", GuidedSetupPageTests.Topic)).ToArray());
            await W.Post("continue (turned on)", Ntfy, GuidedForm.Fields(Page, Ntfy, "continue", ("f.enabled", "true"), ("present.enabled", "1")).ToArray());
            await W.Get("/guided-setup/ntfy/search");
            await W.Get("/guided-setup/ntfy/turn-on");

            //outputTo Email with an Email.yaml the start refuses: the form's own Save refuses a change (D71)
            File.WriteAllText(FileOf(Host, "General.yaml"), GuidedSetupPageTests.GeneralWith("Email"));
            File.WriteAllText(FileOf(Host, "Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: guided-user\nsendFromAddress: from@example.invalid\n");
            Page = await W.Get(Mail);
            Page = await W.Post("continue (a save the form refuses)", Mail, GuidedForm.Fields(Page, Mail, "continue", ("f.smtpUsername", "another-user")).ToArray());
            await W.Post("revert", Mail, GuidedForm.Fields(Page, Mail, "revert").ToArray());
            //a change on disk under unsaved edits: the form's conflict, and its save's refusal
            File.WriteAllText(FileOf(Host, "Email.yaml"), GuidedSetupPageTests.ValidEmail(Secret));
            Page = await W.Get(Mail);
            Page = await W.Post("check (an edit)", Mail, GuidedForm.Fields(Page, Mail, "check", ("f.smtpServer", "mine.example.invalid")).ToArray());
            File.WriteAllText(FileOf(Host, "Email.yaml"), GuidedSetupPageTests.ValidEmail(Secret).Replace("smtp.example.invalid", "theirs.example.invalid", StringComparison.Ordinal));
            Page = await W.Post("continue (a conflict)", Mail, GuidedForm.Fields(Page, Mail, "continue").ToArray());
            Page = await W.Post("use-theirs", Mail, GuidedForm.Fields(Page, Mail, "use-theirs").ToArray());
            await W.Post("continue (ready)", Mail, GuidedForm.Fields(Page, Mail, "continue").ToArray());
            await W.Get("/guided-setup/email/turn-on");
            await W.Get("/guided-setup/email/done");
            foreach (string Other in new[] { "/guided-setup/Email", "/guided-setup/webhook", "/guided-setup/email/method", "/guided-setup/email/notifications/Email.yaml", "/guided-setup/email/search/Missing.yaml" })
            {
                await W.Get(Other);
            }
            await W.Post("continue", "/guided-setup", ("action", "continue"));
            await W.Post("continue", "/guided-setup/email/done", ("action", "continue"));
            await W.Post("continue", "/guided-setup/email/turn-on", ("action", "continue"));
            Assert.That(W.Bodies, Has.None.Contain(Secret), "the SMTP password is never in a page");
            return W.Seen;
        }

        // the start page's section in each state of the saved files, and where its link leads
        private static async Task<List<string>> StartPageWalk()
        {
            await using WebTestHost Host = await GuidedSetupPageTests.Start();
            using Browser Owner = await Host.SignedIn();
            var Seen = new List<string>();
            //one session (sign-ins are rate-limited): the one lead comes with the first look, when nothing is saved
            async Task Look(string What)
            {
                WebPage First = await Owner.Get("/");
                Seen.Add(Observe(What + ": GET /", First, Host.Folder));
                if (First.Status == HttpStatusCode.Redirect)
                {
                    Seen.Add(Observe(What + ": GET / again", await Owner.Get("/"), Host.Folder));
                }
            }
            await Look("nothing saved");
            File.WriteAllText(FileOf(Host, "Ntfy.yaml"), GuidedSetupPageTests.ValidNtfy);
            await Look("Ntfy.yaml ready");
            File.WriteAllText(FileOf(Host, "Email.yaml"), GuidedSetupPageTests.ValidEmail(Secret));
            await Look("both notification files ready");
            File.WriteAllText(FileOf(Host, "General.yaml"), GuidedSetupPageTests.GeneralWith("Console"));
            await Look("both ready, outputTo Console");
            File.Delete(FileOf(Host, "Ntfy.yaml"));
            await Look("Email.yaml ready");
            File.WriteAllText(FileOf(Host, "Trip.yaml"), SearchesPageTests.Search("Flow trip"));
            await Look("Email.yaml and a search ready");
            File.WriteAllText(FileOf(Host, "General.yaml"), GuidedSetupPageTests.GeneralWith("Email"));
            await Look("email complete");
            File.WriteAllText(FileOf(Host, "Ntfy.yaml"), GuidedSetupPageTests.ValidNtfy);
            File.WriteAllText(FileOf(Host, "General.yaml"), GuidedSetupPageTests.GeneralWith("Ntfy"));
            await Look("ntfy complete");
            File.Delete(FileOf(Host, "Trip.yaml"));
            await Look("outputTo Ntfy without a search");
            return Seen;
        }

        // ---- the expected observations (only what a browser sees) ----

        [Test]
        public async Task TheEmailWalk_IsTheSameFlow() => Assert.That(await EmailWalk(), Is.EqualTo(ExpectedEmailWalk));

        [Test]
        public async Task TheNtfyWalkAndTheRefusals_AreTheSameFlow() => Assert.That(await NtfyAndRefusalsWalk(), Is.EqualTo(ExpectedNtfyAndRefusalsWalk));

        [Test]
        public async Task TheStartPagesSection_IsTheSameInEveryState() => Assert.That(await StartPageWalk(), Is.EqualTo(ExpectedStartPageWalk));

        private static readonly string[] ExpectedEmailWalk =
        {
            "GET / -> 302 | location=/guided-setup",
            "GET / -> 200 | title=Start – Campertron | focus= | start-page=[Campertron is not set up yet: it does not check any search or notify you.; Start guided setup@/guided-setup]",
            "GET /guided-setup -> 200 | title=Guided setup: How you are notified – Campertron | h2=Step 1 of 4: How you are notified | steps=[How you are notified (current step)*@/guided-setup; Email or ntfy settings (to do); A search to check (to do); Review and turn on (to do)] | nav=[Skip guided setup and use the full web UI@/] | focus=",
            "GET /guided-setup?continue=1 -> 200 | title=Error: Guided setup: How you are notified – Campertron | h2=Step 1 of 4: How you are notified | steps=[How you are notified (current step)*@/guided-setup; Email or ntfy settings (to do); A search to check (to do); Review and turn on (to do)] | nav=[Skip guided setup and use the full web UI@/] | focus=error-summary | error-summary=[Choose email or ntfy to continue.]",
            "GET /guided-setup?choice=webhook&continue=1 -> 200 | title=Error: Guided setup: How you are notified – Campertron | h2=Step 1 of 4: How you are notified | steps=[How you are notified (current step)*@/guided-setup; Email or ntfy settings (to do); A search to check (to do); Review and turn on (to do)] | nav=[Skip guided setup and use the full web UI@/] | focus=error-summary | error-summary=[Choose email or ntfy to continue.]",
            "GET /guided-setup?choice=email&continue=1 -> 302 | location=/guided-setup/email",
            "GET /guided-setup/email -> 302 | location=/guided-setup/email/notifications",
            "GET /guided-setup/email/notifications -> 200 | title=Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus= | needs=[Email.yaml: The file does not exist.]",
            "POST /guided-setup/email/notifications create -> 200 | title=Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus=status | status=Info: Created Email.yaml with the default settings. | needs=[Email.yaml: smtpServer is empty.; Email.yaml: smtpPort is not between 1 and 65535.; Email.yaml: sendFromAddress is empty.; Email.yaml: sendToAddressList has no address: add at least one recipient.]",
            "POST /guided-setup/email/notifications continue (a port refused) -> 200 | title=Error: Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus=error-summary | status=Error: Nothing was saved: fix the errors shown first. | error-summary=[SMTP port: Enter a port number, such as 587.] | needs=[Email.yaml: smtpServer is empty.; Email.yaml: smtpPort is not between 1 and 65535.; Email.yaml: sendFromAddress is empty.; Email.yaml: sendToAddressList has no address: add at least one recipient.]",
            "POST /guided-setup/email/notifications continue (no recipient) -> 200 | title=Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus=needs | status=Info: Saved Email.yaml. The previous version is in Email.yaml.bak. | needs=[Email.yaml: sendToAddressList has no address: add at least one recipient.]",
            "POST /guided-setup/email/notifications row-add -> 200 | title=Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus=f-sendToAddressList-0 | needs=[Email.yaml: sendToAddressList has no address: add at least one recipient.]",
            "POST /guided-setup/email/notifications continue (ready) -> 302 | location=/guided-setup/email/search",
            "GET /guided-setup/email/search -> 200 | title=Guided setup: A search to check – Campertron | h2=Step 3 of 4: A search to check | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (current step)*@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/notifications; Leave guided setup and use the full web UI@/] | focus=step-heading | notice=Info: Saved Email.yaml. The previous version is in Email.yaml.bak. | search-state=Warning: to continue, at least one search must be Enabled (autoRun), have a campground and be saved without errors. Campertron needs at least one enabled search before it can monitor. Enable or create a search first. There is no search file: in the Searches tab, create one with New search, choose its campground, check Enabled (autoRun) and save it. Where these words name the Searches tab, in this web UI they mean its Searches page.",
            "GET /guided-setup/email/search -> 200 | title=Guided setup: A search to check – Campertron | h2=Step 3 of 4: A search to check | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (current step)*@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/notifications; Leave guided setup and use the full web UI@/] | focus= | search-state=Warning: to continue, at least one search must be Enabled (autoRun), have a campground and be saved without errors. Campertron needs at least one enabled search before it can monitor. Enable or create a search first. There is no search file: in the Searches tab, create one with New search, choose its campground, check Enabled (autoRun) and save it. Where these words name the Searches tab, in this web UI they mean its Searches page.",
            "POST /guided-setup/email/search?handler=create create (a name refused) -> 200 | title=Guided setup: A search to check – Campertron | h2=Step 3 of 4: A search to check | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (current step)*@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/notifications; Leave guided setup and use the full web UI@/] | focus=new-search-message | search-state=Warning: to continue, at least one search must be Enabled (autoRun), have a campground and be saved without errors. Campertron needs at least one enabled search before it can monitor. Enable or create a search first. There is no search file: in the Searches tab, create one with New search, choose its campground, check Enabled (autoRun) and save it. Where these words name the Searches tab, in this web UI they mean its Searches page. | new-search-message=Error: The name can't contain / or \\ or ..: a search file is directly in the settings folder. Nothing was written.",
            "POST /guided-setup/email/search?handler=create create -> 302 | location=/guided-setup/email/search/Lake%20trip.yaml",
            "GET /guided-setup/email/search/Lake%20trip.yaml -> 200 | title=Guided setup: A search to check – Campertron | h2=Step 3 of 4: A search to check | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (current step)*@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/notifications; Leave guided setup and use the full web UI@/] | focus=created | created=Info: Created Lake trip.yaml with the example search's settings. It is not enabled (autoRun: false): the Results tab runs it when you select it, and the console's Search and email mode run it once you enable it and save.",
            "POST /guided-setup/email/search/Lake%20trip.yaml use-campground-id -> 200 | title=Guided setup: A search to check – Campertron | h2=Step 3 of 4: A search to check | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (current step)*@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/notifications; Leave guided setup and use the full web UI@/] | focus=campground-current",
            "POST /guided-setup/email/search/Lake%20trip.yaml continue (not enabled) -> 200 | title=Guided setup: A search to check – Campertron | h2=Step 3 of 4: A search to check | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (current step)*@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/notifications; Leave guided setup and use the full web UI@/] | focus=notice | notice=Warning: to continue, at least one search must be Enabled (autoRun), have a campground and be saved without errors. | status=Info: Saved Lake trip.yaml. The previous version is in Lake trip.yaml.bak.",
            "POST /guided-setup/email/search/Lake%20trip.yaml continue (people refused) -> 200 | title=Error: Guided setup: A search to check – Campertron | h2=Step 3 of 4: A search to check | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (current step)*@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/notifications; Leave guided setup and use the full web UI@/] | focus=error-summary | status=Error: Nothing was saved: fix the errors shown first. | error-summary=[People (totalHumans): Enter a number of people, such as 2.]",
            "GET /guided-setup/email/search/Lake%20trip.yaml -> 200 | title=Error: Guided setup: A search to check – Campertron | h2=Step 3 of 4: A search to check | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (current step)*@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/notifications; Leave guided setup and use the full web UI@/] | focus= | status=Error: Nothing was saved: fix the errors shown first. | error-summary=[People (totalHumans): Enter a number of people, such as 2.]",
            "POST /guided-setup/email/search/Lake%20trip.yaml continue (ready) -> 302 | location=/guided-setup/email/turn-on",
            "GET /guided-setup/email/turn-on -> 200 | title=Guided setup: Review and turn on – Campertron | h2=Step 4 of 4: Review and turn on | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (done)@/guided-setup/email/search; Review and turn on (current step)*@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/search; Leave guided setup and use the full web UI@/] | focus=step-heading | notice=Info: Saved Lake trip.yaml. The previous version is in Lake trip.yaml.bak. | checklist=[Email settings (Email.yaml): ready.; A search to check: ready.; outputTo in General.yaml: not set yet (no General.yaml the monitoring could use).] | turn-on=Turn the monitoring on (save outputTo: Email in General.yaml)",
            "GET /guided-setup/email/turn-on -> 200 | title=Guided setup: Review and turn on – Campertron | h2=Step 4 of 4: Review and turn on | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (done)@/guided-setup/email/search; Review and turn on (current step)*@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/search; Leave guided setup and use the full web UI@/] | focus= | checklist=[Email settings (Email.yaml): ready.; A search to check: ready.; outputTo in General.yaml: not set yet (no General.yaml the monitoring could use).] | turn-on=Turn the monitoring on (save outputTo: Email in General.yaml)",
            "GET /guided-setup/email/search -> 200 | title=Guided setup: A search to check – Campertron | h2=Step 3 of 4: A search to check | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (current step)*@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/notifications; Leave guided setup and use the full web UI@/] | focus= | search-state=Info: at least one enabled search is ready to be checked. | new-search-message=Info: Created Lake trip.yaml with the example search's settings. It is not enabled (autoRun: false): the Results tab runs it when you select it, and the console's Search and email mode run it once you enable it and save. | continue=Continue@/guided-setup/email/turn-on",
            "GET /guided-setup/email/turn-on -> 200 | title=Guided setup: Review and turn on – Campertron | h2=Step 4 of 4: Review and turn on | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (done)@/guided-setup/email/search; Review and turn on (current step)*@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/search; Leave guided setup and use the full web UI@/] | focus= | checklist=[Email settings (Email.yaml): ready.; A search to check: ready.; outputTo in General.yaml: Console now; the last step sets it to Email.] | turn-on=Turn the monitoring on (save outputTo: Email in General.yaml)",
            "POST /guided-setup/email/turn-on?handler=turnon turn on (General.yaml unsaved) -> 200 | title=Guided setup: Review and turn on – Campertron | h2=Step 4 of 4: Review and turn on | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (done)@/guided-setup/email/search; Review and turn on (current step)*@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/search; Leave guided setup and use the full web UI@/] | focus=notice | notice=Error: General.yaml has unsaved changes on its settings page, so nothing was saved here. Save or revert them there first. | checklist=[Email settings (Email.yaml): ready.; A search to check: ready.; outputTo in General.yaml: Console now; the last step sets it to Email.] | turn-on=Turn the monitoring on (save outputTo: Email in General.yaml)",
            "POST /guided-setup/email/turn-on?handler=turnon turn on (out of date) -> 200 | title=Guided setup: Review and turn on – Campertron | h2=Step 4 of 4: Review and turn on | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (done)@/guided-setup/email/search; Review and turn on (current step)*@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/search; Leave guided setup and use the full web UI@/] | focus=page-notice | page-notice=Warning: This page was out of date (the settings changed in another tab or on disk, or the page was sent again), so nothing you entered was applied. The page shows the current settings. | checklist=[Email settings (Email.yaml): ready.; A search to check: ready.; outputTo in General.yaml: Console now; the last step sets it to Email.] | turn-on=Turn the monitoring on (save outputTo: Email in General.yaml)",
            "GET /guided-setup/email/turn-on -> 200 | title=Guided setup: Review and turn on – Campertron | h2=Step 4 of 4: Review and turn on | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (done)@/guided-setup/email/search; Review and turn on (current step)*@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/search; Leave guided setup and use the full web UI@/] | focus= | checklist=[Email settings (Email.yaml): ready.; A search to check: ready.; outputTo in General.yaml: Console now; the last step sets it to Email.] | turn-on=Turn the monitoring on (save outputTo: Email in General.yaml)",
            "POST /guided-setup/email/turn-on?handler=turnon turn on -> 302 | location=/guided-setup/email/done",
            "GET /guided-setup/email/done -> 200 | title=Guided setup: Done – Campertron | h2=Campertron is set up to email you | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (done)@/guided-setup/email/search; Review and turn on (done)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/turn-on; Leave guided setup and use the full web UI@/] | focus=step-heading | notice=Info: General.yaml was saved. | monitoring-state=Info: Not running. | start-form",
            "GET /guided-setup/email/done -> 200 | title=Guided setup: Done – Campertron | h2=Campertron is set up to email you | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (done)@/guided-setup/email/search; Review and turn on (done)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/turn-on; Leave guided setup and use the full web UI@/] | focus= | monitoring-state=Info: Not running. | start-form",
            "GET /guided-setup/email/turn-on -> 200 | title=Guided setup: Review and turn on – Campertron | h2=Step 4 of 4: Review and turn on | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (done)@/guided-setup/email/search; Review and turn on (current step)*@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/search; Leave guided setup and use the full web UI@/] | focus= | checklist=[Email settings (Email.yaml): ready.; A search to check: ready.; outputTo in General.yaml: Email, so the monitoring is turned on.] | continue=Continue@/guided-setup/email/done",
            "POST /guided-setup/email/turn-on?handler=turnon turn on (already on) -> 302 | location=/guided-setup/email/done",
            "GET /guided-setup/email/done -> 200 | title=Guided setup: Done – Campertron | h2=Campertron is set up to email you | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (done)@/guided-setup/email/search; Review and turn on (done)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/turn-on; Leave guided setup and use the full web UI@/] | focus=step-heading | notice=Info: outputTo in General.yaml is already this method. | monitoring-state=Info: Not running. | start-form",
            "GET /guided-setup/email -> 302 | location=/guided-setup/email/done",
            "GET /guided-setup -> 200 | title=Guided setup: How you are notified – Campertron | h2=Step 1 of 4: How you are notified | steps=[How you are notified (current step)*@/guided-setup; Email or ntfy settings (to do); A search to check (to do); Review and turn on (to do)] | nav=[Skip guided setup and use the full web UI@/] | focus= | chosen=email",
            "GET / -> 200 | title=Start – Campertron | focus= | start-page=[Notifications are set up: the monitoring checks your enabled searches every minute and notifies you by email.; Open guided setup@/guided-setup]",
        };

        private static readonly string[] ExpectedNtfyAndRefusalsWalk =
        {
            "POST /guided-setup/email/turn-on?handler=turnon turn on (nothing ready) -> 200 | title=Guided setup: Review and turn on – Campertron | h2=Step 4 of 4: Review and turn on | steps=[How you are notified (done)@/guided-setup; Email settings (to do)@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (current step)*@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/search; Leave guided setup and use the full web UI@/] | focus=notice | notice=Error: nothing was saved: the monitoring would refuse the saved files. Fix the problems listed first. | turn-on-unavailable=nothing was saved: the monitoring would refuse the saved files. Fix the problems listed first. | problems=[Email.yaml: The file does not exist.; Campertron needs at least one enabled search before it can monitor. Enable or create a search first. There is no search file: in the Searches tab, create one with New search, choose its campground, check Enabled (autoRun) and save it.] | checklist=[Email settings (Email.yaml): not ready. Email settings; A search to check: not ready. A search to check; outputTo in General.yaml: not set yet (no General.yaml the monitoring could use).] | turn-on=Turn the monitoring on (save outputTo: Email in General.yaml) (disabled)",
            "GET /guided-setup/email/done -> 200 | title=Guided setup: Done – Campertron | h2=Guided setup is not finished | steps=[How you are notified (done)@/guided-setup; Email settings (to do)@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/turn-on; Leave guided setup and use the full web UI@/] | focus= | checklist=[Email settings (to do); A search to check (to do); Review and turn on (to do)]",
            "GET /guided-setup/ntfy/done -> 200 | title=Guided setup: Done – Campertron | h2=Guided setup is not finished | steps=[How you are notified (done)@/guided-setup; ntfy settings (to do)@/guided-setup/ntfy/notifications; A search to check (to do)@/guided-setup/ntfy/search; Review and turn on (to do)@/guided-setup/ntfy/turn-on] | nav=[Back@/guided-setup/ntfy/turn-on; Leave guided setup and use the full web UI@/] | focus= | checklist=[ntfy settings (to do); A search to check (to do); Review and turn on (to do)]",
            "GET /guided-setup/ntfy -> 302 | location=/guided-setup/ntfy/notifications",
            "GET /guided-setup/ntfy/notifications -> 200 | title=Guided setup: ntfy settings – Campertron | h2=Step 2 of 4: ntfy settings | steps=[How you are notified (done)@/guided-setup; ntfy settings (current step)*@/guided-setup/ntfy/notifications; A search to check (to do)@/guided-setup/ntfy/search; Review and turn on (to do)@/guided-setup/ntfy/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus= | needs=[Ntfy.yaml: The file does not exist, and outputTo: Ntfy needs it with enabled: true, the server and the topic.]",
            "POST /guided-setup/ntfy/notifications create -> 200 | title=Guided setup: ntfy settings – Campertron | h2=Step 2 of 4: ntfy settings | steps=[How you are notified (done)@/guided-setup; ntfy settings (current step)*@/guided-setup/ntfy/notifications; A search to check (to do)@/guided-setup/ntfy/search; Review and turn on (to do)@/guided-setup/ntfy/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus= | needs=[Ntfy.yaml: The file does not exist, and outputTo: Ntfy needs it with enabled: true, the server and the topic.]",
            "POST /guided-setup/ntfy/notifications continue (not turned on) -> 200 | title=Guided setup: ntfy settings – Campertron | h2=Step 2 of 4: ntfy settings | steps=[How you are notified (done)@/guided-setup; ntfy settings (current step)*@/guided-setup/ntfy/notifications; A search to check (to do)@/guided-setup/ntfy/search; Review and turn on (to do)@/guided-setup/ntfy/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus=needs | status=Info: Created Ntfy.yaml with your settings. | needs=[Ntfy.yaml: enabled is false, and outputTo: Ntfy needs ntfy: set enabled: true, or choose another outputTo.]",
            "POST /guided-setup/ntfy/notifications continue (turned on) -> 302 | location=/guided-setup/ntfy/search",
            "GET /guided-setup/ntfy/search -> 200 | title=Guided setup: A search to check – Campertron | h2=Step 3 of 4: A search to check | steps=[How you are notified (done)@/guided-setup; ntfy settings (done)@/guided-setup/ntfy/notifications; A search to check (current step)*@/guided-setup/ntfy/search; Review and turn on (to do)@/guided-setup/ntfy/turn-on] | nav=[Back@/guided-setup/ntfy/notifications; Leave guided setup and use the full web UI@/] | focus=step-heading | notice=Info: Saved Ntfy.yaml. The previous version is in Ntfy.yaml.bak. | search-state=Warning: to continue, at least one search must be Enabled (autoRun), have a campground and be saved without errors. Campertron needs at least one enabled search before it can monitor. Enable or create a search first. There is no search file: in the Searches tab, create one with New search, choose its campground, check Enabled (autoRun) and save it. Where these words name the Searches tab, in this web UI they mean its Searches page.",
            "GET /guided-setup/ntfy/turn-on -> 200 | title=Guided setup: Review and turn on – Campertron | h2=Step 4 of 4: Review and turn on | steps=[How you are notified (done)@/guided-setup; ntfy settings (done)@/guided-setup/ntfy/notifications; A search to check (to do)@/guided-setup/ntfy/search; Review and turn on (current step)*@/guided-setup/ntfy/turn-on] | nav=[Back@/guided-setup/ntfy/search; Leave guided setup and use the full web UI@/] | focus= | turn-on-unavailable=nothing was saved: the monitoring would refuse the saved files. Fix the problems listed first. | problems=[Campertron needs at least one enabled search before it can monitor. Enable or create a search first. There is no search file: in the Searches tab, create one with New search, choose its campground, check Enabled (autoRun) and save it.] | checklist=[ntfy settings (Ntfy.yaml): ready.; A search to check: not ready. A search to check; outputTo in General.yaml: not set yet (no General.yaml the monitoring could use).] | turn-on=Turn the monitoring on (save outputTo: Ntfy in General.yaml) (disabled)",
            "GET /guided-setup/email/notifications -> 200 | title=Error: Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus= | status=Info: Email.yaml was changed on disk and has been reloaded. | error-summary=[Recipients (sendToAddressList): sendToAddressList has no address: add at least one recipient.] | needs=[Email.yaml: sendToAddressList has no address: add at least one recipient.]",
            "POST /guided-setup/email/notifications continue (a save the form refuses) -> 200 | title=Error: Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus=error-summary | status=Error: Email.yaml has problems that must be fixed before it can be saved. Nothing was written. | error-summary=[Recipients (sendToAddressList): sendToAddressList has no address: add at least one recipient.] | needs=[Email.yaml: sendToAddressList has no address: add at least one recipient.]",
            "POST /guided-setup/email/notifications revert -> 200 | title=Error: Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus=status | status=Info: Your changes to Email.yaml were reverted. | error-summary=[Recipients (sendToAddressList): sendToAddressList has no address: add at least one recipient.] | needs=[Email.yaml: sendToAddressList has no address: add at least one recipient.]",
            "GET /guided-setup/email/notifications -> 200 | title=Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus= | status=Info: Email.yaml was changed on disk and has been reloaded.",
            "POST /guided-setup/email/notifications check (an edit) -> 200 | title=Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus=preview",
            "POST /guided-setup/email/notifications continue (a conflict) -> 200 | title=Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus=status | status=Error: Email.yaml was changed by another program; resolve that first. Nothing was written. | conflict=Changed by another program Warning: Email.yaml was changed by another program while you were editing it. Both changed: smtpServer. Your changes are kept here; nothing was written. Keep my values for smtpServer Discard my changes and use the file on disk",
            "POST /guided-setup/email/notifications use-theirs -> 200 | title=Guided setup: Email settings – Campertron | h2=Step 2 of 4: Email settings | steps=[How you are notified (done)@/guided-setup; Email settings (current step)*@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup; Leave guided setup and use the full web UI@/] | focus=status | status=Info: Your changes were discarded; Email.yaml shows the version on disk.",
            "POST /guided-setup/email/notifications continue (ready) -> 302 | location=/guided-setup/email/search",
            "GET /guided-setup/email/turn-on -> 200 | title=Guided setup: Review and turn on – Campertron | h2=Step 4 of 4: Review and turn on | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (current step)*@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/search; Leave guided setup and use the full web UI@/] | focus=step-heading | notice=Info: Your changes were discarded; Email.yaml shows the version on disk. | turn-on-unavailable=nothing was saved: the monitoring would refuse the saved files. Fix the problems listed first. | problems=[Campertron needs at least one enabled search before it can monitor. Enable or create a search first. There is no search file: in the Searches tab, create one with New search, choose its campground, check Enabled (autoRun) and save it.] | checklist=[Email settings (Email.yaml): ready.; A search to check: not ready. A search to check; outputTo in General.yaml: Email, so the monitoring is turned on.] | turn-on=Turn the monitoring on (save outputTo: Email in General.yaml) (disabled)",
            "GET /guided-setup/email/done -> 200 | title=Guided setup: Done – Campertron | h2=Guided setup is not finished | steps=[How you are notified (done)@/guided-setup; Email settings (done)@/guided-setup/email/notifications; A search to check (to do)@/guided-setup/email/search; Review and turn on (to do)@/guided-setup/email/turn-on] | nav=[Back@/guided-setup/email/turn-on; Leave guided setup and use the full web UI@/] | focus= | checklist=[A search to check (to do); Review and turn on (to do)]",
            "GET /guided-setup/Email -> 404",
            "GET /guided-setup/webhook -> 404",
            "GET /guided-setup/email/method -> 404",
            "GET /guided-setup/email/notifications/Email.yaml -> 404",
            "GET /guided-setup/email/search/Missing.yaml -> 404",
            "POST /guided-setup continue -> 404",
            "POST /guided-setup/email/done continue -> 404",
            "POST /guided-setup/email/turn-on continue -> 404",
        };

        private static readonly string[] ExpectedStartPageWalk =
        {
            "nothing saved: GET / -> 302 | location=/guided-setup",
            "nothing saved: GET / again -> 200 | title=Start – Campertron | focus= | start-page=[Campertron is not set up yet: it does not check any search or notify you.; Start guided setup@/guided-setup]",
            "Ntfy.yaml ready: GET / -> 200 | title=Start – Campertron | focus= | start-page=[Notifications are not set up yet: Campertron does not notify you of new openings.; Continue guided setup@/guided-setup/ntfy]",
            "both notification files ready: GET / -> 200 | title=Start – Campertron | focus= | start-page=[Notifications are not set up yet: Campertron does not notify you of new openings.; Continue guided setup@/guided-setup]",
            "both ready, outputTo Console: GET / -> 200 | title=Start – Campertron | focus= | start-page=[Notifications are not set up yet: Campertron does not notify you of new openings.; Continue guided setup@/guided-setup]",
            "Email.yaml ready: GET / -> 200 | title=Start – Campertron | focus= | start-page=[Notifications are not set up yet: Campertron does not notify you of new openings.; Continue guided setup@/guided-setup/email]",
            "Email.yaml and a search ready: GET / -> 200 | title=Start – Campertron | focus= | start-page=[Your notification settings and a search are ready: the last step turns the monitoring on.; Continue guided setup@/guided-setup/email]",
            "email complete: GET / -> 200 | title=Start – Campertron | focus= | start-page=[Notifications are set up: the monitoring checks your enabled searches every minute and notifies you by email.; Open guided setup@/guided-setup]",
            "ntfy complete: GET / -> 200 | title=Start – Campertron | focus= | start-page=[Notifications are set up: the monitoring checks your enabled searches every minute and notifies you with ntfy.; Open guided setup@/guided-setup]",
            "outputTo Ntfy without a search: GET / -> 200 | title=Start – Campertron | focus= | start-page=[Notifications are not set up yet: Campertron does not notify you of new openings.; Continue guided setup@/guided-setup/ntfy]",
        };
    }
}
