# CLAUDE.md — Campertron

@docs/ai-context/README.md
@docs/ai-context/current-state.md

## Purpose
Campertron polls recreation.gov for campsite availability that matches user YAML search configs, and reports hits to the console, an HTML file, or email. A separate Avalonia GUI is a **campground lookup** (find campground IDs and details) that also edits the settings files (CM5), creates and edits the search files (CM6) and, since GS2, runs saved search files on request and shows their typed results in its Results tab (manual-search semantics: no `Camp.History`, no Email settings, no file written) and, since V011-2 (D60), shows the console's View Config items in Settings > System, read-only, and since V011-1 (D61) refreshes the campground data there on request. It never sends email or runs on its own.

Projects (`Campertron.sln`): `CampertronConsole`, `CampertronGUI` (Avalonia 12.1.6), `CampertronLibrary` (all business logic), `CampertronUnitTests`, `CampertronGUITests` (headless) and `docker-compose.dcproj`, on .NET 11 (`net11.0`). Details: `docs/ai-context/repository-map.md` and `docs/ai-context/architecture.md`.

## Reading order
- Claude Code loads this file and, through the two import lines above, the router `docs/ai-context/README.md` and the current state `docs/ai-context/current-state.md` (import tested: `docs/ai-context/history/README.md` §Core loading). If either is not in your context, read both completely before anything else.
- Then read only what the router's routing table names for the task. Do not load `docs/ai-context/history/`, or the historical sections the router lists, unless the route, an investigation or a discrepancy requires it.
- `current-state.md` is the only current authority for the repository and release state, the roadmap position, the authorized work, the deferred `v0.1.1` requirements (V011-1 to V011-3), blockers, risks and boundaries.
- Before a tag or release operation, and for audits, forensics and discrepancies, follow the router's §Release and audit reading policy.

## Approval boundaries
Each of these always needs separate explicit approval from the user, also inside an approved task:
- tags;
- releases, drafts or publication;
- changes to release assets or published release bodies;
- manual workflow dispatches;
- workflow or job reruns;
- publication recovery;
- repository ruleset or hosted-repository setting changes;
- genuinely material product decisions, including unresolved behavior, defaults or final user-facing terminology.

Approval for one separately controlled action never carries over to another.
Published release notes and published tags are immutable historical records unless a separately approved recovery action explicitly requires otherwise.
Editing a workflow file as part of an explicitly approved non-publication implementation is not automatically prohibited; a documentation-only task does not authorize it.

## Autonomous non-publication work
Within an approved non-publication task, proceed autonomously, without asking for routine approvals or for implementation decisions that repository evidence can settle.
This covers: complete investigation; a dedicated branch; the approved implementation; validation; commits; pushes; pull-request creation; addressing review or validation findings; a compliant merge after the required checks pass; post-merge verification; safe deletion of the completed branch locally and remotely; pruning stale remote-tracking references.

Stop and report only for:
- a genuinely material product or documentation-policy decision that repository evidence and the approved decisions cannot resolve;
- an inaccessible required file;
- a material repository or publication discrepancy;
- an uncorrectable serious blocker;
- an action outside the authorization;
- any action that separately requires explicit approval (§Approval boundaries).

## Branches and cleanup
- Every change reaches the protected `master` through a pull request from a branch (ER-32).
- Keep production code changes and documentation-only changes in separate commits (ER-24).
- After a merge, verify that the completed branch has no unique or unmerged commits; then delete it locally and remotely and prune stale remote-tracking references (`git fetch --prune`).
- Preserve `master`, protected branches, active work and every branch that holds unique or unmerged commits.

## Data safety
- Never open, list, hash, copy, inspect or query real Campertron user data, even read-only: the configuration, cache and preference folders (`CampertronConfig`, `CampertronCache`, `CampertronExtract` and `CampertronGUI` under `LocalApplicationData`, on Linux `$XDG_DATA_HOME` or `~/.local/share`), saved searches, `General.yaml`, `Email.yaml`, `Camp.History`, `RecreationDotOrg.db` and any other real application data.
- Use only repository files, Git metadata, read-only GitHub state and isolated temporary locations: `XDG_DATA_HOME="$(mktemp -d)"` for tests and local runs; `unshare -rm` with a `tmpfs` over `/home` for released programs and screenshots (`docs/ai-context/maintainer-procedures.md` §Rules for every procedure).
- The tests never reach the network (`NoLiveNetwork`) and never send email.

