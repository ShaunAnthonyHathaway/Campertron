# CLAUDE.md — Campertron

## Purpose
Campertron polls recreation.gov for campsite availability that matches user YAML search configs, and reports hits to the console, an HTML file, or email. A separate Avalonia GUI is a **read-only campground lookup** (find campground IDs and details). It does **not** search for availability.

## Solution layout (`Campertron.sln`)
| Project | Type | Entry point |
|---|---|---|
| `CampertronConsole` | Exe, net10.0 | `CampertronConsole/Program.cs` → `Load.RunConsoleSearch(Load.GetConfig())` |
| `CampertronGUI` | WinExe, net10.0, Avalonia 12.1.6 + ReactiveUI (`ReactiveUI.Avalonia.Reactive` 12.1.6, System.Reactive flavor; decision D23) | `CampertronGUI/Program.cs` → `App` → `Views/MainWindow` |
| `CampertronLibrary` | Class library, net10.0 | All business logic (`CampertronLibrary/function/**`, models in `class/**`) |
| `CampertronUnitTests` | NUnit 4 tests, net10.0 | `CampertronUnitTests/function/RecDotOrg/data/*.cs`, `CampertronUnitTests/function/RecDotOrg/sqlite/*.cs` |
| `CampertronGUITests` | NUnit 4.5.1 + Avalonia.Headless.NUnit 12.1.6, net10.0 | `CampertronGUITests/*.cs` (headless App/MainWindow/view-model tests with a tiny test-owned DB) |
| `docker-compose.dcproj` | VS Docker Compose project | `docker-compose.yml`, `CampertronConsole/Dockerfile` |

Stack: .NET 10 (`net10.0`; EF Core packages are 9.0.19), EF Core Sqlite, YamlDotNet, Newtonsoft.Json and System.Text.Json, System.Net.Mail for email (no MailKit/MimeKit). The SQLite DB ships as the Brotli-compressed embedded resource `CampertronLibrary/content/RecreationDotOrg.db.br` (26.6 MB; it extracts to a 226 MB `RecreationDotOrg.db`). Only the 6 RIDB tables Campertron reads are kept. The other 15 are intentionally omitted (`RidbDatabase.OmittedTables`, business-rules §8).

