using Microsoft.Extensions.DependencyModel;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Campertron.ViewModels
{
    public class ReactiveViewModel : ReactiveObject
    {
        public ReactiveViewModel()
        {
            this.WhenAnyValue(p => p.SelectedState)
                .Subscribe(p => this.RaisePropertyChanged(nameof(CityList)));

            this.WhenAnyValue(p => p.SelectedCity)
                .Subscribe(p => this.RaisePropertyChanged(nameof(CampgroundList)));

            this.WhenAnyValue(p => p.SelectedPark)
                .Subscribe(p => this.RaisePropertyChanged(nameof(CampgroundListByPark)));

            this.WhenAnyValue(p => p.SelectedCampground)
                .Subscribe(p => this.RaisePropertyChanged(nameof(CampgroundByName)));

            this.WhenAnyValue(p => p.SelectedCampground)
                .Subscribe(p => this.RaisePropertyChanged(nameof(CampgroundByName)));
        }
        private string? _SelectedPark;
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
        private string? _SelectedCity;
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
        private string? _SelectedState;
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
        private string? _SelectedCampground;
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
        private string? _SelectedCampgroundID;
        public string? SelectedCampgroundID
        {
            get
            {
                return _SelectedCampgroundID;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedCampgroundID, value);
            }
        }
        public List<String> StateList
        {
            get
            {
                return CampertronLibrary.function.sqlite.Read.UniqueStates();
            }
        }
        public List<String> CityList
        {
            get
            {
                return CampertronLibrary.function.sqlite.Read.UniqueCities(SelectedState);
            }
        }
        public List<String> CampgroundList
        {
            get
            {
                return CampertronLibrary.function.sqlite.Read.UniqueParks(SelectedState, SelectedCity);
            }
        }
        public List<String> ParkList
        {
            get
            {
                return CampertronLibrary.function.sqlite.Read.UniqueCampgrounds();
            }
        }
        public List<String> CampgroundListByPark
        {
            get
            {
                return CampertronLibrary.function.sqlite.Read.UniqueCampgroundsByPark(SelectedPark);
            }
        }
        public FacilitiesData CampgroundByName
        {
            get
            {
                return CampertronLibrary.function.sqlite.Read.GetParkCampgroundByName(SelectedCampground);                
            }
        }        
    }
}