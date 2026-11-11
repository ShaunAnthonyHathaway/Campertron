# Build and test

Legend: **[V]** verified (command run or file read) · **[A]** assumption, not executed · **[R]** recommendation

## Required environment
- [V] .NET SDK 10.x with the **.NET 10 runtime and the .NET 10 targeting pack**. All four projects target `net10.0`. There is no `global.json`, so the host uses the newest installed SDK.
- [V] On Arch/CachyOS, the SDK, runtime and targeting pack are separate packages. The audit machine needed `dotnet-sdk` 10.0.14.sdk114-1, `dotnet-runtime` 10.0.14 and `dotnet-targeting-pack-10.0` 10.0.14.sdk114-1. When one was missing, the failure looked like this:
  - **runtime missing:** every `dotnet` command fails with exit code 150, "You must install or update .NET to run this application … 'Microsoft.NETCore.App', version '10.0.14'"
  - **targeting pack missing:** `restore` fails with `NETSDK1226: Prune Package data not found .NETCoreApp 10.0 Microsoft.NETCore.App`. Do **not** work around this with `AllowMissingPrunePackageData`.
- [V] The GUI needs a desktop session (Avalonia `UsePlatformDetect`, `WinExe`). An isolated smoke test passed on 2026-11-10 (§E).
- [V] Docker is only needed for the container build (`CampertronConsole/Dockerfile`, base images `mcr.microsoft.com/dotnet/runtime:10.0` and `sdk:10.0`). It needs **BuildKit**, the default builder since Docker 23, because the Dockerfile uses `$BUILDPLATFORM` and `$TARGETARCH`. Docker is **not installed** on the audit machine; the image is built and smoke-tested by the release workflow (§Release workflow).
- [V] Tests and search need network access to `www.recreation.gov`. RIDB refresh needs `ridb.recreation.gov`. Restore needs `api.nuget.org`.
- [V] Disk: the embedded `RecreationDotOrg.db.br` is 26,616,039 bytes and extracts to a 226,471,936-byte `RecreationDotOrg.db`. Until `07e786d`, the embedded `RecreationDotOrg.zip` was 81,232,118 bytes and extracted to 328,126,464 bytes.

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
docker build -f CampertronConsole/Dockerfile -t campertronconsole .   # [A] docker not installed; needs BuildKit (TARGETARCH)
docker buildx build --platform linux/amd64,linux/arm64 -f CampertronConsole/Dockerfile .   # multi-arch, as in release.yml
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

### Refreshing the embedded database (maintainer procedure)
Verified 2026-11-10 for `07e786d`. All steps run in a scratch directory, never in the user's data dirs.
1. `dotnet build Campertron.sln -c Release`
2. Create an empty `X=$(mktemp -d)` and write `$X/CampertronConfig/General.yaml` with `outputTo: UnitTest`, `autoRefresh: false`, `refreshRidbDataDayInterval: 30` and any `lastRidbDataRefresh`. Then run `XDG_DATA_HOME=$X CampertronConsole/bin/Release/net10.0/CampertronConsole`. This extracts the bundled DB and exits without a menu.
3. Set `autoRefresh: true` and a `lastRidbDataRefresh` older than 30 days, then run the console again. It downloads the RIDB export (about 573 MB), imports the 6 used files and validates the result (about 2.5 min). **The output must not contain "failed validation".**
4. `sqlite3 $X/CampertronConfig/RecreationDotOrg.db "VACUUM; PRAGMA integrity_check;"` must print `ok`.
5. Compress with .NET's built-in encoder, not a CLI tool, so the output matches what `BrotliStream` reads. Use `new System.IO.Compression.BrotliEncoder(11, 24)`, feed the file through `Compress(..., isFinalBlock: false)` and finish with `isFinalBlock: true`. Then verify that decompressing through `BrotliStream` gives the same SHA-256 as the DB. This takes about 6 minutes.
6. Replace `CampertronLibrary/content/RecreationDotOrg.db.br`, then run the full isolated test suite. `EmbeddedDatabaseTests` checks the shipped DB with the same validation as step 3.
7. GitHub rejects files over 100 MiB; the file is currently 26.6 MB. Record the export date, ETag, sizes and hashes in this file.

