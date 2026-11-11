# Console feature inventory

Legend: **[V]** verified by tracing source · **[I]** inference · **[?]** unknown

The console has **one command-line argument, `--version`** (F13) [V]. Any other arguments are ignored (`CampertronConsole/Program.cs`). Its user-facing surface is:
- the interactive menu, in desktop mode with `outputTo` other than `Email`
- the headless loop, when `outputTo: Email`
- `--version`

There are **no validator classes** in the repository [V]. "Validation" below means inline checks in the code.

Test names refer to `CampertronUnitTests/function/RecDotOrg/data/LoadUnitTests.cs` (`LoadTests.*`) and `DataUnitTests.cs` (`DataTests.*`).

---

## F1. Startup / first-run bootstrap
- **Entry:** `Program.Main` → `Load.GetConfig()` (`CampertronLibrary/function/RecDotOrg/data/Load.cs`) [V]
- **Behavior [V]:**
  - Picks container or desktop paths.
  - Writes sample `General.yaml` and `Email.yaml` if they are missing (`Yaml.GeneralConfigGenerateSampleConfig`, `Yaml.EmailConfigGenerateSampleConfig`).
  - `Load.DbExistsCheck`: if `RecreationDotOrg.db` is missing, `Load.ExtractEmbeddedDbTo` decompresses the embedded `content/RecreationDotOrg.db.br` with `System.IO.Compression.BrotliStream`. It writes to `RecreationDotOrg.db.partial-<guid>` and moves that into place only when complete, so an interrupted extraction leaves no partial DB. If the DB exists, `AutoRefresh` is true and `LastRidbDataRefresh` is older than `RefreshRidbDataDayInterval` days, it saves `General.yaml` with the new timestamp **before** calling `RefreshRidbRecreationData.RefreshData`. If the DB exists, `LegacyAddressLayout.RepairIfNeeded` first runs a one-time repair. It replaces the DB with the bundled one if the DB has the swapped 2024 layout, or has the corrected layout but still contains the 15 omitted RIDB tables (a pre-0.1.0-beta.4 DB). The bundled DB must pass `RidbDatabase.Validate`. The old DB is kept as a `.legacy-layout-<timestamp>.bak` or `.pre-0.1.0-beta.4-<timestamp>.bak` file, and the cache files derived from it are deleted (business-rules §8).
- **Models:** `CampertronInternalConfig`, `GeneralConfig`, `EmailConfig` (`class/Base/Config.cs`)
- **Validation [V]:** in a container, throws `Exception("Config path does not exist")` or `Exception("Cache path does not exist")`.
- **Logging [V]:** none on the success path. An auto-refresh prints the RefreshData messages (F6).
- **Dependencies:** filesystem, embedded resource, YamlDotNet
- **Tests [V]:** `LoadTests.GetConfigTest` (not null), `DbExistsTest`, `DbExistsCheckTest` (returns true; always true by construction). Every `LoadTests` test also exercises it through the field initializer `Load.Preload(Load.GetConfig())`.

## F2. Interactive menu
- **Entry:** `Load.RunConsoleSearch(cfg, false)` → `Menu.Init(cfg)` when `OutputTo != Email` (`function/Base/Menu.cs`) [V]
- **Behavior [V]:**
  - Clears the screen with escape codes, sets the console title to "🤖 CAMPERTRON 🤖", prints an ASCII logo, and shows the options `Search`, `Refresh Ridb Data (slow)`, `View Config`, `Exit`.
  - Up and Down arrows move the selection and Enter selects. Up from Search and Down from Exit stay in place.
  - `MenuLocation` enum: `Search`, `Refresh`, `ViewConfig`, `Exit`.
  - In a container, `WriteMenu` prints "Unsupported output type in container" and calls `Environment.Exit(1)`.
- **Validation:** none.
- **Logging:** console only.
- **Tests [V]:** none. `OutputType.UnitTest` makes `Menu.Init` skip all drawing.

## F3. Search, console output (`outputTo: Console`)
- **Entry:** menu Search → `Load.RunConsoleSearch(cfg, true)` [V]
- **Pipeline [V]:**
  - `Load.Preload`
  - `AvailabilityApi.GetAvailabilitiesByCampground` (per config, in parallel), then `ProcessMonth`, `FilterData` and, if `ConsecutiveDays > 1`, `FilterConsecutiveDays`
  - `Load.PostProcess`, then `CampsiteConfig.ProcessConsoleConfig`, which writes colored console output
- **Output [V]:**
  - A per-campground header (`Cache.WriteProgress`)
  - A "Searching for entries during {Month}/{Year} 🔎" line per month (only when `ConsecutiveDays == 1`)
  - For each hit, the date and weekday, check-in and check-out, site name, loop and type, equipment, and the URL
  - A month calendar with hit days in blue (`Calendar.GenerateCalendar`, Console output only)
  - "No entries found on {M}/{yyyy}" when a month has no hits
