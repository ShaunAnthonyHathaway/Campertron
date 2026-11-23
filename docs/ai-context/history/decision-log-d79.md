# Decision log from D79

> **Historical record — not current authority.** The complete records of the decisions from D79 on. `decision-log.md` holds D1 to D78 and the recommendations R2 and R5 to R8; it reached its size recommendation (251,416 bytes of about 250 KB) with D78, so this file was started on 2026-11-23 UTC by the maintainer's instruction of 2026-11-22 (PR #120). The current index, with each decision's status and the document that now holds its rule, is `../decisions.md`; the current roadmap position is `../current-state.md`.

Each decision has the labels of `decision-log.md`: Decision, Evidence and Stated rationale. A record is written once; a later change is a dated note beside it.

## Decisions from D79

### D79
**Decision:** **`v0.3.0`'s scope is locked (the maintainer's decisions of 2026-11-22 on the read-only architecture investigation, `validation/bv_v0.2.0.md` §CB; D76's scope lock applies from now on).**
(1) The desktop GUI is kept and maintained long-term, alongside the web UI (D84).
(2) In scope: the self-hosted web UI (D80 to D83, D85) for both user types of `../current-state.md` §Roadmap boundaries: full web-app users, and notification-only users with a guided path; a test-notification button in both UIs and a random ntfy topic generator in the guided path and both UIs' `Ntfy.yaml` editors (D86); the container's campground-data refresh download moved to `/cache` (D85, open question #27); the unified app icon, its design pending the maintainer's review of §CB's previews (no icon work until the maintainer chose it).
(3) Platforms: web mode is available on every platform (it is part of every console archive) and documented container-first; `--web` smoke steps in `release.yml` need their own approval later.
(4) The pull requests, in this order (PR 1 is this record): 2 the monitoring service and the monitoring lock (D83); 3 the shared form layer (the desktop GUI's form logic extracted into a layer without Avalonia, the GUI's behavior unchanged); 4 the web host and its security (D80, D81, D82, the logging exception; the Dockerfile's base image and port, D85); 5 the settings pages, with the test-notification button and the random topic generator in both UIs and their library functions (D86); 6 the searches pages; 7 the results page;
8 the monitoring page; 9 the system and campgrounds pages and the refresh, with the container's refresh download moved to `/cache` (D85, #27) before the web UI offers the refresh; 10 the guided path, which uses the topic generator; 11 the icon, once the maintainer chose its design; 12 the `--web` smoke steps of the release workflow (their own approval); 13 the user documentation (the README) and the documentation's consolidation; 14, after `v0.3.0` is published, the site update (D87): the web UI, the guided setup, Docker's `--web`, the screenshots and the new icon and favicon, merged with the maintainer's approval. Each pull request also updates the AI-context documents for what it changes.
(5) After PR 13: the local test of the merged `master` (a local Linux x64 build with isolated synthetic settings), the maintainer's test, the fixes, then one candidate; the curated notes and every release step with its own approval (`CLAUDE.md` §Approval boundaries). PR 14 follows the publication.
(6) Later, with no version assigned: a built-in HTTPS server, several users, an installable web app, a guided path in the desktop GUI. `v0.4.0` to `v0.6.0` stay as D76 placed them.
(7) This record authorizes no implementation: PR 2 starts with the maintainer's instruction.

**Evidence:** the maintainer's instructions of 2026-11-22; `validation/bv_v0.2.0.md` §CB.

**Stated rationale:** fix the scope and the order before any implementation; keep the desktop GUI for its users; build the shared parts with the most risk (the monitoring service, the form layer) before the pages that use them; describe on the site only what is released.

### D80
**Decision:** **The web UI is ASP.NET Core Razor Pages in a new Razor class library, `CampertronWeb`, hosted by `CampertronConsole` (2026-11-22; option 1 of §CB).**
(1) Started by `CampertronConsole --web`, with `--listen` (the address) and `--port`; without `--web` every mode is unchanged.
(2) Server-rendered pages and plain HTML forms that work without JavaScript; at most a small first-party script, served by Campertron itself and using no library, for progress and re-checks without a reload. No new package: only the ASP.NET Core shared framework (`Microsoft.AspNetCore.App`, a framework reference). Not Blazor, which needs the package `Microsoft.AspNetCore.App.Internal.Assets`, JavaScript and a WebSocket per tab (§CB).
(3) The library code the desktop GUI uses for every file, validation, search and refresh (`ConfigEditSession`, `ConfigValidation`, `SecretFileCheck`, `CampsiteSearch`, `RidbRefreshService`, `CurrentConfiguration`, `Read`), and the GUI's form logic through the shared layer of D79's PR 3, so both UIs say the same words.
(4) When nothing is set up, the first screen is the guided path for notification-only users, with a clear way to skip to the full editor; the guided path writes the same files through the same code and validation as the editors.
(5) Test-only tools: axe-core (MPL-2.0) for accessibility checks and, if needed, an HTML parser, in test projects only, never shipped.

**Evidence:** §CB: a self-contained linux-x64 console archive measured 66.3 MB (`.tar.gz`) and 78.7 MB with the framework, `check_publish_runtime` passing unchanged; the runtime packs for all eight release RIDs; the offline restores (Razor Pages needs no package, interactive Blazor one); the inventory of what the library offers and what is inside the GUI.

**Stated rationale:** no third-party code, no required JavaScript, one archive and one image, and full control of accessibility and the words.

### D81
**Decision:** **The web UI's access protection and security defaults (2026-11-22; the maintainer's choice of reachability differs from §CB's recommendation of loopback only).**
(1) A password is required by default. On the first start without one, Campertron prints a one-time setup code on its console (in a container: `docker logs`), and the setup page needs that code to set the password. The password is kept as a PBKDF2-SHA256 hash in an owner-only file in the settings folder, never in a YAML settings file.
(2) Reachable from the network by default: on the desktop and in the container web mode listens on all interfaces; the Docker documentation publishes `-p 8790:8790` and names `-p 127.0.0.1:8790:8790` as the local-only option.
(3) Both can be turned off: the password (no sign-in) and the network listening (local only).
(4) A save that leaves the password off while network listening is on shows a clear warning at that save and asks for confirmation. After it nothing warns again and nothing blocks: no lasting banner, no refusal to start.
(5) Kept from §CB's section D: the session cookie (HttpOnly, SameSite=Strict, Secure over HTTPS, an idle timeout; a password change signs out every session); antiforgery tokens on every POST, and changes by POST only; Origin and Host checks; security headers with a strict Content-Security-Policy (`script-src 'self'`, `frame-ancestors 'none'`), `nosniff`, `Referrer-Policy: no-referrer`, `no-store` for signed-in pages, no `Server` header and no CORS;
ASP.NET Core's Data Protection keys in the settings folder, owner-only (its default, `~/.aspnet/DataProtection-Keys`, is outside Campertron's folders and lost in a container, §CB); forwarded headers trusted only from configured proxy addresses; HTTPS from a reverse proxy; rate limits for the sign-in, the site and the request size; one manual search or refresh at a time per process; the write-only SMTP password (D82); no request that takes a path (a search file is chosen from the folder's list or named through `SearchFileName.Check`).
(6) How the Host check works while network listening is the default (which names and addresses it accepts) is proposed with D79's PR 4.
(7) An exception to `CLAUDE.md` §Validation and logging: ASP.NET Core's log output is off; Campertron writes only its own fixed lines through `ConsoleUi`: the listening address, the setup code and the number of failed sign-ins with the client address. Never a request body, query string, cookie, header, form value or file content.

**Evidence:** §CB (section D; the probe that wrote a Data Protection key to `~/.aspnet`).

**Stated rationale:** Campertron runs on home servers and is used from other devices; a password from the first start protects it there, and after one clear warning the user decides.

### D82
**Decision:** **Secrets in the web UI (2026-11-22).**
(1) The SMTP password (`smtpPassword`) is write-only: the page says whether one is set and offers Replace and Remove; it is never sent to the browser (no Show, masked in the preview).
(2) The ntfy topic is shown in the web UI's `Ntfy.yaml` editor, as in the desktop GUI's (D69); everywhere else it stays hidden (ER-34).
(3) The password and token files: their path is edited; the file is judged from its metadata only (D75, D77) and never opened.
(4) An approved exception to ER-33 and ER-34: the web UI may show whether `CAMPERTRON_EMAIL_PASSWORD` and `CAMPERTRON_NTFY_TOKEN` are set in its own process, yes or no only, never the value (its monitoring is the one that would use them). The rules' text changes with the pull request that implements it.

**Evidence:** §CB (section D).

**Stated rationale:** the password never leaves the server; the topic is needed to subscribe to the notifications; whether a variable is set explains which source the monitoring uses.

### D83
**Decision:** **The unattended monitoring in web mode, and a monitoring lock (2026-11-22, as §CB proposed).**
(1) A host-neutral monitoring service in the library, in the pattern of GS1's search (D45) and the refresh service (D61), runs the monitoring's checks; the console's email and ntfy mode become its adapter, with their output, exit codes, one-minute wait, start checks and `Camp.History` rules unchanged (ER-02, ER-03, ER-13).
(2) In web mode the monitoring starts by itself when `outputTo` is Email or Ntfy and the start's checks pass (D64, D66, D67, D71, D73); otherwise the page says why, in the start's words. Start, Stop and Restart from the browser; Stop holds for the running process only (it saves nothing, and the next start of web mode starts the monitoring again); a check that is sending finishes its deliveries and records before it stops. The search files are read at each start of the monitoring, as today, and the page says when they changed since.
(3) Web mode keeps running with no search file or no search to check, waiting to be configured, and creates no example search (as email mode since D65).
(4) A failed send follows the maintainer's rule (D76): a visible error, the monitoring stops, the failed delivery's openings are not recorded in `Camp.History`; the process and the web UI stay up, and nothing restarts the monitoring by itself.
(5) A monitoring lock: one monitoring at a time per settings folder, by a lock file in it, like the refresh's (D26); a second monitoring, of web mode or of the console's email or ntfy mode, refuses to start with a clear message. ER-02 and ER-03 change with the pull request that implements it (D79's PR 2).
(6) Manual searches from the browser keep the desktop Results tab's rules (ER-19, ER-20); a refresh of the campground data pauses the monitoring.

**Evidence:** §CB (sections A and C: the loop in `Load.RunConsoleSearch` blocks, keeps static state, has no stop or status and ends the process on a failure).

**Stated rationale:** the notification-only user sets up and starts the monitoring in the browser; one monitoring per folder prevents duplicate notifications; the send-failure rule stays the maintainer's.

### D84
**Decision:** **The full GUI rule is extended to the web UI (2026-11-22; D76's permanent rule, `CLAUDE.md` §Product rules).**
The rule reads: "Full UI rule: every Campertron feature must be fully controllable from the desktop GUI and from the web UI, with the same files, validation and words, except for listed exceptions." Desktop-only: the theme's storage, the automatic restart, window placement, opening folders. Web-only: the sign-in and the password, the monitoring's control.
A feature pull request covers both UIs and both test suites, or records an approved exception; the console-to-GUI map gets a web column with the web UI. Until the web UI exists the rule applies to the desktop GUI as before.

**Evidence:** the maintainer's instruction of 2026-11-22; §CB (section E, the parity table).

**Stated rationale:** two UIs with the same files must not drift apart; the exceptions are what only one host can do.

### D85
**Decision:** **Web mode in the container (2026-11-22).**
(1) Opt-in: the image's default stays the headless monitoring (D9, D67); web mode is started with `--web` (for example Compose `command: ["--web"]`).
(2) Port 8790 and `EXPOSE 8790`; the image's base becomes `mcr.microsoft.com/dotnet/aspnet:11.0` (about 27 MB more, §CB).
(3) No `HEALTHCHECK` in the image (the default headless mode serves nothing): a `--healthcheck` mode, documented for Compose, checks the web UI on the loopback address.
(4) The web UI's password file and keys live in `/config`; the volumes and secrets stay as they are.
(5) The container's campground-data refresh downloads into `/cache` instead of the container's own storage (`/app`, writable for the image's user, §CB), part of open question #27; where the extraction goes is settled by the pull request that implements it (D79's PR 9).

**Evidence:** §CB (section G; `/app` owned by the user `app` in a cached image).

**Stated rationale:** existing containers keep their behavior; a refresh's 1.4 GB peak belongs in a mounted folder, not in the container's own storage.

### D86
**Decision:** **A test-notification button and a random ntfy topic generator, in both UIs (2026-11-22; in `v0.3.0`'s scope, replacing §CB's "later" for them).**
(1) A test notification: on the user's action, one test message to the saved email or ntfy settings, from the `Email.yaml` and `Ntfy.yaml` editors of the desktop GUI and the web UI and from the guided path, its result said in words. It searches nothing, writes nothing and never touches `Camp.History` or the monitoring; a failed test send is a visible error only.
(2) To send, it needs the password or the token from its configured source at that moment (the file, the variable or the plaintext), never shown, logged or kept. This is an exception to ER-33's and ER-34's "the GUI never opens the file or reads the variable" and to ER-34's "never send a test message", following from this decision; the pull request that implements it (D79's PR 5) writes the exact rule, and with it the desktop GUI sends a message for the first time (`CLAUDE.md` §Purpose changes then).
(3) The random ntfy topic generator: a cryptographically random, hard-to-guess topic offered in the guided path and in both UIs' `Ntfy.yaml` editors; it fills the field and saves nothing by itself.

**Evidence:** the maintainer's instruction of 2026-11-22.

**Stated rationale:** a notification-only user should know before leaving that the notifications arrive; a guessable topic is the main risk of ntfy.sh.

### D87
**Decision:** **A permanent rule, Website sync (the maintainer's approval of 2026-11-22; `CLAUDE.md` §Product rules).** The maintainer's words:
"Website sync: campertron.com (site/) must stay consistent with the README and docs. Any PR that changes features, setup, downloads, Docker instructions, screenshots or user-facing wording covered by the site must include or schedule the matching site/ update. Site screenshot copies are refreshed whenever README screenshots change. The site stays version-free and unpromoted until the public announcement.
Because merging site/ changes publishes via pages.yml, site updates describing unreleased features are merged only after that release is published, and each site merge needs user approval (normally bundled with the release approval)."
Recorded in `CLAUDE.md` §Product rules, `../maintainer-procedures.md` §The landing page and `../release-process.md` (the release procedure's step 8). For `v0.3.0`, D79's PR 14 is the site update after the publication.

**Evidence:** the maintainer's instruction of 2026-11-22, during PR #120.

**Stated rationale:** the site must not promise what users can't download yet, nor fall behind what they can.

### D88
**Decision:** **The test notification reads the password or the token on the user's action, in both UIs (the maintainer's decision of 2026-11-22, on the start of D79's PR 2; it settles what D86's point (2) left to its pull request).**
The test-notification button of D86 may read the SMTP password and ntfy's token in the desktop GUI and in the web UI, only at the moment the user presses it, from the source the monitoring would use, by the same precedence: for the SMTP password the file `smtpPasswordFile` names, then `CAMPERTRON_EMAIL_PASSWORD`, then the plaintext `smtpPassword` (D66); for ntfy's token the file `tokenFile` names, then `CAMPERTRON_NTFY_TOKEN` (D67).
What it reads is used for that one send only and is never displayed, logged, cached or written.
A narrow exception to ER-33 and ER-34 ("the GUI never opens the file or reads the variable", and ER-34's "never send a test message"). The exact rule and its tests come with the pull request that implements the button (D79's PR 5); until then ER-33 and ER-34 apply as written, and nothing of it is implemented.

**Evidence:** the maintainer's instruction of 2026-11-22.

**Stated rationale:** a test send that signs in with another password than the monitoring would prove nothing; reading the source only on the user's action, for one send, keeps the secret out of every screen, log and file.

### D89
**Decision:** **The unified app icon is variant D (the maintainer's choice of 2026-11-22 from §CB's previews; D79's icon item is no longer pending a choice, but nothing of the icon is implemented).**
Variant D, the simplified redraw (a dark teal rounded square, glowing eyes, a two-tone orange tent), is used at all sizes for the desktop GUI, the console's executable, the web UI's favicon and, after `v0.3.0` is published, the site's favicon and apple-touch icon (D87).
Before D79's PR 11 a clean vector (SVG) redraw is produced for the maintainer's approval, with these refinements: the robot's head readable (a lighter head shade or an edge), so that it is not lost against the teal at small sizes; an optional small campfire at the tent's door at 48 px and up only, omitted at 16 to 32 px; a subtle outline or edge, so that the square stands out on dark taskbars and browser tabs; the bright eyes and the two-tone orange tent kept as they are.
Preview sheets at 16, 20, 24, 32, 48, 64, 128, 256 and 512 px on light and dark backgrounds come with it. No icon implementation until the maintainer approved the redraw.

**Evidence:** the maintainer's instruction of 2026-11-22, during D79's PR 2; `validation/bv_v0.2.0.md` §CB (the previews A to D).

**Stated rationale:** one drawing that stays readable from 16 px to 512 px, on light and dark surfaces, for every place Campertron shows an icon.

### D90
**Decision:** **The monitoring service and the monitoring lock as implemented (D79's PR 2, 2026-11-23 UTC, on the maintainer's instruction of 2026-11-22; D83's points (1) and (5) for the console; web mode's use of the service, points (2) to (4) and (6), comes with D79's later pull requests).**
(1) **The service.** `MonitoringService` (`CampertronLibrary/function/RecDotOrg/monitoring/`), an instance with no static state, in the pattern of D45's search service and D61's refresh service. `Start(settings)` runs the start's checks in the console's former order (the `Email.yaml` and `Ntfy.yaml` checks, the SMTP password and ntfy's token from their sources, the search files read once and D64's gate, then the monitoring lock, then each campground's cache) and returns a typed result (`MonitoringStartResult`: started, or the problem and its words; a start that threw carries its exception); then the checks run on a thread of the service's own, one minute apart (`CheckInterval`).
`Stop` asks the run to end and waits until it has: the wait between two checks ends at once; a check that is searching ends at its next checkpoint and nothing of it is sent or recorded (its openings come again); a check that is sending finishes every notification and its records in `Camp.History` first. `Restart` is `Stop` and `Start` (the search files read again, the check number from 1). A host's cancellation token stops it as `Stop` does.
`GetStatus` gives a snapshot: the state (not started, starting, checking, sending, waiting, stopped, failed), whether a stop was asked for, when it started, how many searches it checks, the last check's number and time, the next check's time, the last error with its words, the last start's result, and whether the search files changed since the start (by their names, sizes and times of writing, never their contents).
(2) **No static state.** `Load.CheckNumber` and `Load.CheckWait` are gone: the check number, the wait, the clock and the output are the service's (`MonitoringOptions`). The service writes nothing to a console and ends no process: its events (`MonitoringEvent`: starting, a status line, a warning, the searches loaded, a check's report, an email or an ntfy notification sent, the deliveries finished) go to the host. A check's searches reach the service through `RunningData.Monitoring` (`MonitoringRun`: the token, the warning and status lines, what a blocked request does).
Every start counts its checks from 1 (before, the number was the process's, which is the same for the console: one start for each process).
(3) **The console is its adapter** (`MonitoringConsole`): it writes each event through `ConsoleUi` with the calls the loop made, in their order, rethrows a start's or a check's exception unchanged for `Program.Main` to report (exit code 1), and gives the service the console's own answer to a blocked request (the error, the five-minute wait and exit code 1, TD-44). Its output, exit codes, start checks (D64 to D67, D71), one-minute wait, send-failure behavior (D76) and `Camp.History` rules (ER-03) are unchanged. Without a host's own handling a blocked request ends the monitoring as failed and the process goes on (for web mode).
(4) **The monitoring lock.** The file `Campertron.monitoring.lock` in the settings folder, kept open without sharing while a monitoring runs, as the refresh lock is (D26): one monitoring at a time for a settings folder, whether a console in email or ntfy mode, a container, or (later) web mode. The lock is the open handle: the operating system releases it when the monitoring ends in any way (a stop, a failure, a kill, a power loss), so a lock is never stale and a lock file left behind locks nothing; the file is never deleted and stays empty. It is created for its owner only (0600 on Unix; the settings folder's access rights on Windows); an existing file keeps its rights.
It is taken last of the start's checks, so a start that is refused for another reason still creates nothing (D64's promise). It is separate from the refresh lock and the repair lock.
(5) **The message and the exit code.** A start the lock refuses says one error on standard error after the start line, naming the mode ("email mode" or "ntfy mode"): "… can't start: another Campertron is already monitoring this settings folder.", then "Only one monitoring runs for a settings folder, so that every opening is notified once. Stop the other one first (another CampertronConsole in email or ntfy mode, or a container that uses the same settings folder), or give this one its own settings folder." and "Nothing was searched or sent.".
A settings folder that can't hold the lock's file says "… can't start: the monitoring's lock file can't be created or opened in the settings folder.", what the file is for and to let the user that runs Campertron write to the folder. Both end with **exit code 1**, the code of every handled failure and of every other start that is refused (D64, D66, D67): a supervisor sees a failure, never a process that seems to monitor.
(6) **What changed for a user:** a second monitoring of one settings folder is refused where it used to send every notification twice; the file `Campertron.monitoring.lock` appears in the settings folder at the first monitoring; a settings folder the monitoring can't write to is refused at the start (before, the first opening's email was sent and the marker's write then failed, at every restart again). The details of an unexpected failure (the lines after "Details, for a problem report:") name the service's functions.
(7) **Limits:** on Linux and macOS the lock is advisory (`flock`, which every .NET process takes for an open without sharing), so it holds between Campertron's processes on one host, also across containers that use the same folder; a file system without such locks (some network shares) gives no protection. Not run natively on Windows or macOS (open question #49). It does not make the campground cache safe for two processes (TD-54 stays open: a monitoring and a manual search can still build one campground's cache at once).

**Evidence:** `validation/bv_v0.2.0.md` §CC (the tests, the negative controls and the local build's check: a console in email mode against a loopback SMTP server, and the lock refusing a second instance).

**Stated rationale:** web mode needs a monitoring it can start, stop and show; the console must not change for it; one monitoring for a folder prevents duplicate notifications; and a start that is refused must stay a visible failure.
