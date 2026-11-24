#!/usr/bin/env bash
# Tests for check-docs.sh. A small, complete fixture repository (its own documents, a test project
# and a Git history with tags and a merged pull request) is built in a temporary directory; it must
# pass, and each negative control (one deliberate violation in a copy) must fail with the expected
# check. The last tests run the checker on this repository itself. Nothing here uses the network or
# a user-data folder. Run: bash .github/docs-policy/test-check-docs.sh
set -uo pipefail
export LC_ALL=C

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
checker="$here/check-docs.sh"
repo="$(cd "$here/../.." && pwd)"
AWK="$(command -v gawk 2>/dev/null || command -v awk)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
passed=0
failed=0

pass() { passed=$((passed + 1)); echo "PASS  $1"; }
fail() { failed=$((failed + 1)); echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/      /' | head -20; }

# run DIR [ARGS...]: run the checker on DIR; sets $out and $rc
run() { local d="$1"; shift; out="$(bash "$checker" --root "$d" "$@" 2>&1)"; rc=$?; }

# expect DESCRIPTION DIR RC PATTERN [ARGS...]: the exit code is RC and the output matches PATTERN
expect() {
  local desc="$1" dir="$2" want="$3" pat="$4"; shift 4
  run "$dir" "$@"
  if [ "$rc" != "$want" ]; then fail "$desc (exit $rc, expected $want)" "$out"; return; fi
  if [ -n "$pat" ] && ! printf '%s\n' "$out" | grep -qE -- "$pat"; then fail "$desc (no line matching: $pat)" "$out"; return; fi
  pass "$desc"
}

# replace FILE OLD NEW: replace the first occurrence of OLD (literal text) on a line of FILE
replace() {
  local f="$1"
  OLD="$2" NEW="$3" "$AWK" 'BEGIN { o = ENVIRON["OLD"]; n = ENVIRON["NEW"] }
    !done && (i = index($0, o)) > 0 { $0 = substr($0, 1, i - 1) n substr($0, i + length(o)); done = 1 }
    { print } END { if (!done) exit 3 }' "$f" > "$f.tmp" || { echo "replace: no \"$2\" in $f" >&2; rm -f "$f.tmp"; return 1; }
  mv "$f.tmp" "$f"
}
# drop FILE TEXT: remove every line that contains TEXT
drop() { grep -v -F -- "$2" "$1" > "$1.tmp"; mv "$1.tmp" "$1"; }

# case: a fresh copy of the valid fixture; prints its path
case_dir() { local d; d="$(mktemp -d "$work/case.XXXXXX")"; cp -a "$work/base/." "$d/"; echo "$d"; }

# ---------------------------------------------------------------------------------------------
# The fixture
# ---------------------------------------------------------------------------------------------
make_fixture() {
  local d="$1"
  mkdir -p "$d" && cd "$d" || exit 1
  export GIT_AUTHOR_NAME=Fixture GIT_AUTHOR_EMAIL=fixture@example.invalid GIT_COMMITTER_NAME=Fixture GIT_COMMITTER_EMAIL=fixture@example.invalid
  export GIT_AUTHOR_DATE=2026-01-01T00:00:00Z GIT_COMMITTER_DATE=2026-01-01T00:00:00Z
  g() { git -c commit.gpgsign=false -c tag.gpgsign=false -c core.hooksPath=/dev/null "$@"; }
  g init -q -b master .
  echo seed > seed.txt && g add seed.txt && g commit -q -m seed
  g tag -a v0.1.0-beta.1 -m v0.1.0-beta.1
  g checkout -q -b feature && echo feature > feature.txt && g add feature.txt && g commit -q -m feature
  g checkout -q master && g merge -q --no-ff feature -m "Merge pull request #5 from fixture/feature"
  g tag -a v0.1.0 -m v0.1.0
  g tag v0.1-old
  local o1 c1 o2 c2 head merge
  o1="$(git rev-parse refs/tags/v0.1.0-beta.1)"; c1="$(git rev-parse v0.1.0-beta.1^{commit})"
  o2="$(git rev-parse refs/tags/v0.1.0)"; c2="$(git rev-parse v0.1.0^{commit})"
  head="$(git rev-parse feature)"; merge="$(git rev-parse master)"
  mkdir -p docs/ai-context/history/validation .github/docs-policy FixtureTests

  cat > .github/docs-policy/docs-policy.conf <<'EOF'
core | CLAUDE.md
core | docs/ai-context/README.md
core | docs/ai-context/current-state.md
budget-core-total | 36000
budget-file | CLAUDE.md | 16000 | 150
budget-current | 60000
budget-business-rules | 100000
budget-history | 250000
core-chars-limit | 140000
line-limit | 1000
id-gap | TD | 17 | 19 | never assigned
marker | docs/ai-context/current-state.md | Roadmap boundaries | No third preview | roadmap boundary
safe-path | /home/camper/ | the isolated user
EOF

  cat > CLAUDE.md <<'EOF'
# CLAUDE.md

@docs/ai-context/README.md
@docs/ai-context/current-state.md

## Purpose
A fixture. The deferred requirements are V011-1 to V011-2.

## Invariant index
- ER-01 Keep the filter rules.
- ER-02 Keep the retries.
EOF

  cat > docs/ai-context/README.md <<'EOF'
# AI context: router and rules

Authority: current. This file says what to read.

## Mandatory reading order

1. The mandatory core: `CLAUDE.md`, this file and `current-state.md`.
2. The documents the routing table names.
3. History only when a route needs it.

## Authority map

| Subject | Current authority |
|---|---|
| Routing | this file |
| State | `current-state.md` |
| Rules | `engineering-rules.md` (ER-01 to ER-02) |
| Behavior | `business-rules.md` §1; `business-rules-gui.md` §2 |
| Features | `feature-inventory.md` (F1 to F2) |
| Structure | `repository-map.md` |
| Tests | `test-coverage.md` |
| Build | `build-and-test.md` |
| Releases | `release-process.md` |
| Procedures | `maintainer-procedures.md` |
| Decisions | `decisions.md` (D1 to D2 and R1 to R1) |
| Debt | `technical-debt.md` (TD-01 to TD-20) |
| Questions | `open-questions.md` (#1 to #2) |
| History | `history/README.md` |

## Task routing

| Task | Read | When needed | Not normally |
|---|---|---|---|
| Build work | B&T §Commands; repository-map | test-coverage | RP |
| Feature work | ER-01, ER-02; business-rules §1; business-rules-gui §2; feature-inventory F1 | decisions D1; technical-debt TD-01; open-questions #1 | history |
| Release operation | RP §Release workflow; current-state; TD-03 | MP §Procedures; records §A | business-rules |

## Maintaining these documents

- New records take the next free label: decisions D3, technical debt TD-21, open questions #3, validation records §C, engineering rules ER-03.
- A new business-rule section takes the next global number, §3, in the file of its area.
- History is described in `history/README.md` §Label policy.
EOF

  cat > docs/ai-context/current-state.md <<EOF
# Current state

Authority: **the only current authority** for the repository and release state.

## Latest release

- **\`v0.1.0\`, Latest and immutable.**
- Tag \`v0.1.0\`: annotated, object \`$o2\`, on commit \`$c2\`.
- GitHub release 1002: immutable, Latest.

## Changes on \`master\` since \`v0.1.0\`

- Roadmap step 9, Phase 1 (PR #5, \`${head:0:7}\`, merged as \`${merge:0:7}\`) added the core.

## Deferred \`v0.1.1\` requirements

Design and implementation are not approved.

### V011-1 First requirement
Something to do.

### V011-2 Second requirement
Something else.

## Release blockers

- **TD-03, live: it blocks the next stable publication.**

## Open risks

- **TD-02, unresolved:** a risk.

## Roadmap boundaries

- No third preview (no \`preview.3\`).
EOF

  cat > docs/ai-context/decisions.md <<'EOF'
# Architecture decisions

Authority: current. The index of D1 to D2 and R1 to R1. A new decision takes the next free ID, D3.

## Decision index
| ID | Subject | Status | Record |
|---|---|---|---|
| D1 | First | current | [D1](history/decision-log.md#d1) |
| D2 | Second | current | [D2](history/decision-log.md#d2) |

## Recommendations
| ID | Recommendation | Disposition | Record |
|---|---|---|---|
| R1 | First | done | `history/decision-log.md` §Recommendations |
EOF

  cat > docs/ai-context/history/decision-log.md <<'EOF'
# Decision log

> **Historical record — not current authority.** The decision records.

### D1
Decision one.

### D2
Decision two.

## Recommendations
- R1. First.
EOF

  {
    echo '# Technical debt'
    echo
    echo 'Authority: current. TD-01 to TD-20; TD-17, TD-18 and TD-19 were never assigned and are never reused; a new item takes TD-21.'
    echo
    echo '## Index'
    echo '| ID | Subject | Effective disposition | Status | Actionable | Blocking | Trigger or prerequisite | Evidence | Record |'
    echo '|---|---|---|---|---|---|---|---|---|'
    echo '| TD-01 | One | open | open | approval | no | — | D1 | [TD-01](#td-01) |'
    echo '| TD-02 | Two | a risk | open | decision | no | — | `current-state.md` §Open risks | [TD-02](#td-02) |'
    echo '| TD-03 | Three | live | open | approval | **the next stable publication** | — | `current-state.md` §Release blockers | [TD-03](#td-03) |'
    for i in 04 05 06 07 08 09 10 11 12 13 14 15 16; do echo "| TD-$i | Item $i | resolved | resolved | — | no | — | — | [TD-$i](history/technical-debt.md#td-$i) |"; done
    echo '| TD-17, TD-18, TD-19 | never assigned; never reuse these IDs | — | — | — | — | — | — | — |'
    echo '| TD-20 | Twenty | resolved | resolved | — | no | — | — | [TD-20](history/technical-debt.md#td-20) |'
    echo
    echo '## Records'
    for i in 01 02 03; do echo; echo "### TD-$i"; echo "**Issue:** item $i."; done
  } > docs/ai-context/technical-debt.md
  {
    echo '# Technical debt: historical records'
    echo
    echo '> **Historical record — not current authority.** The resolved items.'
    for i in 04 05 06 07 08 09 10 11 12 13 14 15 16 20; do echo; echo "### TD-$i"; echo "**Issue:** item $i, resolved."; done
  } > docs/ai-context/history/technical-debt.md

  cat > docs/ai-context/open-questions.md <<'EOF'
# Open questions

Authority: current. The questions #1 to #2. A new question takes #3.

## Index
| # | Question | Status | Open | Related | Record |
|---|---|---|---|---|---|
| #1 | First? | open | yes | TD-01 | [#1](#1) |
| #2 | Second? | answered | no | — | [#2](history/open-questions.md#2) |

### #1
Is the first one intended?
EOF
  cat > docs/ai-context/history/open-questions.md <<'EOF'
# Open questions: historical records

> **Historical record — not current authority.** The answered questions.

### #2
Was the second one answered? Yes.
EOF

  cat > docs/ai-context/engineering-rules.md <<'EOF'
# Engineering rules

Authority: current. The IDs ER-01 to ER-02 are permanent; a new rule takes the next free ID.

### ER-01 Filter rules
- Keep the filter rules.

### ER-02 Retries
- Keep the retries.
EOF

  cat > docs/ai-context/feature-inventory.md <<'EOF'
# Feature inventory

Authority: current. The features F1 to F2. Which tests cover them is in `test-coverage.md`.

## F1. Startup
- Starts.

## F2. Menu
- Shows a menu.
EOF

  cat > docs/ai-context/business-rules.md <<'EOF'
# Business rules

Authority: current. Sections §1 and §2; §2 is in `business-rules-gui.md`.

## Section index
| § | Rules | File |
|---|---|---|
| [§1](#1-search) | Search | this file |
| [§2](business-rules-gui.md#2-gui) | GUI | `business-rules-gui.md` |

## 1. Search
- The search rules.

## 2. GUI
Moved to [`business-rules-gui.md`](business-rules-gui.md#2-gui) by the split.
EOF
  cat > docs/ai-context/business-rules-gui.md <<'EOF'
# Business rules: GUI

Authority: current. Section §2.

## 2. GUI
- The GUI rules.
EOF

  cat > docs/ai-context/repository-map.md <<'EOF'
# Repository map

Authority: current. The projects and files; the tests are in `test-coverage.md`.

## Solution
- `FixtureTests`: the test project.
EOF

  cat > docs/ai-context/build-and-test.md <<'EOF'
# Build and test

Authority: current. Build commands.

## Commands
- Run the tests with `XDG_DATA_HOME="$(mktemp -d)" dotnet test`.

## Moved sections

### A. First record
Moved to [`history/validation/a-b.md`](history/validation/a-b.md#a-first-record) by the split.
EOF

  cat > docs/ai-context/release-process.md <<'EOF'
# Release process

Authority: current. The release procedure.

## Release workflow
**Failure policy** nothing is retried by itself.
EOF

  cat > docs/ai-context/maintainer-procedures.md <<'EOF'
# Maintainer procedures

Authority: current. The procedures.

## Procedures
- Never open `~/.local/share/CampertronConfig`; use `X=$(mktemp -d)` and `ls $X/CampertronConfig`.
- In the isolated account: `cat /home/camper/.local/share/CampertronConfig/General.yaml`.
EOF

  cat > docs/ai-context/test-coverage.md <<'EOF'
# Test coverage

Authority: current. The tests.

## Totals
| Suite | Classes | Tests | Cases | On Linux | Runs in |
|---|---|---|---|---|---|
| `FixtureTests` | 1 | 2 | 3 | 3 passed | `ci.yml` |
| Both .NET test projects | 1 | 2 | 3 | 3 passed | |
| Release policy (`.github/release-policy/test-release-policy.sh`) | — | 5 | — | 5 passed | `release.yml` |

## Test classes
| Class | Project | File | Tests | Cases | Skipped on Linux |
|---|---|---|---|---|---|
| `AlphaTests` | `FixtureTests` | `FixtureTests/AlphaTests.cs` | 2 | 3 | — |
EOF
  echo '<Project Sdk="Microsoft.NET.Sdk" />' > FixtureTests/FixtureTests.csproj
  cat > FixtureTests/AlphaTests.cs <<'EOF'
namespace FixtureTests;

public class AlphaTests
{
    [Test]
    public void First() { }

    [TestCase(1)]
    [TestCase(2)]
    public void Second(int n) { }
}
EOF

  cat > docs/ai-context/history/README.md <<EOF
# History index

> **Historical record — not current authority.** Nothing indexed here is current authority.

## Label policy

- Labels: decisions D1 to D2; R1 to R1; TD-01 to TD-20; open questions #1 to #2; F1 to F2; ER-01 to ER-02; V011-1 to V011-2.
- A new record takes the next free label: §C; D3; R2; TD-21; #3; ER-03. TD-17 to TD-19 were never assigned.

## Label index

| Labels | Defined now |
|---|---|
| D1 to D2 | \`../decisions.md\` |

## Release index

| Version | Published (UTC) | Tag object → commit | Release run | Release ID | Record |
|---|---|---|---|---|---|
| \`v0.1.0-beta.1\` | 2026-01-01 | \`${o1:0:8}\` → \`${c1:0:7}\` | 1 | 1001 | §A |
| \`v0.1.0\` | 2026-01-02 | \`${o2:0:8}\` → \`${c2:0:7}\` | 2 | 1002 | §B |

The lightweight tag \`v0.1-old\` points to the first commit.

## Phase 1 migration map

| Source | Destination | Authority |
|---|---|---|
| old core, §Solution layout | \`../current-state.md\` | current |

## Planned phases

1. Done (PR #5): the core.
2. Checks.
EOF

  cat > docs/ai-context/history/releases.md <<'EOF'
# Releases

> **Historical record — not current authority.** The release history.

### Release history
**`v0.1.0-beta.1`, published 2026-01-01** [V]
- First.

**`v0.1.0`, published 2026-01-02** [V]
- Second; it named §Old section and `cat ~/.local/share/CampertronConfig/General.yaml` then. TD-18 was never assigned.
EOF

  cat > docs/ai-context/history/validation/a-b.md <<'EOF'
# Validation records §A and §B

> **Historical record — not current authority.** Records.

### A. First record
- Verified.

### B. Second record
- Verified.
EOF
  cd - >/dev/null || exit 1
}

make_fixture "$work/base"

# ---------------------------------------------------------------------------------------------
# The valid fixture, exit codes and the interface
# ---------------------------------------------------------------------------------------------
expect "the valid fixture passes with no finding" "$work/base" 0 '^check-docs: 0 error\(s\), 0 warning\(s\)'
run "$work/base" --verbose
if printf '%s\n' "$out" | grep -q 'family D1 to D2' && printf '%s\n' "$out" | grep -q 'family TD-01 to TD-20' && printf '%s\n' "$out" | grep -q '1 test classes in the source, 1 class rows' && printf '%s\n' "$out" | grep -q 'latest stable: v0.1.0'; then
  pass "--verbose lists what was checked (ID families, test classes, the latest stable tag)"
else fail "--verbose lists what was checked" "$out"; fi
out="$(bash "$checker" --bogus 2>&1)"; rc=$?
[ "$rc" = 2 ] && pass "an unknown argument is a usage error (exit 2)" || fail "an unknown argument is a usage error (exit $rc)" "$out"
mkdir -p "$work/empty"
out="$(cd "$work/empty" && bash "$checker" 2>&1)"; rc=$?
[ "$rc" = 2 ] && printf '%s' "$out" | grep -q 'not inside a Git work tree' && pass "outside a repository it stops with exit 2 and says why" || fail "outside a repository (exit $rc)" "$out"
out="$(bash "$checker" --root "$work/empty" 2>&1)"; rc=$?
[ "$rc" = 2 ] && printf '%s' "$out" | grep -q 'not a Campertron repository' && pass "--root without CLAUDE.md and docs/ai-context is refused (exit 2)" || fail "--root without the documents (exit $rc)" "$out"
out="$(bash "$checker" --list-checks 2>&1)"; rc=$?
[ "$rc" = 0 ] && printf '%s' "$out" | grep -q '^safe.command' && pass "--list-checks lists the checks" || fail "--list-checks" "$out"
out="$(cd "$work/base/docs/ai-context/history" && bash "$checker" 2>&1)"; rc=$?
[ "$rc" = 0 ] && pass "run from a subdirectory, it finds the work tree" || fail "run from a subdirectory (exit $rc)" "$out"

# Determinism: two runs, another locale and another working directory give the same bytes.
a="$(bash "$checker" --root "$work/base" --verbose 2>&1)"; b="$(bash "$checker" --root "$work/base" --verbose 2>&1)"
c="$(cd / && LANG=de_DE.UTF-8 LC_ALL=de_DE.UTF-8 bash "$checker" --root "$work/base" --verbose 2>/dev/null)"
[ "$a" = "$b" ] && [ "$a" = "$c" ] && pass "two runs, another locale and another directory give identical output" || fail "deterministic output" "$(diff <(printf '%s\n' "$a") <(printf '%s\n' "$c"))"
d="$(cd "$work/base" && HOME=/nonexistent XDG_DATA_HOME=/nonexistent bash "$checker" --verbose 2>&1)"
[ "$a" = "$d" ] && pass "the result does not depend on HOME or XDG_DATA_HOME" || fail "independent of HOME and XDG_DATA_HOME" "$d"
if unshare -rn true 2>/dev/null; then
  e="$(unshare -rn bash "$checker" --root "$work/base" --verbose 2>&1)"
  [ "$a" = "$e" ] && pass "without a network (a namespace with no interface) the result is the same" || fail "without a network" "$e"
else
  echo "SKIP  without a network: unshare -rn is not available here"
fi
# Static check: no command of the checker reaches a network service or builds anything.
netpat='(curl|wget|gh (api|release|run|pr|repo)|git (fetch|pull|push|clone|ls-remote)|docker |dotnet |ssh |ncat|telnet|nslookup|dig )'
if grep -v -E '^\s*#' "$checker" | grep -q -E "$netpat"; then
  fail "the checker calls no network or build tool" "$(grep -n -v -E '^\s*#' "$checker" | grep -E "$netpat")"
else pass "the checker calls no network or build tool"; fi

# ---------------------------------------------------------------------------------------------
# Warnings and registered exceptions
# ---------------------------------------------------------------------------------------------
d="$(case_dir)"; replace "$d/.github/docs-policy/docs-policy.conf" 'budget-current | 60000' 'budget-current | 100'
expect "a size over a budget (a recommendation) only warns: exit 3" "$d" 3 '^WARN \[file.budget\] docs/ai-context/[a-z-]+\.md: [0-9]+ bytes, over the budget'
d="$(case_dir)"; replace "$d/.github/docs-policy/docs-policy.conf" 'core-chars-limit | 140000' 'core-chars-limit | 100'
expect "always-loaded instructions that reach their character limit are an error (a required limit)" "$d" 1 '^ERROR \[file\.core-limit\] CLAUDE\.md: the always-loaded instructions \(CLAUDE\.md and its imports\) are [0-9]+ characters, not below the required limit of 100 '
d="$(case_dir)"; printf 'Multi-byte text: %s\n' "$(printf '\303\251%.0s' $(seq 1 20))" >> "$d/docs/ai-context/current-state.md"
chars=0; bytes=0
for c in CLAUDE.md docs/ai-context/README.md docs/ai-context/current-state.md; do chars=$((chars + $(tr -d '\200-\277' < "$d/$c" | wc -c))); bytes=$((bytes + $(wc -c < "$d/$c"))); done
replace "$d/.github/docs-policy/docs-policy.conf" 'core-chars-limit | 140000' "core-chars-limit | $((chars + 1))"
if [ "$bytes" -le $((chars + 1)) ]; then fail "the limit counts characters: the fixture has no multi-byte text ($bytes bytes, $chars characters)"
else expect "the limit counts characters, not bytes: multi-byte text over it in bytes but under it in characters passes" "$d" 0 "^INFO \[file\.core-limit\] CLAUDE\.md: the always-loaded instructions \(CLAUDE\.md and its imports\) are $chars characters, below the required limit of $((chars + 1))\$" --verbose; fi
d="$(case_dir)"; long="$(printf 'x%.0s' $(seq 1 1100))"; echo "$long" >> "$d/docs/ai-context/history/releases.md"
expect "an unregistered long line in a history file is an error" "$d" 1 '^ERROR \[file.line-length\] docs/ai-context/history/releases.md:[0-9]+: 1100 characters'
h="$(tail -n 1 "$d/docs/ai-context/history/releases.md" | tr -d '\n' | sha256sum | cut -c1-64)"
echo "allow-long-line | docs/ai-context/history/releases.md | $h | a moved table row" >> "$d/.github/docs-policy/docs-policy.conf"
expect "a registered long line (a moved table row) is allowed" "$d" 0 '^ALLOWED \[file.line-length\] docs/ai-context/history/releases.md' --verbose
echo "allow-long-line | docs/ai-context/history/releases.md | 0000000000000000000000000000000000000000000000000000000000000000 | stale" >> "$d/.github/docs-policy/docs-policy.conf"
expect "a registered exception that matches nothing warns" "$d" 3 '^WARN \[file.exception\] docs/ai-context/history/releases.md: registered long-line exception'
d="$(case_dir)"; long="$(printf 'y%.0s' $(seq 1 1001))"; echo "$long" >> "$d/docs/ai-context/decisions.md"
expect "a line over 1,000 characters in a current file is an error (a hard limit)" "$d" 1 '^ERROR \[file.line-length\] docs/ai-context/decisions.md'
echo "legacy-long-lines | docs/ai-context/decisions.md | predates Phase 1" >> "$d/.github/docs-policy/docs-policy.conf"
expect "a file registered as older than Phase 1 keeps its long lines" "$d" 0 ''
d="$(case_dir)"; drop "$d/docs/ai-context/repository-map.md" 'Authority: current.'
expect "a current file without an Authority statement is an error" "$d" 1 '^ERROR \[auth.current\] docs/ai-context/repository-map.md'
echo "legacy-no-authority | docs/ai-context/repository-map.md | predates Phase 1" >> "$d/.github/docs-policy/docs-policy.conf"
expect "a registered legacy file without an Authority statement is allowed" "$d" 0 '^ALLOWED \[auth.current\] docs/ai-context/repository-map.md' --verbose
d="$(case_dir)"; printf '\n| A | B |\n|---|---|\n| `x | y` | z |\n' >> "$d/docs/ai-context/repository-map.md"
expect "a pipe in a code span that splits a table row is an error" "$d" 1 '^ERROR \[md.table\] docs/ai-context/repository-map.md:[0-9]+: table row has 3 cells, its table 2'
h="$(tail -n 1 "$d/docs/ai-context/repository-map.md" | tr -d '\n' | sha256sum | cut -c1-64)"
echo "known-defect | md.table | docs/ai-context/repository-map.md | $h | to be escaped separately" >> "$d/.github/docs-policy/docs-policy.conf"
expect "a registered known defect is reported as a labeled warning" "$d" 3 '^WARN \[md.table\] .*\(known defect: to be escaped separately\)'
d="$(case_dir)"; echo "known-defect | md.table | docs/ai-context/repository-map.md | - | gone" >> "$d/.github/docs-policy/docs-policy.conf"
expect "a known defect that no longer occurs warns" "$d" 3 '^WARN \[file.exception\] docs/ai-context/repository-map.md: the registered known defect'

# ---------------------------------------------------------------------------------------------
# Negative controls: files, links, paths, labels and Markdown
# ---------------------------------------------------------------------------------------------
d="$(case_dir)"; rm "$d/docs/ai-context/current-state.md"
expect "a missing core file is an error" "$d" 1 '^ERROR \[file.core\] docs/ai-context/current-state.md: core file missing'
d="$(case_dir)"; echo '@docs/ai-context/history/README.md' >> "$d/CLAUDE.md"
expect "importing a history file into CLAUDE.md is an error" "$d" 1 '^ERROR \[file.core\] CLAUDE.md:[0-9]+: CLAUDE.md imports docs/ai-context/history/README.md'
d="$(case_dir)"; drop "$d/CLAUDE.md" '@docs/ai-context/current-state.md'
expect "a core file CLAUDE.md does not import is an error" "$d" 1 '^ERROR \[file.core\] CLAUDE.md: CLAUDE.md does not import the core file docs/ai-context/current-state.md'
d="$(case_dir)"; echo 'See [the map](no-such-file.md).' >> "$d/docs/ai-context/repository-map.md"
expect "a broken link is an error" "$d" 1 '^ERROR \[link.target\] docs/ai-context/repository-map.md:[0-9]+: link target does not exist: no-such-file.md'
d="$(case_dir)"; echo 'See [the rules](business-rules.md#9-no-such-section).' >> "$d/docs/ai-context/repository-map.md"
expect "a link to a missing anchor is an error" "$d" 1 '^ERROR \[link.anchor\] docs/ai-context/repository-map.md:[0-9]+: no heading of docs/ai-context/business-rules.md has the anchor #9-no-such-section'
d="$(case_dir)"; echo 'The plan is in `history/plans.md`.' >> "$d/docs/ai-context/repository-map.md"
expect "a named Markdown path that does not exist is an error" "$d" 1 '^ERROR \[link.path\] docs/ai-context/repository-map.md:[0-9]+: names history/plans.md'
d="$(case_dir)"; echo 'Read `release-process.md` §Rollback policy first.' >> "$d/docs/ai-context/repository-map.md"
expect "a section label that matches nothing is an error" "$d" 1 '^ERROR \[label.section\] docs/ai-context/repository-map.md:[0-9]+: section label §Rollback policy.* \(cited in docs/ai-context/release-process.md\)'
d="$(case_dir)"; echo 'Read `release-process.md` §Failure policy, `history/validation/a-b.md` §B, business-rules §2 and B&T §Moved sections.' >> "$d/docs/ai-context/repository-map.md"
expect "labels resolve through bold labels, records, the section index and stubs" "$d" 0 ''
d="$(case_dir)"; printf '\n### A. Another first record\n- Verified.\n' >> "$d/docs/ai-context/history/validation/a-b.md"
expect "a lettered record defined twice is an error" "$d" 1 '^ERROR \[id.sequence\] docs/ai-context/history/validation/a-b.md:[0-9]+: §A is defined twice'
d="$(case_dir)"; printf '\n## 1. Search again\n- More rules.\n' >> "$d/docs/ai-context/business-rules-gui.md"
expect "a business-rules section defined in two files is an error" "$d" 1 '^ERROR \[id.sequence\] docs/ai-context/business-rules[a-z-]*.md:[0-9]+: §1 is defined twice'
d="$(case_dir)"; replace "$d/docs/ai-context/business-rules.md" '## 2. GUI' '## 2. The GUI rules'
expect "a stub whose heading differs from its destination is an error" "$d" 1 '^ERROR \[mig.stub\] docs/ai-context/business-rules.md:[0-9]+: the stub heading differs'
d="$(case_dir)"; replace "$d/docs/ai-context/decisions.md" '| D2 | Second | current |' $'| D2 | Second | current |\n\n| D9 | Lost | current |'
expect "a table row after a blank line (outside its table) is an error" "$d" 1 '^ERROR \[md.table\] docs/ai-context/decisions.md:[0-9]+: a line starting with "\|" that is not part of a table'
d="$(case_dir)"; printf '\n```\nunclosed\n' >> "$d/docs/ai-context/repository-map.md"
expect "an unclosed code fence is an error" "$d" 1 '^ERROR \[md.fence\] docs/ai-context/repository-map.md'
d="$(case_dir)"; printf '\n## Solution\n- Again.\n' >> "$d/docs/ai-context/repository-map.md"
expect "a duplicate heading in a current file warns" "$d" 3 '^WARN \[md.heading\] docs/ai-context/repository-map.md:[0-9]+: heading "Solution" repeats'

# ---------------------------------------------------------------------------------------------
# Negative controls: identifiers
# ---------------------------------------------------------------------------------------------
d="$(case_dir)"; replace "$d/docs/ai-context/decisions.md" '| D2 | Second | current | [D2](history/decision-log.md#d2) |' $'| D2 | Second | current | [D2](history/decision-log.md#d2) |\n| D2 | Again | current | [D2](history/decision-log.md#d2) |'
expect "a duplicate ID is an error" "$d" 1 '^ERROR \[id.sequence\] docs/ai-context/decisions.md:[0-9]+: D2 is defined twice'
d="$(case_dir)"; echo 'As decided in D9.' >> "$d/docs/ai-context/repository-map.md"
expect "an undefined ID is an error" "$d" 1 '^ERROR \[id.undefined\] docs/ai-context/repository-map.md:[0-9]+: D9 is not defined \(the family runs to D2\)'
d="$(case_dir)"; echo 'See TD-18 for the details.' >> "$d/docs/ai-context/repository-map.md"
expect "a reference that reuses TD-18 is an error" "$d" 1 '^ERROR \[id.undefined\] docs/ai-context/repository-map.md:[0-9]+: TD-18 is a never-assigned ID'
d="$(case_dir)"; replace "$d/docs/ai-context/technical-debt.md" '| TD-17, TD-18, TD-19 |' $'| TD-17 | Reused | open | open | approval | no | — | — | [TD-17](#td-17) |\n| TD-18, TD-19 |'; printf '\n### TD-17\n**Issue:** reused.\n' >> "$d/docs/ai-context/technical-debt.md"
expect "assigning TD-17 is an error" "$d" 1 '^ERROR \[id.sequence\] docs/ai-context/technical-debt.md:[0-9]+: TD-17 is a never-assigned ID and must not be used'
d="$(case_dir)"; echo 'See TD-7.' >> "$d/docs/ai-context/repository-map.md"
expect "a malformed ID is an error" "$d" 1 '^ERROR \[id.undefined\] docs/ai-context/repository-map.md:[0-9]+: TD-7 is malformed'
d="$(case_dir)"; drop "$d/docs/ai-context/history/decision-log.md" '### D2'
expect "an index row without its record is an error" "$d" 1 '^ERROR \[id.index\] docs/ai-context/decisions.md:[0-9]+: D2 has no record'
# A later decision log (history/decision-log-<suffix>.md, since D79): the index links to the log that holds the record
move_d2() { drop "$1/docs/ai-context/history/decision-log.md" '### D2'; drop "$1/docs/ai-context/history/decision-log.md" 'Decision two.'
  printf '# Decision log from D2\n\n> **Historical record — not current authority.** The decision records from D2.\n\n### D2\nDecision two.\n' > "$1/docs/ai-context/history/decision-log-d2.md"; }
d="$(case_dir)"; move_d2 "$d"; replace "$d/docs/ai-context/decisions.md" '[D2](history/decision-log.md#d2)' '[D2](history/decision-log-d2.md#d2)'
expect "a decision record in a later decision log, linked from the index, passes" "$d" 0 '^check-docs: 0 error\(s\), 0 warning\(s\)'
d="$(case_dir)"; move_d2 "$d"
expect "a decision index link to the wrong decision log is an error" "$d" 1 '^ERROR \[id.index\] docs/ai-context/decisions.md:[0-9]+: D2: the index links to docs/ai-context/history/decision-log.md but the record is in docs/ai-context/history/decision-log-d2.md'
d="$(case_dir)"; replace "$d/docs/ai-context/technical-debt.md" '[TD-01](#td-01)' '[TD-01](history/technical-debt.md#td-01)'
expect "a TD index link to the wrong file is an error" "$d" 1 '^ERROR \[id.index\] docs/ai-context/technical-debt.md:[0-9]+: TD-01: the index links to docs/ai-context/history/technical-debt.md but the record is in docs/ai-context/technical-debt.md'
d="$(case_dir)"; replace "$d/docs/ai-context/open-questions.md" '| #1 | First? | open | yes |' '| #1 | First? | open | no |'
expect "a question marked not open with a current record is an error" "$d" 1 '^ERROR \[id.index\] docs/ai-context/open-questions.md:[0-9]+: #1: its record link should be'
d="$(case_dir)"; replace "$d/docs/ai-context/feature-inventory.md" 'The features F1 to F2.' 'The features F1 to F3.'
expect "a range statement past the family's end is an error" "$d" 1 '^ERROR \[id.range\] docs/ai-context/feature-inventory.md:[0-9]+: "F1 to F3": the family now runs to F2'
d="$(case_dir)"; replace "$d/docs/ai-context/decisions.md" 'next free ID, D3.' 'next free ID, D2.'
expect "a stale next-free declaration is an error" "$d" 1 '^ERROR \[id.next-free\] docs/ai-context/decisions.md:[0-9]+: declares D2 as a new ID, but it is in use'
d="$(case_dir)"; replace "$d/docs/ai-context/open-questions.md" 'A new question takes #3.' 'A new question takes #5.'
expect "a next-free declaration that skips IDs is an error" "$d" 1 '^ERROR \[id.next-free\] docs/ai-context/open-questions.md:[0-9]+: declares #5 as the next ID, but the next is #3'
d="$(case_dir)"; drop "$d/docs/ai-context/current-state.md" '### V011-2'
expect "a missing V011 requirement is an error" "$d" 1 '^ERROR \[id.range\] CLAUDE.md:[0-9]+: "V011-1 to V011-2": the family now runs to V011-1'

# ---------------------------------------------------------------------------------------------
# Negative controls: routing and authority
# ---------------------------------------------------------------------------------------------
d="$(case_dir)"; printf '# Glossary\n\nAuthority: current. Words.\n' > "$d/docs/ai-context/glossary.md"
expect "a current file in no route and not in the authority map is an error" "$d" 1 '^ERROR \[route.task\] docs/ai-context/glossary.md: no route'
d="$(case_dir)"; replace "$d/docs/ai-context/README.md" '2. The documents the routing table names.' '2. The documents the routing table names and `history/README.md`.'
expect "history in the mandatory reading is an error" "$d" 1 '^ERROR \[route.mandatory\] docs/ai-context/README.md:[0-9]+: the mandatory reading names a history file'
d="$(case_dir)"; replace "$d/docs/ai-context/README.md" '| Rules | `engineering-rules.md` (ER-01 to ER-02) |' $'| Rules | `engineering-rules.md` (ER-01 to ER-02) |\n| Rules | `CLAUDE.md` |'
expect "a subject with two authorities is an error" "$d" 1 '^ERROR \[route.map\] docs/ai-context/README.md:[0-9]+: subject "Rules" has two rows'
d="$(case_dir)"; drop "$d/CLAUDE.md" '- ER-02 '
expect "an engineering rule missing from CLAUDE.md's invariant index is an error" "$d" 1 '^ERROR \[route.rules\] CLAUDE.md: the invariant index has no line for ER-02'
d="$(case_dir)"; replace "$d/docs/ai-context/README.md" '| Feature work | ER-01, ER-02;' '| Feature work | ER-01;'
expect "an engineering rule no route names is an error" "$d" 1 '^ERROR \[route.task\] docs/ai-context/engineering-rules.md:[0-9]+: ER-02 is named by no route'
d="$(case_dir)"; drop "$d/docs/ai-context/history/decision-log.md" 'Historical record'
expect "a history file without the historical banner is an error" "$d" 1 '^ERROR \[auth.history\] docs/ai-context/history/decision-log.md'
d="$(case_dir)"; echo 'Authority: current. This file decides.' >> "$d/docs/ai-context/history/releases.md"
expect "a history file that claims authority is an error" "$d" 1 '^ERROR \[auth.history\] docs/ai-context/history/releases.md:[0-9]+: a history file must not claim authority'
d="$(case_dir)"; replace "$d/docs/ai-context/README.md" '| Release operation | RP §Release workflow; current-state; TD-03 | MP §Procedures;' '| Release operation | RP §Release workflow; current-state; TD-03 | records'
expect "a release-operation route without the maintainer procedures is an error" "$d" 1 '^ERROR \[rel.routing\] docs/ai-context/README.md:[0-9]+: the Release operation route does not name maintainer-procedures.md'

# ---------------------------------------------------------------------------------------------
# Negative controls: current state, release history and migration records
# ---------------------------------------------------------------------------------------------
d="$(case_dir)"; drop "$d/docs/ai-context/current-state.md" 'TD-03, live'
expect "a blocking item missing from §Release blockers is an error (the TD-57 rule)" "$d" 1 '^ERROR \[state.blocker\] docs/ai-context/current-state.md: TD-03 blocks the next stable publication'
expect "... and no release route then needs to name it" "$d" 1 'TD-03 cites §Release blockers but is not there'
d="$(case_dir)"; drop "$d/docs/ai-context/current-state.md" 'TD-02, unresolved'
expect "a risk item missing from §Open risks is an error" "$d" 1 '^ERROR \[state.blocker\] docs/ai-context/current-state.md: TD-02 cites §Open risks but is not there'
d="$(case_dir)"; drop "$d/docs/ai-context/current-state.md" 'No third preview'
expect "a missing roadmap boundary is an error" "$d" 1 '^ERROR \[state.marker\] docs/ai-context/current-state.md: section "Roadmap boundaries" no longer says: No third preview'
d="$(case_dir)"; replace "$d/docs/ai-context/current-state.md" '**`v0.1.0`, Latest' '**`v0.0.9`, Latest'; replace "$d/docs/ai-context/current-state.md" 'Tag `v0.1.0`' 'Tag `v0.0.9`'
expect "a latest release that is not the latest stable tag is an error" "$d" 1 '^ERROR \[state.release\] docs/ai-context/current-state.md: §Latest release does not name v0.1.0'
d="$(case_dir)"; replace "$d/docs/ai-context/current-state.md" 'GitHub release 1002' 'GitHub release 1009'
expect "a release ID that differs from the release index is an error" "$d" 1 '^ERROR \[state.release\] docs/ai-context/current-state.md: release ID 1009 differs'
d="$(case_dir)"; (cd "$d" && GIT_COMMITTER_DATE=2026-01-03T00:00:00Z git -c tag.gpgsign=false tag -a v0.2.0 -m v0.2.0)
expect "a newer stable tag that current-state.md does not name is an error" "$d" 1 '^ERROR \[state.release\] docs/ai-context/current-state.md: §Latest release does not name v0.2.0'
expect "... and the release index must list it" "$d" 1 '^ERROR \[rel.index\] docs/ai-context/history/README.md: local tag v0.2.0 has no row'
d="$(case_dir)"; replace "$d/docs/ai-context/history/README.md" '1. Done (PR #5)' '1. Done (PR #6)'
expect "a done phase whose pull request current-state.md does not record is an error" "$d" 1 '^ERROR \[state.phase\] docs/ai-context/current-state.md: phase 1 is done \(PR #6'
d="$(case_dir)"; replace "$d/docs/ai-context/history/README.md" '2. Checks.' '3. Checks.'
expect "planned phases out of order are an error" "$d" 1 '^ERROR \[state.phase\] docs/ai-context/history/README.md:[0-9]+: planned phase 3 follows phase 1'
d="$(case_dir)"; m="$(cd "$d" && git rev-parse --short=7 master)"; replace "$d/docs/ai-context/current-state.md" "merged as \`$m\`" 'merged as `0000000`'
expect "a recorded merge commit that is not in Git is an error" "$d" 1 '^ERROR \[mig.pr\] docs/ai-context/current-state.md:[0-9]+: PR #5: merge commit 0000000 is not in the local history'
d="$(case_dir)"; f="$(cd "$d" && git rev-parse --short=7 feature)"; s="$(cd "$d" && git rev-parse --short=7 master~1)"; replace "$d/docs/ai-context/current-state.md" "(PR #5, \`$f\`" "(PR #5, \`$s\`"
expect "a recorded head that is not the merged head is an error" "$d" 1 '^ERROR \[mig.pr\] docs/ai-context/current-state.md:[0-9]+: PR #5: the merged head is'
d="$(case_dir)"; replace "$d/docs/ai-context/history/README.md" "| old core, §Solution layout | \`../current-state.md\` | current |" "| old core, §Solution layout | \`../current-state.md\` |  |"
expect "a migration-map row without an authority classification is an error" "$d" 1 '^ERROR \[mig.authority\] docs/ai-context/history/README.md:[0-9]+'
d="$(case_dir)"; f="$d/docs/ai-context/history/README.md"
"$AWK" '/^\| `v0\.1\.0-beta\.1` \|/ { held = $0; next } { print } /^\| `v0\.1\.0` \|/ && held != "" { print held; held = "" }' "$f" > "$f.tmp" && mv "$f.tmp" "$f"
expect "a release index out of order is an error" "$d" 1 '^ERROR \[rel.index\] docs/ai-context/history/README.md:[0-9]+: v0.1.0-beta.1 is out of order'
d="$(case_dir)"; replace "$d/docs/ai-context/history/releases.md" '**`v0.1.0`, published' '**`v0.1.1`, published'
expect "a release history that differs from the index is an error" "$d" 1 '^ERROR \[rel.index\] docs/ai-context/history/releases.md: §Release history.s entries'
d="$(case_dir)"; replace "$d/docs/ai-context/history/README.md" 'The lightweight tag `v0.1-old`' 'The lightweight tag'
expect "a local tag the release index does not mention is an error" "$d" 1 '^ERROR \[rel.index\] docs/ai-context/history/README.md: local tag v0.1-old is not mentioned'
d="$(case_dir)"; o="$(grep -o '`[0-9a-f]\{8\}` → ' "$d/docs/ai-context/history/README.md" | tail -1 | cut -c2-9)"; replace "$d/docs/ai-context/history/README.md" "\`$o\` → " '`00000000` → '
expect "a tag object that differs from the local tag is an error" "$d" 1 '^ERROR \[rel.index\] docs/ai-context/history/README.md:[0-9]+: v0.1.0: the tag object is'
d="$(case_dir)"; rm -rf "$d/.git"
expect "without Git metadata the Git checks are skipped and said so" "$d" 0 'no Git metadata'

# ---------------------------------------------------------------------------------------------
# Negative controls: tests
# ---------------------------------------------------------------------------------------------
d="$(case_dir)"; replace "$d/docs/ai-context/test-coverage.md" '| `FixtureTests/AlphaTests.cs` | 2 | 3 | — |' '| `FixtureTests/AlphaTests.cs` | 3 | 3 | — |'
expect "a test count that differs from the source is an error" "$d" 1 '^ERROR \[test.count\] docs/ai-context/test-coverage.md:[0-9]+: AlphaTests: 3 tests here, 2 in the source'
d="$(case_dir)"; printf 'namespace FixtureTests;\n\npublic class BetaTests\n{\n    [Test]\n    public void Only() { }\n}\n' > "$d/FixtureTests/BetaTests.cs"
expect "a test class without a row is an error" "$d" 1 '^ERROR \[test.project\] docs/ai-context/test-coverage.md: BetaTests \(FixtureTests, FixtureTests/BetaTests.cs\) has 1 tests but no row'
d="$(case_dir)"; replace "$d/docs/ai-context/test-coverage.md" '| `FixtureTests` | 1 | 2 | 3 | 3 passed |' '| `FixtureTests` | 1 | 2 | 4 | 4 passed |'
expect "totals that do not add up are an error" "$d" 1 '^ERROR \[test.count\] docs/ai-context/test-coverage.md:[0-9]+: FixtureTests: §Totals says 1 classes, 2 tests, 4 cases'
d="$(case_dir)"; mkdir -p "$d/GammaTests" && echo '<Project />' > "$d/GammaTests/GammaTests.csproj" && printf 'public class GammaTests\n{\n    [Test]\n    public void One() { }\n}\n' > "$d/GammaTests/GammaTests.cs"
expect "a test project missing from test-coverage.md is an error" "$d" 1 '^ERROR \[test.project\] docs/ai-context/test-coverage.md: test project GammaTests has no class'
d="$(case_dir)"; echo '- `AlphaTests` (3 cases): the alpha tests.' >> "$d/docs/ai-context/repository-map.md"
expect "a test inventory with counts outside test-coverage.md is an error" "$d" 1 '^ERROR \[test.inventory\] docs/ai-context/repository-map.md:[0-9]+'
cat > "$work/ok.trx" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<TestRun>
  <Results>
    <UnitTestResult executionId="e1" testId="a1" testName="First" outcome="Passed" />
    <UnitTestResult executionId="e2" testId="a2" testName="Second(1)" outcome="Passed" />
    <UnitTestResult executionId="e3" testId="a3" testName="Second(2)" outcome="Passed" />
  </Results>
  <TestDefinitions>
    <UnitTest name="First" id="a1">
      <TestMethod className="FixtureTests.AlphaTests" name="First" />
    </UnitTest>
    <UnitTest name="Second(1)" id="a2">
      <TestMethod className="FixtureTests.AlphaTests" name="Second" />
    </UnitTest>
    <UnitTest name="Second(2)" id="a3">
      <TestMethod className="FixtureTests.AlphaTests" name="Second" />
    </UnitTest>
  </TestDefinitions>
</TestRun>
EOF
mkdir -p "$work/trx-ok" "$work/trx-bad" && cp "$work/ok.trx" "$work/trx-ok/" && grep -v 'a3' "$work/ok.trx" > "$work/trx-bad/bad.trx"
expect "--trx-dir: case counts that agree with the results pass" "$work/base" 0 '' --trx-dir "$work/trx-ok"
expect "--trx-dir: a case count that differs from the results is an error" "$work/base" 1 '^ERROR \[test.trx\] docs/ai-context/test-coverage.md:[0-9]+: AlphaTests: 3 cases \(0 skipped\) here, 2' --trx-dir "$work/trx-bad"
printf 'PASS  a\nPASS  b\nPASS  c\nPASS  d\nPASS  e\n' > "$work/policy-ok.log"; printf 'PASS  a\nFAIL  b\n' > "$work/policy-bad.log"
expect "--policy-log: a matching release-policy count passes" "$work/base" 0 '' --policy-log "$work/policy-ok.log"
expect "--policy-log: a different count or a FAIL line is an error" "$work/base" 1 '^ERROR \[test.policy\]' --policy-log "$work/policy-bad.log"

# ---------------------------------------------------------------------------------------------
# Negative controls: data safety, and historical text without false positives
# ---------------------------------------------------------------------------------------------
d="$(case_dir)"; echo '- Check the settings: `cat ~/.local/share/CampertronConfig/General.yaml`.' >> "$d/docs/ai-context/maintainer-procedures.md"
expect "a current command that reads a real user-data folder is an error" "$d" 1 '^ERROR \[safe.command\] docs/ai-context/maintainer-procedures.md:[0-9]+'
d="$(case_dir)"; printf '\n```\nsha256sum "$HOME/Library/Application Support/CampertronConfig/RecreationDotOrg.db"\n```\n' >> "$d/docs/ai-context/maintainer-procedures.md"
expect "a fenced command that hashes a real user-data file is an error" "$d" 1 '^ERROR \[safe.command\] docs/ai-context/maintainer-procedures.md:[0-9]+'
expect "prohibitions, isolated paths and history do not trip the data-safety check" "$work/base" 0 ''
d="$(case_dir)"; printf '\nThe old record names §Never existed, TD-17 to TD-19 as a gap and runs `ls ~/.local/share/CampertronCache`.\n' >> "$d/docs/ai-context/history/releases.md"
expect "historical text (old section names, the TD gap, old commands) gives no false positive" "$d" 0 ''

# ---------------------------------------------------------------------------------------------
# The CI integration: the steps of .github/workflows/ci.yml that run the checks. A green run cannot
# show that the step would still fail on an error, so its exit-code handling runs here with a stub.
# ---------------------------------------------------------------------------------------------
ci="$repo/.github/workflows/ci.yml"
# ci_step NAME: that step's `run: |` block from ci.yml, dedented (as test-release-policy.sh reads release.yml)
ci_step() {
  "$AWK" -v name="$1" '
    function indent(s) { match(s, /^ */); return RLENGTH }
    !found && $0 ~ /^ *- name: / && substr($0, index($0, "- name: ") + 8) == name { found = 1; step = indent($0); next }
    found && !inrun { if ($0 ~ /^ *- name: / && indent($0) <= step) exit; if ($0 ~ /^ *run: [|]$/) { inrun = 1; key = indent($0) }; next }
    inrun { if ($0 ~ /^ *$/) { blank = blank "\n"; next }; if (indent($0) <= key) exit
            if (!body) body = indent($0); printf "%s%s\n", blank, substr($0, body + 1); blank = "" }
    END { exit !(found && inrun) }' "$ci"
}
# ci_run NAME DIR [VAR=VALUE...]: run that step in DIR with bash -e, as Actions runs a step; sets $out and $rc
ci_run() {
  local name="$1" dir="$2"; shift 2
  if ! ci_step "$name" > "$work/ci-step.sh"; then out="ci.yml has no step \"$name\" with a run: | block"; rc=97; return; fi
  mkdir -p "$work/ci-temp"; : > "$work/ci-summary.md"
  out="$(cd "$dir" && env RUNNER_TEMP="$work/ci-temp" GITHUB_STEP_SUMMARY="$work/ci-summary.md" "$@" bash -e "$work/ci-step.sh" 2>&1)"; rc=$?
}
mkdir -p "$work/ci-ws/.github/docs-policy"
cat > "$work/ci-ws/.github/docs-policy/check-docs.sh" <<'EOF'
echo "WARN [file.budget] stub.md: a stub finding"
echo "check-docs: 0 error(s), 1 warning(s), 0 allowed by a registered exception; stub"
exit "${STUB_RC:-0}"
EOF
step="Check the documentation policy"
for c in 0 3 1 2 7; do
  ci_run "$step" "$work/ci-ws" STUB_RC="$c"
  case "$c" in 0|3) want=0 ;; *) want="$c" ;; esac
  case "$c" in
    0) pat='^check-docs.sh: no finding \(exit 0\)' ;;
    3) pat='^::notice title=Documentation policy::check-docs: 0 error\(s\), 1 warning\(s\).*warnings only \(exit 3\)' ;;
    1) pat='^::error title=Documentation policy::check-docs.sh reported errors \(exit 1\)' ;;
    2) pat='^::error title=Documentation policy::check-docs.sh stopped with a usage or environment problem \(exit 2\)' ;;
    *) pat='^::error title=Documentation policy::check-docs.sh exited with an unexpected code \(7\)' ;;
  esac
  if [ "$rc" = "$want" ] && printf '%s\n' "$out" | grep -qE -- "$pat" && printf '%s\n' "$out" | grep -q '^WARN \[file.budget\] stub.md' && grep -q '^WARN \[file.budget\] stub.md' "$work/ci-summary.md"; then
    pass "ci.yml: checker exit $c gives step exit $want, with the full output in the log and the job summary"
  else fail "ci.yml: checker exit $c (step exit $rc, expected $want)" "$out"; fi
