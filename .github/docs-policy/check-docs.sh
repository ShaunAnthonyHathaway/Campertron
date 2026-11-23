#!/usr/bin/env bash
# check-docs.sh: deterministic documentation-policy checks for Campertron's AI-context documents
# (CLAUDE.md, docs/ai-context/ and docs/ai-context/history/). It reads repository files and local
# Git metadata only: no network, no GitHub or GHCR, and never a user-data folder.
# Usage, the checks, exit codes and how to register an exception: .github/docs-policy/README.md.
set -u
export LC_ALL=C

usage() {
  cat <<'EOF'
Usage: check-docs.sh [--root DIR] [--trx-dir DIR] [--policy-log FILE] [--verbose] [--list-checks]

Checks the AI-context documents of a Campertron repository against its documentation policy.

  --root DIR         the repository to check (default: the Git work tree of the current directory)
  --trx-dir DIR      also compare test-coverage.md's case counts with the .trx files in DIR
  --policy-log FILE  also compare the release-policy test count with a saved run of
                     .github/release-policy/test-release-policy.sh
  --verbose          also list the findings a registered exception allows
  --list-checks      list the checks and exit
  -h, --help         show this help

Exit codes: 0 no finding; 3 warnings only; 1 at least one error; 2 usage or environment problem.
EOF
}

list_checks() {
  cat <<'EOF'
file.core          the core files exist and CLAUDE.md imports exactly the router and current-state.md
file.budget        size and line-count budgets (recommendations: warnings)
file.line-length   no line over the limit in the core and the files created since Phase 1
file.exception     every registered exception is still used
link.target        relative Markdown links resolve to a file or directory of the repository
link.anchor        link fragments resolve to a heading by GitHub's anchor rules
link.path          paths named in current documents (Markdown files) exist
label.section      section labels (§...) resolve to a heading, a bold label, a stub or a record
md.fence           code fences are closed
md.table           no table row outside a table, no row with more cells than its table
md.heading         duplicate headings in current documents; heading text GitHub may slug differently
id.sequence        each ID family is defined once per ID, without gaps (TD-17 to TD-19 stay unused)
id.index           index rows, their links and their records agree
id.undefined       no reference to an ID beyond its family, malformed IDs, no reuse of the TD gap
id.range           "first to last" range statements end at the family's last ID
id.next-free       next-free declarations name the next ID of each family
route.map          every current document is in the router's authority map, each subject once
route.task         every current document is routed by a task; every engineering rule is routed
route.mandatory    no history in the mandatory reading
route.rules        CLAUDE.md's invariant index has one line for each engineering rule
auth.current       current documents state their authority (registered legacy files excepted)
auth.history       history files carry the historical banner and claim no authority
state.release      current-state.md names the latest stable tag, its objects and its release
state.blocker      blocking and risk items of technical-debt.md are in current-state.md
state.marker       required current-state facts (roadmap boundaries, deferred requirements)
state.phase        planned phases are numbered in order and the done ones are recorded
test.project       every test project and test class is in test-coverage.md, once
test.count         test counts agree with the test attributes in the source; totals add up
test.inventory     no test inventory with counts outside test-coverage.md
test.trx           (with --trx-dir) case counts agree with test results
test.policy        (with --policy-log) the release-policy count agrees with a run
rel.index          the release index and the release history agree with the local tags
rel.routing        release routing names the procedures and the blocking items
mig.stub           a moved heading's stub has the destination's heading text
mig.pr             recorded pull requests and merge commits agree with local Git history
mig.authority      migration-map rows carry an authority classification
safe.command       no command in a current document reads a real user-data folder
EOF
}

ROOT_ARG="" TRX_DIR="" POLICY_LOG="" VERBOSE=0
while [ $# -gt 0 ]; do
  case "$1" in
    --root) [ $# -ge 2 ] || { usage >&2; exit 2; }; ROOT_ARG="$2"; shift 2 ;;
    --trx-dir) [ $# -ge 2 ] || { usage >&2; exit 2; }; TRX_DIR="$2"; shift 2 ;;
    --policy-log) [ $# -ge 2 ] || { usage >&2; exit 2; }; POLICY_LOG="$2"; shift 2 ;;
    --verbose) VERBOSE=1; shift ;;
    --list-checks) list_checks; exit 0 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "check-docs: unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [ -n "$ROOT_ARG" ]; then
  ROOT="$(cd "$ROOT_ARG" 2>/dev/null && pwd)" || { echo "check-docs: not a directory: $ROOT_ARG" >&2; exit 2; }
else
  ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || ROOT=""
  [ -n "$ROOT" ] || { echo "check-docs: not inside a Git work tree; run it in the repository or pass --root DIR" >&2; exit 2; }
fi
if [ ! -f "$ROOT/CLAUDE.md" ] || [ ! -d "$ROOT/docs/ai-context" ]; then
  echo "check-docs: $ROOT has no CLAUDE.md and docs/ai-context/: not a Campertron repository" >&2
  exit 2
fi
CONF="$ROOT/.github/docs-policy/docs-policy.conf"
[ -f "$CONF" ] || { echo "check-docs: missing $CONF" >&2; exit 2; }
if [ -n "$TRX_DIR" ] && [ ! -d "$TRX_DIR" ]; then echo "check-docs: not a directory: $TRX_DIR" >&2; exit 2; fi
if [ -n "$POLICY_LOG" ] && [ ! -f "$POLICY_LOG" ]; then echo "check-docs: not a file: $POLICY_LOG" >&2; exit 2; fi
if [ -n "$TRX_DIR" ]; then TRX_DIR="$(cd "$TRX_DIR" && pwd)"; fi
if [ -n "$POLICY_LOG" ]; then POLICY_LOG="$(cd "$(dirname "$POLICY_LOG")" && pwd)/$(basename "$POLICY_LOG")"; fi

AWK="$(command -v gawk 2>/dev/null || command -v awk)"
WORK="$(mktemp -d)" || { echo "check-docs: cannot create a temporary directory" >&2; exit 2; }
trap 'rm -rf "$WORK"' EXIT
F="$WORK/findings.tsv"
: > "$F"
cd "$ROOT" || exit 2

HIST="docs/ai-context/history"
AI="docs/ai-context"

emit() { printf '%s\t%s\t%s\t%s\t%s\n' "$1" "$2" "$3" "$4" "$5" >> "$F"; }

# ---------------------------------------------------------------------------------------------
# Inventory: the repository's files (tracked and untracked but not ignored) and the documents.
# ---------------------------------------------------------------------------------------------
IS_GIT=0
SHALLOW=0
if git -C "$ROOT" rev-parse --git-dir >/dev/null 2>&1 && [ "$(git -C "$ROOT" rev-parse --show-toplevel 2>/dev/null)" = "$ROOT" ]; then
  IS_GIT=1
  git -c core.quotepath=off ls-files --cached --others --exclude-standard | sort -u > "$WORK/files"
else
  find . -type f ! -path './.git/*' | sed 's|^\./||' | sort -u > "$WORK/files"
fi
# Keep only files that exist (a deleted but still indexed file is not there).
while IFS= read -r p; do [ -f "$p" ] && printf '%s\n' "$p"; done < "$WORK/files" > "$WORK/files.ok"
mv "$WORK/files.ok" "$WORK/files"

grep -E '^docs/ai-context/[^/]+\.md$' "$WORK/files" > "$WORK/current" || true
grep -E "^$HIST/.+\\.md\$" "$WORK/files" > "$WORK/history" || true
{ echo CLAUDE.md; cat "$WORK/current" "$WORK/history"; } > "$WORK/scope"
{ echo CLAUDE.md; cat "$WORK/current"; } > "$WORK/currentscope"
grep -E '\.md$' "$WORK/files" > "$WORK/allmd" || true

conf() { "$AWK" -F ' [|] ' -v k="$1" '/^[ \t]*#/ || NF == 0 { next } { sub(/[ \t]+$/, "") } $1 == k' "$CONF"; }

