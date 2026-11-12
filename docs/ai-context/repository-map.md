# Repository map

Legend: **[V]** verified in source · **[I]** inference · **[?]** unknown (see open-questions.md)

Audited at commit `0e41fbf` on branch `v0.1-beta.4`.

## Solution
`Campertron.sln` [V] contains 6 projects: `CampertronConsole`, `CampertronLibrary`, `CampertronGUI`, `CampertronUnitTests`, `CampertronGUITests` (added with the Avalonia 12 migration), and `docker-compose` (`docker-compose.dcproj`).

Project references [V] (from the `.csproj` files):
```
CampertronConsole   ──► CampertronLibrary
CampertronGUI       ──► CampertronLibrary
CampertronUnitTests ──► CampertronLibrary
CampertronGUITests  ──► CampertronGUI ──► CampertronLibrary
```
No project references `CampertronConsole`. Only `CampertronGUITests` references `CampertronGUI`.

## Projects

### CampertronConsole (`CampertronConsole/CampertronConsole.csproj`)
- Exe, `net10.0`, `Nullable` enabled, `DockerDefaultTargetOS=Linux` [V]. Packages: EF Core Tools 9.0.19 (`PrivateAssets=all`), FileProviders.Embedded 9.0.8, Azure Containers Tools Targets 1.22.1 [V].
- `Program.cs` [V]: `--version` (exactly that one argument) prints `RuntimeInfo.Describe` and returns; otherwise `Load.RunConsoleSearch(Load.GetConfig())`. No other argument parsing.
- `Dockerfile` [V]: multi-stage, multi-architecture build. `mcr.microsoft.com/dotnet/sdk:10.0` runs on `$BUILDPLATFORM` and restores, builds and publishes the console with `-a $TARGETARCH` (framework-dependent, `UseAppHost=false`); the final stage is `runtime:10.0`. `VERSION`/`REVISION` build args and OCI labels (title, description, source, MIT license, version, revision). `ENTRYPOINT ["dotnet", "CampertronConsole.dll"]`; `USER app`. Needs BuildKit.
- `Properties/launchSettings.json` [V]: profiles `CampertronConsole`, `WSL`, `Docker`.

### CampertronLibrary (`CampertronLibrary/CampertronLibrary.csproj`)
- `net10.0` [V]. Embeds `content/RecreationDotOrg.db.br` as an `EmbeddedResource` [V]. It is the Brotli-compressed `RecreationDotOrg.db` (26,616,039 bytes; 226,471,936 bytes extracted; SHA-256 of the DB `8712856097a91ee1…`), built from the RIDB export of 2026-11-10 with only the 6 used tables. It replaced `content/RecreationDotOrg.zip` (81,232,118 bytes, holding a 328,126,464-byte DB dated 2024-11-18).
- Packages [V]: EF Core Sqlite 9.0.19, EF Core Tools 9.0.19, FileProviders.Embedded 9.0.8, Newtonsoft.Json 13.0.3, YamlDotNet 16.3.0.

