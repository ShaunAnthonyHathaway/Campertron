using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // D73 (open question #48): the GUI never saves outputTo: Email or Ntfy while the unattended monitoring's start would have
    // no search to check (or would stop on a search file), says so at Output when it opens with such a saved outputTo, and
    // warns, without blocking, when a search change would leave none. In the real window on the headless platform, each test
    // with its own settings folder. The texts are written out here and only controls and view models that existed before
    // D73 are used, so this class also runs against an older GUI: on 365f798 it fails (the negative control of §BS).
    public class MonitoringSearchCheckGuiTests
    {
        private const int SettingsTab = MainWindow.SettingsTabIndex;
        private const int SearchesTab = MainWindow.SearchesTabIndex;
        private const string Needs = "Campertron needs at least one enabled search before it can monitor. Enable or create a search first.";
        private const string NoneEnabled = Needs + " No search is Enabled (autoRun): in the Searches tab, check Enabled (autoRun) for a search with a campground and save it.";
        private const string NoCampground = Needs + " No Enabled (autoRun) search has a campground, so none would be checked: in the Searches tab, choose a campground for an enabled search and save it.";
        private const string NoFile = Needs + " There is no search file: in the Searches tab, create one with New search, choose its campground, check Enabled (autoRun) and save it.";
        private static string Refused(string File) =>
            "The search file " + File + " can't be used, and the monitoring stops at its start with that file's error. Open it in the Searches tab, fix it and save it first.";
        private static string Mode(OutputType Output) => Output == OutputType.Ntfy ? "ntfy mode (outputTo: Ntfy)" : "email mode (outputTo: Email)";
        private static string Notice(OutputType Output, string Message) =>
            "Warning: " + Message + " Until then, " + Mode(Output) + " refuses to start. Searches can still be edited, saved and run in the Results tab.";
        private static string EditorWarning(OutputType Output, string Message) =>
            "Warning: With this change saved, " + Mode(Output) + " would refuse to start. " + Message + " This warning doesn't block saving.";

        private const string Password = "Gui-Monitor-S3cret";
        private const string Topic = "guiMonitorTopic0123456789ab";
        private const string EmailText = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: gui-user\nsmtpPassword: " + Password +
            "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";
        private const string NtfyText = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n";

        private string Folder = string.Empty;
        private string General => Path.Join(Folder, "General.yaml");

        private static string GeneralText(OutputType Output) =>
            "# general\noutputTo: " + Output + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n";

        // a search of the test database's campground: a week from today, every day; its name is a value no message shows
        private static string Search(string Name, bool AutoRun, string? Campground = null) =>
            "displayName: Canary " + Name + "\nautoRun: " + (AutoRun ? "true" : "false") + "\n" +
            (Campground == "" ? "# no campground\n" : "campgroundID: '" + (Campground ?? GuiTestEnvironment.CampgroundAlphaID) + "'\n") +
            "totalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\nshowMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\n" +
            "showFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";

        private static string EqualDates(string Name)
        {
            string Day = DateTime.Today.AddDays(40).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return Search(Name, true).Replace("searchBy: DaysOut\nsearchValue: 7\n", "searchBy: StartEndDate\nsearchValueDates:\n- " + Day + "\n- " + Day + "\n");
        }

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "monitoring-search-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(General, GeneralText(OutputType.Console));
            File.WriteAllText(Path.Join(Folder, "Email.yaml"), EmailText);
        }

        [TearDown]
        public void DeleteFolder()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, true);
            }
        }

        private void Write(string Name, string Text) => File.WriteAllText(Path.Join(Folder, Name), Text);

        // Email: email only; Ntfy: ntfy only; EmailAndNtfy: outputTo: Email with ntfy on
        private OutputType Prepare(string Monitoring)
        {
            if (Monitoring != "Email")
            {
                Write("Ntfy.yaml", NtfyText);
            }
            return Monitoring == "Ntfy" ? OutputType.Ntfy : OutputType.Email;
        }

        // the folder states the monitoring's start refuses
        private void Folder_(string State)
        {
            switch (State)
            {
                case "AllDisabled":
                    Write("Trip.yaml", Search("Trip", false));
                    Write("Weekend.yaml", Search("Weekend", false));
                    break;
                case "NoCampground":
                    Write("Trip.yaml", Search("Trip", true, ""));
                    break;
                case "InvalidSearch":
                    Write("Trip.yaml", EqualDates("Trip"));
                    break;
                case "NoSearchFile":
                    break;
            }
        }

        private static string Expected(string State) => State switch
        {
            "AllDisabled" => NoneEnabled,
            "NoCampground" => NoCampground,
            "InvalidSearch" => Refused("Trip.yaml"),
            _ => NoFile,
        };

        private (MainWindow Window, MainWindowViewModel ViewModel) Show(int Tab)
        {
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = Tab;
            Settle(Window);
            return (Window, ViewModel);
        }

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static void ToTab(MainWindow Window, int Tab)
        {
            Tabs(Window).SelectedIndex = Tab;
            Settle(Window);
        }

        private static Control GeneralEditor(MainWindow Window) => In<Control>(Named<SettingsView>(Window, "SettingsContent"), "GeneralEditor");
        private static Control SearchEditor(MainWindow Window) => In<Control>(Named<SearchesView>(Window, "SearchesContent"), "SearchEditor");

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found in " + Root.Name);

        private static void Click(Button Button)
        {
            Assert.That(Button.IsEffectivelyEnabled, Is.True, Button.Name + " is disabled");
            Button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        private static void Choose(SettingsViewModel Settings, OutputType Output)
        {
            Settings.General.OutputTo.Selected = Settings.General.OutputTo.Choices.Single(c => c.Value.Equals(Output));
            Dispatcher.UIThread.RunJobs();
        }

        // the start's own search check on the saved folder (D64's rule, with the start's reading): null when it would
        // check, else why not (a search file it stops on, or no search to check)
        private string? StartsSearchCheck()
        {
            try
            {
                List<CampertronConfig> Loaded = Yaml.CampertronConfigGetConfigs(Folder, false, false, out int Files);
                return Load.CheckSearchesToCheck(Files, Loaded)?.ToString();
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        }

        private OutputType SavedOutput() => Yaml.LoadSettingsText<GeneralConfig>(File.ReadAllText(General)).OutputTo;

        // ---- 2: Output, a changed outputTo ----

        // with no search to check (no search file, none enabled, none with a campground, or a search file the start stops
        // on), choosing Email, Ntfy or Email with ntfy on is an error at Output and nothing is saved; Console and HtmlFile
        // say nothing. A search enabled (here written by another program, then the tab shown again) clears it and saves
        [AvaloniaTest]
        public void OutputTo_Monitoring_IsNotSaved_WithoutASearchToCheck(
            [Values("Email", "Ntfy", "EmailAndNtfy")] string Monitoring, [Values("NoSearchFile", "AllDisabled", "NoCampground", "InvalidSearch")] string State)
        {
            OutputType Output = Prepare(Monitoring);
            Folder_(State);
            var (Window, ViewModel) = Show(SettingsTab);
            SettingsViewModel Settings = ViewModel.Settings!;
            Control G = GeneralEditor(Window);
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False, "Console: nothing to say");
            Choose(Settings, OutputType.HtmlFile);
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False, "HtmlFile: nothing to say");

            Choose(Settings, Output);
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo("Error: With outputTo: " + Output + ", Campertron would refuse to start: " + Expected(State) + " Then save General.yaml."));
            Assert.That(Settings.General.OutputTo.IsError, Is.True);
            Assert.That(AutomationProperties.GetHelpText(In<ComboBox>(G, "OutputToComboBox")), Is.EqualTo(Settings.General.OutputTo.MessageText), "screen readers get it");
            Assert.That(In<Button>(G, "SaveButton").IsEffectivelyEnabled, Is.False, "Save is blocked");
            Settings.General.Save();
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText(OutputType.Console)), "Save refuses whoever calls it");
            Assert.That(StartsSearchCheck(), Is.Not.Null, "the start's own check refuses the folder");

            // the fix: one enabled search with a campground (the broken one fixed), then the tab shown again
            Write("Trip.yaml", Search("Trip", true));
            ToTab(Window, SearchesTab);
            ToTab(Window, SettingsTab);
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False);
            Click(In<Button>(G, "SaveButton"));
            Assert.That(SavedOutput(), Is.EqualTo(Output));
            Assert.That(StartsSearchCheck(), Is.Null, "what was saved, the start checks");
            Window.Close();
        }

        // ---- 4: Output, a saved outputTo when the GUI opens ----

        // General.yaml already has outputTo: Email or Ntfy and no search is enabled: the same words, as a warning, when the
        // GUI opens, and in the Searches tab; another setting can still be saved (it doesn't make it worse)
        [AvaloniaTest]
        public void ASavedMonitoringOutput_WithoutASearchToCheck_IsAWarningWhenTheGuiOpens([Values("Email", "Ntfy", "EmailAndNtfy")] string Monitoring)
        {
            OutputType Output = Prepare(Monitoring);
            File.WriteAllText(General, GeneralText(Output));
            Folder_("AllDisabled");
            var (Window, ViewModel) = Show(SettingsTab);
            SettingsViewModel Settings = ViewModel.Settings!;
            Control G = GeneralEditor(Window);
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo("Warning: With outputTo: " + Output + ", Campertron refuses to start: " + NoneEnabled));
            Assert.That(Settings.General.OutputTo.IsError, Is.False);
            TextBlock Message = G.GetVisualDescendants().OfType<TextBlock>().Single(T => T.Text == Settings.General.OutputTo.MessageText);
            Assert.That(Message.IsEffectivelyVisible, Is.True);

            Settings.General.AutoRefresh.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Click(In<Button>(G, "SaveButton"));
            Assert.That(File.ReadAllText(General), Does.Contain("autoRefresh: true").And.Contain("outputTo: " + Output));

            ToTab(Window, SearchesTab);
            SearchesViewModel Searches = ViewModel.Searches!;
            Assert.That(Searches.FolderNoticeText, Is.EqualTo(Notice(Output, NoneEnabled)));
            // a change that doesn't cause it: the folder notice says it, the editor doesn't repeat it
            SearchFileViewModel Trip = Searches.Files.Single(F => F.FileName == "Trip.yaml");
            Searches.SelectedFile = Trip;
            Trip.DisplayName.Text = "Renamed";
            Settle(Window);
            Assert.That(In<TextBlock>(SearchEditor(Window), "MonitoringWarningText").IsVisible, Is.False);
            Trip.RevertCommand.Execute(null);

            // the saved outputTo still counts while the form shows Console (the monitoring starts with what is saved)
            ToTab(Window, SettingsTab);
            Choose(Settings, OutputType.Console);
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False, "Console: nothing at Output");
            ToTab(Window, SearchesTab);
            Assert.That(Searches.FolderNoticeText, Is.EqualTo(Notice(Output, NoneEnabled)), "the saved " + Output + " counts until Console is saved");
            Window.Close();
        }

        // ---- 3: search editing ----

        // with outputTo Email or Ntfy saved, disabling the last enabled search warns in the editor before the save, never
        // blocks it, and once saved the Searches tab and Output say it; enabling it again clears every message
        [AvaloniaTest]
        public void DisablingTheLastEnabledSearch_Warns_ButSaves([Values("Email", "Ntfy", "EmailAndNtfy")] string Monitoring)
        {
            OutputType Output = Prepare(Monitoring);
            File.WriteAllText(General, GeneralText(Output));
            Write("Trip.yaml", Search("Trip", true));
            Write("Weekend.yaml", Search("Weekend", false));
            var (Window, ViewModel) = Show(SearchesTab);
            SearchesViewModel Searches = ViewModel.Searches!;
            Assert.That(Searches.HasFolderNotice, Is.False);
            SearchFileViewModel Trip = Searches.Files.Single(F => F.FileName == "Trip.yaml");
            Searches.SelectedFile = Trip;
            Settle(Window);
            TextBlock Warning = In<TextBlock>(SearchEditor(Window), "MonitoringWarningText");
            Assert.That(Warning.IsVisible, Is.False);

            Trip.AutoRun.IsChecked = false;
            Settle(Window);
            Assert.That(Warning.IsEffectivelyVisible, Is.True);
            Assert.That(Warning.Text, Is.EqualTo(EditorWarning(Output, NoneEnabled)));
            Assert.That(AutomationProperties.GetLiveSetting(Warning), Is.EqualTo(AutomationLiveSetting.Polite), "announced when it appears");
            Assert.That(File.ReadAllText(Path.Join(Folder, "Trip.yaml")), Does.Contain("autoRun: true"), "nothing saved yet");
            Click(In<Button>(SearchEditor(Window), "SaveButton"));
            Assert.That(File.ReadAllText(Path.Join(Folder, "Trip.yaml")), Does.Contain("autoRun: false"), "the warning never blocks the save");
            Assert.That(Warning.IsVisible, Is.False, "saved: the folder notice says it now");
            Assert.That(Searches.FolderNoticeText, Is.EqualTo(Notice(Output, NoneEnabled)));
            ToTab(Window, SettingsTab);
            Assert.That(ViewModel.Settings!.General.OutputTo.MessageText, Is.EqualTo("Warning: With outputTo: " + Output + ", Campertron refuses to start: " + NoneEnabled));

            ToTab(Window, SearchesTab);
            Trip.AutoRun.IsChecked = true;
            Settle(Window);
            Assert.That(Warning.IsVisible, Is.False, "enabling one is no problem");
            Click(In<Button>(SearchEditor(Window), "SaveButton"));
            Assert.That(Searches.HasFolderNotice, Is.False);
            ToTab(Window, SettingsTab);
            Assert.That(ViewModel.Settings!.General.OutputTo.HasMessage, Is.False);
            Window.Close();
        }

        // a change the start would refuse in the last enabled search warns too (here a negative number of days, a field
        // error that already blocks the save, as before); with another enabled search, disabling one says nothing; with
        // outputTo Console, nothing is said at all
        [AvaloniaTest]
        public void OnlyAChangeThatLeavesNoSearchToCheck_Warns()
        {
            File.WriteAllText(General, GeneralText(OutputType.Email));
            Write("Trip.yaml", Search("Trip", true));
            var (Window, ViewModel) = Show(SearchesTab);
            SearchesViewModel Searches = ViewModel.Searches!;
            SearchFileViewModel Trip = Searches.Files.Single();
            Searches.SelectedFile = Trip;
            Settle(Window);
            TextBlock Warning = In<TextBlock>(SearchEditor(Window), "MonitoringWarningText");
            Trip.SearchValue.Text = "-3";
            Settle(Window);
            Assert.That(Warning.Text, Is.EqualTo(EditorWarning(OutputType.Email, Refused("Trip.yaml"))));
            Trip.RevertCommand.Execute(null);
            Settle(Window);
            Assert.That(Warning.IsVisible, Is.False);

            Write("Weekend.yaml", Search("Weekend", true));
            Searches.CheckForExternalChanges();
            Settle(Window);
            Searches.SelectedFile = Trip;
            Trip.AutoRun.IsChecked = false;
            Settle(Window);
            Assert.That(Warning.IsVisible, Is.False, "Weekend.yaml is still enabled");
            Trip.RevertCommand.Execute(null);
            Settle(Window);

            ToTab(Window, SettingsTab);
            Choose(ViewModel.Settings!, OutputType.Console);
            Click(In<Button>(GeneralEditor(Window), "SaveButton"));
            File.Delete(Path.Join(Folder, "Weekend.yaml"));
            ToTab(Window, SearchesTab);
            Searches.SelectedFile = Trip;
            Trip.AutoRun.IsChecked = false;
            Settle(Window);
            Assert.That(Warning.IsVisible, Is.False, "Console: no monitoring");
            Click(In<Button>(SearchEditor(Window), "SaveButton"));
            Assert.That(Searches.FolderNoticeText, Does.Not.Contain("refuses to start"), "Console: the notice about the console's Search, as before");
            Window.Close();
        }

        // the last enabled search deleted by another program (the GUI never deletes one): the Searches tab and Output say it
        // when they look at the disk again
        [AvaloniaTest]
        public void TheLastSearchDeletedOnDisk_IsSaid_InTheSearchesTabAndAtOutput([Values("Email", "Ntfy")] string Monitoring)
        {
            OutputType Output = Prepare(Monitoring);
            File.WriteAllText(General, GeneralText(Output));
            Write("Trip.yaml", Search("Trip", true));
            var (Window, ViewModel) = Show(SearchesTab);
            Assert.That(ViewModel.Searches!.HasFolderNotice, Is.False);
            File.Delete(Path.Join(Folder, "Trip.yaml"));
            ViewModel.Searches.CheckForExternalChanges();
            Settle(Window);
            Assert.That(ViewModel.Searches.FolderNoticeText, Is.EqualTo(Notice(Output, NoFile)));
            ToTab(Window, SettingsTab);
            Assert.That(ViewModel.Settings!.General.OutputTo.MessageText, Is.EqualTo("Warning: With outputTo: " + Output + ", Campertron refuses to start: " + NoFile));
            Window.Close();
        }

        // ---- 5: no secret, path or value ----

        [AvaloniaTest]
        public void TheMessages_ShowNoSecretPathOrValue()
        {
            Prepare("EmailAndNtfy");
            Write("Trip.yaml", EqualDates("Trip"));
            var (Window, ViewModel) = Show(SettingsTab);
            Choose(ViewModel.Settings!, OutputType.Email);
            var Texts = new List<string?> { ViewModel.Settings!.General.OutputTo.MessageText };
            ToTab(Window, SearchesTab);
            Texts.Add(ViewModel.Searches!.FolderNoticeText);
            Write("Trip.yaml", Search("Trip", true));
            ViewModel.Searches.CheckForExternalChanges();
            Settle(Window);
            SearchFileViewModel Trip = ViewModel.Searches.Files.Single();
            ViewModel.Searches.SelectedFile = Trip;
            Trip.AutoRun.IsChecked = false;
            Settle(Window);
            Texts.Add(In<TextBlock>(SearchEditor(Window), "MonitoringWarningText").Text);
            Assert.That(Texts, Has.All.StartWith("Error: ").Or.StartWith("Warning: "), "the three messages were all shown");
            foreach (string Never in new[] { Folder, GuiTestEnvironment.Root, "Canary", GuiTestEnvironment.CampgroundAlphaID, Password, Topic, "example.invalid" })
            {
                Assert.That(Texts.Where(T => T != null && T.Contains(Never, StringComparison.Ordinal)), Is.Empty, Never);
            }
            Window.Close();
        }

        // ---- 6: sizes, themes and the keyboard ----

        // the message at Output (an error) and the editor's warning fit every supported width in Light and Dark, the error in
        // the theme's error brush, the warning in the body text's; nothing is logged
        [AvaloniaTest]
        public void TheMessages_FitEverySupportedSize_InLightAndDark([Values(720, 1024, 1600)] int Width, [Values("Light", "Dark")] string Variant)
        {
            using var Log = new AvaloniaLogCapture();
            ThemeVariant Theme = Variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current!.RequestedThemeVariant = Theme;
            File.WriteAllText(General, GeneralText(OutputType.Ntfy));
            Write("Ntfy.yaml", NtfyText);
            Write("Trip.yaml", Search("Trip", true));
            var (Window, ViewModel) = Show(SearchesTab);
            Resize(Window, Width, Width == 720 ? 540 : Width == 1024 ? 768 : 1000);
            SearchFileViewModel Trip = ViewModel.Searches!.Files.Single();
            ViewModel.Searches.SelectedFile = Trip;
            Trip.AutoRun.IsChecked = false;
            Settle(Window);
            TextBlock Warning = In<TextBlock>(SearchEditor(Window), "MonitoringWarningText");
            Assert.That(Warning.IsEffectivelyVisible, Is.True);
            AssertFits(Window, Warning, Named<SearchesView>(Window, "SearchesContent"), "SearchesScrollViewer");
            Assert.That(Warning.Bounds.Height, Is.GreaterThan(Warning.FontSize * 1.5), "it wraps instead of running off");
            Trip.RevertCommand.Execute(null);
            Write("Trip.yaml", Search("Trip", false));
            File.WriteAllText(General, GeneralText(OutputType.Console));

            ToTab(Window, SettingsTab);
            Choose(ViewModel.Settings!, OutputType.Ntfy);
            Settle(Window);
            Control G = GeneralEditor(Window);
            TextBlock Message = G.GetVisualDescendants().OfType<TextBlock>().Single(T => T.Text == ViewModel.Settings!.General.OutputTo.MessageText);
            AssertFits(Window, Message, Named<SettingsView>(Window, "SettingsContent"), "SettingsScrollViewer");
            Application.Current.TryGetResource("SystemControlErrorTextForegroundBrush", Theme, out object? Error);
            Assert.That(((ISolidColorBrush)Message.Foreground!).Color, Is.EqualTo(((ISolidColorBrush)Error!).Color), "an error, in the theme's error brush");
            Assert.That(((ISolidColorBrush)Warning.Foreground!).Color, Is.Not.EqualTo(((ISolidColorBrush)Error!).Color), "a warning, not an error");
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Log.Entries, Is.Empty);
        }

        private static void AssertFits(MainWindow Window, TextBlock Text, Control Content, string ScrollName)
        {
            ScrollViewer Scroll = In<ScrollViewer>(Content, ScrollName);
            Assert.That(Scroll.Extent.Width, Is.LessThanOrEqualTo(Scroll.Viewport.Width + 0.5), "no horizontal overflow");
            Point? Right = Text.TranslatePoint(new Point(Text.Bounds.Width, 0), Window);
            Assert.That(Right!.Value.X, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), Text.Name ?? "the message");
        }

        // the keyboard alone: Space on Enabled (autoRun) shows the warning, and Save, reached and pressed from the keyboard,
        // still saves
        [AvaloniaTest]
        public void Keyboard_DisablesTheLastSearch_AndStillSaves()
        {
            File.WriteAllText(General, GeneralText(OutputType.Email));
            Write("Trip.yaml", Search("Trip", true));
            var (Window, ViewModel) = Show(SearchesTab);
            ViewModel.Searches!.SelectedFile = ViewModel.Searches.Files.Single();
            Settle(Window);
            CheckBox Enabled = In<CheckBox>(SearchEditor(Window), "AutoRunCheckBox");
            Enabled.Focus(NavigationMethod.Tab);
            Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Settle(Window);
            Assert.That(Enabled.IsChecked, Is.False, "Space toggled it");
            Assert.That(In<TextBlock>(SearchEditor(Window), "MonitoringWarningText").IsEffectivelyVisible, Is.True);
            Button Save = In<Button>(SearchEditor(Window), "SaveButton");
            Assert.That(Save.IsTabStop && Save.Focusable && Save.IsEffectivelyEnabled, Is.True, "Save stays reachable");
            Save.Focus(NavigationMethod.Tab);
            Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Settle(Window);
            Assert.That(File.ReadAllText(Path.Join(Folder, "Trip.yaml")), Does.Contain("autoRun: false"));
            Window.Close();
        }
    }
}