done
job="$("$AWK" '/^  [A-Za-z0-9_-]+:$/ { on = ($0 == "  build-test:") } on' "$ci")"
pos() { printf '%s\n' "$job" | grep -n -F -x -- "      - name: $1" | head -1 | cut -d: -f1; }
p1="$(pos "Show the documentation-policy environment")"; p2="$(pos "Test the documentation-policy checker")"; p3="$(pos "Check the documentation policy")"; p4="$(pos "Set up .NET 11 SDK")"
if printf '%s\n' "$job" | grep -q -x '    name: Restore, build, test, vulnerability scan' && printf '%s\n' "$job" | grep -q -x '          fetch-depth: 0' \
   && printf '%s\n' "$job" | grep -q -x '        run: bash .github/docs-policy/test-check-docs.sh' && ! printf '%s\n' "$job" | grep -q 'continue-on-error' \
   && [ -n "$p1" ] && [ -n "$p2" ] && [ -n "$p3" ] && [ -n "$p4" ] && [ "$p1" -lt "$p2" ] && [ "$p2" -lt "$p3" ] && [ "$p3" -lt "$p4" ]; then
  pass "ci.yml: the required job checks out the full history and runs the checker's tests and the checker before the build, without continue-on-error"
else fail "ci.yml: the required job's checkout and documentation-policy steps" "$job"; fi
step="Show the documentation-policy environment"
ci_run "$step" "$work/base"
if [ "$rc" = 0 ] && printf '%s\n' "$out" | grep -q '^awk for check-docs.sh: /' && printf '%s\n' "$out" | grep -q '^awk version: .' && printf '%s\n' "$out" | grep -q '^Local tags: 3;'; then
  pass "ci.yml: the environment step names the awk and passes with the full history and the tags"
