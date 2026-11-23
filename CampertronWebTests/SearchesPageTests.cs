using System.Net;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronWeb.Pages;

namespace CampertronWebTests
{
    // The web UI's searches pages (v0.3.0's PR 6, D96; business-rules-settings §33): the search files of the settings
    // folder, New search, Look for changes on disk, each file's editor and Save As, through the shared form layer (the
    // desktop GUI's Searches tab's form, D91), on the real host. Every change is a POST with the antiforgery token from this
    // web UI; only the console's own search files are listed and a file is found by its exact listed name, never by a path;
    // names are the library's rules; a value the form refuses is shown and never saved; an out-of-date page applies nothing
    // and a change on disk under the edits is a conflict; the picker chooses by exact ID and the selectors change only what
    // the user changed; a session's form is its own and ends with it; nothing starts, stops or locks a monitoring or writes
    // Camp.History; New search and Save As are rate-limited. The campground data is in memory: no database is opened.
    public class SearchesPageTests
    {
        internal const string Campground = "232447";
        internal static readonly CampgroundChoice UpperPines = new CampgroundChoice(Campground, "UPPER PINES", "YOSEMITE VALLEY", "CA");
        internal static readonly CampgroundChoice LowerPines = new CampgroundChoice("232450", "LOWER PINES", "YOSEMITE VALLEY", "CA");

        internal static string Search(string Name, bool AutoRun = true, string Extra = "") =>
            "# my search\ndisplayName: " + Name + "   # its name\nautoRun: " + (AutoRun ? "true" : "false") + "\ncampgroundID: '" + Campground + "'\n" +
            "totalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\nshowMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\n" +
            "showFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\nfilterMatching: Exact\n" + Extra;

        // the campgrounds of the picker and the values of a campground, in memory
        internal static List<CampgroundPickerEntry> Campgrounds()
        {
            var All = new List<CampgroundPickerEntry> { new CampgroundPickerEntry(UpperPines, "Yosemite National Park"), new CampgroundPickerEntry(LowerPines, "Yosemite National Park") };
            for (int i = 0; i < 80; i++)
            {
                All.Add(new CampgroundPickerEntry(new CampgroundChoice((300000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), "MEADOW " + i, "SOMEWHERE", "OR"), null));
            }
            return All;
        }

        internal static CampgroundFilterChoices Choices(string Id) => new CampgroundFilterChoices(Id, 30,
            new[] { new FilterChoice("Tent", 3), new FilterChoice("tent", 1), new FilterChoice("RV", 1) },
            new[] { new FilterChoice("STANDARD NONELECTRIC", 30) },
            Enumerable.Range(1, 30).Select(i => new FilterChoice(i.ToString("000", System.Globalization.CultureInfo.InvariantCulture), 1)),
            new[] { new FilterChoice("Shade:Yes", 2, "Shade", "Yes"), new FilterChoice("Shade:No", 1, "Shade", "No") });

        // a web UI with the campground data in memory (Configure: more settings of the test's own)
        internal static Task<WebTestHost> Start(bool WithPassword = true, string? WebYaml = null, Action<WebHostSettings>? Configure = null) =>
            WebTestHost.Start(WebYaml, WithPassword, Configure: Settings =>
            {
                Settings.Campgrounds = Campgrounds;
                Settings.FilterChoices = Choices;
                Settings.PrepareCampgroundData = () => throw new AssertionException("the campground data is in memory");
                Configure?.Invoke(Settings);
            });

        private static string FileOf(WebTestHost Host, string Name) => Path.Join(Host.Folder, Name);

        // ---- the pages and who may use them ----

        [Test]
        public async Task TheSearchesPages_NeedTheOwner_ListOnlyTheConsolesSearchFiles_AndOpenNoOtherFile()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Bravo.yaml"), Search("Second trip", AutoRun: false));
            File.WriteAllText(FileOf(Host, "alpha.yaml"), Search("First trip"));
            File.WriteAllText(FileOf(Host, "General.yaml"), "outputTo: Console\n");
            File.WriteAllText(FileOf(Host, "Email.yaml"), "smtpServer: smtp.example.invalid\n");
            File.WriteAllText(FileOf(Host, "Other.yml"), Search("Not read"));
            File.WriteAllText(FileOf(Host, "alpha.yaml.bak"), Search("A backup"));
            Directory.CreateDirectory(FileOf(Host, "Folder.yaml"));

            using Browser Visitor = Host.Browser();
            WebPage Refused = await Visitor.Get("/searches/alpha.yaml");
            Assert.That(Refused.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Refused.Location, Is.EqualTo("/signin?returnUrl=%2Fsearches%2Falpha.yaml"));
            Assert.That((await Visitor.Post("/searches?handler=create", ("new.name", "Visitor"), ("action", "create"))).Status, Is.Not.EqualTo(HttpStatusCode.OK));
            Assert.That(File.Exists(FileOf(Host, "Visitor.yaml")), Is.False, "no change without the owner");

