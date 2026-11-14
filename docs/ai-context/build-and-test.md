# Build and test

Legend: **[V]** verified (command run or file read) · **[A]** assumption, not executed · **[R]** recommendation

## Required environment
- [V] .NET SDK 10.x with the **.NET 10 runtime and the .NET 10 targeting pack**. All four projects target `net10.0`. There is no `global.json`, so the host uses the newest installed SDK.
- [V] On Arch/CachyOS, the SDK, runtime and targeting pack are separate packages. The audit machine needed `dotnet-sdk` 10.0.14.sdk114-1, `dotnet-runtime` 10.0.14 and `dotnet-targeting-pack-10.0` 10.0.14.sdk114-1. When one was missing, the failure looked like this:
  - **runtime missing:** every `dotnet` command fails with exit code 150, "You must install or update .NET to run this application … 'Microsoft.NETCore.App', version '10.0.14'"
  - **targeting pack missing:** `restore` fails with `NETSDK1226: Prune Package data not found .NETCoreApp 10.0 Microsoft.NETCore.App`. Do **not** work around this with `AllowMissingPrunePackageData`.
- [V] The GUI needs a desktop session (Avalonia `UsePlatformDetect`, `WinExe`). Isolated smoke tests passed on 2026-11-10 (§E, Avalonia 11) and 2026-11-11 (§K, Avalonia 12). The headless GUI tests (`CampertronGUITests`) need no display.
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
dotnet test CampertronGUITests/CampertronGUITests.csproj --no-build   # headless GUI tests; isolate themselves (Linux only, see Test warnings §8)

dotnet build Campertron.sln -c Release          # [A] not run
dotnet run --project CampertronConsole/CampertronConsole.csproj   # [A] interactive, not run
dotnet run --project CampertronGUI/CampertronGUI.csproj           # [A] needs a display, not run
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

**General.yaml timestamp write (TD-37 fix, commits `cfebb9c`, `563d722`)** [V]. The kill point "during the timestamp write" above left an empty `General.yaml` and a failing next start in all three passes. The timestamp is now written by `Yaml.GeneralConfigReplaceYaml` → `ConfigFileReplace.Replace` (business-rules §8 step 11): complete document to `General.yaml.<32 hex>.tmp` in `ConfigPath`, `StreamWriter.Flush` + `FileStream.Flush(flushToDisk: true)`, close, read back and validate (identical text, deserializes, re-serializes identically), `File.Move(…, overwrite: true)`; the destination is never truncated or deleted first; the temporary file is removed on failure, and stale ones at the next refresh start.
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

### Refreshing the README screenshots (maintainer procedure)
Recorded 2026-11-11 for branch `docs/rewrite-readme` (application built from `fa15649`). All captures use isolated data and no personal paths, names or searches.

| File | Shows | Source |
|---|---|---|
| `docs/campertron.png` | hero image (a 1024×1024 JPEG despite the `.png` name) | unchanged; keep unless the maintainer approves a replacement |
| `docs/ss1.png` | console main menu (Windows Terminal) | kept from 2024; the menu labels are unchanged in `Menu.cs` |
| `docs/ss2.png` | console search results (Mather Campground, December 2024) | kept from 2024; the console output has not changed since (beta.9 tagged the values without changing their text: `ConsoleOutputGoldenTests`, §Q) |
| `docs/ss5.png` | console View Config | recaptured 2026-11-11, 1115×489 |
| `docs/ss6.png` | GUI, By Park tab, Zion National Park / WATCHMAN CAMPGROUND, **Dark** theme | recaptured 2026-11-12 after the `v0.1.0-beta.8` release from `master` `84e5649` (the released source), 1400×1043, replacing the Light capture of the same scene, which was a bright block in the README under GitHub's dark theme; before: 2026-11-12 Light (beta.8 layout, §P), 2026-11-11 Light (Avalonia 12), 1400×1011 |
| `docs/ss7.png` | HTML results page (`outputTo: HtmlFile`), desktop (1280 CSS px), **Dark**: header ("2 searches"), navigation, the Mather section with its empty state, the start of the Zion section (counts, check-in/out, November 2026, Friday 11/13/2026, the first row of site cards) | recaptured 2026-11-12 after the beta.9 review (no generation time) from a real isolated run of the two shipped examples, 1400×900 (shown 800 px wide) |
| `docs/ss8.png` | HTML results page at phone width (390 CSS px), **Dark**: the Zion section with counts, check-in/out, November 2026, Friday 11/13/2026 and the cards for D016HC, D040 and F017 | same run, 780×1530 (shown 320 px wide) |

