using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Forms
{
    // The shared form layer (D79's PR 3), at its own level: the Searches form and one search file's form as any UI drives
    // them. The words for autoRun (D63), what a number box refuses, the list's line for each file, the folder notice (also
    // by the monitoring start's own check, D73), the warning for a change that would leave the monitoring no search, New
    // search and Save As, and the filter selectors' campground values through the form's host. The campground data is a
    // list in memory and the selectors' values a function: no database is opened. Temporary folders only.
    public class SearchFormsTests : ConfigEditTestBase
    {
        private const string Campground = "232447";
        private static readonly CampgroundChoice UpperPines = new CampgroundChoice(Campground, "UPPER PINES", "YOSEMITE VALLEY", "CA");

        private static string Search(string Name, bool AutoRun, bool WithCampground = true) =>
            "displayName: " + Name + "\nautoRun: " + (AutoRun ? "true" : "false") + "\n" + (WithCampground ? "campgroundID: '" + Campground + "'\n" : string.Empty) +
            "totalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\nshowMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\n" +
            "showFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";

        private string In(string Name) => Path.Join(Folder, Name);

        // what the form's host would queue for the form's thread: run here, in order, when the test says so
        private readonly Queue<Action> Posted = new Queue<Action>();
        private int Loads;

        [SetUp]
        public void Reset()
        {
            Posted.Clear();
            Loads = 0;
        }

        private void RunPosted()
        {
            while (Posted.Count > 0)
            {
                Posted.Dequeue()();
            }
        }

        private FormHost Host() => new FormHost(() => Posted.Enqueue, (Due, Job) => throw new NotSupportedException(), (Interval, Job) => throw new NotSupportedException());

        private static CampgroundFilterChoices Choices(string Id) => new CampgroundFilterChoices(Id, 3,
            new[] { new FilterChoice("Tent", 3), new FilterChoice("RV", 1) }, new[] { new FilterChoice("STANDARD NONELECTRIC", 3) },
            new[] { new FilterChoice("001", 1), new FilterChoice("002", 1) },
            new[] { new FilterChoice("Shade:Yes", 2, "Shade", "Yes"), new FilterChoice("Shade:No", 1, "Shade", "No") });

        // the Searches form over the test's folder: the campground list in memory, the selectors' values read at once and
        // handed back through the host's post
        private SearchesViewModel Searches()
        {
            FormHost FormsHost = Host();
            return new SearchesViewModel(Folder, () => Now, () => new List<CampgroundPickerEntry> { new CampgroundPickerEntry(UpperPines, "Yosemite National Park") },
                () => new FilterChoiceSource(Id => { Loads++; return Choices(Id); }, () => "db", FormsHost.CapturePost(), Job => { Job(); return Task.CompletedTask; }));
        }

        [Test]
        public void TheWordsForAutoRun_AreEnabledAndNotEnabled_AndNeverSayThatASearchRunsAtAStart()
        {
            Assert.That(AutoRunWording.Enabled, Is.EqualTo("Enabled (autoRun)"));
            Assert.That(AutoRunWording.NotEnabled, Is.EqualTo("Not enabled"));
            Assert.That(AutoRunWording.Label, Is.EqualTo("En_abled (autoRun)"));
            Assert.That(AutoRunWording.Hint, Is.EqualTo("Enabled searches run each time you choose Search in the console and at every check of email mode, which reads "
                + "the search files when it starts (restart it after a change). The Results tab selects enabled searches by default and runs one that is "
                + "not enabled when you select it. Enabling a search doesn't make the GUI search by itself, and saving never runs a search."));
            Assert.That(AutoRunWording.EnabledNote, Is.EqualTo("The console's Search and email mode run it, and the Results tab selects it by default."));
            Assert.That(AutoRunWording.NotEnabledNote, Is.EqualTo("The console's Search and email mode skip it; the Results tab runs it only when you select it."));
            Assert.That(AutoRunWording.HasErrorsNote, Is.EqualTo("It has errors, so it can't be searched until they are fixed."));
            foreach (string Words in new[] { AutoRunWording.Hint, AutoRunWording.EnabledNote, AutoRunWording.NotEnabledNote, SearchesViewModel.NoSearchFilesNotice, SearchesViewModel.EnableNotice })
            {
                Assert.That(Words, Does.Not.Contain("at startup").And.Not.Contain("when Campertron starts"), Words);
            }
        }

        [Test]
        public void TheList_SaysEachFilesNameSettingCampgroundAndState()
        {
            Write(In("Alpha.yaml"), Search("First trip", true));
            Write(In("Bravo.yaml"), Search("Second trip", false));
            Write(In("Charlie.yaml"), "displayName: [not, a, name\n");
            SearchesViewModel Form = Searches();
            Assert.That(Form.IsLoaded, Is.False, "nothing is read before the form is shown");
            Form.Show();

            Assert.That(Form.Files.Select(F => F.FileName), Is.EqualTo(new[] { "Alpha.yaml", "Bravo.yaml", "Charlie.yaml" }));
            Assert.That(Form.SelectedFile, Is.SameAs(Form.Files[0]), "the first file is shown");
            SearchFileViewModel Alpha = Form.Files[0], Bravo = Form.Files[1], Charlie = Form.Files[2];
            Assert.That(Alpha.Title, Is.EqualTo("First trip"));
            Assert.That(Alpha.ListDetails, Is.EqualTo("First trip · Enabled (autoRun) · " + UpperPines.Label));
            Assert.That(Alpha.ListStatus, Is.EqualTo("No problems"));
            Assert.That(Alpha.ListRunNote, Is.EqualTo(AutoRunWording.EnabledNote));
            Assert.That(Alpha.ListAccessibleName, Is.EqualTo("Alpha.yaml, " + Alpha.ListDetails + ", No problems. " + AutoRunWording.EnabledNote));
            Assert.That(Bravo.ListDetails, Is.EqualTo("Second trip · Not enabled · " + UpperPines.Label));
            Assert.That(Bravo.ListRunNote, Is.EqualTo(AutoRunWording.NotEnabledNote));
            // a file whose YAML can't be loaded is shown read-only: its setting was not read, and nothing says that it "runs"
            Assert.That(Charlie.ListDetails, Is.EqualTo("(no name) · not read · no campground"));
            Assert.That(Charlie.ListStatus, Is.EqualTo("Read-only"));
            Assert.That(Charlie.ListRunNote, Is.Empty);
            Assert.That(Charlie.ListStatusIsError, Is.True);
            Assert.That(Charlie.IsEditable, Is.False);

            // an edit shows in the list at once; a search with errors is told as such, never as whether it "runs"
            Alpha.DisplayName.Text = "Renamed";
            Assert.That(Alpha.ListDetails, Does.StartWith("Renamed · "));
            Assert.That(Alpha.ListStatus, Is.EqualTo("No problems, unsaved changes"));
            Assert.That(Form.IsUnsaved("Alpha.yaml"), Is.True);
            Assert.That(Form.IsUnsaved("Bravo.yaml"), Is.False);
        }

        [Test]
        public void ANumberBox_RefusesWhatIsNoWholeNumber_WithItsOwnWords_AndBlocksSave()
        {
            Write(In("Alpha.yaml"), Search("First trip", true));
            SearchesViewModel Form = Searches();
            Form.Show();
            SearchFileViewModel File_ = Form.SelectedFile!;

            File_.TotalHumans.Text = "two";
            Assert.That(File_.TotalHumans.MessageText, Is.EqualTo("Error: Enter a number of people, such as 2."));
            File_.ConsecutiveDays.Text = "1.5";
            Assert.That(File_.ConsecutiveDays.MessageText, Is.EqualTo("Error: Enter a whole number of nights, such as 1."));
            File_.SearchValue.Text = "";
            Assert.That(File_.SearchValue.MessageText, Is.EqualTo("Error: Enter a whole number, such as 7."));
            Assert.That(File_.CanSave, Is.False);
            Assert.That(File_.IsDirty, Is.False, "none of them reached the file's session");

            File_.TotalHumans.Text = " 4 ";
            File_.ConsecutiveDays.Text = "2";
            File_.SearchValue.Text = "14";
            Assert.That(File_.CanSave, Is.True);
            File_.Save();
            Assert.That(File_.StatusIsError, Is.False, File_.StatusText);
            Assert.That(Text(In("Alpha.yaml")), Does.Contain("totalHumans: 4\n").And.Contain("consecutiveDays: 2\n").And.Contain("searchValue: 14\n"));

            Assert.That(File_.SearchBy.Choices.Select(C => C.Label), Is.EqualTo(new[]
            {
                "DaysOut: from today, a number of days ahead", "MonthsOut: from today, a number of months ahead", "SpecificDates: the dates listed",
                "Until: from today until the first date listed", "StartEndDate: from the first date listed to the second",
            }));
            Assert.That(File_.Matching.Selected!.Label, Is.EqualTo("Contains (not set in this file: a search file without filterMatching uses Contains)"));
            Assert.That(File_.AutoRun.Label, Is.EqualTo(AutoRunWording.Label));
            Assert.That(File_.AutoRun.Hint, Is.EqualTo(AutoRunWording.Hint));
        }

        [Test]
        public void TheFolderNotice_SaysWhatTheConsoleAndTheMonitoringWouldFind()
        {
            SearchesViewModel Form = Searches();
            Form.Show();
            Assert.That(Form.HasNoFiles, Is.True);
            Assert.That(Form.FolderNotice, Is.EqualTo("There is no search file. Create one with New search. While there is none, the console's Search creates "
                + "and runs its example search the next time you choose it, and email mode stops with an error."));

            Write(In("Bravo.yaml"), Search("Second trip", false));
            Form.CheckForExternalChanges();
            Assert.That(Form.FolderNoticeText, Does.StartWith("Warning: ").And.EndWith(" To enable a search, check Enabled (autoRun) and save it."));

            // D73: with the monitoring's outputTo shown or saved, the notice is its start's own check
            Form.SetOutput(OutputType.Console, OutputType.Ntfy);
            Assert.That(Form.MonitoringOutput, Is.EqualTo(OutputType.Ntfy));
            Assert.That(Form.FolderNoticeText, Is.EqualTo("Warning: "
                + string.Join(" ", ConfigValidation.MonitoringSearchErrors(Folder, OutputType.Ntfy).Select(D => D.Message).Distinct())
                + " Until then, ntfy mode (outputTo: Ntfy) refuses to start. Searches can still be edited, saved and run in the Results tab."));
            Form.SetOutput(OutputType.Email, OutputType.Ntfy);
            Assert.That(Form.MonitoringOutput, Is.EqualTo(OutputType.Email), "the shown outputTo first");
            Assert.That(Form.FolderNoticeText, Does.Contain("email mode (outputTo: Email) refuses to start"));

            Write(In("Alpha.yaml"), Search("First trip", true));
            Form.CheckForExternalChanges();
            Assert.That(Form.HasFolderNotice, Is.False, "an enabled search with a campground: nothing to say");
        }

        [Test]
        public void D73_AChangeThatWouldLeaveTheMonitoringNoSearch_IsAWarningThatNeverBlocksSave()
        {
            Write(In("Alpha.yaml"), Search("First trip", true));
            SearchesViewModel Form = Searches();
            Form.SetOutput(OutputType.Email, OutputType.Email);
            Form.Show();
            SearchFileViewModel File_ = Form.SelectedFile!;
            Assert.That(File_.HasMonitoringWarning, Is.False);

            File_.AutoRun.IsChecked = false;
            string After = string.Join(" ", ConfigValidation.MonitoringSearchErrors(Folder, OutputType.Email,
                P => Path.GetFileName(P) == "Alpha.yaml" ? File_.Session.Candidate : null).Select(D => D.Message).Distinct());
            Assert.That(File_.MonitoringWarningText, Is.EqualTo("Warning: With this change saved, email mode (outputTo: Email) would refuse to start. " + After
                + " This warning doesn't block saving."));
            Assert.That(File_.CanSave, Is.True);

            File_.AutoRun.IsChecked = true;
            Assert.That(File_.HasMonitoringWarning, Is.False);

            // without the monitoring's outputTo there is no such warning
            Form.SetOutput(OutputType.Console, OutputType.Console);
            File_.AutoRun.IsChecked = false;
            Assert.That(File_.HasMonitoringWarning, Is.False);
        }

        [Test]
        public void NewSearchAndSaveAs_CreateOnly_AndSayWhatHappened()
        {
            Write(In("Alpha.yaml"), Search("First trip", true));
            SearchesViewModel Form = Searches();
            Form.Show();

            Form.NewSearchName = "Alpha";
            Form.NewSearch();
            Assert.That(Form.NewSearchIsError, Is.True, "never over a file that exists");
            Assert.That(Text(In("Alpha.yaml")), Is.EqualTo(Search("First trip", true)));

            Form.NewSearchName = "Weekend";
            Form.NewSearch();
            Assert.That(Form.NewSearchIsError, Is.False, Form.NewSearchMessage);
            Assert.That(File.Exists(In("Weekend.yaml")), Is.True);
            Assert.That(Form.SelectedFile!.FileName, Is.EqualTo("Weekend.yaml"));
            Assert.That(Form.SelectedFile.IsEnabledSearch, Is.False, "a new search is not enabled");
            Assert.That(Form.NewSearchName, Is.Empty);

            Form.SelectedFile = Form.Files.Single(F => F.FileName == "Alpha.yaml");
            Form.SelectedFile.DisplayName.Text = "Copied trip";
            Form.SaveAsName = "Copy";
            Form.SaveAs();
            Assert.That(Form.SaveAsIsError, Is.False, Form.SaveAsMessage);
            Assert.That(Text(In("Copy.yaml")), Does.Contain("displayName: Copied trip"));
            Assert.That(Text(In("Alpha.yaml")), Is.EqualTo(Search("First trip", true)), "the file the edits were made on keeps what is on disk");
            Assert.That(Form.SelectedFile!.FileName, Is.EqualTo("Copy.yaml"));
            Assert.That(Form.Files.Single(F => F.FileName == "Alpha.yaml").IsDirty, Is.False);
        }

        [Test]
        public void TheSelectorsValues_AreReadForTheFileShown_AndHandedBackThroughTheHost()
        {
            Write(In("Alpha.yaml"), Search("First trip", true) + "includeEquipment:\n- Tent\nincludeAttributes:\n- Shade:Yes\n");
            Write(In("Bravo.yaml"), Search("Second trip", false, WithCampground: false));
            SearchesViewModel Form = Searches();
            Form.Show();
            SearchFileViewModel Alpha = Form.Files[0], Bravo = Form.Files[1];

            // read on the worker (here at once); shown only once the host ran the posted job on the form's thread
            Assert.That(Loads, Is.EqualTo(1));
            Assert.That(Alpha.IncludeEquipment.Status, Is.EqualTo(FilterChoicesStatus.Loading));
            Assert.That(Posted, Has.Count.EqualTo(1));
            RunPosted();
            Assert.That(Alpha.IncludeEquipment.Status, Is.EqualTo(FilterChoicesStatus.Loaded));
            Assert.That(Alpha.IncludeEquipment.Options.Select(O => O.Label), Is.EquivalentTo(new[] { "Tent", "RV" }));
            Assert.That(Alpha.IncludeEquipment.Options.Single(O => O.Label == "Tent").IsChecked, Is.True);

            // an attribute's values are one level under their name's heading; a UI indents by the level
            List<FilterOptionRow> Shade = Alpha.IncludeAttributes.Options.ToList();
            Assert.That(Shade[0].IsHeading, Is.True);
            Assert.That(Shade[0].Level, Is.EqualTo(0));
            Assert.That(Shade.Skip(1).Select(O => O.Level), Is.All.EqualTo(1));

            // checking a value is an edit of the file's list
            Alpha.IncludeEquipment.Toggle(Alpha.IncludeEquipment.Options.Single(O => O.Label == "RV"));
            Assert.That(Alpha.IsDirty, Is.True);
            Assert.That(Alpha.Session.Items("includeEquipment").Select(I => I.Value), Is.EqualTo(new[] { "Tent", "RV" }));

            // a file that is only listed reads nothing; one without a campground has no values to offer
            Assert.That(Bravo.IncludeEquipment.Status, Is.Not.EqualTo(FilterChoicesStatus.Loaded));
            Form.SelectedFile = Bravo;
            Assert.That(Bravo.IncludeEquipment.Status, Is.EqualTo(FilterChoicesStatus.NoCampground));
            Assert.That(Loads, Is.EqualTo(1));
        }
    }
}
