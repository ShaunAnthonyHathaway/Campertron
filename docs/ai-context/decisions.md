# Architecture decisions

Authority: current. This file is the current index of the decisions D1 to D64 and the recommendations R1 to R8: what each decided, its status today, which later decision changed it and which current document holds the resulting rule. The complete record of every decision (its decision, evidence and stated rationale) is in `history/decision-log.md`, which is historical: read it to investigate a decision, not to find a current rule. A new decision takes the next free ID, D65 (`README.md` §Maintaining these documents).

Status words: "current" is in force; "extended", "narrowed" or "refined" means that a later decision, named under Changed by, changed its scope; "superseded" means it no longer applies; "completed" marks the record of a one-time event, such as a publication or a validation. Current rule names the document or engineering rule that states the rule today; "—" means none. Labels such as §AC name validation records (`history/README.md` §Validation record index).

Roadmap step 9, Phase 3 (2026-11-17) turned this file into the index. The decisions were one table whose rows after D15 GitHub showed as plain text; the history file gives each its own subsection.

## Decision index
| ID | Subject | Date | Status | Changed by | Current rule | Record |
|---|---|---|---|---|---|---|
| D1 | Shared logic in one class library for the console and GUI hosts | — | current | — | `CLAUDE.md` §Purpose | [D1](history/decision-log.md#d1) |
| D2 | Target .NET 10 for all projects | 2026-11-10 | superseded | D31 (.NET 11) | D31 | [D2](history/decision-log.md#d2) |
| D3 | RIDB reference data in SQLite through EF Core | — | current | D25 (the import writes rows with prepared commands; EF Core keeps the model and queries) | ER-04 | [D3](history/decision-log.md#d3) |
| D4 | A pre-populated database shipped as an embedded resource, no migrations | — | current | D16 (Brotli since 2026-11-10) | — | [D4](history/decision-log.md#d4) |
| D5 | Per-campground static data cached as JSON files | — | current | D27 (the cache directory) | ER-05 | [D5](history/decision-log.md#d5) |
| D6 | User configuration in YAML files; several search files | — | current | D37, D44 (when search files are written) | ER-14, ER-15 | [D6](history/decision-log.md#d6) |
| D7 | Each month's availability fetched once per cycle and shared by the searches | — | current | D35 and TD-40 (each search derives its own data; the cached month stays read-only) | ER-11 | [D7](history/decision-log.md#d7) |
| D8 | Duplicate notifications suppressed with marker directories | — (refined 2026-11-14) | current (refined: the Email loop's history only) | D36 (markers only after delivery) | ER-03; business-rules §5 | [D8](history/decision-log.md#d8) |
| D9 | The container is headless and email-only | — | current | — | — | [D9](history/decision-log.md#d9) |
| D10 | Output as `ConsoleConfigValue` render instructions | — | current | D29, D34 (rendered from the values' typed metadata) | ER-12, ER-13 | [D10](history/decision-log.md#d10) |
| D11 | The GUI uses Avalonia and ReactiveUI | — | current | D23 (Avalonia 12.1.6, `ReactiveUI.Avalonia.Reactive`) | — | [D11](history/decision-log.md#d11) |
| D12 | The GUI's role is campground lookup, not search | — | superseded | the roadmap: D43, D44, D46 | `CLAUDE.md` §Purpose | [D12](history/decision-log.md#d12) |
| D13 | Tests run against real environment state, with `OutputType.UnitTest` in production code | — | current (see full record; TD-01) | D45 (no test reaches the network) | `CLAUDE.md` §Conventions observed | [D13](history/decision-log.md#d13) |
| D14 | CI on GitHub Actions: build, test and a failing vulnerability scan, least privilege | 2026-11-10 | current | D40 (the Windows settings-files job) | `build-and-test.md` §Continuous integration | [D14](history/decision-log.md#d14) |
| D15 | `master` protected by ruleset 26990088 | 2026-11-10 | current | — | `release-process.md` §Branch protection; ER-32 | [D15](history/decision-log.md#d15) |
| D16 | Six RIDB tables, the database shipped Brotli-compressed | 2026-11-10 | current | D51 (Campertron's index, not in the embedded file) | `maintainer-procedures.md` §Refreshing the embedded database | [D16](history/decision-log.md#d16) |
| D17 | Releases only from `release.yml` for a SemVer tag on `master` | 2026-11-10 | current, extended | D19, D30, D32, D33, D48 | `release-process.md`; ER-26 | [D17](history/decision-log.md#d17) |
| D18 | Release tags and releases are protected and immutable | 2026-11-11 | current | — | `release-process.md` §Release protections; ER-26 | [D18](history/decision-log.md#d18) |
| D19 | Every published version is a normal GitHub release marked Latest | 2026-11-11 | current | — | `release-process.md`; ER-26 | [D19](history/decision-log.md#d19) |
| D20 | Old-binary compatibility handled by documentation | 2026-11-11 | current | — | ER-31; business-rules §10 | [D20](history/decision-log.md#d20) |
| D21 | Every release output verified on the required .NET major | 2026-11-11 | current, extended | D31 (.NET 11 and the SDK, package, PDB and toolchain guards) | ER-27; `release-process.md` | [D21](history/decision-log.md#d21) |
| D22 | Installed databases repaired by their own properties, not by version | 2026-11-11 | current | — | business-rules §8 | [D22](history/decision-log.md#d22) |
| D23 | The GUI on Avalonia 12.1.6 with `ReactiveUI.Avalonia.Reactive` 12.1.6 | 2026-11-11 | current | — | — | [D23](history/decision-log.md#d23) |
| D24 | Windows GUI packages exclude the SkiaSharp and HarfBuzzSharp native PDBs | 2026-11-11 | current (until the upstream packages are fixed; open question #24) | — | ER-30 | [D24](history/decision-log.md#d24) |
| D25 | The RIDB import writes rows with prepared commands, not `SaveChanges` | 2026-11-11 | current | — | ER-04 | [D25](history/decision-log.md#d25) |
| D26 | The failure-safe RIDB refresh: a staging database installed with `BackupDatabase` | 2026-11-11 | current, extended | D27 (the cache path), D40 (the `General.yaml` timestamp writer), D61 (the host-neutral refresh service, the console's adapter, the download folder) | ER-05; business-rules §8 | [D26](history/decision-log.md#d26) |
| D27 | `CachePath` is the only runtime cache directory | 2026-11-11 | current | — | ER-05 | [D27](history/decision-log.md#d27) |
| D28 | The GUI's polished, theme-aware layout (beta.8) | 2026-11-12 | current, extended | D33 (layout), D42 (identity), D50 (tabs), D55 (label) | ER-07 | [D28](history/decision-log.md#d28) |
| D29 | The HTML results page from typed metadata; one page per run, one email per search | 2026-11-12 | current | — | ER-12; business-rules §6 | [D29](history/decision-log.md#d29) |
| D30 | Curated release notes are tracked fragments published in the draft | 2026-11-13 | current | — | ER-25; `release-process.md`; `.github/release-notes/README.md` | [D30](history/decision-log.md#d30) |
| D31 | .NET 11 with a deterministic SDK 11 policy and EF Core 11 | 2026-11-14 | current (supersedes D2) | — | ER-27; `build-and-test.md` §Required environment | [D31](history/decision-log.md#d31) |
| D32 | The published container image is the validated OCI image | 2026-11-14 | current, refined | D48 (the arm64 image run natively); D59 (a stable version's floating tags move forward; TD-57 resolved) | ER-29; `release-process.md` | [D32](history/decision-log.md#d32) |
| D33 | R8: the GUI's startup states, layout and native smoke tests | 2026-11-14 | current, extended | D48 (more targets), D49 (the startup's dispatcher), D60 (a host-neutral library summary for the GUI, ER-06), D61 (the host-neutral refresh service and the wait for a refresh after the close), D62 (the restart handoff claimed in `Program.Main`; the restored window) | ER-06, ER-28 | [D33](history/decision-log.md#d33) |
| D34 | Console output polish | 2026-11-14 | current | — | ER-13; business-rules §12 | [D34](history/decision-log.md#d34) |
| D35 | Search correctness (P2): dates from numbers, rolling dates, per-search filtering | 2026-11-14 | current | — | ER-11; business-rules §2 to §4 | [D35](history/decision-log.md#d35) |
| D36 | Email notification history only after delivery (P3) | 2026-11-14 | current | — | ER-03; business-rules §5, §6 | [D36](history/decision-log.md#d36) |
| D37 | Settings safety and failure exits (P4) | 2026-11-14 | current, narrowed | D44 (the GUI writes a search file on an explicit action); D64 (email mode with no search to check stops with an error instead of checking nothing) | ER-15; business-rules §9 | [D37](history/decision-log.md#d37) |
| D38 | `v0.1.0-preview.1` published and verified | 2026-11-15 | completed (publication record) | — | `history/releases.md`; §AC | [D38](history/decision-log.md#d38) |
| D39 | CM1: the comment-preserving YAML document editor | 2026-11-15 | current, extended, refined | D40, D43, D44 (its callers: D39 had none); D41 (it refuses as `Malformed` the text YamlDotNet's scanner throws on) | ER-16; business-rules §13 | [D39](history/decision-log.md#d39) |
| D40 | CM2: the settings writers keep comments and write atomically | 2026-11-15 | current, extended | D43, D44 (the GUI's saves) | ER-18; business-rules §14 | [D40](history/decision-log.md#d40) |
| D41 | CM3: configuration validation and the bounded settings loader | 2026-11-15 | current | — | ER-17; business-rules §15 | [D41](history/decision-log.md#d41) |
| D42 | CM4: the GUI identifies a campground by its FacilityID | 2026-11-15 | current, extended | D44 (the campground picker) | ER-08; business-rules §16 | [D42](history/decision-log.md#d42) |
| D43 | CM5: the GUI edits `General.yaml` and `Email.yaml` | 2026-11-15 | current | D50 (now Settings > Campertron) | ER-10; business-rules §17 | [D43](history/decision-log.md#d43) |
| D44 | CM6: the GUI creates and edits search files | 2026-11-15 | current (narrows D37), extended | D63 (the words for autoRun: Enabled (autoRun), Not enabled) | ER-14; business-rules §18 | [D44](history/decision-log.md#d44) |
| D45 | GS1: the host-neutral search service | 2026-11-15 | current | D46 (the GUI's part) | ER-20; business-rules §19 | [D45](history/decision-log.md#d45) |
| D46 | GS2: the GUI's Results tab | 2026-11-15 | current, extended | D50 (the tab order), D61 (a run and a refresh of the campground data exclude each other), D63 (the words for autoRun; the run's posts to the dispatcher captured at Start, TD-56) | ER-19; business-rules §20 | [D46](history/decision-log.md#d46) |
| D47 | The reconciliation after GS2, and S1: the console menu's loop | 2026-11-15 | current (S1); its dispositions are older than D57's, which apply where they differ | D57 | ER-21; `technical-debt.md` | [D47](history/decision-log.md#d47) |
| D48 | S2: native smoke tests for linux-arm64 and the arm64 image; win-x86 on 64-bit Windows | 2026-11-15 | current | — | ER-28, ER-29; `release-process.md` §Platforms and what a release run shows | [D48](history/decision-log.md#d48) |
| D49 | The preparation of `v0.1.0-preview.2` | 2026-11-16 | completed (its startup fix is a current rule) | — | ER-06; `release-process.md` §Versions, candidates and publication | [D49](history/decision-log.md#d49) |
| D50 | The GUI's navigation and a saved theme preference | 2026-11-16 | current, extended | D55 (the lookup tab's label), D60 (Settings > System's current configuration), D61 (Settings > System's campground data), D62 (a restarted process opens at Settings > System) | ER-09; business-rules §21 | [D50](history/decision-log.md#d50) |
| D51 | Filter selectors, stage 1: OR semantics, `filterMatching`, the filter choices and the index | 2026-11-16 | current | — | ER-22; business-rules §3, §8, §15, §22 | [D51](history/decision-log.md#d51) |
| D52 | Filter selectors, stage 2: the shared selector, equipment and campsite types | 2026-11-16 | current, extended | D53, D54 (the other lists) | ER-23; business-rules §23 | [D52](history/decision-log.md#d52) |
| D53 | Filter selectors, stage 3: the campsite selectors | 2026-11-16 | current | — | ER-23; business-rules §23 | [D53](history/decision-log.md#d53) |
| D54 | Filter selectors, stage 4: the attribute selectors | 2026-11-16 | current | — | ER-23; business-rules §23 | [D54](history/decision-log.md#d54) |
| D55 | The lookup's top-level tab is labeled Campgrounds | 2026-11-17 | current | — | ER-09; business-rules §21 | [D55](history/decision-log.md#d55) |
| D56 | `v0.1.0-preview.2` published and verified | 2026-11-17 | completed (publication record) | — | `history/releases.md`; §AX | [D56](history/decision-log.md#d56) |
| D57 | Final stable-readiness validation for `v0.1.0`: no blocker | 2026-11-17 | completed (its dispositions are current in `technical-debt.md` and `open-questions.md`) | — | `technical-debt.md`; `open-questions.md` | [D57](history/decision-log.md#d57) |
| D58 | `v0.1.0`, the first stable version, published and verified | 2026-11-17 | completed (publication record) | — | `current-state.md` §Latest release; `history/releases.md` | [D58](history/decision-log.md#d58) |
| D59 | TD-57: a stable version's floating container tags move only forward; an exact version tag never moves | 2026-11-18 | current | — | ER-29; `release-process.md` | [D59](history/decision-log.md#d59) |
| D60 | V011-2: Settings > System > Current configuration, from one host-neutral summary that the console's View Config writes too | 2026-11-18 | current | — | ER-06, ER-09, ER-13; business-rules §24 | [D60](history/decision-log.md#d60) |
| D61 | V011-1: Settings > System > Campground data refreshes the database through a host-neutral refresh service that the console's refresh uses too | 2026-11-18 | current, refined (resolves TD-58) | D62 (an automatic restart instead of a manual one after a successful refresh) | ER-05, ER-06, ER-19; business-rules §8, §25 | [D61](history/decision-log.md#d61) |
| D62 | After a successful refresh of the campground data the GUI restarts itself through a one-time handoff, in the old window's place, at Settings > System | 2026-11-18 | current (refines D61 before Phase B was pushed) | — | ER-06, ER-09; business-rules §25, §26 | [D62](history/decision-log.md#d62) |
| D63 | V011-3: the GUI says "Enabled (autoRun)" and "Not enabled" and explains each where it is shown; TD-56's last sites post to the dispatcher captured on the UI thread | 2026-11-18 | current (resolves TD-56) | — | ER-14, ER-19; business-rules-settings §15; business-rules-gui §18 to §20 | [D63](history/decision-log.md#d63) |
| D64 | Email mode starts its checks only when its start produced a search to check; otherwise one error and exit code 1 (answers open question #40) | 2026-11-21 | current (narrows D37 for email mode) | — | ER-02, ER-15, ER-17; business-rules §9, §12; business-rules-settings §15 | [D64](history/decision-log.md#d64) |

## Recommendations (not decisions; nothing below has been adopted)
| ID | Recommendation | Current disposition | Record |
|---|---|---|---|
| R1 | Isolate the test environment (`Load.GetConfig` takes the paths) | partly done: the network part by D45; the rest (tests that start through `Load.GetConfig`) after `v0.1.0` (D47; TD-01) | below |
| R2 | Use `CachePath` consistently | done (D27) | `history/decision-log.md` §Recommendations |
| R3 | Remove MailKit and MimeKit; migrate off `SmtpClient`? | the removal done (`d20e1ad`); migrating off `SmtpClient` after `v0.1.0` (audit 2026-11-14), not started | below |
| R4 | Seams for HTTP and the clock | done for the search by D45 and for the RIDB download by D61; the search's clock is still `DateTime.Now` | below |
| R5 | Separate the search pipeline from the console | done (D45, D46) | `history/decision-log.md` §Recommendations |
| R6 | Evaluate `ReactiveUI.Avalonia` | done (D23) | `history/decision-log.md` §Recommendations |
| R7 | Curated release notes in the draft | done (D30) | `history/decision-log.md` §Recommendations |
| R8 | Native release smoke testing beyond linux-x64 | done (D33, D48); linux-arm has no hosted runner and stays not run (open question #23) | `history/decision-log.md` §Recommendations |

The records of R1, R3 and R4, which are partly open:

- R1. Isolate the test environment: let `Load.GetConfig` accept the config and cache paths, so tests can use a temp dir. This would address TD-01. (Audit disposition 2026-11-14: tracked as TD-01, preview.2. Reconciliation 2026-11-15, D47: the network part is done by D45; the rest, tests that start through `Load.GetConfig`, moves to after `v0.1.0`: it affects developers only, and CI and the test commands isolate their data.)
- R3. ~~Remove the unused MailKit and MimeKit references~~ **Done in `d20e1ad`** (they caused Dependabot alerts). Still open: whether to migrate email off `System.Net.Mail.SmtpClient`, which Microsoft marks obsolete for new development [I]. That would mean re-adding MailKit at a patched version (≥ 4.16.0). (Audit disposition 2026-11-14: after `v0.1.0`.)
- R4. Introduce a seam (an interface or delegate) for HTTP and the clock so the filter and date rules can be unit-tested (technical-debt.md §Missing tests). (Audit disposition 2026-11-14: the HTTP seam with TD-01 in preview.2. The clock part exists for the search dates since D35: an internal `CampertronConfig.GenerateSearchData(DateTime)`.) **Done for the search by D45 (GS1, 2026-11-15):** the interactive search's transport is a delegate (`SearchTransport`, `SearchHostOptions.Transport`) and the console's download an internal replaceable step (`AvailabilityApi.ConsoleDownload`); the required tests run the search, the filters and the consecutive nights through them without the network. The RIDB download (`Web.Download`) has no seam; the clock is still `DateTime.Now` inside the search (the dates take a time since D35).
  **Done for the RIDB download by D61 (V011-1, 2026-11-18):** the refresh service takes the download's `HttpMessageHandler` (`RidbRefreshOptions.Handler`), its folders, timeouts and clock, and the required tests refresh from an in-memory RIDB server.

## Roadmap (maintainer-stated plans, first recorded 2026-11-12, last replaced 2026-11-14 after R8, extended 2026-11-17 after `v0.1.0` by steps 9 and 10; not decisions)
Moved to `history/decision-log.md` §Roadmap by roadmap step 9, Phase 3 (historical record). The current roadmap position, the work authorized now and the `v0.1.1` requirements are in `current-state.md`; the earlier status narrative is in `history/roadmap-and-status.md`.