`docs/ss4.png` (a second menu screenshot) was removed as a duplicate of `ss1.png`. The README references images by relative path (`docs/…`), so a branch shows its own images.

**ss6 (GUI)** [V on Linux x64, KDE Plasma Wayland with XWayland, ImageMagick 7]
1. `dotnet build CampertronGUI` (Release), and an isolated data directory `X` with `CampertronConfig/General.yaml` (`outputTo: Console`, `autoRefresh: false`, a recent `lastRidbDataRefresh`). The GUI extracts the bundled database on start.
2. A scratch launcher (not in the repository) references `CampertronGUI.csproj` and starts the real app with the production chain `AppBuilder.Configure<Campertron.App>().UsePlatformDetect().LogToTrace().UseReactiveUI(_ => { })`. After start it sets the main window to 1100×820, selects the **By Park** tab and the ComboBox items `Zion National Park` and `WATCHMAN CAMPGROUND` in the window's own controls, and waits. Since beta.8 the app follows the system theme. The README screenshot is the **Dark** theme: run on a desktop whose color scheme prefers dark (`org.freedesktop.appearance color-scheme` = 1 through the settings portal) and let the app follow it; the launcher logs `ActualThemeVariant=Dark Requested=Default` and sets no theme. (The earlier Light captures set `Application.Current.RequestedThemeVariant = ThemeVariant.Light` on a Dark desktop.) The launcher also logs the client size, scaling and focused element; the selections are made programmatically, so no element has keyboard focus and no focus ring, dropdown or tooltip appears.
3. Run it with `AVALONIA_GLOBAL_SCALE_FACTOR=1.5 XDG_DATA_HOME=X` and capture the X11 window with `import -window Campertron <file>`. The effective scaling was 2.17 (1.5 × the desktop's 1.45), giving 2390×1781.
4. `magick <file> -filter Lanczos -resize 1400x -strip -define png:compression-level=9 docs/ss6.png` (1400×1043). The beta.8 details scroll, so there is no empty strip to trim; the 2026-11-11 capture used `-crop 2390x1726+0+0 +repage` first.
- `RenderTargetBitmap` of the window was tried first and **does not work** for this: borders and text were drawn with different offsets. Capture the real window instead.

**ss7 and ss8 (HTML results page)** [V on Linux x64, Chrome 155 headless, ImageMagick 7]
1. In an isolated `X=$(mktemp -d)`: `X/CampertronConfig/General.yaml` with `outputTo: HtmlFile`, `autoRefresh: false` and a recent `lastRidbDataRefresh`, plus the two shipped searches `examples/0.1/ZionConfig.yaml` and `SouthRimConfig.yaml` (public campgrounds, no personal values).
2. Produce the page with the real code: either choose **Search** in `XDG_DATA_HOME=X dotnet run --project CampertronConsole -c Release` and copy the `{guid}.html` from `X/CampertronCache`, or use a scratch harness (not in the repository) that runs `Load.GetConfig`, `Preload`, `GetAvailabilitiesByCampground` per search in `Parallel.ForEach` and `Load.OutputResults`, with an `xdg-open` stand-in on `PATH` that copies the file it is asked to open (no browser starts). The page has no generation time, path or local data.
3. Start Chrome headless with a scratch `--user-data-dir`, `--hide-scrollbars` and `--remote-debugging-port`, and drive it over the DevTools protocol without any key or mouse input (no focus ring): `Emulation.setEmulatedMedia` with `prefers-color-scheme: dark`, then `Emulation.setDeviceMetricsOverride` with `deviceScaleFactor: 2` and `Page.captureScreenshot` of the viewport, for ss7 at 1280×900 (`mobile: false`, top of the page) and for ss8 at 390×844 (`mobile: true`, scrolled to 12 px above the Zion section, `#search-2`).
4. Crop at the bottom of a row of cards so no card is cut, then: `magick <ss7 capture> -crop 2560x1646+0+0 +repage -filter Lanczos -resize 1400x -strip -define png:compression-level=9 docs/ss7.png` and `magick <ss8 capture> -crop 780x1530+0+0 +repage -strip -define png:compression-level=9 docs/ss8.png` (the crop heights fit the 2026-11-12 data; choose them from the capture).
- The results are live recreation.gov data, so a recapture shows other sites and dates (on 2026-11-12 the Mather example had no results in this run; see §Q). The command-line `--screenshot` of headless Chrome cannot scroll to a section of a large page (it captured blank areas), which is why the DevTools protocol is used.

**ss5 (console View Config)** [V same environment]
1. `dotnet publish CampertronConsole -c Release -r linux-x64 --self-contained true -o <dir>`.
2. For a neutral path, run inside an unprivileged user and mount namespace with a stand-in home: `unshare -rm <script>`, where the script mounts a `tmpfs` on `/home`, creates `/home/camper/.local/share/CampertronConfig/General.yaml` (as above, `lastRidbDataRefresh` a few days earlier), sets `HOME=/home/camper`, unsets `XDG_DATA_HOME` and `WAYLAND_DISPLAY`, and starts `alacritty` with 92×17 cells, font size 13, padding 10/8 and Windows Terminal's "Campbell" colors (to match `ss1`/`ss2`).
3. In the terminal: `stty raw -echo; { (sleep 5; printf '\033[B'; sleep 0.7; printf '\033[B'; sleep 0.7; printf '\r'; sleep 40) & cat; } | script -q -c <dir>/CampertronConsole /dev/null` (two Down arrows and Enter select View Config). Raw mode and `cat` are needed because the console queries the cursor position; a plain pipe hangs it.
4. Find the window (`xprop -root _NET_CLIENT_LIST`, then the one whose `WM_CLASS` is Alacritty), `import -window <id> <file>`, then `magick <file> -filter Lanczos -resize 1115x -strip docs/ss5.png`.

### Refreshing the embedded database (maintainer procedure)
Verified 2026-11-10 for `07e786d`. All steps run in a scratch directory, never in the user's data dirs.
1. `dotnet build Campertron.sln -c Release`
2. Create an empty `X=$(mktemp -d)` and write `$X/CampertronConfig/General.yaml` with `outputTo: UnitTest`, `autoRefresh: false`, `refreshRidbDataDayInterval: 30` and any `lastRidbDataRefresh`. Then run `XDG_DATA_HOME=$X CampertronConsole/bin/Release/net10.0/CampertronConsole`. This extracts the bundled DB and exits without a menu.
3. Set `autoRefresh: true` and a `lastRidbDataRefresh` older than 30 days, then run the console again. It downloads the RIDB export (about 573 MB), imports the 6 used files and validates the result (about 15 s after the download since §M; the download itself took about 50 s in one sample). **The output must not contain "failed validation".**
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
8. **`CampertronGUITests` isolates itself** [V]. Its `[SetUpFixture]` (`GuiTestEnvironment`) sets `XDG_DATA_HOME` to a new temporary directory inside the test process, fails if `LocalApplicationData` does not resolve there, writes a `General.yaml` with `autoRefresh: false` and creates a tiny deterministic DB from the EF model (corrected layout, no omitted tables, asserted smaller than 5 MB). `Load.GetConfig()` in `MainWindow`'s constructor therefore neither extracts, repairs nor refreshes, and the tests use no network and no real data. The directory is deleted afterwards. On Windows and macOS `XDG_DATA_HOME` does not redirect `LocalApplicationData`, so the fixture ignores all GUI tests there.

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
source .github/release-policy/release-policy.sh              # curated release notes (decision D30, §S); read-only, publishes nothing
check_curated_notes_dir .github/release-notes "ghcr.io/<owner lowercase>/campertronconsole" "$GITHUB_REPOSITORY"   # preview + SHA-256 to the job summary; any invalid fragment fails the job
dotnet --info
dotnet restore Campertron.sln -p:Configuration=Release      # Release, matching the Release build (no project has configuration-conditional packages since §K)
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
- **Not covered by CI:** `docker-compose.dcproj` (skipped by build and scan), the GUI on a real platform backend (rendering, D-Bus shutdown, TD-34), and running anything on Windows or macOS (the TD-35 branch ran the published Windows GUI once in a temporary workflow, §L). The headless GUI tests (`CampertronGUITests`, §K) run in `dotnet test Campertron.sln`. Docker image builds, the linux-x64 GUI start and packaging for all 8 targets are covered by the release workflow's dry runs (§Release workflow), not by `ci.yml`.
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
| `pull_request` to `master` touching `release.yml`, `CampertronConsole/Dockerfile`, `.dockerignore`, `**/*.csproj`, `Campertron.sln`, `CampertronLibrary/content/**` or `.github/release-policy/**` | dry run | `0.0.0-dryrun.<run number>` |

- **Every published version becomes a normal GitHub release marked Latest** (`--latest`, never `--prerelease`), so the newest release always shows on the repository page. GitHub does not allow a prerelease to be Latest (REST docs for `make_latest`: "Drafts and prereleases cannot be set as latest"), so pre-release versions are not published as GitHub prereleases.
- A version with a hyphen is still a **SemVer pre-release version** (`prerelease=true` from `verify`) for everything except the GitHub release flag. The image gets only its exact tag, Docker `latest` and `<major>.<minor>` are not updated, and the notes are worded as a pre-release version. A stable version gets the exact tag, `<major>.<minor>` and `latest`.
- The policy lives in `.github/release-policy/release-policy.sh` (`docker_tag_args`, `github_release_flags`, `expected_archives`, `render_notes`, `validate_notes`; the curated-notes functions of §S: `validate_curated_fragment`, `compose_notes`, `assemble_release_notes`, `check_curated_notes_dir`, `release_body_from_json`, `compare_release_body`, `check_release_view`; and the .NET 10 guards `check_project_frameworks`, `check_publish_runtime`, `check_framework_dependent_runtimeconfig`, `check_framework_report`), which the workflow steps source. It is tested by `.github/release-policy/test-release-policy.sh` (177 cases: prerelease and stable notes, pass/fail fixtures for every .NET 10 guard (§I), and 110 curated-notes cases (§S)).
- **Curated release notes (decision D30, §S):** each release's reviewed notes are a tracked fragment `.github/release-notes/v<version>.md` (format: `.github/release-notes/README.md`), merged to `master` through its own PR before the tag. A publishing tag run composes it with the generated sections and publishes that body in the draft; it never falls back to template-only notes. `.github/release-policy/ReleaseCheck/` is a small net10.0 console project, not in `Campertron.sln` and not shipped, that the smoke job uses to check the GUI state data (§I). [V] The directory is deliberately not called `release/`: the Visual Studio `.gitignore` pattern `[Rr]elease/` (line 22) ignores any directory with that name, so files in `.github/release/` would never be committed.
- The version comes only from the tag (`-p:Version`). The only file committed to `master` for a release is its curated notes fragment, through a PR before the tag.
- Concurrency group `release-<ref>`; only PR runs are cancelled by newer pushes.
- Job names differ from the required check `Restore, build, test, vulnerability scan`, so the workflow cannot satisfy or block that check. Dry-run results are visible on the PR but are not required by the ruleset.

**Jobs** (`needs` in brackets)
1. `verify` "Release verify": version and mode gate (above); runs `.github/release-policy/test-release-policy.sh`; **checks the curated release notes** (`check_curated_notes_dir`: every tracked fragment validates and composes for its own version; each composed body and its SHA-256 goes to the job summary) on every trigger, and **for a publishing tag requires its exact fragment** `.github/release-notes/v<version>.md` in the tagged commit (`assemble_release_notes true`, then `validate_notes`), so a missing or invalid fragment fails before any build; a dry run reports that its synthetic version needs none; **checks that every project targets .NET 10** (`check_project_frameworks`: every `.csproj` has exactly `<TargetFramework>net10.0</TargetFramework>`; no `TargetFrameworks`, `RuntimeFrameworkVersion` or `RollForward` in any `.csproj`, `Directory.Build.props/.targets` or `.pubxml`; a `global.json`, if added, pins a 10.x SDK; every .NET base image in the Dockerfile is `:10.0`); then the same restore, Release build, isolated tests and vulnerability scan as `ci.yml`. **A test failure blocks the release.**
2. `binaries` "Package <app> <rid>" [verify]: a 16-job matrix, app {console, gui} × rid {linux-arm, linux-arm64, linux-x64, osx-arm64, osx-x64, win-arm64, win-x64, win-x86}, with `fail-fast: false`.
   - Publish: `dotnet publish -c Release -r <rid> --self-contained true -p:Version -p:ContinuousIntegrationBuild=true`. Not single-file, not trimmed.
   - Check: the host executable, `CampertronLibrary.dll`, native SQLite and `System.IO.Compression.Native` must be present. **Windows GUI packages (TD-35 guard, §L):** `libSkiaSharp.dll`, `libHarfBuzzSharp.dll` and `av_libglesv2.dll` must be present; `libSkiaSharp.pdb` and `libHarfBuzzSharp.pdb` must be absent, and `CampertronGUI.deps.json` must not list them as native assets. Campertron's own PDBs are allowed.
   - **.NET 10 check** (`check_publish_runtime`, on the exact directory that becomes the archive): `<app>.runtimeconfig.json` has `tfm` `net10.0`, no shared-framework reference (self-contained) and only `Microsoft.NETCore.App` 10.x in `includedFrameworks`; `<app>.deps.json` targets `.NETCoreApp,Version=v10.0/<rid>` with exactly one runtime pack, `Microsoft.NETCore.App.Runtime.<rid>/10.x`; `System.Private.CoreLib.dll` contains only .NET 10 informational versions (the version `RuntimeInformation.FrameworkDescription` reports); and no JSON file in the output mentions `net9.0`, `v9.` or a 9.x runtime pack. Any failure fails that matrix job, so no archive is uploaded and `assemble` cannot find 16.
   - Archive: `campertron-<app>-<version>-<rid>` as `.zip` (Windows, Info-ZIP `zip -r -X`) or `.tar.gz` (Linux/macOS, `--sort=name --owner=0 --group=0`). Each archive has one top-level folder of the same name.
   - Upload as a workflow artifact with `archive: false` (the file itself, 7-day retention).
3. `smoke` "Smoke test linux-x64 (native)" [verify, binaries]:
   - The published console must extract the bundled DB in a clean `XDG_DATA_HOME` (`outputTo: UnitTest`) and exit 0, with no `.partial` file left.
   - The published GUI must still be running after 30 s under `xvfb-run` and must have created the DB.
   - **Runtime reported by the published apps:** `CampertronConsole --version` and `CampertronGUI --version` (no display) must print exactly one `Framework => .NET 10.x.y (<arch>)` line whose version **equals the `Microsoft.NETCore.App` version bundled in that archive** (`check_framework_report`), which shows the process ran on the bundled runtime; the first line must be `<app> <VERSION>[+<commit>]`.
   - **GUI state data:** builds `ReleaseCheck` and runs it twice, on the published GUI and on the published console, each against the `RecreationDotOrg.db` that app extracted. It loads the **published** `CampertronLibrary.dll` and its dependencies from the archive and runs that library's own code: `RidbDatabase.Validate` passes; `LegacyAddressLayout.DetectLayout` is Corrected with only known schema objects; `Read.UniqueStates` is non-empty, all `^[A-Z]{2}$`, no digits, no `01005`, contains AZ, MA and UT; `UniqueCities("MA")` contains BARRE; `UniqueParksByState("MA")` is non-empty; `UniqueCities("01005")` and `UniqueParksByState("01005")` are empty; `UniqueParks("UT", "SPRINGDALE")` contains WATCHMAN CAMPGROUND.
4. `docker` "Docker build (linux/amd64, linux/arm64)" [verify], read-only:
   - `docker buildx` (docker-container driver) builds both architectures.
   - The linux/amd64 image is loaded and run twice:
     - without volumes, it must fail with `Config path does not exist`
     - with `/config` and `/cache` mounted, it must extract the bundled DB inside the container.
   - The image's `org.opencontainers.image.version` label must equal the version.
   - **Container runs on .NET 10:** the image's `DOTNET_VERSION` is 10.x; `/app/CampertronConsole.runtimeconfig.json` (framework-dependent) has `tfm` `net10.0` on `Microsoft.NETCore.App` 10.x (`check_framework_dependent_runtimeconfig`); and `docker run <image> --version` reports `.NET <DOTNET_VERSION>`. (`docker-publish` rebuilds the image from the same Dockerfile for the push and does not repeat these checks; the base images are pinned to `:10.0` by the verify job.)
5. `assemble` "Assemble release (checksums, notes)" [verify, binaries], **read-only, runs on every trigger**: pull request and manual dry runs as well as tag releases.
   - Checks out the repository (for `release-policy.sh`), downloads the 16 archives with `skip-decompress: true` and checks that the directory holds exactly the 16 expected names for this version (`campertron-<app>-<version>-<rid>.<zip|tar.gz>`), each once and nothing else.
   - Generates `SHA256SUMS.txt` (16 lines) and verifies every line with `sha256sum --check --strict`.
   - Builds `RELEASE_NOTES.md` with `assemble_release_notes`. **Publishing tag:** the generated title and status (lines 1–4), the fragment `.github/release-notes/v<version>.md` byte for byte, one blank line, then the generated sections; the fragment is required. **Dry run:** the generated template alone for the synthetic `0.0.0-dryrun.<n>`, and the summary states that template-only notes are permitted only because the run cannot publish. The job summary shows the notes with their SHA-256. The generated parts are:
     - title `# Campertron <version>`
     - status: **Pre-release version** (normal GitHub release marked Latest; image tagged with the exact version only, Docker `latest` and stable aliases not updated) or **Stable release** (marked Latest; image tagged exact, `<major>.<minor>` and `latest`)
     - the 8-platform table naming both archives per platform and whether the platform is natively smoke-tested (only linux-x64)
     - the statement that the other 7 platforms are not smoke-tested natively
     - unsigned-binary notes, the `docker pull ghcr.io/<owner>/campertronconsole:<version>` command with both platforms, and verification instructions.
   - Validates those notes (`validate_notes`). It checks the version title; that the notes say the release is marked Latest and never claim it is a GitHub prerelease or not Latest; that the status matches the pre-release flag, including the Docker tag sentence; that every archive name appears exactly once; 8 table rows; the linux-x64 smoke text; 7 rows marked not run natively; the limitation statement; the Docker pull command and platforms; and the checksum instructions. For a publishing tag, line 5 must also be `## What's new in <version>`.
   - Uploads `SHA256SUMS.txt` and `RELEASE_NOTES.md` as the workflow artifact `release-metadata` (7-day retention).
6. `docker-publish` "Publish image to GHCR" [verify, binaries, smoke, docker, assemble], **tag push only**:
   - Logs in to `ghcr.io` with `GITHUB_TOKEN` and rebuilds and pushes `ghcr.io/<owner lowercase>/campertronconsole` for both architectures.
   - Tags (`docker_tag_args`): `<version>`; stable versions also get `<major>.<minor>` and `latest`. Pre-release versions never move `latest` or create stable aliases.
   - Attests the pushed digest (`push-to-registry: true`, `create-storage-record: false`).
7. `release` "Create GitHub release" [all of the above], **tag push only**. It **publishes the assembled output unchanged**:
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
3. Optional: run `gh workflow run release.yml --ref master` (a dry run) and review its artifacts and summary. Its `release-metadata` has the exact `SHA256SUMS.txt` contents a release would publish but template notes for the synthetic version; the verify job's summary shows the composed body and SHA-256 of every real fragment. Check that the tag does not exist: `git ls-remote --tags origin v<version>` and `gh release view v<version>`.
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
- [R] `dotnet build Campertron.sln`: 0 errors. Compare against the current 77-warning baseline (§R; all compiler warnings, no NuGet, AVLN or RXUIBIND warnings).
- [R] RIDB import changes (`PopulateDbFromRIDBFiles`, `RidbTableInserter`, the mapped models): run `PopulateTests`, then a full-export refresh in an isolated directory, and compare the checkpointed DB with the previous build's output on the same export and culture (§M: schema, counts, `EXCEPT` both ways with `rowid`, per-column `typeof`, SHA-256).
- [R] RIDB refresh changes (`RefreshRidbRecreationData`, `RidbStagedRefresh`, `Clear`, the auto-refresh in `Load.DbExistsCheck`): run `AtomicRefreshTests`, the kill harness and the cross-process scenarios, and the full-export comparison against the in-place core (§N).
- [R] GUI changes: run `CampertronGUITests`, then run the GUI with `XDG_DATA_HOME="$(mktemp -d)"` so it doesn't touch real data. Expect the main window to open. Check Light and Dark, 720×540 and a wide window, and a keyboard pass (Tab must wrap to the tab header, TD-39). Bind every database-backed view-model property exactly once (`Selections_RunOneQueryEach`) and take colors only from theme resources (D28). Since Avalonia 12 the TD-34 shutdown exception has not been observed (§K, §P); report it if it returns.
- [R] Output changes (the producers in `Cache`/`AvailabilityApi`, `ProcessConsoleConfig`, `ResultsHtml`, `Load.OutputResults`): run `ConsoleOutputGoldenTests`, `ResultsHtmlTests` and `HtmlResultsOutputTests`, then generate a page from a real search in an isolated `XDG_DATA_HOME` (with a recording `xdg-open` on `PATH`) and check it in Chrome and Firefox at 320 px and a desktop width, Light and Dark (§Q).
- [R] Search changes (`AvailabilityApi`, `Load.RunConsoleSearch`, anything reading `RunData.SiteData`): run `AvailableDatesTests`, `SharedMonthSearchTests` and `ConsoleOutputGoldenTests`, then compare parallel and one-at-a-time results of several searches of one campground and month in an isolated directory (§R). The cached month data must stay read-only (TD-40).
- [R] CI (`.github/workflows/ci.yml`) must pass on the PR: Release build 0 errors, tests 257/257 (223 + 34 GUI, §R), no vulnerable packages.
- [R] `XDG_DATA_HOME="$(mktemp -d)" dotnet test Campertron.sln …`: 257/257. Apart from `AddressStateTests`, `PopulateTests`, `AtomicRefreshTests`, `ConfigFileReplaceTests`, `LegacyAddressLayoutTests`, `RidbDatabaseTests` and `EmbeddedDatabaseTests`, a pass mainly proves nothing crashes, because the tests don't assert filter results (feature-inventory.md §Test coverage).
