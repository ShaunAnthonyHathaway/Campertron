# AI context: router and rules

Authority: current. This file says what to read for each task, which document is the authority for each subject, how history is kept and how these documents are maintained. `CLAUDE.md` imports it together with `current-state.md`.

## Mandatory reading order

1. The mandatory core: `CLAUDE.md` (loaded automatically), this file and `current-state.md` (imported by `CLAUDE.md`; if either is not in your context, read it completely first).
2. The documents and sections the routing table below requires for the task, and only those.
3. The code. Code wins over the documentation; record any difference you find.
4. History only when the route, an investigation or a discrepancy requires it (§History policy).

## Authority map

| Subject | Current authority |
|---|---|
| Instructions, approval boundaries, autonomous work, data safety, conventions | `CLAUDE.md` |
| Repository and release state, changes since the latest release, roadmap position, authorized work, `v0.1.1` requirements (V011-1 to V011-3), blockers, risks, boundaries | `current-state.md` |
| What to read; how the documents are kept | this file |
| Rules for changing each area, and the checks to run afterwards | `engineering-rules.md` (ER-01 to ER-32) |
| Behavior, edge cases, rules of each feature | `business-rules.md` §1 to §23 |
| Components, data flow, configuration, persistence, integrations | `architecture.md` |
| Projects, directories, classes, test files | `repository-map.md` |
| Console features (F1 to F13) and test coverage | `feature-inventory.md` |
| GUI parity with the console | `console-to-gui-map.md` |
| Environment, commands, test warnings, CI, the current validation baseline, per-area validation | `build-and-test.md` §Required environment, §Commands, §Current validation baseline, §Test warnings, §Continuous integration, §Recommended verification after changes |
| Release policy and procedure: the workflow, gates, permissions, versions, tags, candidates, failure and recovery policy, container tags, platforms, protections, verification after a release | `release-process.md`; curated-notes format: `.github/release-notes/README.md` |
| Maintainer procedures: README screenshots, the embedded database, candidates, release verification commands, manual platform checks, validation harnesses, tooling problems | `maintainer-procedures.md` |
| Decisions and recommendations | `decisions.md` (D1 to D58, R1 to R8) |
| Technical debt | `technical-debt.md` (TD-01 to TD-57; the latest disposition section applies: §Stable-readiness validation, then §Reconciliation after GS2, then §Disposition map) |
| Open questions | `open-questions.md` (#1 to #42) |
| History and its labels; the validation records §A to §AZ (`history/validation/`) and the release history (`history/releases.md`) | `history/README.md` |

Later phases of roadmap step 9 move content (`history/README.md` §Planned phases); each move leaves a pointer at the old place.

## Task routing

Read the core first. File names are in `docs/ai-context/` without `.md`; ER-nn is a rule of `engineering-rules`; § labels are sections of the named file. "B&T" is `build-and-test`, "RP" `release-process` and "MP" `maintainer-procedures`. "Records §X, §Y" are lettered validation records, historical, in `history/validation/` (`history/README.md` §Validation record index says which file holds each).

| Task | Read | When needed | Not normally |
|---|---|---|---|
| Build work | `CLAUDE.md` §Build and test essentials; B&T §Required environment, §Commands, §Current validation baseline, §Test warnings | B&T §Recommended verification; MP §Tooling problems and their fixes | RP, the history |
| GUI work | ER-06 to ER-10, ER-14, ER-19, ER-23 (the area's rules); business-rules §16 to §23 (the area's §); architecture §GUI data flow; console-to-gui-map; B&T §Recommended verification | feature-inventory (the area's section and §Test coverage summary); decisions D28, D33, D42 to D46, D50 to D55; MP §Real GUI windows in a nested session; records §X, §AG to §AK, §AO, §AR to §AW | RP, history/releases |
| Console work | ER-12, ER-13, ER-21; business-rules §11, §12; feature-inventory F1 to F13; architecture §Console output; B&T §Recommended verification | MP §The console on a pseudo-terminal; records §Y, §AL | business-rules §16 to §23 |
| Configuration and YAML | ER-15 to ER-18; business-rules §9, §13 to §15; architecture §Configuration | MP §Negative controls, §Settings-file interruption harness; records §AB, §AD to §AF | RP |
| Database: refresh, repair, index | ER-04, ER-05, ER-22; business-rules §8, §9; architecture §Persistence | MP §Validation harnesses (the kill harness, the full-export comparison), §Manual platform validation of the database refresh; records §H to §J, §M to §O, §AQ | business-rules §16 to §21 |
| Embedded-database maintenance | MP §Rules for every procedure, §Refreshing the embedded database; ER-05; business-rules §8 | MP §Full-export comparison of the RIDB import; records §H, §M, §AQ | RP |
| Search behavior | ER-01, ER-02, ER-11, ER-20, ER-22; business-rules §1 to §7; architecture §Console data flow (search), §Interactive search service | business-rules §19, §22; MP §Parallel versus isolated searches; records §R, §T, §Z, §AJ | RP |
| autoRun behavior and wording | current-state V011-3; ER-14, ER-15, ER-19; business-rules §1, §2, §9, §15, §18 to §20; feature-inventory F3, F5, F10 and §GUI campsite searches and results (D46); console-to-gui-map; TD-56 | MP §Refreshing the README screenshots; records §AB, §AK, §AW | history/releases |
| README screenshot work | MP §Rules for every procedure, §Refreshing the README screenshots; the repository's `README.md` | MP §Real GUI windows in a nested session, §Tooling problems and their fixes; records §AO, §AU, §AW | RP |
| Email behavior | ER-02, ER-03, ER-12, ER-17; business-rules §5 to §7, §12, §15 (`Email.yaml`); feature-inventory F5, F11 | MP §Stand-ins for recreation.gov, email and the browser; records §T, §AA; open questions #9, #31, #40 | business-rules §16 to §23 |
| Testing | B&T §Commands, §Current validation baseline, §Test warnings, §Recommended verification; feature-inventory §Test coverage summary; the rules of the area under test | MP §Validation harnesses; the area's validation record | history/releases |
| CI | B&T §Continuous integration; RP §Release workflow (the pull-request dry run and its path filter); ER-27, ER-32 | records §U, §V, §AE, §AM | business-rules |
| Release engineering | RP (complete); ER-25 to ER-31; TD-57; decisions D17 to D19, D21, D24, D30 to D33, D48 | records §I, §L, §S, §U to §X, §AM, §AY; open questions #32, #33 | business-rules |
| Release notes | `.github/release-notes/README.md`; ER-25; RP §Release workflow (the notes steps); current-state (changes since the latest release) | history/releases (published bodies); records §S, §AP, §AU | business-rules, architecture |
| Release operation | §Release and audit reading policy below; RP (complete); history/README (complete); B&T §Recommended verification; ER-25, ER-26, ER-28, ER-29; technical-debt; open-questions | MP §Candidate download and inspection, §Release verification after publication; the last release's record (§AZ for `v0.1.0`) and the latest history/releases entry | business-rules, feature-inventory |
| Candidate artifact inspection | RP §Versions, candidates and publication; MP §Rules for every procedure, §Candidate download and inspection | records §AN, §AX, §AZ | business-rules |
| Release verification | RP §Release workflow ("Release procedure" step 5); MP §Release verification after publication | records §AC, §AX, §AZ; history/releases | business-rules |
| Documentation | this file (§Maintaining these documents); the target document; ER-24; decisions (next free ID); history/README (labels) | — | the historical records |
| Technical debt and open questions | technical-debt; open-questions; decisions (the rows they cite) | history/README; the records they cite | history/releases |
| Roadmap planning | current-state; decisions §Recommendations and the rows cited; technical-debt; open-questions | history/roadmap-and-status; decisions §Roadmap | the validation records |
| Historical investigation | history/README (complete), then the records it points to (`history/validation/`, history/releases, history/roadmap-and-status) | anything | — |
| Security and privacy | `CLAUDE.md` §Data safety; ER-10, ER-13, ER-18; business-rules §12 (secrets), §13 to §15, §17; TD-49; MP §Rules for every procedure | records §AH, §AY | — |
| Platform support | RP §Platforms and what a release run shows, §Release workflow (native smoke, platform table); open questions #23, #29, #38; the repository's `README.md` §System requirements; ER-28, ER-30 | MP §Manual platform validation of the database refresh; records §K, §L, §X, §AM | business-rules |
| Container work | architecture §Runtime modes; feature-inventory F9; ER-02, ER-29; TD-03, TD-49; open questions #10, #27; RP §Release workflow (the docker jobs) | MP §Local containers; records §N, §O, §W, §AM, §AY | business-rules §16 to §23 |
| `v0.1.1` Settings > System (V011-1, V011-2) | current-state V011-1, V011-2; feature-inventory F6, F7; console-to-gui-map; business-rules §8, §9, §12, §17, §21; ER-05, ER-06, ER-09, ER-10, ER-13; architecture §GUI data flow, §Persistence; TD-54, TD-56, TD-57 | records §N, §AO; decisions D26, D33, D43, D50 | history/releases (until the release) |

Read a named section, not the whole file: find its heading and read from it to the next heading of the same level. A route that needs a complete file says so.

## Release and audit reading policy

Before a tag or release operation, read completely:
- the mandatory core;
- every current document the release-operation route requires;
- the complete history index (`history/README.md`);
- the applicable current release records (the version's curated notes fragment, `current-state.md` §Latest release and the last release's record);
- any historical record that route selects;
- any further historical record needed to resolve a discrepancy or to verify a material release claim.

Also run every applicable deterministic documentation check available at that time. This policy authorizes no tag, dispatch, publication, rerun or recovery (`CLAUDE.md` §Approval boundaries).

A complete read of all historical AI-context documentation is required only for: a complete AI-context or documentation audit; relevant incident or release forensics; a cross-release historical investigation; an unresolved repository, publication, authority or documentation discrepancy; any task whose route explicitly requires the complete historical corpus.

## History policy

- History is not loaded routinely. Load a historical record only when the route names it, an investigation needs it or a discrepancy must be resolved, and then only that record.
- Historical records are kept, never rewritten or deleted. A correction is a dated note beside the record.
- History is never current authority. When history and a current document differ, the current document applies; when a current document and the code differ, the code applies and the difference is recorded.
- Phase 2 moved the historical records of `build-and-test.md` into `history/validation/` and `history/releases.md`. Until later phases move them, these historical records are still inside current files: `decisions.md` §Roadmap (steps 1 to 8 and the released versions); the resolved items and the two older disposition sections of `technical-debt.md`; the closed and answered items of `open-questions.md`.
- A validation record's "Regression checks for later changes" paragraph, or a check it describes, is applied as written where an engineering rule or `build-and-test.md` §Recommended verification after changes cites it; the record itself stays historical. `maintainer-procedures.md` §Validation harnesses is the current description of the reusable methods.
- Labels are permanent (`history/README.md` §Label policy).

## Maintaining these documents

- After a task, update the documents whenever architecture, behavior, commands or feature status changes (`CLAUDE.md` §Required workflow).
- Write each current fact once, in its authority (§Authority map), and point to it elsewhere.
- Update `current-state.md` in the same pull request as any change of the release state, roadmap position, authorization, requirements, blockers, risks or boundaries. After a release, the "Changes on `master` since" list starts again; its old items belong to that release's record.
- New records take the next free label: decisions D59, technical debt TD-58, open questions #43, validation records §BA, §BB and so on (in a new file of `history/validation/` for the period after `v0.1.0`, listed in `history/README.md` §Validation record index; a record is historical once written), engineering rules ER-33 (add its line to `CLAUDE.md` §Invariant index and to the routing table above).
- When content moves, keep its label and leave a pointer with the same heading at the old place; record the move in `history/README.md` with its proof.
- In the core and in files created since Phase 1, keep every line at or under 1,000 characters; break long paragraphs at sentence boundaries. Markdown cannot break a table row, so a row moved unchanged from an older file may be longer (`history/README.md` lists each).
- In the core, do not start a word with an at sign followed by a path except in the import lines of `CLAUDE.md`: Claude Code would import that file.
- Never write a procedure that lists, hashes, copies or opens real user data (`CLAUDE.md` §Data safety).

## Size budgets

Recommendations, not limits: never drop required information to meet one.
- Mandatory core at most about 36 KB: `CLAUDE.md` about 16 KB and 150 lines, `current-state.md` about 8 KB, this file about 12 KB.
- Other current documents at most about 60 KB each, and each part of `business-rules` about 100 KB once Phase 4 splits it; history files at most about 250 KB each.
- No line over 1,000 characters in the core (required) and in the files created since Phase 1, except a moved table row (§Maintaining these documents).