- After rendering, it shows "Press any key to continue..." and returns to the menu (`PostProcess`) [V].
- **Services:** `Yaml`, `Cache`, `AvailabilityApi`, `Data`, `Calendar`, `CampsiteConfig`, `Read`
- **Models:** `CampertronConfig`, `RunningData`, `AvailabilityEntries`, `CampsitesDataEntry`, `AvailabilityData`, `AvailableData`, `CampsitesRecdata`, `ConsoleConfigValue`
- **Validation / rules:** see `business-rules.md`.
- **Logging [V]:**
  - "Retrieving availability for campground ID:…" and "Finished retrieving campground ID:… in N seconds" (`Load.RunConsoleSearch`)
  - "Get: {url}" (`GetWebApiData`)
  - "Generating cache for …" (`Cache`)
  - Heavy-traffic and blocked messages
- **Tests [V]:** `LoadTests.RunConsoleSearchTest` calls `RunConsoleSearch(config, true)` with `OutputType.UnitTest`. This runs the real search path and hits the network. `LoadTests.PreloadTest` and `LoadTests.PostProcessTest` cover the pieces. There are **no** tests of the filter rules, date generation, the calendar or consecutive days.

## F4. Search, HTML file output (`outputTo: HtmlFile`)
- **Entry:** same as F3 (via the menu) [V]
- **Behavior [V]:** `ProcessConsoleConfig` builds HTML (`<br />` line breaks, no colors). It deletes all `*.html` files in `LocalApplicationData/CampertronCache`, writes `{guid}.html` and opens it with the OS shell. This runs **once per campground config item** inside `PostProcess`. `HasNewEntries` creates `Camp.History` markers, because the output type is not `Console`.
- **Tests:** none.

## F5. Headless email notification loop (`outputTo: Email`)
- **Entry:** `Program.Main` → `RunConsoleSearch(cfg)` → `RunConsoleSearch(cfg, false)`, which goes straight into the search loop because `OutputTo == Email` [V]
- **Behavior [V]:**
  - Runs an endless loop of search → `PostProcess` → "Sleeping for 1 minute" → `Thread.Sleep(60000)`.
  - Email is sent only when `HasNewEntries` is true and `FilteredAvailableData.Count() > 0`.
  - It sends **one email per campground config item**, with the subject "Campertron Auto-Email", an HTML body and the HTML file attached. Then it prints "Sent email".
- **Services:** as F3, plus `System.Net.Mail.SmtpClient`
- **Models:** `EmailConfig`
- **Validation:** none. Empty sample values would make `new MailAddress("")` throw [I: .NET `MailAddress` rejects empty strings].
- **Dependencies:** SMTP server
- **Tests:** none.

## F6. Refresh RIDB data
- **Entry:** menu "Refresh Ridb Data (slow)" → `RefreshRidbRecreationData.RefreshData(false, cfg)`. It can also run automatically from `Load.DbExistsCheck` (F1) [V].
- **Behavior [V]:**
  - Downloads `RIDBFullExport_V1_JSON.zip` to `LocalApplicationData` (`Web.Download`).
  - Unzips it to `LocalApplicationData/Extract` and deletes the zip (`Compression.Unzip`).
  - `Clear.All` → `Clear.ClearDatabase` deletes every row from the 6 imported tables, drops the 15 omitted tables if present and runs `VACUUM`. `Clear.All` then deletes all files in `LocalApplicationData/CampertronCache`.
  - `PopulateDbFromRIDBFiles.Populate` loads the 6 `*_API_v1.json` files Campertron uses (CampsiteAttributes, Campsites, Facilities, FacilityAddresses, PermittedEquipment, RecAreas); the other 15 are ignored (business-rules §8), normalizes FacilityAddresses (`AddressStateCode` trimmed, uppercased and mapped to a state abbreviation via `Convert.StateNameToAbbreviation`; `PostalCode` stored as received; `City` trimmed and uppercased), calls a single `SaveChanges()` and deletes the extract dir.
  - Updates `LastRidbDataRefresh` and saves `General.yaml`.
  - Prints "Validating database" and runs `RidbDatabase.Validate` (business-rules §8). On failure it prints the problems in red, waits for a key in an interactive console, and returns `false`.
