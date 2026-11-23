using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Forms
{
    // What v0.3.0's PR 6 (D96) added to the shared form layer for the web UI's searches pages, at the layer's own level:
    // the Searches tab's own words written once (SearchesWording, which both UIs show), the picker's first matches for a UI
    // without a virtualized list and its choice by exact ID, the hints shown apart from their messages, the count of a
    // form's values replaced after Save As (Generation), and the campground data's preparation that every start uses
    // (Load.PrepareDatabase). Temporary folders only; the campground list is in memory.
    public class SearchesPagesFormsTests : ConfigEditTestBase
    {
        private const string Campground = "232447";

        private static string Search(string Name) =>
            "displayName: " + Name + "\nautoRun: true\ncampgroundID: '" + Campground + "'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";

        private SearchesViewModel Searches(int Campgrounds = 3)
        {
            var Entries = new List<CampgroundPickerEntry> { new CampgroundPickerEntry(new CampgroundChoice(Campground, "UPPER PINES", "YOSEMITE VALLEY", "CA"), "Yosemite National Park") };
            for (int i = 1; i < Campgrounds; i++)
            {
                Entries.Add(new CampgroundPickerEntry(new CampgroundChoice((300000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), "MEADOW " + i, null, "OR"), null));
            }
            return new SearchesViewModel(Folder, () => Now, () => Entries,
                () => new FilterChoiceSource(Id => CampgroundFilterChoices.None(Id), () => "db", Job => Job(), Job => { Job(); return Task.CompletedTask; }));
        }

        [Test]
        public void TheSearchesTabsOwnWords_AreWrittenOnce_AsTheDesktopGUIHadThem()
        {
            //the words the desktop GUI's views held before PR 6 moved them here (its views now bind these; a GUI test checks)
            Assert.That(SearchesWording.FilesHeading, Is.EqualTo("Search files"));
            Assert.That(SearchesWording.NewSearchLabel, Is.EqualTo("_New search file"));
            Assert.That(SearchesWording.Plain(SearchesWording.NewSearchLabel), Is.EqualTo("New search file"));
            Assert.That(SearchesWording.NewSearchHelp, Is.EqualTo("The file is created in the settings folder with .yaml added, from the example search, not enabled (autoRun: false)."));
            Assert.That(SearchesWording.NewSearchNote, Does.EndWith("Search files are never deleted here."));
            Assert.That(SearchesWording.SaveAsLabel, Is.EqualTo("Save as a ne_w search file"));
            Assert.That(SearchesWording.Plain(SearchesWording.CampgroundIdLabel), Is.EqualTo("Or enter a campground ID"));
            Assert.That(SearchesWording.FiltersExplanation, Does.StartWith("An include list keeps a campsite that matches any of its values;"));
            Assert.That(SearchesWording.FindPlaceholder, Is.EqualTo("Find…"));
            foreach (string Words in new[] { SearchesWording.NewSearchNote, SearchesWording.NewSearchHelp, SearchesWording.SaveAsNote, SearchesWording.FiltersExplanation })
            {
                Assert.That(Words, Does.Not.Contain("at startup").And.Not.Contain("when Campertron starts"), Words);
            }
        }

        [Test]
        public void ThePicker_ListsItsFirstMatches_SaysHowToNarrowThem_AndChoosesOnlyAnExactListedId()
        {
            Write(Path.Join(Folder, "Alpha.yaml"), Search("First trip"));
            SearchesViewModel Form = Searches(Campgrounds: 120);
            Form.Show();
            CampgroundSettingField Picker = Form.SelectedFile!.Campground;
            Assert.That(Picker.Matches, Has.Count.EqualTo(120));
            Assert.That(Picker.FirstMatches(50).Select(M => M.FacilityID), Is.EqualTo(Picker.Matches.Take(50).Select(M => M.FacilityID)));
            Assert.That(Picker.MoreMatchesText(50), Is.EqualTo("The first 50 are listed. Type a name, city, state, park or an exact ID to narrow the list."));
            Assert.That(Picker.MoreMatchesText(120), Is.Null, "nothing to say when every match is listed");

            Picker.SelectMatch("300007");
            Assert.That(Picker.SelectedMatch?.FacilityID, Is.EqualTo("300007"));
            Picker.SelectMatch(" 300008");
            Picker.SelectMatch("30000");
            Picker.SelectMatch(null);
            Assert.That(Picker.SelectedMatch?.FacilityID, Is.EqualTo("300007"), "no exact listed ID chooses nothing and keeps the choice");
            Picker.FilterText = "meadow 9";
            Picker.SelectMatch("300007");
            Assert.That(Picker.SelectedMatch, Is.Null, "an ID the list does not show now is not chosen");
            Assert.That(Form.SelectedFile.IsDirty, Is.False, "choosing in the list writes nothing until Use");

            Assert.That(Picker.TypedIdHint, Is.EqualTo("A campground ID exactly as Recreation.gov writes it, such as 232445."));
            Assert.That(Picker.TypedIdHelpText, Is.EqualTo(Picker.TypedIdHint));
            Picker.TypedId = "nope";
            Picker.UseTypedId();
            Assert.That(Picker.TypedIdHelpText, Is.EqualTo("Error: " + Picker.TypedIdMessage), "the desktop GUI's help text is unchanged");
            Assert.That(Picker.TypedIdHint, Is.EqualTo("A campground ID exactly as Recreation.gov writes it, such as 232445."), "the hint is the box's alone");
        }

        [Test]
        public void ASelectorsHint_IsItsHelpWithoutTheMessage()
        {
            Write(Path.Join(Folder, "Alpha.yaml"), Search("First trip"));
            SearchesViewModel Form = Searches();
            Form.Show();
            FilterSelectorField Selector = Form.SelectedFile!.IncludeEquipment;
            Assert.That(Selector.CustomHelpText, Is.EqualTo(Selector.CustomHint));
            Assert.That(Selector.CustomHint, Does.StartWith("A value exactly as you type it, kept even when the campground does not have it. "));
            Selector.CustomText = "   ";
            Selector.AddCustom();
            Assert.That(Selector.CustomHelpText, Is.EqualTo("Error: Type a value first. " + Selector.CustomHint));
        }

        [Test]
        public void SaveAs_CountsTheSourcesValuesAsReplaced_SoAPageThatShowedTheEditsIsOutOfDate()
        {
            Write(Path.Join(Folder, "Alpha.yaml"), Search("First trip"));
            SearchesViewModel Form = Searches();
            Form.Show();
            SearchFileViewModel Alpha = Form.SelectedFile!;
            Alpha.DisplayName.Text = "Copied trip";
            int Before = Alpha.Generation;
            Form.SaveAsName = "Copy";
            Form.SaveAs();
            Assert.That(Form.SaveAsIsError, Is.False, Form.SaveAsMessage);
            Assert.That(Alpha.Generation, Is.EqualTo(Before + 1), "the source's values were replaced by what is on disk");
            Assert.That(Alpha.DisplayName.Text, Is.EqualTo("First trip"));
            Assert.That(Form.SelectedFile!.FileName, Is.EqualTo("Copy.yaml"));
        }

        [Test]
        public void PrepareDatabase_ExtractsTheBundledDataOnlyWhenThereIsNone_AndRunsTheStartsStepOnlyForAnExistingOne()
        {
            string Data = Path.Join(Folder, "RecreationDotOrg.db");
            int Existing = 0;
            Load.PrepareDatabase(Folder, null, () => Existing++);
            Assert.That(File.Exists(Data), Is.True, "the bundled database, extracted");
            Assert.That(Existing, Is.Zero, "a database just extracted is not refreshed");
            Assert.That(Directory.GetFiles(Folder, "*.partial-*"), Is.Empty, "no partial file is left");
            DateTime Written = File.GetLastWriteTimeUtc(Data);
            long Length = new FileInfo(Data).Length;

            Load.PrepareDatabase(Folder, null, () => Existing++);
            Assert.That(Existing, Is.EqualTo(1), "an existing database gets the start's own step (the automatic refresh's check)");
            Assert.That(new FileInfo(Data).Length, Is.EqualTo(Length), "and is not extracted again");
            Assert.That(File.GetLastWriteTimeUtc(Data), Is.GreaterThanOrEqualTo(Written));
            Load.PrepareDatabase(Folder, null);
            Assert.That(Existing, Is.EqualTo(1), "without a step, none");
        }
    }
}
