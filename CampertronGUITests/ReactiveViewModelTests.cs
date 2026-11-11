using System.ComponentModel;
using Avalonia.Headless.NUnit;
using Campertron.ViewModels;

namespace CampertronGUITests
{
    // ReactiveUI behavior of the production view model under ReactiveUI.Avalonia.Reactive 12.1.6 (ReactiveUI 26, with
    // source-generated WhenAnyValue). These tests only observe PropertyChanged; they never read the database-backed list
    // properties. They run as [AvaloniaTest] so ReactiveUI is initialized by UseReactiveUI as in the application.
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

        // Each WhenAnyValue subscription in the constructor re-raises the dependent list property after its source
        // property changes.
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
                nameof(ReactiveViewModel.SelectedState), nameof(ReactiveViewModel.CityList),
                nameof(ReactiveViewModel.SelectedCity), nameof(ReactiveViewModel.CampgroundList),
                nameof(ReactiveViewModel.SelectedPark), nameof(ReactiveViewModel.CampgroundListByPark),
                nameof(ReactiveViewModel.SelectedCampground), nameof(ReactiveViewModel.CampgroundByName),
            }));
        }

        // EXISTING BEHAVIOR, NOT DESIRED BEHAVIOR (known defect TD-08, docs/ai-context/technical-debt.md): the
        // SelectedStateByState property reads and writes the _SelectedState field, so the "By City" and "By State" tabs
        // share one selected state. The setter raises only SelectedStateByState (and, through WhenAnyValue,
        // CampgroundListByState), never SelectedState or CityList. This test pins that pre-migration behavior so that
        // the ReactiveUI 20 -> 26 change cannot alter it unnoticed. It must be updated when TD-08 is fixed.
        [AvaloniaTest]
        public void ExistingBehaviorTD08_ByStateSelectionSharesTheByCityStateField()
        {
            var ViewModel = new ReactiveViewModel();
            List<string?> Changes = RecordPropertyChanges(ViewModel);

            ViewModel.SelectedStateByState = GuiTestEnvironment.StateColorado;

            Assert.That(ViewModel.SelectedState, Is.EqualTo(GuiTestEnvironment.StateColorado));
            Assert.That(Changes, Is.EquivalentTo(new[]
            {
                nameof(ReactiveViewModel.SelectedStateByState), nameof(ReactiveViewModel.CampgroundListByState),
            }));
        }
    }
}