AWKLIB='
function trim(s) { sub(/^[ \t]+/, "", s); sub(/[ \t]+$/, "", s); return s }
function chars(s,   c, n) { c = s; n = gsub(/[\200-\277]/, "", c); return length(s) - n }
function emit(sev, chk, f, l, m) { gsub(/\t/, " ", m); print sev "\t" chk "\t" f "\t" l "\t" m >> FINDINGS }
function normpath(p,   n, a, i, k, st, r) {
  n = split(p, a, "/"); k = 0
  for (i = 1; i <= n; i++) {
    if (a[i] == "" || a[i] == ".") continue
    if (a[i] == "..") { if (k == 0) return "/OUTSIDE"; k--; continue }
    st[++k] = a[i]
  }
  r = ""
  for (i = 1; i <= k; i++) r = r (i > 1 ? "/" : "") st[i]
  return r
}
function dirof(p) { if (p !~ /\//) return ""; sub(/\/[^\/]*$/, "", p); return p }
function slugbase(t,   s, lk, tx) {
  s = t; gsub(/`/, "", s)
  while (match(s, /\[[^]]*\]\([^)]*\)/)) {
    lk = substr(s, RSTART, RLENGTH); tx = lk; sub(/\]\(.*$/, "", tx); sub(/^\[/, "", tx)
    s = substr(s, 1, RSTART - 1) tx substr(s, RSTART + RLENGTH)
  }
  s = tolower(s); gsub(/[\200-\377]/, "", s); gsub(/[^a-z0-9 _-]/, "", s); gsub(/ /, "-", s)
  return s
}
function loadexists(file,   l, d, b) {
  while ((getline l < file) > 0) {
    exists[l] = 1; b = l; sub(/^.*\//, "", b); nbase[b]++; basepath[b] = l
    basefiles[b] = basefiles[b] SUBSEP l
    d = l; while (d ~ /\//) { sub(/\/[^\/]*$/, "", d); isdir[d] = 1 }
  }
  close(file)
}
function resolvetok(tok, from,   c, b) {
  if (tok ~ /^[a-z]+:/ || tok ~ /^\//) return ""
  c = normpath((from == "" ? "" : from "/") tok); if (c in exists) return c
  c = normpath("docs/ai-context/" tok); if (c in exists) return c
  c = normpath(tok); if (c in exists) return c
  c = normpath("docs/ai-context/history/" tok); if (c in exists) return c
  b = tok; sub(/^.*\//, "", b)
  if (tok == b && nbase[b] == 1) return basepath[b]
  if (tok == b && nbase[b] > 1) return "AMBIGUOUS"
  return ""
}
function semkey(v,   core, pre, a, n, i, k, ids) {
  sub(/^v/, "", v); core = v; pre = ""
  if (index(v, "-") > 0) { core = substr(v, 1, index(v, "-") - 1); pre = substr(v, index(v, "-") + 1) }
  n = split(core, a, "."); k = ""
  for (i = 1; i <= 3; i++) k = k sprintf("%010d.", a[i] + 0)
  if (pre == "") return k "~"
  n = split(pre, ids, ".")
  for (i = 1; i <= n; i++) k = k (ids[i] ~ /^[0-9]+$/ ? "0" sprintf("%010d", ids[i] + 0) : "1" ids[i]) "."
  return k
}
'

# The configuration (one array per record kind is built where it is needed).
LIMIT="$(conf line-limit | "$AWK" -F ' [|] ' 'NR == 1 { print $2 }')"
[ -n "$LIMIT" ] || LIMIT=1000

# ---------------------------------------------------------------------------------------------
# Extraction: one pass over every Markdown file of the repository.
# ---------------------------------------------------------------------------------------------
X="$WORK/x"; mkdir -p "$X"
: > "$X/headings.tsv"; : > "$X/links.tsv"; : > "$X/bold.tsv"; : > "$X/sect.tsv"; : > "$X/spans.tsv"
: > "$X/code.tsv"; : > "$X/long.tsv"; : > "$X/plain.tsv"
if [ -s "$WORK/allmd" ]; then
  # shellcheck disable=SC2046
  "$AWK" -v OUT="$X" -v LIMIT="$LIMIT" -v FINDINGS="$F" -v HIST="$HIST/" "$AWKLIB"'
function cells(s,   t, n) { t = trim(s); gsub(/\\\|/, "", t); n = gsub(/\|/, "|", t); return (t ~ /\|$/) ? n - 1 : n }
function flushtable() {
  if (inblock && !sepok) emit("ERROR", "md.table", prevfile, bstart, "a line starting with \"|\" that is not part of a table (" bcount " line(s) without a header separator); it renders as text")
  inblock = 0
}
function endfile() {
  if (prevfile == "") return
  flushtable()
  if (fence) emit("ERROR", "md.fence", prevfile, fstart, "code fence never closed: the rest of the file renders as code")
}
FNR == 1 { endfile(); prevfile = FILENAME; fence = 0; split("", occ); inblock = 0 }
{
  line = $0
  if (match(line, /^(   |  | )?(```|~~~)/)) {
    tok = substr(line, RSTART + RLENGTH - 3, 3)
    flushtable()
    if (!fence) { fence = 1; ftok = tok; fstart = FNR }
    else if (tok == ftok && line ~ /^[ ]*(```|~~~)[`~]*[ \t]*$/) fence = 0
    next
  }
  if (fence) { print FILENAME "\t" FNR "\t" line > (OUT "/code.tsv"); next }
  n = chars(line)
  if (n > LIMIT) print FILENAME "\t" FNR "\t" n > (OUT "/long.tsv")
  # Tables
  if (line ~ /^(   |  | )?\|/) {
    if (!inblock) { inblock = 1; bstart = FNR; bcount = 1; sepok = 0 }
    else {
      bcount++
      if (bcount == 2) {
        if (line ~ /^[ |:-]+$/ && line ~ /-/) { sepok = 1; ncols = cells(line) }
      } else if (sepok && cells(line) > ncols) {
        emit("ERROR", "md.table", FILENAME, FNR, "table row has " cells(line) " cells, its table " ncols ": the extra cells are dropped")
      }
    }
  } else flushtable()
  # Headings (ATX)
  if (match(line, /^#+ /) && RLENGTH <= 7) {
    lvl = RLENGTH - 1; txt = substr(line, RLENGTH + 1); sub(/[ \t]+#+[ \t]*$/, "", txt); txt = trim(txt)
    base = slugbase(txt); s = base
    if (s in occ) { while (s in occ) { occ[base]++; s = base "-" occ[base] } }
    occ[s] = 0
    print FILENAME "\t" FNR "\t" lvl "\t" s "\t" base "\t" txt > (OUT "/headings.tsv")
  }
  # Code spans (recorded) and the text without them
  rest = line; nocode = ""
  while (match(rest, /`[^`]+`/)) {
    print FILENAME "\t" FNR "\t" substr(rest, RSTART + 1, RLENGTH - 2) > (OUT "/spans.tsv")
    nocode = nocode substr(rest, 1, RSTART - 1) "``"; rest = substr(rest, RSTART + RLENGTH)
  }
  nocode = nocode rest
  # Inline links and images
  t = nocode
  while (match(t, /\[[^]]*\]\([^)]*\)/)) {
    d = substr(t, RSTART, RLENGTH); sub(/^.*\]\(/, "", d); sub(/\)$/, "", d); d = trim(d)
    sub(/[ \t].*$/, "", d); sub(/^</, "", d); sub(/>$/, "", d)
    print FILENAME "\t" FNR "\t" d > (OUT "/links.tsv")
    t = substr(t, RSTART + RLENGTH)
  }
  plain = line; gsub(/`/, "", plain); gsub(/\t/, " ", plain)
  print FILENAME "\t" FNR "\t" plain > (OUT "/plain.tsv")
  # Bold runs (labels such as **Failure policy**)
  t = plain
  while (match(t, /\*\*[^*]+\*\*/)) { print FILENAME "\t" FNR "\t" substr(t, RSTART + 2, RLENGTH - 4) > (OUT "/bold.tsv"); t = substr(t, RSTART + RLENGTH) }
  # Section citations: the text before and after each section sign
  t = plain; pos = 0
  while ((i = index(t, "\302\247")) > 0) {
    pos += i
    print FILENAME "\t" FNR "\t" substr(plain, 1, pos - 1) "\t" substr(plain, pos + 2, 160) > (OUT "/sect.tsv")
    t = substr(t, i + 2); pos += 1
  }
}
END { endfile() }
' $(cat "$WORK/allmd")
fi

# Restrict findings of the extraction (tables, fences) to the documents in scope.
"$AWK" -F '\t' 'FILENAME == ARGV[1] { s[$1] = 1; next } !($2 ~ /^md\./) || ($3 in s)' "$WORK/scope" "$F" > "$F.tmp" && mv "$F.tmp" "$F"

# ---------------------------------------------------------------------------------------------
# file.*: the core, the imports, budgets and line length
# ---------------------------------------------------------------------------------------------
conf core | "$AWK" -F ' [|] ' '{ print $2 }' > "$WORK/core"
while IFS= read -r c; do
  [ -f "$c" ] || emit ERROR file.core "$c" 0 "core file missing"
done < "$WORK/core"
[ -f "$HIST/README.md" ] || emit ERROR file.core "$HIST/README.md" 0 "the history index is missing"
grep -E '^@' CLAUDE.md | sed 's/^@//' | sort > "$WORK/imports" || true
grep -v '^CLAUDE.md$' "$WORK/core" | sort > "$WORK/core.noclaude"
comm -13 "$WORK/imports" "$WORK/core.noclaude" | while IFS= read -r c; do emit ERROR file.core CLAUDE.md 0 "CLAUDE.md does not import the core file $c (a line @$c)"; done
comm -23 "$WORK/imports" "$WORK/core.noclaude" | while IFS= read -r c; do emit ERROR file.core CLAUDE.md "$(grep -n -F -x "@$c" CLAUDE.md | head -1 | cut -d: -f1)" "CLAUDE.md imports $c, which is not a core file (history and other files are not mandatory reading)"; done

# Budgets (warnings)
{
  total=0
  while IFS= read -r c; do [ -f "$c" ] && total=$((total + $(wc -c < "$c"))); done < "$WORK/core"
  lim="$(conf budget-core-total | "$AWK" -F ' [|] ' '{ print $2 }')"
  if [ -n "$lim" ] && [ "$total" -gt "$lim" ]; then emit WARN file.budget CLAUDE.md 0 "the mandatory core is $total bytes, over its budget of about $lim (docs/ai-context/README.md §Size budgets)"; fi
  conf budget-file | while IFS= read -r rec; do
    p="$(printf '%s' "$rec" | "$AWK" -F ' [|] ' '{ print $2 }')"; b="$(printf '%s' "$rec" | "$AWK" -F ' [|] ' '{ print $3 }')"; l="$(printf '%s' "$rec" | "$AWK" -F ' [|] ' '{ print $4 }')"
    [ -f "$p" ] || continue
    s=$(wc -c < "$p"); n=$(wc -l < "$p")
    [ "$s" -gt "$b" ] && emit WARN file.budget "$p" 0 "$s bytes, over its budget of about $b"
    if [ "$l" != "-" ] && [ -n "$l" ] && [ "$n" -gt "$l" ]; then emit WARN file.budget "$p" 0 "$n lines, over its budget of about $l"; fi
  done
  cur="$(conf budget-current | "$AWK" -F ' [|] ' '{ print $2 }')"; br="$(conf budget-business-rules | "$AWK" -F ' [|] ' '{ print $2 }')"; hi="$(conf budget-history | "$AWK" -F ' [|] ' '{ print $2 }')"
  conf budget-file | "$AWK" -F ' [|] ' '{ print $2 }' > "$WORK/budgetfiles"
  while IFS= read -r p; do
    grep -qxF "$p" "$WORK/budgetfiles" && continue
    s=$(wc -c < "$p")
    case "$p" in
      "$AI"/business-rules*.md) [ -n "$br" ] && [ "$s" -gt "$br" ] && emit WARN file.budget "$p" 0 "$s bytes, over the budget of about $br for each part of business-rules" ;;
      *) [ -n "$cur" ] && [ "$s" -gt "$cur" ] && emit WARN file.budget "$p" 0 "$s bytes, over the budget of about $cur for a current document" ;;
    esac
  done < "$WORK/current"
  while IFS= read -r p; do
    s=$(wc -c < "$p"); [ -n "$hi" ] && [ "$s" -gt "$hi" ] && emit WARN file.budget "$p" 0 "$s bytes, over the budget of about $hi for a history file"
  done < "$WORK/history"
}

# Line length
conf legacy-long-lines | "$AWK" -F ' [|] ' '{ print $2 }' | sort > "$WORK/legacylong"
conf allow-long-line | "$AWK" -F ' [|] ' '{ print $2 "\t" $3 "\t" $4 }' > "$WORK/allowlong"
: > "$WORK/usedlong"
"$AWK" -F '\t' 'FILENAME == ARGV[1] { s[$1] = 1; next } ($1 in s)' "$WORK/scope" "$X/long.tsv" | while IFS=$'\t' read -r p n ch; do
  if grep -qxF "$p" "$WORK/legacylong"; then
    emit ALLOWED file.line-length "$p" "$n" "$ch characters (the file predates Phase 1 and keeps its long lines)"
    continue
  fi
  h="$(sed -n "${n}p" "$p" | tr -d '\n' | sha256sum | cut -c1-64)"
  reason="$("$AWK" -F '\t' -v p="$p" -v h="$h" '$1 == p && $2 == h { print $3; exit }' "$WORK/allowlong")"
  if [ -n "$reason" ]; then
    emit ALLOWED file.line-length "$p" "$n" "$ch characters ($reason)"
    printf '%s\t%s\n' "$p" "$h" >> "$WORK/usedlong"
  else
    emit ERROR file.line-length "$p" "$n" "$ch characters, over the limit of $LIMIT (break it at a sentence boundary; a moved table row needs a registered exception, sha256 $h)"
  fi
done
"$AWK" -F '\t' 'FILENAME == ARGV[1] { u[$1 "\t" $2] = 1; next } !(($1 "\t" $2) in u) { print $1 "\t" $2 }' "$WORK/usedlong" "$WORK/allowlong" | while IFS=$'\t' read -r p h; do
  emit WARN file.exception "$p" 0 "registered long-line exception $h matches no line: remove it from docs-policy.conf"
done
conf legacy-long-lines | "$AWK" -F ' [|] ' '{ print $2 }' | while IFS= read -r p; do [ -f "$p" ] || emit WARN file.exception "$p" 0 "registered legacy file does not exist: remove it from docs-policy.conf"; done

