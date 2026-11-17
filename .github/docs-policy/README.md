# Documentation-policy checks

`check-docs.sh` checks Campertron's AI-context documents (`CLAUDE.md`, `docs/ai-context/` and `docs/ai-context/history/`) against the documentation policy those documents state: the router `docs/ai-context/README.md` (§Maintaining these documents, §Size budgets, §Authority map, §Task routing), `docs/ai-context/history/README.md` (§Label policy) and `docs/ai-context/current-state.md`. It is deterministic: it reads repository files and local Git metadata only, never the network, GitHub or GHCR, and never a user-data folder.

It was added by roadmap step 9, Phase 5 and runs **locally only**. No workflow runs it: wiring it into `ci.yml` is a separate workflow change that needs its own approval.

## Running it

From anywhere in the repository (Bash, Git, and GNU or POSIX awk, grep, sed, sort and `sha256sum`; tested with GNU awk 5.4 and Bash 5.3 on Linux):

```bash
bash .github/docs-policy/check-docs.sh                 # check the documents
bash .github/docs-policy/check-docs.sh --verbose       # also the allowed exceptions and an inventory of what was checked
bash .github/docs-policy/check-docs.sh --list-checks   # the checks
bash .github/docs-policy/test-check-docs.sh            # the checker's own tests
```

Options: `--root DIR` checks another work tree; `--trx-dir DIR` also compares `test-coverage.md`'s case counts with `.trx` results (from `dotnet test --logger trx --results-directory DIR`, run with an isolated `XDG_DATA_HOME`); `--policy-log FILE` also compares the release-policy count with a saved run of `.github/release-policy/test-release-policy.sh`.

**Exit codes:** 0 no finding; 3 warnings only; 1 at least one error; 2 a usage or environment problem (an unknown option, not inside a repository, no `CLAUDE.md` and `docs/ai-context/`, a missing `docs-policy.conf`). A caller that accepts warnings treats 0 and 3 as success.

**Output:** one line per finding, `SEVERITY [check] file:line: message`, errors first, then warnings, sorted by file and line, then a summary line. The output depends only on the repository's contents: no time, no temporary path, the same in every locale and from every directory.

## Severity

- **ERROR**: a mandatory invariant is broken (a broken link, a duplicate or undefined ID, a missing Authority statement, a line over 1,000 characters in a file that must keep the limit, a missing blocker in `current-state.md`).
- **WARN**: a recommendation is not met (a size budget, a duplicate heading in a current file), a registered exception matches nothing any more, or a registered known defect (below) is still there.
- **ALLOWED** (with `--verbose`): a finding a registered exception allows, with its reason.
- **INFO** (with `--verbose`): what was checked: the ID families and their ranges, the test classes found, the local tags, the numbers of headings, links and citations.

## The checks

