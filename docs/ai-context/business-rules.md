# Business rules

Legend: **[V]** verified in source · **[I]** inference (usually standard .NET semantics applied to the code) · **[?]** unknown

These rules describe **current behavior**, including the quirks. Do not change them unless the task asks for it (see CLAUDE.md).

## 1. Search config (`CampertronConfig`, `CampertronLibrary/class/Base/Config.cs`)
YAML keys are camelCase (`Yaml.CampertronConfigConvertFromYaml` uses `CamelCaseNamingConvention`) [V].

| Key | Type | Rule [V] |
|---|---|---|
| `displayName` | string | Shown in the header (`Cache.WriteProgress`). Output is sorted by it (`PostProcess` `OrderBy(p => p.Name)`) |
| `autoRun` | bool | Only `true` configs are loaded (`Yaml.CampertronConfigGetConfigs`) |
| `campgroundID` | string | RIDB `FacilityID`. If null, the search for that config is skipped (`GetAvailabilitiesByCampground`) |
| `totalHumans` | int? | Must satisfy `min_num_people ≤ totalHumans ≤ max_num_people`. [I] Lifted comparisons return false if any side is null, so null excludes everything |
| `searchBy` | `SearchTypes` | `DaysOut`, `MonthsOut`, `SpecificDates`, `Until`, `StartEndDate` |
| `searchValue` | int | Used by `DaysOut` and `MonthsOut` |
| `searchValueDates` | string list | Used by `SpecificDates`, `Until`, `StartEndDate` |
| `showMonday`…`showSunday` | bool | Day-of-week filter (`CampertronConfig.ShowThisDay`, matched on English `DayOfWeek.ToString()`) |
| `includeEquipment`/`excludeEquipment` | string list | §3 |
| `includeSites`/`excludeSites` | string list | Exact `CampsiteName` match |
| `includeAttributes`/`excludeAttributes` | string list | Matched against `"AttributeName:AttributeValue"` strings |
| `includeCampsiteType`/`excludeCampsiteType` | string list | §3 |
| `consecutiveDays` | int | `1` = single nights. `>1` = runs of N consecutive nights per campsite. See §4 for other values |

## 2. Search date generation (`CampertronConfig.GenerateSearchDates`, `GetMonthsOut`)
| SearchBy | Dates produced [V] |
|---|---|
| `DaysOut` | today … today+`searchValue` days, inclusive |
| `MonthsOut` | every day from today … today+`searchValue` months, inclusive |
| `StartEndDate` | `searchValueDates[0]` … `[1]`, inclusive. If start == end: prints "Start date and End date cannot be the same", waits for a key, then `Environment.Exit(0)`. [I] Fewer than 2 entries → `ArgumentOutOfRangeException` from the list indexer |
| `SpecificDates` | Each parseable entry (`Convert.ToDateTime`; failures print "Error converting {x} to DateTime"). An empty list or zero parseable entries → today only. These dates are **not** normalized with `ConvertToZeroHour` |
| `Until` | today … `searchValueDates[0]`. If that date is ≤ now → today only. [I] Empty list → `ArgumentOutOfRangeException` |
| other | `default:` branch switches to `SpecificDates`. [I] Unreachable for valid enum values |

- [V] `ConvertToZeroHour` builds the string `"{M}/{d}/{yyyy} 0:00:00 AM"` and parses it with `Convert.ToDateTime`. `ProcessMonth` builds `CheckDt` the same way. [I] Both parse with the current culture, so they assume a US-style M/d date format.
- [V] `GetMonthsOut` returns the number of months from now to the last search date and caps it at 12, printing "Results limited to 12 months out".
- [V] Months are queried for `i = 0..GetMonthsToCheck()`, and a month is skipped unless it contains a search date (`HasDatesDuringThisMonth`).

## 3. Availability filtering (`AvailabilityApi.ProcessMonth` + `FilterData`)
Availability parsing:
- [V] `CampsitesDataEntry.GenerateDates`: a day counts as available only if the status string is exactly `"Available"`.
- [V] Day offsets assume the API's `availabilities` list covers the **last N days of the month**: `counter = DaysInMonth - availabilities.Count`.

