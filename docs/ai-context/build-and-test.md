# Build and test

Legend: **[V]** verified (command run or file read) · **[A]** assumption, not executed · **[R]** recommendation

## Required environment
- [V] .NET SDK 10.x with the **.NET 10 runtime and the .NET 10 targeting pack**. All four projects target `net10.0`. There is no `global.json`, so the host uses the newest installed SDK.
- [V] On Arch/CachyOS, the SDK, runtime and targeting pack are separate packages. The audit machine needed `dotnet-sdk` 10.0.14.sdk114-1, `dotnet-runtime` 10.0.14 and `dotnet-targeting-pack-10.0` 10.0.14.sdk114-1. When one was missing, the failure looked like this:
  - **runtime missing:** every `dotnet` command fails with exit code 150, "You must install or update .NET to run this application … 'Microsoft.NETCore.App', version '10.0.14'"
  - **targeting pack missing:** `restore` fails with `NETSDK1226: Prune Package data not found .NETCoreApp 10.0 Microsoft.NETCore.App`. Do **not** work around this with `AllowMissingPrunePackageData`.
- [V] The GUI needs a desktop session (Avalonia `UsePlatformDetect`, `WinExe`). An isolated smoke test passed on 2026-11-10 (§E).
- [V] Docker is only needed for the container build (`CampertronConsole/Dockerfile`, base images `mcr.microsoft.com/dotnet/runtime:10.0` and `sdk:10.0`). It is **not installed** on the audit machine, so the image is unverified.
- [V] Tests and search need network access to `www.recreation.gov`. RIDB refresh needs `ridb.recreation.gov`. Restore needs `api.nuget.org`.
- [V] Disk: the embedded DB zip is 81,232,118 bytes and extracts to a 328,126,464-byte `RecreationDotOrg.db`.

## Commands (repo root)
```bash
dotnet --info                                   # expect SDK 10.0.x, runtime Microsoft.NETCore.App 10.0.x
dotnet restore Campertron.sln                   # run BEFORE clean after a TargetFramework change (see note)
dotnet clean Campertron.sln
dotnet build Campertron.sln --no-restore        # Debug
XDG_DATA_HOME="$(mktemp -d)" dotnet test CampertronUnitTests/CampertronUnitTests.csproj --no-build   # ALWAYS isolate (see warnings)

dotnet build Campertron.sln -c Release          # [A] not run
dotnet run --project CampertronConsole/CampertronConsole.csproj   # [A] interactive, not run
dotnet run --project CampertronGUI/CampertronGUI.csproj           # [A] needs a display, not run
docker build -f CampertronConsole/Dockerfile -t campertronconsole .   # [A] docker not installed
docker run -d --name campertron -v /path/to/config:/config -v /path/to/cache:/cache campertronconsole   # from README.md
```
- [V] **`clean` before `restore` fails after a TargetFramework change.** It reports `NETSDK1005: Assets file '…/obj/project.assets.json' doesn't have a target for 'net10.0'`, because `clean` doesn't restore and `obj/` still describes the old framework. Restore first.
- [V] `docker-compose.dcproj` is skipped by `dotnet build Campertron.sln` on Linux without error.
- [V] `docker-compose.yml` bind-mounts Windows host paths `C:\temp\config` and `C:\temp\cache`. Edit them for Linux or macOS.
- [V] The container works only with `outputTo: Email`. Anything else exits with code 1 ("Unsupported output type in container").

## Verified results: 2026-11-10, Linux x86_64 (CachyOS), SDK 10.0.114 / MSBuild 18.0.36 / runtime 10.0.14

### A. Baseline: unchanged `net9.0` code (commit `0e41fbf`) built with SDK 10.0.114
| Command | Result |
|---|---|
| `dotnet clean` | exit 0 |
| `dotnet restore` | exit 0. Warnings: `NU1902` MailKit 4.13.0, `NU1902` MimeKit 4.13.0 |
| `dotnet build --no-restore` | **0 errors, 89 warnings** |
| `dotnet test` (isolated) | **9/9 passed** |

The same results as with SDK 9.0.123 earlier in the audit, which also gave 0 errors, 89 warnings and 9/9 passing.

