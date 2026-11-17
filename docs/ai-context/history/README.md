# History index

> **Historical record — not current authority.** Nothing indexed here is current authority. For the current state read `../current-state.md`, for routing and authority read `../README.md`, and for instructions read `CLAUDE.md` at the repository root. When a historical record and a current document differ, the current document applies.

This file indexes the historical records of the AI context and records how roadmap step 9 moves them. Load a record only when the router's route, an investigation or a discrepancy requires it (`../README.md` §History policy).

## Label policy

- A label is a global, permanent name for a record or a section: the lettered records `build-and-test.md` §A to §AZ; named sections such as §Release workflow; `business-rules.md` §1 to §23; decisions D1 to D58 and recommendations R1 to R8; TD-01 to TD-57; open questions #1 to #42; features F1 to F13; engineering rules ER-01 to ER-32; the requirements V011-1 to V011-3.
- A label never changes meaning and is never reused. A new record takes the next free label: §BA, §BB and so on; D59; R9; TD-58; #43; ER-33. TD-17 to TD-19 were never assigned and stay unused.
- When content moves to another file, its label moves with it, its old place keeps a pointer with the same heading text, and the move is recorded below with its proof. References in code comments and documents therefore keep resolving.

## Label index

Where each family of labels is defined now (after Phase 3), and where the approved audit plans to put its historical part. The last column names the phase that will move a family, or says that it has moved.

| Labels | Defined now | Kind | Planned (phase) |
|---|---|---|---|
| ER-01 to ER-32 | `../engineering-rules.md` | current | — |
| V011-1 to V011-3 | `../current-state.md` | current | — |
| `business-rules.md` §1 to §23 | `../business-rules.md` | current | §13 to §23 in two new files, numbering kept (4) |
| F1 to F13 | `../feature-inventory.md` | current | — |
| `build-and-test.md` §A to §AZ | `validation/`, four files (§Validation record index) | historical | moved in Phase 2 |
| `build-and-test.md` §Release history | `releases.md` | historical | moved in Phase 2 |
| `build-and-test.md` §Release workflow, §Release protections, §Branch protection | `../release-process.md` | current | moved in Phase 2 |
| `build-and-test.md` §Refreshing the README screenshots, §Refreshing the embedded database | `../maintainer-procedures.md` | current procedures | moved in Phase 2 |
| `build-and-test.md` §Required environment, §Commands (repo root), §Current validation baseline (new in Phase 2), §Test warnings, §Continuous integration, §Recommended verification after changes, §Moved sections | `../build-and-test.md` | current | — |
| D1 to D58, R1 to R8 | the index in `../decisions.md` (status, Changed by, current rule); the complete decision records and R2, R5 to R8 in `decision-log.md`; R1, R3, R4 in `../decisions.md` | index current; records historical | moved in Phase 3 |
| `decisions.md` §Roadmap | `decision-log.md` §Roadmap; a stub in `../decisions.md` | historical | moved in Phase 3 (planned for `roadmap-and-status.md`, which Phase 3 was not authorized to change) |
| TD-01 to TD-57 | the index and the open items' records in `../technical-debt.md`; the resolved and closed items, TD-30's record and the three earlier disposition layers in `technical-debt.md` here | index and open records current; the rest historical | moved in Phase 3 |
| Open questions #1 to #42 | the index and the open questions' records in `../open-questions.md`; the closed ones and the earlier disposition summary in `open-questions.md` here | index and open records current; the rest historical | moved in Phase 3 |
| `CLAUDE.md` status narrative, baselines, replaced sections | `roadmap-and-status.md` | historical | — |

## Release index

Every release is immutable. Full records: `releases.md` (§Release history) and the validation records named (`validation/`, §Validation record index).

| Version | Published (UTC) | Tag object → commit | Release run | Release ID | Record |
|---|---|---|---|---|---|
| `v0.1.0-beta.4` | 2026-11-11 03:31:45 | `e73fe6d6` → `5392aad` | 42952907979 | 441205400 | §Release history, §I |
| `v0.1.0-beta.5` | 2026-11-11 13:46:54 | `7de2c762` → `0a20025` | 43031635085 | 441754432 | §Release history |
| `v0.1.0-beta.6` | 2026-11-11 14:47:51 | `6775de8f` → `6a172bd` | 43041084028 | 441825528 | §Release history, §J |
| `v0.1.0-beta.7` | 2026-11-12 04:41:03 | `b55226bc` → `715f95e` | 43155929199 | 442504169 | §Release history |
| `v0.1.0-beta.8` | 2026-11-12 13:05:33 | `f44968fc` → `84e5649` | 43223202344 | 442958907 | §Release history, §P |
| `v0.1.0-beta.9` | 2026-11-14 03:28:01 | `90534273` → `24fd40d` | 43551346862 | 444985519 | §Release history, §Q, §R |
| `v0.1.0-beta.10` | 2026-11-14 18:13:35 | `6e76ea44` → `b5fcf9f` | 43624820186 | 445493647 | §Release history |
| `v0.1.0-preview.1` | 2026-11-15 04:12:29 | `51cf1a22` → `6484dc2` | 43675087450 | 445802503 | §AC, D38 |
| `v0.1.0-preview.2` | 2026-11-17 11:11:47 | `804849f7` → `ae5c439` | 44070558996 | 448518695 | §AX, D56 |
| `v0.1.0` | 2026-11-17 14:37:25 | `892afd60` → `a4f4fb5` | 44102810321 | 448756659 | §AZ, D58 |

