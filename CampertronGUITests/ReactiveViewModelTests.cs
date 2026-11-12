using System.ComponentModel;
using Avalonia.Headless.NUnit;
using Campertron.ViewModels;

namespace CampertronGUITests
{
    // ReactiveUI behavior of the production view model under ReactiveUI.Avalonia.Reactive 12.1.6 (ReactiveUI 26, with
    // source-generated WhenAnyValue). These tests only observe PropertyChanged; they never read the database-backed list
    // or details properties. They run as [AvaloniaTest] so ReactiveUI is initialized by UseReactiveUI as in the application.
    public class ReactiveViewModelTests
    {
        private static List<string?> RecordPropertyChanges(INotifyPropertyChanged Source)
        {
            var Changes = new List<string?>();
            Source.PropertyChanged += (_, e) => Changes.Add(e.PropertyName);
            return Changes;
        }

        [AvaloniaTest]
        public void RaiseAndSetIfChanged_RaisesOnlyForAnActualChange()
        {
            var ViewModel = new ReactiveViewModel();
            List<string?> Changes = RecordPropertyChanges(ViewModel);

            ViewModel.SelectedCity = GuiTestEnvironment.CityAlpha;
            Assert.That(ViewModel.SelectedCity, Is.EqualTo(GuiTestEnvironment.CityAlpha));
            Assert.That(Changes, Does.Contain(nameof(ReactiveViewModel.SelectedCity)));

            Changes.Clear();
            ViewModel.SelectedCity = GuiTestEnvironment.CityAlpha;
            Assert.That(Changes, Is.Empty, "setting the same value must not raise PropertyChanged");
        }

        // Each WhenAnyValue subscription in the constructor re-raises the dependent list property, and each tab's
        // details property, after one of its source properties changes.
        [AvaloniaTest]
        public void WhenAnyValueSubscriptions_RaiseTheDependentProperty()
        {
            var ViewModel = new ReactiveViewModel();
            List<string?> Changes = RecordPropertyChanges(ViewModel);

            ViewModel.SelectedState = GuiTestEnvironment.StateUtah;
            ViewModel.SelectedCity = GuiTestEnvironment.CityAlpha;
            ViewModel.SelectedPark = GuiTestEnvironment.ParkOne;
            ViewModel.SelectedCampground = GuiTestEnvironment.CampgroundAlpha;

            Assert.That(Changes, Is.EquivalentTo(new[]
            {
                nameof(ReactiveViewModel.SelectedState), nameof(ReactiveViewModel.CityList), nameof(ReactiveViewModel.CityCampgroundDetails),
                nameof(ReactiveViewModel.SelectedCity), nameof(ReactiveViewModel.CampgroundList), nameof(ReactiveViewModel.CityCampgroundDetails),
                nameof(ReactiveViewModel.SelectedPark), nameof(ReactiveViewModel.CampgroundListByPark), nameof(ReactiveViewModel.ParkCampgroundDetails),
                nameof(ReactiveViewModel.SelectedCampground), nameof(ReactiveViewModel.CityCampgroundDetails),
            }));
        }

        // TD-08 fixed in beta.8: SelectedStateByState has its own field. Selecting a state on "By State" no longer
        // changes the "By City" state, and raises only the By State properties.
        [AvaloniaTest]
        public void ByStateSelection_DoesNotChangeTheByCityState_TD08()
        {
            var ViewModel = new ReactiveViewModel();
            ViewModel.SelectedState = GuiTestEnvironment.StateUtah;
            List<string?> Changes = RecordPropertyChanges(ViewModel);

            ViewModel.SelectedStateByState = GuiTestEnvironment.StateColorado;

            Assert.That(ViewModel.SelectedStateByState, Is.EqualTo(GuiTestEnvironment.StateColorado));
            Assert.That(ViewModel.SelectedState, Is.EqualTo(GuiTestEnvironment.StateUtah));
            Assert.That(Changes, Is.EquivalentTo(new[]
            {
                nameof(ReactiveViewModel.SelectedStateByState), nameof(ReactiveViewModel.CampgroundListByState),
                nameof(ReactiveViewModel.StateCampgroundDetails),
            }));

            Changes.Clear();
            ViewModel.SelectedState = GuiTestEnvironment.StateColorado;
            Assert.That(Changes, Has.None.EqualTo(nameof(ReactiveViewModel.SelectedStateByState)));
            Assert.That(Changes, Has.None.EqualTo(nameof(ReactiveViewModel.CampgroundListByState)));
            Assert.That(Changes, Has.None.EqualTo(nameof(ReactiveViewModel.StateCampgroundDetails)));
        }

