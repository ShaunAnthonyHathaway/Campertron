# History index

> **Historical record — not current authority.** Nothing indexed here is current authority. For the current state read `../current-state.md`, for routing and authority read `../README.md`, and for instructions read `CLAUDE.md` at the repository root. When a historical record and a current document differ, the current document applies.

This file indexes the historical records of the AI context and records how roadmap step 9 moves them. Load a record only when the router's route, an investigation or a discrepancy requires it (`../README.md` §History policy).

## Label policy

- A label is a global, permanent name for a record or a section: the lettered records `build-and-test.md` §A to §AZ and, since roadmap step 10, §BA to §BM in `validation/`; named sections such as §Release workflow; `business-rules.md` §1 to §28; decisions D1 to D67 and recommendations R1 to R8; TD-01 to TD-59; open questions #1 to #46; features F1 to F13; engineering rules ER-01 to ER-34; the requirements V011-1 to V011-3.
- A label never changes meaning and is never reused. A new record takes the next free label: §BN, §BO and so on; D68; R9; TD-60; #47; ER-35; business rule §29. TD-17 to TD-19 were never assigned and stay unused.
- When content moves to another file, its label moves with it, its old place keeps a pointer with the same heading text, and the move is recorded below with its proof. References in code comments and documents therefore keep resolving.

## Label index

Where each family of labels is defined now (after Phase 4), and where the approved audit plans to put its historical part. The last column names the phase that will move a family, or says that it has moved.

| Labels | Defined now | Kind | Planned (phase) |
|---|---|---|---|
| ER-01 to ER-34 | `../engineering-rules.md` | current | — |
| V011-1 to V011-3 | `../current-state.md` | current | — |
| `business-rules.md` §1 to §28 | §1 to §12 in `../business-rules.md`, with §Section index; §13 to §15, §27 (written there by D66) and §28 (by D67) in `../business-rules-settings.md`; §16 to §26 in `../business-rules-gui.md` (§24 to §26 written there by roadmap step 10, Phases A and B) | current | moved in Phase 4, numbering kept |
| F1 to F13 | `../feature-inventory.md` | current | — |
| `build-and-test.md` §A to §AZ | `validation/`, four files (§Validation record index) | historical | moved in Phase 2 |
| §BA to §BM (roadmap step 10 and after it) | `validation/ba_v0.1.1.md` (§Validation record index) | historical | — (written there) |
| `build-and-test.md` §Release history | `releases.md` | historical | moved in Phase 2 |
| `build-and-test.md` §Release workflow, §Release protections, §Branch protection | `../release-process.md` | current | moved in Phase 2 |
| `build-and-test.md` §Refreshing the README screenshots, §Refreshing the embedded database | `../maintainer-procedures.md` | current procedures | moved in Phase 2 |
| `build-and-test.md` §Required environment, §Commands (repo root), §Current validation baseline (new in Phase 2), §Test warnings, §Continuous integration, §Recommended verification after changes, §Moved sections | `../build-and-test.md` | current | — |
| D1 to D67, R1 to R8 | the index in `../decisions.md` (status, Changed by, current rule); the complete decision records and R2, R5 to R8 in `decision-log.md`; R1, R3, R4 in `../decisions.md` | index current; records historical | moved in Phase 3 |
| `decisions.md` §Roadmap | `decision-log.md` §Roadmap; a stub in `../decisions.md` | historical | moved in Phase 3 (planned for `roadmap-and-status.md`, which Phase 3 was not authorized to change) |
| TD-01 to TD-59 | the index and the open items' records in `../technical-debt.md`; the resolved and closed items, TD-30's record and the three earlier disposition layers in `technical-debt.md` here | index and open records current; the rest historical | moved in Phase 3 |
| Open questions #1 to #46 | the index and the open questions' records in `../open-questions.md`; the closed ones and the earlier disposition summary in `open-questions.md` here | index and open records current; the rest historical | moved in Phase 3 |
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
| `v0.1.1` | 2026-11-18 19:44:03 | `8d166bcd` → `7a6a114` | 44381376768 | 450471043 | §BI |

The lightweight 2024 tags `v0.1-beta1` and `v0.1-beta3` point to `e96bc7c`; their release entries were deleted on 2026-11-11 (§Release history, `v0.1.0-beta.4`).

## Validation record index

| Labels | Period | File (since Phase 2) |
|---|---|---|
| §A to §W, with "Warning breakdown (unique warnings)" after §B and "Test-run side effects (isolated dir)" after §T | 2026-11-10 to `v0.1.0-beta.10` | `validation/a-w_2026-11-10_to_beta.10.md` |
| The first CI runs and the release workflow's "Local validation, 2026-11-10" (§Local validation), at the end of that file | 2026-11-10 | `validation/a-w_2026-11-10_to_beta.10.md` |
| §X to §AC | to `v0.1.0-preview.1` | `validation/x-ac_preview.1.md` |
| §AD to §AX | to `v0.1.0-preview.2` | `validation/ad-ax_preview.2.md` |
| §AY, §AZ | to `v0.1.0` | `validation/ay-az_v0.1.0.md` |
| §BA to §BM | after `v0.1.0`: to `v0.1.1` (roadmap step 10, §BA to §BI) and after it (§BJ, §BK, §BL, §BM) | `validation/ba_v0.1.1.md` |

§BA to §BI are the first records after `v0.1.0`, in the file for that period, which roadmap step 10 created; §BJ (the email-mode repair) and §BK (its acceptance correction), both 2026-11-21, §BL (the SMTP password sources, 2026-11-22) and §BM (ntfy notifications, 2026-11-22) followed `v0.1.1` in the same file, and the next record takes §BN, in the same file while the period lasts (`../README.md` §Maintaining these documents).

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

