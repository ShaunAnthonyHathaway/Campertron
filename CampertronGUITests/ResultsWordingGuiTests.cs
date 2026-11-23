using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.Views;
using static CampertronGUITests.GuiTestHelpers;
using static CampertronGUITests.ResultsTestHelpers;

namespace CampertronGUITests
{
    // The Results tab's own words (v0.3.0's PR 7, D97): its view shows ResultsWording, the words the web UI's results page
    // shows, written once in the shared form layer (ER-37) and unchanged for the desktop GUI: the heading, Start and
    // Cancel with their names and tips, the list's name and its empty state, the three choice buttons with their help, the
    // progress bar's name, the results' label with its access key and the names of the summary and the list, and the
    // campsite's three actions. A real window on the headless platform, in folders of its own; a run through a test
    // transport (never recreation.gov) shows the results' card.
    public class ResultsWordingGuiTests
    {
        private ResultsFolders Folders = null!;

        [SetUp]
        public void CreateFolders() => Folders = new ResultsFolders();

        [TearDown]
        public void DeleteFolders() => Folders.Dispose();

        [AvaloniaTest]
        public void TheResultsTab_ShowsTheSharedWords_ExactlyAsBefore()
        {
            Folders.Write("Trip.yaml", ResultsFolders.Search("Trip", AutoRun: true));
            var (Window, ViewModel) = Folders.ShowWindow(Seams(new TestGov(), new ManualTimers()));
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Run(ViewModel.Results!);
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Control View = Named<ResultsView>(Window, "ResultsContent");
            T Find<T>(string Name) where T : Control => View.GetVisualDescendants().OfType<T>().First(C => C.Name == Name);
            List<string> Texts = View.GetVisualDescendants().OfType<TextBlock>().Select(T => T.Text ?? string.Empty).ToList();

            //the words the view held before PR 7 moved them into the layer, as literals: the view shows the same
            Assert.That(Texts, Does.Contain("Searches to run").And.Contain(ResultsWording.RunHeading));
            Button Start = Find<Button>("StartButton");
            Assert.That((Start.Content, AutomationProperties.GetName(Start), ToolTip.GetTip(Start)),
                Is.EqualTo(("Start", "Start search", "Search the chosen search files now, at recreation.gov")));
            Button Cancel = Find<Button>("CancelButton");
            Assert.That((AutomationProperties.GetName(Cancel), ToolTip.GetTip(Cancel)),
                Is.EqualTo(("Cancel search", "Stop the running search; searches that already finished keep their results")));
            Assert.That(AutomationProperties.GetName(Find<ScrollViewer>("SearchChoicesScroll")), Is.EqualTo("Search files to run"));
            Assert.That(Find<TextBlock>("NoSearchesText").Text, Is.EqualTo("There are no search files in the settings folder. Create one in the Searches tab."));
            Button Enabled = Find<Button>("SelectEnabledButton");
            Assert.That((Enabled.Content, AutomationProperties.GetName(Enabled), AutomationProperties.GetHelpText(Enabled), ToolTip.GetTip(Enabled)),
                Is.EqualTo(("Select enabled", "Select the enabled searches",
                    "Selects the enabled searches (autoRun: true) that can be searched now and clears the others: the default choice. Nothing runs until Start.",
                    "Select the enabled searches (autoRun: true) that can be searched now and clear the others: the default choice")));
            Button All = Find<Button>("SelectAllButton");
            Assert.That((All.Content, AutomationProperties.GetHelpText(All), ToolTip.GetTip(All)),
                Is.EqualTo(("Select all that can run", "Selects every search that can be searched now, enabled or not. Nothing runs until Start.",
                    "Select every search that can be searched now, enabled or not")));
            Assert.That(Find<Button>("ClearSelectionButton").Content, Is.EqualTo("Clear selection"));
            Assert.That(AutomationProperties.GetName(Find<ProgressBar>("SearchProgressBar")), Is.EqualTo("Search progress"));

            //the results' card
            Assert.That(View.GetVisualDescendants().OfType<Label>().Select(L => L.Content as string), Does.Contain("_Results of"), "the label keeps its access key");
            Assert.That(AutomationProperties.GetName(Find<ComboBox>("ResultSearchChoice")), Is.EqualTo("Results of the search"));
            Assert.That(AutomationProperties.GetName(Find<ScrollViewer>("ResultSummaryScroll")), Is.EqualTo("Summary of the search"));
            Assert.That(AutomationProperties.GetName(Find<ListBox>("ResultRowsList")), Is.EqualTo("Openings found"));
            Button Open = Find<Button>("OpenBookingButton");
            Assert.That((Open.Content, AutomationProperties.GetName(Open), ToolTip.GetTip(Open)),
                Is.EqualTo(("Open booking page", "Open the booking page of the chosen campsite", "Opens the campsite's page at recreation.gov in your browser")));
            Button CopyId = Find<Button>("CopyIdButton");
            Assert.That((CopyId.Content, AutomationProperties.GetName(CopyId)), Is.EqualTo(("Copy campsite ID", "Copy the chosen campsite's ID")));
            Button CopyLink = Find<Button>("CopyLinkButton");
            Assert.That((CopyLink.Content, AutomationProperties.GetName(CopyLink)), Is.EqualTo(("Copy booking link", "Copy the chosen campsite's booking link")));
            Assert.That(ViewModel.Results!.ResultSearches, Is.Not.Empty, "the run's results are shown");
            Window.Close();
        }
    }
}