The lightweight 2024 tags `v0.1-beta1` and `v0.1-beta3` point to `e96bc7c`; their release entries were deleted on 2026-11-11 (§Release history, `v0.1.0-beta.4`).

## Validation record index

| Labels | Period | File (since Phase 2) |
|---|---|---|
| §A to §W, with "Warning breakdown (unique warnings)" after §B and "Test-run side effects (isolated dir)" after §T | 2026-11-10 to `v0.1.0-beta.10` | `validation/a-w_2026-11-10_to_beta.10.md` |
| The first CI runs and the release workflow's "Local validation, 2026-11-10" (§Local validation), at the end of that file | 2026-11-10 | `validation/a-w_2026-11-10_to_beta.10.md` |
| §X to §AC | to `v0.1.0-preview.1` | `validation/x-ac_preview.1.md` |
| §AD to §AX | to `v0.1.0-preview.2` | `validation/ad-ax_preview.2.md` |
| §AY, §AZ | to `v0.1.0` | `validation/ay-az_v0.1.0.md` |

No record after §AZ exists yet. The next takes §BA, in a new file of `validation/` for the period after `v0.1.0` (`../README.md` §Maintaining these documents).

## Phase 1 migration map

Roadmap step 9, Phase 1 (PR #84, 2026-11-17 UTC) rewrote `CLAUDE.md` as the compact core. The source is `CLAUDE.md` at `3adcc8be47a5708ebd950ee8d5b3b8448ba6263d` (80,290 bytes, 105 lines, SHA-256 `2249ace878e76436b484be42bc667c82b9cec9293add17a92dcc5a5ad16c3ed9`). Each of its sections went where this table says; "byte" means the section's text is a byte-for-byte substring of the destination, "normalized" means it equals the destination after every run of whitespace is reduced to one space (only line breaks were added, at sentence boundaries, which Markdown renders as the same paragraph).

| Old section (lines) | Bytes | SHA-256 | Treatment | Destination | Proof |
|---|---|---|---|---|---|
| Title (1–2) | 28 | `b49fbf937b50fd82…` | kept | `CLAUDE.md` | byte |
| §Purpose (3–5) | 557 | `5def932a992a9225…` | kept | `CLAUDE.md` §Purpose | byte |
| §Project status (6–8) | 18,560 | `6ce298eda0d4a845…` | moved; current facts rewritten | `roadmap-and-status.md` §1; current facts in `../current-state.md` | normalized |
| §Solution layout (9–20) | 1,956 | `0ff8ad831637144f…` | moved; pointer kept | `roadmap-and-status.md` §3.1; `CLAUDE.md` names `repository-map.md` | byte (body) |
| §Commands (21–36): the heading line (21) | 8,499 of the section's 11,293 | `f5fe130f289df647…` (the whole section) | moved | `roadmap-and-status.md` §2 | normalized |
| §Commands: the command block (22–33) | 990 | | kept | `CLAUDE.md` §Build and test essentials | byte |
| §Commands: the two warnings (34–35) and a blank line | 1,803 + 1 | | kept | `CLAUDE.md` §Build and test essentials | byte |
| §Architecture in one paragraph (37–39) | 4,413 | `652b0cb98403ae93…` | moved; pointer kept | `roadmap-and-status.md` §3.2; `CLAUDE.md` names `architecture.md` | normalized |
| §Conventions observed (40–46) | 712 | `58c6a08e58c80ee2…` | kept | `CLAUDE.md` | byte |
| §Validation and logging (47–50) | 586 | `8522a0a8ecac6526…` | kept | `CLAUDE.md` | byte |
| §Preserving business logic (51–84) | 40,719 | `45a7fe5920b2cafb…` | moved; one-line summaries kept | `../engineering-rules.md` ER-01 to ER-32; `CLAUDE.md` §Invariant index | 12 rules byte, 20 normalized |
| §Required workflow (85–95) | 575 | `9cd47baf22870ee7…` | kept | `CLAUDE.md` | byte |
| §Detailed docs (96–105) | 891 | `07542a5208a16a7c…` | moved (in a code block, its links being root-relative); replaced by the router | `roadmap-and-status.md` §3.3; `../README.md` | byte |

`CLAUDE.md` also gained what the repository had not recorded: the reading order and the imports, the approval boundaries and the autonomous non-publication workflow with its stop conditions (the maintainer's decision 1 of step 9), branch cleanup, data safety, the invariant index and the release safeguards. The pre-tag reading policy (decision 2 of step 9) is in `../README.md` §Release and audit reading policy; it replaces the complete read of every AI-context file before a tag that D58 and §AZ record, which stay as historical records.

## Completeness accounting

- **Bytes:** the eleven sections above add up to the 80,290 bytes of the old `CLAUDE.md`; each is kept, moved or both, as the table says.
- **Proofs** (a scratch script, run on the final files): every "byte" row is a byte substring of its destination; every "normalized" row and each of the 20 line-broken rules is equal after whitespace normalization; the 32 rules are in their original order under ER-01 to ER-32.
- **Tokens:** all 832 distinctive tokens of the old `CLAUDE.md` (hashes, run, release and PR numbers, counts, IDs, § labels, versions, paths and code spans) occur in the new files; none is missing.
- **Current facts:** the facts of the status narrative that are still current are in `../current-state.md` (the latest release with its tag object, commit, run, release ID, body hash and GHCR index; the roadmap position; TD-57, TD-49, TD-56; the boundaries), checked item by item.
- **Untouched:** the nine other files of `docs/ai-context` (`architecture.md`, `build-and-test.md`, `business-rules.md`, `console-to-gui-map.md`, `decisions.md`, `feature-inventory.md`, `open-questions.md`, `repository-map.md`, `technical-debt.md`) are byte-identical to `3adcc8b`; no historical record was rewritten or removed.

## Core loading

Tested 2026-11-17 with Claude Code 2.1.326, in scratch copies of `CLAUDE.md` and `docs/ai-context` outside the repository, each a fresh, non-persistent session without tools or MCP servers (`claude -p … --tools "" --no-session-persistence --strict-mcp-config --output-format json`):
- With the two import lines, the session answered five questions whose answers exist only in `README.md` or `current-state.md` (the full `v0.1.0` body hash, V011-2, the router's rules for console work, the core's size budget, the file of the release reading policy).
- With the import lines removed (the negative control), it answered "not in context" to the four questions that only those files can answer.
- Context size of a trivial prompt, in input tokens: 3,900 without a `CLAUDE.md`; 34,729 with the old `CLAUDE.md` (about 30,800 for it); 9,934 with the new `CLAUDE.md` without the imports; 18,334 with the new core (about 14,400 for it, 53% less than before).

So `CLAUDE.md` relies on the imports, and it still tells the reader to read both files if they are missing (`CLAUDE.md` §Reading order).

## Phase 2 migration map

Roadmap step 9, Phase 2 (PR #86, 2026-11-17 UTC) split `build-and-test.md`. The source is `build-and-test.md` at `63096719402237eebf0acc1035e4132000a0e82a`: 611,639 bytes, 3,067 lines, SHA-256 `cc1bd5e6dd9c6cca3d1659577844975b1b30a19157b7659059c617b71828b897`. Its 3,067 lines were cut into the 22 units below, at section boundaries; no lettered record was divided.

Proofs: "byte" means the unit is a byte-for-byte substring of its destination. "Rejoin" means the destination block, with each added line break turned back into the one space it replaced, equals the source bytes exactly; the breaks were added only at sentence boundaries outside code spans, tables and headings, because the files created since Phase 1 keep their lines at or under 1,000 characters (`../README.md` §Maintaining these documents). "EOF" means the unit ends its file, so its final empty line (one byte) was dropped and the file ends with one newline. Every unit is also equal to its destination after whitespace normalization. Retained units keep their bytes: `build-and-test.md` predates Phase 1, so its long lines stay.

| Source lines | Bytes | SHA-256 | Content | Authority | Treatment | Destination | Proof |
|---|---|---|---|---|---|---|---|
| 1–2 | 18 | `62317252a67314dd…` | Title | current | retained | `../build-and-test.md` | byte |
| 3–4 | 115 | `5e929013dd15bd9e…` | Legend | current | retained | `../build-and-test.md` | byte |
| 5–14 | 2,899 | `d17e20f42dafadad…` | §Required environment | current | retained | `../build-and-test.md` | byte |
| 15–36 | 2,376 | `f70dd5b7ecc0ac06…` | §Commands (repo root) | current | retained | `../build-and-test.md` | byte |
| 37–38 | 107 | `9c48889ea02ed92e…` | "Verified results: 2026-11-10, …" (the heading over the records) | historical | moved verbatim | `validation/a-w_…` | byte |
| 39–839 | 133,932 | `eb7bb6206eef0055…` | §A to §T, with "Warning breakdown (unique warnings)" | historical | moved verbatim | `validation/a-w_…` | rejoin, 7 breaks; one table row of 1,084 characters (line 795, §S) kept |
| 840–898 | 18,995 | `a51c84ad204d1db3…` | §Refreshing the README screenshots (maintainer procedure) | current | moved verbatim; current procedure | `../maintainer-procedures.md` | rejoin, 2 breaks |
| 899–909 | 2,060 | `1808698e9f2c0890…` | §Refreshing the embedded database (maintainer procedure) | current | moved verbatim; current procedure | `../maintainer-procedures.md` | byte |
| 910–918 | 954 | `579444b52b06f362…` | §Test-run side effects (isolated dir) | historical | moved verbatim | `validation/a-w_…` | byte |
| 919–1059 | 25,292 | `7484d9fef17bd773…` | §U to §W | historical | moved verbatim | `validation/a-w_…` | rejoin, 5 breaks |
| 1060–1430 | 67,246 | `fe2b47adf3d242c8…` | §X to §AC | historical | moved verbatim | `validation/x-ac_…` | rejoin, 1 break; EOF |
| 1431–2444 | 215,973 | `80b7837010faaa3a…` | §AD to §AX | historical | moved verbatim | `validation/ad-ax_…` | rejoin, 32 breaks; EOF |
| 2445–2539 | 21,740 | `dfdc2a0a507241a9…` | §AY, §AZ | historical | moved verbatim | `validation/ay-az_…` | rejoin, 2 breaks; EOF |
| 2540–2550 | 3,724 | `8312e7569a42ec07…` | §Test warnings | current | retained | `../build-and-test.md` | byte |
| 2551–2581 | 6,599 | `f70b9ee0c45cd80d…` | §Continuous integration, the description | current | retained | `../build-and-test.md` | byte; a list item of 1,372 characters kept (the file predates Phase 1) |
| 2582–2590 | 786 | `f90ae73ae0c496f9…` | §Continuous integration, the first runs | historical | moved verbatim under a new heading | `validation/a-w_…` | byte |
| 2591–2719 | 31,367 | `f845b82998e03e27…` | §Release workflow, to the end of "Release procedure" | current | moved verbatim; current | `../release-process.md` | rejoin, 3 breaks |
| 2720–2750 | 5,683 | `271789be982fc25a…` | §Release workflow, "Local validation, 2026-11-10" | historical | moved verbatim under a new heading | `validation/a-w_…` | byte; EOF |
| 2751–2758 | 1,335 | `e33acb1041dd018e…` | §Release protections | current | moved verbatim; current | `../release-process.md` | byte |
| 2759–3013 | 55,260 | `f299045a7359a714…` | §Release history | historical | moved verbatim | `releases.md` | rejoin, 2 breaks; EOF |
| 3014–3049 | 4,160 | `7f4bc71aa30d400d…` | §Branch protection | current | moved verbatim; current | `../release-process.md` | byte; EOF |
| 3050–3067 | 11,018 | `9d29c749534c5140…` | §Recommended verification after changes | current | retained | `../build-and-test.md` | byte; a list item of 1,267 characters kept (the file predates Phase 1) |

The `validation/` paths are `validation/a-w_2026-11-10_to_beta.10.md`, `validation/x-ac_preview.1.md`, `validation/ad-ax_preview.2.md` and `validation/ay-az_v0.1.0.md`. Two moved parts that had no heading of their own got one in their new file: "Continuous integration: the first runs" and "Release workflow: local validation, 2026-11-10". Each history file starts with a "Historical record — not current authority" notice; each new current file starts with an "Authority:" statement.

### Lettered records

Each record keeps its exact heading line (`### <label>. …`) in its new file, and `../build-and-test.md` keeps the same line as a stub that names the file. Lines are the record's lines in the source, without the empty lines after it. Mentions count every `§<label>` in the repository after Phase 2 outside this file, including the record's own file and its stub; "code" names the code and workflow files among them.

| Label | Source lines | Now in | Mentions |
|---|---|---|---|
| §A | 39–47 | `validation/a-w_…` | 12 in 8 files |
| §B | 49–72 | `validation/a-w_…` | 3 in 2 files |
| §C | 88–97 | `validation/a-w_…` | none |
| §D | 99–116 | `validation/a-w_…` | none |
| §E | 118–149 | `validation/a-w_…` | 3 in 3 files |
| §F | 151–165 | `validation/a-w_…` | none |
| §G | 167–178 | `validation/a-w_…` | none |
| §H | 180–235 | `validation/a-w_…` | 2 in 1 file |
| §I | 237–287 | `validation/a-w_…` | 11 in 6 files |
| §J | 289–312 | `validation/a-w_…` | 3 in 2 files |
| §K | 314–376 | `validation/a-w_…` | 20 in 10 files |
| §L | 378–427 | `validation/a-w_…` | 16 in 11 files |
| §M | 429–474 | `validation/a-w_…` | 15 in 11 files; code: `RidbTableInserter.cs` |
| §N | 476–596 | `validation/a-w_…` | 35 in 12 files |
| §O | 599–657 | `validation/a-w_…` | 18 in 11 files |
| §P | 659–727 | `validation/a-w_…` | 15 in 10 files; code: `MainWindow.axaml.cs`, `MainWindowUsabilityTests.cs` |
| §Q | 729–758 | `validation/a-w_…` | 12 in 9 files |
| §R | 760–779 | `validation/a-w_…` | 18 in 8 files |
| §S | 781–798 | `validation/a-w_…` | 12 in 6 files |
| §T | 800–838 | `validation/a-w_…` | 23 in 10 files; code: `SearchIsolationTests.cs` |
| §U | 919–949 | `validation/a-w_…` | 17 in 10 files |
| §V | 951–980 | `validation/a-w_…` | 8 in 8 files |
| §W | 982–1058 | `validation/a-w_…` | 16 in 8 files |
| §X | 1060–1162 | `validation/x-ac_…` | 42 in 16 files |
| §Y | 1164–1220 | `validation/x-ac_…` | 28 in 11 files |
| §Z | 1222–1275 | `validation/x-ac_…` | 25 in 11 files |
| §AA | 1277–1319 | `validation/x-ac_…` | 11 in 7 files |
| §AB | 1321–1380 | `validation/x-ac_…` | 23 in 9 files |
| §AC | 1382–1429 | `validation/x-ac_…` | 21 in 12 files |
| §AD | 1431–1482 | `validation/ad-ax_…` | 17 in 10 files |
| §AE | 1484–1560 | `validation/ad-ax_…` | 23 in 14 files; code: `ci.yml`, `ConfigFileReplace.cs` |
| §AF | 1562–1611 | `validation/ad-ax_…` | 16 in 9 files |
| §AG | 1613–1682 | `validation/ad-ax_…` | 18 in 11 files |
| §AH | 1684–1738 | `validation/ad-ax_…` | 14 in 11 files |
| §AI | 1740–1800 | `validation/ad-ax_…` | 12 in 10 files |
| §AJ | 1802–1858 | `validation/ad-ax_…` | 21 in 10 files |
| §AK | 1860–1925 | `validation/ad-ax_…` | 18 in 13 files |
| §AL | 1927–1985 | `validation/ad-ax_…` | 23 in 12 files |
| §AM | 1987–2037 | `validation/ad-ax_…` | 26 in 13 files |
| §AN | 2039–2090 | `validation/ad-ax_…` | 28 in 12 files |
| §AO | 2092–2153 | `validation/ad-ax_…` | 25 in 13 files |
| §AP | 2155–2169 | `validation/ad-ax_…` | 6 in 6 files |
| §AQ | 2171–2222 | `validation/ad-ax_…` | 23 in 11 files; code: `RidbIndexes.cs` |
| §AR | 2224–2264 | `validation/ad-ax_…` | 13 in 8 files |
| §AS | 2266–2296 | `validation/ad-ax_…` | 8 in 4 files |
| §AT | 2298–2324 | `validation/ad-ax_…` | 5 in 2 files |
| §AU | 2326–2356 | `validation/ad-ax_…` | 13 in 7 files |
| §AV | 2358–2365 | `validation/ad-ax_…` | 5 in 3 files |
| §AW | 2367–2397 | `validation/ad-ax_…` | 33 in 11 files |
| §AX | 2399–2443 | `validation/ad-ax_…` | 28 in 10 files |
| §AY | 2445–2488 | `validation/ay-az_…` | 19 in 6 files |
| §AZ | 2490–2538 | `validation/ay-az_…` | 48 in 15 files |

### Named headings

| Heading or part in `build-and-test.md` until Phase 2 | Source lines | Now in, same heading unless noted | Authority |
|---|---|---|---|
| "Verified results: 2026-11-10, Linux x86_64 (CachyOS), SDK 10.0.114 / MSBuild 18.0.36 / runtime 10.0.14" | 37 | `validation/a-w_…` | historical |
| "Warning breakdown (unique warnings)" | 74–86 | `validation/a-w_…` | historical |
| §Refreshing the README screenshots (maintainer procedure), with "README and screenshots reconciled" | 840–897 | `../maintainer-procedures.md` | current |
| §Refreshing the embedded database (maintainer procedure) | 899–908 | `../maintainer-procedures.md` | current |
| §Test-run side effects (isolated dir) | 910–917 | `validation/a-w_…` | historical |
| §Continuous integration: the first run and the first push run | 2582–2589 | `validation/a-w_…`, under "Continuous integration: the first runs" | historical |
| §Release workflow (`.github/workflows/release.yml`) with "Triggers and modes", "Jobs", "Publishing gates.", "Permissions", "Actions", "Failure policy" and "Release procedure" | 2591–2718 | `../release-process.md` | current |
| "Local validation, 2026-11-10" (§Local validation) | 2720–2749 | `validation/a-w_…`, under "Release workflow: local validation, 2026-11-10" | historical |
| §Release protections (applied 2026-11-11, before the first tag) [V] | 2751–2757 | `../release-process.md` | current |
| §Release history | 2759–3012 | `releases.md` | historical |
| §Branch protection (`master`), with "Verified [V]", "Inferred, not verified [I]", "Uncertain [?]" and "Consequences [V from the rules above]" | 3014–3048 | `../release-process.md` | current |

The stubs in `../build-and-test.md` keep every moved heading line, at its level and in its order; its §Moved sections table names every part above.

### Current text written in Phase 2

Not a move: new current text, each with its sources; the source records stay unchanged in `validation/`.

| Destination | Sources | Treatment |
|---|---|---|
| `../build-and-test.md` §Current validation baseline | `../current-state.md` §Repository (its baseline bullet, now a pointer); §AY, §AZ | deduplicated: the numbers have one authority |
| `../build-and-test.md` "Authority:", the CI pointer, §Moved sections and the stubs | this map | new |
| `../release-process.md` §Versions, candidates and publication | D17, D18, D19, D49, D56, D58; §AN, §AX, §AZ | current summary |
| `../release-process.md` §Platforms and what a release run shows | D31, D48; §K, §U, §AM; `release-policy.sh` | current summary |
| `../maintainer-procedures.md` §Rules for every procedure | `CLAUDE.md` §Data safety; §N, §X, §AW, §AX, §AZ | new |
| `../maintainer-procedures.md` §Candidate download and inspection | §AC, §AN, §AX, §AZ | extracted as a current procedure |
| `../maintainer-procedures.md` §Release verification after publication | §AC, §AX, §AZ; "Release procedure" step 5 | extracted as a current procedure |
| `../maintainer-procedures.md` §Manual platform validation of the database refresh | §N, "Manual validation procedures for platforms not run here" | extracted; adapted only where it pointed at §N's own results |
| `../maintainer-procedures.md` §Validation harnesses | §M, §N, §O, §Q to §AW | extracted as current procedures |
| `../maintainer-procedures.md` §Tooling problems and their fixes | §P, §V, §X, §Z, §AF, §AN, §AP, §AQ, §AR, §AU, §AW, §AZ; §Release workflow | extracted |

`CLAUDE.md` changed three pointers only: data safety's isolation (§AW → `maintainer-procedures.md` §Rules for every procedure), the validation baseline (`current-state.md` §Repository → `build-and-test.md` §Current validation baseline) and the release safeguard (`build-and-test.md` §Release workflow → `release-process.md` §Release workflow). The router's authority map, routes, history policy and maintenance rules were updated for the new files; `../current-state.md` records PR #85 and this phase.

### Inbound references

No file outside `CLAUDE.md` and `docs/ai-context/` changed. These comments and files cite `build-and-test.md` and keep resolving through its stubs and §Moved sections:

| File | Cites | Resolves to |
|---|---|---|
| `.github/release-notes/README.md` line 6, `.github/release-policy/ReleaseCheck/Program.cs` line 2, `.github/release-policy/release-policy.sh` line 3, `.github/workflows/release.yml` lines 7–8 | §Release workflow | `../release-process.md` |
| `.github/workflows/ci.yml` line 138, `CampertronLibrary/function/Base/ConfigFileReplace.cs` line 30 | §AE | `validation/ad-ax_…` |
| `CampertronLibrary/function/RecDotOrg/sqlite/RidbIndexes.cs` line 10 | §AQ | `validation/ad-ax_…` |
| `CampertronLibrary/function/RecDotOrg/sqlite/RidbTableInserter.cs` line 7 | §M | `validation/a-w_…` |
| `CampertronGUI/Views/MainWindow.axaml.cs` line 141, `CampertronGUITests/MainWindowUsabilityTests.cs` lines 22 and 424 | §P | `validation/a-w_…` |
| `CampertronUnitTests/function/RecDotOrg/api/SearchIsolationTests.cs` line 278 | §T | `validation/a-w_…` |
| `CampertronUnitTests/function/Base/ConsoleFailureTests.cs` line 378 | `build-and-test.md` without a label (the container message it quotes) | the records that quote it (§Y, §AA, §AB and others) |

The current documents that Phase 2 does not edit (`../engineering-rules.md`, `../business-rules.md`, `../decisions.md`, `../technical-debt.md`, `../open-questions.md`, `../feature-inventory.md`, `../architecture.md`, `../console-to-gui-map.md`, `../repository-map.md`) and `roadmap-and-status.md` keep their `build-and-test.md` citations; each resolves through the same stubs.

## Phase 2 completeness accounting

- **Bytes:** the 22 units cover lines 1 to 3,067 once each, in order, and add up to the 611,639 bytes of the source.
- **Proofs** (a scratch script reading the source from Git, run on the final files): 13 units byte; 8 rejoin, with 54 added breaks in 49 lines; 6 units end their file (EOF). The 52 lettered records §A to §AZ are each whole in one unit, appear once as a record in `validation/` and once as a stub in `../build-and-test.md`, and nowhere else as a heading.
- **Tokens:** all 754 distinctive tokens of the source (hashes, run, release and PR numbers, versions, § labels and URLs) and all 2,920 of its code spans occur in the new files.
- **Long lines:** in the files created by Phase 2 only one line exceeds 1,000 characters, the table row of §S at source line 795 (1,084 characters, in `validation/a-w_…`), which Markdown cannot break.
- **Untouched:** every file outside `CLAUDE.md`, `docs/ai-context/README.md`, `docs/ai-context/current-state.md`, `docs/ai-context/build-and-test.md`, this file and the seven new files is byte-identical to `6309671`, including the repository's `README.md`, the application, tests, workflows, examples, screenshots and release notes.

## Phase 3 migration map

Roadmap step 9, Phase 3 (2026-11-17 UTC) turned `decisions.md`, `technical-debt.md` and `open-questions.md` into compact current indexes and moved their complete historical records here. The sources, at `3f22d83f649a0a3a505b1113c4b245f7b6ce42b0`: `decisions.md` 171,845 bytes, 142 lines, SHA-256 `38217e4c919c32da9da74ca8bdf3dd5e2c0f021e633c0e8846ec3c3581606552`; `technical-debt.md` 98,850 bytes, 148 lines, `4161fb99eb59ac3b5e972041339a81e4215aa35546900e6a4906b9cab40a9607`; `open-questions.md` 36,161 bytes, 63 lines, `55ff4afbfbf96c4b749bf7cd4d6ee1251a80e707d74678c69b014ef53c25d7c3`.

Treatments: "converted" turns a table row into a subsection headed with its ID and labeled with the table's column names, or a numbered list item into a subsection headed with its number; the words are unchanged. A blank line after D15 had ended the decisions table, so GitHub showed D16 to D58 as plain text; and a numbered list made GitHub show most questions with a wrong number. Lines over 1,000 characters outside tables were broken at sentence boundaries. In the records that stay current, a `build-and-test.md` § citation was pointed at its Phase 2 destination (the label kept); the history files keep their citations, which resolve through that file's stubs. Proofs: "rebuilt exactly" means the source row or item is reproduced byte for byte from the subsection, its added breaks turned back into spaces and each pointed citation reversed; "rejoin" is Phase 2's proof; "byte" is a byte-identical line. Line numbers are those of the source file.

| Source | Source lines | Destination | Authority | Treatment | Proof |
|---|---|---|---|---|---|
| D1 to D58 | `decisions.md` lines 8–22 and 24–66, one table row each: D1 (8), D2 (9), D3 (10), D4 (11), D5 (12), D6 (13), D7 (14), D8 (15), D9 (16), D10 (17), D11 (18), D12 (19), D13 (20), D14 (21), D15 (22), D16 (24), D17 (25), D18 (26), D19 (27), D20 (28), D21 (29), D22 (30), D23 (31), D24 (32), D25 (33), D26 (34), D27 (35), D28 (36), D29 (37), D30 (38), D31 (39), D32 (40), D33 (41), D34 (42), D35 (43), D36 (44), D37 (45), D38 (46), D39 (47), D40 (48), D41 (49), D42 (50), D43 (51), D44 (52), D45 (53), D46 (54), D47 (55), D48 (56), D49 (57), D50 (58), D51 (59), D52 (60), D53 (61), D54 (62), D55 (63), D56 (64), D57 (65), D58 (66) | `decision-log.md` §Verified decisions, one subsection each (`### D<n>`) | historical | converted: each row a subsection, the column names Decision, Evidence and Stated rationale as labels | rows rebuilt exactly from the subsections |
| R1, R3, R4 | `decisions.md` R1 (69), R3 (71), R4 (72) | `../decisions.md` §Recommendations | current (partly open) | retained | rejoin |
| R2, R5 to R8 | `decisions.md` R2 (70), R5 (73), R6 (74), R7 (75), R8 (76) (R8 with its sub-list, lines 76–84) | `decision-log.md` §Recommendations | historical (done) | moved | rejoin |
| §Roadmap | `decisions.md` lines 86–142 | `decision-log.md` §Roadmap; a stub with the same heading in `../decisions.md` | historical | moved; the plan named `roadmap-and-status.md`, which Phase 3 was not authorized to change | rejoin |
| Title, intro, §Verified decisions heading, table header | `decisions.md` lines 1, 3, 5, 6–7 | title retained in `../decisions.md`; intro and heading moved to `decision-log.md`; the header became the labels | — | retained / moved / converted | byte |
| Open TD items (26) | `technical-debt.md` TD-01 (56), TD-02 (57), TD-03 (58), TD-06 (61), TD-07 (62), TD-09 (64), TD-10 (65), TD-12 (67), TD-15 (70), TD-16 (71), TD-22 (78), TD-23 (79), TD-24 (80), TD-25 (81), TD-26 (82), TD-27 (83), TD-29 (85), TD-35 (91), TD-36 (92), TD-39 (95), TD-49 (111), TD-50 (112), TD-54 (128), TD-55 (129), TD-56 (130), TD-57 (140) | `../technical-debt.md`, one subsection each (`### TD-<nn>`) under its original section | current | converted, with 11 `build-and-test.md` citation groups pointed at their Phase 2 destinations | rows rebuilt exactly after reversing each rewrite |
| Resolved and closed TD items, and TD-30 (28) | `technical-debt.md` TD-04 (59), TD-05 (60), TD-08 (63), TD-11 (66), TD-13 (68), TD-14 (69), TD-20 (76), TD-21 (77), TD-28 (84), TD-30 (86), TD-31 (87), TD-32 (88), TD-33 (89), TD-34 (90), TD-37 (93), TD-38 (94), TD-40 (96), TD-41 (97), TD-42 (98), TD-43 (99), TD-44 (100), TD-45 (101), TD-46 (108), TD-47 (109), TD-48 (110), TD-51 (113), TD-52 (118), TD-53 (123) | `technical-debt.md` here, one subsection each, under its original section | historical | converted | rows rebuilt exactly |
| §Disposition map | `technical-debt.md` lines 7–24 | `technical-debt.md` here; a stub with the same heading in `../technical-debt.md` | historical | moved | rejoin |
| §Reconciliation after GS2 | `technical-debt.md` lines 25–38 | `technical-debt.md` here; a stub with the same heading in `../technical-debt.md` | historical | moved | rejoin |
| §Stable-readiness validation | `technical-debt.md` lines 39–52 | `technical-debt.md` here; a stub with the same heading in `../technical-debt.md` | historical | moved | rejoin |
| The missing-test notes after TD-56 | `technical-debt.md` lines 131–135 | `../technical-debt.md` §Missing tests, the heading R4 cites, restored | current | retained, 1 citation groups pointed directly | rejoin after reversing the rewrites |
| §Migration risks | `technical-debt.md` lines 142–148 | `../technical-debt.md`, same heading | current | retained | rejoin |
| Title, legend, intro, the intro of §Found by the consolidated deferred-work audit, section headings, table headers | `technical-debt.md` lines 1, 3, 5, 104 and the `##` and `| ID |` lines | retained in `../technical-debt.md`; section headings in each file that holds an item of them; the headers became the labels | — | retained / converted | byte |
| Open questions (22) | `open-questions.md` #36 (12), #24 (16), #38 (18), #39 (19), #40 (20), #42 (21), #7 (26), #9 (28), #10 (29), #11 (30), #31 (31), #13 (35), #14 (36), #15 (37), #21 (39), #25 (42), #26 (43), #27 (44), #32 (50), #33 (51), #29 (59), #30 (60) | `../open-questions.md`, one subsection each (`### #<n>`) in its original group | current | converted (a numbered item became a subsection headed with its number), with 17 citation groups pointed directly | items rebuilt exactly after reversing each rewrite |
| Closed and answered questions (20) | `open-questions.md` #1 (8), #2 (9), #3 (10), #35 (11), #4 (13), #18 (14), #22 (15), #5 (24), #6 (25), #8 (27), #12 (34), #19 (47), #16 (54), #17 (55), #20 (56), #23 (57), #28 (58), #34 (61), #37 (62), #41 (63) | `open-questions.md` here, one subsection each, in its original group | historical | converted | items rebuilt exactly |
| The dispositions paragraph | `open-questions.md` line 5 | `open-questions.md` here | historical | moved | rejoin |
| Title, intro, group headings | `open-questions.md` lines 1, 3 and the `##` lines | retained in `../open-questions.md`; group headings in each file that holds a question of the group | — | retained | byte |

TD-17, TD-18 and TD-19 were never assigned: the index of `../technical-debt.md` says so in a row of its own, and nothing uses them. Each index row of `../decisions.md`, `../technical-debt.md` and `../open-questions.md` links the complete record of its ID, in the current file or here.

### Section citations

| Cited as | Cited from | Resolves to |
|---|---|---|
| `decisions.md` §Roadmap (also "§Roadmap step 7", "§Roadmap step 10") | `../current-state.md` (now pointing here), `../README.md` (updated), `../release-process.md`, `../technical-debt.md` (TD-57's record), `technical-debt.md` and `open-questions.md` here (§Stable-readiness validation, #16), `roadmap-and-status.md`, `validation/x-ac_…`, `validation/ad-ax_…` | the stub with the same heading in `../decisions.md`, then `decision-log.md` §Roadmap |
| `decisions.md` §Recommendations, `decisions.md` R4, R8 | `../README.md`, `../technical-debt.md` (R4), `technical-debt.md` and `open-questions.md` here, `releases.md`, `validation/x-ac_…`, `validation/ad-ax_…` (R8) | `../decisions.md` §Recommendations (each R's disposition) |
| `technical-debt.md` §Disposition map, §Reconciliation after GS2, §Stable-readiness validation | `../open-questions.md`, `decision-log.md` (D47, D57), `open-questions.md` here, `roadmap-and-status.md`, `validation/ad-ax_…`, `validation/ay-az_…` | the stubs with the same headings in `../technical-debt.md`, then `technical-debt.md` here |
| `technical-debt.md` §Missing tests | `../decisions.md` (R4) | `../technical-debt.md` §Missing tests, the heading restored (it was missing, so the citation had never resolved) |
| `open-questions.md` Q19, `technical-debt.md` TD-30, TD-04 and every other ID | `../release-process.md`, `validation/ad-ax_…`, `CampertronUnitTests/function/RecDotOrg/api/SearchIsolationTests.cs`; a pattern search also found 1,294 matches of D1 to D58 in 137 other files and 536 of TD IDs in 74, code comments included | the ID's index row, which links its record |

No code, test, workflow or other document changed for these citations.

## Phase 3 completeness accounting

- **Lines:** every non-blank line of the three sources is accounted for (129, 128 and 52 lines): retained, moved, converted or, for the three table headers, turned into the labels; blank lines only separated blocks.
- **Records:** D1 to D58 have one subsection each in `decision-log.md` and one index row each; R1 to R8 have one index row and one record each; the 54 assigned TD items and #1 to #42 have one subsection each, in the current file or here, and one index row each. A scratch script rebuilt every source row and item exactly from its subsection, reversing the 29 pointed citation groups, and checked the identifiers: no duplicate, no gap except TD-17 to TD-19.
- **Long lines:** in the six files only one line exceeds 1,000 characters, a 1,517-character row of the disposition map's table, here; Markdown cannot break a table row.
- **Untouched:** every file outside `CLAUDE.md`, `docs/ai-context/README.md`, `docs/ai-context/current-state.md`, this file and the three restructured files with their three history files is byte-identical to `3f22d83`, including the repository's `README.md`, the application, tests, workflows, examples, screenshots and release notes.

## Planned phases

The approved audit of roadmap step 9 designed six phases. Phases 1 to 3 are done; each later phase needs its own approval:

2. Done (PR #86): split `build-and-test.md` into `release-process.md`, `maintainer-procedures.md`, `history/releases.md` and the four `history/validation/` files, with stubs and a moved-sections table (§Phase 2 migration map).
3. Done: `decisions.md`, `technical-debt.md` and `open-questions.md` as indexes, their records in `history/` (the decisions table that GitHub showed as text after D15 fixed; §Phase 3 migration map).
4. Split `business-rules.md` (§13 to §15 and §16 to §23 in two files), `test-coverage.md` from the duplicated test tables, and the stale-content corrections the audit found.
5. Documentation-policy checks (a script with tests; wiring it into `ci.yml` is a workflow change).
6. Optional: data-safety enforcement with a Claude Code hook or deny rule.
