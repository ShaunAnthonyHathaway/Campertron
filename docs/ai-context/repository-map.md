# Repository map

Legend: **[V]** verified in source · **[I]** inference · **[?]** unknown (see open-questions.md)

Audited at commit `0e41fbf` on branch `v0.1-beta.4`.

## Solution
`Campertron.sln` [V] contains 5 projects: `CampertronConsole`, `CampertronLibrary`, `CampertronGUI`, `CampertronUnitTests`, and `docker-compose` (`docker-compose.dcproj`).

Project references [V] (from the `.csproj` files):
```
CampertronConsole   ──► CampertronLibrary
CampertronGUI       ──► CampertronLibrary
CampertronUnitTests ──► CampertronLibrary
```
No project references `CampertronConsole` or `CampertronGUI`.

## Projects

### CampertronConsole (`CampertronConsole/CampertronConsole.csproj`)
- Exe, `net10.0`, `Nullable` enabled, `DockerDefaultTargetOS=Linux` [V]. Packages: EF Core Tools 9.0.19 (`PrivateAssets=all`), FileProviders.Embedded 9.0.8, Azure Containers Tools Targets 1.22.1 [V].
- `Program.cs` [V]: a single line, `Load.RunConsoleSearch(Load.GetConfig())`. It has no argument parsing (`args` is unused).
- `Dockerfile` [V]: multi-stage build (`mcr.microsoft.com/dotnet/sdk:10.0` → `runtime:10.0`) that restores, builds and publishes the console; `ENTRYPOINT ["dotnet", "CampertronConsole.dll"]`; `USER app`.
- `Properties/launchSettings.json` [V]: profiles `CampertronConsole`, `WSL`, `Docker`.

### CampertronLibrary (`CampertronLibrary/CampertronLibrary.csproj`)
- `net10.0` [V]. Embeds `content/RecreationDotOrg.zip` as an `EmbeddedResource` [V]. The zip holds a single `RecreationDotOrg.db`, 328,126,464 bytes, dated 2024-11-18 (verified with `unzip -l`).
- Packages [V]: EF Core Sqlite 9.0.19, EF Core Tools 9.0.19, FileProviders.Embedded 9.0.8, Newtonsoft.Json 13.0.3, YamlDotNet 16.3.0.

| Directory | Responsibility | Key symbols |
|---|---|---|
| `class/Base/` | Config and run-state models (global namespace) | `Config.cs`: `GeneralConfig`, `EmailConfig`, `OutputType`, `CampertronConfig`, `SearchTypes`, `CampertronInternalConfig`. `ConsoleConfig.cs`: `ConsoleConfig` (+ nested `ConsoleConfigItem`, `ConsoleConfigValue`, `ConfigType`), `RunningData` |
| `class/RecDotOrg/` | RIDB JSON and EF entity models, availability API models | `Availability.cs`: `AvailabilityEntries`, `CampsitesDataEntry.GenerateDates`. `AvailabilityData.cs`: `AvailableData`. `Generic.cs`: `ReturnParkCampground`, `AvailabilityData`, `AttributeValueLists`, `AttributeValuePair`. Other files are `*Entries` (JSON wrapper with `RECDATA`/`METADATA`) and `*Recdata`/`*Data` (EF entity) pairs, one per RIDB file |
| `function/Base/` | Cross-cutting helpers | `Menu` (interactive console menu), `CampsiteConfig` in `Console.cs` (output rendering + email), `Calendar` + `EnumerableExtensions.GetConsecutiveDays` in `Calendar.cs`, `Yaml` (config load, save and sample generation), `Web.Download`, `Compression.Unzip`, `Convert.StateNameToAbbreviation` |
| `function/RecDotOrg/api/` | External HTTP integrations | `AvailabilityApi` (recreation.gov availability + filtering), `RefreshRidbRecreationData.RefreshData` (RIDB bulk download → DB) |
| `function/RecDotOrg/data/` | Orchestration, caching, deserialization | `Load` (entry orchestration: `GetConfig`, `RunConsoleSearch`, `Preload`, `PostProcess`, `HasNewEntries`, `DbExistsCheck`, `DbExists`), `Cache` (per-campground JSON cache), `Data.DynamicDeserialize`, `LoadJsonFromFile` (RIDB file readers) |
| `function/RecDotOrg/db/` | EF Core context | `SqliteDb.cs`: `RecreationDotOrgContext` (21 `DbSet`s, composite keys in `OnModelCreating`) |
| `function/RecDotOrg/sqlite/` | DB read and write | `Read` (queries for GUI and cache), `Clear.All`, `PopulateDbFromRIDBFiles.Populate` |
| `Migrations/` | EF migration `20230504034206_InitialCreate` + snapshot | `ProductVersion` annotation `7.0.4` [V]. No code calls `Migrate()` or `EnsureCreated()` [V, grep] |
| `content/` | `RecreationDotOrg.zip` (pre-built DB) | Read by `Load.DbExistsCheck` via `EmbeddedFileProvider` |