### B. After migration to `net10.0` (no package changes)
| Command | Result |
|---|---|
| `dotnet clean` (first run, stale `obj/`) | exit 1, 4× `NETSDK1005` (see the note above). It is an ordering issue, not a code issue |
| `dotnet restore` | exit 0. All 4 projects restored. 22 NuGet warnings, see below |
| `dotnet clean` (after restore) | exit 0 |
| `dotnet build --no-restore` | **Build succeeded. 0 errors, 109 warnings, 3.2 s.** Outputs in `*/bin/Debug/net10.0/` for `CampertronLibrary`, `CampertronConsole`, `CampertronUnitTests` and `CampertronGUI` |
| `dotnet build` (full, after clean) | exit 0, 0 errors |
| `XDG_DATA_HOME=<tmp> timeout 580 dotnet test CampertronUnitTests/CampertronUnitTests.csproj --no-build --logger "console;verbosity=normal"` | **Test Run Successful. 9/9 passed, 6.32 s**, run on `.NETCoreApp,Version=v10.0` |

There is one solution (`Campertron.sln`) and one test project (`CampertronUnitTests`). Both were verified.

Per-test results (net10.0):
| Test | Result | Time |
|---|---|---|
| `DataTests.LOAD_DbExists` | Passed | 106 ms |
| `LoadTests.DbExistsCheckTest` | Passed | 28 ms |
| `LoadTests.DbExistsTest` | Passed | 1 ms |
| `LoadTests.GetConfigTest` | Passed | 4 ms |
| `LoadTests.HasNewEntriesTest` | Passed | 1 ms |
| `LoadTests.PostProcessTest` | Passed | 2 ms |
| `LoadTests.PreloadTest` | Passed | 2 ms |
| `LoadTests.RunConsoleSearchTest` | Passed | 488 ms |
| `LoadTests.RunConsoleSearchTest2` | Passed | 1 ms |

### Warning breakdown (unique warnings)
| Code | net9.0 baseline | net10.0 | Notes |
|---|---|---|---|
| CS86xx nullable (8600, 8601, 8602, 8603, 8604, 8618, 8629, 8767) | 85 | 85 | identical |
| CS0169 `ReactiveViewModel._SelectedStateByState` never used | 1 | 1 | TD-08 |
| CS8981 lowercase type name (`availabilities`) | 1 | 1 | |
| NU1902 (moderate vulnerability) | 2 | 12 | MailKit 4.13.0, MimeKit 4.13.0, System.Security.Cryptography.Pkcs 8.0.1, × 4 projects |
| NU1903 (high vulnerability) | 0 | 9 | SQLitePCLRaw.lib.e_sqlite3 2.1.10 and System.Security.Cryptography.Pkcs 8.0.1 × 4 projects, plus Tmds.DBus.Protocol 0.21.2 (GUI) |
| NU1510 `System.Collections.NonGeneric will not be pruned … likely unnecessary` | 0 | 1 | CampertronLibrary |
| **Total** | **89** | **109** | |

- [V] **The new NU1902/NU1903 warnings are not new dependencies.** `dotnet list package --include-transitive` shows the same transitive versions in a pristine `net9.0` copy of HEAD (`SQLitePCLRaw.lib.e_sqlite3` 2.1.10, `System.Security.Cryptography.Pkcs` 8.0.1, `Tmds.DBus.Protocol` 0.21.2). Only the reporting changed: transitive packages are audited when targeting `net10.0`, and the warnings now appear in every project that references the library. [I] This matches the .NET 10 SDK defaulting NuGet audit mode to "all" for net10.0 projects.
- [V] `NU1510` is new with the .NET 10 SDK's package pruning. The package is still referenced and restored.