Per day of the month, the campsites available that day are joined with the cached campsite list on `campsite_id == CampsiteID`. Then these rules apply, **in order** [V]:

1. **excludeCampsiteType** (if non-empty): keep the site only if `CampsiteType` contains **none** of the values (`All(y => !type.Contains(y))`, case-sensitive substring).
2. **includeCampsiteType** (if non-empty): keep the site only if `CampsiteType` contains **all** of the values (`All(y => type.Contains(y))`). Several values act as AND.
3. `FilterData`. Every condition must hold:
   - `totalHumans` within `[Minppl, Maxppl]`.
   - `CampsiteAvailableDate >= DateTime.Now`. Availability dates are local midnight, so **today is never matched** (except exactly at midnight) [I from the values].
   - The date is in `GetSearchDates()` (exact `DateTime` equality).
   - `ShowThisDay(date.DayOfWeek)`.
   - **includeEquipment**: passes if empty, or if some include value `s` and some permitted equipment `e` satisfy `s.Contains(e)`. Note the direction: the **config string contains the RIDB name**, ordinal and case-sensitive. A site with no permitted equipment fails a non-empty include.
   - **excludeEquipment**: passes if empty, or if **some** exclude value contains none of the site's equipment names. So the site is dropped only when **every** exclude value matches.
   - **includeSites** / **excludeSites**: exact `List.Contains(CampsiteName)`.
   - **includeAttributes** / **excludeAttributes**: same shapes as equipment, against `AttValuePairStr` (`"Name:Value"`, built by `Cache.ConvertAttributeValuePairToString`).
   - Duplicate check: `Directory.Exists(ConfigPath/Camp.History/{yyyy}/{M}/{d}/{CampsiteID})` must be false (§5).
4. Matches are added to `RunData.FilteredAvailableData` (`AvailableData`). Check-in and check-out are taken from the attributes `"Checkin Time"` and `"Checkout Time"`.

## 4. Consecutive days
- [V] `consecutiveDays == 1`: hits are written immediately, with a month header and a calendar (`Calendar.GenerateCalendar`, only when `OutputTo == Console`) or "No entries found on {M}/{yyyy}".
- [V] `consecutiveDays > 1`: `FilterConsecutiveDays` groups `RunData.FilteredAvailableData` by `CampsiteID`, keeps groups with at least N entries, and uses `EnumerableExtensions.GetConsecutiveDays(N)` (`Calendar.cs`) to keep only dates that belong to runs of N or more consecutive days. Each campsite is printed once, followed by its dates and a calendar (Console only).
- [V] `GetConsecutiveDays` throws `ArgumentNullException` for null input and `ArgumentException("can't compare against less than 2")` for limits below 2. It is only called when the limit is above 1.
- [V] `consecutiveDays <= 0`: neither the `== 1` output branch nor the `> 1` filter runs. Hits go into `FilteredAvailableData` (so they create markers and can trigger email), but nothing is written to the output list.

## 5. Duplicate-notification suppression (`Load.HasNewEntries`, `AvailabilityApi.FilterData`)
- [V] Markers are **directories** at `ConfigPath/Camp.History/{yyyy}/{M}/{d}/{CampsiteID}`, with `/` hard-coded as the separator.
- [V] `HasNewEntries` creates the markers for every filtered hit **unless `OutputTo == Console`**, so they are created for `Email`, `HtmlFile` and `UnitTest`.
- [V] `FilterData` drops any hit whose marker exists, for **every** output type, including Console.
- [V] Markers are created **before** output or email is attempted (`PostProcess` calls `HasNewEntries` first), so a failed email does not leave the hit eligible for the next run.
- [V] Markers are never deleted by any code.