## Build and test essentials
```
dotnet restore Campertron.sln            # restore before clean after a TargetFramework change
dotnet build Campertron.sln -c Debug
XDG_DATA_HOME="$(mktemp -d)" dotnet test CampertronUnitTests/CampertronUnitTests.csproj   # isolate user data!
dotnet test CampertronGUITests/CampertronGUITests.csproj   # headless GUI tests; isolate themselves (Linux only)
dotnet run --project CampertronConsole
dotnet run --project CampertronGUI
dotnet run --project CampertronConsole -- --version   # version + FrameworkDescription, touches no data (also CampertronGUI)
dotnet run --project CampertronGUI -- --smoke-test   # GUI release smoke test (R8): SMOKE lines, exit 0 only if it passed; uses the normal data folder, so set XDG_DATA_HOME
bash .github/release-policy/test-release-policy.sh    # release policy, curated-notes and .NET 11 SDK/package/runtime/toolchain guard tests
docker build -f CampertronConsole/Dockerfile -t campertronconsole .   # BuildKit required (multi-arch Dockerfile)
```
⚠ The tests are integration tests. They use `LocalApplicationData/CampertronConfig` and `CampertronCache` (on Linux that is `$XDG_DATA_HOME`, else `~/.local/share`, which holds the user's real data). They extract the 226 MB DB. (Until GS1 one test called recreation.gov; since D45 no test reaches the network: `NoLiveNetwork` fails the run if one does, TD-01.) (Since the notification-history fix they no longer write `Camp.History` markers: `UnitTest` never touches it; since D36 no test creates the folder or sends mail.) Always isolate them as shown above. Create the `XDG_DATA_HOME` directory first (`mktemp -d` creates it; CI runs `mkdir -p`). Since D37 (TD-47 resolved) a missing one is created instead of the data going into the test output dir (`CampertronUnitTests/bin/<Config>/net11.0/`, build-and-test.md Test warnings §1). `CampertronGUITests` sets its own temporary `XDG_DATA_HOME` in-process and never extracts the DB; on non-Linux hosts its tests are ignored (Test warnings §8).
⚠ The root `global.json` (decision D31) selects the newest installed **stable .NET 11.0 SDK** (`11.0.100`, `rollForward: latestFeature`, `allowPrerelease: false`): never SDK 10, a prerelease or SDK 12+. Without one, `dotnet` fails with exit code 155 and Campertron's `errorMessage`; install a .NET 11 SDK (on the audit machine `/home/shaun/.dotnet11/dotnet`; the system SDKs there are 9 and 10). Run `dotnet` inside the repository: a command started elsewhere does not see `global.json`. `actions/setup-dotnet` only installs an SDK; `global.json` decides which one is used. Keep `global.json` exactly as approved: `check_global_json` (release policy) fails CI otherwise. Never work around a missing runtime or targeting pack with `AllowMissingPrunePackageData`. See `docs/ai-context/build-and-test.md`.
- Current validation baseline: `docs/ai-context/build-and-test.md` §Current validation baseline. Checks for each area: `docs/ai-context/build-and-test.md` §Recommended verification after changes.
- CI (`.github/workflows/ci.yml`) runs on every pull request to and push to `master`: `Restore, build, test, vulnerability scan` (required) and `Windows settings files (CM1 to CM3, CM5, CM6)`.

## Conventions observed (follow when editing)
- Static classes and static methods throughout. There is no DI, no interfaces, and no logging framework. Console messages go through `ConsoleUi` (decision D34); the RIDB refresh's plain progress lines are still `Console.WriteLine`.
- PascalCase locals and parameters (`ReturnList`, `ThisConfig`) and `ref` parameters for shared state (`ref RunningData`, `ref List<ConsoleConfigValue>`).
- Model classes are in the **global namespace** (`class/**`). Functions are in `CampertronLibrary.function.*`.
- YAML uses camelCase keys (`CamelCaseNamingConvention`).
- `OutputType.UnitTest` is a production enum value used to bypass interactive and looping behavior in tests.

## Validation and logging
- Since D41 the settings files have a validation layer (`ConfigValidation`, business-rules §15) that produces typed, value-free diagnostics; at run time it refuses only dangerous settings and an incomplete `Email.yaml` before the Email loop searches. Other invalid values still surface as runtime exceptions or console warnings, as before (see `docs/ai-context/business-rules.md`).
- There is no structured logging. Do not introduce a logging framework unless the task asks for it. Keep the existing console messages unless the task explicitly changes them.

## Invariant index
One line for each rule of `docs/ai-context/engineering-rules.md`. A line is a reminder, not the rule: read the whole rule before changing its area, and run the suites and negative controls it names.

### Code and behavior
- ER-01 Filter semantics (`SearchFilters`) and the `ProcessMonth`/`CalculateAvailableDates` date math are the contract; change them only when asked and record the change in `business-rules.md`.
- ER-02 Keep the retry and sleep behavior, the `Camp.History` markers and the container and email loop unless the task targets them.
- ER-03 `Camp.History` is the Email loop's history only (`Load.UsesNotificationHistory`): a marker only after its email was delivered, never created eagerly, its format kept.
- ER-04 The RIDB import writes rows through `RidbTableInserter` in EF Core's former row order; update its hand-typed mappings with the models.
- ER-05 The RIDB refresh builds and validates a staging database and installs it with `BackupDatabase`, in a fixed order; never rename, replace or delete the installed database; the cache path only from `CachePath`; since D61 only through `RidbRefreshService`, cancellable only before the cache step.
- ER-06 GUI startup: `Load.GetConfig` on a background task, nothing database-backed before it succeeded, the completion posted to the dispatcher captured on the UI thread; `Program.Main` waits for a refresh to stop or install (D61).
- ER-07 GUI lookup: colors only from Fluent theme resources, each database-backed property bound once, the TD-39 Tab-wrap workaround kept.
- ER-08 A GUI campground is its FacilityID (`CampgroundChoice`), kept exactly; never select, compare or look one up by its name or label.
- ER-09 The top-level tabs are Searches, Results, Settings, Campgrounds (the automation IDs keep the former names); the theme lives only in `CampertronGUI/preferences.json`.
- ER-10 Settings > Campertron edits `General.yaml` and `Email.yaml` only through `ConfigEditSession`; the SMTP password stays masked everywhere.
- ER-11 A cached month is read-only; a search never reads or replaces another search's hits; dates are built from numbers, never parsed from culture-dependent text.
- ER-12 `ResultsHtml` renders only from `Kind`/`Metadata`, encodes every value and stays self-contained; `ConsoleOutputGoldenTests` pins the producers' text.
- ER-13 Console messages only through `ConsoleUi`; keep the `--version`, silent-start and exit-code contracts; never print a value of `Email.yaml`.
- ER-14 The Searches tab writes a search file only on Save, Save As or New search, through `ConfigEditSession<CampertronConfig>`; never delete or re-serialize one.
- ER-15 Create the example search only when no search file exists; never write over a user's search file; never fall back to the working directory.
- ER-16 `YamlDocument` is pure and in memory, edits by text range, refuses instead of normalizing, and never quotes a value in a diagnostic.
- ER-17 Every settings file is loaded through the bounded loader and classified by `ConfigFiles`; at run time refuse only what would take a dangerous path.
- ER-18 Every settings file write goes through `ConfigFileReplace` (verified temporary file, backup, mode, links, external-change checks); keep the Windows CI job green.
- ER-19 Only the Results tab searches, only through `CampsiteSearch.Plan`/`Run`, one operation at a time, never during a refresh (D61); never `Camp.History`, `Email.yaml` or a file write.
- ER-20 `CampsiteSearch` keeps manual-search semantics: at most three requests per month, no console output, process exit or settings write; `RunningData.Host` stays null for the console.
- ER-21 `Menu.ProcessInput` never calls `Menu.WriteMenu`; keep the console calls and their order, so the terminal gets the same bytes.
- ER-22 Every include and exclude list is OR; `filterMatching` is Exact or Contains (a file without it is Contains); Campertron's index only through `RidbIndexes`.
- ER-23 A filter selector keeps the list's rows exactly as the editing session gives them (no rebuild, sort, trim or deduplication) and reads values only off the UI thread.
- ER-24 Keep production code changes and documentation-only changes in separate commits.

### Release safeguards
- Only `.github/workflows/release.yml` publishes, and only for a SemVer tag on `master`; every other run is a dry run (`docs/ai-context/release-process.md` §Release workflow).
- ER-25 Curated release notes are `.github/release-notes/v<VERSION>.md`, merged through their own pull request before the tag; never edit a published body except as approved recovery.
- ER-26 Tags (ruleset 27004221) and immutable releases are final: never re-tag; release a new version instead.
- ER-27 Every release output is .NET 11, built by one stable SDK 11 toolchain; keep every guard in CI and `release.yml`.
- ER-28 Native smoke tests run only on standard GitHub-hosted runners of the architecture they claim; win-x86 is compatibility execution; never claim more than ran.
- ER-29 The published container image is exactly the validated OCI archive; `docker-publish` builds nothing, never moves an exact version tag, and moves a stable version's `<major>.<minor>` and `latest` only forward (D59).
- ER-30 Windows GUI packages drop the SkiaSharp and HarfBuzz native PDBs through `ExcludeWindowsNativeDebugSymbols`; keep Campertron's own PDBs.
- ER-31 The `v0.1.0-beta.4` artifacts are correct; state no cause or blame beyond the investigation's record; older versions are handled by documentation only.
- No release blocker is open: D59 resolved TD-57, and its floating-tag move first runs against GHCR in the next stable publication (`docs/ai-context/current-state.md` §Release blockers, §Open risks).

### Repository
- ER-32 `master` is protected by ruleset 26990088: a pull request from a branch, the required check on an up-to-date branch, resolved conversations, a merge commit only.

## Required workflow
**Before a task:**
1. Read this file.
2. Read the relevant `docs/ai-context/*.md` files for the task.
3. Inspect the current implementation before trusting the docs. Code wins over docs.

**After a task:**
1. Build (and run the tests if safe; see the warning above) before declaring implementation work complete. If you can't run them, say so.
2. Update the docs whenever architecture, behavior, commands or feature status changes.
3. Never replace verified information with assumptions. Mark inferences as such and add unknowns to `open-questions.md`.

## Maintaining the documentation
- How the documents are organized and kept (authority map, history policy, maintenance rules, size budgets): `docs/ai-context/README.md`.
- A new engineering rule takes the next ER ID, a line in §Invariant index and a row in the router's table.
- Keep every line of this file, the router and `current-state.md` at or under 1,000 characters.
