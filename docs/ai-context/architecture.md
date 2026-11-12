# Architecture

Legend: **[V]** verified in source · **[I]** inference · **[R]** recommendation · **[?]** unknown

## Component boundaries and dependency direction
- [V] Three-layer project graph: `CampertronConsole` and `CampertronGUI` → `CampertronLibrary`. The library has no reference back to either host.
- [V] Inside the library there is no layering enforcement. `function/Base/Menu.cs` (UI) calls `RecDotOrg.data.Load` and `RecDotOrg.api.RefreshRidbRecreationData`. `Load` calls back into `Menu.Init`. `Cache` (data) writes to the console via `CampsiteConfig.WriteToConsole`. So UI, orchestration and data access are mutually dependent.
- [V] No interfaces, no DI container, all static classes. The only instantiated service-like type is `RecreationDotOrgContext` (EF `DbContext`), which every query creates with `new RecreationDotOrgContext(configPath)`.
- [V] Models in `CampertronLibrary/class/**` are declared in the global namespace (no `namespace` statement in `Config.cs`, `ConsoleConfig.cs`, `Availability.cs`, `Generic.cs`, etc.).

## Runtime modes
Determined in `Load.GetConfig()` (`CampertronLibrary/function/RecDotOrg/data/Load.cs`):

| Mode | Detection [V] | ConfigPath | CachePath |
|---|---|---|---|
| Container | env `DOTNET_RUNNING_IN_CONTAINER` or `DOTNET_RUNNING_IN_CONTAINERS` is non-null | `/config` (throws `"Config path does not exist"` if missing) | `/cache` (throws `"Cache path does not exist"` if missing) |
| Desktop | otherwise | `Environment.SpecialFolder.LocalApplicationData` + `/CampertronConfig` (created if missing) | `LocalApplicationData/CampertronCache` (created if missing) |

`OutputType` (`GeneralConfig.OutputTo`) then selects the behavior: `Console`, `Email`, `HtmlFile`, or `UnitTest`.

- [V] Before either mode is chosen, `Program.Main` of the console and the GUI handles `--version` (`RuntimeInfo`): it prints the version and `RuntimeInformation.FrameworkDescription` and exits without calling `Load.GetConfig` (business-rules §11).
- [V] All versions share the data folder and DB file name. Versions before 0.1.0-beta.4 misread the current DB layout (business-rules §10).

## Console data flow (search)
```
Program.Main
 └─ Load.GetConfig()                         → CampertronInternalConfig {GeneralConfig, EmailConfig, paths, ContainerMode}
      ├─ Yaml.GeneralConfigGetConfig / EmailConfigGetConfig   (generate samples if missing)
      └─ Load.DbExistsCheck → extract embedded DB, or auto-refresh RIDB (failure-safe: staging DB + BackupDatabase, D26)
 └─ Load.RunConsoleSearch(cfg)  → RunConsoleSearch(cfg, ForceSearch:false)
      ├─ if OutputTo==Email or ForceSearch → search loop (below)
      └─ else Menu.Init(cfg) → interactive menu (Search → RunConsoleSearch(cfg, true))

search loop (while(true)):
  Load.Preload
    ├─ Yaml.CampertronConfigGetConfigs(ConfigPath)  (every *.yaml except general/email with autoRun:true)
    └─ Parallel per unique CampgroundID: Cache.PreCheckCache  (SQLite → JSON cache files)
  Parallel.ForEach config:
    AvailabilityApi.GetAvailabilitiesByCampground
      ├─ Cache.CheckCache (campsite list + header lines)
      ├─ for each month 0..GetMonthsToCheck() that contains search dates:
      │    ProcessMonth → GET recreation.gov monthly availability (deduped via RunData.Urls / SiteData)
      │      → CampsitesDataEntry.GenerateDates → join with cached campsites
      │      → campsite-type include/exclude → FilterData (all other rules, Camp.History check)
      └─ ConsecutiveDays > 1 → FilterConsecutiveDays
  Load.PostProcess
    ├─ HasNewEntries (creates Camp.History marker dirs unless OutputTo==Console)
    ├─ CampsiteConfig.ProcessConsoleConfig per config item (console / HTML file / email)
    ├─ clear RunData collections
    └─ sleep 60 s (Email or container) | "Press any key" → Menu.Init (desktop Console/HtmlFile) | return (UnitTest)
```
- [V] `RunningData` (`class/Base/ConsoleConfig.cs`) is the shared state between parallel tasks: `AllConsoleConfigItems`, `SiteData`, `Urls`, `UniqueCampgroundIds`, `CampertronConfigFiles`, `FilteredAvailableData`.
- [V] Output is built as a list of `ConsoleConfig.ConsoleConfigValue` (`ConfigType` = `WriteLine`/`Write`/`WriteLineColor`/`WriteColor`/`WriteEmptyLine`), then rendered by `CampsiteConfig.ProcessConsoleConfig` (`function/Base/Console.cs`).
- [V] Desktop interactive flow recurses: `PostProcess` → `Menu.Init` → `WriteMenu` (infinite `while(true)` reading keys) → `ProcessInput` → `RunConsoleSearch` → `PostProcess` → `Menu.Init`, and so on. Nothing returns.

