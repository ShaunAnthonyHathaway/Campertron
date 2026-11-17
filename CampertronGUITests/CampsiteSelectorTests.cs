using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using Microsoft.Data.Sqlite;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The campsite selectors (decision D51, stage 3): includeSites and excludeSites as database-backed checklists of the
    // chosen campground's campsite names, in their natural order, a name that several campsite records share shown once with
    // its count, blank and space-padded names visible and kept exactly, names always matched exactly, the measured largest
    // campground's size (360 records) searchable, virtualized and fast. Real windows with the fixture database (Charlie's
    // names) and view models with a choice source the test controls.
    public class CampsiteSelectorTests
    {
        private const string Head =
            "# campsite selector tests\n" +
            "displayName: Sites   # shown in the results\n" +
            "autoRun: false\n";
        private const string Middle =
            "totalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\nsearchValueDates: []\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\n";

        private static string Search(string Lists, string Campground = GuiTestEnvironment.CampgroundCharlieID, string Extra = "") =>
            Head + "campgroundID: '" + Campground + "'\n" + Middle + Extra + Lists + "consecutiveDays: 1\n# end\n";

        private string Folder = string.Empty;
        private string In(string Name) => Path.Join(Folder, Name);

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "sites-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(In("General.yaml"), "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            File.WriteAllText(In("Email.yaml"), "smtpServer: ''\nsmtpPort: 0\n");
            File.Copy(GuiTestEnvironment.DbFile, In("RecreationDotOrg.db"), overwrite: true);
        }

        [TearDown]
        public void DeleteFolder()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, true);
            }
        }

        private (MainWindow Window, SearchesViewModel Searches, SearchFileViewModel File) Open(string Text)
        {
            File.WriteAllText(In("Trip.yaml"), Text);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            SearchesViewModel Searches = ViewModel.Searches!;
            SearchFileViewModel Trip = Searches.Files.Single(F => F.FileName == "Trip.yaml");
            Searches.SelectedFile = Trip;
            WaitUntil(() => Trip.Selectors.All(S => S.Status != FilterChoicesStatus.Loading), () => "the campground's values were read");
            Settle(Window);
            return (Window, Searches, Trip);
        }

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static List<string> Rows(FilterSelectorField Field) =>
            Field.Options.Select(O => (O.IsVariant ? "  " : "") + O.Label + " | " + O.CampsitesText + " " + (O.IsChecked == true ? "[x]" : O.IsChecked == null ? "[-]" : "[ ]")).ToList();

        private static FilterOptionRow Option(FilterSelectorField Field, string Label, bool Variant = false) =>
            Field.Options.Single(O => O.Label == Label && O.IsVariant == Variant);

        [AvaloniaTest]
        public void TheNames_AreTheCampgroundsOwn_InTheirNaturalOrder_SharedNamesOnce()
        {
            var (_, _, Trip) = Open(Search("includeSites: ['10']\nexcludeSites: []\n"));

            Assert.That(Rows(Trip.IncludeSites), Is.EqualTo(new[]
            {
                "(blank) | 1 campsite [ ]",
                "(2 spaces) | 1 campsite [ ]",
                "“ B1 ” (with spaces at its start or end) | 1 campsite [ ]",
                "2 | 1 campsite [ ]",
                "10 | 2 campsites share this name [x]",
                "B1 (2 spellings that differ only by case) | 2 campsites [ ]",
                "  B1 | 1 campsite [ ]",
                "  b1 | 1 campsite [ ]",
            }));
            Assert.That(Trip.IncludeSites.StatusText, Is.EqualTo("7 campsite names at this campground, 1 checked."));
            Assert.That(Option(Trip.IncludeSites, "10").AccessibleName, Is.EqualTo("10, 2 campsites share this name, checked"));
            Assert.That(Option(Trip.IncludeSites, "10").AlsoText, Is.Empty, "names never match more than themselves");
            Assert.That(Trip.IsDirty, Is.False);
        }

        [AvaloniaTest]
        public void ASharedName_IsOneItem_AndBlankAndPaddedNamesAreWrittenExactly()
        {
            string Text = Search("includeSites:\n  - '10'   # the twins\nexcludeSites: []\n");
            var (Window, _, Trip) = Open(Text);

            Option(Trip.IncludeSites, "(blank)").IsChecked = true;
            Option(Trip.IncludeSites, "“ B1 ” (with spaces at its start or end)").IsChecked = true;
            Option(Trip.IncludeSites, "2").IsChecked = true;
            Settle(Window);
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("  - '10'   # the twins\n", "  - '10'   # the twins\n  - ''\n  - ' B1 '\n  - '2'\n")),
                "each name once, exactly as stored, numbers quoted so they stay text");
            // the validation warns about a blank item; the warning is shown with the checked blank name
            Assert.That(Option(Trip.IncludeSites, "(blank)").MessageText, Does.StartWith("Warning: includeSites item 2 is blank"));

            Option(Trip.IncludeSites, "10").IsChecked = false;
            Settle(Window);
            Assert.That(Trip.Session.Candidate, Does.Not.Contain("'10'"), "unchecking a shared name removes its item");
        }

        [AvaloniaTest]
        public void NamesTheCampgroundLacks_AreKept_AndSayTheyMatchExactly()
        {
            string Text = Search("includeSites: [B 1, '010', A002]\nexcludeSites: [b1]\n", Extra: "filterMatching: Contains\n");
            var (Window, _, Trip) = Open(Text);

            Assert.That(Trip.IncludeSites.OtherValues.Select(O => O.Label), Is.EqualTo(new[] { "B 1", "010", "A002" }), "kept, in the file's order");
            Assert.That(Trip.IncludeSites.OtherValues[0].Description,
                Is.EqualTo("Not a campsite name of this campground; campsite names always match exactly, so it matches no campsite here."));
            Assert.That(Trip.IncludeSites.OtherValues[1].Description, Does.Not.Contain("differs only"), "010 is not 10: names match character for character");
            Assert.That(Option(Trip.ExcludeSites, "b1", Variant: true).IsChecked, Is.True);
            Assert.That(Option(Trip.ExcludeSites, "B1 (2 spellings that differ only by case)").IsChecked, Is.Null, "one spelling of two");

            // another campground: Alpha's names; Charlie's checked name is kept as another value
            Trip.Campground.TypedId = GuiTestEnvironment.CampgroundAlphaID;
            Trip.Campground.UseTypedId();
            WaitUntil(() => Trip.Selectors.All(S => S.Status == FilterChoicesStatus.Loaded), () => "Alpha's values");
            Settle(Window);
            Assert.That(Rows(Trip.IncludeSites), Is.EqualTo(new[] { "A001 | 1 campsite [ ]", "A002 | 2 campsites share this name [x]", "G001 | 1 campsite [ ]" }));
            Assert.That(Trip.ExcludeSites.OtherValues.Select(O => O.Label), Is.EqualTo(new[] { "b1" }));
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("campgroundID: '990003'", "campgroundID: '990001'")), "the lists are untouched");
        }

        [AvaloniaTest]
        public void TheSameNameIncludedAndExcluded_IsWarned()
        {
            var (_, _, Trip) = Open(Search("includeSites: ['2']\nexcludeSites: ['2']\n"));
            Assert.That(Trip.ExcludeSites.ConflictText, Does.StartWith("Warning: also in includeSites: “2”."));
        }

        // ---- the largest campground: 360 campsite records (258830 in the shipped data) ----

        private SearchFileViewModel Editor(FilterChoiceSource Source, string Text)
        {
            File.WriteAllText(In("Trip.yaml"), Text);
            var Searches = new SearchesViewModel(Folder, null, () => new List<CampgroundPickerEntry>
            {
                new CampgroundPickerEntry(new CampgroundChoice(GuiTestEnvironment.CampgroundCharlieID, "CHARLIE", null, null), null),
            }, () => Source);
            Searches.Show();
            return Searches.Files.Single(F => F.FileName == "Trip.yaml");
        }

        // 360 names as a large campground writes them (loops A to F, 1 to 60), 12 of them shared by two records
        private static CampgroundFilterChoices Large(string Facility)
        {
            var Names = new List<FilterChoice>();
            foreach (char Loop in "ABCDEF")
            {
                for (int i = 1; i <= 60; i++)
                {
                    Names.Add(new FilterChoice(Loop + i.ToString("000"), i % 30 == 0 ? 2 : 1));
                }
            }
            return new CampgroundFilterChoices(Facility, 372, Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>(), Names, Array.Empty<FilterChoice>());
        }

        [AvaloniaTest]
        public void TheLargestCampground_IsSearchable_Virtualized_AndQuick()
        {
            Dispatcher Ui = Dispatcher.UIThread;
            var Source = new FilterChoiceSource(Large, () => "db", Job => Ui.Post(Job), Job => Task.Run(Job));
            string Text = Search("includeSites: [B030, Mine]\nexcludeSites: []\n");
            SearchFileViewModel Trip = Editor(Source, Text);
            WaitUntil(() => Trip.IncludeSites.Status == FilterChoicesStatus.Loaded, () => "loaded");
            FilterSelectorField Sites = Trip.IncludeSites;

            Assert.That(Sites.Options, Has.Count.EqualTo(360));
            Assert.That((Sites.CanFind, Sites.ShowBulkActions, Sites.ListMaxHeight), Is.EqualTo((true, true, 320d)));
            Assert.That(Sites.Options[29].Label + " " + Sites.Options[29].CampsitesText, Is.EqualTo("A030 2 campsites share this name"));

            // narrowing, then checking every shown name, then clearing them: only shown names change, the kept one stays
            var Clock = Stopwatch.StartNew();
            Sites.FindText = "c05";
            Assert.That(Sites.Options.Select(O => O.Label), Is.EqualTo(new[] { "C050", "C051", "C052", "C053", "C054", "C055", "C056", "C057", "C058", "C059" }));
            Sites.SelectShown();
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("[B030, Mine]", "[B030, Mine, C050, C051, C052, C053, C054, C055, C056, C057, C058, C059]")));
            Sites.FindText = "";
            Sites.SelectShown();
            int Checked = Sites.Options.Count(O => O.IsChecked == true);
            Sites.ClearShown();
            TimeSpan Elapsed = Clock.Elapsed;
            Assert.That(Checked, Is.EqualTo(360));
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("[B030, Mine]", "[Mine]")), "Clear shown removed every name of the campground, not the kept one");
            TestContext.Out.WriteLine($"find, check 10, check 360 and clear 360 (with the session's edits): {Elapsed.TotalMilliseconds:F0} ms");
            Assert.That(Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));

            // one toggle in a list of 360 with every name checked: the selector itself, and the editing session's verified edit
            Sites.SelectShown();
            Action? Session = Sites.RowsChangedForTests;
            Sites.RowsChangedForTests = null;
            Clock.Restart();
            Sites.Toggle(Sites.Options[100]);
            TimeSpan Selector = Clock.Elapsed;
            Sites.RowsChangedForTests = Session;
            Clock.Restart();
            Session!();
            TimeSpan Edit = Clock.Elapsed;
            TestContext.Out.WriteLine($"one toggle among 360 checked: selector {Selector.TotalMilliseconds:F1} ms, the session's edit and validation {Edit.TotalMilliseconds:F0} ms");
            // the selector's own work guards the linear rebuild (1.2 to 3.2 ms; the former quadratic one took 373 ms). The
            // session's edit is the YAML editor's cost with 360 unsaved items (a known limitation: 0.7 to 1 s on a desktop,
            // 2.5 s on a hosted CI runner, master's CI 43996072221), so its bound only catches a large regression
            Assert.That(Selector, Is.LessThan(TimeSpan.FromMilliseconds(250)));
            Assert.That(Edit, Is.LessThan(TimeSpan.FromSeconds(10)));
            Assert.That(Trip.Session.Candidate, Does.Not.Contain("- A041\n"), "the edit removed A041");
            // saved, the 360 names are the file's items: a change is one verified step
            Sites.Toggle(Sites.Options[100]);
            Clock.Restart();
            Trip.Save();
            TestContext.Out.WriteLine($"saving 361 items: {Clock.Elapsed.TotalMilliseconds:F0} ms ({Trip.StatusText})");
            Assert.That(Trip.StatusIsError, Is.False, Trip.StatusText);
            Clock.Restart();
            Sites.Toggle(Sites.Options[150]);
            TestContext.Out.WriteLine($"one toggle among 360 saved items: {Clock.Elapsed.TotalMilliseconds:F0} ms");
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
            Trip.Revert();
            // a usual list: 10 names checked
            Sites.ClearShown();
            Sites.FindText = "D00";
            Sites.SelectShown();
            Sites.FindText = "";
            Clock.Restart();
            Sites.Toggle(Sites.Options[200]);
            TestContext.Out.WriteLine($"one toggle with 9 checked (selector and session): {Clock.Elapsed.TotalMilliseconds:F0} ms");
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
        }

        [AvaloniaTest]
        public void TheLargestCampground_InTheWindow_RealizesOnlyTheRowsInView()
        {
            // the window's own source with 360 names of the fixture database's Charlie? The fixture has 8; a test database
            // with 360 records of one campground stands in for 258830
            using (var Connection = new SqliteConnection("Data Source=" + In("RecreationDotOrg.db") + ";Pooling=False"))
            {
                Connection.Open();
                using var Insert = Connection.CreateCommand();
                Insert.CommandText = "INSERT INTO CampsitesEntries (CampsiteID, FacilityID, CampsiteName, CampsiteType, CampsiteAccessible, CampsiteLongitude, CampsiteLatitude) VALUES ($id, '990004', $name, 'STANDARD NONELECTRIC', 0, 0, 0)";
                var Id = Insert.Parameters.Add("$id", SqliteType.Text);
                var Name = Insert.Parameters.Add("$name", SqliteType.Text);
                for (int i = 1; i <= 360; i++)
                {
                    Id.Value = "99004" + i.ToString("000");
                    Name.Value = "S" + i.ToString("000");
                    Insert.ExecuteNonQuery();
                }
            }
            var (Window, _, Trip) = Open(Search("includeSites: []\nexcludeSites: []\n", GuiTestEnvironment.CampgroundDeltaID));
            Assert.That(Trip.IncludeSites.Options, Has.Count.EqualTo(360));
            FilterSelectorView View = Window.GetVisualDescendants().OfType<FilterSelectorView>().Single(V => V.Name == "IncludeSitesSelector");
            View.BringIntoView();
            Settle(Window);
            ListBox List = View.GetVisualDescendants().OfType<ListBox>().Single(L => L.Name == "ChoiceList");
            int Realized = List.GetVisualDescendants().OfType<ListBoxItem>().Count();
            TestContext.Out.WriteLine($"realized rows: {Realized} of 360, list height {List.Bounds.Height}");
            Assert.That(Realized, Is.GreaterThan(0).And.LessThan(60), "a virtualized list");
            Assert.That(List.Bounds.Height, Is.LessThanOrEqualTo(320));
        }
    }
}
