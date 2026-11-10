# Technical debt

Legend: **[V]** confirmed in source · **[I]** consequence inferred from confirmed code

Each item lists its evidence. Style preferences (naming, brace style, PascalCase locals) are deliberately excluded.

## Correctness and behavioral defects
| ID | Issue | Evidence |
|---|---|---|
| TD-01 | **Tests touch real user state and the network** (config dir, DB extraction, recreation.gov, `Camp.History` markers) | [V] `LoadUnitTests.cs` calls `Load.GetConfig()`, `RunConsoleSearch(config, true)`. [V] `Load.HasNewEntries` creates markers when `OutputTo != Console`. **Confirmed by test run 2026-11-10:** 1 live request, DB extraction, 384 cache files, 118 `Camp.History` markers (in an isolated `XDG_DATA_HOME`) |
| TD-02 | `LoadTests.RunConsoleSearchTest2` cannot fail on search logic | [V] `Load.RunConsoleSearch(CampertronInternalConfig)` returns `true` unconditionally for `UnitTest`. With `ForceSearch=false` and non-Email output, the inner call only reaches `Menu.Init`, which is a no-op for UnitTest. Test run: it passed in 1 ms with no request made |
| TD-03 | The container `AutoRefresh = false` override is lost | [V] `Load.GetConfig` sets it (line 150), then reassigns `ReturnConfig.GeneralConfig = Yaml.GeneralConfigGetConfig(...)` (line 180). It also loads General and Email YAML twice in container mode |
| TD-04 | Consecutive-day filtering operates on state shared across parallel configs | [V] `FilterConsecutiveDays` groups and reassigns `RunData.FilteredAvailableData`, which every config in `Parallel.ForEach` (`Load.RunConsoleSearch`) writes to. [I] One config's consecutive filter can drop or include another config's hits, and the race on reassignment can lose entries |
| TD-05 | Inconsistent cache path | [V] `CampsiteConfig.ProcessConsoleConfig` (HTML) and `Clear.All` use `LocalApplicationData/CampertronCache` instead of `CachePath`. [I] In a container, `/cache` JSON files are never cleared by a refresh, and HTML is written inside the container filesystem |
| TD-06 | HTML and email output runs once per config item | [V] `PostProcess` loops `ProcessConsoleConfig` per `ConsoleConfigItem`, and each call deletes `*.html`, writes a new file and opens a browser or sends an email. [I] With multiple configs: several emails per cycle and several browser launches, where earlier HTML files are deleted by later calls |
| TD-07 | Today's availability is never reported | [V] `FilterData` requires `CampsiteAvailableDate >= DateTime.Now`, and the dates are midnight (`ProcessMonth` `CheckDt.AddDays`). `Until` with a past date yields only today, so it never matches anything |
| TD-08 | GUI "By State" and "By City" share a selected state | [V] `ReactiveViewModel.SelectedStateByState` reads and writes `_SelectedState`. The field `_SelectedStateByState` is unused |
| TD-09 | Unbounded retry and wait loops | [V] `GetWebApiData` catch-all retries forever every 30 s. Heavy traffic recurses without limit. `ProcessMonth` waits on `SiteData` forever. [I] `while (ReceivedData == false)` spins if the JSON has only `count` |
| TD-10 | `Environment.Exit` and `Console.ReadKey` inside library code | [V] `CampertronConfig.GenerateSearchDates` (StartEndDate equal), `GetWebApiData` (REQUEST BLOCKED), `Menu.WriteMenu` (container), `Menu.ProcessInput` (Exit). [I] Prevents reuse from the GUI and makes testing hard |
| TD-11 | Culture-dependent date parsing | [V] `ConvertToZeroHour` and `ProcessMonth` parse the `"M/d/yyyy 0:00:00 AM"` strings with `Convert.ToDateTime`. [I] Fails or swaps day and month under non-US cultures |
| TD-12 | Recursive menu/search call chain never unwinds | [V] `PostProcess` → `Menu.Init` → `WriteMenu` (`while(true)`) → `ProcessInput` → `RunConsoleSearch` → `PostProcess`, and `ProcessInput` → `WriteMenu` recursion on every key press. [I] The stack grows with each interaction |
| TD-13 | Sample `ZionConfig.yaml` overwrites an existing file | [V] `Yaml.CampertronConfigGetConfigs` → `CampertronConfigGenerateSampleConfig` when no `autoRun: true` config exists |
| TD-14 | Markers are created before output or email succeeds | [V] `PostProcess` calls `HasNewEntries` before `ProcessConsoleConfig`. [I] An SMTP failure permanently suppresses those hits |
| TD-15 | Unreachable branch in `PostProcess` | [V] In the non-Email, non-container branch, `OutputTo != Console && != HtmlFile` can only be `UnitTest`, which has already returned, so `RunConsoleSearch(config, false)` there is dead code. [I] The container non-Email branch is also unreachable (see feature-inventory.md F9) |