Roadmap step 9, Phase 3 (PR #87, 2026-11-17 UTC) turned `decisions.md`, `technical-debt.md` and `open-questions.md` into compact current indexes and moved their complete historical records here. The sources, at `3f22d83f649a0a3a505b1113c4b245f7b6ce42b0`: `decisions.md` 171,845 bytes, 142 lines, SHA-256 `38217e4c919c32da9da74ca8bdf3dd5e2c0f021e633c0e8846ec3c3581606552`; `technical-debt.md` 98,850 bytes, 148 lines, `4161fb99eb59ac3b5e972041339a81e4215aa35546900e6a4906b9cab40a9607`; `open-questions.md` 36,161 bytes, 63 lines, `55ff4afbfbf96c4b749bf7cd4d6ee1251a80e707d74678c69b014ef53c25d7c3`.

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
| The missing-test notes after TD-56 | `technical-debt.md` lines 131–135 | `../technical-debt.md` §Missing tests, the heading R4 cites, restored | current | retained, 1 citation group pointed directly | rejoin after reversing the rewrites |
| §Migration risks | `technical-debt.md` lines 142–148 | `../technical-debt.md`, same heading | current | retained | rejoin |
| Title, legend, intro, the intro of §Found by the consolidated deferred-work audit, section headings, table headers | `technical-debt.md` lines 1, 3, 5, 104 and the `##` and `\| ID \|` lines | retained in `../technical-debt.md`; section headings in each file that holds an item of them; the headers became the labels | — | retained / converted | byte |
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

## Phase 4 migration map

Roadmap step 9, Phase 4 (PR #88, 2026-11-17 UTC) split `business-rules.md` by responsibility, made `test-coverage.md` the one current authority for the tests and corrected stale and broken current content. The sources, at `6ad651d20511b95429fdb01aaa0f8ca7d9f47c63`: `business-rules.md` 178,315 bytes, 453 lines, SHA-256 `2caa70f1577ac50372035ba536d91174fc2e0c8e46c075cb5d35c790714033f8`; `feature-inventory.md` 95,552 bytes, 296 lines, `a9e44e1c392c1092f4e9304ff2f9253f18c51f4ed2046a0a3a3702ea09ace07f`; `repository-map.md` 54,293 bytes, 173 lines, `0fcc16e0a74f046fa1c94eca0199fcbb57baa9f3a0978aac02920436a0229b9d`.

Treatments and proofs are those of the earlier phases: "byte" is a byte-for-byte substring of the destination at the commit that moved it, "rejoin" the destination block with its added line breaks turned back into spaces, both after reversing the logged count fixes. The settings and GUI files are new, so their lines over 1,000 characters were broken at sentence boundaries; `business-rules.md`, `feature-inventory.md` and `repository-map.md` predate Phase 1 and keep their long lines. Line numbers are those of the source file.

### business-rules.md

| Source lines | Bytes | SHA-256 | Unit | Destination | Treatment | Proof |
|---|---|---|---|---|---|---|
| 1–1 | 16 | `b6b92967dd5d4885…` | business-rules.md title | `../business-rules.md` | retained | byte |
| 3–5 | 258 | `48901fb1bc1c190f…` | business-rules.md legend and intro | `../business-rules.md` | retained | byte |
| 7–25 | 2,199 | `f8093f7c1b34f928…` | §1 | `../business-rules.md` | retained | byte |
| 27–41 | 4,005 | `e8f513f447cdb435…` | §2 | `../business-rules.md` | retained | byte |
| 43–67 | 5,489 | `c83af29f90b35f40…` | §3 | `../business-rules.md` | retained | byte |
| 69–73 | 1,297 | `4704eec825b91e44…` | §4 | `../business-rules.md` | retained | byte |
| 75–87 | 5,799 | `744ae6c7f631fb18…` | §5 | `../business-rules.md` | retained | byte |
| 89–102 | 6,122 | `73eaa1330fef2ed8…` | §6 | `../business-rules.md` | retained | byte |
| 104–116 | 2,592 | `eee37651e1fd2bde…` | §7 | `../business-rules.md` | retained | byte |
| 118–229 | 31,270 | `ce7253be3a914c01…` | §8 | `../business-rules.md` | retained | byte |
| 231–238 | 5,484 | `d07c8a42f2f7e9af…` | §9 | `../business-rules.md` | retained | byte |
| 240–245 | 2,246 | `cca458b1a19939b7…` | §10 | `../business-rules.md` | retained | byte |
| 247–251 | 1,141 | `5ecc8b74d15ad19b…` | §11 | `../business-rules.md` | retained | byte |
| 253–268 | 8,942 | `cf4cfd631f39f724…` | §12 | `../business-rules.md` | retained | byte |
| 270–282 | 8,150 | `fa57787fc400373b…` | §13 | `../business-rules-settings.md` | moved verbatim (wrapped) | rejoin, 5 breaks |
| 284–295 | 11,236 | `1047617c8eb0d6b1…` | §14 | `../business-rules-settings.md` | moved verbatim (wrapped) | rejoin, 6 breaks |
| 297–331 | 13,540 | `07cbcb3592cb9c01…` | §15 | `../business-rules-settings.md` | moved verbatim (wrapped) | rejoin, 6 breaks |
| 333–345 | 7,130 | `f6daf317ea0b37ca…` | §16 | `../business-rules-gui.md` | moved verbatim (wrapped) | rejoin, 1 break |
| 347–358 | 7,931 | `1b16ff8d2e5fc341…` | §17 | `../business-rules-gui.md` | moved verbatim (wrapped) | rejoin, 1 break |
| 360–369 | 7,180 | `37d26764e9217d60…` | §18 | `../business-rules-gui.md` | moved verbatim (wrapped) | rejoin, 3 breaks |
| 371–407 | 16,652 | `69fd5975a71a0328…` | §19 | `../business-rules-gui.md` | moved verbatim (wrapped) | rejoin, 6 breaks |
| 409–424 | 12,015 | `f3b43cfd4302e7ff…` | §20 | `../business-rules-gui.md` | moved verbatim (wrapped) | rejoin, 4 breaks |
| 426–433 | 5,718 | `f4a60908efd480e0…` | §21 | `../business-rules-gui.md` | moved verbatim (wrapped) | rejoin, 1 break |
| 435–439 | 3,389 | `88310c988ffa5915…` | §22 | `../business-rules-gui.md` | moved verbatim (wrapped) | rejoin, 2 breaks |
| 441–453 | 8,465 | `0543aa16f8e826b4…` | §23 | `../business-rules-gui.md` | moved verbatim (wrapped) | rejoin, 1 break |

New in `../business-rules.md`: the Authority paragraph, §Section index (every section, its file and a link) and a stub with the exact heading of each of §13 to §23. The numbers are global: §13 to §15 are in `../business-rules-settings.md` and §16 to §23 in `../business-rules-gui.md`, each with an Authority paragraph and a copy of the legend.

### The test inventory

| Source | Source lines | Content | Destination | Treatment | Proof |
|---|---|---|---|---|---|
| `feature-inventory.md` | 12 | the note on test names | `../test-coverage.md` §Coverage by feature | moved; a new note in its place | byte |
| `feature-inventory.md` | 26, 39, 63, 68, 82, 102, 107, 121, 131, 136, 140, 148, 155, 161, 168, 175, 182, 189, 196, 202, 209 | the 21 **Tests** entries of the features | `../test-coverage.md` §Coverage by feature, one subsection each with the feature's heading | moved; a pointer in each place | byte, one rejoin (1 break) |
| `feature-inventory.md` | 214–292 | the test table (49,155 bytes) | `../test-coverage.md` §What each test checks | moved | byte; 3 count fixes: `FilterMatchingTests`, `NavigationTests`, `StartupTests` |
| `feature-inventory.md` | 211–212 | §Test coverage summary: its heading and the totals paragraph | the heading stays as a stub; the totals are rebuilt from the discovery in `../test-coverage.md` §Totals | replaced; the paragraph's counts are historical (§AW, §AZ; the 1,073 tests of 2026-11-15 in `validation/ad-ax_…` §AL) | — |
| `feature-inventory.md` | 294–296 | the two paragraphs after the table | `../test-coverage.md` §What is not covered | moved | rejoin, 1 break |
| `repository-map.md` | 93, 95, 96, 100, 102, 109, 118, 119, 124, 129, 130, 131, 132, 143, 148, 149, 150, 151, 152, 153, 158 | the 21 test-list entries that add a fact: the shared fixtures and helpers, and classes without a table row or with a fact the row lacks | `../test-coverage.md` §Test files and fixtures | moved | byte; count fixes in lines 102, 118, 153 (`PopulateTests`, `AtomicRefreshTests`, `StartupTests`) |
| `repository-map.md` | 97, 98, 99, 101, 103, 104, 105, 106, 107, 108, 110, 111, 112, 113, 114, 115, 116, 117, 120, 121, 122, 123, 125, 126, 127, 128, 133, 134, 145, 146, 147, 154, 155, 156, 157 | 35 test-list entries | — | dropped: every code span each states is in its class's row of `../test-coverage.md` §What each test checks (checked by a scratch script) | — |
| `repository-map.md` | 90–92, 94, 137–142, 144 | the test projects' framework, packages, usings and infrastructure files | `../repository-map.md` | retained, with a pointer bullet per project | byte |
| `repository-map.md` | 164 | the release-policy test counts since R8 | `../repository-map.md` | replaced by a pointer to `../test-coverage.md`; the series is in the records it named (§X, §AH, §AI, §AK, §AM, §AN, §AO, §AR, §AW) | — |

`../test-coverage.md` §How the counts were derived to §Test classes are new, from a discovery at `6ad651d`: `dotnet test` of both projects with TRX loggers and an isolated `XDG_DATA_HOME`, matched class by class by a parse of the source's test attributes. Counts it corrected: `FilterMatchingTests` 12 tests, 29 cases → 13, 32; `NavigationTests` 7 → 8 tests; `StartupTests` 8 → 10 (D49 added two); `PopulateTests` "19 tests" → 16 tests, 19 cases; `AtomicRefreshTests` "52 tests" → 45 tests, 52 cases; `business-rules.md` §8's `LegacyAddressLayoutTests` "32 tests" (33 tests, 35 cases) became a pointer. Elsewhere a bare number after a class was already its number of cases.

### Corrections and pointers

After the moves, Phase 4 made 37 corrections (22 stale, 5 broken, 10 pointers), each checked against the code, the tests, the workflows or a current record, and pointed 61 `build-and-test.md` § citations at the file that holds the section since Phase 2, the label kept. A scratch script reversed every one and got the files of the commit before back exactly.

| File | Kind | Evidence |
|---|---|---|
| `business-rules.md` | broken | architecture.md has no §Search pipeline; the metadata and ResultsHtml are described in its §Console data flow (search) |
| `business-rules.md` | pointer | the procedure moved to maintainer-procedures.md in Phase 2 |
| `business-rules.md` | pointer | §N is in a-w, §AE in ad-ax (history/README.md §Validation record index) |
| `business-rules.md` | stale | discovery: LegacyAddressLayoutTests has 33 tests and 35 cases (32 was the count before D51); counts have one authority, test-coverage.md |
| `business-rules-settings.md` | stale | ci.yml line 140: name: Windows settings files (CM1 to CM3, CM5, CM6) |
| `test-coverage.md` | stale | ResultsTabTests.TheResultsTab_IsTheSecond_AndHasTheStartupsFolders |
| `test-coverage.md` | stale | MainWindowTests.MainWindow_HasTheFourTopLevelTabsInOrder |
| `test-coverage.md` | stale | StartupTests.ALateCompletion_GoesToTheDispatcherThatBeganTheAttempt_AndNoWorkerThreadBecomesTheUiThread and TheCompletion_NeverAsksForTheUiDispatcherOnTheWorkerThread (commit 1b16f91, D49) |
| `test-coverage.md` | stale | the same two StartupTests methods |
| `test-coverage.md` | stale | the same two StartupTests methods |
| `test-coverage.md` | broken | repository-map.md §CampertronGUITests no longer lists the tests; the moved entry is in this file |
| `test-coverage.md` | broken | the table moved into this file |
| `feature-inventory.md` | stale | MainWindow.SettingsTabIndex 2 since D50; business-rules §21 |
| `feature-inventory.md` | stale | MainWindow.SearchesTabIndex 0 since D50; NavigationTests.TheTopLevelTabs_AreSearchesResultsSettingsCampgrounds_AndSearchesIsShownFirst |
| `feature-inventory.md` | stale | ResultsTabTests.TheResultsTab_IsTheSecond_AndHasTheStartupsFolders asserts MainWindow.ResultsTabIndex == 1 |
| `repository-map.md` | stale | the Phase 4 path check (validate.py) found every named path |
| `repository-map.md` | stale | CampertronLibrary/function/RecDotOrg/search/SearchFilters.cs (and enum FilterField in it); nothing named SearchFilters under api/ |
| `repository-map.md` | stale | CampertronLibrary/function/RecDotOrg/search/SearchFilters.cs |
| `repository-map.md` | stale | ci.yml lines 140 and 210-222: the job name and its 12 test filters |
| `repository-map.md` | pointer | Phase 2 moved §Release workflow to release-process.md and §Release history to history/releases.md |
| `architecture.md` | stale | business-rules §23 "Which lists": all eight lists since D54; FilterSelectorField per list |
| `architecture.md` | stale | CampertronGUI/ViewModels/FilterChoiceSource.cs line 28 calls Read.FilterChoicesByCampground; FilterProbe.cs line 30 for the smoke test |
| `architecture.md` | broken | the heading is "1. Search config (...)"; business-rules.md §Section index |
| `console-to-gui-map.md` | broken | [A] was used in the --version row without a legend entry; build-and-test.md defines it |
| `console-to-gui-map.md` | stale | business-rules §11: [V for a pipe], verified 2026-11-11; \| more and > file in cmd were not run; #20 resolved for a pipe |
| `console-to-gui-map.md` | stale | current-state.md V011-1 names console-to-gui-map.md F6 as context |
| `console-to-gui-map.md` | stale | current-state.md V011-2 names console-to-gui-map.md F7 as context |
| `build-and-test.md` | stale | ci.yml lines 140 and 210-222 |
| `build-and-test.md` | stale | SearchFiltersTests and FilterSemanticsTests assert the eight lists (D51, technical-debt.md §Missing tests); TD-02 and TD-26 records |
| `release-process.md` | pointer | Phase 3 moved §Roadmap to history/decision-log.md |
| `release-process.md` | pointer | Phase 3 numbers the questions #N; #19's index row links its record |
| `decisions.md` | stale | history/decision-log.md D39 "with no production caller", D40 "with the CM1 editor (YamlDocument.Edit, SetScalar)", D41 "the CM1 editor now refuses that text"; business-rules §13 |
| `technical-debt.md` | pointer | Phase 3 moved §Roadmap |
| `technical-debt.md` | pointer | Phase 4 moved the test inventory |
| `open-questions.md` | pointer | Phase 3 moved the layers |
| `open-questions.md` | pointer | Phase 3 moved the layers |
| `engineering-rules.md` | pointer | navigation note for the pointer updates below |

Pointed `build-and-test.md` citations: `engineering-rules.md` 26, `business-rules.md` 8, `repository-map.md` 8, `business-rules-gui.md` 5, `test-coverage.md` 4, `console-to-gui-map.md` 4, `feature-inventory.md` 3, `architecture.md` 2, `business-rules-settings.md` 1. `../build-and-test.md` keeps its own bare labels, which resolve through its §Moved sections, and `../current-state.md` keeps "`build-and-test.md` §AZ" in PR #82's entry, which says what that pull request changed.

**Historical pointer policy.** A historical record keeps its citations as written: they are part of the record and still resolve, through the stubs of `../build-and-test.md`, `../decisions.md` and `../technical-debt.md`, the stub of `../feature-inventory.md` §Test coverage summary, the headings `../repository-map.md` keeps and `../business-rules.md` §Section index. Phase 4 changed no historical record; it changed only this index (navigation metadata and migration maps: PR #87 named in §Phase 3 migration map and §Planned phases, one plural there corrected, the planned phases numbered from 1, this map).

### Section citations

| Cited as | Cited from | Resolves to |
|---|---|---|
| `business-rules` §13 to §23 | 136 citations in 25 files outside the three business-rules files (code comments and history included) | `../business-rules.md` §Section index, then the section's file |
| `feature-inventory.md` §Test coverage | `validation/` records (2) | the stub with the same heading in `../feature-inventory.md`, then `../test-coverage.md` |
| `repository-map.md` §CampertronUnitTests | `../test-coverage.md` | the same heading in `../repository-map.md` (its §CampertronGUITests heading is kept too) |
| `architecture.md` §Search pipeline, `business-rules.md` §Search config | `business-rules.md` §6, `architecture.md` §Configuration | never resolved; corrected to `architecture.md` §Console data flow (search) and `business-rules.md` §1 |

## Phase 4 completeness accounting

- **Lines:** every non-blank line of `business-rules.md` (410) is retained or moved once; of `feature-inventory.md` and `repository-map.md` every line of the test inventories is moved, dropped as a duplicate or replaced as the tables above say, and every other line is unchanged before the corrections; both also gained an Authority paragraph, as `../business-rules.md` did.
- **Proofs** (scratch scripts reading the sources from Git, at each commit): the settings split 22 byte and 3 rejoin units (17 breaks); the GUI split 14 byte and 11 rejoin (36 breaks); the test inventory 57 byte and 13 rejoin (38 breaks) with 8 logged count fixes; the corrections reversed exactly. Each of §1 to §23 is in exactly one file, with one row in `../business-rules.md` §Section index.
- **Tests:** the 69 classes in `../test-coverage.md` §Test classes are exactly the discovered ones, each once; no current file but `../test-coverage.md` lists the test classes with their counts.
- **Long lines:** `../business-rules-settings.md` and `../business-rules-gui.md` have none; `../test-coverage.md` has 11, table rows moved unchanged from `feature-inventory.md` (lines 199, 200, 201, 207, 208, 214, 215, 218, 223, 224 and 229, 1,051 to 2,058 characters).
- **Untouched:** every file outside `docs/ai-context/` is byte-identical to `6ad651d`, including `CLAUDE.md`, the repository's `README.md`, the application, tests, workflows, examples, screenshots and release notes; in `history/` only this file changed.

## Phase 5 record

Roadmap step 9, Phase 5 (2026-11-17 UTC, PR #89) added deterministic documentation-policy checks with their own tests. It moved no section and changed no label; it ran nothing in CI and changed no workflow.

- **New files** in `.github/docs-policy/`: `check-docs.sh`, the checker (Bash and awk; local files and local Git only: no network, no GitHub or GHCR, no user-data folder); `test-check-docs.sh`, its tests (89 at the Phase 5 commit: a fixture repository with its own documents, test project and Git history must pass, each negative control must fail with its check, and the checker runs on this repository); `docs-policy.conf`, the registered values and exceptions, each with its reason; `README.md`, the usage, the checks, the severities, the exceptions, how to keep them current and the limits.
- **Registered exceptions:** the six current files that predate Phase 1 keep their 27 long lines; 13 table rows moved unchanged keep theirs (11 in `../test-coverage.md`, one in `technical-debt.md` here, one in `validation/a-w_2026-11-10_to_beta.10.md`), as the accounting of Phases 2 to 4 lists them; `../architecture.md` and `../console-to-gui-map.md` predate Phase 1 and have no Authority statement; TD-17 to TD-19 were never assigned; `RELEASE_NOTES.md` is the draft's notes file, not a repository file.
- **Known defects** (reported as warnings until a change that may edit the file corrects them): in `../console-to-gui-map.md` the pipe of the code span `| more` splits the `--version` row, so GitHub drops the end of its Notes cell (checked with GitHub's Markdown renderer); no task route names `../repository-map.md`, which the maintainer's routing decision has to settle.
- **Corrections** (each required by an established rule for the checks to pass):
  §Phase 3 migration map's row for the title, legend and headers of `technical-debt.md` had the code span `| ID |`, whose pipes split it into eight cells, so GitHub showed the wrong text under four of the six headings and dropped the treatment and the proof; the two pipes are escaped now (`\|`), no word changed, and GitHub shows the six cells (checked with its Markdown renderer).
  In `../technical-debt.md` §Missing tests the sentence with the CI counts from D36 to D41 read as current and differed from `../test-coverage.md` (914 tests, 1,234 cases, 20 skipped on Linux, 30 `Assert.Ignore` sites in 15 files); it moved unchanged to `technical-debt.md` §Missing tests: the counts from D36 to D41 here (592 bytes, SHA-256 `db83059150206dce638be71a53ee8c6b7fcd97662ea71f86120a959f01a3fc2c`, byte-identical to line 237 of `c06ff8d`), and a pointer replaced it.
  TD-36's counts (34 `AtomicRefreshTests`, 10 `ConfigFileReplaceTests`) stay: they are D26's evidence and equal the test attributes at its merge `1b7c92b`. The other counts of §Missing tests ("P2 added …", "P4 added …", GS1's 56) say what a change added and are accurate in that context.
- **Pointers:** the router's §Task routing (Documentation, when needed), §Release and audit reading policy and §Maintaining these documents; `../build-and-test.md` §Commands and §Recommended verification after changes.
- **Records:** `../current-state.md` gained Phase 4's commits (PR #88: head `68d624e`, merge `c06ff8ddccf8b27e9e15bd6f1ddc19798c9d86ab`; CI 44189318387 and, after the merge, 44190011314) and the Phase 5 entry; §Planned phases below marks Phase 5 done. Phase 5's merge commit is for the next AI-context change to record.
- **Validation:** on this repository the checker reports no error, 7 warnings (five size budgets over their recommendation and the two known defects) and 42 findings allowed by a registered exception; two runs, another directory, another locale, no network and no home folder give identical output. With a fresh test run (`--trx-dir`, 1,009 and 225 cases) and the release-policy run (`--policy-log`, 419) the counts of `../test-coverage.md` agree.
- **Untouched:** the application, its tests and test projects, the workflows, the release policy, examples, screenshots, release notes and the repository's `README.md`.

Notes added on 2026-11-18 UTC by the Phase 5 follow-up (PR #90); the record above is unchanged:
- **Merged:** PR #89 (`1b0c173`, merged as `6df374c0203f84b4f607d84a0849bed87a97dc25`) on 2026-11-17 at 23:26:51 UTC; CI 44198880425 and, after the merge, 44199443431, both passed on attempt 1. It added the documentation-policy checker and its tests, which run locally only: no workflow integration, no application or publication change.
- **The known defects resolved:** the follow-up escaped the pipe of the code span `| more` in the `--version` row of `../console-to-gui-map.md` (GitHub's renderer then shows the row's three cells with the whole Notes text) and added the route "Repository navigation and code-location lookup" to the router for `../repository-map.md`; with both corrected the checker reported each registered defect as no longer occurring, and both records were removed from `.github/docs-policy/docs-policy.conf`. It also added the documentation-policy suite (89 tests at `6df374c`) to `../test-coverage.md`.

Notes added on 2026-11-18 UTC by the CI integration of the documentation-policy checks (PR #91); the record and the notes above are unchanged:
- **The follow-up merged:** PR #90 (`93e8f77`, merged as `3891d17379e9b0497ef58177e0d678797d80c84e`) on 2026-11-18 at 00:49:34 UTC; CI 44210559408 and, after the merge, 44210977723, both passed on attempt 1. Documentation only: it completed the Phase 5 follow-up, with no workflow integration of the checks and no application or publication change.
- **In CI:** PR #91, separately approved, runs `test-check-docs.sh` and `check-docs.sh` in `ci.yml`'s required job, `Restore, build, test, vulnerability scan`, before the build, so no ruleset change was needed. That job's checkout gained `fetch-depth: 0` (the full history and the tags; about 4 seconds more, as `release.yml`'s `verify` job measured with the same checkout); a step before the checks names the awk and stops on a shallow checkout or one without tags. Exit 0 and 3 (warnings only, with a notice) pass; 1, 2 and any other code fail the job with that code (`.github/docs-policy/README.md` §In CI).
- **The runner's awk:** `ubuntu-latest` was Ubuntu 26.04, image 20261111.196.1 (the job log of 44210977723). Its published package list does not name GNU awk, so mawk 1.3.4 20260129, the `awk` of a plain Ubuntu 26.04 image, was tested first: with that binary the checker's output on this repository was byte-identical to GNU awk 5.4's (exit 3: no error, five size-budget warnings, 42 allowed) and all 89 tests passed. The first pull-request run, 44216149502, then showed that the runner has `/usr/bin/gawk`, GNU Awk 5.3.2, which the checker prefers; the checker and its tests passed with it there, so the job uses the runner's awk and installs nothing, and no portability correction was needed. A comment of `ci.yml` and the message of its first commit had said that the runner has only mawk; a later commit of the PR corrected the comment.
- **The pull-request run 44216149502:** the checkout fetched every branch and the 12 tags (566 commits in the history of HEAD; 5 seconds); the checker's tests gave 97 passed, 0 failed and one `SKIP` (no `unshare -rn` on the runner); the checker gave exit 3 (no error, the five size-budget warnings, 42 allowed) with a notice; every other step and the Windows job passed.
- **Tests:** nine tests read the CI steps from `ci.yml` and run them with a stub checker (exit 0 and 3 pass, 1, 2 and 7 fail with their own code), check the checkout and the order of the steps, and run the environment step on a full, a shallow and a tagless clone; 98 tests in all, under GNU awk and mawk. Five negative controls in `ci.yml` (errors accepted, an unexpected code turned into 1, the shallow checkout, `continue-on-error`, no shallow-clone guard) each failed exactly one of them. `release.yml` and the application are unchanged. Its merge commit is for the next AI-context change to record.

Notes added on 2026-11-18 UTC by the closure of roadmap step 9 (PR #92); the record and the notes above are unchanged:
- **The CI integration merged:** PR #91 (`6b64375`, merged as `d181db8569e96c4b06358cdad2b88e8a5eb126a6`) on 2026-11-18 at 01:40:34 UTC, from the branch `ci/docs-policy-integration`, which was then deleted. Its runs: 44216149502 on `9ba087b`, 44217125135 on the tested head `6b64375` and, after the merge, 44217611341, all passed on attempt 1, with no manual dispatch or rerun and no Release workflow run. It completed the approved CI-integration follow-up to Phase 5; its scope was `ci.yml`, the checker's tests and the documentation, with no ruleset, `release.yml`, application or publication change.
- **The final CI evidence:** in 44217125135 and 44217611341 the checkout had the 12 tags and 568 commits; the environment step found `/usr/bin/gawk`, GNU Awk 5.3.2; the checker's tests gave 97 passed, 0 failed and one skipped (the no-network test needs `unshare -rn`, which the runner does not allow); the checker gave exit 3 (no error, the five recommendation-only size warnings, 42 allowed) with a notice, and the job passed. Locally the checker and all 98 tests had passed under GNU awk 5.4 and under mawk 1.3.4 20260129, the `awk` of a plain Ubuntu 26.04 image.
- **Two wrong commit messages:** `6c11020`'s message says that Ubuntu 26.04 has only mawk, and `9ba087b`'s calls it "the runner's mawk"; both are wrong, as the runs above show `/usr/bin/gawk`, GNU Awk 5.3.2. `dc244c0` corrected the comment in `ci.yml` and `6b64375` the current documents. The messages stay as they are, immutable Git history (no rewrite, rebase or force push); for the runner's environment the current documents and the CI logs are the authority.
- **Two reading deviations in PR #91:** that task required complete reading before the work, but ER-24, ER-27 and ER-32 were read completely only after the merge, and `decisions.md` was not read during the task. Assessed afterwards: the work complies with all three rules (the workflow and the documentation changes were in separate commits, the SDK and package guards are unchanged, and it merged through its pull request with a merge commit); no decision record was required or created; no defect in the implementation came from either deviation. They are process deviations, not application or publication defects; later work follows the required reading order.
- **Step 9 closed:** the maintainer closed roadmap step 9 with Phase 6 deferred (§Planned phases); `../current-state.md` §Roadmap position makes step 10, `v0.1.1`, the next stage, which has not started. The stale comment above `ci.yml`'s Test step is a small future workflow-documentation correction (`../build-and-test.md` §Continuous integration), not a phase or a blocker.

Notes added on 2026-11-18 UTC by roadmap step 10, Phase T (PR #93); the record and the notes above are unchanged:
- **The closure merged:** PR #92 (`fe0ddc9`, merged as `5ad87b93cf50838aeba1537cb96f2912d2bb1b3f`) on 2026-11-18 at 02:04:05 UTC, from the branch `docs/ai-context-step9-closure`, which was then deleted. CI 44219948490 on its head and, after the merge, 44220510408, both passed on attempt 1. It changed four AI-context documents (the router, `../build-and-test.md`, `../current-state.md` and this file): no application, workflow, test, README, screenshot, release-note, dependency or publication change, and no Release workflow run.
- **Step 10 began** with a read-only design investigation of `v0.1.1` (`validation/ba_v0.1.1.md` §BA); its plan and the maintainer's decisions on it are in `../current-state.md`.
- **Phase T, its first phase,** resolved TD-57 by D59 and corrected the stale test comments of `ci.yml` and `release.yml` (`validation/ba_v0.1.1.md` §BB); TD-57's record moved to `technical-debt.md` here, its index row in `../technical-debt.md` keeping its disposition.

Notes added on 2026-11-18 UTC by roadmap step 10, Phase A (V011-2); the record and the notes above are unchanged:
- **Phase T merged:** PR #93 (head `fdf3a87`, merged as `90ccebc2b7b1b39c02ed13cc5690016509bc6433`) on 2026-11-18 at 03:51:25 UTC, from the branch `ci/td57-floating-image-tags`, which was then deleted. Its final CI run 44232403532 and its automatic Release dry run 44232403401 on that head, and CI 44233309570 after the merge, all passed on attempt 1, with no manual dispatch or rerun. The dry run published nothing: no tag, release, asset, attestation or image changed, and GHCR's `0.1.0`, `0.1` and `latest` still name `0.1.0`'s image. TD-57 is resolved (D59); the first real GHCR run of the forward move of the floating tags is still ahead, a publication-readiness risk (`../current-state.md` §Open risks).
- **Its commits, checked against the Git history** (`git show --stat`, and `test-release-policy.sh` run at each commit in a temporary worktree): `2057585` the documentation of PR #92 and §BA; `059fc89` the content-addressed stub registry of `test-release-policy.sh` and its self-test (419 → 420 cases); `9b7a859` the publication change (`release-policy.sh`, `release.yml`'s push step) together with its 45 tests (420 → 465); `a50d5df` the workflow comments; `a7ad316` and `fdf3a87` the documentation. The 45 publication tests are in the implementation's commit; the stub registry they use is in the commit before it, so "tests and implementation in one commit" holds for those 45 tests only.
- **Phase A began** on 2026-11-18, approved by the maintainer after the Phase T report: V011-2, Settings > System > Current configuration, on the branch `feat/v011-2-current-configuration`.
- **Phase A** (PR #94) implemented V011-2 by D60 (`validation/ba_v0.1.1.md` §BC): the library's `CurrentConfiguration`, shared by the console's View Config (its output byte for byte unchanged) and the GUI's new card; business rule §24 in `../business-rules-gui.md` and its row in `../business-rules.md` §Section index; the `system-tab` smoke line; the amended ER-06, ER-09 and ER-13. No label moved; the README and its screenshots are unchanged (Phase D).

Notes added on 2026-11-18 UTC by roadmap step 10, Phase B (V011-1); the record and the notes above are unchanged:
- **Phase A merged:** PR #94 (tested head `233477c`, merged as `739779f973628c9e6d1ab7fb88313947c4e746c6`, tree `f34318b5761e1aefa0ce737fcf141ce7f08414e6`) on 2026-11-18 at 05:00:43 UTC, from the branch `feat/v011-2-current-configuration`, which was then deleted. Its final CI run 44240211673 and its automatic Release dry run 44240211644 (run 63: 27 jobs passed, the image push and the GitHub release skipped) on that head, and CI 44241224492 after the merge, all passed on attempt 1, with no manual dispatch or rerun. V011-2 is implemented (D60, business rule §24); nothing was published, and no tag, release, asset, attestation or image changed.
- **Phase B began** on 2026-11-18, approved by the maintainer after the Phase A report: V011-1, Settings > System > Refresh database, on the branch `feat/v011-1-refresh-database` from `739779f`. Its isolated real-network RIDB refresh needs a separate approval immediately before it runs.
- **Phase B** (PR #95) implemented V011-1 by D61 (`validation/ba_v0.1.1.md` §BD): the library's `RidbRefreshService`, whose adapter is the console's refresh (its output unchanged but for the archive's path), and the GUI's Settings > System > Campground data; business rule §25 in `../business-rules-gui.md` and its row in `../business-rules.md` §Section index; the `system-tab` smoke line; the amended ER-05, ER-06, ER-09, ER-15 and ER-19. The README and its screenshots are unchanged (Phase D).
- **TD-58 moved** to `technical-debt.md` here, resolved by D61, as TD-57 did for D59: its index row in `../technical-debt.md` keeps the disposition and links the record, and its section there says "Resolved or closed items of this section: TD-58". Proof: the record at `739779f` (`../technical-debt.md` §TD-58), without its label `**Issue:** `, is a byte-for-byte substring of the moved record after "Original entry, kept for history:" (1,265 bytes, SHA-256 `fed381bdbad283cf88a62a2dac9256f202bdb14a13ffd1c2a371ea8a4bcc6a98`); the resolution precedes it, as in TD-57's.
- **A stale sentence corrected:** the authority paragraph of `../business-rules-gui.md` still said that V011-3's final wording was not decided; the maintainer decided it on 2026-11-18 (`../current-state.md` V011-3).
- **The automatic restart (D62) superseded the manual restart before Phase B was pushed.** At the real-network approval gate (2026-11-18) the maintainer required that Campertron restart itself after a successful refresh: a one-time token-protected handoff, the new process in the old window's place and size at Settings > System, no remembered window position, a manual restart only as the fallback. The approved V011-1 design said the user restarts Campertron; `../current-state.md` V011-1 keeps that sentence struck through with the superseding decision beside it, and D61's record is unchanged, D62's record says what it replaced.
  Business rule §26 in `../business-rules-gui.md` and its row in `../business-rules.md` §Section index; the amended ER-06 and ER-09; `validation/ba_v0.1.1.md` §BD's addendum records its validation. No label moved; the real-network refresh still needs a renewed approval.
- **The isolated real-network refresh ran** on 2026-11-18, once, after the maintainer's renewed approval (the GUI path with the automatic restart; `validation/ba_v0.1.1.md` §BD): the real export installed and Campertron restarted as designed; open question #43 is partly answered by its measured disk peak.

Notes added on 2026-11-18 UTC by roadmap step 10, Phase C (V011-3); the record and the notes above are unchanged:
- **Phase B merged:** PR #95 (tested head `34a8322`, merged as `c4aebb05ffb1997e26e568def0aa4b3693260b76`, tree `6f82a328a4f7f44428a953023c45a04c67dd2e5f`) on 2026-11-18 at 15:05:29 UTC, from the branch `feat/v011-1-refresh-database`, which was then deleted. Its final CI run 44328265279 and its automatic Release dry run 44328265266 on that head, and CI 44331578047 after the merge, all passed on attempt 1, with no manual dispatch or rerun. V011-1 and its automatic restart are implemented (D61, D62, business rules §25 and §26); the one isolated real-network refresh had run before the merge (§BD). Nothing was published, and no tag, release, asset, attestation or image changed.
- **Phase C began** on 2026-11-18, approved by the maintainer after the Phase B report: V011-3, the coordinated correction of the `autoRun` and "startup" wording, with TD-56's review, on the branch `feat/v011-3-autorun-terminology` from `c4aebb0`. The README and its screenshots stay for Phase D.
- **Phase C** (PR #96) implemented V011-3 by D63 (`validation/ba_v0.1.1.md` §BE): the GUI says "Enabled (autoRun)" and "Not enabled", explained where they are shown, in the Searches and Results tabs, the validation's diagnostics and New search's message; `RunsAtStart` became `IsEnabled` and `IsEnabledAndUsable`; the changed rules in `../business-rules-settings.md` §15 and `../business-rules-gui.md` §18 to §20 (no new section); the amended ER-14 and ER-19. The README and its screenshots are unchanged (Phase D; §BE lists what it changes).
- **TD-56 moved** to `technical-debt.md` here, resolved by D63, as TD-57 and TD-58 did: its index row in `../technical-debt.md` keeps the disposition and links the record, and its section there says "Resolved or closed items of this section: TD-56". Proof: the record at `c4aebb0` (`../technical-debt.md` §TD-56), without its label `**Issue:** `, is a byte-for-byte substring of the moved record, starting right after "Original entry, kept for history:" (2,262 bytes, SHA-256 `1f6d20fbc3659fef956949b9fe211def3d31607a425b1f825a8dc005122998a7`); the resolution precedes it.

Notes added on 2026-11-18 UTC by roadmap step 10, Phase D (the README and its screenshots); the record and the notes above are unchanged:
- **Phase C merged:** PR #96 (tested head `22a58a3`, merged as `b9b9d59ef7bbdea3cf21c5d0e02807ac66e18dcf`, tree `16147bb30df34df166acc9cde1167a27c974e990`) on 2026-11-18 at 16:14:28 UTC, from the branch `feat/v011-3-autorun-terminology`, which was then deleted. Its final CI run 44341600763 and its automatic Release dry run 44341600938 (27 jobs passed, the image push and the GitHub release skipped) on that head, and CI 44344905220 after the merge, all passed on attempt 1, with no manual dispatch or rerun; a first pair on the same head (44341598539, 44341598531) was canceled by the workflows' concurrency when the second push arrived. V011-3 is implemented and TD-56 resolved (D63); nothing was published, and no tag, release, asset, attestation or image changed.
- **Phase D began** on 2026-11-18, approved by the maintainer after the Phase C report: the repository's `README.md` and its screenshots (`docs/ss9.png`, `ss11.png`, `ss12.png`) for V011-1 to V011-3, on the branch `docs/v011-readme-screenshots` from `b9b9d59`; documentation and screenshots only.
- **Phase D** (PR #97) brought `README.md` and the screenshots `docs/ss9.png`, `ss11.png` and `ss12.png` up to date with V011-1 to V011-3 (`validation/ba_v0.1.1.md` §BF): no decision record (README synchronization decides nothing about the product); the screenshot procedure in `../maintainer-procedures.md` gained the Phase D capture and the framing of `ss9` and `ss12`. No label moved.

Notes added on 2026-11-18 UTC by roadmap step 10's curated `v0.1.1` notes; the record and the notes above are unchanged:
- **Phase D merged:** PR #97 (tested head `0ede10d`, merged as `fcd8edcfc5f2c855c40e12b7930cc9f8e3be1d9e`, tree `c5e2c83ad3c66fdde8ea9830ee561c157f73d8f6`) on 2026-11-18 at 16:54:13 UTC, from the branch `docs/v011-readme-screenshots`, which was then deleted. Its CI runs 44350705643 (on `ade75f1`) and 44351681085 (on the final head) and CI 44352585189 after the merge passed on attempt 1, with no manual dispatch or rerun; no Release dry run ran, as `release.yml`'s pull-request filter names none of its paths. It changed the README and three screenshots and the AI-context records only; nothing was published.
- **The curated notes began** on 2026-11-18, approved by the maintainer after the Phase D report: the curated fragment for `v0.1.1` in `.github/release-notes/`, on the branch `docs/v011-release-notes` from `fcd8edc`; no readiness check, candidate or publication.
- **The curated notes** (PR #98) added `.github/release-notes/v0.1.1.md` (`validation/ba_v0.1.1.md` §BG): no decision record (the notes describe decisions already made). They were not the last content pull request before the tag, as `../release-process.md` "Release procedure" step 2 recommends, because the maintainer's plan puts the readiness check after them; `../current-state.md` records what the readiness check confirms. No label moved.

Notes added on 2026-11-18 UTC by roadmap step 10's `v0.1.1` readiness check; the record and the notes above are unchanged:
- **The curated notes merged:** PR #98 (tested head `62fc800`, merged as `ef72d5fe2f8dd9db5c3299ac4c09a298e0921b4a`, tree `ee403a19e1978adc53ccd0f9f2936d2ca01a4f77`) on 2026-11-18 at 17:20:13 UTC, from the branch `docs/v011-release-notes`, which was then deleted. Its CI runs 44355899118 and 44356735475 and CI 44357533288 after the merge passed on attempt 1, each composing the same body (13,475 bytes, `bb0d02fd…`); no Release dry run ran (the path filter), and nothing was published.
- **The readiness check began** on 2026-11-18, approved by the maintainer after the notes report, on the branch `docs/v011-readiness` from `ef72d5f`. PR #98 deviated from the release procedure's step 2 (a notes-only, last content pull request); `../current-state.md` records the deviation and that the readiness pull request is the last content pull request before the candidate.
- **The readiness check** (PR #99; `validation/ba_v0.1.1.md` §BH) found no blocker and changed the README's readiness wording only: no decision record (it decides nothing about the product). Its merge commit is the commit of the candidate and the tag; its own record waits for the documentation pull request after the publication. No label moved.

Notes added on 2026-11-18 UTC by the documentation pull request after the publication of `v0.1.1`; the record and the notes above are unchanged:
- **The readiness check merged:** PR #99 (tested head `bcf5fea`, merged as `7a6a11427b56116617dd5844b8a65a70eda7daba`, tree `859e2aa2c54d1ba4e5a385eda95b2931f1e2e39e`) on 2026-11-18 at 18:01:21 UTC, from the branch `docs/v011-readiness`, which was then deleted. CI 44363379165, 44364226390 and, after the merge, 44365095331 passed on attempt 1; no Release dry run ran. It was the last content pull request before the tag.
- **`v0.1.1` was released:** the candidate (Release run #68, 44369739534), tested and approved by the maintainer, the pre-tag dry run (#69, 44375549514) and the tag run (#70, 44381376768) all ran on `7a6a114`, on attempt 1; the release is published (§Release index, `releases.md`, `validation/ba_v0.1.1.md` §BI).
- **A move:** `../current-state.md` §Changes on `master` since `v0.1.0` (its body, 21,499 bytes, SHA-256 `a9bff1ced7d496ad8c67907db48ed710a944e6e368fffbfe5483d6571c858cac`, at the commit before the move) moved to `validation/ba_v0.1.1.md` §BI, byte for byte, as the router asks after a release; the current file keeps a section of the same name that points there, and its list starts again with §Changes on `master` since `v0.1.1`. No label moved.

Notes added on 2026-11-21 UTC by the email-mode repair (open question #40); the record and the notes above are unchanged:
- **The documentation after the publication merged:** PR #100 (`a5acd37`, merged as `60bcf820cb27f0f9cb52632322e95ce9cc912d57`, tree `10293442c383f7d5af3ca402b01df6330d7c5667`) on 2026-11-18 at 20:26:39 UTC, from the branch `docs/v011-post-publication`, which was then deleted. CI 44389657514 on `4188788`, 44390447241 on the tested head and, after the merge, 44391086336 passed on attempt 1; no Release dry run ran (the path filter), and no workflow run, dispatch or rerun followed it before this repair. Documentation only: the README's version examples, the release records and `../current-state.md` reset; no application, test, workflow, release-note, tag, release or image change.
- **The repair began** on 2026-11-21, approved by the maintainer: email mode must not enter its periodic loop when its start produced no enabled and usable search, on the branch `fix/email-mode-no-search-to-check` from `60bcf82`.
- **The repair** (`validation/ba_v0.1.1.md` §BJ) is decision D64 (`decision-log.md`): email mode starts its checks only with a search to check, otherwise one error and exit code 1. It found TD-59 (`../technical-debt.md`) and open question #44 (`../open-questions.md`), recorded and not changed.
- **#40 moved** to `open-questions.md` here, answered by D64, in a new section at its end (§Answered by the email-mode repair); its index row in `../open-questions.md` keeps the answer and links the record, its group there lists it as answered, and the group's line here has a dated note. Proof: the record at `60bcf82` (`../open-questions.md` §#40, its two lines without the heading) is a byte-for-byte substring of the moved record, starting right after "Original record, kept for history:" (1,745 bytes, SHA-256 `702e7fca80fb3a945a97d552649adfff252db5a58cd7591a19e1a0e541c6173c`); the answer precedes it.
- **The pull request:** PR #101 from the branch, head `ab31fc3` (five commits: the record of PR #100, the reproduction tests, the repair, §BJ and the documentation). CI 44958456562 passed on attempt 1. The automatic Release dry run #71 (44958456672) passed on attempt 1 (27 jobs, the GHCR and release jobs skipped): the release workflow's pull-request path filter includes `CampertronGUI/**`, which the repair's comment in `SearchesViewModel.cs` matches; the expectation before the push, that no dry run would run, was wrong. Nothing was dispatched, rerun or published. Its merge is recorded by the next documentation change.

Notes added on 2026-11-21 UTC by the email-mode repair's acceptance correction; the record and the notes above are unchanged:
- **The repair merged:** PR #101 (`bf532d5`, merged as `ff7baeca117c6beee0cab743479283135c90309b`, tree `bb3ec977a33714d16979498069484915b558d73f`) on 2026-11-21 at 23:01:32 UTC, from the branch `fix/email-mode-no-search-to-check`, which was then deleted. CI #212 (44959078210) and the automatic Release dry run #72 (44959078182) on the tested head and CI #213 (44959708026) after the merge passed on attempt 1; nothing was dispatched, rerun or published.
- **The acceptance correction began** on 2026-11-21, approved by the maintainer after the repair's report, on the branch `fix/email-mode-no-example-search` from `ff7baec`: whether email mode creates the example search in a settings folder without a search file, and whether an `outputTo: UnitTest` start can stand for a zero-search email mode.
- **The correction** (`validation/ba_v0.1.1.md` §BK) is decision D65 (`decision-log.md`): email mode created the example search in a folder without a search file and checked it; it now creates nothing and stops with D64's error. An `outputTo: UnitTest` start is not email mode (it searches nothing and ends with 0), kept and documented. No label moved.
- **The pull request:** PR #102 from the branch, head `a43b988` (four commits: the record of PR #101's merge, the tests, the correction, the documentation). CI #214 (44965387452) and the automatic Release dry run #73 (44965387405, 27 jobs, the publication jobs skipped; the path filter's `CampertronGUI/**`) passed on attempt 1. Nothing was dispatched, rerun or published. Its merge is recorded by the next documentation change.

Notes added on 2026-11-22 UTC by the SMTP password sources; the record and the notes above are unchanged:
- **The correction merged:** PR #102 (`6a31c86`, merged as `d7b1059cefe0893a4167bee4d96abd9147a50f74`, tree `09379e9910c606dde49c4ef787a26f0865a67080`) on 2026-11-22 at 00:26:45 UTC, from the branch `fix/email-mode-no-example-search`, which was then deleted. CI #215 (44966063569) and the automatic Release dry run #74 (44966063566) on the tested head and CI #216 (44966650250) after the merge passed on attempt 1; nothing was dispatched, rerun or published.
- **The SMTP password sources began** on 2026-11-22, approved by the maintainer after the correction's report, on the branch `feat/smtp-password-sources` from `d7b1059`: a password file named in `Email.yaml` and an environment variable before the plaintext `smtpPassword`, without a silent fallback.
- **The SMTP password sources** (`validation/ba_v0.1.1.md` §BL) are decision D66 (`decision-log.md`): the file `smtpPasswordFile` names, then `CAMPERTRON_EMAIL_PASSWORD`, then `smtpPassword`, with no fallback from a configured source that can't be used; business rule §27 in `../business-rules-settings.md` with its row in `../business-rules.md` §Section index; engineering rule ER-33 with its `CLAUDE.md` line and routes; open question #45. TD-49 stays open, its record extended. No label moved.
- **The pull request:** PR #103 from the branch, head `88deed9` (eight commits: the record of PR #102's merge, the resolver, its use by email mode, the send, the report and the GUI, the Windows CI job, a test from a negative control, the README, the documentation). CI #217 (44971935456) and the automatic Release dry run #75 (44971935436, 27 jobs, the publication jobs skipped; the path filter's `CampertronGUI/**`) passed on attempt 1. Nothing was dispatched, rerun or published. Its merge is recorded by the next documentation change.

Notes added on 2026-11-22 UTC by ntfy notifications; the record and the notes above are unchanged:
- **The SMTP password sources merged:** PR #103 (`6022669`, merged as `8a23ff1c68101c151076f77f0c4f4e5daac7fb04`, tree `7b23fd3a7e34e345898c66bce20a209adcbc1b26`) on 2026-11-22 at 01:50:07 UTC, from the branch `feat/smtp-password-sources`, which was then deleted. CI #218 (44972602547) and the automatic Release dry run #76 (44972602549) on the tested head and CI #219 (44973292423) after the merge passed on attempt 1; nothing was dispatched, rerun or published.
- **ntfy notifications began** on 2026-11-22, approved by the maintainer after the SMTP password report, on the branch `feat/ntfy-notifications` from `8a23ff1`: ntfy as the first notification provider besides email.
- **ntfy notifications** (`validation/ba_v0.1.1.md` §BM) are decision D67 (`decision-log.md`): the optional `Ntfy.yaml`, `outputTo: Ntfy`, one check for every enabled provider with each independent, `Camp.History` once any provider delivered (the maintainer's decision of 2026-11-22), the token from a file or `CAMPERTRON_NTFY_TOKEN` without a fallback, the topic as a secret; business rule §28 in `../business-rules-settings.md` with its row in `../business-rules.md` §Section index; engineering rule ER-34 with its `CLAUDE.md` line and routes, ER-02, ER-03, ER-13, ER-17 and ER-33 amended; open question #46. No label moved.
- **The pull request:** PR #104 from the branch, head `5654d23` (thirteen commits: the record of PR #103's merge, the shared secret-file reader, `Ntfy.yaml` and the token, the transport and the message, the monitoring's providers and the history, the console, the GUI and the redaction, two test assertions, the Windows CI job, two wording corrections from the process runs, the README, a test pin, the documentation). CI #220 (44979295786) and the automatic Release dry run #77 (44979295799, 27 jobs, the publication jobs skipped; the path filter's `CampertronGUI/**`) passed on attempt 1. Nothing was dispatched, rerun or published. Its merge is recorded by the next documentation change.

## Planned phases

The approved audit of roadmap step 9 designed six phases. Phases 1 to 5 are done and Phase 6 is deferred; a later phase needs its own approval:

1. Done (PR #84): the mandatory core: `CLAUDE.md` rewritten, the router `README.md`, `current-state.md`, `engineering-rules.md` and `history/` (§Phase 1 migration map).
2. Done (PR #86): split `build-and-test.md` into `release-process.md`, `maintainer-procedures.md`, `history/releases.md` and the four `history/validation/` files, with stubs and a moved-sections table (§Phase 2 migration map).
3. Done (PR #87): `decisions.md`, `technical-debt.md` and `open-questions.md` as indexes, their records in `history/` (the decisions table that GitHub showed as text after D15 fixed; §Phase 3 migration map).
4. Done (PR #88): split `business-rules.md` (§13 to §15 and §16 to §23 in two files), `test-coverage.md` from the duplicated test tables, and the stale-content corrections the audit found (§Phase 4 migration map).
5. Done (PR #89): the documentation-policy checks, `.github/docs-policy/check-docs.sh` with its tests, run locally; wiring them into `ci.yml` is a separate workflow change that needs its own approval (§Phase 5 record). Since PR #91 CI's required job runs them (§Phase 5 record, the notes of PR #91).
6. Optional: data-safety enforcement with a Claude Code hook or deny rule. Deferred on 2026-11-18 by the maintainer (PR #92): not begun, not required before `v0.1.1` and not rejected; stronger live enforcement for real user-data paths may be considered separately later, with its own approval, if enforcement at tool-call time proves useful.

Roadmap step 9 is complete (closed on 2026-11-18, PR #92): Phases 1 to 5 and the CI integration of Phase 5's checks (PR #91) are done, and Phase 6 is deferred. Step 10, `v0.1.1`, is the next roadmap stage (`../current-state.md` §Roadmap position).
