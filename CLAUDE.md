# CLAUDE.md — Campertron

## Purpose
Campertron polls recreation.gov for campsite availability that matches user YAML search configs, and reports hits to the console, an HTML file, or email. A separate Avalonia GUI is a **read-only campground lookup** (find campground IDs and details). It does **not** search for availability.

## Solution layout (`Campertron.sln`)
| Project | Type | Entry point |
|---|---|---|
| `CampertronConsole` | Exe, net10.0 | `CampertronConsole/Program.cs` → `Load.RunConsoleSearch(Load.GetConfig())` |
| `CampertronGUI` | WinExe, net10.0, Avalonia 11.3.14 + ReactiveUI (`Avalonia.ReactiveUI` 11.3.3) | `CampertronGUI/Program.cs` → `App` → `Views/MainWindow` |
| `CampertronLibrary` | Class library, net10.0 | All business logic (`CampertronLibrary/function/**`, models in `class/**`) |
| `CampertronUnitTests` | NUnit 4 tests, net10.0 | `CampertronUnitTests/function/RecDotOrg/data/*.cs` |
| `docker-compose.dcproj` | VS Docker Compose project | `docker-compose.yml`, `CampertronConsole/Dockerfile` |

Stack: .NET 10 (`net10.0`; EF Core packages are 9.0.19), EF Core Sqlite, YamlDotNet, Newtonsoft.Json and System.Text.Json, System.Net.Mail for email (no MailKit/MimeKit). The SQLite DB ships as the embedded resource `CampertronLibrary/content/RecreationDotOrg.zip`, which is about 81 MB.

## Commands (from repo root; .NET 10 SDK + runtime + targeting pack. Verified 2026-11-10 with SDK 10.0.114: restore OK, build 0 errors / 87 warnings, tests 14/14, isolated GUI smoke test OK. CI: `.github/workflows/ci.yml` runs the Release equivalents on every PR and push to `master`)
```
dotnet restore Campertron.sln            # restore before clean after a TargetFramework change
dotnet build Campertron.sln -c Debug
XDG_DATA_HOME="$(mktemp -d)" dotnet test CampertronUnitTests/CampertronUnitTests.csproj   # isolate user data!
dotnet run --project CampertronConsole
dotnet run --project CampertronGUI
docker build -f CampertronConsole/Dockerfile -t campertronconsole .
```
⚠ The tests are integration tests. They use `LocalApplicationData/CampertronConfig` and `CampertronCache` (on Linux that is `$XDG_DATA_HOME`, else `~/.local/share`, which holds the user's real data). They extract the 328 MB DB, call recreation.gov, and write `Camp.History` markers that suppress future real notifications. Always isolate them as shown above. **The `XDG_DATA_HOME` directory must already exist** (`mktemp -d` creates it; CI runs `mkdir -p`). If it doesn't, the tests still pass but were observed writing their data into the test output dir (`CampertronUnitTests/bin/<Config>/net10.0/`) instead (build-and-test.md, Test warnings §1).
⚠ No `global.json` exists, and the repo must not be pinned to .NET 9. The SDK 10 build needs the .NET 10 runtime **and** targeting pack. If either is missing, it fails with exit code 150 or `NETSDK1226`. Never work around it with `AllowMissingPrunePackageData`. See `docs/ai-context/build-and-test.md`.

## Architecture in one paragraph
Everything lives in static classes in `CampertronLibrary`. Config (`Load.GetConfig`) picks paths: `/config` + `/cache` in a container, otherwise `LocalApplicationData/CampertronConfig` + `/CampertronCache`. It loads `General.yaml` and `Email.yaml` and ensures `RecreationDotOrg.db` exists. Search (`Load.RunConsoleSearch` → `Preload` → `AvailabilityApi.GetAvailabilitiesByCampground` in parallel per config → `PostProcess`) fetches monthly availability JSON from recreation.gov and filters it with the rules in `AvailabilityApi.FilterData`. It renders through a `ConsoleConfigValue` list that `CampsiteConfig.ProcessConsoleConfig` writes to the console or turns into HTML and email. Static RIDB data (campsites, equipment, attributes, facilities) comes from SQLite through `sqlite/Read.cs` and is cached as per-campground JSON in the cache dir. Details: `docs/ai-context/architecture.md`.

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
- Keep production code changes separate from documentation-only changes, in separate commits.
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