## Dead, stale or duplicated code
| ID | Issue | Evidence [V] |
|---|---|---|
| TD-20 | ~~Unused package references `MailKit`, `MimeKit`~~ **Resolved in `d20e1ad`:** both removed from `CampertronLibrary.csproj`. They were unused (email uses `System.Net.Mail`) and were the source of Dependabot alerts GHSA-9j88-vvj5-vhgr and GHSA-g7hc-96xr-gvvx | [V] build 0 errors, tests 9/9 after removal. No MailKit/MimeKit/BouncyCastle/Pkcs assemblies in the build output |
| TD-21 | ~~Stray `using static Org.BouncyCastle.Math.EC.ECCurve;`~~ **Resolved in `d20e1ad`:** removed from `function/Base/Menu.cs` and `function/RecDotOrg/data/Load.cs` | [V] Nothing used them: a build without them compiled with 0 errors, the only BouncyCastle TypeRef (`ECCurve`) disappeared, and the compiler warnings were otherwise identical |
| TD-22 | Unused methods | `Read.GetParkCampgroundIdByName`, `Read.UniqueParks(String)`, `Read.GetPermittedEquipmentByCampsite`, `Read.GetCampSiteAttributesByCampsite` (`Cache` has its own versions), `Calendar.WriteDate`, `CampsiteConfig.WriteToConsole(List<ConsoleConfigValue>)` (no external caller) |
| TD-23 | Unused GUI members | `ReactiveViewModel.SelectedCampgroundByState`, `SelectedCampgroundID`, `GetConfigPath` (only used internally, not bound). The `config` local in the `MainWindow` constructor, plus a commented-out block |
| TD-24 | Duplication | `Cache.CheckCache` and `Cache.PreCheckCache` repeat the cache-build block. The three `*ConvertToYaml` and three `*ConvertFromYaml` methods in `Yaml` are identical except for the type. `LoadJsonFromFile` has 21 near-identical methods. `PopulateDbFromRIDBFiles.Populate` has 21 near-identical blocks. In `FilterData`, `HitDates` and `HitCounter++` are updated twice when `ConsecutiveDays == 1`. The three GUI tabs duplicate the details XAML |
| TD-25 | Stale EF migrations | `Migrations/*` annotate `ProductVersion` `7.0.4` while the packages are 9.0.19, and no code applies migrations |
| TD-26 | Misnamed test | `DataTests.LOAD_DbExists` tests `Data.DynamicDeserialize` and asserts only `Assert.That(true)` |
| TD-27 | Misleading names | `Read.UniqueCampgrounds` returns RecArea (park) names. `Read.UniqueParks*` return facility (campground) names. `GetParkCampgroundInfo(string CampsiteID, …)` takes a facility ID. `FacilityAddressesRecdata.PostalCode` is used as the state |
| TD-28 | Documentation drift | `examples/0.1/README.md` says SouthRim searches "21 days" but `SouthRimConfig.yaml` has `searchValue: 30`. README says Zion is "30 days", which matches the example file, but the generated sample (`CampertronConfigGenerateSampleConfig`) uses 7. Root `README.md` links the `v0.1-beta3` release while the current branch is `v0.1-beta.4` |
| TD-29 | `docker-compose.yml` uses Windows-only host paths | `C:\temp\config`, `C:\temp\cache` |
| TD-30 | 89 warnings in the net10.0 Debug build: 87 compiler warnings (85 nullable-reference CS86xx, CS0169, CS8981; identical to the net9.0 baseline) + 2 NuGet warnings (NU1903 ×1 for Tmds.DBus.Protocol, NU1510 ×1). It was 109 before `d20e1ad` removed MailKit/MimeKit, and 93 before `35c888f` updated EF Core to 9.0.19 | [V] `dotnet build` 2026-11-10 with SDK 10.0.114, breakdown in build-and-test.md |
| TD-31 | No pinned SDK (`global.json` absent), so builds depend on whichever SDK is newest on the machine | [V] the build needs a .NET 10 SDK, runtime and targeting pack. Missing pieces caused exit code 150 and `NETSDK1226` on the audit machine (build-and-test.md) |
| TD-32 | Vulnerable transitive packages. **SQLite part resolved in `35c888f`:** `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 (high, GHSA-2m69-gcr7-jv3q / CVE-2025-6965, vulnerable range <= 2.1.11) came via EF Core Sqlite 9.0.8 → `SQLitePCLRaw.bundle_e_sqlite3` 2.1.10. EF Core Sqlite and Tools were updated to 9.0.19, which resolves SQLitePCLRaw 2.1.12. **Still open:** `Tmds.DBus.Protocol` 0.21.2 (high, GHSA-xrw6-gwf8-vvr9, path Avalonia.Desktop 11.3.3 → Avalonia.X11 → Avalonia.FreeDesktop, GUI only). Both were already present on net9.0, but were reported only after the net10.0 migration. `System.Security.Cryptography.Pkcs` 8.0.1 is gone since `d20e1ad` (it came only through MimeKit). GitHub Dependabot does not alert on transitive packages here [I: no lock file exposes them] | [V] after `35c888f`, `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages for Console, Library and UnitTests, and only `Tmds.DBus.Protocol` for GUI |
| TD-33 | `System.Collections.NonGeneric` 4.3.0 is likely unnecessary on net10.0 | [V] `NU1510` from `dotnet restore`. No code usage was audited; verify before removing |