- **Models:** the 6 mapped `class/RecDotOrg` types, `RecreationDotOrgContext`
- **Logging [V]:** "Downloading…", "Extracting…", "Clearing Database", "Clearing database tables", "Clearing cache", "Populating Database from…", "Processing {file}" for each file, "Saving changes to DB...Please wait"
- **Error handling [V]:** none during download and import; exceptions propagate. The result is validated afterwards.
- **Tests:** `RidbDatabaseTests` (validation and `Clear.ClearDatabase`), `AddressStateTests` (import of the 6 files), `EmbeddedDatabaseTests` (the shipped DB passes the validation). There is no test of the download itself.

## F7. View Config
- **Entry:** menu "View Config" → `Menu.ProcessInput`, case `MenuLocation.ViewConfig` [V]
- **Behavior [V]:** prints the Config path, the Cache path, "RIDB refresh => {days} days ago" (from `LastRidbDataRefresh`), the OS description and architecture, and the framework description. Waits for a key.
- **Tests:** none.

## F8. Exit
- **Entry:** menu "Exit" → clears the screen and calls `Environment.Exit(0)` [V].

## F9. Container mode
- **Entry:** `Load.GetConfig` container branch [V]
- **Behavior [V]:**
  - Uses `/config` and `/cache`.
  - Sets `ContainerMode = true`.
  - Assigns `GeneralConfig.AutoRefresh = false`, but `GeneralConfig` is then reloaded from YAML a few lines later, so the override is lost (`Load.cs` lines 148–150 vs 180). See TD-03.
  - Only `outputTo: Email` works. Any other value reaches `Menu.WriteMenu`, which prints "Unsupported output type in container" and calls `Environment.Exit(1)`.
  - The `PostProcess` container branch ("Sleeping for 1 minute" for non-Email output) is therefore unreachable [I: by the above control flow].
- **Tests:** none.

## F10. Search config discovery and sample generation
- **Entry:** `Yaml.CampertronConfigGetConfigs` (called by `Load.Preload`) [V]
- **Behavior [V]:**
  - Reads every top-level `*.yaml` in `ConfigPath` except `general.yaml` and `email.yaml` (compared after `ToLower()`).
  - Keeps only `autoRun: true` and calls `GenerateSearchData()` on each.
  - If none qualify, prints "No config files found, generating sample at …/ZionConfig.yaml" and writes or **overwrites** `ZionConfig.yaml` (campground `232445`, `DaysOut` 7, 2 humans, tent equipment, `Shade:Yes` and `Campfire Allowed:Yes`, excluding `GROUP`).
- **Tests:** indirect, through `PreloadTest`.

## F11. Duplicate-notification suppression
- **Entry:** `Load.HasNewEntries` + `AvailabilityApi.FilterData` [V]
- **Behavior:** see `business-rules.md` §Duplicate suppression.
- **Tests [V]:** `LoadTests.HasNewEntriesTest`, which uses an empty list and expects false.

## F12. Availability JSON parsing
- **Entry:** `Data.DynamicDeserialize` (`function/RecDotOrg/data/Data.cs`) [V]
- **Tests [V]:** `DataTests.LOAD_DbExists` deserializes a fixture but asserts only `Assert.That(true)`, so it verifies only that no exception was thrown.

---

## F13. `--version`
- **Entry:** `Program.Main` (console and GUI) → `RuntimeInfo.IsVersionRequest(args)` → `RuntimeInfo.Describe` (`CampertronLibrary/function/Base/RuntimeInfo.cs`) [V]
- **Behavior [V]:** for exactly the one argument `--version`, prints `<app> <informational version>` and `Framework => <FrameworkDescription> (<arch>)` and exits 0, before any configuration, DB, cache or window is touched. Rules in business-rules §11.
- **Purpose:** identifies which build a copy is and which .NET runtime it runs on (business-rules §10), and is used by the release smoke tests and the container check (build-and-test.md §Release workflow).
- **Tests [V]:** `RuntimeInfoTests` (argument matching, output format, tests run on .NET 10). The published apps are checked by the release workflow's smoke and docker jobs.

## Test coverage summary [V]
Last run 2026-11-11: **65/65 passed** (details in build-and-test.md §J).

