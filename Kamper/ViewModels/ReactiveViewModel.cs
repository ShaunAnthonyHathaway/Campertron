using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kamper.ViewModels
{
    // Instead of implementing "INotifyPropertyChanged" on our own we use "ReachtiveObject" as 
    // our base class. Read more about it here: https://www.reactiveui.net
    public class ReactiveViewModel : ReactiveObject
    {
        public ReactiveViewModel()
        {
            // We can listen to any property changes with "WhenAnyValue" and do whatever we want in "Subscribe".
            this.WhenAnyValue(p => p.SelectedState)
                .Subscribe(p => this.RaisePropertyChanged(nameof(CityList)));

            this.WhenAnyValue(p => p.SelectedCity)
                .Subscribe(p => this.RaisePropertyChanged(nameof(FacilityList)));
        }
        private string? _SelectedCity; // This is our backing field for Name
        public string? SelectedCity
        {
            get
            {
                return _SelectedCity;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedCity, value);
            }
        }
        private string? _SelectedState; // This is our backing field for Name
        public string? SelectedState
        {
            get
            {
                return _SelectedState;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedState, value);
            }
        }
        private string? _SelectedFacility; // This is our backing field for Name
        public string? SelectedFacility
        {
            get
            {
                return _SelectedFacility;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedFacility, value);
            }
        }
        public List<String> StateList
        {
            get
            {
                return KampLibrary.function.sqlite.Read.UniqueStates();
            }
        }
        public List<String> CityList
        {
            get
            {
                return KampLibrary.function.sqlite.Read.UniqueCities(SelectedState);
            }
        }
        public List<String> FacilityList
        {
            get
            {
                return KampLibrary.function.sqlite.Read.UniqueFacilities(SelectedState, SelectedCity);
            }
        }
    }
}
