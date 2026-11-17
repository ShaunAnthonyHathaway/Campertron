# Validation records §X to §AC: to `v0.1.0-preview.1`

> **Historical record — not current authority.** Moved from `docs/ai-context/build-and-test.md` (at `6309671`) by roadmap step 9, Phase 2, on 2026-11-17, unchanged except that lines over 1,000 characters outside tables were broken at sentence boundaries (`../README.md` §Phase 2 migration map has the source lines and the proofs). Commands and statements here, including comparisons or copies of the real data folders, record what was done then and are never current procedure (`CLAUDE.md` §Data safety). For current instructions read `../../build-and-test.md`, `../../release-process.md` and `../../maintainer-procedures.md`; for the current state, `../../current-state.md`.

The records §X to §AC in their original order (2026-11-14 to 2026-11-15). A label in these records (§A to §AZ, §Release history, §Release workflow and the other named sections) keeps the meaning it had in `build-and-test.md`; `../README.md` §Label index says where it is now.

## Validation records §X to §AC

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

**Root causes confirmed before the change** [V] (`master` `4419ce8`): `Yaml.CampertronConfigGetConfigs` called `CampertronConfigGenerateSampleConfig` whenever no file had `autoRun: true`, and that writes `ZionConfig.yaml` with a truncating `StreamWriter` (TD-13); `GeneralConfigGetConfig` and `CampertronConfigGetConfigs` passed a null document on (an empty file) to code that dereferences it (TD-37); `CampertronConfig.GenerateSearchDates` wrote the equal-date error, called `Console.ReadKey()` and `Environment.Exit(0)` inside the loader's file-context `try` (TD-45); `AvailabilityApi.GetWebApiData` called `Thread.Sleep(300000)` and `Environment.Exit(0)` after a blocked answer (TD-44); `Load.GetConfig` joined `Environment.GetFolderPath(LocalApplicationData)` (no `SpecialFolderOption`, so `""` for a missing folder) with the folder names (TD-47);
`Compression.Unzip` deleted `LocalApplicationData/Extract` recursively and extracted into it, and `PopulateDbFromRIDBFiles.Populate` deleted it again (TD-48).

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
