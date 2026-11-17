using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.search;
using Microsoft.Data.Sqlite;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The filter selectors end to end (decisions D51 to D54): all eight include and exclude lists and the matching choice
    // set in the real window, saved, then read by the console's loader and searched by the Results tab's own service
    // (CampsiteSearch.Plan and Run, a transport the test answers: never recreation.gov) on the fixture database's Alpha;
    // and the keyboard through the whole filters card (one tab stop per list).
    public class FilterIntegrationTests
    {
        private const string Lists =
            "includeEquipment: []\nexcludeEquipment: []\nincludeSites: []\nexcludeSites: []\n" +
            "includeAttributes: []\nexcludeAttributes: []\nincludeCampsiteType: []\nexcludeCampsiteType: []\n";

        private string Folder = string.Empty;
        private string In(string Name) => Path.Join(Folder, Name);
        private DateTime First;

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "integration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(In("General.yaml"), "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            File.WriteAllText(In("Email.yaml"), "smtpServer: ''\nsmtpPort: 0\n");
            File.Copy(GuiTestEnvironment.DbFile, In("RecreationDotOrg.db"), overwrite: true);
            DateTime Ahead = DateTime.Now.AddMonths(2);
            First = new DateTime(Ahead.Year, Ahead.Month, 10);
            File.WriteAllText(In("Trip.yaml"),
                "# every list from the GUI\ndisplayName: Every list\nautoRun: true\ncampgroundID: '" + GuiTestEnvironment.CampgroundAlphaID + "'\ntotalHumans: 2\n" +
                "searchBy: StartEndDate\nsearchValue: 0\nsearchValueDates:\n- " + First.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\n- " +
                First.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\n" +
                "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\n" +
                Lists + "consecutiveDays: 1\n");
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

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private (MainWindow Window, SearchFileViewModel Trip) Open()
        {
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            SearchFileViewModel Trip = ViewModel.Searches!.Files.Single(F => F.FileName == "Trip.yaml");
            ViewModel.Searches.SelectedFile = Trip;
            WaitUntil(() => Trip.Selectors.All(S => S.Status == FilterChoicesStatus.Loaded), () => "Alpha's values");
            Settle(Window);
            return (Window, Trip);
        }

        private static void Check(FilterSelectorField Field, string Prefix, string Label) =>
            Field.Options.Single(O => O.IsChoice && O.Prefix == Prefix && O.Label == Label).IsChecked = true;

        [AvaloniaTest]
        public void EveryList_SetInTheGui_IsWhatTheSearchUses()
        {
            var (Window, Trip) = Open();
            Assert.That(Trip.Selectors.Select(S => S.Key), Is.EquivalentTo(new[]
            {
                "includeEquipment", "excludeEquipment", "includeSites", "excludeSites", "includeAttributes", "excludeAttributes", "includeCampsiteType", "excludeCampsiteType"
            }), "all eight lists are selectors");

            Window.GetVisualDescendants().OfType<ComboBox>().Single(C => C.Name == "MatchingComboBox").SelectedItem = Trip.Matching.Choices.Single(C => C.Value is FilterMatchMode.Exact);
            Check(Trip.IncludeEquipment, "", "RV");
            Check(Trip.IncludeEquipment, "", "Tent");
            Trip.ExcludeEquipment.CustomText = "Trailer";
            Trip.ExcludeEquipment.AddCustom();
            Check(Trip.IncludeSites, "", "A001");
            Check(Trip.IncludeSites, "", "G001");
            Check(Trip.ExcludeSites, "", "G001");
            Check(Trip.IncludeAttributes, "Shade: ", "Yes");
            Check(Trip.IncludeAttributes, "Checkin Time: ", "2:00 PM");
            Check(Trip.ExcludeAttributes, "Shade: ", "No");
            Check(Trip.IncludeCampsiteType, "", "STANDARD NONELECTRIC");
            Check(Trip.IncludeCampsiteType, "", "GROUP STANDARD NONELECTRIC");
            Check(Trip.ExcludeCampsiteType, "", "STANDARD ELECTRIC");
            Settle(Window);
            Assert.That(Trip.ExcludeSites.ConflictText, Does.Contain("G001"), "G001 is in both: allowed, warned");
            Assert.That(Trip.CanSave, Is.True, string.Join(" ", Trip.Session.Notices.Select(N => N.Message)));
            Trip.Save();
            Assert.That(Trip.StatusIsError, Is.False, Trip.StatusText);

            // the file as the console's loader reads it at a start
            CampertronConfig Loaded = Yaml.CampertronConfigGetConfigs(Folder).Single();
            Assert.That(Loaded.EffectiveFilterMatching, Is.EqualTo(FilterMatchMode.Exact));
            Assert.That(new[] { Loaded.IncludeEquipment, Loaded.ExcludeEquipment, Loaded.IncludeSites, Loaded.ExcludeSites, Loaded.IncludeAttributes, Loaded.ExcludeAttributes,
                Loaded.IncludeCampsiteType, Loaded.ExcludeCampsiteType }, Is.EqualTo(new List<string>[]
            {
                new() { "RV", "Tent" }, new() { "Trailer" }, new() { "A001", "G001" }, new() { "G001" },
                new() { "Shade:Yes", "Checkin Time:2:00 PM" }, new() { "Shade:No" }, new() { "STANDARD NONELECTRIC", "GROUP STANDARD NONELECTRIC" }, new() { "STANDARD ELECTRIC" }
            }));
            Assert.That(File.ReadAllText(In("Trip.yaml")), Does.StartWith("# every list from the GUI\n").And.Contain("\nfilterMatching: Exact\n"));

            // the Results tab's search: every Alpha campsite open on both nights. OR in every list: types (STANDARD
            // NONELECTRIC or GROUP STANDARD NONELECTRIC: 11, 13, 14), equipment (Tent or RV, exactly: 14 permits "tent"),
            // names (A001 or G001), excluded G001, attributes (Shade:Yes or the check-in time), excluded Shade:No and
            // Trailer: campsite 9900011 alone
            SearchRunPlan Plan = CampsiteSearch.Plan(new SearchRunRequest(Folder, Path.Join(Folder, "cache"), new[] { "Trip.yaml" }), DateTime.Now);
            Assert.That(Plan.Entries.Single().State, Is.EqualTo(SearchPlanState.Ready), string.Join(" ", Plan.Entries.Single().Diagnostics.Select(D => D.Message)));
            Directory.CreateDirectory(Path.Join(Folder, "cache"));
            string[] Alpha = GuiTestEnvironment.FilterCampsites.Where(C => C.Facility == GuiTestEnvironment.CampgroundAlphaID).Select(C => C.Campsite).ToArray();
            var Requests = new List<string>();
            SearchRunResult Result = Task.Run(() => CampsiteSearch.Run(Plan, null, CancellationToken.None, new SearchHostOptions
            {
                Transport = (Request, Token) =>
                {
                    lock (Requests)
                    {
                        Requests.Add(Request.Url);
                    }
                    return new SearchResponse(200, RunProbe.MonthJson(Request.Month, Alpha));
                },
                RetryDelay = TimeSpan.Zero,
                HeavyTrafficDelay = TimeSpan.Zero
            })).Result;
            SearchResult Search = Result.Searches.Single();
            Assert.That(Search.State, Is.EqualTo(SearchState.Complete));
            Assert.That(Search.Openings.Select(O => (O.CampsiteId, O.Night)), Is.EquivalentTo(new[] { ("9900011", First), ("9900011", First.AddDays(1)) }));
            Assert.That(Requests, Has.Count.EqualTo(1), "one month, through the test's transport");
        }

        [AvaloniaTest]
        public void TheKeyboard_TakesOneTabStopPerList_ThroughTheWholeFiltersCard()
        {
            var (Window, Trip) = Open();
            ComboBox Matching = Window.GetVisualDescendants().OfType<ComboBox>().Single(C => C.Name == "MatchingComboBox");
            Matching.BringIntoView();
            Matching.Focus();
            Settle(Window);
            Border Card = Window.GetVisualDescendants().OfType<Border>().Single(B => B.Name == "FiltersCard");
            var Stops = new List<string>();
            for (int i = 0; i < 120; i++)
            {
                Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Settle(Window);
                if (Focused(Window) is not Control Now || !Card.GetVisualDescendants().Contains(Now))
                {
                    break;
                }
                string Owner = Now.FindAncestorOfType<FilterSelectorView>()?.DataContext is FilterSelectorField F ? F.Key : "card";
                Stops.Add(Owner + ":" + (Now is ListBoxItem ? "list" : Now.Name ?? Now.GetType().Name));
            }
            TestContext.Out.WriteLine(string.Join("\n", Stops));
            // with a short list (no find box, no bulk actions) and nothing kept: the list, the typed-value box and Add
            foreach (FilterSelectorField Field in Trip.Selectors)
            {
                Assert.That(Stops.Where(S => S.StartsWith(Field.Key + ":", StringComparison.Ordinal)),
                    Is.EqualTo(new[] { Field.Key + ":list", Field.Key + ":CustomBox", Field.Key + ":AddCustomButton" }), Field.Key + ": its list is one tab stop");
            }
            Assert.That(Stops, Has.Count.EqualTo(Trip.Selectors.Count * 3), "nothing else in the card takes a stop, never one per value");
        }
    }
}
