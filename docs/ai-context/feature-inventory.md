# Console feature inventory

Authority: current. This file is the current authority for Campertron's features: the console's F1 to F13 and the library and GUI features below, each with its entry points, behavior, models, validation and logging. Which tests cover a feature, what they check and how many there are is in `test-coverage.md` (since roadmap step 9, Phase 4).

Legend: **[V]** verified by tracing source · **[I]** inference · **[?]** unknown

The console has **one command-line argument, `--version`** (F13) [V]. Any other arguments are ignored (`CampertronConsole/Program.cs`). Its user-facing surface is:
- the interactive menu, in desktop mode with `outputTo` other than `Email`
- the headless loop, when `outputTo: Email`
- `--version`

Since D41 the settings files have a validation class, `ConfigValidation` (§Settings validation below, business-rules §15) [V]. Everywhere else "Validation" below means inline checks in the code.

Each feature's **Tests** entry points to `test-coverage.md` §Coverage by feature, the current authority for which tests cover the feature, what they check and how many there are.

---

## F1. Startup / first-run bootstrap
- **Entry:** `Program.Main` → `Load.GetConfig()` (`CampertronLibrary/function/RecDotOrg/data/Load.cs`) [V]
- **Behavior [V]:**
  - Picks container or desktop paths. On the desktop `LocalApplicationData` is created if it is missing (`Load.DesktopDataFolder`, `SpecialFolderOption.Create`), and a result without a full path stops the start; nothing is ever put into the working directory (since D37, TD-47).
  - Writes sample `General.yaml` and `Email.yaml` if they are missing (`Yaml.GeneralConfigGenerateSampleConfig`, `Yaml.EmailConfigGenerateSampleConfig`).
  - `Load.DbExistsCheck`: if `RecreationDotOrg.db` is missing, `Load.ExtractEmbeddedDbTo` decompresses the embedded `content/RecreationDotOrg.db.br` with `System.IO.Compression.BrotliStream`. It writes to `RecreationDotOrg.db.partial-<guid>` and moves that into place only when complete, so an interrupted extraction leaves no partial DB. If the DB exists, `AutoRefresh` is true and `LastRidbDataRefresh` is older than `RefreshRidbDataDayInterval` days, it calls `RefreshRidbRecreationData.RefreshData`, which writes the new timestamp to `General.yaml` only after a successful installation (D26, business-rules §9; before D26 the timestamp was saved before the refresh, so a failed refresh was not retried for the whole interval). If the DB exists, `LegacyAddressLayout.RepairIfNeeded` first runs a one-time repair. It replaces the DB with the bundled one if the DB has the swapped 2024 layout, or has the corrected layout but still contains the 15 omitted RIDB tables (a pre-0.1.0-beta.4 DB). The bundled DB must pass `RidbDatabase.Validate`. The old DB is kept as a `.legacy-layout-<timestamp>.bak` or `.pre-0.1.0-beta.4-<timestamp>.bak` file, and the cache files derived from it are deleted (business-rules §8).
- **Models:** `CampertronInternalConfig`, `GeneralConfig`, `EmailConfig` (`class/Base/Config.cs`)
- **Validation [V]:** in a container, throws `Exception("Config path does not exist")` or `Exception("Cache path does not exist")`. An empty `General.yaml` (or one with only comments) is reported as "General.yaml could not be used: The file has no settings: it is empty or contains only comments.", exit code 1 (since D37, TD-37; before, a `NullReferenceException`). On the desktop, no full `LocalApplicationData` path stops the start with an error instead of using the working directory (D37).
- **Logging [V]:** the library writes nothing on the success path. The console sets `Load.FirstStartNotice`, so a first start prints "Preparing campground data (first start)..." before the extraction (not for `outputTo: UnitTest`, and never in the GUI; D34). A startup repair prints its notice, which stays on the screen until a key is pressed before the menu is drawn. An auto-refresh prints the RefreshData messages (F6). A settings file that cannot be read ends the console with "Error: <file> could not be read: … (line N, column M)", the file's path and what to do, on standard error, exit code 1 (`ConsoleUi.ReportFailure`; before D34 an unhandled exception).
- **Dependencies:** filesystem, embedded resource, YamlDotNet
- **Tests:** `test-coverage.md` §Coverage by feature.