## GUI data flow
- [V] `MainWindow` constructor (`CampertronGUI/Views/MainWindow.axaml.cs`) calls `Load.GetConfig()`, which has the same side effects as the console: it creates dirs, writes sample YAML, extracts the DB, and may auto-refresh. The result is assigned to a local and not used further.
- [V] `ReactiveViewModel` computes its lists in property getters, each with a fresh SQLite query through `Read.*`. Its path comes from `GetConfigPath`, which is hard-coded to `LocalApplicationData/CampertronConfig` (it does not use `CampertronInternalConfig.ConfigPath`).
- [V] Property-change chaining is done with `WhenAnyValue(...).Subscribe(RaisePropertyChanged(...))` in the constructor.

## Configuration
| File (in ConfigPath) | Type | Loader [V] | Keys |
|---|---|---|---|
| `General.yaml` | `GeneralConfig` | `Yaml.GeneralConfigGetConfig` | `outputTo`, `refreshRidbDataDayInterval`, `lastRidbDataRefresh`, `autoRefresh` |
| `Email.yaml` | `EmailConfig` | `Yaml.EmailConfigGetConfig` | `smtpServer`, `smtpPort`, `smtpUsername`, `smtpPassword`, `sendToAddressList`, `sendFromAddress` |
| any other `*.yaml` | `CampertronConfig` | `Yaml.CampertronConfigGetConfigs` | see `business-rules.md` §Search config |

- [V] Missing `General.yaml` or `Email.yaml` → sample written (`GeneralConfigGenerateSampleConfig`: `OutputTo=Console`, `RefreshRidbDataDayInterval=30`, `AutoRefresh=false`, `LastRidbDataRefresh=UtcNow-50y`; `EmailConfigGenerateSampleConfig`: empty strings, port 0).
- [V] SMTP password is stored in plain text in `Email.yaml`.
- [V] No environment-variable or command-line configuration other than the container detection variables.