| Directory | Responsibility | Key symbols |
|---|---|---|
| `class/Base/` | Config and run-state models (global namespace) | `Config.cs`: `GeneralConfig`, `EmailConfig`, `OutputType`, `CampertronConfig`, `SearchTypes`, `CampertronInternalConfig`. `ConsoleConfig.cs`: `ConsoleConfig` (+ nested `ConsoleConfigItem`, `ConsoleConfigValue`, `ConfigType`, and the HTML presentation metadata `OutputKind`/`OutputMetadata`, D29), `RunningData` |
| `class/RecDotOrg/` | RIDB JSON and EF entity models, availability API models | `Availability.cs`: `AvailabilityEntries`, `CampsitesDataEntry.GenerateDates`. `AvailabilityData.cs`: `AvailableData`. `Generic.cs`: `ReturnParkCampground`, `AvailabilityData`, `AttributeValueLists`, `AttributeValuePair`. Other files are `*Entries` (JSON wrapper with `RECDATA`/`METADATA`) and `*Recdata`/`*Data` (EF entity) pairs, one per RIDB file |
| `function/Base/` | Cross-cutting helpers | `Menu` (interactive console menu), `CampsiteConfig` in `Console.cs` (output rendering + email; `WriteAndOpenHtmlResults` writes and opens one page per `HtmlFile` run through the replaceable `OpenHtmlFile`, `WriteHtmlResults` writes the HTML results to `CachePath`, `CreateResultsEmail` builds the email with it attached), `ResultsHtml` (the HTML results page from the tagged output, D29), `Calendar` + `EnumerableExtensions.GetConsecutiveDays` in `Calendar.cs`, `Yaml` (config load, save and sample generation), `Web.Download`, `Compression.Unzip`, `ConfigFileReplace` (validated temporary file + rename, used for the RIDB refresh timestamp in `General.yaml`, TD-37), `Convert.StateNameToAbbreviation`, `RuntimeInfo` (`--version`: version and `FrameworkDescription`) |
| `function/RecDotOrg/api/` | External HTTP integrations | `AvailabilityApi` (recreation.gov availability + filtering), `RefreshRidbRecreationData.RefreshData` (RIDB bulk download → failure-safe refresh; an overload takes the extract-directory provider, used by tests; the cache directory is always `InternalConfig.CachePath`) |
| `function/RecDotOrg/data/` | Orchestration, caching, deserialization | `Load` (entry orchestration: `GetConfig`, `RunConsoleSearch`, `Preload`, `PostProcess`, `HasNewEntries`, `DbExistsCheck`, `DbExists`), `Cache` (per-campground JSON cache), `Data.DynamicDeserialize`, `LoadJsonFromFile` (RIDB file readers) |
| `function/RecDotOrg/db/` | EF Core context | `SqliteDb.cs`: `RecreationDotOrgContext` (21 `DbSet`s, composite keys in `OnModelCreating`) |
| `function/RecDotOrg/sqlite/` | DB read and write | `Read` (queries for GUI and cache), `Clear` (`ClearDatabase`, `ClearRefreshCache`, `CheckCacheDirectory`, `All`; the refresh clears `CampertronInternalConfig.CachePath`, TD-05/D27), `PopulateDbFromRIDBFiles.Populate`, `RidbStagedRefresh` (refresh lock, stale staging cleanup, staging DB build/validation and `BackupDatabase` installation, D26), `RidbTableInserter` (prepared-command insert of the six RIDB tables in EF Core's row order, D25), `LegacyAddressLayout` (one-time startup repair: replaces a DB with the swapped 2024 layout, or a pre-0.1.0-beta.4 DB that still has the omitted tables, with the bundled DB; clears the derived cache), `RidbDatabase` (omitted-table list, refresh-time validation) |
| `Migrations/` | EF migration `20230504034206_InitialCreate` + snapshot | `ProductVersion` annotation `7.0.4` [V]. No code calls `Migrate()` or `EnsureCreated()` [V, grep] |
| `content/` | `RecreationDotOrg.db.br` (pre-built DB, Brotli) | Read by `Load.ExtractEmbeddedDbTo` (via `EmbeddedFileProvider` + `BrotliStream`) for first-run extraction and the legacy repair |

### CampertronGUI (`CampertronGUI/CampertronGUI.csproj`)
- WinExe, `net10.0`. Avalonia, Avalonia.Desktop and Avalonia.Themes.Fluent at **12.1.6**, and `ReactiveUI.Avalonia.Reactive` **12.1.6** (ReactiveUI 26.0.1, System.Reactive 7.0.0) [V]. `Avalonia.Diagnostics` and `Avalonia.ReactiveUI` were removed in the Avalonia 12 migration (decision D23, build-and-test.md §K). `ImplicitUsings` is off.
- Before the migration (`8476688`): Avalonia 11.3.14, Avalonia.Diagnostics 11.3.14 (Debug only) and `Avalonia.ReactiveUI` 11.3.3.
- Target `ExcludeWindowsNativeDebugSymbols` (after `ResolvePackageAssets`): drops `libSkiaSharp.pdb` and `libHarfBuzzSharp.pdb` of `SkiaSharp/HarfBuzzSharp.NativeAssets.Win32` from Windows publish output and `.deps.json` (TD-35, decision D24) [V].
- Namespace `Campertron` (not `CampertronGUI`) [V].

| File | Role |
|---|---|
| `Program.cs` | `--version` prints `RuntimeInfo.Describe` and returns before Avalonia starts; otherwise `BuildAvaloniaApp().StartWithClassicDesktopLifetime`, `.UsePlatformDetect()`, `.LogToTrace()`, `.UseReactiveUI(_ => { })` (from `ReactiveUI.Avalonia.Reactive`). `Program` is `internal` |
| `App.axaml(.cs)` | Fluent theme following the system Light/Dark theme (`RequestedThemeVariant="Default"`, beta.8) plus `Styles/AppStyles.axaml`; sets `MainWindow` with `MainWindowViewModel` |
| `Styles/AppStyles.axaml` | The GUI's styles (beta.8, D28): `card` borders, text roles (`title`, `sectionHeading`, `fieldLabel`, `secondary`, `body`, `campgroundId`), `ComboBox.filter`, tab header size and the content column width. Colors only from Fluent `SystemControl*` resources through `DynamicResource` |
| `ViewLocator.cs` | Convention mapping from `*ViewModel` to `*View` |
| `ViewModels/MainWindowViewModel.cs` | Exposes `ReactiveViewModel` |
| `ViewModels/ReactiveViewModel.cs` | All GUI state and queries (calls `CampertronLibrary.function.RecDotOrg.sqlite.Read`), with separate selections and a details property per tab (`ParkCampgroundDetails`, `CityCampgroundDetails`, `StateCampgroundDetails`; beta.8). `ReactiveObject`/`RaiseAndSetIfChanged` from `ReactiveUI.Reactive`; 7 `WhenAnyValue(...).Subscribe(...)` calls (3 of them on several properties), source-generated by ReactiveUI.Binding (`ReactiveUI.Binding.Reactive`) |
| `ViewModels/CampgroundDetails.cs` | Immutable details snapshot for `CampgroundDetailsView`: trimmed values, "Not listed" for missing ones, collapsed blank lines, the tab's filter context (beta.8) |
| `ViewModels/ViewModelBase.cs` | Empty `ReactiveObject` |
| `Views/MainWindow.axaml(.cs)` | 1024×768 (minimum 720×540), centered. 3 tabs: "By Park", "By City", "By State", each a filter card and a `CampgroundDetailsView` in a left-aligned column. The constructor calls `Load.GetConfig()` and installs the Tab-wrap workaround (TD-39) |
| `Views/CampgroundDetailsView.axaml(.cs)` | Shared details view (beta.8): empty state, title, context, selectable ID with **Copy ID** (`CopyTextAsync` never throws), Contact, Description, Directions, Fees in one focusable `ScrollViewer` |
| `Views/DisplayConverters.cs` | `City`: shows an empty city as "(No city listed)" without changing the value |

### CampertronUnitTests (`CampertronUnitTests/CampertronUnitTests.csproj`)
- `net10.0` [V].
- NUnit 4.3.2, NUnit3TestAdapter 5.0.0, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4 [V].
- `Usings.cs`: `global using NUnit.Framework;` (no trailing newline) [V].
- `function/RecDotOrg/data/LoadUnitTests.cs`: class `LoadTests` with 8 tests [V].
- `function/RecDotOrg/data/DataUnitTests.cs`: class `DataTests` with 1 test, `LOAD_DbExists` [V].
- `function/RecDotOrg/sqlite/CampgroundDetailsTests.cs`: class `CampgroundDetailsTests` with 6 tests for `Read.GetCampgroundDetailsByName` (unprefixed values, plain text, same name matching, missing values, null/unknown/disabled) and the unchanged label-prefixed contract of `Read.GetParkCampgroundByName`, on temporary DBs [V].
- `function/RecDotOrg/sqlite/AddressStateTests.cs`: class `AddressStateTests` with 5 tests for the FacilityAddresses state/postal-code rules (business-rules §8). Each test creates its own temporary SQLite DB with `EnsureCreated()`, so it uses neither the user dirs nor the network [V].
- `function/RecDotOrg/sqlite/RidbDatabaseTests.cs`: class `RidbDatabaseTests` with 12 tests for `RidbDatabase` and `Clear.ClearDatabase`, using temporary DBs [V].
- `function/RecDotOrg/sqlite/PopulateTests.cs`: class `PopulateTests` with 19 tests for `PopulateDbFromRIDBFiles.Populate` and `RidbTableInserter` (equivalence with a test-only copy of the previous EF import, culture order, model drift, failures), on copies of the shipped DB emptied by `Clear.ClearDatabase`, in temporary directories [V].
- `function/RecDotOrg/data/EmbeddedDatabaseTests.cs`: class `EmbeddedDatabaseTests` with 3 tests. They extract the shipped `RecreationDotOrg.db.br` to a temp dir and validate it, including the GUI state queries (two-letter states, MA → BARRE, no `01005`) [V].
- `function/Base/RuntimeInfoTests.cs`: class `RuntimeInfoTests` with 3 tests for `RuntimeInfo` (`--version`), including that the tests run on .NET 10 [V].
- `function/RecDotOrg/sqlite/LegacyAddressLayoutTests.cs`: class `LegacyAddressLayoutTests` with 32 tests (7 classification, 8 legacy repair, 17 pre-0.1.0-beta.4 detection and repair) for `LegacyAddressLayout`. Each test uses its own temporary directory and DBs. The bundled DB is not used [V].
- `function/RecDotOrg/data/StartupRepairTests.cs`: class `StartupRepairTests` with 1 test: the startup repair through `Load.DbExistsCheck` with the real bundled DB, in temporary directories [V].
- `function/Base/ConfigFileReplaceTests.cs`: class `ConfigFileReplaceTests` with 10 tests for `ConfigFileReplace` and `Yaml.GeneralConfigReplaceYaml` (same bytes as the existing writer, every pre-replacement failure leaves `General.yaml` unchanged, cleanup failure, stale temporary files), in temporary directories [V].
- `function/RecDotOrg/sqlite/AtomicRefreshTests.cs`: class `AtomicRefreshTests` with 50 tests (34 TD-36 tests and 16 TD-05 cache-path tests) for the failure-safe refresh (`RefreshData` with a fixture extract directory, `RidbStagedRefresh`, `Clear.ClearRefreshCache`), on small fixture DBs and the shipped DB in temporary directories; uses the `RidbFixture` from `PopulateTests.cs` [V].
- `function/Base/ResultsHtmlTests.cs`: class `ResultsHtmlTests` with 25 structural parsing tests (System.Xml.Linq) of `ResultsHtml.Render` (feature-inventory.md §Test coverage); `function/Base/OutputStreams.cs` builds their tagged streams with the production producers [V].
- `function/Base/ConsoleOutputGoldenTests.cs`: class `ConsoleOutputGoldenTests` with 3 tests: the console output of the tagged producers is byte-identical to the pre-beta.9 output [V].
- `function/Base/HtmlResultsOutputTests.cs`: class `HtmlResultsOutputTests` with 6 tests (7 cases): one page and one browser launch per `HtmlFile` run, one email page per search; browser launch recorded, no mail sent [V].
- `function/Base/CachePathTests.cs`: class `CachePathTests` with 23 tests for TD-05: the desktop `CachePath`, the cache-clear guards and what a clear deletes (sentinels, neighbors, links), the HTML results and the email attachment in `CachePath` (no message sent). `LocalApplicationData` is redirected into each test's temporary directory (Linux) [V].

### CampertronGUITests (`CampertronGUITests/CampertronGUITests.csproj`)
- `net10.0`, `ImplicitUsings` on [V]. Avalonia.Headless.NUnit 12.1.6, Avalonia.Themes.Fluent 12.1.6, **NUnit 4.5.1** (required by Avalonia.Headless.NUnit 12.1.6; `CampertronUnitTests` stays on 4.3.2), NUnit3TestAdapter 5.0.0, NUnit.Analyzers 4.9.2, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4. References `CampertronGUI`.
- `Usings.cs`: `global using NUnit.Framework;` (no trailing newline, as in `CampertronUnitTests`). `ReactiveUI.Reactive`'s `buildTransitive` props also add `global using ReactiveUI.Binding.Reactive;`, whose `ViewLocator` clashes with `Campertron.ViewLocator` (qualify it) [V].
- `TestAppBuilder.cs`: `[assembly: AvaloniaTestApplication]`; `Campertron.App` with `UseHeadless` and `UseReactiveUI(_ => { })`, mirroring `Program.BuildAvaloniaApp` (which is `internal`). Default isolation: a new application per test.
- `GuiTestEnvironment.cs`: `[SetUpFixture]` that points `XDG_DATA_HOME` (this process only) at a temporary directory with `General.yaml` (`autoRefresh: false`) and a tiny deterministic EF-created DB (3 parks, one with a 91-character name; 4 campgrounds in UT/CO, including one with an empty city and no phone, email, directions or fee, and one with a 96-character name padded with six trailing spaces, a sentence-long phone value and a long description), so `Load.GetConfig()` does not extract, repair or refresh. Ignores all tests on non-Linux hosts (build-and-test.md, Test warnings §8).
- `AvaloniaLogCapture.cs`: `ILogSink` recording Avalonia warnings and errors.
- `GuiTestHelpers.cs`: shared helpers; controls are found by `x:Name` (`ParkComboBox`, `ByParkDetails`, `CampgroundIdText`, ...), not by tree position.
- `MainWindowTests.cs` (9 tests, 10 cases): app initialization (FluentTheme following the system theme, `ViewLocator`); `MainWindow` construction without DB extraction; the three tabs; each tab content's DataContext and its details view's `CampgroundDetails`; lists through compiled bindings; selection flows on all tabs to the displayed ID; no Avalonia warnings or errors in a full session in Light and in Dark; clean `Close()`.
- `ReactiveViewModelTests.cs` (6 tests): `RaiseAndSetIfChanged` raises only on change; each `WhenAnyValue` subscription raises its dependent list and the tab's details; TD-08 fixed (By State no longer changes the By City state); each tab's campground raises only its own details; `CampgroundDetails` missing-value text, trimming and context; the "(No city listed)" converter.
- `MainWindowUsabilityTests.cs` (16 tests, 18 cases): minimum size and centered start; empty state per tab; placeholders, accessible names and `Label.Target` of every selector; dependent selectors disabled until their prerequisite; independent per-tab selections and details; a filter change clears the dependent campground; GUI-owned labels and raw values; "Not listed" and "(No city listed)"; heading levels and the ID's help text; Tab order including the wrap to the tab header and arrow-key tab switching (TD-39); Copy ID with the keyboard (headless clipboard) and a missing clipboard; long content at 720×540, 1024×768 and 1600×1000 (no horizontal overflow, scrollable to Fees, left-aligned 960 column); theme resources in Light and Dark without shadows or LightGray; one EF Core query per selection.

## Other top-level items
- `.github/workflows/ci.yml` [V]: GitHub Actions CI. Release restore/build/test and a vulnerability scan on PRs to and pushes to `master` (build-and-test.md §Continuous integration).
- `.github/workflows/release.yml` [V]: release pipeline. A SemVer tag on `master` publishes 16 platform archives + `SHA256SUMS.txt` with attestations to a GitHub release, and a linux/amd64+arm64 image to GHCR. Manual runs and relevant PRs are dry runs (build-and-test.md §Release workflow). First release: `v0.1.0-beta.4`, 2026-11-11 (§Release history).
- `.github/release-policy/release-policy.sh` [V]: release policy sourced by `release.yml` (Docker tags, GitHub release flags, expected archive names, release-note rendering and validation, and the .NET 10 guards for projects, publish outputs, the image and `--version`). `.github/release-policy/test-release-policy.sh`: its tests (67), run by the `verify` job.
- `.github/release-policy/ReleaseCheck/` [V]: net10.0 console tool, not in `Campertron.sln` and not shipped. The release smoke job runs it to check the GUI state data with the published `CampertronLibrary.dll` against the DB the published app extracted (build-and-test.md §Release workflow).
- `docker-compose.yml` [V]: service `campertronconsole`. Bind-mounts `C:\temp\config` → `/config` and `C:\temp\cache` → `/cache` (Windows host paths).
- `launchSettings.json` (root) [V]: Docker Compose debug profile.
- `examples/0.1/`: `ZionConfig.yaml`, `SouthRimConfig.yaml`, and a `README.md` that documents the YAML keys.
- `docs/*.png`, `docs/*.svg`: README images.
- `.idea/.idea.Kamp/`: Rider project metadata (name "Kamp").
- Git tags [V]: `v0.1-beta1` and `v0.1-beta3` (lightweight, both on `e96bc7c`), `v0.1.0-beta.4` (annotated, on `5392aad`), `v0.1.0-beta.5` (annotated, on `0a20025`), `v0.1.0-beta.6` (annotated, on `6a172bd`), `v0.1.0-beta.7` (annotated, on `715f95e`) and `v0.1.0-beta.8` (annotated, on `84e5649`). All are protected by tag ruleset 27004221.
- GitHub releases [V, 2026-11-12]: `v0.1.0-beta.8` (ID 442958907, Latest), `v0.1.0-beta.7` (ID 442504169), `v0.1.0-beta.6` (ID 441825528), `v0.1.0-beta.5` (ID 441754432) and `v0.1.0-beta.4` (ID 441205400). All are normal, immutable releases. The 2024 release entries were deleted and their tags kept (build-and-test.md §Release history).
