using CampertronLibrary.function.Base;
using ReactiveUI.Reactive;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace Campertron.ViewModels
{
    // The Settings tab (CM5, decision D43): editors for General.yaml and Email.yaml in the settings folder Load.GetConfig
    // uses (created only after the startup succeeded, like the lookup tabs). Search files are not edited here (CM6), and
    // nothing here searches, connects or sends. Email.yaml's checks follow the outputTo the General editor shows.
    public sealed class SettingsViewModel : ViewModelBase
    {
        private bool _IsGeneralSelected = true;
        private string? _FolderStatus;

        public SettingsViewModel(string ConfigPath) : this(ConfigPath, null)
        {
        }

        internal SettingsViewModel(string ConfigPath, Func<DateTime>? Now)
        {
            this.ConfigPath = ConfigPath;
            General = new GeneralSettingsViewModel(new ConfigEditSession<GeneralConfig>(ConfigPath, Now));
            Email = new EmailSettingsViewModel(new ConfigEditSession<EmailConfig>(ConfigPath, Now));
            Email.SetEmailOutput(General.IsEmailOutput);
            General.PropertyChanged += General_PropertyChanged;
            OpenFolderCommand = new RelayCommand(() => _ = OpenFolder());
        }

        public string ConfigPath { get; }
        public GeneralSettingsViewModel General { get; }
        public EmailSettingsViewModel Email { get; }

        // which file the tab shows (two radio buttons)
        public bool IsGeneralSelected
        {
            get => _IsGeneralSelected;
            set
            {
                if (_IsGeneralSelected != value)
                {
                    this.RaiseAndSetIfChanged(ref _IsGeneralSelected, value);
                    this.RaisePropertyChanged(nameof(IsEmailSelected));
                    Email.HidePassword();
                }
            }
        }
        public bool IsEmailSelected
        {
            get => !_IsGeneralSelected;
            set => IsGeneralSelected = !value;
        }

        // opens the settings folder in the platform's file manager (the view sets it to Avalonia's launcher; tests replace
        // it). It gets the settings folder as a directory to show, never a command line.
        public Func<DirectoryInfo, Task<bool>>? FolderLauncher { get; set; }
        public RelayCommand OpenFolderCommand { get; }
        public string? FolderStatus
        {
            get => _FolderStatus;
            private set
            {
                this.RaiseAndSetIfChanged(ref _FolderStatus, value);
                this.RaisePropertyChanged(nameof(HasFolderStatus));
            }
        }
        public bool HasFolderStatus => FolderStatus != null;

        public async Task OpenFolder()
        {
            FolderStatus = null;
            bool Opened = false;
            try
            {
                Opened = FolderLauncher != null && Directory.Exists(ConfigPath) && await FolderLauncher(new DirectoryInfo(ConfigPath));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is NotSupportedException
                || ex is System.ComponentModel.Win32Exception)
            {
                Opened = false;
            }
            if (!Opened)
            {
                FolderStatus = "Campertron couldn't open the settings folder. It is " + ConfigPath;
            }
        }

        // when the tab is shown or the window is activated: each editor looks at its file on disk
        public void CheckForExternalChanges()
        {
            General.CheckForExternalChanges();
            Email.CheckForExternalChanges();
        }

        // leaving the tab or the window: the password is masked again
        public void HideSecrets()
        {
            Email.HidePassword();
        }

        private void General_PropertyChanged(object? Sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GeneralSettingsViewModel.IsEmailOutput))
            {
                Email.SetEmailOutput(General.IsEmailOutput);
            }
        }
    }
}