### CampertronGUI (`CampertronGUI/CampertronGUI.csproj`)
- WinExe, `net10.0`. Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent and Avalonia.Diagnostics (Debug only) at 11.3.14. `Avalonia.ReactiveUI` stays at 11.3.3, because Avalonia published no 11.3.14 of it (last listed 11.3.8; 11.3.9 unlisted). It requires only Avalonia >= 11.3.3 [V].
- Namespace `Campertron` (not `CampertronGUI`) [V].

| File | Role |
|---|---|
| `Program.cs` | `BuildAvaloniaApp().StartWithClassicDesktopLifetime`, `.LogToTrace()`, `.UseReactiveUI()` |
| `App.axaml(.cs)` | Fluent theme, Light variant; sets `MainWindow` with `MainWindowViewModel` |
| `ViewLocator.cs` | Convention mapping from `*ViewModel` to `*View` |
| `ViewModels/MainWindowViewModel.cs` | Exposes `ReactiveViewModel` |
| `ViewModels/ReactiveViewModel.cs` | All GUI state and queries (calls `CampertronLibrary.function.RecDotOrg.sqlite.Read`) |
| `ViewModels/ViewModelBase.cs` | Empty `ReactiveObject` |
| `Views/MainWindow.axaml(.cs)` | 3 tabs: "By Park", "By City", "By State". The constructor calls `Load.GetConfig()` |

### CampertronUnitTests (`CampertronUnitTests/CampertronUnitTests.csproj`)
- `net10.0` [V].
- NUnit 4.3.2, NUnit3TestAdapter 5.0.0, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4 [V].
- `Usings.cs`: `global using NUnit.Framework;` (no trailing newline) [V].
- `function/RecDotOrg/data/LoadUnitTests.cs`: class `LoadTests` with 8 tests [V].
- `function/RecDotOrg/data/DataUnitTests.cs`: class `DataTests` with 1 test, `LOAD_DbExists` [V].
- `function/RecDotOrg/sqlite/AddressStateTests.cs`: class `AddressStateTests` with 5 tests for the FacilityAddresses state/postal-code rules (business-rules §8). Each test creates its own temporary SQLite DB with `EnsureCreated()`, so it uses neither the user dirs nor the network [V].

## Other top-level items
- `.github/workflows/ci.yml` [V]: GitHub Actions CI. Release restore/build/test and a vulnerability scan on PRs to and pushes to `master` (build-and-test.md §Continuous integration).
- `docker-compose.yml` [V]: service `campertronconsole`. Bind-mounts `C:\temp\config` → `/config` and `C:\temp\cache` → `/cache` (Windows host paths).
- `launchSettings.json` (root) [V]: Docker Compose debug profile.
- `examples/0.1/`: `ZionConfig.yaml`, `SouthRimConfig.yaml`, and a `README.md` that documents the YAML keys.
- `docs/*.png`, `docs/*.svg`: README images.
- `.idea/.idea.Kamp/`: Rider project metadata (name "Kamp").
- Git tags [V]: `v0.1-beta1`, `v0.1-beta3`.
