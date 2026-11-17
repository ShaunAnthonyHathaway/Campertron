# Build and test

Legend: **[V]** verified (command run or file read) · **[A]** assumption, not executed · **[R]** recommendation

## Required environment
- [V] A stable **.NET 11 SDK** (11.0.100 or a later 11.0 release; it contains the .NET 11 runtime and targeting pack). All six projects target `net11.0` (decision D31). The root `global.json` (`11.0.100`, `rollForward: latestFeature`, `allowPrerelease: false`) makes `dotnet` use the newest installed stable 11.0 SDK and never SDK 10, a prerelease or SDK 12+; without one, every `dotnet` command in the repository fails with exit code 155 and Campertron's `errorMessage` (§U). Run `dotnet` inside the repository: a command started elsewhere does not see `global.json`. On the audit machine the .NET 11 SDK is a per-user install, `/home/shaun/.dotnet11/dotnet` (SDK 11.0.100, runtime 11.0.0); the system packages below still provide SDKs 9.0.123 and 10.0.114, which `global.json` rejects. Before D31 there was no `global.json`, so the newest installed SDK was used (TD-31).
- [V] (Before D31, .NET 10.) On Arch/CachyOS, the SDK, runtime and targeting pack are separate packages. The audit machine needed `dotnet-sdk` 10.0.14.sdk114-1, `dotnet-runtime` 10.0.14 and `dotnet-targeting-pack-10.0` 10.0.14.sdk114-1. When one was missing, the failure looked like this:
  - **runtime missing:** every `dotnet` command fails with exit code 150, "You must install or update .NET to run this application … 'Microsoft.NETCore.App', version '10.0.14'"
  - **targeting pack missing:** `restore` fails with `NETSDK1226: Prune Package data not found .NETCoreApp 10.0 Microsoft.NETCore.App`. Do **not** work around this with `AllowMissingPrunePackageData`.
- [V] The GUI needs a desktop session (Avalonia `UsePlatformDetect`, `WinExe`). Isolated smoke tests passed on 2026-11-10 (§E, Avalonia 11) and 2026-11-11 (§K, Avalonia 12). The headless GUI tests (`CampertronGUITests`) need no display.
- [V] Docker is only needed for the container build (`CampertronConsole/Dockerfile`, base images `mcr.microsoft.com/dotnet/runtime:11.0` and `sdk:11.0`, Ubuntu 26.04). It needs **BuildKit**, the default builder since Docker 23, because the Dockerfile uses `$BUILDPLATFORM` and `$TARGETARCH`. Docker Engine **29.9.3** (with buildx) is installed on the audit machine and was used for the local container validations of §N, §O and §Y and for the `v0.1.0-beta.10` image check (§Release history); it was not installed at the start of the 2026-11-10 audit. The published image is built and smoke-tested by the release workflow (§Release workflow).
- [V] A search needs network access to `www.recreation.gov`; the tests do not since D45 (§AJ; before, one test made a live request). RIDB refresh needs `ridb.recreation.gov`. Restore needs `api.nuget.org`.
- [V] Disk: the embedded `RecreationDotOrg.db.br` is 26,616,039 bytes and extracts to a 226,471,936-byte `RecreationDotOrg.db`. Until `07e786d`, the embedded `RecreationDotOrg.zip` was 81,232,118 bytes and extracted to 328,126,464 bytes.

## Commands (repo root)
```bash
dotnet --info                                   # expect SDK 11.0.x (global.json file: <repo>/global.json), runtime Microsoft.NETCore.App 11.0.x
dotnet restore Campertron.sln                   # run BEFORE clean after a TargetFramework change (see note)
dotnet clean Campertron.sln
dotnet build Campertron.sln --no-restore        # Debug
XDG_DATA_HOME="$(mktemp -d)" dotnet test CampertronUnitTests/CampertronUnitTests.csproj --no-build   # ALWAYS isolate (see warnings)
dotnet test CampertronGUITests/CampertronGUITests.csproj --no-build   # headless GUI tests; isolate themselves (Linux only, see Test warnings §8)

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

### F. After removing System.Collections.NonGeneric (commit `f027b6d`, branch `cleanup/remove-system-collections-nongeneric`)
Package change: removed `<PackageReference Include="System.Collections.NonGeneric" Version="4.3.0" />` from `CampertronLibrary.csproj`, the only project that referenced it directly.
- **Why it was unnecessary:** the .NET 10 shared framework provides `System.Collections.NonGeneric` 10.0.0.0, in `Microsoft.NETCore.App.Ref/10.0.14`. For net10.0, the 4.3.0 package's compile and runtime assets were only `_._` placeholders, so it contributed no compiled assembly.
- **Usage check:** no source uses the assembly's types (`Queue`, `Stack`, `SortedList`, `CollectionBase`, `DictionaryBase`, `ReadOnlyCollectionBase`, `CaseInsensitiveComparer`, `CaseInsensitiveHashCodeProvider`, `CollectionsUtil`, or the forwarded `ArrayList`/`Hashtable`/`Comparer`/`IHashCodeProvider`), and none of the 4 compiled assemblies referenced it.
- **Dependency change:** only `System.Collections.NonGeneric/4.3.0` dropped out of CampertronLibrary's resolved packages. No other package was added, removed or changed in any project.

| Command | Result |
|---|---|
| `dotnet restore` | exit 0, **0 warnings**. NU1510 is gone |
| `dotnet clean` | exit 0 |
| `dotnet build --no-restore` | **0 errors, 87 warnings** (was 88). The compiler warnings are identical to `master`, line numbers included. Compiled output is unchanged versus `master`: each project's `bin/Debug/net10.0` file list, `.deps.json`, and DLL metadata (assembly refs, type defs/refs, method counts, total IL size) are identical |
| `XDG_DATA_HOME=<tmp> timeout 580 dotnet test … --no-build` | **9/9 passed, 6.70 s**. The real config dir was unchanged |
| `dotnet list Campertron.sln package --vulnerable --include-transitive` | **No vulnerable packages** in Console, Library, GUI or UnitTests. `docker-compose.dcproj` can't be scanned ("uses package.config"), so its packages are not covered |

Current warning breakdown (unique): CS86xx ×85, CS0169 ×1, CS8981 ×1, **total 87**. There are no NuGet warnings.

### G. FacilityAddresses state fix (branch `fix/gui-state-from-address-state-code`)
Production change: `Read.cs` GUI queries and `PopulateDbFromRIDBFiles` use `AddressStateCode` as the state instead of `PostalCode`. A one-time repair, `LegacyAddressLayout`, replaces DBs built from the swapped 2024 export (business-rules §8). New test classes: `AddressStateTests` (5 tests) and `LegacyAddressLayoutTests` (15 tests).

| Command | Result |
|---|---|
| `dotnet build Campertron.sln -c Release --no-incremental` | **0 errors, 87 warnings**. No new warnings |
| `dotnet build Campertron.sln -c Debug --no-incremental` | **0 errors, 87 warnings** |
| `XDG_DATA_HOME=<tmp> timeout 900 dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **29/29 passed** (9 existing + 5 `AddressStateTests` + 15 `LegacyAddressLayoutTests`) |
| `AddressStateTests` against the **old** `Read.cs`/`PopulateDbFromRIDBFiles.cs` (production files temporarily reverted) | **5/5 failed** as intended. For example, `UniqueStates` returned `"84767"` instead of `"CO","UT"`, and `AddressStateCode` stayed `" Utah "` |

- [V] `AddressStateTests` creates each DB with `RecreationDotOrgContext.Database.EnsureCreated()` in `Path.GetTempPath()/CampertronAddressStateTests-<guid>` and deletes it afterwards (`SqliteConnection.ClearAllPools()` first). It does not use `Load.GetConfig`, `LocalApplicationData`, the network or the embedded DB. `LegacyAddressLayoutTests` works the same way, in `CampertronLegacyLayoutTests-<guid>`. No temp directories were left behind.
- [V] The schema created by `EnsureCreated()` comes from the EF model, not from the shipped DB. For `FacilitiesEntries`, `FacilityAddressesEntries` and `RecAreaEntries`, the column names, types, NOT NULL flags and primary keys (`pragma_table_info`) are identical to the DB embedded at `529b8a2`. The tests don't cover the shipped DB file itself.

### H. Slim database and Brotli resource (branch `data/refresh-embedded-ridb-db`)
Commits:
- `4215b79`: stop importing the 15 unused tables, plus the refresh validation
- `07e786d`: refreshed 6-table DB shipped as `RecreationDotOrg.db.br`, Brotli extraction.

Business rules are in business-rules.md §8.

**Database** [V]
- Source: RIDB export `RIDBFullExport_V1_JSON.zip`, 573,475,696 bytes, Last-Modified 2026-11-10 18:46:38 GMT, ETag `714cf290f92ac3ae7acf37aa02eb8746`.
- Rebuilt by the in-app refresh of this branch on top of an extracted 2024 DB, so it also exercised dropping the 15 tables. Refresh time 138 s; validation passed.
- `VACUUM` brought it to **226,471,936 bytes** (SHA-256 `8712856097a91ee1…`). `integrity_check` ok, `foreign_key_check` clean.
- It holds the 6 tables + `__EFMigrationsHistory`, each with the same schema as the 2024 DB. Row counts:

  | Table | Rows |
  |---|---|
  | CampsiteAttributes | 2,407,137 |
  | Campsites | 130,948 |
  | Facilities | 15,433 |
  | FacilityAddresses | 16,505 |
  | PermittedEquipment | 441,779 |
  | RecAreas | 3,651 |

  Every row of every kept table is identical (`EXCEPT` in both directions) to a full 21-table DB built from the same export by the PR #16 importer.
- Compressed with .NET `BrotliEncoder` (quality 11, window 24) in 366 s to **26,616,039 bytes** (SHA-256 `700cbd00f0722cec…`). The `BrotliStream` round trip is SHA-256 identical. `CampertronLibrary.dll` is now 26.9 MB.

**Build and tests** [V]
| Command | Result |
|---|---|
| `dotnet build Campertron.sln -c Release --no-incremental` | **0 errors, 86 warnings**. Compared with master `b501f92` (87), exactly one `CS8604` in `Load.cs` disappeared with the removed first-run unzip code. No warning was added |
| `dotnet build Campertron.sln -c Debug --no-incremental` | 0 errors |
| `XDG_DATA_HOME=<tmp> dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **43/43 passed** (29 + 12 `RidbDatabaseTests` + 2 `EmbeddedDatabaseTests`) |

**Installation scenarios** (real Release console binary, isolated `XDG_DATA_HOME`, `outputTo: UnitTest`) [V]
| Scenario | Result |
|---|---|
| Clean install (no DB) | DB extracted from `.db.br`, SHA-256 matches; passes `RidbDatabase.Validate`; 51 states, 463 parks, Watchman under UT/SPRINGDALE. Startup **544 ms** vs 188 ms for a normal start, so extraction is about 0.35 s. No `.partial` file left |
| Existing corrected install (full 21-table 2026 DB) | Not touched (hash unchanged, no backup). GUI queries work (51 states, Watchman found). Its 15 extra tables remain until the next refresh, so `RidbDatabase.Validate` reports them; startup does not run the validation |
| Legacy 2024 install | Repaired with the bundled DB in **592 ms**: `.bak` byte-identical to the original, new DB passes the validation. The second start does nothing |
| In-app refresh of an existing full DB | The 15 tables were dropped and the validation passed (the database build above) |
| GUI clean install (Release build) | Main window after 3 s; DB extracted from `.db.br`, SHA-256 matches |

**Publish, all 8 release targets** (`dotnet publish <App> -c Release -r <rid> --self-contained true -p:ContinuousIntegrationBuild=true`, console and GUI, on linux-x64) [V]
| Target | Console / GUI publish | Package size (console / GUI) | Native libs present | Run natively here |
|---|---|---|---|---|
| linux-arm | ok / ok | 106 / 123 MB | `libe_sqlite3.so`, `libSystem.IO.Compression.Native.so`, `libSkiaSharp.so` (GUI) | no (no emulator) |
| linux-arm64 | ok / ok | 119 / 139 MB | same (`.so`) | no (no emulator) |
| linux-x64 | ok / ok | 114 / 135 MB | same (`.so`) | **yes**: published console clean install 478 ms (normal start 151 ms), legacy repair 545 ms, published GUI window after 2 s; every run produced DB `8712856097a91ee1…` |
| osx-arm64 | ok / ok | 118 / 146 MB | `libe_sqlite3.dylib`, `libSystem.IO.Compression.Native.dylib`, `libSkiaSharp.dylib` (GUI) | no |
| osx-x64 | ok / ok | 111 / 140 MB | same (`.dylib`) | no |
| win-arm64 | ok / ok | 121 / 146 MB | `e_sqlite3.dll`, `System.IO.Compression.Native.dll`, `libSkiaSharp.dll` (GUI) | no (no Wine) |
| win-x64 | ok / ok | 112 / 138 MB | same (`.dll`) | no |
| win-x86 | ok / ok | 106 / 130 MB | same (`.dll`) | no |

- In **all 16 packages**, the `RecreationDotOrg.db.br` manifest resource was read from `CampertronLibrary.dll` metadata, without loading the assembly. In each, the resource is SHA-256 `700cbd00f0722cec…` and decompresses with `BrotliStream` to the validated DB `8712856097a91ee1…`.
- `CampertronLibrary.dll` itself differs between some targets only because the SDK stamps the current git commit into `AssemblyInformationalVersion` (`1.0.0+4215b79…` vs `1.0.0+07e786d…`, committed mid-run).
- [I] The extraction is managed code (`BrotliStream`) using the runtime's own `System.IO.Compression.Native` library, which is present in every package. The 7 targets other than linux-x64 are therefore expected to behave the same, but they are **supported, not native-smoke-tested**, in line with the release plan.

### I. Beta.4 investigation and release hardening (branch `fix/beta5-release-regressions`, 2026-11-11)
**Report.** After `v0.1.0-beta.4` was published, two symptoms were reported on Linux x64, and the release was suspected to be broken:
- the GUI "By City" and "By State" state lists showed ZIP codes such as `01005`, and `01005` led to the city BARRE
- the console's View Config showed `Framework => .NET 9.0.0 (X64)`.

**1. The published beta.4 artifacts are correct** [V]. Checked in isolated temporary data directories:
- Source: the product code is identical between the `v0.1.0-beta.4` tag (`5392aad`) and `master` (`97b5e54`). The state fix (PR #16, merge `b501f92`) and the corrected 6-table DB (PR #17, merge `bdb8fb3`) were both merged before the tag.
- Release record: release 441205400 is `immutable=true`. All 17 assets were uploaded 2026-11-11 03:31:29–03:31:44 UTC, before publication at 03:31:45, and none was updated afterwards. A fresh download of the linux-x64 console and GUI archives matches `SHA256SUMS.txt` and the GitHub asset digests (console `b67435ca…`, GUI `91716c06…`), and their build-provenance attestations verify.
- Published linux-x64 console, clean install, View Config driven through a pseudo-terminal: `Framework => .NET 10.0.15 (X64)`.
- Published linux-x64 GUI library (`0.1.0-beta.4+5392aad…`) on the DB its console extracted (SHA-256 `8712856097a91ee1…`): 51 two-letter states; MA lists BARRE and 7 other cities; `01005` lists nothing; Watchman under UT/SPRINGDALE; layout Corrected; `RidbDatabase.Validate` passes.
- Upgrade from the 2024 DB (extracted from `529b8a2`, SHA-256 `285c3483…`): **before** the repair, the beta.4 library shows ZIP codes as states (1,149 ZIP-like values, `01005` → BARRE, MA empty). The published console **and** the published GUI both replaced that DB at startup (`.bak` kept). Afterwards they gave 51 states, a Corrected layout, and validation passed.
- All 16 published archives: checksums OK; every `runtimeconfig.json` is `net10.0` with `Microsoft.NETCore.App` 10.0.15; `System.Private.CoreLib.dll` `10.0.15-servicing.26529.108`; no .NET 9 reference. GHCR image `0.1.0-beta.4`: `DOTNET_VERSION=10.0.15` on amd64 and arm64.
- No `global.json`, `Directory.Build.*`, publish profile, `RollForward` or `RuntimeFrameworkVersion` exists; all four projects target `net10.0`.

**2. Provenance of the executables that showed the symptoms** [V unless marked]. Established read-only from the machine where the symptoms were seen: archives, file hashes, filesystem timestamps, the browser's download records, the systemd user journal and the shell history. Nothing was modified.
- **The locally downloaded beta.4 GUI archive is the published asset.** `~/Downloads/campertron-gui-0.1.0-beta.4-linux-x64.tar.gz` (73,684,346 bytes) matches the GitHub digest, `SHA256SUMS.txt` and a fresh download byte for byte. Its extracted folder `~/Downloads/campertron-gui-0.1.0-beta.4-linux-x64/` matches the archive file for file (248 files). It is the .NET 10.0.15 build whose state queries return two-letter states (§1).
- **The GUI executable that ran was a different, older file.** The systemd user journal records `Started /home/shaun/Downloads/campertron-linux-x64/CampertronGUI` at 2026-11-11 05:23:01 and 05:24:38 (local). It records no start of the `CampertronGUI` in the beta.4 folder; its retained history begins 2026-11-09. The launched file (75,144 bytes, SHA-256 `d07add83…`) is not the beta.4 executable (78,192 bytes, `eb3dbc51…`).
- **What the older folder contains.** `~/Downloads/campertron-linux-x64/` holds 253 files, byte-identical to `~/Downloads/campertron-linux-x64.zip`. The browser's download records show that zip came from the `v0.1-beta3` release asset URL on 2026-11-06. Its Campertron assemblies carry the version `1.0.0+a7d2192…` (commit `a7d2192`, the `v0.1-beta3` branch head). Its embedded database is byte-identical to the 2024 `RecreationDotOrg.zip` in the repository. It is `net9.0`, and all 184 of its runtime files are byte-identical to Microsoft's .NET 9.0.0 runtime pack. It shares no Campertron, runtime or configuration file with beta.4; the only common files are 6 identical third-party NuGet DLLs.
- **The two folders do not overlap.** The beta.4 GUI archive puts all of its entries under its own top-level folder `campertron-gui-0.1.0-beta.4-linux-x64/`, and it contains no `CampertronConsole`. The 2024 zip has no top-level folder; its extraction created `campertron-linux-x64/`. That folder has no subfolders, and its mtime (2026-11-06 19:04:46) shows that no entry has been added, removed or renamed in it since then. Nothing shows that the beta.4 extraction overwrote or mixed files into it.
- **The beta.4 console asset was not found locally.** There is no `campertron-console-0.1.0-beta.4-linux-x64.tar.gz` and no beta.4 console anywhere in `/home`, `/tmp`, `/opt` or `/usr/local`, nor in the Trash. The browser's download records have no entry for it under any name or URL. `~/Downloads` was last modified at the GUI extraction (05:22:50). The available evidence does not establish that the console asset ever finished downloading on this machine.
- **The console executable that reported .NET 9 was therefore not established to be the beta.4 console.** The shell history shows `./CampertronConsole` at 05:27:42 in the beta.4 GUI folder, which contains no console, and at 05:28:20 in `campertron-linux-x64/`, whose `CampertronConsole` is the 2024 `net9.0` build (`163d75cd…`).
- **The behavior matches the older code.** The older code reads `FacilityAddresses.PostalCode` as the state (business-rules §10). On a copy of that machine's database, which has the corrected address layout, the older library lists 1,171 ZIP-like "states" and `01005` → BARRE. The beta.4 library lists state codes and MA → BARRE (business-rules §8).
- **Unknown** [?]:
  - why the `CampertronGUI` in `campertron-linux-x64/` was opened instead of the one in the beta.4 folder (the journal records a start through the desktop session, not the action that triggered it);
  - what happened to the intended beta.4 console download.

**3. What is affected.** Every pre-0.1.0-beta.4 version reads the state from `PostalCode`. It therefore lists ZIP codes as states on any database with the corrected address layout: the database that 0.1.0-beta.4 and later install, or one refreshed from current RIDB data. Its binaries can't be changed, so the protection is documentation and `--version` (business-rules §10, decisions D20). Not affected: the beta.4 executables, on clean installs and on upgrades.

**Hardening** (no beta.4 product defect was fixed; no release made):
- `--version` for CampertronConsole and CampertronGUI (`CampertronLibrary/function/Base/RuntimeInfo.cs`): prints `<app> <InformationalVersion>` and `Framework => <FrameworkDescription> (<arch>)`, then exits before any configuration, DB or window. Used by the smoke tests, and lets users identify a copy.
- Release workflow guards (§Release workflow): `check_project_frameworks` in verify, `check_publish_runtime` on each of the 16 publish outputs, `--version` runtime check of both published linux-x64 apps against the bundled runtime, `ReleaseCheck` GUI-state validation of the published library and its extracted DB, and the container runtime checks.
- Release notes: a required "Compatibility with older versions" section (`render_notes`, `validate_notes`). README: an upgrade note.
- Tests: `RuntimeInfoTests` (3) and `EmbeddedDatabaseTests.EmbeddedDatabase_GuiStateQueries_ReturnStateCodesNotZipCodes` (the GUI state queries on the shipped DB).

**Validation, 2026-11-11, SDK 10.0.114 / runtime 10.0.14** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release --no-incremental` / `-c Debug --no-incremental` | 0 errors, 86 warnings each (unchanged) |
| `XDG_DATA_HOME=<tmp> dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **47/47 passed** (43 + 3 `RuntimeInfoTests` + 1 `EmbeddedDatabaseTests`). The real `~/.local/share/CampertronConfig` listing was unchanged |
| Vulnerability scan (4 solution projects + `ReleaseCheck`) | 0 vulnerable packages |
| `bash .github/release-policy/test-release-policy.sh` | **67/67 passed** (27 existing + 3 notes tampering + 37 .NET 10 guard cases) |
| Mutation check of the policy tests (guard deliberately broken in a copy) | caught each time: CoreLib check (3 failures), included-framework version (1), compatibility warning (1), project TFM (2) |
| `check_publish_runtime` on real outputs | beta.4 console and GUI pass; the 2024 `v0.1-beta3` build fails (`tfm is "net9.0"`) |
| Workflow file | parsed with YamlDotNet; job graph, `needs`, permissions and publishing `if:` conditions unchanged; all 31 `run:` scripts pass `bash -n` |
| Packaging scripts #08–#11 for all **16** (app, RID) pairs, as extracted, on a clean export of the working tree, `VERSION=0.0.0-dryrun.900` | all published, passed the contents check and the new .NET 10 check (runtime pack 10.0.14 locally), and were archived (console 61.8–66.8 MB, GUI 69.9–77.7 MB). Info-ZIP replaced by a Python stand-in |
| Smoke scripts #12–#15 on the local linux-x64 archives (`xvfb-run` stand-in using the real display) | console clean install (226,471,936-byte DB), GUI ran 30 s, both `--version` reported `.NET 10.0.14 (X64)` = bundled runtime, `ReleaseCheck` passed for GUI and console (51 states) |
| Negative checks | `ReleaseCheck` with the beta.4 library on the 2024 DB: exit 1, 11 failed checks (ZIP states, `01005` → BARRE, MA empty, Legacy layout, validation). Step #14 with a runtimeconfig claiming 10.0.15 while the process ran 10.0.14: exit 1 |
| Assemble scripts #21–#24 on the 16 local archives | 16 names, 16 checksums OK, notes (with the compatibility section) validated |
| `--version` on the Debug/Release builds with no display | console and GUI print version and `.NET 10.0.14 (X64)`, exit 0, create nothing in `XDG_DATA_HOME` |

Not run locally: Docker (not installed; the new container step runs in the PR dry run), Info-ZIP, `xvfb-run`, Windows and macOS. [A] On Windows, `CampertronGUI.exe --version` is a WinExe and prints only when its output is redirected (for example `| more`); not verified.

### J. Startup repair of pre-0.1.0-beta.4 databases (branch `fix/repair-stale-ridb-on-upgrade`, 2026-11-11)
**Report.** Before a manual "Refresh Ridb Data", the beta.5 GUI listed full state names next to their codes (for example `Arizona` and `AZ`). After the refresh, only the codes were listed.

**Finding** [V] (business-rules §8): the **embedded DB was not the cause**. Through the published beta.5 library:
- the embedded DB (`8712856097a91ee1…`) lists 51 two-letter states, AZ only, and passes `Validate`;
- the refreshed live DB (`14f10e57…`, 233,791,488 bytes) is row-for-row identical to it in all six tables and `__EFMigrationsHistory`;
- the live DB **before** the refresh (`d5f4025f…`, 354,697,216 bytes, last refreshed 2026-11-06 with the pre-PR #16 importer) lists 60 entries, including `ARIZONA` next to AZ, and still has the 15 omitted tables. It has the corrected layout, so the legacy-only startup repair kept it, and startup never runs `Validate`.

The embedded resource `CampertronLibrary/content/RecreationDotOrg.db.br` is **unchanged** (SHA-256 `700cbd00f0722cec…`, 26,616,039 bytes). The fix generalizes the startup repair instead (decision D22).

**Validation, 2026-11-11, SDK 10.0.114 / runtime 10.0.14** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Debug --no-incremental` / `-c Release --no-incremental` | 0 errors, 86 warnings each (unchanged) |
| `XDG_DATA_HOME=<pre-created tmp> dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **65/65 passed** (47 + 17 `LegacyAddressLayoutTests` + 1 `StartupRepairTests`). The real `~/.local/share/CampertronConfig` listing was unchanged |
| Mutation check (in a temporary copy) | detecting only the legacy layout: 6 tests fail; not validating the bundled DB: 1 test fails |
| Vulnerability scan (4 solution projects + `ReleaseCheck`) | 0 vulnerable packages |
| `bash .github/release-policy/test-release-policy.sh` | 67/67 |
| Detection cost (median of 15, real DBs) | current DB 14.0 ms, stale DB 14.3 ms, of which the new omitted-table check is 0.25–0.29 ms (the existing layout scan is 12.9–13.9 ms) |
| Published linux-x64 console and GUI, isolated directories | stale real DB repaired on the first start in 2.45 s (console) and through the GUI start. Second start: nothing, 151 ms. Current embedded DB and newer refreshed DB: untouched, 149 / 148 ms. Details in business-rules §8 |
| Packaging scripts #08–#11 for all **16** pairs, as extracted from the workflow, on a clean export of the branch (`VERSION=0.0.0-dryrun.902`; each archive checked and deleted at once) | all published and passed the contents check and the .NET 10 check (runtime 10.0.14 locally). Every archive's `CampertronLibrary.dll` contains the exact `RecreationDotOrg.db.br` bytes |
| Smoke scripts #12–#15 on the local linux-x64 archives | console clean install and GUI start both extracted `8712856097a91ee1…`; `--version` reported `.NET 10.0.14 (X64)` for both; `ReleaseCheck` passed (51 states) |

Not run locally: Docker, Windows and macOS. The real stale DB used in the scenarios came from the maintainer's data folder before its refresh. Only the scratch copy existed afterwards, and it was deleted when the scratch space was cleared, so later checks use the synthetic fixtures of the tests.

### K. Avalonia 12 migration (branch `feat/avalonia-12`, 2026-11-11)
Commits: `bd35f88` (GUI packages and source), `7a3db44` (headless GUI tests), `318a376` (stale `ci.yml` comment), then the docs commit. Based on `8476688`. Decision D23.

**Package changes (CampertronGUI)** [V]
| Package | Before | After |
|---|---|---|
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` | 11.3.14 | **12.1.6** |
| `Avalonia.Diagnostics` (Debug only) | 11.3.14 | **removed**: no 12.x exists, and the app never called `AttachDevTools()`. Nothing replaces it (no `AvaloniaUI.DiagnosticsSupport`, no paid tooling) |
| `Avalonia.ReactiveUI` | 11.3.3 (deprecated on nuget.org: "Now maintained by ReactiveUI team as ReactiveUI.Avalonia package") | **removed** |
| `ReactiveUI.Avalonia.Reactive` | — | **12.1.6** (the System.Reactive-compatible flavor, D23) |

- Transitive [V]: ReactiveUI 20.1.1 → `ReactiveUI.Reactive`/`ReactiveUI.Core` 26.0.1, plus `ReactiveUI.Binding.Reactive` 9.1.0 (with its source generator and analyzer), `ReactiveUI.Primitives*` 9.0.0, `ReactiveUI.SourceGenerators` 4.2.0. System.Reactive 6.0.1 → **7.0.0**. Splat 15.1.1 → 21.0.0. DynamicData 8.4.1 dropped. SkiaSharp 2.88.9 → **3.119.4**, HarfBuzzSharp 8.3.1.1 → 8.3.1.3, Tmds.DBus.Protocol 0.21.3 → **0.94.1** (outside GHSA-xrw6-gwf8-vvr9's `>= 0.22.0, < 0.92.0`; Avalonia 12.0.0's 0.90.3 would have failed the scan), MicroCom.Runtime 0.11.0 → 0.11.6, ANGLE 2.1.25547.20250602 → 2.1.27548.20260419. New: `Avalonia.HarfBuzz`, `Avalonia.FreeDesktop.AtSpi` (Linux accessibility).
- `Avalonia.BuildServices` stays at **11.3.2**. It is a separately versioned build-tooling package, its newest version, and Avalonia 12.1.6 itself declares `[11.3.2, )`. No other 11.x Avalonia package is in any restore graph.
- With `Avalonia.Diagnostics` gone, no project has a configuration-conditional package: the Debug and Release restore graphs are identical (GUI 66 packages, GUI tests 83).

**Source changes** [V]: `Program.cs` uses `ReactiveUI.Avalonia.Reactive.AppBuilderExtensions.UseReactiveUI(Action<ReactiveUIBuilder>)` with an empty configuration (`_ => { }`, as in the official 12.1.2 template); the package has no parameterless overload. In the `.Reactive` distribution `ReactiveObject`, `RaiseAndSetIfChanged` and `RaisePropertyChanged` are in `ReactiveUI.Reactive`, and `WhenAnyValue` in `ReactiveUI.Binding.Reactive`, so the two view models changed `using` lines only. `Subscribe(Action<T>)` still binds to System.Reactive's `ObservableExtensions`. No XAML, `ViewLocator`, `App` or `MainWindow` change was needed.

**Migration-sensitive behavior** [V]
- **WhenAnyValue is source-generated.** The compiled `CampertronGUI.dll` contains 5 `__Intercept_WhenAnyValue_*` interceptors (one per call in `ReactiveViewModel`) and no reference to `RuntimeObservationFallback`. No RXUIBIND diagnostic was reported.
- **TabItem DataContext** (Avalonia 12 fixes #20541, #20856): each tab's content still resolves `{Binding ReactiveViewModel}` against `MainWindowViewModel`, and its details panel `{Binding CampgroundByName}` against `ReactiveViewModel`, headless and on the real display.
- **Compiled bindings:** the window already used `x:CompileBindings="True"`. The compiler's data-type inference from `DataContext="{Binding …}"` still works under Avalonia 12 (no AVLN diagnostics, bindings verified at run time).
- **Implicit global usings:** `ReactiveUI.Reactive` 26.0.1's `buildTransitive` props add `global using ReactiveUI.Binding.Reactive;` and `…ObservableForProperty;` to projects with `ImplicitUsings` (here `CampertronGUITests`). That namespace has its own `ViewLocator`, so the tests write `Campertron.ViewLocator`. `CampertronGUI` has `ImplicitUsings` off and is not affected.

**Build, tests and scans** [V] (Linux x86_64, SDK 10.0.114, runtime 10.0.14)
| Check | Result |
|---|---|
| `dotnet restore Campertron.sln -p:Configuration=Debug` / `Release` | exit 0, 0 NuGet warnings each |
| `dotnet build Campertron.sln -c Debug --no-restore --no-incremental` / `-c Release` | **0 errors, 86 warnings** each. Same set as the `8476688` baseline (86 unique, identical once line numbers are ignored): 8 GUI warnings moved one line down because of the added `using`. **0 AVLN, 0 RXUIBIND**, 0 warnings in `CampertronGUITests` |
| `XDG_DATA_HOME=<tmp> dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **77/77 passed** in 14 s: `CampertronUnitTests` 65/65, `CampertronGUITests` 12/12 (about 1 s) |
| The same in Debug | 77/77 |
| Mutation checks (temporary edits, reverted) | removing the `WhenAnyValue(SelectedCity)` subscription: 2 GUI tests fail; adding a `ReflectionBinding` to a missing property: 6 GUI tests fail (Avalonia 12 binding errors reach the test log sink) |
| Vulnerability scan (CI logic, 5 solution projects), Debug and Release restore graphs | 0 vulnerable packages |
| `bash .github/release-policy/test-release-policy.sh` | 67/67. `check_project_frameworks .` accepts all projects, including `CampertronGUITests` |
| Real `~/.local/share/CampertronConfig` and `CampertronCache` (path, size, mtime, ctime) | identical before and after the whole session (397 entries) |

**Publish, all 8 release targets** [V] (`dotnet publish <App> -c Release -r <rid> --self-contained true -p:ContinuousIntegrationBuild=true`, on linux-x64; each output then got the release workflow's "Check package contents" and `check_publish_runtime`). Every one of the 16 published, passed both checks and bundles runtime 10.0.14. Console packages have exactly the same file list and sizes as with Avalonia 11. GUI packages (files / MB on disk, Avalonia 11.3.14 → 12.1.6):

| Target | GUI files | GUI MB | GUI native libraries (Avalonia 12) | Run natively here |
|---|---|---|---|---|
| linux-arm | 247 → 260 | 123 → 128 | `libSkiaSharp.so`, `libHarfBuzzSharp.so`, `libe_sqlite3.so` | no: build, publish and static inspection only |
| linux-arm64 | 248 → 261 | 139 → 145 | same | no: static only |
| linux-x64 | 248 → 261 | 135 → 140 | same | **yes** (below) |
| osx-arm64 | 248 → 261 | 146 → 149 | `.dylib` equivalents + `libAvaloniaNative.dylib` | no: static only |
| osx-x64 | 248 → 261 | 140 → 143 | same | no: static only |
| win-arm64 | 249 → 264 | 146 → 252 | `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll` (ANGLE), `e_sqlite3.dll` **+ `libSkiaSharp.pdb`, `libHarfBuzzSharp.pdb`** | no: static only |
| win-x64 | 249 → 264 | 138 → 243 | same | no: static only |
| win-x86 | 248 → 263 | 130 → 237 | same | no: static only |

- Every GUI package lost `Avalonia.ReactiveUI.dll`, `ReactiveUI.dll` and `DynamicData.dll`, and gained `Avalonia.HarfBuzz.dll`, `Avalonia.FreeDesktop.AtSpi.dll` (managed, also on Windows and macOS), 14 `ReactiveUI.*`/`Splat.*` assemblies and, on Windows, the two native PDBs. (Corrected 2026-11-12 from "12"; counted in the `v0.1.0-beta.7` packages against `v0.1.0-beta.6`, which give the same file counts: 11 `ReactiveUI.*` (`ReactiveUI.Avalonia.Reactive`, `ReactiveUI.Binding.Avalonia.Reactive`, `ReactiveUI.Binding.Reactive`, `ReactiveUI.Core`, `ReactiveUI.Disposables`, `ReactiveUI.Primitives`, `ReactiveUI.Primitives.Avalonia.Reactive`, `ReactiveUI.Primitives.Core`, `ReactiveUI.Primitives.Reactive`, `ReactiveUI.Reactive`, `ReactiveUI.SourceGenerators`) and 3 `Splat.*` (`Splat.Builder`, `Splat.Core`, `Splat.Logging`). Net change per GUI package: 16 added, 3 removed.) `libSkiaSharp` grew by about 2 MB (Skia 3).
- **Windows size growth (TD-35):** `SkiaSharp.NativeAssets.Win32` 3.119.4 and `HarfBuzzSharp.NativeAssets.Win32` 8.3.1.3 ship native debug symbols in `runtimes/win-*/native/` (`libSkiaSharp.pdb` 84–87 MB, `libHarfBuzzSharp.pdb` 21 MB), and the self-contained publish copies them. That is about +105 MB extracted and, by a deflate estimate, about +27 MB per Windows GUI zip. They are not needed at run time. They were left in place because excluding them changes packaging, which was outside the migration's scope. **Since mitigated:** §L excludes them (TD-35, D24).

**Native runtime checks, linux-x64 only** [V] (real display: Wayland session, XWayland `DISPLAY=:0`; Xvfb is not installed here, so the release workflow's `xvfb-run` path was not run locally and is left to the PR dry run)
- Published console clean install (release smoke script): exit 0, DB 226,471,936 bytes, no partial file. Published GUI: still running after 30 s (`timeout` exit 124), DB created. Both DBs `8712856097a91ee1…`. Both `--version` outputs, without a display: `.NET 10.0.14 (X64)` = bundled runtime, `check_framework_report` passed. Same results for the Avalonia 11 baseline packages.
- Real-window screenshots (`import`) of the published Avalonia 11 and 12 GUIs at start: visually equivalent (0.75 % of pixels differ, small vertical offsets in the empty details panels). Fluent theme, borders and shadows, tabs and text look the same.
- A scratch launcher (not in the repo) ran `Campertron.App` with the production `AppBuilder` chain plus `LogToTextWriter(Warning)` and unhandled-exception handlers, in an isolated `XDG_DATA_HOME` with the real extracted DB. Window `Campertron.Views.MainWindow`, visible and active, scaling 1.45. All three tabs: content DataContext `ReactiveViewModel`, presented in the window. By Park listed 463 parks; Zion National Park → SOUTH CAMPGROUND (UT) → ID 272266 in the details panel. By City and By State listed 51 states; UT → 56 cities / 248 campgrounds. Rendered tab images and real-window captures show correct text, wrapping, scroll viewers and ComboBoxes. **No Avalonia warning or error** was logged.
- Shutdown (`MainWindow.Close()`): lifetime exit code 0, process exit code 0, **no unhandled exception in 5/5 runs**. The same launcher built from `8476688` (Avalonia 11.3.14) in the same session hit TD-34 in **3/3** runs; because the launcher keeps the process alive 3 s after the lifetime returns, the late exception terminated the process with exit code 134 (SIGABRT). See TD-34.
- Observed pre-existing data, not a migration effect: the shipped DB has 7 reservable Utah campgrounds with an empty `City`, so `Read.UniqueCities("UT")` lists `""` first and the City box can show an empty selection.

**Avalonia 12 platform support tiers** (official "Supported platforms" page, read 2026-11-11; recorded here only, the RID list, README and release notes are unchanged. Later, the curated `v0.1.0-beta.7` release notes added a known-limitation line about `win-x86` and `linux-arm`; see open question #23)
- Windows: Tier 1 Windows 11 24H2 x64/ARM64; Tier 2 Windows 11 22H2 x64/ARM64 and Windows 10 22H2 x64; **Tier 3 (commercial support only): earlier Windows 10/11 builds, x64 and x86**. So `win-x86` is Tier 3, and Windows 10 on ARM64 is not listed.
- macOS: Tier 1 macOS 26 (ARM64, x64); Tier 2 macOS 15 and 14; Tier 3 macOS 13 and earlier.
- Linux desktop: x64 and ARM64 only (Tier 1 Ubuntu 25.x, Fedora 43, Debian 13; Tier 2 Ubuntu 16.04–24.x, Fedora 30–42, Debian 9–12). **Desktop `linux-arm` (armv7) is not listed**; ARM32 appears only for Raspberry Pi OS under embedded Linux. Skia needs glibc ≥ 2.17. Avalonia uses X11 directly; Wayland is an opt-in package since 12.1.0 (not referenced).
- The Avalonia 11 docs listed no architectures, so this is a formalization rather than a removal. All 8 targets still receive native Skia and HarfBuzz binaries (verified in the nupkgs and publish outputs) and remain supported Campertron targets.

### L. Windows native debug symbols excluded from the GUI (TD-35, branch `fix/td35-exclude-windows-native-pdbs`, 2026-11-11)
Commits: `e0fe9fb` (MSBuild target), `4279eb4` (release guard), `53e9148` (temporary Windows validation workflow), `b985bda` (empty commit, only to retrigger that workflow), `e798b22` (workflow removed), then the docs commit. Based on `c6c2070`. Decision D24. **Status: mitigated in Campertron, not resolved upstream.**

**Cause** [V]: `SkiaSharp.NativeAssets.Win32` 3.119.2 and later (in use: 3.119.4, the minimum Avalonia 12.1.6 allows) and `HarfBuzzSharp.NativeAssets.Win32` 8.3.1.3 and later (in use: 8.3.1.3) put `libSkiaSharp.pdb` and `libHarfBuzzSharp.pdb` next to the DLLs in `runtimes/win-{x64,x86,arm64}/native/`. NuGet classifies everything there as a `native` asset, so the SDK adds the PDBs to `NativeCopyLocalItems` (output of `ResolvePackageAssets`), copies them into every self-contained Windows publish, and lists them in `CampertronGUI.deps.json`. The last versions without them are 3.119.1 and 8.3.1.2; every version Avalonia 12.1.6 accepts (up to SkiaSharp 4.156.0, HarfBuzzSharp 8.3.1.5) has them. Upstream: mono/SkiaSharp issues #4165 and #4259; PR #5424 (moves the PDBs to symbol packages) is open with changes requested (2026-11-11). Both packages are MIT. No official opt-out exists: `CopyDebugSymbolFilesFromPackages` covers only `lib/` symbols, the packages' `ShouldIncludeNative*` properties exist only in their .NET Framework targets and remove the DLLs too, and `ExcludeAssets="native"` removes the DLLs.

**Implementation** [V]
- `CampertronGUI.csproj`, target `ExcludeWindowsNativeDebugSymbols`, `AfterTargets="ResolvePackageAssets"`: removes from `NativeCopyLocalItems` only items whose `Extension` is `.pdb` **and** whose `NuGetPackageId` is `SkiaSharp.NativeAssets.Win32` or `HarfBuzzSharp.NativeAssets.Win32`. No other package, file type or project is affected; non-Windows RIDs and the console have no matching items.
- Removing the items, rather than setting `CopyToPublishDirectory=false` on them, also removes the `.deps.json` entries: a plain publish reuses the build `.deps.json` (`_UseBuildDependencyFile`), which is generated from these items. A prototype with `CopyToPublishDirectory=false` dropped the files but kept the entries.
- **Caveat:** `NativeCopyLocalItems` and its `NuGetPackageId` metadata are SDK implementation details, not a documented API. If a future SDK renames them, the target silently matches nothing and the PDBs return; the release guard below catches that.
- **Guard** (`release.yml`, "Check package contents", Windows GUI packages only): fails if `libSkiaSharp.dll`, `libHarfBuzzSharp.dll` or `av_libglesv2.dll` is missing, if `libSkiaSharp.pdb` or `libHarfBuzzSharp.pdb` is present, or if `CampertronGUI.deps.json` lists either PDB as a native asset (`jq`). Campertron's `CampertronGUI.pdb` and `CampertronLibrary.pdb` are expected and stay in every package.

**Local validation** (linux-x64, SDK 10.0.114, compared with `master` `c6c2070`) [V]
| Check | Result |
|---|---|
| Restore; build Debug and Release (`--no-incremental`) | 0 errors, **86 warnings** each, identical to `master` including line numbers; no MSBuild warning from the target |
| Package graphs of all 5 projects, Debug and Release | unchanged (GUI 66, GUITests 83, Console 46, Library 45, UnitTests 39) |
| Tests, Release and Debug (isolated `XDG_DATA_HOME`) | 77/77 each (65 + 12 GUI); again 77/77 in Release after the workflow removal |
| `test-release-policy.sh`; `check_project_frameworks` | 67/67; OK |
| Vulnerability scan, Debug and Release graphs | 0 in all 5 projects |
| All 16 publishes | every package passes the committed "Check package contents" step (with the guard) and `check_publish_runtime` |
| Guard fixtures (step extracted from `release.yml`) | 8/8 as expected: fixed win-x64 GUI passes; a package with either PDB, a fixed package with the old `.deps.json`, or one missing `libSkiaSharp.dll` or `av_libglesv2.dll` fails; linux-x64 GUI and win-x64 console are not checked |
| linux-x64 smoke | console clean install exit 0, DB 226,471,936 bytes; GUI alive at 30 s (real display; Xvfb not installed locally); both `--version` `.NET 10.0.14 (X64)` = bundled; `ReleaseCheck` 13/13 for GUI and console |
| Real `~/.local/share` data | identical before and after (397 entries) |

**Package contents, same version string as `master`** [V]: each Windows GUI package lost exactly the two PDBs and exactly their two `.deps.json` native entries. Every other file keeps its exact size, including `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll`, `e_sqlite3.dll`, `CampertronGUI.pdb` and `CampertronLibrary.pdb`. The other 13 packages are identical to `master` (files, sizes, `.deps.json` assets).

| Target | Files | `libSkiaSharp.pdb` | `libHarfBuzzSharp.pdb` | `.deps.json` bytes | Extracted saving |
|---|---|---|---|---|---|
| win-x64 | 264 → 262 | 84,033,536 | 20,918,272 | 69,437 → 69,221 | **104,952,024** |
| win-x86 | 263 → 261 | 86,761,472 | 20,705,280 | 69,353 → 69,137 | **107,466,968** |
| win-arm64 | 264 → 262 | 84,938,752 | 20,967,424 | 69,461 → 69,241 | **105,906,396** |

**Zip sizes** (bytes). "Before" is the real PR #26 dry-run archive (`0.0.0-dryrun.11`); "after" is that size minus the compressed size of the two PDB entries in it [V arithmetic, not a re-zipped archive]. A local deflate-6 re-zip of the fixed outputs gave reductions within 0.6 MB of these (25.2–26.4 %). The PR dry run for this branch gives the definitive sizes.
| Target | Before | After (expected) | Reduction | beta.6 (Avalonia 11) |
|---|---|---|---|---|
| win-x64 | 104,769,089 | 78,320,500 | −26,448,589 (−25.2 %) | 76,200,604 |
| win-x86 | 101,320,434 | 74,669,211 | −26,651,223 (−26.3 %) | 72,602,528 |
| win-arm64 | 102,247,291 | 76,108,718 | −26,138,573 (−25.6 %) | 73,922,496 |

**Windows runtime validation** [V]: temporary branch-only workflow `td35-windows-validation.yml` (push trigger, `contents: read`, no artifacts or uploads), removed in `e798b22`. Run [43115931458](https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43115931458) on `b985bda`, 2026-11-11 22:56–22:59 UTC, **success, 3/3 jobs**:
| Job | Runner image | OS / process arch | Files | GUI alive at 30 s | Modules loaded (Skia, HarfBuzz, ANGLE, SQLite) | `--version` |
|---|---|---|---|---|---|---|
| [win-x64](https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43115931458/job/130843794131) | `windows-latest` = windows-2025-vs2026 20261103.298.1, Windows 10.0.26100 | X64 / X64 | 262 (142.1 MB) | yes | 160 modules; all 4 loaded | `.NET 10.0.15 (X64)` = bundled |
| [win-x86](https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43115931458/job/130843793672) | `windows-latest` = windows-2025-vs2026 20261103.298.1, Windows 10.0.26100 | X64 OS, x86 process (WOW64) | 261 (133.9 MB) | yes | 160 modules (read through 32-bit Windows PowerShell); all 4 loaded | `.NET 10.0.15 (X86)` = bundled |
| [win-arm64](https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43115931458/job/130843794050) | `windows-11-arm` = windows-11-vs2026-arm64 20261101.203.1, Windows 10.0.26200 | **Arm64 / Arm64 (native, not emulated)** | 262 (150.2 MB) | yes | 159 modules; all 4 loaded | `.NET 10.0.15 (Arm64)` = bundled |
- Every job (SDK 10.0.404 on the runners): publish succeeded; both native PDBs absent; the four native DLLs and both Campertron PDBs present; `.deps.json` lists the four native DLLs (Skia 3.119.4, HarfBuzz 8.3.1.3, ANGLE 2.1.27548.20260419, e_sqlite3 2.1.12) and no PDB.
- Data isolation: the `Local AppData` known-folder registry value was pointed at `$RUNNER_TEMP\td35-localappdata` and a new process was verified to resolve it before the GUI started; it was restored afterwards (`always()`). The GUI created `CampertronConfig\RecreationDotOrg.db` (226,471,936 bytes) there; the runner's real `CampertronConfig` and `CampertronCache` did not exist before or after.
- The GUI wrote nothing to stdout or stderr. `--version` was captured through a pipe in Git Bash, so piped output of the `WinExe` works (open question #20); its first line was `CampertronGUI 0.0.0-td35check.2+b985bda…`, and `check_framework_report` passed.
- Limitations: the GUI ran on the runner's non-interactive desktop; rendering was not inspected (no screenshots) and no UI interaction was performed. The x86 job ran on x64 Windows (WOW64), the only way GitHub-hosted runners run x86. Runners used SDK 10.0.404 / runtime 10.0.15, local validation SDK 10.0.114 / runtime 10.0.14.
- First attempt: run 43096376900 (on `53e9148`) was stranded in `queued` with 0 jobs by a GitHub Actions outage (githubstatus.com: "Actions is experiencing degraded availability", major outage). After recovery it could be neither cancelled (API: "Cannot cancel a workflow run that is completed" / force-cancel "has not been queued yet", 409) nor rerun ("This workflow is already running", 403), and still showed `queued` when this was written. The empty commit `b985bda` retriggered the workflow without changing it. Actions artifact downloads also returned HTTP 502 during the outage, so the earlier dry-run zips could not be re-downloaded for a direct comparison.

### M. RIDB refresh: prepared-command import (branch `perf/ridb-refresh-population`, 2026-11-11)
Commits: `ace9ad7` (import), `8b21be6` (tests), then the docs commit. Based on `2fa6f9a`. Decision D25; TD-36.

**Measured bottleneck** [V] (Phase 1, live RIDB export 573,475,696 bytes, ETag `714cf290…`, the export the embedded DB was built from; Linux x64, Ryzen 5 5600X, NVMe, SDK 10.0.114 / runtime 10.0.14, culture en-US; every run a fresh process on a fresh copy of the embedded DB, input files in the OS cache)
- The previous import added all 3,015,453 rows to one `DbContext` and called `SaveChanges` once. `SaveChanges` took **60.4 s** of a **69.4–71.1 s** `Populate`. It executed **3,015,453 separate SQL commands** (one per row; 1 transaction), managed memory grew from 3.5 to 6.7 GB during the save, peak RSS was **6.1 GB**, 30 GB were allocated and garbage collection paused for 15.8–16.5 s.
- The same rows in the same order through one prepared Microsoft.Data.Sqlite command per table took about 6.4 s including the commit, so about **54 s of the save was EF Core overhead**, not SQLite. Parsing took 3.1 s, `Add` 6.4 s, `DetectChanges` 0.8 s, `Clear.ClearDatabase` 0.6 s, validation 1.6 s, extraction 0.2 s. The download (about 50 s at 11.5 MB/s, one sample) is network-bound and unchanged.
- Rejected, measured: `AddRange` (74.6 s total), `AutoDetectChangesEnabled = false` (71.3 s, ×2), per-table `SaveChanges` + `ChangeTracker.Clear()` in one transaction (69.7–70.7 s, 6.3 GB), `MaxBatchSize(1000)` (no effect: still 3,015,453 commands). Durability pragmas could save at most the 0.4 s commit. Deferring indexes does not apply (only primary-key indexes; rows already arrive in key order). No third-party bulk-insert package was needed.

**Design** [V] (`RidbTableInserter`, called by `PopulateDbFromRIDBFiles.Populate`)
- JSON loading, FacilityAddresses normalization, console messages, `Clear.All`/`ClearDatabase`, validation, `General.yaml` and the extract-directory cleanup are unchanged.
- One transaction on the `RecreationDotOrgContext`'s own connection; per table one prepared, parameterized `INSERT` (constant SQL, parameters created once, values replaced per row); commit only after all six tables. No EF `Add`/`SaveChanges` for these rows, no pragma, schema, index or package change.
- Row order = EF Core's `SaveChanges` order (EF Core 9 `ModificationCommandComparer`): tables in ordinal table-name order, rows by the complete primary key, component by component in model key order, each with `Comparer<T>.Default`. For strings that is the **current culture**, so the physical order is culture-dependent, as it was (TD-36). Several application queries have no `ORDER BY`, so the order is preserved exactly.
- Hand-typed mappings per table (explicit columns, parameters and value assignments; `DBNull.Value` for nulls; no reflection). `PopulateTests` compares them with the EF model and the shipped DB (model-drift guard).
- Null rows throw (`InvalidDataException`); null or duplicate keys are rejected by SQLite's NOT NULL / PRIMARY KEY constraints (`SqliteException`). The previous import threw `InvalidOperationException` from EF for duplicate or null keys; the end state is the same.

**Benchmarks, new import** [V] (same conditions; production `Populate` timed from outside, 4 runs; the first run after the build, `new0`, is included and is not slower)
| Run | `Populate` | Clear + `Populate` + validate | Peak RSS | Allocated | GC pause |
|---|---|---|---|---|---|
| new0 | 12.55 s | 14.78 s | 1,954 MiB | 5,611 MB | 0.54 s |
| new1 | 12.68 s | 14.88 s | 1,954 MiB | 5,611 MB | 0.56 s |
| new2 | 12.52 s | 14.72 s | 1,955 MiB | 5,611 MB | 0.57 s |
| new3 | 12.56 s | 14.76 s | 1,954 MiB | 5,611 MB | 0.54 s |
| Previous import (Phase 1, 4 runs) | 69.4–71.1 s | 71.6–73.3 s | about 6,110 MiB | 30,072 MB | 15.8–16.5 s |

So `Populate` is about **5.6× faster** and peak memory about **3.1× lower**; a full refresh including the download goes from about 122 s to about 65 s on this connection. Stage breakdown (3 instrumented runs calling the production per-table insert methods; the sort timed separately with the same comparisons): parse + normalize 2.98–3.20 s, sort 3.02–3.12 s, insert 6.38–6.67 s, commit 0.40 s, validation 1.60–1.72 s. Cold-cache runs could not be measured (no root to drop the page cache); all runs, before and after, read the JSON from the OS cache.

**Equivalence on the full export** [V] (7 runs: new0–new3 and the 3 instrumented runs, against the previous import's output on the same export and culture, and against the embedded DB)
- `PRAGMA integrity_check` ok; `foreign_key_check` clean. `sqlite_master` (tables, the 7 automatic primary-key indexes, SQL) identical; `pragma table_info` (columns, types, NOT NULL, primary-key order) identical.
- Per table, row counts equal (2,407,137 / 130,948 / 15,433 / 16,505 / 441,779 / 3,651, plus `__EFMigrationsHistory` 1); `EXCEPT` in both directions 0; `rowid`-aware `EXCEPT` 0; per-column `typeof` counts identical for all 70 columns.
- Size 233,791,488 bytes, 57,078 pages, 0 free pages, WAL mode. After `PRAGMA wal_checkpoint(TRUNCATE)` every file is **byte-identical** to the previous import's output: SHA-256 `2c28f2d53f337fe98cd7a7c50ae5d8152a1ac6043d201c563810576cdc9c0b3b`.
- `ReleaseCheck` (the branch's published linux-x64 console library) on the new database: 13/13 PASS, 51 states.

**Failure behavior** [V] (unchanged, TD-36): `Clear.ClearDatabase` still commits the deletes before the import. With malformed JSON, a duplicate key in the first or in the last inserted table, a null row or a null key, the exception propagates, the insert transaction rolls back, all six tables are empty, `RidbDatabase.Validate` fails ("the state list is empty") and the extract directory remains; the previous EF import ends in the same state (`PopulateTests`). An atomic refresh is a separate future project.

**Repository validation** [V]
| Check | Result |
|---|---|
| Restore; build Debug and Release (`--no-incremental`) | 0 errors, 86 warnings each; the warning sets are identical to `2fa6f9a` including line numbers |
| Package graphs, all 5 projects, Debug and Release | identical to `2fa6f9a`; no project, solution or package change |
| Vulnerability scan, Debug and Release graphs | 0 in all 5 projects |
| `XDG_DATA_HOME=<tmp> dotnet test Campertron.sln`, Release and Debug | **96/96** each (84 `CampertronUnitTests` = 65 + 19 `PopulateTests`, 12 GUI) |
| Mutation checks on `RidbTableInserter` (temporary, reverted) | caught: ordinal comparer, swapped composite-key order, unsorted table, swapped values, text in a REAL column, dropped column, changed nullability, changed key order, skipped null rows, commit after a failure. A bool bound as text `"1"` is stored as INTEGER by column affinity and is not a difference |
| `test-release-policy.sh`; `check_project_frameworks` | 67/67; OK |
| Published linux-x64 console clean install (isolated) | exit 0; embedded DB extracted unchanged (`8712856097a91ee1…`), no partial file |
| Published linux-x64 GUI (isolated, real display) | still running after 30 s, DB `8712856097a91ee1…`, no output; both `--version` `.NET 10.0.14 (X64)` |
| Real `~/.local/share/CampertronConfig` and `CampertronCache` | unchanged (397 entries) |

### N. Failure-safe RIDB refresh (TD-36 part 1, branch `fix/ridb-atomic-refresh`, 2026-11-11)
Decision D26, business-rules §8 (*Failure-safe refresh*). Based on `676ee5a`. Linux x64 (CachyOS, Ryzen 5 5600X, NVMe btrfs), SDK 10.0.114 / runtime 10.0.14, SQLite 3.53.3 (e_sqlite3 via SQLitePCLRaw 2.1.12), culture en-US.

**Phase 1 findings that shaped the design** [V] (scratch prototypes, isolated directories):
- Rename-based installation is unsafe: a hot WAL left next to the installed path was replayed onto a file renamed into place (the renamed OLD file opened as the NEW data), and an idle connection in another process kept reading the old inode after a rename. SQLite documents both (howtocorrupt.html §1.4, §2.5); .NET documents no atomicity for `File.Move(…, overwrite: true)`. The staging file renamed into place also differs from the in-place result in 3 header bytes (change counter, schema cookie, version-valid-for).
- `BackupDatabase` into the installed WAL database is one write transaction: 28/28 SIGKILL/SIGTERM kills during it left the DB fully old or fully new with `quick_check` ok; an interrupted one left the main file byte-identical. A reader holding a snapshot keeps it; new connections see the new data.
- A read-only connection to a WAL DB leaves a 0-byte `-wal` and a 32 KiB `-shm` behind (as in real data folders); harmless.
- An EF `Read.*` call leaves 3 handles on the DB file in the idle pool until the pool is cleared.

**Busy handling** [V]: Microsoft.Data.Sqlite's `BackupDatabase` (EF Core repo, `SqliteConnection.cs`) calls `sqlite3_backup_step(-1)` once; `Default Timeout` is applied only to commands, and `Open()` sets no busy timeout. Without a busy timeout a competing writer makes the step fail immediately. The installation therefore sets `PRAGMA busy_timeout` (30,000 ms) on the destination. Measured: a competing write transaction (same process, other connection, and another process) makes the installation fail with `SqliteException`, `SqliteErrorCode` 5 (`SQLITE_BUSY`), after the timeout, the DB byte-identical; a writer that releases within the timeout lets it succeed (`AtomicRefreshTests`, cross-process scenarios below).

**Deterministic tests** [V]: `AtomicRefreshTests`, 29 tests at first (34 after the General.yaml fix below; feature-inventory.md §Test coverage), 2 s. Mutation checks (temporary, reverted), each caught: cache cleared after the installation (3 tests fail), timestamp written before the staging work (16), no targeted pool release (1, open staging handle), no busy timeout (1), in-place clear of the installed DB (14), prefix-only stale-directory match (2), no repair lock (2). A repair lock opened with `FileShare.ReadWrite` instead of `None` is still exclusive against a `FileShare.None` holder on Unix (shared `flock`), so that weaker mutation is not detected.

**Full export** [V] (live export 2026-11-11, ETag `102488648295dd185cdbe8539adf4dcd`, 573,490,128 bytes; the same export as Phase 1). Every run a fresh process on a fresh copy of the embedded DB, input from the OS cache. "Current" is the pre-TD-36 in-place core (`ClearDatabase` + `Populate` + `Validate` on the installed DB); "new" is the production `RefreshData` with the download replaced by the extracted directory (scratch harness, below). Two sessions of 3 runs each:

| Run | Current: clear + populate + validate | New: template + clear + populate + validate + cache + install (to "Installed") | New: whole `RefreshData` incl. `quick_check`, cleanup, `General.yaml` | New populate | Peak RSS current / new | Peak config dir current / new |
|---|---|---|---|---|---|---|
| A1 | 15.66 s | 16.82 s | 17.20 s | 13.26 s | 1,959 / 1,959 MiB | 448 / 670 MiB |
| A2 | 15.42 s | 16.78 s | 17.17 s | 13.26 s | 1,956 / 1,963 MiB | 448 / 670 MiB |
| A3 | 15.78 s | 17.69 s | 18.07 s | 13.86 s | 1,957 / 1,963 MiB | 448 / 670 MiB |
| B1 | 15.54 s | 16.96 s | 17.36 s | 13.37 s | 1,952 / 1,964 MiB | 448 / 670 MiB |
| B2 | 15.77 s | 17.36 s | 17.75 s | 13.60 s | 1,958 / 1,964 MiB | 448 / 670 MiB |
| B3 | 16.18 s | 17.22 s | 17.61 s | 13.56 s | 1,957 / 1,960 MiB | 448 / 670 MiB |

- **Byte-identical in all 6 runs**: current and new installed DB SHA-256 `a825ce994716b77585ca67db132ba68b5a2dd6a8d6f7e59f6b2e75070c7b078e` (also the Phase 1 hash on this export).
- Stage times of the new path: template backup 0.39–0.51 s, clear 0.62–0.72 s, populate 13.26–13.86 s, validate 1.59–1.69 s, cache step < 1 ms (3 files), installation (backup + close-time checkpoint) 0.91–0.99 s, then `quick_check`, staging removal and the `General.yaml` write together 0.38–0.40 s.
- **Overhead vs the in-place core in the same session: +1.4 to +2.3 s** (mean +1.8 s), of which the post-install `quick_check` and cleanup are about 0.4 s. Peak RSS unchanged (+≤ 12 MiB). Peak disk in `ConfigPath` +222 MiB (staging DB and its WAL next to the installed DB).
- **Accepted by the maintainer (2026-11-11):** staging through installation took **16.78–17.69 s**; **one of the six runs (A3, 17.69 s) exceeded the provisional 17.5 s target by 0.19 s**, the other five were 16.78–17.36 s. The same-session in-place control was also slower than Phase 1 (whole core 15.42–16.18 s vs 14.39–14.50 s; populate 13.13–13.87 s vs 12.73–12.89 s), with a desktop session (browsers) running. Population runs unchanged production code and tracked the same-session control (13.26–13.86 s vs 13.13–13.87 s), not the Phase 1 numbers. The mean overhead was about 1.8 s, all six full-export outputs were byte-identical, and peak memory was effectively unchanged. Not every run met 17.5 s; no production change was made to recover 0.19 s, and `quick_check`, durability and the cache order were not weakened for performance.
- **Confirmation run after the General.yaml fix** (one run, same export): in-place core 15.31 s; new path staging through installation 16.57 s, whole `RefreshData` 16.97 s; peak RSS 1,956 / 1,962 MiB; peak `ConfigPath` 448 / 670 MiB; both SHA-256 `a825ce99…` (byte-identical).
- **Long-lived reader** (another process holds a read transaction for the whole refresh): the refresh succeeded in 17.08–17.44 s; "Another connection is still reading the previous data…" was printed; the WAL stayed at 235,165,512 bytes next to the 226,471,936-byte DB until the reader finished; peak `ConfigPath` 663 MiB during the refresh, and 440 MiB (DB + WAL) after it until the reader ended. After the reader ended, a checkpoint completed, `quick_check` ok, SHA-256 `a825ce99…`.

**Kill harness** [V] (manual, repeatable; scratch, not in the repository). A .NET console referencing `CampertronLibrary` runs the production `RefreshData(false, config, () => <extracted export>, Clear.RefreshCachePath())` in an isolated `XDG_DATA_HOME` (`CampertronConfig/` with the embedded DB marked `OLDMARK` in its first facility, `General.yaml` with `outputTo: UnitTest`, `autoRefresh: false`, `lastRidbDataRefresh: 2020-01-02T03:04:05.0000000Z`; `CampertronCache/` with 3 files, or 150,000 for the cache points) and timestamps every console line. A Python driver (`python3 -I`, `ctypes` inotify, no packages) SIGKILLs it at each point, triggered by a production console message plus a delay or by an inotify event in `ConfigPath`, and then checks: the DB after recovery (`PRAGMA quick_check`, old or new by the marker and row counts), WAL size before recovery, staging directories, remaining cache files, the timestamp in `General.yaml`, the next start (`Load.GetConfig` → `DbExistsCheck`, then `Read.UniqueStates` and `UniqueParks("UT","SPRINGDALE")`), and that the next refresh's stale cleanup removes the staging directory (`RefreshData` with a provider that stops right after the cleanup). Rules: DB `quick_check` ok and fully old or fully new; new DB ⇒ no old cache file left; old DB ⇒ timestamp unchanged; next start ok; stale staging removed. No sidecar was touched by hand.

| Kill point (trigger) | Result, 3 passes (the last on the final code) |
|---|---|
| template creation ("Preparing a staging copy" + 0.15 s) | OLD, ok, cache and timestamp unchanged, staging left → removed by the next refresh |
| staging clear ("Clearing database tables" + 0.3 s) | same |
| population ("Populating Database" + 6 s) | same |
| after staging validation ("Clearing cache" + 0 s, 150,000 cache files) | OLD, ok, all 150,000 cache files still there |
| during cache invalidation ("Clearing cache" + 0.4 s) | OLD, ok, 132,575–133,549 of 150,000 cache files left (accepted: old DB, partly empty cache) |
| immediately before `BackupDatabase` ("Installing the refreshed database" + 0 s) | OLD, ok, WAL 0 bytes, cache empty |
| during `BackupDatabase` (+0.3 s, +0.6 s) | OLD, ok; uncommitted WAL of 108–223 MB discarded by recovery |
| around the commit and the close-time checkpoint (+0.80, 0.85, 0.90, 0.95, 1.00 s) | NEW, ok; committed WAL of 235,165,512 bytes replayed by recovery; timestamp still the original; cache empty |
| after the installation, during `quick_check` ("Installed the refreshed database" + 0 s) | NEW, ok, timestamp original (next start may refresh again), staging left → removed |
| during cleanup (inotify: deletion of the staging `RecreationDotOrg.db`, armed after "Installed…") | NEW, ok, timestamp original, staging left → removed |
| during the timestamp write (inotify: open of `General.yaml`, armed after "Installed…") | NEW, ok, **`General.yaml` 0 bytes, next start fails** (`NullReferenceException` in `Load.DbExistsCheck`, `Load.cs` line 264): **TD-37**, the pre-existing truncating `StreamWriter`, not changed here |

All 3 passes: 15/16 points pass, the 16th is TD-37 each time (fixed afterwards, see *General.yaml timestamp write* below; 31/31 on the final code). The first pass's cleanup trigger fired on SQLite deleting the staging `-journal` during the template copy (a second template-creation kill, also a pass); it was then armed after "Installed…" and restricted to the staging `RecreationDotOrg.db`. LastRidbDataRefresh was never advanced before a successful installation.

**Cross-process scenarios** [V] (same harness, separate processes):

| Scenario | Result |
|---|---|
| Competing writer holds a write transaction for 60 s from the validation on | installation waited 30 s, then `SqliteException` "SQLite Error 5: 'database is locked'" from `RidbStagedRefresh.Install`; DB SHA-256 unchanged (`6361e781…`), OLD, `quick_check` ok, timestamp original, staging removed, cache empty (cleared before the installation) |
| Competing writer releases after 12 s | the installation waited about 11.5 s, then succeeded; SHA-256 `a825ce99…` |
| Idle SQLite connection (Python `sqlite3`, no transaction) open across the refresh | refresh succeeded; the same connection read the new data afterwards |
| Two refreshes started 0.3 s apart | the first succeeded; the second returned `false` in 0.01 s: "Another RIDB refresh is running (The process cannot access the file '…/RecreationDotOrg.refresh.lock' because it is being used by another process.). Nothing was changed." |
| Refresh lock held by another process | `false`, nothing changed (DB, cache, timestamp) |
| Repair lock held by another process | `IOException` at "Installing the refreshed database"; DB unchanged, timestamp original |
| Startup repair in another process during the installation (installed DB with omitted tables, so `RepairIfNeeded` takes the repair lock) | the startup printed "Could not check or repair …: The process cannot access the file …" and continued normally; the refresh succeeded; the omitted tables were gone; the next start repaired nothing |

**Repository validation** [V]
| Check | Result |
|---|---|
| `dotnet restore Campertron.sln` | OK |
| `dotnet build Campertron.sln -c Debug --no-incremental` / `-c Release --no-incremental` | 0 errors, **84 warnings** each (86 before: a CS8604 and a CS8602 in `RefreshRidbRecreationData.cs` disappeared; no new warning) |
| `XDG_DATA_HOME="$(mktemp -d)" dotnet test Campertron.sln -c Release --blame-hang-timeout 10m` | **125/125** (113 `CampertronUnitTests` = 84 + 29 `AtomicRefreshTests`, 12 GUI) |
| `dotnet test CampertronGUITests/CampertronGUITests.csproj` | 12/12 |
| `bash .github/release-policy/test-release-policy.sh` | 67/67 |
| `dotnet list package --vulnerable --include-transitive`, each solution `.csproj` and `ReleaseCheck` | no vulnerable packages. `dotnet list Campertron.sln …` exits 1 only because it can't scan `docker-compose.dcproj` (packages.config), as before |
| Real `~/.local/share/CampertronConfig` and `CampertronCache` | unchanged (listing, `General.yaml` and DB SHA-256 compared before and after) |

**General.yaml timestamp write (TD-37 fix, commits `cfebb9c`, `563d722`)** [V]. (Superseded by D40, §AE: the same same-directory rename, now of a file in which only the timestamp was edited, with a backup, permissions, links and an external-change check; the record below is D26's writer.) The kill point "during the timestamp write" above left an empty `General.yaml` and a failing next start in all three passes. The timestamp is now written by `Yaml.GeneralConfigReplaceYaml` → `ConfigFileReplace.Replace` (business-rules §8 step 11): complete document to `General.yaml.<32 hex>.tmp` in `ConfigPath`, `StreamWriter.Flush` + `FileStream.Flush(flushToDisk: true)`, close, read back and validate (identical text, deserializes, re-serializes identically), `File.Move(…, overwrite: true)`; the destination is never truncated or deleted first; the temporary file is removed on failure, and stale ones at the next refresh start.
- **Evidence for the operation:** .NET 10 `FileSystem.Unix.cs` `MoveFile` with `overwrite` calls `Interop.Sys.Rename` (copy + delete only on `EXDEV`); `pal_io.c` `SystemNative_Rename` = `rename()`, `SystemNative_FSync` = `fcntl(F_FULLFSYNC)` on macOS, else `fsync`. POSIX `rename()`: the new name "shall remain visible … and refer either to the file referred to by new or old", and on failure (other than `EIO`) the file named by new "shall be unaffected". .NET 10 `FileSystem.Windows.cs` → `Interop.Kernel32.MoveFile` → `MoveFileExW(MOVEFILE_COPY_ALLOWED | MOVEFILE_REPLACE_EXISTING)` (no `MOVEFILE_WRITE_THROUGH`); Microsoft's `MoveFileExW` documentation states no atomicity guarantee. `File.Move` docs: no atomicity statement.
- **Claimed:** verified process-interruption safety on Linux x64 (btrfs) for every tested point; best available same-directory replacement on the other platforms. **Not claimed:** power-loss safety (the directory is not fsynced after the rename, so the old file can reappear, which is still valid), Windows or macOS behavior (not run).
- **Deterministic tests:** `ConfigFileReplaceTests` 10/10 and the General.yaml tests in `AtomicRefreshTests` (34/34 in total).
- **Kill harness, General.yaml points** (inotify on `ConfigPath`, armed after "Installed the refreshed database"; 2 passes each, plus 8 timing points 0.30–0.42 s after that message):

| Point | Result (both passes) |
|---|---|
| temporary file created (`IN_CREATE`) | `General.yaml` old, complete (119 bytes); 1 temporary file left; next start ok; removed by the next refresh |
| temporary file being written (`IN_MODIFY`) | same |
| temporary file flushed and closed (`IN_CLOSE_WRITE`) | same |
| immediately before the replacement (second `IN_OPEN` of the temporary file, the validation read) | same |
| at the replacement (`IN_MOVED_FROM` of the temporary file) | new, complete; no temporary file; next start ok |
| immediately after the replacement (`IN_MOVED_TO` `General.yaml`) | new, complete; next start ok |
| after the replacement + 5 ms | the process had already exited 0: new, complete |
| timing sweep +0.30 … +0.42 s | old (killed during `quick_check`, staging cleanup or before the write) or new (finished); never empty or partial; next start ok every time |
| `General.yaml` opened for writing in place (the old trigger) | never fires any more: the file is no longer written in place |

  In the success path the temporary file is consumed by the rename, so there is no separate temporary-file cleanup to interrupt; the cleanup after a failure is covered by `ConfigFileReplaceTests`. After every kill: `General.yaml` deserialized, was never 0 bytes, held the complete old or new values (a new timestamp only with the new DB), the next start (`Load.GetConfig`) completed, the DB was fully old or new with `quick_check` ok, a new DB never had old cache files, and temporary `General.yaml` files were never read (only `General.yaml` and `*.yaml` are) and were removed by the next refresh.
- **Complete matrix on the final code:** all 31 points (16 database and cleanup points plus 15 General.yaml points) passed: **31/31**; the second General.yaml pass: **15/15**. The cross-process scenarios were repeated with the same results (X1 `SQLITE_BUSY`, DB unchanged; X2 and X3 SHA-256 `a825ce99…`; X4/X5 refused; X6 `IOException`, unchanged; X7 repair backed off).
- **Missing installed DB, failed installation:** `Install_IntoAMissingDatabase_ThatFails_…` uses a staging DB with page size 8192; SQLite rejects a backup into the WAL destination with page size 4096 (`SQLITE_READONLY`, error 8) after the bundled DB was placed. The created DB and its `-wal`/`-shm`/`-journal` are removed, no `.partial` file remains, and the next `Load.DbExistsCheck` extracts the bundled DB, which passes `Validate`.

**Repository validation after the General.yaml fix** [V]: restore OK; Debug and Release builds (`--no-incremental`) 0 errors, 84 warnings each; `XDG_DATA_HOME="$(mktemp -d)" dotnet test Campertron.sln -c Release --blame-hang-timeout 10m` **140/140** (128 `CampertronUnitTests` = 84 + 34 `AtomicRefreshTests` + 10 `ConfigFileReplaceTests`, 12 GUI); `CampertronGUITests` 12/12; release policy 67/67; no vulnerable packages in any project (`dotnet list Campertron.sln …` exits 1 only because of `docker-compose.dcproj`); real data folder unchanged.

**Docker validation** [V] (2026-11-11, real container; Docker Engine 29.9.3, Compose 5.7.0, buildx 0.38.2, containerd 2.4.2, runc 1.5.2 from the CachyOS repositories, installed for this validation; overlayfs, systemd cgroups). Image `campertron-td36:test` (`sha256:cce99aad4cd0…`) built with `CampertronConsole/Dockerfile` from a clean `git archive` of `563d722` (`VERSION=0.0.0-td36`, `REVISION` label `563d722…`); `--version`: `.NET 10.0.15 (X64)`; user `app` (uid 1654), `HOME=/home/app` (no `.local`), `WORKDIR /app` owned by `app`, `DOTNET_RUNNING_IN_CONTAINER=true`. Every run used only disposable bind mounts under the scratch work directory (`<work>/docker/<scenario>/config` → `/config`, `…/cache` → `/cache`, optionally `…/appdata` → `/home/app/.local/share`), mode 0777, with the marked old embedded DB (`6361e781…`), `General.yaml` with `outputTo: UnitTest` (container mode then exits 0 after `Load.GetConfig`), `autoRefresh: true`, an old `lastRidbDataRefresh`, and one derived cache file `232445-Campsites.json` in `/cache`. The live export (same ETag) was downloaded by the container each time. No named volume; the containers, images and build cache were removed afterwards; the host's real Campertron folders were never mounted and are unchanged.
- **Paths:** the DB is installed at `/config/RecreationDotOrg.db`; staging directories are created as `/config/RecreationDotOrg.refresh-<guid>` (seen during each run, removed afterwards); the lock files stay in `/config`. The download, `Extract` and the refresh's cache step use `LocalApplicationData`: `/home/app/.local/share` if it exists; otherwise `Environment.GetFolderPath` returns `""` and they are relative to the working directory `/app` (container layer).

| Scenario | Result |
|---|---|
| A: `/home/app/.local/share` absent | downloaded and extracted into `/app`, staging built and validated, then "Clearing cache" threw `DirectoryNotFoundException: /app/CampertronCache` (unhandled, container exit 139). **Installed DB unchanged** (`6361e781…`, OLD, `quick_check` ok), `General.yaml` unchanged, staging removed, `/cache` untouched; remnants: 0-byte `-wal`/`-shm` (read-only opens) and the refresh lock |
| B: `.local/share` mounted, no `CampertronCache` in it | the same abort at the cache step (`/home/app/.local/share/CampertronCache`); DB, `General.yaml` unchanged; zip and `Extract` removed (the zip by the unzip, `Extract` by the import) |
| C: `.local/share/CampertronCache` present | success, exit 0: **SHA-256 `a825ce99…`** (byte-identical to the host result), `quick_check` ok, `lastRidbDataRefresh` advanced only after the installation, no zip, `Extract`, staging, WAL or temporary `General.yaml` left; the appdata `CampertronCache` file was deleted |
| C restarted (`docker start -a`, refresh no longer due) | exit 0; the startup opened the DB (repair check); `quick_check` ok; `ReleaseCheck` with the image's own `/app` library inside the image: "GUI state data checks passed." |
| K: `docker kill` 0.3 s after "Installing the refreshed database" | 138,872,896 bytes of uncommitted WAL; `docker start` of the same container: the startup recovered the DB, the refresh (still due, the timestamp was not advanced) removed the stale staging directory and installed `a825ce99…`, exit 0 |

- **TD-05 interaction (not fixed here; fixed afterwards, §O):** in C and K, `/cache/232445-Campsites.json` survived the refresh. A container's searches use `CachePath` = `/cache`, but the refresh clears `LocalApplicationData/CampertronCache`, so in a container the new DB coexists with derived cache files from the old one, and the refresh fails safely (A, B) unless that unrelated directory exists. Both are TD-05 (the in-place refresh cleared the same directory, and threw after already emptying the DB); on the desktop `CachePath` is that directory, and the kill harness confirms the cache order there.

**Release-workflow checks, all 16 app/RID pairs** [V] (from a clean `git archive` export of `9ef909d`, `VERSION=0.0.0-dryrun.td36`, the workflow's publish command; outputs deleted after checking, no archive created or uploaded): every pair of CampertronConsole and CampertronGUI × linux-arm, linux-arm64, linux-x64, osx-arm64, osx-x64, win-arm64, win-x64, win-x86 published, passed the package-contents check (host, `CampertronLibrary.dll`, native SQLite and compression libraries; no `libSkiaSharp.pdb`/`libHarfBuzzSharp.pdb` in the Windows GUI packages) and `check_publish_runtime` (self-contained .NET 10, runtime 10.0.14).

**Published linux-x64 smoke tests** [V] (isolated `XDG_DATA_HOME`): console clean install exit 0, DB 226,471,936 bytes, SHA-256 `8712856097a91ee1…` (the embedded DB), no partial file; both `--version` print `0.0.0-dryrun.td36` and `.NET 10.0.14 (X64)`; the GUI still ran after 30 s on the real display; `ReleaseCheck` passed (51 states) for both extracted DBs. **End to end:** the published console with `autoRefresh: true`, an old `lastRidbDataRefresh` and `outputTo: UnitTest` downloaded the live export (same ETag), extracted it and ran the failure-safe refresh in 67 s (exit 0): messages in the expected order, installed SHA-256 `a825ce99…` (byte-identical to the in-place result), `lastRidbDataRefresh` advanced, cache emptied, zip and `Extract` removed, no staging directory left, `ReleaseCheck` passed on the refreshed DB. Docker, Windows and macOS were not run.

**Manual validation procedures for platforms not run here.** Use a published build of the branch and an isolated data folder (Windows: a separate user account or VM, since `XDG_DATA_HOME` does not redirect `%LOCALAPPDATA%`). Before each, set `General.yaml` to `outputTo: Console` and note the SHA-256 of `RecreationDotOrg.db` and `lastRidbDataRefresh`.
1. **Windows, GUI holding pooled connections while the console refreshes:** start `CampertronGUI.exe`, select a park, state and city so it has queried the DB. Then run `CampertronConsole.exe` → "Refresh Ridb Data (slow)". Expected: the refresh completes ("Installed the refreshed database"); the GUI keeps working and shows the new data after a new selection; no "being used by another process" error; afterwards no `RecreationDotOrg.refresh-*` directory in `%LOCALAPPDATA%\CampertronConfig`.
2. **Windows, competing writer:** while the console refresh is validating, open the DB in `sqlite3.exe` and run `BEGIN IMMEDIATE;` (keep it open for more than 30 s). Expected: after "Installing the refreshed database", about 30 s later, an unhandled `SqliteException` with error 5; the DB hash and `lastRidbDataRefresh` unchanged; then `ROLLBACK;`. Repeat and `ROLLBACK;` after 10 s: the refresh succeeds.
3. **macOS** (osx-arm64 and osx-x64): run the console refresh with the GUI open, as in 1. Then run it again and force-quit the console (`kill -9 <pid>`) right after "Installing the refreshed database" is printed. Expected: the next start works and the DB is either the previous or the new one (`sqlite3 RecreationDotOrg.db "PRAGMA quick_check;"` prints `ok`).
4. **Docker with `/config`** (run on Linux x64, see *Docker validation* above; repeat on other hosts): `docker run` the image with `-v <dir>:/config -v <dir2>:/cache` and a `General.yaml` with `autoRefresh: true` and an old `lastRidbDataRefresh` (container mode still auto-refreshes, TD-03). Expected: either a successful refresh, or a failure that leaves `/config/RecreationDotOrg.db` unchanged (see open question #27 about the cache directory in containers); no `RecreationDotOrg.refresh-*` directory left after a successful run. Repeat with `docker kill` during "Installing the refreshed database": the next container start works.
5. **Insufficient disk space** (Linux): put the data folder on a small filesystem, for example a 600 MiB loop-mounted ext4 image, with the 226 MB DB installed (the staging copy, its WAL and the installation's WAL need about 700 MB more). Run the refresh with an extracted export. Expected: an `SqliteException` (`SQLITE_FULL`, "database or disk is full") during staging or the installation, the installed DB hash unchanged, `lastRidbDataRefresh` unchanged.
6. **Forced termination during the installation:** the kill harness above (Linux). On Windows: Task Manager → End task on the console right after "Installing the refreshed database"; expected as in 3.
7. **All 8 publish targets:** the release workflow's publish, contents and `check_publish_runtime` steps (results above). Running the published apps natively is covered only for linux-x64 here; the others need the procedures above on their platforms.


### O. Configured cache path (TD-05, branch `fix/td-05-cache-path`, 2026-11-11)
Decision D27, business-rules §6 and §8 step 6. Based on `1b7c92b` (PR #31). Linux x64 (CachyOS), SDK 10.0.114, runtime 10.0.14 on the host; the image runs .NET 10.0.15.

**Paths before and after** [V]:

| Operation | Desktop before / after | Container before | Container after |
|---|---|---|---|
| Derived JSON cache (write, read) | `LocalApplicationData/CampertronCache` (= `CachePath`) / unchanged | `/cache` (`CachePath`) | `/cache`, unchanged |
| Refresh cache step | `LocalApplicationData/CampertronCache` / `CachePath`, the same directory | `/home/app/.local/share/CampertronCache` if `.local/share` exists, else `/app/CampertronCache` (§N) | `/cache` |
| HTML results (`HtmlFile`, `Email`) and the email attachment | `LocalApplicationData/CampertronCache` / `CachePath`, the same directory | `/app/CampertronCache/<guid>.html` in the container layer (pre-fix image below) | `/cache/<guid>.html` |
| RIDB download and `Extract` (not cache, unchanged) | `LocalApplicationData` | `/home/app/.local/share` if it exists, else relative to `/app` | unchanged (open question #27) |

**Deletion safety** [V]: the clear deletes what it deleted before, every file directly in the directory. Subdirectories and the directory itself are kept. A symbolic link to a file and a dangling link are unlinked (their targets are untouched), and a link to a directory is not a file for `Directory.GetFiles`, so it is kept and not followed (`CachePathTests`). New guards: an unset or whitespace path, a filesystem root (`/`, `//`, `/tmp/..`) and `ConfigPath` (also as `config/`, `config/../CampertronConfig`, `config/.`) are refused by `Clear.CheckCacheDirectory`. In `RefreshData` this happens before the refresh lock and the download, so nothing changes. `ClearRefreshCache` also refuses a directory that contains `RecreationDotOrg.db` or `General.yaml`; a cache path that is a link to the config directory is refused there, after the staging validation and before anything is deleted or installed (DB byte-identical). A failure while deleting propagates as before; the staging cleanup in `finally` reports its own problems without replacing that exception. If the configured cache directory is itself a link (or a bind mount), the files in its target are the cache and are cleared, as before. Not covered: a `CachePath` that is an ancestor of `ConfigPath` or an unrelated directory chosen on purpose. Production never sets one, because both paths are fixed in `Load.GetConfig`.

**Deterministic tests** [V]: `CachePathTests` (23) and 16 new `AtomicRefreshTests` (feature-inventory.md §Test coverage). Existing `AtomicRefreshTests` calls dropped the removed cache-directory argument (they already set `CachePath` to the same directory). Mutation checks (temporary, reverted; filter `CachePathTests|AtomicRefreshTests`, 73 tests):
- refresh clears `LocalApplicationData/CampertronCache` again (the pre-fix code): 17 failures
- HTML written to `LocalApplicationData/CampertronCache` again: 9
- no config-file guard: 3
- no root check: 2
- no config-directory check: 4

**Repository validation** [V]:

| Check | Result |
|---|---|
| `dotnet restore Campertron.sln` | OK ("All projects are up-to-date") |
| `dotnet build Campertron.sln -c Debug --no-incremental` / `-c Release --no-incremental` | 0 errors, 84 warnings each (unchanged; the email warnings in `Console.cs` moved with the code) |
| `XDG_DATA_HOME="$(mktemp -d)" dotnet test Campertron.sln -c Release --blame-hang-timeout 10m` | **179/179** (167 `CampertronUnitTests` = 128 + 23 `CachePathTests` + 16 `AtomicRefreshTests`; 12 GUI) |
| `dotnet test CampertronGUITests/CampertronGUITests.csproj` | 12/12 |
| `bash .github/release-policy/test-release-policy.sh` | 67/67 |
| `dotnet list Campertron.sln package --vulnerable --include-transitive` | exit 1 only because `docker-compose.dcproj` uses packages.config; the 4 projects it lists have no vulnerable packages. Each `.csproj` of the solution and `ReleaseCheck` scanned separately: no vulnerable packages (exit 0) |
| The production commit alone (`6f31470`, worktree) | builds, 0 errors / 84 warnings |
| Real `~/.local/share/CampertronConfig` and `CampertronCache` | unchanged (listing with sizes and mtimes, DB and `General.yaml` SHA-256 compared before and after) |

**Docker validation** [V] (Docker Engine 29.9.3, overlayfs, systemd cgroups). Image `campertron-td05:test` built with `CampertronConsole/Dockerfile` from a clean `git archive` of `923e382` (the branch's code; later commits change only documentation), `VERSION=0.0.0-td05`; `--version` `.NET 10.0.15 (X64)`; user `app`, `DOTNET_RUNNING_IN_CONTAINER=true`. Only disposable bind mounts under the scratch directory (mode 0777). The old DB is the embedded DB extracted by the image (SHA-256 `8712856097a91ee1…`). The live RIDB export had the same ETag as §N (`102488648295dd185cdbe8539adf4dcd`), so the expected refreshed DB is `a825ce99…`. Search runs used `outputTo: Email` with SMTP `127.0.0.1:9` (connection refused, no message sent) and a Watchman (232445) search over 60 days. Times are UTC log timestamps. `docker diff` was checked after every scenario.

| Scenario | Result |
|---|---|
| No `/cache` mount | "Cache path does not exist" (unhandled, exit 139), as before; nothing else created |
| S1: search on the old DB | 388 derived JSON files and the HTML result written to `/cache`; `SmtpException` (exit 139); container layer unchanged |
| Same search, pre-fix image (`1b7c92b`) | 388 JSON files in `/cache`, but the HTML in `/app/CampertronCache/<guid>.html` in the container layer |
| S2: successful refresh, only `/config` and `/cache` mounted (the docker-compose layout); `/cache` held S1's 389 files plus 2 marked stale files; sentinel in `/config` | exit 0. Before the fix this layout aborted at the cache step (§N, A) |
| S2 results | DB **`a825ce99…`**, `quick_check` ok; `/cache` empty (stale JSON and the `-Cached.json` completion markers gone); sentinels unchanged; `lastRidbDataRefresh` advanced (written after "Installed the refreshed database"). `/config` holds `Email.yaml` (sample), `General.yaml`, the DB and the two lock files: no staging, zip, `Extract`, WAL/SHM or temporary YAML. Container layer unchanged (the download and `Extract` in `/app` were removed). **Cache step: 2.3 ms for 391 files** ("Clearing cache" → "Installing the refreshed database") |
| S3: `docker start` of the same container | exit 0, no download; DB `a825ce99…`, `quick_check` ok |
| S3: `ReleaseCheck` inside the image (`/app` library, refreshed DB) | "GUI state data checks passed." (51 states), exit 0 |
| S3b: restarted with an Email search | the cache was rebuilt in `/cache` from the new DB (389 files; `232445-Campsites.json` differs from the old-DB version, `CampInfo`/`CampsitesByPark` identical) and the HTML was written to `/cache`; container layer unchanged |
| S4: `/cache` mode 0555 (not deletable for `app`), 389 stale files | "Clearing cache" → `UnauthorizedAccessException` (exit 139). DB **byte-identical** (`8712856…`), `quick_check` ok; stale files and `lastRidbDataRefresh` unchanged; staging removed; only a 0-byte `-wal` and a 32 KiB `-shm` from read-only opens (as in §N) |
| S5: `docker kill` on "Installing the refreshed database" (after the cache step) | the kill landed during the uncommitted backup (28,728,792-byte WAL). Recovery: OLD (`8712856…`), `quick_check` ok, `/cache` empty, timestamp unchanged, staging left. Restart: the stale staging was removed, the refresh installed `a825ce99…`, exit 0 |
| S6: `docker kill` on "Installed the refreshed database"; `/home/app/.local/share` mounted with `CampertronCache/232445-Cached.json` (the directory the pre-fix refresh cleared) | NEW (`a825ce99…`), `quick_check` ok, `/cache` empty (no stale file), timestamp still old, staging left. Restart: staging removed, refresh again (timestamp not yet written), exit 0, `quick_check` ok; the `LocalApplicationData` sentinel unchanged; zip and `Extract` removed |

- The second refresh in S6 used the already-new DB as its template and produced SHA-256 `d9b2a4ea…`: same `.dump` as `a825ce99…`; only 3 header bytes differ (file change counter, schema cookie, version-valid-for). This is D26 behavior (the template's history), not related to the cache path.
- Afterwards all containers, both images (and the pulled base images), and the build cache were removed. Docker's state (no images, containers, volumes or build cache) was the same as before. No named volume was used. The host's real Campertron folders were never mounted.

**Desktop validation** [V] (the Release `CampertronConsole.dll` of the branch, isolated `XDG_DATA_HOME` with the old DB, 389 stale cache files, `CampertronCache2` neighbor and a sentinel):
- Refresh: exit 0, DB `a825ce99…`, `quick_check` ok, `CampertronCache` empty, neighbor and sentinel unchanged, no zip or `Extract` left.
- Timings: download 51.2 s; staging copy through installation 15.15 s (populate 12.48 s, validate 1.60 s, cache step 2.1 ms for 389 files, installation 0.39 s). That is in line with §N (populate 13.26–13.86 s, validate 1.59–1.69 s). The population code is unchanged and was not benchmarked again.
- An Email search with refused SMTP rebuilt the cache in `CampertronCache` and wrote the HTML there.

**Not run:** Windows and macOS (case-insensitive path comparison and `%LOCALAPPDATA%` paths untested); Docker on other hosts or with named volumes; an actual email send.

### P. GUI visual polish (v0.1.0-beta.8, branch `feat/gui-visual-polish`, 2026-11-12)
Decision D28; TD-08 resolved; TD-38 (duplicate names) and TD-39 (TabControl keyboard trap) recorded. Based on `9852997`. Linux x64 (CachyOS, KDE Plasma Wayland with XWayland), SDK 10.0.114, runtime 10.0.14.

**What changed** [V]: Direction A ("Refined Fluent") from the Phase 1 investigation, kept to presentation and the minimum view-model change.
- Theme: `App.axaml` follows the system theme (`RequestedThemeVariant="Default"`, was `Light`). `Styles/AppStyles.axaml` takes every color from Fluent `SystemControl*` resources through `DynamicResource`. The former hard-coded `LightGray` borders and `BoxShadow`s (white halos when forced to Dark) are gone.
- Window: 1024×768 as before (now in XAML), minimum 720×540, `CenterScreen`.
- Each tab: a filter card (`Grid` with labels above stretching ComboBoxes, so a label never wraps away from its selector), `PlaceholderText`, `AutomationProperties.Name`, `Label.Target` access keys (`_Park`, `_Campground`, `_State`, `C_ity`), dependent selectors disabled until their prerequisite, ellipsis trimming for long names, "(No city listed)" for an empty city (item template only).
- Details: one `CampgroundDetailsView` for all tabs (was three copies of fixed-height `ScrollViewer`s; the fees box was cut off at 768 DIP and unreachable in smaller windows). It holds an empty state, a title (heading level 1) with the tab's filter context, "Campground ID" with a selectable ID (help text "Campground ID") and **Copy ID**, Contact, Description, Directions and Fees (heading level 2) and "Not listed" for missing values. All of it is in one focusable `ScrollViewer`, in a left-aligned column capped at 976 DIP (960 for the cards).
- Data: `Read.GetCampgroundDetailsByName` returns unprefixed values; `Read.GetParkCampgroundByName` is unchanged (D28). Per-tab selection state (TD-08). The Tab-wrap workaround in `MainWindow` (TD-39).
- Not changed: queries and filtering, the console, refresh, cache, YAML, email, HTML output, Docker, packages, project files, `release.yml`, release policy.

**Avalonia 12 findings** [V] (12.1.6, verified against the assemblies and the official docs):
- `TextBlockAutomationPeer.GetNameCore` returns the text: `AutomationProperties.Name` and `LabeledBy` are ignored for `TextBlock`/`SelectableTextBlock`. Values therefore follow their visible labels in reading order, and the focusable ID also carries `AutomationProperties.HelpText`. ComboBoxes take `AutomationProperties.Name`.
- `IClipboard` is not implementable outside Avalonia, so a throwing clipboard cannot be faked. `CopyTextAsync` catches every exception; only the missing-clipboard path is tested automatically. The headless platform has a working in-memory clipboard, used by `CopyId_CopiesTheRawIdWithTheKeyboard`.
- `KeyboardNavigationHandler` is internal. Tab order is tested with headless `KeyPress(Key.Tab)` and `FocusManager.GetFocusedElement`.
- `SelectableTextBlock` is a tab stop by default. The long values use `IsTabStop="False"` (still selectable with the mouse); the ID stays a tab stop.
- TabControl keyboard trap: TD-39.
- `AutomationLandmarkType` exists, but no landmark was added. Each tab has one details region with a level-1 heading and a named `ScrollViewer` ("Campground details"), and no screen reader was available here to show that a landmark would improve navigation.

**Tests** [V]: 6 `CampertronUnitTests` (`CampgroundDetailsTests`) and 22 GUI tests (34 instead of 12). The existing GUI tests now find controls by `x:Name` instead of `StackPanel`/`Children[1]`/first `TextBlock`. They assert the system theme instead of Light, and include the fixture's new parks, campgrounds and empty city in their expected lists. `FullLookupSession` runs in Light and Dark. The TD-08 test now asserts the fix. No pixel comparison: the headless setup does not render, fonts differ by platform and `RenderTargetBitmap` misrenders this window (§Refreshing the README screenshots); layout is asserted through bounds, extents and scroll positions instead. Mutation checks (temporary edits, reverted), each caught by its test:

| Mutation | Failing tests |
|---|---|
| TD-08 reintroduced | `ByStateSelection_DoesNotChangeTheByCityState_TD08`, `Tabs_KeepIndependentSelectionsAndDetails`, `DependentSelectors_…` |
| details property bound a second time | `Selections_RunOneQueryEach` (EF Core `CommandExecuted` count) |
| details `ScrollViewer` not focusable | `Keyboard_Tab…` |
| fixed-height details (old style) | `LongContent_…` at 720, 1024 and 1600 |
| `LightGray` card border | `ThemeResources_ResolveInLightAndDarkWithoutHardCodedHalos` |

**Repository validation** [V]:

| Check | Result |
|---|---|
| `dotnet restore Campertron.sln` | OK |
| `dotnet build Campertron.sln -c Debug --no-incremental` / `-c Release --no-incremental` | 0 errors, **81 warnings** each (was 84), 0 AVLN, 0 RXUIBIND. Compared with a build of `origin/master` (line numbers ignored), exactly 3 are gone and none are new: CS0169 `_SelectedStateByState` never used (TD-08) and CS8603/CS8604 from the removed `CampgroundByName` getter |
| `XDG_DATA_HOME="$(mktemp -d)" dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m`, and the same in Debug | **207/207** each (173 `CampertronUnitTests` = 167 + 6 `CampgroundDetailsTests`; 34 GUI) |
| `bash .github/release-policy/test-release-policy.sh` | 67/67 |
| Vulnerability scan (CI logic, each `.csproj` of the solution) | 0 vulnerable packages in all 5 projects |
| `dotnet publish CampertronGUI -c Release -r linux-x64 --self-contained true -p:ContinuousIntegrationBuild=true` | 261 files (same count as the `v0.1.0-beta.7` linux-x64 GUI). `--version`: `.NET 10.0.14 (X64)`. Started with an isolated `XDG_DATA_HOME` on the real display (Xvfb is not installed here): still running after 30 s (`timeout` 124), DB extracted (226,471,936 bytes) |
| Real `~/.local/share/CampertronConfig` and `CampertronCache` | unchanged (path, size, mtime and ctime listing hashed before and after) |

**Real-window validation** [V] (Linux x64, X11 window via XWayland, desktop scaling 1.45, desktop theme **Dark**, real extracted DB in an isolated `XDG_DATA_HOME`). A scratch launcher (not in the repository) ran `Campertron.App` with the production `AppBuilder` chain plus `LogToTextWriter(Warning)` and unhandled-exception handlers, drove the window's own controls and captured it with `import -window Campertron`:
- **Start:** centered (position 2043,1010 on a 5568×3132 work area = (5568 − 1024·1.448)/2), `ActualThemeVariant` Dark from the platform. Asking for 500×400 kept the client at 720×540 (the minimum).
- **Light** (forced with `RequestedThemeVariant = Light`) and **Dark** were captured at 1024×768, 720×540 and 1600×1000.
  - Inspected on the final build (content column left-aligned with the tab headers): Dark at 1024×768 (Zion, the empty state, Copy ID focus) and at 1600×1000, and the Light README capture at 1100×820. All aligned with the tab headers, nothing clipped.
  - Inspected only on an earlier build of this branch (same controls and styles; the content column was centered, the empty-state card lacked its right gutter, and blank lines were not yet collapsed): Light at 1024×768, 720×540 and 1600×1000. Nothing clipped, and the details scroll to Fees.
  - Not visually inspected: Dark at 720×540.
  - On the final build, left alignment, the 960 cap, no horizontal overflow and scrolling to Fees at all three sizes are asserted headless by `LongContent_StaysInsideTheWindowAndReachableAtEverySize`.
- **Long text:** the longest park (80 characters, "Pike and San Isabel National Forests & Cimarron and Comanche National Grasslands") and the longest campground name (100 characters with trailing spaces, "Sandy Bottoms Recreation Area Campground (Clayton, GA)") are trimmed in the selectors and wrap as titles.
- **Empty city and missing data:** 7 Utah campgrounds have no city ("(No city listed)" → DEVILS CANYON, context "UT", Email "Not listed"). Runs of blank lines in RIDB descriptions are collapsed to one (display only).
- **Dark** (final build, 1024×768 and 1600×1000): no halos, readable secondary text.
- **Scaling** (`AVALONIA_GLOBAL_SCALE_FACTOR` 0.69, 1.38, 2.0 → effective 1.0, 2.0, 2.9): run on the earlier build with the centered column, not re-run on the final build. Effective 1.0 and 2.9 were inspected (identical layout in DIPs, crisp text); 2.0 was checked only through its log (scaling 1.998, client 1024×768, clean exit).
- **Keyboard:** XTest key events were not delivered to the XWayland window on this KWin session, so raw key events were injected through the window's platform input callback (reflection; the path hardware keys take into Avalonia's input manager).
  - Tab: TabItem → ParkComboBox → ParkCampgroundComboBox → DetailsScrollViewer → CampgroundIdText → CopyIdButton → TabItem (wrap, TD-39) → ParkComboBox.
  - Down selected list items; PageDown scrolled the details by 552 DIP.
  - Enter on Copy ID put `233598` on the X11 clipboard (read back through Avalonia) and showed "Copied to the clipboard."
  - Alt+C moved focus to the campground selector; Right on the header switched to By City.
  - Focus visibility, inspected on the earlier build: the park selector (accent fill), the details area, the ID and Copy ID (Fluent focus ring). On the final build only Copy ID (focus ring). The campground selector and the tab header were captured with focus but not inspected.
- **Shutdown:** in every run (Light, Dark, keyboard, 3 scales, README) the lifetime returned 0 with no unhandled exception (TD-34 not seen).
- **Measured in Phase 1** (same host): list queries 6–64 ms, details query 77 ms, synchronous on the UI thread. Acceptable, so no loading indicator or async loading was added.

**Not run:** a physical-keyboard pass and a screen reader (Orca and Accerciser are not installed); Windows and macOS (open question #29); the throwing-clipboard path. Xvfb was not run locally (it is not installed here); see the dry run below.

**Release workflow dry run** [V]:
- **Why manual:** `release.yml` runs on a pull request only when the PR changes a file matching its path filters (`.github/workflows/release.yml`, `CampertronConsole/Dockerfile`, `.dockerignore`, `**/*.csproj`, `Campertron.sln`, `CampertronLibrary/content/**`, `.github/release-policy/**`). PR #34 changed none of them, so it got CI (`ci.yml`, passed on `12bf3a0`) but no automatic release dry run. With the maintainer's approval, a manual dispatch was used instead: `gh workflow run release.yml --ref feat/gui-visual-polish`.
- **Run:** 43219645735, `workflow_dispatch`, commit `12bf3a0`, version `0.0.0-dryrun.15`.
- **Results:** all 20 jobs that ran passed. They were Release verify (release-policy tests 67/67), the 16 packages, the Docker build (linux/amd64 and linux/arm64, not pushed), Smoke test linux-x64 (native) and Assemble release.
- **xvfb GUI start:** the smoke job's `GUI starts` step passed: the published linux-x64 GUI was still running after 30 s under `xvfb-run` (`timeout` returned 124) and had created `RecreationDotOrg.db`. In the same job, the console clean install extracted 226,471,936 bytes, both apps reported `.NET 10.0.15 (X64)`, and `ReleaseCheck` passed for the GUI and the console (51 states).
- **Nothing published:** `Publish image to GHCR` and `Create GitHub release` were skipped, because they require a `v*` tag push. The 16 archives and `release-metadata` were uploaded as workflow artifacts with 7-day retention. No tag, release or image was created.

### Q. HTML results page (v0.1.0-beta.9, branch `feat/html-visual-polish`, 2026-11-12)
Decision D29; page rules in business-rules §6. Linux x64, SDK 10.0.114 / runtime 10.0.14, Chrome 155 and Firefox (headless), axe-core 4.10.2 (validation only, injected into the test browser; not part of the page). All data, configuration, cache and output in scratch directories (`XDG_DATA_HOME` per run); nothing written to the real `CampertronConfig`/`CampertronCache`. Recorded after the review corrections (generation time removed, CS8604 removed, fallback and email tests added).

| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release --no-incremental` / `-c Debug --no-incremental` | 0 errors, **78 warnings** each (was 81). Compared with a Release build of `master` `da9484c` by file, code and message (counted per identity): **none introduced**; three removed in `AvailabilityApi.cs`: the two CS8629 `(DateTime)ThisEntry.CampsiteAvailableDate` casts in `FilterData` (still present; the `?.` reads that made the compiler treat the date as maybe-null moved into `AvailabilityValues`) and one of two identical CS8602 on `PermittedEquipmentList` (the duplicated output code is now one builder; the other moved with it). All other warnings unchanged. No AVLN or RXUIBIND |
| `XDG_DATA_HOME="$(mktemp -d)" dotnet test …` (`CampertronUnitTests`, `CampertronGUITests`), Release and Debug | **244/244** each (210 `CampertronUnitTests` = 173 + 25 `ResultsHtmlTests` + 3 `ConsoleOutputGoldenTests` + 9 `HtmlResultsOutputTests` cases; 34 GUI) |
| `bash .github/release-policy/test-release-policy.sh` | 67/67 |
| Vulnerability scan (CI logic, each `.csproj` of the solution) | 0 vulnerable packages in all 5 projects |
| Golden-test mutation (`CheckOut:` → `Checkout:` in `AvailabilityValues`, before the review) | `ConsoleRendering_OfTheTaggedProducers_IsByteForByteUnchanged` fails |
| Real console output, `master` library vs this branch (same four real searches, run one after another in name order, Console output, calendars included) | byte-identical (2,576,486 bytes, same SHA-256) |

**Real output** [V]: a scratch harness (not in the repository) ran the real search path (`Load.GetConfig`, `Preload`, `GetAvailabilitiesByCampground` in `Parallel.ForEach`, `Load.OutputResults`) against recreation.gov with an `xdg-open` stand-in on `PATH` that copied the file it was asked to open. Two sets: the two shipped examples (`examples/0.1`: Watchman single nights with filters, Mather two consecutive nights), and those two plus a Watchman search without filters over 60 days (display name with `&`, quotes, `<any>` and 120 characters, a fixture value) and a search with `includeSites: [NOSUCHSITE]` (fixture filter, no results). Each run wrote **one** file and requested **one** launch; before beta.9 the four searches gave four files, and 2 of them had already been deleted when the stand-in ran (TD-06).
- Four searches: 3,428,558 bytes, 9,853 availability entries (Mather 1,284, the 60-day search 6,477, Zion 2,092, an empty search), 8,831 links. In Chrome (CDP, `file://`): load event after 0.40–0.90 s (10 width/theme loads), standards mode, UTF-8; jumping to the end and rendering two frames took 0.03 s.
- Two shipped examples: 58,800 bytes; Zion 130 entries, Mather none (empty state). Observed with the same live data and both the `master` and the branch library: run in parallel with only the Zion example, the Mather example produced no results, while in the four-search set it produced 1,284 (TD-04 note; not investigated, not changed by beta.9). The page shows the streams as produced.
- Also observed (not changed by beta.9; `class/RecDotOrg/Availability.cs` is untouched): 2 of 4 four-search attempts ended with an unhandled `ArgumentException` ("Destination array was not long enough") in `CampsitesDataEntry.GenerateDates` called from `ProcessMonth` in `Parallel.ForEach`, when two searches read the same campground month. Investigated and fixed as TD-40 (§R).
- Before beta.9, for comparison: the per-search fragments had no doctype, charset, title, headings or links, and `<any>` in a display name disappeared as an unknown element.

**Email pages** [V, without sending]: the page the Email path renders for each of the four searches (one stream, no name, as `ProcessConsoleConfig` passes it): one complete document each, one `section`, no navigation, header "1 search", booking links only to recreation.gov campsites (2,092 / 6,477 / 0 / 258 links); 2.4 MB for the 60-day search. `HtmlResultsOutputTests` checks the rest (subject, sender, recipients unchanged; attachment bytes equal the body as UTF-8 without BOM). No email was sent; Gmail, Outlook and Apple Mail were not inspected (open question #31).

**Browser checks** [V, Chrome via CDP and Firefox headless screenshots, final code]:
- Widths 320, 390, 768, 1280 and 1920 CSS px, Light and Dark: no horizontal overflow on any of 6 pages (the two-search page, the four-search page, four email pages; 60 measurements in Chrome); cards in one column at 320 and 390, two at 768, four at 1280 and 1920; long names wrap. Firefox (top of the page): the same layout at all five widths in Light and at 390 and 1280 in Dark (`ui.systemUsesDarkTheme`), for the two-search and four-search pages.
- Header: title, the number of searches, and the navigation for several searches; no `time` element and no generation time.
- Light/Dark: `prefers-color-scheme` emulated in Chrome; text contrast (computed from the tokens) at least 5.8:1 in Light and 8.6:1 in Dark, links 5.8:1 and 9.0:1; card borders are decorative (1.3:1, 1.8:1).
- axe-core 4.10.2: **0 violations, 0 incomplete** in 44 runs: every width and theme for the two-search page and the small email pages, Light 320 and Dark 1920 for the two large pages. Automated checks only; no screen reader was run.
- Keyboard (Chrome, CDP key events): Tab moves through the navigation links, then the booking links in order; Shift+Tab goes back; every focused link shows the 3 px focus outline (`:focus-visible`); Enter on a navigation link moves to its section and the next Tab continues inside it.
- Without CSS (all `style` elements removed; two-search page and email pages at 1280 and 390): title, search count, headings, campground line, counts, dates, site lines, "Equipment:" and the links remain readable in document order.
- Print (Chrome `printToPDF`, Letter): forced light colors, navigation hidden, two card columns, each booking URL printed under its link, cards not split; 13 pages for the two-search page, 147 for the Zion email page, 646 for the four-search page. Firefox print was not checked.
- Not checked: Gmail, Outlook and Apple Mail (no email was sent; open question #31), Safari, Windows and macOS default-browser launch (mechanism unchanged), a screen reader.
- `<details>`/`<summary>` was not used (D29): collapsed content does not print, email clients handle it inconsistently, and the full page performed well without it.

### R. TD-40: parallel searches of the same campground and month (branch `fix/td40-generatedates-race`, 2026-11-13)
Technical debt TD-40; rule in business-rules §3. Linux x64, SDK 10.0.114 / runtime 10.0.14. All data, configuration, cache and output in scratch directories (`XDG_DATA_HOME` per run, configuration files written there, a hard link to an extracted bundled DB); never the real `CampertronConfig`/`CampertronCache`; no email sent. The scratch harness (not in the repository) calls the real `AvailabilityApi.GetAvailabilitiesByCampground` in `Parallel.ForEach`, as `Load.RunConsoleSearch` does. Its offline mode puts recreation.gov month JSON captured once (Watchman 232445 Nov 2026–Jan 2027, Mather 232490 Nov–Dec 2026; scratch only, not committed) into `RunData.SiteData`, freshly parsed for every iteration, with the URLs already claimed, so the searches never wait for a download and run `ProcessMonth` at the same moment: more overlap than a live run. Each scenario first runs its searches one at a time for the reference counts (availability entries per search).

| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release --no-incremental` / `-c Debug --no-incremental` | 0 errors, **77 warnings** each (was 78). Compared with a Release build of `master` `4fd338b` by file, code and message (counted per identity): **none introduced**; one removed: the CS8602 on `CampsitesTable.AvailabilityDates.Contains(Checker)` in `ProcessMonth` (the query now reads the search's own non-null list). Debug has the same identities |
| `XDG_DATA_HOME="$(mktemp -d)" dotnet test …` (`CampertronUnitTests`, `CampertronGUITests`), Release and Debug | **257/257** each (223 `CampertronUnitTests` = 210 + 12 `AvailableDatesTests` + 1 `SharedMonthSearchTests`; 34 GUI) |
| `SharedMonthSearchTests` against the old loop (`GenerateDates` on the cached entries put back into `ProcessMonth` in a scratch build) | fails: `AvailabilityDates` written into the cached month |
| `ConsoleOutputGoldenTests` | 3/3, unchanged |
| `bash .github/release-policy/test-release-policy.sh` | 67/67 |
| Vulnerability scan (CI logic, each `.csproj` of the solution) | 0 vulnerable packages in all 5 projects |
| Full output text, `master` library vs this branch, offline, one search at a time (six searches: Watchman single nights over 3 days, 30, 60 days and with an unmatched site filter, Watchman two nights over 30 days, Mather two nights; 14,311 date lines) | **byte-identical** (4,706,403 bytes, same SHA-256) |
| Two threads deriving dates from one shared parsed month (`AvailableDatesBySite`, barrier start), 4,000 iterations each on Watchman Nov and Dec and Mather Nov | 0 exceptions, 0 wrong results, 0 changes to the shared month (statuses, status lists, `AvailabilityDates`). The old `GenerateDates` on a shared month, same harness: wrong dates in about 85 % of iterations |
| Offline scenarios, this branch, 200 parallel iterations each, all on Watchman unless noted: A same 7 days with different filters; A with the files in reverse order; B 7 and 45 days; C single nights 7 days + two nights 30 days; D same month, dates that don't overlap; E same campground, different months; F Watchman + Mather; G one search; H the two shipped examples + Watchman 60 days + an unmatched site filter | **0 exceptions, 0 iterations with a count different from the one-at-a-time reference (0 fewer, 0 more), 0 iterations with anything written into a cached month**, in all 9 scenarios (1,800 iterations). The reference counts equal the Phase 1 references (for example A 297/304, H 6,642/0/874/2,256) |
| The same check on `master` `4fd338b` (control, 60 iterations each of A and H) | cached month written in every iteration; fewer results in 12 (A) and 15 (H) iterations, never more; no exception in this run |
| Live, recreation.gov, isolated (the four searches of scenario H; 5 pairs, 70 requests): each search one at a time, then all in parallel | 0 exceptions; identical counts per search in all 5 pairs (Watchman 60 days 6,635, Zion 2,249, Mather 874, unmatched 0) |

Before the fix (Phase 1, same harness and data, 60 parallel iterations per scenario): exceptions in 0–2 iterations and fewer results in 8–17 iterations of every same-campground-and-month scenario, on `master` `4fd338b`, `da9484c` and the beta.8 source `84e5649`; none for different months, different campgrounds or one search. Thirty live end-to-end runs (`Load.RunConsoleSearch`) did not throw: in a live run one search downloads while the others poll every 500 ms, which narrows the overlap; the earlier 2 of 4 failing live runs (§Q) are not explained, and no crash rate is claimed.

**Reconciliation with §T (2026-11-14).** The §R results above are historical and were not re-verified: the §R harness and the captured month JSON were scratch only and no longer exist, and the filters of the scenario searches were not recorded, so scenario C (single nights 7 days + two nights 30 days on Watchman) cannot be rerun exactly. The §R metric was the number of availability entries per search, compared with a one-at-a-time pass. A reconstruction of scenario C (§T: the same two searches, `DaysOut` 7 and 30, unfiltered, in file order, on seeded in-memory months instead of the captured JSON) gives the same answer with that metric (0 iterations with fewer or more entries in 200, on `master` `cb504cb` and on `fix/manual-search-notification-history`), but an exact text comparison shows that the two-night search's output does differ in 37–39 % of parallel iterations (74 and 77 of 200): a site's Site/Equipment/URL block appears or is missing depending on the order in which the searches fill the shared `FilteredAvailableData` (TD-04; business-rules §6 "a date entry without its site part"). §R's "0 differences" therefore holds for entry counts only, not for the text of consecutive-day output.

### S. Curated release notes in the draft (R7, decision D30, branch `ci/r7-curated-notes-in-draft`, 2026-11-13)
Workflow and release-policy change only; no application source, project, Docker or data file changed. Linux x64, SDK 10.0.114. Nothing was pushed, dispatched or published for this validation: no tag, release, artifact, attestation or GHCR change (the release records, tags and GHCR digests were compared before and after).

**Fragment rules** (`validate_curated_fragment FILE VERSION`): a regular file (not a symlink) named `v<VERSION>.md`; non-empty, valid UTF-8, no NUL, no CR, ending with exactly one newline and no blank or whitespace-only last line; first line exactly `## What's new in <VERSION>` and no second "What's new in" heading; no level-one heading (ATX `#` or setext `===`); no level-two `Downloads`, `Compatibility with older versions`, `Docker` or `Verify your download` heading (any case; a `### Docker` subsection is allowed); no `**Pre-release version.**`/`**Stable release.**`; no release-status statement (case-insensitive, word-bounded: "marked [as] [the] latest", "GitHub pre[-]release", "is/as [not] [the] latest release", "published/released as [a] normal/stable/draft/pre[-]release"), which still permits prose such as "Upgrading from 0.1.0-beta.9", "the latest beta", "this latest release" or "there is no `latest` tag for beta versions"; no download-table row (`` | ` ``); none of this version's 16 archive names. The bytes are never normalized.

**Composition** (`compose_notes`): template lines 1–4, the fragment unchanged, one blank line, the template from line 5 (`## Downloads`). It checks that the template still has that layout, that the pre-release flag matches the version, and that the body is at most 125,000 bytes (stricter than GitHub's 125,000-character limit). The fragments of beta.7, beta.8 and beta.9, cut from their published bodies in a scratch directory (not committed, no backfill), each pass `validate_curated_fragment` and compose byte for byte into the published bodies (SHA-256 `fe64bab3…`, `fb4244c7…`, `8e748064…`).

**Directory check** (`check_curated_notes_dir`): only `README.md` and `v<SemVer>.md` regular files; names that differ only by case, other files and subdirectories fail. Each fragment composes for its own version (pre-release if the name has a hyphen) and must pass `validate_notes`. Errors name the file and go to stderr; the preview (one block per fragment with the complete SHA-256, byte count and the composed body in a code fence) goes to stdout and is deterministic. No fragment is required for any version here.

| Check | Result |
|---|---|
| `bash .github/release-policy/test-release-policy.sh` | **177/177** (67 existing, unchanged, + 110 curated-notes cases) |
| Mutation check (scratch copies): spoof rule disabled; blank line dropped from composition; `jq -r` instead of `jq -j` for the body; template fallback when publishing | each caught (6, 3, 4 and 4 failing cases; the unmutated copy passes 177/177) |
| `release.yml` and `ci.yml` parsed with YamlDotNet 16.3.0 | triggers, PR paths, permissions, job graph, publishing gates and action SHA pins unchanged; 42 `run:` blocks, all pass `bash -n` |
| Stub rehearsal: the `run:` blocks extracted verbatim and run with `bash --noprofile --norc -eo pipefail` in a scratch git repository, a logging and stateful `gh` stub first on `PATH`, an empty `GH_CONFIG_DIR` and a dummy token | beta `0.1.0-beta.10` and stable `1.2.3`: draft created with the composed notes, draft read back, published, Latest and public body checked; the published body is byte-identical to the approved notes; no notes edit. Missing fragment: fails in verify (step 4), nothing created. Invalid fragment: fails in verify (step 3). Artifact notes one byte off: fails at the reconstruction, no draft. Draft body one byte off: fails at the draft check, release left a draft, `--draft=false` never called. Public body one byte off: fails after publication, reported, no edit. `workflow_dispatch` and `pull_request`: template notes for `0.0.0-dryrun.42` with the dry-run statement; publishing jobs gated off; no `gh release` call. A real fragment in a dry run is validated and previewed (SHA-256 equal to the tag run's composed body); an invalid one fails the dry run |
| `dotnet build Campertron.sln -c Release --no-incremental` | 0 errors, **77 warnings**; identities identical to a Release build of `origin/master` `2c2778b` |
| `XDG_DATA_HOME="<temp dir>" dotnet test Campertron.sln -c Release --no-build` | **257/257** (223 + 34 GUI) |
| Vulnerability scan (each `.csproj`) | 0 vulnerable packages in all 5 projects |

### T. Manual searches and notification history (branch `fix/manual-search-notification-history`, 2026-11-14)
Defect: a manual Search with `outputTo: HtmlFile` (or a `UnitTest` or Email run) wrote `Camp.History` markers, and `FilterData` dropped marked hits for every output type, so later manual HtmlFile and Console Searches hid those campsites and dates ("No entries found" / "No new matching availability to display."). Present since `15ffc1d` (2024-01-21; Console stopped writing in `87874a8`, 2024-09-14), so in every tagged release through `v0.1.0-beta.9`; reproduced identically on the beta.8 and beta.9 sources. Fix (business-rules §5, D8 refined, open question #8 closed): one `internal` policy `Load.UsesNotificationHistory` (`OutputTo == Email`) gates both the read in `FilterData` and the write in `HasNewEntries`. No public signature, configuration key or marker format changed; existing markers are not touched. Linux x64, SDK 10.0.114 / runtime 10.0.14. All data, configuration, cache, history and output in scratch directories (`XDG_DATA_HOME` per run); never the real `CampertronConfig`/`CampertronCache` (nothing in them changed); no email sent (Email cycles stop at the gate), no browser opened (`OpenHtmlFile` replaced, a recording `xdg-open` first on `PATH` for beta.8). Not all of the validation was offline: see **Network, email and browser** below.

| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release --no-incremental` / `-c Debug --no-incremental` | 0 errors, **76 warnings** each (was 77). Compared with builds of `master` `cb504cb` by file, code and message (counted per identity): **none introduced**; one removed: the CS8602 on `InternalConfig.GeneralConfig.OutputTo` in `HasNewEntries` (now the null-safe policy). Debug has the same identities |
| `XDG_DATA_HOME="<new temp dir>" dotnet test Campertron.sln -c Release --no-build` and `-c Debug` | **278/278** each (244 `CampertronUnitTests` = 223 + 16 `NotificationHistoryTests` cases + 5 new `LoadTests` cases; 34 GUI). Each run made the one existing live request of `LoadTests.RunConsoleSearchTest` (TD-01; no new test makes one) and wrote **0** `Camp.History` markers (before: 118 on 2026-11-10) |
| Focused (Release) | `NotificationHistoryTests` 16/16, `LoadTests` 13/13 (its `RunConsoleSearchTest` made the TD-01 live request), `SharedMonthSearchTests` 1/1, `AvailableDatesTests` 12/12, `ConsoleOutputGoldenTests` 3/3 (unchanged), `HtmlResultsOutputTests` 9/9, `ResultsHtmlTests` 25/25 |
| Mutation checks (temporary edits, reverted, byte-identical restore checked) | old read and write: 13 of the 22 new cases fail; fixed write with the old unconditional read: 7 fail; fixed read with the old `!= Console` write: 9 fail; history disabled for Email as well: 8 fail (the Email cases) |
| `bash .github/release-policy/test-release-policy.sh` | 177/177 (no release-policy file changed) |
| Vulnerability scan (each of the 5 `.csproj`, `--include-transitive`) | 0 vulnerable packages |

**Before/after** (scratch harness, not in the repository: real `Load.GetConfig` → `Preload` → `GetAvailabilitiesByCampground` → `PostProcess` per manual run, each run a new process, one in-memory month of campground 232445 two months ahead with 7 matches; stops at "Press any key"; Email cycles replicate only the gate; no network). Matches per run; markers after the sequence:

| Sequence | `master` `cb504cb` | this branch |
|---|---|---|
| Console, Console | 7, 7; 0 markers | 7, 7; 0 markers |
| HtmlFile, HtmlFile (separate processes, and twice in one process) | 7, **0**; 7 markers | 7, 7; 0 markers; same page (7 booking links) both times |
| Console, HtmlFile, Console | 7, 7, **0** | 7, 7, 7 |
| HtmlFile, Console | 7, **0** | 7, 7 |
| UnitTest, Console | 7, **0** | 7, 7; 0 markers |
| Email, restart, Console | 7, 0, **0** | 7, 0, 7 |
| Email, Console, HtmlFile, Email, Email (changed month) | 7, **0**, **0**, 0, 1 | 7, 7, 7, 0, 1; the 7 Email markers unchanged by the manual runs (diff empty) |
| Email cycles base, base, changed (site removed, site added), base (reappears), changed | 7, 0, 1, 0, 0; 8 markers | 7, 0, 1, 0, 0; 8 markers; the Email-written marker paths identical to `master` |
| Overlapping single-night and two-night searches, HtmlFile, HtmlFile, Console | 12, 2, 2 (only the 5 markers the two-night search left, TD-04) | 12, 12, 12; 0 markers |

**Parallel vs one at a time** (§R requirement; scratch harness, not in the repository; no network: the month URLs are claimed in advance, and the harness fails on any other URL). Seeded in-memory months (this month and the next two, every campsite of 232445, 30 % available, seed `20261114`); each pass has one `RunningData` shared by its searches, as `Load.RunConsoleSearch`; the reference is the searches one at a time in file order, then N parallel passes; per search it compares the entry count (Availability/ConsecutiveDate values, the §R metric), the exact text and the multiset of lines. Built against `master` `cb504cb` and against `b52e581` (the library sources checked byte for byte). Modes: Console with an empty `Camp.History`, Console with a marker for every available site and date, Email with those markers.

| Scenario | Result, `master` and this branch alike |
|---|---|
| **Narrow** (reconstruction of §R scenario C: single nights `DaysOut` 7 + two nights `DaysOut` 30, no filters), 200 iterations | reference 416 / 863 entries on both builds. Single nights: 0 differences of any kind. Two nights: **0 iterations with a different entry count** (the §R result), but the text differs in 77 (`master`) / 74 (branch) iterations, as a multiset too: site blocks present or missing (sample iteration: 161 site blocks in the reference, 173 in parallel) |
| **Broad** stress (single nights, weekends only, a 10-site `includeSites` subset, two nights, three nights; one month two months ahead), 100 iterations, run twice | reference 1,811 / 896 / 85 / 853 / 758 entries on both builds. The three single-night searches: 0 differences. Two nights: more entries than the reference in 100/100 iterations. Three nights: fewer entries in 23 and 26 (`master`) / 33 and 25 (branch) iterations, text differences in 89–95 |
| With every hit marked | Console: `master` 0 entries for every search (the defect), this branch the full reference counts, with the same TD-04 pattern as with an empty history. Email: 0 entries on both builds. Marker count unchanged afterwards (the search writes none) |

Interpretation: the narrow and broad results differ because of the scenario, not the build. The broad scenario puts five searches with overlapping campsites and dates into one shared `FilteredAvailableData`, which `FilterConsecutiveDays` reads (other searches' entries count towards a site's group and supply its first entry) and replaces (dropping entries a slower search has already added), so it changes even the entry counts of the consecutive-day searches; in the narrow scenario the single-night entries are a subset of the two-night search's own, so only the site block varies. [I] Both are TD-04 (open, not changed here): only consecutive-day searches are affected, single-night searches never differ, and `master` shows the same pattern at rates within the same run-to-run spread. The notification-history fix changes only whether `FilterData` consults `Camp.History` (always false for Console), which is the same for every search and pass.

**What to run after changes to the search or to `Camp.History`:** `NotificationHistoryTests`, `SharedMonthSearchTests`, `AvailableDatesTests`, `ConsoleOutputGoldenTests`, then a parallel-versus-one-at-a-time comparison like the above. **A regression of this fix** is: any marker written or read by a Console, HtmlFile or UnitTest run; any difference between Console with and without markers present in the one-at-a-time pass; any parallel difference for a single-night search; or Email reporting a marked campsite and date. Differences in consecutive-day searches that `master` shows too are TD-04, not a regression.

**Network, email and browser** [V]: the new `NotificationHistoryTests`, the new `LoadTests` cases and all scratch harnesses made no recreation.gov request (month data in memory, URLs claimed in advance; the harnesses fail on any other URL and ran with a dead `HTTPS_PROXY`). The existing `LoadTests.RunConsoleSearchTest` made its known live request (TD-01, one GET for campground 232445) in each full-suite run (Release and Debug) and in the focused `LoadTests` run; no code in this branch adds live-network behavior. No email was sent (the Email path was stopped at the gate; the existing email tests use a closed loopback port) and no browser was opened (`OpenHtmlFile` replaced; for the beta.8 comparison a recording `xdg-open` first on `PATH`).

### Refreshing the README screenshots (maintainer procedure)
Recorded 2026-11-11 for branch `docs/rewrite-readme` (application built from `fa15649`). All captures use isolated data and no personal paths, names or searches.

| File | Shows | Source |
|---|---|---|
| `docs/campertron.png` | hero image (a 1024×1024 JPEG despite the `.png` name) | unchanged; keep unless the maintainer approves a replacement |
| `docs/ss1.png` | console main menu: the logo and the four options, Search selected | **recaptured 2026-11-16** (§README and screenshots reconciled, below) from the current console, 735×365, in the terminal setup of `ss2` and `ss5` (60×13 cells), shown 470 px wide. Before: a 2024 Windows Terminal capture with its title bar and tab, 1115×628, kept until then because the menu's logo, labels, colors and arrow were unchanged |
| `docs/ss2.png` | console search results after the console output polish (D34): the list of searches, the "Checked" line, the shipped Zion example's results grouped by date with booking links, the November 2026 calendar with `*` marks, the empty December and the summary. **Example data:** the campground and campsites are real rows of the bundled database; the availability was supplied by a local stand-in for recreation.gov | recaptured 2026-11-14 from the final D34 code, 1115×987 (before: a 2024 Windows Terminal capture of the former layout, 1115×628) |
| `docs/ss5.png` | console View Config after D34 (version, folders, output, searches, refresh age, OS, `Framework => .NET 11.0.0 (X64)`) | recaptured 2026-11-14 from the final D34 code (1115×552); **cropped 2026-11-16** to 735×526, without resampling, to the same 60 columns as `ss1` (only background was removed), shown 470 px wide |
| `docs/ss6.png` | GUI, Campsites > By Park (the four top-level tabs, the By Park, By City and By State row), Zion National Park / WATCHMAN CAMPGROUND ("— SPRINGDALE, UT", "ID 232445"), **Dark** | **recaptured 2026-11-16 for D50** from `456c351`, 1600×1424 (§AO). Before: the six-tab capture of earlier that day; the R8 capture of 2026-11-14, 1400×1044, three tabs; earlier captures: 2026-11-12 (beta.8), 2026-11-11 |
| `docs/ss7.png` | HTML results page (`outputTo: HtmlFile`), desktop (1280 CSS px), **Dark**: header ("2 searches"), navigation, the Mather section with its empty state, the start of the Zion section (counts, check-in/out, November 2026, Friday 11/13/2026, the first row of site cards) | recaptured 2026-11-12 after the beta.9 review (no generation time) from a real isolated run of the two shipped examples, 1400×900 (shown 800 px wide) |
| `docs/ss8.png` | HTML results page at phone width (390 CSS px), **Dark**: the Zion section with counts, check-in/out, November 2026, Friday 11/13/2026 and the cards for D016HC, D040 and F017 | same run, 780×1530 (shown 320 px wide) |
| `docs/ss9.png` | GUI, Results tab after a real search of the two enabled example searches (recreation.gov, 2026-11-16): the list of searches, "Finished: 3,436 openings found in 2 searches", the Zion search's results by night, a selected campsite (C003) and the three actions, **Dark** | **recaptured 2026-11-16 for D50**, 1600×1424 (§AO). Before: the six-tab capture of earlier that day; 2026-11-15 (GS2, §AK), 1200×900 with stand-in data, which left 220 px of empty window beside the capped content column |
| `docs/ss10.png` | GUI, Settings > Campertron with `Email.yaml` selected: example values (`smtp.example.com`, `you@example.com`), the hidden password and **Show password**, **Dark** | **recaptured 2026-11-16 for D50**, 1600×1424; first added earlier that day (the Settings tab before D50) |
| `docs/ss11.png` | GUI, Searches tab (the first of the four top-level tabs): three search files (two enabled, one disabled, the Zion example selected), the new-search box and the start of the selected search's form, **Dark** | **recaptured 2026-11-16 for D50**, 1600×1424; first added earlier that day |
| `docs/ss12.png` | GUI, Settings > System: the Theme card with System, Light and Dark (Dark chosen), "Campertron is shown in its Dark theme." and where the choice is kept (`/home/camper/.local/share/CampertronGUI/preferences.json`), **Dark** | **new 2026-11-16 for D50**: the 1600×1424 capture cropped to 1600×824 without resampling (only the black background below the card was removed; compared with the capture's top part: 0 different pixels), shown 800 px wide like the others |

`docs/ss4.png` (a second menu screenshot) was removed as a duplicate of `ss1.png`. The README references images by relative path (`docs/…`), so a branch shows its own images.

**README and screenshots reconciled before `v0.1.0-preview.2` (2026-11-16, branch `docs/readme-current-screenshots`)** [V]. The README showed the three-tab GUI of R8 (`ss6`) directly before the six-tab Results screenshot, so two different interfaces looked current, and `ss9` (1200×900) left a wide empty strip to the right of the content. Rules since then:
- **Only the current application is shown.** A screenshot of an earlier version is replaced, also when the feature it shows still exists; nothing is kept as history in the README (no historical screenshot remains).
- **One framing for the GUI:** the real window's whole client area (since D50 one exception: `ss12`, whose tab is short, is the same capture cut below its card, same width and scale), 1000×890 device-independent pixels, captured at scale 1.6 as 1600×1424 pixels and committed without resampling (`magick <capture> -strip -define png:compression-level=9`; compared with the capture: 0 different pixels); shown 800 px wide (exactly half). The content column is capped at 976 (`ContentColumnMaxWidth`), so a window of at most 1016 leaves no empty strip; the 16-pixel gutter on the right is the window's own and holds the scroll bar. 890 high because the Results tab gives its list of searches a tenth of its height once results exist: at 890 the list ends after a search's first line instead of through a line.
- **One framing for the console:** `ss1` and `ss5` are 60 columns of the same cells (735 px wide, shown 470 px wide, the text size of `ss2` at 800).
- **How the GUI set was captured** (Linux x64; no window on the live desktop): a `git archive` export of `master` (`b9aa8e5`); a scratch launcher (assembly name `CampertronGUITests`, so it sees the GUI's internals; published self-contained) that starts the real application with `Program.BuildAvaloniaApp()`, requests the Dark theme, sets the window to 1000×890 at position 0,0, chooses the tab and makes the selections in the window's own controls or view models (By Park: the park and the campground; Settings: `Email.yaml`; Searches: the Zion file; Results: **Start** through the tab's own command, then the Zion result and its second campsite), waits, and runs `import -window <the window's X11 ID>`; no key or pointer input, so there is no focus ring, hover or open list. It runs in the nested session of the R8 capture (`dbus-run-session --config-file=<no service directories> -- kwin_wayland --virtual --xwayland --width 2600 --height 1900 --exit-with-session=<script>`, a short `XDG_RUNTIME_DIR`) with `AVALONIA_GLOBAL_SCALE_FACTOR=1.6`, and inside `unshare -rm` with a `tmpfs` on `/home` and `HOME=/home/camper`, so the paths the Settings and Searches tabs show are `/home/camper/.local/share/CampertronConfig` and nothing of the real home is visible (the launcher is self-contained because the SDK under the real home is hidden too).
- **Data:** an isolated folder with `General.yaml` (`outputTo: Console`, `autoRefresh: false`, a recent `lastRidbDataRefresh`), an `Email.yaml` with the README's example values, and three search files: copies of the shipped examples as `Zion tent sites.yaml` and `South Rim weekends.yaml`, and `Watchman two nights.yaml` (the Zion example with `autoRun: false` and `consecutiveDays: 2`). The Results capture is a real search (4 month requests to recreation.gov, none failed), so a recapture shows other numbers.
- **`ss1`:** the current console (`dotnet publish … -p:Version=0.1.0-dev`) in `alacritty` as for `ss2` and `ss5` (font size 13, padding 10/8, no decorations, the "Campbell" colors, `WINIT_X11_SCALE_FACTOR=1.5`), 60×13 cells, no key; `import -window`, then `magick … -filter Lanczos -resize 735x -strip -define png:compression-level=9` (the same cell size as `ss5`). **`ss5`:** `magick docs/ss5.png -crop 735x526+0+0 +repage -strip -define png:compression-level=9` (the removed parts held only the background color).
- **Unchanged because current and well framed:** `ss2` (the console's results since D34), `ss7` and `ss8` (the results page since beta.9; D34 kept the page byte-identical).
- **Checked as GitHub shows it:** the README rendered with GitHub's Markdown API (`gh api markdown`, mode `gfm`) into a page with GitHub's content width, viewed in headless Chrome at 1280 and 400 CSS px, and the branch's README on github.com after the push (the pull request records it).

**ss6 (GUI)** [V on Linux x64, KDE Plasma Wayland with XWayland, ImageMagick 7]
1. `dotnet build CampertronGUI` (Release), and an isolated data directory `X` with `CampertronConfig/General.yaml` (`outputTo: Console`, `autoRefresh: false`, a recent `lastRidbDataRefresh`). The GUI extracts the bundled database on start.
2. A scratch launcher (not in the repository) references `CampertronGUI.csproj` and starts the real app with the production chain `AppBuilder.Configure<Campertron.App>().UsePlatformDetect().LogToTrace().UseReactiveUI(_ => { })`. After start it sets the main window to 1100×820, selects the **By Park** tab and the ComboBox items `Zion National Park` and `WATCHMAN CAMPGROUND` in the window's own controls, and waits. Since beta.8 the app follows the system theme. The README screenshot is the **Dark** theme: run on a desktop whose color scheme prefers dark (`org.freedesktop.appearance color-scheme` = 1 through the settings portal) and let the app follow it; the launcher logs `ActualThemeVariant=Dark Requested=Default` and sets no theme. (The earlier Light captures set `Application.Current.RequestedThemeVariant = ThemeVariant.Light` on a Dark desktop.) The launcher also logs the client size, scaling and focused element; the selections are made programmatically, so no element has keyboard focus and no focus ring, dropdown or tooltip appears.
3. Run it with `AVALONIA_GLOBAL_SCALE_FACTOR=1.5 XDG_DATA_HOME=X` and capture the X11 window with `import -window Campertron <file>`. The effective scaling was 2.17 (1.5 × the desktop's 1.45), giving 2390×1781.
4. `magick <file> -filter Lanczos -resize 1400x -strip -define png:compression-level=9 docs/ss6.png` (1400×1043). The beta.8 details scroll, so there is no empty strip to trim; the 2026-11-11 capture used `-crop 2390x1726+0+0 +repage` first.
- `RenderTargetBitmap` of the window was tried first and **does not work** for this: borders and text were drawn with different offsets. Capture the real window instead.
- **R8 capture (2026-11-14), without the live desktop:** the same launcher steps (production chain, 1100×820, By Park, the two selections, no focus) in a nested `kwin_wayland --virtual --xwayland --width 2600 --height 1900` session under `dbus-run-session --config-file=<a session-bus configuration without service directories>` (so nothing is D-Bus-activated), with `AVALONIA_GLOBAL_SCALE_FACTOR=2.17` (effective 2.17, as before) and an isolated `XDG_DATA_HOME`. That session has no settings portal, so the launcher requested `ThemeVariant.Dark` (logged `requested=Dark actual=Dark`, focus none). `import -window` gave 2387×1780, resized with `magick … -filter Lanczos -resize 1400x -strip -define png:compression-level=9` to 1400×1044. Fonts come from the same fontconfig; without the desktop's Xft settings, semibold text looks slightly heavier than in the 2026-11-12 capture.

**ss7 and ss8 (HTML results page)** [V on Linux x64, Chrome 155 headless, ImageMagick 7]
1. In an isolated `X=$(mktemp -d)`: `X/CampertronConfig/General.yaml` with `outputTo: HtmlFile`, `autoRefresh: false` and a recent `lastRidbDataRefresh`, plus the two shipped searches `examples/0.1/ZionConfig.yaml` and `SouthRimConfig.yaml` (public campgrounds, no personal values).
2. Produce the page with the real code: either choose **Search** in `XDG_DATA_HOME=X dotnet run --project CampertronConsole -c Release` and copy the `{guid}.html` from `X/CampertronCache`, or use a scratch harness (not in the repository) that runs `Load.GetConfig`, `Preload`, `GetAvailabilitiesByCampground` per search in `Parallel.ForEach` and `Load.OutputResults`, with an `xdg-open` stand-in on `PATH` that copies the file it is asked to open (no browser starts). The page has no generation time, path or local data.
3. Start Chrome headless with a scratch `--user-data-dir`, `--hide-scrollbars` and `--remote-debugging-port`, and drive it over the DevTools protocol without any key or mouse input (no focus ring): `Emulation.setEmulatedMedia` with `prefers-color-scheme: dark`, then `Emulation.setDeviceMetricsOverride` with `deviceScaleFactor: 2` and `Page.captureScreenshot` of the viewport, for ss7 at 1280×900 (`mobile: false`, top of the page) and for ss8 at 390×844 (`mobile: true`, scrolled to 12 px above the Zion section, `#search-2`).
4. Crop at the bottom of a row of cards so no card is cut, then: `magick <ss7 capture> -crop 2560x1646+0+0 +repage -filter Lanczos -resize 1400x -strip -define png:compression-level=9 docs/ss7.png` and `magick <ss8 capture> -crop 780x1530+0+0 +repage -strip -define png:compression-level=9 docs/ss8.png` (the crop heights fit the 2026-11-12 data; choose them from the capture).
- The results are live recreation.gov data, so a recapture shows other sites and dates (on 2026-11-12 the Mather example had no results in this run; see §Q). The command-line `--screenshot` of headless Chrome cannot scroll to a section of a large page (it captured blank areas), which is why the DevTools protocol is used.

**ss2 and ss5 since the console output polish (2026-11-14)** [V on Linux x64, alacritty 0.17, ImageMagick 7; no window on the live desktop]
1. `dotnet publish CampertronConsole -c Release -r linux-x64 --self-contained true -p:Version=0.1.0-dev -p:IncludeSourceRevisionInInformationalVersion=false -o <dir>`: View Config shows the version, and a development label claims no released or future version.
2. A nested session as for `ss6`: `dbus-run-session --config-file=<a session-bus configuration without service directories> -- kwin_wayland --virtual --xwayland --width 1900 --height 1900 --exit-with-session=<script>`, with a short `XDG_RUNTIME_DIR` (the Wayland socket path must stay below 108 bytes).
3. The script runs `unshare -rm <inner script>`. The inner script mounts a `tmpfs` on `/home` (so nothing under the real home is visible: take every input from elsewhere), creates `/home/camper/.local/share/CampertronConfig` with `General.yaml` (`outputTo: Console`, `autoRefresh: false`, `lastRidbDataRefresh` four days earlier), the shipped `examples/0.1/ZionConfig.yaml` (for `ss5` also `SouthRimConfig.yaml`), an already extracted `RecreationDotOrg.db` and that campground's cache files, sets `HOME=/home/camper`, unsets `XDG_DATA_HOME` and `WAYLAND_DISPLAY`, and sets `WINIT_X11_SCALE_FACTOR=1.5`.
4. For `ss2` the search must not reach recreation.gov and must be repeatable: a local HTTPS stand-in answers as `www.recreation.gov` through `HTTPS_PROXY` with a throwaway CA in `SSL_CERT_FILE` (and `SSL_CERT_DIR=/nonexistent`), returning real campsite IDs of the campground with invented availability (2026-11-14: site 343 open on 11/20, 11/21 and 11/27, site 440 on 11/20, nothing in December).
5. Start `alacritty --config-file <toml> -e sh -c "stty raw -echo; { (sleep 4; <keys>; sleep 60) & cat; } | script -q -c 'stty cols <C> rows <R>; exec <dir>/CampertronConsole' /dev/null"` with font size 13, padding 10/8, no decorations and Windows Terminal's "Campbell" colors. `ss2`: 104×41 cells, key Enter (Search). `ss5`: 92×20 cells, keys Down, Down, Enter. The `stty cols … rows …` matters: through the pipe `script` cannot learn the terminal's size, the console then sees no usable width and wraps at its 80-column default.
6. After 9 s find the window (`xprop -root _NET_CLIENT_LIST`, the one whose `WM_CLASS` is Alacritty), `import -window <id> <file>`, then `magick <file> -filter Lanczos -resize 1115x -strip -define png:compression-level=9 docs/ss2.png` (and `ss5`). Look at both images before committing them.

**ss5 before the console output polish (2026-11-11)** [V same environment]
1. `dotnet publish CampertronConsole -c Release -r linux-x64 --self-contained true -o <dir>`.
2. For a neutral path, run inside an unprivileged user and mount namespace with a stand-in home: `unshare -rm <script>`, where the script mounts a `tmpfs` on `/home`, creates `/home/camper/.local/share/CampertronConfig/General.yaml` (as above, `lastRidbDataRefresh` a few days earlier), sets `HOME=/home/camper`, unsets `XDG_DATA_HOME` and `WAYLAND_DISPLAY`, and starts `alacritty` with 92×17 cells, font size 13, padding 10/8 and Windows Terminal's "Campbell" colors (to match `ss1`/`ss2`).
3. In the terminal: `stty raw -echo; { (sleep 5; printf '\033[B'; sleep 0.7; printf '\033[B'; sleep 0.7; printf '\r'; sleep 40) & cat; } | script -q -c <dir>/CampertronConsole /dev/null` (two Down arrows and Enter select View Config). Raw mode and `cat` are needed because the console queries the cursor position; a plain pipe hangs it.
4. Find the window (`xprop -root _NET_CLIENT_LIST`, then the one whose `WM_CLASS` is Alacritty), `import -window <id> <file>`, then `magick <file> -filter Lanczos -resize 1115x -strip docs/ss5.png`.

### Refreshing the embedded database (maintainer procedure)
Verified 2026-11-10 for `07e786d`. All steps run in a scratch directory, never in the user's data dirs.
1. `dotnet build Campertron.sln -c Release`
2. Create an empty `X=$(mktemp -d)` and write `$X/CampertronConfig/General.yaml` with `outputTo: UnitTest`, `autoRefresh: false`, `refreshRidbDataDayInterval: 30` and any `lastRidbDataRefresh`. Then run `XDG_DATA_HOME=$X CampertronConsole/bin/Release/net11.0/CampertronConsole` (`net10.0` when this procedure was recorded, before D31). This extracts the bundled DB and exits without a menu.
3. Set `autoRefresh: true` and a `lastRidbDataRefresh` older than 30 days, then run the console again. It downloads the RIDB export (about 573 MB), imports the 6 used files and validates the result (about 15 s after the download since §M; the download itself took about 50 s in one sample). **The output must not contain "failed validation".**
4. `sqlite3 $X/CampertronConfig/RecreationDotOrg.db "VACUUM; PRAGMA integrity_check;"` must print `ok`.
5. Compress with .NET's built-in encoder, not a CLI tool, so the output matches what `BrotliStream` reads. Use `new System.IO.Compression.BrotliEncoder(11, 24)`, feed the file through `Compress(..., isFinalBlock: false)` and finish with `isFinalBlock: true`. Then verify that decompressing through `BrotliStream` gives the same SHA-256 as the DB. This takes about 6 minutes.
6. Replace `CampertronLibrary/content/RecreationDotOrg.db.br`, then run the full isolated test suite. `EmbeddedDatabaseTests` checks the shipped DB with the same validation as step 3.
7. GitHub rejects files over 100 MiB; the file is currently 26.6 MB. Record the export date, ETag, sizes and hashes in this file.
8. Since D51 (§AQ) the refresh of step 3 builds Campertron's index `IX_CampsiteAttributesEntries_EntityID` (about 35 MB more before compression), so a database embedded after this procedure has it; `RidbDatabase.Validate` and `EmbeddedDatabaseTests` do not require it, and every start builds it on a database without it.

### Test-run side effects (isolated dir)
On Linux, `LocalApplicationData` resolves to `$XDG_DATA_HOME` (default `~/.local/share`); before D37 it was `""` (the working directory) when that folder did not exist, and since D37 `Load.GetConfig` creates it (TD-47). Each run, as recorded on 2026-11-10 (the `Camp.History` line no longer applies: since the notification-history fix, §T, the test runs write **0** markers (measured in the §T full runs); the live request and the extraction remain, TD-01):
- extracted the DB (328 MB at the time; 226 MB since `07e786d`)
- created 384 cache JSON files
- made 1 live `GET https://www.recreation.gov/api/camps/availability/campground/232445/month?start_date=2026-11-01…`
- created `Camp.History` markers: 118 on the net9.0 run, 116 on the net10.0 run, because live availability changed between runs

The real `~/.local/share/CampertronConfig` listing was compared before and after each run and was unchanged.

### U. .NET 11 platform migration (decision D31, branch `feat/net11-platform`, 2026-11-14)

**Why.** An investigation of the SDK used by CI found that GitHub's `ubuntu-26.04` runner images 20261111.82.6 and 20261111.196.1 (first seen 2026-11-12 in run 43219645735) preinstall .NET SDK 11.0.100 next to 10.0.404, while image 20261101.186.1 does not. With no `global.json`, `dotnet` used the newest installed SDK, so each job compiled with SDK 11.0.100 or 10.0.404 depending on its runner image, although `setup-dotnet` requested `10.0.x` (it reported "10.0.404 is already installed" and changed nothing). The compiler identity is recorded in each portable PDB (`compiler-version`, `runtime-version`): in `v0.1.0-beta.9`, 15 of the 16 archives (all but console `win-x86`) were compiled by SDK 11.0.100 (Roslyn 5.12.0 on .NET 11.0.0), and in `v0.1.0-beta.8` 2 archives (console `osx-arm64`, `win-arm64`). Checked directly: beta.9 console `linux-x64` (SDK 11) and `win-x86` (SDK 10.0.404), beta.8 console `osx-arm64` (SDK 11) and `linux-x64` (SDK 10.0.404); the others by their job's runner image. **Every beta.8 and beta.9 artifact still targeted net10.0 and bundled .NET 10.0.15, all .NET 10 guards passed, and no functional difference was found**; SDK 11 output differs only in Campertron's own DLLs/PDBs and `deps.json` (`localPath` entries). The Docker build was unaffected (`sdk:10.0` = SDK 10.0.404). EF Core 9 and the .NET 9 Microsoft.Extensions packages reached end of support on 2026-11-10. The maintainer chose a direct move to .NET 11 and EF Core 11, without a temporary .NET 10 SDK policy or EF Core 10.

**Platform.** .NET 11.0.0 / SDK 11.0.100 (GA 2026-11-10; STS, supported until 2028-11-14; official release metadata) were still the newest stable versions on 2026-11-14. Changes: the six `.csproj` files `net10.0` → `net11.0` (byte-order marks kept); root `global.json` (version `11.0.100`, `rollForward: latestFeature`, `allowPrerelease: false`, `errorMessage`; no other key); Dockerfile `sdk:11.0` / `runtime:11.0`; `RuntimeInfoTests` expects .NET 11; a comment in `ConfigFileReplace.cs`. No `LangVersion` (C# 15 is the SDK default). Application logic is unchanged.

**Dependencies.** Library: EF Core Sqlite 9.0.19 → **11.0.0**, FileProviders.Embedded 9.0.8 → **11.0.0**, EF Core Tools 9.0.19 removed. Console: EF Core Tools and the redundant FileProviders.Embedded removed. Resolved: EF Core, Relational, Abstractions, Analyzers and Microsoft.Data.Sqlite.Core 11.0.0; Microsoft.Extensions Caching.Memory, DependencyInjection, DependencyModel, Logging and FileProviders.Embedded 11.0.0 (seven other Microsoft.Extensions packages are part of the .NET 11 shared framework and no longer restored); SQLitePCLRaw 2.1.12 → **3.0.5** (bundle_e_sqlite3, config.e_sqlite3, core, provider.e_sqlite3) with the native `SQLite` **3.53.4** package (SQLitePCLRaw's maintainer; 3.53.3 before). The native file names are unchanged on all 8 RIDs (`e_sqlite3.dll`, `libe_sqlite3.so`, `libe_sqlite3.dylib`); `SQLitePCLRaw.batteries_v2.dll` keeps its name. The restore graph went from 104 to 78 distinct packages (EF Core Design, Roslyn workspaces, Humanizer, Mono.TextTemplating, System.CodeDom 6.0.0 and System.Composition 7.0.0 no longer come in through Tools), with no prerelease, no vulnerable and no deprecated production package. Unchanged by decision: Avalonia 12.1.6, ReactiveUI.Avalonia.Reactive 12.1.6, YamlDotNet 16.3.0, Newtonsoft.Json 13.0.3, the test packages (two deprecated test-only transitive packages, open question #36), setup-dotnet v6.0.0. EF Core 10/11 breaking changes checked against the code: no mapped DateTime/DateTimeOffset, no `GetDateTime*` (Microsoft.Data.Sqlite 10 UTC changes), no parameterized collections, no `Migrate()`, no complex types or JSON columns; EF only renames SQL parameters (`@__State_0` → `@State`). EF Core 11's analyzer reports **EF1003** for `ExecuteSqlRaw("DROP TABLE IF EXISTS \"" + OmittedTable + "\"")` in `Clear.cs`; the table name comes from the constant `RidbDatabase.OmittedTables` and identifiers cannot be parameterized, so one statement has a justified `#pragma warning disable/restore EF1003`.

**Guards** (`release-policy.sh`, `DOTNET_MAJOR_REQUIRED=11`): `check_global_json` (strict JSON, exactly the four approved keys and values, `version` matches `^11\.0\.[1-9][0-9]{2}$`), `check_selected_sdk` (output of `dotnet --version`, same pattern) after every `setup-dotnet` with `dotnet --info` in the log (CI, `verify`, all 16 `binaries`, `smoke`), `check_package_policy` on every restored `project.assets.json` (fails closed without readable input; rejects prerelease packages and EF Core / Microsoft.Data.Sqlite / Microsoft.Extensions packages of another major; other packages may target older frameworks), `check_pdb_provenance` (`pdb_option` reads the null-separated compilation options of `CampertronLibrary.pdb` and `<app>.pdb`; `runtime-version` must start with 11; official and source-built stamps both accepted; only Campertron's own PDBs, so the TD-35 exclusion of native PDBs is unaffected) for every publish output and the container, `check_single_toolchain` over the `CampertronLibrary.pdb` of all 16 archives in `assemble`, and `check_publish_runtime`'s JSON check now rejects any `"netN.0"`, `.NETCoreApp,Version=vN.` or runtime pack `/N.` other than 11 (package asset paths such as `lib/net8.0/` are allowed). `global.json` was added to the release dry-run PR paths. `test-release-policy.sh`: 223 tests = 176 of the 177 existing tests retargeted to .NET 11 with unchanged meaning, plus 47 new ones (global.json 14, project/Dockerfile 2, publish JSON 4, selected SDK 7, PDB provenance 6, toolchain 4, packages 10). The 177th existing test, "global.json pinning SDK 10 accepted", contradicts the policy and was replaced by "global.json: SDK 10 rejected" (one of the 47). Three package tests cover the fail-closed paths of `check_package_policy`: no asset files, a missing asset file and invalid asset JSON (each needs a non-zero exit and, for the last two, the matching diagnostic; a guard without these checks fails all three).

**Validation** [V] (Linux x64, CachyOS; `/home/shaun/.dotnet11/dotnet` SDK 11.0.100 / runtime 11.0.0; comparison builds of `master` `6da5179` with the system SDK 10.0.114 / runtime 10.0.14; a copy of the working tree in a scratch directory with its own `DOTNET_CLI_HOME`, NuGet caches, `XDG_DATA_HOME`, test, build and publish output; the real `CampertronConfig`/`CampertronCache`, the installed SDKs and `~/.nuget` unchanged)
| Check | Result |
|---|---|
| SDK selection | `11.0.100` at the root and in all six project directories; system `dotnet` (SDKs 9.0.123, 10.0.114): exit 155 with the `errorMessage`. Copied SDK root with folders `11.0.100`, `11.0.200-preview.1`, `12.0.100`: the repository selects `sdk/11.0.100` (outside it: 12.0.100); with a stable `11.0.200` added it selects `sdk/11.0.200`; with only 12.0.100 it fails with the message |
| Restore Debug/Release | exit 0, no NU warnings |
| Build Debug/Release (`--no-incremental`) | 0 errors, **76 warnings, file/line/code identities identical** to `master` built on .NET 10; no NU, NETSDK, SYSLIB, CA, EF, AVLN or IL warning |
| Tests (`XDG_DATA_HOME` isolated) | **278/278** (244 + 34 GUI) |
| Release-policy tests | **223/223** |
| Vulnerable packages, 5 projects | 0 |
| Package policy | passes on the restored graph; rejects `master`'s EF Core 9 graph |
| Embedded DB (extracted, then every `Read.*` query family and `RidbDatabase.Validate`) | `8712856097a91ee1…` before and after (unchanged); query results (89 result sets) and EF `GenerateCreateScript` DDL byte-identical to `master`; SQL identical apart from parameter names |
| Production RIDB refresh (`RefreshData` with the production `Compression.Unzip`/`ZipFile` extraction, .NET 11 ZIP CRC32 validation; live export ETag `954c7d08…`, 573,705,015 bytes; culture en-US) | both builds 17–18 s, `refresh=True`; 233,889,792 bytes; `master` `85c96f90…`, D31 `1b9df117…`, the same two hashes as the investigation; **exactly one byte differs, offset 99**: SQLite's library version number in the file header (3053003 → 3053004) |
| 16 publishes (`release.yml` flags) | all exit 0; package-contents checks, TD-35 (0 native PDB files and 0 `deps.json` entries on the 3 Windows GUI targets), `check_publish_runtime` (11.0.0) and `check_pdb_provenance` pass on all 16; native SQLite library architecture matches each RID; files: console 217–219, GUI 262–263 (linux-x64: +2 `Microsoft.Extensions.*.Abstractions` DLLs that are part of the .NET 11 runtime); one toolchain: `5.12.0-1.26529.118+297acf930…` on `11.0.0-rtm.26529.118+297acf930…` |
| Workflows | parsed with YamlDotNet 16.3.0; 4 `setup-dotnet` steps (v6.0.0, `11.0.x`), each followed by the SDK check; 49 `run:` blocks pass `bash -n`; the new CI, `binaries`, `assemble`, `smoke` and `docker` steps run verbatim (simulated runner variables): pass on the real outputs; `assemble` fails with both toolchains and their archives listed when one archive carries a real SDK 10 `CampertronLibrary.pdb` |
| linux-x64 smoke (release steps verbatim) | console clean install (DB 226,471,936 bytes), `--version` `.NET 11.0.0 (X64)` for both apps, `ReleaseCheck` passes for both; published GUI still running at 30 s on the real display (no `xvfb-run` here) |
| GUI shutdown | scratch launcher (production `AppBuilder` chain, isolated data): `MainWindow` visible, `Close()`, lifetime and process exit 0, no unhandled exception, **3/3** |
| Container (dedicated buildx builder, removed afterwards) | linux/amd64 + linux/arm64 build; amd64 image: Ubuntu 26.04.1, `DOTNET_VERSION` 11.0.0; the docker job's two test steps verbatim: refusal without `/config`, DB extraction with `/config` and `/cache`, runtimeconfig and `--version` on 11.0.0, image PDBs from an SDK 11 compiler |

**Not verified locally:** native Windows and macOS execution (R8, open question #34); hardware older than x86-64-v2. Pre-existing and unchanged: the container exits 139 after an unhandled exception (open question #35).

**Hardware** [V: Microsoft's .NET 11 breaking change "Minimum hardware requirements updated"]: x86/x64 needs x86-64-v2 (SSE4.2, POPCNT, …) instead of v1 on Windows, Linux and macOS (`win-x64`, `win-x86`, `linux-x64`, `osx-x64`, the amd64 container); ReadyToRun code targets x86-64-v3 on Windows and Linux, so v2-only CPUs start more slowly. Arm64: unchanged minimum; ReadyToRun targets armv8.0+LSE on Linux and armv8.2+RCPC on Windows, so older boards such as Raspberry Pi 3/4 start more slowly. All 8 RIDs are kept (README §System requirements).

### V. PDB-provenance checks fail the release (branch `ci/enforce-pdb-provenance`, 2026-11-14)
Workflow and test change only: `.github/workflows/release.yml` and `test-release-policy.sh`. Unchanged: `release-policy.sh`, the application, projects, Dockerfile and release notes.

**Defect.** D31 (PR #41, merged after `v0.1.0-beta.9`) added `check_pdb_provenance` to the package jobs and to the docker job.
- **Docker job** ("Container runs on .NET 11"): the check ran as the step's last command, `echo "Image compiler: $(check_pdb_provenance …)"`. Bash ignores the status of a command substitution used as an argument (and clears `-e` inside it), so a failed check left the step at exit 0. The `::error::` text was printed after `Image compiler: `, but the runner reads a workflow command only at the start of a line (actions/runner `ActionCommand.cs`), so no annotation appeared. A failed check could not stop the GHCR push or the release.
- **Package jobs** ("Check .NET 11 runtime and compiler provenance"): `toolchain="$(check_pdb_provenance …)"` failed the step, but the assignment captured the `::error::` line, so the job failed without a reason.
- **Affected:** the workflow from the PR #41 merge (`6d4d977`) until this fix. Only release dry runs used it (43609159756, 43613282648), and both logged the success-path identity (SDK 11 compiler). No published release was affected: beta.4–beta.9 predate the check.

**Fix.** Both call sites use `if ! value="$(check_pdb_provenance …)"; then echo "$value"; exit 1; fi`. A failed check fails the job and prints its `::error::` line at the start of a line; successful log lines are unchanged. A direct call was rejected. In a direct call under `set -e`, the `pdb_option` pipeline (`tr … | grep -m1`) returns the status of `tr`, which hits a broken pipe on real-size PDBs, so valid images failed in a reproduction. [I] The runner's processes ignore SIGPIPE, which explains the `tr: write error: Broken pipe` lines in the release logs. Inside `$(…)`, bash clears `-e`, so that status is harmless there.

**Tests.** `test-release-policy.sh` gains 8 tests (223 → 231).
- `workflow_step` extracts a step's `run: |` block from `release.yml`.
- The steps run under `bash -e` in a scratch workspace, with fixture PDBs and a `docker` stub that exits 99 on any unexpected call. No Docker, network or repository write is involved.
- Each step has four cases: SDK 11 PDBs are accepted; the success log line is unchanged; an SDK 10 PDB fails the step; the failure leaves a line starting with `::error::`.

| Check [V] (Linux x64, SDK 11.0.100, scratch clones) | Result |
|---|---|
| `bash .github/release-policy/test-release-policy.sh` | **231/231**; with the previous `release.yml`, 228/231 (failing: docker enforcement, docker annotation, package annotation) |
| `workflow_step` vs YamlDotNet 16.3.0 (`release.yml` parsed, 7 jobs) | byte-identical `run` values for both steps |
| Both changed step scripts | `bash -n` passes |
| Both steps verbatim, real-size PDBs, SIGPIPE ignored | SDK 11: exit 0, unchanged log line (6 `Broken pipe` lines each). SDK 10: exit 1 and exactly one line starting with `::error::CampertronLibrary.pdb:` |
| Composed `v0.1.0-beta.10` notes (proposed fragment) | unchanged |

**Still open at the time.** Two items were recorded for separate investigation before `v0.1.0-beta.10` is published:
- The publishing job (`docker-publish`) rebuilt the image from the floating `sdk:11.0`/`runtime:11.0` tags and pushed it without repeating these checks, so the pushed image was not the image the docker job checked.
- The docker job inspected only the linux/amd64 image, so the arm64 image's PDB provenance was not inspected.

**Both are resolved by §W (D32):** the image is built once, both platforms are checked, and docker-publish pushes exactly the validated archive.

Also unchanged: the `Broken pipe` log noise, and the missing zero-project guard of `release.yml`'s vulnerability scan (`ci.yml` has one; added to `release.yml` by S2, §AM).

### W. The published image is the validated image (decision D32, branch `ci/publish-validated-image`, 2026-11-14)
Workflow, release-policy and test change only: `release.yml`, `release-policy.sh` (new functions only; no existing function changed, `render_notes` untouched) and `test-release-policy.sh`. Unchanged: the application, projects, Dockerfile, permissions, publishing gates, tag policy, archives and release notes.

**Gap.** The docker job built both platforms and smoke-tested a `--load`ed amd64 image, but kept neither the multi-platform result nor any digest. The tag-only `docker-publish` job created a new builder on another runner, rebuilt both platforms from the floating `sdk:11.0`/`runtime:11.0` tags (and the floating `moby/buildkit:buildx-stable-1` builder image) and pushed that build without any check. Rebuilds are not byte-identical [V, measured 2026-11-14]: two builds of `68b371e` in fresh builders, on the same machine, from the same context and the same base digests (`runtime:11.0@sha256:3fe5f549…`, `sdk:11.0@sha256:65f3c7c6…`), produced different image indexes (`2049ea3f…`, `9022dff0…`) and different amd64 and arm64 manifests, configs and app layers. The compiled files were byte-identical; the layer file times and the config `created` times differed, so pinning base digests alone would not make a rebuild identical. The arm64 image was never inspected.

**Design.** One build, checked, then published unchanged:
- **docker job** (every trigger, `contents: read`, no registry contact):
  - The single multi-platform build exports `--output type=oci,dest=$RUNNER_TEMP/container-image`.
  - "Check both platform images" runs `check_oci_archive` (`oci_unpack` + `check_oci_image`). The archive may hold only `oci-layout`, `index.json` and `blobs/sha256/<hex>` regular files. index.json must reference one image index, which must list exactly one linux/amd64 and one linux/arm64 image; its other entries may only be BuildKit attestation manifests (`unknown/unknown`, `vnd.docker.reference.type=attestation-manifest`), at most one per image. Every blob must match its descriptor, every layer its diff_id, and no layer may have an absolute or `../` member.
  - Per platform: config os/architecture; `User` app; entrypoint `dotnet CampertronConsole.dll`; `DOTNET_VERSION` 11.x; the version, revision and source labels; Ubuntu 26.04 (`usr/lib/os-release`); the `/app` files, with upper layers, whiteouts and opaque directories applied; a .NET 11 runtimeconfig; `libe_sqlite3.so` built for x86-64 or AArch64; Campertron's PDBs from an SDK 11 compiler. Across the two: the same `DOTNET_VERSION`, the same compiler and a byte-identical `CampertronLibrary.dll`.
  - The step writes `image_digest`, the platform manifest and config digests, both compiler identities and `archive_sha256` to the job outputs and the summary.
  - "Check the loaded image is the validated amd64 image" (`check_loaded_image`): the natively tested `campertron-smoke:local` must have the validated amd64 manifest digest as its ID (containerd image store) or the config digest (classic store), and the same layer diff_ids. Locally the ID was the manifest digest.
  - The existing smoke and .NET 11 steps then run on that image, unchanged.
  - "Rehearse the publication copy (no registry)": `skopeo copy --all --preserve-digests` from `oci-archive:` into a local `oci:` layout. `check_oci_copy` requires the same image index digest, with every manifest (both images and the attestations) and every blob present and unchanged, and `skopeo inspect --raw` must return exactly the index bytes.
  - The archive is uploaded unchanged as the artifact `container-image` (`archive: false`; the artifact takes the file's name). It is kept 1 day for dry runs and 7 days for tag runs, and doesn't match the `campertron-*` archive patterns.
- **docker-publish** (tag push only; `needs`, `if` and permissions unchanged):
  - It downloads `container-image`. Before `docker login` it requires the file SHA-256, the image index and both platform manifests to equal the docker job's outputs, and runs the full image check again.
  - "Push the validated image" (`publish_oci_archive`): for each reference from `docker_tag_refs` (the existing `docker_tag_args` policy: the exact tag only for a pre-release, plus `MAJOR.MINOR` and `latest` for stable):
    - It reads the tag first. If it exists with the validated digest, nothing is copied (an idempotent re-run). If it exists with any other digest, the step fails and the tag is never moved. Only `manifest unknown` counts as absent; any other error fails.
    - It copies with `skopeo copy --all --preserve-digests`, using the credentials `docker login` stored in `~/.docker/config.json` (unencrypted on the runner, seen in the `v0.1.0-beta.9` run's log), passed as `--authfile`.
    - It checks that the registry's raw index has the validated digest and lists both platform manifests.
  - It doesn't build anything. The image attestation's `subject-digest` is the docker job's validated `image_digest`.

**Tests.** `test-release-policy.sh` goes from 231 to **277**. No Docker, network or registry is used.
- **32 OCI-policy tests** on synthetic two-platform OCI archives (gzip layers, diff_ids, configs, BuildKit-style attestation manifests with an empty config):
  - a valid image, and the exact output;
  - each rejected: an SDK 10 PDB on amd64 and on arm64; mixed compilers; a missing amd64 or arm64 image; an extra runnable platform; an attestation for an unknown image; an architecture mismatch; `DOTNET_VERSION` 10, or different per platform; a net10.0 runtimeconfig; the version, revision or source label; a root user; Ubuntu 24.04; native SQLite of the wrong architecture on either platform; a differing `CampertronLibrary.dll`; a whiteout that removes a file; a missing blob; a corrupted blob; an archive member outside the layout;
  - an upper layer that replaces a file is honored;
  - the loaded-image correlation in both image-store modes;
  - the rehearsal-copy check, both accepting and rejecting;
  - `docker_tag_refs` for pre-release and stable.
- **7 workflow-step tests:** the real steps extracted from `release.yml` and run under `bash -e`, with `docker` and `skopeo` stubs (the skopeo stub keeps a registry in a directory and logs every call):
  - the image check passes and records its outputs;
  - an arm64 SDK 10 PDB fails it with an `::error::` line;
  - a mismatched loaded image fails;
  - a pre-release pushes the archive only to `…:0.1.0-beta.10`, with `--all --preserve-digests`;
  - a file or index digest mismatch fails before any skopeo call;
  - an existing tag with another digest is refused, with no copy;
  - a registry digest that differs after the copy fails.
- **7 job-structure checks:**
  - docker-publish builds nothing;
  - its `needs` and `if` are kept;
  - the attestation uses the validated digest;
  - the docker job has no write permission;
  - one exported multi-platform build is checked;
  - the docker job never contacts a registry;
  - the image is published only after every validation job, and before the GitHub release.
- Against the previous `release.yml`, **267/277**: exactly the 7 workflow-step tests and 3 structure checks (no rebuild, attestation digest, OCI export and check) fail.

| Check [V] (Linux x64, Docker 29.9.3, buildx 0.38.2 / BuildKit v0.34.2, SDK 11.0.100, scratch builder and clones) | Result |
|---|---|
| `bash .github/release-policy/test-release-policy.sh` | **277/277**; previous `release.yml`: 267/277 |
| Real multi-platform OCI build of `68b371e` (`--output type=oci`) | archive members: 27 blobs, `index.json`, `oci-layout`. `check_oci_archive` passes: index `c699c418…`; amd64 `a54d0366…` and arm64 `65412372…`, both compiled by `5.12.0-1.26529.118+297acf93…` on `11.0.0-rtm.26529.118`; `DOTNET_VERSION` 11.0.0; 2 attestation manifests (empty config, `application/vnd.in-toto+json`, SLSA provenance v1); native SQLite x86-64 and AArch64; Ubuntu 26.04 |
| The changed docker-job steps run verbatim on that archive and real Docker | image check passes (10 output keys); loaded-image correlation passes (ID = amd64 manifest digest `a54d0366…`, 9 identical diff_ids); smoke test (database 226,471,936 bytes) and .NET 11 checks pass |
| Rehearsal step locally | not run: skopeo isn't installed here, and running skopeo's container image was not permitted. The release dry run on the runner (Skopeo 1.21.0-dev preinstalled, runner image 20261111.196.1) must prove it |
| `release.yml` parsed with YamlDotNet 16.3.0 | 7 jobs; every `run` block of `docker` and `docker-publish` passes `bash -n`; the test's `workflow_step` extraction is byte-identical to YamlDotNet for all 8 changed or new steps |
| Composed `v0.1.0-beta.10` notes (proposed fragment) | unchanged (`49ad73e3…`) |

**Guarantees.**
- The GHCR tag points to the exact image index the docker job validated: both platforms' manifests, configs, layers and PDBs.
- The version tag appears only after every validation job has passed and the archive has been re-verified.
- Nothing is rebuilt.
- A re-run pushes identical bytes and cannot move an existing version tag.
- Dry runs never contact a registry: the docker job has no login, push or `docker://`, and docker-publish is gated as before.

**Limitations and recovery.**
- **Not covered:**
  - arm64 code is never executed; native ARM64 execution is R8.
  - The base images are still floating per build: a tag run publishes exactly what it validated, but a dry run and a later tag run can differ.
  - Pinning base digests is an optional, separate hardening decision.
  - skopeo's push to GHCR runs for the first time on a tag run. The rehearsal covers its copy engine and raw output; the registry protocol and the `manifest unknown` text are covered only by the tests' stub. **First live use: `v0.1.0-beta.10` (2026-11-14, §Release history).** The absent tag was recognized (GHCR's answer matched `manifest unknown`, the only path to a copy), the authenticated copy succeeded, and the registry index equals the validated digest with both platform manifests.
- **Failure handling:**
  - A failure before `Push the validated image` publishes nothing.
  - An interrupted copy leaves only untagged blobs; re-running docker-publish is idempotent while the 7-day artifact exists.
  - If the tag exists with another digest, the job stops. Only the maintainer decides about a GHCR version.
  - An attestation failure after the push leaves the tag in place; a re-run repeats the identical push and the attestation.
  - The release job's own failure handling is unchanged (§Failure policy).

### X. R8: GUI startup states, layout polish and native smoke tests (decision D33, branch `feat/r8-gui-usability-native-smoke`, PR #47, 2026-11-14)
Based on `master` `2eaa6c1`. Investigation: the R8 Phase 1 report (2026-11-14, read-only). Linux x64 (CachyOS, KDE Plasma 6.8 Wayland), SDK 11.0.100 / runtime 11.0.0.

**What changed** [V]
- **Startup (G01, G02).**
  - The window opens before anything expensive happens. `Load.GetConfig` (folders, `General.yaml`/`Email.yaml`, extraction, repair or refresh) used to run synchronously in the `MainWindow` constructor, so no window existed while it worked. Any exception aborted the GUI: a corrupt `General.yaml` or one with an unknown key gave exit code 134, a core dump and no window, with the message only on stderr.
  - `MainWindowViewModel` now owns the startup. `Start()` runs on `Window.Opened`. `Load.GetConfig` runs in `Task.Run`, and the task never faults (the exception is captured). The outcome is posted to the UI thread and applied only if it belongs to the latest attempt of a window that is still open (attempt counter, `Detach()` on `Closed`).
  - `ReactiveViewModel` is created only after success, with `CampertronInternalConfig.ConfigPath`, so no list or details getter can query before the database is ready. Opening a missing database path would create an empty database file.
  - `StartupStatusView` shows "Preparing campground data…" with an indeterminate `ProgressBar`. On failure it shows: "Campertron couldn't prepare its campground data"; the error message (`MainWindowViewModel.Describe`: the message only, unwrapped, at most 500 characters, plus the line and column of a `YamlException`); the settings folder (both selectable, tab stops, with help text); advice to fix or delete `General.yaml` or `Email.yaml`; and **Try again**, which gets the focus.
  - `Retry()` is accepted only in the Failed state, so attempts never overlap. Repeated failures stay in the error state; a later success shows the tabs.
  - After the window closes, `Program.Main` waits for a still-running `Load.GetConfig`, so an extraction, repair or refresh finishes as before R8, when it ran before any window existed.
  - The settings folder of the error state is the GUI's existing default rule (`ReactiveViewModel.DefaultConfigPath`, formerly the instance property `GetConfigPath`). `Load.GetConfig` returns no path when it fails, and the library has no other accessor; on success the GUI uses `Load.GetConfig`'s own `ConfigPath`.
  - TD-23 code made obsolete is removed: the `config`/path locals and the commented-out console launch in the constructor. The library is unchanged.
- **Layout (G03).**
  - The details `ScrollViewer` spans the whole tab (transparent background, so its empty part is hit-testable), and the capped, left-aligned column is inside it.
  - Before, at 1600 and 2400 DIP the wheel did nothing in the 603/1403 DIP right of the column, and the scroll bar floated beside the column.
  - The filter card, the empty state, the details column and the startup cards each use the D28 capped star column (`ColumnDefinition Width="*" MaxWidth="976"`) with a 16-DIP gutter. An interim attempt with `MaxWidth` + `HorizontalAlignment="Left"` shrank the filter card to its content (443 DIP); the real-window review caught it, and the tests now require every card to fill the capped column.
- **Focus (G04).** The details viewer starts 4 DIP up and left of its content, which is inset by 4 DIP: its focus rectangle no longer touches the title, and the content stays aligned with the filters. The campground ID and the error state's selectable texts have a 4,2 inset; the ID is sized to the number.
- **Identity (G05).** `Application.Name="Campertron"` (was Avalonia's default "Avalonia Application"); the GUI `app.manifest` identity is `CampertronGUI` (was `CampertronConsole`).
- **Type-ahead (G06).** Placeholders "Select or type a park/campground/state/city". The existing ComboBox text search (prefix) is unchanged and now tested; no filtering was added.
- **Smoke mode (T01).**
  - `CampertronGUI --smoke-test` (`SmokeTest.cs`) runs `Program.BuildAvaloniaApp` with `StartWithClassicDesktopLifetime` and attaches to the real `MainWindow` from the lifetime's `Startup` event.
  - The normal startup prepares the data (same data folder and `Load.GetConfig`; nothing extra written; no extra network access).
  - It requires, in order: the window opened with a non-zero native handle (`TryGetPlatformHandle`); the ready state; a rendered frame (two `RequestAnimationFrame` callbacks); park items in the window (`ParkComboBox.ItemCount`) and states (`StateList`); then it calls `MainWindow.Close()`. The close must be programmatic (`WindowClosing`), the lifetime must return 0, and nothing may be unhandled or still preparing.
  - Report: one `SMOKE key=value` line per step on stdout: `startup`, `window-opened`, `native-handle`, `ready`, `config-path`, `rendered-frame`, `parks`, `states`, `window-size`, `render-scaling`, `theme`, since D43 `settings-probe` (`SettingsProbe`: General.yaml and Email.yaml edited and saved in a temporary folder) and `settings-tab` (both files editable in the Settings tab), since D44 `search-probe` (`SearchProbe`: New search, the enabled toggle alone, list and comment edits with a byte-order mark and CRLF, Save As without overwrite and the exact campground ID from the installed data, in a temporary folder) and `searches-tab` (the Searches tab with its file count and the campground count), `close-requested`, `closing`, `shutdown`, `result`. Failures go to stderr.
  - Exit codes: 0 pass, 1 failure, 2 timeout (60 s, on a timer thread, so a blocked UI thread still ends the process).
  - `Main` returns the lifetime's exit code (0, as before). Normal starts and `--version` are unchanged.
- **Native smoke (T02).** See §Release workflow. `native-smoke` runs the same run's archives natively on `windows-2025`, `windows-11-arm`, `macos-26-intel` and `macos-26`. linux-x64 replaces "alive after 30 s under `xvfb-run`" with the smoke test. `docker-publish` and `release` need `native-smoke`, and `CampertronGUI/**` pull requests get the release dry run. The generated notes mark the five natively smoke-tested platforms (`NATIVE_SMOKE_RIDS_DEFAULT`).

**Tests** [V]
- GUI tests 34 → **52**:
  - `StartupTests` (8): loading state with a blocked loader (tabs hidden, no `ReactiveViewModel`, **0 EF commands on any thread**); success with the loaded `ConfigPath`; the accessible error state (selectable message and folder, heading, Tab order, focus on Try again, no query); retry, overlap and repeated failure, then success; closing while preparing (late completion ignored, no unhandled exception or Avalonia log entry); observed loader exceptions (task never faults, no `UnobservedTaskException`); one attempt per `Start`; `Describe`.
  - `SmokeTestTests` (7): argument parsing (and that `--version` is unaffected); the full report in order with Avalonia's own frame signal (render timer forced); startup failure; missing or zero handle; missing frame (timeout, exit code 2, one result line); window closed before the end; unclean lifetime exit.
  - `MainWindowUsabilityTests` (+2 tests, +1 case): `FocusTargets_KeepTheirContentInsideAnInset` and `TypeAhead_SelectsTheFirstItemStartingWithTheTypedText`. `LongContent_*` now covers 720/1024/1600/**2400**: the scroll area reaches the tab edge, the wheel scrolls at its right edge, every card is `min(tab, 976) − 16` wide, the column is aligned with the filters, Fees is in reach.
  - `MainWindowTests`: the application name, and the ready state and `ConfigPath` after construction.
  - Existing tests wait for the ready state (`GuiTestHelpers.ShowMainWindow`), and `QueryCounter` moved to `GuiTestHelpers` (optionally counting all threads).
- Mutation check: a left-aligned filter card fails `LongContent_*` ("filter card does not fill the capped column").
- Release-policy tests 277 → **322**:
  - `check_gui_smoke_report` (23) and `check_gui_smoke_run` (4).
  - The native GUI step run verbatim against a stub GUI (6: pass, failure, stderr, wrong folder, emulated process, existing folder).
  - The linux-x64 GUI step (2).
  - Job structure (5: pinned labels, no SDK/rebuild/write permission, report/timeouts/log upload, linux-x64 smoke mode, path filter).
  - Release notes (3 tamper cases, the rendered table, and the jobs equal `NATIVE_SMOKE_RIDS_DEFAULT`).

**Local validation** [V] (final branch, SDK 11.0.100)

| Check | Result |
|---|---|
| Restore; build Debug and Release (`--no-incremental`) | 0 errors, **76 warnings** each. Compared with `master` built the same way: 71 identical file/line/column/code/message identities; the other 5 are `ReactiveViewModel.cs` CS8604 with identical messages, 12 lines lower (161→173, 168→180 ×2, 183→195, 222→234). None added or removed |
| `XDG_DATA_HOME="$(mktemp -d)" dotnet test Campertron.sln -c Release` | **296/296** (244 + 52); GUI tests also 52/52 three times in Debug and once in Release |
| `test-release-policy.sh` | **322/322** |
| Vulnerability scan, each of the 5 projects | 0 vulnerable packages |
| `check_global_json`, `check_selected_sdk`, `check_package_policy` (5 asset files) | pass |
| `check_curated_notes_dir` | the beta.10 fragment validates. Its composed preview is now `e7ebacf0…` (10,556 bytes), because the generated platform table changed; the published beta.10 body (`49ad73e3…`) is immutable and unchanged |
| `release.yml` | parsed with YamlDotNet 16.3.0; 49 `run:` blocks (45 on `master`) pass `bash -n` |
| Real `~/.local/share/CampertronConfig` and `CampertronCache` | no change since before this work (newest change 11:21:39 local, from a separately launched beta.10 copy, see open question #37) |

**Real-window review** [V]. Done in nested `kwin_wayland --virtual --xwayland` sessions (2600×1700, scale 1), each with its own `dbus-run-session` and a session-bus configuration *without service activation*, so no portal or secret service is started (the Phase 1 nested sessions with activation made `xdg-desktop-portal-kde` and `ksecretd` abort and dump core). No window appeared on the live desktop. A scratch launcher (not in the repository) ran the real `App` with the production `AppBuilder` chain and classic desktop lifetime, drove the window's own controls, injected platform key, text and wheel events through the window's input callback (reflection; Avalonia 12 hides the raw-event constructors from the reference assemblies) and captured it with `import -window`. Without a settings portal the system theme is Light; Dark was requested in the launcher.
- **Startup:** first run with extraction: Preparing card captured, ready after 0.95–1.16 s. A corrupt `General.yaml`, and one with an unknown key (`theme: dark`), show the error state with "Property 'theme' not found on type 'GeneralConfig'. (line 5, column 1)". Try again while still invalid stays in the error state, with focus back on Try again. After the file was corrected, Try again (Enter) → ready with 463 parks. Error-state Tab order: message → folder → Try again → message. No core dump in any R8 run.
- **Light and Dark at 720×540, 1024×768, 1600×1000 and 2400×1500** (both themes, identical metrics):
  - The viewer spans x = 16 … width − 20 (the 20 DIP right of it is the TabControl's own margin).
  - The scroll bar is at x = width − 36.
  - The filter card and the details column are both 664 DIP wide at 720 and 961 at ≥ 1024 (the same layout rounding as before R8).
  - The title is 4 DIP inside the viewer's focus rectangle; the ID's focus target is 105 DIP.
  - A wheel event 26 DIP from the right window edge scrolled 0 → 150 at every size; at 1600 a wheel right of the column (x = 1200) also scrolled.
  - Fees is reachable (y 289/380, 539/608, 771/840, 1271/1340).
  - Long names are trimmed and wrap; "(No city listed)", disabled selectors and empty states show as before.
- **Keyboard:**
  - Header → Tab → park selector. Typing "Zion" selects Zion National Park; Alt+C → campground; "WATCH" → WATCHMAN CAMPGROUND.
  - Tab: details → ID → Copy ID → tab header (TD-39 wrap) → park → campground. Shift+Tab reverses.
  - Enter on Copy ID put `232445` on the clipboard.
  - Focus rings no longer touch the content.
- **Shutdown:** every run ended with a programmatic `WindowClosing` close, lifetime 0, no unhandled exception.
- **`--smoke-test` of a linux-x64 publish** (release flags, `0.0.0-r8local`), in the nested session (X11, handle `XID:0x40000f`):
  - first run: pass in 2.1 s, extracting the DB;
  - second start: pass;
  - corrupt and unknown-key `General.yaml`: exit 1, `startup failed: …` on stderr, no core dump. Before R8 these aborted with 134.

**PR dry runs** [V]
- **Run 43636830549** (commit `24a9d52`):
  - Native smoke passed on win-x64, win-arm64 and linux-x64.
  - **Both macOS jobs failed** in the console step. On macOS, .NET resolves `LocalApplicationData` through the system (`NSApplicationSupportDirectory`), not through `$HOME`. The console therefore ignored the `General.yaml` placed under an overridden `HOME`, created its sample configuration in the real `~/Library/Application Support`, opened its menu without a terminal and aborted (134).
  - Fix `7c9d94e`: the macOS jobs use the runner user's real folder, as Windows does, require it to be absent first and remove what the console step created. This also confirms README's macOS data folder.
- **Run 43637549330** (commit `7c9d94e`): **all jobs succeeded**, `docker-publish` and `release` skipped. Native smoke:

| Job | Runner (image) | Process | GUI report |
|---|---|---|---|
| linux-x64 (132578402669) | `ubuntu-latest`, `xvfb-run` | X64 | `XID:0x20000f`, 1024x768, Light, `/home/runner/work/_temp/gui-data/CampertronConfig` |
| win-x64 (132578402796) | `windows-2025` = win25-vs2026 20261103.298.1, `RUNNER_ARCH` X64 | X64 | `HWND:0x2016a`, 1024x749, Light, `C:\Users\runneradmin\AppData\Local\CampertronConfig` |
| win-arm64 (132578402759) | `windows-11-arm` = win11-vs2026-arm64 20261112.217.1, ARM64 | Arm64 | `HWND:0x40270`, 1024x749, Light, same folder |
| osx-x64 (132578402697) | `macos-26-intel` = macos26 20260824.0517.1, X64 | X64 | `NSWindow:0x7f8c51b13250`, 1024x768, Light, `/Users/runner/Library/Application Support/CampertronConfig` |
| osx-arm64 (132578402753) | `macos-26` = macos26 20261102.0473.1, ARM64 | Arm64 | `NSWindow:0x73ef20280`, 1024x646, Light, same folder |

  Every job:
  - console `--version` on the bundled `.NET 11.0.0` in a native process;
  - console clean install extracted 226,471,936 bytes;
  - GUI `--version`;
  - GUI smoke test exit 0, empty stderr, 463 parks, 51 states, programmatic close, `shutdown=clean exit-code=0`, `result=pass`, and the GUI extracted 226,471,936 bytes itself.

  This is the first native Windows and macOS run of the .NET 11 GUI and console (open question #34), including Avalonia's .NET 11 macOS shutdown path. The 749 and 646 client heights are the runners' screens limiting the 768-DIP window.
- **Run 43638175021** (commit `a2eed72`, the generated-notes change): 24 jobs succeeded, `docker-publish` and `release` skipped; all five native smoke jobs passed again, and `assemble` validated the template notes with the new platform table. CI 43638174983 succeeded.

**Not run:** a physical-keyboard pass and a screen reader; rendering, fonts, theme detection and the macOS application menu on Windows/macOS (open question #29; the native smoke tests take no screenshots by design); linux-arm64, win-x86, linux-arm and the arm64 container (recorded R8 scope items, deferred to the consolidated audit, decisions.md R8).

### Y. Console output polish (decision D34, branch `feat/console-output-polish`, PR #49, merged as `7e325cd`, 2026-11-14)
Console presentation only: no change to search, filters, retry timing, `Camp.History`, the HTML page, emails, dependencies, workflows, release policy or the GUI. Linux x64 (CachyOS), SDK 11.0.100 / runtime 11.0.0. What changed: decisions D34, business-rules §12, architecture.md §Console output.

**Investigation before the change** [V] (`master` `d3d1313`, isolated runs): the reset sequence `\f ESC c ESC[3J` was written into redirected output and `docker logs` on every Email check and erased the terminal's scrollback; the menu crashed with an `ArgumentOutOfRangeException` (exit 134, core dump) whenever standard input or output was not a terminal; settings errors were unhandled exceptions without the file name (134, in the container 139); `CampsiteConfig.WriteToConsole` never reset its color, so later lines inherited it and Ctrl+C left the terminal magenta with a hidden cursor; a failed request retried silently every 30 s; a consecutive-night search without results printed only its header; the calendar marked open days by color alone; View Config's cursor arithmetic broke when a path wrapped; a startup repair notice was erased by the menu before it could be read.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release --no-incremental` (and Debug) | 0 errors, **74 warnings** |
| Warning identities vs `master` `d3d1313` (file, code, message; both built `--no-incremental`) | none added; two removed in `Menu.cs` (one CS8602, one CS8629, with the old View Config code); no warning in the new files |
| `XDG_DATA_HOME="$(mktemp -d)" dotnet test Campertron.sln -c Release --no-build` | **385/385** (333 `CampertronUnitTests` + 52 `CampertronGUITests`); 89 more unit-test cases than before (`ConsoleUiTests` 41, `ConsoleResultsTests` 22, `ConsoleFailureTests` 21, `ConsoleOutputTests` 5) |
| `dotnet list <project> package --vulnerable --include-transitive`, all five projects | no vulnerable packages |
| `bash .github/release-policy/test-release-policy.sh` | 322/322 (unchanged; no workflow or policy file changed) |
| Each of the three code commits, exported alone | builds; its tests pass (the two intermediate ones without `LoadTests`, to avoid extra live requests) |

**The HTML page is byte-identical** [V]. (1) A scratch harness (not in the repository) builds fixed output streams with the production producers (three searches with single nights over three months including an empty one, two consecutive-night sites, a search with markup characters, missing values and an untyped note, an empty search and all-empty months; the whole run, each search alone as its email page, and no searches; en-US and de-DE) and writes `ResultsHtml.Render` as UTF-8. Compiled once against the library of `d3d1313` and once against this branch: all ten files are byte-identical. SHA-256, before = after:
  - `en-US-run-all.html` `eb0b2ee9d6e1b8799ff7539d1527f908a16ac5703d44a280de2211d214d5f683`
  - `en-US-email-zion.html` `ac816d585f13fb7e2bb1b7bced300a6f0237a4ff01ba6cd9bb8fd33bb7d9f657`
  - `en-US-email-mather.html` `e344e3ba1922ecfc380f3b32297e9f1bdfe35e4a28717c665e40f3a583aaf38c`
  - `en-US-email-odd.html` `727f02d9c0773ffb20360fb4a0a534a45a1be63f0e8c4a85e79e6554589fe900`
  - `en-US-no-searches.html` and `de-DE-no-searches.html` `5fca9ff62176f54e66348c02435a0277e7df948fe5426ad94d9811a0a8a34c4a`
  - `de-DE-run-all.html` `7b51aee71b621c63461c7e6077e1dad3da3db70c06ae2d3bb8da319c19d50064`
  - `de-DE-email-zion.html` `1081a070c9b959df31943c46d45b3b57767a2f6cdfe3435f249a74475b67e566`
  - `de-DE-email-mather.html` `3605af2ad8454238c9922a77b11453d92f9f4faa600b84e343ae0dc13772adf3`
  - `de-DE-email-odd.html` `7bb864a7bd0643f21103c8a095a4c16d264efaef2c22a16ac3c7348f8d77480a`

(2) End to end with the published consoles of `d3d1313` and of this branch, the two shipped examples and the same stand-in data on the same day: the `HtmlFile` page (8,626 bytes, `0d9cdc4b8e2f5a2d934fda63f93544ed9fc826a4eae935abb5811c2915039efe`) and the first Email page (4,796 bytes, `7eef53d5f02344ed8b4f0b9809e35686a95a46829a64b5d95fade449ffda2e14`) are byte-identical, and both Email runs wrote the same 8 `Camp.History` markers. `ResultsHtmlTests` (25) and `HtmlResultsOutputTests` pass unchanged. Console-mode streams now carry `Calendar`-tagged calendar values, which the page builder ignores; no HtmlFile or Email stream contains a calendar.

**Isolation of the runs** [V]: the console published from the branch (and, for comparisons, from an export of `d3d1313`), `HOME` and `XDG_DATA_HOME` in a scratch directory under `env -i`, a stand-in `xdg-open` first on `PATH`, SMTP on a closed loopback port, and every recreation.gov request answered by a local HTTPS stand-in (`HTTPS_PROXY`, throwaway CA in `SSL_CERT_FILE`, `SSL_CERT_DIR=/nonexistent`) with real campsite IDs of the bundled database and invented availability. No request reached recreation.gov, no email was sent, no browser opened, and the real `CampertronConfig`/`CampertronCache` were not touched. The existing `LoadTests.RunConsoleSearchTest` made its known live request in each full test run (TD-01). The menu was driven on a pseudo-terminal by a scratch Python driver that answers cursor queries and records the raw byte stream.

**Terminal, redirection, signals** [V] (final code)
| Scenario | Result |
|---|---|
| `--version`, old vs new build with the same version property | stdout byte-identical (69 bytes), stderr empty, exit 0, no file created; `check_framework_report` and the workflow's first-line pattern pass on the new output |
| `outputTo: UnitTest` clean start, redirected and on a pseudo-terminal | 0 bytes on both streams and on the terminal, exit 0, database extracted (226,471,936 bytes), no partial file |
| Menu → Search → View Config → Exit at 80×24; searches at 40, 100 and 160 columns | exit 0; no `ESC c`, no `ESC[3J`, no form feed in any stream; the cursor is shown again at the end; View Config is intact with wrapping paths; only lists and name lines wrap, links stay whole |
| No availability; long names; sites without loop, equipment or times; an unknown campground ID | "No matching availability." once per search; names in full; no empty labels; a warning for the unknown ID |
| `HtmlFile`, two examples | the page's path and the summary on the console; one page, one open request |
| Console mode with stdout to a file, stdout piped, stdin from `/dev/null`, everything redirected | "Error: the Campertron menu needs an interactive terminal. …" on standard error, exit 1, 0 bytes on stdout, no escape sequence in a file |
| Invalid `General.yaml` (bad value, malformed, unknown key), `Email.yaml`, search file | exit 1; "Error: {file} could not be read: … (line N, column M)", the path and the advice on standard error; nothing on stdout; an `Email.yaml` error quotes no value |
| Empty `General.yaml` | exit 1, "unexpected problem: NullReferenceException" with details (the cause is TD-37, unchanged) |
| Search file without `campgroundID`, without `totalHumans`, `consecutiveDays: 0`, a bad date, `MonthsOut 14`, no `autoRun: true` | the warning or message of business-rules §12, the search runs as before, exit 0 |
| Email, new results, SMTP refused | start line, searches, "Check 1: 2 searches, 8 new openings; sending 2 emails.", then "Error: the email could not be sent: …" on standard error, exit 1; 8 markers written first (TD-14, unchanged); no address, server or password in the output |
| Email, nothing new, two checks, redirected | one timestamped line per check; 0 control bytes; stderr empty |
| Signals to the redirected Email loop | SIGTERM 143, SIGHUP 129, SIGINT 130 with "Stopped." on standard error |
| Ctrl+C at the menu and during a search with a dead network | exit 130; the cursor is shown again and the color reset before "Stopped." |
| Dead network; heavy traffic; blocked; HTTP 502 with an HTML body | a warning per attempt with the campground, month, reason and "retrying in 30 seconds"; a warning per heavy-traffic retry; "Error: Blocked by API …" with the 5-minute note; the parse failure reported as a failed attempt |
| `NO_COLOR=1`; `TERM` dumb, empty, vt100; default | no color with `NO_COLOR` (cursor control still used); no control sequence at all on a dumb terminal or without `TERM`; colors used otherwise: green, cyan, dark green (logo), red (menu arrow) |
| Menu → Refresh without a route to the download | "Error: a download failed: …", exit 1, cursor restored |
| Startup repair of a database with a pre-0.1.0-beta.4 table | the repair notices stay on the screen with "Press any key to continue..." before the menu is drawn |
| GUI `--smoke-test` (Release build, nested `kwin_wayland --virtual` session, clean data folder) | exit 0, only `SMOKE` lines on stdout, stderr empty, `check_gui_smoke_run` passes: the library still writes nothing on a first start |

**Container** [V] (`docker build -f CampertronConsole/Dockerfile` from the branch, Docker 29.9.3, amd64; the image was removed afterwards): `--version` two lines, exit 0; without `/config`: "Error: Config path does not exist" and the mount advice on standard error, exit **1** (before 139), and the release workflow's check (non-zero exit and that text) passes; `outputTo: Console`: "Error: Unsupported output type in container. …" on standard error, exit 1; the `outputTo: UnitTest` clean install: 0 bytes of output, exit 0, database extracted; the Email loop for two checks: `docker logs` holds the start line, the searches and one line per check, 0 control bytes, standard error empty; `docker stop` took 0.2 s with exit code 143.

**Not run** [V]: Windows and macOS (open question #38): no workflow was dispatched or rerun for this branch, and a PR that changes only `CampertronLibrary`, `CampertronConsole/Program.cs`, tests and docs does not trigger the release dry run (its path filter covers project files, the Dockerfile, `CampertronGUI/**` and release inputs). A real RIDB refresh (570 MB download) and the full 5-minute wait after a block were not run. arm64 was not run. **Added after the merge (consolidated audit, 2026-11-14):** before PR #49 was merged, the maintainer reviewed the console output manually on Windows and reported that it looked good (PR #49 comment). That is a maintainer-reported manual visual review, not an automated or native-smoke run; macOS stays not run (open question #38).

**Regression checks for later changes**: the five console test classes; for anything touching the terminal, a pseudo-terminal run that checks the raw stream for `ESC c`, `ESC[3J` and a shown cursor at the end, the three redirections, the three signals and `docker logs`; for anything touching `ResultsHtml` or the producers, the byte comparison of the page.

### Z. Search correctness (P2 of the deferred-work cleanup, decision D35, branch `fix/search-correctness`, 2026-11-14)
Search only: TD-11, TD-46 and TD-04 (technical-debt.md). No change to filters, today's exclusion (TD-07), the time zone, retries, the Email gate, `Camp.History` or its markers, output text, the HTML page, emails, configuration bootstrap, exit codes, the GUI, workflows, dependencies or release policy. Linux x64 (CachyOS), SDK 11.0.100 / runtime 11.0.0. What changed: D35, business-rules §2–§4, architecture.md §Console data flow.

**Root causes confirmed before the change** [V] (`master` `e0667be`, whose code equals the audited `7e325cd`): `CampertronConfig.ConvertToZeroHour` and `CheckDt` in `GetAvailabilitiesByCampground` parsed `"M/d/yyyy 0:00:00 AM"` text with the current culture, and the month URL used `ToString("MM")`, which follows the culture's calendar (a probe gave `08` for November in fa-IR); `Load.RunConsoleSearch` calls `Preload`, the only path to `GenerateSearchData`, once before `while (true)`; `FilterData` added every hit to `RunData.FilteredAvailableData`, and `FilterConsecutiveDays` grouped, filtered and reassigned it.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Debug` and `-c Release`, both `--no-incremental` | 0 errors, **74 warnings** each |
| Warning identities vs `master` `e0667be` built the same way (file, code, message) | none added, none removed; Debug and Release equal. Only line numbers moved: all 27 warnings of `AvailabilityApi.cs` and 10 of the 16 in `Load.cs` |
| `XDG_DATA_HOME="<new temp dir>" dotnet test Campertron.sln -c Release --no-build` | **429/429** (377 `CampertronUnitTests` + 52 `CampertronGUITests`); 44 new unit-test cases (`SearchDatesTests` 34, `SearchIsolationTests` 10). `LoadTests.RunConsoleSearchTest` made its known live request (TD-01); no new test makes one. The isolated `Camp.History` held no marker (only the empty root directory, TD-43) |
| Targeted suites (`SearchDatesTests`, `SearchIsolationTests`, `SharedMonthSearchTests`, `NotificationHistoryTests`, `AvailableDatesTests`, `ConsoleOutputGoldenTests`, `ConsoleResultsTests`, `ResultsHtmlTests`, `HtmlResultsOutputTests`) | 132/132 |
| `bash .github/release-policy/test-release-policy.sh` | 322/322 (no release-policy or workflow file changed) |
| `check_curated_notes_dir .github/release-notes` (under bash, as CI) | valid; the `v0.1.0-beta.10` preview is unchanged (`e7ebacf0…`, 10,556 bytes) |
| `check_package_policy` | passed (6 asset files locally: the 5 projects and a local `ReleaseCheck` restore) |
| `dotnet list <project> package --vulnerable --include-transitive`, all five projects | no vulnerable packages |

**Negative controls** [V]. Temporary edits of the branch, restored byte for byte (`cmp`) before each commit; the dedicated tests run each time:
| Reverted or bypassed | Result |
|---|---|
| TD-11: `ConvertToZeroHour` parses `"M/d/yyyy 0:00:00 AM"` again | 14 of 31 fail: `SearchDates_AreTheSameAsInEnUs` and `Search_FindsTheSameAvailabilityAsInEnUs` in en-GB, en-AU, de-DE, fr-FR, es-MX, th-TH and fa-IR |
| TD-11: `AvailabilityApi.MonthStart` parses `"M/1/yyyy …"` again | 14 of 31 fail; searches that then asked for a month the test does not hold were stopped by the test's 60-second guard behind a dead proxy (no network) |
| TD-11: `MonthUrl` formats with the current culture again | 2 of 31 fail (fa-IR: `RequestedMonth_…` and `Search_…`) |
| TD-46: `RegenerateSearchData` does nothing (the former frozen dates) | 2 of 34 fail (`Regeneration_RollsRelativeWindowsWithTheTime_AndKeepsWrittenDates`, `EmailLoopRegeneration_UsesTheLoadedSearches_WithoutReadingTheFiles`) |
| TD-46: regeneration writes its warnings | 1 fails (`Regeneration_RepeatsNoWarning`) |
| TD-46: `Load.RegenerateSearchDates` does nothing | 1 fails (`EmailLoopRegeneration_…`) |
| TD-46: the loop's call | not reachable by a unit test (the Email loop never returns); shown by the container control below |
| TD-04: the former filtering (the TD-46 commit's `AvailabilityApi.cs`) | 5 of 10 fail in each of 3 runs: the missed-notification, false-two-night and other-campground cases in their failing order, and both all-searches comparisons |

**Parallel versus isolated** [V]. A scratch harness (not in the repository) built once against the library of `master` `e0667be` and once against this branch; months seeded in memory (30 % available, as §T), URLs claimed, a dead proxy. Each search alone (its own `RunningData`) is the reference; a run is all searches with one `RunningData` in file order, in reverse order and 100 times in parallel. Compared: every value of each search's stream (kind, write type, color, text, campsite, date), the entry counts and the run's set against the union of the searches' own sets.
| Scenario (entries alone) | `master` | this branch |
|---|---|---|
| broad, Watchman: single nights (1,770), weekends only (587), a ten-site subset (88), two nights (923), three nights (375) | file order: the run's set differs from the union, two and three nights differ (three nights with more entries); reverse order: two nights differs (more entries); parallel: the set differs in 100/100, two nights in 100/100 (more entries in 99, fewer in 1), three nights in 100/100 (more entries); single-night searches never differ | **0 differences** in every mode: every stream byte-equal to the search alone, the run's set equal to the union |
| the shipped examples' filters: Zion single nights (1,635), Mather two nights on Fridays and Saturdays (298) | file order: Mather's output and the run's set differ; parallel: both differ in 100/100 | **0 differences** in every mode |

**A search alone, before and after** [V]: 7 searches (the two scenarios) × the value stream with kinds and metadata, the `ResultsHtml` page and the `ConsoleResults` rendering at 100 columns: all 21 files byte-identical between `master` and this branch, consecutive searches included (the per-search set keeps the former `ConcurrentBag` order).

**Console in other cultures** [V]. Release builds of `master` and this branch, run with .NET 11.0.0 on a pseudo-terminal (a scratch driver answering cursor queries: Search, a key, Exit), `env -i` with `HOME` and `XDG_DATA_HOME` in a scratch directory, two Watchman searches (single nights and two nights, `DaysOut 90`), and a local HTTPS stand-in for recreation.gov (`HTTPS_PROXY`, a throwaway CA in `SSL_CERT_FILE`, `SSL_CERT_DIR=/nonexistent`) with real campsite IDs of the bundled database and invented availability in January 2027:
| `LANG` | `master` | this branch |
|---|---|---|
| `en_US.UTF-8` | 7 + 5 openings, "Done: 2 searches, 12 openings at 4 sites." | the same; the results section byte-identical to `master`'s |
| `en_GB.UTF-8` | "Error: TwoNights.yaml could not be used: String '11/14/2026 0:00:00 AM' was not recognized as a valid DateTime.", exit 1 | 7 + 5 openings, the same summary, dates as `03/01/2027` |
| `de_DE.UTF-8` | the same error, exit 1 | 7 + 5 openings, the same summary, dates as `So 03.01.2027` |

The stand-in received only requests for November 2026 to February 2027 (4 per completed run): no month was misread.

**Container: the Email loop across a local midnight** [V]. Local images built from exports of `master` and of this branch (`docker build`, Docker 29.9.3, amd64; never pushed, removed afterwards), `--network host` to reach two local stand-ins (the HTTPS one above, and an SMTP one with STARTTLS that stores messages and relays nothing), and a time zone compiled with `zic` for the test (a fixed offset, mounted at `/usr/share/zoneinfo/Test/…`, `TZ=Test/…`), so that local midnight came a few minutes after the start. Searches: Watchman `DaysOut 1` (before midnight 14–15 November, after it 15–16 November), Mather `MonthsOut 14` (the 12-month warning) and Mather `SpecificDates` with an invalid entry (the invalid-date warning); the only availability is Watchman campsite 10146649 on 16 November, which only the rolled window contains. The invalid-date search file was deleted after the first check.
- **Control run, both images side by side** (midnight 01:05 UTC): checks 1–9 "nothing new" on both. Check 10, the first after midnight: this branch "1 new opening; sending 3 emails"; `master` "nothing new" through check 15. (This branch then stopped with "Error: the email could not be sent: Authentication failed.", exit 1: the SMTP stand-in mishandled `AUTH LOGIN` with an initial response; the marker had been written first, TD-14, unchanged and planned for P3.)
- **Second run, this branch, with the stand-in fixed** (midnight 01:17 UTC): checks 1–6 "nothing new"; check 7, the first after midnight, "1 new opening; sending 3 emails" and three "Email sent:" lines (one per search, including the deleted file's search, which is still loaded); the Watchman email holds 16 November and campsite 10146649; marker `Camp.History/2026/11/16/10146649` written; checks 8–9 "nothing new" (the marker), the container still running; `docker stop` exit 143.
- **Both runs**: each warning once, at the start (2 per container, never repeated in 9 to 15 checks); no "No search files found" or "creating the example search" line and no file recreated after the deletion; the log wording unchanged; `docker logs` with 0 control bytes.

**Not run** [V]: Windows and macOS (the changes are platform-neutral; the separately approved release dry run before the `v0.1.0-preview.1` tag runs the native smoke jobs, decisions.md §Roadmap); arm64; a whole day of Email-loop uptime (the midnight in a test time zone stands in for it); no request reached recreation.gov except the existing TD-01 test request; no email left the machine.

**Regression checks for later changes**: `SearchDatesTests`, `SearchIsolationTests`, `SharedMonthSearchTests`, `AvailableDatesTests`, `NotificationHistoryTests`; for the search, the parallel-versus-isolated comparison above (any difference is a regression); for dates, a run under `LANG=en_GB.UTF-8`; for the Email loop, a run across a local midnight.

### AA. Email notification history after delivery (P3 of the deferred-work cleanup, decision D36, branch `fix/email-history-after-delivery`, 2026-11-14)
Email notification history only: TD-14 and TD-43 (technical-debt.md). No change to the marker format or path, the search, filters, the Email gate's condition, the email content, the console wording, exit codes, retries, the SMTP client, settings, the GUI, workflows, dependencies or release policy. Linux x64 (CachyOS), SDK 11.0.100 / runtime 11.0.0. What changed: D36, business-rules §5–§6, architecture.md §Console data flow.

**Root causes confirmed before the change** [V] (`master` `fcff731`): `Load.PostProcess` called `HasNewEntries` first. It created `ConfigPath/Camp.History` for every output type (TD-43) and, for Email, every marker of the check's hits, before `OutputResults` → `CampsiteConfig.ProcessConsoleConfig` → `SmtpClient.Send`. A refused or partly failed check therefore left every marker written (TD-14).

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Debug` and `-c Release`, both `--no-incremental` | 0 errors, **74 warnings** each |
| Warning identities vs `master` `fcff731` built the same way (file, code, message) | none added, none removed; Debug and Release equal. Only line numbers moved: 3 warnings of `Console.cs`, 7 of `Load.cs`, 3 of `LoadUnitTests.cs` |
| `XDG_DATA_HOME="<new temp dir>" dotnet test Campertron.sln -c Release --no-build` | **442/442** (390 `CampertronUnitTests` + 52 `CampertronGUITests`); 13 new unit-test cases (`EmailDeliveryHistoryTests`). `LoadTests.RunConsoleSearchTest` made its known live request (TD-01); no new test makes one, and no test sends mail. The isolated settings folder had no `Camp.History` afterwards (before D36 it held the empty root directory, §Z) |
| `bash .github/release-policy/test-release-policy.sh` | 322/322 (no release-policy or workflow file changed) |
| `check_curated_notes_dir .github/release-notes` (under bash, as CI) | valid; the `v0.1.0-beta.10` preview is unchanged (`e7ebacf0…`, 10,556 bytes) |
| `check_package_policy` | passed (6 asset files locally) |
| `dotnet list <project> package --vulnerable --include-transitive`, all five projects | no vulnerable packages |

**Negative controls** [V]. Temporary edits of the branch, restored byte for byte (`cmp`) before continuing; the dedicated tests run each time (the TD-14 controls at the TD-14 commit, when `EmailDeliveryHistoryTests` and `NotificationHistoryTests` had 24 cases):
| Reverted or bypassed | Result |
|---|---|
| TD-14: the Email gate calls `HasNewEntries` again (which writes every marker before the sends) | 4 of 24 fail: `FirstEmailFails_RecordsNothing_AndTheFailurePropagates`, `LaterEmailFails_KeepsTheMarkersOfTheEmailsSentBeforeIt`, `NextCheck_ReportsWhatWasNotSent_AndRecordsItOnceSent`, `WhileAnEmailIsSent_ItsOpeningsAreNotRecordedYet` |
| TD-14: each email's openings are recorded before its send | the same 4 of 24 fail |
| TD-43: the read-only check creates `Camp.History` again (the former eager creation) | 11 of 35 fail (with the `LoadTests` `HasNewEntries` cases, in a fresh `XDG_DATA_HOME`): `EmailCheckWithNothingNew_CreatesNoHistoryFolder`, `FirstEmailFails_…`, `FirstSentEmail_CreatesTheHistoryFolder`, `ManualAndTestRuns_CreateNoHistoryFolder` (Console, HtmlFile, UnitTest), `HasNewEntries_NonEmpty_WritesMarkersOnlyForEmail` (Console, HtmlFile, UnitTest), `HasNewEntries_WithoutGeneralConfig_WritesNoMarker`, `HasNewEntriesTest` |

**The Email loop with the real `SmtpClient`** [V]. A scratch harness (not in the repository) built once against the library of `master` `fcff731` and once against this branch. It runs one check of three searches (A: Watchman single nights; B: Mather two nights; C: Mather `consecutiveDays: 0`, whose hits no email shows) over months held in memory, through the real `Load.PostProcess` (on a thread; the check is complete at its "Next check" line, before the loop's 60-second sleep) with the real send: a local SMTP stand-in with STARTTLS and a throwaway CA (it stores messages and relays nothing; it can reject the Nth message with 550), or a closed port. Settings and cache in a scratch directory.
| Scenario | `master` | this branch |
|---|---|---|
| all three emails accepted | completed; `Camp.History` with 13 entries (6 markers, no file) | completed; the same 13 entries; the three emails identical to `master`'s (subject, sender, recipients, body; each attachment equals its body); the console identical apart from the time ("Check 1: 3 searches, 8 new openings; sending 3 emails.", three "Email sent:" lines) |
| SMTP refused (closed port) | `SmtpException`; 13 entries: every marker of the check | `SmtpException`; no `Camp.History` |
| second email rejected (550) | `SmtpException`; 13 entries | `SmtpException`; 8 entries: only the 3 markers of A's delivered email; B's openings and C's hits unrecorded |

No password appeared in any output.

**Menu Search on a pseudo-terminal** [V]. Release builds of `master` and this branch (the pseudo-terminal driver and the HTTPS stand-in of §Z, `env -i` with a scratch `HOME` and `XDG_DATA_HOME`, two Watchman searches, a recorder for the browser launch): `outputTo: Console` and `HtmlFile`, Search, a key, Exit, exit 0. `master` leaves an empty `Camp.History` in both modes; this branch leaves none. The console results (73 lines) are identical and the HtmlFile pages byte-identical between the builds; one page opened per HtmlFile run; 4 requests per run (November 2026 to February 2027).

**Container** [V]. Local images built from exports of `master` and this branch (`docker build`, amd64; never pushed; images, containers and container-owned scratch files removed afterwards), `--network host`, the HTTPS stand-in (`HTTPS_PROXY`, the throwaway CA in `SSL_CERT_FILE`, `SSL_CERT_DIR=/nonexistent`), one Watchman search (`DaysOut 90`, 7 openings in January 2027), `/config` and `/cache` bind-mounted from a scratch directory.
- **SMTP refused on the first send** (a closed port): both builds "Check 1: 1 search, 7 new openings; sending 1 email." then "Error: the email could not be sent: Failure sending mail. (Connection refused …)" and the hint line, exit 1. `master`: `/config/Camp.History` with 7 markers; this branch: no `/config/Camp.History`.
- **Successful send** (the SMTP stand-in): both builds "Email sent: Watchman single nights (7 openings)", check 2 "nothing new", still running until `docker stop` (exit 143). Marker trees identical (16 entries, 7 markers, no file); the emails identical (the attachment equals the body). Ordering: on this branch `Camp.History` was created 1.5 ms after the stand-in stored the message; on `master` 98.5 ms before it.
- **Non-Email output**: `outputTo: Console`, "Error: Unsupported output type in container. …", exit 1; `outputTo: UnitTest`, no output, exit 0; no `Camp.History` on either build (in a container these modes stop before searching; the TD-43 difference shows on the desktop, above).
- **All runs**: the log wording identical between the builds (only the times differ); 0 control bytes in `docker logs`; the SMTP password (a dummy) never printed.

**Not run** [V]: Windows and macOS (the change is platform-neutral; the separately approved release dry run before the `v0.1.0-preview.1` tag runs the native smoke jobs, decisions.md §Roadmap); arm64; a real mail provider (no email left the machine); no request reached recreation.gov except the existing TD-01 test request.

**Regression checks for later changes**: `EmailDeliveryHistoryTests`, `NotificationHistoryTests`, `LoadTests`; for the Email loop, the harness's refused and partial scenarios above (a marker written before its email was delivered, or a `Camp.History` directory without a marker, is a regression).

### AB. Settings safety and failure exits (P4 of the deferred-work cleanup, decision D37, branch `fix/settings-safety-and-exits`, 2026-11-14)
TD-13, the empty-file part of TD-37, TD-45, TD-44, TD-47 and TD-48 (technical-debt.md). No change to search results or filters, P2's dates and isolation, P3's delivery-ordered history, the output and email content, retries, the download location, the container paths, the YAML writers, `Email.yaml` validation, the GUI, workflows, dependencies or release policy. Linux x64 (CachyOS), SDK 11.0.100 / runtime 11.0.0. What changed: D37, business-rules §2, §7–§9, §12, architecture.md §Configuration and §Persistence.

**Root causes confirmed before the change** [V] (`master` `4419ce8`): `Yaml.CampertronConfigGetConfigs` called `CampertronConfigGenerateSampleConfig` whenever no file had `autoRun: true`, and that writes `ZionConfig.yaml` with a truncating `StreamWriter` (TD-13); `GeneralConfigGetConfig` and `CampertronConfigGetConfigs` passed a null document on (an empty file) to code that dereferences it (TD-37); `CampertronConfig.GenerateSearchDates` wrote the equal-date error, called `Console.ReadKey()` and `Environment.Exit(0)` inside the loader's file-context `try` (TD-45); `AvailabilityApi.GetWebApiData` called `Thread.Sleep(300000)` and `Environment.Exit(0)` after a blocked answer (TD-44); `Load.GetConfig` joined `Environment.GetFolderPath(LocalApplicationData)` (no `SpecialFolderOption`, so `""` for a missing folder) with the folder names (TD-47); `Compression.Unzip` deleted `LocalApplicationData/Extract` recursively and extracted into it, and `PopulateDbFromRIDBFiles.Populate` deleted it again (TD-48).

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Debug` and `-c Release`, both `--no-incremental` | 0 errors, **74 warnings** each |
| Warning identities vs `master` `4419ce8` built the same way (file, code, message) | none added, none removed (48 identities); Debug and Release equal. Only line numbers moved: 27 warnings of `AvailabilityApi.cs`, 6 of `Load.cs` |
| Each of the six implementation commits on its own | builds, 0 errors, 74 warnings |
| `XDG_DATA_HOME="<new temp dir>" HOME="<new temp dir>" dotnet test Campertron.sln -c Release --no-build` | **480/480** (428 `CampertronUnitTests` + 52 `CampertronGUITests`); 38 new unit-test cases (`SettingsBootstrapTests` 10, `DataFolderTests` 12, `ConsoleFailureTests` 15, `AtomicRefreshTests` 1); `CachePathTests.GetConfig_Desktop_…` now computes its expected path with `SpecialFolderOption.DoNotVerify` (it used the lookup that gives `""` for a missing folder, so it expected the working directory). `LoadTests.RunConsoleSearchTest` made its known live request (TD-01); no new test makes one (the TD-44 tests use a loopback HTTP stand-in). The real `~/.local/share/CampertronConfig` and `CampertronCache` were compared before and after (398 entries): unchanged |
| The same with `XDG_DATA_HOME` pointing at a **missing** folder | 480/480; the folder is created and holds the data; nothing is written into the test output folder (Test warnings §1 no longer applies; before, the data went into `CampertronUnitTests/bin/…`) |
| Targeted suites (`SettingsBootstrapTests`, `DataFolderTests`, `ConsoleFailureTests`, `AtomicRefreshTests`, `EmailDeliveryHistoryTests`, `NotificationHistoryTests`, `LoadTests`, `SearchIsolationTests`, `SearchDatesTests`, `SharedMonthSearchTests`, `AvailableDatesTests`, `ConsoleOutputGoldenTests`, `ConsoleResultsTests`, `ResultsHtmlTests`, `HtmlResultsOutputTests`, `CachePathTests`) | 290/290 |
| `bash .github/release-policy/test-release-policy.sh` | 322/322 (no release-policy or workflow file changed) |
| `check_curated_notes_dir .github/release-notes` (under bash, as CI) | valid; the `v0.1.0-beta.10` preview is unchanged (`e7ebacf0…`, 10,556 bytes) |
| `check_package_policy` | passed (5 asset files) |
| `dotnet list <project> package --vulnerable --include-transitive`, all five projects | no vulnerable packages |

**Negative controls** [V]. Temporary edits of the branch, restored byte for byte (`cmp`) before each commit; the dedicated tests run each time (stdin from `/dev/null`, so a key read throws instead of waiting):
| Reverted or bypassed | Result |
|---|---|
| TD-13: `master`'s `Yaml.cs` | 6 of 10 `SettingsBootstrapTests` fail: the disabled `ZionConfig.yaml`, several disabled files, and every output type with no enabled search (4) |
| TD-37: the null reaches the old dereference (`?? throw` removed at both sites) | 8 of 40 fail: the empty and comment-only `General.yaml` (3), its exit code, the empty and comment-only search file with and without another search (4) |
| TD-45: `master`'s `Config.cs` (error, key wait, exit 0) | both `EqualStartAndEndDate_…` cases fail (an `InvalidOperationException` from the key read instead of one error naming the file) |
| TD-44: `ExitProcess(0)` | `BlockedRequest_KeepsItsMessageAndWait_ThenExitsWithCode1` fails |
| TD-47: the former lookup in `GetConfig` (no `Create`, no full-path check) | 3 of 7 fail: the fresh home and both "without a full path" cases |
| TD-48: the generic `Extract` name | 5 of 63 fail: four `Unzip_…` cases and `Refresh_ExtractsIntoItsOwnFolder_…` |

**The console, before and after** [V]. Release builds of `master` and this branch, `env -i` with a scratch `HOME` and `XDG_DATA_HOME` (a hard link to one extracted database, a copy for the refresh), a pseudo-terminal driver answering cursor queries (scratch, not in the repository) or redirected input, and a local HTTPS stand-in for www.recreation.gov and ridb.recreation.gov (`HTTPS_PROXY`, a throwaway CA in `SSL_CERT_FILE`, `SSL_CERT_DIR=/nonexistent`) answering availability from a data file, with recreation.gov's block page, or with a fixture RIDB export built from the tests' `RidbFixture` (and a marker facility):
| Case | `master` | this branch |
|---|---|---|
| TD-13: a commented, CRLF `ZionConfig.yaml` with `autoRun: false` and a disabled `Mather.yaml`; menu Search | "No search file has autoRun: true; creating the example search", `ZionConfig.yaml` replaced by the example (new bytes and time stamp), the example searched (1 request), exit 0 | the warning, "Running 0 searches:", "Done: 0 searches, no matching availability.", back to the menu, exit 0; both files byte-identical with their time stamps; 0 requests |
| TD-13: no search file, then a second start (this branch) | | first start "No search files found; creating the example search …", the example searched; second start: no creation line, `ZionConfig.yaml` unchanged (bytes, time stamp) |
| TD-13: two disabled files, HtmlFile / Email (75 s) / UnitTest (this branch) | | HtmlFile: one page opened ("0 searches", the empty state), 0 requests; Email: the warning, "Check 1: 0 searches, nothing new.", "Check 2: …", no `Camp.History`, 0 requests; UnitTest: nothing written, exit 0; the files unchanged in each |
| TD-37: empty `General.yaml`, redirected | "Campertron stopped because of an unexpected problem: NullReferenceException …" with the stack trace, exit 1 | "Error: General.yaml could not be used: The file has no settings: it is empty or contains only comments.", the file, "Fix the file, or delete it and Campertron creates a new one.", exit 1, nothing on standard output; the same once on a pseudo-terminal, without a key; the file still 0 bytes |
| TD-37: empty and comment-only search file; menu Search | "Trip.yaml could not be used: Object reference not set to an instance of an object.", exit 1 | "Trip.yaml could not be used: The file has no settings: …", "Fix the file, or move it out of the settings folder.", exit 1, no key; the file unchanged; no `ZionConfig.yaml` |
| TD-37: empty `Email.yaml`, Console Search (this branch) | | searched as usual ("Done: 1 search, …"), exit 0, the file still 0 bytes |
| TD-45: equal dates, menu Search, no key sent | the error, then waiting for a key until the driver stopped it after 20 s; with a key: exit 0 | one error "Trip.yaml could not be used: search "Equal dates": Start date and End date cannot be the same.", the file and what to do, exit 1 after 0.7 s, no key |
| TD-45: equal dates, Email, redirected | two errors (the date error, then "… could not be used: Cannot read keys …"), exit 1 | one error, exit 1 |
| TD-44: blocked answer, menu Search on a pseudo-terminal | the message, exit **0** after 309.1 s (the block came about 9 s after the start) | the same message, exit **1** after 309.1 s; the cursor is shown again (`ESC[?25h`) and the color reset at the end |
| TD-47: a fresh `HOME` without `.local/share`, no `XDG_DATA_HOME` (redirected, so the menu stops with its "needs an interactive terminal" error, exit 1) | `CampertronConfig` and `CampertronCache` (with the database) in the working directory | in the created `~/.local/share`; the working directory empty |
| TD-47: `XDG_DATA_HOME` set to a missing folder | in the working directory | the folder created and used; the working directory empty |
| TD-47: an existing `~/.local/share` with another program's folder | in it | in it (the same path); the other folder intact |
| TD-48: menu Refresh, a foreign `LocalApplicationData/Extract` (a file and a subfolder) and a sibling folder | the export extracted into `Extract`, refreshed and installed (the marker facility present); the foreign `Extract` **deleted** (4 of 6 entries gone) | extracted into `CampertronExtract`, refreshed and installed (the marker present); `CampertronExtract` removed afterwards; the foreign `Extract` and the sibling unchanged (names, sizes, time stamps) |

The refresh runs were ended by the driver after "The campground data was refreshed." (it did not recognize the redrawn menu); both databases had been installed.

**Container** [V]. Local images built from exports of `master` and this branch (`docker build`, amd64; never pushed; images, containers and container-owned scratch files removed afterwards with the image itself, nothing pulled), `--network host`, the HTTPS stand-in above, `/config` and `/cache` bind-mounted from a scratch directory, one Watchman search (`DaysOut 90`):
- **Blocked answer** (Email output): both builds "Error: Blocked by API for exceeding request limit, try again later (WATCHMAN CAMPGROUND, 2026 November). Campertron waits 5 minutes and then exits.", then 300.01 s from that line to the end; `master` exit **0**, this branch exit **1**; 0 control bytes in `docker logs`.
- **Empty `General.yaml`**: this branch the file-specific error with `/config/General.yaml`, exit 1; `master` "unexpected problem: NullReferenceException", exit 1.
- **Empty search file**: this branch "Watchman.yaml could not be used: The file has no settings: …", exit 1, the file still 0 bytes, no example; `master` "… Object reference not set …".
- **Search files, none enabled** (a disabled `Watchman.yaml` and a commented `ZionConfig.yaml`): this branch the warning, "Running 0 searches:", "Check 1/2: 0 searches, nothing new.", `docker stop` exit 143, both files byte-identical with their time stamps; `master` replaced `ZionConfig.yaml` and searched the example.
- **Paths**: the start line reads "settings /config | cache /cache" on both builds.
- **P3 regression** (this branch): SMTP refused, exit 1, no `/config/Camp.History`; SMTP stand-in, "Email sent: Watchman single nights (7 openings)", 7 markers, `Camp.History` created 0.65 ms after the stand-in stored the message, check 2 "nothing new", `docker stop` exit 143.
- **All runs**: no password printed; 0 control bytes.

**Not run** [V]: Windows and macOS (the changes are platform-neutral: `SpecialFolderOption.Create` and `Path.IsPathFullyQualified` behave the same there, and `%LOCALAPPDATA%` and `~/Library/Application Support` always exist; the separately approved release dry run before the `v0.1.0-preview.1` tag runs the native smoke jobs, decisions.md §Roadmap); arm64; a container refresh (the container's extraction folder, `CampertronExtract` relative to `/app`, follows from the same code, open question #27); no request reached recreation.gov or ridb.recreation.gov except the existing TD-01 test request; no email left the machine.

**Regression checks for later changes**: `SettingsBootstrapTests`, `DataFolderTests`, `ConsoleFailureTests`, `AtomicRefreshTests`, `CachePathTests`; for settings files, a run with a commented, disabled `ZionConfig.yaml` (its bytes must not change); for exit codes, equal dates and a blocked answer from a stand-in (exit 1); for the data folder, a start with a fresh `HOME` (nothing in the working directory); for the refresh, a foreign `Extract` folder (unchanged).

### AC. `v0.1.0-preview.1`: curated notes, native-smoke dry run and publication (decision D38, 2026-11-14 to 2026-11-15)
Release preparation and publication of the first preview, each step separately approved by the maintainer: the curated notes (PR #54), one manual native-smoke dry run, one tag. No repository file changed after PR #54; the only Git change of the publication is the tag. Release history entry: §Release history.

**Curated notes (PR #54)** [V]: `.github/release-notes/v0.1.0-preview.1.md` only (13,059 bytes, SHA-256 `2c9eee717637dd06…`), merged as `6484dc21c31f2c78681f998f3e2c334d3e584153`. The composed body is 16,746 bytes, SHA-256 `dd8db68632ac5568c4028f89e40e2dab3fcc9ce09b96e2e0454baec3de0bb51c`, identical locally, in the PR's CI (#89) and in the post-merge CI (#90, which also passed 0 errors / 74 warnings and 428 + 52 tests). The `v0.1.0-beta.10` fragment and its composition (`e7ebacf0…`) are unchanged.

**Native-smoke dry run** [V]: Release run #30, https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43673714514 (`workflow_dispatch` on `master`, head `6484dc2`, attempt 1, 03:44:42 to 03:51:25 UTC). `workflow_dispatch` has no inputs, so the version was the synthetic `0.0.0-dryrun.30` and nothing could publish.
- 24 jobs succeeded; `docker-publish` and `release` were skipped.
- Verify: release-policy tests 322/322; both curated fragments valid, the preview.1 body composed to `dd8db686…` (16,746 bytes); 0 errors and 74 warnings, identical to CI #90; tests 428 + 52; 0 vulnerable packages.
- The first native smoke on the D34 and P1 to P4 code (the pull-request path filter does not match it; the last native smoke had run on PR #47's head `f9bc747`): the same results as run #31 below, with `0.0.0-dryrun.30+6484dc2…`.
- Artifacts (18: 16 archives, `container-image` 254,141,440 bytes, `release-metadata` 2,316 bytes) were downloaded and checked locally (digests, `SHA256SUMS.txt`, integrity, layout, embedded version, runtime config, the TD-35 guard, the linux-x64 `--version`), then deleted. The template-only `RELEASE_NOTES.md` equals `render_notes` for the synthetic version, as designed.
- Nothing was published: 9 tags, 7 releases, no draft, no GHCR tag for the dry run and no attestation.

**Pre-tag state** [V]: `master`, `origin/master` and `HEAD` at `6484dc2`, the tree clean, `master` the only branch, no open PR; CI #90 and Release run #30 successful on attempt 1; 9 tags, 7 releases (no draft), GHCR with the 7 beta tags and their attestation tags only; no `v0.1.0-preview.1` tag, release or image; both rulesets active (tags: update and deletion blocked, no bypass); immutable releases enabled; the publishing composition (`assemble_release_notes true`) gave `dd8db686…` and passed `validate_notes`; release-policy tests 322/322.

**Tag** [V]: `git tag -a v0.1.0-preview.1 -m "Campertron 0.1.0-preview.1" 6484dc21c31f2c78681f998f3e2c334d3e584153`, annotated and unsigned like every earlier release tag, object `51cf1a225c95f1fdc9ccf7437d19f0a20ed55b58`, tagger Shaun Hathaway. Only this tag was pushed, once, with the explicit refspec `refs/tags/v0.1.0-preview.1` (04:03:44 UTC); the remote tag peels to `6484dc2`.

**Release run #31** [V]: https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43675087450 (`push` of `v0.1.0-preview.1`, head `6484dc2`, attempt 1, 04:03:47 to 04:12:33 UTC): **26/26 jobs succeeded**, none skipped, no rerun.

| Job | Runner | Result |
|---|---|---|
| Release verify | ubuntu-latest | release-policy tests 322/322; both fragments valid; the publishing tag's notes required and composed (`dd8db686…`); .NET 11 project check; SDK 11.0.100 through `global.json`; package policy (5 projects); 0 errors and 74 warnings, identical to CI #90; tests 428/428 + 52/52; 0 vulnerable packages |
| 16 packages (console and GUI × linux-arm, linux-arm64, linux-x64, osx-arm64, osx-x64, win-arm64, win-x64, win-x86) | ubuntu-latest | runtime 11.0.0 and compiler `5.12.0-1.26529.118` in every package; TD-35 guard on the Windows GUI packages |
| Smoke linux-x64 (native) | ubuntu-latest, `xvfb-run` | console clean install (226,471,936 bytes); GUI smoke test `result=pass` (window, native handle, ready, rendered frame, 463 parks, 51 states, programmatic close, clean shutdown, exit 0); both `--version` report `0.1.0-preview.1+6484dc2…` on `.NET 11.0.0 (X64)`; `ReleaseCheck` GUI state data passed |
| Smoke win-x64 (native) | `windows-2025` (win25-vs2026 20261103.298.1), X64 | console `--version` (X64) and clean install; GUI smoke `result=pass`, clean shutdown, exit 0 |
| Smoke win-arm64 (native) | `windows-11-arm` (win11-vs2026-arm64 20261101.203.1), Arm64 | the same, Arm64 |
| Smoke osx-x64 (native) | `macos-26-intel` (macos26 20260824.0517.1), X64 | the same, X64 |
| Smoke osx-arm64 (native) | `macos-26` (macos26 20261102.0473.1), Arm64 | the same, Arm64 |
| Docker build (linux/amd64, linux/arm64) | ubuntu-latest | one multi-platform OCI build: index `b18e32cf…`, amd64 `45464f99…`, arm64 `b6666608…`, one compiler for both; the loaded amd64 image is the validated one (9 identical diff_ids); container clean install (226,471,936 bytes), `DOTNET_VERSION` 11.0.0, `--version` `0.1.0-preview.1+6484dc2…`; the publication rehearsal kept the index (4 manifests, 29 blobs); archive `3a43d0f0…` |
| Assemble | ubuntu-latest | 16 expected names; one toolchain for all 16; 16/16 checksums; notes validated |
| Publish image to GHCR | ubuntu-latest | before registry contact the downloaded archive matched `3a43d0f0…` and the validated index and manifests; the only reference was `…:0.1.0-preview.1`, published at `b18e32cf…`; image provenance attestation (Sigstore, Rekor) stored in the repository and the registry |
| Create GitHub release | ubuntu-latest | 16/16 checksums; the notes rebuilt from the tagged commit equal the assembled copy (`dd8db686…`); build-provenance attestation over 17 subjects; a draft with 17 assets and a byte-identical body, then published (`prerelease: false`, `draft: false`), `releases/latest` returned the tag, and the public body was read back byte-identical |

Executed natively: linux-x64, win-x64, win-arm64, osx-x64, osx-arm64 and the amd64 container. Built and checked but not run: linux-arm, linux-arm64 and win-x86; the arm64 image was checked structurally (platform, .NET 11, native SQLite, compiler provenance), not run.

**Published release** [V]: ID 445802503, https://github.com/ShaunAnthonyHathaway/Campertron/releases/tag/v0.1.0-preview.1, "Campertron 0.1.0-preview.1", published 04:12:29 UTC by `github-actions[bot]`. Immutable, **Latest**, not a draft; `prerelease: false` because policy publishes every version, also a SemVer pre-release, as a normal release marked Latest (D19). `v0.1.0-beta.10` is no longer Latest; nothing else about it changed.
- **Body:** 16,746 bytes, SHA-256 `dd8db68632ac5568c4028f89e40e2dab3fcc9ce09b96e2e0454baec3de0bb51c`: the approved fragment composed with the generated sections, published in the draft (D30). No dry-run version or placeholder in it.
- **17 assets:** the 16 archives `campertron-{console,gui}-0.1.0-preview.1-<rid>.{tar.gz,zip}` (1,142,921,700 bytes in total; console 63,316,836 to 68,415,984, GUI 73,883,727 to 81,168,117) and `SHA256SUMS.txt` (1,830 bytes); exactly `expected_archives 0.1.0-preview.1`.

**Independent verification after publication** (fresh download into an empty scratch directory, deleted afterwards) [V]:
- All 17 files match their GitHub sizes and digests; 16/16 `sha256sum --check --strict` against `SHA256SUMS.txt`, which lists exactly the 16 archives.
- All 16 archives open, have exactly one top-level folder `campertron-<app>-0.1.0-preview.1-<rid>` and the host executable (executable bit in the tar archives); the app DLL and `CampertronLibrary.dll` embed `0.1.0-preview.1+6484dc21c31f…`; the runtime config is `net11.0` / 11.0.0; the three Windows GUI archives carry no `libSkiaSharp.pdb` or `libHarfBuzzSharp.pdb` and keep Campertron's own PDBs.
- The linux-x64 console and GUI ran natively here for `--version` only: `0.1.0-preview.1+6484dc2…`, `.NET 11.0.0 (X64)`, exit 0, nothing written.
- **Attestations:** `gh attestation verify <file> --repo ShaunAnthonyHathaway/Campertron --signer-workflow ShaunAnthonyHathaway/Campertron/.github/workflows/release.yml --format json` passed for all 17 files: one attestation with 17 subjects, each subject's SHA-256 equal to its file; signer `release.yml@refs/tags/v0.1.0-preview.1`, source commit `6484dc2`, run 43675087450 attempt 1. `gh attestation verify oci://ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.0-preview.1` passed (SLSA provenance v1, subject `b18e32cf…`, the same signer and source).
- **GHCR:** `0.1.0-preview.1` = `sha256:b18e32cffa55075f02766fcf06c1d5fd7b2d629f0ce17f3b3cb087b68973bb95` (the served index bytes hash to it); linux/amd64 `45464f995d6c…` and linux/arm64 `b66666085386…`, plus one BuildKit SLSA v1 provenance manifest per platform (`2dc92e9c…` → amd64, `92ef6a9d…` → arm64); both images labeled version `0.1.0-preview.1`, revision `6484dc21c31f…`, source `https://github.com/ShaunAnthonyHathaway/Campertron`. Registry tags: the 8 versions and their attestation tags (16). **No `latest`, `0.1` or `0.1.0`** (404, as policy requires for a pre-release) and no `0.0.0-dryrun.30`; `0.1.0-beta.10` is still `a63e99aa…`.

**Final state** [V]: 10 tags (the 9 earlier ones unchanged); 8 releases, no draft; `v0.1.0-beta.10` (445493647) unchanged and immutable: 17 assets, body `49ad73e3…`, tag object `6e76ea44…`; both rulesets active, immutable releases enabled; `master` at `6484dc2`, no branch, commit or PR created by the publication. One manual dispatch (#30) and one tag run (#31), no rerun, no second attempt. **No recovery was required, and no follow-up release action is pending for preview.1.**

**Not run** [V]: the native execution of linux-arm, linux-arm64, win-x86 and the arm64 container; the console's interactive output on Windows and macOS (the native smoke covers `--version` and the silent start only; open question #38); rendering in email clients (open question #31).

### AD. CM1: the comment-preserving YAML document editor (decision D39, branch `feat/yaml-document-editing`, 2026-11-15)
Library and tests only: `function/Base/YamlDocument.cs`, `class/Base/YamlDocumentModel.cs`, `Yaml.SettingsDeserializer` (the three settings loaders now share the deserializer they each built; the writers and `GeneralConfigReplaceYaml` are unchanged) and three test classes. Nothing calls the editor, so no file is written differently and no runtime behavior changed. Linux x64 (CachyOS), SDK 11.0.100 / runtime 11.0.0. What changed: D39, business-rules §13, architecture.md §Configuration.

**Facts re-derived before the implementation** [V] (scratch prototypes outside the repository, YamlDotNet 16.3.0, its default parser; the test that pins each is named):
- `Mark.Index` counts UTF-16 code units; an astral character is two (`AstralCharacters_CountAsTwoUtf16Units_AsInTheParsersMarks`).
- A flow collection's end event is a zero-length mark **at** its `]` or `}` (`TheParser_ReportsAFlowSequencesEndAtItsClosingBracket`).
- A block scalar's range runs over its last line break and the blank lines after it, so it ends where the next key's line starts (`ABlockScalarsRangeRunsOverItsLastLineBreak_AndTheEditorStopsAtItsLastCharacter`); its header comment is inside the range.
- An empty value (`key:`, `key: # c`, `key:   `) is a zero-length range right after the colon (`EmptyValue_IsAZeroLengthRangeRightAfterTheColon`).
- A leading U+FEFF in a string is not skipped by the parser: it becomes part of the first key, which the loader then rejects (`AByteOrderMarkInTheText_IsKept_AndOffsetsCountIt`).
- An anchored or tagged collection's start event covers its properties (for a block sequence, the text up to the first `-`). YamlDotNet reads `'x'#c` and `{k: v}#c` as a comment but `[a]#c` as a syntax error; `  - a` followed by `   - b` is one item, `a - b`; a flow sequence with a trailing comma parses.
- The loader (`Deserialize<T>` with the camelCase convention) accepts duplicate keys (the last wins), aliases, directives and document markers, standard tags, quoted and complex keys, quoted numbers and booleans, `yes` as true, enum names in any case and as numbers; it rejects unknown keys (D20), merge keys, custom tags, several documents and a string-held U+FEFF; it reads `null`, `Null`, `NULL`, `~` and an empty value as null for a text setting; a date with an offset becomes local time. The writers' serializer writes the text `null` (and `true`, `007`) plain.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Debug` and `-c Release`, both `--no-incremental` | 0 errors, **74 warnings** each |
| Warning identities vs `master` `a66f084` built the same way (Release, scratch clone; file, code, message and line) | 48 identities on both: none added, none removed, no moved line (`Yaml.cs` has none; the new files have none) |
| The production commit alone (`7f17684`, scratch clone) | builds, 0 errors, 74 warnings |
| `XDG_DATA_HOME="<new temp dir>" dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **601/601** (549 `CampertronUnitTests` + 52 `CampertronGUITests`); 121 new cases (`YamlDocumentTests` 43, `YamlDocumentPreservationTests` 40, `YamlDocumentRefusalTests` 38). the run includes the known live request of `LoadTests.RunConsoleSearchTest` (TD-01); no new test uses the network |
| The loaders' suites (CLAUDE.md: `SettingsBootstrapTests`, `DataFolderTests`, `ConsoleFailureTests`, `AtomicRefreshTests`, `CachePathTests`, plus `ConfigFileReplaceTests`) | 142/142 |
| `bash .github/release-policy/test-release-policy.sh` | 322/322 (no policy or workflow file changed) |
| `check_curated_notes_dir .github/release-notes` (under bash, as CI) | valid; both previews unchanged (`e7ebacf0…`, 10,556 bytes; `dd8db686…`, 16,746 bytes) |
| `check_global_json`, `check_selected_sdk` (11.0.100), `check_package_policy` | pass (6 asset files: the 5 projects and a local `ReleaseCheck` restore) |
| `dotnet list <project> package --vulnerable --include-transitive`, all five projects | no vulnerable packages |
| Dependencies | none added or changed: YamlDotNet stays 16.3.0; no `.csproj`, `global.json`, workflow or release-policy file changed |
| Formatting: `git diff --check`; `dotnet format whitespace --verify-no-changes` on the new and changed files (the repository has no `.editorconfig` and CI runs no formatter) | clean, except two findings in `Yaml.cs` outside the change (`new EmailConfig();;` and a trailing space after `return config;`) that `master` has identically; left as they are |

**Negative controls** [V]. Each a temporary edit of `YamlDocument.cs`, built and run against the editor's 118 tests of the time (3 were added afterwards), then the file restored and checked byte for byte (`sha256sum -c`):
| Mutation | Result |
|---|---|
| Targeted patching replaced by whole-object serialization (`Serialize(Intended)`) | 76 of 118 fail: every edit is refused ("Text outside the edited ranges changed") |
| The same, with the verification disabled too | 75 fail: the goldens get the re-serialized text |
| A parser range shifted by one (every scalar's end + 1) | 97 fail |
| An edit's range shifted by one (`SetScalar` replaces `[start + 1, end + 1)`) | 35 fail, refused by the checks |
| The known block scalar corruption: a removal "through the line of the parser's end", which is the next key's line | 3 fail: the two block scalar removal goldens and the naive-removal test; each edit is refused ("The keys or their order changed", or the result has no settings left) |
| The same, with the verification disabled too | 16 fail: the 14 of the next row plus the two block scalar goldens; `Edit` now returns `smtpPort: 25\n` for `notes: \|\n  text\nsmtpServer: x\nsmtpPort: 25\n`: the next key silently deleted |
| The verification disabled alone | 14 fail, all of them the verification's own tests; the block scalar goldens pass (the planner is right, the check is the second line of defense) |
| Inline comments not detected (a `#` after a space skipped) | 63 fail: values followed by a comment are refused as an unknown layout |
| Unknown keys omitted (the plan removes them) | 5 fail: the unknown-key, nested-section and block scalar goldens (the checks accept a removal the plan declares, so only the goldens can catch this) |
| The runtime model check bypassed | 6 fail |
| The loaded model compared with itself instead of the intended one | 5 fail |
| A `File.Exists` call in `Analyze` | 1 fails: the IL scan |
| An exception's inner message in the loader-failure diagnostic | 2 fail: the secret appears |
| The null-form check removed from plain writing | 2 fail |

**Cross-version check** [V, outside the repository; nothing in it changed]: YamlDotNet 18.1.0's default parser path gives event streams and marks identical to 16.3.0 for 22 documents covering the editor's cases (flow end marks, block scalar ranges and headers, empty values, anchors and tags on collections, CRLF and CR, astral characters, directives and markers, an indented root, `'x'#c`, `{k: v}#c`). YamlDotNet stays 16.3.0 (D39).

**Isolation** [V]: everything is in memory except one test, which writes the runtime writer's output into its own temporary folder and deletes it. The real `~/.local/share/CampertronConfig` and `CampertronCache` were fingerprinted (path, size, mtime and ctime of 700 entries) before the work and compared after it: unchanged. No new network access, no email, no browser.

**Not run** [V]: Windows and macOS (the editor is platform-neutral: strings, YamlDotNet's parser and its deserializer, no file or path; LF, CRLF and CR documents are tested on Linux), a container (the console does not call the editor), arm64. No workflow was dispatched; the change touches no path of the release dry run.

**Regression checks for later changes**: `YamlDocumentTests`, `YamlDocumentPreservationTests`, `YamlDocumentRefusalTests`; for a change to the planner, the checks or `Yaml.SettingsDeserializer`, the negative controls above (a control that no longer fails shows a gap in the tests).

### AE. CM2: safe, atomic, comment-preserving settings writers (decision D40, branch `fix/yaml-writers-preserve-and-atomic`, 2026-11-15)
Library, tests and one CI job: `function/Base/ConfigFileReplace.cs` (rewritten) and `function/Base/Yaml.cs` (`GeneralConfigReplaceYaml`, the three `*ConvertToYaml` sample writers, the sample paths of `EmailConfigGetConfig`, `GeneralConfigGetConfig` and `CampertronConfigGetConfigs`); `YamlDocument` (CM1) is unchanged; `.github/workflows/ci.yml` gained the Windows job below. Linux x64 (CachyOS, btrfs), SDK 11.0.100 / runtime 11.0.0, and Windows in CI. What changed: D40, business-rules §8 step 11, §9, §13 and §14, architecture.md §Configuration and §Persistence.

**Reconciled before the implementation** [V] (code on `master` `1ce41ef`, .NET's own implementation, scratch checks outside the repository):
- **Encoding.** The loaders read with `File.ReadAllText`: UTF-8 by default, a byte-order mark detected and dropped (also a UTF-16 one, whose text is converted silently), invalid bytes turned into U+FFFD. The writers wrote with `StreamWriter` (UTF-8, no byte-order mark). The loaders are unchanged; the writer now decodes strictly and keeps a UTF-8 byte-order mark (business-rules §14).
- **`File.Move` on Unix** (IL of `System.IO.FileSystem.MoveFile`): `overwrite: true` is `rename(2)`; `overwrite: false` is `link(2)` and `unlink(2)`, with a copy fallback (`LinkOrCopyFile`) when the file system has no hard links. On Windows (.NET source) both are `MoveFileExW` with `MOVEFILE_COPY_ALLOWED`, plus `MOVEFILE_REPLACE_EXISTING` for an overwrite. `File.Replace` checks no precondition on either platform, and no portable API offers a conditional rename, so the concurrency check is optimistic (business-rules §14).
- **D26's replacement** (`ConfigFileReplace.Replace` before D40) gave `General.yaml` the temporary file's default mode (0644 for a 0600 file with umask 022) and replaced a symbolic link with a regular file (the step-5 investigation's prototypes); it re-serialized the in-memory settings, dropping every comment.
- **Backups.** The only existing `.bak` convention is the startup repair's timestamped database backup (`RecreationDotOrg.db.<label>-<time>.bak`, business-rules §8); a settings file's single rolling `<name>.bak` does not collide with it. `MainWindowTests` checks that a GUI start leaves no `*.bak` in the settings folder: a start creates samples only, which have no backup, and it passes.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Debug` and `-c Release`, both `--no-incremental` | 0 errors, **74 warnings** each |
| Warning identities vs `master` `1ce41ef` built the same way (Release, scratch clone; file, code, message and line) | 48 identities on both: none added, none removed, no moved line. The new test methods that use Unix file modes carry `[UnsupportedOSPlatform("windows")]`, so the platform analyzer (CA1416) adds nothing ; the Windows job's build (the library and the unit tests) has the same 63 distinct warnings as their Linux build |
| The first commit alone (`764f1ac`, the writer, its callers and its tests; a separate worktree) | builds, 0 errors, 74 warnings; the writer, settings, refresh, data-folder, failure, cache-path and CM1 suites 292 passed, 1 skipped (the Windows-only test) |
| `XDG_DATA_HOME="<new temp dir>" dotnet test Campertron.sln -c Release --no-build` | **681 tests: 629 `CampertronUnitTests` (610 passed, 19 skipped: the 18 `ConfigFileReplaceWindowsTests` and `OnWindows_AReparsePoint_IsRefused`, Windows only) + 52 `CampertronGUITests`**, 0 failed (the same in CI run 43730551024). 80 new unit test cases: `ConfigFileReplaceTests` 10 → 42 (rewritten for the new API: the 10 old cases tested `Replace(path, write, validate)`, which no longer exists; each guarantee they checked is checked again, and their "same bytes as `GeneralConfigConvertToYaml`" case was replaced on purpose by the timestamp-only edit), `GeneralYamlTimestampTests` 24 (new), `ConfigFileReplaceWindowsTests` 18 (new, Windows only), `SettingsBootstrapTests` 10 → 15, `AtomicRefreshTests` 51 → 52. The run includes the known live request of `LoadTests.RunConsoleSearchTest` (TD-01); no new test uses the network |
| CM1's suites (`YamlDocumentTests` 43, `YamlDocumentPreservationTests` 40, `YamlDocumentRefusalTests` 38) | 121/121, unchanged |
| The loaders' suites (`SettingsBootstrapTests` 15, `DataFolderTests` 12, `ConsoleFailureTests` 36, `AtomicRefreshTests` 52, `CachePathTests` 23) plus `ConfigFileReplaceTests` and `GeneralYamlTimestampTests` | 203 passed, 1 skipped (the Windows-only test) |
| The Windows job's filter run on Linux with an empty data folder | 220 tests, 201 passed, 19 skipped (Windows only); nothing was written into the data folder |
| `bash .github/release-policy/test-release-policy.sh` | 322/322 (no policy or workflow file changed) |
| `check_curated_notes_dir .github/release-notes` (under bash, as CI) | valid; both fragments unchanged (`v0.1.0-beta.10.md` `c91c0173…`, 6,909 bytes; `v0.1.0-preview.1.md` `2c9eee71…`, 13,059 bytes) |
| `check_global_json`, `check_selected_sdk` (11.0.100), `check_package_policy` | pass (6 asset files) |
| `dotnet list <project> package --vulnerable --include-transitive`, all five projects | no vulnerable packages |
| Dependencies | none added or changed: YamlDotNet stays 16.3.0; no `.csproj`, `global.json`, release-policy, model, GUI or console file changed; the only workflow change is the Windows job (approved) |
| Formatting: `git diff --check`; `dotnet format whitespace --verify-no-changes` on the changed files | clean, except the two findings in `Yaml.cs` that `master` has identically (`new EmailConfig();;` and a trailing space after `return config;`, §AD) |

**Negative controls** [V]. Each a temporary edit of `ConfigFileReplace.cs` or `Yaml.cs`, built and run against `ConfigFileReplaceTests`, `GeneralYamlTimestampTests`, `SettingsBootstrapTests` and `AtomicRefreshTests` (130 tests at the time), then the files restored and checked byte for byte (`sha256sum -c`):
| # | Mutation | Failing tests |
|---|---|---|
| 1 | `General.yaml` re-serialized as a whole (the D26 behavior) | 16 |
| 2 | Comments dropped from the candidate | 11 |
| 3 | Permissive UTF-8 decoding (invalid bytes become U+FFFD) | 3 |
| 4 | The byte-order mark dropped | 2 |
| 5 | The temporary file not read back and checked | 2 |
| 6 | The final read-back skipped | 3 |
| 7 | The file's mode not given to the new file | 6 |
| 8 | The backup skipped | 17 |
| 9 | The comparison with what was read (hash and identity) skipped | 6 |
| 10 | The stale in-memory settings written instead of the current file (the D26 behavior) | 15 |
| 11 | A symbolic link replaced instead of its target | 2 |
| 12 | The sample writers back on a truncating `StreamWriter` | 3 |
| 13 | The stale-temporary cleanup matching any `*.tmp` | 1 |

**Interruption harness** [V]. A scratch console program outside the repository references the library, sets the internal `ConfigFileReplace.Step` seam through reflection to print each step and pause at a chosen one, and runs `Yaml.GeneralConfigReplaceYaml` on a commented `General.yaml` (replacement) or `Yaml.EmailConfigGetConfig` on an empty folder (creation) in a new temporary folder. A Python driver waits for the pause, kills the process with `SIGKILL`, records the file (old, new, absent or invalid, by exact bytes), the backup and the temporary files, then starts a new process that loads the file with the loader, one that writes again, ages the leftover temporary files past 10 minutes and runs the refresh-start cleanup. Two passes; the run on the committed production code (`764f1ac`; the later commits change only tests and documentation) is recorded here, and an earlier run on the same code gave the same results:
| Kill point | File | Backup | Temporary files left | Next load | Next write | Left after cleanup |
|---|---|---|---|---|---|---|
| replace: `temporary-writing` (file created, nothing written), `temporary-flushed`, `temporary-written`, `temporary-verified` | old | none | 1 | ok | new | 0 |
| replace: backup `temporary-writing`, `temporary-flushed`, `backup-written` | old | none | 2 | ok | new | 0 |
| replace: `backup-installed`, `before-precondition`, `before-replace` | old | old | 1 | ok | new | 0 |
| replace: `after-replace` | new | old | 0 | ok | nothing to write (holds the timestamp) | 0 |
| replace: not killed | new | old | 0 | ok | nothing to write | 0 |
| create: `temporary-writing`, `temporary-flushed`, `temporary-written`, `before-create` | absent | none | 1 | – | created | 0 |
| create: `after-create`, not killed | new | none | 0 | – | kept (the file exists) | 0 |

36/36 points passed (12 replacement and 6 creation points, two passes), before and again after the Windows-only additions to the writer (`7bf528d`; the same results). **Random kills** (`SIGKILL` 30 to 250 ms after the start, fixed seed): 60 replacements gave 22 old and 38 new files, 40 creations gave 10 absent and 30 new files; 0 partial, empty or invalid files, every backup equal to the old file. Before an interrupted write's leftovers are 10 minutes old, the next write leaves them alone; they are never loaded.

**Container** [V, local, nothing pushed]: `docker build -f CampertronConsole/Dockerfile -t campertron-cm2-local:validation .` (linux/amd64), with empty host folders mounted as `/config` and `/cache` (writable for the image's `app` user):
| Run | Result |
|---|---|
| First start with empty folders (the sample's `outputTo: Console`) | the container created `General.yaml` and `Email.yaml` (owner `app`, 0644); `Email.yaml` byte-identical to `master`'s sample, `General.yaml` identical except the generated time; then "Unsupported output type in container" and exit 1, as before |
| The host's CM2 writer (`Yaml.GeneralConfigReplaceYaml`, a scratch program) updated that `General.yaml`; container started again | only the timestamp line changed, `General.yaml.bak` = the previous bytes; the container loaded it and stopped with the same message (exit 1) |
| A commented `General.yaml` with a UTF-8 byte-order mark, CRLF line breaks and mode 0640 (`outputTo: UnitTest`), updated by the host writer; a `.bak` and a `General.yaml.<32 hex>.tmp` holding `outputTo: Email` in the folder | only the timestamp changed (byte-order mark, CRLF, comments and 0640 kept on the file and the backup); made readable for the container's user (0644), the container loaded it as `UnitTest` and exited 0 (neither the backup nor the temporary file was loaded) |
| The same file switched to `outputTo: Email`, no search file, `--network none`, stopped after 75 s | the container created `ZionConfig.yaml` (byte-identical to `master`'s sample), loaded it and started its search ("Running 1 search: … WATCHMAN CAMPGROUND …"), then retried the unreachable request as usual |

**Isolation** [V]: every test and check used new temporary folders (the harness and container folders in the session's scratch directory, removed afterwards). The real `~/.local/share/CampertronConfig` and `CampertronCache` were fingerprinted (path, size, mtime and ctime of 700 entries) before the work and compared after it: unchanged (their newest change predates the CM2 work). On Windows the job checks that the runner user's `CampertronConfig` and `CampertronCache` never exist.

**Windows: the automatic CI job** [V]. CM2's approval required Windows evidence from an automatic workflow before merge, and none existed: `ci.yml` ran only on Ubuntu, and the pull-request path filter of `release.yml` matches none of CM2's files. The maintainer then approved one focused Windows job in the normal pull-request CI, a narrow exception to the rule that workflows stay unchanged. No workflow was dispatched or rerun; every run below was started by a push to the pull request.
- **Job** `windows-settings-files` ("Windows settings files (CM1, CM2)"; "… (CM1 to CM3)" since D41, §AF) in `.github/workflows/ci.yml`: the pinned standard `windows-2025` runner (image win25-vs2026 20261103.298.1, Windows X64), 30-minute timeout, the same triggers as the Ubuntu job (every pull request to `master` and every push to `master`, no path filter) and the workflow's `contents: read`; no secret, upload, package, release or publication. Steps: checkout without credentials; `actions/setup-dotnet` 11.0.x (the same pinned SHAs); `check_global_json` and `check_selected_sdk` (SDK 11.0.100); restore and Release build of `CampertronUnitTests` and the library; `check_package_policy` on their two asset files; a fresh folder in the runner user's LocalApplicationData whose access rules are printed (`CAMPERTRON_TEST_USER_FOLDER`, used by the Windows-only tests; never `CampertronConfig` or `CampertronCache`, which must not exist before or after); `dotnet test CampertronUnitTests/CampertronUnitTests.csproj --configuration Release --no-build --blame-hang-timeout 10m --filter "FullyQualifiedName~CampertronUnitTests.function.Base.YamlDocument|FullyQualifiedName~CampertronUnitTests.function.Base.ConfigFileReplace|FullyQualifiedName~CampertronUnitTests.function.Base.GeneralYamlTimestampTests|FullyQualifiedName~CampertronUnitTests.function.Base.SettingsBootstrapTests"` with a TRX logger; a summary of the TRX (per class, every skipped test with its reason; it fails if the Windows-only class did not run); the data-folder check, which also removes the probe folder. It is not a required check of the `master` ruleset (only the Ubuntu job is).
- **Runs**: 43729516008 (`2dfb3d9`): the Ubuntu job passed, the Windows job had one failure: a file moved in from another folder was refused by the explicit-rule check, not by the new access-rule comparison, because Windows had turned its inherited rules into a protected DACL of explicit copies (the test was corrected, `7afa526`). 43730078017 (`7afa526`): one failure: after its folder's DACL was rewritten with `SetFileSecurityW`, the existing file's rules had also become protected explicit copies (that test now records the fact and accepts either refusal; the comparison is exercised by injection instead, `28071cc`). 43730551024 (`28071cc`): both jobs green. Windows found no defect in the production code; the access-rule comparison was added before the first Windows run, because the explicit-rule check alone can't see a file whose inherited rules differ from what a new file in its folder gets.
- **Results of 43730551024, Windows job**: build 0 errors, 67 warnings (the library and the unit tests only: the same 63 distinct warnings as their Linux build). TRX: **220 tests, 207 passed, 0 failed, 13 skipped**: 8 Unix-only test cases that NUnit leaves out on Windows because of their `[UnsupportedOSPlatform("windows")]` (the console summary therefore counts 212: 207 passed, 5 skipped) and 5 Unix link and permission tests (`Assert.Ignore`). Per class: `YamlDocumentTests` 43, `YamlDocumentPreservationTests` 40, `YamlDocumentRefusalTests` 38, `ConfigFileReplaceTests` 30 passed and 12 skipped, `ConfigFileReplaceWindowsTests` 18 of 18, `GeneralYamlTimestampTests` 23 passed and 1 skipped (the Unix link test), `SettingsBootstrapTests` 15. `CampertronConfig` and `CampertronCache` were never created; the tests left nothing in the probe folder.
- **ACL facts** (printed by the job and by the tests): LocalApplicationData and a new folder in it: `D:(A;OICIID;FA;;;SY)(A;OICIID;FA;;;BA)(A;OICIID;FA;;;LA)`, not protected, 0 explicit and 3 inherited rules, owner BUILTIN\Administrators (the runner user is an administrator); a new file there: `D:(A;ID;FA;;;SY)(A;ID;FA;;;BA)(A;ID;FA;;;LA)`, not protected, 0 explicit, 3 inherited, attributes `Archive`. An ordinary file therefore has only inherited rules, and CM2 updates it with exactly those rules (the file and its backup compared as SDDL before and after). A file moved in from another folder, and a file whose folder's DACL was rewritten with `SetFileSecurityW`, had `D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;LA)` (protected, 3 explicit rules) and were refused as files with access rules of their own. Refusing every file with explicit or protected rules therefore does not affect ordinary files.
- **What passed natively on Windows**: an ordinary `General.yaml` (comments, CRLF, a BOM) in the LocalApplicationData folder got only the new timestamp with the same access rules and attributes and an exact backup; replacement and creation through `MoveFileExW`, both read-backs, the backup and its replacement by rename; strict decoding (invalid UTF-8, UTF-16 and UTF-32 refused) and the BOM; LF, CRLF, CR and mixed line breaks; external changes (content, a deletion, a recreation with the same bytes, an access-rule change); injected failures at each of 8 replacement and 3 creation steps (the file intact, the earlier backup intact or wholly replaced, only the operation's own temporary files removed, unrelated files and look-alike names kept); a hidden file and its backup stay hidden; a read-only file, explicit and protected rules and a temporary file (of the content or of the backup) given other rules are refused before anything is renamed; a file held open without delete sharing (only the rename fails), a file held open for writing (not even read), a backup held open and a locked temporary file (the rename fails, the file is intact, only that temporary file is left and the stale cleanup removes it later) are refused cleanly; a symbolic link with the file's name is neither followed nor replaced, a junction with the file's name is refused as a directory, a creation never writes through a dangling link, and a backup name that is a link is replaced by rename, never written through (the runner user could create symbolic links; on an account without that privilege those tests skip and say why); the samples are created with their bytes and never written over, also when another program creates them first.
- **Not established on Windows**: process interruption (no kill harness ran there; the injected failures are its deterministic equivalent, and the old-or-new guarantee rests on the Linux harness and `MoveFileExW`'s documented behavior), power loss, other Windows versions and file systems (ReFS, network shares, cloud placeholders), Windows on arm64, a non-administrator account.

**macOS**: not run; no automatic workflow runs this code there, and nothing here claims macOS behavior. Also not established: power loss, a network or FUSE file system, a file system without hard links (the creation's copy fallback), arm64.

**Regression checks for later changes**: `ConfigFileReplaceTests`, `GeneralYamlTimestampTests`, `SettingsBootstrapTests` and `AtomicRefreshTests` (and CM1's suites for the editor), and the Windows CI job (`ConfigFileReplaceWindowsTests` runs only there); for a change to the writer's order, checks, permissions, links or cleanup, the negative controls and the interruption harness above.

### AF. CM3: configuration validation and the bounded settings loader (decision D41, branch `feat/config-validation`, 2026-11-15)
Library, tests and the Windows CI job's filter: `class/Base/ConfigDiagnostic.cs`, `function/Base/ConfigFiles.cs`, `function/Base/ConfigValidation.cs` (new); `Yaml.cs` (the bounded reader and scan, the loaders through them, the run-time refusals, `ConfigFiles` in the search loader), `Load.cs` (the Email output's start check, the `CheckWait` seam), `Menu.cs` and `ConsoleUi.cs` (the shared classification), `YamlDocument.cs` (`EventList` internal, the limits' comment, the `InvalidOperationException` refusal); `.github/workflows/ci.yml` (the Windows job runs the CM3 suites). Linux x64 (CachyOS, btrfs), SDK 11.0.100 / runtime 11.0.0. What changed: D41, business-rules §9, §12 to §15.

**Facts established before the design** [V] (a scratch program outside the repository, YamlDotNet 16.3.0 through `Yaml.SettingsDeserializer`): duplicate keys load (the last wins); a second document fails ("Expected 'StreamEnd'"); a flow-mapping root loads; an unknown key fails at the key ("Property 'x' not found on type …"); enum names load in any case and numbers load even when undefined (`searchBy: 9`); `.nan` and `.inf` load as doubles while `NaN` and `Infinity` don't; a bad value fails with an inner `FormatException`, `OverflowException` or `InvalidCastException` at the value; null list items load as null; 5,000 nested lists fail fast for these models (no stack overflow); `DateTime.AddDays(NaN)` returns the time unchanged (so `.nan` refreshed on every start) and `AddDays(±∞)` or beyond about ±3.6 million days throws; `MailAddressCollection.Add` accepts a comma-separated list; `SmtpClient.Port` accepts 65536 and `Host` rejects only the empty string. Found while testing: YamlDotNet's scanner throws `InvalidOperationException` for an unclosed flow list followed by another line (its deserializer wrapped it in a `YamlException`; the CM1 editor threw it), and an alias to a list used where a text is expected is read as the text `(Collection)`.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release` and `-c Debug`, `--no-incremental` | 0 errors, **74 warnings** each |
| Warning identities vs `master` `c071658` (Release, file, code, message, line) | 48 identities on both, none added or removed; 11 moved lines, all in `Load.cs` below the inserted check and seam |
| Each commit alone (worktree) | `b9acc65` (production) builds 0/74 and passes the 629 existing unit tests; `5dc5468` builds; the later commits are the tested tree |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build` | **762 tests: 710 `CampertronUnitTests` (691 passed, 19 skipped: the Windows-only tests) + 52 `CampertronGUITests`**, 0 failed. 81 new: `ConfigFilesTests` 20, `SettingsLoaderLimitsTests` 10, `ConfigValidationTests` 29, `EmailOutputValidationTests` 22. CM1 (121) and CM2 (`ConfigFileReplaceTests` 42, `GeneralYamlTimestampTests` 24, `SettingsBootstrapTests` 15, `AtomicRefreshTests` 52) unchanged and green, and the loaders' suites (`ConsoleFailureTests` 36, `DataFolderTests` 12, `CachePathTests` 23, `SearchDatesTests` 34, `LoadTests` 13, `ConsoleOutputGoldenTests` 3) too |
| The Windows job's filter run on Linux with an empty data folder | 301 tests, 282 passed, 19 skipped (Windows only); nothing was written into the data folder |
| `bash .github/release-policy/test-release-policy.sh` | 322/322 |
| `check_curated_notes_dir`, `check_global_json`, `check_selected_sdk` (11.0.100), `check_package_policy` (6 asset files) | pass |
| `dotnet list <project> package --vulnerable --include-transitive`, five projects | none |
| Dependencies | none added: YamlDotNet stays 16.3.0; no `.csproj`, `global.json`, release workflow or release-policy file changed |
| Formatting: `git diff --check`; `dotnet format whitespace --verify-no-changes` on the changed files | clean except findings that `master` has on the same untouched lines (`Yaml.cs` `;;` and a trailing space; `Load.cs` lines 73 to 84) |

**Negative controls** [V]. Each a temporary edit, built and run against `ConfigFilesTests`, `SettingsLoaderLimitsTests`, `ConfigValidationTests`, `EmailOutputValidationTests`, `ConsoleFailureTests`, `SettingsBootstrapTests`, the CM1 suites, `GeneralYamlTimestampTests` and `ConfigFileReplaceTests` (319 tests), then restored and checked by SHA-256 (rebuild after restoring before any later `--no-build` run):
| # | Mutation | Failing tests |
|---|---|---|
| 1 | Unknown keys ignored (`IgnoreUnmatchedProperties` in `SettingsDeserializer`) | 8 |
| 2 | The classifier lower-cases with the current culture | 8 |
| 3 | The Email output's check after `Preload` | 3 |
| 4 | The SMTP port bounds removed | 5 |
| 5 | Recipient parsing bypassed | 3 |
| 6 | The sender's value put into its diagnostic | 1 |
| 7 | A negative `searchValue` let through | 4 |
| 8 | The `StartEndDate` range checks (equal, reversed) removed | 1 |
| 9 | The size limits (characters and bytes) disabled | 2 |
| 10 | The event limit disabled | 1 |
| 11 | The nesting limit disabled | 1 |
| 12 | The alias limit disabled | 2 |
| 13 | The validation deserializes with other settings (`IgnoreUnmatchedProperties`) | 4 |

**Container** [V, local, nothing pushed]: `docker build -f CampertronConsole/Dockerfile -t campertron-cm3-local:validation .` (linux/amd64), empty `/config` and `/cache`, `--network none` unless noted:
| Run | Result |
|---|---|
| First start | the samples were created; "Unsupported output type in container", exit 1, as before |
| `outputTo: Email` with the sample `Email.yaml`, no search file | "Error: Email.yaml could not be used: the Email output can't start: smtpServer is empty. smtpPort is not between 1 and 65535. sendFromAddress is empty. sendToAddressList has no address.", the file and what to do, exit 1; no example search created, the cache empty, no check line |
| A valid `Email.yaml` and a search file with 101 aliases | "Big.yaml could not be used: The file has more than 100 aliases.", exit 1 |
| A `General.yaml` of 1.1 million characters | "General.yaml could not be used: The file is larger than Campertron reads (at most 1048576 characters and 3145731 bytes).", exit 1 |
| Valid settings and an enabled search (some CRLF lines) | the Email loop started ("Running 1 search: Trip …"), then retried the unreachable request as usual; stopped after 45 s |

**Windows** [V, the automatic CI job `Windows settings files (CM1 to CM3)`]: the job's filter now also runs `ConfigFilesTests`, `SettingsLoaderLimitsTests`, `ConfigValidationTests` and `EmailOutputValidationTests` (the classification in seven cultures, the limits, CRLF input, the validation, the Email output's check without secrets) with the CM1 and CM2 suites; its results are recorded in the pull request. **macOS**: not run. Not established: other YamlDotNet versions (16.3.0 only), a GUI that shows the diagnostics (CM5).

**Isolation** [V]: temporary folders only; the real `~/.local/share/CampertronConfig` and `CampertronCache` (700 entries, newest change before the CM2 work) were unchanged.

**Regression checks for later changes**: `ConfigFilesTests`, `SettingsLoaderLimitsTests`, `ConfigValidationTests`, `EmailOutputValidationTests`, with the CM1 and CM2 suites and `ConsoleFailureTests`; for a change to the limits, the loaders' deserializer or the run-time refusals, the negative controls above.

### AG. CM4: campground identity by FacilityID (decision D42, branch `fix/campground-identity`, 2026-11-15)
Library, GUI and tests: `class/RecDotOrg/CampgroundChoice.cs` (new), `function/RecDotOrg/sqlite/Read.cs` (the FacilityID queries; the name-based methods unchanged); `CampertronGUI/ViewModels/ReactiveViewModel.cs`, `CampgroundDetails.cs`, `Views/MainWindow.axaml`; the GUI tests' fixture and helpers, the adapted GUI tests and three new test classes. No workflow, release-policy, `.csproj`, `global.json`, schema or shipped-database change. Linux x64 (CachyOS, btrfs), SDK 11.0.100 / runtime 11.0.0. What changed: D42, business-rules §8 and §16.

**Shipped data** [V] (read-only queries on the extracted `RecreationDotOrg.db`, SHA-256 `8712856097a91ee1…`, from the unchanged `RecreationDotOrg.db.br` `700cbd00…`; reservable, enabled `Campground` facilities):
| Finding | Result |
|---|---|
| Campgrounds; distinct FacilityIDs (TEXT primary key) | 4,966; 4,966 (unique) |
| FacilityID values | all digits, 5 to 8 characters, no leading zero, no padding |
| Distinct names (trimmed, case-insensitive) | 4,936: 24 names shared (30 extra rows), the empty name by 8 campgrounds, 23 pairs; no null or whitespace-only name; 103 names with outer spaces; the longest 94 characters; 2 names also used by a reservable, enabled facility of another type |
| Same name in one list | none within a state, a city or a park (the pairs are in different places); the bug was the details: `GetCampgroundDetailsByName` returns the first facility of a name, for example Utah's FISH CREEK CAMPGROUND (10241505) for Montana's (232493); 23 of the 46 same-named campgrounds got another campground's details that way |
| Qualifying address (Physical or Default with a state) | 3,337 have one (the By State and By City lists, unchanged); 1,629 have none (752 have no address row); 162 have two (4 in different states, 65 with different cities) |
| By Park | before 463 parks and 3,330 campgrounds (an address was required); now 522 parks and 4,937 campgrounds (1,607 more, 1,607 entries without a location); 29 campgrounds have no recreation area; one park name belongs to two areas (Sardis Lake, 154 and 577) |
| In no list | 22 campgrounds (no park and no qualifying address), all 8 blank-name ones among them (TD-53, open question #41) |
| Reachable in some tab | 4,944 (before: 3,337) |

**Queries** [V] (EF Core SQL captured through its `DiagnosticListener`; `EXPLAIN QUERY PLAN` on the shipped DB; Release, warm, median of 3; the only indexes are the primary keys; tables: 15,433 facilities, 16,505 addresses, 3,651 recreation areas):
| Query | SQL and plan | Time (before → now) |
|---|---|---|
| By State (`CampgroundChoicesByState`, CA: 435 entries) | `@State` parameter; scan of the addresses, facility by primary key (as `UniqueParksByState`, without its `DISTINCT` and `ORDER BY` temporary B-trees; ordering in managed code) | 4–5 → 5–6 ms |
| By City (UT, SPRINGDALE) | `@State`, `@City`; the same plan | 5 → 4–5 ms |
| Parks (`ParksWithCampgrounds`) | scan of the facilities, recreation area by primary key, `DISTINCT`, `ORDER BY` (before: a scan of the addresses) | 24 → 18–21 ms |
| By Park (`CampgroundChoicesByPark`, Zion: 3, Yosemite: 16) | `@Park`; scan of the facilities, recreation area by primary key, `LEFT JOIN` to the qualifying addresses through SQLite's automatic covering index (built per query; no new permanent index) | 17–18 → 22–25 ms |
| Details (`GetCampgroundDetailsById`) | `@FacilityID`; primary-key search, `LIMIT 2` (before, by name: a scan of the facilities) | 3 → under 1 ms |
| All 51 states / every state and city / all 522 parks, one after another | one command each; no FacilityID twice in a list | 143 ms / 1.9 s / 11.5 s |

One command per list or details read, none for no filter or a blank ID; every value is a parameter. The labels are built in managed code (`CampgroundChoice`), so the SQL stays a plain row query and the label rules are unit-tested without a database. Each query does one scan of one table, as before; no index or migration is needed.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release` and `-c Debug`, `--no-incremental` | 0 errors, **70 warnings** each (`master` `6de9748`: 74) |
| Warning identities vs `master` (Release, file, code, message) | 44 vs 48: none added; 4 removed, all `CS8604` in `ReactiveViewModel.cs` (nullable arguments to `UniqueParks`, `UniqueParksByState` and `UniqueCampgroundsByPark`, which the GUI no longer calls); the 2 `Read.cs` warnings moved with the inserted code |
| Each commit alone (worktree) | `bd4175e` (library) 0/74, 710 + 52 tests pass; `f2876bc` (library tests) 0/74, the new library tests and the 52 GUI tests pass; `4712c93` (GUI) 0/70, the 52 adapted GUI tests pass; `7a00173` is the tested tree |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build` | **790 tests: 720 `CampertronUnitTests` (701 passed, 19 skipped: the Windows-only tests) + 70 `CampertronGUITests`**, 0 failed. 28 new: `CampgroundChoiceTests` 4, `CampgroundIdentityTests` (library) 6, `CampgroundIdentityTests` (GUI) 18 cases. Adapted: `MainWindowTests`, `MainWindowUsabilityTests`, `ReactiveViewModelTests` (selections by FacilityID), `StartupTests` and `SmokeTestTests` (the fixture's fourth park). CM1, CM2 and CM3 suites unchanged and green |
| `bash .github/release-policy/test-release-policy.sh` | 322/322 |
| `check_curated_notes_dir`, `check_global_json`, `check_selected_sdk` (11.0.100), `check_package_policy` (6 asset files) | pass |
| `dotnet list <project> package --vulnerable --include-transitive`, five projects | none |
| Dependencies | none added; YamlDotNet stays 16.3.0 |
| Formatting: `git diff --check`; `dotnet format whitespace --verify-no-changes` on the changed C# files | clean except the findings `master` has on the same untouched `Read.cs` lines (moved) |

**Negative controls** [V]. Each a temporary edit (a scratch script outside the repository), built and run against the whole GUI suite and, for library edits, `CampgroundChoiceTests`, `CampgroundIdentityTests`, `CampgroundDetailsTests` and `AddressStateTests`; then restored and checked by SHA-256, and everything rebuilt before the next `--no-build` run:
| # | Mutation | Failing tests |
|---|---|---|
| 1 | The GUI lists reduced back to names (the name-based queries, the name as the identity) | 37 |
| 2 | Details looked up by name (`GetCampgroundDetailsByName`) | 8 |
| 3 | Equality by name instead of FacilityID | 10 |
| 4 | Same-named rows collapsed (grouped by name) | 16 |
| 5 | The details of the first campground of the name returned for an ID | 10 |
| 6a, 6b | The `Unnamed campground` fallback removed from the labels / from the details | 6 / 2 |
| 7 | By Park as an inner join (campgrounds without an address dropped) | 9 |
| 8a, 8b | A filter change keeps the campground / a cleared or unknown selection keeps the previous details | 1 / 6 |
| 9 | The ID and the park inlined as SQL constants (`EF.Constant`) | 2 |
| 10 | Cached details for fast selections | 21 |
| 11 | A web request to an availability URL (and a reference to `AvailabilityApi`) on selection | 2 |
| 12 | A settings-file write (`General.yaml`) on selection | 1 |

The GUI's details are computed synchronously from the current selection, so there is no asynchronous race to stale details; control 10 shows that a cached result would be caught.

**Linux real-window review** [V]. A scratch launcher (not in the repository) ran the real `App` with the production `AppBuilder` chain plus `LogToTextWriter(Warning)`, drove the window's own controls, injected key and text input through the window's input callback (reflection, as in §X) and captured the window (`import -window Campertron`; drop-downs from the root window, cropped). It ran in a nested `kwin_wayland --virtual` session under its own `dbus-run-session` without service activation; nothing appeared on the live desktop. KWin's own Xwayland failed to start there ("Failed to establish the XCB connection"), so the session started a rootful `Xwayland :9 -geometry 2600x1700` itself (scale 1). Two isolated `XDG_DATA_HOME` folders: a copy of the shipped database, and a fixture database with CM4's identity cases.
- **Shipped data:** 522 parks, 51 states. Zion National Park lists `Lava Point Campground — ID 10138237` (no address, newly listed), `SOUTH CAMPGROUND (UT) — SPRINGDALE, UT — ID 272266` and `WATCHMAN CAMPGROUND — SPRINGDALE, UT — ID 232445`; each item's accessible name is its label. By State: UT → FISH CREEK CAMPGROUND shows 10241505 (Fish Creek Campground, UT), MT → FISH CREEK CAMPGROUND shows 232493 (Glacier, phone 406-888-7800); `GetCampgroundDetailsByName` returns 10241505 for both.
- **Fixture:** the identity park lists 8 entries in the documented order with distinct labels; the twins with the same name and place differ only by ID; `Unnamed campground — ID 990008` and `Unnamed campground — IDENTITYVILLE, UT — ID 990007` select and show "Unnamed campground" with their own details; the Mailing-only campground has no location; `ID 0990010` keeps its zero; By City UT/IDENTITYVILLE lists 4 entries; changing the city after selecting 990012 cleared the selection and the details.
- **Keyboard:** typing "Yosemite" in the park selector selected Yosemite National Park; Tab → the campground selector; "WAWONA" → `Wawona Campground — YOSEMITE NATIONAL PARK, CA — ID 232446`; Down → `Wawona Horse Campsites — ID 10220609` (no address); Up → back; the details followed each. Tab from the campground selector: details area, ID, Copy ID, the tab header (TD-39 wrap).
- **Layout and themes:** Light and Dark (requested) at 1024×768 and 720×540: the selector shows the name and place, trimmed, and the ID at its right; the drop-down's IDs are right-aligned and whole. **Found and fixed in this review:** the drop-down first grew to its longest entry (708 DIP for a 307 DIP selector at 720×540 in the headless test, past the window edge in the real one) and pushed the IDs out of sight; it is now capped at the selector's width, and `ALongName_IsTrimmed_ButItsIdStaysWholeAndInside` checks the selector and the drop-down at 720 and 1024 on every tab.
- Exit code 0 for both runs; the only Avalonia warning was the AT-SPI server failing to find an accessibility bus, which the nested session does not provide (no application message). The scratch launcher's own fixture setup crashed once on a leftover fixture database from an earlier hung attempt (systemd-coredump recorded it without a core; `ulimit -c 0`); the repeated run passed.

**Windows and macOS** [V]: only through the pull request's automatic Release dry run (the path filter matches `CampertronGUI/**`; nothing dispatched or rerun), run 43743107413 on `7a00173` (PR #59): success; the 16 packages, the Docker build and the assembly passed, and the publishing jobs (GHCR image, GitHub release) were skipped, so nothing was published. Its native smoke tests started each run's own published GUI on **linux-x64, win-x64, win-arm64, osx-x64 and osx-arm64**: on every platform `SMOKE ready=yes`, `rendered-frame=yes`, `parks=522`, `states=51`, `theme=Light`, `shutdown=clean exit-code=0` and `result=pass` (the updated bindings and templates initialize natively and the shipped data loads through the new park query). They do not select campgrounds or capture the screen. CI run 43743107448 on the same commit: the Ubuntu job (0 errors / 70 warnings; 720 + 70 tests, 19 skipped; no vulnerable packages) and `Windows settings files (CM1 to CM3)` (293 tests, 288 passed, 5 Unix-only skipped, as after CM3; CM4 does not change these suites) passed. Not run: a screen reader, a physical keyboard, appearance on Windows and macOS. Container: not run (no console, search or container change; the library's new methods are only called by the GUI).

**Isolation** [V]: temporary folders only; the real `~/.local/share/CampertronConfig` and `CampertronCache` (700 entries, newest change 2026-11-15T12:40:01Z, before this work) were unchanged; the shipped `RecreationDotOrg.db.br` is unchanged.

**Regression checks for later changes**: `CampgroundChoiceTests`, `CampgroundIdentityTests` (library and GUI), `CampgroundDetailsTests`, `AddressStateTests` and the whole GUI suite; for a change to the lists, the details lookup or the selector templates, the negative controls above and a real-window look at a long name at 720×540.

### AH. CM5: GUI editing of General.yaml and Email.yaml (decision D43, branch `feat/gui-settings-editor`, 2026-11-15)
Library, GUI, tests, the smoke contract and the Windows CI job's filter: `class/Base/ConfigEditModel.cs` and `function/Base/ConfigEditSession.cs` (new); `ConfigFileReplace.Create` (an optional Unix mode), `Yaml.cs` (the sample settings as builders shared by the start and the GUI, `CreateSampleSettingsFile`), `Config.cs` (`InternalsVisibleTo` the GUI and its tests); the GUI's `SettingsViewModel`, `SettingsFileViewModel`, `GeneralSettingsViewModel`, `EmailSettingsViewModel`, `SettingFields`, `RelayCommand`, `SettingsView`, `GeneralSettingsView`, `EmailSettingsView`, `SettingsFileStatusView`, `SettingsFileActionsView`, `SecretTextBox`, `SettingsProbe` (new), `MainWindow.axaml(.cs)`, `MainWindowViewModel`, `AppStyles.axaml`, `SmokeTest.cs`; `.github/release-policy/release-policy.sh` (`check_gui_smoke_report`: two new steps) and its tests; `.github/workflows/ci.yml` (the Windows job runs the session suites). Linux x64 (CachyOS, btrfs), SDK 11.0.100 / runtime 11.0.0. What changed: D43, business-rules §14, §17.

**Facts established before the design** [V]: everything CM1 to CM3 built is internal and the GUI is another assembly, so the GUI (and its tests) were given the library's internals rather than making the editor, writer and validation public. YamlDotNet 16.3.0 loads `lastRidbDataRefresh: …Z` as a UTC `DateTime`; the editor keeps a value's quoting style (`smtpServer: ''` becomes `'smtp.example.org'`) and edits the serializer's empty flow list `[]` item by item. Avalonia 12.1.6's `TextBoxAutomationPeer` reports the masked text for a `PasswordChar` box but the **real text once `RevealPassword` is on**, and its `Value` is sealed: hence `SecretTextBox` with its own peer. `TextBox.CanCopy` is false with a `PasswordChar`, also while revealed. The Fluent theme has `SystemControlErrorTextForegroundBrush` but no warning brush (warnings carry "Warning:" in their text). Avalonia's launcher is `LauncherExtensions.LaunchDirectoryInfoAsync`. YamlDotNet's serializer writes the samples with the platform's line breaks (CRLF on Windows, shown by the Windows job).

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release` and `-c Debug`, `--no-incremental` | 0 errors, **70 warnings** each (`master` `294ab24`: 70) |
| Warning identities vs `master` | 44 on both, none added or removed (two analyzer findings in the new tests, CA1416 and a CS0108 name clash, were fixed before the commits) |
| Each commit alone (worktree) | `94f3907` (library) 0/70, the 790 existing tests pass; `6c5a4f4` (library tests) 0/70, 38 passed + 1 Windows-only skipped; `779aa3b` (CI) 0/70; `936209a` (GUI) 0/70, the 70 existing GUI tests (adapted) and the release-policy tests pass |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build` | **848 tests: 759 `CampertronUnitTests` (739 passed, 20 skipped: the Windows-only tests) + 89 `CampertronGUITests`**, 0 failed. 58 new: `ConfigEditSessionTests` 29 cases, `ConfigEditEmailTests` 10, `SettingsEditorTests` 19. Adapted: `MainWindowTests` (four tabs), `CampgroundIdentityTests` (four tabs; its IL, web and fingerprint helpers moved to `GuiTestHelpers`), `SmokeTestTests` (the two new steps). CM1, CM2, CM3 and CM4 suites unchanged and green |
| `bash .github/release-policy/test-release-policy.sh` | **329/329** (322 + 7 cases for the settings probe and the Settings tab lines) |
| `check_curated_notes_dir`, `check_global_json`, `check_selected_sdk` (11.0.100), `check_package_policy` (6 asset files) | pass |
| `dotnet list <project> package --vulnerable --include-transitive`, five projects | none |
| Dependencies | none added: YamlDotNet stays 16.3.0; no `.csproj` or `global.json` change |
| Formatting: `git diff --check`; `dotnet format whitespace --verify-no-changes` on the changed files | clean except findings `master` has on the same untouched lines (`Config.cs`, `Yaml.cs`, moved); one pre-existing trailing space in a touched method of `Yaml.cs` removed |

**Negative controls** [V]. Each a temporary edit (a scratch script outside the repository), built and run against `ConfigEditSessionTests`, `ConfigEditEmailTests`, `SettingsEditorTests` and `SmokeTestTests`, then restored and checked by SHA-256, and everything rebuilt before the next `--no-build` run:
| # | Mutation | Failing tests |
|---|---|---|
| 1 | The edited text replaced by a serialization of the whole settings object | 21 |
| 2 | Unknown keys loaded leniently and removed on save | 2 |
| 3 | Dirty tracking ignores `autoRefresh` and `smtpServer` | 25 |
| 4 | The external-change precondition bypassed (no check before the save, the writer given a fresh read) | 1 |
| 5 | A conflict resolved automatically with the edited values | 8 |
| 6a, 6b | No `PasswordChar` / the password box's accessibility value is its text | 1 / 1 |
| 7 | The preview not masked | 6 |
| 8a, 8b | A validation notice / an editor error carrying its field's value (a first variant, the first edit's value, was not caught; the test was strengthened with a refused edit of the password itself) | 3 / 1 |
| 9 | The backup readable by others | 6 |
| 10 | A save without edits writes the file | 4 |
| 11 | Validation errors no longer block the session's save | 5 |
| 12 | The GUI's Save enabled with blocking errors | 3 |
| 13 | A request to an availability URL (and a call into `AvailabilityApi`) on save | 1 |
| 14 | Every search file touched on save | 1 |

**Linux real-window review** [V] (as in §AG: a nested `kwin_wayland --virtual` session with its own D-Bus and a rootful Xwayland, nothing on the live desktop; a scratch launcher with the production `AppBuilder` chain, `LogToTextWriter(Warning)`, the window's own controls and keyboard input through the window's input callback; `import` captures). An isolated `XDG_DATA_HOME`: the GUI extracted the database itself; `General.yaml` with comments and CRLF, `Email.yaml` with comments, a fake password and mode 0644.
- Settings tab: the General form with the file's values; Tab order from the tab header: General.yaml, Email.yaml, Open settings folder, Output, the refresh check box, interval, last refresh, Reload, back to the header (TD-39 wrap). Edits by keyboard (interval 0, the check box with Space) and the output set to Email: the interval's warning at its field, the refresh explanation updated, the preview showing exactly the three changed lines; saved: the file kept its CRLF and comments, `General.yaml.bak` written.
- Email form: in the Email output its problems are errors and Save is disabled; the Info notice about other users reading the 0644 file; the password masked, revealed on screen with Show password while its accessibility value stayed the mask; after editing server, port, sender, a new recipient and the password the preview showed both password lines as `smtpPassword: •••••• # keep it safe`; saved (mode 0644 kept, backup 0644); the reveal ended with the save. A change by another program while editing opened the conflict card ("in other settings than yours"), Apply my changes and Save kept both changes. Dark at 720×540: an error at the port field; the file deleted on disk showed "Email.yaml does not exist." with Create, which created it 0600.
- No exposed text, tooltip, accessible name, help text or accessibility value held the password in any state (the window scanned at each step). Exit code 0; no Avalonia warning (the nested session's missing AT-SPI accessibility bus aside).

**Windows and macOS** [V]: the pull request's automatic Release dry run (path filter `CampertronGUI/**`; nothing dispatched or rerun), run 43749693457 on `6ae57dd` (PR #60): success; 16 packages, the Docker build and its container smoke tests, the assembly; publishing skipped. Its native smoke tests ran each run's own published GUI with the new settings probe:
| Platform | `settings-probe` | `settings-tab` | parks | shutdown |
|---|---|---|---|---|
| linux-x64 | pass … permissions=640,600 | yes general=editable email=editable | 522 | clean exit-code=0 |
| win-x64 | pass … permissions=access-rules-kept | yes general=editable email=editable | 522 | clean exit-code=0 |
| win-arm64 | pass … permissions=access-rules-kept | yes general=editable email=editable | 522 | clean exit-code=0 |
| osx-x64 | pass … permissions=640,600 | yes general=editable email=editable | 522 | clean exit-code=0 |
| osx-arm64 | pass … permissions=640,600 | yes general=editable email=editable | 522 | clean exit-code=0 |

("pass general=saved email=created,saved backups=exact line-endings=crlf bom=kept comments=kept": the probe's temporary `General.yaml` (BOM, CRLF, comments, mode 0640) and a created `Email.yaml` (0600) were edited and saved through the session, compared byte for byte, with exact backups of the same mode or access rules and no temporary file left.) So the CM2 writer ran natively on macOS for the first time (open question #25). The CI job `Windows settings files (CM1 to CM3, CM5)` runs the session suites natively on `windows-2025`; on `6ae57dd` it found one test assuming LF in the created sample (fixed in `52ab816`); `ConfigEditEmailTests.TheAccessRulesOnWindows_AreNeverBroadened` passed there. Not run: a screen reader, a physical keyboard, the Settings tab's appearance on Windows and macOS (the smoke tests take no screenshots), process interruption during a GUI save. Container: no container or console behavior change (the start's samples keep their bytes, `SettingsBootstrapTests`); the dry run's container smoke tests passed; not run separately.

**Isolation** [V]: temporary folders only; the real `~/.local/share/CampertronConfig` and `CampertronCache` (700 entries, newest change before this work) were unchanged; the shipped `RecreationDotOrg.db.br` is unchanged.

**Regression checks for later changes**: `ConfigEditSessionTests`, `ConfigEditEmailTests`, `SettingsEditorTests`, `SmokeTestTests`, the CM1 to CM3 suites and the release-policy tests; for a change to the session, the preview, the password box or the smoke probe, the negative controls above.

### AI. CM6: GUI creation and editing of search files (decision D44, branch `feat/gui-search-editor`, 2026-11-15)
Library, GUI, tests, the smoke contract and the Windows CI job's filter: `class/RecDotOrg/CampgroundPickerEntry.cs` (new), `Read.CampgroundPickerEntries`; `ConfigEditSession.cs` (search files, Save As, `SearchFiles.Create`, the campground check, empty list items), `ConfigEditModel.cs` (`ConfigListItem.Value` nullable), `ConfigFiles.cs` (`SortedSearchFileNames`, `SearchFileName`), `ConfigValidation.cs` (`SearchFolderDiagnostic`, `RunsAtStart`; `ValidateFolder` unchanged in result), `Yaml.cs` (`SearchSampleConfig` shared by the start, `CreateSampleSearchFile`); the GUI's `SearchesViewModel`, `SearchFileViewModel`, `CampgroundPicker.cs` (`CampgroundCatalog`, `CampgroundSettingField`), `SearchesView`, `SearchFileView`, `ListFieldView`, `SearchProbe` (new), `SettingFields.cs` (list rows shared with Email.yaml's recipients), `SettingsFileViewModel` (list handling, `PreviewNote`), `EmailSettingsViewModel`, `GeneralSettingsViewModel` (`OutputToValue`), `MainWindowViewModel`, `MainWindow.axaml(.cs)` (`SearchesTabIndex` 4), `SettingsFileActionsView`, `SmokeTest.cs`; `.github/release-policy/release-policy.sh` (`check_gui_smoke_report`: two new steps) and its tests; `.github/workflows/ci.yml` (the Windows job runs the search-file suites). Linux x64 (CachyOS, btrfs), SDK 11.0.100 / runtime 11.0.0. What changed: D44, business-rules §9, §18.

**Facts established before the design** [V]: the CM1 editor already supports `CampertronConfig` (its scalar types, its enum and its nine string lists, the model's only nested values), so no editor change was needed. It refuses to edit a sequence item by item when an item is a bare `-` (empty) and accepts `- ~`; it quotes a new item that would read as another type (`'2027-07-09'`) and a flow-list item with a colon (`['Pets Allowed:No']`). YamlDotNet 16.3.0 reads `searchBy: 7` into the enum as the undefined value 7. `ConfigFileReplace.Create`'s create-only move is `File.Move(overwrite: false)` (link(2) on Unix), so a concurrent winner is never written over. In the shipped database, 4,966 reservable, enabled campgrounds, 22 without a named park and without a qualifying address (TD-53), 8 with a blank name; `Read.CampgroundPickerEntries` reads them with one query in about 50 ms (CampgroundPickerTests' timing line). CM5's Email editor would have thrown on a recipient list with an empty item (`SetItems` refused a null value); the shared list rows keep such an item as it is (found while generalizing the rows; fixed with them).

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release` and `-c Debug`, `--no-incremental` | 0 errors, **70 warnings** each (`master` `6beedfb`: 70) |
| Warning identities vs `master` | 44 on both, none added or removed (a CS0108 name clash, `ListSettingField.Changed`, was renamed before the commits) |
| Each commit alone (worktree, Release, `--no-incremental`, full suites) | `1b25117`, `c8c9bc7`, `16d445e`, `44f5143`, `55e28e3`: 0 errors, 70 warnings with `master`'s identities, both test projects pass; `55e28e3` also release-policy 337/337. `c8c9bc7` carries a one-character adaptation in `EmailSettingsViewModel` (`Item.Value!`, the former behavior) so it adds no nullable warning; `55e28e3` replaces it with the shared rows |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build` | **961 tests: 858 `CampertronUnitTests` (838 passed, 20 skipped: the Windows-only tests) + 103 `CampertronGUITests`**, 0 failed. 113 new: `SearchFileSessionTests` 31 cases, `SearchFilesTests` 51, `CampgroundPickerTests` 17, `SearchEditorTests` 14. Adapted: `MainWindowTests`, `CampgroundIdentityTests` (GUI; five tabs), `SettingsEditorTests` (its IL scan's types: `ListRow`), `SmokeTestTests` (the two new steps); `CampgroundIdentityTests`' SQL recorder is shared (library). CM1 to CM5 suites unchanged and green |
| `bash .github/release-policy/test-release-policy.sh` | **337/337** (329 + 8 cases for the search probe and the Searches tab lines) |
| `check_curated_notes_dir`, `check_global_json`, `check_selected_sdk` (11.0.100), `check_package_policy` | pass (in CI) |
| `dotnet list <project> package --vulnerable --include-transitive`, five projects | none (in CI) |
| Dependencies | none added: YamlDotNet stays 16.3.0; no `.csproj`, `global.json` or database change |

**Negative controls** [V]. Each a temporary edit (a scratch script outside the repository), built and run against `SearchFileSessionTests`, `SearchFilesTests`, `CampgroundPickerTests`, `ConfigEditSessionTests`, `ConfigEditEmailTests`, `ConfigFilesTests`, `SearchEditorTests`, `SmokeTestTests` and `SettingsEditorTests`, then restored and checked by SHA-256, and everything rebuilt afterwards:
| # | Mutation | Failing tests (library / GUI) | For example |
|---|---|---|---|
| 1 | Whole-file re-serialization replaces the targeted edits | 26 / 12 | `AChangedPassword_IsSavedThroughTheEditor_AndMaskedInThePreview`, `ADeletedFile_IsMissingWhenClean_AndAConflictWhenEdited` … |
| 2 | Search files identified with culture-sensitive lower-casing | 9 / 0 | `Classify_IsOrdinalAndCaseInsensitive_InEveryCulture`, `EveryFormerCaller_UsesTheSharedRule_AndNoneLowerCasesAName` … |
| 3 | General.yaml and Email.yaml in the search list | 1 / 16 | `AFileThatCantBeEdited_OpensReadOnly_WithItsReason`, `ATypedID_IsUsedOnlyIfItIsExactlyACampground` … |
| 4 | Unknown content loaded leniently and dropped on save | 2 / 2 | `AFileThatCantBeEdited_OpensReadOnly_WithItsReason`, `FilesThatCantBeEditedSafely_OpenReadOnly_WithAReason` … |
| 5 | List comments lost (whole list replaced) | 5 / 4 | `AnExternalChangeOfOtherSettings_IsAConflict_ThatKeepsBothChanges`, `AnExternalChangeOfTheSameSettings_NeedsTheChoiceToKeepMine` … |
| 6 | Null lists normalized to [] without an edit | 10 / 4 | `ADeletedFile_KeepsTheEdits_AndCanBeCreatedAgain`, `ALineBreakChangeOnDisk_IsKept_WhenTheEditsAreApplied` … |
| 7 | FacilityID parsed as a number | 0 / 2 | `ATypedID_IsUsedOnlyIfItIsExactlyACampground`, `ThePicker_ChoosesAnyCampground_ByItsFacilityID` |
| 8 | A campground label (name) used as identity | 0 / 1 | `ThePicker_ChoosesAnyCampground_ByItsFacilityID` |
| 9 | Duplicate names collapse | 9 / 2 | `Success_ReportsEveryStepInOrder_ClosesTheWindow_AndReturnsZero`, `TheFilter_MatchesWordsAndExactIDs` … |
| 10 | Campgrounds without a location disappear | 5 / 3 | `ATypedID_IsUsedOnlyIfItIsExactlyACampground`, `Success_ReportsEveryStepInOrder_ClosesTheWindow_AndReturnsZero` … |
| 11 | A typed partial ID accepted | 0 / 1 | `ATypedID_IsUsedOnlyIfItIsExactlyACampground` |
| 12 | File-name traversal allowed | 3 / 0 | `AnUnsafeOrReservedName_IsRefused` |
| 13 | Save As writes over an existing file | 2 / 3 | `SaveAs_NeverWritesOverAFile`, `SaveAs_WritesTheEditedSearchToANewFile` … |
| 14 | The conflict checks bypassed | 1 / 0 | `AFileChangedSinceItWasRead_IsNeverOverwritten` |
| 15 | The enabled toggle changes another setting | 0 / 1 | `TheForm_EditsTheSearch_AndTheSaveIsExact` |
| 16 | The validation bypassed | 9 / 3 | `AFileThatCantBeEdited_OpensReadOnly_WithItsReason`, `ASearchFile_OpensWithEverySetting` … |
| 17 | A campsite availability request when the tab is shown | 0 / 1 | `TheSearchesTab_NeverSearchesOrShowsResults` |
| 18 | An availability-result control in the Searches tab | 0 / 2 | `TheSearchesTab_HasAccessibleNames_AndAKeyboardOrder`, `TheSearchesTab_NeverSearchesOrShowsResults` |

(14 changes the save path CM5 and CM6 share; CM5's `ConfigEditSessionTests` catches it. 4 changes the shared deserializer, the bounded loader's, temporarily for the control only.)

**Linux real-window review** [V] (as in §AG and §AH: a nested `kwin_wayland --virtual` session with its own D-Bus and a rootful Xwayland, nothing on the live desktop; a scratch launcher with the production `AppBuilder` chain (`App`, `UsePlatformDetect`, `LogToTextWriter(Warning)`, `UseReactiveUI`) and the real startup (`Load.GetConfig`) on an isolated `XDG_DATA_HOME` with the extracted shipped database; the window's own controls and Avalonia's own Tab navigation; `import` captures). Search files: one with a byte-order mark, CRLF and comments, a malformed one, a disabled one, a `.bak` and a `.yml`.
- Ready after 788 ms; the Searches tab shown (files and 4,966 campgrounds read) in 421 ms. Listed in order: `Alpha.yaml`, `Broken.yaml` (read-only, "can't be edited safely here: The document is not valid YAML"), `disabled.yaml`; the `.bak` and `.yml` not listed. Tab order from the tab header: the file list, New search file name, Create search, Look for changes on disk, Run this search, Name, Find a campground, the campground list, the campground ID, Use this ID, Dates to search, Days or months ahead, the dates' rows.
- Editing: the name, the toggle (its Info at the check box), a date row added; the filter "unnamed" found the 8 unnamed campgrounds, "BLACK ROCK" four entries with locations, IDs and parks (two of them the same name in different places); the typed ID `23244` refused, `232445` chosen (WATCHMAN CAMPGROUND — SPRINGDALE, UT); the preview showed exactly the changed lines (the new date quoted); saved: the byte-order mark, CRLF and every comment kept, `Alpha.yaml.bak` written.
- Save As "Alpha copy" created the file and left `Alpha.yaml` byte for byte; "alpha COPY" refused (differs only in case). New search "../escape" refused; "My trip" created disabled and opened. A change on disk while editing opened the conflict card ("in other settings than yours"); Apply my changes and Save kept both. Revert restored the field. Dark and Light at 720×540: the column 664 wide in a 720 window, the campground list and its IDs readable. No availability or result text. Exit code 0; no Avalonia warning (the nested session's missing AT-SPI accessibility bus aside). Screenshots reviewed: the list and editor, the picker, the preview, the conflict card, Dark and Light at 720×540; the folder warning was colored as an error, unlike CM5's warnings (text "Warning:" in the normal color), and was corrected before the commits.
- The GUI smoke test of the Release build in the same kind of session: every step passed, `search-probe=pass … permissions=640`, `searches-tab=yes files=0 campgrounds=4966`, only `SMOKE` lines, accepted by `check_gui_smoke_report`.

**Windows and macOS** [V]: the pull request's automatic Release dry run (path filter `CampertronGUI/**`; nothing dispatched or rerun), run 43755784996 on `44ea737` (PR #61): success; 16 packages, the Docker build and its container smoke tests, the assembly; publishing skipped. Its native smoke tests ran each run's own published GUI with the new search probe:
| Platform | `search-probe` | `searches-tab` | `settings-probe` | parks | shutdown |
|---|---|---|---|---|---|
| linux-x64 | pass … permissions=640 | yes files=0 campgrounds=4966 | pass … 640,600 | 522 | clean exit-code=0 |
| win-x64 | pass … permissions=access-rules-kept | yes files=0 campgrounds=4966 | pass … access-rules-kept | 522 | clean exit-code=0 |
| win-arm64 | pass … permissions=access-rules-kept | yes files=0 campgrounds=4966 | pass … access-rules-kept | 522 | clean exit-code=0 |
| osx-x64 | pass … permissions=640 | yes files=0 campgrounds=4966 | pass … 640,600 | 522 | clean exit-code=0 |
| osx-arm64 | pass … permissions=640 | yes files=0 campgrounds=4966 | pass … 640,600 | 522 | clean exit-code=0 |

("pass created=disabled auto-run=toggled-alone lists=kept comments=kept line-endings=crlf bom=kept save-as=created,no-overwrite campground-id=exact": in a temporary folder, New search created a disabled search; in a search file with a byte-order mark, CRLF, comments and mode 0640 the enabled toggle changed `autoRun` alone, then a reordered and extended list (its comments moving with their items), the name and a campground chosen by exact ID from the installed data (a partial ID refused) were saved byte for byte, with exact backups of the same mode or access rules; Save As wrote the new file and refused a name differing only in case; no temporary file was left.) The CI job `Windows settings files (CM1 to CM3, CM5, CM6)` on `windows-2025`, run 43755784966: 414 tests, 406 passed, 8 skipped (Unix-only), 0 failed; `SearchFileSessionTests` 30 passed and 1 skipped (`APermissionChange_KeepsTheEdits`, Unix permissions), `SearchFilesTests` 51 passed (the case-twin part of the discovery test runs only on a case-sensitive file system). Not run: a screen reader, a physical keyboard, the Searches tab's appearance on Windows and macOS (the smoke tests take no screenshots), process interruption during a GUI save. Container: no container or console behavior change (the start's example search keeps its bytes, `SettingsBootstrapTests`; the console reads a GUI-saved search, `SearchFilesTests`); the dry run's container smoke tests passed; not run separately.

**Isolation** [V]: temporary folders only; the real `~/.local/share/CampertronConfig` and `CampertronCache` were unchanged; the shipped `RecreationDotOrg.db.br` is unchanged.

**Regression checks for later changes**: `SearchFileSessionTests`, `SearchFilesTests`, `CampgroundPickerTests`, `SearchEditorTests`, `SmokeTestTests`, the CM1 to CM5 suites and the release-policy tests; for a change to the session, the names, the picker, the list rows or the smoke probe, the negative controls above.

### AJ. GS1: the host-neutral search service (decision D45, branch `refactor/search-core`, 2026-11-15)
Library and tests only: `function/RecDotOrg/search/CampsiteSearch.cs` and `SearchHost.cs` (new), `class/Base/SearchRunModel.cs` (new), `AvailabilityApi.cs` (the host branch in `ProcessMonth`, the month's header after its fetch, cancellation checkpoints, `ConsoleDownload`, `HasDatesDuringThisMonth` internal, the occupancy metadata), `Cache.cs` (`PrepareCampground`, the token and `MayBuild` overloads), `class/Base/ConsoleConfig.cs` (`RunningData.Host`, `OutputMetadata.MinOccupancy`/`MaxOccupancy`); tests `CampsiteSearchTests`, `SearchHostTests`, `SearchTestSupport` (new), `NoLiveNetwork` (new), `LoadTests` (adapted). No GUI, workflow, release-policy, project-file, `global.json`, dependency or database change. Linux x64 (CachyOS), SDK 11.0.100 / runtime 11.0.0. What changed: D45, business-rules §19, architecture.md §Interactive search service.

**Facts established before the design** [V] (the R5 investigation's scratch prototype on `9f7f161`, and this branch): the parser's answers of other shapes (TD-55); after `AvailabilityApi.ExitProcess` returns (the test seam) a blocked month never reaches `SiteData`, so a search waiting for it would poll forever (hence the shared months' signal); `FilterData` reads `Camp.History` only when `OutputTo == Email`; opening a missing `RecreationDotOrg.db` with the context's connection string creates an empty file (`Data Source=…`, the default `ReadWriteCreate`), so the service never builds a cache without the database; filtering one month of every Watchman campsite takes about 0.3 s, so the cancellation test's filtering is made heavier (an attribute filter of 3,000 values).

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release` and `-c Debug`, `--no-incremental` | 0 errors, **70 warnings** each |
| Warning identities vs `master` `9f7f161` built the same way (file, code, message) | 44 on both, none added or removed, Debug and Release equal; 42 moved lines (6 in `ConsoleConfig.cs`, 27 in `AvailabilityApi.cs`, 6 in `Cache.cs`, 3 in `LoadUnitTests.cs`). (An extracted `Load.SearchOne` for the console's loop moved one CS8604 to a new call; it was dropped, and the console's loop is byte-identical.) |
| Each code commit alone (export, Release, `--no-incremental`) | `e90ad8f` (production): 0 errors, 70 warnings, 856 + 103 tests pass (the two `RunConsoleSearchTest*` excluded, to avoid the old live request); `7aadcf4` (tests): the full run below |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build` (on `7aadcf4`, inside the network namespace of the next row) | **1,017 tests: 914 `CampertronUnitTests` (894 passed, 20 skipped: the Windows-only tests) + 103 `CampertronGUITests`**, 0 failed. 56 new: `CampsiteSearchTests` 24, `SearchHostTests` 32 cases (23 tests). `LoadTests.RunConsoleSearchTest` answers its months through the seam. The CM1 to CM6 suites unchanged and green |
| The same run inside a network namespace with only a loopback interface (`bwrap --dev-bind / / --unshare-net`, the same user) | **1,017, 0 failed**: no test needs the network. The same namespace with `master`'s `LoadTests.RunConsoleSearchTest`: "the request to recreation.gov … failed (attempt 1, 2, 3: Network is unreachable); retrying in 30 seconds", aborted by a 90-second hang timeout (exit 1) |
| `bash .github/release-policy/test-release-policy.sh` | **337/337** (no policy or workflow file changed) |
| `check_curated_notes_dir`, `check_global_json`, `check_selected_sdk` (11.0.100), `check_package_policy` | pass |
| `dotnet list <project> package --vulnerable --include-transitive`, five projects | no vulnerable packages |
| Dependencies | none added; YamlDotNet stays 16.3.0 |

**Negative controls** [V]. Each a temporary edit of a scratch copy of the branch (never the working tree), built and run against the tests listed, then restored and checked by SHA-256 (a scratch script outside the repository):
| # | Mutation | Failing (of run) | For example |
|---|---|---|---|
| 1 | Result completeness: the typed results lose their last opening | 9 of 24 | `ASearch_FindsEveryOpening_OfEveryDateItAsksFor`, `ConsecutiveNights_AreTheConsolesRuns`, `Filters_AreTheConsolesFilters` |
| 2 | Date completeness: the last night of each month dropped | 2 of 24 | `ASearch_FindsEveryOpening…`, `ManyOpenings_AreAllThere_WithoutTruncation` |
| 3 | Only the first month a search needs is planned | 3 of 56 | `APartlyFailedSearch_KeepsItsCheckedMonths_AndNamesTheOthers`, `Progress_IsTruthful_AndInOrder` |
| 4 | Filter completeness: `FilterData` ignores `includeSites` (shared with the console) | 1 of 1 | `Filters_AreTheConsolesFilters` |
| 5 | History isolation: the run's configuration is Email | 1 of 2 | `CampHistory_IsNeitherReadNorWritten_AndEmailSettingsCantHideResults` |
| 6 | Cancellation during a request: the transport does not get the run's token | 1 of 1 | `CanceledDuringARequest_EndsPromptly` (the run never ended) |
| 7 | Cancellation during the wait before a retry: an uncancellable sleep | 1 of 1 | `CanceledDuringTheWaitBeforeARetry_EndsPromptly` |
| 8 | Cancellation during filtering: no checkpoint in a month's day and campsite loops | 1 of 1 | `CanceledWhileTheResultsAreFiltered_EndsPromptly`: 2.83 s from the cancellation to the end (the test passes in about 0.16 s in all) |
| 9 | Shared-month release: a failed month never releases its waiters | 1 of 1 | `AFailedSharedMonth_ReleasesEverySearchWaitingForIt` (the run never ended) |
| 10 | Retry limit: four requests per month | 9 of 9 | `EveryRetryableOutcome_EndsAfterThreeRequestsInAll` (each case) |
| 11 | Block: HTTP 429 an ordinary error status | 1 of 1 | `Http429_IsABlockToo` |
| 12 | Block: a block does not stop further requests | 1 of 1 | `TheBlockPage_StopsFurtherRequests_KeepsWhatCame_AndNeverExits` |
| 13 | Failed-month representation: a month that was not checked marked checked | 18 of 32 | `ACompleteFailure_IsAFailedRun_WithoutOpenings`, `AClientError_IsAskedOnce` |
| 14 | Raw exception leakage: issue messages built from the exceptions the run kept | 1 of 1 | `Problems_HoldNoValueOfTheAnswerTheExceptionOrTheFile` |
| 15 | Console writes: the campsite details prepared with the console's status line | 2 of 2 | `CanceledWhileTheCampsiteDetailsArePrepared_LeavesNoCacheThatLooksComplete` (the console capture), `TheService_NeverReaches…` (IL) |
| 16 | Process exit: a block ends the process as the console's does | 2 of 2 | `TheBlockPage…` (the recorded exit), `TheService_NeverReaches…` |
| 17 | Search-file rewrite: planning writes the file back | 2 of 3 | `Plan_ADisabledFile_IsSearchedExplicitly_AndStaysAsItIs`, `CampHistory_IsNeitherReadNorWritten…` |
| 18 | Real network access, console: `RunConsoleSearchTest` without the seam | 1 of 2, and `NoLiveNetwork` fails the run | "a test reached for the network …" |
| 19 | Real network access, interactive: a run with the production transport | 1 of 1, and `NoLiveNetwork` fails the run | `OneOpening` |

(Control 8 first passed: one month of every campsite filters in about 0.3 s, under the one-second bound. The test was made heavier before the commits, as above.)

**Compatibility of the console path, `master` against the branch** [V]. A scratch harness (not in the repository) compiled once against the library of `master` `9f7f161` and once against this branch, run with isolated data folders: Watchman's and Mather's campsite details from the bundled database, two months held in memory (about 30 % available, the same in both builds), nine searches (single nights, weekends, a ten-campsite subset, two and three nights, Mather two weekend nights, the example's filters, `consecutiveDays: 0`, Mather single nights; 16,368 openings), each through the console's `GetAvailabilitiesByCampground` with Console, HtmlFile, Email (an empty `Camp.History`) and UnitTest output, in en-US and de-DE. **All 109 outputs are byte-identical**: every value stream (kind, write type, color, text, the metadata both builds have), every run's results page (`ResultsHtml.Render`), every search's Email page and every console rendering (`ConsoleResults.Render`, with colors). For example (en-US, SHA-256): the HtmlFile page of the run `5d6e4deed22949da74a99c305f5470640becb581727b57fffd2325fc5f52d7a5` (5,374,194 bytes), the console rendering `1cf7f2164537edc6890ab26f3364efd0cb2588f7e0c4edc084b301d3ed3d02d4`, the Email pages `78f9c837f34942396f9741556ee7c52724f286723cf6b8fcf6ccaef7425e9a24` and `e306820e8e52cc8db37fb8c0c6055dd9f7fe8e8aabc08784d6dd693cc5bb3a11`.

**Parallel versus isolated** [V] (the same harness, as §Z): every search in one shared run in file order, in reverse order and 100 times in parallel, for HtmlFile and Email output (204 runs on each build): **0 differences** from each search alone on `master` and on the branch. The branch's interactive service: its typed openings equal the openings of the HtmlFile streams for all nine searches (0 mismatches, value for value and as campsite-and-night sets), and 20 interactive runs of all nine searches give each search exactly its result alone (0 differences).

**Container** [V]. Local images built from exports of `master` `9f7f161` and of this branch (`docker build -f CampertronConsole/Dockerfile`, Docker 29.9.3, amd64; never pushed; images, containers and container-owned scratch files removed afterwards), `--network host`, a local HTTPS stand-in for www.recreation.gov behind `HTTPS_PROXY` (it answers `CONNECT` for that host only, terminates TLS with a throwaway CA given in `SSL_CERT_FILE`, `SSL_CERT_DIR=/nonexistent`, generates months from real Watchman campsite IDs of the bundled database with about 30 % availability, or the block page; it never connects anywhere) and a local SMTP stand-in with STARTTLS (stores messages, relays nothing); `/config` and `/cache` bind-mounted from scratch folders; one Watchman search (ten nights two months ahead), `outputTo: Email`, a dummy SMTP password used as a sentinel:
- **First start and the Email loop**, both images side by side: "Preparing campground data (first start)...", the database extracted (226,471,936 bytes), "Preparing campsite details for WATCHMAN CAMPGROUND...", the search listed, "Check 1: 1 search, 575 new openings; sending 1 email.", "Email sent: Watchman ten nights (575 openings)", "Check 2" and "Check 3: 1 search, nothing new."; `docker stop` exit 143. The two emails are identical (subject, sender, recipient, the HTML body and the attachment: the same bytes, SHA-256 `0809f062aaec41f86d330a2b071bb21b5a845d37460f7d52598685aec5aaf173`, 222,127 bytes), and so are the `Camp.History` trees (587 entries, 575 markers).
- **The console's blocked path**, both images: "Error: Blocked by API for exceeding request limit, try again later (WATCHMAN CAMPGROUND, 2027 January). Campertron waits 5 minutes and then exits." at 23:35:55.9, then the real wait and exit code **1** at 23:40:55.9 (300.0 s), unchanged.
- **All runs**: 0 control bytes in `docker logs`; the SMTP password nowhere in a log or a message; only requests through the stand-in.

**Isolation** [V]: temporary folders and scratch copies only; the real `~/.local/share/CampertronConfig` and `CampertronCache` were compared before and after by a metadata fingerprint (path, type, size, time, mode of every entry; contents not read): 700 entries, identical before and after the work on the branch; the shipped `RecreationDotOrg.db.br` is unchanged (SHA-256 `700cbd00f0722ceca4b93e3ccb29bce17a4d6c584579cf504212d4b400a81e0b`, last changed in `07e786d`). No request reached recreation.gov: the tests, the harness and the containers used the transport seam, the console's download seam or the local stand-ins. No optional live check was run.

**Not run** [V]: Windows and macOS (a library-and-tests pull request does not match the release dry run's path filter, and the Windows CI job's filter does not include the new suites; the service has no platform-specific code; native evidence comes with GS2's GUI); arm64; a real mail provider; the GUI (not changed).

**Regression checks for later changes**: `CampsiteSearchTests`, `SearchHostTests` and `LoadTests`; the search suites of §Z; for the console path, the harness comparison above (every output byte-identical) and the parallel-versus-isolated comparison; for the network guard, a run inside a network namespace; for the interactive host, the negative controls above.

### AK. GS2: the GUI's Results tab (decision D46, branch `feat/gui-search-results`, 2026-11-15)
GUI, GUI tests and the release policy's smoke-report check only: `CampertronGUI/ViewModels/ResultsViewModel.cs`, `ResultRows.cs`, `ProgressGate.cs`, `Views/ResultsView.axaml(.cs)`, `RunProbe.cs`, `AssemblyInfo.cs` (new); `MainWindowViewModel.cs` (`CachePath`, `Results`, `PendingSearch`, the detach), `MainWindow.axaml(.cs)` (the sixth tab, `ResultsTabIndex`), `SearchesViewModel.cs` (`IsUnsaved`, a read-only query), `SmokeTest.cs`, `App.axaml.cs` and `Program.cs` (the bounded wait for a canceled search); tests `ResultsTestSupport`, `ResultsTabTests`, `ResultsBehaviorTests`, `ResultsViewTests` (new), `SmokeTestTests` and `GuiTestHelpers.AllTabs` (adapted); `.github/release-policy/release-policy.sh` (`GUI_SMOKE_KEYS`, `check_gui_smoke_report`) and `test-release-policy.sh`. No library, console, workflow, project-file, `global.json`, dependency or database change. Linux x64 (CachyOS), SDK 11.0.100 / runtime 11.0.0. What changed: D46, business-rules §20, architecture.md §GUI data flow.

**Facts established on the way** [V]: an Avalonia `TabItem`'s content is created when the tab is first shown (a test must select a tab before it inspects its view); a `Button` disabled by its command keeps `IsEnabled` true and reports `IsEffectivelyEnabled` false; a compiled binding through a null intermediate (`SelectedResult.Rows`) logs a binding warning even with a `FallbackValue`, so the view binds non-null properties of its own (`SelectedRows`, `SelectedEmptyText`); a fixed `AutomationProperties.Name` on a live status text would hide its changing text from a screen reader, so the status is named by its text; the headless platform's font is wider than the Linux desktop's (the three selection buttons wrap at 720 there), which is why the tab has an outer scroll for a too-low window; .NET's string hash is randomized per process (the review harness's stand-in data uses a stable hash). **Found by the real-window review and fixed before the commits:** the progress details line (stage, counts, elapsed time) never appeared while a run went on, because its visibility was never raised (the headless tests then checked only the view model); the details are now raised with their text and shown only while a run goes on, and `ResultsViewTests.TheProgressBar_HasANameAndAValueWhenItsTotalIsKnown` checks the line in the window (negative control 19). Also from the review: the loop is shown as stored (the data already says "Loop D"; the page and the console do the same), the run's summary is the status line only (it was shown twice), the summary lists the status, counts and months before the campground, and the file list gets smaller once there are results.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Release` and `-c Debug`, `--no-incremental` | 0 errors, **70 warnings** each |
| Warning identities vs `master` `51335f0` built the same way (file, code, message; and with positions) | 44 on both, none added or removed, Debug and Release equal; **no moved line** |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **1,058 tests: 914 `CampertronUnitTests` (894 passed, 20 skipped: the Windows-only tests) + 144 `CampertronGUITests`**, 0 failed. 41 new: `ResultsTabTests` 17 cases (15 tests), `ResultsBehaviorTests` 15 (14), `ResultsViewTests` 9 (8); `SmokeTestTests` (7) adapted. GS1's suites, the CM1 to CM6 suites and every earlier GUI test unchanged and green |
| `bash .github/release-policy/test-release-policy.sh` | **348/348** (337 before; 11 new smoke-report cases: a missing, not shown or other-folder Results tab, an unbound Cancel, a missing or failed run probe, no openings, no prompt cancel, a block that ended the app, the network used, the probe before the tab) |
| `dotnet list <project> package --vulnerable --include-transitive`, five projects | no vulnerable packages |
| `git diff --check`, new files | no whitespace error, no trailing whitespace, LF only |
| Dependencies, project files, workflows, `global.json`, the shipped database | unchanged (`RecreationDotOrg.db.br` SHA-256 `700cbd00f0722ceca4b93e3ccb29bce17a4d6c584579cf504212d4b400a81e0b`) |

**Network.** No test reaches recreation.gov: every run in the tests answers through `TestGov` (an in-memory transport through `SearchHostOptions.Transport`) or a test `Run` seam, the campsite details are fixture cache files (`RunProbe.WriteCampground`); `ResultsViewTests.OnlyTheResultsTab…` counts zero HttpClient requests during a run; the library suite keeps GS1's `NoLiveNetwork` guard. The smoke probe and the review harness use the same kind of fixture transport.

**Negative controls** [V]. Each a temporary edit of a scratch copy of the branch (never the working tree), built and run against `ResultsTabTests`, `ResultsBehaviorTests`, `ResultsViewTests`, `SmokeTestTests` and `CampgroundIdentityTests` (67 tests), then restored and checked by SHA-256 (a scratch script outside the repository); the restored copy passed 67/67:
| # | Mutation | Failing (of 67) | For example |
|---|---|---|---|
| 1 | Dirty-file protection: unsaved Searches-tab changes don't block a file | 1 | `AFileWithUnsavedChanges_IsBlocked_UntilItIsSavedOrReverted` |
| 2 | autoRun preservation: Start sets `autoRun: true` in the chosen files | 4 | `ADisabledFile_ChosenExplicitly_IsSearched_AndNothingIsWritten`, `EmailSettingsAndCampHistory_AreNeitherUsedNorChanged` |
| 3 | Search-file rewrite: listing writes each file back unchanged | 4 | the same two, `ABlockedRun_…` (both cases) |
| 4 | Repeated Start: no guard against a run in progress | 1 | `WhileARunGoesOn_StartIsGuarded_TheChoiceIsLocked_AndCancelIsSafe` |
| 5 | Stale progress: no operation check and a gate that never closes | 1 | `AnEarlierRunsProgressEndAndTicker_AreIgnored` (first passed: re-running the stale job moved the gate's last publication, so the late snapshot waited on a timer the test never fired; the test now advances the clock and fires it) |
| 6 | Stale results: an earlier run's end is applied | 1 | `AnEarlierRunsProgressEndAndTicker_AreIgnored` |
| 7 | Cancellation: Cancel doesn't cancel the token | 7 | `Cancel_EndsTheRunPromptly` (request, retry, filtering), `WhileARunGoesOn…`, `TheProgressBar…`, the smoke probe (`Success_ReportsEveryStep…`, `UncleanLifetimeExit_Fails`) |
| 8 | Progress flooding: a job queued for every snapshot | 5 | `Progress_IsThrottled_ToTenASecond_WithOneJobAtATime`, `TheEnd_IsShownAtOnce_AndStopsTheProgressTimer`, `ClosingTheWindow_…`, `ABlockedRun_…` |
| 9 | Progress flooding: no interval between publications | 2 | `Progress_IsThrottled_…`, `TheEnd_IsShownAtOnce_…` |
| 10 | Failed-month representation: months not checked shown as checked | 3 | `EachEnd_IsToldApartByItsText`, `ABlockedRun_…` (both cases) |
| 11 | Block cooldown: a blocked run doesn't hold Start | 4 | `ABlockedRun_…` (both cases), the smoke probe |
| 12 | Link validation: any https link accepted | 1 | `BookingLinks_AreCheckedBeforeUse` |
| 13 | Direct GUI HTTP: an `HttpClient` in the Results tab | 2 | `OnlyTheResultsTab_UsesTheSearchService…`, `CampgroundIdentityTests.TheLookup_StartsNoCampsiteSearchAndNoWebRequest` |
| 14 | Console-pipeline dependency: a `Load.RunConsoleSearch` reference in the Results tab | 2 | the same two |
| 15 | Accessibility: Start loses its name and the status its live region | 1 | `EveryControl_HasAnAccessibleName_AndStatesAreSaidInWords` |
| 16 | Virtualization: the result list a plain `StackPanel` | 1 | `TwelveThousandOpenings_CreateOnlyTheVisibleRows` |
| 17 | Window close: `Detach` doesn't cancel the run | 1 | `ClosingTheWindow_DuringAStalledRequest_CancelsTheRunAndIgnoresItsEnd` (the run went on) |
| 18 | Smoke clean shutdown: the smoke test never closes the window after the run probe | 2 | `Success_ReportsEveryStepInOrder_ClosesTheWindow_AndReturnsZero`, `UncleanLifetimeExit_Fails` |
| 19 | Progress details: their visibility never raised (the review's finding) | 1 | `TheProgressBar_HasANameAndAValueWhenItsTotalIsKnown` |

**GUI smoke test, Linux, real window** [V]: `CampertronGUI --smoke-test` (Debug build) in the nested KWin session (below) with a new, empty `XDG_DATA_HOME`: exit 0, empty standard error, every line `SMOKE …`, the report accepted by `check_gui_smoke_report`, among them `results-tab=yes files=0 paths=startup start=bound cancel=bound` and `run-probe=pass planned=1 completed=openings:20 results=bound canceled=prompt blocked=no-exit cooldown=on network=none`, then `closing=programmatic=yes reason=WindowClosing`, `shutdown=clean exit-code=0`, `result=pass`.

**The pull request's automatic Release dry run** [V] (PR #63, run 43783278584, `pull_request`, synthetic version `0.0.0-dryrun.38`, built from the pull request's merge commit `c09bda7`; never dispatched or rerun): every job succeeded (verify with the release-policy tests, the 16 packages, the Docker build, assemble, and the native smoke tests on linux-x64, win-x64, win-arm64, osx-x64 and osx-arm64); `Publish image to GHCR` and `Create GitHub release` were skipped, as on every pull request. On each of the five platforms the published GUI's `--smoke-test` reported `results-tab=yes files=0 paths=startup start=bound cancel=bound`, `run-probe=pass planned=1 completed=openings:20 results=bound canceled=prompt blocked=no-exit cooldown=on network=none`, `shutdown=clean exit-code=0` and `result=pass`: the Results tab, its view model with the startup's folders, the fixture run, its typed results bound to the list, the cancel during a stalled request, the rate-limited run without process exit and with Start held, and the clean shutdown, natively, with Avalonia and its native libraries loaded. The pull request's CI run 43783278616 (both jobs, including `Windows settings files (CM1 to CM3, CM5, CM6)`) succeeded on the same head `8b06544`.

**Real-window review** [V] (Linux x64, KDE Plasma's KWin as a nested `kwin_wayland --virtual` session (2600×1700) under `dbus-run-session` with a bus configuration without service activation, a rootful `Xwayland :9` inside it; the real GUI started by a scratch harness outside the repository with the production `AppBuilder` chain (`UsePlatformDetect`, `UseReactiveUI`), an isolated `XDG_DATA_HOME` under `/tmp` (the bundled database extracted there) and a main view model whose Results tab answers through a **local stand-in transport** (months generated in memory for the bundled database's campsites with a stable pattern; never recreation.gov); keys through Avalonia's platform input (`TopLevel.PlatformImpl.Input` with `RawKeyEventArgs`), captures with ImageMagick's `import -window <XID>`; `Program.Main`'s bounded wait mirrored):
| Scenario | Seen |
|---|---|
| Light and Dark; 720×540, 1024×768, 1200×900, 1600×1000, 2400×1500 | Start, Cancel and the status always visible; the list of files compacts after a run; the results card with the list and the actions; capped column on wide windows; no overlap after the fixes above |
| Several openings (two Watchman searches, one disabled) | "Finished: 313 openings found in 2 searches." (183 openings at 132 campsites on 10 nights; 61 campsites with two nights in a row), nights in date order, campsite rows with only their populated details, the disabled search listed "Not run at startup" and not run; the same counts in every run |
| No availability | "Complete: no matching availability in the months checked", "No campsite matched this search in the months checked." |
| Incomplete (the second month answered HTTP 503 three times) | "Incomplete: some months were not checked; 272 openings found in the others", "Months checked: November 2026. Not checked: December 2026 (recreation.gov answered with an error).", "Error: … (December 2026, HTTP 503, 3 requests)" |
| Blocked (HTTP 429 for Mather after Watchman was answered) | the run stopped at once, Watchman kept "Complete: 22 openings found", Mather "Stopped: recreation.gov limited the requests…", "Start is available again in 5:00.", Start disabled, the window open |
| 17,280 openings (Watchman, 90 days, every night open) | the run and its presentation in 2.5 s; 17,370 rows, 7 realized after scrolling to the end in 100 ms |
| Long names, a file with an error, a file with a warning (720×540) | names trimmed with tooltips, "Can't be searched: … Error: The start and end dates of searchValueDates are the same; … (line 8)" in words, the warning shown, the error file's check box disabled |
| Cancel during a stalled request (keyboard: Enter on Cancel) | progress "Checking availability at recreation.gov · Campsite details: 1 of 1 campground ready · Availability: 0 of 1 month request finished · Elapsed 0:01", then "Canceled. …" 143 to 148 ms after Enter |
| Window closed during a stalled request | the lifetime ended 16 ms after the close; the canceled run had ended within 2 ms of the bounded wait; process exit code 0 |
| Keyboard only | Tab from the tab header: Start, the three check boxes, Select enabled, Select all that can run, Clear selection, back to the Results header (Cancel skipped while nothing runs); Enter toggled a check box and started the run; after it, Tab from the result choice into the list, Down chose an opening, Tab to Open booking page and Copy campsite ID, Enter copied "342" through the real clipboard ("Copied the campsite ID 342."), Tab wrapped to the header after Copy booking link |

The README screenshot `docs/ss9.png` (1200×900, Dark) is the review's capture of the stand-in run (two Watchman searches, site D015 chosen); no real configuration is in it.

**Isolation** [V]: temporary folders and scratch copies only; the real `~/.local/share/CampertronConfig` and `CampertronCache` were compared before and after by a metadata fingerprint (path, type, size, time, mode of every entry; contents not read): 700 entries, identical (SHA-256 of the listing `8d8d6420…` both times). Search-file and `Camp.History` bytes were compared in the tests' own folders (`ADisabledFile_ChosenExplicitly…`, `EmailSettingsAndCampHistory_…`: every settings file, search file and marker unchanged after runs); the real folders were never written. No request reached recreation.gov.

**Not run** [V]: the GUI on Windows and macOS other than the release dry run's smoke test above (the Results tab's appearance, keyboard use, screen readers, the booking-link launcher and the clipboard there: open question #29); arm64 Linux, linux-arm and win-x86 (built, not run, as for every release); a live recreation.gov search (not required, not run).

**Regression checks for later changes**: `ResultsTabTests`, `ResultsBehaviorTests`, `ResultsViewTests`, `SmokeTestTests`, `CampgroundIdentityTests`, GS1's suites and the release-policy tests; the negative controls above on the changed part; for layout, accessibility or keyboard changes the nested-session review (Light, Dark, 720×540 to 2400×1500, keyboard only, a stalled cancel and a close during a stalled request).

### AL. S1: the console menu's loop and the documentation reconciliation (decision D47, branch `fix/console-menu-loop`, 2026-11-15)
Library, one new test class and documentation: `CampertronLibrary/function/Base/Menu.cs` (`WriteMenu`, `ProcessInput`; 16 lines added, 18 removed), `CampertronUnitTests/function/Base/MenuKeysTests.cs` (new), `CLAUDE.md` and the files of `docs/ai-context`. No other production file, no GUI, workflow, release-policy, project-file, `global.json`, dependency or database change. Linux x64 (CachyOS), SDK 11.0.100 / runtime 11.0.0. What changed: D47, business-rules §12, technical-debt.md §Reconciliation after GS2, TD-12 and TD-54.

**Why** [V]. The reconciliation of the remaining work after GS2 (2026-11-15, read-only on `master` `6451739`) measured TD-12: `Menu.ProcessInput` called `Menu.WriteMenu` for every key (the arrows and, through its `default:` case, every other key), and `WriteMenu`'s loop called `ProcessInput`, so nothing returned. The Release console of `6451739`, on a pseudo-terminal with isolated data, pressing Up at Search: "Stack overflow. … Repeated 17231 times: Menu.WriteMenu / Menu.ProcessInput", SIGABRT, with the default 8 MB main-thread stack; after 4,202 key presses with `ulimit -s 1024`. TD-12 was approved for preview.2 by the 2026-11-14 audit; it is not a blocker of preview.2 (it needs thousands of key presses) and is fixed now because it is small and no implementation stage follows preview.2.

**The change** [V]. The loop in `WriteMenu` draws the options (`WriteOptions`), clears the six lines below, places the cursor four lines up, reads a key and calls `ProcessInput`; `ProcessInput` sets `_location` for an arrow and returns, returns for a key the menu does not use, and runs the selected option for Enter as before. The eight recursive calls in the arrow cases and the one in the `default:` case are gone; the console calls and their order are the same. Still as before: each Search, Refresh and View Config starts one nested menu (`Load.PostProcess` → `Menu.Init`, `Menu.Init`, `Menu.Draw`); `Load.cs` is untouched.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Debug` and `-c Release`, `--no-incremental` | 0 errors, **70 warnings** each |
| Warnings vs an export of `master` `6451739` built the same way | the 70 lines identical in file, line, column, code and message (44 identities by file, code and message), Debug and Release; the one `Menu.cs` warning (CS8602, line 18) is above the change and did not move |
| `MenuKeysTests` | 15 cases (5 tests), passed |
| The console suites | 122 passed: `ConsoleUiTests` 41, `ConsoleResultsTests` 22, `ConsoleOutputTests` 5, `ConsoleFailureTests` 36, `ConsoleOutputGoldenTests` 3 (unchanged), `MenuKeysTests` 15 |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **1,073 tests: 929 `CampertronUnitTests` (909 passed, 20 skipped: the Windows-only tests) + 144 `CampertronGUITests`**, 0 failed. 15 new, nothing else changed |
| The same inside a network namespace with only a loopback interface (`bwrap --dev-bind / / --unshare-net`), the no-network guard included | 1,073 tests, 0 failed, in three complete runs, the last on the final sources; no test needs the network. A first run had one failure in a GUI test (below) |
| `bash .github/release-policy/test-release-policy.sh` | 348/348 (no policy or workflow file changed) |
| `check_global_json`, `check_selected_sdk` (11.0.100), `check_curated_notes_dir`, `check_package_policy` | pass |
| `dotnet list <project> package --vulnerable --include-transitive --format json`, five projects | 0 vulnerable packages in each |

**One failure seen once, not caused by this change** [V]. In the first full run inside the network namespace `SmokeTestTests.MissingRenderedFrame_TimesOut` (a GUI test) failed after 4 ms, before its own code ran: Avalonia's headless test session threw "The calling thread cannot access this object because a different thread owns it" while it set up the test's application (`HeadlessUnitTestSession.EnsureIsolatedApplication` → `AvaloniaHeadlessPlatform.Initialize` → `DefaultRenderLoop.Add`). The other 143 GUI tests and the 929 library tests of that run passed. It did not recur: three later complete runs inside the namespace and two outside it passed, and so did the GUI suite alone, four times inside the namespace and four times outside it on the branch and four times inside it on the export of `6451739`. The branch changes no GUI code and no GUI test. [I] A race in the headless session's start-up when both test assemblies start at the same time; not investigated further; if it returns, it belongs with the GUI tests, not with the menu.

**Negative controls** [V]. Temporary edits of `Menu.cs` in a scratch copy of the branch (never the working tree), built and run against `MenuKeysTests`, then restored and compared byte for byte with the branch's file:
| # | Mutation | Failing (of 15) |
|---|---|---|
| 1a | One recursive call restored: Up at Search calls `WriteMenu` again | 4: `ProcessInput_NeverCallsWriteMenu_AndTheLoopInWriteMenuReadsTheKeys` (the IL), `OneMillionKeyPresses_OnASmallStack_NeverGoDeeper`, `AnArrowKey_…(Search,UpArrow,Search)`, `TheArrowKeys_WalkThroughTheFourOptionsInOrder_BothWays` |
| 1b | The recursive call restored in the `default:` case | 6: the IL test, the million-key test and the four `AKeyTheMenuDoesNotUse_ChangesNothing_AndReturns` cases |
| 2 | Down at Search leaves the selection unchanged | 2: `AnArrowKey_…(Search,DownArrow,Refresh)` and `TheArrowKeys_WalkThroughTheFourOptionsInOrder_BothWays` |

Control 1a first failed only three tests: the million-key test's first key mix never pressed Up at Search, so it missed that call. Its round of 16 keys now presses Up, Down and a key the menu does not use at every one of the four options, and both variants of control 1 fail it. (In the test host a restored call fails with an exception from the console, not with a stack overflow, because there is no terminal: the process-level proof is the key-press run below.)

**The terminal stream, before and after** [V]. Release consoles built from exports without `.git` of `6451739` and of the branch (so both report the same version, `CampertronConsole 1.0.0`), run one after the other on the same isolated data folder (the bundled database already extracted, one search file with `autoRun: false`) by a scratch pseudo-terminal driver outside the repository, which answers cursor queries, sends each key after the output went quiet and records every byte the console writes. The session, 32 steps: Down ×4, Up, `x`, Left, Tab, Space, Up ×4, Right, Down ×2, Enter (View Config), a key, Up ×2, Enter (Search: the no-enabled-search warning, "Running 0 searches:", "Done: 0 searches, no matching availability."; no request), a key, Down, `x`, Down ×2, Enter (Exit).
| Terminal | Bytes | SHA-256 of the stream, baseline = branch | End |
|---|---|---|---|
| 24 × 80 | 28,637 | `b620b9478c2db82157407f83fd5a41ae1b2a022e94ba14b3e336b52c2505cdb9` | exit code 0 |
| 40 × 100 | 33,637 | `979197ddbf513599b09ce0a5861e36e5284afb058070122c2b3dfafd1e851971` | exit code 0 |
| 30 × 160 | 48,634 | `c4a396a629a3d7731eedc92298d64c0aa074917d8ec2b16f59398abb676b9d61` | exit code 0 |

**Byte-identical at all three sizes** (`cmp`). In the stream: 25 drawings of the menu, no form feed, no `ESC c`, no `ESC[3J`, the cursor shown again at the end.

**Key presses** [V] (the same two builds and data; batches of 64 keys in the mix Up, Down, `x`, Down, Up, Left, Up, Down; never Enter until the end; `ulimit -c 0`):
| Build | Main-thread stack | Result |
|---|---|---|
| `6451739` | 1 MB (`ulimit -s 1024`) | "Stack overflow", SIGABRT after 4,294 drawings of the menu |
| `6451739` | 8 MB (the default) | "Stack overflow", SIGABRT after 18,514 drawings |
| branch | 1 MB | 250,048 key presses, 250,052 drawings (the first, one per key, three for the way to Exit), then Exit: exit code 0 |
| branch | 8 MB | the same: 250,048 key presses, exit code 0 |

**Menu, redirection and signals of §Y, on the branch** [V] (the baseline build gave the same result in each signal case): Ctrl+C at the menu and SIGINT end the process by SIGINT (a shell reports 130) with "Stopped." and the cursor shown again; SIGTERM ends it by signal 15 (143) and SIGHUP by signal 1 (129); with everything redirected, and with standard output piped, "Error: the Campertron menu needs an interactive terminal. …" on standard error, exit code 1, 0 bytes on standard output and no escape sequence in any stream; `--version` gives the same bytes from both builds. Observed in both builds and not changed here: after SIGTERM or SIGHUP at the menu the recorded stream does not end with the sequence that shows the cursor again (not investigated; business-rules §12 names Exit, a failure and Ctrl+C).

**Container** [V] (`docker build -f CampertronConsole/Dockerfile` from an export of the branch, Docker 29.9.3, amd64, `--network none` for the runs; never pushed, the image removed afterwards): without `/config`, "Error: Config path does not exist" with the mount advice, exit code 1, nothing on standard output; with empty `/config` and `/cache` (the samples and the database are created, `outputTo: Console`), "Error: Unsupported output type in container. …", exit code 1; `--version` two lines on `.NET 11.0.0 (X64)`; 0 control bytes. The first build attempt failed in its package restore when the network connection dropped; the second succeeded.

**Documentation corrected** [V] (each checked against the code): `YamlDocument` and `ConfigFileReplace.Replace` have two callers, `Yaml.GeneralConfigReplaceYaml` and the GUI's `ConfigEditSession` (CLAUDE.md, architecture.md, business-rules §13 and §14, repository-map.md); the GUI saves `Email.yaml` and search files, so "never replaces a search file" and "no current path replaces `Email.yaml`" are gone (§14); the GUI shows the validation's diagnostics (CLAUDE.md, architecture.md, business-rules §15); the campground picker lists the 22 campgrounds no lookup tab lists, and CM6 is implemented (§16); the native smoke jobs have run on D34 code (§12); feature-inventory.md no longer says there are no validator classes, that the Email loop validates nothing, that search files are found after `ToLower()` or that GUI configuration editing is not started; two curated notes fragments are tracked and the release-policy suite has 348 tests (repository-map.md); 70 warnings (technical-debt.md TD-30); the Windows CI job's name and suites (CLAUDE.md, technical-debt.md).

**Isolation** [V]: scratch folders and scratch exports only; every console, test and container run used its own data folder. The real `~/.local/share/CampertronConfig` and `CampertronCache` were compared before and after by a metadata fingerprint (path, type, size, time, mode; contents not read): 310 and 390 entries, identical. The shipped `RecreationDotOrg.db.br` is unchanged (SHA-256 `700cbd00f0722ceca4b93e3ccb29bce17a4d6c584579cf504212d4b400a81e0b`). No request reached recreation.gov. The deliberate stack-overflow runs of the baseline build leave entries in the system's `coredumpctl` journal even with `ulimit -c 0` (no core file is stored).

**Not run** [V]: the menu on Windows and macOS (open question #38; the release workflow's native smoke tests cover `--version` and the silent start only, and a pull request that changes library code, a test and documentation does not match the release dry run's path filter, so this change has no native run until the next pull request that does, or the pre-tag dry run); arm64; a real search from the menu (the session's Search has no enabled search file, so it makes no request).

**Regression checks for later changes to `Menu.cs`**: `MenuKeysTests` and the five console suites; the two negative controls above; the pseudo-terminal comparison of a scripted session with the build before (byte-identical) and a run of 200,000 or more key presses with a 1 MB stack.

### AM. S2: native smoke tests for linux-arm64, the arm64 container image and win-x86 (decision D48, branch `ci/native-smoke-arm64-x86`, 2026-11-15)
Workflow, release policy and its tests, and documentation: `.github/workflows/release.yml` (`smoke`, `native-smoke`, the new `container-smoke`, the publishing jobs' `needs`, the vulnerability scan), `.github/release-policy/release-policy.sh` and `test-release-policy.sh`. No application, test-project, project-file, `global.json`, dependency, `ci.yml` or database change (0 files outside `.github`, `docs` and `CLAUDE.md`). What changed: D48, §Release workflow.

**What lacked execution, and which evidence is which** [V, from `release.yml` and the dry runs up to §AL]:
| Artifact | Before S2 | Since S2 |
|---|---|---|
| linux-x64, win-x64, win-arm64, osx-x64, osx-arm64 archives; linux/amd64 image | run natively in every release run (R8, D32) | unchanged |
| linux-arm64 console and GUI archives | built and inspected (contents, runtime, native SQLite architecture, compiler provenance); never executed | run natively on `ubuntu-24.04-arm` |
| linux/arm64 container image | checked structurally in the archive; never executed | run natively on `ubuntu-24.04-arm`, from the validated archive |
| win-x86 console and GUI archives | built and inspected; executed once, in the temporary TD-35 validation of a .NET 10 build (§L) | run as 32-bit processes on 64-bit Windows (compatibility execution, not a native 32-bit system) |
| linux-arm (ARM32) archives | built and inspected | unchanged: no GitHub-hosted runner; not run |

The kinds of evidence, kept apart: **automated tests** (the 1,073 library and GUI tests; Ubuntu, with the settings suites on Windows) say nothing about a published archive on its platform; **build-only and structural checks** (all 16 archives and both images) show what a package contains, not that it starts; the GUI tests are **headless** (no platform window); the **native launch checks** of this section start the published executables: `--version`, a clean install that extracts the database, the GUI's built-in smoke test (a real window with a native handle, its data and its settings, search-editor and Results-tab probes, a clean close) and the container's start conditions; **interactive behavior** (the console menu, key handling, appearance, screen readers) is in none of them. S1's menu change (§AL) is in every binary these jobs start, and that they start is all these jobs show about it: the menu is never drawn in a workflow, on any platform (open question #38).

**The route** [V]: no existing job could run the missing artifacts, so a workflow change was required; a pull request that changes `release.yml` gets the Release dry run automatically (its path filter), so the pull request's own runs are the native evidence. Nothing was dispatched or rerun by hand.

**The runner** [V, GitHub's "Supported runners and hardware resources" page, read 2026-11-15]: the standard GitHub-hosted runners for public repositories include Linux arm64 with the labels `ubuntu-24.04-arm`, `ubuntu-22.04-arm` and `ubuntu-26.04-arm`, and "Use of the standard GitHub-hosted runners is free and unlimited on public repositories." `ubuntu-24.04-arm` is the label decisions.md R8 named and is pinned, like the Windows and macOS labels.

**Identifying the loaded arm64 image** [V]. The docker job's amd64 check compares the image ID with the validated manifest or config digest; that works because its image comes from the same builder. An image loaded from an archive on another runner has an ID that depends on the image store, so the arm64 job compares the config blob and the layers instead. Locally (Docker 29.9.3, containerd image store, a hand-built one-layer arm64 image, no network): after `docker load` the ID was `sha256:cbc29026…`, a manifest Docker made, not the config `sha256:cf6c99c3…`; a `docker save` of it named exactly that config in `manifest.json` and `.RootFS.Layers` was the original diff_id, and `check_loaded_platform_image` accepted it. The arm64 runner turned out to use the classic store (`overlay2`, Docker 28.0.4) while the x64 runner uses the containerd store, so both cases occur in one run.

**Release-policy tests** [V]: `bash .github/release-policy/test-release-policy.sh` **388/388** (348 before; 351 after the scan commit alone). New: the scan step run verbatim with a `dotnet` stub (3); the notes' rows and statements with tamper cases (6 new, 2 adapted); `saved_image_config` (5) and `check_loaded_platform_image` (6); the arm64 container job's four steps with docker and skopeo stubs (11); the Linux steps for both platforms (4); structure (pinned labels, no emulation, no registry, no write permission, the jobs equal to the platforms the notes list; 5 new, 4 adapted).

**Negative controls** [V] (a scratch copy of the branch, restored and compared byte for byte; 388/388 after restoring):
| # | Mutation | Failing |
|---|---|---|
| 1 | The scan's zero-project guard removed | 1 |
| 2 | `container-smoke` dropped from `docker-publish`'s `needs` | 2 |
| 3 | The arm64 runner label unpinned (`ubuntu-latest`) | 1 |
| 4 | win-x86 named as a native run (no `execution:`) | 4 |
| 5 | The loaded image's config not compared | 2 |
| 6 | linux-arm listed as natively smoke-tested | 5 |
| 7 | The arm64 container job reading from a registry | 2 |
| 8 | The Linux process-architecture check removed | 1 |

**Local validation** [V] (Linux x64, SDK 11.0.100): Debug and Release builds 0 errors, 70 warnings (the application sources are those of `1f337c9`); the complete suite inside a network namespace with only a loopback interface, 1,073 tests (909 + 20 skipped, 144), 0 failed; no vulnerable packages in the five projects; `check_global_json`, `check_selected_sdk`, `check_package_policy` and `check_curated_notes_dir` pass (the two tracked fragments compose with the new generated sections to `c48c93a4…`, 10,670 bytes, and `e0af1f9d…`, 16,860 bytes; the published bodies are immutable and unchanged); `release.yml` parsed with YamlDotNet 16.3.0: 9 jobs, 55 `run:` blocks, all pass `bash -n`, the same five pinned actions, the publishing conditions unchanged. A local two-platform image build was not possible (the registries were unreachable from the machine at the time); the pull request's docker job built it.

**The pull request's automatic runs** [V] (PR #65, head `9f3be1c`, both started by the push, attempt 1, nothing dispatched or rerun): CI run 43804206966, both jobs succeeded. Release dry run 43804207028 (`pull_request`, synthetic version `0.0.0-dryrun.40`, built from the pull request's merge commit `5b44e6d`): **27 jobs succeeded** and `Publish image to GHCR` and `Create GitHub release` were skipped, as on every pull request (29 jobs in all; this sentence said "29 jobs succeeded" until D49's recount, §AN). Verify: release-policy tests 388/388, 0 errors and 70 warnings, 929 (909 passed, 20 skipped) + 144 tests, 0 vulnerable packages in the 5 projects. The new jobs:

| Job | Runner (image) | Result |
|---|---|---|
| Smoke test linux-arm64 (native) | `ubuntu-24.04-arm` (ubuntu24-arm64 20261111.182.1), `RUNNER_ARCH` ARM64, `aarch64` | console clean install: 226,471,936 bytes; GUI smoke test under `xvfb`: `native-handle=XID:0x20000f`, 522 parks, 51 states, `settings-probe=pass … permissions=640,600`, `search-probe=pass … permissions=640`, `searches-tab=yes files=0 campgrounds=4966`, `results-tab=yes … start=bound cancel=bound`, `run-probe=pass planned=1 completed=openings:20 results=bound canceled=prompt blocked=no-exit cooldown=on network=none`, `shutdown=clean exit-code=0`, `result=pass`; both `--version`: `0.0.0-dryrun.40+5b44e6d…`, `Framework => .NET 11.0.0 (Arm64)`; `ReleaseCheck` passed on the GUI's and the console's database |
| Container smoke test linux/arm64 (native) | `ubuntu-24.04-arm`, Docker 28.0.4, image store `overlay2`, skopeo 1.13.3 (preinstalled) | the archive is the docker job's (`archive_sha256=8c6cafdf…`, index `342839f2…`, arm64 manifest `dc08f6c7…`, config `9854bccf…`) and passed the complete image check again; "Loaded linux/arm64 image is the validated image: the same config sha256:9854bccf…, 9 identical layer diff_ids"; no `/config`: refused; with `/config` and `/cache`: 226,471,936 bytes extracted in the container; `DOTNET_VERSION` 11.0.0; `--version` `Framework => .NET 11.0.0 (Arm64)`; `uname -m` `aarch64` |
| Smoke test win-x86 (64-bit Windows) | `windows-2025` (win25-vs2026 20261103.298.1), `RUNNER_ARCH` X64 | console and GUI `--version`: `Framework => .NET 11.0.0 (X86)`; console clean install: 226,471,936 bytes; GUI smoke test: `native-handle=HWND:0x30206`, `config-path=C:\Users\runneradmin\AppData\Local\CampertronConfig`, 522 parks, 51 states, both probes `permissions=access-rules-kept`, the same `results-tab` and `run-probe` lines, `shutdown=clean exit-code=0`, `result=pass`; the GUI extracted 226,471,936 bytes |

The six earlier smoke jobs passed as before (linux-x64 on `ubuntu-latest` = ubuntu26 20261111.196.1, win-x64, win-arm64, osx-x64, osx-arm64, each `result=pass` with the Results tab's run probe, and the amd64 container in the docker job: "Loaded amd64 image … equal to its manifest digest (containerd image store), 9 identical layer diff_ids"). The assemble job validated the notes with the new platform table.

**Isolation** [V]: scratch folders only; the real `~/.local/share/CampertronConfig` and `CampertronCache` have the same metadata fingerprints as before (310 and 390 entries); the shipped `RecreationDotOrg.db.br` is unchanged (`700cbd00…`). No tag, release, draft, image or attestation was created; the dry runs' artifacts expire by themselves (1 day for the image, 7 days for the archives).

**Not run, and not shown** [V]: linux-arm (no hosted runner); win-x86 on a 32-bit Windows system; the console menu and every other interactive behavior on any platform in a workflow (open question #38); appearance, fonts, theme detection and screen readers (open question #29; no screenshots); a search against recreation.gov (the Results tab's probe uses a fixture transport); the arm64 image on other hosts.

**Regression checks for later changes to the release workflow**: `test-release-policy.sh`, the negative controls above on the changed part, `bash -n` on every `run:` block, and the pull request's own Release dry run with every job green; a new smoke target needs its row in the notes and must appear in exactly one of `NATIVE_SMOKE_RIDS_DEFAULT` and `COMPAT_SMOKE_RIDS_DEFAULT`.

### AN. Preparation of `v0.1.0-preview.2`: the generated notes' wording, the live search comparison, a test that failed by chance and the candidate (decision D49, branch `release/preview2-preparation`, 2026-11-16)
Release policy and its tests (`.github/release-policy/release-policy.sh`, one sentence of `render_notes`; `test-release-policy.sh`), one statement of the GUI's startup (`CampertronGUI/ViewModels/MainWindowViewModel.cs`, `Begin`) with two tests (`CampertronGUITests/StartupTests.cs`, a helper in `GuiTestHelpers.cs`), and documentation. No library, console, project-file, workflow, `global.json`, dependency or database change. Not a publication: nothing was tagged, released or published, and no workflow was dispatched or rerun. What it decides: D49.

**Starting state** [V]: `master`, `origin/master` and `HEAD` at `dcf8b2f` (the merge of S2, PR #65), the tree clean, `master` the only branch, no open pull request, post-merge CI run 43806594198 successful on attempt 1; 10 tags, 8 releases, no draft, `v0.1.0-preview.1` immutable and Latest; the newest manual dispatch is still run 43673714514 (preview.1's dry run).

**The procedure for preview.2** [V, from §Release workflow's release procedure and decisions.md §Roadmap step 7]:
1. Everything that preview.2 contains is merged and CI on `master` is green (CM1 to CM6, GS1, GS2, S1, S2; no further implementation project is planned or approved).
2. One live comparison of a search in the console and in the Results tab (below; done).
3. The curated notes `.github/release-notes/v0.1.0-preview.2.md` through their own pull request, the last content pull request (the pull request after this one; whether the fragment is on `master` is the record).
4. The maintainer's own test of a candidate (the maintainer's condition of 2026-11-16 before any approval to publish). The candidate is a Release dry run's archives (below).
5. The pre-tag dry run, `gh workflow run release.yml --ref master`: a manual dispatch, with its own approval. Not approved, not run.
6. The tag `v0.1.0-preview.2` on the notes pull request's merge commit or later: its own approval. Not approved.
7. After the publication: its verification (release procedure step 5) and the documentation step (step 7: the README's download names are still preview.1's).

The version comes only from the tag (`-p:Version` in `release.yml`); no repository file carries a version, and nothing is packaged outside the workflow. A fix that the maintainer's test makes necessary goes through its own pull request, and the notes are then reviewed again (they must stay the last content pull request).

**The generated notes' wording** [V]: `render_notes`' Downloads sentence said "a **GUI** archive (campground lookup)". The GUI has edited the settings files since D43 and the search files since D44 and has run searches on request since D46 (the README says so since GS2), so the sentence is now "a **GUI** archive (campground lookup, settings and search editing, and searches on request)". Release-policy tests **392/392** (388 before): for a pre-release and a stable version the generated notes hold exactly the new sentence and no longer the old wording. Negative control (a scratch copy with the old sentence): those 4 tests fail, 388 pass. The generated sections are part of every composed body, so the two tracked fragments now compose to `f40582b5…` (10,724 bytes, `v0.1.0-beta.10`) and `442772a4…` (16,914 bytes, `v0.1.0-preview.1`); their published bodies are immutable and unchanged.

**A test that could fail a release run by chance** [V]: this pull request's first Release dry run (43857156609, on the policy commit `526a682`) failed in `Release verify`, step "Test (Release)": `SmokeTestTests.MissingRenderedFrame_TimesOut` failed after 14 ms with "System.InvalidOperationException : The calling thread cannot access this object because a different thread owns it." from `Avalonia.Headless.HeadlessUnitTestSession.EnsureIsolatedApplication` → `AvaloniaHeadlessPlatform.Initialize` → `Compositor..ctor` → `DefaultRenderLoop.Add` (143 of 144 GUI tests passed, the library's 929 as always; the jobs after verify were skipped, nothing was rerun). The same commit's CI run passed the same tests, and §AL had recorded this failure once locally as not reproduced. A test failure blocks a release run, so a tag run could have failed the same way and needed an approved rerun.
- **Cause** [V, Avalonia 12.1.6's source and a reproduction]: `MainWindowViewModel.Begin` ran `Load.GetConfig` on a worker thread and its continuation asked for `Dispatcher.UIThread` there to post the completion. In Avalonia 12 `Dispatcher.UIThread` is the first dispatcher created (`s_uiThread ??= this` in the constructor), and a thread that asks while there is none creates its own (`UIThread` → `CurrentDispatcher` → `new Dispatcher(null)`). `Avalonia.Headless` clears the dispatchers between two tests (`Dispatcher.ResetForUnitTests` after a test, `ResetBeforeUnitTests` before the next, both `ResetGlobalState`). `SmokeTestTests.MissingNativeHandle_Fails` opens two windows with the real loader and closes them at once, so their preparations complete after the test; when a completion asked for `Dispatcher.UIThread` between the next test's reset and its application's first use of it, the worker thread became the UI thread, and the next test in the fixture's order, `MissingRenderedFrame_TimesOut`, failed in its setup on the session's thread.
- **Reproduced** [V] (scratch copies of `master`, removed): a test that leaves a thread asking for `Dispatcher.UIThread` makes the next `[AvaloniaTest]` fail with exactly that exception and stack, 3 of 3 runs; with the continuation of `Begin` asking repeatedly for 700 ms (the real code asks once, at a moment nobody controls), `MissingNativeHandle_Fails` followed by `MissingRenderedFrame_TimesOut` fails 3 of 3 runs in `MissingRenderedFrame_TimesOut` with the dry run's message and stack; with the fix and the completion arriving 700 ms late, 3 of 3 runs pass.
- **Fix** [V]: `Begin`, which runs on the UI thread (`Start` and `Retry` verify it), captures `Dispatcher.UIThread`, and the continuation posts the completion to that dispatcher. A running application has one dispatcher for its whole life, so nothing changes there: the same object receives the same post (a post to a dispatcher that has shut down is dropped without an exception, Avalonia's `InvokeAsyncImpl`). The attempt and detach guards, `PendingPreparation` and the order of everything else are unchanged.
- **Tests** [V] (`StartupTests`, +2; 146 GUI tests): `ALateCompletion_GoesToTheDispatcherThatBeganTheAttempt_AndNoWorkerThreadBecomesTheUiThread` (a plain test whose thread plays the UI thread: it starts an attempt, clears the dispatchers as the headless session does between two tests, lets the loader finish, and requires the completion on the first dispatcher and that the thread asking next still gets its own; it finds Avalonia's internal `Dispatcher.ResetBeforeUnitTests` by reflection and says so if an Avalonia change removes it) and `TheCompletion_NeverAsksForTheUiDispatcherOnTheWorkerThread` (from the IL: no lambda of `MainWindowViewModel` refers to `Dispatcher.UIThread`, the continuation posts and runs `Complete`, and only `Start`, `Retry` and `Begin` ask for it). **Negative control** (a scratch copy with the former statement): both fail (the first after its 10 s wait, "the completion did not reach the dispatcher that began the attempt"), the other 8 `StartupTests` pass.
- **Validation** [V] (Linux x64, SDK 11.0.100): Release build of the solution 0 errors, **70 warnings** (no warning in the changed files; identities and positions unchanged); `CampertronUnitTests` 929 (909 passed, 20 Windows-only skipped), `CampertronGUITests` **146/146**; the complete GUI suite twenty times in a row, 146/146 each time. The published GUI's own start is covered by the smoke test of every platform in the pull request's dry run (below): its `ready=yes` line needs the posted completion.
- **The same pattern, not changed** [V, code]: `ResultsSeams.Post`'s production default (`Dispatcher.UIThread.Post`) is called on the search's worker thread and by its continuation, so a GUI test that ended while a run's end was still to be posted could do the same to the test after it. Not observed: the Results tab's tests wait for the end they check, and no such failure occurred in the repeated runs above. Recorded as TD-56; a change there belongs to a change of the Results tab with its own validation (D46's list).

**Live comparison of the console and the Results tab** [V] (2026-11-16, 11:15 to 11:17 UTC; Linux x64; Release builds of an export of `dcf8b2f` with SDK 11.0.100; `HOME` and `XDG_DATA_HOME` in a scratch folder; requests to the real recreation.gov, 12 month requests in all, none failed, blocked or tried again):
- **Data folder**: created by a first console start (sample `General.yaml` and `Email.yaml`, the database extracted), then `outputTo: HtmlFile`. Three search files, all `autoRun: true`: `Zion.yaml` and `SouthRim.yaml`, byte-identical copies of the shipped examples `examples/0.1/ZionConfig.yaml` and `SouthRimConfig.yaml`, and `ZionTwoNights.yaml` (Watchman, `DaysOut` 30, no group sites, `consecutiveDays: 2`). Two campgrounds (232445, 232490), November and December 2026: 4 month requests per run.
- **Console**: the real `CampertronConsole` on a pseudo-terminal: the menu, Enter on Search, the results page written and handed to a stand-in `xdg-open` that kept a copy, a key, Exit; exit code 0. Run twice, before and after the GUI's run.
- **Results tab**: the real application and window (`Program.BuildAvaloniaApp`, `App`, `MainWindow`, the normal startup) in a nested KWin session (`kwin_wayland --virtual` with Xwayland), with the production `MainWindowViewModel`, `ResultsViewModel`, `CampsiteSearch.Plan`/`Run` and `HttpSearchTransport`. A scratch launcher only chose the Results tab, executed the tab's own `StartCommand`, and at the end wrote the run's typed result (`ResultsViewModel.LastResult`), what the tab presents and a picture of the window. The tab listed the three files in the console's order, all selected ("Runs at startup").
- **Compared**: for every search the set of (campsite ID, night) pairs, read from the console's page (each site's booking link and `<time datetime>`) and from the tab's `SearchResult.Openings`.

| Search file | Campground | Console, run 1 | Results tab | Console, run 2 | Only in the console / only in the tab |
|---|---|---|---|---|---|
| `SouthRim.yaml` (Fri and Sat, 2 consecutive nights) | MATHER CAMPGROUND (232490) | 862 nights at 240 campsites | 862 nights at 240 campsites (431 stays) | 862 | 0 / 0 (both runs) |
| `Zion.yaml` (single nights, 3 filters) | WATCHMAN CAMPGROUND (232445) | 2,581 openings at 172 campsites on 28 nights | 2,581 at 172 on 28 nights | 2,581 | 0 / 0 (both runs) |
| `ZionTwoNights.yaml` (2 consecutive nights) | WATCHMAN CAMPGROUND (232445) | 2,570 nights at 173 campsites | 2,570 at 173 (293 stays) | 2,570 | 0 / 0 (both runs) |
| Total | | "Done: 3 searches, 6,013 openings at 585 sites." | "Finished: 6,013 openings found in 3 searches." | the same line as run 1 | 0 / 0 |

- **The tab's run**: status `Completed`, 4 of 4 month requests finished, 0 failed, no issue and no contained defect, both months checked for every search, elapsed 0:01; every booking link exactly `https://www.recreation.gov/camping/campsites/<campsite ID>`; the progress line "Checking availability at recreation.gov · Campsite details: 2 of 2 campgrounds ready · Availability: 0 of 4 month requests finished"; the presented rows match (2,581 opening rows; 240 and 173 site rows), and the picture shows the Mather results with their stays.
- **Side effects**: none beyond the cache: the three search files byte-identical, `General.yaml` as set for the comparison, no `Camp.History`.
- **What this shows**: the Results tab's own transport reaches recreation.gov and its results equal the console's for these searches at that time, single nights and consecutive nights, with months shared by two searches. **What it does not show**: keyboard or pointer input (the launcher executed the command), Windows and macOS, a live block, timeout or retry (the automated tests cover those with stand-ins), other campgrounds or date types, the Email loop.

**The candidate for the maintainer's own test** [V]: the repository's non-publication mechanism is the Release dry run (`pull_request` or `workflow_dispatch`): `release.yml` builds, checks and smoke-tests all 16 archives and both container images exactly as for a tag, with the synthetic version `0.0.0-dryrun.<run number>`, and skips `docker-publish` and `release`. Nothing is tagged, published or attested, and the archives expire by themselves (7 days; the container image 1 day).
- **This pull request's automatic runs** (PR #66; every run started by a push, attempt 1, nothing dispatched or rerun). On the policy commit `526a682`: CI run 43857157062, both jobs succeeded; Release dry run 43857156609 failed in verify as described above and built nothing. On `1b16f91` (the policy commit, the startup fix and its tests; the application of the candidate): CI run 43859298206, both jobs succeeded; Release dry run 43859298616 (`0.0.0-dryrun.43`, built from the pull request's merge commit `d3b905c`): **27 jobs succeeded**, `Publish image to GHCR` and `Create GitHub release` skipped. Verify: release-policy tests 392/392, 0 errors and 70 warnings, 929 (909 passed, 20 skipped) + 146 tests, no vulnerable packages in the 5 projects. All seven archive smoke jobs ended with `result=pass`: linux-x64, linux-arm64, win-x64, win-arm64, osx-x64 and osx-arm64 natively, win-x86 as `(X86)` processes on 64-bit Windows; the linux/arm64 image ran in a native arm64 container (`(Arm64)`, `aarch64`), identified as the validated image. The documentation commit after it starts the same two runs again; their archives, built from the pull request's final tree, are the candidate (the pull request's checks list them).
- **The application as it was at the start**: S2's dry run 43805587646 (`0.0.0-dryrun.41`) was built from the commit `28d499a`, whose tree `ae01e79c…` is the tree of `dcf8b2f`. It lacks only the startup fix above. Its linux-x64 archives were downloaded and checked here (the download path and the local run of a candidate): both match the run's `SHA256SUMS.txt`; both `--version` report `0.0.0-dryrun.41+28d499a…` on `.NET 11.0.0 (X64)`; the GUI's `--smoke-test`, in the nested session with a new data folder, ended with `result=pass` (522 parks, 51 states, `settings-probe=pass`, `search-probe=pass`, `run-probe=pass`, `shutdown=clean exit-code=0`, empty standard error).
- **Downloading**: the archives are uploaded as they are (`archive: false`), and `gh run download` 2.104.0 fails on them ("error extracting zip archive: zip: not a valid zip file"). `gh api repos/ShaunAnthonyHathaway/Campertron/actions/artifacts/<artifact id>/zip > <artifact name>` returns the archive itself (IDs: `gh api 'repos/ShaunAnthonyHathaway/Campertron/actions/runs/<run id>/artifacts?per_page=100'`); the run's page in a browser offers the same files. `release-metadata` is an ordinary artifact (`gh run download -n release-metadata` works) and holds the run's `SHA256SUMS.txt`.
- **How a candidate differs from the release**: its version text (`0.0.0-dryrun.<n>+<commit>` in `--version`, View Config and the archive names), no attestation, no published image. The curated notes are in no archive. [I] The same sources and toolchain give the same program; the builds are not compared byte for byte.

**Not done here, each needing its own approval** [V]: the maintainer's test of the candidate; the pre-tag dry run; the tag. **What a dry run does not show** [V]: the tag-only jobs `docker-publish` and `release` (skipped in every dry run: the push to GHCR, the attestations, the draft and its publication), the publishing composition of the notes inside the workflow (`assemble_release_notes true` runs only for a tag; CI and the verify job compose and validate every tracked fragment), and the real version in the artifacts. The last run of those paths was `v0.1.0-preview.1`'s (§AC); `release.yml` has changed since then (S2, §AM).

**Isolation** [V]: scratch folders only (the builds from a `git archive` export, the live data folder, the nested session's runtime folder); the real `~/.local/share/CampertronConfig` and `CampertronCache` have the same metadata fingerprints as before (310 and 390 entries); the shipped `RecreationDotOrg.db.br` is unchanged (`700cbd00…`).

### AO. The GUI's navigation and a saved theme preference (decision D50, branch `feat/gui-navigation-and-theme`, 2026-11-16)
GUI, GUI tests and the release policy's smoke-report check only: `CampertronGUI/Views/MainWindow.axaml(.cs)`, `Styles/AppStyles.axaml`, `App.axaml(.cs)`, `ViewModels/MainWindowViewModel.cs`, `SmokeTest.cs`, and new `ThemePreference.cs`, `ThemeProbe.cs`, `ViewModels/SystemSettingsViewModel.cs`, `Views/SystemSettingsView.axaml(.cs)`; tests `NavigationTests`, `ThemePreferenceTests` (new) and the GUI tests that named tab positions (`GuiTestHelpers`, `MainWindowTests`, `MainWindowUsabilityTests`, `CampgroundIdentityTests`, `StartupTests`, `SettingsEditorTests`, `SearchEditorTests`, `ResultsTabTests`, `SmokeTestTests`); `.github/release-policy/release-policy.sh` (`GUI_SMOKE_KEYS`, `check_gui_smoke_report`) and `test-release-policy.sh`; README and its GUI screenshots. No library, console, workflow, project-file, `global.json`, dependency or database change. Linux x64 (CachyOS, KDE Plasma Wayland), SDK 11.0.100 / runtime 11.0.0. What changed: D50, business-rules §21, architecture.md §GUI data flow.

**Starting state** [V]: `master`, `origin/master` and `HEAD` at `ffaefa8` (the merge of PR #68), the tree clean, `master` the only branch, one worktree, no stash, no open pull request; 10 tags, 8 releases (no draft), `v0.1.0-preview.1` immutable and Latest, no `v0.1.0-preview.2` tag or release; Release dry run 43871764484 (run 45, `workflow_dispatch`, attempt 1, `ffaefa8`): 27 jobs succeeded, `Publish image to GHCR` and `Create GitHub release` skipped, 16 archives, `container-image` and `release-metadata`. The maintainer's personal test of that candidate asked for this project, so the candidate is superseded (not eligible for a tag); no replacement was built.

**Facts established on the way** [V]: Avalonia 12.1.6's TabControl trap (TD-39) also holds on a nested tab header (with the workaround switched off in a scratch build: Tab from the Campsites header reached By Park and the park selector and then stayed; Shift+Tab stayed on the selector and on the By Park header); with the former workaround Shift+Tab from the first selector skipped the nested header, and after a wrap Tab from the top-level header went past it (Avalonia then enters the nested row's content directly). A tab row's panel is a `KeyboardNavigationMode.Once` group (`TabControl.OnApplyTemplate`). Fluent pads a TabControl's content by `TabItemMargin` (12,0,12,0), so a nested row first sat 12 to the right of the other tabs' content with headers as large as the top level's; the `nested` style moves it back and uses 16-point headers. `JsonObject` reads its members lazily: a duplicate member did not fail `JsonNode.Parse` but the first `TryGetPropertyValue` (found by `ThemePreferenceTests` and fixed before the commit: the parser now asks once, inside its `try`). The radio buttons are three Tab stops (Avalonia gives a `RadioButton` group no single stop). On X11 the platform window keeps its own keyboard device (`X11Window._keyboard`), not `AvaloniaLocator`'s, which matters only for a review launcher that injects keys.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Debug` and `-c Release`, `--no-incremental` | 0 errors, **70 warnings** each |
| Warning identities vs `master` `ffaefa8` built the same way (an export; file, code, message; and with positions) | 44 identities on both, 70 with positions, none added or removed, Debug and Release equal; no moved line |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **1,117 tests: 929 `CampertronUnitTests` (909 passed, 20 Windows-only skipped) + 188 `CampertronGUITests`**, 0 failed. 42 new: `NavigationTests` 7, `ThemePreferenceTests` 35 cases (14 tests); 9 existing GUI test files adapted to the tab positions (the expected keyboard sequences now include the nested headers) |
| The same suites in a network namespace with only loopback (`unshare -rn`) | 188 GUI passed; library 894 passed, 35 skipped (the 20 Windows-only tests and 15 permission tests that skip for a privileged process, which the namespace's root mapping is), 0 failed |
| `bash .github/release-policy/test-release-policy.sh` | **410/410** (392 before; 18 new smoke-report cases: a missing navigation step, another top-level order, the six former tabs, another start tab, another Campsites row, Settings without System, the navigation after the parks, a missing, failed or incomplete theme probe, a default that is not System, no Dark applied, a missing or not shown System tab, other choices, a clean start that is not System or has a preferences file, the System tab before the Settings tab) |
| `dotnet list <project> package --vulnerable --include-transitive`, five projects | no vulnerable packages |
| `git diff --check`, new files | no whitespace error, LF only, final newlines |
| Dependencies, project files, workflows, `global.json`, the shipped database | unchanged (`RecreationDotOrg.db.br` SHA-256 `700cbd00f0722ceca4b93e3ccb29bce17a4d6c584579cf504212d4b400a81e0b`) |

**Negative controls** [V]. Each a temporary edit of a scratch export of the branch's commit (never the working tree), built and run against the whole GUI suite (188 tests), then restored and checked by SHA-256; the restored copy passed 188/188:
| # | Mutation | Failing (of 188) | For example |
|---|---|---|---|
| 1 | Top-level order: Searches and Results swapped | 9 | `TheTopLevelTabs_AreSearchesResultsSettingsCampsites_AndSearchesIsShownFirst`, `MainWindow_HasTheFourTopLevelTabsInOrder`, the smoke report |
| 2 | Start tab: Campsites instead of Searches | 7 | `TheTopLevelTabs_…`, `TheSearchesTab_ListsTheSearchFiles_WithTheirState`, the smoke report |
| 3 | Campsites row: By City and By State swapped | 4 | `TheCampsitesTab_HoldsByParkByCityAndByState_WithByParkFirst`, `Keyboard_ArrowKeysSwitchTabsAndTabEntersTheContent` |
| 4 | Campsites row opens on By City | 3 | `TheCampsitesTab_…`, `EverySelection_RunsOneParameterizedQuery_AndNoNameLookup` |
| 5 | Settings row: Campertron renamed | 6 | `TheSettingsTab_HoldsCampertronAndSystem_WithCampertronFirst`, `SettingsCampertron_IsTheFirstSettingsTab_AndOpensBothFiles` |
| 6 | Settings row opens on System | 22 | every Settings editor test, `TheSettingsTab_…` |
| 7 | Default theme Light instead of System | 7 | `WithoutAFile_TheThemeIsSystem_AndReadingCreatesNothing`, `TheStart_WithoutAPreference_FollowsTheSystem`, the smoke report |
| 8 | The start reads the saved theme but doesn't apply it | 1 | `TheStart_AppliesTheSavedTheme_AndAChoiceSurvivesARestart` |
| 9 | A save writes nothing (and reports success) | 9 | `ASavedTheme_IsReadBackByANewStore`, `ChoosingATheme_AppliesItAtOnce_SavesIt_AndTouchesNoSettingsFile`, the smoke report |
| 10 | A malformed file gives Dark | 11 | `AnUnusablePreference_FallsBackToSystem`, `ATooLargeOrUnreadablePreference_FallsBackToSystem`, the smoke report |
| 11 | An unknown theme gives Dark | 9 | `AnUnusablePreference_FallsBackToSystem`, `AnUnusablePreference_StartsWithSystem_SaysSo_AndAChoiceReplacesIt` |
| 12 | The preference kept in the settings folder (`CampertronConfig`) | 3 | `TheCurrentUsersPreference_IsInTheGuisOwnFolder_ApartFromTheSettings`, `WalkingThroughEveryTab_WritesNothing` |
| 13 | A save drops the object's other members | 3 | `Saving_KeepsOtherMembers_AndReplacesAMalformedFile`, the smoke report |
| 14 | The former TD-39 workaround (top-level header only) | 3 | `Keyboard_ReachesEveryRowAndTab_AndWrapsOnBothLevels`, `Keyboard_TabCyclesThroughHeaderSelectorsDetailsIdAndCopyButton` |
| 15 | Searches not shown to its view model when the startup is ready | 18 | `TheSearchesTab_ListsTheSearchFiles_WithTheirState`, the search editor tests |
| 16 | Settings > System counted as Settings > Campertron (no masking when leaving for System) | 1 | `SettingsCampertron_LooksAtItsFilesWhenShown_AndLeavingItForSystemHidesThePassword` |
| 17 | Settings > System bound to the wrong view model | 12 | `TheSystemTab_IsAccessible_AndFitsInLightAndDark`, `ChoosingATheme_…`, the smoke report |
| 18 | Release policy: `master`'s `check_gui_smoke_report` against the new reports | 10 of 410 | every case that expects the new report to pass (it is refused) |

**GUI smoke test, Linux, real window** [V]: the Debug `CampertronGUI --smoke-test` in a nested KWin session (`dbus-run-session --config-file=<no service directories> -- kwin_wayland --virtual --xwayland`, KWin's own Xwayland worked this time) with a new, empty `XDG_DATA_HOME`: exit 0, empty standard error, every line `SMOKE …`, accepted by `check_gui_smoke_run`, among them `navigation=start=Searches tabs=Searches,Results,Settings,Campsites settings=Campertron,System campsites=By Park,By City,By State`, `parks=522`, `theme=Light`, `theme-probe=pass default=System saved=Dark reloaded=Dark kept-keys=yes malformed=System unknown=System applied=Light,Dark,System`, `system-tab=yes choices=System,Light,Dark selected=System preference=missing`, `result=pass`; the data folder held only `CampertronConfig` and `CampertronCache` afterwards (no `CampertronGUI`).

**The pull request's automatic runs** [V] (PR #69, head `e730caa`; started by the push, attempt 1, nothing dispatched or rerun): CI run 43962926991, both jobs succeeded (`Restore, build, test, vulnerability scan` and `Windows settings files (CM1 to CM3, CM5, CM6)`); Release dry run 43962926565 (run 46, `pull_request`): **27 jobs succeeded**, `Publish image to GHCR` and `Create GitHub release` skipped. On every archive smoke job, natively on linux-x64, linux-arm64, win-x64, win-arm64, osx-x64 and osx-arm64 and as 32-bit processes on 64-bit Windows for win-x86, the published GUI's `--smoke-test` reported `navigation=start=Searches tabs=Searches,Results,Settings,Campsites settings=Campertron,System campsites=By Park,By City,By State`, `theme=Light` (the runners' own theme, not controlled), `theme-probe=pass default=System saved=Dark reloaded=Dark kept-keys=yes malformed=System unknown=System applied=Light,Dark,System`, `system-tab=yes choices=System,Light,Dark selected=System preference=missing` and `result=pass`: the store's write, replace and read and the forced Light and Dark themes work natively on all seven, and a clean start is System. The documentation commit that records this starts the same two runs again.

**Real-window review** [V] (the same nested session; the real application through `Program.BuildAvaloniaApp` and its startup, started by a scratch launcher outside the repository (assembly name `CampertronGUITests`) that chose tabs in the window's own controls or view models and typed keys through the X11 window's own input (`PlatformImpl.Input`, `RawKeyEventArgs` with the window's keyboard device); isolated data folders):
| Scenario | Seen |
|---|---|
| Start | the window opened on Searches with the search files and campground data read (`ready top=0 searches-loaded=True`) |
| Keyboard | from the Searches header Right, Right → Settings; Tab: Campertron header, `GeneralFileButton`, `EmailFileButton`; Shift+Tab back: `GeneralFileButton`, Campertron header, Settings header; System: the three theme buttons, then the wrap to the Settings header and System again; Campsites: By Park header, the park selector, the wrap to the Campsites header, By Park again; Shift+Tab: Campsites header, the park selector (backward wrap), By Park header |
| Choosing Dark with the keyboard (Space on the Dark button) | applied at once (`actual=Dark`), "Saved. Campertron starts with this theme next time.", `preferences.json` = `{"theme": "Dark"}` (indented) |
| Restart (the real `CampertronGUI --smoke-test` on the same folder) | `theme=Dark`, `system-tab=… selected=Dark preference=saved` |
| Preferences file `{"theme": [` / `{"theme":"Sepia","later":1}` / removed | `selected=System preference=malformed` / `unknown` / `missing`, `theme=Light` (the session has no theme setting), every run `result=pass` |
| System following the operating system (a stand-in for xdg-desktop-portal's `org.freedesktop.portal.Settings` on the nested session bus, answering `color-scheme` and emitting `SettingChanged`) | System with the portal dark: Dark, "…as the system asks."; the portal to light, then dark: Light, then Dark, while running; Light chosen: stays Light through a light and a dark change; Dark chosen with the portal light: Dark; System chosen again: Light, then Dark with the portal. Avalonia read `color-scheme` and `accent-color` once and followed the 6 signals |

**README screenshots** (2026-11-16, §Refreshing the README screenshots): `ss11` Searches, `ss9` Results, `ss10` Settings > Campertron, `ss12` Settings > System (new), `ss6` Campsites > By Park, all from the branch's commit `456c351`, with the procedure of 2026-11-16 (1000×890 at scale 1.6, `unshare -rm` with a tmpfs `/home` and `HOME=/home/camper`, the three search files, the README's example `Email.yaml`), Dark through the feature itself: a `preferences.json` with `"theme": "Dark"` in `/home/camper/.local/share/CampertronGUI` (no theme requested by the launcher). The Results capture is a real search of the two enabled examples (4 month requests to recreation.gov, none failed): "Finished: 3,436 openings found in 2 searches". The README was rendered with GitHub's Markdown API into a page of GitHub's content width and viewed in headless Chrome at 1280 and 400 CSS pixels (images scale to the column, no horizontal scroll); every image is referenced, every anchor matches a heading and every relative link exists.

**Isolation** [V]: scratch folders only; the real `~/.local/share/CampertronConfig` and `CampertronCache` have the same metadata fingerprint before and after (700 entries, `b44b5711…`), no `~/.local/share/CampertronGUI` was created, no probe folder was left in the temporary folder, and the shipped database is unchanged. The only requests to recreation.gov were the screenshot runs' (each 4 month requests).

**Not run** [V]: the GUI on Windows and macOS except the release dry run's smoke tests (the theme probe applies Light and Dark there; whether System follows the Windows and macOS settings is not inspected: open question #29); screen readers; a candidate build (no manual dispatch).

**Regression checks for later changes**: `NavigationTests`, `ThemePreferenceTests`, `MainWindowUsabilityTests`, `SettingsEditorTests`, `SearchEditorTests`, `ResultsTabTests`, `StartupTests`, `SmokeTestTests` and the release-policy tests; the negative controls above on the changed part; for navigation, keyboard or theme changes the real-window review (keyboard on both levels, a choice and a restart, a stand-in portal for System).

### AP. The curated `v0.1.0-preview.2` notes re-reviewed for D50 (PR #70, branch `release-notes/v0.1.0-preview.2-navigation-theme`, 2026-11-16)
Release notes and documentation only; no application, test, workflow or release-policy change. Not a publication: nothing was built, tagged, released or published, and no workflow was dispatched or rerun.

**Starting state** [V]: `master`, `origin/master` and `HEAD` at `ecd691e` (the merge of PR #69), the tree clean, `master` the only branch, one worktree, no stash, no open pull request; post-merge CI run 43967262351 successful on attempt 1; 10 tags, 8 releases (no draft), `v0.1.0-preview.1` immutable and Latest, no `v0.1.0-preview.2` tag or release; the newest manual dispatch still Release run 43871764484 (run 45), whose candidate is superseded by D50; the pull request dry runs 43962926565 and 43964559532 are validation only, not a candidate.

**Source and rules** [V]: the authoritative curated notes are `.github/release-notes/v0.1.0-preview.2.md` (from PR #67, 11,216 bytes, `806f82b6…`), a fragment under the rules of `.github/release-notes/README.md` and `validate_curated_fragment` (D30): the first line `## What's new in 0.1.0-preview.2`, no generated section, status or release-flag statement, no download-table row or archive name, UTF-8 with LF and one final newline; CI's `check_curated_notes_dir` composes every fragment and shows each body.

**Review** [V]: the notes were compared with every pull request merged since `v0.1.0-preview.1` (#55 to #69). #56 to #65 were covered; #66's user-facing part is the generated Downloads sentence (still accurate after D50: "campground lookup, settings and search editing, and searches on request"); #68 changed only the README; #69 (D50) was missing, and three lines had become stale ("the new Settings tab", "By Park", "the GUI's Settings tab"). Every new statement was checked against D50's record (business-rules §21, §AO, the native smoke lines of dry runs 46 and 47).
- **Added:** "Four tabs, in the order you use them" (Searches, Results, Settings, Campsites, Searches first; Campsites' By Park, By City and By State, the GUI's only tabs in 0.1.0-preview.1; Settings' Campertron and System; each tab keeps its state; the keyboard on both rows of tabs) and "A light or dark window" (System the default, following the computer's setting as before; Light and Dark; applied at once, kept in `CampertronGUI/preferences.json` next to `CampertronConfig`, apart from the settings files, not used by the console; System with a notice when the saved choice can't be used); Upgrading: "The GUI opens on Searches" and "Theme choice" (the file appears only after a choice, next to `CampertronConfig`, not in it).
- **Corrected:** the introduction (the four tabs and the theme), "Settings > Campertron", "By Park (under Campsites)", Upgrading's email bullet, the publishing checks (the smoke test also checks the four tabs, saves a theme choice in a temporary folder and switches the window to the light and the dark theme), and the Windows and macOS limitation (the layout and the themes; whether System follows the Windows and macOS setting is not checked, open question #29).
- **Not included:** the database-backed filter selectors and their open decisions (investigated read-only, not implemented), filter-matching changes, single-file packaging, later versions.

**Checks** [V]: the fragment is 13,350 bytes, SHA-256 `b8b6def1a22d932c3c4781b832d0f7b2b34626f477af72b72a01eb2bda62b782`; `validate_curated_fragment` and `check_curated_notes_dir` pass (3 fragments valid; the two published fragments compose unchanged, `f40582b5…` and `442772a4…`); the publishing composition (`assemble_release_notes true 0.1.0-preview.2 …`) passes `validate_notes` and holds the fragment byte for byte: 17,205 bytes, SHA-256 `d0cc33ff9f28a9279a2d83adc7e8936f0effaadfd692b4adf671c073aafbd3c5`; release-policy tests 410/410; GitHub's Markdown rendering of the composed body: 7 `##` and 10 `###` headings, 57 list items, no stray markup. (Sourcing `release-policy.sh` in zsh fails: its functions' local `path` is zsh's `PATH`; run it with bash, as CI does.)

**Still to come, each with its own approval** [V]: a new candidate (a manual dispatch of the Release dry run), the maintainer's personal test of it, the pre-tag dry run and the tag; after the publication, the README's download names.

### AQ. Database-backed filter selectors, stage 1: OR semantics, `filterMatching`, the filter choices and the attribute index (decision D51, branch `feat/filter-matching-foundation`, 2026-11-16)
Library, its tests, the search editor's hints, documentation and examples: `class/Base/Config.cs`, `class/Base/ConfigDiagnostic.cs`, new `class/RecDotOrg/CampgroundFilterChoices.cs`, `function/Base/ConfigValidation.cs`, `function/Base/Yaml.cs`, `function/RecDotOrg/api/AvailabilityApi.cs`, new `function/RecDotOrg/api/SearchFilters.cs`, `function/RecDotOrg/data/Load.cs`, `function/RecDotOrg/sqlite/Clear.cs`, `LegacyAddressLayout.cs`, `Read.cs`, `RidbStagedRefresh.cs`, new `RidbIndexes.cs`; `CampertronGUI/ViewModels/SearchFileViewModel.cs` (hints only). No dependency, project file, workflow, release policy or `global.json` change; the embedded database is unchanged.

**Starting state** [V]: `master`, `origin/master` and `HEAD` at `fd65cf3` (the merge of PR #70), the tree clean, `master` the only branch, one worktree, no stash, no open pull request; post-merge CI 43972377576 successful on attempt 1; 10 tags, 8 releases (no draft), `v0.1.0-preview.1` immutable and Latest, no `v0.1.0-preview.2` tag, release or draft; the newest manual dispatch is still Release run 43871764484 (run 45, `ffaefa8`), whose candidate D50 superseded; dry runs 46 and 47 were validation only; no replacement candidate, no dispatch or rerun since.

**Database facts re-verified** [V] (read-only SQL with the `sqlite3` 3.54.0 CLI on a copy of the shipped database extracted from `RecreationDotOrg.db.br` (226,471,936 bytes, SHA-256 `8712856097a91ee1…`) and on a copy of a real data folder's database refreshed 2026-11-11 (233,795,584 bytes, `a825ce994716b775…`, the original only read by `cp`); the numbers are in business-rules §22). For example: `SELECT count(DISTINCT EquipmentName) FROM PermittedEquipmentEntries` (15), `SELECT count(DISTINCT CampsiteType) FROM CampsitesEntries` (39), `SELECT count(*) FROM (SELECT DISTINCT AttributeName, AttributeValue FROM CampsiteAttributesEntries)` (3,489), `SELECT FacilityID, count(*) n FROM CampsitesEntries GROUP BY FacilityID ORDER BY n DESC LIMIT 3` (258830 360, 232490 357, 233627 328), orphans `… WHERE NOT EXISTS (SELECT 1 FROM CampsitesEntries c WHERE c.CampsiteID = e.CampsiteID)` (4,850 equipment, 1,200 attribute rows), duplicate names `SELECT count(*), count(DISTINCT FacilityID) FROM (SELECT FacilityID, CampsiteName, count(*) c FROM CampsitesEntries GROUP BY FacilityID, CampsiteName HAVING c>1)` (1,115 in 120), case variants `… GROUP BY upper(AttributeName||':'||AttributeValue) HAVING count(DISTINCT AttributeName||':'||AttributeValue)>1` (28), `PRAGMA journal_mode` (wal). The previously recorded matching was confirmed in the code: ordinal `string.Contains`/`List.Contains`, no trimming, equipment and attributes "list value contains the campsite's", types "campsite type contains the list value", exact names, AND for excludeEquipment, excludeAttributes and includeCampsiteType.

**YamlDotNet 16.3.0 and an enum** [V] (a scratch program outside the repository, the same `DeserializerBuilder` and naming convention): for a non-nullable enum `filterMatching:`, `~` and `null` give the first member; for a nullable one null; `''` and `Fuzzy` throw; `0`, `1` give members and `7`, `-1` undefined values; `' Exact'` gives Exact (trimmed); `'Exact, Contains'` gives Contains (flags); any case is accepted; a duplicate key takes the last value; the default serializer writes `filterMatching: ` for null (hence `OmitNull`).

**The index: plans and timings** [V] (copies of the shipped database; the user's database never opened by the product). Without the index `EXPLAIN QUERY PLAN SELECT AttributeName FROM CampsiteAttributesEntries WHERE EntityID = ?` is `SCAN CampsiteAttributesEntries`; with it `SEARCH … USING INDEX IX_CampsiteAttributesEntries_EntityID (EntityID=?)` (also asserted by `RidbIndexesTests`). Timings through the product's code (Microsoft.Data.Sqlite with the bundled SQLite 3.53.4, Release, a scratch harness referencing `CampertronLibrary`; 12-core x86-64, local NVMe, warm page cache; medians, the first runs after a copy were cold and are listed separately):
| Operation | Without | With | Method |
|---|---|---|---|
| One campsite's attributes (`Read.GetCampSiteAttributesByCampsite("10146649")`, 28 rows) | 98.8 ms (min 98.1, max 130.3) | 0.41 ms (min 0.39) | 15 runs after a warm-up |
| A campground's search cache (`Cache.GenerateCacheForCampground`, one lookup per campsite, `Parallel.ForEach`) | 258830 (360 sites): 6,599 ms (6,432 / 6,599 / 7,654); Watchman 232445 (192): 3,728 ms | 58 ms (54 / 58; first run 741, cold); 37 ms (35 / 37; first 721, cold) | 3 runs each, a new cache folder each |
| A campground's filter choices (`Read.FilterChoicesByCampground`) | 258830 191 ms, 232445 257 ms, 232490 345 ms | 22 ms, 27 ms, 34 ms | 15 runs after a warm-up |
EF Core 11 translates the choices' ID lists to `IN (@Ids1, …)` (`ToQueryString`). Variance: the cold first runs after copying a 226 MB file dominate (about 0.7 s); warm runs vary by a few percent. With the `sqlite3` CLI (3.54.0) the same lookup took 90 ms → 0.01 ms and the `IN (SELECT … WHERE FacilityID = ?)` form 121 ms → 8 ms; a plain `JOIN` of campsites and attributes kept the full scan even with the index (727 ms; the planner without statistics prefers scanning the attribute table), which is why the choices use an ID list. Creation: 0.57 s (CLI) and 682 ms (`RidbIndexes.Create` in `RidbIndexesTests`), 226,471,936 → 261,906,432 bytes; a second `Create` and `EnsureQuietly` write nothing (same SHA-256 and modification time). Contents: every table's rows equal the original's (`EXCEPT` both ways, counts equal), `PRAGMA integrity_check` ok, `RidbDatabase.Validate` passes, the schema gained exactly the index.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Debug` and `-c Release`, `--no-incremental` | 0 errors, **56 warnings** each (70 on `master` `fd65cf3`) |
| Warning identities vs `master` built the same way (file, code, message; and with positions) | 44 identities on `master`, 42 on the branch: none added; removed are two `AvailabilityApi.cs` identities (CS8604 for `List.Contains` and `Enumerable.Any`), with 14 positions, of the replaced filter code; the other `AvailabilityApi.cs` positions moved up by 3 lines (after the type filters) or 24 (after `FilterData`'s conditions); Debug and Release equal |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **1,194 tests: 1,006 `CampertronUnitTests` (986 passed, 20 Windows-only skipped) + 188 `CampertronGUITests`**, 0 failed. 77 new: `SearchFiltersTests` 23 cases, `FilterSemanticsTests` 7, `FilterMatchingTests` 29 cases, `FilterChoicesTests` 8 cases, `RidbIndexesTests` 7, `LegacyAddressLayoutTests` +3 cases; adapted: `TheCodes_AreTheDocumentedOnes`, `ASearchFile_OpensWithEverySetting`, the example's bytes in `SettingsBootstrapTests`, `ModelKeys_AreTheKeysCampertronsWriterWrites`, and `AtomicRefreshTests`' in-place comparison (the in-place build now builds the index; byte equality without SQLite's three header counters, business-rules §8) |
| The same suites in a network namespace with only loopback (`unshare -rn sh -c 'ip link set lo up && …'`; without `lo` up vstest's data collector can't connect) | 188 GUI passed; library 970 passed, 36 skipped (the 20 Windows-only tests and 16 permission tests that skip for a privileged process, which the namespace's root mapping is; the new read-only-database test is one of them), 0 failed |
| `bash .github/release-policy/test-release-policy.sh` | 410/410 (unchanged) |
| `dotnet list <project> package --vulnerable --include-transitive`, five projects | no vulnerable packages |
| `git diff --check` | no whitespace error |

**Negative controls** [V]. Each a temporary edit in a separate worktree of the branch's commit (never the working tree), built and run against the focused suites (176 tests: `SearchFiltersTests`, `FilterSemanticsTests`, `FilterMatchingTests`, `FilterChoicesTests`, `RidbIndexesTests`, `LegacyAddressLayoutTests`, `AtomicRefreshTests`, `SettingsBootstrapTests`), then restored; the restored build succeeded and the worktree was clean:
| # | Mutation | Failing (of 176) | For example |
|---|---|---|---|
| 1 | excludeEquipment back to AND | 3 | `ExcludeEquipment_DropsACampsiteWithAnyValue` (both modes), `ExcludeEquipment_IsOr_InBothModes` |
| 2 | includeCampsiteType back to AND | 3 | `IncludeCampsiteType_KeepsACampsiteOfAnyType` (both), `IncludeCampsiteType_IsOr_InBothModes` |
| 3 | excludeAttributes back to AND | 3 | `ExcludeAttributes_DropsACampsiteWithAnyValue` (both), `ExcludeAttributes_IsOr_InBothModes` |
| 4 | Exact behaves as Contains | 2 | `Exact_IsTheWholeValue_CaseSpacesAndPunctuationCount`, `ExactAndContains_DifferWherePartsAreUsed` |
| 5 | Contains behaves as Exact | 5 | `AFileWithoutFilterMatching_UsesContains`, `Contains_IsTheMatchingBeforeD51_InItsDirectionPerList`, `TheExampleSearch_FindsWhatItFoundBefore_NowWithExactValues` |
| 6 | A file without `filterMatching` defaults to Exact | 7 | `AFileWithoutIt_UsesContains_WithoutADiagnostic`, `AnUnrelatedEdit_NeverAddsIt`, `AStart_RefusesItInAnEnabledSearch_AndIgnoresADisabledOne` |
| 7 | A new search keeps the legacy Contains | 3 | `ANewSearch_IsWrittenWithExact`, the example's bytes, `TheExampleSearch_…` |
| 8 | An empty `filterMatching` accepted | 4 | `OtherSpellings_AreAnError_NeverAGuess("", "~", "null")` |
| 9 | The start skips the text check | 1 | `AStart_RefusesItInAnEnabledSearch_AndIgnoresADisabledOne` |
| 10 | The choices trim campsite names | 1 | `Values_AreKeptAsStored_UntrimmedBlankColonsAndCase` |
| 11 | The choices merge case variants | 3 | `TheChoices_AreTheCampgroundsOwnValues` (232445, 232490), `Values_AreKeptAsStored_…` |
| 12 | The repair refuses Campertron's index | 2 | `Repair_LegacyDatabaseWithCampertronsIndex_IsStillReplaced_AlsoByABundledDatabaseWithIt`, `OnTheShippedData_ItIsBuiltOnce_…` |
| 13 | The start does not index | 2 | `AFirstStart_IndexesTheBundledDatabaseItExtracts`, `AStart_IndexesAnInstalledDatabaseOnce_ThenOnlyReadsIt` |
| 14 | The refresh does not index | 2 | both `AtomicRefreshTests` in-place comparisons |
| 15 | The choices take equipment rows of other campsites | 6 | `MeasuredFacts_OfTheShippedData`, `RowsOfCampsitesThatAreGone_AreNeverOffered`, `TheChoices_AreTheCampgroundsOwnValues` |

**Real application** [V]: the Debug `CampertronGUI --smoke-test` in a nested KWin session (`dbus-run-session --config-file=<bus without service directories, socket under /tmp/claude-1000/crt> -- kwin_wayland --virtual --xwayland --exit-with-session=<script>`) with a new, empty `XDG_DATA_HOME`: exit 0, empty standard error, accepted by `check_gui_smoke_run`, `search-probe=pass …` (New search now writes `filterMatching: Exact`), `campgrounds=4966`, `run-probe=pass`; the extracted database had `IX_CampsiteAttributesEntries_EntityID` after the first start. The Debug console with `outputTo: Email`, a complete `Email.yaml` and an enabled search with `filterMatching: 1`, in a network namespace without any interface up: "Error: Bad.yaml could not be used: filterMatching must be Exact or Contains; a search file without filterMatching uses Contains.", the file and the advice, exit code 1, before any request; the search file byte for byte unchanged, no `Camp.History`.

**Isolation** [V]: scratch folders only (`XDG_DATA_HOME` under the session's scratch directory for every test, harness and application run); the real data folders' fingerprint (paths, sizes, modification times and modes of `~/.local/share/CampertronConfig`, `CampertronCache`, `CampertronGUI`, and SHA-256 of the files in the first and the last) is identical before and after; the real `RecreationDotOrg.db` was only copied (`cp`), its SHA-256 unchanged (`a825ce99…`). No request reached recreation.gov.

**Regression checks for later changes**: business-rules §3, §8, §15, §22 and CLAUDE.md's filter rule list the suites; rerun the negative controls above on the changed part.

### AR. Database-backed filter selectors, stage 2: the shared selector, equipment and campsite types, the matching choice (decision D52, branch `feat/filter-selectors-equipment-types`, 2026-11-16)
GUI, its tests, the release policy's smoke-report check, two library adjustments, documentation: `CampertronGUI/ViewModels/FilterSelectorField.cs`, `FilterChoiceSource.cs` (new), `SearchFileViewModel.cs`, `SearchesViewModel.cs`, `SettingsFileViewModel.cs`, `Views/FilterSelectorView.axaml(.cs)` (new), `SearchFileView.axaml`, `FilterProbe.cs` (new), `SmokeTest.cs`; `CampertronLibrary/function/RecDotOrg/search/SearchFilters.cs` (moved from `api`, unchanged), `ConfigEditSession.cs` (`InFile`, `MustRewrite`); `.github/release-policy/release-policy.sh` (`GUI_SMOKE_KEYS`, `check_gui_smoke_report`) and `test-release-policy.sh`. No dependency, project file, workflow or `global.json` change; the embedded database is unchanged.

**Starting state** [V]: `master`, `origin/master` and `HEAD` at `95c4d7c` (the merge of PR #71, stage 1), post-merge CI 43984671514 successful, the stage-1 branch deleted locally and remotely, one worktree, no stash, no open pull request; tags, releases and the superseded candidate as in §AQ.

**Build, tests, policy** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln -c Debug` and `-c Release`, `--no-incremental` | 0 errors, 56 warnings each; identities and positions identical to `master` (`95c4d7c`) |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **1,218 tests: 1,009 `CampertronUnitTests` (989 passed, 20 Windows-only skipped) + 209 `CampertronGUITests`**, 0 failed (21 + 3 new) |
| The same suites in a network namespace with only loopback | 209 GUI passed; library 973 passed, 36 skipped (the 20 Windows-only tests and 16 permission tests skipped as the namespace's root), 0 failed |
| `bash .github/release-policy/test-release-policy.sh` | **418/418** (8 new `filter-probe` cases: missing, failed, read on the UI thread, without the index, a selector that did not toggle, without campsites, before the Searches tab, without a check) |
| `dotnet list <project> package --vulnerable --include-transitive`, five projects | no vulnerable packages |
| Each commit alone (a worktree per commit: build, its suites) | `0069c91` (library) and `d343c90` (GUI, policy, adapted tests) each built and passed the whole suite (1,006 + 188); `2669e28` is the branch's tip above. A first try of this check failed 13 and 92 library tests within about 60 ms each: the session's `/tmp` (a 16 GB tmpfs holding the scratch folders and every test's copies of the 226 MB database) was full; after removing the session's own isolated data folders both commits passed. Keep an eye on free space in `/tmp` when many isolated runs accumulate. |

**Tests added and adapted** [V]: `FilterSelectorTests` (21, new): the campground's values and checks (Alpha: `Tent` and `tent` grouped, counts, accessible names, the check boxes neither focusable nor exposed), checking appends one item and the save keeps every comment (`+   - Trailer`, the backup exact), unchecking removes every exact item, groups, kept values with their positions, descriptions and the validation's message, an unrelated edit keeps the list and adds no `filterMatching`, typed values exactly as typed and never twice, Exact and Contains descriptions and "also matches", the matching choice (not set, Exact, back to not set: no edit), Bravo's `RV/MOTORHOME` also matching `RV`, the include/exclude warning, no campground, campground changes (one read, the kept values, the cache when going back), a refreshed database read again, the keyboard (Down, Space, Tab out to the typed-value box, Shift+Tab back into the list), a locked list, a refused `filterMatching: 1` rewritten by choice and New search's Exact, the worker thread with an injected source and with the production source behind an exclusive lock (rollback-journal copy: in WAL mode readers are not blocked), an older answer ignored, a failure with Retry (edits do not retry), long lists (find, Select/Clear shown), only the shown file reading. `FilterMatchingTests` +3 cases (a refused spelling replaced by the value it loads as, the quotes kept). Adapted: `SearchEditorTests` (the shown file's 3 queries counted after they are in; the button allow-list and the IL boundary types include the selectors), `SmokeTestTests` (the `filter-probe` line), `GuiTestEnvironment` (campsites, equipment and attributes for Alpha and Bravo; the index a start builds).

**Negative controls** [V]. Each a temporary edit in a separate worktree of the branch's commit, built and run against `FilterSelectorTests`, `SearchEditorTests`, `SmokeTestTests`, `NavigationTests` and `MainWindowUsabilityTests` (70 tests; the library's `FilterMatchingTests` too where the library changed; the policy tests for #15), then restored; the restored build passed the same 70:
| # | Mutation | Failing | For example |
|---|---|---|---|
| 1 | A checked value is added first instead of last | 2 | `CheckingAValue_AddsOneItemAtTheEnd_…`, `AGroup_ChecksEverySpelling_…` |
| 2 | Unchecking removes only the first exact item | 1 | `UncheckingAValue_RemovesEveryItemThatIsExactlyIt_AndNothingElse` |
| 3 | Case variants not grouped | 5 | `TheSelectors_ShowTheCampgroundsValues_…`, `AGroup_…`, `TheSameValueIncludedAndExcluded_…` |
| 4 | A typed value is trimmed | 1 | `AValueIsAddedExactlyAsTyped_NeverTwice` |
| 5 | Values the campground lacks are dropped when its values load | 5 | `ChangingTheCampground_ReadsItsValues_AndTheListStaysAsItIs`, `ALongList_…`, `CheckingAValue_…` |
| 6 | An older answer is applied | 1 | `AnOlderAnswer_NeverReplacesTheValuesOfANewerCampground` |
| 7 | The production source reads on the UI thread | 1 | `TheProductionSource_NeverReadsOnTheUiThread` (30 s: the UI thread waited for the lock) |
| 8 | The TD-39 workaround removed (`WrapTabAtContentEdge`) | 3 | `Keyboard_ArrowKeysSwitchTabsAndTabEntersTheContent`, `Keyboard_TabCyclesThroughHeaderSelectorsDetailsIdAndCopyButton`, `Keyboard_ReachesEveryRowAndTab_AndWrapsOnBothLevels` |
| 9 | "Not set" writes Contains | 1 | `ExactAndContains_SayWhatEachValueMatches` |
| 10 | A refused `filterMatching` can't be rewritten (`MustRewrite` ignored) | 1 GUI, 3 library | `ANewSearch_ShowsExact_AndAnInvalidValueIsKeptUntilChosen`, `ARefusedSpelling_IsReplacedEvenByTheValueItLoadsAs` (3 cases) |
| 11 | Space does nothing (the tunnel handler removed) | 1 | `Keyboard_TheListIsOneTabStop_ArrowsMove_AndSpaceChecks` |
| 12 | A locked list takes edits (the selector's and the file's checks removed) | 1 | `ALockedList_IsShownButNotEditable` |
| 13 | A refreshed database is not noticed | 1 | `ARefreshedDatabase_WhileTheWindowIsOpen_IsReadAgain` |
| 14 | Every listed file reads its values | 2 | `AListedFile_ReadsNothing_UntilItIsShown`, `TheSearchesTab_ListsTheSearchFiles_WithTheirState` |
| 15 | Release policy without `filter-probe` in `GUI_SMOKE_KEYS` | 10 of 418 | every case that expects the new report to pass |

**Real application** [V]: the Debug `CampertronGUI --smoke-test` in the nested KWin session with a new `XDG_DATA_HOME`: exit 0, empty standard error, accepted by `check_gui_smoke_run`, `filter-probe=pass campground=274410 campsites=22 equipment=11 types=3 names=22 attributes=73 thread=worker index=yes selector=toggled` (the first listed campground with campsites in the bundled data). A real-window review (a scratch launcher outside the repository, assembly name `CampertronGUITests`, the production `Program.BuildAvaloniaApp`, the window at 1000×890 and scale 1, `import -window` of the X11 window) of a copy of the Zion example (Exact) in Dark and of a pre-D51 file (no `filterMatching`; `TENT`, `Hovercraft`, `excludeEquipment: [Tent]`, `excludeCampsiteType: [GROUP]`) in Light: the Matching choice, the 11 equipment types and 5 types of Watchman with their campsites, the checked values, the kept values with their descriptions, "also matches “CAR”" for `CARAVAN/CAMPER VAN` under Contains, the warning under `excludeEquipment`, and a keyboard-focused row. The first review showed the list's selected (cursor) row filled with the accent color and an inner scroll for the 11 equipment types; both were changed (no fill for the cursor, "checked" wording, short lists shown whole, find and bulk actions only above 12 values) and the review repeated.

**Isolation** [V]: scratch folders only; the real data folders' fingerprint unchanged; no request to recreation.gov.

**Regression checks for later changes**: business-rules §23 and CLAUDE.md's selector rule list the suites; rerun the negative controls above on the changed part and the real-window review for layout changes.

### AS. Database-backed filter selectors, stage 3: the campsite selectors (decision D53, branch `feat/filter-selectors-campsites`, 2026-11-16)
GUI and its tests only: `CampertronGUI/ViewModels/FilterSelectorField.cs`, `SearchFileViewModel.cs`, `Views/SearchFileView.axaml`, `FilterSelectorView.axaml`; `CampertronGUITests/CampsiteSelectorTests.cs` (new), `GuiTestEnvironment.cs` (Charlie's eight campsite names). No library, policy, workflow, dependency or database change.

**Starting state** [V]: `master` at `a2ee15b` (the merge of PR #72, stage 2), its post-merge CI 43990180364 successful on attempt 1, the stage-2 branch deleted locally and remotely; PR #72's automatic runs: CI 43989159009 and Release dry run 43989159028 (27 jobs succeeded, the 2 publishing jobs skipped; on all seven smoke targets `filter-probe=pass campground=274410 campsites=22 equipment=11 types=3 names=22 attributes=73 thread=worker index=yes selector=toggled`, so the start built Campertron's index natively on Linux x64 and arm64, Windows x64, arm64 and x86 (on 64-bit Windows), macOS Intel and Apple silicon). The branch was prepared in a worktree on stage 2's commits and rebased onto `a2ee15b`.

**Build, tests** [V]
| Check | Result |
|---|---|
| `dotnet build Campertron.sln`, Debug and Release, `--no-incremental` | 0 errors, 56 warnings; identities and positions identical to `master` |
| `XDG_DATA_HOME=<new temp dir> dotnet test Campertron.sln -c Release --no-build --blame-hang-timeout 10m` | **1,224 tests: 1,009 `CampertronUnitTests` (989 passed, 20 Windows-only skipped) + 215 `CampertronGUITests`**, 0 failed (6 new) |
| The same in a network namespace with only loopback | 215 GUI passed; library 973 passed, 36 skipped, 0 failed |
| Release policy, vulnerable packages | 418/418 (unchanged); no vulnerable packages (no package change) |
| The GUI commit alone (`2758e17`) | built; GUI suite 209/209 (its own tests come with the next commit) |

**Timings** [V] (`CampsiteSelectorTests`, Debug, headless, 12-core x86-64): find, check 10, Select all shown on 360 names and Clear shown, with the editing session's edits: 1.24 to 1.34 s in all; one change among 360 checked names: the selector 1.2 to 1.4 ms, the session's edit and validation 740 to 968 ms (360 unsaved inserted items replanned, one verified editor step each); after a save (19 ms for 361 items), one change among them 10 ms; with 9 checked names, 10 ms. Before the selector's rebuild was made linear, one change among 360 checked names took 373 ms in all.

**Negative controls** [V] (a separate worktree at the stage's commit, against `CampsiteSelectorTests` and `FilterSelectorTests`, 27 tests; restored: 27/27):
| # | Mutation | Failing | For example |
|---|---|---|---|
| 1 | Names in the list's order instead of their natural order | 1 | `TheNames_AreTheCampgroundsOwn_InTheirNaturalOrder_SharedNamesOnce` |
| 2 | A shared name counted as plain campsites | 3 | `TheNames_…`, `NamesTheCampgroundLacks_AreKept_AndSayTheyMatchExactly`, `TheLargestCampground_IsSearchable_Virtualized_AndQuick` |
| 3 | Kept names described with Contains matching | 1 | `NamesTheCampgroundLacks_AreKept_AndSayTheyMatchExactly` |
| 4 | A checked name is trimmed | 1 | `ASharedName_IsOneItem_AndBlankAndPaddedNamesAreWrittenExactly` |
| 5 | No message on a checked value's row | 1 | `ASharedName_IsOneItem_…` |
| 6 | Long lists not bounded (every row realized) | 3 | `TheLargestCampground_InTheWindow_RealizesOnlyTheRowsInView`, `TheLargestCampground_IsSearchable_…`, `ALongList_CanBeNarrowed_…` |
| 7 | No warning for a name included and excluded | 1 | `TheSameNameIncludedAndExcluded_IsWarned` |
(An eighth, "unchecking a shared name removes one item", did not apply: its text occurs twice in the file; the removal of every exact item is stage 2's control 2.)

**Real window** [V]: the scratch launcher of §AR with a copy of the Zion example pointed at 258830 (`includeSites: [A001, Z999]`), Dark, 1000×890, scale 1: the campground's values were in within the first 400 ms polling interval; `includeSites` and `excludeSites` 351 rows each, 8 realized, the list 320 high; natural order 1, 2, 3 …; "2 campsites share this name"; Select all shown and Clear shown; A001 and Z999 kept with "campsite names always match exactly".

**Isolation** [V]: scratch folders only; the real data folders unchanged; no request to recreation.gov.

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
- **Windows settings-file job** (since CM2, D40, §AE): `windows-settings-files` ("Windows settings files (CM1, CM2)" until D41) runs with the same triggers on the pinned `windows-2025` runner: SDK and package policy, a Release build of the library and the unit tests, and the CM1 and CM2 suites (`YamlDocument*`, `ConfigFileReplace*`, `GeneralYamlTimestampTests`, `SettingsBootstrapTests`), including `ConfigFileReplaceWindowsTests`, which run only on Windows, since D41 the CM3 suites (`ConfigFilesTests`, `SettingsLoaderLimitsTests`, `ConfigValidationTests`, `EmailOutputValidationTests`; named "Windows settings files (CM1 to CM3)") since D43 the settings editor's session suites (`ConfigEditSessionTests`, `ConfigEditEmailTests`; named "Windows settings files (CM1 to CM3, CM5)"; its summary fails the job unless `ConfigEditEmailTests.TheAccessRulesOnWindows_AreNeverBroadened` passed) and since D44 the search-file suites (`SearchFileSessionTests`, `SearchFilesTests`; named "Windows settings files (CM1 to CM3, CM5, CM6)"); it checks that the runner user's `CampertronConfig` and `CampertronCache` never exist and summarizes the results (per class, skipped tests with reasons) in the job summary. It is not a required check of the ruleset; the CM2 merge required it green. A newer push to the same PR cancels an in-progress PR run. `master` runs are never cancelled.
- **Permissions:** `contents: read` only, which `actions/checkout` needs to fetch the repo with `GITHUB_TOKEN`. All other scopes are `none`. No secrets are used, and there is no AI or Claude automation.
- **Actions,** pinned to full commit SHAs:
  - `actions/checkout` v7.0.1 (`3d3c42e5aac5ba805825da76410c181273ba90b1`), with `persist-credentials: false`.
  - `actions/setup-dotnet` v6.0.0 (`a98b56852c35b8e3190ac28c8c2271da59106c68`), with `dotnet-version: "11.0.x"` (D31; `"10.0.x"` before). It **only installs** an SDK next to the runner image's preinstalled SDKs (8.0–11.0); the root `global.json` decides which one `dotnet` uses. Before D31, runner images from 20261111.x preinstalled SDK 11.0.100 and, without `global.json`, `dotnet` picked it although setup-dotnet had asked for 10.0.x (§U).
- **Repository setting:** GitHub Actions was disabled on the repo until the owner enabled it on 2026-11-10. The API now reports `enabled: true` with `allowed_actions: selected`, `github_owned_allowed: true` and `verified_allowed: false`, so only `actions/*` actions can run. Any third-party action added to the workflow will be blocked until that setting changes.

CI commands, in order:
```bash
source .github/release-policy/release-policy.sh              # curated release notes (decision D30, §S); read-only, publishes nothing
check_curated_notes_dir .github/release-notes "ghcr.io/<owner lowercase>/campertronconsole" "$GITHUB_REPOSITORY"   # preview + SHA-256 to the job summary; any invalid fragment fails the job
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
- **Not covered by CI:** `docker-compose.dcproj` (skipped by build and scan), and the GUI on a real platform backend (rendering, D-Bus shutdown, TD-34). The headless GUI tests (`CampertronGUITests`, §K) run in `dotnet test Campertron.sln`. Docker image builds, packaging for all 8 targets and the native console and GUI smoke tests on linux-x64, win-x64, win-arm64, osx-x64 and osx-arm64 (R8, §X) and, since S2 (§AM), on linux-arm64, the win-x86 run on 64-bit Windows and the native run of the linux/arm64 container image are covered by the release workflow's dry runs (§Release workflow), not by `ci.yml`. Since D40 `ci.yml` also runs the settings-file suites (CM1, CM2) on Windows; the rest of the tests run only on Ubuntu, and `ci.yml` runs nothing on macOS. Pull requests that change release inputs or `CampertronGUI/**` get that dry run automatically.
- **Network risks:** ~~the tests call recreation.gov.~~ Since D45 (GS1, §AJ) no test reaches the network (`NoLiveNetwork` fails the run otherwise), so CI no longer depends on recreation.gov. Before: one test made a live request; network errors retried forever in app code (the hang and step timeouts caught that), and a "REQUEST BLOCKED" response would have slept 5 minutes and exited the test host.

First run [V]: run 42914891230 (PR #14, `pull_request` event, 2026-11-10 22:28 UTC), **success** in about 1m 9s.
- Runner: Ubuntu 26.04 image, .NET SDK **10.0.404**.
- Restore: no NuGet warnings.
- Build: **0 errors, 87 warnings**, matching the local baseline.
- Test: **9/9 passed** (14 s), with `XDG_DATA_HOME=/home/runner/work/_temp/campertron-data`.
- Scan: 0 vulnerable packages in all 4 `.csproj` projects, with `docker-compose.dcproj` skipped.
- 0 error annotations. The 174 warning annotations are the existing compiler warnings.
- The first `push`-to-`master` run [V]: run 42916238884 (push of merge commit `5ee2ff6`, 2026-11-10 22:37 UTC), **success**. .NET SDK 10.0.404; build 0 errors / 87 warnings; tests 9/9; 0 vulnerable packages in all 4 projects; 0 error annotations.

## Release workflow (`.github/workflows/release.yml`)
Added in PR #18 (`ci/release-workflow`); the `assemble` job was added in PR #19 (`ci/release-assembly`). The **first real release, `v0.1.0-beta.4`, was published by this workflow on 2026-11-11** and every job succeeded (§Release history). The release-related repository settings are applied (§Release protections).

**Triggers and modes**
| Trigger | Mode | Version |
|---|---|---|
| Push of a tag matching `v[0-9]+.[0-9]+.[0-9]+*` | **publish**, only if the tag is SemVer (`vMAJOR.MINOR.PATCH[-prerelease]`, no leading zeros), its commit is an ancestor of `origin/master`, and no GitHub release with that tag exists. Otherwise `verify` fails and nothing runs | tag without `v` |
| `workflow_dispatch` | dry run, always: there is no input that publishes | `0.0.0-dryrun.<run number>` |
| `pull_request` to `master` touching `release.yml`, `global.json`, `CampertronConsole/Dockerfile`, `.dockerignore`, `**/*.csproj`, `Campertron.sln`, `CampertronLibrary/content/**`, `.github/release-policy/**` or (since R8) `CampertronGUI/**` | dry run | `0.0.0-dryrun.<run number>` |

- **Every published version becomes a normal GitHub release marked Latest** (`--latest`, never `--prerelease`), so the newest release always shows on the repository page. GitHub does not allow a prerelease to be Latest (REST docs for `make_latest`: "Drafts and prereleases cannot be set as latest"), so pre-release versions are not published as GitHub prereleases.
- A version with a hyphen is still a **SemVer pre-release version** (`prerelease=true` from `verify`) for everything except the GitHub release flag. The image gets only its exact tag, Docker `latest` and `<major>.<minor>` are not updated, and the notes are worded as a pre-release version. A stable version gets the exact tag, `<major>.<minor>` and `latest`.
- The policy lives in `.github/release-policy/release-policy.sh` (`docker_tag_args`, `github_release_flags`, `expected_archives`, `render_notes`, `validate_notes`; the curated-notes functions of §S: `validate_curated_fragment`, `compose_notes`, `assemble_release_notes`, `check_curated_notes_dir`, `release_body_from_json`, `compare_release_body`, `check_release_view`; and the .NET 11 guards (D31: `check_global_json`, `check_selected_sdk`, `check_package_policy`, `check_pdb_provenance`, `check_single_toolchain`, `pdb_option`), `check_project_frameworks`, `check_publish_runtime`, `check_framework_dependent_runtimeconfig`, `check_framework_report`; and the GUI smoke-test checks of R8, `check_gui_smoke_report` and `check_gui_smoke_run`, with `NATIVE_SMOKE_RIDS_DEFAULT`), which the workflow steps source. It is tested by `.github/release-policy/test-release-policy.sh` (388 cases since §AM, 348 since §AK, 337 since §AI, 322 since §X; 277 since §W: prerelease and stable notes, pass/fail fixtures for every runtime guard (§I, retargeted to .NET 11 in §U) and for the .NET 11 SDK, package, PDB and toolchain guards (§U), the curated-notes cases (§S), the workflow-step tests of the PDB-provenance checks (§V), and the OCI image, publication and job-structure tests (§W)).
- **Curated release notes (decision D30, §S):** each release's reviewed notes are a tracked fragment `.github/release-notes/v<version>.md` (format: `.github/release-notes/README.md`), merged to `master` through its own PR before the tag. A publishing tag run composes it with the generated sections and publishes that body in the draft; it never falls back to template-only notes. `.github/release-policy/ReleaseCheck/` is a small net11.0 console project, not in `Campertron.sln` and not shipped, that the smoke job uses to check the GUI state data (§I). [V] The directory is deliberately not called `release/`: the Visual Studio `.gitignore` pattern `[Rr]elease/` (line 22) ignores any directory with that name, so files in `.github/release/` would never be committed.
- The version comes only from the tag (`-p:Version`). The only file committed to `master` for a release is its curated notes fragment, through a PR before the tag.
- Concurrency group `release-<ref>`; only PR runs are cancelled by newer pushes.
- Job names differ from the required check `Restore, build, test, vulnerability scan`, so the workflow cannot satisfy or block that check. Dry-run results are visible on the PR but are not required by the ruleset.

**Jobs** (`needs` in brackets)
1. `verify` "Release verify": version and mode gate (above); runs `.github/release-policy/test-release-policy.sh`; **checks the curated release notes** (`check_curated_notes_dir`: every tracked fragment validates and composes for its own version; each composed body and its SHA-256 goes to the job summary) on every trigger, and **for a publishing tag requires its exact fragment** `.github/release-notes/v<version>.md` in the tagged commit (`assemble_release_notes true`, then `validate_notes`), so a missing or invalid fragment fails before any build; a dry run reports that its synthetic version needs none; **checks that every project targets .NET 11** (`check_project_frameworks`: every `.csproj` has exactly `<TargetFramework>net11.0</TargetFramework>`; no `TargetFrameworks`, `RuntimeFrameworkVersion` or `RollForward` in any `.csproj`, `Directory.Build.props/.targets` or `.pubxml`; the root `global.json` is the approved SDK 11 policy (`check_global_json`, D31); every .NET base image in the Dockerfile is `:11.0`); then the SDK check (`check_global_json`, `check_selected_sdk`), restore, the package policy (`check_package_policy`), and the same Release build, isolated tests and vulnerability scan as `ci.yml` (since S2, §AM, also its guard: a scan that finds no project fails). **A test failure blocks the release.**
2. `binaries` "Package <app> <rid>" [verify]: a 16-job matrix, app {console, gui} × rid {linux-arm, linux-arm64, linux-x64, osx-arm64, osx-x64, win-arm64, win-x64, win-x86}, with `fail-fast: false`.
   - Publish: `dotnet publish -c Release -r <rid> --self-contained true -p:Version -p:ContinuousIntegrationBuild=true`. Not single-file, not trimmed.
   - Check: the host executable, `CampertronLibrary.dll`, native SQLite and `System.IO.Compression.Native` must be present. **Windows GUI packages (TD-35 guard, §L):** `libSkiaSharp.dll`, `libHarfBuzzSharp.dll` and `av_libglesv2.dll` must be present; `libSkiaSharp.pdb` and `libHarfBuzzSharp.pdb` must be absent, and `CampertronGUI.deps.json` must not list them as native assets. Campertron's own PDBs are allowed.
   - **SDK check** (D31) right after `setup-dotnet`: `check_global_json`, `check_selected_sdk "$(dotnet --version)"` and `dotnet --info` in the log; the same step runs in `verify` and `smoke`, and `ci.yml` has it too.
   - **.NET 11 check** (`check_publish_runtime`, on the exact directory that becomes the archive): `<app>.runtimeconfig.json` has `tfm` `net11.0`, no shared-framework reference (self-contained) and only `Microsoft.NETCore.App` 11.x in `includedFrameworks`; `<app>.deps.json` targets `.NETCoreApp,Version=v11.0/<rid>` with exactly one runtime pack, `Microsoft.NETCore.App.Runtime.<rid>/11.x`; **compiler provenance** (`check_pdb_provenance`): `CampertronLibrary.pdb` and `<app>.pdb` record a compiler running on .NET 11 (official `11.0.0-rtm.…` or source-built `11.0.0-dev` stamps); `System.Private.CoreLib.dll` contains only .NET 11 informational versions (the version `RuntimeInformation.FrameworkDescription` reports); and no JSON file in the output references another .NET major (`"netN.0`, `.NETCoreApp,Version=vN.` or a runtime pack `/N.`; package asset paths such as `lib/net8.0/` are allowed). Any failure fails that matrix job, so no archive is uploaded and `assemble` cannot find 16.
   - Archive: `campertron-<app>-<version>-<rid>` as `.zip` (Windows, Info-ZIP `zip -r -X`) or `.tar.gz` (Linux/macOS, `--sort=name --owner=0 --group=0`). Each archive has one top-level folder of the same name.
   - Upload as a workflow artifact with `archive: false` (the file itself, 7-day retention).
3. `smoke` "Smoke test <rid> (native)" [verify, binaries]: since S2 (D48, §AM) a 2-entry matrix, `fail-fast: false`: linux-x64 on `ubuntu-latest` and linux-arm64 on the pinned standard runner `ubuntu-24.04-arm` (before S2 only linux-x64, as "Smoke test linux-x64 (native)"). `RUNNER_ARCH` must be the archive's architecture (no emulation), and each `--version` must come from a process of that architecture. The steps below run for both:
   - The published console must extract the bundled DB in a clean `XDG_DATA_HOME` (`outputTo: UnitTest`) and exit 0, with no `.partial` file left.
   - **GUI smoke test** (since R8, §X; before: the GUI only had to be running after 30 s under `xvfb-run`): `CampertronGUI --smoke-test` under `xvfb-run` with a clean `XDG_DATA_HOME` must exit 0 with an empty stderr and a report that passes `check_gui_smoke_run` (window with a native handle, ready state, data folder `$XDG_DATA_HOME/CampertronConfig`, rendered frame, parks and states, since D43, D44 and D46 the settings, search-editor and Results-tab run probes, programmatic close, clean shutdown, this version); the GUI must have created the DB. Step timeout 5 minutes (the smoke test's own: 60 s).
   - **Runtime reported by the published apps:** `CampertronConsole --version` and `CampertronGUI --version` (no display) must print exactly one `Framework => .NET 11.x.y (<arch>)` line whose version **equals the `Microsoft.NETCore.App` version bundled in that archive** (`check_framework_report`), which shows the process ran on the bundled runtime; the first line must be `<app> <VERSION>[+<commit>]`.
   - **GUI state data:** builds `ReleaseCheck` and runs it twice, on the published GUI and on the published console, each against the `RecreationDotOrg.db` that app extracted. It loads the **published** `CampertronLibrary.dll` and its dependencies from the archive and runs that library's own code: `RidbDatabase.Validate` passes; `LegacyAddressLayout.DetectLayout` is Corrected with only known schema objects; `Read.UniqueStates` is non-empty, all `^[A-Z]{2}$`, no digits, no `01005`, contains AZ, MA and UT; `UniqueCities("MA")` contains BARRE; `UniqueParksByState("MA")` is non-empty; `UniqueCities("01005")` and `UniqueParksByState("01005")` are empty; `UniqueParks("UT", "SPRINGDALE")` contains WATCHMAN CAMPGROUND.
3b. `native-smoke` "Smoke test <rid> (native)" [verify, binaries] (R8, D33, §X), read-only, `fail-fast: false`, a matrix on pinned GitHub-hosted standard runners: win-x64 `windows-2025`, win-arm64 `windows-11-arm`, osx-x64 `macos-26-intel`, osx-arm64 `macos-26`, and since S2 (D48, §AM) a fifth entry, win-x86 on `windows-2025`, named "Smoke test win-x86 (64-bit Windows)": the 32-bit archives run as 32-bit processes (`(X86)` in `--version`) on the 64-bit runner (`RUNNER_ARCH` X64). That entry is **compatibility execution**, not a native run (no hosted runner is a 32-bit Windows system); its matrix entry carries `execution: 64-bit Windows` and the release policy lists it in `COMPAT_SMOKE_RIDS_DEFAULT`, not in `NATIVE_SMOKE_RIDS_DEFAULT`. No SDK is installed (the apps are self-contained) and nothing is built.
   - Downloads the two archives of its RID from this run (`skip-decompress: true`), requires `RUNNER_ARCH` to be the native architecture, extracts them (`Expand-Archive` on Windows, `tar` on macOS).
   - **Console:** `--version` must report this version on the bundled .NET 11 runtime (`check_framework_report`) in a native process (`(X64)`/`(Arm64)`); a clean install with `outputTo: UnitTest` must extract the DB (≥ 100 MB, no `.partial`) into the runner user's real data folder, which must not exist before: `%LOCALAPPDATA%\CampertronConfig` on Windows, `~/Library/Application Support/CampertronConfig` on macOS (.NET asks macOS for it through `NSApplicationSupportDirectory`, so `HOME` cannot redirect it). The step then removes the folders it created.
   - **GUI:** `--version` as above; `CampertronGUI --smoke-test` from the empty folder must exit 0 with an empty stderr and a report that passes `check_gui_smoke_run` with that data folder and this version, and the GUI must have extracted the DB. Steps time out after 5 minutes; a failed job uploads only its smoke logs (`native-smoke-logs-<rid>`, 7 days).
   - First run: §X (PR #47). The Windows jobs need about 1–2 minutes, the macOS jobs under 1 minute after start.
4. `docker` "Docker build (linux/amd64, linux/arm64)" [verify], read-only, never contacts a registry (D32, §W):
   - `docker buildx` (docker-container driver) builds both architectures **once** and exports that build as the OCI archive `$RUNNER_TEMP/container-image`.
   - **Both platform images are checked in the archive without running them** (`check_oci_archive`: platforms, attestation manifests, blob and diff_id digests, .NET 11, labels, user, Ubuntu 26.04, native SQLite architecture, PDB compiler provenance, one compiler for both). The index, platform manifest and config digests, compilers and the archive SHA-256 become job outputs.
   - The linux/amd64 image is loaded (`--load`, same builder) and must be the validated amd64 image (`check_loaded_image`: the same manifest or config digest and the same layer diff_ids). It is then run twice:
     - without volumes, it must fail with `Config path does not exist`
     - with `/config` and `/cache` mounted, it must extract the bundled DB inside the container.
   - The image's `org.opencontainers.image.version` label must equal the version.
   - **Container runs on .NET 11:** the image's `DOTNET_VERSION` is 11.x; `/app/CampertronConsole.runtimeconfig.json` (framework-dependent) has `tfm` `net11.0` on `Microsoft.NETCore.App` 11.x (`check_framework_dependent_runtimeconfig`); `docker run <image> --version` reports `.NET <DOTNET_VERSION>`; and the image's `CampertronLibrary.pdb` and `CampertronConsole.pdb` pass `check_pdb_provenance`, which shows that the SDK build stage compiled with an SDK 11 compiler (D31).
   - **Rehearsal (no registry):** `skopeo copy --all --preserve-digests` into a local OCI layout must keep the image index digest and every manifest and blob, and `skopeo inspect --raw` must return exactly the index bytes (`check_oci_copy`).
   - Uploads the archive unchanged as the artifact `container-image` (`archive: false`; 1 day for dry runs, 7 for tag runs). docker-publish pushes exactly this archive; nothing rebuilds the image.
4b. `container-smoke` "Container smoke test linux/arm64 (native)" [verify, docker] (S2, D48, §AM), read-only, on `ubuntu-24.04-arm`, never contacts a registry and builds nothing:
   - Downloads the docker job's `container-image` archive, requires its SHA-256, image index, arm64 manifest and arm64 config to equal the docker job's outputs and runs the complete image check again (`check_oci_archive`).
   - Loads the archive's linux/arm64 image into the runner's Docker with `skopeo copy --override-os linux --override-arch arm64 oci-archive:… docker-daemon:campertron-smoke-arm64:local` (skopeo is installed with `apt-get` if the image lacks it).
   - **The loaded image must be the validated arm64 image** (`check_loaded_platform_image`): Docker reports the architecture arm64, its config blob (read from a `docker save` of it, `saved_image_config`) is the validated config, and its layer diff_ids are that config's. The image ID is not compared: a loaded image can get a manifest of Docker's own (with the containerd image store the ID is that manifest's digest; shown locally in §AM).
   - Runs it natively: without volumes it must fail with `Config path does not exist`; with `/config` and `/cache` it must extract the bundled DB; the version label must equal the version; `DOTNET_VERSION` is 11.x and `--version` reports it in an `(Arm64)` process; `uname -m` in the container is `aarch64`.
5. `assemble` "Assemble release (checksums, notes)" [verify, binaries], **read-only, runs on every trigger**: pull request and manual dry runs as well as tag releases.
   - Checks out the repository (for `release-policy.sh`), downloads the 16 archives with `skip-decompress: true` and checks that the directory holds exactly the 16 expected names for this version (`campertron-<app>-<version>-<rid>.<zip|tar.gz>`), each once and nothing else.
   - **One toolchain** (D31): extracts `CampertronLibrary.pdb` from every archive (`unzip -p` / `tar -xzf --wildcards -O`), lists `<archive> <compiler-version> <runtime-version>` and requires exactly one compiler identity (`check_single_toolchain`); a mix lists each toolchain with its archives and fails. The identity goes to the job summary.
   - Generates `SHA256SUMS.txt` (16 lines) and verifies every line with `sha256sum --check --strict`.
   - Builds `RELEASE_NOTES.md` with `assemble_release_notes`. **Publishing tag:** the generated title and status (lines 1–4), the fragment `.github/release-notes/v<version>.md` byte for byte, one blank line, then the generated sections; the fragment is required. **Dry run:** the generated template alone for the synthetic `0.0.0-dryrun.<n>`, and the summary states that template-only notes are permitted only because the run cannot publish. The job summary shows the notes with their SHA-256. The generated parts are:
     - title `# Campertron <version>`
     - status: **Pre-release version** (normal GitHub release marked Latest; image tagged with the exact version only, Docker `latest` and stable aliases not updated) or **Stable release** (marked Latest; image tagged exact, `<major>.<minor>` and `latest`)
     - the Downloads sentence saying what each program does (since D49, §AN: the GUI archive is "campground lookup, settings and search editing, and searches on request"; before, "campground lookup")
     - the 8-platform table naming both archives per platform and how the platform is smoke-tested (column "Smoke test in CI" since S2; "Native smoke test in CI" before): "yes: console clean install and GUI smoke test" for `NATIVE_SMOKE_RIDS_DEFAULT` (since S2 linux-arm64, linux-x64, osx-arm64, osx-x64, win-arm64, win-x64), since S2 "on 64-bit Windows: console clean install and GUI smoke test" for `COMPAT_SMOKE_RIDS_DEFAULT` (win-x86), "no: supported and cross-built, not run natively" for linux-arm (from R8 to S2 linux-arm, linux-arm64 and win-x86 were "no"; before R8 only linux-x64 was "yes")
     - the statement naming the natively smoke-tested platforms, that win-x86 runs the same tests as a 32-bit process on 64-bit Windows and not on a 32-bit system, and that linux-arm is not run in CI (`NATIVE_SMOKE_STATEMENT`); in the Docker section, that both images are run in CI before publication (`CONTAINER_SMOKE_STATEMENT`, since S2)
     - unsigned-binary notes, the `docker pull ghcr.io/<owner>/campertronconsole:<version>` command with both platforms, and verification instructions.
   - Validates those notes (`validate_notes`). It checks the version title; that the notes say the release is marked Latest and never claim it is a GitHub prerelease or not Latest; that the status matches the pre-release flag, including the Docker tag sentence; that every archive name appears exactly once; 8 table rows; the smoke-test text in each `NATIVE_SMOKE_RIDS_DEFAULT` row and in no other row; since S2 the 64-bit-Windows text in each `COMPAT_SMOKE_RIDS_DEFAULT` row and in no other; the remaining rows marked not run natively; the smoke-test statement and the container statement; the Docker pull command and platforms; and the checksum instructions. For a publishing tag, line 5 must also be `## What's new in <version>`. `test-release-policy.sh` checks that the workflow's smoke jobs run exactly `NATIVE_SMOKE_RIDS_DEFAULT`.
   - Uploads `SHA256SUMS.txt` and `RELEASE_NOTES.md` as the workflow artifact `release-metadata` (7-day retention).
6. `docker-publish` "Publish image to GHCR" [verify, binaries, smoke, native-smoke, docker, container-smoke, assemble], **tag push only**:
   - **Builds nothing (D32, §W).** It downloads the docker job's `container-image` archive and, before any registry contact, requires its SHA-256, image index and both platform manifests to equal the docker job's outputs; then it runs the full image check again.
   - Logs in to `ghcr.io` with `GITHUB_TOKEN` (`docker login`; skopeo reads `~/.docker/config.json` through `--authfile`) and pushes the exact archive with `skopeo copy --all --preserve-digests` (`publish_oci_archive`).
     - A tag that already exists must already be the validated digest; it is never moved.
     - After each copy, the registry's raw index must have the validated digest and list both platform manifests.
   - Tags (`docker_tag_refs`, derived from `docker_tag_args`): `<version>`; stable versions also get `<major>.<minor>` and `latest`. Pre-release versions never move `latest` or create stable aliases.
   - Attests the validated index digest, which is the pushed digest (`push-to-registry: true`, `create-storage-record: false`).
7. `release` "Create GitHub release" [all of the above, including `native-smoke` and `container-smoke`], **tag push only**. It **publishes the assembled output unchanged**:
   - Downloads the 16 archives and the `release-metadata` artifact.
   - Re-checks the archives byte for byte against the assembled `SHA256SUMS.txt` (`--check --strict`), checks that it lists exactly the downloaded files, and checks that the notes title names this version. It does not regenerate checksums.
   - **Reconstructs the approved notes from the tagged checkout** (`assemble_release_notes true`: the exact fragment is required and validated again) and compares them byte for byte with the `release-metadata` copy (`compare_release_body`); line 5 must be `## What's new in <version>`, and `validate_notes` runs again. All of this happens before attestation and before any draft exists.
   - Attests the 16 archives and `SHA256SUMS.txt` (`actions/attest-build-provenance`).
   - `gh release create <tag> <files> --draft --verify-tag --notes-file RELEASE_NOTES.md --latest` for every version (`github_release_flags`), so the draft carries the approved notes. `--generate-notes` is not used. Then checks that the draft has 17 assets.
   - **Checks the draft before publishing** (`check_release_view`): `gh release view <tag> --json body,isDraft,isPrerelease,name,tagName,assets`; the body is extracted with `jq -j` (never `--jq`, which appends a newline) and must equal `RELEASE_NOTES.md` byte for byte and pass `validate_notes`; the release must still be a draft, not a prerelease, titled `Campertron <version>`, tagged `v<version>`, with 17 assets. On any failure the job stops and leaves an unpublished draft.
   - Only then `gh release edit --draft=false --latest`.
   - Checks that the published release is not a prerelease or draft (`gh release view --json isPrerelease,isDraft`) and that `GET /releases/latest` returns this tag, then **reads the public body back** the same way and requires byte equality and `validate_notes` again. Any failure fails the job; a body mismatch is reported, never repaired automatically. `gh release view` has no `isLatest` field in gh 2.103.3; only `gh release list` has it.

**Publishing gates.** `docker-publish` and `release` run only if `needs.verify.outputs.publish == 'true'` **and** `github.event_name == 'push'` **and** `github.ref` starts with `refs/tags/v`. A pull request or manual run fails the event condition, even if `verify` misjudged the mode. These two jobs are also the only ones with write permissions, and the only ones that log in to GHCR, attest or call `gh release`.

**Permissions**
- Workflow default: `contents: read`, which `verify`, `binaries`, `smoke`, `docker` and `assemble` use.
- `docker-publish`: `contents: read`, `packages: write`, `id-token: write`, `attestations: write`.
- `release`: `contents: write`, `id-token: write`, `attestations: write`.
- No secrets other than `GITHUB_TOKEN`.
- Artifact attestations are free for public repositories [I: GitHub documentation].

**Actions** (all GitHub-owned, so allowed by the current Actions setting; pinned to full SHAs):

| Action | Version | SHA (prefix) |
|---|---|---|
| `actions/checkout` | v7.0.1 | `3d3c42e5…` |
| `actions/setup-dotnet` | v6.0.0 | `a98b5685…` (same as `ci.yml`; v6.1.0 exists) |
| `actions/upload-artifact` | v7.0.2 | `cf430e03…` |
| `actions/download-artifact` | v8.0.2 | `9000827c…` |
| `actions/attest-build-provenance` | v4.2.2 | `4d101475…` |

Docker and releases are driven by the `docker` and `gh` CLIs that are preinstalled on the runner; there are no third-party actions.

**Failure policy** [R]
- If anything fails before `docker-publish` (including `assemble`), nothing has been published. That includes a missing, invalid or mismatched curated fragment, which fails in `verify` before any build. Fix the problem through a PR, then tag a new version (the tag can't be moved).
- If `docker-publish` succeeds but `release` fails, the image exists without a release. Before the draft exists (checksum or reconstruction failure) nothing else was created. If the draft exists (the draft check failed), it stays an unpublished draft. Leave the image and any draft untouched until the maintainer decides. Recovery with the maintainer's approval: inspect the draft, delete it, then re-run the failed job. A re-run uses the workflow file from the tagged commit and the same run's `release-metadata`. [?] Whether the `verify` job's existing-release check (read-only token) sees a draft, and whether deleting an unpublished draft leaves its tag name reusable, are unverified (open questions #32, #33).
- If the public body check fails, the release is already public and immutable. Its notes are still editable, but a correction with `gh release edit --notes-file` is recovery only and needs the maintainer's explicit approval. Never delete, replace or recreate the release.
- A tag is never moved or re-pushed. If the problem needs a code or workflow change, publish a new version greater than the latest existing release. Never replace or recreate an existing version.

**Release procedure** [R]
1. Merge to `master` through a PR, and make sure CI on `master` is green.
2. **Curated notes PR, the last content PR before tagging:** add only `.github/release-notes/v<version>.md` (format: `.github/release-notes/README.md`). CI validates every fragment and shows the composed body and its SHA-256 in the job summary; review the diff and that preview. Merging it approves the exact bytes. Pushing the tag (step 4) is the separate approval to publish.
3. Optional in general: run `gh workflow run release.yml --ref master` (a dry run) and review its artifacts and summary. The archives are uploaded as they are (`archive: false`), so `gh run download` cannot fetch them ("error extracting zip archive"); download one with `gh api repos/ShaunAnthonyHathaway/Campertron/actions/artifacts/<artifact id>/zip > <artifact name>` or from the run's page (§AN). **For `v0.1.0-preview.1` this dry run was a required pre-tag gate** (decisions.md §Roadmap, recorded 2026-11-14), because the pull-request path filter does not match library or console code and no native smoke job had run on the D34 or cleanup code; it ran as Release run #30 on `6484dc2` and passed (§AC). Like every manual dispatch or rerun, it needs separate explicit approval, and passing it does not approve the tag. Its `release-metadata` has the exact `SHA256SUMS.txt` contents a release would publish but template notes for the synthetic version; the verify job's summary shows the composed body and SHA-256 of every real fragment. Check that the tag does not exist: `git ls-remote --tags origin v<version>` and `gh release view v<version>`.
4. `git tag -a v<version> -m "Campertron <version>" <master sha of the notes PR merge or later>`, then `git push origin v<version>`.
5. Watch the run until every job finishes (`gh run watch <id> --exit-status`), then verify:
   - the release has the 16 archives plus `SHA256SUMS.txt` (17 assets), is a normal (non-prerelease) release, is immutable, and is Latest (`gh api repos/<repo>/releases/latest` returns the tag)
   - `sha256sum -c SHA256SUMS.txt` on the downloaded assets
   - `gh attestation verify <file> --repo ShaunAnthonyHathaway/Campertron --signer-workflow ShaunAnthonyHathaway/Campertron/.github/workflows/release.yml --format json` for every file (gh 2.104.0 prints nothing on success when its output is not a terminal; `--format json` shows the verified signer, source commit and subject)
   - the image index `ghcr.io/shaunanthonyhathaway/campertronconsole:<version>` lists linux/amd64 and linux/arm64, and `gh attestation verify oci://…:<version>` succeeds
   - for a pre-release version, that `latest` and `<major>.<minor>` were not created in GHCR (for a stable version, that both point to the new digest).
   - the release body has the SHA-256 of the composed notes shown in the run summary and in the notes PR's CI preview (the workflow already compared it byte for byte before and after publication).
   - **Curated notes are no longer installed after publication** (decision D30). Before D30, `v0.1.0-beta.7`, `v0.1.0-beta.8` and `v0.1.0-beta.9` were published with the template and their curated notes installed afterwards with `gh release edit --notes-file` (§Release history). Since D30 that edit is a recovery step only, with the maintainer's explicit approval: validate the corrected file with `validate_notes`, save the stored body and asset inventory, edit, read the body back and compare byte for byte, and confirm that the title, flags, immutability, Latest, tag, assets, attestations and GHCR tags are unchanged. Titles and notes of immutable releases stay editable; assets and tags do not.
6. GHCR package visibility needs no action. [V] The package was **public from its first push** (`v0.1.0-beta.4`): anonymous tag listing, manifest and blob pulls all succeeded without any visibility change. [I] It most likely inherited public visibility from the repository it is linked to through the `org.opencontainers.image.source` label; the package-settings API was not readable with the maintainer's CLI token (no `read:packages` scope).
7. **Required: verify the current documentation** after the release is published and verified (step 5).
   - Search `README.md` and the other current user-facing documentation for pinned release and container-image versions. Verify the current download links, artifact guidance, Docker commands and GHCR commands against the new release.
   - Update stale current guidance through a separate documentation PR. Never change the published tag or release to do this.
   - Keep legitimate historical references, such as the release history, compatibility notes and repair rules. Don't rewrite immutable release history.
   - This is a manual documentation step. `release.yml` does not change repository files.

**Local validation, 2026-11-10** (Linux x64, SDK 10.0.114; Docker, Info-ZIP `zip`, `xvfb-run`, actionlint and shellcheck are not installed here) [V]
- **Workflow file:** parsed with YamlDotNet; job graph, conditions and permissions as above. Every `uses:` is an `actions/*` action pinned to a 40-hex SHA. Every job has a timeout. All 21 `run:` scripts pass `bash -n`.
- **Version and mode script, run with real git and `gh`:**
  - `v0.1.0-beta.4` on `master`: publish, prerelease.
  - `v1.2.3`: publish, stable.
  - Rejected: a tag on a non-master commit, `v0.1-beta5`, `v0.1-beta3`, `v01.2.3`, and a tag whose release already exists (`gh` stub).
  - Dispatch and PR: dry run `0.0.0-dryrun.<n>`.
- **Image tag script:** `0.1.0-beta.4` gets only its exact tag; `1.2.3` gets `1.2.3`, `1.2` and `latest`; the owner is lowercased.
- **Packaging scripts for all 16 (app, rid) pairs**, run with `VERSION=0.1.0-beta.4`: publish, the contents check and the archive step all succeeded. Archive sizes:
  - console: 61.8–66.8 MB
  - GUI: 69.9–77.7 MB
  - about 1.13 GB per release

  Every archive has a single top-level folder named like the archive. Hosts in the tar.gz archives are `-rwxr-xr-x 0/0`. Each zip contains its `.exe` and passes `testzip`. Locally, the zips were made by a Python stand-in for `zip`; the runner uses Info-ZIP.
- **Smoke scripts:** the published linux-x64 console extracted the 226,471,936-byte DB. The published GUI ran for the whole 30 s (a stand-in for `xvfb-run` used the real display).
- **Checksum script:** 16 lines, and `sha256sum -c` passes. The notes script renders the expected Markdown.
- **Dockerfile stages, without Docker:** reproduced with the same commands in a clean export of the tree, with the `.dockerignore` exclusions applied, for `TARGETARCH` amd64 and arm64. Both had 0 errors and produced framework-dependent output without an apphost. Native `libe_sqlite3.so` was x86-64 and AArch64 respectively, and the version was stamped `0.1.0-beta.4`. In container mode without `/config`, the amd64 output fails with `Config path does not exist` (exit 134).
- **Assemble and release-consumption scripts** (branch `ci/release-assembly`), run as extracted from the workflow against 16 real archives built by the packaging scripts: **27/27 scenarios behaved as expected**, each failure with the intended `::error::` message.
  - Accepted, for `0.1.0-beta.4` (prerelease), `0.0.0-dryrun.1` and `1.2.3` (stable): assembly produced `SHA256SUMS.txt` (16 lines, all verified) and `RELEASE_NOTES.md`. The release job's verification accepted that output, and its `dist/` then held the 16 archives plus `SHA256SUMS.txt`.
  - Rejected by `assemble`: a missing archive, an extra archive, archives for another version, and a Windows archive in the wrong format.
  - Rejected by the notes validation (10 cases): wrong version title; prerelease and stable status swapped either way; an archive row removed; the linux-x64 smoke claim removed; a non-native platform marked as tested; the limitation statement removed; the wrong Docker tag; the Docker platforms removed; the `latest`-unchanged note removed.
  - Rejected by the release job: an archive modified after assembly, an extra or missing archive at release time, and notes assembled for another version.
- **First GitHub dry run of the assemble job (PR #19, run 42947115639) failed, as it should have.** `actions/download-artifact` v8 extracts any download whose content type is zip or whose URL ends in `.zip`, unless `skip-decompress: true` is set. This was verified in the pinned `dist/index.js`, `streamExtractExternal`. The 6 Windows archives, uploaded unarchived as `.zip`, therefore arrived as extracted directories, and the name check rejected them.
  - The same defect was in the tag-only `release` job merged in #18. Its `ls campertron-* | wc -l` also counted directories, so the first real release would have failed or attached the wrong files.
  - **Fix:** `skip-decompress: true` on the three steps that download release archives (assemble, release, smoke). The `release-metadata` download (a normal zipped artifact) is still extracted.
- **Latest-policy change** (branch `ci/prerelease-latest-policy`):
  - `bash .github/release-policy/test-release-policy.sh`: **27/27 passed**. That covers Docker tags for both kinds of version; the GitHub flags being exactly `--latest` (no `--prerelease` or `--latest=false`); notes rendering and validation for `0.1.0-beta.5` and `1.2.3`; and 12 rejected tampering cases, including the old "GitHub prerelease, not Latest" wording.
  - The changed workflow scripts were run as extracted, with `gh` replaced by a logging stub, for `0.1.0-beta.5` and `1.2.3`: **28/28 passed**. Assembly and validation passed. Docker tags were the exact tag only, and exact + `1.2` + `latest`, respectively. The stub recorded `gh release create … --draft … --latest` and `gh release edit … --draft=false --latest`, with no prerelease flag. The post-publish check passed when the tag was Latest, and failed when another tag was Latest or the release was a prerelease.
  - The new check's commands were run read-only against the live `v0.1.0-beta.4`: `gh release view --json isPrerelease,isDraft` gave `false false`, and `releases/latest` gave `v0.1.0-beta.4`.
- **Not run locally, left to GitHub:** `docker buildx` and the container smoke tests, Info-ZIP `zip`, `xvfb-run` and the artifact upload and download actions all passed in the PR #18 and #19 dry runs (Info-ZIP is not preinstalled on the Ubuntu 26.04 runner image; the Windows jobs install it). The tag-only `docker-publish` and `release` jobs first ran for `v0.1.0-beta.4` and succeeded (§Release history).

### Release protections (applied 2026-11-11, before the first tag) [V]
Read back via `GET /repos/ShaunAnthonyHathaway/Campertron/rulesets/27004221` and `GET …/immutable-releases` before `v0.1.0-beta.4` was created, and again after it was published.
- **Tag ruleset "Protect release tags", ID 27004221** (https://github.com/ShaunAnthonyHathaway/Campertron/rules/27004221): `target: tag`, `enforcement: active`, `conditions.ref_name.include: ["refs/tags/v*"]`, `exclude: []`, rules `update` and `deletion`, **`bypass_actors: []`** (`current_user_can_bypass: never`, administrators included).
  - Tags matching `v*` can be **created** but never **moved or deleted**. This also covers the old lightweight tags `v0.1-beta1` and `v0.1-beta3`.
  - Consequence: a bad or failed release is never fixed by re-tagging. Release the next version instead (§Failure policy).
- **Immutable releases: enabled** (`{"enabled":true,"enforced_by_owner":false}`). Published releases, their assets and their tags cannot be changed afterwards. The workflow's draft-first creation attaches every asset before publishing, which is what immutable releases require. The first read-back right after `PUT` still showed `enabled: false`; it showed `true` about 10 seconds later.
- Unchanged: the `master` ruleset 26990088 and the repository visibility (public).

### Release history
**`v0.1.0-beta.4`, published 2026-11-11 03:31:45 UTC** [V]
- Tag: annotated, unsigned, object `e73fe6d6f65da5d107f47d60271547690daf5316`, pointing to commit `5392aad651036d3604838abd2fe142b774f5c59f` (merge of PR #19). Only this tag was pushed, with an explicit refspec. After the release, the remote tag still resolved to that commit.
- Workflow run https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/42952907979: **22/22 jobs succeeded** (03:27:42 to 03:31:48 UTC). That covers verify (43/43 tests, 0 vulnerable packages); 16 packages; linux-x64 console and GUI smoke; Docker amd64 + arm64 builds and container smoke; assemble (16 names, 16 checksums `OK`, notes validated); docker-publish; and release.
- GitHub release ID 441205400, https://github.com/ShaunAnthonyHathaway/Campertron/releases/tag/v0.1.0-beta.4. It was published as a **prerelease**, **immutable** (`isImmutable: true`) and **not latest** (`releases/latest` was then `v0.1-beta3`); see the 2026-11-11 policy change below. It was published by `github-actions[bot]` at 03:31:45, one second after the last of the 17 assets was uploaded (03:31:29 to 03:31:44).
- **17 assets:** the 16 archives (1,095,684,703 bytes in total; console 61,844,967 to 65,596,012, GUI 69,945,912 to 76,310,887) plus `SHA256SUMS.txt` (1,782 bytes). After downloading the published assets: `SHA256SUMS.txt` has 16 lines, all 16 verify `OK`, and it lists exactly the 16 archives.
- **Attestations:** all **17** release files verify with `gh attestation verify --signer-workflow …/release.yml` (one provenance attestation with 17 subjects). The image attestation verifies through the GitHub API and from the bundle stored in the registry (`--bundle-from-oci`).
- **GHCR image `ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.0-beta.4`:**
  - index digest `sha256:65d86711addfbe8201bbd5a190fd6280445cd2bfb37a62a7d036576585b02208`
  - **linux/amd64** `sha256:7b02bcb4…` and **linux/arm64** `sha256:0cd741da…`, plus two BuildKit attestation manifests
  - labels: version `0.1.0-beta.4`, revision `5392aad…`, MIT license, source pointing to this repository; `USER app`
  - **public**: anonymous pull works.
  - Registry tags: `0.1.0-beta.4` and the attestation tag `sha256-65d86711…`. **No `latest`, `0.1` or `0.1.0`** (404): prereleases create neither `latest` nor stable aliases.
- **Changed after publication, 2026-11-11, at the maintainer's request** (new Latest policy) [V]:
  - Release 441205400 was updated with `PATCH …/releases/441205400` sending only `prerelease: false` and `make_latest: "true"`. It is now a **normal release marked Latest** (`releases/latest` returns it) and still **immutable**.
  - That change left the notes, name, tag and all 17 asset IDs, sizes and digests identical. The notes were corrected separately (below).
  - Afterwards, re-downloading the assets gave 16/16 checksums `OK`, all 17 release-file attestations verify, and the image attestation verifies.
  - The GHCR image is unchanged: the tag list and digest are identical, and `latest`, `0.1` and `0.1.0` still return 404. Docker still treats the version as a pre-release.
  - **Release notes corrected, 2026-11-11** [V]: the status paragraph (line 3) was replaced with `PATCH …/releases/441205400` sending only `body`. Before, it read "**Prerelease.** This version is published as a GitHub prerelease and is not marked as the latest release…". It now reads: "**Pre-release version.** `0.1.0-beta.4` is a beta version (a SemVer pre-release). It is published as a normal GitHub release and marked Latest, so it appears on the repository page. The container image is tagged `0.1.0-beta.4` only: Docker `latest` and the stable aliases are not updated."
    - The other 29 lines are byte-identical, the notes still have 30 lines, and the stored body passes `validate_notes` from `.github/release-policy/release-policy.sh`.
    - Re-verified afterwards: the title, tag, commit, prerelease flag, immutability and all 17 assets are unchanged; 16/16 checksums `OK`; 17/17 release-file attestations and the image attestation verify; the GHCR tags and digest are unchanged; and `latest`, `0.1` and `0.1.0` still return 404.
    - An intermediate edit had added one trailing newline; a second body-only edit restored it.
- **Older releases removed, 2026-11-11** [V]: the release entries `v0.1-beta.3` (ID 186222084) and `v0.1-beta.1` (ID 178602660) were deleted with `DELETE …/releases/{id}`. Their 8 uploaded 2024 zips each went with them. The lightweight tags `v0.1-beta1` and `v0.1-beta3` are unchanged and still point to `e96bc7c…`, and tag ruleset 27004221 protects them. `v0.1.0-beta.4` was then the only release.
- **Investigated 2026-11-11: the published artifacts are correct** (§I). The executables that showed ZIP codes as GUI states and `.NET 9.0.0` were not established to be beta.4 executables: the GUI that ran was a separate 2024 executable, and the beta.4 console asset was not found on that machine (§I.2). No corrective release was made; the release guards of §I were added instead.
- **Defect fixed before the release.** The PR #19 dry run found that `actions/download-artifact` v8 extracted the Windows `.zip` archives on download. It was fixed with `skip-decompress: true` before any tag existed (see the PR #19 entry under §Local validation). Without the fix, the first real release would have failed or published wrong assets.

**`v0.1.0-beta.5`, published 2026-11-11 13:46:54 UTC** [V]
- Tag: annotated, unsigned, object `7de2c762470432e2a6147d467eccdbbb76165637` ("Campertron 0.1.0-beta.5"), pointing to commit `0a2002575b80d26aae2f9b8cba5c1e8aeb9e6681` (merge of PR #22). Only this tag was pushed, with an explicit refspec. Before tagging: `master` was clean and current; no local tag, remote tag or release for `v0.1.0-beta.5` existed; and the tree was identical to PR #22's head `357ceb5`, whose release dry run (run 43028284708) had succeeded.
- Content: the release hardening of PR #22 (§I): `--version`, the .NET 10 guards, the GUI state-data check and the compatibility section in the notes. The application behavior is otherwise identical to `v0.1.0-beta.4`.
- Workflow run https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43031635085: **22/22 jobs succeeded** (13:43:00 to 13:46:58 UTC), including every new guard.
- GitHub release ID 441754432, https://github.com/ShaunAnthonyHathaway/Campertron/releases/tag/v0.1.0-beta.5. It is a **normal release marked Latest** (`prerelease: false`, `releases/latest` returns it) and **immutable**, published by `github-actions[bot]`. The 17 assets were uploaded 13:46:40 to 13:46:53, before publication, and none was changed afterwards. `v0.1.0-beta.4` is no longer Latest; nothing else about it changed.
- **17 assets:** the 16 archives (console 61,844,070 to 65,595,976 bytes, GUI 69,945,714 to 76,318,085) plus `SHA256SUMS.txt` (1,782 bytes). The published notes (35 lines) contain "Compatibility with older versions" and pass `validate_notes`.
- **Independent verification after publication** (fresh download into an empty directory):
  - 16/16 checksums `OK` against `SHA256SUMS.txt`, which lists exactly the 16 archives; all 17 files match their GitHub asset digests.
  - 17/17 release-file attestations verify (`--signer-workflow …/release.yml`).
  - `check_publish_runtime` passes for all 16 extracted archives: runtime 10.0.15, and `CampertronLibrary.dll` version `0.1.0-beta.5+0a20025…`.
  - linux-x64 `CampertronConsole --version` and `CampertronGUI --version` report `0.1.0-beta.5+0a20025…` and `Framework => .NET 10.0.15 (X64)`, which is the bundled runtime.
  - linux-x64 console clean install and a GUI start each extracted the DB (`8712856097a91ee1…`). `ReleaseCheck` passed on both: validation, Corrected layout, 51 two-letter states, no `01005`, MA → BARRE, and Watchman.
- **GHCR image `ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.0-beta.5`:**
  - index digest `sha256:b99be8bccfa4f84cbd9d0e95bf33ddde70be608fcac9a918d48cf41515ec94b6`
  - **linux/amd64** `sha256:d90541ce…` and **linux/arm64** `sha256:0c84a93d…`, plus two attestation manifests
  - both platforms: `DOTNET_VERSION=10.0.15`, `USER app`, labels version `0.1.0-beta.5` and revision `0a20025…`. The console's runtimeconfig, read from the pushed layers, is `net10.0` on `Microsoft.NETCore.App` 10.0.0, and passes `check_framework_dependent_runtimeconfig`.
  - The image attestation verifies (SLSA provenance, signer `release.yml@refs/tags/v0.1.0-beta.5`).
  - Registry tags: `0.1.0-beta.4`, `0.1.0-beta.5` and their attestation tags. **No `latest`, `0.1` or `0.1.0`** (404). The `0.1.0-beta.4` digest is unchanged.
  - Not run locally (Docker isn't installed): the pushed image itself. The workflow's container check ran the image it built from the same commit (amd64) and got `CampertronConsole 0.1.0-beta.5+0a20025…` and `Framework => .NET 10.0.15 (X64)`.
- **Untouched:** the tags `v0.1-beta1`, `v0.1-beta3` and `v0.1.0-beta.4` (identical `ls-remote` before and after), and the `v0.1.0-beta.4` release record (ID, flags, immutability, notes, and all 17 asset IDs, sizes, digests and timestamps identical).

**`v0.1.0-beta.6`, published 2026-11-11 14:47:51 UTC** [V]
- Tag: annotated, unsigned, object `6775de8f3574946dc87e899c7df55ba9b707c8db` ("Campertron 0.1.0-beta.6"), pointing to commit `6a172bd37646dcbcccea4d4666ff69b67787ea0c` (merge of PR #24). Only this tag was pushed. Before tagging, `master` was clean and current; no local tag, remote tag or release existed; and the tree was identical to PR #24's head `678c7f5`, whose manual release dry run (run 43040219121, `workflow_dispatch`, because PR #24 touches no release-workflow path) had succeeded.
- Content: the startup repair of pre-0.1.0-beta.4 databases (§J, business-rules §8, decision D22). The embedded `RecreationDotOrg.db.br` is unchanged (SHA-256 `700cbd00f0722cec…`), and so are the application's other behavior and the release hardening.
- Workflow run https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43041084028: **22/22 jobs succeeded** (14:42:34 to 14:47:56 UTC).
- GitHub release ID 441825528, https://github.com/ShaunAnthonyHathaway/Campertron/releases/tag/v0.1.0-beta.6. It is a **normal release marked Latest** and **immutable**, published by `github-actions[bot]`. The 17 assets were uploaded 14:47:36 to 14:47:49, before publication. `v0.1.0-beta.5` is no longer Latest; nothing else about it changed.
- **17 assets:** the 16 archives (console 61,849,809 to 65,598,544 bytes, GUI 69,951,438 to 76,313,150) plus `SHA256SUMS.txt` (1,782 bytes).
- **Independent verification after publication** (fresh download):
  - 16/16 checksums `OK`, listing exactly the 16 archives; 17/17 GitHub asset digests match; 17/17 release-file attestations verify.
  - All 16 archives pass `check_publish_runtime` (runtime 10.0.15; `CampertronLibrary.dll` version `0.1.0-beta.6+6a172bd…`), and each contains the exact embedded `RecreationDotOrg.db.br`.
  - linux-x64 `CampertronConsole --version` and `CampertronGUI --version` report `0.1.0-beta.6+6a172bd…` and `Framework => .NET 10.0.15 (X64)`, which is the bundled runtime.
  - Clean installs from the published GUI and console extracted `8712856097a91ee1…`. `ReleaseCheck` passed: 51 two-letter states, AZ without `Arizona`, no ZIP codes, MA → BARRE, Watchman, Corrected, `Validate` passes. No refresh was needed.
  - **Upgrade:** for each of the published console and GUI, a pre-0.1.0-beta.4-class DB (the published DB plus the 15 omitted tables, with 3 AZ campgrounds set to `ARIZONA`) was replaced on the first start. A `.pre-0.1.0-beta.4-….bak` backup was kept, the derived cache file was removed and `abc.html` kept, and `ReleaseCheck` then passed. The second start replaced nothing and the DB was unchanged.
  - **Not replaced:** a newer valid DB (the published DB plus an extra campground, so byte-different), with its cache kept.
  - The real pre-refresh DB of business-rules §8 was not used: its scratch copy had been deleted when the scratch space was cleared. It was used before the release, with the local publish (§J).
- **GHCR image `ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.0-beta.6`:**
  - index digest `sha256:6438c252f920ceac8875beeff4bb122f9c387696e3387ccf6e9a3b86aac15b48`
  - **linux/amd64** `sha256:76fd2376…` and **linux/arm64** `sha256:c8096aca…`, plus two attestation manifests
  - both platforms: `DOTNET_VERSION=10.0.15`, labels version `0.1.0-beta.6` and revision `6a172bd…`; the console runtimeconfig in the pushed layers passes `check_framework_dependent_runtimeconfig`.
  - The image attestation verifies (signer `release.yml@refs/tags/v0.1.0-beta.6`).
  - Registry tags: `0.1.0-beta.4`, `0.1.0-beta.5`, `0.1.0-beta.6` and their attestation tags. **No `latest`, `0.1` or `0.1.0`** (404). The beta.4 and beta.5 digests are unchanged.
  - Not run locally (no Docker): the pushed image. The workflow's container check on its own amd64 build reported `CampertronConsole 0.1.0-beta.6+6a172bd…` and `.NET 10.0.15 (X64)`.
- **Untouched:** the tags `v0.1-beta1`, `v0.1-beta3`, `v0.1.0-beta.4` and `v0.1.0-beta.5` (identical `ls-remote`), and the beta.4 and beta.5 release records (identical to the snapshot taken before tagging).

**`v0.1.0-beta.7`, published 2026-11-12 04:41:03 UTC** [V]
- Tag: annotated, unsigned, object `b55226bc94b1706367620d73c341b4105158c97c` ("Campertron 0.1.0-beta.7"), pointing to commit `715f95e3da1a651785065bb64560eb9aacebc21b` (merge of PR #32). Only this tag was pushed, with the explicit refspec `refs/tags/v0.1.0-beta.7`. Before tagging: `master` was clean and current; no local tag, remote tag or release for `v0.1.0-beta.7` existed; CI on `715f95e` (run 43152990886) had succeeded; there were no open PRs; both rulesets and immutable releases were active; and `release.yml` at the tagged commit was the rehearsed file (blob `fa8302d0…`).
- Content: everything merged since `v0.1.0-beta.6` (PRs #25–#32): the Avalonia 12 migration (§K), the TD-35 Windows native-PDB exclusion (§L), the documentation audit, the RIDB import optimization (§M), the README rewrite and screenshots, the failure-safe RIDB refresh and the interruption-safe `General.yaml` timestamp (TD-36 part 1, §N), and the configured cache path (TD-05, §O). The embedded `RecreationDotOrg.db.br` is unchanged from beta.6 (SHA-256 `700cbd00…`; extracted DB `8712856097a91ee1…`).
- **Validation before tagging** (local, at `715f95e`, SDK 10.0.114): restore OK; Debug and Release builds (`--no-incremental`) 0 errors, 84 warnings each, all compiler warnings (compared with a beta.6 build: no new warning, two removed by TD-36, two pre-existing ones with changed signature text); `XDG_DATA_HOME="$(mktemp -d)" dotnet test Campertron.sln -c Release --blame-hang-timeout 10m` **179/179** (167 + 12 GUI); `CampertronGUITests` 12/12; `test-release-policy.sh` **67/67**; **no vulnerable packages** in any of the 5 solution projects or `ReleaseCheck`, each scanned individually (`dotnet list Campertron.sln …` exits 1 only because of `docker-compose.dcproj`).
- **Release rehearsal before tagging** (local, nothing uploaded; the manual dry run was not used because it uploads 7-day workflow artifacts). The `run:` scripts were extracted from `release.yml` and run verbatim. The 16 packages were built from a git checkout of `715f95e` in a disposable `mcr.microsoft.com/dotnet/sdk:10.0` container (Ubuntu 24.04, SDK 10.0.404 / runtime 10.0.15, Info-ZIP, `xvfb-run`, with `DOTNET_RUNNING_IN_CONTAINER` unset for the smoke steps). All 16 passed publish, the contents check (TD-35 guard), `check_publish_runtime` and archiving; `assemble` and the release job's checksum verification passed; the Docker job's steps passed (both architectures built; amd64 run; arm64 built and inspected, not run, no emulation). The rehearsal archives have different SHA-256 values from the published ones (tar and zip store file times), and their sizes are within 2,380 bytes. GUI archives extract about 90 bytes larger on the runner because `CampertronGUI.dll` records the absolute path of its PDB (`/home/runner/work/…`); source paths are mapped to `/_/`.
- Workflow run https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43155929199 (`push` of `v0.1.0-beta.7`, attempt 1): **22/22 jobs succeeded** (04:36:40 to 04:41:06 UTC).
- GitHub release ID 442504169, https://github.com/ShaunAnthonyHathaway/Campertron/releases/tag/v0.1.0-beta.7. It is a **normal release marked Latest** and **immutable**, published by `github-actions[bot]`. The 17 assets were uploaded 04:40:51 to 04:41:00, before publication. `v0.1.0-beta.6` is no longer Latest; nothing else about it changed.
- **17 assets:** the 16 archives (1,111,117,168 bytes in total; console 61,858,121 to 65,611,268, GUI 72,037,730 to 78,331,617) plus `SHA256SUMS.txt` (1,782 bytes). Compared with beta.6, the console packages differ only in `CampertronLibrary.dll`/`.pdb`, and each GUI package gained 16 and lost 3 files (Avalonia 12, §K) with no native PDBs.
- **Independent verification after publication** (fresh download into an empty directory):
  - 16/16 checksums `OK` with `sha256sum --check --strict`, listing exactly the 16 archives; all 17 files match their GitHub asset digests.
  - 17/17 release-file attestations verify (`--signer-workflow …/release.yml`; SLSA provenance v1, signer `release.yml@refs/tags/v0.1.0-beta.7`, source digest `715f95e`). GitHub's immutable-release attestation verifies (`gh release verify v0.1.0-beta.7`) and lists the same 17 digests.
  - All 16 extracted archives pass the release content checks (single top-level folder, tar entries `0/0` with executable hosts, zip CRCs, required files, only Campertron's own PDBs, no development files, the TD-35 guard) and `check_publish_runtime` (runtime **10.0.15**). Each contains `0.1.0-beta.7+715f95e…` in `CampertronLibrary.dll` and the host DLL, and the embedded `RecreationDotOrg.db.br` byte for byte.
  - linux-x64, from the published archives: the console clean install extracted the 226,471,936-byte DB, with no partial file. The GUI was still running after 30 s (real display; Xvfb isn't installed locally, and the workflow's smoke job used `xvfb-run`). Both `--version` outputs report `0.1.0-beta.7+715f95e…` and `Framework => .NET 10.0.15 (X64)`, the bundled runtime. Both DBs are `8712856097a91ee1…`, and `ReleaseCheck` passed (13/13 checks, 51 states) on both.
- **GHCR image `ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.0-beta.7`:**
  - index digest `sha256:eea998ce38843a533d3b022ae24fed053b22ab88001dd767266e459380c514c4`
  - **linux/amd64** `sha256:56f958d7…` and **linux/arm64** `sha256:72ff8e6f…`, plus two attestation manifests
  - both platforms: `DOTNET_VERSION=10.0.15`, `USER app`, labels version `0.1.0-beta.7` and revision `715f95e…`
  - The image attestation verifies (signer `release.yml@refs/tags/v0.1.0-beta.7`).
  - Registry tags: `0.1.0-beta.4` to `0.1.0-beta.7` and their attestation tags. **No `latest`, `0.1` or `0.1.0`** (404). The beta.4, beta.5 and beta.6 digests are unchanged.
  - **amd64, pulled and run** (Docker Engine 29.9.3, disposable bind mounts only): `--version` reports `0.1.0-beta.7+715f95e…` and `.NET 10.0.15 (X64)`. Without `/config` it fails with "Config path does not exist". A clean start with `/config` and `/cache` extracted `8712856…`, and `ReleaseCheck` (the image's `/app` library) passed. A Watchman search wrote 389 derived JSON files and the HTML result to `/cache`, and `docker diff` showed no write to the container layer. It ended with an `ArgumentException` (empty address) and sent no mail, because the clean start had created an empty sample `Email.yaml`. A refresh on the same RIDB export as §N/§O (ETag `102488648295dd185cdbe8539adf4dcd`) succeeded in 66 s: `/cache` was emptied, including the stale files; the DB is `a825ce99…` with `quick_check` ok; `lastRidbDataRefresh` advanced; no staging, zip, `Extract` or WAL was left. A restart did not download again, and `ReleaseCheck` passed on the refreshed DB.
  - **arm64: inspected, not run** (no emulation on the validation host). From a created, never started container: `libe_sqlite3.so` is AArch64, the runtimeconfig passes `check_framework_dependent_runtimeconfig`, and the library carries the version stamp and the embedded DB.
- **Curated release notes, installed after verification:** the release first showed the generated template (04:41:03 to 04:50:27 UTC; byte-identical to the rehearsal's rendering). After every check above had passed, the approved curated notes (72 lines, SHA-256 `fe64bab3…`: what's new, upgrade guidance, known limitations including the `win-x86`/`linux-arm` platform-tier line, then the generated sections) were installed with `gh release edit v0.1.0-beta.7 --notes-file …`. The stored body is byte-identical to the approved file and passes `validate_notes`. The title, flags, immutability, Latest, tag, all 17 asset IDs, sizes and digests, the re-downloaded SHA-256 values, all attestations and the GHCR tags and digests were unchanged afterwards.
- **Untouched:** the tags `v0.1-beta1`, `v0.1-beta3`, `v0.1.0-beta.4`, `v0.1.0-beta.5` and `v0.1.0-beta.6` (identical `ls-remote`), and the beta.4, beta.5 and beta.6 release records (IDs, flags, immutability, notes, and all asset IDs, sizes, digests and timestamps identical to the snapshot taken before tagging). No repository file changed during the release.
- **Not run:** the Windows and macOS packages (packaged and inspected only), the `linux-arm` and `linux-arm64` packages, and the arm64 container, all not executed natively; an actual email send; Docker on other hosts or with named volumes; power-loss and full-disk behavior. Only the linux-x64 archives received native release smoke execution. The expanded native smoke-test project is decisions.md R8.

**`v0.1.0-beta.8`, published 2026-11-12 13:05:33 UTC** [V]
- Tag: annotated, unsigned, object `f44968fc9ec9d873ea6fc5c07be5f1166e35c62f` ("Campertron 0.1.0-beta.8"), pointing to commit `84e5649b3cf55f8a48f9bac91f902e416cc9cb58` (merge of PR #34). Only this tag was pushed, with the explicit refspec `refs/tags/v0.1.0-beta.8`. Before tagging: `master` was clean and current with no later commits; no local tag, remote tag or release for `v0.1.0-beta.8` existed; there were no open PRs; CI on `84e5649` (run 43221407327) had succeeded; both rulesets and immutable releases were active; `release.yml` (blob `fa8302d0…`) and `.github/release-policy/` were unchanged since `v0.1.0-beta.7`. A snapshot of the tags, the beta.4–beta.7 release records and the GHCR digests was taken for the collateral comparison.
- Content: the GUI visual polish of PR #34 (§P, decision D28) and the README screenshot refresh. The console, search, refresh, cache, Docker image behavior and release policy are unchanged; the library change is `Read.GetCampgroundDetailsByName` plus a shared private lookup (`GetParkCampgroundByName` behaves as before). The embedded `RecreationDotOrg.db.br` is unchanged from beta.7 (SHA-256 `700cbd00…`). Pre-release dry run: manual run 43219645735 (`0.0.0-dryrun.15`) on `12bf3a0`, whose tree differs from the released tree only in `build-and-test.md` (§P).
- Workflow run https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43223202344 (`push` of `v0.1.0-beta.8`, head `84e5649`, attempt 1): **22/22 jobs succeeded** (13:00:48 to 13:05:37 UTC). Verify: release-policy tests 67/67, .NET 10 project check, tests **207/207** (173 + 34 GUI), 0 vulnerable packages in the 5 projects. All 16 packages passed the contents check (TD-35 guard on the 3 Windows GUI packages) and `check_publish_runtime` (10.0.15). Smoke: console clean install (226,471,936 bytes), xvfb GUI start, both `--version` outputs `0.1.0-beta.8+84e5649…` on the bundled `.NET 10.0.15 (X64)`, `ReleaseCheck` 13/13 on the GUI and on the console. Docker: both architectures built, amd64 container smoke passed. Assemble: 16 names, 16/16 checksums, notes validated.
- GitHub release ID 442958907, https://github.com/ShaunAnthonyHathaway/Campertron/releases/tag/v0.1.0-beta.8. A **normal release marked Latest** (`prerelease: false`, `releases/latest` returns it) and **immutable**, published by `github-actions[bot]`. The 17 assets were uploaded 13:05:14 to 13:05:28, before publication. `v0.1.0-beta.7` is no longer Latest; nothing else about it changed.
- **17 assets:** the 16 archives (1,111,212,201 bytes in total; console 61,859,448 to 65,611,995, GUI 72,046,888 to 78,346,372) plus `SHA256SUMS.txt` (1,782 bytes).
- **Independent verification after publication** (fresh download into an empty directory):
  - 16/16 checksums `OK` with `sha256sum --check --strict`, listing exactly the 16 archives; all 17 files match their GitHub asset digests.
  - 17/17 release-file attestations verify (`--signer-workflow …/release.yml`; signer `release.yml@refs/tags/v0.1.0-beta.8`, source digest `84e5649`, trigger `push`). `gh release verify v0.1.0-beta.8` passes.
  - All 16 extracted archives pass the release content checks (single top-level folder, tar entries `0/0` with executable hosts, zip CRCs, required files, only Campertron's own PDBs, no development files, the TD-35 guard) and `check_publish_runtime` (runtime **10.0.15**). Each carries `0.1.0-beta.8+84e5649…` in `CampertronLibrary.dll` and the host DLL, and the embedded `RecreationDotOrg.db.br` byte for byte. Only linux-x64 was executed; the other 14 archives were inspected only.
  - linux-x64, from the published archives, isolated `XDG_DATA_HOME`: the console clean install exited 0 and extracted the 226,471,936-byte DB (`8712856097a91ee1…`) with no partial file. The GUI was still running after 30 s on the real display (`timeout` 124; Xvfb isn't installed locally, and the workflow's smoke job used `xvfb-run`) and extracted the same DB; stopped by `timeout`, it left a 0-byte `-wal` and a `-shm` file, with the DB unchanged. Both `--version` outputs report `0.1.0-beta.8+84e5649…` and `Framework => .NET 10.0.15 (X64)`, the bundled runtime. `ReleaseCheck` passed (13/13, 51 states) on both DBs.
- **GHCR image `ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.0-beta.8`:**
  - index digest `sha256:d8f97e8a9fe87303a17ad917e37a495ba4046359a32408abf24b993da1f7b81c`
  - **linux/amd64** `sha256:10744ac6…` and **linux/arm64** `sha256:ee324f8b…`, plus two attestation manifests
  - both platforms: `DOTNET_VERSION=10.0.15`, `USER app`, labels version `0.1.0-beta.8` and revision `84e5649…`
  - The image attestation verifies (signer `release.yml@refs/tags/v0.1.0-beta.8`), also with `--bundle-from-oci`.
  - Registry tags: `0.1.0-beta.4` to `0.1.0-beta.8` and their attestation tags. **No `latest`, `0.1` or `0.1.0`** (404). The beta.4 to beta.7 digests are unchanged.
  - **amd64, pulled and run** (Docker Engine 29.9.3, disposable bind mounts only): `--version` reports `0.1.0-beta.8+84e5649…` and `.NET 10.0.15 (X64)`. Without `/config` it fails with "Config path does not exist" (exit 139; the `0.1.0-beta.7` image gives the same exit code and identical output on this host). A clean start with `/config` and `/cache` extracted `8712856…`, and `ReleaseCheck` (the image's `/app` library) passed. A Watchman search (the example `ZionConfig.yaml`, `outputTo: Email`) wrote 389 files (388 derived JSON files and the HTML result) to `/cache`, and `docker diff` showed no write to the container layer. As for beta.7, it ended with an `ArgumentException` (empty address) and sent no mail, because the clean start had created an empty sample `Email.yaml`. The RIDB refresh was not repeated: the refresh, cache and Docker code is unchanged since beta.7, which validated it, and nothing in this run suggested a regression.
  - **arm64: inspected, not run** (no emulation on the validation host). From a created, never started container: `libe_sqlite3.so` is AArch64, the runtimeconfig passes `check_framework_dependent_runtimeconfig`, both DLLs carry the version stamp, the library contains the embedded DB, and its `CampertronLibrary.dll` is identical to the amd64 image's.
- **Curated release notes, installed after verification:** the release first showed the generated template (byte-identical to `render_notes` for this version). After every check above had passed, the approved curated notes (75 lines, SHA-256 `fb4244c7…`: what's new in the GUI, upgrade guidance, known limitations including duplicate campground names (TD-38) and the `win-x86`/`linux-arm` platform-tier line, then the generated sections) were installed with `gh release edit v0.1.0-beta.8 --notes-file …`. The stored body is byte-identical to the approved file and passes `validate_notes`. The title, flags, immutability, Latest, tag, all 17 asset IDs, sizes, digests and timestamps, the re-downloaded files (byte-identical), all attestations, `gh release verify` and the GHCR tags and digests were unchanged afterwards.
- **Untouched:** the tags `v0.1-beta1`, `v0.1-beta3` and `v0.1.0-beta.4` to `v0.1.0-beta.7` (identical `ls-remote`; the only addition is `v0.1.0-beta.8`), and the beta.4 to beta.7 release records (IDs, flags, immutability, notes, and all asset IDs, sizes, digests and timestamps identical to the pre-tag snapshot). No repository file changed during the release.
- **Not run:** the Windows and macOS packages (packaged and inspected only), the `linux-arm` and `linux-arm64` packages, and the arm64 container, all not executed natively; a physical-keyboard pass, a screen reader and the Windows/macOS GUI (open question #29); an actual email send. Only the linux-x64 archives received native release smoke execution (decisions.md R8).

**`v0.1.0-beta.9`, published 2026-11-14 03:28:01 UTC** [V]
- Tag: annotated, unsigned, object `905342731a0d6ce696db37236d74b23999107f23` ("Campertron 0.1.0-beta.9"), pointing to commit `24fd40d309db1cf4b35cb2ff30914190238bdd3a` (merge of PR #37). Only this tag was pushed, with the explicit refspec `refs/tags/v0.1.0-beta.9`. Before tagging: `master` was clean and current with no later commits; no local tag, remote tag, release, draft or GHCR tag for `v0.1.0-beta.9` existed; there were no open PRs; both rulesets and immutable releases were active; `release.yml` (blob `fa8302d0…`) and `.github/release-policy/` (tree `1bb99070…`) were unchanged since `v0.1.0-beta.8`. A snapshot of the tags, the beta.4–beta.8 release records and the GHCR digests was taken for the collateral comparison.
- Content: the generated HTML results page of PR #36 (§Q, decision D29) with the README screenshots `docs/ss7.png` and `docs/ss8.png`, and the TD-40 fix of PR #37 (§R). The GUI, the release policy and the Docker image setup are unchanged; the embedded `RecreationDotOrg.db.br` is unchanged from beta.8. Pre-release dry run: manual run 43550319398 (`0.0.0-dryrun.17`) on `24fd40d` itself: 20 jobs succeeded and the 2 publishing jobs were skipped, with 16 archives, 16/16 checksums and validated notes in the `release-metadata` artifact, and nothing published.
- Workflow run https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43551346862 (`push` of `v0.1.0-beta.9`, head `24fd40d`, attempt 1): **22/22 jobs succeeded** (03:23:00 to 03:28:05 UTC). Verify: release-policy tests 67/67, .NET 10 project check, Release build 0 errors and 77 warnings (identities identical to the approved baseline), tests **257/257** (223 + 34 GUI), 0 vulnerable packages in the 5 projects. All 16 packages passed the contents check (TD-35 guard on the 3 Windows GUI packages) and `check_publish_runtime` (10.0.15). Smoke: console clean install (226,471,936 bytes), xvfb GUI start, both `--version` outputs `0.1.0-beta.9+24fd40d…` on the bundled `.NET 10.0.15 (X64)`, `ReleaseCheck` on the GUI and on the console. Docker: both architectures built, amd64 container smoke passed. Assemble: 16 names, 16/16 checksums, notes validated.
- GitHub release ID 444985519, https://github.com/ShaunAnthonyHathaway/Campertron/releases/tag/v0.1.0-beta.9. A **normal release marked Latest** (`prerelease: false`, `releases/latest` returns it) and **immutable**, published by `github-actions[bot]`. The 17 assets were uploaded 03:27:41 to 03:27:59, before publication. `v0.1.0-beta.8` is no longer Latest; nothing else about it changed.
- **17 assets:** the 16 archives (1,111,408,364 bytes in total; console 61,870,739 to 65,623,504, GUI 72,059,581 to 78,358,063) plus `SHA256SUMS.txt` (1,782 bytes).
- **Independent verification after publication** (fresh download into an empty directory):
  - 16/16 checksums `OK` with `sha256sum --check --strict`, listing exactly the 16 archives; all 17 files match their GitHub asset digests.
  - 17/17 release-file attestations verify (`--signer-workflow …/release.yml --format json`; signer `release.yml@refs/tags/v0.1.0-beta.9`, source digest `24fd40d`, trigger `push`). `gh release verify v0.1.0-beta.9` passes and lists the same 17 digests.
  - All 16 extracted archives pass the release content checks (single top-level folder, tar entries `0/0` with executable hosts, zip CRCs, required files, native SQLite of the expected architecture, only Campertron's own PDBs, the TD-35 guard) and `check_publish_runtime` (runtime **10.0.15**). Each carries `0.1.0-beta.9+24fd40d…` in `CampertronLibrary.dll` and the app DLL, and the embedded `RecreationDotOrg.db.br` byte for byte. Only linux-x64 was executed; the other 14 archives were inspected only.
  - linux-x64, from the published archives, isolated `XDG_DATA_HOME`: the console clean install exited 0 and extracted the 226,471,936-byte DB (`8712856097a91ee1…`) with no partial file. The GUI was still running after 30 s on the real display (`timeout` 124) and extracted the same DB; stopped by `timeout`, it left a 0-byte `-wal` and a `-shm` file. Both `--version` outputs report `0.1.0-beta.9+24fd40d…` and `Framework => .NET 10.0.15 (X64)`, the bundled runtime. `ReleaseCheck` passed (13/13, 51 states) on both DBs.
- **GHCR image `ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.0-beta.9`:**
  - index digest `sha256:59aee161e5c0244d59d8d3fbc869dff3882965948ac960148519766a25a90325`
  - **linux/amd64** `sha256:c0fc6eba…` and **linux/arm64** `sha256:110bc24a…`, plus two attestation manifests
  - both platforms: `DOTNET_VERSION=10.0.15`, `USER app`, labels version `0.1.0-beta.9` and revision `24fd40d…`
  - The image attestation verifies (signer `release.yml@refs/tags/v0.1.0-beta.9`).
  - Registry tags: `0.1.0-beta.4` to `0.1.0-beta.9` and their attestation tags. **No `latest`, `0.1` or `0.1.0`** (404). The beta.4 to beta.8 digests are unchanged.
  - **amd64, pulled and run** (Docker Engine 29.9.3, disposable bind mounts only): `--version` reports `0.1.0-beta.9+24fd40d…` and `.NET 10.0.15 (X64)`. Without `/config` it fails with "Config path does not exist" (exit 139, as for beta.8). A clean start with `/config` and `/cache` extracted `8712856…` with no partial file, and `ReleaseCheck` (the image's `/app` library) passed 13/13. A Watchman search (the example `ZionConfig.yaml`, `outputTo: Email`) wrote 389 files to `/cache` (388 derived JSON files and a 904,649-byte results page with 2,392 recreation.gov booking links and no script), and `docker diff` showed no write to the container layer. As for beta.8, it ended with an `ArgumentException` (empty address) and sent no mail, because the clean start had created an empty sample `Email.yaml`.
  - **arm64: inspected, not run** (no emulation on the validation host). From a created, never started container: `libe_sqlite3.so` is AArch64, the runtimeconfig passes `check_framework_dependent_runtimeconfig`, both DLLs carry the version stamp, the library contains the embedded DB, and its `CampertronLibrary.dll` is identical to the amd64 image's. `CampertronConsole.dll` differs from amd64 in the PE header's Machine field (the Dockerfile builds with `-a $TARGETARCH`), exactly as between the two beta.8 images.
- **Curated release notes, installed after verification:** the release first showed the generated template (34 lines, SHA-256 `7b9000d6…`, byte-identical to `render_notes` for this version). After the dry run, the maintainer approved a correction of the two bullets on searches that run at the same time: searches still share the downloaded month data, and each search now calculates its available dates separately without changing it. After every check above had passed, the approved curated notes (71 lines, 8,436 bytes, SHA-256 `8e74806417b27297e2c51bc29e67eaac8c0914859deb67c8fb92ecf35dca3518`: the redesigned results page, email, the parallel-search fix, upgrade guidance, known limitations including email clients, consecutive-night searches run together with other searches (TD-04), duplicate campground names (TD-38) and the `win-x86`/`linux-arm` platform-tier line, then the generated sections) were installed with `gh release edit v0.1.0-beta.9 --notes-file …`. The stored body is byte-identical to the approved file and passes `validate_notes`. The title, flags, immutability, Latest, tag, all 17 asset IDs, sizes, digests and timestamps, all attestations, `gh release verify` and the GHCR tags and digests were unchanged afterwards.
- **Untouched:** the tags `v0.1-beta1`, `v0.1-beta3` and `v0.1.0-beta.4` to `v0.1.0-beta.8` (identical `ls-remote`; the only addition is `v0.1.0-beta.9`), and the beta.4 to beta.8 release records (IDs, titles, flags, immutability, full notes, and all asset IDs, sizes, digests and timestamps identical to the pre-tag snapshot). No repository file changed during the release.
- **Not run:** the Windows and macOS packages (packaged and inspected only), the `linux-arm` and `linux-arm64` packages, and the arm64 container, all not executed natively; an actual email send, so rendering in Gmail, Outlook and Apple Mail is still unverified (open question #31). Only the linux-x64 archives received native release smoke execution (decisions.md R8).

**`v0.1.0-beta.10`, published 2026-11-14 18:13:35 UTC** [V]
- Tag: annotated, unsigned, object `6e76ea44f1362fd62857c2808646920cfd9b4f3f` ("Campertron 0.1.0-beta.10"), pointing to commit `b5fcf9f570f3791ade0925d0e92385084d51813e` (merge of PR #45, tree `5929e7e4…`). Only this tag was pushed, with the explicit refspec `refs/tags/v0.1.0-beta.10`. Before tagging:
  - `master` was clean and current with no later commits, and its post-merge CI (run 43623965637) had succeeded.
  - No local tag, remote tag, release, draft or GHCR tag for `v0.1.0-beta.10` existed, and there were no open PRs.
  - Both rulesets and immutable releases were active.
  - In a clean clone of the tagged commit, the curated notes composed to the approved body and `test-release-policy.sh` passed 277/277.
  - A snapshot of the tags, the beta.4–beta.9 release records and the GHCR digests was taken for the collateral comparison.
- Content: PRs #40 to #45.
  - The manual-search notification-history fix (PR #40, §T, business-rules §5).
  - The .NET 11 platform migration (PR #41, §U, D31).
  - The generated-notes wording fix (PR #42).
  - The PDB-provenance enforcement (PR #43, §V).
  - The validated-image publication (PR #44, §W, D32).
  - The curated notes (PR #45).
- Unchanged and first uses: the embedded `RecreationDotOrg.db.br` is unchanged from beta.9. There was no manual dry run; the release dry runs of PRs #41 to #44 exercised the changed workflow (the last was 43621409952). This is the first release with curated notes in the draft (D30), the .NET 11 guards (D31) and the validated-image publication (D32).
- Workflow run https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43624820186 (`push` of `v0.1.0-beta.10`, head `b5fcf9f`, attempt 1): **22/22 jobs succeeded** (18:07:27 to 18:13:38 UTC), none skipped.
  - **Verify:**
    - Release-policy tests 277/277. The curated notes were required and composed (`49ad73e3…`).
    - .NET 11 project check, SDK 11.0.100 through `global.json`, package policy for 5 projects.
    - Release build: 0 errors and 76 warnings, with identities identical to the approved baseline; every warning annotation in the run is one of them.
    - Tests **278/278** (244 + 34 GUI). 0 vulnerable packages in the 5 projects.
  - **All 16 packages:** SDK 11.0.100, the contents check (TD-35 guard on the 3 Windows GUI packages), `check_publish_runtime` (11.0.0) and `check_pdb_provenance`.
  - **Smoke:**
    - Console clean install: 226,471,936 bytes.
    - xvfb GUI start.
    - Both `--version` outputs report `0.1.0-beta.10+b5fcf9f…` on the bundled `.NET 11.0.0 (X64)`.
    - `ReleaseCheck` 13/13 on the GUI and on the console.
  - **Assemble:**
    - 16 names.
    - One toolchain for all 16 archives: `5.12.0-1.26529.118+297acf93…` on .NET `11.0.0-rtm.26529.118+297acf93…`.
    - 16/16 checksums. Notes validated.
  - **Docker (D32):**
    - One multi-platform OCI build, from `runtime:11.0@sha256:3fe5f549…` and `sdk:11.0@sha256:65f3c7c6…`.
    - `check_oci_archive` passed for both platforms, including the PDB compiler provenance and one compiler for both.
    - The loaded amd64 image is the validated amd64 manifest (containerd image store, 9 identical diff_ids). The container smoke and .NET 11 checks passed.
    - The skopeo 1.21.0-dev rehearsal kept the index (4 manifests, 29 blobs).
    - The archive was uploaded as `container-image`: 254,096,384 bytes, SHA-256 `32fb56f7…`.
  - **docker-publish:**
    - Before `docker login`, the downloaded archive matched the docker job's SHA-256, index and both platform manifests, and passed the full image check again.
    - The only reference was `…:0.1.0-beta.10`. It was absent, so nothing was moved.
    - **The first authenticated `skopeo copy --all --preserve-digests` to GHCR succeeded** (§W). The registry index equals the validated digest and lists both platform manifests.
    - The image attestation's subject is that digest. Nothing was built in this job.
  - **Release job:**
    - 16/16 checksums against the assembled `SHA256SUMS.txt`.
    - The notes rebuilt from the tagged commit equal the `release-metadata` copy.
    - Build-provenance attestation over 17 subjects.
    - The draft had 17 assets and a byte-identical body before it was published. The public body was read back byte-identical, and `releases/latest` returned the tag.
- GitHub release ID 445493647, https://github.com/ShaunAnthonyHathaway/Campertron/releases/tag/v0.1.0-beta.10. A **normal release marked Latest** (`prerelease: false`, `releases/latest` returns it) and **immutable**, published by `github-actions[bot]`. The 17 assets were uploaded 18:13:23 to 18:13:33, before publication. `v0.1.0-beta.9` is no longer Latest; nothing else about it changed.
- **Release body:** the approved curated fragment (`c91c0173…`, once, byte for byte) composed with the generated sections. 85 lines, 10,401 bytes, SHA-256 `49ad73e35c3226d0f503856982346b38179715c31c6b39c55b17bf21fae8f1e9`. It was published in the draft; no notes were installed after publication (D30).
- **17 assets:** the 16 archives (1,142,489,216 bytes in total; console 63,300,284 to 68,393,539, GUI 73,854,432 to 81,133,889) plus `SHA256SUMS.txt` (1,798 bytes).
- **Independent verification after publication** (fresh download into an empty directory):
  - **Checksums and digests:** 16/16 checksums `OK` with `sha256sum --check --strict`, listing exactly the 16 archives. All 17 files match their GitHub asset digests.
  - **Attestations:** 17/17 release-file attestations verify (`--signer-workflow …/release.yml --format json`; signer `release.yml@refs/tags/v0.1.0-beta.10`, source digest `b5fcf9f`, trigger `push`, 17 subjects). `gh release verify v0.1.0-beta.10` passes and lists the same 17 digests.
  - **All 16 extracted archives:**
    - They pass the release content checks: single top-level folder, safe member names, tar entries `0/0`, zip test, required files, only Campertron's own PDBs, and the TD-35 guard.
    - They pass `check_publish_runtime` (runtime **11.0.0**) and `check_pdb_provenance`, with one compiler for all 16.
    - Native SQLite matches each RID, read from the binary headers.
    - Each carries `0.1.0-beta.10+b5fcf9f…` in `CampertronLibrary.dll` and the app DLL. `CampertronLibrary.dll` is byte-identical in all 16 and embeds the approved `RecreationDotOrg.db.br` exactly once.
    - Only linux-x64 was executed; the other 14 archives were inspected only.
  - **linux-x64, from the published archives, isolated `XDG_DATA_HOME`:**
    - **Console:** the clean install exited 0 and extracted the 226,471,936-byte DB (`8712856097a91ee1…`) with no partial file.
    - **`--version`:** both apps report `0.1.0-beta.10+b5fcf9f…` and `Framework => .NET 11.0.0 (X64)`, the bundled runtime.
    - **GUI** (real display, KDE Plasma Wayland with XWayland): it extracted the same DB. In two runs it stayed up for 15 s and 30 s and then exited 0 on a normal close request (KWin `closeWindow()`), with no output; the DB was closed cleanly and is unchanged. The first GUI run instead exited by itself (code 0, DB closed cleanly) within 13 s of launch (an upper bound: the harness checked it once, 12 s after the DB appeared), before the close request. Another run stayed up until a 40 s `timeout`. The cause is undetermined (open question #37; the R8 investigation found it most likely to be a desktop close during concurrent manual use of a separate beta.10 copy, not an application defect).
    - **`ReleaseCheck`:** passed (13/13, 51 states) on both DBs.
  - **Upgrade from beta.9:**
    - Setup: data first initialized by the published beta.9 console, with the example `ZionConfig.yaml` and 2,387 `Camp.History` markers. Of those, 2,385 were written by beta.9's Email mode, which then aborted on the sample's empty address and sent no mail; 2 were synthetic.
    - The beta.9 Console search showed "No entries found" for both months.
    - The first beta.10 start was a Console search on the same data. It listed 2,385 entries on 172 sites, all of them marked. It left the DB (same inode, mtime and hash), the config files and every marker unchanged, with no repair or migration. `ReleaseCheck` passed on that data.
    - In beta.10 Email mode, the marked matches were not reported again. Two newly opened dates got exactly two new markers before the expected abort on the empty address; no mail was sent.
- **GHCR image `ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.0-beta.10`:**
  - index digest `sha256:a63e99aa41d7b217cf0e6957ec1c6de649850eab6258fe3a5ed9eb7f16727bf5`, the docker job's validated index
  - **linux/amd64** `sha256:23b9329a06bdd38a2006318ad545c09ae08ee3111910d3dd6539350345e6ef4a` and **linux/arm64** `sha256:2d9ce5b45351fe413ecb7a203d73ca07c968d96226f04f957c9bab6527fa4beb`, plus two attestation manifests; no other platform
  - **Registry re-check:** the registry image was fetched byte for byte into a local OCI layout and passes `check_oci_image`, with every output identical to the docker job's. That covers both platforms: `DOTNET_VERSION=11.0.0`, `USER app`, the entrypoint, labels version `0.1.0-beta.10`, revision `b5fcf9f…` and source, Ubuntu 26.04, native SQLite x86-64 and AArch64, and SDK 11 compiler provenance.
  - The image attestation verifies (subject `a63e99aa…`, signer `release.yml@refs/tags/v0.1.0-beta.10`).
  - Registry tags: `0.1.0-beta.4` to `0.1.0-beta.10` and their attestation tags. **No `latest`, `0.1` or `0.1.0`** (404). The beta.4 to beta.9 digests are unchanged.
  - **amd64, pulled and run** (Docker Engine 29.9.3, disposable bind mounts only):
    - The pulled layers equal the validated amd64 diff_ids.
    - `--version` reports `0.1.0-beta.10+b5fcf9f…` and `.NET 11.0.0 (X64)`, on Ubuntu 26.04.1 as uid `app`.
    - Without `/config` it fails with "Config path does not exist" (exit 139, open question #35).
    - A clean start with `/config` and `/cache` extracted `8712856…` with no partial file, and `ReleaseCheck` (the image's `/app` library) passed 13/13.
    - `docker diff` showed only the two bind-mount points `/config` and `/cache`, which the image doesn't contain, and no file write.
  - **arm64:** validated structurally and for compiler provenance (above), not run.
- **Untouched:** the tags `v0.1-beta1`, `v0.1-beta3` and `v0.1.0-beta.4` to `v0.1.0-beta.9` (identical `ls-remote`; the only addition is `v0.1.0-beta.10`). The beta.4 to beta.9 release records are identical to the pre-tag snapshot: IDs, titles, flags, immutability, full notes, and all 102 asset IDs, sizes, digests, states and timestamps. No repository file changed during the release.
- **Not run:**
  - Not executed natively: the Windows and macOS packages (packaged and inspected only), the `linux-arm` and `linux-arm64` packages, and the arm64 container. Only the linux-x64 archives and the amd64 container received native execution (decisions.md R8).
  - An actual email send, so rendering in email clients is still unverified (open question #31).

**`v0.1.0-preview.1`, published 2026-11-15 04:12:29 UTC** [V]
- The first preview (D38). Full record: §AC.
- Tag: annotated, unsigned, object `51cf1a225c95f1fdc9ccf7437d19f0a20ed55b58` ("Campertron 0.1.0-preview.1"), pointing to commit `6484dc21c31f2c78681f998f3e2c334d3e584153` (merge of PR #54). Only this tag was pushed, with the explicit refspec `refs/tags/v0.1.0-preview.1`.
- Content: R8 (PR #47, D33, §X), the console output polish (PR #49, D34, §Y) and the deferred-work cleanup P1 to P4 (PRs #50 to #53, D35 to D37, §Z to §AB), with the curated notes of PR #54. Before the tag, the approved native-smoke dry run (Release run #30, 43673714514) passed on the same commit.
- Workflow run https://github.com/ShaunAnthonyHathaway/Campertron/actions/runs/43675087450 (#31, `push`, attempt 1): **26/26 jobs succeeded**, including native smoke on linux-x64, win-x64, win-arm64, osx-x64 and osx-arm64, `docker-publish` and `release`. Tests 428 + 52, 0 errors and 74 warnings, release-policy tests 322/322, 0 vulnerable packages.
- GitHub release ID 445802503, https://github.com/ShaunAnthonyHathaway/Campertron/releases/tag/v0.1.0-preview.1: a **normal release marked Latest** (`prerelease: false`) and **immutable**, 17 assets, body 16,746 bytes, SHA-256 `dd8db68632ac5568c4028f89e40e2dab3fcc9ce09b96e2e0454baec3de0bb51c`. `v0.1.0-beta.10` is no longer Latest; nothing else about it changed.
- GHCR `ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.0-preview.1`: index `sha256:b18e32cffa55075f02766fcf06c1d5fd7b2d629f0ce17f3b3cb087b68973bb95` (linux/amd64 `45464f99…`, linux/arm64 `b6666608…`); no `latest`, `0.1` or `0.1.0`.
- Verified after publication: checksums, archives, embedded version, the 17-subject build-provenance attestation and the image attestation (§AC). No recovery was required.

## Branch protection (`master`)
### Verified [V]
Read back from GitHub on 2026-11-10 via `GET /repos/ShaunAnthonyHathaway/Campertron/rulesets/26990088` and `GET …/rules/branches/master`.
- **Ruleset:** "Protect master", **ID 26990088** (https://github.com/ShaunAnthonyHathaway/Campertron/rules/26990088), created 2026-11-10.
  - `enforcement: active`, `target: branch`, `conditions.ref_name.include: ["refs/heads/master"]`, `exclude: []`.
  - It is the repo's only ruleset. There is no classic branch protection (`GET …/branches/master/protection` → 404). The branches API reports `master` as `protected: true`.
- **Pull requests required** (`pull_request` rule):
  - `required_approving_review_count: 0` (single maintainer).
  - `required_review_thread_resolution: true`: all conversations must be resolved.
  - `dismiss_stale_reviews_on_push: false`, `require_code_owner_review: false`, `require_last_push_approval: false`, `required_reviewers: []`.
  - `allowed_merge_methods: ["merge"]`: **merge commits are the only merge method into `master`**. Squash and rebase are not allowed.
- **Required CI check** (`required_status_checks` rule):
  - `{"context": "Restore, build, test, vulnerability scan", "integration_id": 15368}`. This is the CI job `name:` in `.github/workflows/ci.yml`; 15368 is the `github-actions` app.
  - `strict_required_status_checks_policy: true`: the branch must be up to date with `master` before merging.
  - `do_not_enforce_on_create: false`.
- **Force pushes blocked** (`non_fast_forward` rule), and **deletion of `master` blocked** (`deletion` rule).
- **No bypass:** `bypass_actors: []`. The ruleset applies to everyone, including repository administrators; `current_user_can_bypass: "never"` for the owner.
- **Linear history not required:** there is no `required_linear_history` rule. The only rules are `deletion`, `non_fast_forward`, `pull_request` and `required_status_checks`.
- **Repo-level merge settings are unchanged:** `allow_merge_commit: true`, `allow_squash_merge: true`, `allow_rebase_merge: true`. The ruleset is what restricts PRs into `master` to merge commits.
- **Repo type:** public, owned by a personal user account (`owner.type: User`). The account's plan isn't visible to the CLI token (`plan: null`).

### Inferred, not verified [I]
- Rulesets are available for public repos on GitHub Free, so the plan doesn't matter here. A private repo would need GitHub Pro. Source: GitHub documentation, not checked through the API.
- Rulesets' "Evaluate" (dry-run) mode is Enterprise-only, which is why the ruleset was created `active`. Source: GitHub documentation, not checked through the API.

### Uncertain [?]
- **`require_extra_approval_for_unattributed_changes: true`** is present in the stored `pull_request` rule, but it was **not** in the request body that created the ruleset; GitHub added it as a default.
  - Its exact enforcement behavior has not been verified. [I] The name suggests an extra approval when a PR contains changes that can't be attributed to a user.
  - How it interacts with `required_approving_review_count: 0`, for example whether it could ever require an approval on a single-maintainer PR, is unknown.
  - ~~It has not been observed on a PR yet. The first PR merged under the ruleset will show whether it blocks anything.~~ **Answered empirically (consolidated audit, 2026-11-14):** all 35 PRs merged since the ruleset was created (#15 to #49) merged with 0 approving reviews, so it has never required an approval here; its documented meaning is still unverified. Changing it to `false` would be a ruleset settings change (open-questions.md Q19, closed).

### Consequences [V from the rules above]
- No direct pushes to `master`, including docs-only changes. Changes go branch → PR → green `Restore, build, test, vulnerability scan` check on an up-to-date branch → all conversations resolved → merge commit.
- Renaming the CI job's `name:`, or adding a `paths:` filter that skips some PRs, would leave the required check unreported and block merges until the ruleset is updated.
- As an admin, the owner can't bypass the rules but can edit or disable the ruleset.

## Recommended verification after changes
- [R] `dotnet build Campertron.sln`: 0 errors. Compare against the current 70-warning baseline (§AG to §AL; 74 from §Y to §AF; 76 from §T to §X; all compiler warnings, no NuGet, AVLN or RXUIBIND warnings).
- [R] Interactive search changes (`CampsiteSearch`, `SearchHost`, `HttpSearchTransport`, `SearchRunModel`, `RunningData.Host`, the host branch of `ProcessMonth`, `Cache.PrepareCampground`): run `CampsiteSearchTests`, `SearchHostTests` and `LoadTests`, the search suites of §Z, then the negative controls, the harness comparison of the console path (every output byte-identical) and the parallel-versus-isolated comparisons of §AJ, and the whole suite inside a network namespace. Keep the console's path unchanged (`Host` null), three requests per month in all, no process exit and no console output, no `Camp.History` and no Email settings, typed results only from the values' metadata, and every waiter of a shared month released (business-rules §19).
- [R] RIDB import changes (`PopulateDbFromRIDBFiles`, `RidbTableInserter`, the mapped models): run `PopulateTests`, then a full-export refresh in an isolated directory, and compare the checkpointed DB with the previous build's output on the same export and culture (§M: schema, counts, `EXCEPT` both ways with `rowid`, per-column `typeof`, SHA-256).
- [R] RIDB refresh changes (`RefreshRidbRecreationData`, `RidbStagedRefresh`, `Clear`, the auto-refresh in `Load.DbExistsCheck`): run `AtomicRefreshTests`, the kill harness and the cross-process scenarios, and the full-export comparison against the in-place core (§N).
- [R] GUI changes: run `CampertronGUITests` (since D42 including `CampgroundIdentityTests`, since D43 `SettingsEditorTests`, since D44 `SearchEditorTests`, since D46 `ResultsTabTests`, `ResultsBehaviorTests` and `ResultsViewTests`; after a change to the campground lists, details or selector templates also the library's `CampgroundIdentityTests` and `CampgroundChoiceTests`, §AG), then run the GUI with `XDG_DATA_HOME="$(mktemp -d)"` so it doesn't touch real data. Expect the main window to open with "Preparing campground data" and then the tabs; also run `CampertronGUI --smoke-test` (exit 0, `SMOKE result=pass`) and a start with an invalid `General.yaml` (error state, Try again). Check Light and Dark, 720×540 and a wide window (2400), and a keyboard pass (Tab must wrap to the tab header, TD-39). Bind every database-backed view-model property exactly once (`Selections_RunOneQueryEach`), create nothing database-backed before the startup succeeded (`StartupTests`) and take colors only from theme resources (D28). If the smoke report changes, change `check_gui_smoke_report` with it. A GUI PR gets the release dry run with the native smoke tests (§X). Since Avalonia 12 the TD-34 shutdown exception has not been observed (§K, §P, §X); report it if it returns.
- [R] Results-tab changes (`ResultsViewModel`, `ResultRows.cs`, `ProgressGate`, `ResultsView`, `RunProbe`, the tab's wiring in `MainWindow`/`MainWindowViewModel`, the shutdown wait in `App`/`Program`): run the Results suites, `SmokeTestTests`, `CampgroundIdentityTests`, GS1's suites and the release-policy tests, then the negative controls and the nested-session review of §AK. Keep: only the Results tab uses `CampsiteSearch` (no HTTP client, `AvailabilityApi`, console pipeline, `Camp.History` or `Email.yaml` in the GUI), no file written, one operation at a time with every callback checked, `Run` off the UI thread, at most ten progress publications a second, the five-minute local hold after a block, the link check, copies only on the user's action, the close canceling the run, and the smoke report changed together with `check_gui_smoke_report` (business-rules §20).
- [R] Console output changes (`ConsoleUi`, `ConsoleResults`, `Menu`, the messages in `Load`, `Cache`, `Yaml`, `GetWebApiData`, `CampertronConsole/Program.cs`): run `ConsoleUiTests`, `ConsoleResultsTests`, `ConsoleOutputTests`, `ConsoleFailureTests` and `ConsoleOutputGoldenTests`, then the isolated terminal, redirection, signal and container checks of §Y. For `Menu.cs` also `MenuKeysTests`, and keep `ProcessInput` returning to the loop in `WriteMenu` (never calling it): compare a scripted pseudo-terminal session byte for byte with the build before and press 200,000 keys with a 1 MB stack (§AL). `--version`, the silent `outputTo: UnitTest` start, the library's silence on a first start and the container messages are contracts (business-rules §12).
- [R] Output changes (the producers in `Cache`/`AvailabilityApi`, `ProcessConsoleConfig`, `ResultsHtml`, `Load.OutputResults`): run `ConsoleOutputGoldenTests`, `ConsoleResultsTests`, `ResultsHtmlTests` and `HtmlResultsOutputTests`, compare a rendered page byte for byte with the previous build's (§Y), then generate a page from a real search in an isolated `XDG_DATA_HOME` (with a recording `xdg-open` on `PATH`) and check it in Chrome and Firefox at 320 px and a desktop width, Light and Dark (§Q).
- [R] Search changes (`AvailabilityApi`, `Load.RunConsoleSearch`, `CampertronConfig`'s dates, anything reading `RunData.SiteData` or `RunData.FilteredAvailableData`): run `AvailableDatesTests`, `SharedMonthSearchTests`, `SearchIsolationTests`, `SearchDatesTests` and `ConsoleOutputGoldenTests`, then compare every search of a shared run (in both orders and in parallel) with the same search run alone, as in §Z: since D35 **any** difference is a regression, consecutive-night searches included (before D35 their differences were TD-04, §T). The cached month data must stay read-only (TD-40); a search must never read or replace another search's hits (TD-04); dates the search builds itself must not be parsed from culture-dependent text (TD-11); the Email loop's regeneration must stay quiet and must not read the search files (TD-46).
- [R] Notification-history changes (`Load.UsesNotificationHistory`, `Load.HasUnreportedEntries`, `Load.RecordNotificationHistory`, `Load.HasNewEntries`, the `Camp.History` check in `FilterData`, `Load.ReportResults`'s Email gate, the sends in `Load.OutputResults` and `CampsiteConfig.SendResultsEmail`): run `NotificationHistoryTests`, `EmailDeliveryHistoryTests` and `LoadTests`, then the isolated before/after sequences of §T (manual Console/HtmlFile repeatedly and across mode switches, Email cycles) and the refused and partial SMTP scenarios of §AA. Only Email may read or write `Camp.History`, a marker only after its email was delivered, and the directory only with its first marker; keep the marker format.
- [R] Settings, exit-code and data-folder changes (`Yaml.*GetConfig*`, the example search, `CampertronConfig.GenerateSearchDates`' errors, `AvailabilityApi`'s blocked path, `Load.GetConfig`/`DesktopDataFolder`, `Compression.Unzip`): run `SettingsBootstrapTests`, `DataFolderTests`, `ConsoleFailureTests`, `AtomicRefreshTests` and `CachePathTests`, then the console checks of §AB (a commented, disabled `ZionConfig.yaml` stays byte-identical; equal dates and a blocked answer exit 1; a fresh `HOME` leaves the working directory empty; a foreign `Extract` folder survives a refresh).
- [R] YAML document editor changes (`YamlDocument`, `YamlDocumentModel`, `Yaml.SettingsDeserializer`): run `YamlDocumentTests`, `YamlDocumentPreservationTests` and `YamlDocumentRefusalTests`, then the negative controls of §AD on the changed part. Keep it free of file, console, environment and network access (the IL scan), keep every refusal and lock, never let a diagnostic quote a value, and never return text that failed a check (business-rules §13). Changing the loaders' deserializer also changes what the editor checks against: run the loaders' suites of §AB too.
- [R] Settings writer changes (`ConfigFileReplace`, `Yaml.GeneralConfigReplaceYaml`, the sample writers `Yaml.*ConvertToYaml` and their callers in `Yaml.*GetConfig*`): run `ConfigFileReplaceTests`, `GeneralYamlTimestampTests`, `SettingsBootstrapTests` and `AtomicRefreshTests`, then the negative controls and the interruption harness of §AE on the changed part. Never write a settings file in place or re-serialize it, never write over a file that exists or changed since it was read, keep the mode, the links, the backup and the strict UTF-8, and keep every message free of the file's values (business-rules §14). The Windows paths (`MoveFileExW`, the access rules, read-only files, sharing, reparse points) run natively only in the Windows CI job: a change to them needs that job green (and a new Windows-only behavior a test in `ConfigFileReplaceWindowsTests`).
- [R] Configuration validation and loader changes (`ConfigValidation`, `ConfigDiagnostic`, `ConfigFiles`, `Yaml.ReadSettingsText`/`ScanSettings`/`LoadSettingsText`, `Load.CheckEmailOutputSettings`): run `ConfigFilesTests`, `SettingsLoaderLimitsTests`, `ConfigValidationTests`, `EmailOutputValidationTests`, the CM1 and CM2 suites and `ConsoleFailureTests`, then the negative controls of §AF on the changed part. Keep the loader, the validation and the editor on the same limits and the same deserializer, refuse at run time only what would take a dangerous path, check `Email.yaml` before the Email loop searches, and never put a value of a file into a diagnostic (business-rules §15).
- [R] Release workflow and release-policy changes (`release.yml`, `release-policy.sh`, `test-release-policy.sh`): run `bash .github/release-policy/test-release-policy.sh` (392, §AN), `bash -n` on every `run:` block and the negative controls of the changed part; the pull request gets the Release dry run automatically, and every job of it, the native, 64-bit-Windows and container smoke jobs included, must be green before the merge. Never claim a native run for a platform that is only built, inspected, emulated or run as compatibility execution.
- [R] CI (`.github/workflows/ci.yml`) must pass on the PR, both jobs: Ubuntu: Release build 0 errors, tests 1073 (929 + 144 GUI; 20 Windows-only tests skipped on Linux; §AL), no vulnerable packages; Windows (`Windows settings files (CM1 to CM3, CM5, CM6)`): the CM1, CM2 and CM3 suites, the settings editor's session suites and the search-file suites (§AE, §AF, §AH, §AI).
- [R] `XDG_DATA_HOME="$(mktemp -d)" dotnet test Campertron.sln …`: 1073 (1053 passed, 20 skipped on Linux; §AL); no test reaches the network. Apart from `AddressStateTests`, `CampgroundIdentityTests`, `CampgroundChoiceTests`, `PopulateTests`, `AtomicRefreshTests`, `ConfigFileReplaceTests`, `GeneralYamlTimestampTests`, `SettingsBootstrapTests`, `LegacyAddressLayoutTests`, `RidbDatabaseTests` and `EmbeddedDatabaseTests`, a pass mainly proves nothing crashes, because the tests don't assert filter results (feature-inventory.md §Test coverage).
