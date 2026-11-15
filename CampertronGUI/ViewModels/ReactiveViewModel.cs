using CampertronLibrary.function.RecDotOrg.sqlite;
using ReactiveUI.Binding.Reactive;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.IO;

namespace Campertron.ViewModels
{
    public class ReactiveViewModel : ReactiveObject
    {
        // The GUI's default location of CampertronConfig (LocalApplicationData/CampertronConfig, as Load.GetConfig uses
        // on the desktop). Used only where Load.GetConfig has not returned a path: the designer, view-model tests and
        // the startup-error message.
        public static string DefaultConfigPath
        {
            get
            {
                var folder = Environment.SpecialFolder.LocalApplicationData;
                var path = Environment.GetFolderPath(folder);
                var configpath = Path.Join(path, "CampertronConfig");
                return configpath;
            }
        }

        public ReactiveViewModel() : this(DefaultConfigPath)
        {
        }

        // ConfigPath is CampertronInternalConfig.ConfigPath from Load.GetConfig (MainWindowViewModel creates this view
        // model only after it succeeded).
        public ReactiveViewModel(string ConfigPath)
        {
            this.ConfigPath = ConfigPath;
            // A campground is selected from its tab's current list (decision D42): a filter change clears the tab's
            // campground before its list is raised again, so a campground of the previous filter (or one with the same
            // name) never stays selected and no details of it remain on screen.
            this.WhenAnyValue(p => p.SelectedState)
                .Subscribe(p =>
                {
                    SelectedCampground = null;
                    this.RaisePropertyChanged(nameof(CityList));
                });

            this.WhenAnyValue(p => p.SelectedCity)
                .Subscribe(p =>
                {
                    SelectedCampground = null;
                    this.RaisePropertyChanged(nameof(CampgroundList));
                });

            this.WhenAnyValue(p => p.SelectedPark)
                .Subscribe(p =>
                {
                    SelectedCampgroundByPark = null;
                    this.RaisePropertyChanged(nameof(CampgroundListByPark));
                });

            this.WhenAnyValue(p => p.SelectedStateByState)
                .Subscribe(p =>
                {
                    SelectedCampgroundByState = null;
                    this.RaisePropertyChanged(nameof(CampgroundListByState));
                });

            // Each tab has its own campground selection and details (beta.8). The details also follow the tab's
            // filters, so a filter change never leaves another filter's campground context on screen.
            this.WhenAnyValue(p => p.SelectedPark, p => p.SelectedCampgroundByPark)
                .Subscribe(p => this.RaisePropertyChanged(nameof(ParkCampgroundDetails)));

            this.WhenAnyValue(p => p.SelectedState, p => p.SelectedCity, p => p.SelectedCampground)
                .Subscribe(p => this.RaisePropertyChanged(nameof(CityCampgroundDetails)));

            this.WhenAnyValue(p => p.SelectedStateByState, p => p.SelectedCampgroundByState)
                .Subscribe(p => this.RaisePropertyChanged(nameof(StateCampgroundDetails)));
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
        // The campground selected on each tab: an entry of that tab's list, identified by its FacilityID (D42, TD-38).
        private CampgroundChoice? _SelectedCampground;
        public CampgroundChoice? SelectedCampground
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
        private CampgroundChoice? _SelectedCampgroundByState;
        public CampgroundChoice? SelectedCampgroundByState
        {
            get
            {
                return _SelectedCampgroundByState;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedCampgroundByState, value);
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
        private string? _SelectedStateByState;
        public string? SelectedStateByState
        {
            get
            {
                return _SelectedStateByState;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedStateByState, value);
            }
        }
        private CampgroundChoice? _SelectedCampgroundByPark;
        public CampgroundChoice? SelectedCampgroundByPark
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
        public string ConfigPath { get; }
        public List<String> StateList
        {
            get
            {
                return Read.UniqueStates(ConfigPath);
            }
        }
        public List<String> StateListByState
        {
            get
            {
                return Read.UniqueStates(ConfigPath);
            }
        }
        public List<String> CityList
        {
            get
            {
                return Read.UniqueCities(SelectedState, ConfigPath);
            }
        }
        // The campground lists hold one CampgroundChoice per FacilityID (D42): campgrounds that share a name stay
        // separate entries, each with its location and ID, in CampgroundChoice.ListOrder.
        public List<CampgroundChoice> CampgroundList
        {
            get
            {
                return Read.CampgroundChoicesByStateAndCity(SelectedState, SelectedCity, ConfigPath);
            }
        }
        // Every park with a campground, also one whose campgrounds have no address (By Park is not a location filter).
        public List<String> ParkList
        {
            get
            {
                return Read.ParksWithCampgrounds(ConfigPath);
            }
        }
        public List<CampgroundChoice> CampgroundListByPark
        {
            get
            {
                return Read.CampgroundChoicesByPark(SelectedPark, ConfigPath);
            }
        }
        // One details snapshot per tab. Each getter runs one query (none while no campground is selected), so bind
        // each of them exactly once.
        public CampgroundDetails ParkCampgroundDetails
        {
            get
            {
                return Details(SelectedCampgroundByPark, SelectedPark);
            }
        }
        public CampgroundDetails CityCampgroundDetails
        {
            get
            {
                return Details(SelectedCampground, CampgroundDetails.CityContext(SelectedCity, SelectedState));
            }
        }
        public CampgroundDetails StateCampgroundDetails
        {
            get
            {
                return Details(SelectedCampgroundByState, SelectedStateByState);
            }
        }
        // The details of exactly the selected campground, looked up by its FacilityID, never by its name.
        private CampgroundDetails Details(CampgroundChoice? Campground, string? Context)
        {
            if (Campground == null)
            {
                return CampgroundDetails.None;
            }
            FacilitiesData? Facility = Read.GetCampgroundDetailsById(Campground.FacilityID, ConfigPath);
            return Facility == null ? CampgroundDetails.None : new CampgroundDetails(Facility, Context);
        }
        public List<CampgroundChoice> CampgroundListByState
        {
            get
            {
                return Read.CampgroundChoicesByState(SelectedStateByState, ConfigPath);
            }
        }
    }
}