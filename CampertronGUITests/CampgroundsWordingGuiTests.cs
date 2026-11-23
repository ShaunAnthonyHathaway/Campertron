using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The Campgrounds tab's and Settings > System's words that the web UI's campgrounds and system pages show too (v0.3.0's
    // PR 9, D99): the desktop GUI's views bind CampgroundsWording, RefreshWording and SystemWording, written once in the
    // shared form layer (ER-37), and show exactly the words they held before: the tabs, the labels with their access keys,
    // the placeholders, what each tab asks for, a campground's details and the campground data's card. A real window on
    // the headless platform with the fixture database.
    public class CampgroundsWordingGuiTests
    {
        [AvaloniaTest]
        public void TheCampgroundsTabAndTheSystemCards_ShowTheSharedWords_ExactlyAsBefore()
        {
            var (Window, ViewModel) = ShowMainWindow();
            Assert.That(Headers(Tabs(Window))[MainWindow.CampsitesTabIndex], Is.EqualTo("Campgrounds").And.EqualTo(CampgroundsWording.Heading));
            Assert.That(Headers(CampsitesTabs(Window)), Is.EqualTo(new[] { "By Park", "By City", "By State" }));
            Assert.That(Headers(CampsitesTabs(Window)), Is.EqualTo(new[] { CampgroundsWording.ByPark, CampgroundsWording.ByCity, CampgroundsWording.ByState }));

            var Labels = new List<string>();
            var Placeholders = new List<string>();
            for (int Index = 0; Index < 3; Index++)
            {
                Grid Content = SelectTab(Window, Index);
                Labels.AddRange(Content.GetVisualDescendants().OfType<Label>().Select(L => L.Content as string ?? string.Empty));
                Placeholders.AddRange(Content.GetVisualDescendants().OfType<ComboBox>().Select(C => C.PlaceholderText ?? string.Empty));
                Assert.That(Details(Window, Index).EmptyHint, Is.EqualTo(new[] { CampgroundsWording.ByParkHint, CampgroundsWording.ByCityHint, CampgroundsWording.ByStateHint }[Index]));
            }
            Assert.That(Labels, Is.EqualTo(new[] { "_Park", "_Campground", "_State", "C_ity", "_Campground", "_State", "_Campground" }));
            Assert.That(Placeholders, Is.EqualTo(new[]
            {
                "Select or type a park", "Select or type a campground", "Select or type a state", "Select or type a city", "Select or type a campground",
                "Select or type a state", "Select or type a campground",
            }));
            Assert.That(Details(Window, 0).EmptyHint, Is.EqualTo("Choose a park, then one of its campgrounds, to see the campground ID and details."));
            Assert.That(DisplayConverters.NoCityListed, Is.EqualTo("(No city listed)").And.EqualTo(CampgroundsWording.NoCityListed));

            //a campground's details: the labels and the missing values
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkIdentity);
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.TwinUtahID);
            Dispatcher.UIThread.RunJobs();
            List<string> Texts = Details(Window, 0).GetVisualDescendants().OfType<TextBlock>().Select(T => T.Text ?? string.Empty).ToList();
            Assert.That(Texts, Is.SupersetOf(new[] { "Campground ID", "Use this number as the campgroundID in a search file.", "Contact", "Phone", "Email", "Description", "Directions", "Fees" }));
            Assert.That(Named<ScrollViewer>(Details(Window, 0), "DetailsScrollViewer").GetValue(Avalonia.Automation.AutomationProperties.NameProperty), Is.EqualTo("Campground details"));
            Assert.That(Details(Window, 0).DataContext, Is.InstanceOf<CampgroundDetails>(), "the shared form layer's snapshot");

            //Settings > System: the current configuration's and the campground data's card's words
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Control System = Named<SystemSettingsView>(Window, "SystemSettingsContent");
            List<string> SystemTexts = System.GetVisualDescendants().OfType<TextBlock>().Select(T => T.Text ?? string.Empty).ToList();
            Assert.That(SystemTexts, Is.SupersetOf(new[] { "Current configuration", "Campground data", "Last refreshed", SystemWording.ConfigurationIntro, RefreshWording.Explanation }));
            Assert.That(SystemWording.ConfigurationIntro, Does.StartWith("What the console's View Config shows: this version, the settings and cache folders,"));
            Button Refresh = System.GetVisualDescendants().OfType<Button>().Single(B => B.Name == "RefreshDatabaseButton");
            Assert.That(Refresh.Content, Is.EqualTo("Refresh campground data…").And.EqualTo(RefreshWording.RefreshLabel));
            Assert.That(Refresh.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), Is.EqualTo("Refresh campground data"));
            Button Start = System.GetVisualDescendants().OfType<Button>().Single(B => B.Name == "StartRefreshButton");
            Assert.That(Start.Content, Is.EqualTo("Start refresh"));
            Button Back = System.GetVisualDescendants().OfType<Button>().Single(B => B.Name == "RefreshBackButton");
            Assert.That((Back.Content, Back.GetValue(Avalonia.Automation.AutomationProperties.NameProperty)), Is.EqualTo(("Back", "Back, don't refresh")));
            Button Cancel = System.GetVisualDescendants().OfType<Button>().Single(B => B.Name == "CancelRefreshButton");
            Assert.That(Cancel.GetValue(Avalonia.Automation.AutomationProperties.NameProperty), Is.EqualTo("Cancel the refresh"));
            Assert.That(System.GetVisualDescendants().OfType<ProgressBar>().Single().GetValue(Avalonia.Automation.AutomationProperties.NameProperty), Is.EqualTo("Campground data refresh progress"));
            Window.Close();
            _ = ViewModel;
        }
    }
}
