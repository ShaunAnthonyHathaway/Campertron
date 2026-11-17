using System.Diagnostics;
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
    // The attribute selectors (decision D51, stage 4): includeAttributes and excludeAttributes as database-backed lists of
    // the chosen campground's Name:Value attributes, grouped under a heading per attribute name (a heading checks nothing),
    // each value shown by its value part and written as Name:Value exactly (a blank value, a colon in the value and spaces
    // kept), spellings that differ only by case grouped, Contains matching's surprises shown ("Shade:Yes" contains
    // "Shade:"), kept values, a campground's 270 values in 92 names searchable and virtualized. Real windows with the fixture
    // database (Charlie's attributes) and view models with a choice source the test controls.
    public class AttributeSelectorTests
    {
        private const string Head = "# attribute selector tests\ndisplayName: Attributes   # shown in the results\nautoRun: false\n";
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
            Folder = Path.Join(GuiTestEnvironment.Root, "attributes-" + Guid.NewGuid().ToString("N"));
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
            Field.Options.Select(O => new string(' ', O.Level * 2) + O.Label + " | " + O.CampsitesText + (O.IsHeading ? "" : O.IsChecked == true ? " [x]" : O.IsChecked == null ? " [-]" : " [ ]")).ToList();

        private static FilterOptionRow Value(FilterSelectorField Field, string Prefix, string Label) =>
            Field.Options.Single(O => O.IsChoice && O.Prefix == Prefix && O.Label == Label);

        [AvaloniaTest]
        public void TheAttributes_AreGroupedByName_WithTheirValuesExactly()
        {
            var (_, _, Trip) = Open(Search("includeAttributes: ['Shade:Yes']\nexcludeAttributes: []\n"));

            Assert.That(Rows(Trip.IncludeAttributes), Is.EqualTo(new[]
            {
                "Checkin Time | 1 value",
                "  2:00 PM | 1 campsite [ ]",
                "Shade | 4 values",
                "  (blank) | 1 campsite [ ]",
                "  “ Yes” (with spaces at its start or end) | 1 campsite [ ]",
                "  YES (2 spellings that differ only by case) | 3 campsites [-]",
                "    YES | 1 campsite [ ]",
                "    Yes | 2 campsites [x]",
                "Toilet | 1 value",
                "  Flush | 1 campsite [ ]",
            }));
            Assert.That(Trip.IncludeAttributes.StatusText, Is.EqualTo("6 attribute values at this campground, 1 checked."));
            FilterOptionRow Heading = Trip.IncludeAttributes.Options.Single(O => O.IsHeading && O.Label == "Shade");
            Assert.That((Heading.IsCheckable, Heading.AccessibleName), Is.EqualTo((false, "Attribute Shade, 4 values")));
            Assert.That(Value(Trip.IncludeAttributes, "Shade: ", "(blank)").AccessibleName, Is.EqualTo("Shade: (blank), 1 campsite, not checked"));
            Assert.That(Value(Trip.IncludeAttributes, "Checkin Time: ", "2:00 PM").Values.Single(), Is.EqualTo("Checkin Time:2:00 PM"), "Name:Value, the colon in the value kept");
            Assert.That(Trip.IsDirty, Is.False);
        }

        [AvaloniaTest]
        public void CheckingAValue_WritesNameAndValueExactly()
        {
            string Text = Search("includeAttributes:\n  - Toilet:Flush   # mine\nexcludeAttributes: []\n", Extra: "filterMatching: Exact\n");
            var (Window, _, Trip) = Open(Text);

            Value(Trip.IncludeAttributes, "Shade: ", "(blank)").IsChecked = true;
            Value(Trip.IncludeAttributes, "Shade: ", "“ Yes” (with spaces at its start or end)").IsChecked = true;
            Value(Trip.IncludeAttributes, "Checkin Time: ", "2:00 PM").IsChecked = true;
            Settle(Window);

            string Expected = Text.Replace("  - Toilet:Flush   # mine\n", "  - Toilet:Flush   # mine\n  - 'Shade:'\n  - 'Shade: Yes'\n  - Checkin Time:2:00 PM\n");
            Assert.That(Trip.Session.Candidate, Is.EqualTo(Expected));
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(Trip.Session.Candidate!).IncludeAttributes,
                Is.EqualTo(new[] { "Toilet:Flush", "Shade:", "Shade: Yes", "Checkin Time:2:00 PM" }), "read back exactly");
        }

        [AvaloniaTest]
        public void AHeading_ChecksNothing_WithTheMouseOrTheKeyboard()
        {
            var (Window, _, Trip) = Open(Search("includeAttributes: []\nexcludeAttributes: []\n"));
            FilterOptionRow Heading = Trip.IncludeAttributes.Options.First(O => O.IsHeading);
            Heading.IsChecked = true;
            Trip.IncludeAttributes.Toggle(Heading);
            Assert.That(Trip.IsDirty, Is.False);

            FilterSelectorView View = Window.GetVisualDescendants().OfType<FilterSelectorView>().Single(V => V.Name == "IncludeAttributesSelector");
            View.BringIntoView();
            Settle(Window);
            ListBox List = View.GetVisualDescendants().OfType<ListBox>().Single(L => L.Name == "ChoiceList");
            List.SelectedIndex = 0;
            Settle(Window);
            List.GetVisualDescendants().OfType<ListBoxItem>().First().Focus();
            Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Settle(Window);
            Assert.That(Trip.IsDirty, Is.False, "Space on a heading does nothing");
            Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Settle(Window);
            Assert.That(Trip.IncludeAttributes.Options[1].IsChecked, Is.True, "the value under it is checked");
            Assert.That(List.GetVisualDescendants().OfType<CheckBox>().First(C => (C.DataContext as FilterOptionRow)?.IsHeading == true).IsVisible, Is.False, "a heading has no check box");
            Assert.That(List.GetVisualDescendants().OfType<ListBoxItem>().Select(AutomationProperties.GetName), Does.Contain("Attribute Checkin Time, 1 value"));
        }

        [AvaloniaTest]
        public void ContainsMatching_ShowsThatAValueAlsoMatchesTheBlankOne()
        {
            var (_, _, Trip) = Open(Search("includeAttributes: [Shade:Yes, 'Shade:Maybe', Toilet]\nexcludeAttributes: []\n"));
            Assert.That(Value(Trip.IncludeAttributes, "Shade: ", "Yes").AlsoText, Is.EqualTo("With Contains matching it also matches “Shade:”."),
                "a value contains the campground's blank value of its name");
            Assert.That(Trip.IncludeAttributes.OtherValues.Select(O => O.Label + " | " + O.Description), Is.EqualTo(new[]
            {
                "Shade:Maybe | Not one of this campground's attribute values; with Contains matching it matches one of them: “Shade:”.",
                "Toilet | Not among this campground's attribute values; it matches none of them with Contains matching.",
            }));

            Trip.Matching.Selected = Trip.Matching.Choices.Single(C => C.Value is FilterMatchMode.Exact);
            Assert.That(Value(Trip.IncludeAttributes, "Shade: ", "Yes").AlsoText, Is.Empty);
            Assert.That(Trip.IncludeAttributes.OtherValues[0].Description, Is.EqualTo("Not among this campground's attribute values; it matches none of them with Exact matching."));
        }

        [AvaloniaTest]
        public void Find_NarrowsByNameAndValue_AndKeepsTheHeadings()
        {
            var (_, _, Trip) = Open(Search("includeAttributes: []\nexcludeAttributes: []\n"));
            Trip.IncludeAttributes.FindText = "shade yes";
            Assert.That(Rows(Trip.IncludeAttributes), Is.EqualTo(new[]
            {
                "Shade | 4 values",
                "  “ Yes” (with spaces at its start or end) | 1 campsite [ ]",
                "  YES (2 spellings that differ only by case) | 3 campsites [ ]",
                "    YES | 1 campsite [ ]",
                "    Yes | 2 campsites [ ]",
            }));
            Trip.IncludeAttributes.FindText = "2:00";
            Assert.That(Rows(Trip.IncludeAttributes), Is.EqualTo(new[] { "Checkin Time | 1 value", "  2:00 PM | 1 campsite [ ]" }));
        }

        [AvaloniaTest]
        public void TheSameAttributeIncludedAndExcluded_IsWarned()
        {
            var (_, _, Trip) = Open(Search("includeAttributes: [Shade:Yes]\nexcludeAttributes: [Shade:Yes]\n"));
            Assert.That(Trip.ExcludeAttributes.ConflictText, Does.StartWith("Warning: also in includeAttributes: “Shade:Yes”."));
        }

        // ---- a large campground: 270 values in 92 names (Watchman in the shipped data) ----

        [AvaloniaTest]
        public void ALargeCampground_IsSearchable_Virtualized_AndQuick()
        {
            var Values = new List<FilterChoice>();
            for (int Name = 1; Name <= 92; Name++)
            {
                int Count = Name <= 86 ? 3 : 2;
                for (int Value = 0; Value < Count; Value++)
                {
                    string AttributeName = "Attribute " + Name.ToString("00");
                    string AttributeValue = Value == 0 ? string.Empty : "Value " + Value;
                    Values.Add(new FilterChoice(AttributeName + ":" + AttributeValue, Name, AttributeName, AttributeValue));
                }
            }
            Assert.That(Values, Has.Count.EqualTo(270));
            Dispatcher Ui = Dispatcher.UIThread;
            var Source = new FilterChoiceSource(Id => new CampgroundFilterChoices(Id, 192, Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>(), Values),
                () => "db", Job => Ui.Post(Job), Job => Task.Run(Job));
            File.WriteAllText(In("Trip.yaml"), Search("includeAttributes: []\nexcludeAttributes: []\n"));
            var Searches = new SearchesViewModel(Folder, null, () => new List<CampgroundPickerEntry>
            {
                new CampgroundPickerEntry(new CampgroundChoice(GuiTestEnvironment.CampgroundCharlieID, "CHARLIE", null, null), null),
            }, () => Source);
            Searches.Show();
            SearchFileViewModel Trip = Searches.Files.Single();
            WaitUntil(() => Trip.IncludeAttributes.Status == FilterChoicesStatus.Loaded, () => "loaded");
            FilterSelectorField Attributes = Trip.IncludeAttributes;

            Assert.That(Attributes.Options, Has.Count.EqualTo(270 + 92));
            Assert.That((Attributes.CanFind, Attributes.ListMaxHeight), Is.EqualTo((true, 320d)));
            var Clock = Stopwatch.StartNew();
            Attributes.FindText = "attribute 07";
            Assert.That(Attributes.Options.Select(O => O.Label), Is.EqualTo(new[] { "Attribute 07", "(blank)", "Value 1", "Value 2" }));
            Attributes.SelectShown();
            TestContext.Out.WriteLine($"find and check one name's 3 values among 270: {Clock.Elapsed.TotalMilliseconds:F0} ms");
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(Trip.Session.Candidate!).IncludeAttributes,
                Is.EqualTo(new[] { "Attribute 07:", "Attribute 07:Value 1", "Attribute 07:Value 2" }));
            Attributes.FindText = "";
            Clock.Restart();
            Attributes.Toggle(Attributes.Options.First(O => O.IsChoice && O.Prefix == "Attribute 50: "));
            TestContext.Out.WriteLine($"one toggle among 270 values: {Clock.Elapsed.TotalMilliseconds:F0} ms");
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
        }
    }
}
