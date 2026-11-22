using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // Settings > System > Current configuration (V011-2, decision D60): the console's eight View Config items, read-only,
    // in a card below Theme, from the library's shared summary. It exists once the startup has succeeded, reads again when
    // Settings > System is shown and when the window is activated while it is shown, writes nothing, never shows a value of
    // Email.yaml, and Copy and Open settings folder act only when pressed, through seams these tests replace (no real
    // clipboard or file manager). Each test has its own settings folder (a copy of the fixture database) and its own
    // System tab view model, so the application's theme preference is never read or written.
    public class CurrentConfigurationTests
    {
        private const string Secret = "CurrentConfig-Gui-Sentinel-5c1e";
        private const string GeneralText = "# Campertron settings " + Secret + "\noutputTo: HtmlFile\nrefreshRidbDataDayInterval: 4321\n" +
            "lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n";
        private const string EmailText = "smtpServer: mail." + Secret + ".example\nsmtpPort: 587\nsmtpUsername: camper\nsmtpPassword: " + Secret + "\n" +
            "sendToAddressList:\n  - first@example.org\nsendFromAddress: camper@example.org\n";
        private const string SearchText = "displayName: Alpha " + Secret + "\nautoRun: false\ncampgroundID: '" + GuiTestEnvironment.CampgroundAlphaID + "'\n" +
            "totalHumans: 2\nsearchBy: DaysOut\nsearchValue: 30\nconsecutiveDays: 1\n";

        private string Folder = string.Empty;
        private string Cache = string.Empty;
        private string Preferences = string.Empty;

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "current-config-" + Guid.NewGuid().ToString("N"));
            Cache = Path.Join(Folder, "cache");
            Preferences = Path.Join(GuiTestEnvironment.Root, "current-config-preferences-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Cache);
            File.WriteAllText(Path.Join(Folder, "General.yaml"), GeneralText);
            File.WriteAllText(Path.Join(Folder, "Email.yaml"), EmailText);
            File.WriteAllText(Path.Join(Folder, "Alpha-" + Secret + ".yaml"), SearchText);
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"));
        }

        [TearDown]
        public void DeleteFolder()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string Path_ in new[] { Folder, Preferences })
            {
                if (Directory.Exists(Path_))
                {
                    Directory.Delete(Path_, true);
                }
            }
        }

        // a System tab view model of the test's own (its theme store in a folder that is only ever read)
        private SystemSettingsViewModel NewSystemSettings()
        {
            var Store = new ThemePreferenceStore(Preferences);
            return new SystemSettingsViewModel(Store, Store.Load(), _ => { }, () => Application.Current?.ActualThemeVariant);
        }

        private (MainWindow Window, MainWindowViewModel ViewModel) Show(Func<CampertronInternalConfig>? Loader = null)
        {
            var ViewModel = new MainWindowViewModel(Loader ?? (() => new CampertronInternalConfig { ConfigPath = Folder, CachePath = Cache }), null, NewSystemSettings());
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Settle(Window);
            return (Window, ViewModel);
        }

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static void ShowSystem(MainWindow Window)
        {
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Settle(Window);
        }

        private static CurrentConfigurationView Card(MainWindow Window) =>
            Named<SystemSettingsView>(Window, "SystemSettingsContent").FindControl<CurrentConfigurationView>("CurrentConfigurationContent")
                ?? throw new AssertionException("CurrentConfigurationContent not found");

        // the settings folder's files except the campground database and its SQLite companions
        private static Dictionary<string, string> Settings(string Folder) =>
            Fingerprint(Folder).Where(e => !e.Key.StartsWith("RecreationDotOrg.db", StringComparison.Ordinal)).ToDictionary(e => e.Key, e => e.Value);

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().SingleOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found in " + Root.Name);

        // the labels and values as drawn, in their visual order
        private static List<string?> Shown(CurrentConfigurationView View, string Name) =>
            View.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == Name && t.IsEffectivelyVisible)
                .OrderBy(t => t.TranslatePoint(new Point(0, 0), View)!.Value.Y).Select(t => t.Text).ToList();

        private static void Click(Button Button)
        {
            Assert.That(Button.IsEffectivelyEnabled, Is.True, Button.Name + " is disabled");
            Button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        // every text the System tab shows or gives to accessibility clients
        private static List<string> Exposed(Control Root)
        {
            var Texts = new List<string>();
            foreach (Control C in Root.GetVisualDescendants().OfType<Control>().Prepend(Root))
            {
                if (C is TextBlock T && T.Text != null)
                {
                    Texts.Add(T.Text);
                }
                if (ToolTip.GetTip(C) is string Tip)
                {
                    Texts.Add(Tip);
                }
                Texts.Add(AutomationProperties.GetName(C) ?? string.Empty);
                Texts.Add(AutomationProperties.GetHelpText(C) ?? string.Empty);
                AutomationPeer Peer = ControlAutomationPeer.CreatePeerForElement(C);
                Texts.Add(Peer.GetName() ?? string.Empty);
                Texts.Add(Peer.GetHelpText() ?? string.Empty);
            }
            return Texts;
        }

        private static string Expected(string FolderPath, string CachePath, string Output, string Searches, string Refresh) => string.Join(Environment.NewLine, new[]
        {
            "Version => " + RuntimeInfo.Describe(typeof(App).Assembly).Split(Environment.NewLine)[0],
            "Config => " + FolderPath,
            "Cache => " + CachePath,
            "Output => " + Output,
            "Searches => " + Searches,
            "RIDB refresh => " + Refresh,
            "OS => " + System.Runtime.InteropServices.RuntimeInformation.OSDescription + " (" + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture + ")",
            "Framework => " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " (" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture + ")",
        });

        private static string RefreshOf(string Iso) =>
            CurrentConfiguration.RefreshText(Yaml.LoadSettingsText<GeneralConfig>("lastRidbDataRefresh: '" + Iso + "'\n").LastRidbDataRefresh, DateTime.UtcNow, System.Globalization.CultureInfo.CurrentCulture);

        // ---- the card ----

        // Under Settings > System, below Theme: the heading and the eight labels and values in order, the GUI's own version
        // ("CampertronGUI <version>"), the startup's folders unredacted, the output and refresh of General.yaml, the search
        // files counted (never named), and the System tab's DataContext is still the System view model.
        [AvaloniaTest]
        public void TheCard_IsUnderSystemBelowTheTheme_WithTheEightItems()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel) = Show();
            ShowSystem(Window);

            SystemSettingsView System = Named<SystemSettingsView>(Window, "SystemSettingsContent");
            Assert.That(System.DataContext, Is.SameAs(ViewModel.SystemSettings), "the System tab keeps its view model");
            CurrentConfigurationView View = Card(Window);
            Assert.That(View.IsEffectivelyVisible, Is.True);
            Assert.That(View.DataContext, Is.SameAs(ViewModel.SystemSettings.Configuration));
            Border Theme = System.FindControl<Border>("ThemeCard")!;
            Assert.That(View.TranslatePoint(new Point(0, 0), System)!.Value.Y, Is.GreaterThan(Theme.TranslatePoint(new Point(0, Theme.Bounds.Height), System)!.Value.Y), "below Theme");
            Assert.That(In<TextBlock>(View, "ConfigurationHeading").Text, Is.EqualTo("Current configuration"));

            Assert.That(Shown(View, "ConfigurationLabel"), Is.EqualTo(CurrentConfiguration.Labels));
            string Text = Expected(Folder, Cache, "HtmlFile", "1 search file", RefreshOf("2020-01-02T03:04:05.0000000Z"));
            Assert.That(Shown(View, "ConfigurationValue"), Is.EqualTo(Text.Split(Environment.NewLine).Select(l => l.Substring(l.IndexOf(" => ", StringComparison.Ordinal) + 4))));
            Assert.That(ViewModel.SystemSettings.Configuration!.Text, Is.EqualTo(Text));
            Assert.That(Shown(View, "ConfigurationValue")[0], Does.StartWith("CampertronGUI "), "the GUI's own program and version");
            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        // The card exists only once the startup has succeeded: not while it prepares, not after a failure, and then with the
        // folders of the attempt that succeeded.
        [AvaloniaTest]
        public void TheCard_ExistsOnlyAfterTheStartupSucceeded()
        {
            using var Gate = new ManualResetEventSlim(false);
            int Attempt = 0;
            var ViewModel = new MainWindowViewModel(() =>
            {
                if (Interlocked.Increment(ref Attempt) == 1)
                {
                    throw new InvalidOperationException("first attempt fails");
                }
                Gate.Wait(TimeSpan.FromSeconds(10));
                return new CampertronInternalConfig { ConfigPath = Folder, CachePath = Cache };
            }, null, NewSystemSettings());
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Failed);
            Assert.That(ViewModel.SystemSettings.Configuration, Is.Null, "not after a failure");
            Assert.That(ViewModel.SystemSettings.HasConfiguration, Is.False);

            Assert.That(ViewModel.Retry(), Is.True);
            Dispatcher.UIThread.RunJobs();
            Assert.That(ViewModel.IsPreparing, Is.True);
            Assert.That(ViewModel.SystemSettings.Configuration, Is.Null, "not while the startup prepares");
            Gate.Set();
            WaitForState(ViewModel, StartupState.Ready);
            Assert.That(ViewModel.SystemSettings.HasConfiguration, Is.True);
            Assert.That(ViewModel.SystemSettings.Configuration!.ConfigPath, Is.EqualTo(Folder));
            Assert.That(ViewModel.SystemSettings.Configuration.Items[2].Value, Is.EqualTo(Cache));
            Window.Close();
        }

        // ---- refreshing ----

        // Shown again, the card reads General.yaml and the search files again, once each time (one handler per event,
        // never registered twice), and once when the window is activated while it is shown; never while another tab is
        // shown. A file another program changed or removed shows at the next read; nothing is written or created.
        [AvaloniaTest]
        public void TheCard_ReadsWhenShownAndOnActivation_OnceEach_AndWritesNothing()
        {
            var (Window, ViewModel) = Show();
            CurrentConfigurationViewModel Card_ = ViewModel.SystemSettings.Configuration!;
            int Created = Card_.Reads;
            Assert.That(Created, Is.EqualTo(1), "read once when the startup succeeded");

            for (int Time = 1; Time <= 5; Time++)
            {
                ShowSystem(Window);
                Assert.That(Card_.Reads, Is.EqualTo(Created + Time), "once per showing");
                ShowSettingsTab(Window, MainWindow.CampertronSettingsTabIndex);
                Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
                Settle(Window);
                Tabs(Window).SelectedIndex = MainWindow.SettingsTabIndex;
                Settle(Window);
                Assert.That(Card_.Reads, Is.EqualTo(Created + Time), "Settings > Campertron does not read it");
            }
            int Shown_ = Card_.Reads;
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            Window.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Card_.Reads, Is.EqualTo(Shown_), "not on activation while another tab is shown");

            ShowSystem(Window);
            Dictionary<string, string> Before = Settings(Folder);
            int Reads = Card_.Reads;
            // another program changes General.yaml and adds a search file while System is shown; activation shows it
            File.WriteAllText(Path.Join(Folder, "General.yaml"), GeneralText.Replace("outputTo: HtmlFile", "outputTo: Email"));
            File.WriteAllText(Path.Join(Folder, "Second.yaml"), SearchText);
            Window.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Card_.Reads, Is.EqualTo(Reads + 1), "once on activation");
            Assert.That(Card_.Items[3].Value, Is.EqualTo("Email"));
            Assert.That(Card_.Items[4].Value, Is.EqualTo("2 search files"));
            Assert.That(Shown(Card(Window), "ConfigurationValue")[3], Is.EqualTo("Email"), "the card shows it");

            // General.yaml removed: unknown, and it is not created again
            File.Delete(Path.Join(Folder, "General.yaml"));
            ShowSettingsTab(Window, MainWindow.CampertronSettingsTabIndex);
            ShowSystem(Window);
            Assert.That(Card_.Items[3].Value, Is.EqualTo("unknown"));
            Assert.That(Card_.Items[5].Value, Is.EqualTo("unknown"));
            Assert.That(File.Exists(Path.Join(Folder, "General.yaml")), Is.False, "showing the card created no General.yaml");
            Window.Close();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Dictionary<string, string> After = Settings(Folder);
            Assert.That(After.Keys, Is.EquivalentTo(Before.Keys.Except(new[] { "General.yaml" }).Append("Second.yaml")));
            foreach (string Name in Before.Keys.Where(N => N != "General.yaml"))
            {
                Assert.That(After[Name], Is.EqualTo(Before[Name]), Name);
            }
        }

        // Walking through every tab and showing the card writes nothing in the settings folder, the cache folder or the
        // GUI's preferences, and neither action runs by itself (the fakes are never called).
        [AvaloniaTest]
        public void Navigation_WritesNothing_AndRunsNeitherAction()
        {
            Dictionary<string, string> Before = Fingerprint(Folder);
            var (Window, ViewModel) = Show();
            int Copies = 0, Launches = 0;
            CurrentConfigurationViewModel Card_ = ViewModel.SystemSettings.Configuration!;
            Func<string, Task<bool>> FakeCopy = _ => { Copies++; return Task.FromResult(true); };
            Func<DirectoryInfo, Task<bool>> FakeLaunch = _ => { Launches++; return Task.FromResult(true); };
            Card_.CopyText = FakeCopy;
            Card_.FolderLauncher = FakeLaunch;
            foreach (int Top in new[] { MainWindow.SearchesTabIndex, MainWindow.ResultsTabIndex, MainWindow.SettingsTabIndex, MainWindow.CampsitesTabIndex })
            {
                Tabs(Window).SelectedIndex = Top;
                Settle(Window);
            }
            ShowSystem(Window);
            Window.Activate();
            ShowSettingsTab(Window, MainWindow.CampertronSettingsTabIndex);
            ShowSystem(Window);
            Assert.That(Card_.CopyText, Is.SameAs(FakeCopy), "the view kept the test's clipboard");
            Assert.That(Card_.FolderLauncher, Is.SameAs(FakeLaunch), "and the test's launcher");
            Assert.That((Copies, Launches), Is.EqualTo((0, 0)), "nothing copied or opened without a press");
            Assert.That(Card_.HasStatus, Is.False);
            Window.Close();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.That(Fingerprint(Folder), Is.EqualTo(Before));
            Assert.That(Directory.Exists(Preferences), Is.False, "no theme preference was written");
        }

        // ---- the actions ----

        // Copy puts exactly the text shown, "Label => value" per line in the card's order, on the clipboard, and says so in
        // the polite status; a clipboard that refuses, throws or is missing is reported in words, never thrown, and writes
        // nothing. The copied text holds no value of Email.yaml.
        [AvaloniaTest]
        public void Copy_CopiesExactlyWhatIsShown_AndReportsAFailure()
        {
            var (Window, ViewModel) = Show();
            ShowSystem(Window);
            CurrentConfigurationViewModel Card_ = ViewModel.SystemSettings.Configuration!;
            CurrentConfigurationView View = Card(Window);
            var Copied = new List<string>();
            Card_.CopyText = Text => { Copied.Add(Text); return Task.FromResult(true); };
            TextBlock Status = In<TextBlock>(View, "ConfigurationStatusText");
            Assert.That(Status.IsVisible, Is.False);
            Assert.That(AutomationProperties.GetLiveSetting(Status), Is.EqualTo(AutomationLiveSetting.Polite));

            Click(In<Button>(View, "CopyConfigurationButton"));

            List<string?> Labels = Shown(View, "ConfigurationLabel");
            List<string?> Values = Shown(View, "ConfigurationValue");
            Assert.That(Copied, Is.EqualTo(new[] { string.Join(Environment.NewLine, Labels.Zip(Values, (L, V) => L + " => " + V)) }), "exactly the shown text");
            Assert.That(Copied[0], Does.Not.Contain(Secret).And.Not.Contain("smtp").And.Not.Contain("4321"));
            Assert.That(Status.IsVisible, Is.True);
            Assert.That(Status.Text, Is.EqualTo("Copied the current configuration to the clipboard."));
            Assert.That(SeverityOf(Status), Is.Not.EqualTo("Error"));

            foreach (Func<string, Task<bool>>? Failing in new Func<string, Task<bool>>?[]
            {
                _ => Task.FromResult(false), _ => throw new InvalidOperationException("no clipboard"), _ => Task.FromException<bool>(new IOException("busy")), null,
            })
            {
                Card_.CopyText = Failing;
                Click(In<Button>(View, "CopyConfigurationButton"));
                Assert.That(Status.Text, Is.EqualTo("Campertron couldn't copy the current configuration. Select the values and copy them yourself."));
                Assert.That(SeverityOf(Status), Is.EqualTo("Error"), "and in words, not by color alone");
            }
            Window.Close();
        }

        // Open settings folder passes only the startup's settings folder, as a directory, to the launcher; success and every
        // failure (refused, thrown, no launcher, the folder gone) are said in the polite status, never thrown.
        [AvaloniaTest]
        public void OpenSettingsFolder_PassesOnlyTheSettingsFolder_AndReportsAFailure()
        {
            var (Window, ViewModel) = Show();
            ShowSystem(Window);
            CurrentConfigurationViewModel Card_ = ViewModel.SystemSettings.Configuration!;
            CurrentConfigurationView View = Card(Window);
            var Opened = new List<string>();
            Card_.FolderLauncher = Dir => { Opened.Add(Dir.FullName); return Task.FromResult(true); };
            TextBlock Status = In<TextBlock>(View, "ConfigurationStatusText");

            Click(In<Button>(View, "OpenConfigFolderButton"));
            Assert.That(Opened, Is.EqualTo(new[] { Path.GetFullPath(Folder) }));
            Assert.That(Status.Text, Is.EqualTo("Opened the settings folder in your file manager."));
            Assert.That(SeverityOf(Status), Is.Not.EqualTo("Error"));

            foreach (Func<DirectoryInfo, Task<bool>>? Failing in new Func<DirectoryInfo, Task<bool>>?[]
            {
                _ => Task.FromResult(false), _ => throw new InvalidOperationException("no file manager"), null,
            })
            {
                Card_.FolderLauncher = Failing;
                Click(In<Button>(View, "OpenConfigFolderButton"));
                Assert.That(Status.Text, Is.EqualTo("Campertron couldn't open the settings folder. It is " + Folder));
                Assert.That(SeverityOf(Status), Is.EqualTo("Error"));
            }
            Window.Close();

            // a settings folder that no longer exists is never passed on
            var Gone = new CurrentConfigurationViewModel(Path.Join(Folder, "gone"), () => new ConfigurationSummary(Array.Empty<ConfigurationItem>()));
            int Launches = 0;
            Gone.FolderLauncher = _ => { Launches++; return Task.FromResult(true); };
            WaitFor(Gone.OpenFolder());
            Assert.That(Launches, Is.Zero);
            Assert.That(Gone.StatusIsError, Is.True);
        }

        // ---- accessibility and layout ----

        // A heading, each value with its label as help text after its label in reading order, named buttons with help
        // texts, a polite status; Tab reaches Copy then Open settings folder after the themes (NavigationTests checks the
        // whole order). Nothing the System tab shows or exposes holds a value of Email.yaml, another General.yaml value or a
        // search file's name or content.
        [AvaloniaTest]
        public void TheCard_IsAccessible_AndExposesNoSecret()
        {
            var (Window, _) = Show();
            ShowSystem(Window);
            CurrentConfigurationView View = Card(Window);
            Assert.That(AutomationProperties.GetHeadingLevel(In<TextBlock>(View, "ConfigurationHeading")), Is.EqualTo(2));
            List<SelectableTextBlock> Values = View.GetVisualDescendants().OfType<SelectableTextBlock>().Where(t => t.Name == "ConfigurationValue").ToList();
            Assert.That(Values.Select(v => AutomationProperties.GetHelpText(v)), Is.EqualTo(CurrentConfiguration.Labels), "each value says its label");
            Assert.That(Values.All(v => !v.IsTabStop), Is.True, "values are selectable text, not tab stops");
            foreach (var (Name, Label, Help) in new[]
            {
                ("CopyConfigurationButton", "Copy current configuration", "Copy the values above to the clipboard as text, one line each"),
                ("OpenConfigFolderButton", "Open settings folder", "Show the settings folder in your file manager"),
            })
            {
                Button Button = In<Button>(View, Name);
                AutomationPeer Peer = ControlAutomationPeer.CreatePeerForElement(Button);
                Assert.That(Peer.GetName(), Is.EqualTo(Label), Name);
                Assert.That(Peer.GetHelpText(), Is.EqualTo(Help), Name);
                Assert.That(Button.IsTabStop && Button.Focusable, Is.True, Name);
            }
            List<string> Texts = Exposed(Named<SystemSettingsView>(Window, "SystemSettingsContent"));
            Assert.That(Texts.Where(t => t.Contains(Secret, StringComparison.Ordinal) || t.Contains("4321", StringComparison.Ordinal)
                || t.Contains("camper@example.org", StringComparison.Ordinal) || t.Contains("Alpha-", StringComparison.Ordinal)), Is.Empty);
            Window.Close();
        }

        // The card fits the window in Light and Dark at the smallest, the default and a wide size: no horizontal overflow,
        // every text and button inside the window, a long folder path wrapped; colors only from the theme (no local
        // foreground or background on any of its controls); no binding error or other log entry.
        [AvaloniaTest]
        public void TheCard_FitsInLightAndDark([Values("Light", "Dark")] string Variant, [Values(720, 1024, 2400)] int Width)
        {
            using var Log = new AvaloniaLogCapture();
            string Long = Path.Join(Folder, string.Concat(Enumerable.Repeat("a-very-long-folder-name-without-spaces-", 6)));
            Directory.CreateDirectory(Long);
            File.WriteAllText(Path.Join(Long, "General.yaml"), GeneralText);
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Long, "RecreationDotOrg.db"));
            var (Window, _) = Show(() => new CampertronInternalConfig { ConfigPath = Long, CachePath = Cache });
            Application.Current!.RequestedThemeVariant = Variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Resize(Window, Width, Width == 720 ? 540 : Width == 1024 ? 768 : 1350);
            ShowSystem(Window);
            CurrentConfigurationView View = Card(Window);

            ScrollViewer Scroll = Named<SystemSettingsView>(Window, "SystemSettingsContent").FindControl<ScrollViewer>("SystemScrollViewer")!;
            Assert.That(Scroll.Extent.Width, Is.LessThanOrEqualTo(Scroll.Viewport.Width + 0.5), "no horizontal overflow");
            foreach (Control C in View.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && (c is TextBlock || c is Button)))
            {
                Point? Right = C.TranslatePoint(new Point(C.Bounds.Width, 0), Window);
                Assert.That(Right!.Value.X, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), C.Name ?? C.GetType().Name);
                foreach (AvaloniaProperty Color in new AvaloniaProperty[] { TextBlock.ForegroundProperty, TemplatedControl.ForegroundProperty, TemplatedControl.BackgroundProperty, Border.BackgroundProperty })
                {
                    Assert.That(Avalonia.Diagnostics.AvaloniaObjectExtensions.GetDiagnostic(C, Color).Priority, Is.Not.EqualTo(Avalonia.Data.BindingPriority.LocalValue),
                        (C.Name ?? C.GetType().Name) + " has a color of its own: " + Color.Name);
                }
            }
            SelectableTextBlock Config = View.GetVisualDescendants().OfType<SelectableTextBlock>().First(t => t.Name == "ConfigurationValue" && t.Text == Long);
            Assert.That(Config.Bounds.Height, Is.GreaterThan(Config.FontSize * 1.9), "the long path wraps");
            Assert.That(Log.BindingEntries, Is.Empty);
            Assert.That(Log.Entries, Is.Empty);
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            Window.Close();
        }

        // ---- what the card's code refers to ----

        // From the IL: the card reads through the library's shared summary only; it never loads Email.yaml, writes a
        // settings file, starts a process, writes to the console, searches, refreshes the database or makes a web request.
        [Test]
        public void TheCardsCode_ReadsOnlyTheSharedSummary()
        {
            List<string> Called = ReferencedMembers(typeof(CurrentConfigurationViewModel), typeof(CurrentConfigurationView)).Select(c => c.Member).Distinct().ToList();
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.CurrentConfiguration::Read"));
            string[] Forbidden =
            {
                "EmailConfig", "CampertronLibrary.function.Base.Yaml::", "CampertronLibrary.function.Base.ConfigFileReplace::", "ConfigEditSession",
                "System.IO.File::", "System.IO.Directory::Create", "System.IO.Directory::Delete", "System.Diagnostics.Process", "System.Console",
                "CampertronLibrary.function.RecDotOrg.", "CampsiteSearch", "System.Net.", "System.Environment::GetEnvironmentVariable",
            };
            Assert.That(Called.Where(m => Forbidden.Any(f => m.Contains(f, StringComparison.Ordinal))), Is.Empty);
        }
    }
}