### C. After removing MailKit/MimeKit (commit `d20e1ad`, branch `fix/dependency-vulnerabilities`)
| Command | Result |
|---|---|
| `dotnet restore` | exit 0. 6 NuGet warnings (below) |
| `dotnet clean` | exit 0 |
| `dotnet build --no-restore` | **0 errors, 93 warnings**. The compiler warnings are identical to a `master` build, ignoring line numbers in the two edited files |
| `XDG_DATA_HOME=<tmp> timeout 580 dotnet test … --no-build` | **9/9 passed, 6.78 s** on `.NETCoreApp,Version=v10.0`. The real config dir was unchanged |
| `dotnet list Campertron.sln package --vulnerable --include-transitive` | `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 High (GHSA-2m69-gcr7-jv3q) in all 4 projects. `Tmds.DBus.Protocol` 0.21.2 High (GHSA-xrw6-gwf8-vvr9) in CampertronGUI. `docker-compose.dcproj` can't be scanned ("uses package.config") |

Current warning breakdown (unique): CS86xx ×85, CS0169 ×1, CS8981 ×1, NU1903 ×5 (SQLitePCLRaw ×4 projects + Tmds.DBus ×1), NU1510 ×1, **total 93**. The 16 warnings removed versus §B are all MailKit, MimeKit and `System.Security.Cryptography.Pkcs` advisories (Pkcs only came in through MimeKit).

### D. After updating EF Core to 9.0.19 (commit `35c888f`, branch `fix/sqlite-vulnerability`)
Package changes: `Microsoft.EntityFrameworkCore.Sqlite` 9.0.8 → 9.0.19 (CampertronLibrary), and `Microsoft.EntityFrameworkCore.Tools` 9.0.8 → 9.0.19 (CampertronLibrary, CampertronConsole). Resolved versions moved with them:
- `SQLitePCLRaw.*`: 2.1.10 → 2.1.12
- EF Core, `Microsoft.Data.Sqlite.Core`, `Microsoft.Extensions.*`: 9.0.8 → 9.0.19
- `Microsoft.EntityFrameworkCore.Design`: 9.0.8 → 9.0.19
- `Microsoft.Build.Framework`: 17.8.3 → 17.8.43

No package IDs were added or removed.

| Command | Result |
|---|---|
| `dotnet restore` | exit 0. 2 NuGet warnings: NU1903 Tmds.DBus.Protocol (GUI) and NU1510 |
| `dotnet clean` | exit 0 |
| `dotnet build --no-restore` | **0 errors, 89 warnings**. The compiler warnings are identical to `master`, line numbers included |
| `XDG_DATA_HOME=<tmp> timeout 580 dotnet test … --no-build` | **9/9 passed, 6.15 s** on `.NETCoreApp,Version=v10.0`. The tests query the shipped DB through the new SQLite. The real config dir was unchanged |
| `dotnet list Campertron.sln package --vulnerable --include-transitive` | Console, Library, UnitTests: no vulnerable packages. GUI: `Tmds.DBus.Protocol` 0.21.2 High (GHSA-xrw6-gwf8-vvr9). `docker-compose.dcproj` can't be scanned |

Current warning breakdown (unique): CS86xx ×85, CS0169 ×1, CS8981 ×1, NU1903 ×1 (Tmds.DBus), NU1510 ×1, **total 89**. [I] The native `libe_sqlite3.so` shipped with SQLitePCLRaw 2.1.12 contains the version string `3.53.3`.

### E. After updating Avalonia to 11.3.14 (commit `21c08eb`, branch `fix/avalonia-dbus-vulnerability`)
Package changes (CampertronGUI only):
- **Direct:** `Avalonia`, `Avalonia.Desktop`, `Avalonia.Diagnostics` (Debug), `Avalonia.Themes.Fluent` 11.3.3 → 11.3.14. `Avalonia.ReactiveUI` stays at 11.3.3: no 11.3.14 exists (last listed 11.3.8), and it needs only Avalonia >= 11.3.3. Its own dependencies (ReactiveUI 20.1.1, System.Reactive 6.0.1) are the same in 11.3.8.
- **Transitive:**
  - `Tmds.DBus.Protocol` 0.21.2 → 0.21.3 (the fix). `Avalonia.FreeDesktop` 11.3.3–11.3.13 depend on 0.21.2; 11.3.14 is the first to depend on 0.21.3.
  - `Avalonia.FreeDesktop`, `.X11`, `.Win32`, `.Native`, `.Skia`, `.Remote.Protocol`, `.Controls.ColorPicker`, `.Themes.Simple`: 11.3.3 → 11.3.14.
  - `Avalonia.BuildServices` 0.0.31 → 11.3.2, as declared by `Avalonia` 11.3.14 itself. Avalonia renumbered the package after 0.0.31.

No package IDs were added or removed, and the other three projects are unchanged.

| Command | Result |
|---|---|
| `dotnet restore` | exit 0. 1 NuGet warning (NU1510) |
| `dotnet clean` | exit 0 |
| `dotnet build --no-restore` | **0 errors, 88 warnings**. The compiler and XAML warnings are identical to `master` |
| `XDG_DATA_HOME=<tmp> timeout 580 dotnet test … --no-build` | **9/9 passed, 6.04 s**. The real config dir was unchanged |
| `dotnet list Campertron.sln package --vulnerable --include-transitive` | **No vulnerable packages** in Console, Library, GUI or UnitTests. `docker-compose.dcproj` can't be scanned ("uses package.config"), so its packages are not covered |

Current warning breakdown (unique): CS86xx ×85, CS0169 ×1, CS8981 ×1, NU1510 ×1, **total 88**.

**Isolated GUI smoke test** (2026-11-10, Wayland session via XWayland `DISPLAY=:0`, `XDG_DATA_HOME=<tmp>`):
- **Real binary** `CampertronGUI/bin/Debug/net10.0/CampertronGUI`:
  - Started, and the main window appeared after about 3 s (`WM_NAME` "Campertron", `WM_CLASS` "CampertronGUI", focused, detected via `xprop`).
  - It stayed running, and startup (`Load.GetConfig()`) created the config/cache dirs, sample YAML and the DB in the temp dir.
  - No stdout/stderr output. It was closed with SIGTERM (exit code 143).
- **Logging launcher** (scratch project only, not in the repo; it runs `Campertron.App` with the same `AppBuilder` chain plus `LogToTextWriter(Console.Out, LogEventLevel.Warning)` and unhandled-exception handlers):
  - The main window was `Campertron.Views.MainWindow` with DataContext `MainWindowViewModel`: title "Campertron", visible, active, about 1024×768.
  - Switching through all 3 tabs loaded data from the DB: "By Park" showed 450 parks, and "By City" and "By State" showed 50 states each.
  - **No Avalonia binding, XAML, layout or other warnings** were logged.
  - `MainWindow.Close()` → lifetime exit code 0, process exit code 0.
  - On shutdown, every run hit an unhandled `TaskCanceledException` from `Tmds.DBus.Protocol` (4/4 on this branch). It is pre-existing: it reproduced 3/3 on `master` with the same exception and origin (TD-34).
- **Real config dir:** all 395 entries of `~/.local/share/CampertronConfig` and `CampertronCache` (path, size, mtime, ctime) were identical before and after.

### Test-run side effects (isolated dir)
On Linux, `LocalApplicationData` resolves to `$XDG_DATA_HOME` (default `~/.local/share`). Each run:
- extracted the 328 MB DB
- created 384 cache JSON files
- made 1 live `GET https://www.recreation.gov/api/camps/availability/campground/232445/month?start_date=2026-11-01…`
- created `Camp.History` markers: 118 on the net9.0 run, 116 on the net10.0 run, because live availability changed between runs

