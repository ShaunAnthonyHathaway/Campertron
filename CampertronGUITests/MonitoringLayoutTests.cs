using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.Views;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The Monitoring tab and the close question in the responsive layout (v0.4.0's phase 4; D106, D107, ER-42): a mixed
    // page (the status and the actions in the side column, the last check in the main column; one centered column below the
    // wide width), the same controls in both layouts, every action reachable and inside the window from the least window up,
    // in Light and Dark with the theme's resources.
    public class MonitoringLayoutTests : DesktopMonitoringTestBase
    {
        [TearDown]
        public void ResetTheme() => Application.Current!.RequestedThemeVariant = ThemeVariant.Default;

        private static Rect In(Control Control, Visual Root) => new Rect(Control.TranslatePoint(new Point(0, 0), Root)!.Value, Control.Bounds.Size);

        private static void Theme(string Name) =>
            Application.Current!.RequestedThemeVariant = Name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        [AvaloniaTest]
        public void EveryAction_IsInsideTheWindow_InBothLayoutsAndThemes([Values(720, 1024, 1191, 1192, 1920, 2560)] int Width, [Values("Light", "Dark")] string ThemeName)
        {
            Theme(ThemeName);
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            Resize(Window, Width, 540);
            ShowMonitoringTab(Window);
            Press(ViewModel.MonitoringTab!.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            ViewModel.MonitoringTab.Update();
            Settle(Window);
            ResponsiveColumns Layout = InTab<ResponsiveColumns>(Window, "MonitoringLayout");
            Assert.That(Layout.IsWide, Is.EqualTo(Width >= 1192), "the boundary every tab shares");
            Assert.That(Layout.Classes, Does.Contain("mixed"), "a mixed page (D107)");
            foreach (string Name in new[] { "StartMonitoringButton", "StopMonitoringButton", "RestartMonitoringButton", "CheckSearchChoice", "CheckOpenBookingButton" })
            {
                Control Part = InTab<Control>(Window, Name);
                Part.BringIntoView();
                Settle(Window);
                Rect At = In(Part, Window);
                Assert.That(Part.IsEffectivelyVisible, Is.True, Name + " at " + Width);
                Assert.That(At.X, Is.GreaterThanOrEqualTo(0), Name + " at " + Width);
                Assert.That(At.Right, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), Name + " is cut off at " + Width);
                Assert.That(At.Y, Is.GreaterThanOrEqualTo(0).And.LessThan(Window.ClientSize.Height), Name + " is scrolled into the window at " + Width);
                Assert.That(At.Width, Is.GreaterThan(20), Name + " is squeezed at " + Width);
            }
            // the cards are the theme's, and every message keeps its severity's resources (ER-35)
            Border Status = InTab<Border>(Window, "MonitoringStatusCard");
            Assert.That(Status.Background, Is.Not.Null, "the card's background is a theme resource");
            foreach (string Key in new[] { "SeverityErrorTextBrush", "SeverityWarningTextBrush", "SeverityInfoTextBrush", "SystemControlBackgroundAltMediumHighBrush" })
            {
                Assert.That(Window.TryFindResource(Key, Window.ActualThemeVariant, out object? Found) && Found is IBrush, Is.True, Key + " in " + ThemeName);
            }
            Window.Close();
        }

        // one instance of each control in both layouts, the side part first in reading and Tab order (ER-42)
        [AvaloniaTest]
        public void Resizing_KeepsTheSameControls_InTheSameOrder_AndTheMonitoringGoesOn()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            Resize(Window, 1024, 768);
            ShowMonitoringTab(Window);
            Press(ViewModel.MonitoringTab!.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            Button Start = InTab<Button>(Window, "StartMonitoringButton");
            ResponsiveColumns Layout = InTab<ResponsiveColumns>(Window, "MonitoringLayout");
            Assert.That(Layout.Children.Select(C => C.Name), Is.EqualTo(new[] { "MonitoringSide", "LastCheckCard" }), "the status and actions first");
            foreach (int Width in new[] { 1920, 720, 1192, 1191 })
            {
                Resize(Window, Width, 768);
                Settle(Window);
                Assert.That(InTab<Button>(Window, "StartMonitoringButton"), Is.SameAs(Start), "one instance at " + Width);
                Assert.That(Layout.Children.Select(C => C.Name), Is.EqualTo(new[] { "MonitoringSide", "LastCheckCard" }));
                Rect Side = In(InTab<Control>(Window, "MonitoringSide"), Window);
                Rect Main = In(InTab<Control>(Window, "LastCheckCard"), Window);
                Assert.That(Layout.IsWide ? Main.X > Side.Right : Main.Y > Side.Bottom - 0.5, Is.True, "side then main at " + Width);
            }
            Assert.That(ViewModel.Monitoring!.Controller.View().Phase, Is.EqualTo(MonitoringControlPhase.Running), "resizing changes nothing of the monitoring");
            Window.Close();
        }

        // the tab's colors come from the theme (ER-07, ER-35): a card and the state's text change with Light and Dark, and the
        // new views name no color of their own
        [AvaloniaTest]
        public void TheTabsColors_ComeFromTheTheme()
        {
            WriteSearch();
            var (Window, _) = ShowWindow();
            ShowMonitoringTab(Window);
            (Color Card, Color Text) Colors()
            {
                Settle(Window);
                return (((ISolidColorBrush)InTab<Border>(Window, "MonitoringStatusCard").Background!).Color,
                    ((ISolidColorBrush)InTab<AnnouncedText>(Window, "MonitoringStateText").Foreground!).Color);
            }
            Theme("Light");
            (Color Card, Color Text) Light = Colors();
            Theme("Dark");
            (Color Card, Color Text) Dark = Colors();
            Assert.That(Dark.Card, Is.Not.EqualTo(Light.Card), "the card's background is the theme's");
            Assert.That(Dark.Text, Is.Not.EqualTo(Light.Text), "the state's text is the theme's");
            string Root = TestContext.CurrentContext.TestDirectory;
            while (!File.Exists(Path.Join(Root, "Campertron.sln")))
            {
                Root = Path.GetDirectoryName(Root)!;
            }
            foreach (string View in new[] { "MonitoringView.axaml", "MainWindow.axaml" })
            {
                string Xaml = File.ReadAllText(Path.Join(Root, "CampertronGUI", "Views", View));
                Assert.That(System.Text.RegularExpressions.Regex.IsMatch(Xaml, "(Background|Foreground|BorderBrush|Fill|Color)=\"(#|[A-Z][a-z]+\")"), Is.False,
                    View + " names a color of its own instead of the theme's resources");
            }
            Window.Close();
        }

        // the close question fits the least window, in both themes
        [AvaloniaTest]
        public void TheCloseQuestion_FitsTheLeastWindow([Values("Light", "Dark")] string ThemeName)
        {
            Theme(ThemeName);
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            Resize(Window, 720, 540);
            Press(ViewModel.MonitoringTab!.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            Assert.That(ViewModel.HoldsClose(true), Is.True);
            Settle(Window);
            foreach (string Name in new[] { "CloseConfirmation", "KeepOpenButton", "StopAndCloseButton", "CloseConfirmationText" })
            {
                Rect At = In(Named<Control>(Window, Name), Window);
                Assert.That(At.X >= 0 && At.Y >= 0 && At.Right <= Window.ClientSize.Width + 0.5 && At.Bottom <= Window.ClientSize.Height + 0.5, Is.True,
                    Name + " outside the least window: " + At);
            }
            Assert.That(Named<Border>(Window, "CloseConfirmationScrim").Background, Is.Not.Null, "the theme's scrim");
            Press(ViewModel.KeepOpenCommand);
            Window.Close();
        }
    }
}