| Test | Target | Notes |
|---|---|---|
| `LoadTests.DbExistsTest` | `Load.DbExists` | depends on real user dirs |
| `LoadTests.GetConfigTest` | `Load.GetConfig` | |
| `LoadTests.HasNewEntriesTest` | `Load.HasNewEntries` | empty input |
| `LoadTests.DbExistsCheckTest` | `Load.DbExistsCheck` | always returns true |
| `LoadTests.PostProcessTest` | `Load.PostProcess` | UnitTest mode |
| `LoadTests.PreloadTest` | `Load.Preload` | builds caches from DB |
| `LoadTests.RunConsoleSearchTest` | `Load.RunConsoleSearch(cfg,true)` | **network**, real search |
| `LoadTests.RunConsoleSearchTest2` | `Load.RunConsoleSearch(cfg)` | with UnitTest output this goes to `Menu.Init`, which does nothing, and the wrapper returns `true` unconditionally, so the test cannot fail on search logic |
| `DataTests.LOAD_DbExists` | `Data.DynamicDeserialize` | trivial assertion |
| `AddressStateTests.UniqueStates_ReturnsStateCodesNotPostalCodes` | `Read.UniqueStates` | own temp DB (EF `EnsureCreated`); no user dirs, no network |
| `AddressStateTests.StateFiltering_UsesAddressStateCode` | `Read.UniqueCities`, `UniqueParks(state, city)`, `UniqueParksByState` | a ZIP code selects nothing |
| `AddressStateTests.CampgroundWithEmptyPostalCode_IsIncludedWhenStateIsPresent` | the 6 GUI `Read` list queries + unused `UniqueParks(path)` | empty `PostalCode`, state present |
| `AddressStateTests.Populate_Watchman_ResolvesUtahFromAddressStateCodeAndKeepsPostalCode` | `PopulateDbFromRIDBFiles.Populate` → `Read` | 21 fixture JSON files; Watchman `UT` / `84767` |
| `AddressStateTests.Populate_NormalizesFullStateNameInAddressStateCodeAndLeavesPostalCodeUnchanged` | `PopulateDbFromRIDBFiles.Populate` | `" Utah "` → `UT`; `84767-0001` kept |
| `LegacyAddressLayoutTests` (7 `Classify_*` tests) | `LegacyAddressLayout.Classify` | positive (legacy), negative (corrected, a few malformed rows) and ambiguous (mixed, too few rows, no ZIP codes) |
| `RidbDatabaseTests` (12) | `RidbDatabase.Validate`, `RidbDatabase.RequiredColumns`, `Clear.ClearDatabase` | valid DB passes; omitted table present, missing table, missing column, ZIP in state list, Watchman missing, empty, corrupt and missing file all fail; clearing drops all 15 omitted tables from a full DB and works on a slim DB |
| `EmbeddedDatabaseTests` (3) | `Load.ExtractEmbeddedDbTo` + the shipped `RecreationDotOrg.db.br` | the shipped DB passes `RidbDatabase.Validate`, is Corrected and has only known objects; no `.partial` file left; an existing destination is not overwritten; the GUI state queries on the shipped data return only two-letter states (no digits, no `01005`; AZ, MA, UT present), MA lists BARRE, `01005` lists no city or campground, Watchman under UT/SPRINGDALE |
| `RuntimeInfoTests` (3) | `RuntimeInfo.IsVersionRequest`, `RuntimeInfo.Describe` | only exactly `--version` is a version request; two-line output with the informational version and `FrameworkDescription`; the tests and the library run on and target .NET 10 |
| `LegacyAddressLayoutTests` (8 legacy `Repair_*` tests) | `LegacyAddressLayout.RepairIfNeeded` | replace + byte-identical backup, WAL-mode DB, no change for corrected/ambiguous/slightly malformed DBs, DBs with user objects, a legacy bundled DB, or a missing DB. Own temp dirs; the bundled DB is not used |
| `LegacyAddressLayoutTests` (17 pre-0.1.0-beta.4 tests) | `DetectRepairReason`, `PresentOmittedTables`, `GuiStateValues`, `NonNormalizedGuiStates`, `RepairIfNeeded`, `ClearDerivedCache` | a corrected-layout DB with the 15 omitted tables and `ARIZONA`/`UTAH`/`IN ` states is ObsoleteTables and is replaced. Afterwards `Validate` passes, states are two-letter only, AZ without ARIZONA, MA → BARRE, Watchman. A second start does nothing. Not replaced: the current-style DB, a newer refreshed DB, non-normalized states without omitted tables, DBs with user objects, an ambiguous layout with omitted tables, a bundled DB that fails validation or needs repair itself, and a failed extraction. User files in `ConfigPath` are untouched. Only derived cache files are removed (legacy and pre-beta.4 cases), and the cache is untouched when no repair happens. `GuiStateValues` equals `Read.UniqueStates` |
| `StartupRepairTests` (1) | `Load.DbExistsCheck` (the console/GUI startup path) with the real bundled DB | a pre-0.1.0-beta.4-style install (bundled DB plus omitted tables and ARIZONA) is repaired once; the second call does nothing |

There are no tests for: `AvailabilityApi` filtering, `CampertronConfig.GenerateSearchDates`/`GetMonthsOut`, `GetConsecutiveDays`, `Calendar`, `Yaml`, `Read` (other than the address/state queries above), `Cache`, `RefreshData`, email, the menu, or any GUI code.