## 6. Output and email gating (`Load.PostProcess`, `CampsiteConfig.ProcessConsoleConfig`)
- [V] Output runs if `OutputTo != Email`, or if (`Email` and `HasNewEntries` and `FilteredAvailableData.Count() > 0`).
- [V] `ProcessConsoleConfig` is called once per campground config item. For `Email` and `HtmlFile`, **each call** deletes the old `*.html`, writes a new file, and either sends an email or opens a browser.
- [V] Email settings: `SmtpClient` with `EnableSsl = true`, `DeliveryMethod = Network`, `UseDefaultCredentials = false`, `NetworkCredential(SmtpUsername, SmtpPassword)`, subject `"Campertron Auto-Email"`, `IsBodyHtml = true`, HTML file attached as `application/octet-stream`.
- [V] Consecutive `WriteEmptyLine` items collapse to one blank line (`LastConfigType` check).

## 7. Retry, back-off and termination (`AvailabilityApi.GetWebApiData`, `Data.DynamicDeserialize`)
| Condition | Behavior [V] |
|---|---|
| Any exception during fetch or parse | `catch` swallows it, then `Thread.Sleep(30000)` and retries **without limit** |
| Response body contains `REQUEST BLOCKED` (case-insensitive) | prints "Blocked by API for exceeding request limit, try again later", then `Thread.Sleep(300000)` and `Environment.Exit(0)` |
| Body contains "We are currently experiencing heavy traffic on our site" | prints "Heavy traffic...retrying in 1 second", then `Thread.Sleep(1000)` and a **recursive** re-fetch, without limit |
| Another thread is already fetching the same URL | polls `RunData.SiteData` every 500 ms **without limit** (`ProcessMonth`) |
| Email mode, between cycles | `Thread.Sleep(60000)` |
| HTTP status code | never checked. The body is read whatever the status [V] |

- [I] If a JSON response has no top-level property other than `count`, `ReceivedData` never becomes true and the `while (ReceivedData == false)` loop in `GetWebApiData` spins forever.

## 8. Static-data rules (RIDB → SQLite → GUI / cache)
- [V] **Tables kept and omitted** (branch `data/refresh-embedded-ridb-db`, 2026-11-10). `RecreationDotOrgContext` maps, `PopulateDbFromRIDBFiles` imports, and the DB contains only the 6 RIDB tables Campertron reads:
  - `CampsiteAttributesEntries`, `CampsitesEntries`, `FacilitiesEntries`, `FacilityAddressesEntries`, `PermittedEquipmentEntries`, `RecAreaEntries`
  - plus EF's `__EFMigrationsHistory`

  The other 15 RIDB export tables are **intentionally omitted**. A repository-wide search found no use of them outside their own model class, DB mapping, import, clear step and the repair's table list. That covers DbSet names, entity, wrapper and loader names, raw SQL, string literals, reflection, XAML and JSON. They are listed in `RidbDatabase.OmittedTables`:

  | Omitted table | RIDB export file | Model class (file) | JSON loader | Composite key that was removed |
  |---|---|---|---|---|
  | `ActivityEntries` | `Activities_API_v1.json` | `ActivityRecdata` (`Activites.cs`) | `LoadEntriesActivity` | — |
  | `EntityActivitiesEntries` | `EntityActivities_API_v1.json` | `EntityActivitiesRecdata` | `LoadEntriesEntityActivities` | `EntityID, ActivityID` |
  | `EventsEntries` | `Events_API_v1.json` | `EventsRecdata` | `LoadEntriesEvents` | — |
  | `LinksEntries` | `Links_API_v1.json` | `LinksRecdata` | `LoadEntriesLinks` | — |
  | `MediaEntries` | `Media_API_v1.json` | `MediaRecdata` | `LoadEntriesMediaEntries` | `EntityID, EntityMediaID` |
  | `MemberToursEntries` | `MemberTours_API_v1.json` | `MemberToursRecdata` | `LoadEntriesMemberTours` | — |
  | `OrganizationEntries` | `Organizations_API_v1.json` | `OrganizationRecdata` | `LoadEntriesOrganization` | — |
  | `OrgEntitiesEntries` | `OrgEntities_API_v1.json` | `OrgEntitiesRecdata` | `LoadEntriesOrgEntities` | `EntityID, OrgID` |
  | `PermitEntranceAttributesEntries` | `PermitEntranceAttributes_API_v1.json` | `PermitEntranceAttributesRecdata` | `LoadEntriesPermitEntranceAttributes` | `AttributeID, EntityID` |
  | `PermitEntrancesEntries` | `PermitEntrances_API_v1.json` | `PermitEntrancesRecdata` | `LoadEntriesPermitEntrances` | — |
  | `PermitEntranceZonesEntries` | `PermitEntranceZones_API_v1.json` | `PermitEntranceZonesRecdata` | `LoadEntriesPermitEntranceZones` | — |
  | `RecAreaAddressesEntries` | `RecAreaAddresses_API_v1.json` | `RecAreaAddressesRecdata` | `LoadEntriesRecAreaAddresses` | — |
  | `RecAreaFacilitiesEntries` | `RecAreaFacilities_API_v1.json` | `RecAreaFacilitiesRecdata` | `LoadEntriesRecAreaFacilities` | `RecAreaID, FacilityID` |
  | `TourAttributesEntries` | `TourAttributes_API_v1.json` | `TourAttributesFacilityRecData` | `LoadEntriesTourAttributes` | `AttributeID, EntityID` |
  | `ToursEntries` | `Tours_API_v1.json` | `ToursFacilityRecData` | `LoadEntriesTours` | — |

  The model classes (`class/RecDotOrg/*.cs`) and JSON loaders (`LoadJsonFromFile`) are kept but unused. Measured on the 2026-11-10 data, omitting the tables shrinks the DB from 345 MB to 226 MB; Media alone was about 92 MB including its index.
