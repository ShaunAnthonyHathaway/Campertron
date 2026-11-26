using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Forms
{
    // The problems a form shows (v0.4.0's phase 3, D117; until then composed by the web UI's WebSettingsForm.Errors): every
    // field, row, value and box in error, then the file's own errors, in the form's order and in the words a list of them
    // says; each with where it belongs, which a host turns into its own element. Temporary folders only; no network, no
    // database: the campground list is in memory.
    public class FormProblemsTests : ConfigEditTestBase
    {
        private const string Search = "displayName: T\nautoRun: true\ncampgroundID: '232447'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";

        private SearchesViewModel Searches() => new SearchesViewModel(Folder, () => Now,
            () => new List<CampgroundPickerEntry> { new CampgroundPickerEntry(new CampgroundChoice("232447", "UPPER PINES", "YOSEMITE VALLEY", "CA"), "Yosemite National Park") },
            () => new FilterChoiceSource(Id => CampgroundFilterChoices.None(Id), () => "db", Job => Job(), Job => { Job(); return Task.CompletedTask; }));

        private static List<string> Listed(SettingsFileViewModel Form) =>
            FormProblems.Of(Form).Select(P => P.Place + "|" + (P.Field?.Key ?? "-") + "|" + P.Index + "|" + P.Text).ToList();

        [Test]
        public void ASettingsForm_ListsItsFieldsAndRowsInError_InItsOrder_AndNoneWhenThereIsNone()
        {
            EmailSettingsViewModel Form = new SettingsViewModel(Folder, () => Now).Email;
            Form.CreateCommand.Execute(null);
            Assert.That(FormProblems.Of(Form), Is.Empty);
            Assert.That(FormProblems.Any(Form), Is.False);
            Form.SmtpPort.Text = "x";
            Form.Recipients.AddCommand.Execute(null);
            Form.Recipients.AddCommand.Execute(null);
            Form.Recipients.Rows[0].Text = "to@example.invalid";
            Form.Recipients.Rows[1].Text = "not an address";
            Assert.That(Listed(Form), Is.EqualTo(new[]
            {
                "Field|smtpPort|0|SMTP port: Enter a port number, such as 587.",
                "Row|sendToAddressList|1|Recipient 2: sendToAddressList item 2 is not a valid email address.",
            }));
            Assert.That(FormProblems.Any(Form), Is.True);
            Form.Revert();
            Assert.That(FormProblems.Of(Form), Is.Empty, "a problem is the form's own: gone with its value");
        }

        [Test]
        public void ASearchForm_ListsItsPickersTypedId_ItsSelectorsValuesAndTypedValue_ThenTheFilesOwnErrors()
        {
            File.WriteAllText(Path.Join(Folder, "Trip.yaml"), Search + "includeEquipment:\n- ~\n- Tent\n");
            File.WriteAllText(Path.Join(Folder, "Unknown.yaml"), Search + "unknownKey: 1\n");
            SearchesViewModel Form = Searches();
            Form.Show();
            SearchFileViewModel Trip = Form.Files.Single(F => F.FileName == "Trip.yaml");
            Form.SelectedFile = Trip;
            Trip.TotalHumans.Text = "many";
            Trip.Campground.TypedId = "abc";
            Trip.Campground.UseTypedIdCommand.Execute(null);
            FilterSelectorField Equipment = Trip.FieldList.OfType<FilterSelectorField>().Single(F => F.Key == "includeEquipment");
            Equipment.AddCustomCommand.Execute(null);
            Assert.That(Listed(Trip), Is.EqualTo(new[]
            {
                "CampgroundId|campgroundID|0|" + SearchesWording.CampgroundIdName + ": " + Trip.Campground.TypedIdMessage,
                "Field|totalHumans|0|People (totalHumans): Enter a number of people, such as 2.",
                "OtherValue|includeEquipment|0|includeEquipment: includeEquipment item 1 is empty, which stops the search when a campsite is checked.",
                "CustomValue|includeEquipment|0|" + Equipment.CustomName + ": Type a value first.",
            }));
            Assert.That(Trip.Campground.TypedIdMessage, Is.Not.Null.And.Not.Empty);
            //a file the form can't use: its own error, with no field
            SearchFileViewModel Unknown = Form.Files.Single(F => F.FileName == "Unknown.yaml");
            Form.SelectedFile = Unknown;
            Assert.That(Listed(Unknown), Is.EqualTo(new[] { "File|-|0|unknownKey is not a setting Campertron knows; Campertron can't use the file until the key is removed or corrected." }));
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty);
        }
    }
}
