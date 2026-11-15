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
            ("ParkComboBox", "Park", "Select or type a park"),
            ("ParkCampgroundComboBox", "Campground", "Select or type a campground"),
            ("CityStateComboBox", "State", "Select or type a state"),
            ("CityComboBox", "City", "Select or type a city"),
            ("CityCampgroundComboBox", "Campground", "Select or type a campground"),
            ("StateComboBox", "State", "Select or type a state"),
            ("StateCampgroundComboBox", "Campground", "Select or type a campground"),
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
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);
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
            SelectCampground(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);

            SelectTab(Window, 2);
            Assert.That(Named<ComboBox>(Window, "StateComboBox").SelectedItem, Is.Null, "TD-08: By City state leaked into By State");
            Assert.That(ViewModel.ReactiveViewModel!.SelectedStateByState, Is.Null);
            Assert.That(ShowsEmptyState(Details(Window, 2)), Is.True, "By City campground leaked into By State");

            SelectTab(Window, 0);
            Assert.That(ShowsEmptyState(Details(Window, 0)), Is.True, "By City campground leaked into By Park");
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkTwo);
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundBravoID);

            SelectTab(Window, 2);
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateUtah);
            SelectCampground(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.CampgroundCharlieID);

            // Each tab still shows its own campground and filters.
            SelectTab(Window, 1);
            Assert.That(Named<ComboBox>(Window, "CityStateComboBox").SelectedItem, Is.EqualTo(GuiTestEnvironment.StateUtah));
            Assert.That(DisplayedId(Details(Window, 1)), Is.EqualTo(GuiTestEnvironment.CampgroundAlphaID));
            SelectTab(Window, 0);
            Assert.That(DisplayedId(Details(Window, 0)), Is.EqualTo(GuiTestEnvironment.CampgroundBravoID));
            SelectTab(Window, 2);
            Assert.That(DisplayedId(Details(Window, 2)), Is.EqualTo(GuiTestEnvironment.CampgroundCharlieID));
            Assert.That(ViewModel.ReactiveViewModel!.SelectedState, Is.EqualTo(GuiTestEnvironment.StateUtah));
            Assert.That(ViewModel.ReactiveViewModel!.SelectedCampground?.FacilityID, Is.EqualTo(GuiTestEnvironment.CampgroundAlphaID));

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
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkTwo);
            Assert.That(ViewModel.ReactiveViewModel!.SelectedCampgroundByPark, Is.Null);
            Assert.That(ShowsEmptyState(Details(Window, 0)), Is.True);

            // A new state clears the city (even the empty city) and its campground list.
            SelectTab(Window, 1);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateUtah);
            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.EmptyCity);
            SelectCampground(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundCharlieID);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateColorado);
            Assert.That(ViewModel.ReactiveViewModel!.SelectedCity, Is.Null);
            Assert.That(ViewModel.ReactiveViewModel!.SelectedCampground, Is.Null);
            Assert.That(Ids(Named<ComboBox>(Window, "CityCampgroundComboBox")), Is.Empty);
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
            SelectCampground(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);
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
            Assert.That(Shown, Is.EqualTo(new[] { "(No city listed)", GuiTestEnvironment.CityAlpha, GuiTestEnvironment.CityIdentityUtah }));
            City.IsDropDownOpen = false;

            Select(City, GuiTestEnvironment.EmptyCity);
            // The stored value is selected and queried unchanged.
            Assert.That(ViewModel.ReactiveViewModel!.SelectedCity, Is.EqualTo(GuiTestEnvironment.EmptyCity));
            Assert.That(Ids(Named<ComboBox>(Window, "CityCampgroundComboBox")), Is.EqualTo(new[] { GuiTestEnvironment.CampgroundCharlieID }));
            SelectCampground(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundCharlieID);

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
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);
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
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);
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
            SelectCampground(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);
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
            SelectCampground(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.CampgroundBravoID);
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
        // at the minimum size, the default size and large windows. Since R8 the details scroll area spans the whole tab:
        // the scroll bar is at the window edge and the mouse wheel scrolls anywhere below the filters, also right of the
        // capped column, which stays left-aligned with the filter card.
        [AvaloniaTest]
        public void LongContent_StaysInsideTheWindowAndReachableAtEverySize([Values(720, 1024, 1600, 2400)] int Width)
        {
            using var Log = new AvaloniaLogCapture();
            int Height = Width switch { 720 => 540, 1024 => 768, 1600 => 1000, _ => 1500 };
            var (Window, _) = ShowMainWindow();
            Resize(Window, Width, Height);
            SelectTab(Window, 0);
            Window.UpdateLayout();
            // Every card fills the capped column: min(tab width, 976) less the 16 gutter, so at most 960.
            Grid Content = Named<Grid>(Window, "ByParkContent");
            double CardWidth = Math.Min(Content.Bounds.Width, 976) - 16;
            Border Filters = Named<Border>(Window, "ParkFilters");
            Assert.That(Filters.Bounds.Width, Is.EqualTo(CardWidth).Within(1), "filter card does not fill the capped column");
            Assert.That(Named<Border>(Details(Window, 0), "EmptyState").Bounds.Width, Is.EqualTo(CardWidth).Within(1), "empty state does not fill the capped column");
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkThree);
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundDeltaID);
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
            Assert.That(Filters.TranslatePoint(new Point(0, 0), Content)!.Value.X, Is.EqualTo(0).Within(0.5), "filter card is not left-aligned");
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

            // The scroll area reaches the right edge of the tab content; the column inside it keeps the 960 cap and the
            // filter card's left edge.
            Point ScrollRight = Scroll.TranslatePoint(new Point(Scroll.Bounds.Width, 0), Content)!.Value;
            Assert.That(ScrollRight.X, Is.EqualTo(Content.Bounds.Width).Within(0.5), "details scroll area does not reach the tab edge");
            StackPanel Column = Named<StackPanel>(View, "DetailsColumn");
            Assert.That(Column.Bounds.Width, Is.EqualTo(CardWidth).Within(1), "details column is not as wide as the filter card");
            Assert.That(Column.TranslatePoint(new Point(0, 0), Window)!.Value.X,
                Is.EqualTo(Filters.TranslatePoint(new Point(0, 0), Window)!.Value.X).Within(0.5), "details column not aligned with the filters");
            if (Width >= 1600)
            {
                Assert.That(Content.Bounds.Width - Column.Bounds.Right, Is.GreaterThan(Width / 4), "expected empty space right of the capped column");
            }

            // The mouse wheel scrolls from the far right of the details area (right of the column on wide windows).
            Point WheelAt = Scroll.TranslatePoint(new Point(Scroll.Bounds.Width - 8, Scroll.Bounds.Height / 2), Window)!.Value;
            Assert.That(Scroll.Offset.Y, Is.EqualTo(0));
            Window.MouseWheel(WheelAt, new Vector(0, -3));
            Dispatcher.UIThread.RunJobs();
            Assert.That(Scroll.Offset.Y, Is.GreaterThan(0), "the mouse wheel did not scroll at the right edge of the details area");

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

        // The focus rectangles of the details area and of the campground ID are drawn at their bounds; R8 keeps the content
        // at least 4 DIP inside them, and the ID's focus target is the number itself, not the whole column.
        [AvaloniaTest]
        public void FocusTargets_KeepTheirContentInsideAnInset()
        {
            var (Window, _) = ShowMainWindow();
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);
            Window.UpdateLayout();
            CampgroundDetailsView View = Details(Window, 0);
            ScrollViewer Scroll = Named<ScrollViewer>(View, "DetailsScrollViewer");
            Point ColumnInScroll = Named<StackPanel>(View, "DetailsColumn").TranslatePoint(new Point(0, 0), Scroll)!.Value;
            Assert.That(ColumnInScroll.X, Is.GreaterThanOrEqualTo(4), "details content touches the left edge of its focus rectangle");
            Assert.That(ColumnInScroll.Y, Is.GreaterThanOrEqualTo(4), "details content touches the top edge of its focus rectangle");

            SelectableTextBlock Id = Named<SelectableTextBlock>(View, "CampgroundIdText");
            TextBlock IdLabel = Named<TextBlock>(View, "CampgroundIdLabel");
            Assert.That(Id.Padding.Left, Is.GreaterThanOrEqualTo(4));
            Assert.That(Id.Padding.Top, Is.GreaterThanOrEqualTo(2));
            Assert.That(Id.HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Left));
            Assert.That(Id.Bounds.Width, Is.EqualTo(Id.DesiredSize.Width - Id.Margin.Left - Id.Margin.Right).Within(0.5), "the ID is not sized to its content");
            Assert.That(Id.Bounds.Width, Is.LessThan(IdLabel.Bounds.Width * 2), "the ID's focus target spans the column");
            Assert.That(Id.Bounds.X + Id.Padding.Left, Is.EqualTo(IdLabel.Bounds.X).Within(0.5), "the digits are not aligned with their label");
            Assert.That(Id.Focus(NavigationMethod.Tab), Is.True);
            Window.Close();
        }

        // The selectors already select the first item that starts with the typed text (ComboBox text search); R8 only
        // tells users so in the placeholders. Typing on a focused, closed selector selects and updates its dependents.
        [AvaloniaTest]
        public void TypeAhead_SelectsTheFirstItemStartingWithTheTypedText()
        {
            var (Window, ViewModel) = ShowMainWindow();
            SelectTab(Window, 0);
            ComboBox Park = Named<ComboBox>(Window, "ParkComboBox");
            Assert.That(Park.IsTextSearchEnabled, Is.True);
            Assert.That(Park.IsEditable, Is.False, "no new filtering: the selector is not editable");
            Park.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();

            Window.KeyTextInput("Fixture Recreation Area Tw");
            Dispatcher.UIThread.RunJobs();

            Assert.That(Park.SelectedItem, Is.EqualTo(GuiTestEnvironment.ParkTwo));
            Assert.That(ViewModel.ReactiveViewModel!.SelectedPark, Is.EqualTo(GuiTestEnvironment.ParkTwo));
            Assert.That(Ids(Named<ComboBox>(Window, "ParkCampgroundComboBox")), Is.EqualTo(new[] { GuiTestEnvironment.CampgroundBravoID }));
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
                SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);
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
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlphaID);
            Assert.That(Queries.TakeCount(), Is.EqualTo(1), "campground -> details");
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkTwo);
            Assert.That(Queries.TakeCount(), Is.EqualTo(1), "new park -> campground list; the cleared campground runs no query");
            Window.Close();
        }
    }
}
