using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using Microsoft.Data.Sqlite;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The database-backed filter selectors of the search editor (decision D51), stage 2: equipment and campsite types, the
    // matching choice, case-variant groups and the values a campground does not have. Real windows on the headless
    // platform with the fixture database (Alpha: Tent and tent, STANDARD NONELECTRIC inside GROUP STANDARD NONELECTRIC;
    // Bravo: RV/MOTORHOME, RV, Horse), each test in its own settings folder; and the view models with a choice source the
    // test controls (the worker thread, stale answers, failures). Nothing here searches availability.
    public class FilterSelectorTests
    {
        private const string Header =
            "# a search of the selector tests\n" +
            "displayName: Alpha weekends   # shown in the results\n" +
            "autoRun: false\n";
        private const string Middle =
            "totalHumans: 2\n" +
            "searchBy: DaysOut\n" +
            "searchValue: 7\n" +
            "searchValueDates: []\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\n";

        private static string Search(string Lists, string? Campground = GuiTestEnvironment.CampgroundAlphaID, string Extra = "") =>
            Header + (Campground == null ? "" : "campgroundID: '" + Campground + "'\n") + Middle + Extra + Lists + "consecutiveDays: 1\n# end\n";

        private string Folder = string.Empty;
        private string In(string Name) => Path.Join(Folder, Name);

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "selectors-" + Guid.NewGuid().ToString("N"));
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

        // ---- the real window ----

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
            WaitLoaded(Trip, Window);
            return (Window, Searches, Trip);
        }

        private static void WaitLoaded(SearchFileViewModel File, Window Window)
        {
            WaitUntil(() => File.Selectors.All(S => S.Status != FilterChoicesStatus.Loading), () => "the campground's values were read");
            Settle(Window);
        }

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static FilterSelectorView SelectorView(Window Window, string Key) =>
            Window.GetVisualDescendants().OfType<FilterSelectorView>().Single(v => (v.DataContext as FilterSelectorField)?.Key == Key);

        private static T Part<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found");

        private static FilterOptionRow Option(FilterSelectorField Field, string Label) => Field.Options.Single(O => O.Label == Label);

        private static List<string> Labels(FilterSelectorField Field) => Field.Options.Select(O => (O.IsVariant ? "  " : "") + O.Label + " " + Check(O.IsChecked)).ToList();
        private static string Check(bool? Checked) => Checked == true ? "[x]" : Checked == null ? "[-]" : "[ ]";

        private static void Save(SearchFileViewModel File)
        {
            Assert.That(File.CanSave, Is.True, string.Join(" ", File.Session.Notices.Select(N => N.Message)));
            File.Save();
            Assert.That(File.StatusIsError, Is.False, File.StatusText);
        }

        [AvaloniaTest]
        public void TheSelectors_ShowTheCampgroundsValues_CheckedAsTheListHasThem()
        {
            var (Window, _, Trip) = Open(Search("includeEquipment:\n  - Tent # small\nexcludeCampsiteType: []\n"));

            Assert.That(Labels(Trip.IncludeEquipment), Is.EqualTo(new[]
            {
                "RV [ ]", "SMALL TENT [ ]", "Tent (2 spellings that differ only by case) [-]", "  Tent [x]", "  tent [ ]", "Trailer [ ]"
            }));
            Assert.That(Labels(Trip.ExcludeEquipment).Count(L => L.EndsWith("[x]")), Is.Zero);
            Assert.That(Labels(Trip.IncludeCampsiteType), Is.EqualTo(new[] { "GROUP STANDARD NONELECTRIC [ ]", "STANDARD ELECTRIC [ ]", "STANDARD NONELECTRIC [ ]" }));
            Assert.That(Option(Trip.IncludeEquipment, "SMALL TENT").CampsitesText, Is.EqualTo("1 campsite"));
            Assert.That(Option(Trip.IncludeCampsiteType, "STANDARD NONELECTRIC").CampsitesText, Is.EqualTo("2 campsites"));
            Assert.That(Trip.IncludeEquipment.StatusText, Is.EqualTo("5 equipment types at this campground, 1 checked."));
            Assert.That(Trip.IncludeEquipment.OtherValues, Is.Empty);
            Assert.That(Trip.IsDirty, Is.False, "showing the values changes nothing");

            // one tab stop per list; each row read by its value, campsites and state (the list's rows are realized once it is
            // in view: the list is virtualized)
            SelectorView(Window, "includeEquipment").BringIntoView();
            Settle(Window);
            ListBox List = Part<ListBox>(SelectorView(Window, "includeEquipment"), "ChoiceList");
            List<ListBoxItem> Items = List.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            Assert.That(Items.Select(AutomationProperties.GetName), Does.Contain("Spelling Tent, 2 campsites, checked").And.Contain("RV, 1 campsite, not checked")
                .And.Contain("Tent (2 spellings that differ only by case), 3 campsites, partly checked. Checking it adds every spelling; each spelling is its own value in the search."));
            Assert.That(List.GetVisualDescendants().OfType<CheckBox>().All(C => !C.Focusable && !C.IsTabStop && AutomationProperties.GetAccessibilityView(C) == AccessibilityView.Raw), Is.True);
            Assert.That(AutomationProperties.GetName(List), Is.EqualTo("Include equipment (includeEquipment): equipment types of the campground"));
            Assert.That(AutomationProperties.GetName(Part<TextBox>(SelectorView(Window, "includeEquipment"), "CustomBox")), Is.EqualTo("Another value for includeEquipment"));
            Assert.That(AutomationProperties.GetName(Part<Button>(SelectorView(Window, "excludeEquipment"), "AddCustomButton")), Is.EqualTo("Add the value to excludeEquipment"));
        }

        [AvaloniaTest]
        public void CheckingAValue_AddsOneItemAtTheEnd_AndTheSaveKeepsEverythingElse()
        {
            string Text = Search("includeEquipment:\n  # tents first\n  - Tent # small\n  - 'RV'\nexcludeCampsiteType: [GROUP]\n");
            var (Window, _, Trip) = Open(Text);

            Option(Trip.IncludeEquipment, "Trailer").IsChecked = true;
            Option(Trip.ExcludeCampsiteType, "STANDARD ELECTRIC").IsChecked = true;
            Settle(Window);

            string Expected = Text.Replace("  - 'RV'\n", "  - 'RV'\n  - Trailer\n").Replace("excludeCampsiteType: [GROUP]", "excludeCampsiteType: [GROUP, STANDARD ELECTRIC]");
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Expected));
            Assert.That(Trip.PreviewText, Does.Contain("+   - Trailer"));
            Save(Trip);
            Assert.That(File.ReadAllText(In("Trip.yaml")), Is.EqualTo(Expected));
            Assert.That(File.ReadAllText(In("Trip.yaml.bak")), Is.EqualTo(Text));
            Assert.That(Option(Trip.IncludeEquipment, "Trailer").IsChecked, Is.True, "after the save the list is read again");
        }

        [AvaloniaTest]
        public void UncheckingAValue_RemovesEveryItemThatIsExactlyIt_AndNothingElse()
        {
            string Text = Search("includeEquipment:\n  - Tent # first\n  - RV\n  - Tent # again\n  - tent\n");
            var (Window, _, Trip) = Open(Text);
            Assert.That(Option(Trip.IncludeEquipment, "Tent (2 spellings that differ only by case)").IsChecked, Is.True);

            Option(Trip.IncludeEquipment.Options.Where(O => O.IsVariant).ToList(), "Tent").IsChecked = false;
            Settle(Window);

            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("  - Tent # first\n", "").Replace("  - Tent # again\n", "")));
            Assert.That(Option(Trip.IncludeEquipment, "Tent (2 spellings that differ only by case)").IsChecked, Is.Null, "tent stays");
        }

        private static FilterOptionRow Option(List<FilterOptionRow> Rows, string Label) => Rows.Single(O => O.Label == Label);

        [AvaloniaTest]
        public void AGroup_ChecksEverySpelling_AndUnchecksThemAll()
        {
            string Text = Search("includeEquipment: []\n");
            var (Window, _, Trip) = Open(Text);
            FilterOptionRow Group = Option(Trip.IncludeEquipment, "Tent (2 spellings that differ only by case)");

            Group.IsChecked = true;
            Settle(Window);
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("includeEquipment: []", "includeEquipment: [Tent, tent]")));
            Assert.That(Option(Trip.IncludeEquipment, "Tent (2 spellings that differ only by case)").IsChecked, Is.True);

            Option(Trip.IncludeEquipment, "Tent (2 spellings that differ only by case)").IsChecked = false;
            Settle(Window);
            Assert.That(Trip.IsDirty, Is.False, "back to the file's list: no edit");
        }

        [AvaloniaTest]
        public void ValuesTheCampgroundDoesNotHave_AreKept_ShownApart_AndRemovedOnlyByTheUser()
        {
            string Text = Search("includeEquipment:\n  - Hovercraft # mine\n  - Tent\n  - TENT\n  - ~\n  - ' Trailer'\n");
            var (Window, _, Trip) = Open(Text);

            List<FilterCustomRow> Others = Trip.IncludeEquipment.OtherValues.ToList();
            Assert.That(Others.Select(O => (O.Position, O.Label)), Is.EqualTo(new[]
            {
                (1, "Hovercraft"), (3, "TENT"), (4, "(empty item)"), (5, "“ Trailer” (with spaces at its start or end)")
            }));
            Assert.That(Others[0].Description, Does.StartWith("Not among this campground's equipment types; it matches none of them with Contains matching."));
            Assert.That(Others[1].Description, Does.Contain("differs only in upper and lower case from “Tent”"));
            Assert.That(Others[2].MessageText, Does.Contain("includeEquipment item 4 is empty"), "the validation's message at its item");
            Assert.That(Option(Trip.IncludeEquipment.Options.Where(O => O.IsVariant).ToList(), "Tent").IsChecked, Is.True);

            // an unrelated change keeps the list byte for byte, and filterMatching is not added
            Trip.DisplayName.Text = "Alpha later";
            Settle(Window);
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("displayName: Alpha weekends", "displayName: Alpha later")),
                Trip.Session.State + " " + string.Join(" | ", Trip.Session.Notices.Select(N => N.Severity + " " + N.Code + " " + N.Key + " " + N.Message)));
            Trip.Revert();

            // removing one keeps the others, with their comments (the rows shown after the revert)
            Trip.IncludeEquipment.OtherValues[0].RemoveCommand.Execute(null);
            Settle(Window);
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("  - Hovercraft # mine\n", "")));
            Assert.That(Trip.IncludeEquipment.OtherValues.Select(O => O.Label), Is.EqualTo(new[] { "TENT", "(empty item)", "“ Trailer” (with spaces at its start or end)" }));
        }

        [AvaloniaTest]
        public void AValueIsAddedExactlyAsTyped_NeverTwice()
        {
            string Text = Search("excludeCampsiteType: []\n");
            var (Window, _, Trip) = Open(Text);
            FilterSelectorField Exclude = Trip.ExcludeCampsiteType;

            Exclude.CustomText = " GROUP ";
            Exclude.AddCustom();
            Settle(Window);
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("excludeCampsiteType: []", "excludeCampsiteType: [' GROUP ']")));
            Assert.That(Exclude.CustomText, Is.Empty);

            Exclude.CustomText = " GROUP ";
            Exclude.AddCustom();
            Assert.That((Exclude.CustomIsError, Exclude.CustomMessage), Is.EqualTo((true, "The list already holds exactly this value.")));
            Exclude.CustomText = "";
            Exclude.AddCustom();
            Assert.That(Exclude.CustomMessage, Is.EqualTo("Type a value first."));
            // D71: a value of only spaces is refused like an empty one (it would match every or no campsite)
            string? Before = Trip.Session.Candidate;
            Exclude.CustomText = "   ";
            Exclude.AddCustom();
            Settle(Window);
            Assert.That((Exclude.CustomIsError, Exclude.CustomMessage, Trip.Session.Candidate), Is.EqualTo((true, "Type a value first.", Before)));

            // a value of the campground typed in is checked in the list
            Exclude.CustomText = "STANDARD ELECTRIC";
            Exclude.AddCustom();
            Settle(Window);
            Assert.That(Option(Exclude, "STANDARD ELECTRIC").IsChecked, Is.True);
            Assert.That(Exclude.CustomMessage, Is.EqualTo("Added; it is one of this campground's values and is checked above."));
        }

        [AvaloniaTest]
        public void ExactAndContains_SayWhatEachValueMatches()
        {
            var (Window, _, Trip) = Open(Search("excludeCampsiteType: [GROUP]\n"));
            ComboBox Matching = Part<ComboBox>(Window, "MatchingComboBox");
            Assert.That(Trip.Matching.Selected!.Label, Does.StartWith("Contains (not set in this file"));
            Assert.That(Trip.ExcludeCampsiteType.OtherValues.Single().Description,
                Is.EqualTo("Not one of this campground's campsite types; with Contains matching it matches one of them: “GROUP STANDARD NONELECTRIC”."));
            Assert.That(Option(Trip.IncludeCampsiteType, "STANDARD NONELECTRIC").AlsoText, Is.EqualTo("With Contains matching it also matches “GROUP STANDARD NONELECTRIC”."));
            Assert.That(Option(Trip.IncludeEquipment, "SMALL TENT").AlsoText, Is.Empty, "no other equipment name is part of it");

            Matching.SelectedItem = Trip.Matching.Choices.Single(C => C.Value is FilterMatchMode.Exact);
            Settle(Window);
            Assert.That(Trip.Session.Candidate, Does.EndWith("consecutiveDays: 1\nfilterMatching: Exact\n# end\n"));
            Assert.That(Trip.ExcludeCampsiteType.OtherValues.Single().Description, Is.EqualTo("Not among this campground's campsite types; it matches none of them with Exact matching."));
            Assert.That(Option(Trip.IncludeCampsiteType, "STANDARD NONELECTRIC").AlsoText, Is.Empty, "Exact matches the value alone");

            // back to "not set": no edit, nothing written
            Matching.SelectedItem = Trip.Matching.Choices[0];
            Settle(Window);
            Assert.That(Trip.IsDirty, Is.False);
        }

        [AvaloniaTest]
        public void Bravo_ContainsMatchingShowsWhatALongerValueAlsoMatches()
        {
            var (Window, _, Trip) = Open(Search("includeEquipment: [RV/MOTORHOME]\n", GuiTestEnvironment.CampgroundBravoID, "filterMatching: Contains\n"));
            Assert.That(Trip.Matching.Selected!.Value, Is.EqualTo(FilterMatchMode.Contains));
            Assert.That(Trip.Matching.Choices.Select(C => C.Value).OfType<FilterMatchMode>(), Is.EqualTo(new[] { FilterMatchMode.Exact, FilterMatchMode.Contains }), "no \"not set\" for a file that has it");
            Assert.That(Option(Trip.IncludeEquipment, "RV/MOTORHOME").AlsoText, Is.EqualTo("With Contains matching it also matches “RV”."));
            Assert.That(Option(Trip.IncludeEquipment, "RV/MOTORHOME").AccessibleName, Does.EndWith("With Contains matching it also matches “RV”."));
        }

        [AvaloniaTest]
        public void TheSameValueIncludedAndExcluded_IsAllowed_WithAWarning()
        {
            var (Window, _, Trip) = Open(Search("includeEquipment: [Tent, RV]\nexcludeEquipment: [Tent]\n"));
            Assert.That(Trip.ExcludeEquipment.ConflictText,
                Is.EqualTo("Warning: also in includeEquipment: “Tent”. A campsite that has it is left out, because an exclude list wins over an include list."));
            Assert.That(Part<TextBlock>(SelectorView(Window, "excludeEquipment"), "ConflictMessage").IsVisible, Is.True);
            Assert.That(Trip.IsDirty, Is.False, "never resolved by itself");

            Option(Trip.ExcludeEquipment.Options.Where(O => O.IsVariant).ToList(), "Tent").IsChecked = false;
            Settle(Window);
            Assert.That(Trip.ExcludeEquipment.ConflictText, Is.Null);
        }

        [AvaloniaTest]
        public void WithoutACampground_TheSelectorsSaySo_AndTypedValuesStillWork()
        {
            string Text = Search("includeEquipment: [Tent]\n", Campground: null);
            var (Window, _, Trip) = Open(Text);

            Assert.That(Trip.Selectors.All(S => S.Status == FilterChoicesStatus.NoCampground), Is.True);
            Assert.That(Trip.IncludeEquipment.StatusText, Is.EqualTo("Choose a campground to pick from its equipment types. Values typed below are kept either way."));
            Assert.That(Part<ListBox>(SelectorView(Window, "includeEquipment"), "ChoiceList").IsVisible, Is.False);
            Assert.That(Trip.IncludeEquipment.OtherValues.Single().Description, Is.EqualTo("Kept as written."));

            Trip.IncludeEquipment.CustomText = "RV";
            Trip.IncludeEquipment.AddCustom();
            Settle(Window);
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("includeEquipment: [Tent]", "includeEquipment: [Tent, RV]")));
        }

        [AvaloniaTest]
        public void ChangingTheCampground_ReadsItsValues_AndTheListStaysAsItIs()
        {
            string Text = Search("includeEquipment: [Tent, SMALL TENT]\n");
            var (Window, Searches, Trip) = Open(Text);
            int Before = Searches.ChoiceSource!.Loads;

            Trip.Campground.TypedId = GuiTestEnvironment.CampgroundBravoID;
            Trip.Campground.UseTypedId();
            WaitLoaded(Trip, Window);

            Assert.That(Searches.ChoiceSource.Loads, Is.EqualTo(Before + 1));
            Assert.That(Labels(Trip.IncludeEquipment), Is.EqualTo(new[] { "Horse [ ]", "RV [ ]", "RV/MOTORHOME [ ]" }));
            Assert.That(Trip.IncludeEquipment.OtherValues.Select(O => O.Label), Is.EqualTo(new[] { "Tent", "SMALL TENT" }), "never dropped");
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("campgroundID: '990001'", "campgroundID: '990002'")));

            // back to Alpha: its values are kept from before (the database is unchanged)
            Trip.Campground.TypedId = GuiTestEnvironment.CampgroundAlphaID;
            Trip.Campground.UseTypedId();
            WaitLoaded(Trip, Window);
            Assert.That(Searches.ChoiceSource.Loads, Is.EqualTo(Before + 1));
            Assert.That(Trip.IncludeEquipment.OtherValues, Is.Empty);
        }

        [AvaloniaTest]
        public void ARefreshedDatabase_WhileTheWindowIsOpen_IsReadAgain()
        {
            var (Window, Searches, Trip) = Open(Search("includeEquipment: []\n"));
            Assert.That(Trip.IncludeEquipment.Options.Select(O => O.Label), Does.Not.Contain("Kayak"));

            using (var Connection = new SqliteConnection("Data Source=" + In("RecreationDotOrg.db") + ";Pooling=False"))
            {
                Connection.Open();
                using var Command = Connection.CreateCommand();
                Command.CommandText = "INSERT INTO PermittedEquipmentEntries (EquipmentName, CampsiteID) VALUES ('Kayak', '9900011')";
                Command.ExecuteNonQuery();
            }
            File.SetLastWriteTimeUtc(In("RecreationDotOrg.db"), DateTime.UtcNow.AddMinutes(1));
            Searches.CheckForExternalChanges();
            WaitLoaded(Trip, Window);

            Assert.That(Trip.IncludeEquipment.Options.Select(O => O.Label), Does.Contain("Kayak"));
        }

        [AvaloniaTest]
        public void TheProductionSource_NeverReadsOnTheUiThread()
        {
            // the window's own source (FilterChoiceSource(ConfigPath)): while another connection holds the database exclusively,
            // showing a file of another campground returns at once and its values are read when the lock is released
            // this test's copy in rollback-journal mode, so an exclusive transaction keeps every reader out (in WAL mode
            // readers go on reading)
            using (var Journal = new SqliteConnection("Data Source=" + In("RecreationDotOrg.db") + ";Pooling=False"))
            {
                Journal.Open();
                using var Mode = Journal.CreateCommand();
                Mode.CommandText = "PRAGMA journal_mode=DELETE";
                Assert.That(Mode.ExecuteScalar(), Is.EqualTo("delete"));
            }
            var (Window, Searches, _) = Open(Search("includeEquipment: []\n"));
            // a file of a campground whose values were never read, listed after the start
            File.WriteAllText(In("Zulu.yaml"), Search("includeEquipment: []\n", GuiTestEnvironment.CampgroundBravoID));
            Searches.CheckForExternalChanges();
            SearchFileViewModel Bravo = Searches.Files.Single(F => F.FileName == "Zulu.yaml");
            using (var Lock = new SqliteConnection("Data Source=" + In("RecreationDotOrg.db") + ";Pooling=False"))
            {
                Lock.Open();
                using (var Begin = Lock.CreateCommand())
                {
                    Begin.CommandText = "BEGIN EXCLUSIVE";
                    Begin.ExecuteNonQuery();
                }
                var Clock = System.Diagnostics.Stopwatch.StartNew();
                Searches.SelectedFile = Bravo;
                Dispatcher.UIThread.RunJobs();
                // a UI thread that waited for the locked database would wait for the 30 s command timeout (the lock is held
                // until the end of the test); 10 s leaves room for a slow hosted runner
                Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "the UI thread did not wait for the database");
                Assert.That(Bravo.IncludeEquipment.Status, Is.EqualTo(FilterChoicesStatus.Loading));
                Thread.Sleep(300);
                Dispatcher.UIThread.RunJobs();
                Assert.That(Bravo.IncludeEquipment.Status, Is.EqualTo(FilterChoicesStatus.Loading), "still waiting for the lock, on the worker");
            }
            WaitLoaded(Bravo, Window);
            Assert.That(Bravo.IncludeEquipment.Options.Select(O => O.Label), Is.EqualTo(new[] { "Horse", "RV", "RV/MOTORHOME" }));
        }

        [AvaloniaTest]
        public void Keyboard_TheListIsOneTabStop_ArrowsMove_AndSpaceChecks()
        {
            var (Window, _, Trip) = Open(Search("includeEquipment: []\n"));
            FilterSelectorView View = SelectorView(Window, "includeEquipment");
            ListBox List = Part<ListBox>(View, "ChoiceList");
            List.SelectedIndex = 0;
            Settle(Window);
            ListBoxItem First = List.GetVisualDescendants().OfType<ListBoxItem>().First();
            First.Focus();
            Settle(Window);

            Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Settle(Window);
            Assert.That((Focused(Window) as ListBoxItem)?.DataContext, Is.SameAs(Trip.IncludeEquipment.Options[1]), "Down moves to the next value");
            Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Settle(Window);
            Assert.That(Trip.IncludeEquipment.Options[1].IsChecked, Is.True, "Space checks the focused value");
            Assert.That(Trip.IsDirty, Is.True);
            Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Settle(Window);
            Assert.That(Trip.IncludeEquipment.Options[1].IsChecked, Is.False, "and unchecks it");

            Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Settle(Window);
            IInputElement? After = Focused(Window);
            Assert.That(After is ListBoxItem Item && List.GetVisualDescendants().Contains(Item), Is.False, "Tab leaves the list instead of visiting every value");
            Assert.That(After, Is.SameAs(Part<TextBox>(View, "CustomBox")));
            Window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            Settle(Window);
            Assert.That(Focused(Window) is ListBoxItem Back && List.GetVisualDescendants().Contains(Back), Is.True, "Shift+Tab returns into the list");
        }

        [AvaloniaTest]
        public void ALockedList_IsShownButNotEditable()
        {
            var (Window, _, Trip) = Open(Search("includeEquipment:\n  - Tent\n  -\n"));
            Assert.That(Trip.IncludeEquipment.IsEditable, Is.False);
            Assert.That(Trip.IncludeEquipment.LockReason, Does.Contain("can't edit item by item"));
            Assert.That(Part<ListBox>(SelectorView(Window, "includeEquipment"), "ChoiceList").IsEnabled, Is.False);
            Trip.IncludeEquipment.Toggle(Trip.IncludeEquipment.Options[0]);
            Trip.IncludeEquipment.CustomText = "RV";
            Trip.IncludeEquipment.AddCustom();
            Assert.That(Trip.IsDirty, Is.False, "nothing changes a locked list");
            Assert.That(Trip.ExcludeEquipment.IsEditable, Is.True, "the other lists stay editable");
        }

        [AvaloniaTest]
        public void ANewSearch_ShowsExact_AndAnInvalidValueIsKeptUntilChosen()
        {
            var (Window, Searches, _) = Open(Search("includeEquipment: []\n", Extra: "filterMatching: 1\n"));
            SearchFileViewModel Trip = Searches.Files.Single(F => F.FileName == "Trip.yaml");
            Assert.That(Trip.Matching.Selected!.Label, Is.EqualTo("Not Exact or Contains in the file (kept as it is until you choose)"));
            Assert.That(Trip.Matching.MessageText, Does.StartWith("Error: filterMatching must be Exact or Contains"));
            Assert.That(Trip.CanSave, Is.False);
            // "1" loads as Contains; choosing Contains writes it out over the refused text
            Searches.SelectedFile = Trip;
            Part<ComboBox>(Window, "MatchingComboBox").SelectedItem = Trip.Matching.Choices.Single(C => C.Value is FilterMatchMode.Contains);
            Settle(Window);
            Assert.That(Trip.Session.Candidate, Does.Contain("\nfilterMatching: Contains\n").And.Not.Contain("filterMatching: 1"));
            Assert.That(Trip.CanSave, Is.True);
            Trip.Revert();

            Searches.NewSearchName = "Fresh";
            Searches.NewSearch();
            Settle(Window);
            SearchFileViewModel Fresh = Searches.SelectedFile!;
            Assert.That(Fresh.FileName, Is.EqualTo("Fresh.yaml"));
            Assert.That(Fresh.Matching.Selected!.Value, Is.EqualTo(FilterMatchMode.Exact));
            Assert.That(Fresh.Matching.Choices.Select(C => C.Value).OfType<FilterMatchMode>().Count(), Is.EqualTo(2));
        }

        // ---- the worker thread and stale answers (a choice source the test controls) ----

        private static CampgroundFilterChoices ChoicesOf(string Facility, params string[] Equipment) =>
            new CampgroundFilterChoices(Facility, Equipment.Length, Equipment.Select(E => new FilterChoice(E, 1)), Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>());

        private SearchFileViewModel Editor(FilterChoiceSource Source, string Text)
        {
            File.WriteAllText(In("Trip.yaml"), Text);
            var Searches = new SearchesViewModel(Folder, null, () => new List<CampgroundPickerEntry>
            {
                new CampgroundPickerEntry(new CampgroundChoice(GuiTestEnvironment.CampgroundAlphaID, "ALPHA", null, null), null),
                new CampgroundPickerEntry(new CampgroundChoice(GuiTestEnvironment.CampgroundBravoID, "BRAVO", null, null), null),
            }, () => Source);
            Searches.Show();
            return Searches.Files.Single(F => F.FileName == "Trip.yaml");
        }

        [AvaloniaTest]
        public void TheValues_AreReadOnAWorkerThread_WhileTheUiThreadKeepsRunning()
        {
            int UiThread = Environment.CurrentManagedThreadId;
            int LoadThread = 0;
            using var Gate = new ManualResetEventSlim(false);
            Dispatcher Ui = Dispatcher.UIThread;
            var Source = new FilterChoiceSource(Id =>
            {
                LoadThread = Environment.CurrentManagedThreadId;
                Gate.Wait(TimeSpan.FromSeconds(10));
                return ChoicesOf(Id, "Tent");
            }, () => "db", Job => Ui.Post(Job), Job => Task.Run(Job));

            SearchFileViewModel Trip = Editor(Source, Search("includeEquipment: []\n"));
            Assert.That(Trip.IncludeEquipment.Status, Is.EqualTo(FilterChoicesStatus.Loading));
            Assert.That(Trip.IncludeEquipment.StatusText, Is.EqualTo("Reading this campground's equipment types…"));
            bool Ran = false;
            Ui.Post(() => Ran = true);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Ran, Is.True, "the UI thread runs while the values are read");

            Gate.Set();
            WaitUntil(() => Trip.IncludeEquipment.Status == FilterChoicesStatus.Loaded, () => "loaded");
            Assert.That(LoadThread, Is.Not.EqualTo(UiThread).And.Not.Zero, "never on the UI thread");
            Assert.That(Trip.IncludeEquipment.Options.Single().Label, Is.EqualTo("Tent"));
        }

        [AvaloniaTest]
        public void AnOlderAnswer_NeverReplacesTheValuesOfANewerCampground()
        {
            var Jobs = new List<Action>();
            var Posted = new List<Action>();
            var Source = new FilterChoiceSource(Id => Id == GuiTestEnvironment.CampgroundAlphaID ? ChoicesOf(Id, "Alpha tent") : ChoicesOf(Id, "Bravo horse"),
                () => "db", Job => Posted.Add(Job), Job => { Jobs.Add(Job); return Task.CompletedTask; });
            SearchFileViewModel Trip = Editor(Source, Search("includeEquipment: []\n"));
            Assert.That(Jobs, Has.Count.EqualTo(1), "Alpha is being read");

            Trip.Campground.TypedId = GuiTestEnvironment.CampgroundBravoID;
            Trip.Campground.UseTypedId();
            Assert.That(Jobs, Has.Count.EqualTo(2), "then Bravo");

            // Bravo's answer arrives first, Alpha's (older) after it
            Jobs[1]();
            Posted.Last()();
            Assert.That(Trip.IncludeEquipment.Options.Single().Label, Is.EqualTo("Bravo horse"));
            Jobs[0]();
            Posted.Last()();
            Assert.That(Trip.IncludeEquipment.Options.Single().Label, Is.EqualTo("Bravo horse"), "the older answer is ignored");
            Assert.That(Trip.IncludeEquipment.Status, Is.EqualTo(FilterChoicesStatus.Loaded));
        }

        [AvaloniaTest]
        public void AFailure_IsShown_AndRetryReadsAgain()
        {
            bool Fail = true;
            Dispatcher Ui = Dispatcher.UIThread;
            var Source = new FilterChoiceSource(Id => Fail ? throw new InvalidOperationException("no database") : ChoicesOf(Id, "Tent"),
                () => "db", Job => Ui.Post(Job), Job => Task.Run(Job));
            SearchFileViewModel Trip = Editor(Source, Search("includeEquipment: [Tent]\n"));
            WaitUntil(() => Trip.IncludeEquipment.Status == FilterChoicesStatus.Failed, () => "failed");

            Assert.That(Trip.IncludeEquipment.StatusText, Is.EqualTo("Campertron couldn't read this campground's values from its campground data."));
            Assert.That(Trip.HasChoicesProblem, Is.True);
            Assert.That(Trip.IncludeEquipment.OtherValues.Single().Label, Is.EqualTo("Tent"), "the list is kept");
            Trip.DisplayName.Text = "Edited";
            Assert.That(Source.Loads, Is.EqualTo(1), "an edit does not read again");

            Fail = false;
            Trip.RetryChoicesCommand.Execute(null);
            WaitUntil(() => Trip.IncludeEquipment.Status == FilterChoicesStatus.Loaded, () => "loaded after Retry");
            Assert.That(Trip.HasChoicesProblem, Is.False);
            Assert.That(Trip.IncludeEquipment.Options.Single().IsChecked, Is.True);
        }

        [AvaloniaTest]
        public void ALongList_CanBeNarrowed_AndTheShownValuesCheckedOrClearedTogether()
        {
            Dispatcher Ui = Dispatcher.UIThread;
            string[] Many = Enumerable.Range(1, 20).Select(i => (i % 2 == 0 ? "Even " : "Odd ") + i.ToString("00")).ToArray();
            var Source = new FilterChoiceSource(Id => ChoicesOf(Id, Many), () => "db", Job => Ui.Post(Job), Job => Task.Run(Job));
            string Text = Search("includeEquipment: [Odd 01, Mine]\n");
            SearchFileViewModel Trip = Editor(Source, Text);
            WaitUntil(() => Trip.IncludeEquipment.Status == FilterChoicesStatus.Loaded, () => "loaded");
            FilterSelectorField Include = Trip.IncludeEquipment;
            Assert.That((Include.CanFind, Include.ShowBulkActions, Include.ListMaxHeight), Is.EqualTo((true, true, 320d)), "20 values: find, bulk actions, a bounded list");
            Assert.That(Trip.IncludeCampsiteType.CanFind, Is.False, "a short list is shown whole, without a find box");

            Include.FindText = "even 1";
            Assert.That(Include.Options.Select(O => O.Label), Is.EqualTo(new[] { "Even 10", "Even 12", "Even 14", "Even 16", "Even 18" }), "every word, ignoring case");
            Assert.That(Include.StatusText, Is.EqualTo("20 equipment types at this campground, 1 checked. 5 shown."));
            Include.SelectShown();
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("[Odd 01, Mine]", "[Odd 01, Mine, Even 10, Even 12, Even 14, Even 16, Even 18]")));

            Include.FindText = "";
            Assert.That(Include.Options.Count(O => O.IsChecked == true), Is.EqualTo(6));
            Include.FindText = "odd";
            Include.ClearShown();
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Text.Replace("[Odd 01, Mine]", "[Mine, Even 10, Even 12, Even 14, Even 16, Even 18]")),
                "only the shown values were cleared; the value the campground does not have stays");
        }

        [AvaloniaTest]
        public void AListedFile_ReadsNothing_UntilItIsShown()
        {
            int Loads = 0;
            Dispatcher Ui = Dispatcher.UIThread;
            var Source = new FilterChoiceSource(Id => { Interlocked.Increment(ref Loads); return ChoicesOf(Id, "Tent"); }, () => "db", Job => Ui.Post(Job), Job => Task.Run(Job));
            File.WriteAllText(In("A.yaml"), Search("includeEquipment: []\n"));
            File.WriteAllText(In("B.yaml"), Search("includeEquipment: []\n", GuiTestEnvironment.CampgroundBravoID));
            SearchFileViewModel Trip = Editor(Source, Search("includeEquipment: []\n"));
            WaitUntil(() => Volatile.Read(ref Loads) >= 1, () => "the first file's values");
            Thread.Sleep(50);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Volatile.Read(ref Loads), Is.EqualTo(1), "only the shown file (the first of three) reads its campground's values");
            Assert.That(Trip.IncludeEquipment.Status, Is.EqualTo(FilterChoicesStatus.NoCampground), "a listed file's selectors wait until it is shown");
        }
    }
}