        // Each tab's campground selection raises only that tab's details.
        [AvaloniaTest]
        public void CampgroundSelections_RaiseOnlyTheirOwnTabsDetails()
        {
            var ViewModel = new ReactiveViewModel();
            List<string?> Changes = RecordPropertyChanges(ViewModel);

            ViewModel.SelectedCampgroundByPark = GuiTestEnvironment.CampgroundAlpha;
            Assert.That(Changes, Is.EquivalentTo(new[] { nameof(ReactiveViewModel.SelectedCampgroundByPark), nameof(ReactiveViewModel.ParkCampgroundDetails) }));

            Changes.Clear();
            ViewModel.SelectedCampgroundByState = GuiTestEnvironment.CampgroundBravo;
            Assert.That(Changes, Is.EquivalentTo(new[] { nameof(ReactiveViewModel.SelectedCampgroundByState), nameof(ReactiveViewModel.StateCampgroundDetails) }));

            Changes.Clear();
            ViewModel.SelectedCampground = GuiTestEnvironment.CampgroundAlpha;
            Assert.That(Changes, Is.EquivalentTo(new[] { nameof(ReactiveViewModel.SelectedCampground), nameof(ReactiveViewModel.CityCampgroundDetails) }));
        }

        [Test]
        public void CampgroundDetails_OwnsMissingValueTextAndContext()
        {
            Assert.That(CampgroundDetails.None.HasCampground, Is.False);
            Assert.That(CampgroundDetails.None.CampgroundId, Is.Empty);

            var Details = new CampgroundDetails(new FacilitiesData
            {
                FacilityID = " 990001 ",
                FacilityName = "NAME WITH TRAILING SPACES   ",
                FacilityPhone = "  ",
                FacilityEmail = null,
                FacilityDescription = "\n\nOverview\nText\n",
                FacilityDirections = "",
                FacilityUseFeeDescription = "$5"
            }, "  Park  ");

            Assert.That(Details.HasCampground, Is.True);
            Assert.That(Details.CampgroundId, Is.EqualTo("990001"));
            Assert.That(Details.Name, Is.EqualTo("NAME WITH TRAILING SPACES"));
            Assert.That(Details.Context, Is.EqualTo("Park"));
            Assert.That(Details.HasContext, Is.True);
            Assert.That(Details.Phone, Is.EqualTo(CampgroundDetails.NotListed));
            Assert.That(Details.Email, Is.EqualTo(CampgroundDetails.NotListed));
            Assert.That(Details.Description, Is.EqualTo("Overview\nText"));
            Assert.That(Details.Directions, Is.EqualTo(CampgroundDetails.NotListed));
            Assert.That(Details.Fees, Is.EqualTo("$5"));

            Assert.That(CampgroundDetails.CityContext("SPRINGDALE", "UT"), Is.EqualTo("SPRINGDALE, UT"));
            Assert.That(CampgroundDetails.CityContext("", "UT"), Is.EqualTo("UT"));
            Assert.That(CampgroundDetails.CityContext(null, null), Is.Empty);
            Assert.That(new CampgroundDetails(new FacilitiesData { FacilityID = "1" }, null).HasContext, Is.False);
            // Runs of blank lines collapse to one blank line; single line breaks and paragraphs stay.
            var Spaced = new CampgroundDetails(new FacilitiesData { FacilityID = "1", FacilityDescription = "A\r\nB\n\n\n \n\nC\n\nD" }, null);
            Assert.That(Spaced.Description, Is.EqualTo("A\nB\n\nC\n\nD"));
        }

        [Test]
        public void CityDisplayConverter_ShowsEmptyCitiesAsNoCityListed()
        {
            Assert.That(Campertron.Views.DisplayConverters.City.Convert("", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo("(No city listed)"));
            Assert.That(Campertron.Views.DisplayConverters.City.Convert("   ", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo("(No city listed)"));
            Assert.That(Campertron.Views.DisplayConverters.City.Convert("SPRINGDALE", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo("SPRINGDALE"));
        }
    }
}
