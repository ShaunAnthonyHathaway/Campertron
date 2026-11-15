using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.sqlite;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Campertron.ViewModels
{
    // The Searches tab (CM6, decision D44): the search files of the settings folder (every top-level *.yaml except
    // General.yaml and Email.yaml, exactly the files the console runs; never a backup or temporary file), each with its
    // name, display name, whether it runs, its campground and its state, and an editor for the one chosen. New search
    // creates a file from the example search (disabled), Save As writes the chosen file under a new name; both create only
    // and never write over a file. Files are written only when the user saves; nothing here deletes a file, runs a search
    // or shows availability. Created after the startup succeeded; the files and the campground data are read when the tab
    // is first shown.
    public sealed class SearchesViewModel : ViewModelBase
    {
        private readonly Func<DateTime>? _Now;
        private SearchFileViewModel? _SelectedFile;
        private bool _IsLoaded;
        private OutputType? _Output;
        private string _NewSearchName = string.Empty;
        private string? _NewSearchMessage;
        private bool _NewSearchIsError;
        private string _SaveAsName = string.Empty;
        private string? _SaveAsMessage;
        private bool _SaveAsIsError;
        private string? _FolderNotice;
        private bool _FolderNoticeIsWarning;

        public SearchesViewModel(string ConfigPath) : this(ConfigPath, null, null)
        {
        }

        internal SearchesViewModel(string ConfigPath, Func<DateTime>? Now, Func<List<CampgroundPickerEntry>>? LoadCampgrounds)
        {
            this.ConfigPath = ConfigPath;
            _Now = Now;
            Catalog = new CampgroundCatalog(LoadCampgrounds ?? (() => Read.CampgroundPickerEntries(ConfigPath)));
            NewSearchCommand = new RelayCommand(NewSearch);
            SaveAsCommand = new RelayCommand(SaveAs, () => SelectedFile != null && SelectedFile.IsEditable);
            CheckCommand = new RelayCommand(CheckForExternalChanges);
        }

        public string ConfigPath { get; }
        internal CampgroundCatalog Catalog { get; }
        public ObservableCollection<SearchFileViewModel> Files { get; } = new ObservableCollection<SearchFileViewModel>();
        public bool IsLoaded => _IsLoaded;
        public bool HasNoFiles => _IsLoaded && Files.Count == 0;

        public SearchFileViewModel? SelectedFile
        {
            get => _SelectedFile;
            set
            {
                if (!ReferenceEquals(_SelectedFile, value))
                {
                    this.RaiseAndSetIfChanged(ref _SelectedFile, value);
                    this.RaisePropertyChanged(nameof(HasSelection));
                    this.RaisePropertyChanged(nameof(HasNoSelection));
                    SaveAsMessage = null;
                    SaveAsCommand.Refresh();
                }
            }
        }
        public bool HasSelection => SelectedFile != null;
        public bool HasNoSelection => _IsLoaded && Files.Count > 0 && SelectedFile == null;

        // the settings folder's search files as a start uses them (no search file, or none that runs), or null
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
        }

        // General.yaml's outputTo as the General editor shows it (the search validation's messages mention the Email loop)
        internal void SetOutput(OutputType? Output)
        {
            _Output = Output;
            foreach (SearchFileViewModel File in Files)
            {
                File.SetOutput(Output);
            }
            UpdateFolderNotice();
        }

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
            var File = new SearchFileViewModel(new ConfigEditSession<CampertronConfig>(ConfigPath, FileName, _Now, Lookup), Catalog);
            File.SetOutput(_Output);
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
            };
            return File;
        }

        // the files on disk as a start runs them (open question #40: with none enabled the Email loop keeps checking 0
        // searches; shown, not changed)
        private void UpdateFolderNotice()
        {
            if (!_IsLoaded)
            {
                return;
            }
            List<SearchFileViewModel> OnDisk = Files.Where(F => File.Exists(F.FilePath)).ToList();
            ConfigDiagnostic? Folder = ConfigValidation.SearchFolderDiagnostic(OnDisk.Count, OnDisk.Any(F => F.Session.RunsAtStart), _Output);
            FolderNoticeIsWarning = Folder?.Severity == ConfigSeverity.Warning;
            FolderNotice = Folder == null ? null : Folder.Code == ConfigCode.NoSearchFiles
                ? "There is no search file. Create one with New search; Campertron also creates and runs its example search at its next start when there is none."
                : Folder.Message + " Enable a search with Run this search (autoRun) and save it.";
        }

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