### Test-run side effects (isolated dir)
On Linux, `LocalApplicationData` resolves to `$XDG_DATA_HOME` (default `~/.local/share`). Each run:
- extracted the DB (328 MB at the time; 226 MB since `07e786d`)
- created 384 cache JSON files
- made 1 live `GET https://www.recreation.gov/api/camps/availability/campground/232445/month?start_date=2026-11-01…`
- created `Camp.History` markers: 118 on the net9.0 run, 116 on the net10.0 run, because live availability changed between runs

The real `~/.local/share/CampertronConfig` listing was compared before and after each run and was unchanged.

## Test warnings: read before running `dotnet test`
1. **Real user directories** [V]. `LoadTests` uses `Load.GetConfig()` → `LocalApplicationData/CampertronConfig` and `CampertronCache`. It creates dirs, writes sample YAML and extracts the DB. Isolate with `XDG_DATA_HOME`. [A] On Windows this is `%LOCALAPPDATA%`, and no isolation recipe has been verified there.
   - **The `XDG_DATA_HOME` directory must exist before the tests start** [V]. On 2026-11-10, `XDG_DATA_HOME=<tmp>/campertron-data` pointing at a missing directory still gave 9/9 passing tests. But the data (313 MB `CampertronConfig` plus `CampertronCache`) was written to `CampertronUnitTests/bin/Release/net10.0/` instead, and the isolated dir was never created. [I] `Environment.GetFolderPath(LocalApplicationData)` returns `""` when the folder doesn't exist, so `Path.Join("", "CampertronConfig")` becomes relative to the test host's working directory. Use `mktemp -d`, or `mkdir -p "$XDG_DATA_HOME"` first, as CI does.
2. **Network** [V]. `LoadTests.RunConsoleSearchTest` performs a real search for every `autoRun: true` YAML in the config dir, or the generated Zion sample if there are none.
3. **Notification markers** [V]. `OutputType.UnitTest` is not `Console`, so `Load.HasNewEntries` writes `Camp.History` markers. Against a real config dir, these suppress future real notifications.
4. **Config writes.** `GetConfig` → `DbExistsCheck` can rewrite `General.yaml` and download the RIDB export if auto-refresh is due [A].
5. **Container env.** If `DOTNET_RUNNING_IN_CONTAINER` is set, `GetConfig` requires `/config` and `/cache`.
6. **Field initializer.** `LoadTests` runs `Load.Preload(Load.GetConfig())` when the fixture is constructed.
7. **Hang risk.** The retry loops have no limit (business-rules §7). Use `timeout` or `--blame-hang-timeout` when running unattended.