The real `~/.local/share/CampertronConfig` listing was compared before and after each run and was unchanged.

## Test warnings: read before running `dotnet test`
1. **Real user directories** [V]. `LoadTests` uses `Load.GetConfig()` → `LocalApplicationData/CampertronConfig` and `CampertronCache`. It creates dirs, writes sample YAML and extracts the DB. Isolate with `XDG_DATA_HOME`. [A] On Windows this is `%LOCALAPPDATA%`, and no isolation recipe has been verified there.
2. **Network** [V]. `LoadTests.RunConsoleSearchTest` performs a real search for every `autoRun: true` YAML in the config dir, or the generated Zion sample if there are none.
3. **Notification markers** [V]. `OutputType.UnitTest` is not `Console`, so `Load.HasNewEntries` writes `Camp.History` markers. Against a real config dir, these suppress future real notifications.
4. **Config writes.** `GetConfig` → `DbExistsCheck` can rewrite `General.yaml` and download the RIDB export if auto-refresh is due [A].
5. **Container env.** If `DOTNET_RUNNING_IN_CONTAINER` is set, `GetConfig` requires `/config` and `/cache`.
6. **Field initializer.** `LoadTests` runs `Load.Preload(Load.GetConfig())` when the fixture is constructed.
7. **Hang risk.** The retry loops have no limit (business-rules §7). Use `timeout` or `--blame-hang-timeout` when running unattended.

There is no CI configuration in the repository [V].

## Recommended verification after changes
- [R] `dotnet build Campertron.sln`: 0 errors. Compare against the current 88-warning baseline (§E: 87 compiler + 1 NuGet).
- [R] GUI changes: run the GUI with `XDG_DATA_HOME="$(mktemp -d)"` so it doesn't touch real data. Expect the main window to open, and expect the known shutdown exception (TD-34) on close.
- [R] `XDG_DATA_HOME="$(mktemp -d)" dotnet test …`: 9/9. A pass mainly proves nothing crashes, because the tests don't assert filter results (feature-inventory.md §Test coverage).
