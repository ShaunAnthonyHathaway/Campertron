# Current state

Authority: **the only current authority** for the repository and release state, the changes since the latest release, the roadmap position, the work authorized now, the deferred `v0.1.1` requirements, release blockers, open risks and roadmap boundaries. Where another document disagrees on one of these, this file applies and the other document is stale.

Last verified: 2026-11-17 UTC. Update this file in the same pull request as any change it describes.

## Repository

- The programs have not changed since the `v0.1.0` source commit `a4f4fb5`: no change to application code, tests, workflows, the release policy, `global.json`, project files, the Dockerfile or the embedded database. Only documentation changed (next section).
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

Documentation only:
- PR #82 (`bcad6dd`, merged as `e9ad33a`) recorded `v0.1.0`: decision D58, `build-and-test.md` §AZ, roadmap steps 9 and 10, TD-57 live.
- PR #83 (`3bec498`, merged as `3adcc8b`; CI 44142070534 and, after the merge, 44142679451) corrected README accuracy only: the GUI named in the introduction, the wording about shared files, and how the console loads search files in each mode. It changed no architecture, behavior, command or feature status, so it has no decision entry or validation record.
- Roadmap step 9, Phase 1 (PR #84) added the mandatory core (`CLAUDE.md` rewritten, `docs/ai-context/README.md`, this file), `engineering-rules.md` and `history/` (`history/README.md` §Phase 1 migration map).
- PR #85 (`b389d29`, merged as `6309671`; CI 44163859073 and, after the merge, 44164472500) changed `README.md` only. It removed the obsolete comparisons of the stable release with beta, preview and other pre-release builds, and kept the x86-64-v2 processor requirement as direct guidance for the stable release. It changed no application behavior; the application code is still that of the `v0.1.0` source commit. It has no decision record.
- Roadmap step 9, Phase 2 (PR #86, `9371213`, merged as `3f22d83`; CI 44175508297 and, after the merge, 44176256615) split `build-and-test.md`: the release policy and procedure into `release-process.md`, the hands-on procedures into `maintainer-procedures.md`, the release history into `history/releases.md` and the validation records §A to §AZ into four files in `history/validation/`. `build-and-test.md` keeps the build, test and CI material, gains §Current validation baseline (moved from §Repository above) and points to every moved section (`history/README.md` §Phase 2 migration map).
- Roadmap step 9, Phase 3 (PR #87, `0c8be79`, merged as `6ad651d`; CI 44182483933 and, after the merge, 44183093528) turned `decisions.md`, `technical-debt.md` and `open-questions.md` into compact current indexes (each decision's status and current rule; one effective disposition per debt item and one status per question, with the open items' complete records) and moved the complete historical records to `history/decision-log.md`, `history/technical-debt.md` and `history/open-questions.md` (`history/README.md` §Phase 3 migration map). No application or publication change.
- Roadmap step 9, Phase 4 (PR #88) split `business-rules.md` by responsibility (§1 to §12 stay, with a section index; §13 to §15 are in `business-rules-settings.md`, §16 to §23 in `business-rules-gui.md`; the numbers are unchanged), made `test-coverage.md` the one authority for the tests (its counts from a new discovery; the test lists of `feature-inventory.md` and `repository-map.md` moved there or were dropped as duplicates), and corrected stale and broken current content and the pointers made obsolete by Phases 2 and 3 (`history/README.md` §Phase 4 migration map). Documentation only: no application or publication change.

## Roadmap position

- Steps 1 to 8 are complete (`history/decision-log.md` §Roadmap; `history/roadmap-and-status.md`).
- **Step 9, the AI-context consolidation, is in progress.** Its audit is done and approved. Phase 1, core and authority, Phase 2, the split of `build-and-test.md`, Phase 3, the indexes of the decisions, technical debt and open questions, and Phase 4, the split of `business-rules.md`, `test-coverage.md` and the stale-content corrections, are done. Phases 5 and 6 are designed (`history/README.md` §Planned phases) and each needs its own approval.
- **Step 10, `v0.1.1`, follows step 9.** Its requirements are V011-1 to V011-3 below; none is designed or implemented. `v0.1.1` is a stable version, so publishing it needs TD-57 fixed first.
- Authorized now: nothing beyond step 9 Phase 4. Each later phase, any `v0.1.1` design or implementation, and every tag, release, dispatch or rerun needs its own approval (`CLAUDE.md` §Approval boundaries).

## Deferred `v0.1.1` requirements

The IDs are permanent. Design and implementation are not approved for any of them.

### V011-1 Settings > System > Refresh database
Expose in the GUI the database refresh that the console offers as "Refresh Ridb Data (slow)". Context: `feature-inventory.md` F6, `console-to-gui-map.md` F6, `business-rules.md` §8 and §9, `engineering-rules.md` ER-05, TD-54.

### V011-2 Settings > System > View current configuration
Expose in the GUI what the console shows as "View Config". Context: `feature-inventory.md` F7, `console-to-gui-map.md` F7, `business-rules.md` §12.

### V011-3 Coordinated correction of the autoRun and "startup" terminology
Correct misleading `autoRun` and "startup" wording consistently, as the implementation evidence supports: GUI labels, tooltips, accessible names, diagnostics, the directly affected tests, README wording, screenshots and the current AI-context records. **The final user-facing wording is not decided**; choosing it is a material product decision. "Enabled (autoRun)" is only a candidate, because the GUI already says "Select enabled".

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
- Tests that assert these strings: `ResultsTabTests`, `ResultsViewTests`, `SearchFilesTests`.
- `README.md` lines 65 and 173 (line 172 already matches the behavior since PR #83); the screenshots `docs/ss9.png` and `docs/ss11.png`, recaptured with the procedure of `maintainer-procedures.md` §Refreshing the README screenshots.
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
