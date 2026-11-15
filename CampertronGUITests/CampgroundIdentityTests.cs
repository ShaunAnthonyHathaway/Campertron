using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // CM4 (decision D42, TD-38): the GUI identifies a campground by its FacilityID. Each list holds one CampgroundChoice per
    // FacilityID, campgrounds with the same name stay separate entries with their location and ID, and the details are
    // looked up by the selected entry's FacilityID, never by its name. Uses GuiTestEnvironment's identity fixtures: three
    // campgrounds named "FIXTURE TWIN CAMPGROUND" (two of them in the same city and state), a blank and a whitespace-only
    // name, a Mailing-only and an address-less campground, a leading-zero ID and a same-named facility of another type.
    public class CampgroundIdentityTests
    {
        private const string Dash = " \u2014 ";
        private const string Identityville = GuiTestEnvironment.CityIdentityUtah + ", " + GuiTestEnvironment.StateUtah;

        // The identity park in list order (CampgroundChoice.ListOrder): name (trimmed, case-insensitive), then state, city
        // and ID; unnamed campgrounds last.
        private static readonly (string ID, string Label)[] IdentityPark =
        {
            (GuiTestEnvironment.LeadingZeroID, GuiTestEnvironment.LeadingZeroName + Dash + Identityville + Dash + "ID 0990010"),
            (GuiTestEnvironment.MailingOnlyID, GuiTestEnvironment.MailingOnlyName + Dash + "ID 990009"),
            (GuiTestEnvironment.TwinColoradoID, GuiTestEnvironment.TwinName + Dash + "IDENTITYTOWN, CO" + Dash + "ID 990006"),
            (GuiTestEnvironment.TwinUtahID, GuiTestEnvironment.TwinName + Dash + Identityville + Dash + "ID 990005"),
            (GuiTestEnvironment.TwinSameLocationID, "fixture twin campground" + Dash + Identityville + Dash + "ID 990012"),
            (GuiTestEnvironment.WhitespaceNameID, "Unnamed campground" + Dash + "ID 990008"),
            (GuiTestEnvironment.BlankNameID, "Unnamed campground" + Dash + Identityville + Dash + "ID 990007"),
        };

        // what each identity fixture's details say (GuiTestEnvironment.NewIdentityCampground)
        private static readonly Dictionary<string, (string Tag, string Name)> Expected = new Dictionary<string, (string, string)>
        {
            [GuiTestEnvironment.TwinUtahID] = ("Utah twin", GuiTestEnvironment.TwinName),
            [GuiTestEnvironment.TwinColoradoID] = ("Colorado twin", GuiTestEnvironment.TwinName),
            [GuiTestEnvironment.TwinSameLocationID] = ("Same-place twin", "fixture twin campground"),
            [GuiTestEnvironment.BlankNameID] = ("Blank name", CampgroundChoice.UnnamedName),
            [GuiTestEnvironment.WhitespaceNameID] = ("Whitespace name", CampgroundChoice.UnnamedName),
            [GuiTestEnvironment.MailingOnlyID] = ("Mailing address only", GuiTestEnvironment.MailingOnlyName),
            [GuiTestEnvironment.LeadingZeroID] = ("Leading zero", GuiTestEnvironment.LeadingZeroName),
        };

        private static void AssertDetailsOf(CampgroundDetails Details, string FacilityID)
        {
            (string Tag, string Name) = Expected[FacilityID];
            Assert.That(Details.HasCampground, Is.True, FacilityID);
            Assert.That(Details.CampgroundId, Is.EqualTo(FacilityID));
            Assert.That(Details.Name, Is.EqualTo(Name), FacilityID);
            Assert.That(Details.Description, Is.EqualTo(GuiTestEnvironment.IdentityDescription(Tag)), FacilityID);
            Assert.That(Details.Directions, Is.EqualTo(Tag + " directions"), FacilityID);
            Assert.That(Details.Fees, Is.EqualTo(Tag + " fee"), FacilityID);
            Assert.That(Details.Phone, Is.EqualTo(GuiTestEnvironment.IdentityPhone(FacilityID)), FacilityID);
            Assert.That(Details.Email, Is.EqualTo(FacilityID + "@example.invalid"), FacilityID);
        }

        private static CampgroundChoice Listed(List<CampgroundChoice> List, string FacilityID)
        {
            return List.SingleOrDefault(c => c.FacilityID == FacilityID) ?? throw new AssertionException(FacilityID + " is not listed");
        }

        // ---- view model ----

        // Each campground is listed once, by its FacilityID, with a label that tells it apart from every other entry.
        [AvaloniaTest]
        public void Lists_HoldOneChoicePerFacilityID_EvenForTheSameName()
        {
            var ViewModel = new ReactiveViewModel();

            ViewModel.SelectedPark = GuiTestEnvironment.ParkIdentity;
            List<CampgroundChoice> Park = ViewModel.CampgroundListByPark;
            Assert.That(Park.Select(c => c.FacilityID), Is.EqualTo(IdentityPark.Select(e => e.ID)));
            Assert.That(Park.Select(c => c.Label), Is.EqualTo(IdentityPark.Select(e => e.Label)));
            Assert.That(Park.Count(c => string.Equals(c.Name, GuiTestEnvironment.TwinName, StringComparison.OrdinalIgnoreCase)), Is.EqualTo(3));
            Assert.That(Park.Select(c => c.Label).Distinct().Count(), Is.EqualTo(Park.Count));
            Assert.That(Park.Select(c => c.FacilityID), Has.None.EqualTo(GuiTestEnvironment.OtherFacilityID), "a facility of another type is listed");

            // By State and By City (location filters, unchanged): two of the twins share their name and place, so only
            // the ID tells them apart; campgrounds without a Physical or Default address there are not listed.
            ViewModel.SelectedStateByState = GuiTestEnvironment.StateUtah;
            Assert.That(ViewModel.CampgroundListByState.Select(c => c.FacilityID), Is.EqualTo(new[]
            {
                GuiTestEnvironment.CampgroundAlphaID, GuiTestEnvironment.CampgroundCharlieID, GuiTestEnvironment.LeadingZeroID,
                GuiTestEnvironment.TwinUtahID, GuiTestEnvironment.TwinSameLocationID, GuiTestEnvironment.BlankNameID,
            }));
            ViewModel.SelectedState = GuiTestEnvironment.StateUtah;
            ViewModel.SelectedCity = GuiTestEnvironment.CityIdentityUtah;
            List<CampgroundChoice> City = ViewModel.CampgroundList;
            Assert.That(City.Select(c => c.FacilityID), Is.EqualTo(new[]
            {
                GuiTestEnvironment.LeadingZeroID, GuiTestEnvironment.TwinUtahID, GuiTestEnvironment.TwinSameLocationID, GuiTestEnvironment.BlankNameID,
            }));
            CampgroundChoice First = Listed(City, GuiTestEnvironment.TwinUtahID);
            CampgroundChoice Second = Listed(City, GuiTestEnvironment.TwinSameLocationID);
            Assert.That(First.Title, Is.EqualTo(GuiTestEnvironment.TwinName + Dash + Identityville));
            Assert.That(Second.Title, Is.EqualTo("fixture twin campground" + Dash + Identityville));
            Assert.That(First, Is.Not.EqualTo(Second));
            Assert.That(First.Label, Is.Not.EqualTo(Second.Label));
        }

        // Selecting each campground of the same name shows exactly that campground's details, on every tab.
        [AvaloniaTest]
        public void SelectingEachDuplicate_ShowsExactlyItsOwnDetails()
        {
            var ViewModel = new ReactiveViewModel();
            ViewModel.SelectedPark = GuiTestEnvironment.ParkIdentity;
            foreach (string FacilityID in new[] { GuiTestEnvironment.TwinColoradoID, GuiTestEnvironment.TwinUtahID, GuiTestEnvironment.TwinSameLocationID, GuiTestEnvironment.TwinUtahID })
            {
                ViewModel.SelectedCampgroundByPark = Listed(ViewModel.CampgroundListByPark, FacilityID);
                Assert.That(ViewModel.SelectedCampgroundByPark.FacilityID, Is.EqualTo(FacilityID));
                AssertDetailsOf(ViewModel.ParkCampgroundDetails, FacilityID);
            }

            ViewModel.SelectedStateByState = GuiTestEnvironment.StateUtah;
            foreach (string FacilityID in new[] { GuiTestEnvironment.TwinSameLocationID, GuiTestEnvironment.TwinUtahID })
            {
                ViewModel.SelectedCampgroundByState = Listed(ViewModel.CampgroundListByState, FacilityID);
                AssertDetailsOf(ViewModel.StateCampgroundDetails, FacilityID);
            }

            ViewModel.SelectedState = GuiTestEnvironment.StateUtah;
            ViewModel.SelectedCity = GuiTestEnvironment.CityIdentityUtah;
            foreach (string FacilityID in new[] { GuiTestEnvironment.TwinUtahID, GuiTestEnvironment.TwinSameLocationID, GuiTestEnvironment.LeadingZeroID })
            {
                ViewModel.SelectedCampground = Listed(ViewModel.CampgroundList, FacilityID);
                AssertDetailsOf(ViewModel.CityCampgroundDetails, FacilityID);
            }
        }

        // A blank or whitespace-only name is listed and shown as "Unnamed campground", with its ID and its own details.
        [AvaloniaTest]
        public void BlankAndWhitespaceNames_AreListedAndSelectableAsUnnamedCampgrounds()
        {
            var ViewModel = new ReactiveViewModel();
            ViewModel.SelectedPark = GuiTestEnvironment.ParkIdentity;
            foreach (string FacilityID in new[] { GuiTestEnvironment.BlankNameID, GuiTestEnvironment.WhitespaceNameID })
            {
                CampgroundChoice Choice = Listed(ViewModel.CampgroundListByPark, FacilityID);
                Assert.That(Choice.IsUnnamed, Is.True);
                Assert.That(Choice.DisplayName, Is.EqualTo(CampgroundChoice.UnnamedName));
                Assert.That(Choice.Label, Does.StartWith(CampgroundChoice.UnnamedName + Dash).And.EndWith(Dash + "ID " + FacilityID));
                ViewModel.SelectedCampgroundByPark = Choice;
                AssertDetailsOf(ViewModel.ParkCampgroundDetails, FacilityID);
            }
        }

        // No selection shows nothing and runs no query; clearing a selection clears its details.
        [AvaloniaTest]
        public void NoOrAClearedSelection_ShowsNoDetailsAndRunsNoQuery()
        {
            var ViewModel = new ReactiveViewModel();
            using var Queries = new QueryCounter();
            foreach (CampgroundDetails None in new[] { ViewModel.ParkCampgroundDetails, ViewModel.CityCampgroundDetails, ViewModel.StateCampgroundDetails })
            {
                Assert.That(None, Is.SameAs(CampgroundDetails.None));
            }
            Assert.That(Queries.TakeCount(), Is.Zero);

            ViewModel.SelectedPark = GuiTestEnvironment.ParkIdentity;
            ViewModel.SelectedCampgroundByPark = Listed(ViewModel.CampgroundListByPark, GuiTestEnvironment.TwinUtahID);
            AssertDetailsOf(ViewModel.ParkCampgroundDetails, GuiTestEnvironment.TwinUtahID);
            Queries.TakeCount();

            ViewModel.SelectedCampgroundByPark = null;
            CampgroundDetails Cleared = ViewModel.ParkCampgroundDetails;
            Assert.That(Cleared.HasCampground, Is.False);
            Assert.That(Cleared.CampgroundId, Is.Empty);
            Assert.That(Cleared.Name, Is.Empty);
            Assert.That(Queries.TakeCount(), Is.Zero);
        }

        // A FacilityID that is not a listed campground (not in the database, or another kind of facility with the same
        // name) shows no details: never another record with that name, and never the previous campground's details.
        [AvaloniaTest]
        public void AnUnknownFacilityID_ShowsNoDetails_NotASameNamedRecord()
        {
            var ViewModel = new ReactiveViewModel();
            ViewModel.SelectedPark = GuiTestEnvironment.ParkIdentity;
            ViewModel.SelectedCampgroundByPark = Listed(ViewModel.CampgroundListByPark, GuiTestEnvironment.TwinUtahID);
            AssertDetailsOf(ViewModel.ParkCampgroundDetails, GuiTestEnvironment.TwinUtahID);

            foreach (string Unknown in new[] { "990099", GuiTestEnvironment.OtherFacilityID, "990010", " 990005", "990005 " })
            {
                ViewModel.SelectedCampgroundByPark = new CampgroundChoice(Unknown, GuiTestEnvironment.TwinName,
                    GuiTestEnvironment.CityIdentityUtah, GuiTestEnvironment.StateUtah);
                Assert.That(ViewModel.ParkCampgroundDetails, Is.SameAs(CampgroundDetails.None), Unknown);
            }
        }

        // A filter change clears its tab's campground, also when the new list holds a campground with the same name.
        [AvaloniaTest]
        public void AFilterChange_ClearsTheTabsCampgroundAndDetails()
        {
            var ViewModel = new ReactiveViewModel();

            ViewModel.SelectedStateByState = GuiTestEnvironment.StateUtah;
            ViewModel.SelectedCampgroundByState = Listed(ViewModel.CampgroundListByState, GuiTestEnvironment.TwinUtahID);
            ViewModel.SelectedStateByState = GuiTestEnvironment.StateColorado;
            Assert.That(ViewModel.SelectedCampgroundByState, Is.Null);
            Assert.That(ViewModel.StateCampgroundDetails, Is.SameAs(CampgroundDetails.None));
            Assert.That(ViewModel.CampgroundListByState.Select(c => c.Name), Does.Contain(GuiTestEnvironment.TwinName), "the new list holds the same name");

            ViewModel.SelectedState = GuiTestEnvironment.StateUtah;
            ViewModel.SelectedCity = GuiTestEnvironment.CityIdentityUtah;
            ViewModel.SelectedCampground = Listed(ViewModel.CampgroundList, GuiTestEnvironment.TwinUtahID);
            ViewModel.SelectedCity = GuiTestEnvironment.CityAlpha;
            Assert.That(ViewModel.SelectedCampground, Is.Null);
            Assert.That(ViewModel.CityCampgroundDetails, Is.SameAs(CampgroundDetails.None));
            ViewModel.SelectedCampground = Listed(ViewModel.CampgroundList, GuiTestEnvironment.CampgroundAlphaID);
            ViewModel.SelectedState = GuiTestEnvironment.StateColorado;
            Assert.That(ViewModel.SelectedCampground, Is.Null);
            Assert.That(ViewModel.CityCampgroundDetails, Is.SameAs(CampgroundDetails.None));

            ViewModel.SelectedPark = GuiTestEnvironment.ParkIdentity;
            ViewModel.SelectedCampgroundByPark = Listed(ViewModel.CampgroundListByPark, GuiTestEnvironment.TwinColoradoID);
            ViewModel.SelectedPark = GuiTestEnvironment.ParkTwo;
            Assert.That(ViewModel.SelectedCampgroundByPark, Is.Null);
            Assert.That(ViewModel.ParkCampgroundDetails, Is.SameAs(CampgroundDetails.None));
        }

        // The details are computed from the current selection whenever they are raised (synchronously, no cache), so a
        // fast sequence of selections never shows a previous campground's details.
        [AvaloniaTest]
        public void RapidSelections_AlwaysShowTheCurrentSelectionsDetails()
        {
            var ViewModel = new ReactiveViewModel();
            ViewModel.SelectedPark = GuiTestEnvironment.ParkIdentity;
            List<CampgroundChoice> List = ViewModel.CampgroundListByPark;
            var Shown = new List<(string? Selected, string Details)>();
            ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ReactiveViewModel.ParkCampgroundDetails))
                {
                    Shown.Add((ViewModel.SelectedCampgroundByPark?.FacilityID, ViewModel.ParkCampgroundDetails.CampgroundId));
                }
            };

            string?[] Sequence =
            {
                GuiTestEnvironment.TwinUtahID, GuiTestEnvironment.TwinColoradoID, GuiTestEnvironment.TwinSameLocationID, null,
                GuiTestEnvironment.TwinUtahID, GuiTestEnvironment.BlankNameID, GuiTestEnvironment.LeadingZeroID, GuiTestEnvironment.TwinUtahID,
            };
            foreach (string? FacilityID in Sequence)
            {
                ViewModel.SelectedCampgroundByPark = FacilityID == null ? null : Listed(List, FacilityID);
            }

            Assert.That(Shown, Has.Count.EqualTo(Sequence.Length));
            Assert.That(Shown.Select(s => s.Selected), Is.EqualTo(Sequence));
            Assert.That(Shown.Select(s => s.Details), Is.EqualTo(Sequence.Select(s => s ?? string.Empty)));
        }

        // ---- window ----

        // One query per selection on every tab (the dependent list, or the details), and the details query selects the
        // campground by its FacilityID as a parameter, never by its name.
        [AvaloniaTest]
        public void EverySelection_RunsOneParameterizedQuery_AndNoNameLookup()
        {
            var (Window, ViewModel) = ShowMainWindow();
            using var Queries = new QueryCounter();
            void Expect(int Count, string What)
            {
                Assert.That(Queries.TakeCount(), Is.EqualTo(Count), What);
            }

            SelectTab(Window, 0);
            Queries.TakeCommands();
            Expect(0, "By Park is open");
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkIdentity);
            Expect(1, "park -> campground list");
            foreach (string FacilityID in new[] { GuiTestEnvironment.TwinUtahID, GuiTestEnvironment.TwinColoradoID, GuiTestEnvironment.LeadingZeroID })
            {
                Queries.TakeCommands();
                SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), FacilityID);
                var Commands = Queries.TakeCommands();
                Expect(1, "campground -> details");
                Assert.That(Commands, Has.Count.EqualTo(1));
                (string Sql, Dictionary<string, object?> Parameters) = Commands[0];
                string Where = Sql.Substring(Sql.IndexOf("WHERE", StringComparison.Ordinal));
                Assert.That(Where, Does.Contain("\"FacilityID\" = @"));
                Assert.That(Where, Does.Not.Contain("FacilityName"), "the details are looked up by name");
                Assert.That(Sql, Does.Not.Contain(FacilityID), "the ID is not a parameter");
                Assert.That(Parameters.Values, Is.EqualTo(new object?[] { FacilityID }));
                Assert.That(DisplayedId(Details(Window, 0)), Is.EqualTo(FacilityID));
            }
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
            Expect(1, "new park -> campground list; the cleared campground runs no query");

            SelectTab(Window, 1);
            Queries.TakeCount();
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateUtah);
            Expect(1, "state -> city list");
            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.CityIdentityUtah);
            Expect(1, "city -> campground list");
            SelectCampground(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.TwinSameLocationID);
            Expect(1, "campground -> details");
            Assert.That(DisplayedId(Details(Window, 1)), Is.EqualTo(GuiTestEnvironment.TwinSameLocationID));
            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.CityAlpha);
            Expect(1, "new city -> campground list");

            SelectTab(Window, 2);
            Queries.TakeCount();
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateUtah);
            Expect(1, "state -> campground list");
            SelectCampground(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.TwinUtahID);
            Expect(1, "campground -> details");
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateColorado);
            Expect(1, "new state -> campground list");
            Assert.That(ViewModel.ReactiveViewModel!.SelectedCampgroundByState, Is.Null);
            Assert.That(ShowsEmptyState(Details(Window, 2)), Is.True);
            Window.Close();
        }

        // The selection path refers to the database reads only: no settings file, YAML, writer or console search code,
        // and a whole lookup session leaves every file of the settings folder unchanged.
        [AvaloniaTest]
        public void Selections_ReadAndWriteNoSettingsFile()
        {
            var Allowed = new HashSet<string>
            {
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::UniqueStates", "CampertronLibrary.function.RecDotOrg.sqlite.Read::UniqueCities",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::ParksWithCampgrounds", "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundChoicesByPark",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundChoicesByStateAndCity", "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundChoicesByState",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::GetCampgroundDetailsById", "CampgroundChoice::get_FacilityID",
            };
            Assembly Library = typeof(CampgroundChoice).Assembly;
            List<(Assembly? Assembly, string Member)> Called = ReferencedMembers(typeof(ReactiveViewModel), typeof(CampgroundDetails)).ToList();
            List<string> LibraryCalls = Called.Where(c => c.Assembly == Library && !c.Member.StartsWith("FacilitiesData::get_", StringComparison.Ordinal))
                .Select(c => c.Member).Distinct().ToList();
            Assert.That(LibraryCalls, Is.SubsetOf(Allowed));
            Assert.That(LibraryCalls, Does.Contain("CampertronLibrary.function.RecDotOrg.sqlite.Read::GetCampgroundDetailsById"));
            string[] Forbidden = { "System.IO.File", "System.IO.Directory", "System.IO.Stream", "System.Net.", "System.Diagnostics.Process" };
            Assert.That(Called.Select(c => c.Member).Where(m => Forbidden.Any(f => m.StartsWith(f, StringComparison.Ordinal))), Is.Empty);

            var (Window, _) = ShowMainWindow();
            Dictionary<string, string> Before = Fingerprint(GuiTestEnvironment.ConfigPath);
            RunLookupSession(Window);
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Fingerprint(GuiTestEnvironment.ConfigPath), Is.EquivalentTo(Before));
        }

        // The GUI stays a lookup: no part of it refers to the availability search, the RIDB download or the console
        // search pipeline, and a whole lookup session makes no web request.
        [AvaloniaTest]
        public void TheLookup_StartsNoCampsiteSearchAndNoWebRequest()
        {
            Assembly Gui = typeof(ReactiveViewModel).Assembly;
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.api.", "CampertronLibrary.function.Base.Web::", "CampertronLibrary.function.Base.ConfigFileReplace::",
                "CampertronLibrary.function.RecDotOrg.data.Load::RunConsoleSearch", "CampertronLibrary.function.RecDotOrg.data.Load::Preload",
                "CampertronLibrary.function.RecDotOrg.data.Load::PostProcess", "CampertronLibrary.function.RecDotOrg.data.Load::OutputResults",
                "CampertronLibrary.function.RecDotOrg.data.Load::ReportResults", "System.Net.Http.",
            };
            List<string> Called = ReferencedMembers(Gui.GetTypes()).Select(c => c.Member).Distinct().ToList();
            Assert.That(Called, Has.Count.GreaterThan(100));
            Assert.That(Called.Where(m => Forbidden.Any(f => m.StartsWith(f, StringComparison.Ordinal))), Is.Empty);

            using var Requests = new WebRequestListener();
            Requests.Prove();
            var (Window, _) = ShowMainWindow();
            RunLookupSession(Window);
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Requests.Count, Is.Zero);
        }

        // Every entry of the drop-down shows its name and place (trimmed) and its ID, which is never trimmed; entries
        // with the same name, even the same name and place, are told apart by their ID; screen readers and the tooltip
        // get the whole label, blank names included.
        [AvaloniaTest]
        public void TheDropDown_ShowsEachEntrysIdAndReadsItsWholeLabel()
        {
            var (Window, _) = ShowMainWindow();
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkIdentity);
            ComboBox Campground = Named<ComboBox>(Window, "ParkCampgroundComboBox");
            Campground.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();

            List<ComboBoxItem> Items = Campground.GetRealizedContainers().Cast<ComboBoxItem>().ToList();
            Assert.That(Items, Has.Count.EqualTo(IdentityPark.Length));
            var Seen = new List<string>();
            for (int Index = 0; Index < Items.Count; Index++)
            {
                ComboBoxItem Item = Items[Index];
                CampgroundChoice Choice = (CampgroundChoice)Item.DataContext!;
                Assert.That(Choice.FacilityID, Is.EqualTo(IdentityPark[Index].ID));
                TextBlock Title = Item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampgroundItemTitle");
                TextBlock Id = Item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampgroundItemId");
                Assert.That(Id.Text, Is.EqualTo("ID " + Choice.FacilityID));
                Assert.That(Id.IsEffectivelyVisible && Id.Bounds.Width > 0, Is.True, Choice.FacilityID);
                Assert.That(Title.Text, Is.EqualTo(Choice.Title));
                Assert.That(ControlAutomationPeer.CreatePeerForElement(Item).GetName(), Is.EqualTo(IdentityPark[Index].Label));
                Assert.That(ToolTip.GetTip(Item.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "CampgroundItem")), Is.EqualTo(IdentityPark[Index].Label));
                Seen.Add(Title.Text + " | " + Id.Text);
            }
            Assert.That(Seen.Distinct().Count(), Is.EqualTo(Items.Count), "two entries look the same");
            Assert.That(Seen.Count(s => s.StartsWith(GuiTestEnvironment.TwinName + Dash + Identityville + " |", StringComparison.Ordinal)), Is.EqualTo(1));

            Campground.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();
            Window.Close();
        }

        // The selected entry shows its ID in the selector too (a leading zero kept), and the combo box keeps its name.
        [AvaloniaTest]
        public void TheSelector_ShowsTheSelectedEntrysIdExactly()
        {
            var (Window, _) = ShowMainWindow();
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkIdentity);
            ComboBox Campground = Named<ComboBox>(Window, "ParkCampgroundComboBox");
            SelectCampground(Campground, GuiTestEnvironment.LeadingZeroID);
            Window.UpdateLayout();

            Assert.That(SelectorText(Campground, "CampgroundItemId"), Is.EqualTo("ID 0990010"));
            Assert.That(SelectorText(Campground, "CampgroundItemTitle"), Is.EqualTo(GuiTestEnvironment.LeadingZeroName + Dash + Identityville));
            Assert.That(DisplayedId(Details(Window, 0)), Is.EqualTo(GuiTestEnvironment.LeadingZeroID));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(Campground).GetName(), Is.EqualTo("Campground"));
            Window.Close();
        }

        // Type-ahead selects the first entry starting with the typed text and the arrow keys move between entries with
        // the same name; the details follow each entry.
        [AvaloniaTest]
        public void Keyboard_SelectsEntriesWithTheSameNameByTypeAheadAndArrowKeys()
        {
            var (Window, ViewModel) = ShowMainWindow();
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkIdentity);
            ComboBox Campground = Named<ComboBox>(Window, "ParkCampgroundComboBox");
            Campground.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();

            Window.KeyTextInput("FIXTURE TW");
            Dispatcher.UIThread.RunJobs();
            Assert.That(ViewModel.ReactiveViewModel!.SelectedCampgroundByPark?.FacilityID, Is.EqualTo(GuiTestEnvironment.TwinColoradoID));
            Assert.That(DisplayedId(Details(Window, 0)), Is.EqualTo(GuiTestEnvironment.TwinColoradoID));

            foreach (string Next in new[] { GuiTestEnvironment.TwinUtahID, GuiTestEnvironment.TwinSameLocationID, GuiTestEnvironment.WhitespaceNameID })
            {
                Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                Dispatcher.UIThread.RunJobs();
                Assert.That(ViewModel.ReactiveViewModel!.SelectedCampgroundByPark?.FacilityID, Is.EqualTo(Next));
                Assert.That(DisplayedId(Details(Window, 0)), Is.EqualTo(Next));
            }
            Assert.That(Named<TextBlock>(Details(Window, 0), "CampgroundNameText").Text, Is.EqualTo(CampgroundChoice.UnnamedName));
            Window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(DisplayedId(Details(Window, 0)), Is.EqualTo(GuiTestEnvironment.TwinSameLocationID));
            Window.Close();
        }

        // The ID uses the theme's secondary text brush in Light and Dark, and opening the new entries logs nothing.
        [AvaloniaTest]
        public void TheEntries_UseThemeBrushesAndLogNothing([Values("Light", "Dark")] string Variant)
        {
            using var Log = new AvaloniaLogCapture();
            ThemeVariant Theme = Variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current!.RequestedThemeVariant = Theme;
            var (Window, _) = ShowMainWindow();
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkIdentity);
            ComboBox Campground = Named<ComboBox>(Window, "ParkCampgroundComboBox");
            SelectCampground(Campground, GuiTestEnvironment.TwinUtahID);
            Campground.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Window.ActualThemeVariant, Is.EqualTo(Theme));

            Assert.That(Application.Current.TryGetResource("SystemControlForegroundBaseMediumBrush", Theme, out object? Secondary), Is.True);
            TextBlock SelectedId = Campground.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampgroundItemId");
            Assert.That(((ISolidColorBrush)SelectedId.Foreground!).Color, Is.EqualTo(((ISolidColorBrush)Secondary!).Color));
            foreach (ComboBoxItem Item in Campground.GetRealizedContainers().Cast<ComboBoxItem>())
            {
                TextBlock Id = Item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampgroundItemId");
                Assert.That(((ISolidColorBrush)Id.Foreground!).Color, Is.EqualTo(((ISolidColorBrush)Secondary!).Color));
            }
            Campground.IsDropDownOpen = false;
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Log.Entries, Is.Empty);
        }

        // A long name is trimmed in the selector, but the ID stays whole and inside the selector, on every tab, at the
        // minimum and the default window width.
        [AvaloniaTest]
        public void ALongName_IsTrimmed_ButItsIdStaysWholeAndInside([Values(720, 1024)] int Width)
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, _) = ShowMainWindow();
            Resize(Window, Width, Width == 720 ? 540 : 768);
            var Steps = new (int Tab, string Combo, Action Filters)[]
            {
                (0, "ParkCampgroundComboBox", () => Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkThree)),
                (1, "CityCampgroundComboBox", () =>
                {
                    Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateColorado);
                    Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.CityBravo);
                }),
                (2, "StateCampgroundComboBox", () => Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateColorado)),
            };
            foreach (var (Tab, ComboName, Filters) in Steps)
            {
                SelectTab(Window, Tab);
                Filters();
                ComboBox Campground = Named<ComboBox>(Window, ComboName);
                SelectCampground(Campground, GuiTestEnvironment.CampgroundDeltaID);
                Window.UpdateLayout();

                TextBlock Title = Campground.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampgroundItemTitle");
                TextBlock Id = Campground.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampgroundItemId");
                Assert.That(Id.Text, Is.EqualTo("ID " + GuiTestEnvironment.CampgroundDeltaID));
                Assert.That(Id.Bounds.Width, Is.GreaterThanOrEqualTo(NaturalWidth(Id) - 0.5), ComboName + ": the ID is trimmed");
                Assert.That(Title.Bounds.Width, Is.LessThan(NaturalWidth(Title)), ComboName + ": the long name is not trimmed");
                Point IdRight = Id.TranslatePoint(new Point(Id.Bounds.Width, 0), Campground)!.Value;
                Assert.That(IdRight.X, Is.LessThanOrEqualTo(Campground.Bounds.Width + 0.5), ComboName + ": the ID is outside the selector");
                Point ComboRight = Campground.TranslatePoint(new Point(Campground.Bounds.Width, 0), Window)!.Value;
                Assert.That(ComboRight.X, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), ComboName + ": the selector is outside the window");

                // The drop-down is as wide as its selector, so there too a long name is trimmed and every ID is whole.
                Campground.IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs();
                List<ComboBoxItem> Items = Campground.GetRealizedContainers().Cast<ComboBoxItem>().ToList();
                Assert.That(Items, Is.Not.Empty);
                Control DropDown = (Control)Campground.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single().Child!;
                Assert.That(DropDown.Bounds.Width, Is.LessThanOrEqualTo(Campground.Bounds.Width + 0.5), ComboName + ": the drop-down is wider than its selector");
                foreach (ComboBoxItem Item in Items)
                {
                    TextBlock ItemId = Item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampgroundItemId");
                    Assert.That(ItemId.Bounds.Width, Is.GreaterThanOrEqualTo(NaturalWidth(ItemId) - 0.5), ComboName + ": an ID is trimmed in the drop-down");
                    Point ItemIdRight = ItemId.TranslatePoint(new Point(ItemId.Bounds.Width, 0), DropDown)!.Value;
                    Assert.That(ItemIdRight.X, Is.LessThanOrEqualTo(DropDown.Bounds.Width + 0.5), ComboName + ": an ID is outside the drop-down");
                }
                TextBlock LongTitle = Items.Single(i => ((CampgroundChoice)i.DataContext!).FacilityID == GuiTestEnvironment.CampgroundDeltaID)
                    .GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampgroundItemTitle");
                Assert.That(LongTitle.Bounds.Width, Is.LessThan(NaturalWidth(LongTitle)), ComboName + ": the long name is not trimmed in the drop-down");
                Campground.IsDropDownOpen = false;
                Dispatcher.UIThread.RunJobs();
            }
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Log.Entries, Is.Empty);
        }

        // CM4 adds no campsite results to the window: three lookup tabs (and, since CM5, Settings) whose only lists are the
        // selectors, and whose only buttons are Copy ID (and the startup's Try again).
        [AvaloniaTest]
        public void TheWindow_StaysALookupWithoutCampsiteResults()
        {
            var (Window, _) = ShowMainWindow();
            var Lists = new HashSet<string>();
            var Buttons = new HashSet<string?>();
            for (int Index = 0; Index < ExpectedTabs.Length; Index++)
            {
                SelectTab(Window, Index);
                foreach (ItemsControl List in Window.GetVisualDescendants().OfType<ItemsControl>())
                {
                    Lists.Add(List.GetType().Name);
                }
                foreach (Button Button in Window.GetVisualDescendants().OfType<Button>().Where(b => b.GetType() == typeof(Button)))
                {
                    Buttons.Add(Button.Name);
                }
            }
            Assert.That(Tabs(Window).Items.Cast<TabItem>().Select(t => t.Header), Is.EqualTo(AllTabs));
            Assert.That(Lists, Is.EquivalentTo(new[] { nameof(TabControl), nameof(ComboBox) }));
            Assert.That(Buttons, Is.SubsetOf(new[] { "CopyIdButton", "RetryButton" }));
            Window.Close();
        }

        // ---- helpers ----

        // Selects in every tab, including the duplicates, a blank name, an entry without a location and a leading zero.
        private static void RunLookupSession(MainWindow Window)
        {
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkIdentity);
            foreach (string FacilityID in IdentityPark.Select(e => e.ID))
            {
                SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), FacilityID);
                Assert.That(DisplayedId(Details(Window, 0)), Is.EqualTo(FacilityID));
            }
            SelectTab(Window, 1);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateUtah);
            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.CityIdentityUtah);
            SelectCampground(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.TwinSameLocationID);
            SelectTab(Window, 2);
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateColorado);
            SelectCampground(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.TwinColoradoID);
            Assert.That(DisplayedId(Details(Window, 2)), Is.EqualTo(GuiTestEnvironment.TwinColoradoID));
        }

        private static string? SelectorText(ComboBox Combo, string Name)
        {
            return Combo.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == Name).Text;
        }

        // the width the text needs on one line, untrimmed
        private static double NaturalWidth(TextBlock Source)
        {
            var Probe = new TextBlock
            {
                Text = Source.Text,
                FontFamily = Source.FontFamily,
                FontSize = Source.FontSize,
                FontWeight = Source.FontWeight,
                FontStyle = Source.FontStyle,
            };
            Probe.Measure(Size.Infinity);
            return Probe.DesiredSize.Width;
        }
    }
}
