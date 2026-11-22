# Build and test

Authority: current. This file is the current authority for the build and test environment, the commands, the test warnings, routine continuous integration (`ci.yml`), the current validation baseline and the checks to run after a change in each area. The release policy and procedure are in `release-process.md`; hands-on maintainer procedures are in `maintainer-procedures.md`; the present repository and release state is in `current-state.md`.

Until roadmap step 9, Phase 2 (2026-11-17), this file also held the validation records §A to §AZ, the release workflow, the release history, the branch protection and two maintainer procedures. §Moved sections says where each went; every moved heading also keeps a stub with its text at the end of this file. A label in the sections below that names a lettered record (§A to §AZ) or a moved heading resolves through that table.

Legend: **[V]** verified (command run or file read) · **[A]** assumption, not executed · **[R]** recommendation

## Required environment
- [V] A stable **.NET 11 SDK** (11.0.100 or a later 11.0 release; it contains the .NET 11 runtime and targeting pack). All six projects target `net11.0` (decision D31). The root `global.json` (`11.0.100`, `rollForward: latestFeature`, `allowPrerelease: false`) makes `dotnet` use the newest installed stable 11.0 SDK and never SDK 10, a prerelease or SDK 12+; without one, every `dotnet` command in the repository fails with exit code 155 and Campertron's `errorMessage` (§U). Run `dotnet` inside the repository: a command started elsewhere does not see `global.json`. On the audit machine the .NET 11 SDK is a per-user install, `/home/shaun/.dotnet11/dotnet` (SDK 11.0.100, runtime 11.0.0); the system packages below still provide SDKs 9.0.123 and 10.0.114, which `global.json` rejects. Before D31 there was no `global.json`, so the newest installed SDK was used (TD-31).
- [V] (Before D31, .NET 10.) On Arch/CachyOS, the SDK, runtime and targeting pack are separate packages. The audit machine needed `dotnet-sdk` 10.0.14.sdk114-1, `dotnet-runtime` 10.0.14 and `dotnet-targeting-pack-10.0` 10.0.14.sdk114-1. When one was missing, the failure looked like this:
  - **runtime missing:** every `dotnet` command fails with exit code 150, "You must install or update .NET to run this application … 'Microsoft.NETCore.App', version '10.0.14'"
  - **targeting pack missing:** `restore` fails with `NETSDK1226: Prune Package data not found .NETCoreApp 10.0 Microsoft.NETCore.App`. Do **not** work around this with `AllowMissingPrunePackageData`.
