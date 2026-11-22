using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // D74: the one shared style of a message by its severity (SeverityBlock): Error red, Warning amber, Info blue, each with
    // its tint, its colored left edge and its own icon shape, the text in the severity's color, readable in Light, Dark and
    // System (each text at least 7:1 on its tint, each edge and icon at least 4.5:1) at every supported size; the severity
    // also in words for screen readers. In the real window on the headless platform with the Email.yaml editor, which shows
    // one message of each severity at once.
    public class SeverityStyleTests
    {
        private string Folder = string.Empty;

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "severity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            string Secret = Path.Join(Folder, "smtp_password");
            File.WriteAllText(Secret, "not-a-real-secret\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Secret, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.WriteAllText(Path.Join(Folder, "General.yaml"), "outputTo: Email\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n");
            // an error (the port), a warning (a repeated recipient) and an info (the password file is used)
            File.WriteAllText(Path.Join(Folder, "Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 70000\nsmtpUsername: user\nsmtpPassword: ''\nsmtpPasswordFile: '" + Secret +
                "'\nsendToAddressList:\n- to@example.invalid\n- to@example.invalid\nsendFromAddress: from@example.invalid\n");
            File.WriteAllText(Path.Join(Folder, "Watch.yaml"), "displayName: Watch\nautoRun: true\ncampgroundID: '232445'\n");
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

        private (MainWindow Window, SettingsViewModel Settings, Control Editor) ShowEmail()
        {
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = MainWindow.SettingsTabIndex;
            Settle(Window);
            ViewModel.Settings!.IsEmailSelected = true;
            ViewModel.Settings.IsGeneralSelected = false;
            Settle(Window);
            Control Editor = Named<SettingsView>(Window, "SettingsContent").GetVisualDescendants().OfType<Control>().First(c => c.Name == "EmailEditor");
            return (Window, ViewModel.Settings, Editor);
        }

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static TextBlock Message(Control Root, string Text) =>
            Root.GetVisualDescendants().OfType<TextBlock>().First(T => T.Text == Text && T.Parent is SeverityBlock);

        private static Color ColorOf(IBrush? Brush) => ((ISolidColorBrush)Brush!).Color;

        private static Color Resource(string Key, ThemeVariant Variant)
        {
            Assert.That(Application.Current!.TryGetResource(Key, Variant, out object? Found), Is.True, Key);
            return ((ISolidColorBrush)Found!).Color;
        }

        // WCAG 2 contrast ratio
        private static double Contrast(Color A, Color B)
        {
            static double L(Color C)
            {
                static double F(byte V) { double c = V / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
                return 0.2126 * F(C.R) + 0.7152 * F(C.G) + 0.0722 * F(C.B);
            }
            double La = L(A), Lb = L(B);
            return (Math.Max(La, Lb) + 0.05) / (Math.Min(La, Lb) + 0.05);
        }

        // the hue of a color in degrees (0 to 360); a gray has none (-1), so it is in no severity's family
        private static double Hue(Color C)
        {
            double R = C.R / 255.0, G = C.G / 255.0, B = C.B / 255.0;
            double Max = Math.Max(R, Math.Max(G, B)), Min = Math.Min(R, Math.Min(G, B)), D = Max - Min;
            if (D == 0)
            {
                return -1;
            }
            double H = Max == R ? (G - B) / D % 6 : Max == G ? (B - R) / D + 2 : (R - G) / D + 4;
            return (H * 60 + 360) % 360;
        }

        [AvaloniaTest]
        public void EachSeverity_HasItsStyle_InLightDarkAndSystem_AtEverySize([Values(720, 1024, 1600)] int Width, [Values("Light", "Dark", "System")] string Variant)
        {
            using var Log = new AvaloniaLogCapture();
            Application.Current!.RequestedThemeVariant = Variant switch { "Dark" => ThemeVariant.Dark, "Light" => ThemeVariant.Light, _ => ThemeVariant.Default };
            var (Window, Settings, Editor) = ShowEmail();
            Resize(Window, Width, Width == 720 ? 540 : Width == 1024 ? 768 : 1000);
            Settle(Window);
            ThemeVariant Actual = Window.ActualThemeVariant;
            Assert.That(Actual, Is.EqualTo(Variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light), "System follows the platform's (Light here)");
            EmailSettingsViewModel E = Settings.Email;
            var Shown = new[]
            {
                ("Error", Message(Editor, E.SmtpPort.MessageText)),
                ("Warning", Message(Editor, E.Recipients.Rows[1].MessageText)),
                ("Info", Message(Editor, E.SmtpPasswordFile.MessageText)),
            };
            Assert.That(Shown.Select(S => S.Item2.Text!.Split(':')[0]), Is.EqualTo(new[] { "Error", "Warning", "Info" }), "each message keeps its label");
            var Icons = new List<Geometry?>();
            foreach (var (Severity, Text) in Shown)
            {
                SeverityBlock Block = (SeverityBlock)Text.Parent!;
                Assert.That(Block.EffectiveSeverity, Is.EqualTo(Severity));
                Assert.That(Block.IsEffectivelyVisible, Is.True);
                Color Background = Resource("Severity" + Severity + "BackgroundBrush", Actual);
                Color Accent = Resource("Severity" + Severity + "AccentBrush", Actual);
                Color Foreground = Resource("Severity" + Severity + "TextBrush", Actual);
                Assert.That(ColorOf(Block.Frame.Background), Is.EqualTo(Background), Severity + ": the tint");
                Assert.That(ColorOf(Block.Frame.BorderBrush), Is.EqualTo(Accent), Severity + ": the edge");
                Assert.That(Block.Frame.BorderThickness.Left, Is.EqualTo(4));
                Assert.That(ColorOf(Block.Icon.Foreground), Is.EqualTo(Accent), Severity + ": the icon");
                Assert.That(ColorOf(Text.Foreground), Is.EqualTo(Foreground), Severity + ": the text");
                Assert.That(Contrast(Foreground, Background), Is.GreaterThanOrEqualTo(7.0), Severity + ": the text's contrast");
                Assert.That(Contrast(Accent, Background), Is.GreaterThanOrEqualTo(4.5), Severity + ": the icon's and edge's contrast");
                // each severity in its own color family (Error red, Warning amber, Info blue), so no two look alike
                foreach (var (Part, Color_) in new[] { ("tint", Background), ("edge", Accent), ("text", Foreground) })
                {
                    double H = Hue(Color_);
                    bool InFamily = Severity switch { "Error" => (H >= 0 && H < 20) || H > 340, "Warning" => H >= 30 && H <= 60, _ => H >= 190 && H <= 230 };
                    Assert.That(InFamily, Is.True, Severity + ": the " + Part + " is " + (Severity == "Error" ? "red" : Severity == "Warning" ? "amber" : "blue") + " (hue " + H.ToString("0") + ")");
                }
                Assert.That(Block.Icon.Data, Is.Not.Null);
                Icons.Add(Block.Icon.Data);
                Point? Right = Block.TranslatePoint(new Point(Block.Bounds.Width, 0), Window);
                Assert.That(Right!.Value.X, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), Severity + ": fits the window");
                Assert.That(Text.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
            }
            Assert.That(Icons.Distinct().Count(), Is.EqualTo(3), "a shape for each severity, not color alone");
            ScrollViewer Scroll = Named<SettingsView>(Window, "SettingsContent").GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "SettingsScrollViewer");
            Assert.That(Scroll.Extent.Width, Is.LessThanOrEqualTo(Scroll.Viewport.Width + 0.5), "no horizontal overflow");
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Log.Entries, Is.Empty);
        }

        // screen readers get the severity in words: a message with its label is read as it is; a status without one (the
        // result of Save) gets the severity before it; the icon is not read; a status is still announced when it appears
        [AvaloniaTest]
        public void TheSeverity_IsAnnounced_InWords()
        {
            var (Window, Settings, Editor) = ShowEmail();
            EmailSettingsViewModel E = Settings.Email;
            TextBlock Port = Message(Editor, E.SmtpPort.MessageText);
            AutomationPeer PortPeer = ControlAutomationPeer.CreatePeerForElement((SeverityBlock)Port.Parent!);
            Assert.That(PortPeer.GetName(), Is.EqualTo(Port.Text), "labelled: read as it is");
            Assert.That(PortPeer.GetAutomationControlType(), Is.EqualTo(AutomationControlType.Text));
            Assert.That(PortPeer.GetChildren(), Is.Empty, "the TextBlock and the icon are not read again");
            foreach (SeverityBlock Block in Editor.GetVisualDescendants().OfType<SeverityBlock>().Where(B => B.Message != null))
            {
                Assert.That(AutomationProperties.GetAccessibilityView(Block.Icon), Is.EqualTo(AccessibilityView.Raw), "the icon is not read");
                Assert.That(AutomationProperties.GetAccessibilityView(Block.Message!), Is.EqualTo(AccessibilityView.Raw), "nor the TextBlock");
                Assert.That(AutomationProperties.GetLiveSetting(Block), Is.EqualTo(AutomationProperties.GetLiveSetting(Block.Message!)), "the block takes the live setting");
            }

            E.SmtpServer.Text = "smtp2.example.invalid";
            Settle(Window);
            E.Save();
            Settle(Window);
            TextBlock Status = Editor.GetVisualDescendants().OfType<TextBlock>().First(T => T.Name == "StatusText");
            Assert.That(E.StatusIsError, Is.True);
            Assert.That(Status.Text, Does.Not.StartWith("Error"), "the words are unchanged: no label of their own");
            Assert.That(SeverityOf(Status), Is.EqualTo("Error"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement((SeverityBlock)Status.Parent!).GetName(), Is.EqualTo("Error: " + Status.Text));
            Assert.That(AutomationProperties.GetLiveSetting((SeverityBlock)Status.Parent!), Is.EqualTo(AutomationLiveSetting.Polite), "still announced when it appears");

            E.SmtpPort.Text = "587";
            E.Recipients.Rows[1].RemoveCommand!.Execute(null);
            Settle(Window);
            E.Save();
            Settle(Window);
            Assert.That(SeverityOf(Status), Is.EqualTo("Info"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement((SeverityBlock)Status.Parent!).GetName(), Is.EqualTo("Info: " + Status.Text));
            Window.Close();
        }

        // a block without text or with a hidden message is not shown, and comes back with its message; its TextBlock stays
        // in the visual tree either way
        [AvaloniaTest]
        public void ABlock_FollowsItsMessage()
        {
            var (Window, Settings, Editor) = ShowEmail();
            EmailSettingsViewModel E = Settings.Email;
            TextBlock Port = Message(Editor, E.SmtpPort.MessageText);
            SeverityBlock Block = (SeverityBlock)Port.Parent!;
            E.SmtpPort.Text = "587";
            Settle(Window);
            Assert.That(Block.IsVisible, Is.False);
            Assert.That(Editor.GetVisualDescendants().Contains(Port), Is.True, "still in the tree while hidden");
            E.SmtpPort.Text = "0";
            Settle(Window);
            Assert.That(Block.IsVisible, Is.True);
            Assert.That(Block.EffectiveSeverity, Is.EqualTo(SeverityOf(Port)).And.Not.Empty);
            Window.Close();
        }
    }
}