## Continuous integration (`.github/workflows/ci.yml`)
Added on branch `ci/add-build-test-workflow` (commit `ec70898`, PR #14) [V].
- **Triggers:** `pull_request` targeting `master`, and `push` to `master`. Runs on `ubuntu-latest` with a 30-minute job timeout. A newer push to the same PR cancels an in-progress PR run. `master` runs are never cancelled.
- **Permissions:** `contents: read` only, which `actions/checkout` needs to fetch the repo with `GITHUB_TOKEN`. All other scopes are `none`. No secrets are used, and there is no AI or Claude automation.
- **Actions,** pinned to full commit SHAs:
  - `actions/checkout` v7.0.1 (`3d3c42e5aac5ba805825da76410c181273ba90b1`), with `persist-credentials: false`.
  - `actions/setup-dotnet` v6.0.0 (`a98b56852c35b8e3190ac28c8c2271da59106c68`), with `dotnet-version: "10.0.x"`.
- **Repository setting:** GitHub Actions was disabled on the repo until the owner enabled it on 2026-11-10. The API now reports `enabled: true` with `allowed_actions: selected`, `github_owned_allowed: true` and `verified_allowed: false`, so only `actions/*` actions can run. Any third-party action added to the workflow will be blocked until that setting changes.

CI commands, in order:
```bash
dotnet --info
dotnet restore Campertron.sln -p:Configuration=Release      # Release, so Debug-only Avalonia.Diagnostics stays out of the asset graph
dotnet build Campertron.sln --configuration Release --no-restore
mkdir -p "$XDG_DATA_HOME"                                    # XDG_DATA_HOME=${{ runner.temp }}/campertron-data; must exist (see Test warnings §1)
dotnet test Campertron.sln --configuration Release --no-build --blame-hang-timeout 10m   # step timeout 15 min
# for each .csproj listed by `dotnet sln Campertron.sln list`:
dotnet list <project> package --vulnerable --include-transitive
dotnet list <project> package --vulnerable --include-transitive --format json   # counted with jq; any finding fails the job
```
- **The vulnerability scan fails the job on any finding.** That includes newly published advisories, so CI can turn red without a code change. `docker-compose.dcproj` is skipped with a message: it isn't a PackageReference project, so `dotnet list package` can't scan it. Each `dotnet list package` call restores implicitly in the default (Debug) configuration, so the scan also covers Debug-only `Avalonia.Diagnostics`.
- **No NuGet cache.** setup-dotnet's `cache` requires `packages.lock.json`, which this repo doesn't have. A manual cache would be about 534 MB compressed (1.2 GiB, 92 packages) to save a roughly 28–36 s cold restore.
- **Not covered by CI:** `docker-compose.dcproj` (skipped by build and scan), GUI shutdown (TD-34), and running anything on Windows or macOS. Docker image builds, the linux-x64 GUI start and packaging for all 8 targets are covered by the release workflow's dry runs (§Release workflow), not by `ci.yml`.
- **Network risks:** the tests call recreation.gov. Network errors retry forever in app code, which the hang and step timeouts catch. A "REQUEST BLOCKED" response makes the app sleep 5 minutes and then call `Environment.Exit(0)`, which would fail the test run.

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
| `pull_request` to `master` touching `release.yml`, `CampertronConsole/Dockerfile`, `.dockerignore`, `**/*.csproj`, `Campertron.sln` or `CampertronLibrary/content/**` | dry run | `0.0.0-dryrun.<run number>` |

- **Every published version becomes a normal GitHub release marked Latest** (`--latest`, never `--prerelease`), so the newest release always shows on the repository page. GitHub does not allow a prerelease to be Latest (REST docs for `make_latest`: "Drafts and prereleases cannot be set as latest"), so pre-release versions are not published as GitHub prereleases.
- A version with a hyphen is still a **SemVer pre-release version** (`prerelease=true` from `verify`) for everything except the GitHub release flag. The image gets only its exact tag, Docker `latest` and `<major>.<minor>` are not updated, and the notes are worded as a pre-release version. A stable version gets the exact tag, `<major>.<minor>` and `latest`.
- The policy lives in `.github/release-policy/release-policy.sh` (`docker_tag_args`, `github_release_flags`, `expected_archives`, `render_notes`, `validate_notes`), which the workflow steps source. It is tested by `.github/release-policy/test-release-policy.sh` (27 cases, prerelease and stable). [V] The directory is deliberately not called `release/`: the Visual Studio `.gitignore` pattern `[Rr]elease/` (line 22) ignores any directory with that name, so files in `.github/release/` would never be committed.
- The version comes only from the tag (`-p:Version`); nothing is committed to `master` for a release.
- Concurrency group `release-<ref>`; only PR runs are cancelled by newer pushes.
- Job names differ from the required check `Restore, build, test, vulnerability scan`, so the workflow cannot satisfy or block that check. Dry-run results are visible on the PR but are not required by the ruleset.

**Jobs** (`needs` in brackets)
1. `verify` "Release verify": version and mode gate (above); runs `.github/release-policy/test-release-policy.sh`; then the same restore, Release build, isolated tests and vulnerability scan as `ci.yml`. **A test failure blocks the release.**
2. `binaries` "Package <app> <rid>" [verify]: a 16-job matrix, app {console, gui} × rid {linux-arm, linux-arm64, linux-x64, osx-arm64, osx-x64, win-arm64, win-x64, win-x86}, with `fail-fast: false`.
   - Publish: `dotnet publish -c Release -r <rid> --self-contained true -p:Version -p:ContinuousIntegrationBuild=true`. Not single-file, not trimmed.
   - Check: the host executable, `CampertronLibrary.dll`, native SQLite and `System.IO.Compression.Native` must be present.
   - Archive: `campertron-<app>-<version>-<rid>` as `.zip` (Windows, Info-ZIP `zip -r -X`) or `.tar.gz` (Linux/macOS, `--sort=name --owner=0 --group=0`). Each archive has one top-level folder of the same name.
   - Upload as a workflow artifact with `archive: false` (the file itself, 7-day retention).
3. `smoke` "Smoke test linux-x64 (native)" [verify, binaries]:
   - The published console must extract the bundled DB in a clean `XDG_DATA_HOME` (`outputTo: UnitTest`) and exit 0, with no `.partial` file left.
   - The published GUI must still be running after 30 s under `xvfb-run` and must have created the DB.
4. `docker` "Docker build (linux/amd64, linux/arm64)" [verify], read-only:
   - `docker buildx` (docker-container driver) builds both architectures.
   - The linux/amd64 image is loaded and run twice:
     - without volumes, it must fail with `Config path does not exist`
     - with `/config` and `/cache` mounted, it must extract the bundled DB inside the container.
   - The image's `org.opencontainers.image.version` label must equal the version.
5. `assemble` "Assemble release (checksums, notes)" [verify, binaries], **read-only, runs on every trigger**: pull request and manual dry runs as well as tag releases.
   - Checks out the repository (for `release-policy.sh`), downloads the 16 archives with `skip-decompress: true` and checks that the directory holds exactly the 16 expected names for this version (`campertron-<app>-<version>-<rid>.<zip|tar.gz>`), each once and nothing else.
   - Generates `SHA256SUMS.txt` (16 lines) and verifies every line with `sha256sum --check --strict`.
   - Renders `RELEASE_NOTES.md`, which the job summary shows too:
     - title `# Campertron <version>`
     - status: **Pre-release version** (normal GitHub release marked Latest; image tagged with the exact version only, Docker `latest` and stable aliases not updated) or **Stable release** (marked Latest; image tagged exact, `<major>.<minor>` and `latest`)
     - the 8-platform table naming both archives per platform and whether the platform is natively smoke-tested (only linux-x64)
     - the statement that the other 7 platforms are not smoke-tested natively
     - unsigned-binary notes, the `docker pull ghcr.io/<owner>/campertronconsole:<version>` command with both platforms, and verification instructions.
   - Validates those notes (`validate_notes`). It checks the version title; that the notes say the release is marked Latest and never claim it is a GitHub prerelease or not Latest; that the status matches the pre-release flag, including the Docker tag sentence; that every archive name appears exactly once; 8 table rows; the linux-x64 smoke text; 7 rows marked not run natively; the limitation statement; the Docker pull command and platforms; and the checksum instructions.
   - Uploads `SHA256SUMS.txt` and `RELEASE_NOTES.md` as the workflow artifact `release-metadata` (7-day retention).
6. `docker-publish` "Publish image to GHCR" [verify, binaries, smoke, docker, assemble], **tag push only**:
   - Logs in to `ghcr.io` with `GITHUB_TOKEN` and rebuilds and pushes `ghcr.io/<owner lowercase>/campertronconsole` for both architectures.
   - Tags (`docker_tag_args`): `<version>`; stable versions also get `<major>.<minor>` and `latest`. Pre-release versions never move `latest` or create stable aliases.
   - Attests the pushed digest (`push-to-registry: true`, `create-storage-record: false`).
7. `release` "Create GitHub release" [all of the above], **tag push only**. It **publishes the assembled output unchanged**:
   - Downloads the 16 archives and the `release-metadata` artifact.
   - Re-checks the archives byte for byte against the assembled `SHA256SUMS.txt` (`--check --strict`), checks that it lists exactly the downloaded files, and checks that the notes title names this version. It does not regenerate checksums or notes.
   - Attests the 16 archives and `SHA256SUMS.txt` (`actions/attest-build-provenance`).
   - `gh release create <tag> <files> --draft --verify-tag --notes-file RELEASE_NOTES.md --latest` for every version (`github_release_flags`). `--generate-notes` is not used, so the published notes are exactly the validated ones.
   - Checks that the draft has 17 assets, then `gh release edit --draft=false --latest`.
   - Checks that the published release is not a prerelease or draft (`gh release view --json isPrerelease,isDraft`) and that `GET /releases/latest` returns this tag. Otherwise the job fails. `gh release view` has no `isLatest` field in gh 2.103.3; only `gh release list` has it.

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
- If anything fails before `docker-publish` (including `assemble`), nothing has been published. Fix the problem through a PR, then tag a new version.
- If `docker-publish` succeeds but `release` fails, the image exists without a release. Delete any draft, then re-run the failed job. A re-run uses the workflow file from the tagged commit.
- A tag is never moved or re-pushed. If the problem needs a code or workflow change, release the next version, for example `v0.1.0-beta.5`.

**Release procedure** [R]
1. Merge to `master` through a PR, and make sure CI on `master` is green.
2. Optional: run `gh workflow run release.yml --ref master` (a dry run) and review its artifacts, including `release-metadata` (the exact `SHA256SUMS.txt` and notes a release would publish).
3. Check that the tag does not exist: `git ls-remote --tags origin v<version>` and `gh release view v<version>`.
4. `git tag -a v<version> -m "Campertron <version>" <master sha>`, then `git push origin v<version>`.
5. Watch the run until every job finishes (`gh run watch <id> --exit-status`), then verify:
   - the release has the 16 archives plus `SHA256SUMS.txt` (17 assets), is a normal (non-prerelease) release, is immutable, and is Latest (`gh api repos/<repo>/releases/latest` returns the tag)
   - `sha256sum -c SHA256SUMS.txt` on the downloaded assets
   - `gh attestation verify <file> --repo ShaunAnthonyHathaway/Campertron --signer-workflow ShaunAnthonyHathaway/Campertron/.github/workflows/release.yml` for every file
   - the image index `ghcr.io/shaunanthonyhathaway/campertronconsole:<version>` lists linux/amd64 and linux/arm64, and `gh attestation verify oci://…:<version>` succeeds
   - for a pre-release version, that `latest` and `<major>.<minor>` were not created in GHCR (for a stable version, that both point to the new digest).
6. GHCR package visibility needs no action. [V] The package was **public from its first push** (`v0.1.0-beta.4`): anonymous tag listing, manifest and blob pulls all succeeded without any visibility change. [I] It most likely inherited public visibility from the repository it is linked to through the `org.opencontainers.image.source` label; the package-settings API was not readable with the maintainer's CLI token (no `read:packages` scope).

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
- **Older releases removed, 2026-11-11** [V]: the release entries `v0.1-beta.3` (ID 186222084) and `v0.1-beta.1` (ID 178602660) were deleted with `DELETE …/releases/{id}`. Their 8 uploaded 2024 zips each went with them. The lightweight tags `v0.1-beta1` and `v0.1-beta3` are unchanged and still point to `e96bc7c…`, and tag ruleset 27004221 protects them. `v0.1.0-beta.4` is now the only release.
- **Defect fixed before the release.** The PR #19 dry run found that `actions/download-artifact` v8 extracted the Windows `.zip` archives on download. It was fixed with `skip-decompress: true` before any tag existed (see the PR #19 entry under §Local validation). Without the fix, the first real release would have failed or published wrong assets.

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
  - It has not been observed on a PR yet. The first PR merged under the ruleset will show whether it blocks anything. Changing it to `false` would be a ruleset settings change (open-questions.md Q19).

### Consequences [V from the rules above]
- No direct pushes to `master`, including docs-only changes. Changes go branch → PR → green `Restore, build, test, vulnerability scan` check on an up-to-date branch → all conversations resolved → merge commit.
- Renaming the CI job's `name:`, or adding a `paths:` filter that skips some PRs, would leave the required check unreported and block merges until the ruleset is updated.
- As an admin, the owner can't bypass the rules but can edit or disable the ruleset.

## Recommended verification after changes
- [R] `dotnet build Campertron.sln`: 0 errors. Compare against the current 87-warning baseline (§F: all compiler warnings, no NuGet warnings).
- [R] GUI changes: run the GUI with `XDG_DATA_HOME="$(mktemp -d)"` so it doesn't touch real data. Expect the main window to open, and expect the known shutdown exception (TD-34) on close.
- [R] CI (`.github/workflows/ci.yml`) must pass on the PR: Release build 0 errors, tests 43/43, no vulnerable packages.
- [R] `XDG_DATA_HOME="$(mktemp -d)" dotnet test …`: 43/43. Apart from `AddressStateTests`, `LegacyAddressLayoutTests`, `RidbDatabaseTests` and `EmbeddedDatabaseTests`, a pass mainly proves nothing crashes, because the tests don't assert filter results (feature-inventory.md §Test coverage).