| Check | What it enforces |
|---|---|
| `file.core` | The core files exist; `CLAUDE.md` imports exactly the router and `current-state.md` (no history). |
| `file.budget` | The size budgets of the router's §Size budgets (warnings). |
| `file.line-length` | No line over 1,000 characters in the core and the files created since Phase 1; the registered legacy files and moved table rows are allowed. |
| `file.exception` | Each registered exception still matches something. |
| `link.target`, `link.anchor` | Relative links resolve; fragments match a heading by GitHub's anchor rules (lower case, punctuation dropped, spaces to hyphens, `-1`, `-2` for repeats). |
| `link.path` | Markdown paths named in current files (also outside links) exist. |
| `label.section` | Each `§` label in a current file resolves: to a heading, a bold label or a stub of the file named before it, of the citing file, or to a global label (the lettered records, the business-rules sections, `build-and-test.md`'s stubs and the validation records). A citation may shorten a heading to its first words. |
| `md.fence`, `md.table`, `md.heading` | Code fences close; no table row outside a table (a blank line inside a table) and no row with more cells than its table (an unescaped `\|`, also inside a code span); duplicate headings in current files (warning). |
| `id.sequence`, `id.index` | D, R, TD, questions, ER, F, V011, the lettered records and the business-rules sections run from their first ID without a gap and are defined once; TD-17 to TD-19 stay unassigned; index rows, their record links and their records agree (decisions, technical debt, open questions). |
| `id.undefined` | No reference beyond a family's last ID; no malformed ID (`TD-7`); TD-17 to TD-19 only as never assigned. |
| `id.range`, `id.next-free` | "First to last" range statements (`D1 to D58`) end at the last ID, in current files and the history index's label sections; each next-free declaration names the next ID. Families extend monotonically: there is no hard-coded maximum. |
| `route.map`, `route.task`, `route.mandatory`, `route.rules` | Every current file is in the authority map and in a task route; each subject has one row; no history file in the mandatory reading; every engineering rule has one line in `CLAUDE.md`'s invariant index and is routed. |
| `auth.current`, `auth.history` | Current files start with an Authority statement (`current-state.md`: the only current authority); history files carry "Historical record — not current authority" and claim no authority. |
| `state.release` | `current-state.md` §Latest release names the latest stable tag in local Git, its tag object, its commit and a release ID that equals the history index's §Release index. |
| `state.blocker` | Each technical-debt item marked blocking is in §Release blockers with what it blocks (so TD-57 stays visible); each item whose evidence cites §Release blockers or §Open risks is there (TD-49, TD-56). |
| `state.marker` | The facts registered as markers: the roadmap boundaries, the deferred `v0.1.1` requirements, V011-3's undecided wording. |
| `state.phase` | The history index's planned phases are numbered in order; each done phase's pull request is recorded in `current-state.md`. |
| `test.project`, `test.count`, `test.inventory` | Every test project and every test class of the source is in `test-coverage.md` once, with an existing file; the test counts equal the test attributes in the source; the totals add up; no other current file lists test counts. |
| `test.trx`, `test.policy` | With `--trx-dir` or `--policy-log`: the case counts and the release-policy count equal a test run. |
| `rel.index`, `rel.routing` | §Release index lists every local SemVer tag once, oldest first, with the tag objects and commits Git has; every other tag is mentioned; `releases.md` has the same versions in the same order; the release-operation route names `release-process.md` and `maintainer-procedures.md`, and a release route names each blocking item. |
| `mig.stub`, `mig.pr`, `mig.authority` | A stub ("Moved to …") keeps its destination's heading text; each recorded "PR #N (`head`, merged as `merge`)" is that pull request's merge in local Git (skipped in a shallow clone); migration-map rows carry an authority classification. |
| `safe.command` | No command in a current file (a code span or a fenced block) reads a real user-data folder (`~/.local/share/Campertron…`, `%LOCALAPPDATA%\Campertron…`, `~/Library/Application Support/Campertron…`), unless the line isolates it (`mktemp`, `XDG_DATA_HOME=`, `unshare`, a tmpfs) or states a prohibition. |

## Registered values and exceptions (`docs-policy.conf`)

`docs-policy.conf` records, in a form the checker reads, the values and exceptions the policy documents name; the documents stay the authority. One record per line, fields separated by ` | `:

| Record | Meaning |
|---|---|
| `core \| path` | A core file (the router's §Mandatory reading order). |
| `budget-core-total`, `budget-file`, `budget-current`, `budget-business-rules`, `budget-history` | The size budgets in bytes (1 KB = 1,000 bytes) and, for a file, its line budget. |
| `line-limit \| 1000` | The line limit in characters. |
| `legacy-long-lines \| path \| reason` | A current file older than Phase 1 that keeps its long lines. |
| `allow-long-line \| path \| sha256 \| reason` | One long line allowed, by the SHA-256 of the line without its line break (`sed -n 'Np' FILE \| tr -d '\n' \| sha256sum`); an edited line needs a new record. |
| `legacy-no-authority \| path \| reason` | A current file older than Phase 1 without an Authority statement. |
| `id-gap \| TD \| 17 \| 19 \| reason` | IDs never assigned. |
| `marker \| path \| section \| text \| reason` | A section (by its heading text) of a file that must contain the text. |
| `not-a-path \| name \| reason` | A name that looks like a Markdown path but is not a repository file. |
| `known-defect \| check \| path \| sha256 or - \| reason` | A known error in a file the change that found it could not correct: reported as a labeled warning until it is fixed. |
| `safe-path \| prefix \| reason` | A path prefix that is an isolated location (for `safe.command`). |

Register an exception only where the policy allows it (for example a table row moved unchanged, which Markdown cannot break; `history/README.md` lists each) and give the reason. A stale record warns.

## Keeping it current

The checker extends each family by itself; these changes need only the document edits the router already asks for:

- **A new decision, technical-debt item, open question or engineering rule:** add its index row and record (or rule, and its `CLAUDE.md` line and route) as the router says, and update every "first to last" range and next-free declaration the checker names (`D1 to D58` → `D1 to D59`, "takes the next free ID, D59" → D60; also in `history/README.md` §Label policy and §Label index).
- **A new business-rule section:** its heading `## N. Title` in the file of its area, its row in `business-rules.md` §Section index, and the next-free number in the router.
- **A new task route or current document:** a row in the router's §Authority map and a route that names the document; the checker fails otherwise.
- **A new history record (a validation record §BA, …):** its heading `### BA. Title` in a `history/validation/` file that starts with the historical banner, its row in `history/README.md` §Validation record index, and the next-free labels (§BB).
- **A release:** its row in `history/README.md` §Release index (oldest first, with the tag object and commit prefixes), its entry in `history/releases.md`, and `current-state.md` §Latest release with the tag object, commit and release ID. The checker compares them with the local tags (fetch the tags first).
- **A new test class or test:** its row in `test-coverage.md` §Test classes and the totals; the checker counts the test attributes in the source.
- **A changed roadmap fact registered as a marker:** change the marker in the same pull request as the decision that changes the fact.

After changing the checker, run `test-check-docs.sh`, which builds a small fixture repository (documents, a test project, a Git history with tags and a merged pull request) in a temporary directory, checks that it passes, applies one violation per negative control and checks the expected finding, and finally runs the checker on this repository.

## Limitations and manual review

- **Anchors:** GitHub's anchor rules are reproduced for ASCII headings. A heading with a non-ASCII letter (not punctuation such as —, → or §) gets a warning, because the checker drops all non-ASCII text.
- **Section labels:** a citation resolves if any matching label exists in the candidate files; a label that exists but was meant for another file is not found. History files are not checked: they name sections as they were then, and the old names resolve through the stubs.
- **Identifiers:** references are found by pattern (`D`, `R` and `F` followed by a number without a leading zero, `TD-` and `ER-` with two digits, `#N` after "question"); a `#N` without that word is not checked. Next-free declarations are recognized by "next free" and "take(s) the next" or "takes" followed by an ID.
- **Tests:** a test is a method with `[Test]`, `[TestCase]`, `[TestCaseSource]`, `[AvaloniaTest]` or `[Theory]`, and its class is the class declared one indentation level above the attribute (four spaces); unusual formatting needs a manual check. Case counts need `--trx-dir`.
- **Data safety:** only commands in code spans and fenced blocks are checked; prose instructions, commands on files named without a path (`sqlite3 RecreationDotOrg.db` in an isolated account) and running the programs without an isolated `XDG_DATA_HOME` need a manual review.
- **Release history:** the checker compares the documents with local tags and commits only. Published releases, assets, attestations, release bodies and container images are not checked (they need GitHub and GHCR, which the checker never contacts); `maintainer-procedures.md` §Release verification after publication describes those checks.
- **Not checked at all:** whether a statement is true, whether a route is the right one, the content of history records, and anything outside `CLAUDE.md` and `docs/ai-context/` except the links of this file.
