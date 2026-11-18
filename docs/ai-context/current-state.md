# Current state

Authority: **the only current authority** for the repository and release state, the changes since the latest release, the roadmap position, the work authorized now, the deferred `v0.1.1` requirements, release blockers, open risks and roadmap boundaries. Where another document disagrees on one of these, this file applies and the other document is stale.

Last verified: 2026-11-18 UTC. Update this file in the same pull request as any change it describes.

## Repository

- Since the `v0.1.0` source commit `a4f4fb5` the programs changed only by roadmap step 10, Phase A (V011-2, D60): the library's shared current-configuration summary (the console's View Config output byte for byte unchanged), the GUI's Settings > System > Current configuration, the smoke test's `system-tab` line and their tests. `global.json`, the project files, the packages, the Dockerfile and the embedded database are unchanged.
- Besides documentation (next section): roadmap step 9, Phase 5 added the documentation-policy checker and its own tests in `.github/docs-policy/`, which `ci.yml`'s required job runs since PR #91. Roadmap step 10, Phase T (PR #93, D59) changed the container publication of the release policy (`release-policy.sh`, its tests and the push step of `release.yml`) and corrected two stale comments in `ci.yml` and `release.yml`; Phase A extended `check_gui_smoke_report` and its tests for the new `system-tab` line.
- The .NET validation baseline changed with Phase A: 1,272 tests (1,033 unit, 20 of them Windows-only, and 239 GUI) instead of 1,234, with the same 0 errors and 56 warnings; the release-policy suite has 471 cases since Phase A (465 since D59). The numbers are in `build-and-test.md` §Current validation baseline, and the per-area checks in its §Recommended verification after changes.

## Latest release

- **`v0.1.0`, Latest and immutable:** the first stable version, published 2026-11-17 14:37:25 UTC (decision D58; §AZ in `history/validation/ay-az_v0.1.0.md`).
- Tag `v0.1.0`: annotated, object `892afd60b8a7d521ef4645175b0047f387b8b778`, on commit `a4f4fb50d918cf7398a5c378fa6f310bfa3eb579` (tree `7bc837ee7909977cd0c29cb340333cfad3359f5f`), the merge of PR #81.
- Release run #58 (44102810321): 29/29 jobs, attempt 1, no rerun. Before it, on the same commit: the candidate, Release dry run #56 (44085647906), which the maintainer tested personally and approved, and the pre-tag dry run #57 (44098300331).
- GitHub release 448756659: immutable, Latest, not a draft, `prerelease: false`; 17 assets (16 archives and `SHA256SUMS.txt`), all attested; body 12,019 bytes, SHA-256 `3d0732a85108179f942559073a3ebc32164221b497fb55f670225dbe2ef31e38`.
- GHCR `ghcr.io/shaunanthonyhathaway/campertronconsole`: `0.1.0`, `0.1` and `latest`, all `sha256:38039d81e110467e2c77f2603dd8cc30a13df360e7c9d3f7870b80795a95af1a`.
- The earlier releases, `v0.1.0-beta.4` to `v0.1.0-preview.2`, are immutable and no longer Latest (`history/README.md` §Release index).
- Published release notes, release bodies, assets and tags are immutable historical records. Never rewrite them, including `.github/release-notes/v0.1.0-preview.2.md` and `v0.1.0.md`, unless a separately approved recovery action explicitly requires it.

## Changes on `master` since `v0.1.0`