- [V] The GUI needs a desktop session (Avalonia `UsePlatformDetect`, `WinExe`). Isolated smoke tests passed on 2026-11-10 (§E, Avalonia 11) and 2026-11-11 (§K, Avalonia 12). The headless GUI tests (`CampertronGUITests`) need no display.
- [V] Docker is only needed for the container build (`CampertronConsole/Dockerfile`, base images `mcr.microsoft.com/dotnet/runtime:11.0` and `sdk:11.0`, Ubuntu 26.04). It needs **BuildKit**, the default builder since Docker 23, because the Dockerfile uses `$BUILDPLATFORM` and `$TARGETARCH`. Docker Engine **29.9.3** (with buildx) is installed on the audit machine and was used for the local container validations of §N, §O and §Y and for the `v0.1.0-beta.10` image check (§Release history); it was not installed at the start of the 2026-11-10 audit. The published image is built and smoke-tested by the release workflow (§Release workflow).
- [V] A search needs network access to `www.recreation.gov`; the tests do not since D45 (§AJ; before, one test made a live request). RIDB refresh needs `ridb.recreation.gov` (no test contacts it: since D61 the refresh's tests download from an in-memory server). Restore needs `api.nuget.org`.
- [V] Disk: the embedded `RecreationDotOrg.db.br` is 26,616,039 bytes and extracts to a 226,471,936-byte `RecreationDotOrg.db`. Until `07e786d`, the embedded `RecreationDotOrg.zip` was 81,232,118 bytes and extracted to 328,126,464 bytes.

## Commands (repo root)
```bash
dotnet --info                                   # expect SDK 11.0.x (global.json file: <repo>/global.json), runtime Microsoft.NETCore.App 11.0.x
dotnet restore Campertron.sln                   # run BEFORE clean after a TargetFramework change (see note)
dotnet clean Campertron.sln
dotnet build Campertron.sln --no-restore        # Debug
XDG_DATA_HOME="$(mktemp -d)" dotnet test CampertronUnitTests/CampertronUnitTests.csproj --no-build   # ALWAYS isolate (see warnings)
dotnet test CampertronGUITests/CampertronGUITests.csproj --no-build   # headless GUI tests; isolate themselves (Linux only, see Test warnings §8)
bash .github/docs-policy/check-docs.sh          # documentation-policy checks of CLAUDE.md and docs/ai-context (.github/docs-policy/README.md): exit 0 pass, 3 warnings only, 1 errors, 2 usage or environment; CI's required job runs it too (§Continuous integration)
bash .github/docs-policy/test-check-docs.sh     # the checker's own tests (a fixture repository in a temporary directory, and the ci.yml steps with a stub checker); also in CI

dotnet build Campertron.sln -c Release          # [V] run with --no-incremental in §R, §S, §T, §X and §Y, and by CI
dotnet run --project CampertronConsole/CampertronConsole.csproj   # interactive; the console itself was run interactively (published builds on a pseudo-terminal) in §Y
dotnet run --project CampertronGUI/CampertronGUI.csproj           # needs a display; the GUI was run in real and nested-session windows in §K, §P and §X
dotnet run --project CampertronGUI/CampertronGUI.csproj -- --smoke-test   # GUI release smoke test (R8, §X): SMOKE lines on stdout, exit 0 only if it passed; needs a display
docker build -f CampertronConsole/Dockerfile -t campertronconsole .   # needs BuildKit (TARGETARCH); run 2026-11-11 with Docker 29.9.3 + buildx (§N)
docker buildx build --platform linux/amd64,linux/arm64 -f CampertronConsole/Dockerfile .   # multi-arch, as in release.yml
docker run -d --name campertron -v /path/to/config:/config -v /path/to/cache:/cache campertronconsole   # local image built above; README.md uses ghcr.io/shaunanthonyhathaway/campertronconsole:<version>
```
- [V] **`clean` before `restore` fails after a TargetFramework change.** It reports `NETSDK1005: Assets file '…/obj/project.assets.json' doesn't have a target for 'net10.0'`, because `clean` doesn't restore and `obj/` still describes the old framework. Restore first.
- [V] `docker-compose.dcproj` is skipped by `dotnet build Campertron.sln` on Linux without error.
- [V] `docker-compose.yml` bind-mounts Windows host paths `C:\temp\config` and `C:\temp\cache`. Edit them for Linux or macOS.
- [V] The container works only with `outputTo: Email` and, since D67, `outputTo: Ntfy`. Anything else exits with code 1 ("Unsupported output type in container").

## Current validation baseline
- [V] Unchanged since the `v0.1.0` release run #58 (44102810321, 2026-11-17): Debug and Release builds of `Campertron.sln` with 0 errors and 56 warnings, the same 42 identities at the same positions; 1,234 tests, 1,009 `CampertronUnitTests` (20 Windows-only tests skipped on Linux) and 225 `CampertronGUITests`; release-policy tests 419/419; no vulnerable packages in the five projects. Its record is §AZ.
  Since then: 465 release-policy cases since D59 (roadmap step 10, Phase T, §BB), with the .NET builds and tests unchanged; and since roadmap step 10, Phase A (V011-2, D60, §BC) 1,272 tests, 1,033 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 239 `CampertronGUITests`, release-policy tests 471, and Debug and Release builds still with 0 errors and the same 56 warnings at the same positions; and since roadmap step 10, Phase B (V011-1, D61 and its automatic restart D62, §BD) 1,452 tests, 1,095 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 357 `CampertronGUITests`, release-policy tests 482, and Debug and Release builds still with 0 errors and the same 56 warnings at the same positions.
  And since roadmap step 10, Phase C (V011-3 and TD-56, D63, §BE) 1,467 tests, 1,097 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 370 `CampertronGUITests`, release-policy tests still 482, and Debug and Release builds still with 0 errors and the same 56 warnings (the same set as `c4aebb0`, compared line by line).
  And since the email-mode repair (D64, §BJ; after `v0.1.1`) 1,491 tests, 1,121 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 370 `CampertronGUITests`, release-policy tests still 482, documentation-policy tests still 98, and Debug and Release builds still with 0 errors and the same 56 warnings (the same set as `60bcf82`, compared by file, code and message).
  And since its acceptance correction (D65, §BK) 1,496 tests, 1,126 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 370 `CampertronGUITests`, the policy suites unchanged, and Debug and Release builds still with 0 errors and the same 56 warnings (the same 42 identities as `ff7baec`).
  And since the SMTP password sources (D66, §BL) 1,581 tests, 1,210 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 371 `CampertronGUITests`, the policy suites unchanged, and Debug and Release builds still with 0 errors and the same 56 warnings (the same 42 identities as `d7b1059`; 13 at other lines).
  And since ntfy notifications (D67, §BM) 1,708 tests, 1,336 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 372 `CampertronGUITests`, the policy suites unchanged, and Debug and Release builds with 0 errors and 54 warnings: the 42 identities of `8a23ff1` less two CS8604 in `Load.cs` (the email loop's former possibly-null arguments, which `Notifications.Deliver` replaced), none added (compared by file, code and message).
  And since the `v0.2.0` release readiness (D68, §BN) 1,722 tests, 1,350 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 372 `CampertronGUITests`, release-policy tests 489, documentation-policy tests still 98, and clean Debug and Release builds of `ea1d2ea` and of the branch both with 0 errors and 54 warnings, the same 40 identities (compared by file, code and message).
  And since the GUI `Ntfy.yaml` editor (D69, §BO; after the rejected `v0.2.0` candidate #81) 1,748 tests, 1,359 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 389 `CampertronGUITests`, release-policy tests 493, documentation-policy tests still 98, and clean Debug and Release builds of the branch with 0 errors and 54 warnings, the same 40 identities as `7cdd4af` (compared by file, code and message).
  And since the GUI's SMTP password file field (D70, §BP) 1,764 tests, 1,361 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 403 `CampertronGUITests`, release-policy tests still 493, documentation-policy tests still 98, and clean Debug and Release builds with 0 errors and the same 54 warnings, the same 40 identities.
  And since the GUI save validation (D71, §BQ) 1,801 tests, 1,390 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 411 `CampertronGUITests`, release-policy tests still 493, documentation-policy tests still 98, and a clean Debug build with 0 errors and the same 54 warnings.
  And since the results email (D72, §BR) 1,828 tests, 1,417 `CampertronUnitTests` (the same 20 Windows-only tests skipped on Linux) and 411 `CampertronGUITests`, release-policy tests still 493, documentation-policy tests still 98, and a clean Debug build with 0 errors and the same 54 warnings.
- [V] The CI job `Windows settings files (CM1 to CM3, CM5, CM6)` passes on every pull request and push (§Continuous integration).
- Whether anything changed since that run is recorded in `current-state.md` §Repository; what each change area must run against this baseline is §Recommended verification after changes.

## Test warnings: read before running `dotnet test`
1. **Real user directories** [V]. `LoadTests` uses `Load.GetConfig()` → `LocalApplicationData/CampertronConfig` and `CampertronCache`. It creates dirs, writes sample YAML and extracts the DB. Isolate with `XDG_DATA_HOME`. [A] On Windows this is `%LOCALAPPDATA%`, and no isolation recipe has been verified there.
   - **Since D37 (TD-47 resolved) a missing `XDG_DATA_HOME` folder is created** [V, §AB: the full suite with a missing folder passed and wrote nothing into the test output folder]. Creating it first (`mktemp -d`, or `mkdir -p` as CI does) is still recommended. Before D37 [V]: On 2026-11-10, `XDG_DATA_HOME=<tmp>/campertron-data` pointing at a missing directory still gave 9/9 passing tests. But the data (313 MB `CampertronConfig` plus `CampertronCache`) was written to `CampertronUnitTests/bin/Release/net10.0/` instead, and the isolated dir was never created. [I] `Environment.GetFolderPath(LocalApplicationData)` returns `""` when the folder doesn't exist, so `Path.Join("", "CampertronConfig")` becomes relative to the test host's working directory. ([V] since the 2026-11-14 audit's probe; the same happens to the console and GUI themselves: TD-47.) Use `mktemp -d`, or `mkdir -p "$XDG_DATA_HOME"` first, as CI does.
2. **Network** [V]. ~~`LoadTests.RunConsoleSearchTest` performs a real search for every `autoRun: true` YAML in the config dir, or the generated Zion sample if there are none.~~ Since D45 (GS1, §AJ) it runs that search with every month answered through `AvailabilityApi.ConsoleDownload`, and no test reaches the network: `NoLiveNetwork` (an assembly-wide fixture) fails the run if one does. The whole suite passes inside a network namespace with only a loopback interface.
3. **Notification markers** [V]. ~~`OutputType.UnitTest` is not `Console`, so `Load.HasNewEntries` writes `Camp.History` markers. Against a real config dir, these suppress future real notifications.~~ Since the notification-history fix (§T) only `Email` reads or writes `Camp.History`; the tests (`UnitTest`, and Email only in temporary directories) write no marker into the configured directory (0 in the §T full runs).
4. **Config writes.** `GetConfig` → `DbExistsCheck` can download the RIDB export if auto-refresh is due and then update `General.yaml`'s timestamp (since D40 only that value, keeping the previous bytes in `General.yaml.bak`) [A].
5. **Container env.** If `DOTNET_RUNNING_IN_CONTAINER` is set, `GetConfig` requires `/config` and `/cache`.
6. **Field initializer.** `LoadTests` runs `Load.Preload(Load.GetConfig())` when the fixture is constructed.
7. **Hang risk.** The console's retry loops have no limit (business-rules §7). Use `timeout` or `--blame-hang-timeout` when running unattended. (No test reaches the network since D45, so they no longer meet a real failure; the interactive search's own loops are bounded.)
8. **`CampertronGUITests` isolates itself** [V]. Its `[SetUpFixture]` (`GuiTestEnvironment`) sets `XDG_DATA_HOME` to a new temporary directory inside the test process, fails if `LocalApplicationData` does not resolve there, writes a `General.yaml` with `autoRefresh: false` and creates a tiny deterministic DB from the EF model (corrected layout, no omitted tables, asserted smaller than 5 MB). `Load.GetConfig()`, which `MainWindowViewModel` runs on a background task after the window has opened (R8, D33; before R8 it ran in `MainWindow`'s constructor), therefore neither extracts, repairs nor refreshes, and the tests use no network and no real data. The directory is deleted afterwards. On Windows and macOS `XDG_DATA_HOME` does not redirect `LocalApplicationData`, so the fixture ignores all GUI tests there.

## Continuous integration (`.github/workflows/ci.yml`)
Added on branch `ci/add-build-test-workflow` (commit `ec70898`, PR #14) [V].
- **Triggers:** `pull_request` targeting `master`, and `push` to `master`. Runs on `ubuntu-latest` with a 30-minute job timeout.
- **Windows settings-file job** (since CM2, D40, §AE): `windows-settings-files` ("Windows settings files (CM1, CM2)" until D41) runs with the same triggers on the pinned `windows-2025` runner: SDK and package policy, a Release build of the library and the unit tests, and the CM1 and CM2 suites (`YamlDocument*`, `ConfigFileReplace*`, `GeneralYamlTimestampTests`, `SettingsBootstrapTests`), including `ConfigFileReplaceWindowsTests`, which run only on Windows, since D41 the CM3 suites (`ConfigFilesTests`, `SettingsLoaderLimitsTests`, `ConfigValidationTests`, `EmailOutputValidationTests`; named "Windows settings files (CM1 to CM3)") since D43 the settings editor's session suites (`ConfigEditSessionTests`, `ConfigEditEmailTests`; named "Windows settings files (CM1 to CM3, CM5)"; its summary fails the job unless `ConfigEditEmailTests.TheAccessRulesOnWindows_AreNeverBroadened` passed) and since D44 the search-file suites (`SearchFileSessionTests`, `SearchFilesTests`; named "Windows settings files (CM1 to CM3, CM5, CM6)"), since D66 `SmtpPasswordTests` (the password file's paths, links and folders on Windows; the name unchanged) since D67 `NtfyConfigTests` (`Ntfy.yaml` and the token file's paths) and since D69 `ConfigEditNtfyTests` (the GUI's `Ntfy.yaml` session: a created file's access rules, saves, conflicts); it checks that the runner user's `CampertronConfig` and `CampertronCache` never exist and summarizes the results (per class, skipped tests with reasons) in the job summary. It is not a required check of the ruleset; the CM2 merge required it green. A newer push to the same PR cancels an in-progress PR run. `master` runs are never cancelled.
- **Permissions:** `contents: read` only, which `actions/checkout` needs to fetch the repo with `GITHUB_TOKEN`. All other scopes are `none`. No secrets are used, and there is no AI or Claude automation.
- **Actions,** pinned to full commit SHAs:
  - `actions/checkout` v7.0.1 (`3d3c42e5aac5ba805825da76410c181273ba90b1`), with `persist-credentials: false`. Since PR #91 the Ubuntu job also has `fetch-depth: 0`: the full history and the tags, for the documentation-policy checks (about 4 seconds more, as `release.yml`'s `verify` job measured with the same checkout); the Windows job keeps the default shallow checkout.
  - `actions/setup-dotnet` v6.0.0 (`a98b56852c35b8e3190ac28c8c2271da59106c68`), with `dotnet-version: "11.0.x"` (D31; `"10.0.x"` before). It **only installs** an SDK next to the runner image's preinstalled SDKs (8.0–11.0); the root `global.json` decides which one `dotnet` uses. Before D31, runner images from 20261111.x preinstalled SDK 11.0.100 and, without `global.json`, `dotnet` picked it although setup-dotnet had asked for 10.0.x (§U).
- **Repository setting:** GitHub Actions was disabled on the repo until the owner enabled it on 2026-11-10. The API now reports `enabled: true` with `allowed_actions: selected`, `github_owned_allowed: true` and `verified_allowed: false`, so only `actions/*` actions can run. Any third-party action added to the workflow will be blocked until that setting changes.

CI commands, in order:
```bash
source .github/release-policy/release-policy.sh              # curated release notes (decision D30, §S); read-only, publishes nothing
check_curated_notes_dir .github/release-notes "ghcr.io/<owner lowercase>/campertronconsole" "$GITHUB_REPOSITORY"   # preview + SHA-256 to the job summary; any invalid fragment fails the job
command -v gawk || command -v awk; git rev-parse --is-shallow-repository; git tag -l   # the awk, Bash and Git the checker uses; a shallow checkout or one without tags fails (exit 2)
bash .github/docs-policy/test-check-docs.sh                  # the documentation-policy checker's tests; their exit code decides
bash .github/docs-policy/check-docs.sh                       # exit 0, and 3 (warnings only, with a notice), pass; 1, 2 and any other code fail with that code; output to the log and the job summary
check_global_json . && check_selected_sdk "$(dotnet --version)"   # exact global.json policy and a stable .NET 11.0 SDK (D31)
dotnet --info
dotnet restore Campertron.sln -p:Configuration=Release      # Release, matching the Release build (no project has configuration-conditional packages since §K)
check_package_policy <every */obj/project.assets.json>       # at least 5; no prerelease, no EF Core/Microsoft.Data.Sqlite/Microsoft.Extensions of another major
dotnet build Campertron.sln --configuration Release --no-restore
mkdir -p "$XDG_DATA_HOME"                                    # XDG_DATA_HOME=${{ runner.temp }}/campertron-data; must exist (see Test warnings §1)
dotnet test Campertron.sln --configuration Release --no-build --blame-hang-timeout 10m   # step timeout 15 min
# for each .csproj listed by `dotnet sln Campertron.sln list`:
dotnet list <project> package --vulnerable --include-transitive
dotnet list <project> package --vulnerable --include-transitive --format json   # counted with jq; any finding fails the job
```
- **The vulnerability scan fails the job on any finding.** That includes newly published advisories, so CI can turn red without a code change. `docker-compose.dcproj` is skipped with a message: it isn't a PackageReference project, so `dotnet list package` can't scan it. Each `dotnet list package` call restores implicitly in the default (Debug) configuration. Since the Avalonia 12 migration (§K) the Debug and Release graphs are identical, because `Avalonia.Diagnostics` (Debug-only) was removed.
- **No NuGet cache.** setup-dotnet's `cache` requires `packages.lock.json`, which this repo doesn't have. A manual cache would be about 534 MB compressed (1.2 GiB, 92 packages) to save a roughly 28–36 s cold restore.
- **Curated release notes (decision D30, §S):** the first step after checkout runs `check_curated_notes_dir` on `.github/release-notes/`. Any invalid fragment fails the required check; each composed body and its complete SHA-256 is written to the job summary for review. It needs no permission beyond `contents: read`, creates nothing and doesn't require a fragment for any version (only a publishing tag run does).
- **Documentation-policy checks (since PR #91, step 9's checks of Phase 5):** after the curated notes and before the .NET setup, the Ubuntu job shows the awk, Bash and Git the checker uses and stops (exit 2) on a shallow checkout or one without tags, runs the checker's tests `test-check-docs.sh` and runs `check-docs.sh` on the checkout. Exit 0 and 3 (warnings only: a notice, the warnings in the log and the job summary) pass; 1, 2 and any other code fail the required check with that code. The runner image has GNU awk (5.3.2 in the first run), which the checker prefers; nothing is installed, and mawk, the fallback, gives the same result. In CI the network-namespace test skips (no `unshare -rn` on the runner): 97 tests pass, one skips. Details, the tests of these steps and how to reproduce them: `.github/docs-policy/README.md` §In CI.
- **Not covered by CI:** `docker-compose.dcproj` (skipped by build and scan), and the GUI on a real platform backend (rendering, D-Bus shutdown, TD-34). The headless GUI tests (`CampertronGUITests`, §K) run in `dotnet test Campertron.sln`. Docker image builds, packaging for all 8 targets and the native console and GUI smoke tests on linux-x64, win-x64, win-arm64, osx-x64 and osx-arm64 (R8, §X) and, since S2 (§AM), on linux-arm64, the win-x86 run on 64-bit Windows and the native run of the linux/arm64 container image are covered by the release workflow's dry runs (§Release workflow), not by `ci.yml`. Since D40 `ci.yml` also runs the settings-file suites on Windows (CM1 and CM2, since D41 to D44 also CM3, CM5 and CM6: the job `Windows settings files (CM1 to CM3, CM5, CM6)` above); the rest of the tests run only on Ubuntu, and `ci.yml` runs nothing on macOS. Pull requests that change release inputs or `CampertronGUI/**` get that dry run automatically.
- **Network risks:** ~~the tests call recreation.gov.~~ Since D45 (GS1, §AJ) no test reaches the network (`NoLiveNetwork` fails the run otherwise), so CI no longer depends on recreation.gov. Before: one test made a live request; network errors retried forever in app code (the hang and step timeouts caught that), and a "REQUEST BLOCKED" response would have slept 5 minutes and exited the test host. Roadmap step 10, Phase T (PR #93, §BB) corrected the comment above `ci.yml`'s Test step, which had said that the tests extract the embedded 328 MB database, call recreation.gov and need an existing `XDG_DATA_HOME` folder (the database extracts to 226,471,936 bytes since `07e786d`, §Required environment; no test reaches the network since D45; a missing folder is created since D37), and the comment above `release.yml`'s Test step, which had said that the tests contact recreation.gov; comments only.

The first runs of this workflow on 2026-11-10 (run 42914891230 on PR #14 and the first push run 42916238884) are a historical record: `history/validation/a-w_2026-11-10_to_beta.10.md` §Continuous integration: the first runs.

## Recommended verification after changes
- [R] `dotnet build Campertron.sln`: 0 errors. Compare against the current 54-warning baseline (since §BM, D67: 56 less two CS8604 of `Load.cs`; 56 from §AQ, D51, with the same identities and positions through §AZ, the `v0.1.0` release run, to §BL; 70 from §AG to §AO; 74 from §Y to §AF; 76 from §T to §X; all compiler warnings, no NuGet, AVLN or RXUIBIND warnings).
- [R] Interactive search changes (`CampsiteSearch`, `SearchHost`, `HttpSearchTransport`, `SearchRunModel`, `RunningData.Host`, the host branch of `ProcessMonth`, `Cache.PrepareCampground`): run `CampsiteSearchTests`, `SearchHostTests` and `LoadTests`, the search suites of §Z, then the negative controls, the harness comparison of the console path (every output byte-identical) and the parallel-versus-isolated comparisons of §AJ, and the whole suite inside a network namespace. Keep the console's path unchanged (`Host` null), three requests per month in all, no process exit and no console output, no `Camp.History` and no Email settings, typed results only from the values' metadata, and every waiter of a shared month released (business-rules §19).
- [R] RIDB import changes (`PopulateDbFromRIDBFiles`, `RidbTableInserter`, the mapped models): run `PopulateTests`, then a full-export refresh in an isolated directory, and compare the checkpointed DB with the previous build's output on the same export and culture (§M: schema, counts, `EXCEPT` both ways with `rowid`, per-column `typeof`, SHA-256).
- [R] RIDB refresh changes (`RidbRefreshService` and its model since D61, `RefreshRidbRecreationData`, `RidbStagedRefresh`, `Clear`, `Web.Download`, `Compression.Extract`, the auto-refresh in `Load.DbExistsCheck`): run `RidbRefreshServiceTests`, `AtomicRefreshTests`, `PopulateTests` and `DataFolderTests`, the kill harness and the cross-process scenarios, and the full-export comparison against the in-place core (§N); since D61 also compare the console's refresh on a pseudo-terminal byte for byte with the previous build (only the archive's path in "Extracting …" may differ), with a stand-in for `ridb.recreation.gov` in a network namespace, and stall the stand-in to see the 60-second timeout end the refresh (§BD). Keep the order, the checkpoints only before the cache step, the download folder of its own and every end's cleanup (business-rules §8, ER-05).
- [R] GUI changes: run `CampertronGUITests` (since D42 including `CampgroundIdentityTests`, since D43 `SettingsEditorTests`, since D69 `NtfySettingsEditorTests` and since D70 `EmailPasswordFileEditorTests`, since D44 `SearchEditorTests`, since D60 `CurrentConfigurationTests`, since D61 `CampgroundDataTests` and since D62 `AutomaticRestartTests`, since D46 `ResultsTabTests`, `ResultsBehaviorTests` and `ResultsViewTests`; after a change to the campground lists, details or selector templates also the library's `CampgroundIdentityTests` and `CampgroundChoiceTests`, §AG), then run the GUI with `XDG_DATA_HOME="$(mktemp -d)"` so it doesn't touch real data. Expect the main window to open with "Preparing campground data" and then the tabs; also run `CampertronGUI --smoke-test` (exit 0, `SMOKE result=pass`) and a start with an invalid `General.yaml` (error state, Try again). Check Light and Dark, 720×540 and a wide window (2400), and a keyboard pass (Tab must wrap to the tab header, TD-39). Bind every database-backed view-model property exactly once (`Selections_RunOneQueryEach`), create nothing database-backed before the startup succeeded (`StartupTests`) and take colors only from theme resources (D28). If the smoke report changes, change `check_gui_smoke_report` with it. A GUI PR gets the release dry run with the native smoke tests (§X). Since Avalonia 12 the TD-34 shutdown exception has not been observed (§K, §P, §X); report it if it returns.
- [R] Results-tab changes (`ResultsViewModel`, `ResultRows.cs`, `ProgressGate`, `ResultsView`, `RunProbe`, the tab's wiring in `MainWindow`/`MainWindowViewModel`, the shutdown wait in `App`/`Program`): run the Results suites, `SmokeTestTests`, `CampgroundIdentityTests`, GS1's suites and the release-policy tests, then the negative controls and the nested-session review of §AK, and `AutoRunWordingTests` with §BE's negative controls (a retired word put back, the former post). Keep: only the Results tab uses `CampsiteSearch` (no HTTP client, `AvailabilityApi`, console pipeline, `Camp.History` or `Email.yaml` in the GUI), no file written, one operation at a time with every callback checked, `Run` off the UI thread, at most ten progress publications a second, the five-minute local hold after a block, the link check, copies only on the user's action, the close canceling the run, and the smoke report changed together with `check_gui_smoke_report` (business-rules §20).
- [R] Campground-data changes (`CampgroundDataViewModel`, `CampgroundDataView`, `DataOperationGate`, `ProgressGate<T>`, the card's wiring in `MainWindowViewModel`/`MainWindow`/`SystemSettingsViewModel`, `App.WaitForPendingRefresh`/`ReportRefreshAfterClose` and `Program.Main`'s wait): run `CampgroundDataTests`, the Results suites (the data gate), `NavigationTests`, `SmokeTestTests`, `CampgroundIdentityTests`, `RidbRefreshServiceTests` and the release-policy tests, then the negative controls and the nested-session scenarios of §BD (a refresh, a close while it downloads, a close while it installs, a failure after the close, Cancel) against a stand-in in a network namespace. Keep: nothing starts before Start refresh, the refresh only through `RefreshSeams.Run` off the UI thread, every callback checked, Cancel only before the cache step, fixed words without paths or messages, no live reload, and the process alive until a started installation has finished (business-rules §25).
- [R] Automatic-restart changes (D62: `RestartHandoff.cs`, `AutomaticRestart.cs`, `WindowPlacement.cs`, the restart in `CampgroundDataViewModel`, `MainWindow`'s capture, placement and arrival navigation, `MainWindowViewModel.Arrival`, `App.Arrival`, `Program.Main`'s claim, the smoke test's `restart=` key): run `AutomaticRestartTests`, `CampgroundDataTests`, `NavigationTests`, `StartupTests`, `SmokeTestTests`, `ThemePreferenceTests`, `ResultsViewTests` (only `AutomaticRestart` starts a process) and the release-policy tests, then the negative controls and the nested-session restart scenarios of §BD (the apphost, a maximized window, a self-contained build, the dotnet host, a failed start, unsaved changes) with an isolated `XDG_DATA_HOME`.
  Keep: the token only in the new process's environment and removed there, the handoff create-new 0600 in its 0700 folder, claimed once by a rename, bounded, strictly parsed and deleted, no shell and no retry, the old process alive until the claim, an unclaimed process ended, the gate held until the end, no restart with unsaved changes, an ordinary start unchanged and no window position remembered (business-rules §26).
- [R] Console output changes (`ConsoleUi`, `ConsoleResults`, `Menu`, the messages in `Load`, `Cache`, `Yaml`, `GetWebApiData`, `CampertronConsole/Program.cs`): run `ConsoleUiTests`, `ConsoleResultsTests`, `ConsoleOutputTests`, `ConsoleFailureTests` and `ConsoleOutputGoldenTests`, then the isolated terminal, redirection, signal and container checks of §Y. For `Menu.cs` also `MenuKeysTests`, and keep `ProcessInput` returning to the loop in `WriteMenu` (never calling it): compare a scripted pseudo-terminal session byte for byte with the build before and press 200,000 keys with a 1 MB stack (§AL). `--version`, the silent `outputTo: UnitTest` start, the library's silence on a first start and the container messages are contracts (business-rules §12).
- [R] Output changes (the producers in `Cache`/`AvailabilityApi`, `ProcessConsoleConfig`, `ResultsHtml`, `Load.OutputResults`): run `ConsoleOutputGoldenTests`, `ConsoleResultsTests`, `ResultsHtmlTests` and `HtmlResultsOutputTests`, compare a rendered page byte for byte with the previous build's (§Y), then generate a page from a real search in an isolated `XDG_DATA_HOME` (with a recording `xdg-open` on `PATH`) and check it in Chrome and Firefox at 320 px and a desktop width, Light and Dark (§Q).
- [R] Results email changes (`ResultsEmail`, `CampsiteConfig.CreateResultsEmail`, the email branch of `ProcessConsoleConfig`; D72, ER-12): run `ResultsEmailTests`, `ResultsEmailDeliveryTests`, `HtmlResultsOutputTests`, `CachePathTests`, `EmailDeliveryHistoryTests` and `NotificationDeliveryTests`, then §BR's negative controls (`ResultsEmailDeliveryTests` against a library from before D72 must fail) and look at both paths' HTML bodies in a browser in light and dark mode with a temporary profile (§BR). No test or check sends an email.
- [R] Search changes (`AvailabilityApi`, `Load.RunConsoleSearch`, `CampertronConfig`'s dates, anything reading `RunData.SiteData` or `RunData.FilteredAvailableData`): run `AvailableDatesTests`, `SharedMonthSearchTests`, `SearchIsolationTests`, `SearchDatesTests` and `ConsoleOutputGoldenTests`, then compare every search of a shared run (in both orders and in parallel) with the same search run alone, as in §Z: since D35 **any** difference is a regression, consecutive-night searches included (before D35 their differences were TD-04, §T). The cached month data must stay read-only (TD-40); a search must never read or replace another search's hits (TD-04); dates the search builds itself must not be parsed from culture-dependent text (TD-11); the Email loop's regeneration must stay quiet and must not read the search files (TD-46).
- [R] Notification-history changes (`Load.UsesNotificationHistory`, `Load.HasUnreportedEntries`, `Load.RecordNotificationHistory`, `Load.HasNewEntries`, the `Camp.History` check in `FilterData`, `Load.ReportResults`'s Email gate, the sends in `Load.OutputResults` and `CampsiteConfig.SendResultsEmail`): run `NotificationHistoryTests`, `EmailDeliveryHistoryTests` and `LoadTests`, then the isolated before/after sequences of §T (manual Console/HtmlFile repeatedly and across mode switches, Email cycles) and the refused and partial SMTP scenarios of §AA. Only the monitoring (Email and, since D67, Ntfy) may read or write `Camp.History`, a marker only after a notification that shows it was delivered (since D67 by any enabled provider; also run `NotificationDeliveryTests`), and the directory only with its first marker; keep the marker format.
- [R] Settings, exit-code and data-folder changes (`Yaml.*GetConfig*`, the example search, `CampertronConfig.GenerateSearchDates`' errors, `AvailabilityApi`'s blocked path, `Load.GetConfig`/`DesktopDataFolder`, `Compression.Unzip`): run `SettingsBootstrapTests`, `DataFolderTests`, `ConsoleFailureTests`, `AtomicRefreshTests` and `CachePathTests`, then the console checks of §AB (a commented, disabled `ZionConfig.yaml` stays byte-identical; equal dates and a blocked answer exit 1; a fresh `HOME` leaves the working directory empty; a foreign `Extract` folder survives a refresh).
- [R] YAML document editor changes (`YamlDocument`, `YamlDocumentModel`, `Yaml.SettingsDeserializer`): run `YamlDocumentTests`, `YamlDocumentPreservationTests` and `YamlDocumentRefusalTests`, then the negative controls of §AD on the changed part. Keep it free of file, console, environment and network access (the IL scan), keep every refusal and lock, never let a diagnostic quote a value, and never return text that failed a check (business-rules §13). Changing the loaders' deserializer also changes what the editor checks against: run the loaders' suites of §AB too.
- [R] Settings writer changes (`ConfigFileReplace`, `Yaml.GeneralConfigReplaceYaml`, the sample writers `Yaml.*ConvertToYaml` and their callers in `Yaml.*GetConfig*`): run `ConfigFileReplaceTests`, `GeneralYamlTimestampTests`, `SettingsBootstrapTests` and `AtomicRefreshTests`, then the negative controls and the interruption harness of §AE on the changed part. Never write a settings file in place or re-serialize it, never write over a file that exists or changed since it was read, keep the mode, the links, the backup and the strict UTF-8, and keep every message free of the file's values (business-rules §14). The Windows paths (`MoveFileExW`, the access rules, read-only files, sharing, reparse points) run natively only in the Windows CI job: a change to them needs that job green (and a new Windows-only behavior a test in `ConfigFileReplaceWindowsTests`).
- [R] Configuration validation and loader changes (`ConfigValidation`, `ConfigDiagnostic`, `ConfigFiles`, `Yaml.ReadSettingsText`/`ScanSettings`/`LoadSettingsText`, `Load.CheckEmailOutputSettings`): run `ConfigFilesTests`, `SettingsLoaderLimitsTests`, `ConfigValidationTests`, `EmailOutputValidationTests`, the CM1 and CM2 suites and `ConsoleFailureTests`, then the negative controls of §AF on the changed part. Keep the loader, the validation and the editor on the same limits and the same deserializer, refuse at run time only what would take a dangerous path, check `Email.yaml` before the Email loop searches, and never put a value of a file into a diagnostic (business-rules §15).
- [R] Email mode's start (`Load.RunConsole`, the email branch of `Load.RunConsoleSearch`, `Load.CheckSearchesToCheck`, `SearchesToCheck`, `EmailStartProblemText`, `RequireSearchesToCheck`, `Load.Prepare`, `Yaml.CampertronConfigGetConfigs`'s overload; D64, ER-02): run `EmailStartTests`, `EmailOutputValidationTests`, `SettingsBootstrapTests` and `SearchFilesTests`, then §BJ's and §BK's negative controls and the process harness (a scratch data folder, a loopback-only network namespace, redirected output, the folder snapshotted before and after: a folder with nothing to check, also one without a search file, must exit 1 within seconds with one error and no file created or changed, a usable one must keep checking).
- [R] SMTP password changes (`SmtpPassword`, `EmailConfig.SmtpPasswordFile`, `Load.CheckSmtpPassword`, `CampsiteConfig.SmtpCredential`, `ConsoleUi.ReportFailure(Exception, EmailConfig)`, the password's validation and GUI notices, since D70 the GUI's SMTP password file field and `ConfigValidation.PasswordFilePathProblem`; D66, ER-33): run ER-33's suites and the Windows CI job, then §BL's negative controls (for the GUI field §BP's) and `maintainer-procedures.md` §SMTP password sources, process and container (every source, every problem, a link chain, the containers' bind mount, Compose secret and variable, a restart policy; unique canaries searched for everywhere; the password file unchanged).
- [R] ntfy changes (`Notifications`, `NtfyConfig`, `NtfyEndpoint`, `NtfyToken`, `NtfyPublisher`, `SecretFile`, `Load.CheckNtfyOutputSettings`/`CheckNtfyToken`, `ConfigValidation.CheckNtfy`, the GUI's `NtfyStatus` and, since D69, its `Ntfy.yaml` editor (`NtfySettingsViewModel`, the session's new file and key removal; ER-10); D67, ER-34): run ER-34's suites and the Windows CI job, then §BM's negative controls (for the GUI editor §BO's) and `maintainer-procedures.md` §ntfy notifications, process and container (each provider combination, every refusal and failure, a redirect, a timeout, a restart with the same history; the containers' bind mount, Compose secret, variable and restart policy; unique canary topics and tokens searched for everywhere; loopback stand-ins only, never ntfy.sh).
- [R] Release workflow and release-policy changes (`release.yml`, `release-policy.sh`, `test-release-policy.sh`): run `bash .github/release-policy/test-release-policy.sh` (493 since §BO, 489 from §BN, 482 from §BD, 471 from §BC, 465 from §BB, 419 from §AW), for a change to the container publication also the stub-registry rehearsal (`maintainer-procedures.md` §Validation harnesses, §BB), `bash -n` on every `run:` block and the negative controls of the changed part; the pull request gets the Release dry run automatically, and every job of it, the native, 64-bit-Windows and container smoke jobs included, must be green before the merge. Never claim a native run for a platform that is only built, inspected, emulated or run as compatibility execution.
- [R] Documented examples (`examples/`, the README's settings blocks, the `CAMPERTRON_` variables the documents name): run `DocumentedExamplesTests` and `SettingsLoaderLimitsTests`; keep every example a placeholder that validates as it is documented (the ntfy topic `campertron-<random>` refused until it is replaced) and the Compose secrets at the paths the settings name (§BN).
- [R] Documentation changes (`CLAUDE.md`, `docs/ai-context/`): run `bash .github/docs-policy/check-docs.sh` (no error; warnings are recommendations or registered known defects); after a change of the checker, its `docs-policy.conf` or the documentation-policy steps of `ci.yml`, also `bash .github/docs-policy/test-check-docs.sh`. Since PR #91 CI's required job runs both, so an error blocks the merge (`.github/docs-policy/README.md` §In CI).
- [R] CI (`.github/workflows/ci.yml`) must pass on the PR, both jobs: Ubuntu: the documentation-policy tests and check (no error; since PR #91), Release build 0 errors and 54 warnings (since §BM; 56 before), tests 1,828 (1,417 + 411 GUI; 20 Windows-only tests skipped on Linux; since §BR; 1,801 from §BQ; 1,764 from §BP; 1,748 from §BO; 1,722 from §BN; 1,708 from §BM; 1,581 from §BL; 1,496 from §BK; 1,452 from §BD; 1,272 from §BC; 1,234 from §AW, the same in the `v0.1.0` release run, §AZ), no vulnerable packages; Windows (`Windows settings files (CM1 to CM3, CM5, CM6)`): the CM1, CM2 and CM3 suites, the settings editor's session suites, the search-file suites, since D66 `SmtpPasswordTests` and since D67 `NtfyConfigTests` (§AE, §AF, §AH, §AI, §BL, §BM).
- [R] `XDG_DATA_HOME="$(mktemp -d)" dotnet test Campertron.sln …`: 1,828 (1,808 passed, 20 skipped on Linux; since §BR; 1,801 from §BQ; 1,764 from §BP; 1,748 from §BO; 1,722 from §BN; 1,708 from §BM; 1,581 from §BL; 1,496 from §BK; 1,467 from §BE; 1,452 from §BD; 1,272 from §BC; 1,234 from §AW, the same in the `v0.1.0` release run, §AZ); no test reaches the network. The suites assert their results, the search's filters and dates included (since D51 `SearchFiltersTests` and `FilterSemanticsTests`; since D35 and D45 `SearchIsolationTests`, `SearchDatesTests`, `CampsiteSearchTests` and `SearchHostTests`); a few prove only that nothing crashes (`LoadTests.RunConsoleSearchTest2`, TD-02; `DataTests.LOAD_DbExists`, TD-26). What each test checks, and what no test covers: `test-coverage.md`.

## Moved sections
Roadmap step 9, Phase 2 moved these sections out of this file on 2026-11-17, from `6309671` (`history/README.md` §Phase 2 migration map has each one's source lines and proof). Labels keep their meaning and are never reused. Current material went to `release-process.md` and `maintainer-procedures.md`; historical records went to `history/` and are not current authority. The paths are relative to `docs/ai-context/`.

| Label or heading here until Phase 2 | Now in | Kind |
|---|---|---|
| "Verified results: 2026-11-10, Linux x86_64 (CachyOS), SDK 10.0.114 / MSBuild 18.0.36 / runtime 10.0.14" (the heading over the records) | `history/validation/a-w_2026-11-10_to_beta.10.md` | historical |
| §A to §W, with "Warning breakdown (unique warnings)" and "Test-run side effects (isolated dir)" | `history/validation/a-w_2026-11-10_to_beta.10.md` | historical records |
| §X to §AC | `history/validation/x-ac_preview.1.md` | historical records |
| §AD to §AX | `history/validation/ad-ax_preview.2.md` | historical records |
| §AY and §AZ | `history/validation/ay-az_v0.1.0.md` | historical records |
| §Continuous integration: the first run and the first push run of 2026-11-10 | `history/validation/a-w_2026-11-10_to_beta.10.md` §Continuous integration: the first runs | historical; the rest of §Continuous integration stays here |
| §Refreshing the README screenshots (maintainer procedure), with "README and screenshots reconciled" | `maintainer-procedures.md`, same heading | current procedure |
| §Refreshing the embedded database (maintainer procedure) | `maintainer-procedures.md`, same heading | current procedure |
| §Release workflow (`.github/workflows/release.yml`), with "Triggers and modes", "Jobs", "Publishing gates", "Permissions", "Actions", "Failure policy" and "Release procedure" | `release-process.md`, same headings | current |
| §Release workflow: "Local validation, 2026-11-10" (§Local validation in §Release history) | `history/validation/a-w_2026-11-10_to_beta.10.md` §Release workflow: local validation, 2026-11-10 | historical |
| §Release protections (applied 2026-11-11, before the first tag) [V] | `release-process.md`, same heading | current |
| §Release history | `history/releases.md`, same heading | historical |
| §Branch protection (`master`), with "Verified [V]", "Inferred, not verified [I]", "Uncertain [?]" and "Consequences [V from the rules above]" | `release-process.md`, same headings | current |
| The validation baseline in `current-state.md` §Repository (not in this file before) | §Current validation baseline, above | current |

The headings below are stubs: each keeps the text and level it had here, so a search for a cited heading (for example `### AE.`) finds where it went.

## Verified results: 2026-11-10, Linux x86_64 (CachyOS), SDK 10.0.114 / MSBuild 18.0.36 / runtime 10.0.14
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record). The lettered records below were under this heading.

### A. Baseline: unchanged `net9.0` code (commit `0e41fbf`) built with SDK 10.0.114
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### B. After migration to `net10.0` (no package changes)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### Warning breakdown (unique warnings)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### C. After removing MailKit/MimeKit (commit `d20e1ad`, branch `fix/dependency-vulnerabilities`)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### D. After updating EF Core to 9.0.19 (commit `35c888f`, branch `fix/sqlite-vulnerability`)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### E. After updating Avalonia to 11.3.14 (commit `21c08eb`, branch `fix/avalonia-dbus-vulnerability`)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### F. After removing System.Collections.NonGeneric (commit `f027b6d`, branch `cleanup/remove-system-collections-nongeneric`)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### G. FacilityAddresses state fix (branch `fix/gui-state-from-address-state-code`)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### H. Slim database and Brotli resource (branch `data/refresh-embedded-ridb-db`)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### I. Beta.4 investigation and release hardening (branch `fix/beta5-release-regressions`, 2026-11-11)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### J. Startup repair of pre-0.1.0-beta.4 databases (branch `fix/repair-stale-ridb-on-upgrade`, 2026-11-11)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### K. Avalonia 12 migration (branch `feat/avalonia-12`, 2026-11-11)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### L. Windows native debug symbols excluded from the GUI (TD-35, branch `fix/td35-exclude-windows-native-pdbs`, 2026-11-11)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### M. RIDB refresh: prepared-command import (branch `perf/ridb-refresh-population`, 2026-11-11)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### N. Failure-safe RIDB refresh (TD-36 part 1, branch `fix/ridb-atomic-refresh`, 2026-11-11)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### O. Configured cache path (TD-05, branch `fix/td-05-cache-path`, 2026-11-11)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### P. GUI visual polish (v0.1.0-beta.8, branch `feat/gui-visual-polish`, 2026-11-12)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### Q. HTML results page (v0.1.0-beta.9, branch `feat/html-visual-polish`, 2026-11-12)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### R. TD-40: parallel searches of the same campground and month (branch `fix/td40-generatedates-race`, 2026-11-13)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### S. Curated release notes in the draft (R7, decision D30, branch `ci/r7-curated-notes-in-draft`, 2026-11-13)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### T. Manual searches and notification history (branch `fix/manual-search-notification-history`, 2026-11-14)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### Refreshing the README screenshots (maintainer procedure)
Moved to [`maintainer-procedures.md`](maintainer-procedures.md) (current procedure).

### Refreshing the embedded database (maintainer procedure)
Moved to [`maintainer-procedures.md`](maintainer-procedures.md) (current procedure).

### Test-run side effects (isolated dir)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### U. .NET 11 platform migration (decision D31, branch `feat/net11-platform`, 2026-11-14)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### V. PDB-provenance checks fail the release (branch `ci/enforce-pdb-provenance`, 2026-11-14)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### W. The published image is the validated image (decision D32, branch `ci/publish-validated-image`, 2026-11-14)
Moved to [`history/validation/a-w_2026-11-10_to_beta.10.md`](history/validation/a-w_2026-11-10_to_beta.10.md) (historical record).

### X. R8: GUI startup states, layout polish and native smoke tests (decision D33, branch `feat/r8-gui-usability-native-smoke`, PR #47, 2026-11-14)
Moved to [`history/validation/x-ac_preview.1.md`](history/validation/x-ac_preview.1.md) (historical record).

### Y. Console output polish (decision D34, branch `feat/console-output-polish`, PR #49, merged as `7e325cd`, 2026-11-14)
Moved to [`history/validation/x-ac_preview.1.md`](history/validation/x-ac_preview.1.md) (historical record).

### Z. Search correctness (P2 of the deferred-work cleanup, decision D35, branch `fix/search-correctness`, 2026-11-14)
Moved to [`history/validation/x-ac_preview.1.md`](history/validation/x-ac_preview.1.md) (historical record).

### AA. Email notification history after delivery (P3 of the deferred-work cleanup, decision D36, branch `fix/email-history-after-delivery`, 2026-11-14)
Moved to [`history/validation/x-ac_preview.1.md`](history/validation/x-ac_preview.1.md) (historical record).

### AB. Settings safety and failure exits (P4 of the deferred-work cleanup, decision D37, branch `fix/settings-safety-and-exits`, 2026-11-14)
Moved to [`history/validation/x-ac_preview.1.md`](history/validation/x-ac_preview.1.md) (historical record).

### AC. `v0.1.0-preview.1`: curated notes, native-smoke dry run and publication (decision D38, 2026-11-14 to 2026-11-15)
Moved to [`history/validation/x-ac_preview.1.md`](history/validation/x-ac_preview.1.md) (historical record).

### AD. CM1: the comment-preserving YAML document editor (decision D39, branch `feat/yaml-document-editing`, 2026-11-15)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AE. CM2: safe, atomic, comment-preserving settings writers (decision D40, branch `fix/yaml-writers-preserve-and-atomic`, 2026-11-15)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AF. CM3: configuration validation and the bounded settings loader (decision D41, branch `feat/config-validation`, 2026-11-15)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AG. CM4: campground identity by FacilityID (decision D42, branch `fix/campground-identity`, 2026-11-15)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AH. CM5: GUI editing of General.yaml and Email.yaml (decision D43, branch `feat/gui-settings-editor`, 2026-11-15)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AI. CM6: GUI creation and editing of search files (decision D44, branch `feat/gui-search-editor`, 2026-11-15)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AJ. GS1: the host-neutral search service (decision D45, branch `refactor/search-core`, 2026-11-15)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AK. GS2: the GUI's Results tab (decision D46, branch `feat/gui-search-results`, 2026-11-15)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AL. S1: the console menu's loop and the documentation reconciliation (decision D47, branch `fix/console-menu-loop`, 2026-11-15)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AM. S2: native smoke tests for linux-arm64, the arm64 container image and win-x86 (decision D48, branch `ci/native-smoke-arm64-x86`, 2026-11-15)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AN. Preparation of `v0.1.0-preview.2`: the generated notes' wording, the live search comparison, a test that failed by chance and the candidate (decision D49, branch `release/preview2-preparation`, 2026-11-16)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AO. The GUI's navigation and a saved theme preference (decision D50, branch `feat/gui-navigation-and-theme`, 2026-11-16)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AP. The curated `v0.1.0-preview.2` notes re-reviewed for D50 (PR #70, branch `release-notes/v0.1.0-preview.2-navigation-theme`, 2026-11-16)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AQ. Database-backed filter selectors, stage 1: OR semantics, `filterMatching`, the filter choices and the attribute index (decision D51, branch `feat/filter-matching-foundation`, 2026-11-16)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AR. Database-backed filter selectors, stage 2: the shared selector, equipment and campsite types, the matching choice (decision D52, branch `feat/filter-selectors-equipment-types`, 2026-11-16)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AS. Database-backed filter selectors, stage 3: the campsite selectors (decision D53, branch `feat/filter-selectors-campsites`, 2026-11-16)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AT. Database-backed filter selectors, stage 4: the attribute selectors (decision D54, branch `feat/filter-selectors-attributes`, 2026-11-16)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AU. Database-backed filter selectors, stage 5: the final integration, the README and the curated notes (branch `docs/filter-selectors-final`, 2026-11-16)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AV. Two timing bounds after `master`'s CI (branch `fix/selector-timing-bound`, 2026-11-17 UTC)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AW. The campground lookup's tab labeled Campgrounds (decision D55, branch `fix/campgrounds-tab-label`, 2026-11-17 UTC)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AX. `v0.1.0-preview.2`: the candidate, the maintainer's test, the pre-tag dry run and the publication (decision D56, 2026-11-17 UTC)
Moved to [`history/validation/ad-ax_preview.2.md`](history/validation/ad-ax_preview.2.md) (historical record).

### AY. Final stable-readiness validation for `v0.1.0` (roadmap step 8, decision D57, branch `docs/stable-readiness-validation`, 2026-11-17 UTC)
Moved to [`history/validation/ay-az_v0.1.0.md`](history/validation/ay-az_v0.1.0.md) (historical record).

### AZ. `v0.1.0`: the curated notes, the candidate, the maintainer's test, the pre-tag dry run and the publication (decision D58, 2026-11-17 UTC)
Moved to [`history/validation/ay-az_v0.1.0.md`](history/validation/ay-az_v0.1.0.md) (historical record).

## Release workflow (`.github/workflows/release.yml`)
Moved to [`release-process.md`](release-process.md) (current).

### Release protections (applied 2026-11-11, before the first tag) [V]
Moved to [`release-process.md`](release-process.md) (current).

### Release history
Moved to [`history/releases.md`](history/releases.md) (historical record).

## Branch protection (`master`)
Moved to [`release-process.md`](release-process.md) (current).

### Verified [V]
Moved to [`release-process.md`](release-process.md) (current).

### Inferred, not verified [I]
Moved to [`release-process.md`](release-process.md) (current).

### Uncertain [?]
Moved to [`release-process.md`](release-process.md) (current).

### Consequences [V from the rules above]
Moved to [`release-process.md`](release-process.md) (current).
