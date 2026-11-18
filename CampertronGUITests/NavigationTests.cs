using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The window's navigation (decision D50): four top-level tabs, Searches, Results, Settings and Campgrounds, with Searches
    // shown at the start; Settings holds Campertron (General.yaml and Email.yaml) and System (the theme); Campgrounds holds the
    // three lookup tabs By Park, By City and By State. Each test has its own settings folder (a copy of the fixture
    // database) and a window whose startup loader returns it.
    public class NavigationTests
    {
        private const string Secret = "Nav-Sentinel-Secret-91b3";
        private const string GeneralText = "# Campertron settings\noutputTo: Console\nrefreshRidbDataDayInterval: 30\n" +
            "lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n";
        private const string EmailText = "smtpServer: mail.example.org\nsmtpPort: 587\nsmtpUsername: camper\nsmtpPassword: " + Secret + "\n" +
            "sendToAddressList:\n  - first@example.org\nsendFromAddress: camper@example.org\n";
        private const string SearchText = "displayName: Alpha weekends\nautoRun: false\ncampgroundID: '" + GuiTestEnvironment.CampgroundAlphaID + "'\n" +
            "totalHumans: 2\nsearchBy: DaysOut\nsearchValue: 30\nconsecutiveDays: 1\n";

        private string Folder = string.Empty;

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "navigation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Join(Folder, "General.yaml"), GeneralText);
            File.WriteAllText(Path.Join(Folder, "Email.yaml"), EmailText);
            File.WriteAllText(Path.Join(Folder, "Alpha.yaml"), SearchText);
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"));
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

        private (MainWindow Window, MainWindowViewModel ViewModel) Show()
        {
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder, CachePath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Settle(Window);
            return (Window, ViewModel);
        }

        private static void Settle(MainWindow Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        // shown in the window: attached to it (a tab row presents only its selected tab's content) and visible
        private static bool Presented(Control Control)
        {
            return Avalonia.Controls.TopLevel.GetTopLevel(Control) != null && Control.IsEffectivelyVisible;
        }

        private static string? FocusName(MainWindow Window)
        {
            IInputElement? Element = Focused(Window);
            return Element is TabItem Tab ? "Tab:" + Tab.Header : (Element as Control)?.Name ?? Element?.GetType().Name;
        }

        private static List<string?> PressTab(MainWindow Window, int Count, RawInputModifiers Modifiers = RawInputModifiers.None)
        {
            var Visited = new List<string?>();
            for (int i = 0; i < Count; i++)
            {
                Window.KeyPress(Key.Tab, Modifiers, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                Visited.Add(FocusName(Window));
            }
            return Visited;
        }

        private static void Press(MainWindow Window, Key Key, PhysicalKey Physical)
        {
            Window.KeyPress(Key, RawInputModifiers.None, Physical, null);
            Dispatcher.UIThread.RunJobs();
        }

        // ---- composition ----

        // The top-level tabs in this exact order, Searches shown first and read at the start, each tab named for
        // accessibility and automation by its header and its x:Name.
        [AvaloniaTest]
        public void TheTopLevelTabs_AreSearchesResultsSettingsCampgrounds_AndSearchesIsShownFirst()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel) = Show();

            TabControl Top = Tabs(Window);
            Assert.That(Headers(Top), Is.EqualTo(new[] { "Searches", "Results", "Settings", "Campgrounds" }));
            Assert.That(Top.Items.Cast<TabItem>().Select(t => t.Name), Is.EqualTo(new[] { "SearchesTab", "ResultsTab", "SettingsTab", "CampsitesTab" }));
            Assert.That(new[] { MainWindow.SearchesTabIndex, MainWindow.ResultsTabIndex, MainWindow.SettingsTabIndex, MainWindow.CampsitesTabIndex },
                Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(Top.SelectedIndex, Is.EqualTo(MainWindow.SearchesTabIndex), "Searches is the tab shown at the start");
            Assert.That(Presented(Named<SearchesView>(Window, "SearchesContent")), Is.True);
            Assert.That(Named<SearchesView>(Window, "SearchesContent").DataContext, Is.SameAs(ViewModel.Searches));
            Assert.That(ViewModel.Searches!.IsLoaded, Is.True, "the first tab read its files when the startup was ready");
            Assert.That(ViewModel.Searches.Files.Select(f => f.FileName), Is.EqualTo(new[] { "Alpha.yaml" }));
            Assert.That(ViewModel.Results!.IsLoaded, Is.False, "Results reads nothing until it is shown");
            foreach (TabItem Item in Top.Items.Cast<TabItem>())
            {
                AutomationPeer Peer = ControlAutomationPeer.CreatePeerForElement(Item);
                Assert.That(Peer.GetName(), Is.EqualTo(Item.Header), Item.Name);
                Assert.That(Peer.GetAutomationId(), Is.EqualTo(Item.Name), Item.Name);
            }
            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        // D55: the campground lookup's tab is labeled Campgrounds (it shows campgrounds; a campsite is a site within one): its
        // header as drawn, the name a screen reader hears and its header text. Its automation IDs keep the former label
        // (CampsitesTab, CampsitesTabControl), so a tool that finds the tab by its ID is unaffected; no tab says Campsites.
        [AvaloniaTest]
        public void TheLookupTab_IsLabeledCampgrounds_AndKeepsItsAutomationIds()
        {
            var (Window, _) = Show();
            TabItem Lookup = (TabItem)Tabs(Window).Items[MainWindow.CampsitesTabIndex]!;
            Assert.That(Lookup.Header, Is.EqualTo("Campgrounds"));
            Assert.That(Lookup.GetVisualDescendants().OfType<TextBlock>().Select(T => T.Text), Does.Contain("Campgrounds"), "the header as drawn");
            AutomationPeer Peer = ControlAutomationPeer.CreatePeerForElement(Lookup);
            Assert.That(Peer.GetName(), Is.EqualTo("Campgrounds"), "the name a screen reader hears");
            Assert.That(Peer.GetAutomationId(), Is.EqualTo("CampsitesTab"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(CampsitesTabs(Window)).GetAutomationId(), Is.EqualTo("CampsitesTabControl"));
            Assert.That(Lookup.Content, Is.SameAs(CampsitesTabs(Window)));
            IEnumerable<object?> Every = Tabs(Window).Items.Concat(SettingsTabs(Window).Items).Concat(CampsitesTabs(Window).Items).Cast<TabItem>().Select(T => T.Header);
            Assert.That(Every, Has.None.EqualTo("Campsites"));
            Window.Close();
        }

        // Campgrounds holds the three lookup tabs in this order, By Park first; each shows the lookup with its own
        // ReactiveViewModel bindings, as the three top-level lookup tabs did.
        [AvaloniaTest]
        public void TheCampgroundsTab_HoldsByParkByCityAndByState_WithByParkFirst()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel) = Show();

            TabControl Row = CampsitesTabs(Window);
            Assert.That(Headers(Row), Is.EqualTo(new[] { "By Park", "By City", "By State" }));
            Assert.That(Row.Items.Cast<TabItem>().Select(t => t.Name), Is.EqualTo(new[] { "ByParkTab", "ByCityTab", "ByStateTab" }));
            Assert.That(Row.SelectedIndex, Is.EqualTo(MainWindow.ByParkTabIndex), "By Park is the Campgrounds tab shown first");
            Assert.That(((TabItem)Tabs(Window).Items[MainWindow.CampsitesTabIndex]!).Content, Is.SameAs(Row), "the row is the Campgrounds tab's content");
            Assert.That(Presented(Named<Grid>(Window, "ByParkContent")), Is.False, "the lookup is not shown at the start");

            Tabs(Window).SelectedIndex = MainWindow.CampsitesTabIndex;
            Settle(Window);
            Assert.That(Row.FindAncestorOfType<TabControl>(), Is.SameAs(Tabs(Window)));
            Assert.That(Presented(Named<Grid>(Window, "ByParkContent")), Is.True, "Campgrounds opens on By Park");
            Assert.That(Presented(Named<Grid>(Window, "ByCityContent")), Is.False);
            for (int Index = 0; Index < ExpectedTabs.Length; Index++)
            {
                Grid Content = SelectTab(Window, Index);
                Assert.That(Presented(Content), Is.True, ExpectedTabs[Index]);
                Assert.That(Content.DataContext, Is.SameAs(ViewModel.ReactiveViewModel), ExpectedTabs[Index]);
                Assert.That(Details(Window, Index).DataContext, Is.InstanceOf<CampgroundDetails>(), ExpectedTabs[Index]);
                Assert.That(ControlAutomationPeer.CreatePeerForElement((TabItem)Row.SelectedItem!).GetName(), Is.EqualTo(ExpectedTabs[Index]));
            }
            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        // Settings holds Campertron (the General.yaml and Email.yaml editors, as the Settings tab had them) and System (the
        // theme), Campertron first.
        [AvaloniaTest]
        public void TheSettingsTab_HoldsCampertronAndSystem_WithCampertronFirst()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel) = Show();

            TabControl Row = SettingsTabs(Window);
            Assert.That(Headers(Row), Is.EqualTo(new[] { "Campertron", "System" }));
            Assert.That(Row.Items.Cast<TabItem>().Select(t => t.Name), Is.EqualTo(new[] { "CampertronSettingsTab", "SystemSettingsTab" }));
            Assert.That(Row.SelectedIndex, Is.EqualTo(MainWindow.CampertronSettingsTabIndex), "Campertron is the Settings tab shown first");
            Assert.That(((TabItem)Tabs(Window).Items[MainWindow.SettingsTabIndex]!).Content, Is.SameAs(Row), "the row is the Settings tab's content");

            ShowSettingsTab(Window);
            SettingsView Campertron = Named<SettingsView>(Window, "SettingsContent");
            Assert.That(Presented(Campertron), Is.True);
            Assert.That(Campertron.DataContext, Is.SameAs(ViewModel.Settings));
            Assert.That(ViewModel.Settings!.General.IsEditable && ViewModel.Settings.Email.IsEditable, Is.True, "both files open as before");
            Assert.That(Presented(Named<SystemSettingsView>(Window, "SystemSettingsContent")), Is.False);

            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            SystemSettingsView System = Named<SystemSettingsView>(Window, "SystemSettingsContent");
            Assert.That(Presented(System), Is.True);
            Assert.That(System.DataContext, Is.SameAs(ViewModel.SystemSettings));
            Assert.That(Presented(Campertron), Is.False);
            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        // ---- state ----

        // Moving between the top-level tabs and inside the two rows keeps what each tab shows: the lookup's selections and
        // details, the Settings editor's chosen file and an unsaved edit, the chosen search file, and the tab chosen in
        // each row.
        [AvaloniaTest]
        public void SwitchingTabs_KeepsEachTabsState()
        {
            var (Window, ViewModel) = Show();
            ReactiveViewModel Lookup = ViewModel.ReactiveViewModel!;

            SelectTab(Window, MainWindow.ByCityTabIndex);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateUtah);
            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.CityAlpha);
            SelectCampground(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);
            SelectTab(Window, MainWindow.ByParkTabIndex);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
            SelectTab(Window, MainWindow.ByCityTabIndex);

            ShowSettingsTab(Window);
            ViewModel.Settings!.IsEmailSelected = true;
            ViewModel.Settings.Email.SmtpServer.Text = "smtp.example.net";
            Dispatcher.UIThread.RunJobs();
            Assert.That(ViewModel.Settings.Email.IsDirty, Is.True);
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);

            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            SearchFileViewModel? Chosen = ViewModel.Searches!.SelectedFile;
            Assert.That(Chosen, Is.Not.Null);

            // round trips through every tab
            foreach (int Index in new[] { MainWindow.ResultsTabIndex, MainWindow.CampsitesTabIndex, MainWindow.SettingsTabIndex, MainWindow.SearchesTabIndex,
                MainWindow.CampsitesTabIndex, MainWindow.ResultsTabIndex, MainWindow.SettingsTabIndex })
            {
                Tabs(Window).SelectedIndex = Index;
                Settle(Window);
            }

            Assert.That(SettingsTabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SystemSettingsTabIndex), "the Settings row keeps its tab");
            Assert.That(ViewModel.Settings.IsEmailSelected, Is.True);
            Assert.That(ViewModel.Settings.Email.SmtpServer.Text, Is.EqualTo("smtp.example.net"), "an unsaved edit stays");
            Assert.That(ViewModel.Settings.Email.IsDirty, Is.True);
            Assert.That(File.ReadAllText(Path.Join(Folder, "Email.yaml")), Is.EqualTo(EmailText), "switching tabs saves nothing");
            Assert.That(ViewModel.Searches.SelectedFile, Is.SameAs(Chosen));

            Tabs(Window).SelectedIndex = MainWindow.CampsitesTabIndex;
            Settle(Window);
            Assert.That(CampsitesTabs(Window).SelectedIndex, Is.EqualTo(MainWindow.ByCityTabIndex), "the Campsites row keeps its tab");
            Assert.That(Lookup.SelectedState, Is.EqualTo(GuiTestEnvironment.StateUtah));
            Assert.That(Lookup.SelectedCity, Is.EqualTo(GuiTestEnvironment.CityAlpha));
            Assert.That(Lookup.SelectedCampground?.FacilityID, Is.EqualTo(GuiTestEnvironment.CampgroundAlphaID));
            Assert.That(DisplayedId(Details(Window, MainWindow.ByCityTabIndex)), Is.EqualTo(GuiTestEnvironment.CampgroundAlphaID));
            Assert.That(Lookup.SelectedPark, Is.EqualTo(GuiTestEnvironment.ParkOne));
            Assert.That(ViewModel.ReactiveViewModel, Is.SameAs(Lookup), "the lookup's view model is the same");
            Window.Close();
        }

        // Settings > Campertron keeps CM5's rules on the second level: the editors look at their files when that tab is
        // shown (from the other Settings tab or from another top-level tab), and a revealed password is masked again when
        // the user leaves it for System as well as for another top-level tab.
        [AvaloniaTest]
        public void SettingsCampertron_LooksAtItsFilesWhenShown_AndLeavingItForSystemHidesThePassword()
        {
            var (Window, ViewModel) = Show();
            ShowSettingsTab(Window);
            SettingsViewModel Settings = ViewModel.Settings!;
            Settings.IsEmailSelected = true;
            Settings.Email.IsPasswordRevealed = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.Email.IsPasswordRevealed, Is.True);

            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Assert.That(Settings.Email.IsPasswordRevealed, Is.False, "leaving Campertron for System");
            ShowSettingsTab(Window);
            Settings.Email.IsPasswordRevealed = true;
            Tabs(Window).SelectedIndex = MainWindow.CampsitesTabIndex;
            Settle(Window);
            Assert.That(Settings.Email.IsPasswordRevealed, Is.False, "leaving Settings");

            // another program changes General.yaml while System is shown; coming back to Campertron shows it
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            File.WriteAllText(Path.Join(Folder, "General.yaml"), GeneralText.Replace("refreshRidbDataDayInterval: 30", "refreshRidbDataDayInterval: 9"));
            ShowSettingsTab(Window);
            Assert.That(Settings.General.RefreshInterval.Text, Is.EqualTo("9"), "Campertron looked at the file when it was shown");
            // and from another top-level tab
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            File.WriteAllText(Path.Join(Folder, "General.yaml"), GeneralText.Replace("refreshRidbDataDayInterval: 30", "refreshRidbDataDayInterval: 12"));
            Tabs(Window).SelectedIndex = MainWindow.SettingsTabIndex;
            Settle(Window);
            Assert.That(Settings.General.RefreshInterval.Text, Is.EqualTo("12"));
            Window.Close();
        }

        // ---- keyboard ----

        // With the keyboard: the top-level headers switch with the arrow keys, Tab enters a tab at its own row's header
        // (Settings, Campgrounds) or its content (Searches), and the arrow keys on a nested header switch only that row.
        // Shift+Tab walks back from the first field to the row's header and then to the top-level header; Tab from the last
        // field wraps to the top-level header (the TD-39 workaround on both levels).
        [AvaloniaTest]
        public void Keyboard_ReachesEveryRowAndTab_AndWrapsOnBothLevels()
        {
            var (Window, _) = Show();
            ((TabItem)Tabs(Window).SelectedItem!).Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.That(FocusName(Window), Is.EqualTo("Tab:Searches"));
            Press(Window, Key.Right, PhysicalKey.ArrowRight);
            Assert.That(FocusName(Window), Is.EqualTo("Tab:Results"));
            Press(Window, Key.Right, PhysicalKey.ArrowRight);
            Assert.That(FocusName(Window), Is.EqualTo("Tab:Settings"));
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SettingsTabIndex));

            // Settings: its row's header, then Campertron's first field
            Assert.That(PressTab(Window, 2), Is.EqualTo(new[] { "Tab:Campertron", "GeneralFileButton" }));
            Assert.That(PressTab(Window, 2, RawInputModifiers.Shift), Is.EqualTo(new[] { "Tab:Campertron", "Tab:Settings" }));
            PressTab(Window, 1);
            Press(Window, Key.Right, PhysicalKey.ArrowRight);
            Assert.That(SettingsTabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SystemSettingsTabIndex));
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SettingsTabIndex), "the nested row's arrows don't change the top-level tab");
            Assert.That(FocusName(Window), Is.EqualTo("Tab:System"));
            // System: the three themes, in order (the selectable folder text is not a stop), then since V011-2 the current
            // configuration's two actions (its values are selectable text, not stops), then since V011-1 Refresh campground
            // data… (its confirmation's buttons are hidden until it is pressed); the last stop wraps
            List<string?> System = PressTab(Window, 7);
            Assert.That(System, Is.EqualTo(new[] { "SystemThemeButton", "LightThemeButton", "DarkThemeButton", "CopyConfigurationButton", "OpenConfigFolderButton",
                "RefreshDatabaseButton", "Tab:Settings" }), string.Join(", ", System));
            Assert.That(PressTab(Window, 1), Is.EqualTo(new[] { "Tab:System" }), "the row remembers System");

            // Campgrounds: its row's header, then By Park's first selector
            ((TabItem)Tabs(Window).SelectedItem!).Focus(NavigationMethod.Tab);
            Press(Window, Key.Right, PhysicalKey.ArrowRight);
            Assert.That(FocusName(Window), Is.EqualTo("Tab:Campgrounds"));
            Assert.That(PressTab(Window, 2), Is.EqualTo(new[] { "Tab:By Park", "ParkComboBox" }));
            Assert.That(PressTab(Window, 1), Is.EqualTo(new[] { "Tab:Campgrounds" }), "the disabled campground selector is skipped and Tab wraps");
            Window.Close();
        }

        // ---- the lookup is still a lookup ----

        // Nothing in the navigation writes a file: walking through every tab and row changes nothing in the settings folder.
        [AvaloniaTest]
        public void WalkingThroughEveryTab_WritesNothing()
        {
            Dictionary<string, string> Before = Fingerprint(Folder);
            var (Window, _) = Show();
            foreach (int Top in new[] { MainWindow.SearchesTabIndex, MainWindow.ResultsTabIndex, MainWindow.SettingsTabIndex, MainWindow.CampsitesTabIndex })
            {
                Tabs(Window).SelectedIndex = Top;
                Settle(Window);
            }
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            for (int Index = 0; Index < ExpectedTabs.Length; Index++)
            {
                SelectTab(Window, Index);
            }
            Window.Close();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.That(Fingerprint(Folder), Is.EqualTo(Before));
            Assert.That(Directory.Exists(Path.Join(GuiTestEnvironment.Root, Campertron.ThemePreferenceStore.FolderName)), Is.False, "no theme preference was written");
        }
    }
}
