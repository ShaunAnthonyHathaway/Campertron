using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace CampertronLibrary.function.Forms
{
    // The Searches tab (CM6, decision D44): the search files of the settings folder (every top-level *.yaml except
    // General.yaml and Email.yaml, exactly the files the console reads; never a backup or temporary file), each with its
    // name, display name, whether it is enabled (autoRun), its campground and its state, and an editor for the one chosen.
    // New search creates a file from the example search (not enabled), Save As writes the chosen file under a new name;
    // both create only and never write over a file. Files are written only when the user saves; nothing here deletes a
    // file, runs a search or shows availability. Created after the startup succeeded; the files and the campground data
    // are read when the tab is first shown.
    public sealed class SearchesViewModel : FormObject
    {
        private readonly Func<DateTime>? _Now;
        private readonly Func<FilterChoiceSource> _MakeChoiceSource;
        private FilterChoiceSource? _ChoiceSource;
        private SearchFileViewModel? _SelectedFile;
        private bool _IsLoaded;
        private OutputType? _Output;
        private OutputType? _SavedOutput;
        private string _NewSearchName = string.Empty;
        private string? _NewSearchMessage;
        private bool _NewSearchIsError;
        private string _SaveAsName = string.Empty;
        private string? _SaveAsMessage;
        private bool _SaveAsIsError;
        private string? _FolderNotice;
        private bool _FolderNoticeIsWarning;

        // Host: the form's thread (the UI thread), where the filter selectors' campground values are handed back
        public SearchesViewModel(string ConfigPath, FormHost Host) : this(ConfigPath, null, null, () => new FilterChoiceSource(ConfigPath, Host))
        {
        }

        internal SearchesViewModel(string ConfigPath, Func<DateTime>? Now, Func<List<CampgroundPickerEntry>>? LoadCampgrounds, Func<FilterChoiceSource> MakeChoiceSource)
        {
            this.ConfigPath = ConfigPath;
            _Now = Now;
            // the filter selectors' campground values (D51), made when the tab is first shown, on the UI thread
            _MakeChoiceSource = MakeChoiceSource;
            Catalog = new CampgroundCatalog(LoadCampgrounds ?? (() => Read.CampgroundPickerEntries(ConfigPath)));
            NewSearchCommand = new RelayCommand(NewSearch);
            SaveAsCommand = new RelayCommand(SaveAs, () => SelectedFile != null && SelectedFile.IsEditable);
            CheckCommand = new RelayCommand(CheckForExternalChanges);
        }

        public string ConfigPath { get; }
        internal CampgroundCatalog Catalog { get; }
        public ObservableCollection<SearchFileViewModel> Files { get; } = new ObservableCollection<SearchFileViewModel>();
        // the selectors' source of campground values (null until the tab is first shown)
        internal FilterChoiceSource? ChoiceSource => _ChoiceSource;
        public bool IsLoaded => _IsLoaded;
        public bool HasNoFiles => _IsLoaded && Files.Count == 0;

        public SearchFileViewModel? SelectedFile
        {
            get => _SelectedFile;
            set
            {
                if (!ReferenceEquals(_SelectedFile, value))
                {
                    _SelectedFile?.Deactivate();
                    this.RaiseAndSetIfChanged(ref _SelectedFile, value);
                    // only the file shown in the editor reads its campground's values
                    _SelectedFile?.Activate();
                    this.RaisePropertyChanged(nameof(HasSelection));
                    this.RaisePropertyChanged(nameof(HasNoSelection));
                    SaveAsMessage = null;
                    SaveAsCommand.Refresh();
                }
            }
        }
        public bool HasSelection => SelectedFile != null;
        public bool HasNoSelection => _IsLoaded && Files.Count > 0 && SelectedFile == null;

        // the settings folder's search files as the console's Search and email mode use them (no search file, or none
        // enabled and usable), or null
        public string? FolderNotice
        {
            get => _FolderNotice;
            private set
            {
                this.RaiseAndSetIfChanged(ref _FolderNotice, value);
                this.RaisePropertyChanged(nameof(HasFolderNotice));
                this.RaisePropertyChanged(nameof(FolderNoticeText));
            }
        }
        public bool HasFolderNotice => FolderNotice != null;
        public bool FolderNoticeIsWarning
        {
            get => _FolderNoticeIsWarning;
            private set => this.RaiseAndSetIfChanged(ref _FolderNoticeIsWarning, value);
        }
        public string FolderNoticeText => FolderNotice == null ? string.Empty : (FolderNoticeIsWarning ? "Warning: " : "Info: ") + FolderNotice;

        public string NewSearchName
        {
            get => _NewSearchName;
            set => this.RaiseAndSetIfChanged(ref _NewSearchName, value ?? string.Empty);
        }
        public RelayCommand NewSearchCommand { get; }
        public string? NewSearchMessage
        {
            get => _NewSearchMessage;
            private set
            {
                this.RaiseAndSetIfChanged(ref _NewSearchMessage, value);
                this.RaisePropertyChanged(nameof(HasNewSearchMessage));
            }
        }
        public bool HasNewSearchMessage => NewSearchMessage != null;
        public bool NewSearchIsError
        {
            get => _NewSearchIsError;
            private set => this.RaiseAndSetIfChanged(ref _NewSearchIsError, value);
        }

        public string SaveAsName
        {
            get => _SaveAsName;
            set => this.RaiseAndSetIfChanged(ref _SaveAsName, value ?? string.Empty);
        }
        public RelayCommand SaveAsCommand { get; }
        public string? SaveAsMessage
        {
            get => _SaveAsMessage;
            private set
            {
                this.RaiseAndSetIfChanged(ref _SaveAsMessage, value);
                this.RaisePropertyChanged(nameof(HasSaveAsMessage));
            }
        }
        public bool HasSaveAsMessage => SaveAsMessage != null;
        public bool SaveAsIsError
        {
            get => _SaveAsIsError;
            private set => this.RaiseAndSetIfChanged(ref _SaveAsIsError, value);
        }

        // looks at the folder and every file on disk again
        public RelayCommand CheckCommand { get; }

        // the Searches tab is shown: the first time the files and the campground data are read, later the files are
        // compared with the disk (a clean editor follows a change, an edited one opens a conflict)
        public void Show()
        {
            if (!_IsLoaded)
            {
                _IsLoaded = true;
                _ChoiceSource ??= _MakeChoiceSource();
                Catalog.Ensure();
                Rescan();
                SelectedFile = Files.FirstOrDefault();
                return;
            }
            CheckForExternalChanges();
        }

        public void CheckForExternalChanges()
        {
            if (!_IsLoaded)
            {
                return;
            }
            foreach (SearchFileViewModel File in Files.ToList())
            {
                File.CheckForExternalChanges();
            }
            Rescan();
            // the campground data may have been refreshed meanwhile (by the console): the shown values follow it
            SelectedFile?.EnsureChoices();
        }

        // whether this tab holds unsaved changes to the file (the Results tab doesn't search it until they are saved or
        // reverted, GS2); a file the tab has not read has none
        internal bool IsUnsaved(string FileName) => Files.Any(F => F.IsDirty && string.Equals(F.FileName, FileName, StringComparison.Ordinal));

        // General.yaml's outputTo as the General editor shows it (the search validation's messages mention the Email loop)
        // and, since D73, as General.yaml has it on disk: with either one Email or Ntfy, the tab says when the unattended
        // monitoring would have no search to check
        internal void SetOutput(OutputType? Output) => SetOutput(Output, null);

        internal void SetOutput(OutputType? Output, OutputType? Saved)
        {
            _Output = Output;
            _SavedOutput = Saved;
            foreach (SearchFileViewModel File in Files)
            {
                File.SetOutput(Output);
                File.SetMonitoring(MonitoringOutput);
            }
            UpdateFolderNotice();
        }

        // the unattended monitoring's outputTo when the General editor shows it or General.yaml has it (the shown one first),
        // else null (D73)
        internal OutputType? MonitoringOutput => IsMonitoring(_Output) ? _Output : IsMonitoring(_SavedOutput) ? _SavedOutput : null;

        private static bool IsMonitoring(OutputType? Output) => Output == OutputType.Email || Output == OutputType.Ntfy;

        // The folder's search files against the list: new files are added, files gone from disk are removed unless they
        // have unsaved changes (those stay, with their conflict, until the user resolves it), the order is by name and the
        // chosen file stays chosen while it is listed.
        private void Rescan()
        {
            List<string> Names;
            try
            {
                Names = Directory.Exists(ConfigPath) ? ConfigFiles.SortedSearchFileNames(ConfigPath) : new List<string>();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Names = Files.Select(F => F.FileName).ToList();
            }
            var OnDisk = new HashSet<string>(Names, StringComparer.Ordinal);
            SearchFileViewModel? Selected = SelectedFile;
            foreach (SearchFileViewModel File in Files.ToList())
            {
                if (!OnDisk.Contains(File.FileName) && !File.IsDirty)
                {
                    Files.Remove(File);
                }
            }
            foreach (string Name in Names)
            {
                if (!Files.Any(F => F.FileName == Name))
                {
                    Files.Add(Open(Name));
                }
            }
            List<SearchFileViewModel> Ordered = Files.OrderBy(F => F.FileName, StringComparer.OrdinalIgnoreCase).ThenBy(F => F.FileName, StringComparer.Ordinal).ToList();
            for (int i = 0; i < Ordered.Count; i++)
            {
                int At = Files.IndexOf(Ordered[i]);
                if (At != i)
                {
                    Files.Move(At, i);
                }
            }
            SelectedFile = Selected != null && Files.Contains(Selected) ? Selected : null;
            this.RaisePropertyChanged(nameof(HasNoFiles));
            this.RaisePropertyChanged(nameof(HasNoSelection));
            UpdateFolderNotice();
        }

        private SearchFileViewModel Open(string FileName)
        {
            Func<string, CampgroundChoice?>? Lookup = Catalog.LoadError == null ? Id => Catalog.Find(Id)?.Choice : null;
            var File = new SearchFileViewModel(new ConfigEditSession<CampertronConfig>(ConfigPath, FileName, _Now, Lookup), Catalog, _ChoiceSource);
            File.SetOutput(_Output);
            File.SetMonitoring(MonitoringOutput);
            File.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SearchFileViewModel.IsEditable) && ReferenceEquals(File, SelectedFile))
                {
                    SaveAsCommand.Refresh();
                }
                if (e.PropertyName == nameof(SearchFileViewModel.IsDirty))
                {
                    UpdateFolderNotice();
                }
                if (e.PropertyName == nameof(SearchFileViewModel.StatusText))
                {
                    // a save (or a reload) changed what is on disk: the folder notice and the other files' warnings follow
                    // it (D73; General.yaml's check of outputTo reads the files again when the Settings tab is shown)
                    UpdateFolderNotice();
                }
            };
            return File;
        }

        // the files on disk as the console's Search and email mode run them (with none enabled, email mode stops at its start
        // with an error since D64, open question #40; the GUI only shows it). Since D73, with the monitoring's outputTo shown
        // or saved, by the monitoring start's own check (ConfigValidation.MonitoringSearchErrors): no search to check, or a
        // search file it stops on
        private void UpdateFolderNotice()
        {
            if (!_IsLoaded)
            {
                return;
            }
            foreach (SearchFileViewModel Each in Files)
            {
                Each.UpdateMonitoringWarning();
            }
            if (MonitoringOutput is OutputType Monitoring)
            {
                List<ConfigDiagnostic> Errors = ConfigValidation.MonitoringSearchErrors(ConfigPath, Monitoring);
                FolderNoticeIsWarning = Errors.Count > 0;
                FolderNotice = Errors.Count == 0 ? null : MonitoringNotice(Monitoring, Errors);
                return;
            }
            List<SearchFileViewModel> OnDisk = Files.Where(F => File.Exists(F.FilePath)).ToList();
            ConfigDiagnostic? Folder = ConfigValidation.SearchFolderDiagnostic(OnDisk.Count, OnDisk.Any(F => F.Session.IsEnabledAndUsable), _Output);
            FolderNoticeIsWarning = Folder?.Severity == ConfigSeverity.Warning;
            FolderNotice = Folder == null ? null : Folder.Code == ConfigCode.NoSearchFiles ? NoSearchFilesNotice : Folder.Message + " " + EnableNotice;
        }

        // the folder notice's own words (V011-3, D63): the console creates its example search when it next searches, not
        // when the GUI or the console starts
        internal const string NoSearchFilesNotice = "There is no search file. Create one with New search. While there is none, the console's Search creates "
            + "and runs its example search the next time you choose it, and email mode stops with an error.";
        internal const string EnableNotice = "To enable a search, check " + AutoRunWording.Enabled + " and save it.";

        // the folder notice with the monitoring's outputTo (D73): what its start finds, and that it refuses to start until then
        internal static string MonitoringNotice(OutputType Monitoring, IEnumerable<ConfigDiagnostic> Errors) =>
            string.Join(" ", Errors.Select(D => D.Message).Distinct()) + " Until then, " + ModeName(Monitoring)
            + " refuses to start. Searches can still be edited, saved and run in the Results tab.";

        internal static string ModeName(OutputType Monitoring) => Monitoring == OutputType.Ntfy ? "ntfy mode (outputTo: Ntfy)" : "email mode (outputTo: Email)";

        public void NewSearch()
        {
            ConfigEditResult Result = SearchFiles.Create(ConfigPath, NewSearchName, out string? Created);
            NewSearchMessage = Result.Message;
            NewSearchIsError = !Result.Succeeded;
            Rescan();
            if (Created != null)
            {
                NewSearchName = string.Empty;
                SelectedFile = Files.FirstOrDefault(F => F.FileName == Created);
            }
        }

        public void SaveAs()
        {
            SearchFileViewModel? Source = SelectedFile;
            if (Source == null)
            {
                return;
            }
            ConfigEditResult Result = Source.Session.SaveAs(SaveAsName, out string? Created);
            SaveAsMessage = Result.Message;
            SaveAsIsError = !Result.Succeeded;
            if (Created == null)
            {
                return;
            }
            // the edits are now in the new file: the file they were made on shows what is on disk again
            Source.DiscardAfterSaveAs();
            Rescan();
            SaveAsName = string.Empty;
            SelectedFile = Files.FirstOrDefault(F => F.FileName == Created);
            SaveAsMessage = Result.Message;
            SaveAsIsError = false;
        }
    }
}