## F2. Interactive menu
- **Entry:** `Load.RunConsoleSearch(cfg, false)` → `Menu.Init(cfg)` when `OutputTo != Email` (`function/Base/Menu.cs`) [V]
- **Behavior [V]:**
  - Clears the visible screen (`ConsoleUi.ClearScreen`: `ESC[H ESC[2J`, never the scrollback; before D34 `\f ESC c ESC[3J`), hides the cursor, sets the console title to "🤖 CAMPERTRON 🤖", prints an ASCII logo, and shows the options `Search`, `Refresh Ridb Data (slow)`, `View Config`, `Exit`. Unread notices, warnings and errors are acknowledged with a key press first.
  - Up and Down arrows move the selection and Enter selects. Up from Search and Down from Exit stay in place; any other key changes nothing. After every key the options are drawn again by the loop in `Menu.WriteMenu` (since D47; before, `Menu.ProcessInput` called `WriteMenu` again for every key, so each key press went one level deeper until a stack overflow ended the console: TD-12). Each Search, Refresh and View Config still starts one nested menu (business-rules §12).
  - `MenuLocation` enum: `Search`, `Refresh`, `ViewConfig`, `Exit`.
  - In a container, `Menu.Init` writes "Error: Unsupported output type in container. The container sends results by email only: set outputTo: Email in /config/General.yaml." to standard error and calls `Environment.Exit(1)`.
  - Without a terminal on both standard input and output (redirected output, a pipe, no stdin), `Menu.Init` writes "Error: the Campertron menu needs an interactive terminal. For unattended use, set outputTo: Email in General.yaml." and calls `Environment.Exit(1)` (`Menu.StartProblem`; before D34 the menu crashed with an `ArgumentOutOfRangeException`).
  - The cursor and the color are restored on Exit, on a failure and on Ctrl+C.
- **Validation:** none.
- **Logging:** console only.
- **Tests:** `test-coverage.md` §Coverage by feature.

