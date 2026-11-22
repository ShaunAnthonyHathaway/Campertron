using CampertronLibrary.function.Base;
using ReactiveUI.Reactive;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace Campertron.ViewModels
{
    // The Settings tab (CM5, decision D43): editors for General.yaml, Email.yaml and, since D69, Ntfy.yaml in the settings
    // folder Load.GetConfig uses (created only after the startup succeeded, like the lookup tabs). Search files are not
    // edited here (CM6), and nothing here searches, connects, sends or publishes. Email.yaml's and Ntfy.yaml's checks follow
    // the outputTo the General editor shows; no editor changes another file.
    public sealed class SettingsViewModel : ViewModelBase
    {
        // which file the tab shows
        private enum Shown
        {
            General,
            Email,
            Ntfy,
        }

        private Shown _Selected = Shown.General;
        private string? _FolderStatus;

        public SettingsViewModel(string ConfigPath) : this(ConfigPath, null)
        {
        }

        internal SettingsViewModel(string ConfigPath, Func<DateTime>? Now)
        {
            this.ConfigPath = ConfigPath;
            General = new GeneralSettingsViewModel(new ConfigEditSession<GeneralConfig>(ConfigPath, Now));
            Email = new EmailSettingsViewModel(new ConfigEditSession<EmailConfig>(ConfigPath, Now));
            Ntfy = new NtfySettingsViewModel(new ConfigEditSession<NtfyConfig>(ConfigPath, Now));
            Email.SetEmailOutput(General.IsEmailOutput);
            Ntfy.SetOutput(General.OutputToValue);
            Ntfy.ShowGeneral = () => IsGeneralSelected = true;
            General.PropertyChanged += General_PropertyChanged;
            Ntfy.PropertyChanged += Ntfy_PropertyChanged;
            OpenFolderCommand = new RelayCommand(() => _ = OpenFolder());
        }

        public string ConfigPath { get; }
        public GeneralSettingsViewModel General { get; }
        public EmailSettingsViewModel Email { get; }
        public NtfySettingsViewModel Ntfy { get; }

        // which file the tab shows (three radio buttons): a radio button that is unchecked sets false, which changes nothing
        public bool IsGeneralSelected
        {
            get => _Selected == Shown.General;
            set
            {
                if (value)
                {
                    Select(Shown.General);
                }
            }
        }
        public bool IsEmailSelected
        {
            get => _Selected == Shown.Email;
            set
            {
                if (value)
                {
                    Select(Shown.Email);
                }
            }
        }
        public bool IsNtfySelected
        {
            get => _Selected == Shown.Ntfy;
            set
            {
                if (value)
                {
                    Select(Shown.Ntfy);
                }
            }
        }

        private void Select(Shown File)
        {
            if (_Selected == File)
            {
                return;
            }
            _Selected = File;
            this.RaisePropertyChanged(nameof(IsGeneralSelected));
            this.RaisePropertyChanged(nameof(IsEmailSelected));
            this.RaisePropertyChanged(nameof(IsNtfySelected));
            Email.HidePassword();
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
            Ntfy.CheckForExternalChanges();
            General.RefreshNtfyStatus();
        }

        // leaving the tab or the window: the password is masked again
        public void HideSecrets()
        {
            Email.HidePassword();
        }

        // whether any of the three editors holds unsaved changes (an automatic restart waits for them, D62)
        public bool HasUnsavedChanges => General.IsDirty || Email.IsDirty || Ntfy.IsDirty;

        private void General_PropertyChanged(object? Sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GeneralSettingsViewModel.IsEmailOutput))
            {
                Email.SetEmailOutput(General.IsEmailOutput);
                Ntfy.SetOutput(General.OutputToValue);
            }
        }

        // a save, reload or conflict resolution of Ntfy.yaml: the General form's read-only line about ntfy reads it again
        private void Ntfy_PropertyChanged(object? Sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsFileViewModel.StatusText))
            {
                General.RefreshNtfyStatus();
            }
        }
    }
}
