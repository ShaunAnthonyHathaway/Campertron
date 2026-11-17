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