## Commands (from repo root; .NET 10 SDK + runtime + targeting pack. Verified 2026-11-11 with SDK 10.0.114 after the Avalonia 12 migration: restore OK, build 0 errors / 86 warnings (no AVLN/RXUIBIND), tests 77/77 (65 + 12 GUI), release-policy tests 67/67, isolated GUI smoke test OK (build-and-test.md §K); after the RIDB import change: tests 96/96 (84 + 12 GUI), build-and-test.md §M; after the failure-safe refresh (D26) and the General.yaml timestamp fix: build 0 errors / 84 warnings, tests 140/140 (128 + 12 GUI), build-and-test.md §N. After the TD-05 cache-path fix: build 0 errors / 84 warnings, tests 179/179 (167 + 12 GUI), build-and-test.md §O. After the beta.8 GUI polish: build 0 errors / 81 warnings, tests 207/207 (173 + 34 GUI), build-and-test.md §P. CI: `.github/workflows/ci.yml` runs the Release equivalents on every PR and push to `master`. Releases: `.github/workflows/release.yml` publishes only for a SemVer tag on `master`; otherwise dry run (build-and-test.md §Release workflow))
```
dotnet restore Campertron.sln            # restore before clean after a TargetFramework change
dotnet build Campertron.sln -c Debug
XDG_DATA_HOME="$(mktemp -d)" dotnet test CampertronUnitTests/CampertronUnitTests.csproj   # isolate user data!
dotnet test CampertronGUITests/CampertronGUITests.csproj   # headless GUI tests; isolate themselves (Linux only)
dotnet run --project CampertronConsole
dotnet run --project CampertronGUI
dotnet run --project CampertronConsole -- --version   # version + FrameworkDescription, touches no data (also CampertronGUI)
bash .github/release-policy/test-release-policy.sh    # release policy + .NET 10 guard tests
docker build -f CampertronConsole/Dockerfile -t campertronconsole .   # BuildKit required (multi-arch Dockerfile)
```
⚠ The tests are integration tests. They use `LocalApplicationData/CampertronConfig` and `CampertronCache` (on Linux that is `$XDG_DATA_HOME`, else `~/.local/share`, which holds the user's real data). They extract the 226 MB DB, call recreation.gov, and write `Camp.History` markers that suppress future real notifications. Always isolate them as shown above. **The `XDG_DATA_HOME` directory must already exist** (`mktemp -d` creates it; CI runs `mkdir -p`). If it doesn't, the tests still pass but were observed writing their data into the test output dir (`CampertronUnitTests/bin/<Config>/net10.0/`) instead (build-and-test.md, Test warnings §1). `CampertronGUITests` sets its own temporary `XDG_DATA_HOME` in-process and never extracts the DB; on non-Linux hosts its tests are ignored (Test warnings §8).
⚠ No `global.json` exists, and the repo must not be pinned to .NET 9. The SDK 10 build needs the .NET 10 runtime **and** targeting pack. If either is missing, it fails with exit code 150 or `NETSDK1226`. Never work around it with `AllowMissingPrunePackageData`. See `docs/ai-context/build-and-test.md`.

## Architecture in one paragraph
Everything lives in static classes in `CampertronLibrary`. Config (`Load.GetConfig`) picks paths: `/config` + `/cache` in a container, otherwise `LocalApplicationData/CampertronConfig` + `/CampertronCache`. It loads `General.yaml` and `Email.yaml` and ensures `RecreationDotOrg.db` exists. If an installed DB has the swapped 2024 layout, or is a pre-0.1.0-beta.4 DB that still has the omitted tables, a one-time startup repair replaces it with the bundled DB (`LegacyAddressLayout.RepairIfNeeded`, business-rules §8). The trigger is a property of the DB, never the app version, and a valid DB is never replaced. Search (`Load.RunConsoleSearch` → `Preload` → `AvailabilityApi.GetAvailabilitiesByCampground` in parallel per config → `PostProcess`) fetches monthly availability JSON from recreation.gov and filters it with the rules in `AvailabilityApi.FilterData`. It renders through a `ConsoleConfigValue` list that `CampsiteConfig.ProcessConsoleConfig` writes to the console or turns into HTML and email. Static RIDB data (campsites, equipment, attributes, facilities) comes from SQLite through `sqlite/Read.cs` and is cached as per-campground JSON in the cache dir. Details: `docs/ai-context/architecture.md`.

## Conventions observed (follow when editing)
- Static classes and static methods throughout. There is no DI, no interfaces, and no logging framework. Output is `Console.WriteLine` and `CampsiteConfig.WriteToConsole`.
- PascalCase locals and parameters (`ReturnList`, `ThisConfig`) and `ref` parameters for shared state (`ref RunningData`, `ref List<ConsoleConfigValue>`).
- Model classes are in the **global namespace** (`class/**`). Functions are in `CampertronLibrary.function.*`.
- YAML uses camelCase keys (`CamelCaseNamingConvention`).
- `OutputType.UnitTest` is a production enum value used to bypass interactive and looping behavior in tests.

## Validation and logging
- There is no validation layer. YAML is deserialized directly, and invalid values surface as runtime exceptions or `Console.WriteLine` messages (see `docs/ai-context/business-rules.md`).
- There is no structured logging. Do not introduce a logging framework unless the task asks for it. Keep the existing console messages unless the task explicitly changes them.

## Preserving business logic
- The filter semantics in `AvailabilityApi.FilterData` and the `ProcessMonth`/`GenerateDates` date math are quirky but are the current contract (for example, include filters use `configValue.Contains(siteValue)`, and today's date is never matched). Do **not** "fix" them as a side effect. Change them only when asked, and record the change in `business-rules.md`.
- Keep the retry and sleep behavior (`AvailabilityApi.GetWebApiData`), the duplicate-notification markers (`Camp.History`), and the container/email loop unless the task targets them.
- The RIDB refresh writes rows through `RidbTableInserter` (prepared SQLite commands, one transaction, EF Core's former `SaveChanges` row order; decision D25), not EF `SaveChanges`. Its column mappings are hand-typed: when a mapped RIDB model changes, update the matching mapping; `PopulateTests` fails otherwise. Keep the row order and run the full-export comparison of build-and-test.md §M after changing the import.
- The RIDB refresh is failure-safe (decision D26, business-rules §8): it builds and validates a staging DB in `ConfigPath/RecreationDotOrg.refresh-<guid>/` and installs it with one `SqliteConnection.BackupDatabase` transaction (`RidbStagedRefresh`). Never install by renaming, replacing or deleting the installed DB. Keep the order: refresh lock → staging build and `Validate` → cache cleared → repair lock + backup → `quick_check` → `LastRidbDataRefresh` written once. The cache step clears the configured `CachePath` (`LocalApplicationData/CampertronCache` on the desktop, `/cache` in a container; TD-05 resolved, decision D27). The cache path comes only from `CampertronInternalConfig.CachePath`; never rebuild it from `LocalApplicationData`, and keep the guards in `Clear.CheckCacheDirectory`/`ClearRefreshCache` (no unset, root or config directory; only top-level files deleted). After changing it, run `AtomicRefreshTests`, the kill harness and the full-export comparison (build-and-test.md §N). The timestamp is written with `Yaml.GeneralConfigReplaceYaml` (validated temporary file renamed over `General.yaml`, `ConfigFileReplace`); don't write it in place. The culture-dependent row order (TD-36 part 2) and the other, still in-place YAML writers (TD-37) are open.
- GUI (beta.8, decision D28): stays a read-only lookup with three tabs, each with its own selections and details. Take colors only from Fluent theme resources (`Styles/AppStyles.axaml`, `DynamicResource`; the app follows the system Light/Dark theme). Bind each database-backed view-model property exactly once (every getter queries SQLite; `Selections_RunOneQueryEach`). The GUI owns its labels through `Read.GetCampgroundDetailsByName`; leave `Read.GetParkCampgroundByName`'s prefixed contract alone. Keep the Tab-wrap workaround in `MainWindow` for the TabControl keyboard trap reproduced with Avalonia 12.1.6 (TD-39); reassess it after any Avalonia upgrade or an upstream fix for that exact behavior. Details are looked up by name and names are not unique (TD-38, not fixed).
- Keep production code changes separate from documentation-only changes, in separate commits.
- Release tags and releases are final: tag ruleset 27004221 blocks moving or deleting any `refs/tags/v*` tag (no bypass), and immutable releases are enabled. Never re-tag; release a new version instead. Releases are made only by `.github/workflows/release.yml` from a SemVer tag on `master` (first: `v0.1.0-beta.4`, 2026-11-11; latest: `v0.1.0-beta.7`, 2026-11-12 UTC). Every release, including a hyphenated pre-release version, is published as a normal GitHub release marked Latest; a hyphen only limits Docker tags to the exact version. Policy and tests: `.github/release-policy/` (decision D19). Details: `docs/ai-context/build-and-test.md` §Release workflow.
- Every release output must be .NET 10. `release.yml` fails on anything else: project TFMs and Docker base images (verify), each of the 16 publish outputs (`check_publish_runtime`), `--version` of the published linux-x64 apps and of the container, and the GUI state data of the published library and DB (`.github/release-policy/ReleaseCheck`). Keep these guards when changing the release workflow (decision D21, build-and-test.md §I).
- Windows GUI packages exclude the SkiaSharp/HarfBuzzSharp native debug symbols (`libSkiaSharp.pdb`, `libHarfBuzzSharp.pdb`, about 105 MB per RID) through the `ExcludeWindowsNativeDebugSymbols` target in `CampertronGUI.csproj`, which filters `NativeCopyLocalItems` by package ID (an SDK-internal item; decision D24). `release.yml` fails if they reappear as files or `.deps.json` entries. Don't replace it with file deletion or `ExcludeAssets="native"` (both are wrong, D24), and keep Campertron's own PDBs. TD-35 is mitigated, not fixed upstream; remove the target only when fixed packages are adopted (open question #24, build-and-test.md §L).
- `v0.1.0-beta.4` was investigated on 2026-11-11 (build-and-test.md §I). Its published artifacts are correct. The GUI that showed ZIP codes as states was a separate 2024 executable, not the one in the beta.4 archive. The console that reported .NET 9 was not established to be the beta.4 console. Why the older executable was opened is unknown; don't attribute it to anyone or state a cause the evidence doesn't establish. Separately, pre-0.1.0-beta.4 versions read `PostalCode` as the state and list ZIP codes on any corrected-layout database. That is handled by documentation only (business-rules §10, decision D20); don't "fix" it by changing the DB layout or file name.
- `master` is protected by ruleset 26990088, with no bypass for anyone, including admins. Every change goes through a PR from a branch, with the `Restore, build, test, vulnerability scan` check passing on an up-to-date branch, all conversations resolved, and a **merge commit** as the only allowed merge method. Direct pushes, force pushes and deletion of `master` are blocked. Details: `docs/ai-context/build-and-test.md` §Branch protection.

## Required workflow
**Before a task:**
1. Read this file.
2. Read the relevant `docs/ai-context/*.md` files for the task.
3. Inspect the current implementation before trusting the docs. Code wins over docs.

**After a task:**
1. Build (and run the tests if safe; see the warning above) before declaring implementation work complete. If you can't run them, say so.
2. Update the docs whenever architecture, behavior, commands or feature status changes.
3. Never replace verified information with assumptions. Mark inferences as such and add unknowns to `open-questions.md`.

## Detailed docs (`docs/ai-context/`)
- [repository-map.md](docs/ai-context/repository-map.md): projects, directories, key classes
- [architecture.md](docs/ai-context/architecture.md): boundaries, data flow, config, persistence, integrations
- [feature-inventory.md](docs/ai-context/feature-inventory.md): console features → implementation and tests
- [console-to-gui-map.md](docs/ai-context/console-to-gui-map.md): GUI parity status
- [business-rules.md](docs/ai-context/business-rules.md): filters, dates, retries, edge cases
- [build-and-test.md](docs/ai-context/build-and-test.md): environment, commands, test caveats
- [decisions.md](docs/ai-context/decisions.md): verified decisions vs recommendations
- [technical-debt.md](docs/ai-context/technical-debt.md): confirmed defects and risks
- [open-questions.md](docs/ai-context/open-questions.md): unresolved unknowns
