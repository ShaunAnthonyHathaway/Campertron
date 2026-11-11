# Console → GUI map

Legend: **[V]** verified · **[I]** inference

GUI facts come from `CampertronGUI/Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`, `ViewModels/ReactiveViewModel.cs`, `ViewModels/MainWindowViewModel.cs` and `App.axaml.cs`. The GUI has no commands, buttons or `ReactiveCommand` [V]. Its only interactions are `ComboBox` selections across three tabs.

| Console workflow (feature-inventory ID) | GUI status | Evidence |
|---|---|---|
| F1 Startup / first-run bootstrap (dirs, sample YAML, DB extraction, auto-refresh) | **Implemented** (shared code) | `MainWindow()` constructor calls `Load.GetConfig()` [V]. It is the same library method as the console, so the side effects are identical. The returned config is unused. A commented-out block in the same constructor used to launch `CampertronConsole` when the DB was missing |
| F2 Interactive menu | **Not implemented** | No menu equivalent. The GUI is a `TabControl` with "By Park", "By City", "By State" [V] |
| F3 Search with console output | **Not implemented** | No reference to `RunConsoleSearch`, `AvailabilityApi` or `Preload` in `CampertronGUI/**` [V, grep]. There is no availability display |
| F4 Search with HTML output | **Not implemented** | Same evidence as F3 |
| F5 Headless email loop | **Not implemented** | No reference to `EmailConfig` or `ProcessConsoleConfig` in the GUI [V] |
| F6 Refresh RIDB data | **Partial** | No user action. It can only happen as a side effect of `Load.GetConfig()` → `DbExistsCheck` when `autoRefresh: true` and the interval has elapsed [V]. That path writes progress to `Console`, which a WinExe does not show [I] |
| F7 View Config | **Not implemented** | No paths, refresh age or OS info shown. `ReactiveViewModel.GetConfigPath` exists but is not bound in XAML [V] |
| F8 Exit | **Unclear** | No explicit exit action. Window close is handled by Avalonia's classic desktop lifetime (`StartWithClassicDesktopLifetime` in `Program.cs`). Not an equivalent workflow |
| F9 Container mode | **Not implemented** | `ReactiveViewModel.GetConfigPath` is hard-coded to `LocalApplicationData/CampertronConfig` and ignores `CampertronInternalConfig.ConfigPath` [V] |
| F10 Search-config discovery and editing | **Not implemented** | No YAML read or write in the GUI. The GUI does not create or edit search configs [V] |
| F11 Duplicate-notification suppression | **Not implemented** | Not applicable without search |

## GUI-only features (no console equivalent)
| GUI feature | Implementation [V] | Notes |
|---|---|---|
| Browse by Park (RecArea) → Campground → details | `ParkList` → `Read.UniqueCampgrounds` (returns `RecAreaName`), `CampgroundListByPark` → `Read.UniqueCampgroundsByPark`, `CampgroundByName` → `Read.GetParkCampgroundByName` | Method names are inverted relative to the data: "Campgrounds" returns park names |
| Browse by State → City → Campground | `StateList` → `Read.UniqueStates`, `CityList` → `Read.UniqueCities`, `CampgroundList` → `Read.UniqueParks(state, city, path)` | |
| Browse by State → Campground | `StateListByState` → `Read.UniqueStates`, `CampgroundListByState` → `Read.UniqueParksByState` | `SelectedStateByState` reads and writes `_SelectedState`, so the "By City" and "By State" tabs share one selected state (TD-08) |
| `--version` | `Program.Main` → `RuntimeInfo` (same as the console, F13) | Prints the version and .NET runtime and exits before Avalonia starts. On Windows (WinExe) only visible when redirected [A] |
| Facility details panel | Binds `FacilityID`, `FacilityName`, `FacilityPhone`, `FacilityEmail`, `FacilityDescription`, `FacilityDirections`, `FacilityUseFeeDescription` of `FacilitiesData`, with labels prefixed by `Read.NullCheck` and HTML stripped by `Read.HtmlToPlainText` | This is how users get the `campgroundID` for a search YAML (README: "run CampertronGUI to get the campground ID") |

The README's GUI screenshot (`docs/ss6.png`) has not been compared against the current XAML.