- [V] **Restoring an omitted table for a future feature:**
  1. Add its `DbSet<…>` back to `RecreationDotOrgContext` (`function/RecDotOrg/db/SqliteDb.cs`), and its composite key in `OnModelCreating` if the table above lists one.
  2. Add its import block back to `PopulateDbFromRIDBFiles.Populate`. The pre-removal version is in git history before commit `4215b79`.
  3. Add a `DELETE FROM` line for it to `Clear.ClearDatabase`.
  4. Remove it from `RidbDatabase.OmittedTables`. The validation then requires it to exist instead of being absent.
  5. Refresh and re-embed the DB (build-and-test.md §Refreshing the embedded database).

  The repair's `LegacyAddressLayout.KnownTables` keeps all 21 names on purpose, so it needs no change.
- [V] **Clearing before an import** (`Clear.ClearDatabase`, called by `Clear.All` during a refresh): empties the 6 imported tables, runs `DROP TABLE IF EXISTS` for each omitted table, then `VACUUM`. A refresh of an older full DB therefore removes the omitted tables. Verified 2026-11-10 by refreshing a full 2024 DB.
- [V] **Refresh-time validation** (`RidbDatabase.Validate`). It runs at the end of every `RefreshRidbRecreationData.RefreshData`, and in CI against the shipped DB (`EmbeddedDatabaseTests`). It checks that:
  - `PRAGMA integrity_check` returns `ok`
  - every table and column mapped by the EF model exists
  - none of the 15 omitted tables exists
  - `Read.UniqueStates` is non-empty and contains only two-letter codes (`^[A-Z]{2}$`)
  - `WATCHMAN CAMPGROUND` is returned by `Read.UniqueParks("UT", "SPRINGDALE")`.

  If any check fails, `RefreshData` prints the problems in red and returns `false`. It waits for a key only in an interactive console (`OutputTo` Console or HtmlFile, not in a container, input not redirected), so container, email and unit-test runs never block. The refreshed data is kept, and `LastRidbDataRefresh` is still updated, as before. `RefreshData` changed from `void` to `bool`; no existing caller uses the result.