            using Browser Owner = await Host.SignedIn();
            WebPage List = await Owner.Get("/searches");
            Assert.That(List.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(List.Header("Cache-Control"), Does.Contain("no-store"));
            Assert.That(List.Header("Content-Security-Policy"), Does.Contain("script-src 'self'").And.Contain("form-action 'self'"));
            List<string> Listed = List.Xml().Descendants("ul").Single(U => (string?)U.Attribute("class") == "search-files").Elements("li").Select(Li => Li.Element("a")!.Value).ToList();
            Assert.That(Listed, Is.EqualTo(new[] { "alpha.yaml", "Bravo.yaml" }), "the console's search files, by name ignoring case, as the desktop GUI lists them");
            Assert.That(List.Text(), Does.Contain("First trip · " + AutoRunWording.Enabled + " · " + UpperPines.Label).And.Contain("Second trip · " + AutoRunWording.NotEnabled));
            Assert.That(List.Text(), Does.Contain(AutoRunWording.EnabledNote).And.Contain(AutoRunWording.NotEnabledNote));
            Assert.That(List.Body, Does.Not.Contain(Host.Folder), "no page shows the settings folder");

            WebPage Alpha = await Owner.Get("/searches/alpha.yaml");
            Assert.That(Alpha.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(SettingsForm.Element(Alpha, "f-displayName")!.Attribute("value")?.Value, Is.EqualTo("First trip"));
            Assert.That(Alpha.Body, Does.Not.Contain(Host.Folder));
            foreach (string Other in new[] { "/searches/Alpha.yaml", "/searches/General.yaml", "/searches/Email.yaml", "/searches/Other.yml", "/searches/alpha.yaml.bak",
                "/searches/Folder.yaml", "/searches/..%2FGeneral.yaml", "/searches/%2Fetc%2Fpasswd", "/searches/alpha.yaml/extra", "/searches/Missing.yaml" })
            {
                Assert.That((await Owner.Get(Other)).Status, Is.EqualTo(HttpStatusCode.NotFound), Other);
            }
            Assert.That((await Owner.Get("/")).Body, Does.Contain("href=\"/searches\""));

            await using WebTestHost Open = await Start(WithPassword: false, WebYaml: "passwordProtection: false\n");
            using Browser Anyone = Open.Browser();
            Assert.That((await Anyone.Get("/searches")).Status, Is.EqualTo(HttpStatusCode.OK), "without password protection everyone is the owner (D81)");
        }

        [Test]
        public async Task EveryChange_IsAPostWithTheFormsTokenFromThisWebUI_AndAGetChangesNothing()
        {
            await using WebTestHost Host = await Start();
            string Text = Search("First trip");
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Text);
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/searches/Alpha.yaml");
            List<(string, string)> Fields = SearchForm.Fields(Page, "save", ("f.displayName", "Renamed trip"));

            Assert.That((await Owner.Get("/searches/Alpha.yaml?action=save&f.displayName=Renamed")).Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await Owner.Post("/searches/Alpha.yaml", Fields.Append(("__RequestVerificationToken", "forged")).ToArray())).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await Owner.Post("/searches?handler=create", ("__RequestVerificationToken", "forged"), ("new.name", "Forged"))).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Owner.Origin = "http://evil.example";
            Assert.That((await Owner.Post("/searches/Alpha.yaml", Fields.ToArray())).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Owner.Origin = Host.Root.GetLeftPart(UriPartial.Authority);
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Text), "nothing changed");
            Assert.That(File.Exists(FileOf(Host, "Forged.yaml")), Is.False);

