using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // beta.8 GUI polish (decision D28): per-tab state, empty and missing-value presentation, labels and accessibility
    // metadata, keyboard access, Copy ID, layout at the window sizes that matter, theme resources and query counts.
    // Pixel output is not compared (no rendering in this headless setup); see build-and-test.md §P for the manual checks.
    public class MainWindowUsabilityTests
    {
        private static readonly (string Combo, string Label, string Placeholder)[] Selectors =
        {
            ("ParkComboBox", "Park", "Select a park"),
            ("ParkCampgroundComboBox", "Campground", "Select a campground"),
            ("CityStateComboBox", "State", "Select a state"),
            ("CityComboBox", "City", "Select a city"),
            ("CityCampgroundComboBox", "Campground", "Select a campground"),
            ("StateComboBox", "State", "Select a state"),
            ("StateCampgroundComboBox", "Campground", "Select a campground"),
        };

        [AvaloniaTest]
        public void Window_HasMinimumSizeAndStartsCentered()
        {
            var (Window, _) = ShowMainWindow();

            Assert.That(Window.MinWidth, Is.EqualTo(720));
            Assert.That(Window.MinHeight, Is.EqualTo(540));
            Assert.That(Window.WindowStartupLocation, Is.EqualTo(WindowStartupLocation.CenterScreen));
            Assert.That(Window.CanResize, Is.True);
            Window.Close();
        }

        [AvaloniaTest]
        public void EveryTab_ShowsItsEmptyStateUntilACampgroundIsSelected()
        {
            var (Window, _) = ShowMainWindow();

            for (int Index = 0; Index < ExpectedTabs.Length; Index++)
            {
                SelectTab(Window, Index);
                CampgroundDetailsView View = Details(Window, Index);
                Assert.That(ShowsEmptyState(View), Is.True, ExpectedTabs[Index]);
                Assert.That(Named<TextBlock>(View, "EmptyHintText").Text, Does.StartWith("Choose a"), ExpectedTabs[Index]);
            }

            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
            Assert.That(ShowsEmptyState(Details(Window, 0)), Is.True, "a park alone selects no campground");
            Select(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);
            Assert.That(ShowsEmptyState(Details(Window, 0)), Is.False);
            Window.Close();
        }

        [AvaloniaTest]
        public void Selectors_HavePlaceholdersAccessibleNamesAndAssociatedLabels()
        {
            var (Window, _) = ShowMainWindow();

            for (int Index = 0; Index < ExpectedTabs.Length; Index++)
            {
                SelectTab(Window, Index);
                List<Label> Labels = Named<Grid>(Window, ContentNames[Index]).GetVisualDescendants().OfType<Label>().ToList();
                foreach (var Selector in Selectors.Where(s => Named<Grid>(Window, ContentNames[Index]).GetVisualDescendants().OfType<ComboBox>().Any(c => c.Name == s.Combo)))
                {
                    ComboBox Combo = Named<ComboBox>(Window, Selector.Combo);
                    Assert.That(Combo.PlaceholderText, Is.EqualTo(Selector.Placeholder), Selector.Combo);
                    Assert.That(AutomationProperties.GetName(Combo), Is.EqualTo(Selector.Label), Selector.Combo);
                    Assert.That(ControlAutomationPeer.CreatePeerForElement(Combo).GetName(), Is.EqualTo(Selector.Label), Selector.Combo);
                    Label? Label = Labels.SingleOrDefault(l => ReferenceEquals(l.Target, Combo));
                    Assert.That(Label, Is.Not.Null, Selector.Combo + " has no label targeting it");
                    Assert.That(((string)Label!.Content!).Replace("_", ""), Is.EqualTo(Selector.Label), Selector.Combo);
                }
            }
            Window.Close();
        }

        [AvaloniaTest]
        public void DependentSelectors_AreDisabledUntilTheirPrerequisiteIsSelected()
        {
            var (Window, _) = ShowMainWindow();

            SelectTab(Window, 0);
            Assert.That(Named<ComboBox>(Window, "ParkComboBox").IsEnabled, Is.True);
            Assert.That(Named<ComboBox>(Window, "ParkCampgroundComboBox").IsEnabled, Is.False);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
            Assert.That(Named<ComboBox>(Window, "ParkCampgroundComboBox").IsEnabled, Is.True);

            SelectTab(Window, 1);
            Assert.That(Named<ComboBox>(Window, "CityComboBox").IsEnabled, Is.False);
            Assert.That(Named<ComboBox>(Window, "CityCampgroundComboBox").IsEnabled, Is.False);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateUtah);
            Assert.That(Named<ComboBox>(Window, "CityComboBox").IsEnabled, Is.True);
            Assert.That(Named<ComboBox>(Window, "CityCampgroundComboBox").IsEnabled, Is.False);
            // The empty city is a real selection, so it enables the campground selector.
            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.EmptyCity);
            Assert.That(Named<ComboBox>(Window, "CityCampgroundComboBox").IsEnabled, Is.True);

            SelectTab(Window, 2);
            Assert.That(Named<ComboBox>(Window, "StateCampgroundComboBox").IsEnabled, Is.False);
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateColorado);
            Assert.That(Named<ComboBox>(Window, "StateCampgroundComboBox").IsEnabled, Is.True);
            Window.Close();
        }

        // TD-08 and the shared campground: activity on one tab must not preselect filters or show details on another.
        [AvaloniaTest]
        public void Tabs_KeepIndependentSelectionsAndDetails()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel) = ShowMainWindow();

            SelectTab(Window, 1);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateUtah);
            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.CityAlpha);
            Select(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);

            SelectTab(Window, 2);
            Assert.That(Named<ComboBox>(Window, "StateComboBox").SelectedItem, Is.Null, "TD-08: By City state leaked into By State");
            Assert.That(ViewModel.ReactiveViewModel.SelectedStateByState, Is.Null);
            Assert.That(ShowsEmptyState(Details(Window, 2)), Is.True, "By City campground leaked into By State");

            SelectTab(Window, 0);
            Assert.That(ShowsEmptyState(Details(Window, 0)), Is.True, "By City campground leaked into By Park");
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkTwo);
            Select(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundBravo);

            SelectTab(Window, 2);
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateUtah);
            Select(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.CampgroundCharlie);

            // Each tab still shows its own campground and filters.
            SelectTab(Window, 1);
            Assert.That(Named<ComboBox>(Window, "CityStateComboBox").SelectedItem, Is.EqualTo(GuiTestEnvironment.StateUtah));
            Assert.That(DisplayedId(Details(Window, 1)), Is.EqualTo(GuiTestEnvironment.CampgroundAlphaID));
            SelectTab(Window, 0);
            Assert.That(DisplayedId(Details(Window, 0)), Is.EqualTo(GuiTestEnvironment.CampgroundBravoID));
            SelectTab(Window, 2);
            Assert.That(DisplayedId(Details(Window, 2)), Is.EqualTo(GuiTestEnvironment.CampgroundCharlieID));
            Assert.That(ViewModel.ReactiveViewModel.SelectedState, Is.EqualTo(GuiTestEnvironment.StateUtah));
            Assert.That(ViewModel.ReactiveViewModel.SelectedCampground, Is.EqualTo(GuiTestEnvironment.CampgroundAlpha));

            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        // Changing a filter clears the dependent campground, so no stale details remain on screen.
        [AvaloniaTest]
        public void ChangingAFilter_ClearsTheDependentCampgroundAndDetails()
        {
            var (Window, ViewModel) = ShowMainWindow();

            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
            Select(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkTwo);
            Assert.That(ViewModel.ReactiveViewModel.SelectedCampgroundByPark, Is.Null);
            Assert.That(ShowsEmptyState(Details(Window, 0)), Is.True);

            // A new state clears the city (even the empty city) and its campground list.
            SelectTab(Window, 1);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateUtah);
            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.EmptyCity);
            Select(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundCharlie);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateColorado);
            Assert.That(ViewModel.ReactiveViewModel.SelectedCity, Is.Null);
            Assert.That(ViewModel.ReactiveViewModel.SelectedCampground, Is.Null);
            Assert.That(Items(Named<ComboBox>(Window, "CityCampgroundComboBox")), Is.Empty);
            Assert.That(ShowsEmptyState(Details(Window, 1)), Is.True);
            Window.Close();
        }

        [AvaloniaTest]
        public void Details_ShowGuiOwnedLabelsRawValuesAndContext()
        {
            var (Window, _) = ShowMainWindow();
            SelectTab(Window, 1);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateUtah);
            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.CityAlpha);
            Select(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);
            CampgroundDetailsView View = Details(Window, 1);

            Assert.That(Named<TextBlock>(View, "CampgroundNameText").Text, Is.EqualTo(GuiTestEnvironment.CampgroundAlpha));
            Assert.That(Named<TextBlock>(View, "CampgroundContextText").Text, Is.EqualTo(GuiTestEnvironment.CityAlpha + ", " + GuiTestEnvironment.StateUtah));
            Assert.That(Named<TextBlock>(View, "CampgroundIdLabel").Text, Is.EqualTo("Campground ID"));
            Assert.That(DisplayedId(View), Is.EqualTo(GuiTestEnvironment.CampgroundAlphaID));
            Assert.That(Named<SelectableTextBlock>(View, "PhoneText").Text, Is.EqualTo("555-0100"));
            Assert.That(Named<SelectableTextBlock>(View, "EmailText").Text, Is.EqualTo("fixture@example.invalid"));
            Assert.That(Named<SelectableTextBlock>(View, "DescriptionText").Text, Is.EqualTo("Fixture description for " + GuiTestEnvironment.CampgroundAlpha));
            Assert.That(Named<SelectableTextBlock>(View, "DirectionsText").Text, Is.EqualTo("Fixture directions"));
            Assert.That(Named<SelectableTextBlock>(View, "FeesText").Text, Is.EqualTo("Fixture fee"));
            // No data-layer label prefixes ("ID:", "Phone:", "Directions:", "UseFee:", "Description:").
            foreach (SelectableTextBlock Value in View.GetVisualDescendants().OfType<SelectableTextBlock>())
            {
                Assert.That(Value.Text, Does.Not.Match("^(ID|Name|Phone|Email|Directions|UseFee|Description):"), Value.Name);
            }
            Window.Close();
        }

        [AvaloniaTest]
        public void MissingValues_AreShownAsNotListed_AndTheEmptyCityAsNoCityListed()
        {
            var (Window, ViewModel) = ShowMainWindow();
            SelectTab(Window, 1);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateUtah);

            ComboBox City = Named<ComboBox>(Window, "CityComboBox");
            City.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            List<string?> Shown = City.GetRealizedContainers().Select(c => c.GetVisualDescendants().OfType<TextBlock>().First().Text).ToList();
            Assert.That(Shown, Is.EqualTo(new[] { "(No city listed)", GuiTestEnvironment.CityAlpha }));
            City.IsDropDownOpen = false;

            Select(City, GuiTestEnvironment.EmptyCity);
            // The stored value is selected and queried unchanged.
            Assert.That(ViewModel.ReactiveViewModel.SelectedCity, Is.EqualTo(GuiTestEnvironment.EmptyCity));
            Assert.That(Items(Named<ComboBox>(Window, "CityCampgroundComboBox")), Is.EqualTo(new[] { GuiTestEnvironment.CampgroundCharlie }));
            Select(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundCharlie);

            CampgroundDetailsView View = Details(Window, 1);
            Assert.That(DisplayedId(View), Is.EqualTo(GuiTestEnvironment.CampgroundCharlieID));
            Assert.That(Named<TextBlock>(View, "CampgroundContextText").Text, Is.EqualTo(GuiTestEnvironment.StateUtah));
            foreach (string Name in new[] { "PhoneText", "EmailText", "DirectionsText", "FeesText" })
            {
                Assert.That(Named<SelectableTextBlock>(View, Name).Text, Is.EqualTo("Not listed"), Name);
            }
            Window.Close();
        }

        [AvaloniaTest]
        public void Details_HaveHeadingsAndLabelledValues()
        {
            var (Window, _) = ShowMainWindow();
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
            Select(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);
            CampgroundDetailsView View = Details(Window, 0);

            Assert.That(AutomationProperties.GetHeadingLevel(Named<TextBlock>(View, "CampgroundNameText")), Is.EqualTo(1));
            foreach (string Heading in new[] { "DescriptionHeading", "DirectionsHeading", "FeesHeading" })
            {
                Assert.That(AutomationProperties.GetHeadingLevel(Named<TextBlock>(View, Heading)), Is.EqualTo(2), Heading);
            }
            // A text block's accessible name is its text (Avalonia 12 TextBlockAutomationPeer); the focusable ID carries
            // its label as help text, and the other values are read after their visible labels.
            AutomationPeer IdPeer = ControlAutomationPeer.CreatePeerForElement(Named<SelectableTextBlock>(View, "CampgroundIdText"));
            Assert.That(IdPeer.GetName(), Is.EqualTo(GuiTestEnvironment.CampgroundAlphaID));
            Assert.That(IdPeer.GetHelpText(), Is.EqualTo("Campground ID"));
            Assert.That(Named<TextBlock>(View, "PhoneLabel").Text, Is.EqualTo("Phone"));
            Assert.That(Named<TextBlock>(View, "EmailLabel").Text, Is.EqualTo("Email"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(Named<ScrollViewer>(View, "DetailsScrollViewer")).GetName(),
                Is.EqualTo("Campground details"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(Named<Button>(View, "CopyIdButton")).GetName(), Is.EqualTo("Copy ID"));
            Window.Close();
        }

        private static string? FocusName(MainWindow Window)
        {
            IInputElement? Element = Focused(Window);
            return Element is TabItem Tab ? "Tab:" + Tab.Header : (Element as Control)?.Name ?? Element?.GetType().Name;
        }

        private static List<string?> PressTab(MainWindow Window, int Count, RawInputModifiers Modifiers = RawInputModifiers.None)
        {
            var Visited = new List<string?>();
            for (int i = 0; i < Count; i++)
            {
                Window.KeyPress(Key.Tab, Modifiers, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                Visited.Add(FocusName(Window));
            }
            return Visited;
        }

        // Tab moves through the tab header, the selectors, the scrollable details area, the selectable ID and Copy ID,
        // then wraps to the selected tab header (MainWindow's TD-39 workaround for the TabControl keyboard trap reproduced
        // with Avalonia 12.1.6). The long text blocks are selectable with the mouse but are not tab stops. Only plain Tab
        // and Shift+Tab are redirected: at both content edges, other keys and modified Tab combinations are left alone.
        [AvaloniaTest]
        public void Keyboard_TabCyclesThroughHeaderSelectorsDetailsIdAndCopyButton()
        {
            var (Window, _) = ShowMainWindow();
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
            Select(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);
            Named<ComboBox>(Window, "ParkComboBox").Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();

            List<string?> Visited = PressTab(Window, 6);
            Assert.That(Visited, Is.EqualTo(new[]
            {
                "ParkCampgroundComboBox", "DetailsScrollViewer", "CampgroundIdText", "CopyIdButton", "Tab:By Park", "ParkComboBox",
            }), string.Join(", ", Visited));

            // Shift+Tab from the first selector reaches the header too, and Shift+Tab walks back through the details.
            Assert.That(PressTab(Window, 1, RawInputModifiers.Shift), Is.EqualTo(new[] { "Tab:By Park" }));
            Named<Button>(Details(Window, 0), "CopyIdButton").Focus(NavigationMethod.Tab);
            Assert.That(PressTab(Window, 3, RawInputModifiers.Shift), Is.EqualTo(new[] { "CampgroundIdText", "DetailsScrollViewer", "ParkCampgroundComboBox" }));

            // Not intercepted. The workaround's only effect is focusing the selected tab header, and only for plain Tab or
            // Shift+Tab. At the last element (Copy ID), other keys leave focus and the tab unchanged. At both edges,
            // modified Tab combinations are left to Avalonia (which itself treats Ctrl+Shift+Tab as a backward Tab
            // inside the content): focus never jumps to the header and the tab does not change. At the first element (a
            // selector) plain keys are not checked, because Up, Down, Enter and Space legitimately operate the ComboBox.
            var OtherKeys = new[]
            {
                (Key.Enter, RawInputModifiers.None, PhysicalKey.Enter), (Key.Space, RawInputModifiers.None, PhysicalKey.Space),
                (Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp), (Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown),
                (Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft), (Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight),
                (Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp), (Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown),
                (Key.Escape, RawInputModifiers.None, PhysicalKey.Escape),
            };
            var ModifiedTabs = new[]
            {
                RawInputModifiers.Control, RawInputModifiers.Control | RawInputModifiers.Shift,
                RawInputModifiers.Alt, RawInputModifiers.Alt | RawInputModifiers.Shift,
            };
            Control FocusEdge(string Edge)
            {
                Control EdgeControl = Edge == "CopyIdButton" ? Named<Button>(Details(Window, 0), Edge) : Named<ComboBox>(Window, Edge);
                EdgeControl.Focus(NavigationMethod.Tab);
                Dispatcher.UIThread.RunJobs();
                return EdgeControl;
            }
            foreach (var (Pressed, Modifiers, Physical) in OtherKeys)
            {
                FocusEdge("CopyIdButton");
                Window.KeyPress(Pressed, Modifiers, Physical, null);
                Dispatcher.UIThread.RunJobs();
                Assert.That(FocusName(Window), Is.EqualTo("CopyIdButton"), $"{Pressed} at CopyIdButton moved focus");
                Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(0), $"{Pressed} at CopyIdButton changed the tab");
            }
            foreach (string Edge in new[] { "CopyIdButton", "ParkComboBox" })
            {
                foreach (RawInputModifiers Modifiers in ModifiedTabs)
                {
                    FocusEdge(Edge);
                    Window.KeyPress(Key.Tab, Modifiers, PhysicalKey.Tab, null);
                    Dispatcher.UIThread.RunJobs();
                    Assert.That(FocusName(Window), Is.Not.EqualTo("Tab:By Park"), $"{Modifiers}+Tab at {Edge} was redirected to the header");
                    Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(0), $"{Modifiers}+Tab at {Edge} changed the tab");
                }
            }
            Window.Close();
        }

        // From the header, the arrow keys switch tabs and Tab enters the new tab's first selector, so every tab is
        // usable without a mouse.
        [AvaloniaTest]
        public void Keyboard_ArrowKeysSwitchTabsAndTabEntersTheContent()
        {
            var (Window, _) = ShowMainWindow();
            Named<ComboBox>(Window, "ParkComboBox").Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.That(PressTab(Window, 2), Is.EqualTo(new[] { "Tab:By Park", "ParkComboBox" }), "the disabled campground selector is skipped");
            PressTab(Window, 1, RawInputModifiers.Shift);

            Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(1));
            Assert.That(FocusName(Window), Is.EqualTo("Tab:By City"));
            Assert.That(PressTab(Window, 1), Is.EqualTo(new[] { "CityStateComboBox" }));
            Window.Close();
        }

        [AvaloniaTest]
        public void CopyId_CopiesTheRawIdWithTheKeyboard()
        {
            var (Window, _) = ShowMainWindow();
            SelectTab(Window, 2);
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateUtah);
            Select(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);
            CampgroundDetailsView View = Details(Window, 2);
            Button Copy = Named<Button>(View, "CopyIdButton");
            TextBlock Status = Named<TextBlock>(View, "CopyStatusText");
            Assert.That(Status.IsVisible, Is.False);

            Copy.Focus(NavigationMethod.Tab);
            Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            Task<string?> Clip = Window.Clipboard!.TryGetTextAsync();
            WaitFor(Clip);
            Assert.That(Clip.Result, Is.EqualTo(GuiTestEnvironment.CampgroundAlphaID));
            Assert.That(Status.IsVisible, Is.True);
            Assert.That(Status.Text, Is.EqualTo(CampgroundDetailsView.CopiedMessage));
            Assert.That(AutomationProperties.GetLiveSetting(Status), Is.EqualTo(AutomationLiveSetting.Polite));

            // The message belongs to that campground only.
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateColorado);
            Select(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.CampgroundBravo);
            Assert.That(Status.IsVisible, Is.False);
            Window.Close();
        }

        // A missing clipboard is reported, not thrown. A clipboard that throws is caught by the same method, but
        // Avalonia 12's IClipboard cannot be implemented outside Avalonia, so that path is covered by the manual
        // validation (build-and-test.md §P) rather than by a fake.
        [AvaloniaTest]
        public void CopyText_ReportsAMissingClipboardOrEmptyIdWithoutThrowing()
        {
            Task<bool> NoClipboard = CampgroundDetailsView.CopyTextAsync(null, "990001");
            WaitFor(NoClipboard);
            Assert.That(NoClipboard.Result, Is.False);

            var (Window, _) = ShowMainWindow();
            Task<bool> NoText = CampgroundDetailsView.CopyTextAsync(Window.Clipboard, "");
            WaitFor(NoText);
            Assert.That(NoText.Result, Is.False);
            Window.Close();
        }

        // Long names are trimmed inside their selector, long text wraps, and everything stays reachable by scrolling,
        // at the minimum size, the default size and a large window.
        [AvaloniaTest]
        public void LongContent_StaysInsideTheWindowAndReachableAtEverySize([Values(720, 1024, 1600)] int Width)
        {
            using var Log = new AvaloniaLogCapture();
            int Height = Width switch { 720 => 540, 1024 => 768, _ => 1000 };
            var (Window, _) = ShowMainWindow();
            Resize(Window, Width, Height);
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkThree);
            Select(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundDelta);
            Window.UpdateLayout();
            CampgroundDetailsView View = Details(Window, 0);
            ScrollViewer Scroll = Named<ScrollViewer>(View, "DetailsScrollViewer");

            foreach (string Name in new[] { "ParkComboBox", "ParkCampgroundComboBox" })
            {
                ComboBox Combo = Named<ComboBox>(Window, Name);
                Point? Right = Combo.TranslatePoint(new Point(Combo.Bounds.Width, 0), Window);
                Assert.That(Right, Is.Not.Null, Name);
                Assert.That(Right!.Value.X, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), Name);
            }
            // One left-aligned column, capped at a readable width (960 for the cards) on wide windows.
            Grid Content = Named<Grid>(Window, "ByParkContent");
            Border Filters = Content.Children.OfType<Border>().Single();
            Assert.That(Filters.Bounds.X, Is.EqualTo(0).Within(0.5), "filter card is not left-aligned");
            Assert.That(Filters.Bounds.Width, Is.LessThanOrEqualTo(962), "filter card wider than the 960 cap (+1 for layout rounding)");
            Assert.That(View.Bounds.X, Is.EqualTo(0).Within(0.5), "details are not left-aligned");
            Assert.That(Named<TextBlock>(View, "CampgroundNameText").Text, Is.EqualTo(GuiTestEnvironment.CampgroundDeltaTrimmed));
            Assert.That(Named<SelectableTextBlock>(View, "PhoneText").Text, Is.EqualTo(GuiTestEnvironment.DeltaPhone));
            foreach (string Name in new[] { "PhoneText", "DescriptionText" })
            {
                Assert.That(Named<SelectableTextBlock>(View, Name).Bounds.Width, Is.LessThanOrEqualTo(Scroll.Viewport.Width + 0.5), Name);
            }
            Assert.That(Scroll.Bounds.Bottom, Is.LessThanOrEqualTo(Window.ClientSize.Height + 0.5), "details area extends below the window");
            Assert.That(Scroll.Extent.Height, Is.GreaterThan(Scroll.Viewport.Height), "the long description should scroll");
            Assert.That(Scroll.Extent.Width, Is.LessThanOrEqualTo(Scroll.Viewport.Width + 0.5), "no horizontal overflow");

            // The last section can be scrolled into view.
            Scroll.ScrollToEnd();
            Window.UpdateLayout();
            SelectableTextBlock Fees = Named<SelectableTextBlock>(View, "FeesText");
            Point? FeesInScroll = Fees.TranslatePoint(new Point(0, 0), Scroll);
            Assert.That(FeesInScroll, Is.Not.Null);
            Assert.That(FeesInScroll!.Value.Y, Is.InRange(0, Scroll.Viewport.Height));

            Assert.That(Log.Entries, Is.Empty);
            Window.Close();
        }

        // Cards and labels take their brushes from Fluent theme resources, so they change with the theme. There is no
        // hard-coded LightGray and no box shadow (the former halos in Dark).
        [AvaloniaTest]
        public void ThemeResources_ResolveInLightAndDarkWithoutHardCodedHalos()
        {
            string[] Keys =
            {
                "SystemControlBackgroundChromeMediumLowBrush", "SystemControlForegroundBaseLowBrush",
                "SystemControlForegroundBaseMediumHighBrush", "SystemControlForegroundBaseMediumBrush",
            };
            var Backgrounds = new Dictionary<string, Color>();
            foreach (ThemeVariant Variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                foreach (string Key in Keys)
                {
                    Assert.That(Application.Current!.TryGetResource(Key, Variant, out object? Value) && Value is ISolidColorBrush, Is.True, Variant + " " + Key);
                }

                Application.Current!.RequestedThemeVariant = Variant;
                var (Window, _) = ShowMainWindow();
                SelectTab(Window, 0);
                Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
                Select(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);
                Assert.That(Window.ActualThemeVariant, Is.EqualTo(Variant));

                List<Border> Cards = Window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).ToList();
                Assert.That(Cards, Is.Not.Empty);
                Application.Current.TryGetResource("SystemControlBackgroundChromeMediumLowBrush", Variant, out object? Expected);
                foreach (Border Card in Cards)
                {
                    Assert.That(((ISolidColorBrush)Card.Background!).Color, Is.EqualTo(((ISolidColorBrush)Expected!).Color), Variant.ToString());
                }
                Backgrounds[Variant.ToString()] = ((ISolidColorBrush)Cards[0].Background!).Color;

                foreach (Border AnyBorder in Window.GetVisualDescendants().OfType<Border>())
                {
                    Assert.That(AnyBorder.BoxShadow.Count, Is.EqualTo(0), "box shadow on " + AnyBorder.Name);
                    Assert.That((AnyBorder.BorderBrush as ISolidColorBrush)?.Color, Is.Not.EqualTo(Colors.LightGray), "LightGray border on " + AnyBorder.Name);
                }
                Window.Close();
            }
            Assert.That(Backgrounds["Light"], Is.Not.EqualTo(Backgrounds["Dark"]));
        }

        // Every list and details property queries the database when read, so the window must bind each exactly once:
        // one query per selection (the dependent list or the details), none for a cleared campground.
        [AvaloniaTest]
        public void Selections_RunOneQueryEach()
        {
            var (Window, _) = ShowMainWindow();
            SelectTab(Window, 0);
            using var Queries = new QueryCounter();

            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
            Assert.That(Queries.TakeCount(), Is.EqualTo(1), "park -> campground list");
            Select(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);
            Assert.That(Queries.TakeCount(), Is.EqualTo(1), "campground -> details");
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkTwo);
            Assert.That(Queries.TakeCount(), Is.EqualTo(1), "new park -> campground list; the cleared campground runs no query");
            Window.Close();
        }

        // Counts EF Core commands executed on this thread (EF Core's DiagnosticListener events).
        private sealed class QueryCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
        {
            private readonly int _Thread = Environment.CurrentManagedThreadId;
            private readonly List<IDisposable> _Subscriptions = new List<IDisposable>();
            private int _Count;

            public QueryCounter()
            {
                _Subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
            }

            public int TakeCount()
            {
                int Count = _Count;
                _Count = 0;
                return Count;
            }

            public void OnNext(DiagnosticListener Listener)
            {
                if (Listener.Name == "Microsoft.EntityFrameworkCore")
                {
                    _Subscriptions.Add(Listener.Subscribe(this));
                }
            }

            public void OnNext(KeyValuePair<string, object?> Event)
            {
                if (Event.Key == "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted" && Environment.CurrentManagedThreadId == _Thread)
                {
                    _Count++;
                }
            }

            public void OnCompleted() { }
            public void OnError(Exception error) { }

            public void Dispose()
            {
                foreach (IDisposable Subscription in _Subscriptions)
                {
                    Subscription.Dispose();
                }
            }
        }
    }
}
