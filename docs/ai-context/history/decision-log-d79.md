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
