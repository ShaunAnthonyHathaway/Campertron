# Architecture decisions

The repository has no ADRs or design notes. The decisions below are **observable choices** in the code and config. Rationale is given only where the repository states it. Otherwise it is marked "not recorded".

## Verified decisions
| # | Decision | Evidence | Stated rationale |
|---|---|---|---|
| D1 | Shared logic lives in one class library consumed by the console and GUI hosts | `ProjectReference` to `CampertronLibrary` in `CampertronConsole.csproj` and `CampertronGUI.csproj` | not recorded |
| D2 | Target .NET 10 for all projects (previously `net9.0` at commit `0e41fbf`) | `<TargetFramework>net10.0</TargetFramework>` in all four `.csproj` files; Dockerfile `sdk:10.0` / `runtime:10.0` | the maintainer asked for the migration during the 2026-11-10 audit session, with explicit instructions not to pin .NET 9. NuGet packages were deliberately left unchanged because none were required for net10.0 (build-and-test.md §B) |
| D3 | Static RIDB reference data is stored in SQLite through EF Core | `RecreationDotOrgContext` (`function/RecDotOrg/db/SqliteDb.cs`), package `Microsoft.EntityFrameworkCore.Sqlite` | not recorded |
| D4 | Ship a pre-populated DB as an embedded zip instead of running migrations | `<EmbeddedResource Include="content\RecreationDotOrg.zip" />`, `Load.DbExistsCheck`. No `Migrate()` call | not recorded |
| D5 | Per-campground static data is cached as JSON files to speed up searches | `Cache.CheckCache` | code comment: "we cache campsite data in local json files to make faster" (`AvailabilityApi.GetAvailabilitiesByCampground`) |
| D6 | User configuration lives in YAML files, and multiple search files are allowed | `function/Base/Yaml.cs`, README "Multiple configuration files can be used" | README: search against all configs, "grab the data it needs … once" |
| D7 | Each monthly availability URL is fetched only once per search cycle and shared across configs | `RunData.Urls.TryAdd`, `RunData.SiteData` (`AvailabilityApi.ProcessMonth`) | code comment: "overlaping config files use the same data that's only retrieved once" |
| D8 | Duplicate notifications are suppressed with filesystem marker directories | `Load.HasNewEntries`, `FilterData` comment "prevents duplicate notifications" | comment only |
| D9 | The container deployment is headless and email-only | `Menu.WriteMenu` exits in a container. `/config` and `/cache` in `Load.GetConfig`. README Docker section | not recorded |
| D10 | Output is abstracted as a list of `ConsoleConfigValue` render instructions, so one result can be rendered to console, HTML or email | `class/Base/ConsoleConfig.cs`, `CampsiteConfig.ProcessConsoleConfig` | not recorded |
| D11 | The GUI uses Avalonia + ReactiveUI (cross-platform desktop) | `CampertronGUI.csproj` packages, `Program.cs` `.UseReactiveUI()` | not recorded |
| D12 | The GUI's role is campground lookup, not search | README: "run CampertronGUI to get the campground ID and other information about the locations" | README |
| D13 | Tests run against real environment state, with a dedicated `OutputType.UnitTest` switch in production code | `OutputType.UnitTest` in `Config.cs`. Checks in `Menu.Init`, `Load.Preload`, `Load.RunConsoleSearch`, `Load.PostProcess`, `CampsiteConfig.ProcessConsoleConfig` | not recorded |

## Recommendations (not decisions; nothing below has been adopted)
- R1. Isolate the test environment: let `Load.GetConfig` accept the config and cache paths, so tests can use a temp dir. This would address TD-01.
- R2. Use the `CampertronInternalConfig.CachePath` consistently for HTML output and in `Clear.All` (TD-05).
- R3. Remove the unused MailKit and MimeKit references, or migrate email to MailKit. `System.Net.Mail.SmtpClient` is marked obsolete for new development by Microsoft [I].
- R4. Introduce a seam (an interface or delegate) for HTTP and the clock so the filter and date rules can be unit-tested (technical-debt.md §Missing tests).
- R5. Before any GUI search work, extract the search pipeline away from `Console` and `Menu` calls (`Load.PostProcess` → `Menu.Init`) so a non-console host can reuse it.
