﻿using CampertronLibrary.function.RecDotOrg.sqlite;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.IO;

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

            this.WhenAnyValue(p => p.SelectedStateByState)
                .Subscribe(p => this.RaisePropertyChanged(nameof(CampgroundListByState)));
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
        private string? _SelectedCampgroundByState;
        public string? SelectedCampgroundByState
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
                return _SelectedState;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedState, value);
            }
        }
        public string GetConfigPath
        {
            get
            {
                var folder = Environment.SpecialFolder.LocalApplicationData;
                var path = Environment.GetFolderPath(folder);
                var configpath = Path.Join(path, "CampertronConfig");
                return configpath;
            }
        }
        public List<String> StateList
        {
            get
            {
                return Read.UniqueStates(GetConfigPath);
            }
        }
        public List<String> StateListByState
        {
            get
            {
                return Read.UniqueStates(GetConfigPath);
            }
        }
        public List<String> CityList
        {
            get
            {
                return Read.UniqueCities(SelectedState, GetConfigPath);
            }
        }
        public List<String> CampgroundList
        {
            get
            {
                return Read.UniqueParks(SelectedState, SelectedCity, GetConfigPath);
            }
        }
        public List<String> ParkList
        {
            get
            {
                return Read.UniqueCampgrounds(GetConfigPath);
                
            }
        }
        public List<String> CampgroundListByPark
        {
            get
            {
                return Read.UniqueCampgroundsByPark(SelectedPark, GetConfigPath);
            }
        }
        public FacilitiesData CampgroundByName
        {
            get
            {
                return Read.GetParkCampgroundByName(SelectedCampground, GetConfigPath);                
            }
        }
        public List<String> CampgroundListByState
        {
            get
            {
                return Read.UniqueParksByState(SelectedStateByState, GetConfigPath);
            }
        }
    }
}