## Persistence
| Store | Location | Written by [V] | Read by [V] |
|---|---|---|---|
| SQLite `RecreationDotOrg.db` | `ConfigPath` | `Load.DbExistsCheck` (extract), `LegacyAddressLayout.RepairIfNeeded` (one-time replacement of a 2024-layout DB or a pre-0.1.0-beta.4 DB that still has the omitted tables; business-rules §8), `RidbStagedRefresh.BuildValidateAndInstall` (RIDB refresh: one `BackupDatabase` transaction from a validated staging DB; decision D26, business-rules §8) | `Read.*`, `Cache.Cache*ByCampsite` |
| Refresh staging DB `RecreationDotOrg.refresh-<guid N>/RecreationDotOrg.db` (+ its `-wal`/`-shm`), built by `Clear.ClearDatabase` and `PopulateDbFromRIDBFiles.Populate` (rows written by `RidbTableInserter`: one transaction, prepared commands, EF Core's row order; decision D25); lock files `RecreationDotOrg.refresh.lock` (kept) and `RecreationDotOrg.repair.lock` (also taken during the installation) | `ConfigPath` | `RidbStagedRefresh` (the staging directory is removed after each refresh; one left by a crash is removed by the next refresh) | `RidbDatabase.Validate`, the installation |
| Replaced-DB backup `RecreationDotOrg.db.legacy-layout-<yyyyMMddHHmmss>.bak` or `RecreationDotOrg.db.pre-0.1.0-beta.4-<yyyyMMddHHmmss>.bak` (+ `-wal`/`-shm` if present); transient `RecreationDotOrg.repair.lock` and `RecreationDotOrg.repair-<guid>/` | `ConfigPath` | `LegacyAddressLayout.RepairIfNeeded` | nothing (the user may delete the backup) |
| JSON cache: `{CampgroundID}-CampInfo.json`, `-Campsites.json`, `-CampsitesByPark.json`, `-Cached.json`, `{CampsiteID}-Equipment.json`, `{CampsiteID}-Attributes.json` (deleted by a RIDB refresh just before its installation, `Clear.ClearRefreshCache`, and, after a successful startup DB replacement, by `LegacyAddressLayout.ClearDerivedCache`) | `CachePath` | `Cache.CheckCache`, `Cache.PreCheckCache`, `Cache.GenerateCacheForCampground` | `Cache.CheckCache`, `Cache.Get*ByCampsite` |
| Duplicate-notification markers: directories `Camp.History/{yyyy}/{M}/{d}/{CampsiteID}` | `ConfigPath` | `Load.HasNewEntries` | `AvailabilityApi.FilterData` |
| HTML results `{guid}.html` (`HtmlFile` and `Email` output; the email attaches this file) | `CachePath` (since the TD-05 fix; before it, always `LocalApplicationData/CampertronCache`, which in the container image was `/app/CampertronCache` in the container layer) | `CampsiteConfig.WriteHtmlResults` (called by `ProcessConsoleConfig`, which deletes the previous `*.html` directly in `CachePath` first) | browser / email attachment (`CampsiteConfig.CreateResultsEmail`) |
| YAML configs | `ConfigPath` | `Yaml.*ConvertToYaml` (in place); the RIDB refresh timestamp by `Yaml.GeneralConfigReplaceYaml` (temporary `General.yaml.<guid N>.tmp`, validated, then renamed over `General.yaml`; `ConfigFileReplace`, TD-37) | `Yaml.*ConvertFromYaml` (temporary files are never read) |

- [V] The DB holds only the 6 RIDB tables Campertron reads (business-rules §8). It ships Brotli-compressed as the embedded resource `content/RecreationDotOrg.db.br` and is decompressed with the built-in `BrotliStream`; there is no third-party compression package.
- [V] The schema is created by shipping a pre-built DB, not by running migrations. `Migrations/` exists, but no `Migrate()` or `EnsureCreated()` call exists (grep).
- [V] `CampertronInternalConfig.CachePath` is the only source of the cache directory at run time (TD-05 resolved, decision D27): the JSON cache, the HTML results and the refresh's cache step all use it. The refresh's cache step (`Clear.ClearRefreshCache(InternalConfig.CachePath)`, formerly inside `Clear.All`) deletes every file directly in `CachePath`. Since D26 it runs after the staging DB passed validation and before the installation; if it fails, nothing is installed. `Clear.CheckCacheDirectory` refuses an unset path, a filesystem root or `ConfigPath` before the refresh downloads anything, and `ClearRefreshCache` also refuses a directory that holds `RecreationDotOrg.db` or `General.yaml`.
- [V] Still in `LocalApplicationData` by design (not cache, not changed by TD-05): the RIDB download `RIDBFullExport_V1_JSON.zip` (`Web.Download`) and the `Extract` directory (`Compression.Unzip`), which both are removed again during a refresh; in the container image `LocalApplicationData` is `""` (no `/home/app/.local/share`), so they are relative to `/app`. The GUI's `ReactiveViewModel.GetConfigPath` and `MainWindow` build `LocalApplicationData/CampertronConfig` themselves (TD-23).
- [V] The cache never expires. `Cache.CacheExist` checks only for `{CampgroundID}-Cached.json`. It is cleared only by a RIDB refresh (before its installation) and by a successful startup DB replacement.

## External integrations
| Integration | Endpoint [V] | Code |
|---|---|---|
| recreation.gov availability | `https://www.recreation.gov/api/camps/availability/campground/{id}/month?start_date={yyyy}-{MM}-01T00%3A00%3A00.000Z` | `AvailabilityApi.ProcessMonth`, `GetWebApiData` |
| RIDB bulk export | `https://ridb.recreation.gov/downloads/RIDBFullExport_V1_JSON.zip` | `RefreshRidbRecreationData.RefreshData` → `Web.Download` |
| SMTP | `EmailConfig.SmtpServer:SmtpPort`, `EnableSsl=true`, `NetworkCredential` | `CampsiteConfig.ProcessConsoleConfig` (`System.Net.Mail.SmtpClient`) |
| Campsite links in output | `https://www.recreation.gov/camping/campsites/{CampsiteID}` | `AvailabilityApi.FilterData` |
| OS shell | opens HTML file with `UseShellExecute=true` | `CampsiteConfig.ProcessConsoleConfig` |

- [V] No API key and no custom headers are sent to recreation.gov. A new `HttpClient` is created per request (`GetWebApiData`, `Web.Download`).

## Concurrency
- [V] `Parallel.ForEach` over unique campground IDs (`Load.Preload`), over campground configs (`Load.RunConsoleSearch`), and over campsite IDs (`Cache.GenerateCacheForCampground`).
- [V] Monthly URL dedup: the first thread to `RunData.Urls.TryAdd(Url)` fetches. The others busy-wait in 500 ms steps on `RunData.SiteData` (`AvailabilityApi.ProcessMonth`).
- [I] `RunData` is passed by `ref` into parallel lambdas, and `FilterConsecutiveDays` reassigns `RunData.FilteredAvailableData`. See `technical-debt.md` TD-04.