# ---------------------------------------------------------------------------------------------
# link.*: Markdown links, anchors and named paths
# ---------------------------------------------------------------------------------------------
conf not-a-path | "$AWK" -F ' [|] ' '{ print $2 }' > "$WORK/notapath"
"$AWK" -F '\t' -v FINDINGS="$F" -v FILES="$WORK/files" -v SCOPE="$WORK/scope" -v CUR="$WORK/currentscope" -v HISTREADME="$HIST/README.md" -v NOTAPATH="$WORK/notapath" "$AWKLIB"'
BEGIN {
  loadexists(FILES)
  while ((getline l < SCOPE) > 0) inscope[l] = 1
  while ((getline l < CUR) > 0) iscur[l] = 1
  while ((getline l < NOTAPATH) > 0) notapath[l] = 1
}
FILENAME ~ /headings\.tsv$/ { slugs[$1 "#" $4] = 1; next }
FILENAME ~ /links\.tsv$/ {
  f = $1; d = $3
  if (!(f in inscope) && f != ".github/docs-policy/README.md") next
  if (d == "" || d ~ /^[a-z][a-z0-9+.-]*:/) next
  path = d; frag = ""
  if (index(d, "#") > 0) { path = substr(d, 1, index(d, "#") - 1); frag = substr(d, index(d, "#") + 1) }
  if (path == "") tgt = f
  else {
    tgt = normpath((dirof(f) == "" ? "" : dirof(f) "/") path)
    if (tgt == "/OUTSIDE" || (!(tgt in exists) && !(tgt in isdir))) { emit("ERROR", "link.target", f, $2, "link target does not exist: " d); next }
  }
  if (frag != "" && tgt ~ /\.md$/ && !((tgt "#" frag) in slugs)) emit("ERROR", "link.anchor", f, $2, "no heading of " tgt " has the anchor #" frag)
  next
}
FILENAME ~ /plain\.tsv$/ {
  f = $1
  if (!(f in iscur) && f != HISTREADME) next
  t = $3; gsub(/[a-z]+:\/\/[^ )>]*/, "", t)
  while (match(t, /[A-Za-z0-9_.\/-]*[A-Za-z0-9_-]\.md([^A-Za-z0-9_]|$)/)) {
    tok = substr(t, RSTART, RLENGTH); sub(/[^A-Za-z0-9_]$/, "", tok); t = substr(t, RSTART + RLENGTH)
    if (tok ~ /^\.\.?$/ || tok ~ /^-/ || (tok in notapath)) continue
    r = resolvetok(tok, dirof(f))
    if (r == "") emit("ERROR", "link.path", f, $2, "names " tok ", which does not exist")
  }
}
' "$X/headings.tsv" "$X/links.tsv" "$X/plain.tsv"

