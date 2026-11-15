using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The Searches tab (CM6, decision D44): the search files of the settings folder and their editor in the real window
    // on the headless platform. Each test has its own settings folder (with a copy of the fixture database for the
    // campground picker) and a window whose startup loader returns it. Nothing here may search availability, show
    // results or touch another file than the one saved.
    public class SearchEditorTests
    {
        private const int SearchesTab = 4;

        private const string AlphaText =
            "# Alpha search\n" +
            "displayName: Alpha weekends   # shown in the results\n" +
            "autoRun: false\n" +
            "campgroundID: '" + GuiTestEnvironment.CampgroundAlphaID + "'\n" +
            "totalHumans: 2\n" +
            "searchBy: SpecificDates\n" +
            "searchValue: 0\n" +
            "searchValueDates:\n" +
            "  # the trip\n" +
            "  - 2027-07-02 # start\n" +
            "showMonday: false\n" +
            "showTuesday: false\n" +
            "showWednesday: false\n" +
            "showThursday: false\n" +
            "showFriday: true\n" +
            "showSaturday: true\n" +
            "showSunday: true\n" +
            "includeEquipment:\n" +
            "  - Tent # small\n" +
            "excludeEquipment: []\n" +
            "includeSites:\n" +
            "excludeSites: []\n" +
            "includeAttributes: []\n" +
            "excludeAttributes: []\n" +
            "includeCampsiteType: []\n" +
            "excludeCampsiteType: []\n" +
            "consecutiveDays: 1\n" +
            "# end\n";

        private string Folder = string.Empty;
        private string In(string Name) => Path.Join(Folder, Name);

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "searches-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(In("General.yaml"), "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            File.WriteAllText(In("Email.yaml"), "smtpServer: ''\nsmtpPort: 0\n");
            File.WriteAllText(In("Alpha.yaml"), AlphaText);
        }

        [TearDown]
        public void DeleteFolder()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, true);
            }
        }

        // the real window with this test's settings folder, on the Searches tab
        private (MainWindow Window, MainWindowViewModel ViewModel, SearchesViewModel Searches) ShowSearches()
        {
            File.Copy(GuiTestEnvironment.DbFile, In("RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = SearchesTab;
            Settle(Window);
            return (Window, ViewModel, ViewModel.Searches!);
        }

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static Control View(MainWindow Window) => Named<SearchesView>(Window, "SearchesContent");

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().SingleOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found");

        private static T Find<T>(MainWindow Window, string Name) where T : Control => In<T>(View(Window), Name);

        // the list field view of one setting (the dates and the filters share a template)
        private static ListFieldView List(MainWindow Window, string Key) =>
            View(Window).GetVisualDescendants().OfType<ListFieldView>().Single(v => (v.DataContext as ListSettingField)?.Key == Key);

        private static void Type(TextBox Box, string Text)
        {
            Box.Text = Text;
            Dispatcher.UIThread.RunJobs();
        }

        private static void Click(Button Button)
        {
            Assert.That(Button.IsEffectivelyEnabled, Is.True, Button.Name + " is disabled");
            Button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        // ---- the list of search files ----

        // 1, 2, 3, 4, 5, 51, 59: the console's search files, in order, each with its name, search, state and campground;
        // the files and the campground data are read when the tab is first shown, with one query for every campground
        [AvaloniaTest]
        public void TheSearchesTab_ListsTheSearchFiles_WithTheirState()
        {
            File.WriteAllText(In("beta.yaml"), AlphaText.Replace("autoRun: false", "autoRun: true").Replace("Alpha weekends", "Beta"));
            File.WriteAllText(In("Broken.yaml"), "displayName: [unclosed\n");
            File.WriteAllText(In("Alpha.yaml.bak"), "displayName: old\n");
            File.WriteAllText(In("Alpha.yaml.0123456789abcdef0123456789abcdef.tmp"), "displayName: temp\n");
            File.WriteAllText(In("other.yml"), "displayName: yml\n");
            File.Copy(GuiTestEnvironment.DbFile, In("RecreationDotOrg.db"), overwrite: true);
            using var Queries = new QueryCounter(AllThreads: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Settle(Window);
            SearchesViewModel Searches = ViewModel.Searches!;
            Assert.That(Searches.IsLoaded, Is.False, "nothing is read before the tab is shown");
            Queries.TakeCount();

            Tabs(Window).SelectedIndex = SearchesTab;
            Settle(Window);

            Assert.That(Queries.TakeCount(), Is.EqualTo(1), "one query for every campground, none per file");
            Assert.That(Searches.Files.Select(F => F.FileName), Is.EqualTo(new[] { "Alpha.yaml", "beta.yaml", "Broken.yaml" }));
            Assert.That(Searches.SelectedFile, Is.SameAs(Searches.Files[0]));
            SearchFileViewModel Alpha = Searches.Files[0];
            Assert.That(Alpha.ListDetails, Is.EqualTo("Alpha weekends \u00B7 Disabled \u00B7 " + GuiTestEnvironment.CampgroundAlpha + " \u2014 ALPHAVILLE, UT \u2014 ID 990001"));
            Assert.That(Alpha.ListStatus, Does.StartWith("No problems").Or.StartWith("Has warnings"));
            Assert.That(Searches.Files[1].ListDetails, Does.Contain("Enabled"));
            Assert.That(Searches.Files[2].ListStatus, Is.EqualTo("Read-only"));
            Assert.That(Searches.Files[2].ListStatusIsError, Is.True);
            ListBox Files = Find<ListBox>(Window, "SearchFilesList");
            List<string?> Names = Files.GetVisualDescendants().OfType<ListBoxItem>().Select(i => AutomationProperties.GetName(i)).ToList();
            Assert.That(Names, Has.Count.EqualTo(3));
            Assert.That(Names[0], Does.StartWith("Alpha.yaml, Alpha weekends"));
            Assert.That(Searches.HasFolderNotice, Is.False, "beta.yaml runs");

            // switching files reads no campground again
            Searches.SelectedFile = Searches.Files[1];
            Settle(Window);
            Assert.That(Queries.TakeCount(), Is.Zero);
            Window.Close();
        }

        // 5: a file that can't be edited is listed and opens read-only with its reason; nothing is offered that writes it
        [AvaloniaTest]
        public void AFileThatCantBeEdited_OpensReadOnly_WithItsReason()
        {
            File.WriteAllText(In("Alpha.yaml"), AlphaText + "extraKey: 1\n");
            var (Window, _, Searches) = ShowSearches();
            SearchFileViewModel Alpha = Searches.Files.Single();

            Assert.That(Alpha.IsEditable, Is.False);
            Assert.That(Alpha.StateText, Does.Contain("can't be edited here"));
            Assert.That(In<Border>(Find<SearchFileView>(Window, "SearchEditor"), "StateCard").IsVisible, Is.True);
            Assert.That(Find<CheckBox>(Window, "AutoRunCheckBox").IsEffectivelyEnabled, Is.False);
            Assert.That(Find<Button>(Window, "SaveAsButton").IsEffectivelyEnabled, Is.False);
            Assert.That(Alpha.FileNotices.Select(N => N.Text), Has.Some.Contains("extraKey is not a setting Campertron knows"));
            Window.Close();
        }

        // ---- editing and saving ----

        // 9, 10, 11, 13, 16, 27, 28: the form edits the file: the preview shows exactly the changed lines, the save keeps
        // comments and every other line, the list shows the new state
        [AvaloniaTest]
        public void TheForm_EditsTheSearch_AndTheSaveIsExact()
        {
            var (Window, _, Searches) = ShowSearches();
            SearchFileViewModel Alpha = Searches.SelectedFile!;

            Type(Find<TextBox>(Window, "DisplayNameBox"), "Alpha in July");
            Find<CheckBox>(Window, "AutoRunCheckBox").IsChecked = true;
            Find<ComboBox>(Window, "SearchByComboBox").SelectedItem = Alpha.SearchBy.Choices.Single(C => C.Value.Equals(SearchTypes.StartEndDate));
            Click(In<Button>(List(Window, "searchValueDates"), "AddButton"));
            Settle(Window);
            Type(List(Window, "searchValueDates").GetVisualDescendants().OfType<TextBox>().Last(b => b.Name == "RowBox"), "2027-07-05");
            Type(Find<TextBox>(Window, "TotalHumansBox"), "4");
            Alpha.Weekdays.Single(D => D.Key == "showThursday").IsChecked = true;
            Settle(Window);

            Assert.That(Alpha.PreviewText.Split('\n'), Is.EqualTo(new[]
            {
                "  # Alpha search",
                "- displayName: Alpha weekends   # shown in the results",
                "- autoRun: false",
                "+ displayName: Alpha in July   # shown in the results",
                "+ autoRun: true",
                "  campgroundID: '990001'",
                "- totalHumans: 2",
                "- searchBy: SpecificDates",
                "+ totalHumans: 4",
                "+ searchBy: StartEndDate",
                "  searchValue: 0",
                "  \u2026",
                "    - 2027-07-02 # start",
                // a new item that would read as a date or another typed value is quoted (it is a text setting)
                "+   - '2027-07-05'",
                "  showMonday: false",
                "  showTuesday: false",
                "  showWednesday: false",
                "- showThursday: false",
                "+ showThursday: true",
                "  showFriday: true",
            }));
            Assert.That(Alpha.PreviewSummary, Is.EqualTo("Changes: displayName, autoRun, totalHumans, searchBy, searchValueDates, showThursday"));
            Assert.That(Alpha.PreviewNote, Does.Not.Contain("password"));
            Assert.That(Alpha.ListStatus, Does.EndWith("unsaved changes"));

            Click(In<Button>(Find<SearchFileView>(Window, "SearchEditor"), "SaveButton"));

            string Expected = AlphaText.Replace("displayName: Alpha weekends", "displayName: Alpha in July").Replace("autoRun: false", "autoRun: true")
                .Replace("totalHumans: 2", "totalHumans: 4").Replace("searchBy: SpecificDates", "searchBy: StartEndDate")
                .Replace("  - 2027-07-02 # start\n", "  - 2027-07-02 # start\n  - '2027-07-05'\n").Replace("showThursday: false", "showThursday: true");
            Assert.That(File.ReadAllText(In("Alpha.yaml")), Is.EqualTo(Expected));
            Assert.That(File.ReadAllText(In("Alpha.yaml.bak")), Is.EqualTo(AlphaText));
            Assert.That(Alpha.StatusText, Is.EqualTo("Saved Alpha.yaml. The previous version is in Alpha.yaml.bak."));
            Assert.That(Alpha.IsDirty, Is.False);
            Assert.That(Alpha.ListDetails, Does.StartWith("Alpha in July \u00B7 Enabled"));
            Assert.That(Find<TextBlock>(Window, "SearchTitle").Text, Is.EqualTo("Alpha in July"));
            Window.Close();
        }

        // 24, 25, 22, 23: problems are shown at their field and item; errors (and text that is no number) block the save,
        // warnings don't
        [AvaloniaTest]
        public void Problems_ShowAtTheirFields_AndOnlyErrorsBlockTheSave()
        {
            var (Window, _, Searches) = ShowSearches();
            SearchFileViewModel Alpha = Searches.SelectedFile!;
            Button Save = In<Button>(Find<SearchFileView>(Window, "SearchEditor"), "SaveButton");

            Type(Find<TextBox>(Window, "ConsecutiveDaysBox"), "two");
            Assert.That(Alpha.ConsecutiveDays.MessageText, Is.EqualTo("Error: Enter a whole number of nights, such as 1."));
            Assert.That(Save.IsEffectivelyEnabled, Is.False);
            Type(Find<TextBox>(Window, "ConsecutiveDaysBox"), "0");
            Assert.That(Alpha.ConsecutiveDays.MessageText, Does.StartWith("Warning: consecutiveDays is below 1"));
            Assert.That(Save.IsEffectivelyEnabled, Is.True, "a warning does not block");

            // searchBy Until reads its date (an unreadable SpecificDates date is only skipped): enabled, an error at the item
            Find<CheckBox>(Window, "AutoRunCheckBox").IsChecked = true;
            Find<ComboBox>(Window, "SearchByComboBox").SelectedItem = Alpha.SearchBy.Choices.Single(C => C.Value.Equals(SearchTypes.Until));
            Type(List(Window, "searchValueDates").GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "RowBox"), "not a date");
            Settle(Window);
            ListRow Row = Alpha.SearchValueDates.Rows.Single();
            Assert.That(Row.MessageText, Is.EqualTo("Error: searchValueDates item 1 is not a date."));
            Assert.That(Row.HelpText, Does.StartWith("Error:"));
            Assert.That(Save.IsEffectivelyEnabled, Is.False);
            Assert.That(Alpha.ListStatus, Does.StartWith("Has errors"));

            foreach (BoolSettingField Day in Alpha.Weekdays)
            {
                Day.IsChecked = false;
            }
            Settle(Window);
            Assert.That(Find<TextBlock>(Window, "WeekdaysMessage").Text, Does.StartWith("Warning: No day of the week is enabled"));
            Assert.That(Alpha.FileNotices.Select(N => N.Message), Has.None.Contains("No day of the week"), "shown with the days only");
            Assert.That(File.ReadAllText(In("Alpha.yaml")), Is.EqualTo(AlphaText));
            Window.Close();
        }

        // revert and reload
        [AvaloniaTest]
        public void RevertAndReload_ShowTheFileAgain()
        {
            var (Window, _, Searches) = ShowSearches();
            SearchFileViewModel Alpha = Searches.SelectedFile!;
            Control Editor = Find<SearchFileView>(Window, "SearchEditor");
            Type(Find<TextBox>(Window, "DisplayNameBox"), "Changed");
            Assert.That(In<Button>(Editor, "ReloadButton").IsEffectivelyEnabled, Is.False, "save or revert first");

            Click(In<Button>(Editor, "RevertButton"));
            Assert.That(Find<TextBox>(Window, "DisplayNameBox").Text, Is.EqualTo("Alpha weekends"));
            Assert.That(Alpha.IsDirty, Is.False);

            File.WriteAllText(In("Alpha.yaml"), AlphaText.Replace("totalHumans: 2", "totalHumans: 6"));
            Click(In<Button>(Editor, "ReloadButton"));
            Assert.That(Find<TextBox>(Window, "TotalHumansBox").Text, Is.EqualTo("6"));
            Window.Close();
        }

        // ---- the campground ----

        // 43, 44, 45, 50, 18: every campground can be chosen: duplicates told apart, the unnamed and the location-less ones,
        // also one without a park or address (TD-53); the choice is the exact FacilityID
        [AvaloniaTest]
        public void ThePicker_ChoosesAnyCampground_ByItsFacilityID()
        {
            var (Window, _, Searches) = ShowSearches();
            SearchFileViewModel Alpha = Searches.SelectedFile!;
            CampgroundSettingField Picker = Alpha.Campground;
            Assert.That(Find<SelectableTextBlock>(Window, "CurrentCampgroundText").Text, Is.EqualTo(GuiTestEnvironment.CampgroundAlpha + " \u2014 ALPHAVILLE, UT \u2014 ID 990001 \u2014 park " + GuiTestEnvironment.ParkOne));
            Assert.That(Picker.Matches.Select(M => M.FacilityID), Does.Contain(GuiTestEnvironment.OrphanID).And.Contain(GuiTestEnvironment.BlankNameID)
                .And.Contain(GuiTestEnvironment.MailingOnlyID).And.Not.Contain(GuiTestEnvironment.OtherFacilityID));

            Type(Find<TextBox>(Window, "CampgroundFilterBox"), "twin");
            Assert.That(Picker.Matches.Select(M => M.FacilityID), Is.EquivalentTo(new[] { GuiTestEnvironment.TwinUtahID, GuiTestEnvironment.TwinColoradoID, GuiTestEnvironment.TwinSameLocationID }));
            Assert.That(Picker.Matches.Select(M => M.Label).Distinct().Count(), Is.EqualTo(3), "duplicate names have different labels");
            Assert.That(Find<TextBlock>(Window, "CampgroundMatchSummary").Text, Does.StartWith("3 of "));
            Settle(Window);
            List<string?> Items = Find<ListBox>(Window, "CampgroundList").GetVisualDescendants().OfType<ListBoxItem>().Select(i => AutomationProperties.GetName(i)).ToList();
            // the realized entries (the list is virtualized) are read as their whole label
            Assert.That(Items, Is.Not.Empty);
            Assert.That(Items, Has.All.Contains("ID 9900").And.All.Contains(GuiTestEnvironment.TwinName));

            Assert.That(Find<Button>(Window, "UseCampgroundButton").IsEffectivelyEnabled, Is.False, "nothing selected");
            Find<ListBox>(Window, "CampgroundList").SelectedItem = Picker.Matches.Single(M => M.FacilityID == GuiTestEnvironment.TwinSameLocationID);
            Settle(Window);
            Click(Find<Button>(Window, "UseCampgroundButton"));
            Assert.That(Picker.Id, Is.EqualTo(GuiTestEnvironment.TwinSameLocationID));
            Assert.That(Alpha.Session().Value("campgroundID"), Is.EqualTo(GuiTestEnvironment.TwinSameLocationID));

            Type(Find<TextBox>(Window, "CampgroundFilterBox"), "orphan");
            Assert.That(Picker.Matches.Single().FacilityID, Is.EqualTo(GuiTestEnvironment.OrphanID));
            Assert.That(Picker.Matches.Single().Choice.Location, Is.Null);
            Assert.That(Picker.Matches.Single().Park, Is.Null);
            Type(Find<TextBox>(Window, "CampgroundFilterBox"), "unnamed");
            Assert.That(Picker.Matches.Select(M => M.FacilityID), Is.EquivalentTo(new[] { GuiTestEnvironment.BlankNameID, GuiTestEnvironment.WhitespaceNameID }));
            Type(Find<TextBox>(Window, "CampgroundFilterBox"), GuiTestEnvironment.LeadingZeroID);
            Assert.That(Picker.Matches.First().FacilityID, Is.EqualTo(GuiTestEnvironment.LeadingZeroID));
            Find<ListBox>(Window, "CampgroundList").SelectedItem = Picker.Matches.First();
            Click(Find<Button>(Window, "UseCampgroundButton"));

            Click(In<Button>(Find<SearchFileView>(Window, "SearchEditor"), "SaveButton"));
            Assert.That(File.ReadAllText(In("Alpha.yaml")), Is.EqualTo(AlphaText.Replace("campgroundID: '990001'", "campgroundID: '0990010'")));
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(File.ReadAllText(In("Alpha.yaml"))).CampgroundID, Is.EqualTo("0990010"));
            Window.Close();
        }

        // 46, 47, 48, 49, 19: a typed ID is used only if a reservable, enabled campground has exactly that ID
        [AvaloniaTest]
        public void ATypedID_IsUsedOnlyIfItIsExactlyACampground()
        {
            var (Window, _, Searches) = ShowSearches();
            SearchFileViewModel Alpha = Searches.SelectedFile!;
            TextBox Box = Find<TextBox>(Window, "CampgroundIdBox");
            Button Use = Find<Button>(Window, "UseCampgroundIdButton");
            foreach ((string Typed, string Message) in new[]
            {
                ("", "Enter a campground ID."),
                ("   ", "Enter a campground ID."),
                ("99001", "No reservable, enabled campground in Campertron's campground data has exactly this ID."),
                ("9900013", "No reservable, enabled campground in Campertron's campground data has exactly this ID."),
                ("990010", "No reservable, enabled campground in Campertron's campground data has exactly this ID."),
                (" 990013", "Remove the spaces before or after the ID. No reservable, enabled campground in Campertron's campground data has exactly this ID."),
                (GuiTestEnvironment.OtherFacilityID, "No reservable, enabled campground in Campertron's campground data has exactly this ID."),
                (GuiTestEnvironment.TwinName, "No reservable, enabled campground in Campertron's campground data has exactly this ID."),
            })
            {
                Type(Box, Typed);
                Click(Use);
                Assert.That(Alpha.Campground.TypedIdMessage, Is.EqualTo(Message), Typed);
                Assert.That(Alpha.Campground.TypedIdIsError, Is.True, Typed);
                Assert.That(Alpha.Session().IsChanged("campgroundID"), Is.False, Typed);
                Assert.That(Find<TextBlock>(Window, "CampgroundIdMessage").Text, Is.EqualTo(Message));
            }

            Type(Box, GuiTestEnvironment.OrphanID);
            Click(Use);
            Assert.That(Alpha.Campground.TypedIdIsError, Is.False);
            Assert.That(Alpha.Campground.TypedIdMessage, Is.EqualTo("Chosen: " + GuiTestEnvironment.OrphanName + " \u2014 ID " + GuiTestEnvironment.OrphanID + "."));
            Assert.That(Alpha.Session().Value("campgroundID"), Is.EqualTo(GuiTestEnvironment.OrphanID));
            Assert.That(Alpha.Session().Notices.Where(N => N.Code == "CampgroundNotFound"), Is.Empty);
            Click(In<Button>(Find<SearchFileView>(Window, "SearchEditor"), "SaveButton"));
            Assert.That(File.ReadAllText(In("Alpha.yaml")), Does.Contain("campgroundID: '990013'\n"));
            Window.Close();
        }

        // ---- new files ----

        // 34, 35, 36, 38, 41, 59: New search creates a disabled search from the example, opens it, and refuses unsafe or
        // used names without writing anything
        [AvaloniaTest]
        public void NewSearch_CreatesADisabledSearch_AndRefusesUnsafeNames()
        {
            var (Window, _, Searches) = ShowSearches();
            Assert.That(Searches.FolderNoticeText, Does.StartWith("Warning: No usable search file has autoRun: true"));
            string[] Before = Directory.GetFiles(Folder).Select(Path.GetFileName).OrderBy(n => n).ToArray()!;
            foreach (string Name in new[] { "../outside", "alpha", "General", "notes.yml", "CON", "" })
            {
                Type(Find<TextBox>(Window, "NewSearchNameBox"), Name);
                Click(Find<Button>(Window, "NewSearchButton"));
                Assert.That(Searches.NewSearchIsError, Is.True, Name);
                Assert.That(Find<TextBlock>(Window, "NewSearchMessage").Text, Does.EndWith("Nothing was written."), Name);
            }
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName).OrderBy(n => n), Is.EqualTo(Before));
            Assert.That(Directory.GetDirectories(Folder), Is.Empty);

            Type(Find<TextBox>(Window, "NewSearchNameBox"), "Weekend trip");
            Click(Find<Button>(Window, "NewSearchButton"));

            Assert.That(Searches.NewSearchIsError, Is.False, Searches.NewSearchMessage);
            Assert.That(Searches.Files.Select(F => F.FileName), Is.EqualTo(new[] { "Alpha.yaml", "Weekend trip.yaml" }));
            Assert.That(Searches.SelectedFile!.FileName, Is.EqualTo("Weekend trip.yaml"));
            Assert.That(Searches.SelectedFile.AutoRun.IsChecked, Is.False);
            Assert.That(Searches.SelectedFile.IsEditable, Is.True);
            Assert.That(Find<TextBox>(Window, "NewSearchNameBox").Text, Is.Empty);
            Assert.That(File.Exists(In("Weekend trip.yaml.bak")), Is.False);
            Assert.That(Searches.HasFolderNotice, Is.True, "a new search does not run until enabled");

            // 53: enabling and saving it: a search runs, the warning goes
            Find<CheckBox>(Window, "AutoRunCheckBox").IsChecked = true;
            Click(In<Button>(Find<SearchFileView>(Window, "SearchEditor"), "SaveButton"));
            Assert.That(Searches.HasFolderNotice, Is.False);
            Window.Close();
        }

        // 40, 41: Save As writes the edited search to a new file and opens it; the source keeps what is on disk
        [AvaloniaTest]
        public void SaveAs_WritesTheEditedSearchToANewFile()
        {
            File.WriteAllText(In("Taken.yaml"), "displayName: taken\n");
            var (Window, _, Searches) = ShowSearches();
            SearchFileViewModel Alpha = Searches.Files.Single(F => F.FileName == "Alpha.yaml");
            Searches.SelectedFile = Alpha;
            Settle(Window);
            Type(Find<TextBox>(Window, "DisplayNameBox"), "Alpha copy");

            Type(Find<TextBox>(Window, "SaveAsNameBox"), "taken");
            Click(Find<Button>(Window, "SaveAsButton"));
            Assert.That(Searches.SaveAsIsError, Is.True);
            Assert.That(File.ReadAllText(In("Taken.yaml")), Is.EqualTo("displayName: taken\n"));

            Type(Find<TextBox>(Window, "SaveAsNameBox"), "Alpha copy");
            Click(Find<Button>(Window, "SaveAsButton"));

            Assert.That(Searches.SaveAsIsError, Is.False, Searches.SaveAsMessage);
            Assert.That(Find<TextBlock>(Window, "SaveAsMessage").Text, Is.EqualTo("Saved as Alpha copy.yaml. Alpha.yaml was not changed."));
            Assert.That(File.ReadAllText(In("Alpha copy.yaml")), Is.EqualTo(AlphaText.Replace("displayName: Alpha weekends", "displayName: Alpha copy")));
            Assert.That(File.ReadAllText(In("Alpha.yaml")), Is.EqualTo(AlphaText));
            Assert.That(Alpha.IsDirty, Is.False, "the edits are in the new file");
            Assert.That(Searches.SelectedFile!.FileName, Is.EqualTo("Alpha copy.yaml"));
            Assert.That(Searches.Files.Select(F => F.FileName), Is.EqualTo(new[] { "Alpha copy.yaml", "Alpha.yaml", "Taken.yaml" }));
            Window.Close();
        }

        // ---- changes by other programs ----

        // 6, 30, 31, 33, 9 (selection kept): files added, changed and removed on disk are followed; an edited file is never
        // overwritten or dropped
        [AvaloniaTest]
        public void ChangesOnDisk_AreFollowed_AndEditsAreNeverLost()
        {
            File.WriteAllText(In("Second.yaml"), AlphaText.Replace("Alpha weekends", "Second"));
            var (Window, _, Searches) = ShowSearches();
            SearchFileViewModel Alpha = Searches.Files[0];
            Button Check = Find<Button>(Window, "CheckSearchFilesButton");

            // added, and a clean file changed: listed and reloaded
            File.WriteAllText(In("Third.yaml"), AlphaText.Replace("Alpha weekends", "Third"));
            File.WriteAllText(In("Second.yaml"), AlphaText.Replace("Alpha weekends", "Second, changed"));
            Click(Check);
            Assert.That(Searches.Files.Select(F => F.FileName), Is.EqualTo(new[] { "Alpha.yaml", "Second.yaml", "Third.yaml" }));
            Assert.That(Searches.Files[1].ListDetails, Does.StartWith("Second, changed"));
            Assert.That(Searches.SelectedFile, Is.SameAs(Alpha), "the selection is kept");

            // the edited file changed on disk: a conflict, nothing written; the edits are applied to the new version
            Type(Find<TextBox>(Window, "DisplayNameBox"), "Mine");
            string Theirs = AlphaText.Replace("consecutiveDays: 1", "consecutiveDays: 3");
            File.WriteAllText(In("Alpha.yaml"), Theirs);
            Click(Check);
            Assert.That(Alpha.HasConflict, Is.True);
            Assert.That(Alpha.ListStatus, Does.EndWith("changed on disk while edited"));
            Control Editor = Find<SearchFileView>(Window, "SearchEditor");
            Assert.That(In<Border>(Editor, "ConflictCard").IsVisible, Is.True);
            Assert.That(In<Button>(Editor, "SaveButton").IsEffectivelyEnabled, Is.False);
            Click(In<Button>(Editor, "ApplyMineButton"));
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(File.ReadAllText(In("Alpha.yaml")), Is.EqualTo(Theirs.Replace("displayName: Alpha weekends", "displayName: Mine")));

            // removed: a clean file leaves the list; an edited one stays with its edits and can be created again
            Searches.SelectedFile = Searches.Files[1];
            Settle(Window);
            Type(Find<TextBox>(Window, "DisplayNameBox"), "Second, mine");
            File.Delete(In("Second.yaml"));
            File.Delete(In("Third.yaml"));
            Click(Check);
            Assert.That(Searches.Files.Select(F => F.FileName), Is.EqualTo(new[] { "Alpha.yaml", "Second.yaml" }));
            SearchFileViewModel Second = Searches.Files[1];
            Assert.That(Second.HasConflict && Second.IsDirty, Is.True);
            Assert.That(Searches.SelectedFile, Is.SameAs(Second));
            Click(In<Button>(Find<SearchFileView>(Window, "SearchEditor"), "RecreateButton"));
            Assert.That(File.ReadAllText(In("Second.yaml")), Does.Contain("displayName: Second, mine"));
            Window.Close();
        }

        // ---- accessibility, layout and scope ----

        // 59 (accessible names), keyboard order from the file list into the editor
        [AvaloniaTest]
        public void TheSearchesTab_HasAccessibleNames_AndAKeyboardOrder()
        {
            var (Window, _, Searches) = ShowSearches();
            Assert.That(AutomationProperties.GetName(Find<ListBox>(Window, "SearchFilesList")), Is.EqualTo("Search files"));
            Assert.That(AutomationProperties.GetName(Find<ListBox>(Window, "CampgroundList")), Is.EqualTo("Campgrounds"));
            Assert.That(AutomationProperties.GetName(Find<TextBox>(Window, "CampgroundIdBox")), Is.EqualTo("Campground ID"));
            ListFieldView Dates = List(Window, "searchValueDates");
            Assert.That(AutomationProperties.GetName(In<TextBox>(Dates, "RowBox")), Is.EqualTo("Date 1"));
            Assert.That(AutomationProperties.GetName(In<Button>(Dates, "RowRemoveButton")), Is.EqualTo("Remove date 1"));
            Assert.That(AutomationProperties.GetName(In<Button>(Dates, "AddButton")), Is.EqualTo("Add date"));
            Assert.That(AutomationProperties.GetName(Find<CheckBox>(Window, "AutoRunCheckBox")), Is.EqualTo("Run this search (autoRun)"));
            Assert.That(AutomationProperties.GetHelpText(Find<CheckBox>(Window, "AutoRunCheckBox")), Does.StartWith("Info: autoRun is false"));

            // from the tab header: the file list, New search, then the chosen file's settings in order (Use the selected
            // campground is skipped while nothing is selected)
            ((TabItem)Tabs(Window).Items[SearchesTab]!).Focus();
            var Order = new List<string?>();
            for (int i = 0; i < 8; i++)
            {
                Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Control? Now = Focused(Window) as Control;
                Order.Add(Now is ListBoxItem Item ? Item.FindAncestorOfType<ListBox>()!.Name + "/item" : Now?.Name);
            }
            Assert.That(Order, Is.EqualTo(new[] { "SearchFilesList/item", "NewSearchNameBox", "NewSearchButton", "CheckSearchFilesButton", "AutoRunCheckBox",
                "DisplayNameBox", "CampgroundFilterBox", "CampgroundList/item" }));
            Window.Close();
        }

        // Light and Dark, the smallest window: nothing wider than the window, long names trimmed
        [AvaloniaTest]
        public void TheSearchesTab_FitsTheSmallestWindow_InLightAndDark()
        {
            File.WriteAllText(In("Alpha.yaml"), AlphaText.Replace("Alpha weekends", string.Concat(Enumerable.Repeat("A very long search name ", 12))));
            var (Window, _, _) = ShowSearches();
            foreach (ThemeVariant Theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current!.RequestedThemeVariant = Theme;
                Resize(Window, 720, 540);
                Settle(Window);
                Control Column = Find<StackPanel>(Window, "SearchesColumn");
                Assert.That(Column.Bounds.Width, Is.LessThanOrEqualTo(Window.ClientSize.Width), Theme.ToString());
                Assert.That(Find<ListBox>(Window, "SearchFilesList").Bounds.Width, Is.LessThanOrEqualTo(Window.ClientSize.Width));
                Assert.That(Find<ListBox>(Window, "CampgroundList").Bounds.Width, Is.LessThanOrEqualTo(Window.ClientSize.Width));
                Assert.That(Find<SearchFileView>(Window, "SearchEditor").Bounds.Width, Is.LessThanOrEqualTo(Window.ClientSize.Width));
            }
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            Window.Close();
        }

        // 55, 56, 62: the Searches tab never searches availability or shows results: no request on save, no result
        // controls, no file but the one saved changed, and its code refers to no search, download or email code
        [AvaloniaTest]
        public void TheSearchesTab_NeverSearchesOrShowsResults()
        {
            var (Window, _, Searches) = ShowSearches();
            Dictionary<string, string> Before = Fingerprint(Folder);
            using var Requests = new WebRequestListener();

            Find<CheckBox>(Window, "AutoRunCheckBox").IsChecked = true;
            Click(In<Button>(Find<SearchFileView>(Window, "SearchEditor"), "SaveButton"));

            Dictionary<string, string> After = Fingerprint(Folder);
            Assert.That(After.Keys.Except(Before.Keys), Is.EqualTo(new[] { "Alpha.yaml.bak" }));
            Assert.That(After.Where(F => F.Key != "Alpha.yaml" && F.Key != "Alpha.yaml.bak").All(F => Before[F.Key] == F.Value), Is.True);
            Assert.That(Requests.Count, Is.Zero);

            var Allowed = new HashSet<string?> { "NewSearchButton", "CheckSearchFilesButton", "UseCampgroundButton", "UseCampgroundIdButton", "SaveButton",
                "RevertButton", "ReloadButton", "StateReloadButton", "CreateButton", "ApplyMineButton", "RecreateButton", "UseTheirsButton", "SaveAsButton",
                "RowUpButton", "RowDownButton", "RowRemoveButton", "AddButton" };
            List<string?> Buttons = View(Window).GetVisualDescendants().OfType<Button>().Where(b => b.GetType() == typeof(Button)).Select(b => b.Name).Distinct().ToList();
            Assert.That(Buttons, Is.SubsetOf(Allowed));
            Assert.That(View(Window).GetVisualDescendants().OfType<Control>().Select(c => c.GetType().Name).Distinct(),
                Has.None.Matches<string>(n => n is "DataGrid" or "Calendar" or "CalendarDatePicker" or "TreeView"));

            Type[] SearchTypes_ =
            {
                typeof(SearchesViewModel), typeof(SearchFileViewModel), typeof(CampgroundSettingField), typeof(CampgroundCatalog),
                typeof(ListSettingField), typeof(ListRow), typeof(SearchesView), typeof(SearchFileView), typeof(ListFieldView),
            };
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.api.", "CampertronLibrary.function.RecDotOrg.data.", "CampertronLibrary.function.Base.Web::",
                "System.Net.", "CampsiteConfig", "System.Diagnostics.Process", "System.Console", "CampertronLibrary.function.Base.ConfigFileReplace::",
                "CampertronLibrary.function.Base.YamlDocument::",
            };
            List<string> Called = ReferencedMembers(SearchTypes_).Select(c => c.Member).Distinct().ToList();
            Assert.That(Called, Does.Contain("CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundPickerEntries"));
            Assert.That(Called.Where(m => Forbidden.Any(f => m.StartsWith(f, StringComparison.Ordinal))), Is.Empty);
            Window.Close();
        }

        // 57, 58, 59: the Settings tab is unchanged next to the Searches tab, and the Searches tab follows its outputTo
        [AvaloniaTest]
        public void TheSearchesTab_FollowsTheGeneralOutput()
        {
            var (Window, ViewModel, Searches) = ShowSearches();
            Assert.That(Searches.FolderNoticeText, Does.EndWith("nothing is searched. Enable a search with Run this search (autoRun) and save it."));
            ViewModel.Settings!.General.OutputTo.Selected = ViewModel.Settings.General.OutputTo.Choices.Single(C => C.Value.Equals(OutputType.Email));
            Settle(Window);
            Assert.That(Searches.FolderNoticeText, Does.Contain("the Email loop checks 0 searches every minute"));
            Assert.That(File.ReadAllText(In("General.yaml")), Does.StartWith("outputTo: Console"), "not saved: the form's value");
            Window.Close();
        }
    }

    internal static class SearchTestExtensions
    {
        // the editing session of a search file's view model (internal to the GUI)
        internal static ConfigEditSession<CampertronConfig> Session(this SearchFileViewModel File) =>
            (ConfigEditSession<CampertronConfig>)typeof(SettingsFileViewModel<CampertronConfig>).GetProperty("Session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(File)!;
    }
}
