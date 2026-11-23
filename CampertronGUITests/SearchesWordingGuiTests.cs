using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The Searches tab's own words (v0.3.0's PR 6, D96): its views show SearchesWording, the words the web UI's searches
    // pages show, written once in the shared form layer (ER-37) and unchanged for the desktop GUI: the headings, the labels
    // with their access keys, the boxes' placeholders and names, the buttons and the notes. A real window on the headless
    // platform with the fixture database, in a settings folder of its own.
    public class SearchesWordingGuiTests
    {
        private string Folder = string.Empty;
        private string In(string Name) => Path.Join(Folder, Name);

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "searches-words-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(In("General.yaml"), "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            File.WriteAllText(In("Email.yaml"), "smtpServer: ''\nsmtpPort: 0\n");
            File.WriteAllText(In("Trip.yaml"), "displayName: Trip\nautoRun: false\ncampgroundID: '" + GuiTestEnvironment.CampgroundAlphaID + "'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
                "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n" +
                "includeSites: []\n");
            File.Copy(GuiTestEnvironment.DbFile, In("RecreationDotOrg.db"), overwrite: true);
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

        [AvaloniaTest]
        public void TheSearchesTab_ShowsTheSharedWords_ExactlyAsBefore()
        {
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Dispatcher.UIThread.RunJobs();
            SearchesViewModel Searches = ViewModel.Searches!;
            Searches.SelectedFile = Searches.Files.Single(F => F.FileName == "Trip.yaml");
            WaitUntil(() => Searches.SelectedFile.Selectors.All(S => S.Status != FilterChoicesStatus.Loading), () => "the campground's values were read");
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Control View = Named<SearchesView>(Window, "SearchesContent");
            T Find<T>(string Name) where T : Control => View.GetVisualDescendants().OfType<T>().First(C => C.Name == Name);
            List<string> Texts = View.GetVisualDescendants().OfType<TextBlock>().Select(T => T.Text ?? string.Empty).ToList();

            //the list's card and New search
            Assert.That(Texts, Does.Contain(SearchesWording.FilesHeading).And.Contain(SearchesWording.NewSearchNote));
            TextBox NewName = Find<TextBox>("NewSearchNameBox");
            Assert.That(NewName.PlaceholderText, Is.EqualTo(SearchesWording.NewSearchPlaceholder));
            Assert.That(AutomationProperties.GetName(NewName), Is.EqualTo(SearchesWording.NewSearchName));
            Assert.That(AutomationProperties.GetHelpText(NewName), Is.EqualTo(SearchesWording.NewSearchHelp));
            Assert.That(Find<Button>("NewSearchButton").Content, Is.EqualTo(SearchesWording.CreateSearch));
            Assert.That(Find<Button>("CheckSearchFilesButton").Content, Is.EqualTo(SearchesWording.LookForChanges));
            Assert.That(ToolTip.GetTip(Find<Button>("CheckSearchFilesButton")), Is.EqualTo(SearchesWording.LookForChangesHelp));
            Assert.That(View.GetVisualDescendants().OfType<Label>().Select(L => L.Content as string), Does.Contain(SearchesWording.NewSearchLabel).And.Contain(SearchesWording.SaveAsLabel)
                .And.Contain(SearchesWording.FindCampgroundLabel).And.Contain(SearchesWording.CampgroundIdLabel), "the labels keep their access keys");

            //the editor's groups, the picker and Save As
            Assert.That(Texts, Does.Contain(SearchesWording.DatesHeading).And.Contain(SearchesWording.WeekdaysHeading).And.Contain(SearchesWording.WeekdaysNote)
                .And.Contain(SearchesWording.FiltersHeading).And.Contain(SearchesWording.FiltersExplanation).And.Contain(SearchesWording.EquipmentHeading)
                .And.Contain(SearchesWording.CampsitesHeading).And.Contain(SearchesWording.CampsiteTypesHeading).And.Contain(SearchesWording.AttributesHeading)
                .And.Contain(SearchesWording.SaveAsNote));
            TextBox Find_ = Find<TextBox>("CampgroundFilterBox");
            Assert.That((Find_.PlaceholderText, AutomationProperties.GetName(Find_), AutomationProperties.GetHelpText(Find_)),
                Is.EqualTo((SearchesWording.FindCampgroundPlaceholder, SearchesWording.FindCampgroundName, SearchesWording.FindCampgroundHelp)));
            Assert.That(AutomationProperties.GetName(Find<ListBox>("CampgroundList")), Is.EqualTo(SearchesWording.CampgroundsName));
            Assert.That(Find<Button>("UseCampgroundButton").Content, Is.EqualTo(SearchesWording.UseSelectedCampground));
            Assert.That(AutomationProperties.GetName(Find<TextBox>("CampgroundIdBox")), Is.EqualTo(SearchesWording.CampgroundIdName));
            Assert.That(Find<Button>("UseCampgroundIdButton").Content, Is.EqualTo(SearchesWording.UseCampgroundId));
            Assert.That(Find<Button>("RetryChoicesButton").Content, Is.EqualTo(SearchesWording.RetryChoices));
            TextBox SaveAs = Find<TextBox>("SaveAsNameBox");
            Assert.That((AutomationProperties.GetName(SaveAs), AutomationProperties.GetHelpText(SaveAs)), Is.EqualTo((SearchesWording.SaveAsName, SearchesWording.SaveAsHelp)));
            Assert.That(Find<Button>("SaveAsButton").Content, Is.EqualTo(SearchesWording.SaveAsButton));

            //a selector's own controls
            FilterSelectorView Sites = View.GetVisualDescendants().OfType<FilterSelectorView>().First(V => V.Name == "IncludeSitesSelector");
            T InSites<T>(string Name) where T : Control => Sites.GetVisualDescendants().OfType<T>().First(C => C.Name == Name);
            Assert.That((InSites<TextBox>("FindBox").PlaceholderText, AutomationProperties.GetHelpText(InSites<TextBox>("FindBox"))), Is.EqualTo((SearchesWording.FindPlaceholder, SearchesWording.FindHelp)));
            Assert.That((InSites<Button>("SelectShownButton").Content, InSites<Button>("ClearShownButton").Content), Is.EqualTo((SearchesWording.SelectShown, SearchesWording.ClearShown)));
            Assert.That((InSites<TextBox>("CustomBox").PlaceholderText, InSites<Button>("AddCustomButton").Content), Is.EqualTo((SearchesWording.CustomPlaceholder, SearchesWording.Add)));
            Window.Close();
        }
    }
}
