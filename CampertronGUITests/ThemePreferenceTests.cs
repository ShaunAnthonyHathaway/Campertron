using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The GUI's theme preference (decision D50): System (the default, following the operating system through Avalonia's
    // Default variant), Light or Dark, applied at once, saved in the GUI's own preferences file
    // (LocalApplicationData/CampertronGUI/preferences.json) apart from every YAML file, read at the start before the window
    // is created, and System whenever the file is missing, unreadable, malformed or names an unknown theme. Each test has
    // its own preferences folder; the shared fixture's per-user folder only ever gets read.
    public class ThemePreferenceTests
    {
        private string Root = string.Empty;
        private string PreferenceFolder => Path.Join(Root, ThemePreferenceStore.FolderName);
        private string PreferenceFile => Path.Join(PreferenceFolder, ThemePreferenceStore.FileName);

        [SetUp]
        public void CreateFolder()
        {
            Root = Path.Join(GuiTestEnvironment.Root, "theme-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        [TearDown]
        public void DeleteFolder()
        {
            if (Directory.Exists(PreferenceFolder))
            {
                foreach (string Entry in Directory.GetFileSystemEntries(PreferenceFolder))
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(Entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                }
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Root, true);
        }

        private ThemePreferenceStore Store() => new ThemePreferenceStore(PreferenceFolder);

        private void WritePreference(string Text)
        {
            Directory.CreateDirectory(PreferenceFolder);
            File.WriteAllText(PreferenceFile, Text);
        }

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        // ---- the store ----

        // Without a file the theme is System, and reading creates nothing.
        [Test]
        public void WithoutAFile_TheThemeIsSystem_AndReadingCreatesNothing()
        {
            Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Missing)));
            Assert.That(Directory.Exists(PreferenceFolder), Is.False, "reading created the folder");
            WritePreference("{}");
            Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Missing)), "an object without a theme");
            WritePreference("{\"later\": 1}");
            Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Missing)), "only another member");
        }

        // Each theme is saved as its name in a JSON object and read back by a new store (a restart).
        [TestCase(ThemeChoice.Light)]
        [TestCase(ThemeChoice.Dark)]
        [TestCase(ThemeChoice.System)]
        public void ASavedTheme_IsReadBackByANewStore(ThemeChoice Choice)
        {
            Assert.That(Store().Save(Choice), Is.Null);
            Assert.That(File.ReadAllText(PreferenceFile), Is.EqualTo("{\n  \"theme\": \"" + Choice + "\"\n}\n"));
            Assert.That(new ThemePreferenceStore(PreferenceFolder).Load(), Is.EqualTo(new ThemeLoad(Choice, ThemePreferenceSource.Saved)));
            Assert.That(Directory.GetFiles(PreferenceFolder), Is.EqualTo(new[] { PreferenceFile }), "no temporary file is left");
        }

        // Names are read ignoring case, and a byte-order mark is not part of the JSON text.
        [TestCase("{\"theme\":\"dark\"}", ThemeChoice.Dark)]
        [TestCase("{\"theme\":\"LIGHT\"}", ThemeChoice.Light)]
        [TestCase("\uFEFF{\"theme\":\"Dark\"}", ThemeChoice.Dark)]
        [TestCase("{ \"later\": [1, 2], \"theme\" : \"Light\" }", ThemeChoice.Light)]
        public void AKnownTheme_IsRead(string Text, ThemeChoice Expected)
        {
            WritePreference(Text);
            Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(Expected, ThemePreferenceSource.Saved)));
        }

        // Anything that is not a JSON object with a known theme falls back to System, and says why.
        [TestCase("", "Malformed")]
        [TestCase("theme: Dark\n", "Malformed")]
        [TestCase("{\"theme\":\"Dark\"", "Malformed")]
        [TestCase("{\"theme\":\"Dark\",}", "Malformed")]
        [TestCase("// Dark\n{\"theme\":\"Dark\"}", "Malformed")]
        [TestCase("[\"Dark\"]", "Malformed")]
        [TestCase("\"Dark\"", "Malformed")]
        [TestCase("{\"theme\":\"Dark\",\"theme\":\"Light\"}", "Malformed")]
        [TestCase("{\"theme\":\"Sepia\"}", "Unknown")]
        [TestCase("{\"theme\":\" Dark\"}", "Unknown")]
        [TestCase("{\"theme\":\"\"}", "Unknown")]
        [TestCase("{\"theme\":2}", "Unknown")]
        [TestCase("{\"theme\":null}", "Unknown")]
        [TestCase("{\"theme\":{\"name\":\"Dark\"}}", "Unknown")]
        public void AnUnusablePreference_FallsBackToSystem(string Text, string Source)
        {
            WritePreference(Text);
            Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.System, Enum.Parse<ThemePreferenceSource>(Source))));
        }

        [Test]
        public void ATooLargeOrUnreadablePreference_FallsBackToSystem()
        {
            WritePreference("{\"theme\":\"Dark\",\"pad\":\"" + new string('x', ThemePreferenceStore.MaxBytes) + "\"}");
            Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Malformed)), "too large");
            File.WriteAllBytes(PreferenceFile, new byte[] { 0x7B, 0xFF, 0xFE, 0x7D });
            Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Malformed)), "not UTF-8");
            File.Delete(PreferenceFile);
            Directory.CreateDirectory(PreferenceFile);
            Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Unreadable)), "a folder in its place");
            Directory.Delete(PreferenceFile);
            if (!OperatingSystem.IsWindows() && Environment.UserName != "root")
            {
                WritePreference("{\"theme\":\"Dark\"}");
                File.SetUnixFileMode(PreferenceFile, UnixFileMode.None);
                Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Unreadable)), "no permission to read it");
            }
            Assert.That(new ThemePreferenceStore(null).Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Unavailable)));
            Assert.That(new ThemePreferenceStore("relative/folder").Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Unavailable)),
                "never the working directory");
            Assert.That(new ThemePreferenceStore("relative/folder").FilePath, Is.Null);
        }

        // A save keeps every other member of the object (a later version's preferences), and replaces a file that is no
        // JSON object.
        [Test]
        public void Saving_KeepsOtherMembers_AndReplacesAMalformedFile()
        {
            WritePreference("{\"later\":{\"kept\":true},\"theme\":\"Light\",\"list\":[1,2]}");
            Assert.That(Store().Save(ThemeChoice.Dark), Is.Null);
            Assert.That(File.ReadAllText(PreferenceFile), Is.EqualTo("{\n  \"later\": {\n    \"kept\": true\n  },\n  \"theme\": \"Dark\",\n  \"list\": [\n    1,\n    2\n  ]\n}\n"));
            WritePreference("not a preferences file");
            Assert.That(Store().Save(ThemeChoice.Light), Is.Null);
            Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.Light, ThemePreferenceSource.Saved)));
            WritePreference("{\"theme\":\"Dark\",\"theme\":\"Light\"}");
            Assert.That(Store().Save(ThemeChoice.Dark), Is.Null, "a duplicate member is replaced, not a failure");
            Assert.That(File.ReadAllText(PreferenceFile), Is.EqualTo("{\n  \"theme\": \"Dark\"\n}\n"));
        }

        // A save that can't be written returns why, leaves no temporary file and throws nothing.
        [Test]
        public void ASaveThatFails_SaysWhy_AndThrowsNothing()
        {
            File.WriteAllText(PreferenceFolder, "a file where the folder should be");
            string? Failure = Store().Save(ThemeChoice.Dark);
            Assert.That(Failure, Is.Not.Null.And.Not.Empty);
            Assert.That(File.ReadAllText(PreferenceFolder), Is.EqualTo("a file where the folder should be"));
            Assert.That(Directory.GetFiles(Root), Is.EqualTo(new[] { PreferenceFolder }));
            Assert.That(new ThemePreferenceStore(null).Save(ThemeChoice.Dark), Is.EqualTo("there is no folder for your user's application data"));
        }

        // The current user's file is in LocalApplicationData (the folder that holds CampertronConfig and CampertronCache),
        // in a folder of the GUI's own: never in the settings folder, never a .yaml file.
        [Test]
        public void TheCurrentUsersPreference_IsInTheGuisOwnFolder_ApartFromTheSettings()
        {
            ThemePreferenceStore User = ThemePreferenceStore.ForCurrentUser();
            string Data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.That(Data, Is.EqualTo(GuiTestEnvironment.Root), "the tests' isolated data folder");
            Assert.That(User.FilePath, Is.EqualTo(Path.Join(Data, "CampertronGUI", "preferences.json")));
            Assert.That(Path.GetDirectoryName(User.FilePath), Is.Not.EqualTo(GuiTestEnvironment.ConfigPath));
            Assert.That(Path.GetExtension(User.FilePath), Is.EqualTo(".json"), "not a .yaml file, so never a search file");
            Assert.That(Directory.Exists(Path.GetDirectoryName(User.FilePath)), Is.False, "looking it up created nothing");
        }

        // ---- the application ----

        // At the start the application read the current user's preference (none in the fixture): System, which is
        // Avalonia's Default variant; the window's System tab shows it, and nothing was written.
        [AvaloniaTest]
        public void TheStart_WithoutAPreference_FollowsTheSystem()
        {
            var App = (Campertron.App)Application.Current!;
            Assert.That(App.SystemSettings, Is.Not.Null, "the start prepared the theme");
            Assert.That(App.SystemSettings!.Theme, Is.EqualTo(ThemeChoice.System));
            Assert.That(App.SystemSettings.StartSource, Is.EqualTo(ThemePreferenceSource.Missing));
            Assert.That(App.SystemSettings.Store.FilePath, Is.EqualTo(ThemePreferenceStore.ForCurrentUser().FilePath));
            Assert.That(App.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Default));
            var (Window, ViewModel) = ShowMainWindow();
            Assert.That(ViewModel.SystemSettings, Is.SameAs(App.SystemSettings), "the window shows the application's theme");
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Assert.That(Radio(Window, "SystemThemeButton").IsChecked, Is.True);
            Assert.That(Radio(Window, "LightThemeButton").IsChecked, Is.False);
            Assert.That(Radio(Window, "DarkThemeButton").IsChecked, Is.False);
            Assert.That(Text(Window, "ThemeLoadNotice").IsVisible, Is.False);
            Window.Close();
            Assert.That(File.Exists(ThemePreferenceStore.ForCurrentUser().FilePath!), Is.False, "showing the theme wrote nothing");
        }

        // The start applies a saved theme before the window exists; a restart (a new store reading the same file) starts
        // with the theme chosen before.
        [AvaloniaTest]
        public void TheStart_AppliesTheSavedTheme_AndAChoiceSurvivesARestart()
        {
            var App = (Campertron.App)Application.Current!;
            WritePreference("{\"theme\":\"Dark\"}");
            SystemSettingsViewModel Started = App.PrepareTheme(Store());
            Assert.That(App.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(App.ActualThemeVariant, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(Started.Theme, Is.EqualTo(ThemeChoice.Dark));
            Assert.That(App.SystemSettings, Is.SameAs(Started));

            var (Window, ViewModel) = ShowMainWindow();
            Assert.That(ViewModel.SystemSettings, Is.SameAs(Started));
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Assert.That(Radio(Window, "DarkThemeButton").IsChecked, Is.True);
            Radio(Window, "LightThemeButton").IsChecked = true;
            Settle(Window);
            Assert.That(App.ActualThemeVariant, Is.EqualTo(ThemeVariant.Light), "applied at once");
            Window.Close();

            // the next start
            SystemSettingsViewModel Restarted = App.PrepareTheme(new ThemePreferenceStore(PreferenceFolder));
            Assert.That(Restarted.Theme, Is.EqualTo(ThemeChoice.Light));
            Assert.That(App.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Light));
        }

        // A malformed, unknown or unreadable preference starts with System, the System tab says why, and choosing a theme
        // replaces the file.
        [AvaloniaTest]
        public void AnUnusablePreference_StartsWithSystem_SaysSo_AndAChoiceReplacesIt()
        {
            var App = (Campertron.App)Application.Current!;
            WritePreference("{\"theme\":\"HighContrast\"}");
            SystemSettingsViewModel Started = App.PrepareTheme(Store());
            Assert.That(Started.Theme, Is.EqualTo(ThemeChoice.System));
            Assert.That(App.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Default));
            var (Window, _) = ShowMainWindow();
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            TextBlock Notice = Text(Window, "ThemeLoadNotice");
            Assert.That(Notice.IsVisible, Is.True);
            Assert.That(Notice.Text, Is.EqualTo("The saved theme preference couldn't be used, so Campertron follows the system theme. Choosing a theme here replaces it."));
            Assert.That(File.ReadAllText(PreferenceFile), Is.EqualTo("{\"theme\":\"HighContrast\"}"), "reading never rewrites the file");

            Radio(Window, "DarkThemeButton").IsChecked = true;
            Settle(Window);
            Assert.That(Notice.IsVisible, Is.False);
            Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(ThemeChoice.Dark, ThemePreferenceSource.Saved)));
            Window.Close();
        }

        // In the window: each choice applies at once (Light and Dark force their theme, System is Avalonia's Default
        // variant) and is saved; the status is a polite live region; nothing in the settings folder changes.
        [AvaloniaTest]
        public void ChoosingATheme_AppliesItAtOnce_SavesIt_AndTouchesNoSettingsFile()
        {
            var App = (Campertron.App)Application.Current!;
            string Folder = Path.Join(Root, "CampertronConfig");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Join(Folder, "General.yaml"), "# mine\noutputTo: Console\nrefreshRidbDataDayInterval: 30\n" +
                "lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n");
            File.WriteAllText(Path.Join(Folder, "Email.yaml"), "smtpServer: ''\nsmtpPort: 0\nsmtpUsername: ''\nsmtpPassword: ''\nsendToAddressList: []\nsendFromAddress: ''\n");
            File.WriteAllText(Path.Join(Folder, "Mine.yaml"), "displayName: Mine # comment\nautoRun: false\n");
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"));
            Dictionary<string, string> Before = Fingerprint(Folder);
            SystemSettingsViewModel System = App.PrepareTheme(Store());
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder, CachePath = Folder }, null, System);
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            TextBlock Status = Text(Window, "ThemeStatusText");
            Assert.That(Status.IsVisible, Is.False);
            Assert.That(AutomationProperties.GetLiveSetting(Status), Is.EqualTo(AutomationLiveSetting.Polite));

            foreach (var (Button, Choice, Shown) in new[]
            {
                ("LightThemeButton", ThemeChoice.Light, ThemeVariant.Light), ("DarkThemeButton", ThemeChoice.Dark, ThemeVariant.Dark),
                ("SystemThemeButton", ThemeChoice.System, ThemeVariant.Default),
            })
            {
                Radio(Window, Button).IsChecked = true;
                Settle(Window);
                Assert.That(System.Theme, Is.EqualTo(Choice), Button);
                Assert.That(App.RequestedThemeVariant, Is.EqualTo(Shown), Button);
                if (Choice != ThemeChoice.System)
                {
                    Assert.That(App.ActualThemeVariant, Is.EqualTo(Shown), Button);
                    Assert.That(Text(Window, "ShownThemeText").Text, Is.EqualTo("Campertron is shown in its " + Choice + " theme."));
                }
                else
                {
                    Assert.That(Text(Window, "ShownThemeText").Text, Does.EndWith("theme, as the system asks."));
                }
                Assert.That(Status.IsVisible, Is.True);
                Assert.That(Status.Text, Is.EqualTo("Saved. Campertron starts with this theme next time."));
                Assert.That(SeverityOf(Status), Is.Not.EqualTo("Error"));
                Assert.That(Store().Load(), Is.EqualTo(new ThemeLoad(Choice, ThemePreferenceSource.Saved)), Button);
                Assert.That(new[] { "SystemThemeButton", "LightThemeButton", "DarkThemeButton" }.Count(b => Radio(Window, b).IsChecked == true), Is.EqualTo(1));
            }
            Window.Close();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.That(Fingerprint(Folder), Is.EqualTo(Before), "no settings or search file changed");
            Assert.That(File.ReadAllText(Path.Join(Folder, "General.yaml")), Does.Not.Contain("theme"));
            Assert.That(File.Exists(ThemePreferenceStore.ForCurrentUser().FilePath!), Is.False, "the user's own preference was not written");
        }

        // A choice that can't be saved still applies until Campertron is closed, and the System tab says so as an error.
        [AvaloniaTest]
        public void AChoiceThatCantBeSaved_StillApplies_AndSaysSo()
        {
            var App = (Campertron.App)Application.Current!;
            File.WriteAllText(PreferenceFolder, "a file where the folder should be");
            SystemSettingsViewModel System = App.PrepareTheme(Store());
            var (Window, _) = ShowMainWindow(new MainWindowViewModel(Load.GetConfig, null, System));
            WaitUntil(() => ((MainWindowViewModel)Window.DataContext!).IsReady, () => "the startup did not finish");
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Radio(Window, "DarkThemeButton").IsChecked = true;
            Settle(Window);
            Assert.That(App.ActualThemeVariant, Is.EqualTo(ThemeVariant.Dark));
            TextBlock Status = Text(Window, "ThemeStatusText");
            Assert.That(Status.Text, Does.StartWith("Campertron couldn't save the theme for the next start ("));
            Assert.That(Status.Text, Does.EndWith("). It applies until Campertron is closed."));
            Assert.That(SeverityOf(Status), Is.EqualTo("Error"));
            Window.Close();
        }

        // Every control of the System tab is named, the heading is a heading, the descriptions are the help texts, and the
        // tab fits the window in Light and Dark without logging a binding error.
        [AvaloniaTest]
        public void TheSystemTab_IsAccessible_AndFitsInLightAndDark([Values("Light", "Dark")] string Variant, [Values(720, 1024)] int Width)
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, _) = ShowMainWindow();
            Application.Current!.RequestedThemeVariant = Variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Resize(Window, Width, 540);
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Settle(Window);
            SystemSettingsView View = Named<SystemSettingsView>(Window, "SystemSettingsContent");
            Assert.That(AutomationProperties.GetHeadingLevel(View.FindControl<TextBlock>("ThemeHeading")!), Is.EqualTo(2));
            foreach (var (Name, Label, Help) in new[]
            {
                ("SystemThemeButton", "System", "Follow the operating system's light or dark setting"),
                ("LightThemeButton", "Light", "Always use the light theme"),
                ("DarkThemeButton", "Dark", "Always use the dark theme"),
            })
            {
                RadioButton Button = Radio(Window, Name);
                AutomationPeer Peer = ControlAutomationPeer.CreatePeerForElement(Button);
                Assert.That(Peer.GetName(), Is.EqualTo(Label));
                Assert.That(Peer.GetHelpText(), Is.EqualTo(Help));
                Assert.That(Button.Content, Is.EqualTo(Label));
                Assert.That(Button.IsTabStop && Button.Focusable, Is.True);
            }
            Border Card = View.FindControl<Border>("ThemeCard")!;
            Assert.That(Card.Bounds.Width, Is.LessThanOrEqualTo(Window.ClientSize.Width), "no horizontal overflow");
            Assert.That(Card.Bounds.Width, Is.GreaterThan(0));
            Assert.That(Log.BindingEntries, Is.Empty);
            Assert.That(Log.Entries, Is.Empty);
            Window.Close();
        }

        private static RadioButton Radio(MainWindow Window, string Name) =>
            Named<SystemSettingsView>(Window, "SystemSettingsContent").FindControl<RadioButton>(Name) ?? throw new AssertionException(Name + " not found");

        private static TextBlock Text(MainWindow Window, string Name) =>
            Named<SystemSettingsView>(Window, "SystemSettingsContent").FindControl<TextBlock>(Name) ?? throw new AssertionException(Name + " not found");
    }
}
