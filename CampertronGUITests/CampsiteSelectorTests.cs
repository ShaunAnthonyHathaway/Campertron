using System.Collections.Specialized;
using System.ComponentModel;
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
        internal static CampgroundFilterChoices Large(string Facility)
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

        // How quick the selector's steps must be (TD-62, §DB). A step is measured over its runs: its time is the fastest run's,
        // because a hosted runner's other work (dotnet test runs the solution's three test projects at once on the runner's
        // four vCPUs) and a collection only ever add time to a run, so one slow run never fails healthy code; its work is the
        // most bytes a run allocated on this thread (the UI thread, which does the whole step), which no runner changes. A
        // run counts only if it did the step inside the measurement: the list rebuilt, the editing session given the list
        // exactly as often as expected, and, for a change of checks, the 360 rows kept (no row made again).
        // A quick step (one change by the selector alone, of a saved list or of a usual one) stays under a quarter of a
        // second, where a click visibly lags. A step with hundreds of unsaved items is the editing session's known limitation
        // (one verified editor step per unsaved item: about 200 MB allocated, 0.2 to 0.8 s here, 2.5 s once on a hosted
        // runner), bounded to catch a large regression only. CampsiteSelectorTimingTests guards this design.
        internal const int LargestCampground = 360;
        internal const int QuickRuns = 5;
        internal static readonly TimeSpan QuickBound = TimeSpan.FromMilliseconds(250);
        internal static readonly TimeSpan SlowBound = TimeSpan.FromSeconds(10);
        internal const long MiB = 1024 * 1024;
        // measured (§DB): the selector's own rebuild of 360 rows 0.46 MiB; a change with one to ten unsaved items 3.0 to
        // 4.1 MiB; 360 unsaved items 189 to 201 MiB
        internal const long SelectorBudget = 2 * MiB;
        internal const long QuickBudget = 16 * MiB;
        internal const long SlowBudget = 512 * MiB;

        // what a step cost over its runs
        internal sealed record StepCost(string What, int Runs, TimeSpan Fastest, long Bytes, int Rebuilds, int Edits, int RowChanges)
        {
            // what is wrong: every run as slow as Bound or slower, a run that allocated Budget or more, or not the step that
            // was measured
            internal List<string> Problems(TimeSpan Bound, long Budget, int Edits, bool KeepsRows)
            {
                var Found = new List<string>();
                if (Fastest >= Bound)
                {
                    Found.Add($"{What}: the fastest of {Runs} runs took {Fastest.TotalMilliseconds:F1} ms; the bound is {Bound.TotalMilliseconds:F0} ms");
                }
                if (Bytes >= Budget)
                {
                    Found.Add($"{What}: a run allocated {Bytes / (double)MiB:F2} MiB; the budget is {Budget / (double)MiB:F0} MiB");
                }
                if (Rebuilds < Runs)
                {
                    Found.Add($"{What}: {Rebuilds} rebuilds of the list in {Runs} runs, so not every run did the step");
                }
                if (this.Edits != Edits)
                {
                    Found.Add($"{What}: the editing session got the list {this.Edits} times; expected {Edits}");
                }
                if (KeepsRows && RowChanges != 0)
                {
                    Found.Add($"{What}: the list's rows changed {RowChanges} times; a change of checks keeps every row");
                }
                return Found;
            }
        }

        // runs Step Runs times and measures each run (its session edits counted through the selector's hook to the session)
        internal static StepCost Measure(string What, FilterSelectorField Field, int Runs, Action Step)
        {
            int Rebuilds = 0, Edits = 0, RowChanges = 0;
            PropertyChangedEventHandler Rebuilt = (_, E) =>
            {
                if (E.PropertyName == nameof(FilterSelectorField.StatusText))
                {
                    Rebuilds++;
                }
            };
            NotifyCollectionChangedEventHandler Changed = (_, _) => RowChanges++;
            Action? Session = Field.RowsChangedForTests;
            Field.RowsChangedForTests = Session == null ? null : () =>
            {
                Edits++;
                Session();
            };
            Field.PropertyChanged += Rebuilt;
            Field.Options.CollectionChanged += Changed;
            TimeSpan Fastest = TimeSpan.MaxValue;
            long Bytes = 0;
            try
            {
                for (int Run = 0; Run < Runs; Run++)
                {
                    long Before = GC.GetAllocatedBytesForCurrentThread();
                    long Start = Stopwatch.GetTimestamp();
                    Step();
                    TimeSpan Took = Stopwatch.GetElapsedTime(Start);
                    Bytes = Math.Max(Bytes, GC.GetAllocatedBytesForCurrentThread() - Before);
                    Fastest = Took < Fastest ? Took : Fastest;
                }
            }
            finally
            {
                Field.Options.CollectionChanged -= Changed;
                Field.PropertyChanged -= Rebuilt;
                Field.RowsChangedForTests = Session;
            }
            TestContext.Out.WriteLine($"{What}: fastest of {Runs} runs {Fastest.TotalMilliseconds:F1} ms, at most {Bytes / (double)MiB:F2} MiB a run, {Rebuilds} rebuilds, {Edits} session edits, {RowChanges} row changes");
            return new StepCost(What, Runs, Fastest, Bytes, Rebuilds, Edits, RowChanges);
        }

        [AvaloniaTest]
        public void TheLargestCampground_IsSearchable_Virtualized_AndQuick()
        {
            Dispatcher Ui = Dispatcher.UIThread;
            var Source = new FilterChoiceSource(Large, () => "db", Job => Ui.Post(Job), Job => Task.Run(Job));
            string Text = Search("includeSites: [B030, Mine]\nexcludeSites: []\n");
            string WithSites(IEnumerable<string> Names) => Text.Replace("[B030, Mine]", "[" + string.Join(", ", Names) + "]");
            SearchFileViewModel Trip = Editor(Source, Text);
            WaitUntil(() => Trip.IncludeSites.Status == FilterChoicesStatus.Loaded, () => "loaded");
            FilterSelectorField Sites = Trip.IncludeSites;
            List<string> All = Sites.Options.Select(O => O.Label).ToList();

            Assert.That(Sites.Options, Has.Count.EqualTo(LargestCampground));
            Assert.That((Sites.CanFind, Sites.ShowBulkActions, Sites.ListMaxHeight), Is.EqualTo((true, true, 320d)));
            Assert.That(Sites.Options[29].Label + " " + Sites.Options[29].CampsitesText, Is.EqualTo("A030 2 campsites share this name"));

            // narrowing, then checking every shown name, then clearing them: only shown names change, the kept one stays (with
            // the session's edits, 360 unsaved items among them)
            int Checked = 0;
            StepCost Sequence = Measure("find, check 10, check 360 and clear 360", Sites, 1, () =>
            {
                Sites.FindText = "c05";
                Assert.That(Sites.Options.Select(O => O.Label), Is.EqualTo(new[] { "C050", "C051", "C052", "C053", "C054", "C055", "C056", "C057", "C058", "C059" }));
                Sites.SelectShown();
                Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("[B030, Mine]", "[B030, Mine, C050, C051, C052, C053, C054, C055, C056, C057, C058, C059]")));
                Sites.FindText = "";
                Sites.SelectShown();
                Checked = Sites.Options.Count(O => O.IsChecked == true);
                Sites.ClearShown();
            });
            Assert.That(Checked, Is.EqualTo(LargestCampground));
            Assert.That(Trip.Session.Candidate, Is.EqualTo(WithSites(new[] { "Mine" })), "Clear shown removed every name of the campground, not the kept one");
            Assert.That(Sequence.Problems(SlowBound, SlowBudget, Edits: 3, KeepsRows: false), Is.Empty);

            // one change in a list of 360 checked names: the selector alone (its work guards the linear rebuild, 0.4 to 4.4 ms;
            // the former quadratic one took 373 ms), then the editing session's verified edit with the 360 unsaved items
            Sites.SelectShown();
            Action Session = Sites.RowsChangedForTests!;
            Sites.RowsChangedForTests = null;
            StepCost Selector = Measure("one change among 360 checked, the selector alone", Sites, QuickRuns, () => Sites.Toggle(Sites.Options[100]));
            Sites.RowsChangedForTests = Session;
            Assert.That((Sites.Options[100].Label, Sites.Options[100].IsChecked), Is.EqualTo(("B041", (bool?)false)), "an odd number of changes unchecked B041");
            Assert.That(Selector.Problems(QuickBound, SelectorBudget, Edits: 0, KeepsRows: true), Is.Empty);
            StepCost Edit = Measure("the session's edit with 360 unsaved items", Sites, 1, Session);
            Assert.That(Trip.Session.Candidate, Is.EqualTo(WithSites(All.Where(N => N != "B041").Prepend("Mine"))), "the edit removed B041, and only it");
            Assert.That(Edit.Problems(SlowBound, SlowBudget, Edits: 0, KeepsRows: true), Is.Empty);

            // saved, the 360 names are the file's items: a change is one verified step
            Sites.Toggle(Sites.Options[100]);
            var Clock = Stopwatch.StartNew();
            Trip.Save();
            TestContext.Out.WriteLine($"saving 361 items: {Clock.Elapsed.TotalMilliseconds:F0} ms ({Trip.StatusText})");
            Assert.That(Trip.StatusIsError, Is.False, Trip.StatusText);
            Assert.That(File.ReadAllText(In("Trip.yaml")), Is.EqualTo(WithSites(All.Where(N => N != "B041").Prepend("Mine").Append("B041"))));
            StepCost Saved = Measure("one change among 360 saved items", Sites, QuickRuns, () => Sites.Toggle(Sites.Options[150]));
            Assert.That(Trip.Session.Candidate, Is.EqualTo(WithSites(All.Where(N => N != "B041" && N != "C031").Prepend("Mine").Append("B041"))),
                "an odd number of changes removed C031");
            Assert.That(Saved.Problems(QuickBound, QuickBudget, Edits: QuickRuns, KeepsRows: true), Is.Empty);
            Trip.Revert();

            // a usual list: saved with one item, then 10 names checked. (TD-62: this step once changed the list while the file
            // still held the 361 saved items, so the session replanned 360 removals, the known limitation, under a one-second
            // bound; Clear shown is that step now)
            StepCost Clear = Measure("Clear shown with 361 saved items", Sites, 1, Sites.ClearShown);
            Assert.That(Trip.Session.Candidate, Is.EqualTo(WithSites(new[] { "Mine" })));
            Assert.That(Clear.Problems(SlowBound, SlowBudget, Edits: 1, KeepsRows: true), Is.Empty);
            Trip.Save();
            Assert.That(Trip.StatusIsError, Is.False, Trip.StatusText);
            Sites.FindText = "D00";
            Sites.SelectShown();
            Sites.FindText = "";
            StepCost Usual = Measure("one change of a usual list", Sites, QuickRuns, () => Sites.Toggle(Sites.Options[200]));
            Assert.That(Trip.Session.Candidate, Is.EqualTo(WithSites(new[] { "Mine", "D001", "D002", "D003", "D004", "D005", "D006", "D007", "D008", "D009", "D021" })),
                "an odd number of changes checked D021");
            Assert.That(Sites.StatusText, Is.EqualTo("360 campsite names at this campground, 10 checked."));
            Assert.That(Usual.Problems(QuickBound, QuickBudget, Edits: QuickRuns, KeepsRows: true), Is.Empty);
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
