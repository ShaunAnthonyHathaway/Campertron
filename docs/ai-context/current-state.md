# Current state

Authority: **the only current authority** for the repository and release state, the changes since the latest release, the roadmap position, the work authorized now, the deferred `v0.1.1` requirements, release blockers, open risks and roadmap boundaries. Where another document disagrees on one of these, this file applies and the other document is stale.

Last verified: 2026-11-18 UTC. Update this file in the same pull request as any change it describes.

## Repository

- The programs have not changed since the `v0.1.0` source commit `a4f4fb5`: no change to application code, the .NET test projects, `release.yml`, the release policy, `global.json`, project files, the Dockerfile or the embedded database. Besides documentation (next section), roadmap step 9, Phase 5 added the documentation-policy checker and its own tests in `.github/docs-policy/`, and since PR #91 `ci.yml`'s required job runs them, the only workflow change.
- The validation baseline is unchanged since the `v0.1.0` release run #58. Its numbers are in `build-and-test.md` §Current validation baseline, and the per-area checks in its §Recommended verification after changes.

## Latest release

- **`v0.1.0`, Latest and immutable:** the first stable version, published 2026-11-17 14:37:25 UTC (decision D58; §AZ in `history/validation/ay-az_v0.1.0.md`).
- Tag `v0.1.0`: annotated, object `892afd60b8a7d521ef4645175b0047f387b8b778`, on commit `a4f4fb50d918cf7398a5c378fa6f310bfa3eb579` (tree `7bc837ee7909977cd0c29cb340333cfad3359f5f`), the merge of PR #81.
- Release run #58 (44102810321): 29/29 jobs, attempt 1, no rerun. Before it, on the same commit: the candidate, Release dry run #56 (44085647906), which the maintainer tested personally and approved, and the pre-tag dry run #57 (44098300331).
- GitHub release 448756659: immutable, Latest, not a draft, `prerelease: false`; 17 assets (16 archives and `SHA256SUMS.txt`), all attested; body 12,019 bytes, SHA-256 `3d0732a85108179f942559073a3ebc32164221b497fb55f670225dbe2ef31e38`.
- GHCR `ghcr.io/shaunanthonyhathaway/campertronconsole`: `0.1.0`, `0.1` and `latest`, all `sha256:38039d81e110467e2c77f2603dd8cc30a13df360e7c9d3f7870b80795a95af1a`.
- The earlier releases, `v0.1.0-beta.4` to `v0.1.0-preview.2`, are immutable and no longer Latest (`history/README.md` §Release index).
- Published release notes, release bodies, assets and tags are immutable historical records. Never rewrite them, including `.github/release-notes/v0.1.0-preview.2.md` and `v0.1.0.md`, unless a separately approved recovery action explicitly requires it.

## Changes on `master` since `v0.1.0`

Documentation only, the documentation-policy checks since Phase 5, and their CI steps since PR #91:
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

## Roadmap position

- Steps 1 to 8 are complete (`history/decision-log.md` §Roadmap; `history/roadmap-and-status.md`).
- **Step 9, the AI-context consolidation, is complete** (closed on 2026-11-18 by the maintainer's decision, PR #92; `history/README.md` §Planned phases). Phase 1 made the compact core and the authority structure. Phase 2 split the current build, release and maintainer content from the release history and the validation records. Phase 3 separated the current indexes of the decisions, technical debt and open questions from their complete history. Phase 4 split the business rules, made `test-coverage.md` the current test authority and corrected stale current documentation. Phase 5 added the deterministic documentation-policy checker and its tests; its follow-up (PR #90) resolved the checker's two known fixable warnings and recorded its suite, and PR #91 put the checker and its tests into the existing required CI job.
- The checker now runs on every pull request to `master` and every push to it (the existing CI triggers), with the full history and the tags. Exit 3, warnings only, passes; 1, 2 and any other code fail the job. Its five remaining warnings are recommendation-only size budgets (the router's §Size budgets).
- **Phase 6 is optional and deferred:** stronger live enforcement, such as a Claude Code hook or deny rule for real user-data paths. It has not begun, is not required before `v0.1.1` and is not rejected; it stays available for separate consideration later, with its own approval, if enforcement at tool-call time proves useful.
- **Step 10, `v0.1.1`, is in progress.** Its read-only design investigation is done (2026-11-18 UTC, on `5ad87b9`; `history/validation/ba_v0.1.1.md` §BA). Its requirements are V011-1 to V011-3 below, with the design decisions the maintainer approved after the investigation; none is implemented.
- **The approved plan** (2026-11-18), in this order, each phase with its own approval and pull request: Phase T, TD-57 and the stale test comments of `ci.yml` and `release.yml`; Phase A, V011-2; Phase B, V011-1; Phase C, V011-3, with TD-56's review; Phase D, the repository's `README.md` and its screenshots.
Then the curated `v0.1.1` notes, a readiness check, a candidate (a manual dispatch), the maintainer's personal test of it and, each separately approved, the tag and the publication. `v0.1.1` is a stable version, so publishing it needs TD-57 fixed first (Phase T).
- Authorized now: Phase T only: TD-57, the stale workflow comments and these records, through a pull request, with no dispatch, rerun, tag, release or publication. Phases A to D, the notes, Phase 6 and every tag, release, dispatch or rerun need their own approval (`CLAUDE.md` §Approval boundaries).

## Deferred `v0.1.1` requirements

The IDs are permanent. The design decisions the maintainer approved on 2026-11-18, after the investigation (`history/validation/ba_v0.1.1.md` §BA), are recorded under each requirement. Implementing them is not approved: each phase needs its own approval.

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

Approved design (2026-11-18; Phase A, not implemented):
- A read-only card in Settings > System with exactly the console's eight View Config items: version, settings folder, cache folder, output, number of searches, the database refresh age, the operating system and the .NET runtime. The paths are not redacted.
- It never shows a value of `Email.yaml` (not even whether one is set), search file names or contents, other `General.yaml` values, environment variables or database content.
- It reads `General.yaml` read-only and never through `Yaml.GeneralConfigGetConfig`, which creates the file when it is missing; it writes nothing.
- **Copy** copies exactly the displayed text; **Open settings folder** opens the folder through the existing `FolderLauncher`.
- The smoke test's `system-tab` line is extended for the new card, with `check_gui_smoke_report` and its policy tests changed together.

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

- **TD-57, live: it blocks the next stable publication.** `docker-publish` never moves an existing image tag, and `0.1` and `latest` exist at `38039d81…`, so any stable release (a version without a hyphen, such as `v0.1.1`) fails there until the release workflow may move a stable version's floating tags. The fix is a release-workflow change that needs its own approval; whether it lands with the `v0.1.1` work or separately is not decided (`technical-debt.md` TD-57).

## Open risks

- **TD-49, unresolved:** `Email.yaml` keeps the SMTP password in plain text, and the README's container recipe makes the settings folder writable by every local user. A host-user recipe was verified (D57), but it is tied to open questions #10 and #27 and the choice is the maintainer's.
- **TD-56, review trigger:** the production default of `ResultsSeams.Post` asks for `Dispatcher.UIThread` on a worker thread, which can make a GUI test fail by chance. Review it with the next change of the Results tab (V011-3 is one).

## Roadmap boundaries

- No third preview (no `preview.3`) and no website work.
- Campsite providers beyond Recreation.gov: after `v0.1.0`, separately authorized, with no version assigned.
- Signing and notarization are not planned (the README says the programs are not code-signed or notarized).
- Standalone single-file packaging is not planned (the archives are self-contained, not single-file or trimmed; D17).