## F3. Search, console output (`outputTo: Console`)
- **Entry:** menu Search → `Load.RunConsoleSearch(cfg, true)` [V]
- **Pipeline [V]:**
  - `Load.Preload`
  - `AvailabilityApi.GetAvailabilitiesByCampground` (per config, in parallel), then `ProcessMonth`, `FilterData` and, if `ConsecutiveDays > 1`, `FilterConsecutiveDays`; the eight include and exclude lists through `SearchFilters` (OR semantics and the search's `filterMatching`, D51, business-rules §3)
  - `Load.PostProcess`, then `Load.OutputResults`, then `CampsiteConfig.WriteConsoleResults` once for the run
- **Output [V] (since D34, business-rules §12; rendered by `ConsoleResults` from the values' metadata):**
  - Before the search: "Running N searches:" with each search's name, campground, dates, weekdays, nights, people and number of filters, and a warning for each search that cannot find anything (`ConsoleResults.DescribeSearches`)
  - Per search: `== name ==`, the campground with its park and ID, "N openings at M sites on K dates" with the shared check-in/out and equipment, or "No matching availability."
  - Single nights: each date ("Fri 11/20/2026 - 2 sites") with its sites (name, loop and type, booking link), then the month's calendar with the open days marked `*`; a month without results inside a search that has results: "{Month Year}: no matching availability."
  - Consecutive nights: each campsite with its dates in date order and a calendar per month
  - "Done: N searches, X openings at Y sites." and the number of searches without availability
  - The output streams themselves are unchanged (header from `Cache.WriteProgress`, "Searching for entries during …", the hits, "No entries found on …", the calendar values for Console): the console no longer writes their text
- Every Search shows every current match: `Camp.History` is neither read nor written (business-rules §5, since branch `fix/manual-search-notification-history`; before, markers left by HtmlFile, UnitTest or Email runs hid matches) [V].
- After rendering, it shows "Press any key to return to the menu..." and returns to the menu (`PostProcess`) [V].
- **Services:** `Yaml`, `Cache`, `AvailabilityApi`, `Data`, `Calendar`, `CampsiteConfig`, `Read`
- **Models:** `CampertronConfig`, `RunningData`, `AvailabilityEntries`, `CampsitesDataEntry`, `AvailabilityData`, `AvailableData`, `CampsitesRecdata`, `ConsoleConfigValue`
- **Validation / rules:** see `business-rules.md`.
- **Logging [V] (since D34):**
  - "Checked "{search}" at {campground}: {months} ({seconds}s)" per finished search (`Load.CheckedText`; not in the Email loop). Before D34: "Retrieving availability for campground ID:…", "Finished retrieving campground ID:… in N seconds" and "Get: {url}" per request
  - "Preparing campsite details for {campground}..." the first time a campground is searched (`Cache`; before: "Generating cache for …")
  - Warnings on standard error for a failed request (with the attempt and the 30-second retry), heavy traffic (1-second retry) and an error for a blocked request (business-rules §7)
- **Tests:** `test-coverage.md` §Coverage by feature.

## F4. Search, HTML file output (`outputTo: HtmlFile`)
- **Entry:** same as F3 (via the menu) [V]
- **Behavior [V] (since beta.9, decision D29):** `Load.OutputResults` orders the run's searches by name and calls `CampsiteConfig.WriteAndOpenHtmlResults` **once per run**: `ResultsHtml.Render` builds one page with a section per search, `WriteHtmlResults` deletes all `*.html` files directly in `CachePath` (`CampertronInternalConfig.CachePath`; before the TD-05 fix always `LocalApplicationData/CampertronCache`, the same directory on the desktop) and writes `{guid}.html` there, and `OpenHtmlFile` opens it once with the OS shell. Since D34 the console then names the page ("Results page (opened in your browser): {path}") and writes the run's summary line. Before beta.9 this ran once per search, and each search's file deleted the previous one, sometimes before the browser had opened it (TD-06). The page: business-rules §6. Every Search shows every current match: `Camp.History` is neither read nor written (business-rules §5). Before branch `fix/manual-search-notification-history`, `HasNewEntries` created markers because the output type was not `Console`, and they hid those matches from every later manual Search.
- **Tests:** `test-coverage.md` §Coverage by feature.

## F5. Headless email notification loop (`outputTo: Email`)
- **Entry:** `Program.Main` → `RunConsoleSearch(cfg)` → `RunConsoleSearch(cfg, false)`, which goes straight into the search loop because `OutputTo == Email` [V]
- **Behavior [V]:**
  - Runs an endless loop of search → `PostProcess` → `Thread.Sleep(60000)`. The search files are read once, by `Preload` before the loop. Since D35 (TD-46) every check after the first regenerates the searches' dates from the current time (`Load.RegenerateSearchDates`), so `DaysOut` and `MonthsOut` windows roll forward while the process runs, without reading a file or repeating a warning; before, they stayed those of the start.
  - Console output (since D34, for logs): one timestamped start line (the program and version from `RuntimeInfo`, the .NET runtime, the settings and cache folders), the list of searches once, then per check one timestamped line: "Check N: 2 searches, nothing new. Next check HH:mm:ss." or "Check N: 2 searches, 8 new openings; sending 2 emails.", followed by one "Email sent: {search} ({n} openings)" line per email and "Next check HH:mm:ss.". No screen clearing and no per-search progress. Before D34: ten untimed lines per check ("Retrieving…", "Get: …", "Finished…", "Sleeping for 1 minute") and `\f ESC c ESC[3J` after every sleep, also in logs.
  - Email is sent only when a hit of the run has no marker (`Load.HasUnreportedEntries`, which only reads) and `FilteredAvailableData.Count() > 0` (`Load.ReportResults`).
  - Only this loop uses `Camp.History` (business-rules §5): each email contains only the matches not reported before; a reported campsite and date stays suppressed in later cycles and after a restart, also if it disappears and comes back.
  - It sends **one email per campground config item**, with the subject "Campertron Auto-Email", an HTML body and the HTML file attached. Then it prints the "Email sent: …" line (before D34 "Sent email"). If sending fails, the exception ends the console with "Error: the email could not be sent: …" and exit code 1 (before D34 an unhandled exception). Since D36 (TD-14) the history follows delivery: each sent email records the openings it shows, the failed email's openings and those of the emails after it stay unrecorded, so the next start reports them again, and the run's remaining hits are recorded after every email of the check was sent; before D36 every marker of the check was written before the first send. `Camp.History` appears with the first marker (TD-43). Since beta.9 the body and the attachment are the `ResultsHtml` page of that one search (no navigation list); before, an unencoded `<br />` fragment.
- **Services:** as F3, plus `System.Net.Mail.SmtpClient`
- **Models:** `EmailConfig`
- **Validation [V]:** since D41 `Load.CheckEmailOutputSettings` checks `Email.yaml` before anything is searched (a server, a port from 1 to 65535, a valid sender, at least one valid recipient; business-rules §15), and an incomplete file ends the start with exit code 1. Before D41 nothing was checked and the sample's empty values failed at the first send (`new MailAddress("")` throws).
- **Dependencies:** SMTP server
- **Tests:** `test-coverage.md` §Coverage by feature.

## F6. Refresh RIDB data
- **Entry:** menu "Refresh Ridb Data (slow)" → `RefreshRidbRecreationData.RefreshData(false, cfg)`. It can also run automatically from `Load.DbExistsCheck` (F1) [V].
- **Behavior [V]** (failure-safe since branch `fix/ridb-atomic-refresh`, decision D26, business-rules §8):
  - Takes `ConfigPath/RecreationDotOrg.refresh.lock`; if another refresh holds it, prints "Another RIDB refresh is running …" and returns `false` without changing anything. Then removes stale `RecreationDotOrg.refresh-<guid>` staging directories.
  - Downloads `RIDBFullExport_V1_JSON.zip` to `LocalApplicationData` (`Web.Download`).
  - Unzips it to `LocalApplicationData/CampertronExtract`, a folder of Campertron's own that it clears first, and deletes the zip (`Compression.Unzip`; since D37, TD-48: before, the generic `LocalApplicationData/Extract`, deleted recursively, which could be another program's folder).
  - `RidbStagedRefresh.BuildValidateAndInstall` creates the staging DB `ConfigPath/RecreationDotOrg.refresh-<guid>/RecreationDotOrg.db` as a `BackupDatabase` copy of the installed DB (or the bundled DB if none is installed). Everything below up to the validation runs on that staging DB; the installed DB is only read.
  - `Clear.ClearDatabase` deletes every row from the 6 imported tables, drops the 15 omitted tables if present and runs `VACUUM`.
  - `PopulateDbFromRIDBFiles.Populate` loads the 6 `*_API_v1.json` files Campertron uses (CampsiteAttributes, Campsites, Facilities, FacilityAddresses, PermittedEquipment, RecAreas); the other 15 are ignored (business-rules §8), normalizes FacilityAddresses (`AddressStateCode` trimmed, uppercased and mapped to a state abbreviation via `Convert.StateNameToAbbreviation`; `PostalCode` stored as received; `City` trimmed and uppercased), calls a single `SaveChanges()` and deletes the extract dir (`CampertronExtract` since D37).
  - The rows are written by `RidbTableInserter` (decision D25): one transaction, one prepared parameterized `INSERT` per table, rows in EF Core's `SaveChanges` order (tables by ordinal name, rows by the complete primary key with `Comparer<T>.Default`, culture-sensitive for strings, TD-36). It replaced EF Core `Add`/`SaveChanges`; the database is byte-identical and `Populate` takes about 12.6 s instead of about 70 s, with a peak of about 2 GB instead of 6.1 GB (history/validation/a-w_2026-11-10_to_beta.10.md §M). A null row, a null key or a duplicate key fails the import and rolls back every insert; since the import runs on the staging DB, the installed DB is unchanged (before TD-36 part 1 was fixed, its tables stayed empty because `Clear.ClearDatabase` had already committed).
  - Prints "Validating database" and runs `RidbDatabase.Validate` on the staging DB (business-rules §8). On failure it prints the problems in red and "The installed database was not changed.", waits for a key in an interactive console, and returns `false`.
  - Checkpoints the staging DB, then deletes all files directly in the configured `CachePath` (`Clear.ClearRefreshCache`; `LocalApplicationData/CampertronCache` on the desktop, `/cache` in a container; TD-05 fixed, decision D27). This runs before the installation, so a crash can't leave the new DB with old cache files. An unset `CachePath`, a filesystem root or the config directory is refused before anything is downloaded (business-rules §8 step 6).
  - Installs it: `BackupDatabase` from the staging DB into the installed DB under `RecreationDotOrg.repair.lock`, one SQLite transaction (busy timeout 30 s). Then a best-effort checkpoint and `PRAGMA quick_check`, and removes the staging directory.
  - Only then updates `LastRidbDataRefresh` and saves `General.yaml` (once). If that write fails, it warns and still returns `true`. `Load.DbExistsCheck` no longer writes the timestamp before an auto-refresh.
- **Models:** the 6 mapped `class/RecDotOrg` types, `RecreationDotOrgContext`
- **Console [V] (D34):** the menu clears the screen before a manual refresh; `RefreshData` itself never clears (an automatic refresh can run at the start of the Email loop). The lock, validation and `quick_check` failures are one `Error` on standard error, the `General.yaml` warning a `Warning`; a successful refresh ends with the notice "The campground data was refreshed.", which stays until a key is pressed (before D34 the screen was cleared at once, also over the `General.yaml` warning). A failed download ends the console with "Error: a download failed: …" and exit code 1.
- **Logging [V]:** "Downloading…", "Extracting…", "Preparing a staging copy of the database", "Clearing Database", "Clearing database tables", "Populating Database from…", "Processing {file}" for each file, "Saving changes to DB...Please wait", "Validating database", "Clearing cache", "Installing the refreshed database", "Installed the refreshed database"; plus, when they apply, the lock, stale-staging, checkpoint and `General.yaml` messages of business-rules §8
- **Error handling [V]:** exceptions propagate, as before; the installed DB, `General.yaml` and (before the cache step) the cache are then unchanged. Validation failures, a held refresh lock and a failed `quick_check` return `false`.
- **Tests:** `test-coverage.md` §Coverage by feature.

## F7. View Config
- **Entry:** menu "View Config" → `Menu.ProcessInput`, case `MenuLocation.ViewConfig` [V]
- **Behavior [V] (since D34):** its own screen (cleared, logo), one `Label => value` line each (`Menu.ViewConfigLines`): `Version` (the first line of `--version`), `Config`, `Cache`, `Output`, `Searches` (the number of search files), `RIDB refresh` ("4 days ago (11/10/2026)", "today (…)", or "never (the campground list that came with Campertron is in use)" for the sample date of a new `General.yaml`), `OS`, `Framework` (the same line as `--version`). Then "Press any key to return to the menu..." and the menu with View Config still selected. Before D34 it was drawn over the menu with fixed cursor offsets (broken when a path wrapped) and showed the raw `TotalDays` value.
- **Tests:** `test-coverage.md` §Coverage by feature.

## F8. Exit
- **Entry:** menu "Exit" → clears the visible screen (the scrollback is kept since D34), restores the cursor and calls `Environment.Exit(0)` [V].

## F9. Container mode
- **Entry:** `Load.GetConfig` container branch [V]
- **Behavior [V]:**
  - Uses `/config` and `/cache`.
  - Sets `ContainerMode = true`.
  - Assigns `GeneralConfig.AutoRefresh = false` at the end of the container branch of `Load.GetConfig`, but `GeneralConfig` is then reloaded from YAML after that branch for both modes, so the override is lost (re-verified 2026-11-14; the line numbers recorded earlier are stale). See TD-03.
  - Only `outputTo: Email` works. Any other value reaches `Menu.Init`, which writes "Error: Unsupported output type in container. …" to standard error and calls `Environment.Exit(1)` (F2).
  - A missing `/config` or `/cache` ends with "Error: Config path does not exist" (or "Cache path …") and the mount advice on standard error, exit code 1 (before D34 an unhandled exception with exit code 139).
  - The `PostProcess` container branch ("Sleeping for 1 minute" for non-Email output) is therefore unreachable [I: by the above control flow].
- **Tests:** `test-coverage.md` §Coverage by feature.

## F10. Search config discovery and sample generation
- **Entry:** `Yaml.CampertronConfigGetConfigs` (called by `Load.Preload`) [V]
- **Behavior [V]:**
  - Reads every top-level `*.yaml` in `ConfigPath` except `General.yaml` and `Email.yaml` in any case (`ConfigFiles`, an ordinal case-insensitive comparison since D41; before, the path was lower-cased with the current culture, business-rules §15).
  - Keeps only `autoRun: true` and calls `GenerateSearchData()` on each.
  - With **no search file** at all, prints "No search files found; creating the example search …/ZionConfig.yaml" and writes and runs `ZionConfig.yaml` (campground `232445`, `DaysOut` 7, 2 humans, tent equipment, `Shade:Yes` and `Campfire Allowed:Yes`, excluding `GROUP`; since D51 `filterMatching: Exact` and excluding Watchman's exact group type `GROUP TENT ONLY AREA NONELECTRIC`, the same campsites); the next start uses that file as it is (unchanged).
  - With search files but **none enabled** (since D37, TD-13 resolved), leaves every file as it is, writes the warning "No search file has autoRun: true, so no search runs. Set autoRun: true in a search file to run that search." and returns no search: Console and UnitTest show "Done: 0 searches, no matching availability.", HtmlFile opens the empty results page, the Email loop checks 0 searches every minute (open question #40). Before D37 the example was written **over** a `ZionConfig.yaml` of the user's (its comments and values lost) and run (before D34 the message was always "No config files found, generating sample at …").
  - An empty search file (or one with only comments) is reported as "{file} could not be used: The file has no settings: it is empty or contains only comments.", exit code 1, and not replaced (since D37, TD-37; before, "Object reference not set …"). Equal `StartEndDate` dates are reported the same way, without a key wait (since D37, TD-45).
- **Tests:** `test-coverage.md` §Coverage by feature.

## F11. Duplicate-notification suppression
- **Entry:** `AvailabilityApi.FilterData` and the Email gate `Load.HasUnreportedEntries` (read); `Load.RecordNotificationHistory` from `Load.OutputResults` and `Load.ReportResults` (write, after delivery, D36); `Load.HasNewEntries` (public, kept, no longer used by the loop) [V]
- **Behavior:** `outputTo: Email` only; Console, HtmlFile and UnitTest never read or write `Camp.History` and create no directory. A marker is written only after the email that shows the opening was sent (TD-14), and the directory appears with the first marker (TD-43). See `business-rules.md` §5.
- **Tests:** `test-coverage.md` §Coverage by feature.

## F12. Availability JSON parsing
- **Entry:** `Data.DynamicDeserialize` (`function/RecDotOrg/data/Data.cs`) [V]
- **Tests:** `test-coverage.md` §Coverage by feature.

---

## F13. `--version`
- **Entry:** `Program.Main` (console and GUI) → `RuntimeInfo.IsVersionRequest(args)` → `RuntimeInfo.Describe` (`CampertronLibrary/function/Base/RuntimeInfo.cs`) [V]
- **Behavior [V]:** for exactly the one argument `--version`, prints `<app> <informational version>` and `Framework => <FrameworkDescription> (<arch>)` and exits 0, before any configuration, DB, cache or window is touched. Rules in business-rules §11.
- **Purpose:** identifies which build a copy is and which .NET runtime it runs on (business-rules §10), and is used by the release smoke tests and the container check (release-process.md §Release workflow).
- **Tests:** `test-coverage.md` §Coverage by feature.
- **GUI only, `--smoke-test` (R8, D33):** a release smoke test of the real GUI (console-to-gui-map.md §GUI-only features); `SmokeTestTests`.

## Settings file writes: the YAML document editor (D39) and the safe writers (D40)
- **Entry:** `YamlDocument.Analyze`, `Edit<T>` and `VerifyCandidate<T>` (`CampertronLibrary/function/Base/YamlDocument.cs`), internal [V]; `ConfigFileReplace.Read`, `Replace` and `Create` (`CampertronLibrary/function/Base/ConfigFileReplace.cs`), internal [V]. **The workflows that use them:** since D40 (CM2) a successful RIDB refresh (menu "Refresh Ridb Data" or auto-refresh) changes only `lastRidbDataRefresh` in `General.yaml`, keeping its comments and formatting, with a rolling `General.yaml.bak`; and every start that finds a sample file missing creates it atomically, never over an existing file. Since D43 and D44 the GUI's Settings and Searches tabs save `General.yaml`, `Email.yaml` and the search files with the same engine and writer (§GUI settings editing and §GUI search-file editing below).
- **User-visible changes (D40):** `General.yaml` keeps its comments, blank lines, order, quoting, line breaks and byte-order mark after a refresh (before, it was re-serialized); a `General.yaml.bak` appears next to it after the first refresh that changes it; a `General.yaml` that is not strict UTF-8, that the editor refuses or the loader rejects, or that another program changes during the write is left as it is, and the refresh shows its existing warning with a reason that quotes no value; the file's permissions and symbolic links are kept; the samples' content is unchanged.
- **Behavior:** the editor edits a settings document's text in memory, keeping comments and formatting outside the edited ranges, refusing what it can't edit safely, and checking every result with the settings loaders' deserializer against the intended model (business-rules §13); the writer reads strictly, writes through a verified temporary file and replaces or creates the file atomically (business-rules §14).
- **Tests:** `test-coverage.md` §Coverage by feature.

## Settings validation (D41)
- **Entry:** `ConfigValidation.ValidateGeneral`, `ValidateEmail`, `ValidateSearch` (a text), `ValidateFile`, `ValidateFolder` (`CampertronLibrary/function/Base/ConfigValidation.cs`), internal [V]; `ConfigFiles.Classify`/`SearchFiles`; the bounded loader `Yaml.ReadSettingsText`/`LoadSettingsText` behind every `Yaml.*ConvertFromYaml`; `Load.CheckEmailOutputSettings` in `Load.RunConsoleSearch`.
- **Behavior:** typed, value-free diagnostics for `General.yaml`, `Email.yaml`, each search file and the settings folder (stable codes, business-rules §15); the loaders bounded by the editor's limits (TD-52); the file classification ordinal and culture-independent; only settings that would take a dangerous path newly refused at start.
- **User-visible changes (D41):** `outputTo: Email` with an incomplete `Email.yaml` now stops before anything is searched with "Email.yaml could not be used: the Email output can't start: …", exit 1 (TD-51; before, it searched and failed at the first send with "unexpected problem: ArgumentException"); a settings file over a limit is "could not be used" with the limit named; a search with a negative or out-of-range `searchValue`, an empty equipment, attribute or campsite-type filter item or an undefined numeric `searchBy`, a `General.yaml` with an undefined numeric `outputTo`, or a non-finite or out-of-range refresh interval with `autoRefresh: true` is refused at start, naming the setting; in Turkish `EMAIL.yaml` is no longer read as a search file. Everything else loads and runs as before; the warnings are not printed (for the GUI, CM5).
- **Tests:** `test-coverage.md` §Coverage by feature.

## GUI campground lookup by FacilityID (D42)
- **Entry:** `ReactiveViewModel` (`CampgroundListByPark`, `CampgroundList`, `CampgroundListByState`, `ParkList`, the three `Selected*` campgrounds and details properties) → `Read.CampgroundChoicesByPark`, `CampgroundChoicesByStateAndCity`, `CampgroundChoicesByState`, `ParksWithCampgrounds`, `GetCampgroundDetailsById`; the model `CampgroundChoice` (`CampertronLibrary/class/RecDotOrg/CampgroundChoice.cs`); `MainWindow.axaml`'s `CampgroundItemTemplate` [V].
- **Behavior:** each campground list holds one entry per FacilityID; campgrounds with the same name stay separate, labeled with their location and ID; the details are those of the selected FacilityID; a blank name is "Unnamed campground"; By Park also lists a park's campgrounds without an address; a filter change clears its tab's campground (business-rules §16).
- **User-visible changes (D42):** every campground entry shows its location (when known) and "ID n"; the drop-down is as wide as its selector; selecting one of two campgrounds with the same name shows its own details (before, the first facility of that name); By Park lists 522 parks and 4,937 campgrounds of the shipped data instead of 463 and 3,330; lists are sorted case-insensitively. The GUI smoke test (`--smoke-test`) reports `parks=522` on the shipped data (it requires only a positive count).
- **Not changed:** the campsite search, the settings files and their writers, the console, the name-based `Read` methods; no picker or configuration editing (CM6, CM5).
- **Tests:** `test-coverage.md` §Coverage by feature.

## GUI settings editing (D43)
- **Entry:** the Settings tab: `SettingsViewModel` (`CampertronGUI/ViewModels/SettingsViewModel.cs`) → `GeneralSettingsViewModel`, `EmailSettingsViewModel` → `ConfigEditSession<T>` (`CampertronLibrary/function/Base/ConfigEditSession.cs`, internal) [V].
- **Behavior:** typed forms for `General.yaml` and `Email.yaml`; problems at their fields; a read-only preview of the lines a save changes with the password masked; Save only when edited, verified and without blocking problems; Revert, Reload, Create; external changes reload a clean form and open a conflict for an edited one; Open settings folder (business-rules §17).
- **User-visible changes (D43):** a fourth tab, Settings (since D50 Settings > Campertron, §GUI navigation and theme preference below). Saving keeps comments and formatting and leaves `<name>.bak`; `Email.yaml` created by the GUI is owner-only on Unix. The smoke test (`--smoke-test`) reports `settings-probe` and `settings-tab` lines.
- **Not changed:** search files, the search, the console, email sending, the start's sample creation, the lookup tabs.
- **Tests:** `test-coverage.md` §Coverage by feature.

## GUI search-file editing (D44)
- **Entry:** the Searches tab: `SearchesViewModel` (`CampertronGUI/ViewModels/SearchesViewModel.cs`) → `SearchFileViewModel` → `ConfigEditSession<CampertronConfig>`; the campground picker `CampgroundSettingField` with `CampgroundCatalog` → `Read.CampgroundPickerEntries`; New search `SearchFiles.Create`, Save As `ConfigEditSession.SaveAs` [V].
- **Behavior:** the console's search files (malformed ones listed read-only), each with its search, whether it runs, its campground and its state; a typed form for every setting of a search with its problems at fields and list rows; dates and filters as rows that keep their comments; a read-only preview; Save, Revert, Reload; conflicts for changes by another program; New search (the example, disabled) and Save As, both create-only with safe names; the campground chosen from every reservable, enabled campground or by its exact ID; the "no search runs" warning (business-rules §18). Nothing deletes a file, runs a search or shows availability.
- **User-visible changes (D44):** a fifth tab, Searches (since D50 the first top-level tab, §GUI navigation and theme preference below). Saving keeps comments and formatting and leaves `<name>.bak`; the 22 campgrounds no lookup tab lists (TD-53) can be chosen. The smoke test (`--smoke-test`) reports `search-probe` and `searches-tab` lines.
- **Not changed:** the search, its dates and filters, the console (it reads the same files), the Email loop (open question #40), the example search the start creates, the lookup tabs, the Settings tab (its recipients now use the shared list rows, with the same names and behavior).
- **Tests:** `test-coverage.md` §Coverage by feature.

## Host-neutral search service (D45)
- **Entry:** `CampsiteSearch.Plan(SearchRunRequest, Now)` and `CampsiteSearch.Run(SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?)` (`CampertronLibrary/function/RecDotOrg/search/CampsiteSearch.cs`, internal); the per-run `SearchHost` (`SearchHost.cs`); the model (`class/Base/SearchRunModel.cs`) [V]. No caller in the console; since GS2 (D46) the GUI's Results tab uses it (next section).
- **Behavior:** reads and checks chosen search files (also disabled ones), searches them with the console's own search (the same months, filters and consecutive nights) and returns immutable typed results: openings, sites with their runs of nights, months checked or not and why, per-search and run states, value-free issues and request counts; progress snapshots; cancellation at every stage; at most three requests per month; a block (the block page, HTTP 429) stops further requests without ending the process; no `Camp.History`, no Email settings, no console output, no file written (business-rules §19).
- **User-visible changes (D45):** none by itself (library only; the GUI's Results tab, D46, is its first user). For tests: the required suite no longer reaches recreation.gov (TD-01's network part).
- **Not changed:** the console, the Email loop, the HTML page, the emails, the console's retries and exits, the GUI.
- **Tests:** `test-coverage.md` §Coverage by feature.

## GUI campsite searches and results (D46)
- **Entry:** the Results tab: `ResultsViewModel` (`CampertronGUI/ViewModels/ResultsViewModel.cs`, with `ResultsSeams`) → `CampsiteSearch.LoadEntry` (the list), `CampsiteSearch.Plan` and `CampsiteSearch.Run` (GS1) through `ProgressGate`; the presentation in `ResultRows.cs` (`ResultsSearchRow`, `ResultSearchViewModel`, `ResultDateRow`, `ResultOpeningRow`, `ResultSiteRow`, `ResultStayRow`, `ResultText`, `BookingLink`); `Views/ResultsView.axaml(.cs)` [V].
- **Behavior:** the saved search files with a check box each (enabled selected, disabled listed "Not run at startup" and run only when selected without changing `autoRun`, files with errors or unsaved Searches-tab changes blocked with their reason); Start (the disk read again, the plan validating before the previous results are cleared, a background run), Cancel, throttled progress (stage, campgrounds, month requests, the files' states, elapsed time), typed results (status in words, counts, checked and unchecked months, issues, single nights by date, consecutive nights by campsite, a virtualized list), booking links and copies after a strict link check, a five-minute Start hold after a block, cancellation when the window closes (business-rules §20). No `Camp.History`, no Email settings, no file written, no console pipeline.
- **User-visible changes (D46):** a sixth tab, Results (since D50 the second top-level tab, §GUI navigation and theme preference below). The smoke test (`--smoke-test`) reports `results-tab` and `run-probe` lines. README describes the tab and shows it (`docs/ss9.png`: stand-in data until 2026-11-16, since then a real run of the example searches, maintainer-procedures.md §Refreshing the README screenshots).
- **Not changed:** the console and its menu Search (still only `autoRun: true` files), the Email loop, the HTML page, the Searches and Settings tabs (the Searches tab gained only a read-only query, `IsUnsaved`), the lookup tabs, GS1.
- **Tests:** `test-coverage.md` §Coverage by feature.

## GUI filter selectors (D51, D52)
- **Entry:** the Searches tab's editor, Campsite filters: the matching choice (`filterMatching`) and, since D52, the equipment and campsite-type selectors (`FilterSelectorField`, `FilterSelectorView`, `FilterChoiceSource`), since D53 the campsite selectors, since D54 the attribute selectors (all eight lists).
- **Behavior:** business-rules §23: the campground's values from the local database, read on a worker thread and kept per campground; checking adds one item at the end, unchecking removes every exact item; case variants grouped; values the campground lacks kept and described in the current mode; typed values as typed; include/exclude warning; one tab stop, arrows and Space; short lists whole, long ones with find and Select/Clear shown.
- **User-visible changes (D52):** the four lists are checklists of the chosen campground's values instead of text rows; the Matching choice; `--smoke-test` reports `filter-probe`.
- **Tests:** `test-coverage.md` §Coverage by feature.

## GUI navigation and theme preference (D50)
- **Entry:** `MainWindow.axaml(.cs)` (four top-level tabs, the nested `SettingsTabControl` and `CampsitesTabControl`, `ShownTabChanged`, the TD-39 workaround on both levels); `App.PrepareTheme` → `ThemePreferenceStore.Load` → `AppTheme.Apply`; Settings > System: `SystemSettingsViewModel` → `AppTheme.Apply` and `ThemePreferenceStore.Save` (`CampertronGUI/ThemePreference.cs`); `SystemSettingsView`.
- **Behavior:** the tabs Searches, Results, Settings (Campertron, System) and Campgrounds (By Park, By City, By State; labeled Campsites until D55), opening on Searches (read when the startup is ready) and each row on its first tab; every tab keeps its state; Settings > Campertron keeps CM5's checks and masking; the theme System (the default, following the operating system), Light or Dark, applied at once, saved in `LocalApplicationData/CampertronGUI/preferences.json`, applied at the next start before the window exists, System for anything unusable (business-rules §21).
- **User-visible changes (D50):** the new tab order and the two nested rows; Settings > System with the theme; the smoke test reports `navigation`, `theme-probe` and `system-tab`. README describes the tabs and the theme and shows Searches, Results, Settings > Campertron, Settings > System and Campgrounds > By Park (labeled Campsites until D55).
- **Not changed:** every tab's content and behavior (the lookup, CM5, CM6, GS2), the library, the console and its appearance, workflows, dependencies.
- **Tests:** `test-coverage.md` §Coverage by feature.

## Test coverage summary [V]
Moved to [`test-coverage.md`](test-coverage.md) by roadmap step 9, Phase 4 (current): the test projects, every test class with its discovered numbers of tests and cases, what each test checks, what skips on which platform and what is not covered. The counts this section gave are historical (`history/validation/`).
