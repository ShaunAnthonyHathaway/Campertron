# Console → GUI map

Legend: **[V]** verified · **[I]** inference

GUI facts come from `CampertronGUI/Views/MainWindow.axaml(.cs)`, `Views/CampgroundDetailsView.axaml(.cs)`, `ViewModels/ReactiveViewModel.cs`, `ViewModels/CampgroundDetails.cs`, `ViewModels/MainWindowViewModel.cs` and `App.axaml(.cs)`. The GUI has no `ReactiveCommand` [V]. Its interactions are `ComboBox` selections across three tabs, selecting text, scrolling the details and one **Copy ID** button (a `Click` handler in `CampgroundDetailsView`, beta.8) [V]. It stays a read-only lookup: no search, results or configuration editing (decision D28).

| Console workflow (feature-inventory ID) | GUI status | Evidence |
|---|---|---|
| F1 Startup / first-run bootstrap (dirs, sample YAML, DB extraction, auto-refresh) | **Implemented** (shared code) | `MainWindow()` constructor calls `Load.GetConfig()` [V]. It is the same library method as the console, so the side effects are identical. The returned config is unused. A commented-out block in the same constructor used to launch `CampertronConsole` when the DB was missing |
| F2 Interactive menu | **Not implemented** | No menu equivalent. The GUI is a `TabControl` with "By Park", "By City", "By State" [V] |
| F3 Search with console output | **Not implemented** | No reference to `RunConsoleSearch`, `AvailabilityApi` or `Preload` in `CampertronGUI/**` [V, grep]. There is no availability display |
| F4 Search with HTML output | **Not implemented** | Same evidence as F3 |
| F5 Headless email loop | **Not implemented** | No reference to `EmailConfig` or `ProcessConsoleConfig` in the GUI [V] |
| F6 Refresh RIDB data | **Partial** | No user action. It can only happen as a side effect of `Load.GetConfig()` → `DbExistsCheck` when `autoRefresh: true` and the interval has elapsed [V]. That path writes progress to `Console`, which a WinExe does not show [I]. Since D26 it is failure-safe: a failed or interrupted refresh leaves the GUI's DB unchanged, and the GUI's own open or pooled connections need not close for the installation (business-rules §8). The GUI's `MainWindow` constructor still blocks during it [I], and a refresh that fails is retried on the next start because the timestamp is no longer written before the refresh |
| F7 View Config | **Not implemented** | No paths, refresh age or OS info shown. `ReactiveViewModel.GetConfigPath` exists but is not bound in XAML [V] |
| F8 Exit | **Unclear** | No explicit exit action. Window close is handled by Avalonia's classic desktop lifetime (`StartWithClassicDesktopLifetime` in `Program.cs`). Not an equivalent workflow |
| F9 Container mode | **Not implemented** | `ReactiveViewModel.GetConfigPath` is hard-coded to `LocalApplicationData/CampertronConfig` and ignores `CampertronInternalConfig.ConfigPath` [V] |
| F10 Search-config discovery and editing | **Not implemented** | No YAML read or write in the GUI. The GUI does not create or edit search configs [V] |
| F11 Duplicate-notification suppression | **Not implemented** | Not applicable without search |

## GUI-only features (no console equivalent)
| GUI feature | Implementation [V] | Notes |
|---|---|---|
| Browse by Park (RecArea) → Campground → details | `ParkList` → `Read.UniqueCampgrounds` (returns `RecAreaName`), `CampgroundListByPark` → `Read.UniqueCampgroundsByPark`, `SelectedCampgroundByPark` → `ParkCampgroundDetails` → `Read.GetCampgroundDetailsByName` | Method names are inverted relative to the data: "Campgrounds" returns park names |
| Browse by State → City → Campground | `StateList` → `Read.UniqueStates`, `CityList` → `Read.UniqueCities`, `CampgroundList` → `Read.UniqueParks(state, city, path)`, `SelectedCampground` → `CityCampgroundDetails` | An empty city is listed as "(No city listed)" (display only, `DisplayConverters.City`); the stored empty value is selected and queried |
| Browse by State → Campground | `StateListByState` → `Read.UniqueStates`, `CampgroundListByState` → `Read.UniqueParksByState`, `SelectedCampgroundByState` → `StateCampgroundDetails` | Each tab keeps its own selections and details since beta.8 (TD-08 fixed) |
| `--version` | `Program.Main` → `RuntimeInfo` (same as the console, F13) | Prints the version and .NET runtime and exits before Avalonia starts. On Windows (WinExe) only visible when redirected [A] |
| Campground details (`CampgroundDetailsView`, shared by the three tabs) | Binds a `CampgroundDetails` snapshot built from `Read.GetCampgroundDetailsByName` (unprefixed values, HTML converted by `Read.HtmlToPlainText`): title, the tab's filter context, "Campground ID" with a selectable ID and **Copy ID**, Contact (Phone, Email), Description, Directions, Fees; "Not listed" for missing values; an empty state before a campground is selected. The GUI owns all labels (D28) | This is how users get the `campgroundID` for a search YAML (README). The lookup is by name, and names are not unique (TD-38) |

The README's GUI screenshot (`docs/ss6.png`) shows the beta.8 `MainWindow` in the **Dark** theme, recaptured on 2026-11-12 after the `v0.1.0-beta.8` release from the released source (it replaced the Light capture made for §P; build-and-test.md §Refreshing the README screenshots).
