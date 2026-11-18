using Avalonia;
using Avalonia.Styling;
using ReactiveUI.Reactive;
using System;

namespace Campertron.ViewModels
{
    // Settings > System (decision D50): the GUI's theme. System follows the operating system (Avalonia's Default
    // variant), Light and Dark force that theme. A choice takes effect at once and is saved for the next start in the GUI's
    // own preferences file (ThemePreferenceStore), never in General.yaml, Email.yaml or a search file. Nothing of the theme
    // reads the database or the settings folder, so it exists from the start of the application (App.PrepareTheme). The
    // current configuration below it (V011-2, decision D60) reads the settings folder, so it is null until the startup has
    // succeeded, when MainWindowViewModel gives it the startup's folders.
    public sealed class SystemSettingsViewModel : ViewModelBase
    {
        private readonly ThemePreferenceStore _Store;
        private readonly Action<ThemeChoice> _Apply;
        private readonly Func<ThemeVariant?> _Actual;
        private ThemeChoice _Theme;
        private string? _Status;
        private bool _StatusIsError;
        private CurrentConfigurationViewModel? _Configuration;

        internal SystemSettingsViewModel(ThemePreferenceStore Store, ThemeLoad Loaded, Action<ThemeChoice> Apply, Func<ThemeVariant?> Actual)
        {
            _Store = Store;
            _Apply = Apply;
            _Actual = Actual;
            _Theme = Loaded.Choice;
            StartSource = Loaded.Source;
            LoadNotice = Loaded.UsedFallback
                ? "The saved theme preference couldn't be used, so Campertron follows the system theme. Choosing a theme here replaces it."
                : null;
        }

        // The application's own: applies a choice to it and shows the theme it actually uses (which, for System, the
        // operating system decides and may change while Campertron runs).
        internal static SystemSettingsViewModel ForApplication(Application App, ThemePreferenceStore Store, ThemeLoad Loaded)
        {
            var ViewModel = new SystemSettingsViewModel(Store, Loaded, Choice => AppTheme.Apply(App, Choice), () => App.ActualThemeVariant);
            App.ActualThemeVariantChanged += (_, _) => ViewModel.RaisePropertyChanged(nameof(ShownTheme));
            return ViewModel;
        }

        internal ThemePreferenceStore Store => _Store;

        // Current configuration (V011-2, decision D60): the console's View Config items, read-only; null until the startup
        // has succeeded (MainWindowViewModel sets it then, with the startup's folders).
        public CurrentConfigurationViewModel? Configuration
        {
            get => _Configuration;
            internal set
            {
                this.RaiseAndSetIfChanged(ref _Configuration, value);
                this.RaisePropertyChanged(nameof(HasConfiguration));
            }
        }
        public bool HasConfiguration => Configuration != null;

        // where the theme of the start came from (the smoke test reports it)
        internal ThemePreferenceSource StartSource { get; }

        public ThemeChoice Theme
        {
            get => _Theme;
            set
            {
                if (_Theme == value)
                {
                    return;
                }
                this.RaiseAndSetIfChanged(ref _Theme, value);
                this.RaisePropertyChanged(nameof(IsSystemTheme));
                this.RaisePropertyChanged(nameof(IsLightTheme));
                this.RaisePropertyChanged(nameof(IsDarkTheme));
                _Apply(value);
                this.RaisePropertyChanged(nameof(ShownTheme));
                string? Failure = _Store.Save(value);
                LoadNotice = null;
                this.RaisePropertyChanged(nameof(LoadNotice));
                this.RaisePropertyChanged(nameof(HasLoadNotice));
                SetStatus(Failure == null
                    ? "Saved. Campertron starts with this theme next time."
                    : "Campertron couldn't save the theme for the next start (" + Failure.TrimEnd('.') + "). It applies until Campertron is closed.",
                    Failure != null);
            }
        }

        // The three radio buttons. Checking one chooses its theme; unchecking (by checking another) changes nothing.
        public bool IsSystemTheme
        {
            get => Theme == ThemeChoice.System;
            set => Choose(value, ThemeChoice.System);
        }
        public bool IsLightTheme
        {
            get => Theme == ThemeChoice.Light;
            set => Choose(value, ThemeChoice.Light);
        }
        public bool IsDarkTheme
        {
            get => Theme == ThemeChoice.Dark;
            set => Choose(value, ThemeChoice.Dark);
        }

        // What the window shows now, in words.
        public string ShownTheme
        {
            get
            {
                ThemeVariant? Actual = _Actual();
                string Name = Actual == ThemeVariant.Dark ? "Dark" : Actual == ThemeVariant.Light ? "Light" : Actual?.ToString() ?? "unknown";
                return Theme == ThemeChoice.System
                    ? "Campertron is shown in its " + Name + " theme, as the system asks."
                    : "Campertron is shown in its " + Name + " theme.";
            }
        }

        // Where the choice is kept, for the user who wants to know (it is not a settings file).
        public string StorageText => _Store.FilePath == null
            ? "Your choice can't be saved on this computer: there is no folder for your user's application data."
            : "Saved for your user account in " + _Store.FilePath + ", apart from General.yaml, Email.yaml and your search files.";

        // When the saved preference couldn't be used (unreadable, not a preferences file, or a theme this version doesn't
        // know), until the user chooses a theme.
        public string? LoadNotice { get; private set; }
        public bool HasLoadNotice => LoadNotice != null;

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

        private void Choose(bool Checked, ThemeChoice Choice)
        {
            if (Checked)
            {
                Theme = Choice;
            }
        }

        private void SetStatus(string Text, bool IsError)
        {
            StatusIsError = IsError;
            Status = Text;
        }
    }
}