- [V] **The validation runs only after a refresh and on the shipped DB, not at startup.** An existing installation with a corrected, full 21-table DB keeps its 15 extra tables until the user's next "Refresh Ridb Data". Those tables are ignored by the model, and the GUI works normally: measured 51 states, Watchman found.
- [I] An installation that ran the in-app refresh with a release older than PR #16 has a DB with ZIP codes in `PostalCode` and **un-normalized** `AddressStateCode` values (`Oregon`, `OREGON`, `Tennessee`, `IN ` were seen in the 2026-11-10 export), because that importer normalized `PostalCode` instead.
- [V] Such a DB was measured on 2026-11-11, on a copy of a real data folder's DB. It is a full 21-table DB with the corrected layout and 68 un-normalized `AddressStateCode` values (`OREGON`, `Tennessee`, `STATE MAPPING BROKEN`, …), and is classified Corrected, so it is not repaired. The published beta.4 library lists **60** state entries: the two-letter codes plus 9 full names such as `ARIZONA`. It lists no ZIP codes; MA lists BARRE, and `01005` lists nothing. `RidbDatabase.Validate` reports its 15 extra tables, but startup doesn't run the validation. The next refresh with a current version normalizes the values and drops the extra tables.
- [V] **FacilityAddresses columns.** In the RIDB export, `AddressStateCode` holds the state and `PostalCode` holds the ZIP/postal code (verified in the 2026-11-10 export; Watchman, facility `232445`: `City` "SPRINGDALE", `PostalCode` "84767", `AddressStateCode` "UT"). The export of 2024-11-18 had the two columns **swapped** (`PostalCode` "UT", `AddressStateCode` "84767"), and the code was written against that layout. Changed on branch `fix/gui-state-from-address-state-code` (2026-11-10) to the corrected layout; see open-questions Q12.
- [V] `PopulateDbFromRIDBFiles`: for `FacilityAddresses`, `AddressStateCode` is trimmed, uppercased and mapped from a full US state name to its 2-letter code (`Convert.StateNameToAbbreviation`; unknown values such as `ILE-DE-FRANCE` or `STATE MAPPING BROKEN` pass through unchanged). `PostalCode` is stored exactly as received. `City` is trimmed, has commas trimmed, and is uppercased. *Previously the state-name normalization was applied to `PostalCode`.* The 2026-11-10 export needs this normalization: its `AddressStateCode` contains values such as `Oregon`, `OREGON`, `Tennessee` and `IN ` (trailing space).
- [V] `Read` queries that feed the GUI only include facilities with `FacilityTypeDescription == "Campground"`, `Reservable == true`, `Enabled == true`, `FacilityAddressType` of `"Physical"` or `"Default"`, and a non-null `AddressStateCode` (non-empty as well, in `UniqueStates`, `UniqueCampgrounds` and `UniqueCampgroundsByPark`). An empty `PostalCode` no longer hides a campground. *Previously these conditions were on `PostalCode`.*
- [V] The "state" shown and filtered on in the GUI (`Read.UniqueStates`, `UniqueCities`, `UniqueParks(state, city)`, `UniqueParksByState`) is `FacilityAddressesRecdata.AddressStateCode`. `PostalCode` is not used by any query. *Previously the state was `PostalCode`.* Covered by `AddressStateTests` (5 tests).
- [V] **The query change depends on the DB layout.** With a DB populated from the corrected export by the fixed code, the state list has 51 two-letter codes (measured 2026-11-10). With a DB built from the swapped 2024-11-18 export, which is the DB embedded at `529b8a2` and any DB a user extracted from it, the state list shows 1,149 ZIP codes instead, and By Park shows 448 parks / 2,915 campgrounds (was 450 / 3,307). Such a DB is fixed by the in-app RIDB refresh, or automatically by the one-time repair below once the bundled DB has the corrected layout.
- [V] **One-time repair of legacy DBs on upgrade** (`LegacyAddressLayout.RepairIfLegacy`, called from `Load.DbExistsCheck` whenever `RecreationDotOrg.db` already exists, before the auto-refresh check; console and GUI both start through `Load.GetConfig`):
  - **Detection** looks only at DB contents (`FacilityAddressesEntries.PostalCode` and `AddressStateCode`, trimmed and uppercased; empty values ignored). There are at least 100 non-empty values in each column.
    - **Legacy:** at least 95% of `PostalCode` values are 2-letter codes (`^[A-Z]{2}$`) and at least 95% of `AddressStateCode` values are ZIP codes (`^[0-9]{5}(-?[0-9]{4})?$`).
    - **Corrected:** at least 95% of `AddressStateCode` values are 2-letter codes and at most 5% of `PostalCode` values are.
    - **Unknown:** anything else (too few rows, mixed layouts, other values). Unknown or Corrected DBs are never touched, so a few malformed records do not trigger a replacement.
    - Measured 2026-11-10: the 2024-11-18 DB has 15,924 of 15,933 `PostalCode` values as 2-letter codes and 6,738 of 6,757 `AddressStateCode` values ZIP-like (**Legacy**). The 2026-11-10 DB has 0 of 7,248 and 0 of 16,353 (**Corrected**).
  - **The DB must contain only objects Campertron creates:** the 21 RIDB tables, `__EFMigrationsHistory`, `sqlite_sequence` and SQLite's automatic primary-key indexes on them (`LegacyAddressLayout.KnownTables`). Any other table, view, trigger or index means the DB was changed outside Campertron. It is then **not** replaced, and a message tells the user to run "Refresh Ridb Data". Campertron itself stores no user data in this DB: the only writers are the embedded-DB extraction, `Clear.All` and `PopulateDbFromRIDBFiles`. Search configs, `Camp.History` markers and the JSON cache are separate files and are not touched.
  - **Replacement steps.** Every step happens in `ConfigPath`, so all moves stay on one filesystem:
    1. Take an exclusive lock file, `RecreationDotOrg.repair.lock` (deleted on close). If another process holds it, skip.
    2. Re-run the detection.
    3. Extract the bundled DB to `RecreationDotOrg.repair-<guid>/`. **It must itself be detected as Corrected** with only known objects; otherwise skip. While the bundled DB was the legacy 2024 one (between PR #16 and the DB refresh), the repair declined, and the result was remembered for the rest of the process. Since the refresh, the bundled DB is the corrected 6-table DB, which passes this check: its extra-table allowlist only flags unknown objects, never missing ones.
    4. `PRAGMA wal_checkpoint(TRUNCATE)` on the old DB. If another connection prevents it, skip.
    5. Rename the old DB (and any `-wal`/`-shm` files) to `RecreationDotOrg.db.legacy-layout-<yyyyMMddHHmmss UTC>.bak`.
    6. Move the extracted DB into place. If this fails, the backup is moved back.
    7. Remove the temporary directory.
  - **Data-loss considerations:**
    - Nothing is deleted. The old DB is kept byte for byte as the `.bak` file (about 328 MB), which the user can delete.
    - The JSON cache in `CachePath` is not cleared. Cached campsite lists keep coming from the old data until a RIDB refresh clears them, which matches the behavior before the upgrade.
    - I/O, permission and SQLite errors are caught. The DB is then left unchanged and a message is printed. The console shows these messages; the GUI (`WinExe`) does not.
    - [I] If the move back after a failed swap also fails, `RecreationDotOrg.db` is missing. The next start then extracts the bundled DB as on a first run, and the `.bak` file is still present.
  - [V] With the refreshed bundled DB (2026-11-10, real console binary, isolated dir), a 2024-11-18 installation was replaced: the `.bak` file was byte-identical to the original, the new DB passed `RidbDatabase.Validate`, and startup took 592 ms. The next start did nothing.
  - [V] Covered by `LegacyAddressLayoutTests` (15 tests). Checked on 2026-11-10 against the real 2024-11-18 DB: detected as Legacy and replaced from a corrected 2026-11-10 DB. The `.bak` SHA-256 matched the original, and afterwards the state list had 51 codes, with Watchman under UT/SPRINGDALE. With today's legacy bundled DB, startup declines the repair and leaves the DB unchanged. That startup took about 0.6 s, including extracting the bundled DB to a temporary directory once per process.
- [V] `RecAreaAddresses` is still swapped in the 2026-11-10 export (`PostalCode` "OR", `AddressStateCode` "97754"). No code reads that table, so it has no effect today.
- [V] `Read.GetParkCampgroundInfo` (used for cache headers) requires `Reservable` and `Enabled`. If either is false, it returns an empty `ReturnParkCampground` with null names.
- [V] `Read.GetParkCampgroundByName` matches names case-insensitively after trimming. It strips HTML (`HtmlToPlainText`), replaces `"Overview"` with `"Description:\r\n"`, and prefixes fields with labels (`NullCheck` returns null for empty values).

## 9. Config bootstrap rules
- [V] If no `autoRun: true` search YAML exists, `ZionConfig.yaml` is generated, **overwriting** any existing file with that name (`Yaml.CampertronConfigGenerateSampleConfig` uses `StreamWriter`).
- [V] Auto-refresh condition: `AutoRefresh && UtcNow - RefreshRidbDataDayInterval days > LastRidbDataRefresh` (`Load.DbExistsCheck`). The sample sets `LastRidbDataRefresh` to 50 years ago, so enabling `autoRefresh` triggers a refresh on the next start.
- [V] Container detection relies on `DOTNET_RUNNING_IN_CONTAINER` or `DOTNET_RUNNING_IN_CONTAINERS`. [I] The official .NET container images set the first of these.

## 10. Compatibility with versions before 0.1.0-beta.4
- [V] All versions use the same data folder (`LocalApplicationData/CampertronConfig`, or `/config` in a container) and the same `RecreationDotOrg.db` file name. A newer version does not keep a separate database.
- [V] The 2024 versions (`v0.1-beta1`/`v0.1-beta3` downloads, code up to `e96bc7c`; `v0.1-beta3` is `net9.0`) read the state from `FacilityAddresses.PostalCode`. In the corrected address layout (§8), `PostalCode` holds ZIP codes. That layout is in the DB that 0.1.0-beta.4 and later extract or install by repair, and in any DB refreshed from current RIDB data, whichever version ran the refresh. **A 2024 GUI reading such a DB lists ZIP codes as states** (for example `01005` → city BARRE; MA lists nothing). Measured 2026-11-11 with the 2024 library: 1,185 ZIP-like "states" on the beta.4 DB, and 1,171 on a copy of a real data folder's refreshed DB. The 2024 console shows `Framework => .NET 9.x` in View Config (`.NET 9.0.0` for the self-contained `v0.1-beta3` download).
- [V] Nothing in a newer version can prevent this: the 2024 binaries can't be changed, and they don't check anything a newer version could write (decisions D20). The protection is documentation: the required "Compatibility with older versions" section of every release's notes (`render_notes`/`validate_notes`) and the README upgrade note tell users not to keep or run old copies, and `--version` (§11) identifies a copy.
- [V] Running a 2024 version first and 0.1.0-beta.4 or later afterwards is handled: the newer version repairs a legacy DB at startup (§8).
- [I] A 2024 version's in-app "Refresh Ridb Data" re-imports all 21 tables with its old normalization (states un-normalized in `AddressStateCode`). A newer version classifies that DB as Corrected and doesn't repair it; how a newer version then shows it is measured in §8.

## 11. `--version` (CampertronConsole and CampertronGUI)
- [V] `RuntimeInfo.IsVersionRequest(args)` is true only for exactly one argument, `--version` (case-sensitive). Any other arguments are ignored as before, and the normal startup runs.
- [V] Output (`RuntimeInfo.Describe`): `<assembly name> <AssemblyInformationalVersion>` (for a release `<version>+<commit>`), then `Framework => <RuntimeInformation.FrameworkDescription> (<ProcessArchitecture>)`, the same text as the console's View Config. Exit code 0.
- [V] It runs first in `Main`, before `Load.GetConfig`, Avalonia or any file access: no directories, YAML, DB, cache or window are created, and it needs no display. In a container it works without `/config` (`docker run <image> --version`).
- [A] On Windows the GUI is a `WinExe`, which has no console; its output appears only when redirected (for example `CampertronGUI.exe --version | more`). Not verified on Windows.
