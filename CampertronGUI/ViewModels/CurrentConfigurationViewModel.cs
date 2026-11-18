using CampertronLibrary.function.Base;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Campertron.ViewModels
{
    // Settings > System > Current configuration (V011-2, decision D60): the eight items of the console's View Config,
    // read-only, from CurrentConfiguration, the library's one formatting of them. It exists once the startup has succeeded
    // (MainWindowViewModel creates it with the startup's folders) and reads again (General.yaml read-only, the settings
    // folder listed by name) when Settings > System is shown and when the window is activated while it is shown; it never
    // watches or polls. Copy puts exactly the shown items' text on the clipboard; Open settings folder shows the settings
    // folder in the file manager. Both run only on the user's action, through seams the view sets (tests replace them).
    // Nothing here writes or creates a file, and nothing shows a value of Email.yaml.
    public sealed class CurrentConfigurationViewModel : ViewModelBase
    {
        private readonly Func<ConfigurationSummary> _Read;
        private ConfigurationSummary _Summary;
        private string? _Status;
        private bool _StatusIsError;

        public CurrentConfigurationViewModel(string ConfigPath, string? CachePath)
            : this(ConfigPath, () => CurrentConfiguration.Read(ConfigPath, CachePath, DateTime.UtcNow, CultureInfo.CurrentCulture, typeof(App).Assembly))
        {
        }

        // tests pass their own reader
        internal CurrentConfigurationViewModel(string ConfigPath, Func<ConfigurationSummary> Read)
        {
            this.ConfigPath = ConfigPath;
            _Read = Read;
            _Summary = Read();
            Reads = 1;
            CopyCommand = new RelayCommand(() => _ = Copy());
            OpenFolderCommand = new RelayCommand(() => _ = OpenFolder());
        }

        // the settings folder of the startup (Load.GetConfig), the folder Open settings folder shows
        public string ConfigPath { get; }

        // the eight items, in order, as shown
        public IReadOnlyList<ConfigurationItem> Items => _Summary.Items;

        // exactly what Copy puts on the clipboard: every shown item as "Label => value", one per line
        public string Text => _Summary.Text;

        // how often the items were read (tests check the refresh points)
        internal int Reads { get; private set; }

        // Reads the items again. A change of what is shown replaces the whole list; an unchanged read changes nothing, so a
        // screen reader is not told about a list that did not change.
        public void Refresh()
        {
            ConfigurationSummary Next = _Read();
            Reads++;
            if (Next.Text == _Summary.Text)
            {
                return;
            }
            _Summary = Next;
            this.RaisePropertyChanged(nameof(Items));
            this.RaisePropertyChanged(nameof(Text));
        }

        // ---- actions (only on the user's action) ----

        public RelayCommand CopyCommand { get; }
        public RelayCommand OpenFolderCommand { get; }

        // set by the view: the clipboard and the platform's launcher (Avalonia's, a directory to show: never a command line
        // or a shell); a view model that already has them (tests) keeps its own
        public Func<string, Task<bool>>? CopyText { get; set; }
        public Func<DirectoryInfo, Task<bool>>? FolderLauncher { get; set; }

        public string? Status
        {
            get => _Status;
            private set
            {
                this.RaiseAndSetIfChanged(ref _Status, value);
                this.RaisePropertyChanged(nameof(HasStatus));
            }
        }
        public bool HasStatus => Status != null;
        public bool StatusIsError
        {
            get => _StatusIsError;
            private set => this.RaiseAndSetIfChanged(ref _StatusIsError, value);
        }

        // Copies the items as shown (the text read last, never a new read or another source). A missing or failing
        // clipboard is reported, never thrown.
        public async Task Copy()
        {
            string Shown = Text;
            bool Copied;
            try
            {
                Copied = CopyText != null && await CopyText(Shown);
            }
            catch (Exception)
            {
                Copied = false;
            }
            SetStatus(Copied ? "Copied the current configuration to the clipboard."
                : "Campertron couldn't copy the current configuration. Select the values and copy them yourself.", !Copied);
        }

        // Shows the startup's settings folder in the file manager, only if it is still a folder. A failure is reported with
        // the folder, never thrown.
        public async Task OpenFolder()
        {
            bool Opened;
            try
            {
                Opened = FolderLauncher != null && Directory.Exists(ConfigPath) && await FolderLauncher(new DirectoryInfo(ConfigPath));
            }
            catch (Exception)
            {
                Opened = false;
            }
            SetStatus(Opened ? "Opened the settings folder in your file manager."
                : "Campertron couldn't open the settings folder. It is " + ConfigPath, !Opened);
        }

        private void SetStatus(string Text, bool IsError)
        {
            StatusIsError = IsError;
            // the same message again is announced again: the status is cleared first
            Status = null;
            Status = Text;
        }
    }
}
