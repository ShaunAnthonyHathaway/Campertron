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
- `Program.cs` [V]: `--version` (exactly that one argument) prints `RuntimeInfo.Describe` and returns; otherwise `Load.RunConsoleSearch(Load.GetConfig())`. No other argument parsing.
- `Dockerfile` [V]: multi-stage, multi-architecture build. `mcr.microsoft.com/dotnet/sdk:10.0` runs on `$BUILDPLATFORM` and restores, builds and publishes the console with `-a $TARGETARCH` (framework-dependent, `UseAppHost=false`); the final stage is `runtime:10.0`. `VERSION`/`REVISION` build args and OCI labels (title, description, source, MIT license, version, revision). `ENTRYPOINT ["dotnet", "CampertronConsole.dll"]`; `USER app`. Needs BuildKit.
- `Properties/launchSettings.json` [V]: profiles `CampertronConsole`, `WSL`, `Docker`.

### CampertronLibrary (`CampertronLibrary/CampertronLibrary.csproj`)
- `net10.0` [V]. Embeds `content/RecreationDotOrg.db.br` as an `EmbeddedResource` [V]. It is the Brotli-compressed `RecreationDotOrg.db` (26,616,039 bytes; 226,471,936 bytes extracted; SHA-256 of the DB `8712856097a91ee1…`), built from the RIDB export of 2026-11-10 with only the 6 used tables. It replaced `content/RecreationDotOrg.zip` (81,232,118 bytes, holding a 328,126,464-byte DB dated 2024-11-18).
- Packages [V]: EF Core Sqlite 9.0.19, EF Core Tools 9.0.19, FileProviders.Embedded 9.0.8, Newtonsoft.Json 13.0.3, YamlDotNet 16.3.0.

| Directory | Responsibility | Key symbols |
|---|---|---|
| `class/Base/` | Config and run-state models (global namespace) | `Config.cs`: `GeneralConfig`, `EmailConfig`, `OutputType`, `CampertronConfig`, `SearchTypes`, `CampertronInternalConfig`. `ConsoleConfig.cs`: `ConsoleConfig` (+ nested `ConsoleConfigItem`, `ConsoleConfigValue`, `ConfigType`), `RunningData` |
| `class/RecDotOrg/` | RIDB JSON and EF entity models, availability API models | `Availability.cs`: `AvailabilityEntries`, `CampsitesDataEntry.GenerateDates`. `AvailabilityData.cs`: `AvailableData`. `Generic.cs`: `ReturnParkCampground`, `AvailabilityData`, `AttributeValueLists`, `AttributeValuePair`. Other files are `*Entries` (JSON wrapper with `RECDATA`/`METADATA`) and `*Recdata`/`*Data` (EF entity) pairs, one per RIDB file |
| `function/Base/` | Cross-cutting helpers | `Menu` (interactive console menu), `CampsiteConfig` in `Console.cs` (output rendering + email), `Calendar` + `EnumerableExtensions.GetConsecutiveDays` in `Calendar.cs`, `Yaml` (config load, save and sample generation), `Web.Download`, `Compression.Unzip`, `Convert.StateNameToAbbreviation`, `RuntimeInfo` (`--version`: version and `FrameworkDescription`) |
| `function/RecDotOrg/api/` | External HTTP integrations | `AvailabilityApi` (recreation.gov availability + filtering), `RefreshRidbRecreationData.RefreshData` (RIDB bulk download → DB) |
| `function/RecDotOrg/data/` | Orchestration, caching, deserialization | `Load` (entry orchestration: `GetConfig`, `RunConsoleSearch`, `Preload`, `PostProcess`, `HasNewEntries`, `DbExistsCheck`, `DbExists`), `Cache` (per-campground JSON cache), `Data.DynamicDeserialize`, `LoadJsonFromFile` (RIDB file readers) |
| `function/RecDotOrg/db/` | EF Core context | `SqliteDb.cs`: `RecreationDotOrgContext` (21 `DbSet`s, composite keys in `OnModelCreating`) |
| `function/RecDotOrg/sqlite/` | DB read and write | `Read` (queries for GUI and cache), `Clear.All`, `PopulateDbFromRIDBFiles.Populate`, `LegacyAddressLayout` (detects and repairs a DB built from the swapped 2024 RIDB export), `RidbDatabase` (omitted-table list, refresh-time validation) |
| `Migrations/` | EF migration `20230504034206_InitialCreate` + snapshot | `ProductVersion` annotation `7.0.4` [V]. No code calls `Migrate()` or `EnsureCreated()` [V, grep] |
| `content/` | `RecreationDotOrg.db.br` (pre-built DB, Brotli) | Read by `Load.ExtractEmbeddedDbTo` (via `EmbeddedFileProvider` + `BrotliStream`) for first-run extraction and the legacy repair |

### CampertronGUI (`CampertronGUI/CampertronGUI.csproj`)
- WinExe, `net10.0`. Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent and Avalonia.Diagnostics (Debug only) at 11.3.14. `Avalonia.ReactiveUI` stays at 11.3.3, because Avalonia published no 11.3.14 of it (last listed 11.3.8; 11.3.9 unlisted). It requires only Avalonia >= 11.3.3 [V].
- Namespace `Campertron` (not `CampertronGUI`) [V].

| File | Role |
|---|---|
| `Program.cs` | `--version` prints `RuntimeInfo.Describe` and returns before Avalonia starts; otherwise `BuildAvaloniaApp().StartWithClassicDesktopLifetime`, `.LogToTrace()`, `.UseReactiveUI()` |
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
- `function/RecDotOrg/sqlite/RidbDatabaseTests.cs`: class `RidbDatabaseTests` with 12 tests for `RidbDatabase` and `Clear.ClearDatabase`, using temporary DBs [V].
- `function/RecDotOrg/data/EmbeddedDatabaseTests.cs`: class `EmbeddedDatabaseTests` with 3 tests. They extract the shipped `RecreationDotOrg.db.br` to a temp dir and validate it, including the GUI state queries (two-letter states, MA → BARRE, no `01005`) [V].
- `function/Base/RuntimeInfoTests.cs`: class `RuntimeInfoTests` with 3 tests for `RuntimeInfo` (`--version`), including that the tests run on .NET 10 [V].
- `function/RecDotOrg/sqlite/LegacyAddressLayoutTests.cs`: class `LegacyAddressLayoutTests` with 15 tests (7 classification, 8 repair) for `LegacyAddressLayout`. Each test uses its own temporary directory and DBs. The bundled DB is not used [V].

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
- Git tags [V]: `v0.1-beta1` and `v0.1-beta3` (lightweight, both on `e96bc7c`), `v0.1.0-beta.4` (annotated, on `5392aad`) and `v0.1.0-beta.5` (annotated, on `0a20025`). All are protected by tag ruleset 27004221.
- GitHub releases [V, 2026-11-11]: `v0.1.0-beta.5` (ID 441754432, Latest) and `v0.1.0-beta.4` (ID 441205400). Both are normal, immutable releases. The 2024 release entries were deleted and their tags kept (build-and-test.md §Release history).