else fail "ci.yml: the environment step with the full history (exit $rc)" "$out"; fi
git clone -q --depth 1 "file://$work/base" "$work/shallow" 2>/dev/null
ci_run "$step" "$work/shallow"
[ "$rc" = 2 ] && printf '%s\n' "$out" | grep -q '^::error title=Documentation policy::the checkout is shallow' && pass "ci.yml: a shallow checkout stops the environment step (exit 2)" || fail "ci.yml: a shallow checkout (exit $rc)" "$out"
git clone -q --no-tags "file://$work/base" "$work/notags" 2>/dev/null
ci_run "$step" "$work/notags"
[ "$rc" = 2 ] && printf '%s\n' "$out" | grep -q '^::error title=Documentation policy::the checkout has no tags' && pass "ci.yml: a checkout without tags stops the environment step (exit 2)" || fail "ci.yml: a checkout without tags (exit $rc)" "$out"

# ---------------------------------------------------------------------------------------------
# This repository
# ---------------------------------------------------------------------------------------------
if [ -f "$repo/CLAUDE.md" ] && [ -d "$repo/docs/ai-context" ]; then
  r1="$(bash "$checker" --root "$repo" --verbose 2>&1)"; rr=$?
  r2="$(cd "$repo/docs" && bash "$checker" --verbose 2>&1)"
  if [ "$rr" = 0 ] || [ "$rr" = 3 ]; then pass "this repository has no error (exit $rr)"; else fail "this repository has no error (exit $rr)" "$(printf '%s\n' "$r1" | grep '^ERROR')"; fi
  [ "$r1" = "$r2" ] && pass "this repository: two runs from different directories give identical output" || fail "this repository: identical output"
fi

echo "=== $passed passed, $failed failed"
[ "$failed" -eq 0 ]