Documentation, the documentation-policy checks since Phase 5 and their CI steps since PR #91, since Phase T the container publication of the release policy, and since Phase A the application (V011-2):
- PR #82 (`bcad6dd`, merged as `e9ad33a`) recorded `v0.1.0`: decision D58, `build-and-test.md` §AZ, roadmap steps 9 and 10, TD-57 live.
- PR #83 (`3bec498`, merged as `3adcc8b`; CI 44142070534 and, after the merge, 44142679451) corrected README accuracy only: the GUI named in the introduction, the wording about shared files, and how the console loads search files in each mode. It changed no architecture, behavior, command or feature status, so it has no decision entry or validation record.
- Roadmap step 9, Phase 1 (PR #84) added the mandatory core (`CLAUDE.md` rewritten, `docs/ai-context/README.md`, this file), `engineering-rules.md` and `history/` (`history/README.md` §Phase 1 migration map).
- PR #85 (`b389d29`, merged as `6309671`; CI 44163859073 and, after the merge, 44164472500) changed `README.md` only. It removed the obsolete comparisons of the stable release with beta, preview and other pre-release builds, and kept the x86-64-v2 processor requirement as direct guidance for the stable release. It changed no application behavior; the application code is still that of the `v0.1.0` source commit. It has no decision record.
- Roadmap step 9, Phase 2 (PR #86, `9371213`, merged as `3f22d83`; CI 44175508297 and, after the merge, 44176256615) split `build-and-test.md`: the release policy and procedure into `release-process.md`, the hands-on procedures into `maintainer-procedures.md`, the release history into `history/releases.md` and the validation records §A to §AZ into four files in `history/validation/`. `build-and-test.md` keeps the build, test and CI material, gains §Current validation baseline (moved from §Repository above) and points to every moved section (`history/README.md` §Phase 2 migration map).
- Roadmap step 9, Phase 3 (PR #87, `0c8be79`, merged as `6ad651d`; CI 44182483933 and, after the merge, 44183093528) turned `decisions.md`, `technical-debt.md` and `open-questions.md` into compact current indexes (each decision's status and current rule; one effective disposition per debt item and one status per question, with the open items' complete records) and moved the complete historical records to `history/decision-log.md`, `history/technical-debt.md` and `history/open-questions.md` (`history/README.md` §Phase 3 migration map). No application or publication change.
- Roadmap step 9, Phase 4 (PR #88, `68d624e`, merged as `c06ff8ddccf8b27e9e15bd6f1ddc19798c9d86ab`; CI 44189318387 and, after the merge, 44190011314) split `business-rules.md` by responsibility (§1 to §12 stay, with a section index; §13 to §15 are in `business-rules-settings.md`, §16 to §23 in `business-rules-gui.md`; the numbers are unchanged), made `test-coverage.md` the one authority for the tests (its counts from a new discovery; the test lists of `feature-inventory.md` and `repository-map.md` moved there or were dropped as duplicates), and corrected stale and broken current content and the pointers made obsolete by Phases 2 and 3 (`history/README.md` §Phase 4 migration map). Documentation only: no application or publication change.
- Roadmap step 9, Phase 5 (PR #89, `1b0c173`, merged as `6df374c0203f84b4f607d84a0849bed87a97dc25`; CI 44198880425 and, after the merge, 44199443431) added the deterministic documentation-policy checker and its tests in `.github/docs-policy/`: `check-docs.sh`, its tests `test-check-docs.sh`, the registered values and exceptions `docs-policy.conf` and the usage `README.md`. They run locally only: no workflow runs them, and wiring them into `ci.yml` is a separate workflow change that needs its own approval.
It also pointed the dated CI counts of `technical-debt.md` §Missing tests to `test-coverage.md` (the sentence moved to `history/technical-debt.md`), escaped two pipes that split a row of `history/README.md` §Phase 3 migration map, and added pointers to the router and `build-and-test.md` (`history/README.md` §Phase 5 record). No application, test-project, workflow or publication change.
- The Phase 5 follow-up (PR #90, `93e8f77`, merged as `3891d17379e9b0497ef58177e0d678797d80c84e`; CI 44210559408 and, after the merge, 44210977723) recorded PR #89, added the documentation-policy suite to `test-coverage.md`, escaped the pipe that split the `--version` row of `console-to-gui-map.md` and added the route "Repository navigation and code-location lookup" to the router, so the checker's two known defects were removed from `.github/docs-policy/docs-policy.conf` (`history/README.md` §Phase 5 record). Documentation only: it completed the Phase 5 follow-up with no workflow integration and no application or publication change.
- The CI integration of the documentation-policy checks (PR #91, `6b64375`, merged as `d181db8569e96c4b06358cdad2b88e8a5eb126a6`; branch `ci/docs-policy-integration`; CI 44217125135 on the tested head and, after the merge, 44217611341, both on attempt 1) completed the separately approved CI-integration follow-up to Phase 5. `ci.yml`'s required job, `Restore, build, test, vulnerability scan`, checks out the full history and the tags (`fetch-depth: 0`) and, before the build, runs `test-check-docs.sh` and `check-docs.sh`: exit 0 and 3 (warnings only) pass; 1, 2 and any other code fail the job. Nine tests of these steps were added (98 in all).
In both runs the runner's awk was GNU Awk 5.3.2; the checker and its tests had also passed locally under mawk. The checker reported no error and the five recommendation-only size warnings; its tests gave 97 passed and one skipped, the no-network test, which needs `unshare -rn` (`.github/docs-policy/README.md` §In CI; `history/README.md` §Phase 5 record). No manual dispatch or rerun, no Release workflow run, and no ruleset, `release.yml`, application or publication change.
- The closure of roadmap step 9 (PR #92, `fe0ddc9`, merged as `5ad87b93cf50838aeba1537cb96f2912d2bb1b3f`; branch `docs/ai-context-step9-closure`, then deleted; CI 44219948490 and, after the merge, 44220510408, both on attempt 1) recorded PR #91 and closed the step on the maintainer's decision: Phase 6 deferred, step 10 next (§Roadmap position; `history/README.md` §Phase 5 record, §Planned phases).
Documentation only, four AI-context documents: no application, workflow, test, README, screenshot, release-note, dependency or publication change, and no Release workflow run.
- Roadmap step 10, Phase T (PR #93, branch `ci/td57-floating-image-tags`) resolved TD-57 (D59): `publish_oci_archive` reads every image tag before it copies anything, never moves an exact version tag, and moves a stable version's `<major>.<minor>` and `latest` only forward, from an image whose version label is an older stable version; release-policy tests 465 (`history/validation/ba_v0.1.1.md` §BB).
It corrected the stale test comments of `ci.yml` and `release.yml` (comments only) and recorded PR #92, the `v0.1.1` design investigation (§BA), the maintainer's decisions on it, TD-58 and open question #43. No application, test-project, tag, release or publication change, and no manual dispatch or rerun.
It merged on 2026-11-18 at 03:51:25 UTC as `90ccebc2b7b1b39c02ed13cc5690016509bc6433` (tested head `fdf3a87`; the branch then deleted). The final CI 44232403532 and automatic Release dry run 44232403401 on that head and CI 44233309570 after the merge passed on attempt 1; the dry run published nothing. Its commits, checked against the Git history: `059fc89` the stub registry and its self-test (420 release-policy tests), `9b7a859` the publication change with its 45 tests (465), `a50d5df` the comments, `a7ad316` and `fdf3a87` the documentation (`history/README.md` §Phase 5 record).
- Roadmap step 10, Phase A (PR #94, branch `feat/v011-2-current-configuration`) implemented V011-2 (D60): `CurrentConfiguration` builds the eight View Config items once for both hosts; the console's View Config writes them byte for byte as before (compared on a pseudo-terminal), and Settings > System shows them in a read-only Current configuration card with Copy and Open settings folder (`business-rules-gui.md` §24; `history/validation/ba_v0.1.1.md` §BC). The card reads `General.yaml` read-only, never creates it, shows no value of `Email.yaml` and writes nothing; the smoke test's `system-tab` line reports it (471 release-policy tests). Tests: 1,272 (1,033 unit and 239 GUI). No tag, release, candidate, dispatch, rerun or publication; README and screenshots unchanged (Phase D).
It merged on 2026-11-18 at 05:00:43 UTC as `739779f973628c9e6d1ab7fb88313947c4e746c6` (tree `f34318b5761e1aefa0ce737fcf141ce7f08414e6`; tested head `233477c`; the branch then deleted). The final CI 44240211673 and automatic Release dry run 44240211644 on that head (27 jobs passed; the image push and the GitHub release skipped) and CI 44241224492 after the merge passed on attempt 1, with no manual dispatch or rerun; nothing was published.
- Roadmap step 10, Phase B (branch `feat/v011-1-refresh-database`) implements V011-1, Settings > System > Refresh database; it started on 2026-11-18 from `739779f`, approved by the maintainer after the Phase A report. Its isolated real-network RIDB refresh needs a separate approval before it runs.

## Roadmap position

- Steps 1 to 8 are complete (`history/decision-log.md` §Roadmap; `history/roadmap-and-status.md`).
- **Step 9, the AI-context consolidation, is complete** (closed on 2026-11-18 by the maintainer's decision, PR #92; `history/README.md` §Planned phases). Phase 1 made the compact core and the authority structure. Phase 2 split the current build, release and maintainer content from the release history and the validation records. Phase 3 separated the current indexes of the decisions, technical debt and open questions from their complete history. Phase 4 split the business rules, made `test-coverage.md` the current test authority and corrected stale current documentation. Phase 5 added the deterministic documentation-policy checker and its tests; its follow-up (PR #90) resolved the checker's two known fixable warnings and recorded its suite, and PR #91 put the checker and its tests into the existing required CI job.
- The checker now runs on every pull request to `master` and every push to it (the existing CI triggers), with the full history and the tags. Exit 3, warnings only, passes; 1, 2 and any other code fail the job. Its five remaining warnings are recommendation-only size budgets (the router's §Size budgets).
- **Phase 6 is optional and deferred:** stronger live enforcement, such as a Claude Code hook or deny rule for real user-data paths. It has not begun, is not required before `v0.1.1` and is not rejected; it stays available for separate consideration later, with its own approval, if enforcement at tool-call time proves useful.
- **Step 10, `v0.1.1`, is in progress.** Its read-only design investigation is done (2026-11-18 UTC, on `5ad87b9`; `history/validation/ba_v0.1.1.md` §BA). Its requirements are V011-1 to V011-3 below, with the design decisions the maintainer approved after the investigation; none is implemented.
- **Phase T is complete** (PR #93; D59, §BB): TD-57 is resolved, so a stable version's `<major>.<minor>` and `latest` move forward, and the stale test comments of the workflows are corrected. **Phase A is complete** (PR #94; D60, §BC): V011-2 is implemented. **Phase B (V011-1) is in progress** (approved 2026-11-18; branch `feat/v011-1-refresh-database`).
- **The approved plan** (2026-11-18), in this order, each phase with its own approval and pull request: Phase T, TD-57 and the stale test comments of `ci.yml` and `release.yml`; Phase A, V011-2; Phase B, V011-1; Phase C, V011-3, with TD-56's review; Phase D, the repository's `README.md` and its screenshots.
Then the curated `v0.1.1` notes, a readiness check, a candidate (a manual dispatch), the maintainer's personal test of it and, each separately approved, the tag and the publication. `v0.1.1` is a stable version; TD-57, which would have blocked its publication, is resolved by Phase T.
- Authorized now: Phase B (V011-1), its implementation and offline validation through its pull request, merge and cleanup. Its isolated real-network RIDB refresh, Phases C and D, the notes, Phase 6 and every tag, release, candidate, dispatch or rerun need their own approval (`CLAUDE.md` §Approval boundaries).

## Deferred `v0.1.1` requirements

The IDs are permanent. The design decisions the maintainer approved on 2026-11-18, after the investigation (`history/validation/ba_v0.1.1.md` §BA), are recorded under each requirement. Phase A implemented V011-2 (D60); V011-1 and V011-3 are not implemented: each phase needs its own approval.

### V011-1 Settings > System > Refresh database
Expose in the GUI the database refresh that the console offers as "Refresh Ridb Data (slow)". Context: `feature-inventory.md` F6, `console-to-gui-map.md` F6, `business-rules.md` §8 and §9, `engineering-rules.md` ER-05, TD-54, TD-58, open question #43.

Approved design (2026-11-18; Phase B, not implemented):
- A host-neutral refresh service in `CampertronLibrary`, in the pattern of GS1's search service (D45): progress, cancellation and a typed result, nothing written to the console. This is an explicit exception to ER-06's "don't change `CampertronLibrary` for the GUI", to be recorded by its own decision. `RefreshRidbRecreationData.RefreshData` stays the console's adapter, with its output and exit behavior unchanged.
- A card in Settings > System with an in-window confirmation card (the download is about 570 MB). Cancel is offered only before the installation starts. Closing the window requests a cancel, and `Program.Main` waits for a running refresh.
- After a successful refresh the user restarts Campertron to see the new data; there is no live reload.
- A stalled download must end or be cancellable: today's download has no timeout for the body (§BA). This is a design requirement of the service, not recorded technical debt.
- The smoke test's `system-tab` line is extended for the new card, with `check_gui_smoke_report` and its policy tests changed together.
- Validation includes an isolated real refresh that downloads the RIDB export (it contacts `ridb.recreation.gov`; never against real user data), which needs its own approval when it runs.

### V011-2 Settings > System > View current configuration
Expose in the GUI what the console shows as "View Config". Context: `feature-inventory.md` F7, `console-to-gui-map.md` F7, `business-rules.md` §12.

Approved design (2026-11-18), implemented by Phase A (PR #94, D60; `business-rules-gui.md` §24):
- A read-only card in Settings > System with exactly the console's eight View Config items: version, settings folder, cache folder, output, number of searches, the database refresh age, the operating system and the .NET runtime. The paths are not redacted.
- It never shows a value of `Email.yaml` (not even whether one is set), search file names or contents, other `General.yaml` values, environment variables or database content.
- It reads `General.yaml` read-only and never through `Yaml.GeneralConfigGetConfig`, which creates the file when it is missing; it writes nothing.
- **Copy** copies exactly the displayed text; **Open settings folder** opens the folder through the existing `FolderLauncher`.
- The smoke test's `system-tab` line is extended for the new card, with `check_gui_smoke_report` and its policy tests changed together.

As implemented: the card is titled "Current configuration", below Theme, with the console's labels (Version, Config, Cache, Output, Searches, RIDB refresh, OS, Framework) from the library's `CurrentConfiguration`, which the console's View Config uses too; the GUI's Version is "CampertronGUI <version>". It exists once the startup has succeeded and reads `General.yaml` again (read-only) when Settings > System is shown and when the window is activated while it is shown; a file it can't read gives "unknown". Copy gives "Label => value" lines, the console's lines without color.

### V011-3 Coordinated correction of the autoRun and "startup" terminology
Correct misleading `autoRun` and "startup" wording consistently, as the implementation evidence supports: GUI labels, tooltips, accessible names, diagnostics, the directly affected tests, README wording, screenshots and the current AI-context records.
**The wording is decided** (the maintainer, 2026-11-18; Phase C, not implemented): the canonical term for a search with `autoRun: true` is "Enabled (autoRun)", and its negative is "Not enabled" (instead of "Disabled"). Explanations are specific to their context, following the verified behavior below; no single sentence is forced across the GUI, the interactive console's Search and Email mode. The implementation settles the exact sentences.

Verified behavior that the wording must describe:
1. Starting the interactive console does not itself run searches.
2. Choosing Search in the interactive console reads the search files each time and runs the searches with `autoRun: true`.
3. Email mode reads the search files once, when it starts, and then runs the `autoRun`-enabled searches every minute.
4. A change to the search files after Email mode started needs a restart of Email mode.
5. The GUI runs no search merely because it starts.
6. In the GUI's Results tab, `autoRun`-enabled searches are selected by default.
7. Searches without `autoRun` run when they are selected manually in the GUI.
8. With no search file, the console creates and runs its example search the next time it searches, not at a generic application start.

Known locations (to be confirmed by the implementation):
- `CampertronGUI/ViewModels/SearchFileViewModel.cs`: "Only searches with autoRun: true run when Campertron starts. Saving never runs a search."
- `CampertronGUI/ViewModels/ResultRows.cs`: "Runs at startup" and "Not run at startup" (row text and accessible names); the identifier `RunsAtStart` (also in `ResultsViewModel.cs`, `ConfigEditSession.cs` and `ConfigValidation.cs`).
- `CampertronGUI/Views/ResultsView.axaml`: "Select the searches that run at startup (autoRun: true)" (tooltip and accessible name) and "also those that don't run at startup".
- `CampertronGUI/ViewModels/SearchesViewModel.cs`: "Campertron also creates and runs its example search at its next start when there is none."
- `CampertronLibrary/function/Base/ConfigValidation.cs`: `NoSearchFiles`, "Campertron creates and runs its example search at its next start."; also review `SearchDisabled`, "autoRun is false: this search does not run."
- Found by the investigation (§BA): `SearchesViewModel.cs`, "Enable a search with Run this search (autoRun) and save it."; `ConfigValidation.cs`'s `NoEnabledSearch` ("so nothing is searched", wrong in the GUI, where a manual run works); the Searches list's "Enabled"/"Disabled" in `SearchFileViewModel.cs`, also in its accessible name; the New search help text and note in `CampertronGUI/Views/SearchesView.axaml`; `ConfigEditSession.cs`, "It does not run until you enable it (autoRun) and save."
- Unchanged: the console's own text in `Yaml.cs` ("No search file has autoRun: true"), which is correct and pinned by `SettingsBootstrapTests`, and "next start" texts unrelated to `autoRun` (a missing file, `autoRefresh`, the theme, the repair).
- Tests that assert these strings: `ResultsTabTests`, `ResultsViewTests`, `SearchFilesTests`, `SearchEditorTests`, `ConfigValidationTests`, `SearchFileSessionTests` and `CampsiteSearchTests`.
- `README.md` lines 59, 61, 65, 67, 166 and 173 (line 172 already matches the behavior since PR #83); the screenshots `docs/ss9.png` and `docs/ss11.png`, recaptured with the procedure of `maintainer-procedures.md` §Refreshing the README screenshots.
- Current AI-context: `business-rules-settings.md` §15 (the folder's `NoSearchFiles`); `business-rules-gui.md` §18 (the enabled toggle, `RunsAtStart`), §19 (`SearchDisabled`) and §20 (the list, the choice, Start, accessibility); `feature-inventory.md` §GUI campsite searches and results (D46); `test-coverage.md`'s `ResultsTabTests` row (moved there from `feature-inventory.md` by Phase 4); `console-to-gui-map.md`'s Results tab row.
- Never rewrite: decisions D44 and D46, `history/validation/ad-ax_preview.2.md` §AI, §AK and §AN, `history/roadmap-and-status.md`, and the published `v0.1.0-preview.2` and `v0.1.0` notes.
- The change touches the Results tab, so TD-56 must be reviewed with it.

## Release blockers

- None. TD-57, which blocked the next stable publication, is resolved by D59 (roadmap step 10, Phase T, PR #93): `docker-publish` moves a stable version's `<major>.<minor>` and `latest` forward and never moves an exact version tag (`release-process.md` §Release workflow). No other technical-debt item blocks a release (`technical-debt.md`).

## Open risks

- **TD-49, unresolved:** `Email.yaml` keeps the SMTP password in plain text, and the README's container recipe makes the settings folder writable by every local user. A host-user recipe was verified (D57), but it is tied to open questions #10 and #27 and the choice is the maintainer's.
- **TD-56, review trigger:** the production default of `ResultsSeams.Post` asks for `Dispatcher.UIThread` on a worker thread, which can make a GUI test fail by chance. Review it with the next change of the Results tab (V011-3 is one).
- **D59's forward move has not run against GHCR:** the release-policy tests and a stub-registry rehearsal cover it (§BB), and dry runs never push. The next stable publication, `v0.1.1`, is its first run there: it moves `0.1` and `latest` from `0.1.0`'s image, whose two platform images carry the version label `0.1.0` (read on 2026-11-18).
A refusal publishes nothing; a failure after a push is completed by an approved rerun of the failed jobs (`release-process.md` §Release workflow, "Failure policy").

## Roadmap boundaries

- No third preview (no `preview.3`) and no website work.
- Campsite providers beyond Recreation.gov: after `v0.1.0`, separately authorized, with no version assigned.
- Signing and notarization are not planned (the README says the programs are not code-signed or notarized).
- Standalone single-file packaging is not planned (the archives are self-contained, not single-file or trimmed; D17).