# ---------------------------------------------------------------------------------------------
# label.section: section labels resolve
# ---------------------------------------------------------------------------------------------
# A label resolves when the text after the section sign starts with a label of the file it is cited
# in (the nearest file named before it on the line), of the citing file, or of the global labels:
# the lettered records, the business-rules sections, and the labels of build-and-test.md (which keeps
# a stub for each section moved out of it) and of the validation records. A label is a heading's text,
# its text before " (" or ": ", its number or letters ("13. ...", "AZ. ..."), or a bold run. A citation
# may also shorten a heading to its first words ("§Recommended verification"). History files are not
# checked: they name sections as they were then. A label inside a next-free declaration is new.
"$AWK" -F '\t' -v FINDINGS="$F" -v FILES="$WORK/files" -v CUR="$WORK/currentscope" -v HIST="$HIST/" -v BT="$AI/build-and-test.md" "$AWKLIB"'
function addlabel(f, s) { s = trim(s); if (s == "" || ((f SUBSEP s) in haslab)) return; haslab[f, s] = 1; labn[f]++; labv[f, labn[f]] = s }
function matchin(f, a,   i, L, nx, c) {
  if (f == "" || f == "AMBIGUOUS") return 0
  c = a
  if (c ~ /^"/) { c = substr(c, 2); sub(/".*$/, "", c) } else sub(/([;,|()]| and | or |\. ).*$/, "", c)
  c = trim(c)
  for (i = 1; i <= labn[f]; i++) {
    L = labv[f, i]
    if (substr(a, 1, length(L)) == L) { nx = substr(a, length(L) + 1, 1); if (nx !~ /[A-Za-z0-9]/) return 1 }
    if (c != "" && substr(L, 1, length(c)) == c) { nx = substr(L, length(c) + 1, 1); if (nx == "" || nx == " ") return 1 }
  }
  return 0
}
function globalmatch(a,   m, k) {
  if (match(a, /^[A-Z][A-Z]?([^A-Za-z0-9]|$)/)) { m = substr(a, 1, (substr(a, 2, 1) ~ /[A-Z]/) ? 2 : 1); if (m in vletter) return 1 }
  if (match(a, /^[0-9]+([^0-9A-Za-z]|$)/)) { m = a; sub(/[^0-9].*$/, "", m); if (m in brnum) return 1 }
  if (matchin(BT, a)) return 1
  for (k in vfile) if (matchin(k, a)) return 1
  return 0
}
function doctoken(before, from,   n, w, k, wd, r) {
  n = split(before, w, /[ ()\[\],;:"{}<>*]+/)
  for (k = n; k >= 1; k--) {
    wd = w[k]; sub(/\047s$/, "", wd); sub(/[.\047]+$/, "", wd)
    if (wd == "B&T") return BT
    if (wd == "RP") return "docs/ai-context/release-process.md"
    if (wd == "MP") return "docs/ai-context/maintainer-procedures.md"
    if (wd == "router") return "docs/ai-context/README.md"
    if (wd ~ /\.md$/) { r = resolvetok(wd, from); if (r != "") { lasttok = wd; return r } }
    else if (wd ~ /^[A-Za-z0-9_.\/-]+$/ && wd !~ /^[0-9.]+$/) { r = resolvetok(wd ".md", from); if (r != "" && r != "AMBIGUOUS") { lasttok = wd ".md"; return r } }
  }
  return ""
}
BEGIN { loadexists(FILES); while ((getline l < CUR) > 0) iscur[l] = 1 }
FILENAME ~ /headings\.tsv$/ {
  f = $1; t = $6; gsub(/`/, "", t)
  if (index(f, HIST "validation/") == 1) vfile[f] = 1
  addlabel(f, t)
  if (index(t, " (") > 0) addlabel(f, substr(t, 1, index(t, " (") - 1))
  if (index(t, ": ") > 0) addlabel(f, substr(t, 1, index(t, ": ") - 1))
  if (match(t, /^[0-9]+\. /)) { addlabel(f, substr(t, 1, RLENGTH - 2)); if (f ~ /business-rules[a-z-]*\.md$/) brnum[substr(t, 1, RLENGTH - 2)] = 1 }
  if (match(t, /^[A-Z][A-Z]?\. /)) { addlabel(f, substr(t, 1, RLENGTH - 2)); if (index(f, HIST "validation/") == 1 || f == BT) vletter[substr(t, 1, RLENGTH - 2)] = 1 }
  next
}
FILENAME ~ /bold\.tsv$/ { b = $3; addlabel($1, b); sub(/[:.]+$/, "", b); addlabel($1, b); next }
FILENAME ~ /sect\.tsv$/ {
  f = $1
  if (!(f in iscur)) next
  a = $4
  if (a !~ /^[A-Za-z0-9"]/) next
  whole = $3 "\302\247" a; pos = length($3) + 1
  if (match(whole, /(next free|takes? the next|takes? (D|R|TD-|#|ER-|\302\247)[0-9A-Z])/) && RSTART < pos) {
    tp = RSTART; rest = substr(whole, tp); te = match(rest, /\. [A-Z]/) ? tp + RSTART : length(whole) + 1
    if (pos < te) next
  }
  lasttok = ""
  tf = doctoken($3, dirof(f))
  if (matchin(tf, a)) next
  if (lasttok != "") { b = lasttok; sub(/^.*\//, "", b); if (nbase[b] > 1) { n = split(basefiles[b], cand, SUBSEP); ok = 0; for (k = 2; k <= n; k++) if (matchin(cand[k], a)) ok = 1; if (ok) next } }
  if (matchin(f, a) || globalmatch(a)) next
  shown = substr(a, 1, 40); sub(/[ ,.;)]*$/, "", shown)
  emit("ERROR", "label.section", f, $2, "section label \302\247" shown (tf != "" ? " (cited in " tf ")" : "") " matches no heading, bold label, stub or record")
}
' "$X/headings.tsv" "$X/bold.tsv" "$X/sect.tsv"

# ---------------------------------------------------------------------------------------------
# md.heading: duplicate headings in current documents, and heading text GitHub may slug differently
# ---------------------------------------------------------------------------------------------
"$AWK" -F '\t' -v FINDINGS="$F" "$AWKLIB"'
FILENAME == ARGV[1] { cur[$1] = 1; next }
($1 in cur) {
  if (($1 SUBSEP $5) in seen) emit("WARN", "md.heading", $1, $2, "heading \"" $6 "\" repeats the heading of line " seen[$1, $5] ": its anchor is #" $4 ", not #" $5)
  else seen[$1, $5] = $2
  t = $6
  gsub(/\342\200[\223\224\230\231\234\235\246]|\342\206\222|\302\247|\302\267|\303\227|\342\211[\244\245]/, "", t)
  if (t ~ /[\200-\377]/) emit("WARN", "md.heading", $1, $2, "heading has non-ASCII text whose GitHub anchor this checker may compute differently")
}
' "$WORK/currentscope" "$X/headings.tsv"

# ---------------------------------------------------------------------------------------------
# id.*: identifier families (D, R, TD, questions, ER, F, V011, the lettered records, business-rules
# sections): definitions, index rows and records, references, range statements, next-free IDs
# ---------------------------------------------------------------------------------------------
GAPS="$(conf id-gap | "$AWK" -F ' [|] ' '$2 == "TD" { for (i = $3; i <= $4; i++) printf "%d ", i }')"
"$AWK" -F '\t' -v FINDINGS="$F" -v GAPS="$GAPS" -v HIST="$HIST/" -v AI="$AI/" -v CUR="$WORK/currentscope" -v SCOPE="$WORK/scope" "$AWKLIB"'
function letters2n(s,   n, i) { n = 0; for (i = 1; i <= length(s); i++) n = n * 26 + index("ABCDEFGHIJKLMNOPQRSTUVWXYZ", substr(s, i, 1)); return n }
function n2letters(n,   s) { s = ""; while (n > 0) { s = substr("ABCDEFGHIJKLMNOPQRSTUVWXYZ", (n - 1) % 26 + 1, 1) s; n = int((n - 1) / 26) }; return s }
function cellsof(s, c,   n) { s = trim(s); sub(/^\|/, "", s); sub(/\|$/, "", s); n = split(s, c, /\|/); return n }
function label(fam, n) {
  if (fam == "D" || fam == "R" || fam == "F") return fam n
  if (fam == "TD" || fam == "ER") return fam "-" sprintf("%02d", n)
  if (fam == "Q") return "#" n
  if (fam == "V") return "V011-" n
  if (fam == "VL") return "\302\247" n2letters(n)
  return "\302\247" n
}
function def(fam, n, f, l) {
  if ((fam SUBSEP n) in defl) emit("ERROR", "id.sequence", f, l, label(fam, n) " is defined twice (also " deff[fam, n] ":" defl[fam, n] ")")
  else { defl[fam, n] = l; deff[fam, n] = f }
  if (!(fam in max) || n > max[fam]) max[fam] = n
}
BEGIN {
  split(GAPS, g, " "); for (i in g) if (g[i] != "") gap[g[i] + 0] = 1
  while ((getline l < CUR) > 0) iscur[l] = 1
  while ((getline l < SCOPE) > 0) inscope[l] = 1
  labelfrom = -1; labelto = -1
}
FILENAME ~ /headings\.tsv$/ {
  f = $1; t = $6
  if (f == AI "engineering-rules.md" && match(t, /^ER-[0-9][0-9]( |$)/)) def("ER", substr(t, 4, 2) + 0, f, $2)
  if (f == AI "feature-inventory.md" && match(t, /^F[0-9]+\. /)) def("F", substr(t, 2, RLENGTH - 3) + 0, f, $2)
  if (f == AI "current-state.md" && match(t, /^V011-[0-9]+( |$)/)) { x = t; sub(/^V011-/, "", x); sub(/[^0-9].*$/, "", x); def("V", x + 0, f, $2) }
  if (index(f, HIST "validation/") == 1 && match(t, /^[A-Z][A-Z]?\. /)) def("VL", letters2n(substr(t, 1, RLENGTH - 2)), f, $2)
  if (f ~ /^docs\/ai-context\/business-rules(-settings|-gui)?\.md$/ && match(t, /^[0-9]+\. /)) { nbr++; brf[nbr] = f; brl[nbr] = $2; brn[nbr] = substr(t, 1, RLENGTH - 2) + 0 }
  # decision records: history/decision-log.md (D1 to D78) and, since D79, further logs history/decision-log-<suffix>.md
  if (index(f, HIST "decision-log") == 1 && substr(f, length(HIST) + 1) ~ /^decision-log(-[a-z0-9]+)?\.md$/ && t ~ /^D[0-9]+$/) { n = substr(t, 2) + 0; if (n in drec) emit("ERROR", "id.sequence", f, $2, "record D" n " appears twice (also " drecf[n] ":" drec[n] ")"); drec[n] = $2; drecf[n] = f }
  if ((f == AI "technical-debt.md" || f == HIST "technical-debt.md") && t ~ /^TD-[0-9]+$/) { n = substr(t, 4) + 0; if (n in tdrec) emit("ERROR", "id.sequence", f, $2, "record " t " appears twice"); tdrec[n] = f }
  if ((f == AI "open-questions.md" || f == HIST "open-questions.md") && t ~ /^#[0-9]+$/) { n = substr(t, 2) + 0; if (n in qrec) emit("ERROR", "id.sequence", f, $2, "record " t " appears twice"); qrec[n] = f }
  if (f == HIST "README.md" && $3 == 2) { if (t == "Label policy") labelfrom = $2; else if (labelfrom > 0 && labelto < 0 && t != "Label index") labelto = $2 - 1 }
  next
}
{
  f = $1; l = $2; t = $3
  if (f ~ /^docs\/ai-context\/business-rules(-settings|-gui)?\.md$/) brtext[f, l] = t
  if (f == AI "decisions.md" && match(t, /^\| D[0-9]+ \|/)) {
    n = substr(t, 4, RLENGTH - 5) + 0; def("D", n, f, l); nc = cellsof(t, c); dindex[n] = l
    lk = trim(c[nc]); pre = "[D" n "](history/"; suf = "#d" n ")"
    x = (index(lk, pre) == 1 && substr(lk, length(lk) - length(suf) + 1) == suf) ? substr(lk, length(pre) + 1, length(lk) - length(pre) - length(suf)) : ""
    if (x ~ /^decision-log(-[a-z0-9]+)?\.md$/) dlink[n] = HIST x
    else emit("ERROR", "id.index", f, l, "D" n ": the record link should be [D" n "](history/decision-log.md#d" n ") or [D" n "](history/decision-log-<suffix>.md#d" n "), naming the log that holds the record")
  }
  if (f == AI "decisions.md" && match(t, /^\| R[0-9]+ \|/)) def("R", substr(t, 4, RLENGTH - 5) + 0, f, l)
  if (f == AI "technical-debt.md" && match(t, /^\| TD-[0-9][0-9] \|/)) {
    n = substr(t, 6, 2) + 0; def("TD", n, f, l); nc = cellsof(t, c); tdindex[n] = trim(c[nc]); tdline[n] = l
  }
  if (f == AI "technical-debt.md" && t ~ /^\| TD-[0-9][0-9], TD-[0-9][0-9]/) {
    cellsof(t, c); m = split(c[1], ids, /,/)
    for (k = 1; k <= m; k++) { x = trim(ids[k]); sub(/^TD-/, "", x); x += 0
      if (!(x in gap)) emit("ERROR", "id.index", f, l, "TD-" sprintf("%02d", x) " is listed as never assigned but is not a registered gap (docs-policy.conf id-gap)")
      gapseen[x] = l }
  }
  if (f == AI "open-questions.md" && match(t, /^\| #[0-9]+ \|/)) {
    n = substr(t, 4, RLENGTH - 5) + 0; def("Q", n, f, l); nc = cellsof(t, c); qopen[n] = trim(c[4]); qlink[n] = trim(c[nc]); qline[n] = l
  }
  if (f == "CLAUDE.md" && match(t, /^- ER-[0-9][0-9] /)) { n = substr(t, 6, 2) + 0; if (n in erline) emit("ERROR", "route.rules", f, l, "ER-" substr(t, 6, 2) " has two lines in the invariant index"); erline[n] = l }
  if (f in inscope) { nr++; rf[nr] = f; rl[nr] = l; rt[nr] = t }
}
function famref(fam, n, f, l, t, intake) {
  if (!(fam in max)) return
  if (intake) { if (!((fam SUBSEP f SUBSEP l) in tkmin) || n < tkmin[fam, f, l]) tkmin[fam, f, l] = n; return }
  if (fam == "TD" && (n in gap)) { if (t !~ /never|gap/) emit("ERROR", "id.undefined", f, l, label(fam, n) " is a never-assigned ID; mention it only as never assigned"); return }
  if (n > max[fam] || n < 1) emit("ERROR", "id.undefined", f, l, label(fam, n) " is not defined (the family runs to " label(fam, max[fam]) ")")
}
function scanrefs(f, l, t, tp, te,   s, off, p, tok, n, m, q) {
  s = " " t " "; off = -1
  while (match(s, /[^A-Za-z0-9_#.\/-](D|R|F)[1-9][0-9]*[^A-Za-z0-9_]/)) {
    p = off + RSTART + 1; tok = substr(s, RSTART + 1, RLENGTH - 2)
    famref(substr(tok, 1, 1), substr(tok, 2) + 0, f, l, t, (tp && p >= tp && p < te))
    off += RSTART + RLENGTH - 2; s = substr(s, RSTART + RLENGTH - 1)
  }
  s = " " t " "; off = -1
  while (match(s, /[^A-Za-z0-9_](TD|ER)-[0-9]+([^0-9]|$)/)) {
    p = off + RSTART + 1; tok = substr(s, RSTART + 1); sub(/[^A-Z0-9-].*$/, "", tok); sub(/[^0-9]$/, "", tok)
    m = index(tok, "-"); n = substr(tok, m + 1)
    if (length(n) != 2) emit("ERROR", "id.undefined", f, l, tok " is malformed: write " substr(tok, 1, m) sprintf("%02d", n + 0))
    famref(substr(tok, 1, m - 1), n + 0, f, l, t, (tp && p >= tp && p < te))
    off += RSTART + RLENGTH - 2; s = substr(s, RSTART + RLENGTH - 1)
  }
  s = " " t
  while (match(s, /[^A-Za-z0-9_]V011-[0-9]+/)) { famref("V", substr(s, RSTART + 6, RLENGTH - 6) + 0, f, l, t, 0); s = substr(s, RSTART + RLENGTH) }
  s = t; off = 0
  while (match(s, /[Qq]uestions? #[0-9]+((, | and | or | to |, and )#[0-9]+)*/)) {
    p = off + RSTART; q = substr(s, RSTART, RLENGTH); off += RSTART + RLENGTH - 1; s = substr(s, RSTART + RLENGTH)
    if (tp && p >= tp && p < te) continue
    while (match(q, /#[0-9]+/)) { famref("Q", substr(q, RSTART + 1, RLENGTH - 1) + 0, f, l, t, 0); q = substr(q, RSTART + RLENGTH) }
  }
  if (tp) {
    s = substr(t, tp, te - tp)
    while (match(s, /#[0-9]+/)) { famref("Q", substr(s, RSTART + 1, RLENGTH - 1) + 0, f, l, t, 1); s = substr(s, RSTART + RLENGTH) }
    s = substr(t, tp, te - tp)
    while (match(s, /\302\247[A-Z][A-Z]?([^A-Za-z0-9]|$)/)) { tok = substr(s, RSTART + 2, 2); sub(/[^A-Z]+$/, "", tok); famref("VL", letters2n(tok), f, l, t, 1); s = substr(s, RSTART + RLENGTH) }
    s = substr(t, tp, te - tp)
    while (match(s, /\302\247[0-9]+/)) { famref("BR", substr(s, RSTART + 2, RLENGTH - 2) + 0, f, l, t, 1); s = substr(s, RSTART + RLENGTH) }
  }
}
function scanranges(f, l, t,   np, pre, fam, k, s, p, rest, n, ok) {
  np = split("D1 to D|R1 to R|TD-01 to TD-|#1 to #|ER-01 to ER-|F1 to F|V011-1 to V011-", pre, "|")
  split("D R TD Q ER F V", fam, " ")
  for (k = 1; k <= np; k++) {
    s = t
    while ((p = index(s, pre[k])) > 0) {
      ok = (p == 1 || substr(s, p - 1, 1) !~ /[A-Za-z0-9_-]/)
      rest = substr(s, p + length(pre[k])); s = rest
      if (ok && match(rest, /^[0-9]+/)) {
        n = substr(rest, 1, RLENGTH) + 0
        if ((fam[k] in max) && n != max[fam[k]]) emit("ERROR", "id.range", f, l, "\"" pre[k] substr(rest, 1, RLENGTH) "\": the family now runs to " label(fam[k], max[fam[k]]))
      }
    }
  }
}
END {
  # Business-rules sections: a heading followed by "Moved to" is a stub, anything else defines the section.
  for (i = 1; i <= nbr; i++) {
    nx = ""; for (k = brl[i] + 1; k <= brl[i] + 5; k++) if (((brf[i] SUBSEP k) in brtext) && trim(brtext[brf[i], k]) != "") { nx = brtext[brf[i], k]; break }
    if (nx ~ /^Moved to /) brstub[brn[i]] = 1
    else def("BR", brn[i], brf[i], brl[i])
  }
  for (n in brstub) if (!(("BR" SUBSEP n) in defl)) emit("ERROR", "id.sequence", AI "business-rules.md", 0, "\302\247" n " has a stub but no section in any business-rules file")
  # Families without gaps (the TD gap stays unused)
  for (fam in max) for (n = 1; n <= max[fam]; n++) {
    if (fam == "TD" && (n in gap)) { if ((fam SUBSEP n) in defl) emit("ERROR", "id.sequence", deff[fam, n], defl[fam, n], "TD-" sprintf("%02d", n) " is a never-assigned ID and must not be used"); continue }
    if (!((fam SUBSEP n) in defl)) emit("ERROR", "id.sequence", (fam == "VL" ? HIST "validation" : "-"), 0, label(fam, n) " is missing: the family runs to " label(fam, max[fam]))
  }
  for (x in gap) if (("TD" in max) && x + 0 <= max["TD"] && !(x in gapseen)) emit("ERROR", "id.index", AI "technical-debt.md", 0, "the index has no row for the never-assigned TD-" sprintf("%02d", x))
  for (x in gap) if (x in tdrec) emit("ERROR", "id.sequence", tdrec[x], 0, "TD-" sprintf("%02d", x) " has a record but was never assigned")
  # Decisions
  for (n in dindex) {
    if (!(n in drec)) emit("ERROR", "id.index", AI "decisions.md", dindex[n], "D" n " has no record (### D" n ") in " HIST "decision-log.md or a later decision log")
    else if ((n in dlink) && dlink[n] != drecf[n]) emit("ERROR", "id.index", AI "decisions.md", dindex[n], "D" n ": the index links to " dlink[n] " but the record is in " drecf[n])
  }
  for (n in drec) if (!(n in dindex)) emit("ERROR", "id.index", drecf[n], drec[n], "record D" n " has no index row in decisions.md")
  # Technical debt
  for (n in tdindex) {
    lk = tdindex[n]; id = "TD-" sprintf("%02d", n)
    if (lk == "[" id "](#" tolower(id) ")") want = AI "technical-debt.md"
    else if (lk == "[" id "](history/technical-debt.md#" tolower(id) ")") want = HIST "technical-debt.md"
    else { emit("ERROR", "id.index", AI "technical-debt.md", tdline[n], id ": the record link should be [" id "](#" tolower(id) ") or [" id "](history/technical-debt.md#" tolower(id) ")"); continue }
    if (!(n in tdrec)) emit("ERROR", "id.index", AI "technical-debt.md", tdline[n], id ": no record (### " id ") in " want)
    else if (tdrec[n] != want) emit("ERROR", "id.index", AI "technical-debt.md", tdline[n], id ": the index links to " want " but the record is in " tdrec[n])
  }
  for (n in tdrec) if (!(n in tdindex) && !(n in gap)) emit("ERROR", "id.index", tdrec[n], 0, "record TD-" sprintf("%02d", n) " has no index row")
  # Open questions
  for (n in qopen) {
    if (qopen[n] == "yes") { want = AI "open-questions.md"; wl = "[#" n "](#" n ")" }
    else if (qopen[n] == "no") { want = HIST "open-questions.md"; wl = "[#" n "](history/open-questions.md#" n ")" }
    else { emit("ERROR", "id.index", AI "open-questions.md", qline[n], "#" n ": the Open column must be yes or no"); continue }
    if (qlink[n] != wl) emit("ERROR", "id.index", AI "open-questions.md", qline[n], "#" n ": its record link should be " wl)
    if (!(n in qrec)) emit("ERROR", "id.index", AI "open-questions.md", qline[n], "#" n ": no record (### #" n ")")
    else if (qrec[n] != want) emit("ERROR", "id.index", AI "open-questions.md", qline[n], "#" n ": the record is in " qrec[n] ", expected in " want)
  }
  for (n in qrec) if (!(n in qopen)) emit("ERROR", "id.index", qrec[n], 0, "record #" n " has no index row")
  # The engineering rules in the invariant index of CLAUDE.md
  if ("ER" in max) {
    for (n = 1; n <= max["ER"]; n++) if (!(n in erline)) emit("ERROR", "route.rules", "CLAUDE.md", 0, "the invariant index has no line for ER-" sprintf("%02d", n))
    for (n in erline) if (n + 0 > max["ER"]) emit("ERROR", "route.rules", "CLAUDE.md", erline[n], "the invariant index names ER-" sprintf("%02d", n) ", which engineering-rules.md does not define")
  }
  # References, range statements and next-free declarations
  for (i = 1; i <= nr; i++) {
    f = rf[i]; l = rl[i]; t = rt[i]; tp = 0; te = 0
    if (match(t, /(next free|takes? the next|takes? (D|R|TD-|#|ER-|\302\247)[0-9A-Z])/)) {
      tp = RSTART; rest = substr(t, tp)
      te = match(rest, /\. [A-Z]/) ? tp + RSTART : length(t) + 1
    }
    scanrefs(f, l, t, tp, te)
    if ((f in iscur) || (f == HIST "README.md" && l >= labelfrom && l <= labelto)) scanranges(f, l, t)
  }
  for (fam in max) emit("INFO", "id.sequence", "-", 0, "family " label(fam, 1) " to " label(fam, max[fam]) (fam == "TD" ? " (with the registered gap)" : ""))
  for (k in tkmin) {
    split(k, kk, SUBSEP); fam = kk[1]; n = tkmin[k]
    if (n <= max[fam]) emit("ERROR", "id.next-free", kk[2], kk[3], "declares " label(fam, n) " as a new ID, but it is in use: the next is " label(fam, max[fam] + 1))
    else if (n != max[fam] + 1) emit("ERROR", "id.next-free", kk[2], kk[3], "declares " label(fam, n) " as the next ID, but the next is " label(fam, max[fam] + 1))
  }
}
' "$X/headings.tsv" "$X/plain.tsv"

# ---------------------------------------------------------------------------------------------
# route.* and auth.*: the router's authority map and task routes; authority statements
# ---------------------------------------------------------------------------------------------
section() { # section FILE HEADING: the lines of the section whose heading text is HEADING (raw)
  "$AWK" -v h="$2" '
    /^(```|~~~)/ { fence = !fence }
    !fence && /^#+ / { lvl = index($0, " ") - 1; t = substr($0, lvl + 2); if (on && lvl <= onlvl) on = 0; if (t == h) { on = 1; onlvl = lvl; next } }
    on { print FNR "\t" $0 }' "$1"
}
ROUTER="$AI/README.md"
section "$ROUTER" "Authority map" > "$WORK/authmap"
section "$ROUTER" "Task routing" > "$WORK/routes"
section "$ROUTER" "Mandatory reading order" > "$WORK/mandatory"
"$AWK" -F '\t' -v FINDINGS="$F" -v ROUTER="$ROUTER" -v AI="$AI/" -v CURRENT="$WORK/current" "$AWKLIB"'
function cellsof(s, c,   n) { s = trim(s); sub(/^\|/, "", s); sub(/\|$/, "", s); n = split(s, c, /\|/); return n }
BEGIN { while ((getline l < CURRENT) > 0) { b = l; sub(/^.*\//, "", b); sub(/\.md$/, "", b); cur[b] = l } }
FILENAME ~ /authmap$/ && $2 ~ /^\|/ && $2 !~ /^\|[- |]+$/ {
  n = cellsof($2, c); if (trim(c[1]) == "Subject") next
  s = trim(c[1]); if (s in subj) emit("ERROR", "route.map", ROUTER, $1, "subject \"" s "\" has two rows (also line " subj[s] ")"); else subj[s] = $1
  t = c[2]
  while (match(t, /`[^`]+\.md`/)) { m = substr(t, RSTART + 1, RLENGTH - 2); t = substr(t, RSTART + RLENGTH); sub(/^.*\//, "", m); sub(/\.md$/, "", m); mapped[m] = 1 }
  if (c[2] ~ /this file/) mapped["README"] = 1
  next
}
FILENAME ~ /routes$/ && $2 ~ /^\|/ && $2 !~ /^\|[- |]+$/ {
  n = cellsof($2, c); if (trim(c[1]) == "Task") next
  t = $2; gsub(/`/, "", t); gsub(/\.md/, "", t)
  m = split(t, w, /[ ;,()\[\]\|"]+/)
  for (k = 1; k <= m; k++) {
    x = w[k]; sub(/[.:]+$/, "", x)
    if (x == "B&T") x = "build-and-test"; if (x == "RP") x = "release-process"; if (x == "MP") x = "maintainer-procedures"
    if (x ~ /^ER-[0-9][0-9]$/) { routed["engineering-rules"] = 1; er[substr(x, 4) + 0] = 1 }
    routed[x] = 1
  }
  while (match(t, /ER-[0-9][0-9] to ER-[0-9][0-9]/)) { a = substr(t, RSTART + 3, 2) + 0; b = substr(t, RSTART + 12, 2) + 0; for (k = a; k <= b; k++) er[k] = 1; t = substr(t, RSTART + RLENGTH) }
  next
}
FILENAME ~ /mandatory$/ {
  if ($2 ~ /^[0-9]+\. / && $2 ~ /history\// && $2 !~ /^[0-9]+\. History only/) emit("ERROR", "route.mandatory", ROUTER, $1, "the mandatory reading names a history file; history is read only when a route, an investigation or a discrepancy needs it")
  next
}
FILENAME ~ /headings\.tsv$/ { if ($1 == AI "engineering-rules.md" && $6 ~ /^ER-[0-9][0-9]/) ers[substr($6, 4, 2) + 0] = $2; next }
END {
  for (b in cur) {
    if (b == "README") continue
    if (!(b in mapped)) emit("ERROR", "route.map", cur[b], 0, "not in the router'"'"'s §Authority map")
    if (!(b in routed)) emit("ERROR", "route.task", cur[b], 0, "no route of the router'"'"'s §Task routing names it")
  }
  for (n in ers) if (!(n in er)) emit("ERROR", "route.task", AI "engineering-rules.md", ers[n], "ER-" sprintf("%02d", n) " is named by no route of the router'"'"'s §Task routing")
}
' "$WORK/authmap" "$WORK/routes" "$WORK/mandatory" "$X/headings.tsv"

conf legacy-no-authority | "$AWK" -F ' [|] ' '{ print $2 "\t" $3 }' > "$WORK/legacyauth"
while IFS= read -r p; do
  if head -n 5 "$p" | grep -q '^Authority:'; then
    if grep -q "^$p	" "$WORK/legacyauth"; then emit WARN file.exception "$p" 0 "has an Authority statement now: remove its legacy-no-authority record"; fi
  else
    reason="$("$AWK" -F '\t' -v p="$p" '$1 == p { print $2; exit }' "$WORK/legacyauth")"
    if [ -n "$reason" ]; then emit ALLOWED auth.current "$p" 1 "no Authority statement ($reason)"
    else emit ERROR auth.current "$p" 1 "no \"Authority:\" statement in its first 5 lines"; fi
  fi
done < "$WORK/current"
if [ -f "$AI/current-state.md" ] && ! head -n 5 "$AI/current-state.md" | grep -q '^Authority: \*\*the only current authority\*\*'; then
  emit ERROR auth.current "$AI/current-state.md" 3 "must state that it is the only current authority for the repository and release state"
fi
while IFS= read -r p; do
  head -n 5 "$p" | grep -q 'Historical record — not current authority' || emit ERROR auth.history "$p" 1 "no \"Historical record — not current authority\" banner in its first 5 lines"
  grep -n '^Authority:' "$p" | cut -d: -f1 | while IFS= read -r n; do emit ERROR auth.history "$p" "$n" "a history file must not claim authority"; done
done < "$WORK/history"

# ---------------------------------------------------------------------------------------------
# state.*: current-state.md against local Git, technical-debt.md, the markers and the phases
# ---------------------------------------------------------------------------------------------
CS="$AI/current-state.md"
CSIN="$CS"; [ -f "$CS" ] || CSIN=/dev/null
# The latest stable release (a SemVer tag without a hyphen) from local Git metadata.
: > "$WORK/tags"
if [ "$IS_GIT" = 1 ]; then git tag -l 'v*' > "$WORK/tags"; fi
"$AWK" "$AWKLIB"'/^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$/ { print semkey($0) "\t" $0 }' "$WORK/tags" | sort > "$WORK/semtags"
latest="$("$AWK" -F '\t' 'index($2, "-") == 0 { v = $2 } END { print v }' "$WORK/semtags")"
if [ -f "$CS" ]; then
  if [ -n "$latest" ]; then
    section "$CS" "Latest release" > "$WORK/cs.latest"
    if ! grep -qF "\`$latest\`" "$WORK/cs.latest"; then
      emit ERROR state.release "$CS" 0 "§Latest release does not name $latest, the latest stable tag in local Git"
    else
      obj="$(git rev-parse -q --verify "refs/tags/$latest")"; com="$(git rev-parse -q --verify "$latest^{commit}")"
      if [ "$(git cat-file -t "$obj")" = tag ] && ! grep -qF "$obj" "$WORK/cs.latest"; then emit ERROR state.release "$CS" 0 "§Latest release does not give $latest's tag object $obj"; fi
      grep -qF "$com" "$WORK/cs.latest" || emit ERROR state.release "$CS" 0 "§Latest release does not give $latest's commit $com"
      rid="$(grep -o 'GitHub release [0-9]\+' "$WORK/cs.latest" | head -1 | grep -o '[0-9]\+$')"
      [ -n "$rid" ] || emit ERROR state.release "$CS" 0 "§Latest release gives no \"GitHub release <ID>\""
      grep -q 'Latest' "$WORK/cs.latest" || emit ERROR state.release "$CS" 0 "§Latest release does not say which release is Latest"
      irow="$(section "$HIST/README.md" "Release index" | grep -F "| \`$latest\` |" | head -1)"
      if [ -n "$rid" ] && [ -n "$irow" ] && ! printf '%s' "$irow" | grep -qF "| $rid |"; then emit ERROR state.release "$CS" 0 "release ID $rid differs from the history index's §Release index row for $latest"; fi
    fi
  fi
  # Blocking and risk items of technical-debt.md
  section "$CS" "Release blockers" > "$WORK/cs.blockers"
  section "$CS" "Open risks" > "$WORK/cs.risks"
  "$AWK" -F '\t' -v FINDINGS="$F" -v CS="$CS" -v TDF="$AI/technical-debt.md" "$AWKLIB"'
  function cellsof(s, c,   n) { s = trim(s); sub(/^\|/, "", s); sub(/\|$/, "", s); n = split(s, c, /\|/); return n }
  FILENAME ~ /cs\.blockers$/ { blk = blk " " $2; next }
  FILENAME ~ /cs\.risks$/ { rsk = rsk " " $2; next }
  $1 == TDF && $3 ~ /^\| TD-[0-9][0-9] \|/ {
    cellsof($3, c); id = trim(c[1]); b = trim(c[6]); ev = c[8]
    if (b != "no" && b != "\342\200\224" && b != "") {
      gsub(/\*/, "", b); w = b; sub(/^the /, "", w)
      if (index(blk, id) == 0) emit("ERROR", "state.blocker", CS, 0, id " blocks " b " (technical-debt.md) but §Release blockers does not name it")
      else if (index(blk, w) == 0) emit("ERROR", "state.blocker", CS, 0, "§Release blockers names " id " but not what it blocks: " w)
    }
    if (ev ~ /current-state\.md §Release blockers/ && index(blk, id) == 0) emit("ERROR", "state.blocker", CS, 0, id " cites §Release blockers but is not there")
    if (ev ~ /current-state\.md §Open risks/ && index(rsk, id) == 0) emit("ERROR", "state.blocker", CS, 0, id " cites §Open risks but is not there")
  }
  ' "$WORK/cs.blockers" "$WORK/cs.risks" "$X/plain.tsv"
  # Markers
  conf marker | while IFS= read -r rec; do
    mf="$(printf '%s' "$rec" | "$AWK" -F ' [|] ' '{ print $2 }')"; ms="$(printf '%s' "$rec" | "$AWK" -F ' [|] ' '{ print $3 }')"; mt="$(printf '%s' "$rec" | "$AWK" -F ' [|] ' '{ print $4 }')"
    if [ ! -f "$mf" ]; then emit ERROR state.marker "$mf" 0 "file missing (marker: $mt)"; continue; fi
    section "$mf" "$ms" > "$WORK/marker"
    if [ ! -s "$WORK/marker" ] && ! grep -qxF "## $ms" "$mf" && ! grep -qxF "### $ms" "$mf"; then emit ERROR state.marker "$mf" 0 "section \"$ms\" missing (it must say: $mt)"
    elif ! grep -qF -- "$mt" "$WORK/marker"; then emit ERROR state.marker "$mf" 0 "section \"$ms\" no longer says: $mt"; fi
  done
fi
# Planned phases (history/README.md) and their records in current-state.md
if [ -f "$HIST/README.md" ]; then
  section "$HIST/README.md" "Planned phases" > "$WORK/phases"
  "$AWK" -F '\t' -v FINDINGS="$F" -v H="$HIST/README.md" -v CS="$CS" "$AWKLIB"'
  FILENAME == ARGV[1] { cs = cs " " $0; next }
  $2 ~ /^[0-9]+\. / {
    n = $2; sub(/\..*$/, "", n); n += 0
    if (n != last + 1) emit("ERROR", "state.phase", H, $1, "planned phase " n " follows phase " last)
    last = n
    if (match($2, /^[0-9]+\. Done \(PR #[0-9]+\)/)) {
      pr = substr($2, RSTART, RLENGTH); sub(/^.*#/, "", pr); sub(/\)$/, "", pr)
      if (index(cs, "Phase " n " (PR #" pr) == 0) emit("ERROR", "state.phase", CS, 0, "phase " n " is done (PR #" pr ", history/README.md) but current-state.md records no \"Phase " n " (PR #" pr "\"")
    }
  }' "$CSIN" "$WORK/phases"
fi

# ---------------------------------------------------------------------------------------------
# test.*: test-coverage.md against the test sources (and, on request, test results)
# ---------------------------------------------------------------------------------------------
TC="$AI/test-coverage.md"
grep -E '^[^/]+Tests/[^/]+\.csproj$' "$WORK/files" | sed 's|/.*||' | sort -u > "$WORK/testprojects"
: > "$WORK/srcclasses"
while IFS= read -r proj; do
  grep -E "^$proj/.+\\.cs\$" "$WORK/files" | grep -v -E '/(bin|obj)/' > "$WORK/cs.list" || true
  [ -s "$WORK/cs.list" ] || continue
  # A test is a method with [Test], [TestCase], [TestCaseSource], [AvaloniaTest] or [Theory]; its class
  # is the innermost class declared one indentation level above the attribute.
  tr '\n' '\0' < "$WORK/cs.list" | xargs -0 "$AWK" -v P="$proj" '
  function ind(s,   t) { t = s; gsub(/\t/, "    ", t); match(t, /^ */); return RLENGTH }
  FNR == 1 { split("", cls); pending = 0 }
  {
    if (match($0, /^[ \t]*((public|internal|private|protected|static|sealed|abstract|partial|file) +)*class +[A-Za-z_][A-Za-z0-9_]*/)) {
      c = substr($0, RSTART, RLENGTH); sub(/^.*class +/, "", c); cls[ind($0)] = c; clsfile[ind($0)] = FILENAME
    }
    if ($0 ~ /^[ \t]*\[(Test|TestCase|TestCaseSource|AvaloniaTest|Theory)([](,]|$)/) { if (!pending) { pending = 1; pind = ind($0) } ; next }
    if (pending && $0 ~ /^[ \t]*\[/) next
    if (pending && $0 ~ /[A-Za-z_][A-Za-z0-9_]* *[(<]/) {
      pending = 0; o = cls[pind - 4]
      if (o == "") o = "?"
      n[o "\t" FILENAME]++
    }
  }
  END { for (k in n) print P "\t" k "\t" n[k] }' >> "$WORK/srcclasses"
done < "$WORK/testprojects"
sort -o "$WORK/srcclasses" "$WORK/srcclasses"
if [ -f "$TC" ]; then
  section "$TC" "Test classes" > "$WORK/tc.classes"
  section "$TC" "Totals" > "$WORK/tc.totals"
  "$AWK" -F '\t' -v FINDINGS="$F" -v TC="$TC" -v PROJ="$WORK/testprojects" "$AWKLIB"'
  function cellsof(s, c,   n) { s = trim(s); sub(/^\|/, "", s); sub(/\|$/, "", s); n = split(s, c, /\|/); return n }
  function num(s) { s = trim(s); gsub(/`/, "", s); if (s !~ /^[0-9]{1,3}(,[0-9][0-9][0-9])*$/ && s !~ /^[0-9]+$/) return -1; gsub(/,/, "", s); return s + 0 }
  function bt(s) { s = trim(s); gsub(/`/, "", s); return s }
  BEGIN { while ((getline l < PROJ) > 0) projs[l] = 1 }
  FILENAME ~ /srcclasses$/ { k = $1 "\t" $2; if (k in src) { src[k] += $4; srcfile[k] = srcfile[k] ", " $3 } else { src[k] = $4; srcfile[k] = $3 }; next }
  FILENAME ~ /tc\.classes$/ && $2 ~ /^\|/ && $2 !~ /^\|[- |]+$/ {
    n = cellsof($2, c); if (bt(c[1]) == "Class") next
    cl = bt(c[1]); pj = bt(c[2]); fl = bt(c[3]); k = pj "\t" cl
    if (n != 6) { emit("ERROR", "test.count", TC, $1, "a class row needs 6 cells"); next }
    if (k in row) { emit("ERROR", "test.project", TC, $1, cl " (" pj ") has two rows (also line " row[k] ")"); next }
    row[k] = $1; tests[k] = num(c[4]); cases[k] = num(c[5]); sk = trim(c[6]); skip[k] = (sk == "\342\200\224") ? 0 : num(sk)
    if (tests[k] < 0 || cases[k] < 0 || skip[k] < 0) emit("ERROR", "test.count", TC, $1, cl ": a count is not a number")
    else if (cases[k] < tests[k] || skip[k] > cases[k]) emit("ERROR", "test.count", TC, $1, cl ": the counts do not fit (tests <= cases, skipped <= cases)")
    if (!(pj in projs)) emit("ERROR", "test.project", TC, $1, pj " is not a test project of the repository")
    if (fl !~ "^" pj "/") emit("ERROR", "test.project", TC, $1, cl ": its file is not in " pj)
    file[k] = fl; pt[pj] += tests[k]; pc[pj] += cases[k]; ps[pj] += skip[k]; pn[pj]++
    next
  }
  FILENAME ~ /tc\.totals$/ && $2 ~ /^\|/ && $2 !~ /^\|[- |]+$/ {
    n = cellsof($2, c); s = bt(c[1]); if (s == "Suite") next
    tot[s] = $1; tcl[s] = num(c[2]); tts[s] = num(c[3]); tcs[s] = num(c[4]); ton[s] = trim(c[5])
    next
  }
  END {
    for (k in src) {
      split(k, kk, "\t")
      if (kk[2] == "?") { emit("ERROR", "test.project", srcfile[k], 0, "test methods whose class this checker cannot find"); continue }
      if (!(k in row)) emit("ERROR", "test.project", TC, 0, kk[2] " (" kk[1] ", " srcfile[k] ") has " src[k] " tests but no row in §Test classes")
      else {
        if (tests[k] >= 0 && tests[k] != src[k]) emit("ERROR", "test.count", TC, row[k], kk[2] ": " tests[k] " tests here, " src[k] " in the source")
        if (index(", " srcfile[k] ", ", ", " file[k] ", ") == 0) emit("ERROR", "test.project", TC, row[k], kk[2] ": the file is " srcfile[k] ", not " file[k])
      }
    }
    for (k in row) if (!(k in src)) { split(k, kk, "\t"); emit("ERROR", "test.project", TC, row[k], kk[2] " (" kk[1] ") has a row but no test in the source") }
    for (p in projs) {
      if (!(p in pn)) { emit("ERROR", "test.project", TC, 0, "test project " p " has no class in §Test classes"); continue }
      if (!(("`" p "`") in tot) && !(p in tot)) { emit("ERROR", "test.count", TC, 0, "§Totals has no row for " p); continue }
      s = (p in tot) ? p : "`" p "`"
      if (tcl[s] != pn[p] || tts[s] != pt[p] || tcs[s] != pc[p]) emit("ERROR", "test.count", TC, tot[s], p ": §Totals says " tcl[s] " classes, " tts[s] " tests, " tcs[s] " cases; the class rows add up to " pn[p] ", " pt[p] ", " pc[p])
      if (match(ton[s], /^[0-9,]+ passed, [0-9,]+ skipped$/)) { split(ton[s], w, " "); a = w[1]; b = w[3]; gsub(/,/, "", a); gsub(/,/, "", b); if (a + b != pc[p] || b + 0 != ps[p]) emit("ERROR", "test.count", TC, tot[s], p ": \"" ton[s] "\" does not fit " pc[p] " cases with " ps[p] " skipped") }
      else if (match(ton[s], /^[0-9,]+ passed$/)) { a = ton[s]; sub(/ .*/, "", a); gsub(/,/, "", a); if (a + 0 != pc[p] || ps[p] != 0) emit("ERROR", "test.count", TC, tot[s], p ": \"" ton[s] "\" does not fit " pc[p] " cases with " ps[p] " skipped") }
      ac += pn[p]; at += pt[p]; aC += pc[p]
    }
    for (s in tot) if (s ~ /^Both/) { if (tcl[s] != ac || tts[s] != at || tcs[s] != aC) emit("ERROR", "test.count", TC, tot[s], "\"" s "\": " tcl[s] ", " tts[s] ", " tcs[s] " differ from the sums " ac ", " at ", " aC) }
    nsrc = 0; for (k in src) nsrc++; nrow = 0; for (k in row) nrow++; emit("INFO", "test.project", TC, 0, nsrc " test classes in the source, " nrow " class rows in \302\247Test classes")
  }' "$WORK/srcclasses" "$WORK/tc.classes" "$WORK/tc.totals"
  # Opt-in: case counts against .trx results
  if [ -n "$TRX_DIR" ]; then
    find "$TRX_DIR" -name '*.trx' -type f | sort > "$WORK/trx.list"
    if [ ! -s "$WORK/trx.list" ]; then emit ERROR test.trx "$TC" 0 "no .trx file in the --trx-dir directory"; else
      tr '\n' '\0' < "$WORK/trx.list" | xargs -0 "$AWK" '
        { while (match($0, /<UnitTest [^>]*id="[^"]+"/)) { s = substr($0, RSTART, RLENGTH); sub(/^.*id="/, "", s); sub(/"$/, "", s); cur = s; $0 = substr($0, RSTART + RLENGTH)
            if (match($0, /<TestMethod [^>]*className="[^"]+"/)) { c = substr($0, RSTART, RLENGTH); sub(/^.*className="/, "", c); sub(/"$/, "", c); sub(/,.*$/, "", c); sub(/^.*\./, "", c); cls[cur] = c } }
          r = $0
          while (match(r, /<UnitTestResult [^>]*>/)) { u = substr(r, RSTART, RLENGTH); r = substr(r, RSTART + RLENGTH)
            id = u; sub(/^.* testId="/, "", id); sub(/".*$/, "", id); o = u; sub(/^.* outcome="/, "", o); sub(/".*$/, "", o); res[++nres] = id "\t" o }
          if (match($0, /<TestMethod [^>]*className="[^"]+"/) && cur != "") { c = substr($0, RSTART, RLENGTH); sub(/^.*className="/, "", c); sub(/"$/, "", c); sub(/,.*$/, "", c); sub(/^.*\./, "", c); cls[cur] = c }
        }
        END { for (i = 1; i <= nres; i++) { split(res[i], a, "\t"); c = (a[1] in cls) ? cls[a[1]] : "?"; n[c]++; if (a[2] == "NotExecuted") sk[c]++ } for (c in n) print c "\t" n[c] "\t" (sk[c] + 0) }' | sort > "$WORK/trx.counts"
      "$AWK" -F '\t' -v FINDINGS="$F" -v TC="$TC" "$AWKLIB"'
        function cellsof(s, c,   n) { s = trim(s); sub(/^\|/, "", s); sub(/\|$/, "", s); n = split(s, c, /\|/); return n }
        FILENAME == ARGV[1] { got[$1] = $2; gsk[$1] = $3; next }
        $2 ~ /^\|/ && $2 !~ /^\|[- |]+$/ { cellsof($2, c); cl = trim(c[1]); gsub(/`/, "", cl); if (cl == "Class") next
          cs[cl] += trim(c[5]); sk = trim(c[6]); sks[cl] += (sk ~ /^[0-9]+$/) ? sk : 0; ln[cl] = $1 }
        END { for (cl in cs) { if (!(cl in got)) emit("ERROR", "test.trx", TC, ln[cl], cl ": no result in the .trx files")
              else if (got[cl] != cs[cl] || gsk[cl] != sks[cl]) emit("ERROR", "test.trx", TC, ln[cl], cl ": " cs[cl] " cases (" sks[cl] " skipped) here, " got[cl] " (" gsk[cl] " not executed) in the results") }
              for (cl in got) if (!(cl in cs)) emit("ERROR", "test.trx", TC, 0, cl ": " got[cl] " results but no row") }' "$WORK/trx.counts" "$WORK/tc.classes"
    fi
  fi
  if [ -n "$POLICY_LOG" ]; then
    got="$(grep -c '^PASS ' "$POLICY_LOG" || true)"
    want="$(grep -F '| Release policy' "$WORK/tc.totals" | head -1 | "$AWK" -F '|' '{ gsub(/[ ,]/, "", $4); print $4 }')"
    if [ -z "$want" ]; then emit ERROR test.policy "$TC" 0 "§Totals has no Release policy row"
    elif [ "$got" != "$want" ]; then emit ERROR test.policy "$TC" 0 "the release-policy tests are $want here, $got PASS lines in $(basename "$POLICY_LOG")"; fi
    grep -q '^FAIL ' "$POLICY_LOG" && emit ERROR test.policy "$TC" 0 "the release-policy run has FAIL lines"
  fi
fi
for p in "$AI/repository-map.md" "$AI/feature-inventory.md"; do
  [ -f "$p" ] || continue
  "$AWK" -F '\t' -v FINDINGS="$F" -v P="$p" "$AWKLIB"'
    $1 == P && ($3 ~ /[A-Za-z]+Tests[^|]?[^|]?[^|]?\([0-9]+[ ;,)]/ || $3 ~ /[0-9]+ (tests|cases)([^A-Za-z]|$)/) { emit("ERROR", "test.inventory", P, $2, "lists test counts; the test inventory belongs in test-coverage.md") }' "$X/plain.tsv"
done

# ---------------------------------------------------------------------------------------------
# rel.*: the release index, the release history and release routing
# ---------------------------------------------------------------------------------------------
HR="$HIST/README.md"
if [ -f "$HR" ]; then
  section "$HR" "Release index" > "$WORK/relindex"
  "$AWK" -F '\t' -v FINDINGS="$F" -v HR="$HR" "$AWKLIB"'
    function cellsof(s, c,   n) { s = trim(s); sub(/^\|/, "", s); sub(/\|$/, "", s); n = split(s, c, /\|/); return n }
    $2 ~ /^\| `v/ { cellsof($2, c); v = trim(c[1]); gsub(/`/, "", v); k = semkey(v)
      if (v in seen) emit("ERROR", "rel.index", HR, $1, v " has two rows"); seen[v] = 1
      if (last != "" && k <= last) emit("ERROR", "rel.index", HR, $1, v " is out of order: the index lists versions oldest first")
      last = k; o = trim(c[3]); gsub(/`/, "", o); split(o, oo, / *\342\206\222 */); print v "\t" trim(oo[1]) "\t" trim(oo[2]) "\t" trim(c[5]) "\t" $1 }' "$WORK/relindex" > "$WORK/relrows"
  if [ "$IS_GIT" = 1 ]; then
    cut -f2 "$WORK/semtags" | while IFS= read -r tg; do
      grep -q "^$tg	" "$WORK/relrows" || emit ERROR rel.index "$HR" 0 "local tag $tg has no row in §Release index"
    done
    while IFS=$'\t' read -r v o c rid ln; do
      if ! git rev-parse -q --verify "refs/tags/$v" >/dev/null; then emit ERROR rel.index "$HR" "$ln" "$v has no local tag"; continue; fi
      to="$(git rev-parse "refs/tags/$v")"; tc="$(git rev-parse "$v^{commit}")"
      case "$to" in "$o"*) ;; *) emit ERROR rel.index "$HR" "$ln" "$v: the tag object is $to, not $o" ;; esac
      case "$tc" in "$c"*) ;; *) emit ERROR rel.index "$HR" "$ln" "$v: the tag points to $tc, not $c" ;; esac
    done < "$WORK/relrows"
    grep -v -E '^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$' "$WORK/tags" | while IFS= read -r tg; do
      grep -qF "\`$tg\`" "$WORK/relindex" || emit ERROR rel.index "$HR" 0 "local tag $tg is not mentioned in §Release index"
    done
  fi
  if [ -f "$HIST/releases.md" ]; then
    grep -n -o -E '^\*\*`v[^`]+`, published' "$HIST/releases.md" | sed -E 's/^([0-9]+):\*\*`([^`]+)`.*/\2\t\1/' > "$WORK/relhist"
    cut -f1 "$WORK/relrows" > "$WORK/rel.a"; cut -f1 "$WORK/relhist" > "$WORK/rel.b"
    if ! cmp -s "$WORK/rel.a" "$WORK/rel.b"; then
      emit ERROR rel.index "$HIST/releases.md" 0 "§Release history's entries ($(tr '\n' ' ' < "$WORK/rel.b" | sed 's/ $//')) differ from §Release index's versions, in order ($(tr '\n' ' ' < "$WORK/rel.a" | sed 's/ $//'))"
    fi
  fi
fi
"$AWK" -F '\t' -v FINDINGS="$F" -v ROUTER="$ROUTER" -v TDF="$AI/technical-debt.md" "$AWKLIB"'
  function cellsof(s, c,   n) { s = trim(s); sub(/^\|/, "", s); sub(/\|$/, "", s); n = split(s, c, /\|/); return n }
  FILENAME ~ /routes$/ { if ($2 ~ /^\| Release operation \|/) { op = $2; opl = $1 } if ($2 ~ /^\| Release/) rel = rel " " $2; next }
  $1 == TDF && $3 ~ /^\| TD-[0-9][0-9] \|/ { cellsof($3, c); b = trim(c[6]); if (b != "no" && b != "\342\200\224" && b != "") blocking[trim(c[1])] = 1 }
  END {
    if (op == "") emit("ERROR", "rel.routing", ROUTER, 0, "§Task routing has no Release operation route")
    else {
      if (op !~ /RP|release-process/) emit("ERROR", "rel.routing", ROUTER, opl, "the Release operation route does not name release-process.md")
      if (op !~ /MP|maintainer-procedures/) emit("ERROR", "rel.routing", ROUTER, opl, "the Release operation route does not name maintainer-procedures.md")
    }
    for (id in blocking) if (index(rel, id) == 0) emit("ERROR", "rel.routing", ROUTER, 0, id " blocks a publication but no release route names it")
  }' "$WORK/routes" "$X/plain.tsv"

# ---------------------------------------------------------------------------------------------
# mig.*: stubs, recorded pull requests and merge commits, migration-map classifications
# ---------------------------------------------------------------------------------------------
"$AWK" -F '\t' -v FINDINGS="$F" -v CUR="$WORK/currentscope" "$AWKLIB"'
  BEGIN { while ((getline l < CUR) > 0) iscur[l] = 1 }
  FILENAME ~ /headings\.tsv$/ { htext[$1 "#" $4] = $6; if ($1 in iscur) { hl[$1, $2] = $6 }; next }
  FILENAME ~ /plain\.tsv$/ { if ($1 in iscur) pl[$1, $2] = $3; next }
  FILENAME ~ /links\.tsv$/ { if ($1 in iscur) { nl[$1, $2]++; lk[$1, $2, nl[$1, $2]] = $3 }; next }
  END {
    for (k in hl) {
      split(k, kk, SUBSEP); f = kk[1]; l = kk[2] + 0
      for (j = l + 1; j <= l + 3; j++) if (((f SUBSEP j) in pl) && trim(pl[f, j]) != "") break
      if (!((f SUBSEP j) in pl) || pl[f, j] !~ /^Moved to /) continue
      if (!((f SUBSEP j SUBSEP 1) in lk)) continue
      d = lk[f, j, 1]; if (index(d, "#") == 0) continue
      p = substr(d, 1, index(d, "#") - 1); fr = substr(d, index(d, "#") + 1)
      tg = (p == "") ? f : normpath((dirof(f) == "" ? "" : dirof(f) "/") p)
      if (!((tg "#" fr) in htext)) continue
      if (htext[tg "#" fr] != hl[f, l]) emit("ERROR", "mig.stub", f, l, "the stub heading differs from its destination heading in " tg ": \"" htext[tg "#" fr] "\"")
    }
  }' "$X/headings.tsv" "$X/plain.tsv" "$X/links.tsv"

SHALLOW=0
if [ "$IS_GIT" = 1 ] && [ "$(git rev-parse --is-shallow-repository 2>/dev/null)" = true ]; then SHALLOW=1; fi
if [ "$IS_GIT" = 1 ] && [ "$SHALLOW" = 0 ]; then
  for p in "$AI/current-state.md" "$HIST/README.md"; do
    [ -f "$p" ] || continue
    grep -n -o -E 'PR #[0-9]+( \(|, )`[0-9a-f]{7,40}`, merged as `[0-9a-f]{7,40}`' "$p" | sed -E 's/^([0-9]+):PR #([0-9]+)( \(|, )`([0-9a-f]+)`, merged as `([0-9a-f]+)`/\1 \2 \4 \5/' | while read -r n pr h m; do
      if ! git cat-file -e "$m^{commit}" 2>/dev/null; then emit ERROR mig.pr "$p" "$n" "PR #$pr: merge commit $m is not in the local history"; continue; fi
      subj="$(git log -1 --format=%s "$m")"
      case "$subj" in "Merge pull request #$pr "*) ;; *) emit ERROR mig.pr "$p" "$n" "PR #$pr: $m is \"$subj\", not the merge of PR #$pr" ;; esac
      p2="$(git rev-parse -q --verify "$m^2" 2>/dev/null || true)"
      case "$p2" in "$h"*) ;; *) emit ERROR mig.pr "$p" "$n" "PR #$pr: the merged head is ${p2:-none}, not $h" ;; esac
    done
  done
fi
if [ -f "$HR" ]; then
  "$AWK" -v FINDINGS="$F" -v HR="$HR" "$AWKLIB"'
    function cellsof(s, c,   n) { s = trim(s); sub(/^\|/, "", s); sub(/\|$/, "", s); n = split(s, c, /\|/); return n }
    /^```/ { fence = !fence }
    fence { next }
    /^\|/ { if (!intab) { intab = 1; col = 0; n = cellsof($0, c); for (i = 1; i <= n; i++) if (trim(c[i]) == "Authority") col = i; hdr = FNR; next }
            if (col && FNR > hdr + 1) { cellsof($0, c); if (trim(c[col]) == "") emit("ERROR", "mig.authority", HR, FNR, "migration-map row without an authority classification") }
            next }
    { intab = 0 }' "$HR"
fi

# ---------------------------------------------------------------------------------------------
# safe.command: no command in a current document reads a real user-data folder
# ---------------------------------------------------------------------------------------------
conf safe-path | "$AWK" -F ' [|] ' '{ print $2 }' > "$WORK/safepaths"
"$AWK" -F '\t' -v FINDINGS="$F" -v CUR="$WORK/currentscope" -v SAFE="$WORK/safepaths" "$AWKLIB"'
  BEGIN { while ((getline l < CUR) > 0) iscur[l] = 1; while ((getline l < SAFE) > 0) safe[++ns] = l }
  !($1 in iscur) { next }
  FILENAME ~ /plain\.tsv$/ { pl[$1, $2] = $3; next }
  # A line that states a prohibition ("Never run ...") is not an instruction.
  FILENAME ~ /spans\.tsv$/ && pl[$1, $2] ~ /([Nn]ever|[Dd]o not|[Dd]on\047t|must not)/ { next }
  {
    t = $3; s = t
    # A path below a registered safe prefix (an isolated location) is removed whole.
    for (i = 1; i <= ns; i++) while ((p = index(s, safe[i])) > 0) { rest = substr(s, p + length(safe[i])); sub(/^[^ "\047`)]*/, "", rest); s = substr(s, 1, p - 1) "SAFE" rest }
    if (s !~ /(\.local\/share\/Campertron|Library\/Application Support\/Campertron|%LOCALAPPDATA%[\\\/]Campertron|LOCALAPPDATA[\\\/]Campertron|AppData[\\\/]Local[\\\/]Campertron|LocalApplicationData\/Campertron)/) next
    if (s ~ /(mktemp|XDG_DATA_HOME=|unshare|tmpfs|\$X\/|\/tmp\/)/) next
    if (s ~ /(^|[ ;|&(`$])(cat|less|more|head|tail|ls|find|stat|du|tree|sha256sum|shasum|sha1sum|md5sum|b2sum|cp|mv|rsync|scp|tar|zip|7z|sqlite3|open|xdg-open|grep|rg|xxd|hexdump|od|file|strings|dir|type|Get-Content|Get-ChildItem|Copy-Item|Get-FileHash|certutil)( |$)/)
      emit("ERROR", "safe.command", $1, $2, "a command reads a real user-data folder; use an isolated folder (XDG_DATA_HOME=\"$(mktemp -d)\", or unshare -rm with a tmpfs over /home)")
  }' "$X/plain.tsv" "$X/code.tsv" "$X/spans.tsv"

# ---------------------------------------------------------------------------------------------
# Known defects: an error registered in docs-policy.conf (a defect in a file the change that
# introduced the check could not correct) is reported as a labeled warning until it is fixed.
# ---------------------------------------------------------------------------------------------
conf known-defect | "$AWK" -F ' [|] ' '{ print $2 "\t" $3 "\t" $4 "\t" $5 }' > "$WORK/known"
if [ -s "$WORK/known" ]; then
  : > "$WORK/known.used"
  while IFS=$'\t' read -r sev chk p n msg; do
    if [ "$sev" = ERROR ]; then
      h="-"
      if [ "$n" -gt 0 ] && [ -f "$p" ]; then h="$(sed -n "${n}p" "$p" | tr -d '\n' | sha256sum | cut -c1-64)"; fi
      reason="$("$AWK" -F '\t' -v c="$chk" -v p="$p" -v h="$h" '$1 == c && $2 == p && $3 == h { print $4; exit }' "$WORK/known")"
      if [ -n "$reason" ]; then
        printf 'WARN\t%s\t%s\t%s\t%s (known defect: %s)\n' "$chk" "$p" "$n" "$msg" "$reason"
        printf '%s\t%s\t%s\n' "$chk" "$p" "$h" >> "$WORK/known.used"
        continue
      fi
    fi
    printf '%s\t%s\t%s\t%s\t%s\n' "$sev" "$chk" "$p" "$n" "$msg"
  done < "$F" > "$F.k"
  mv "$F.k" "$F"
  "$AWK" -F '\t' 'FILENAME == ARGV[1] { u[$1 "\t" $2 "\t" $3] = 1; next } !(($1 "\t" $2 "\t" $3) in u)' "$WORK/known.used" "$WORK/known" | while IFS=$'\t' read -r c p h r; do
    emit WARN file.exception "$p" 0 "the registered known defect ($c, $h) no longer occurs: remove it from docs-policy.conf"
  done
fi

# ---------------------------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------------------------
# An inventory of what was checked, shown with --verbose.
scopecount() { "$AWK" -F '\t' 'FILENAME == ARGV[1] { s[$1] = 1; next } ($1 in s) { n++ } END { print n + 0 }' "$WORK/scope" "$1"; }
emit INFO inventory - 0 "$(scopecount "$X/headings.tsv") headings, $(scopecount "$X/links.tsv") Markdown links, $(scopecount "$X/sect.tsv") section citations, $(scopecount "$X/spans.tsv") code spans and $(scopecount "$X/code.tsv") fenced code lines in the checked files"
lines() { if [ -f "$1" ]; then wc -l < "$1" | tr -d ' '; else echo 0; fi; }
emit INFO inventory - 0 "$(lines "$WORK/files") repository files; $(lines "$WORK/testprojects") test projects; $(lines "$WORK/tags") local tags, latest stable: ${latest:-none}"

sort -t "$(printf '\t')" -k1,1 -k3,3 -k4,4n -k2,2 -k5,5 -u "$F" > "$F.sorted"
errors=$(grep -c '^ERROR	' "$F.sorted" || true)
warnings=$(grep -c '^WARN	' "$F.sorted" || true)
allowed=$(grep -c '^ALLOWED	' "$F.sorted" || true)
"$AWK" -F '\t' -v V="$VERBOSE" '
  function show(sev) { print sev " [" $2 "] " ($3 == "-" ? "" : $3 ($4 + 0 > 0 ? ":" $4 : "") ": ") $5 }
  $1 == "ERROR" { e[++ne] = $0 } $1 == "WARN" { w[++nw] = $0 } $1 == "ALLOWED" { a[++na] = $0 } $1 == "INFO" { o[++no] = $0 }
  END {
    for (i = 1; i <= ne; i++) { $0 = e[i]; show("ERROR") }
    for (i = 1; i <= nw; i++) { $0 = w[i]; show("WARN") }
    if (V) for (i = 1; i <= na; i++) { $0 = a[i]; show("ALLOWED") }
    if (V) for (i = 1; i <= no; i++) { $0 = o[i]; show("INFO") }
  }' FS='\t' "$F.sorted"
ncur=$(wc -l < "$WORK/current" | tr -d ' '); nhis=$(wc -l < "$WORK/history" | tr -d ' ')
notes=""
[ "$IS_GIT" = 1 ] || notes=" (no Git metadata: the tag, release-index and pull-request checks were skipped)"
[ "$SHALLOW" = 1 ] && notes=" (shallow clone: the pull-request records were not checked)"
echo "check-docs: $errors error(s), $warnings warning(s), $allowed allowed by a registered exception; checked CLAUDE.md, $ncur current and $nhis history files$notes"
if [ "$errors" -gt 0 ]; then exit 1; elif [ "$warnings" -gt 0 ]; then exit 3; else exit 0; fi