## Missing tests
- [V] There are no unit tests for: `FilterData` rules, `GenerateSearchDates`/`GetMonthsOut`, `GenerateDates`, `GetConsecutiveDays`, `Calendar.GenerateCalendar`, `Yaml` loading and sample generation, `Read` queries, `Cache`, `RefreshData`/`Populate`, `ProcessConsoleConfig` (HTML/email), `Menu`, any GUI view model. See feature-inventory.md §Test coverage.
- [V] There is no CI pipeline in the repository.

## Migration risks (for example, moving search into the GUI or refactoring)
- [V] The search pipeline writes directly to `Console` (`CampsiteConfig.WriteToConsole`, `Console.WriteLine` in `GetWebApiData`, `Cache`, `Data`, `Clear`, `Populate`) and calls `Menu.Init` from `Load.PostProcess`. A GUI host would need these separated first.
- [V] `Load.GetConfig()` has side effects (dir creation, YAML writes, a 328 MB extraction, possible network refresh) and is called synchronously in the GUI `MainWindow` constructor, so it blocks the UI thread [I].
- [V] Models in the global namespace and static mutable state (`Menu._location`) complicate reuse.
- [V] Changing the filter semantics (§3 of business-rules) changes which notifications users receive. Treat it as a behavior change.
- [V] Changing the `Camp.History` path format would re-notify every previously suppressed hit.
- [V] The DB schema has no migration path in use. Model changes to `*Recdata` need a new embedded DB or a migration strategy.
