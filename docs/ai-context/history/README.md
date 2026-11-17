# History index

> **Historical record — not current authority.** Nothing indexed here is current authority. For the current state read `../current-state.md`, for routing and authority read `../README.md`, and for instructions read `CLAUDE.md` at the repository root. When a historical record and a current document differ, the current document applies.

This file indexes the historical records of the AI context and records how roadmap step 9 moves them. Load a record only when the router's route, an investigation or a discrepancy requires it (`../README.md` §History policy).

## Label policy

- A label is a global, permanent name for a record or a section: the lettered records `build-and-test.md` §A to §AZ; named sections such as §Release workflow; `business-rules.md` §1 to §23; decisions D1 to D58 and recommendations R1 to R8; TD-01 to TD-57; open questions #1 to #42; features F1 to F13; engineering rules ER-01 to ER-32; the requirements V011-1 to V011-3.
- A label never changes meaning and is never reused. A new record takes the next free label: §BA, §BB and so on; D59; R9; TD-58; #43; ER-33. TD-17 to TD-19 were never assigned and stay unused.
- When content moves to another file, its label moves with it, its old place keeps a pointer with the same heading text, and the move is recorded below with its proof. References in code comments and documents therefore keep resolving.

## Label index

Where each family of labels is defined now (after Phase 1), and where the approved audit plans to put its historical part. Later phases replace the "Planned" column with the actual location, one row per label where needed.

| Labels | Defined now | Kind | Planned (phase) |
|---|---|---|---|
| ER-01 to ER-32 | `../engineering-rules.md` | current | — |
| V011-1 to V011-3 | `../current-state.md` | current | — |
| `business-rules.md` §1 to §23 | `../business-rules.md` | current | §13 to §23 in two new files, numbering kept (4) |
| F1 to F13 | `../feature-inventory.md` | current | — |
| `build-and-test.md` §A to §AZ | `../build-and-test.md` | historical records inside a current file | `validation/` files, below (2) |
| `build-and-test.md` §Release history | `../build-and-test.md` | historical | `releases.md` (2) |
| `build-and-test.md` §Release workflow, §Release protections, §Branch protection | `../build-and-test.md` | current | `../release-process.md` (2) |
| `build-and-test.md` §Refreshing the README screenshots, §Refreshing the embedded database | `../build-and-test.md` | current procedures | `../maintainer-procedures.md` (2) |
| D1 to D58, R1 to R8 | `../decisions.md` | records, still in force unless superseded | `decision-log.md` and an index in `decisions.md` (3) |
| `decisions.md` §Roadmap | `../decisions.md` | steps 1 to 8 historical | `roadmap-and-status.md` (3) |
| TD-01 to TD-57 | `../technical-debt.md` | open items current; resolved items and the older dispositions historical | `technical-debt.md` here (3) |
| Open questions #1 to #42 | `../open-questions.md` | open items current; closed ones historical | `open-questions.md` here (3) |
| `CLAUDE.md` status narrative, baselines, replaced sections | `roadmap-and-status.md` | historical | — |

## Release index

Every release is immutable. Full records: `../build-and-test.md` §Release history and the sections named.

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

| Labels | Period | Now in | Planned file (Phase 2) |
|---|---|---|---|
| §A to §W | 2026-11-10 to `v0.1.0-beta.10` | `../build-and-test.md` | `validation/a-w_2026-11-10_to_beta.10.md` |
| §X to §AC | to `v0.1.0-preview.1` | `../build-and-test.md` | `validation/x-ac_preview.1.md` |
| §AD to §AX | to `v0.1.0-preview.2` | `../build-and-test.md` | `validation/ad-ax_preview.2.md` |
| §AY, §AZ | to `v0.1.0` | `../build-and-test.md` | `validation/ay-az_v0.1.0.md` |
| §Test-run side effects, the first CI run, "Local validation, 2026-11-10" | 2026-11-10 | `../build-and-test.md` | `validation/a-w_2026-11-10_to_beta.10.md` |

## Phase 1 migration map

Roadmap step 9, Phase 1 (2026-11-17 UTC) rewrote `CLAUDE.md` as the compact core. The source is `CLAUDE.md` at `3adcc8be47a5708ebd950ee8d5b3b8448ba6263d` (80,290 bytes, 105 lines, SHA-256 `2249ace878e76436b484be42bc667c82b9cec9293add17a92dcc5a5ad16c3ed9`). Each of its sections went where this table says; "byte" means the section's text is a byte-for-byte substring of the destination, "normalized" means it equals the destination after every run of whitespace is reduced to one space (only line breaks were added, at sentence boundaries, which Markdown renders as the same paragraph).

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

## Planned phases

The approved audit of roadmap step 9 designed six phases. Phase 1 is done; each later phase needs its own approval:

2. Split `build-and-test.md`: `release-process.md`, `maintainer-procedures.md`, `history/releases.md` and the four `history/validation/` files, with pointers and a moved-labels table.
3. `decisions.md`, `technical-debt.md` and `open-questions.md` as indexes, their records in `history/` (also fixing the decisions table that GitHub renders as text after D15).
4. Split `business-rules.md` (§13 to §15 and §16 to §23 in two files), `test-coverage.md` from the duplicated test tables, and the stale-content corrections the audit found.
5. Documentation-policy checks (a script with tests; wiring it into `ci.yml` is a workflow change).
6. Optional: data-safety enforcement with a Claude Code hook or deny rule.
