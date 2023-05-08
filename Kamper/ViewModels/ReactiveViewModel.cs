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
                .Subscribe(p => this.RaisePropertyChanged(nameof(CampgroundList)));

            this.WhenAnyValue(p => p.SelectedPark)
                .Subscribe(p => this.RaisePropertyChanged(nameof(CampgroundListByPark)));
        }
        private string? _SelectedPark; // This is our backing field for Name
        public string? SelectedPark
        {
            get
            {
                return _SelectedPark;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedPark, value);
            }
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
        private string? _SelectedCampground; // This is our backing field for Name
        public string? SelectedCampground
        {
            get
            {
                return _SelectedCampground;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedCampground, value);
            }
        }
        private string? _SelectedCampgroundByPark; // This is our backing field for Name
        public string? SelectedCampgroundByPark
        {
            get
            {
                return _SelectedCampgroundByPark;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedCampgroundByPark, value);
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
        public List<String> CampgroundList
        {
            get
            {
                return KampLibrary.function.sqlite.Read.UniqueParks(SelectedState, SelectedCity);
            }
        }
        public List<String> ParkList
        {
            get
            {
                return KampLibrary.function.sqlite.Read.UniqueCampgrounds();
            }
        }
        public List<String> CampgroundListByPark
        {
            get
            {
                return KampLibrary.function.sqlite.Read.UniqueCampgroundsByPark(SelectedPark);
            }
        }
    }
}