            //a button the pages do not have does nothing (there is no deletion, rename or run), whatever handler it names
            foreach ((string Path_, string Action) in new[] { ("/searches/Alpha.yaml", "delete"), ("/searches/Alpha.yaml?handler=delete", "save"), ("/searches", "delete"),
                ("/searches/Alpha.yaml", "rename"), ("/searches/Alpha.yaml", "row-remove.displayName.0"), ("/searches/Alpha.yaml", "sel-all.campgroundID"), ("/searches?handler=run", "create") })
            {
                WebPage Current = await Owner.Get(Path_.StartsWith("/searches/", StringComparison.Ordinal) ? "/searches/Alpha.yaml" : "/searches");
                WebPage Odd = await Owner.Post(Path_, SearchForm.Fields(Current, Action).ToArray());
                Assert.That(Odd.Status, Is.EqualTo(HttpStatusCode.OK), Path_ + " " + Action);
                Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Text), Path_ + " " + Action);
            }
            Assert.That(Directory.GetFiles(Host.Folder, "*.yaml").Select(Path.GetFileName), Is.EqualTo(new[] { "Alpha.yaml" }));
            Assert.That((await Owner.Post("/searches/Alpha.yaml", Fields.ToArray())).Text(), Does.Contain("This page was out of date"), "each of them made the first page out of date");
            Fields = SearchForm.Fields(await Owner.Get("/searches/Alpha.yaml"), "save", ("f.displayName", "Renamed trip"));

            WebPage Saved = await Owner.Post("/searches/Alpha.yaml", Fields.ToArray());
            Assert.That(Saved.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Text.Replace("displayName: First trip   #", "displayName: Renamed trip   #")), "one value changed, the comments kept");
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml.bak")), Is.EqualTo(Text), "the desktop GUI's writer keeps the previous version");
            Assert.That(Saved.Text(), Does.Contain("Saved Alpha.yaml. The previous version is in Alpha.yaml.bak."));
            Assert.That(SettingsForm.Element(Saved, "status")!.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));
            Assert.That(Directory.GetFiles(Host.Folder, "*.tmp"), Is.Empty, "no temporary file is left");
        }

        // ---- editing ----

        [Test]
        public async Task AValueTheFormRefuses_IsShownAtItsFieldAndAtTheTop_KeepsWhatWasTyped_AndIsNeverSaved()
        {
            await using WebTestHost Host = await Start();
            string Text = Search("First trip");
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Text);
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/searches/Alpha.yaml");
            WebPage Refused = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Page, "save", ("f.totalHumans", "two"), ("f.displayName", "Typed name")).ToArray());

            XElement People = SettingsForm.Element(Refused, "f-totalHumans")!;
            Assert.That(People.Attribute("aria-invalid")?.Value, Is.EqualTo("true"));
            Assert.That(People.Attribute("value")?.Value, Is.EqualTo("two"), "what was typed stays");
            Assert.That(SettingsForm.Element(Refused, "f-displayName")!.Attribute("value")?.Value, Is.EqualTo("Typed name"));
            Assert.That(SettingsForm.Element(Refused, "f-totalHumans-msg")!.Value, Is.EqualTo("Error: Enter a number of people, such as 2."), "the form's own words");
            XElement Summary = SettingsForm.Element(Refused, "error-summary")!;
            Assert.That(Summary.Attribute("role")?.Value, Is.EqualTo("alert"));
            Assert.That(Summary.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));
            Assert.That(Summary.Descendants("a").Any(A => A.Attribute("href")?.Value == "#f-totalHumans"), Is.True);
            Assert.That(Refused.Xml().Descendants("title").Single().Value, Does.StartWith("Error: "));
            Assert.That(Refused.Text(), Does.Contain("Nothing was saved: fix the errors shown first."));
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Text));

            //an error of the search's own validation (equal dates of an enabled StartEndDate search) blocks Save too
            WebPage Fixed = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Refused, "revert").ToArray());
            List<(string, string)> Dates = SearchForm.Fields(Fixed, "row-add.searchValueDates", ("f.searchBy", "4"));
            WebPage OneRow = await Owner.Post("/searches/Alpha.yaml", Dates.ToArray());
            WebPage TwoRows = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(OneRow, "row-add.searchValueDates", ("f.searchValueDates.0", "2027-07-04")).ToArray());
            WebPage Equal = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(TwoRows, "save", ("f.searchValueDates.1", "2027-07-04")).ToArray());
            Assert.That(SettingsForm.Element(Equal, "error-summary"), Is.Not.Null, Equal.Text());
            Assert.That(Equal.Text(), Does.Contain("Alpha.yaml has problems that must be fixed before it can be saved. Nothing was written."));
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Text), "a search the monitoring's start would refuse is never saved");

            //Revert: the file's values again
            WebPage Reverted = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Equal, "revert").ToArray());
            Assert.That(Reverted.Text(), Does.Contain("Your changes to Alpha.yaml were reverted."));
            Assert.That(SettingsForm.Element(Reverted, "f-totalHumans")!.Attribute("value")?.Value, Is.EqualTo("2"));
        }

        [Test]
        public async Task EnterChecks_AndShowsWhatSaveWouldWrite_WithoutSavingCreatingOrCopying()
        {
            await using WebTestHost Host = await Start();
            string Text = Search("First trip");
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Text);
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/searches/Alpha.yaml");
            XElement Default = SearchForm.Form(Page).Descendants("button").First();
            Assert.That(((string?)Default.Attribute("value"), (string?)Default.Attribute("tabindex")), Is.EqualTo(("check", "-1")), "Enter presses Check changes");

            //Enter in Save As's box: the entries are checked and the name kept, nothing is written
            WebPage Checked = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Page, "check", ("f.displayName", "Changed name"), ("saveas.name", "Copy")).ToArray());
            Assert.That(SettingsForm.Element(Checked, "preview")!.Value, Does.Contain("+").And.Contain("Changed name"));
            Assert.That(SettingsForm.Element(Checked, "preview")!.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));
            Assert.That(SettingsForm.Element(Checked, "saveas-name")!.Attribute("value")?.Value, Is.EqualTo("Copy"));
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Text));
            Assert.That(File.Exists(FileOf(Host, "Copy.yaml")), Is.False);

            //Enter in New search's box: the name is kept, nothing is created
            WebPage List = await Owner.Get("/searches");
            WebPage Kept = await Owner.Post("/searches", SearchForm.Fields(List, "check", ("new.name", "Weekend")).ToArray());
            Assert.That(SettingsForm.Element(Kept, "new-search-name")!.Attribute("value")?.Value, Is.EqualTo("Weekend"));
            Assert.That(File.Exists(FileOf(Host, "Weekend.yaml")), Is.False);
            Assert.That(SearchForm.Form(Kept).Descendants("button").First().Attribute("value")?.Value, Is.EqualTo("check"));
        }

        [Test]
        public async Task NewSearch_CreatesTheExampleSearchNotEnabled_UnderTheLibrarysNameRules_AndNeverWritesOverAFile()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "General.yaml"), "outputTo: Console\n");
            File.WriteAllText(FileOf(Host, "Existing.yaml"), Search("Existing"));
            string General = File.ReadAllText(FileOf(Host, "General.yaml"));
            string Existing = File.ReadAllText(FileOf(Host, "Existing.yaml"));
            using Browser Owner = await Host.SignedIn();

            var Refusals = new (string Name, string Words)[]
            {
                ("../Escape", "The name can't contain / or \\ or ..: a search file is directly in the settings folder."),
                ("/etc/passwd", "The name can't contain / or \\ or ..: a search file is directly in the settings folder."),
                ("C:\\Windows\\x", "The name can't contain / or \\ or ..: a search file is directly in the settings folder."),
                ("General", "General.yaml, Email.yaml, Ntfy.yaml and Web.yaml are Campertron's settings files, not search files; choose another name."),
                ("web.YAML", "General.yaml, Email.yaml, Ntfy.yaml and Web.yaml are Campertron's settings files, not search files; choose another name."),
                ("CON", "That name is reserved by Windows; choose another."),
                ("Trip.yml", "Search files end in .yaml (Campertron does not read .yml files)."),
                ("existing", "Existing.yaml already exists, and existing.yaml differs from it only in upper and lower case; choose another name."),
                ("Existing.yaml", "Existing.yaml already exists; choose another name."),
                ("Name:Stream", "The name can contain only letters, digits, spaces and - _ . ( )."),
                (".hidden", "The name can't start or end with a dot."),
                ("   ", "Enter a name for the search file."),
            };
            foreach ((string Name, string Words) in Refusals)
            {
                WebPage List = await Owner.Get("/searches");
                WebPage Refused = await Owner.Post("/searches?handler=create", SearchForm.Fields(List, "create", ("new.name", Name)).ToArray());
                Assert.That(Refused.Status, Is.EqualTo(HttpStatusCode.OK), Name);
                XElement Message = SettingsForm.Element(Refused, "new-search-message")!;
                Assert.That(Message.Value, Is.EqualTo("Error: " + Words + " Nothing was written."), Name);
                Assert.That(Message.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"), Name);
                Assert.That(SettingsForm.Element(Refused, "new-search-name")!.Attribute("value")?.Value, Is.EqualTo(Name), "what was typed stays");
            }
            Assert.That(Directory.GetFileSystemEntries(Host.Folder).Select(Path.GetFileName).Where(N => N != "Web.keys" && N != "Web.password").OrderBy(N => N, StringComparer.Ordinal),
                Is.EqualTo(new[] { "Existing.yaml", "General.yaml" }), "nothing was created in the settings folder");
            Assert.That(File.Exists(Path.Join(Path.GetDirectoryName(Host.Folder)!, "Escape.yaml")), Is.False, "nor beside it");
            Assert.That(File.ReadAllText(FileOf(Host, "General.yaml")), Is.EqualTo(General));
            Assert.That(File.ReadAllText(FileOf(Host, "Existing.yaml")), Is.EqualTo(Existing));

            WebPage Page = await Owner.Get("/searches");
            WebPage Created = await Owner.Post("/searches?handler=create", SearchForm.Fields(Page, "create", ("new.name", "Weekend trips (2)")).ToArray());
            Assert.That(Created.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Created.Location, Is.EqualTo("/searches/Weekend%20trips%20%282%29.yaml"));
            Assert.That(File.ReadAllText(FileOf(Host, "Weekend trips (2).yaml")), Does.Contain("autoRun: false"), "the example search, not enabled");
            WebPage Opened = await Owner.Get(Created.Location!);
            XElement Said = SettingsForm.Element(Opened, "created")!;
            Assert.That(Said.Value, Does.StartWith("Info: Created Weekend trips (2).yaml with the example search's settings. It is not enabled (autoRun: false)"));
            Assert.That(Said.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));
            Assert.That(SettingsForm.Element(await Owner.Get(Created.Location!), "created"), Is.Null, "said once");

            //the same page sent again (the back button, a second click) creates nothing more
            WebPage Again = await Owner.Post("/searches?handler=create", SearchForm.Fields(Page, "create", ("new.name", "Another")).ToArray());
            Assert.That(Again.Text(), Does.Contain("This page was out of date"));
            Assert.That(File.Exists(FileOf(Host, "Another.yaml")), Is.False);
        }

        [Test]
        public async Task SaveAs_WritesTheSearchWithItsChangesToANewFile_AndTheSourceKeepsWhatIsOnDisk()
        {
            await using WebTestHost Host = await Start();
            string Text = Search("First trip");
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Text);
            File.WriteAllText(FileOf(Host, "Email.yaml"), "smtpServer: smtp.example.invalid\n");
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/searches/Alpha.yaml");

            foreach (string Taken in new[] { "Email", "alpha", "../Copy" })
            {
                WebPage Refused = await Owner.Post("/searches/Alpha.yaml?handler=saveas", SearchForm.Fields(Page, "saveas", ("f.displayName", "Copied trip"), ("saveas.name", Taken)).ToArray());
                Assert.That(SettingsForm.Element(Refused, "saveas-message")!.Value, Does.StartWith("Error: ").And.EndWith("Nothing was written."), Taken);
                Assert.That(SettingsForm.Element(Refused, "saveas-message")!.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"), Taken);
                Page = Refused;
            }
            Assert.That(File.ReadAllText(FileOf(Host, "Email.yaml")), Is.EqualTo("smtpServer: smtp.example.invalid\n"), "never over a settings file");

            WebPage Saved = await Owner.Post("/searches/Alpha.yaml?handler=saveas", SearchForm.Fields(Page, "saveas", ("saveas.name", "Copy")).ToArray());
            Assert.That(Saved.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Saved.Location, Is.EqualTo("/searches/Copy.yaml"));
            Assert.That(File.ReadAllText(FileOf(Host, "Copy.yaml")), Is.EqualTo(Text.Replace("displayName: First trip   #", "displayName: Copied trip   #")), "the edits, with the comments");
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Text), "the source is not changed");
            WebPage Copy = await Owner.Get(Saved.Location!);
            Assert.That(SettingsForm.Element(Copy, "created")!.Value, Is.EqualTo("Info: Saved as Copy.yaml. Alpha.yaml was not changed."));
            WebPage Source = await Owner.Get("/searches/Alpha.yaml");
            Assert.That(SettingsForm.Element(Source, "f-displayName")!.Attribute("value")?.Value, Is.EqualTo("First trip"), "the source shows what is on disk again");
        }

        [Test]
        public async Task AnOutOfDatePage_AppliesNothing_AndAChangeOnDiskUnderTheEditsIsAConflict()
        {
            await using WebTestHost Host = await Start();
            string Text = Search("First trip");
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Text);
            using Browser Owner = await Host.SignedIn();
            WebPage Old = await Owner.Get("/searches/Alpha.yaml");
            WebPage Newer = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Old, "check", ("f.displayName", "Newer edit")).ToArray());
            Assert.That(SettingsForm.Element(Newer, "f-displayName")!.Attribute("value")?.Value, Is.EqualTo("Newer edit"));

            //the older page (another tab, the back button) saves nothing over the newer edit
            WebPage Stale = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Old, "save", ("f.displayName", "Older edit")).ToArray());
            Assert.That(Stale.Text(), Does.Contain("This page was out of date"));
            Assert.That(SettingsForm.Element(Stale, "page-notice")!.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));
            Assert.That(SettingsForm.Element(Stale, "f-displayName")!.Attribute("value")?.Value, Is.EqualTo("Newer edit"));
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Text));

            //a save sent twice (a double click) saves once
            List<(string, string)> Save = SearchForm.Fields(Stale, "save");
            WebPage First = await Owner.Post("/searches/Alpha.yaml", Save.ToArray());
            Assert.That(First.Text(), Does.Contain("Saved Alpha.yaml."));
            string Once = File.ReadAllText(FileOf(Host, "Alpha.yaml"));
            WebPage Second = await Owner.Post("/searches/Alpha.yaml", Save.ToArray());
            Assert.That(Second.Text(), Does.Contain("This page was out of date"));
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Once));

            //a change on disk under unsaved edits is the form's conflict, never written over
            WebPage Edited = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Second, "check", ("f.totalHumans", "4")).ToArray());
            string Theirs = Once.Replace("searchValue: 7", "searchValue: 9");
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Theirs);
            WebPage Conflict = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Edited, "save").ToArray());
            Assert.That(Conflict.Text(), Does.Contain("Changed by another program"));
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Theirs));
            WebPage Resolved = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Conflict, "apply-mine").ToArray());
            Assert.That(SettingsForm.Element(Resolved, "f-totalHumans")!.Attribute("value")?.Value, Is.EqualTo("4"));
            Assert.That(SettingsForm.Element(Resolved, "f-searchValue")!.Attribute("value")?.Value, Is.EqualTo("9"), "their change and mine");
        }

        [Test]
        public async Task TheCampgroundPicker_ListsTheFirstMatches_AndChoosesOnlyByAnExactIdOfTheData()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Search("First trip"));
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/searches/Alpha.yaml");
            Assert.That(SettingsForm.Element(Page, WebSettingsForm.CampgroundCurrentId)!.Value, Is.EqualTo(UpperPines.Label + " \u2014 park Yosemite National Park"));
            Assert.That(Page.Xml().Descendants("input").Count(I => (string?)I.Attribute("name") == WebSettingsForm.CampgroundMatch), Is.EqualTo(WebSettingsForm.ListedCampgrounds));
            Assert.That(SettingsForm.Element(Page, "campground-summary")!.Value, Does.Contain("The first 50 are listed."));

            WebPage Found = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Page, "find-campground", (WebSettingsForm.CampgroundFind, "lower")).ToArray());
            List<XElement> Matches = Found.Xml().Descendants("input").Where(I => (string?)I.Attribute("name") == WebSettingsForm.CampgroundMatch).ToList();
            Assert.That(Matches.Select(M => (string?)M.Attribute("value")), Is.EqualTo(new[] { "232450" }));
            Assert.That(SettingsForm.Element(Found, "campground-matches")!.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));
            WebPage Chosen = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Found, "use-campground", (WebSettingsForm.CampgroundMatch, "232450")).ToArray());
            Assert.That(SettingsForm.Element(Chosen, WebSettingsForm.CampgroundCurrentId)!.Value, Does.StartWith(LowerPines.Label));
            Assert.That(SettingsForm.Element(Chosen, WebSettingsForm.CampgroundCurrentId)!.Attribute("autofocus")?.Value, Is.EqualTo("autofocus"));

            //a typed ID: exactly one of the data's, else refused without repeating what was typed
            foreach (string Typed in new[] { " 232447", "23244", "UPPER PINES", "<b>9</b>" })
            {
                WebPage Refused = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Chosen, "use-campground-id", (WebSettingsForm.CampgroundTyped, Typed)).ToArray());
                XElement Message = SettingsForm.Element(Refused, WebSettingsForm.CampgroundTypedId + "-msg")!;
                Assert.That(Message.Value, Does.StartWith("Error: ").And.EndWith("No reservable, enabled campground in Campertron's campground data has exactly this ID."), Typed);
                Assert.That(Message.Value, Does.Not.Contain(Typed.Trim()), Typed);
                Assert.That(Refused.Body, Does.Not.Contain("<b>9</b>"), "what was typed is encoded");
                Assert.That(SettingsForm.Element(Refused, "error-summary")!.Descendants("a").Any(A => A.Attribute("href")?.Value == "#" + WebSettingsForm.CampgroundTypedId), Is.True);
                Chosen = Refused;
            }
            WebPage Used = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Chosen, "use-campground-id", (WebSettingsForm.CampgroundTyped, "300007")).ToArray());
            Assert.That(SettingsForm.Element(Used, WebSettingsForm.CampgroundCurrentId)!.Value, Does.StartWith("MEADOW 7"));
            WebPage Saved = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Used, "save").ToArray());
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Does.Contain("campgroundID: '300007'"));
            Assert.That(Saved.Text(), Does.Contain("Saved Alpha.yaml."));
        }

        [Test]
        public async Task TheFilterSelectors_ChangeOnlyWhatTheUserChanged_AndKeepEveryOtherValueOfTheList()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Search("First trip", Extra: "includeEquipment:\n- tent   # the small one\n- Hammock\nincludeSites:\n- '001'\n"));
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/searches/Alpha.yaml");
            Assert.That(SettingsForm.Element(Page, "f-includeEquipment-status")!.Value, Is.EqualTo("3 equipment types at this campground, 1 checked."));
            //"Tent" and "tent" are one group, partly checked, shown unchecked: leaving it changes nothing
            XElement Group = SettingsForm.Element(Page, "f-includeEquipment-o-" + SearchForm.OptionIndex(Page, "includeEquipment", "Tent (2 spellings that differ only by case)"))!;
            Assert.That(Group.Attribute("checked"), Is.Null);
            Assert.That(Group.Parent!.Value, Does.Contain("partly checked"));
            Assert.That(Page.Text(), Does.Contain("Other values in includeEquipment").And.Contain("Hammock"));
            WebPage Same = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Page, "check").ToArray());
            Assert.That(SettingsForm.Element(Same, "preview"), Is.Null, "a page sent as it was shown changes nothing");

            //checking RV adds it at the end; the list's comment and its other value stay
            List<(string, string)> Fields = SearchForm.Fields(Same, "save");
            int Rv = SearchForm.OptionIndex(Same, "includeEquipment", "RV");
            Fields.Add(("f.includeEquipment.o", Rv.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            WebPage Saved = await Owner.Post("/searches/Alpha.yaml", Fields.ToArray());
            Assert.That(Saved.Text(), Does.Contain("Saved Alpha.yaml."));
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Does.Contain("includeEquipment:\n- tent   # the small one\n- Hammock\n- RV\n"));

            //a page that showed other values (the token) applies none of its check boxes, also one the form has unchecked
            Assert.That(SettingsForm.Element(Saved, "f-includeEquipment-status")!.Value, Does.EndWith("2 checked."));
            List<(string, string)> Wrong = SearchForm.Fields(Saved, "check", ("opts.includeEquipment", "0000"));
            Wrong.Add(("f.includeEquipment.o", SearchForm.OptionIndex(Saved, "includeEquipment", "Tent").ToString(System.Globalization.CultureInfo.InvariantCulture)));
            WebPage Ignored = await Owner.Post("/searches/Alpha.yaml", Wrong.ToArray());
            Assert.That(SettingsForm.Element(Ignored, "f-includeEquipment-status")!.Value, Does.EndWith("2 checked."));
            Assert.That(SettingsForm.Element(Ignored, "preview"), Is.Null, "nothing changed");

            //the long list: find, Select all shown, Clear shown; a typed value; Remove
            WebPage FoundSites = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Ignored, "find.includeSites", ("f.includeSites.find", "02")).ToArray());
            Assert.That(SettingsForm.Element(FoundSites, "f-includeSites-status")!.Value, Is.EqualTo("30 campsite names at this campground, 1 checked. 11 shown."));
            WebPage All = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(FoundSites, "sel-all.includeSites").ToArray());
            Assert.That(SettingsForm.Element(All, "f-includeSites-status")!.Value, Is.EqualTo("30 campsite names at this campground, 12 checked. 11 shown."));
            WebPage Cleared = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(All, "sel-clear.includeSites").ToArray());
            Assert.That(SettingsForm.Element(Cleared, "f-includeSites-status")!.Value, Is.EqualTo("30 campsite names at this campground, 1 checked. 11 shown."));
            WebPage Typed = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Cleared, "custom-add.excludeSites", ("f.excludeSites.custom", "<i>A 1</i>")).ToArray());
            Assert.That(SettingsForm.Element(Typed, "f-excludeSites-custom-msg")!.Value, Is.EqualTo("Info: Added as typed."));
            Assert.That(Typed.Body, Does.Not.Contain("<i>A 1</i>"), "a typed value is encoded");
            Assert.That(Typed.Text(), Does.Contain("<i>A 1</i>"));
            WebPage Twice = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Typed, "custom-add.excludeSites", ("f.excludeSites.custom", "<i>A 1</i>")).ToArray());
            Assert.That(SettingsForm.Element(Twice, "f-excludeSites-custom-msg")!.Value, Is.EqualTo("Error: The list already holds exactly this value."));
            WebPage Removed = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Twice, "other-remove.excludeSites.0", ("f.excludeSites.custom", string.Empty)).ToArray());
            Assert.That(Removed.Text(), Does.Not.Contain("Other values in excludeSites"));
            WebPage Done = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Removed, "save").ToArray());
            Assert.That(Done.Text(), Does.Contain("Nothing to save: Alpha.yaml has no changes."), "Select all shown and Clear shown changed only the shown values; the value added was removed");
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Does.Contain("includeSites:\n- '001'\n").And.Not.Contain("excludeSites"));
        }

        // ---- sessions ----

        [Test]
        public async Task ASessionsSearchesForm_IsItsOwn_AndEndsWithTheSession()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Search("First trip"));
            using Browser One = await Host.SignedIn();
            using Browser Two = await Host.SignedIn();
            WebPage Page = await One.Get("/searches/Alpha.yaml");
            await One.Post("/searches/Alpha.yaml", SearchForm.Fields(Page, "check", ("f.displayName", "Unsaved in one")).ToArray());

            WebPage Other = await Two.Get("/searches/Alpha.yaml");
            Assert.That(SettingsForm.Element(Other, "f-displayName")!.Attribute("value")?.Value, Is.EqualTo("First trip"), "another session never gets these unsaved edits");
            Assert.That(Other.Body, Does.Not.Contain("Unsaved in one"));
            Assert.That((await One.Get("/searches")).Text(), Does.Contain("unsaved changes"));
            Assert.That((await Two.Get("/searches")).Text(), Does.Not.Contain("unsaved changes"));

            int Before = Host.Service<WebSettingsWorkspaces>().Count;
            await One.Get("/");
            await One.Post("/signout");
            Assert.That(Host.Service<WebSettingsWorkspaces>().Count, Is.EqualTo(Before - 1), "the form ended with its session");
            using Browser Again = await Host.SignedIn();
            Assert.That((await Again.Get("/searches/Alpha.yaml")).Body, Does.Not.Contain("Unsaved in one"));
        }

        // ---- what the searches pages never do ----

        [Test]
        public async Task TheSearchesPages_NeverRunAMonitoringOrASearch_NeverTakeTheLock_AndNeverWriteCampHistory()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "General.yaml"), "outputTo: Email\n");
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Search("First trip"));
            //a monitoring of another process holds the lock: the files are still edited and saved, as in the desktop GUI
            using FileStream? Held = MonitoringLock.TryAcquire(Host.Folder, out MonitoringLockProblem? Problem);
            Assert.That(Held, Is.Not.Null, Problem?.ToString());
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/searches/Alpha.yaml");
            WebPage Warned = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Page, "check").Where(F => F.Item1 != "f.autoRun").ToArray());
            Assert.That(SettingsForm.Element(Warned, "monitoring-warning")!.Value, Does.StartWith("Warning: With this change saved, email mode (outputTo: Email) would refuse to start."));
            WebPage Saved = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Warned, "save").ToArray());
            Assert.That(Saved.Text(), Does.Contain("Saved Alpha.yaml."), "a warning only (D73)");
            WebPage Created = await Owner.Post("/searches?handler=create", SearchForm.Fields(await Owner.Get("/searches"), "create", ("new.name", "Second")).ToArray());
            Assert.That(Created.Status, Is.EqualTo(HttpStatusCode.Redirect));

            Assert.That(File.Exists(FileOf(Host, "Camp.History")) || Directory.Exists(FileOf(Host, "Camp.History")), Is.False);
            Assert.That(Directory.GetFileSystemEntries(Host.Folder).Select(Path.GetFileName).Where(N => N != "Web.keys" && N != "Web.password").OrderBy(N => N, StringComparer.Ordinal),
                Is.EqualTo(new[] { "Alpha.yaml", "Alpha.yaml.bak", MonitoringLock.FileName, "General.yaml", "Second.yaml" }), "no other file");
            Assert.That(Host.Lines, Is.Empty, "no line of the console: no monitoring, no search");
            //the pages have no monitoring and no search to run
            foreach (string Path_ in new[] { "/searches", "/searches/Alpha.yaml" })
            {
                WebPage Shown = await Owner.Get(Path_);
                Assert.That(Shown.Xml().Descendants("button").Select(B => (string?)B.Attribute("value") ?? string.Empty).Where(V => V.Contains("run", StringComparison.OrdinalIgnoreCase)
                    || V.Contains("start", StringComparison.OrdinalIgnoreCase) || V.Contains("stop", StringComparison.OrdinalIgnoreCase) || V.Contains("delete", StringComparison.OrdinalIgnoreCase)), Is.Empty, Path_);
            }
        }

        [Test]
        public void TheSearchesPagesCode_ReachesNoMonitoringSearchRunNotificationOrDeletion()
        {
            //the page model, the adapter, the workspaces, the campground data and the pages and partials Razor compiled
            Type[] Types = typeof(SearchesModel).Assembly.GetTypes()
                .Where(T => T == typeof(SearchesModel) || T == typeof(WebSettingsForm) || T == typeof(WebSettingsWorkspaces) || T == typeof(WebCampgroundData)
                    || T == typeof(WebPageText) || (T.FullName ?? "").Contains("Searches", StringComparison.Ordinal) || (T.FullName ?? "").Contains("_FilterSelector", StringComparison.Ordinal)
                    || (T.FullName ?? "").Contains("_SettingField", StringComparison.Ordinal))
                .ToArray();
            Assert.That(Types.Select(T => T.Name), Does.Contain("Pages_Searches").And.Contain("Pages_Shared__FilterSelector").And.Contain("Pages_Shared__SettingField"), "the compiled pages are scanned too");
            List<string> Called = IlReferences.ReferencedMembers(Types).Select(C => C.Member).Distinct().ToList();
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.RecDotOrg.api.", "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch",
                "CampertronLibrary.function.RecDotOrg.data.RefreshRidbRecreationData", "CampertronLibrary.function.RecDotOrg.data.Cache", "CampertronLibrary.function.Base.TestNotification",
                "CampertronLibrary.function.Base.Notifications", "CampsiteConfig::", "System.Net.Http.", "System.Net.Mail.", "System.Diagnostics.Process",
                "System.IO.File::Delete", "System.IO.Directory::Delete", "System.IO.FileInfo::Delete", "System.IO.File::Move", "System.IO.File::Replace",
                "System.Console::",
            };
            Assert.That(Called.Where(M => Forbidden.Any(F => M.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            Assert.That(Called.Where(M => M.StartsWith("CampertronLibrary.function.RecDotOrg.data.Load::", StringComparison.Ordinal)), Is.EqualTo(new[] { "CampertronLibrary.function.RecDotOrg.data.Load::PrepareDatabase" }),
                "of the start's code only the campground data's preparation, never its automatic refresh");
            Assert.That(Called, Does.Contain("CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundPickerEntries").And.Contain("CampertronLibrary.function.RecDotOrg.sqlite.Read::FilterChoicesByCampground"));
        }

        [Test]
        public async Task ASearchsPage_TakesAsManyEntriesAsItsValuesNeed_WithTheWebUIsOtherFormLimits()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Search("First trip"));
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/searches/Alpha.yaml");
            //every campsite name checked in both lists: far more entries than the settings pages take
            List<(string, string)> Many = SearchForm.Fields(Page, "check");
            Many.AddRange(Enumerable.Range(0, 30).Select(i => ("f.includeSites.o", i.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            Many.AddRange(Enumerable.Range(0, 300).Select(i => ("extra." + i, "x")));
            WebPage Taken = await Owner.Post("/searches/Alpha.yaml", Many.ToArray());
            Assert.That(Taken.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(SettingsForm.Element(Taken, "f-includeSites-status")!.Value, Is.EqualTo("30 campsite names at this campground, 30 checked."));

            List<(string, string)> TooMany = SearchForm.Fields(Taken, "check");
            TooMany.AddRange(Enumerable.Range(0, SearchesModel.MaxFormValues).Select(i => ("extra." + i, "x")));
            Assert.That((await Owner.Post("/searches/Alpha.yaml", TooMany.ToArray())).Status, Is.EqualTo(HttpStatusCode.BadRequest), "more entries than the page allows");
            List<(string, string)> LongKey = SearchForm.Fields(Taken, "check");
            LongKey.Add((new string('k', CampertronWebHost.MaxFormKeyLength + 1), "x"));
            Assert.That((await Owner.Post("/searches/Alpha.yaml", LongKey.ToArray())).Status, Is.EqualTo(HttpStatusCode.BadRequest), "the web UI's key length");
            List<(string, string)> LongValue = SearchForm.Fields(Taken, "check");
            LongValue.Add(("f.displayName", new string('v', CampertronWebHost.MaxFormValueLength + 1)));
            Assert.That((await Owner.Post("/searches/Alpha.yaml", LongValue.ToArray())).Status, Is.EqualTo(HttpStatusCode.BadRequest), "the web UI's value length");
            Assert.That(File.ReadAllText(FileOf(Host, "Alpha.yaml")), Is.EqualTo(Search("First trip")));

            //the settings pages keep their own limit
            WebPage General = await Owner.Get("/settings/general");
            List<(string, string)> Settings = SettingsForm.Fields(General, "check");
            Settings.AddRange(Enumerable.Range(0, CampertronWebHost.MaxFormValues).Select(i => ("extra." + i, "x")));
            Assert.That((await Owner.Post("/settings/general", Settings.ToArray())).Status, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task NewSearchAndSaveAs_AreRateLimited_AndAHandlerInTheFormsBodyAvoidsNothing()
        {
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Search("First trip"));
            using Browser Owner = await Host.SignedIn();
            int Allowed = 0;
            for (int i = 0; i < CampertronWebHost.FilesPerClient + 1; i++)
            {
                WebPage List = await Owner.Get("/searches");
                WebPage Result = await Owner.Post("/searches?handler=create", SearchForm.Fields(List, "create", ("new.name", "Trip " + i)).ToArray());
                if (Result.Status == HttpStatusCode.TooManyRequests)
                {
                    break;
                }
                Allowed++;
            }
            Assert.That(Allowed, Is.EqualTo(CampertronWebHost.FilesPerClient));
            WebPage Page = await Owner.Get("/searches/Alpha.yaml");
            Assert.That((await Owner.Post("/searches/Alpha.yaml?handler=saveas", SearchForm.Fields(Page, "saveas", ("saveas.name", "Over")).ToArray())).Status, Is.EqualTo(HttpStatusCode.TooManyRequests));
            //a handler named in the body is no handler: the page's own POST runs (Look for changes, Enter), never a creation
            WebPage List2 = await Owner.Get("/searches");
            WebPage Body = await Owner.Post("/searches", SearchForm.Fields(List2, "create", ("new.name", "Sneaked"), ("handler", "create")).ToArray());
            Assert.That(Body.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(File.Exists(FileOf(Host, "Sneaked.yaml")), Is.False);
            Assert.That(File.Exists(FileOf(Host, "Over.yaml")), Is.False);
        }

        // ---- the campground data ----

        [Test]
        public async Task TheCampgroundData_IsPreparedOnceWhenAPageNeedsIt_AndAMissingDatabaseIsNeverOpened()
        {
            int Prepared = 0;
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true, Configure: Settings => Settings.PrepareCampgroundData = () => Prepared++);
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Search("First trip"));
            using Browser Owner = await Host.SignedIn();
            WebPage Page = await Owner.Get("/searches/Alpha.yaml");
            Assert.That(Prepared, Is.EqualTo(1), "the preparation ran when the page needed the data");
            Assert.That(Host.Lines, Is.EqualTo(new[] { WebCampgroundData.FirstUseLine }), "one fixed line");
            Assert.That(File.Exists(FileOf(Host, "RecreationDotOrg.db")), Is.False, "a database that is not there is never opened (SQLite would create an empty one)");
            Assert.That(Page.Text(), Does.Contain("Campertron couldn't read its campground data, so campgrounds can't be listed or checked here."));
            Assert.That(SettingsForm.Element(Page, WebSettingsForm.CampgroundCurrentId)!.Value, Is.EqualTo("ID " + Campground + " (the campground data can't be read to name it)."));
            Assert.That(Page.Body, Does.Not.Contain(Host.Folder));
            //the form is the session's: another page asks the data again only in a new session's form, and the page still edits
            WebPage Saved = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Page, "save", ("f.displayName", "Still editable")).ToArray());
            Assert.That(Saved.Text(), Does.Contain("Saved Alpha.yaml."));
        }

        [Test]
        public async Task AFileTheWebUICantRead_IsSaid_WithoutTheSettingsFolder_AndNamesAreEncoded()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file permissions and file names");
            }
            await using WebTestHost Host = await Start();
            File.WriteAllText(FileOf(Host, "Alpha.yaml"), Search("<script>x</script>"));
            File.WriteAllText(FileOf(Host, "<b>bold & co.yaml"), Search("Odd name"));
            File.WriteAllText(FileOf(Host, "Locked.yaml"), Search("Locked"));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(FileOf(Host, "Locked.yaml"), UnixFileMode.None);
            }
            using Browser Owner = await Host.SignedIn();
            WebPage List = await Owner.Get("/searches");
            Assert.That(List.Body, Does.Not.Contain("<script>x</script>").And.Not.Contain("<b>bold"));
            Assert.That(List.Text(), Does.Contain("<b>bold & co.yaml").And.Contain("<script>x</script>"));
            Assert.That(List.Xml().Descendants("a").Any(A => (string?)A.Attribute("href") == "/searches/%3Cb%3Ebold%20%26%20co.yaml"), Is.True);
            Assert.That((await Owner.Get("/searches/%3Cb%3Ebold%20%26%20co.yaml")).Status, Is.EqualTo(HttpStatusCode.OK));
            WebPage Locked = await Owner.Get("/searches/Locked.yaml");
            Assert.That(Locked.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Locked.Body, Does.Not.Contain(Host.Folder), "the operating system's message names the folder; the page does not");
            Assert.That(Locked.Text(), Does.Contain("the settings folder").Or.Contain("can't be read"));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(FileOf(Host, "Locked.yaml"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }

    // the entries of a searches page's form, as a browser sends them
    internal static class SearchForm
    {
        // the page's form (the layout's sign-out form is another)
        internal static XElement Form(WebPage Page) => Page.Xml().Descendants("form").Single(F => ((string?)F.Attribute("action") ?? "").StartsWith("/searches", StringComparison.Ordinal));

        internal static List<(string Name, string Value)> Fields(WebPage Page, string? Action, params (string Name, string Value)[] Changes)
        {
            XElement Form = SearchForm.Form(Page);
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

        // the position of a selector's value on the page (its check box's value)
        internal static int OptionIndex(WebPage Page, string Key, string Label)
        {
            XElement Label_ = Page.Xml().Descendants("label").First(L => ((string?)L.Attribute("for") ?? "").StartsWith("f-" + Key + "-o-", StringComparison.Ordinal)
                && L.Nodes().OfType<XText>().Any(T => T.Value.Trim() == Label));
            return int.Parse(((string)Label_.Attribute("for")!).Substring(("f-" + Key + "-o-").Length), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
