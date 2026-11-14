#!/usr/bin/env bash
# Release policy for .github/workflows/release.yml, sourced by its steps and exercised by
# test-release-policy.sh. See docs/ai-context/build-and-test.md §Release workflow.
#
# Policy:
# - Every published version becomes a normal (non-prerelease) GitHub release and is marked Latest,
#   including SemVer pre-release versions (a hyphen, e.g. 0.1.0-beta.4), so the newest release always
#   appears on the repository page. GitHub does not allow a prerelease to be Latest.
# - SemVer pre-release versions are still treated as pre-releases for Docker and in the release notes:
#   the image gets only the exact version tag; Docker `latest` and the MAJOR.MINOR alias are not updated.
# - Stable versions get the exact tag, MAJOR.MINOR and `latest` in GHCR.

RELEASE_RIDS_DEFAULT="linux-arm linux-arm64 linux-x64 osx-arm64 osx-x64 win-arm64 win-x64 win-x86"

# docker_tag_args IMAGE VERSION PRERELEASE  -> "--tag IMAGE:VERSION [--tag IMAGE:MAJOR.MINOR --tag IMAGE:latest]"
docker_tag_args() {
  local image="$1" version="$2" prerelease="$3"
  local tags="--tag $image:$version"
  if [ "$prerelease" != "true" ]; then
    tags="$tags --tag $image:${version%.*} --tag $image:latest"
  fi
  echo "$tags"
}

# github_release_flags PRERELEASE -> flags for `gh release create` / `gh release edit`.
# Always a normal release marked Latest; PRERELEASE is accepted only to make the policy explicit.
github_release_flags() {
  echo "--latest"
}

# expected_archives VERSION [RIDS] -> the 16 expected archive names, sorted, one per line
expected_archives() {
  local version="$1" rids="${2:-$RELEASE_RIDS_DEFAULT}" app rid ext
  for app in console gui; do
    for rid in $rids; do
      case "$rid" in win-*) ext=zip ;; *) ext=tar.gz ;; esac
      echo "campertron-$app-$version-$rid.$ext"
    done
  done | sort
}

# render_notes VERSION PRERELEASE IMAGE REPOSITORY [RIDS] -> RELEASE_NOTES.md on stdout
render_notes() {
  local version="$1" prerelease="$2" image="$3" repository="$4" rids="${5:-$RELEASE_RIDS_DEFAULT}" rid ext smoke status
  if [ "$prerelease" = "true" ]; then
    status="**Pre-release version.** \`$version\` is a SemVer pre-release. It is published as a normal GitHub release and marked Latest, so it appears on the repository page. The container image is tagged \`$version\` only: Docker \`latest\` and the stable aliases are not updated."
  else
    status="**Stable release.** Published as a normal GitHub release and marked Latest. The container image is tagged \`$version\`, \`${version%.*}\` and \`latest\`."
  fi
  echo "# Campertron $version"
  echo
  echo "$status"
  echo
  echo "## Downloads"
  echo "Each platform has a **console** archive (search and email notifications) and a **GUI** archive (campground lookup). Extract an archive and run \`CampertronConsole\` or \`CampertronGUI\`. No .NET installation is needed."
  echo
  echo "| Platform | Console | GUI | Native smoke test in CI |"
  echo "|---|---|---|---|"
  for rid in $rids; do
    case "$rid" in win-*) ext=zip ;; *) ext=tar.gz ;; esac
    if [ "$rid" = "linux-x64" ]; then
      smoke="yes: console clean install and GUI start"
    else
      smoke="no: supported and cross-built, not run natively"
    fi
    echo "| \`$rid\` | \`campertron-console-$version-$rid.$ext\` | \`campertron-gui-$version-$rid.$ext\` | $smoke |"
  done
  echo
  echo "Only linux-x64 is run natively in CI. The other seven platforms are supported and built from the same sources, but are not smoke-tested natively."
  echo
  echo "- **macOS:** the binaries are not signed or notarized. If Gatekeeper blocks them, run \`xattr -dr com.apple.quarantine <extracted folder>\`. The GUI is a plain executable, not an \`.app\` bundle."
  echo "- **Windows:** the binaries are not signed, so SmartScreen may warn on first start."
  echo
  echo "## Compatibility with older versions"
  echo "Do not run versions older than 0.1.0-beta.4 (the 2024 \`v0.1-beta\` downloads, which run on .NET 9 or older) on the same user account as this version. All versions share the \`CampertronConfig\` data folder, and the old versions read the state from a database column that now holds ZIP codes, so their GUI lists ZIP codes such as \`01005\` as states. Delete old copies instead of running them."
  echo
  echo "To identify a copy: \`CampertronConsole --version\` and \`CampertronGUI --version\` print the version and the .NET runtime (added after 0.1.0-beta.4). In any version, the console's View Config shows the .NET version it runs on in its \`Framework =>\` line: .NET 10 or later for 0.1.0-beta.4 and later, and .NET 9 or older for the old versions."
  echo
  echo "## Docker"
  echo "\`docker pull $image:$version\` (linux/amd64 and linux/arm64). The container runs headless with \`outputTo: Email\`; mount \`/config\` and \`/cache\`."
  echo
  echo "## Verify your download"
  echo "- Checksums: \`sha256sum -c SHA256SUMS.txt --ignore-missing\`"
  echo "- Build provenance: \`gh attestation verify <file> --repo $repository\`"
}

# validate_notes NOTES_FILE VERSION PRERELEASE IMAGE EXPECTED_ARCHIVES_FILE
# Prints "::error::Release notes: ..." and returns 1 on the first problem; returns 0 when valid.
validate_notes() {
  local notes="$1" version="$2" prerelease="$3" image="$4" expected="$5" archive
  _vn_fail() { echo "::error::Release notes: $1"; return 1; }
  head -n 1 "$notes" | grep -qxF "# Campertron $version" || { _vn_fail "title does not name version $version"; return 1; }
  grep -qF "marked Latest" "$notes" || { _vn_fail "does not state that the release is marked Latest"; return 1; }
  if grep -qiE "published as a GitHub prerelease|not marked as the latest" "$notes"; then
    _vn_fail "claims the GitHub release is a prerelease or not Latest"; return 1
  fi
  if [ "$prerelease" = "true" ]; then
    grep -qF "**Pre-release version.**" "$notes" || { _vn_fail "pre-release version status missing"; return 1; }
    if grep -qF "**Stable release.**" "$notes"; then _vn_fail "pre-release notes claim a stable release"; return 1; fi
    grep -qF "The container image is tagged \`$version\` only: Docker \`latest\` and the stable aliases are not updated." "$notes" \
      || { _vn_fail "pre-release notes do not say Docker latest and stable aliases are unchanged"; return 1; }
  else
    grep -qF "**Stable release.**" "$notes" || { _vn_fail "stable status missing"; return 1; }
    if grep -qF "**Pre-release version.**" "$notes"; then _vn_fail "stable notes claim a pre-release"; return 1; fi
    grep -qF "The container image is tagged \`$version\`, \`${version%.*}\` and \`latest\`." "$notes" \
      || { _vn_fail "stable notes do not list the exact, MAJOR.MINOR and latest Docker tags"; return 1; }
  fi
  while read -r archive; do
    [ "$(grep -cF "\`$archive\`" "$notes")" -eq 1 ] || { _vn_fail "archive $archive is not listed exactly once"; return 1; }
  done < "$expected"
  [ "$(grep -c '^| `' "$notes")" -eq 8 ] || { _vn_fail "platform table does not have 8 rows"; return 1; }
  grep -F '| `linux-x64` |' "$notes" | grep -qF "yes: console clean install and GUI start" || { _vn_fail "linux-x64 smoke-test row is wrong"; return 1; }
  [ "$(grep -cF "no: supported and cross-built, not run natively" "$notes")" -eq 7 ] || { _vn_fail "the 7 non-native platforms are not all marked as not smoke-tested"; return 1; }
  grep -qF "Only linux-x64 is run natively in CI" "$notes" || { _vn_fail "native smoke-test limitation statement missing"; return 1; }
  grep -qxF "## Compatibility with older versions" "$notes" || { _vn_fail "compatibility section missing"; return 1; }
  grep -qF "Do not run versions older than 0.1.0-beta.4" "$notes" || { _vn_fail "warning against running versions older than 0.1.0-beta.4 missing"; return 1; }
  grep -qF "\`CampertronConsole --version\` and \`CampertronGUI --version\`" "$notes" || { _vn_fail "--version instructions missing"; return 1; }
  grep -qF "docker pull $image:$version" "$notes" || { _vn_fail "Docker pull command for $image:$version missing"; return 1; }
  grep -qF "linux/amd64 and linux/arm64" "$notes" || { _vn_fail "Docker platforms missing"; return 1; }
  grep -qF "sha256sum -c SHA256SUMS.txt" "$notes" || { _vn_fail "checksum instructions missing"; return 1; }
  return 0
}

# ---------------------------------------------------------------------------------------------------------
# Curated release notes (decision D30, R7). A reviewed, version-specific fragment
# .github/release-notes/v<VERSION>.md is merged to master before the tag. A publishing tag run composes it
# with the generated sections (render_notes) and publishes exactly that body; it never falls back to the
# template. Dry runs (pull requests, workflow_dispatch) use the template for their synthetic version.
# The functions below print "::error::..." on stderr, so their stdout stays usable as file content.
RELEASE_NOTES_DIR_DEFAULT=".github/release-notes"
# GitHub limits a release body to 125,000 characters; counting bytes is stricter (a character is >= 1 byte).
RELEASE_BODY_MAX_BYTES=125000
SEMVER_PATTERN='(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?'

# is_semver VERSION -> 0 if VERSION is MAJOR.MINOR.PATCH[-prerelease] (no leading "v", no leading zeros)
is_semver() { [[ "$1" =~ ^${SEMVER_PATTERN}$ ]]; }

# semver_prerelease VERSION -> "true" for a SemVer pre-release (contains a hyphen), otherwise "false"
semver_prerelease() { if [[ "$1" == *-* ]]; then echo true; else echo false; fi; }

# curated_notes_path VERSION [DIR] -> DIR/vVERSION.md
curated_notes_path() { echo "${2:-$RELEASE_NOTES_DIR_DEFAULT}/v$1.md"; }

_cn_fail() { echo "::error::Curated release notes $1: $2" >&2; return 1; }

# validate_curated_fragment FILE VERSION
# A fragment is the curated part only: it starts with "## What's new in VERSION" and must not contain any
# generated section, status text or release-flag statement. Its bytes are never changed.
validate_curated_fragment() {
  local file="$1" version="$2" first last hit
  is_semver "$version" || { _cn_fail "$file" "version '$version' is not MAJOR.MINOR.PATCH[-prerelease]"; return 1; }
  { [ -f "$file" ] && [ ! -L "$file" ]; } || { _cn_fail "$file" "missing or not a regular file"; return 1; }
  [ "$(basename -- "$file")" = "v$version.md" ] || { _cn_fail "$file" "file name must be v$version.md"; return 1; }
  [ -s "$file" ] || { _cn_fail "$file" "file is empty"; return 1; }
  iconv -f UTF-8 -t UTF-8 -- "$file" > /dev/null 2>&1 || { _cn_fail "$file" "not valid UTF-8"; return 1; }
  [ "$(tr -d '\000' < "$file" | wc -c)" -eq "$(wc -c < "$file")" ] || { _cn_fail "$file" "contains NUL bytes"; return 1; }
  if LC_ALL=C grep -qa $'\r' -- "$file"; then _cn_fail "$file" "contains carriage returns (use LF line endings)"; return 1; fi
  [ "$(tail -c 1 -- "$file" | od -An -tx1 | tr -d ' \n')" = "0a" ] || { _cn_fail "$file" "does not end with a newline"; return 1; }
  last="$(tail -n 1 -- "$file")"
  [[ "$last" =~ [^[:space:]] ]] || { _cn_fail "$file" "ends with a blank line (end with exactly one newline after the last text line)"; return 1; }
  first="$(head -n 1 -- "$file")"
  [ "$first" = "## What's new in $version" ] || { _cn_fail "$file" "first line must be exactly: ## What's new in $version"; return 1; }
  [ "$(grep -caE "^ {0,3}#+[[:space:]]+What's new in " -- "$file")" -eq 1 ] \
    || { _cn_fail "$file" "has more than one \"What's new in\" heading"; return 1; }
  if grep -qaE '^ {0,3}#([[:space:]]|$)' -- "$file"; then _cn_fail "$file" "contains a level-one heading (# ...); the generated title is the only one"; return 1; fi
  if grep -qaE '^ {0,3}=+[[:space:]]*$' -- "$file"; then _cn_fail "$file" "contains a level-one setext heading underline (===)"; return 1; fi
  hit="$(grep -aiE '^ {0,3}##[[:space:]]+(downloads|compatibility with older versions|docker|verify your download)[[:space:]#]*$' -- "$file" | head -n 1 || true)"
  [ -z "$hit" ] || { _cn_fail "$file" "duplicates the generated heading '$hit'"; return 1; }
  if grep -qaF '**Pre-release version.**' -- "$file" || grep -qaF '**Stable release.**' -- "$file"; then
    _cn_fail "$file" "contains a generated status marker (**Pre-release version.** or **Stable release.**)"; return 1
  fi
  # Release-status statements belong to the generated status line only. These patterns match claims about
  # the GitHub release flags, not ordinary prose such as "Upgrading from 0.1.0-beta.8" or "the latest beta".
  hit="$(grep -aioE '\bmarked[[:space:]]+(as[[:space:]]+)?(the[[:space:]]+)?latest\b|\bgithub[[:space:]]+pre-?release|\b(is|as)[[:space:]]+(not[[:space:]]+)?(the[[:space:]]+)?latest[[:space:]]+release\b|\b(published|released)[[:space:]]+as[[:space:]]+(a[[:space:]]+)?(normal|stable|draft|pre-?release)\b' -- "$file" | head -n 1 || true)"
  [ -z "$hit" ] || { _cn_fail "$file" "states the GitHub release status ('$hit'); that is generated, not curated"; return 1; }
  if grep -qaE '^[[:space:]]*\|[[:space:]]*`' -- "$file"; then _cn_fail "$file" "contains a downloads-table row (| \`...); the table is generated"; return 1; fi
  hit="$(expected_archives "$version" | grep -aoFf - -- "$file" | head -n 1 || true)"
  [ -z "$hit" ] || { _cn_fail "$file" "names the archive $hit, which the generated table already lists exactly once"; return 1; }
  return 0
}

# compose_notes VERSION PRERELEASE IMAGE REPOSITORY FRAGMENT -> the complete release body on stdout:
# generated lines 1-4 (title, blank, status, blank), the fragment byte for byte, one blank line, then the
# generated sections from line 5 ("## Downloads") on.
compose_notes() (
  version="$1"; prerelease="$2"; image="$3"; repository="$4"; fragment="$5"
  [ "$prerelease" = "$(semver_prerelease "$version")" ] || { _cn_fail "$fragment" "prerelease=$prerelease does not match version $version"; exit 1; }
  validate_curated_fragment "$fragment" "$version" || exit 1
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  render_notes "$version" "$prerelease" "$image" "$repository" > "$tmp/template.md"
  if [ "$(sed -n 1p "$tmp/template.md")" != "# Campertron $version" ] || [ -n "$(sed -n 2p "$tmp/template.md")" ] \
    || [ -n "$(sed -n 4p "$tmp/template.md")" ] || [ "$(sed -n 5p "$tmp/template.md")" != "## Downloads" ]; then
    _cn_fail "$fragment" "generated template no longer has the expected title/status/Downloads layout"; exit 1
  fi
  { head -n 4 "$tmp/template.md"; cat -- "$fragment"; echo; tail -n +5 "$tmp/template.md"; } > "$tmp/body.md"
  size="$(wc -c < "$tmp/body.md")"
  [ "$size" -le "$RELEASE_BODY_MAX_BYTES" ] || { _cn_fail "$fragment" "composed body is $size bytes, over the $RELEASE_BODY_MAX_BYTES limit"; exit 1; }
  cat "$tmp/body.md"
)

# assemble_release_notes PUBLISH VERSION PRERELEASE IMAGE REPOSITORY [DIR] -> release body on stdout.
# PUBLISH=true: the fragment DIR/vVERSION.md is required and composed; there is no template-only fallback.
# Otherwise (a dry run, which cannot publish): the generated template for the synthetic version.
assemble_release_notes() {
  local publish="$1" version="$2" prerelease="$3" image="$4" repository="$5" dir="${6:-$RELEASE_NOTES_DIR_DEFAULT}" path
  if [ "$publish" = "true" ]; then
    path="$(curated_notes_path "$version" "$dir")"
    { [ -e "$path" ] || [ -L "$path" ]; } \
      || { _cn_fail "$path" "required to publish $version and missing from this commit; template-only notes are never published"; return 1; }
    compose_notes "$version" "$prerelease" "$image" "$repository" "$path"
  else
    render_notes "$version" "$prerelease" "$image" "$repository"
  fi
}

# notes_preview_block TITLE FILE -> a Markdown block with the SHA-256, the size and the body in a code fence
notes_preview_block() {
  local title="$1" file="$2" sha bytes ticks fence
  sha="$(sha256sum < "$file" | cut -d' ' -f1)"; bytes="$(wc -c < "$file" | tr -d ' ')"
  ticks="$( (grep -ao '`\+' -- "$file" || true) | awk '{ if (length($0) > m) m = length($0) } END { print m + 0 }')"
  [ "$ticks" -ge 3 ] || ticks=2
  fence="$(printf '%*s' "$((ticks + 1))" '' | tr ' ' '`')"
  echo "#### $title"
  echo "- Composed release body SHA-256: \`$sha\` ($bytes bytes)"
  echo
  echo "<details><summary>Composed release body</summary>"
  echo
  echo "${fence}markdown"
  cat -- "$file"
  echo "$fence"
  echo
  echo "</details>"
  echo
}

# check_curated_notes_dir DIR IMAGE REPOSITORY -> Markdown preview on stdout, errors on stderr.
# DIR may hold README.md and v<SemVer>.md fragments only. Every fragment must validate, compose for its own
# version (pre-release or stable from its file name) and pass validate_notes. A future version without a
# fragment is not an error here; only a publishing tag run requires its fragment (assemble_release_notes).
check_curated_notes_dir() (
  dir="$1"; image="$2"; repository="$3"; rc=0; count=0
  [ -d "$dir" ] || { _cn_fail "$dir" "directory does not exist"; exit 1; }
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  find "$dir" -mindepth 1 -maxdepth 1 -printf '%f\n' | LC_ALL=C sort > "$tmp/names"
  while read -r dupe; do
    [ -n "$dupe" ] || continue
    _cn_fail "$dir" "file names differ only by case: $(grep -ixF -- "$dupe" "$tmp/names" | tr '\n' ' ')"; rc=1
  done < <(tr '[:upper:]' '[:lower:]' < "$tmp/names" | LC_ALL=C sort | uniq -d)
  echo "### Curated release notes (\`$dir\`)"
  echo
  while read -r name; do
    path="$dir/$name"
    if [ "$name" = "README.md" ]; then
      { [ -f "$path" ] && [ ! -L "$path" ]; } || { _cn_fail "$path" "README.md must be a regular file"; rc=1; }
      continue
    fi
    if ! [[ "$name" =~ ^v${SEMVER_PATTERN}\.md$ ]]; then
      _cn_fail "$path" "unexpected file; the directory holds only README.md and v<MAJOR.MINOR.PATCH[-prerelease]>.md"; rc=1; continue
    fi
    version="${name#v}"; version="${version%.md}"; prerelease="$(semver_prerelease "$version")"
    if ! compose_notes "$version" "$prerelease" "$image" "$repository" "$path" > "$tmp/body.md"; then rc=1; continue; fi
    expected_archives "$version" > "$tmp/expected.txt"
    if ! validate_notes "$tmp/body.md" "$version" "$prerelease" "$image" "$tmp/expected.txt" >&2; then
      _cn_fail "$path" "composed body fails validate_notes"; rc=1; continue
    fi
    if [ "$prerelease" = "true" ]; then kind="pre-release version"; else kind="stable release"; fi
    notes_preview_block "\`$name\`: version \`$version\` ($kind)" "$tmp/body.md"
    count=$((count + 1))
  done < "$tmp/names"
  if [ "$rc" -ne 0 ]; then
    echo "**Curated release notes check failed** (see the errors in the job log)."
  elif [ "$count" -eq 0 ]; then
    echo "No curated release-note fragments are tracked (README.md only). A publishing tag needs \`v<version>.md\`."
  else
    echo "$count curated release-note fragment(s) valid."
  fi
  exit "$rc"
)

# release_body_from_json JSON_FILE -> the release "body" string exactly as stored, with no added newline
# (`gh ... --jq .body` would append one). Fails if the body is missing or not a string.
release_body_from_json() {
  jq -j 'if (.body | type) == "string" then .body else error("release has no body") end' "$1"
}

# compare_release_body EXPECTED_FILE ACTUAL_FILE LABEL -> 0 if byte-identical, else ::error:: with details
compare_release_body() {
  local expected="$1" actual="$2" label="$3"
  if cmp -s -- "$expected" "$actual"; then return 0; fi
  echo "::error::$label differs from the approved release notes: expected $(wc -c < "$expected" | tr -d ' ') bytes, SHA-256 $(sha256sum < "$expected" | cut -d' ' -f1); got $(wc -c < "$actual" | tr -d ' ') bytes, SHA-256 $(sha256sum < "$actual" | cut -d' ' -f1). $(cmp -- "$expected" "$actual" 2>&1 | head -n 1)" >&2
  return 1
}

# check_release_view JSON_FILE NOTES_FILE VERSION EXPECT_DRAFT IMAGE EXPECTED_ARCHIVES_FILE
# JSON_FILE is `gh release view TAG --json body,isDraft,isPrerelease,name,tagName,assets`. Checks that the
# stored body equals NOTES_FILE byte for byte and passes validate_notes, that the release is (EXPECT_DRAFT=true)
# or is not a draft, is not a GitHub prerelease, and has the expected title, tag and 17 assets.
check_release_view() (
  json="$1"; notes="$2"; version="$3"; expect_draft="$4"; image="$5"; expected="$6"
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  release_body_from_json "$json" > "$tmp/body.md" || { echo "::error::Could not read the release body from $json" >&2; exit 1; }
  if [ "$expect_draft" = "true" ]; then label="Draft release body"; else label="Published release body"; fi
  compare_release_body "$notes" "$tmp/body.md" "$label" || exit 1
  validate_notes "$tmp/body.md" "$version" "$(semver_prerelease "$version")" "$image" "$expected" >&2 \
    || { echo "::error::$label fails validate_notes" >&2; exit 1; }
  state="$(jq -r '[.isDraft, .isPrerelease, .name, .tagName, (.assets | length)] | map(tostring) | join("|")' "$json")"
  want="$expect_draft|false|Campertron $version|v$version|17"
  [ "$state" = "$want" ] || { echo "::error::Release state is '$state' (isDraft|isPrerelease|title|tag|assets), expected '$want'" >&2; exit 1; }
)

# ---------------------------------------------------------------------------------------------------------
# .NET 11 platform guards. Every project targets net11.0, the root global.json selects a stable .NET 11 SDK, and every
# release output must be compiled by that SDK and contain and run on .NET 11; any other .NET major anywhere fails the release. Requires jq. Each check prints
# "::error::..." and returns 1 on the first problem, or returns 0.
DOTNET_MAJOR_REQUIRED=11
# A stable SDK of the required major, e.g. 11.0.100 or 11.0.204 (no prerelease suffix, no other major or minor).
SDK_VERSION_PATTERN="^${DOTNET_MAJOR_REQUIRED}\\.0\\.[1-9][0-9]{2}\$"

_rt_fail() { echo "::error::$1"; return 1; }

# check_project_frameworks REPO_ROOT
# Every .csproj (outside bin/ and obj/) sets <TargetFramework>net11.0</TargetFramework> and nothing else;
# no project or Directory.Build.props/.targets sets TargetFrameworks, RuntimeFrameworkVersion or RollForward;
# the root global.json is the approved SDK policy (check_global_json); every .NET base image in CampertronConsole/Dockerfile is 11.0.
check_project_frameworks() {
  local root="$1" f tfms count=0 image
  while IFS= read -r f; do
    count=$((count+1))
    tfms="$(grep -oE '<TargetFramework>[^<]*</TargetFramework>' "$f" | sed -E 's#</?TargetFramework>##g' | sort -u | tr '\n' ' ')"
    [ "$tfms" = "net${DOTNET_MAJOR_REQUIRED}.0 " ] || { _rt_fail "$f: TargetFramework is '${tfms% }', expected net${DOTNET_MAJOR_REQUIRED}.0"; return 1; }
  done < <(find "$root" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.git/*' | sort)
  [ "$count" -gt 0 ] || { _rt_fail "no .csproj files found under $root"; return 1; }
  while IFS= read -r f; do
    if grep -qE '<(TargetFrameworks|RuntimeFrameworkVersion|RollForward)>' "$f"; then
      _rt_fail "$f sets TargetFrameworks, RuntimeFrameworkVersion or RollForward; the release requires plain net${DOTNET_MAJOR_REQUIRED}.0"; return 1
    fi
  done < <(find "$root" \( -name '*.csproj' -o -name 'Directory.Build.props' -o -name 'Directory.Build.targets' -o -name '*.pubxml' \) -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.git/*' | sort)
  check_global_json "$root" || return 1
  if [ -f "$root/CampertronConsole/Dockerfile" ]; then
    count=0
    while IFS= read -r image; do
      count=$((count+1))
      case "$image" in *":${DOTNET_MAJOR_REQUIRED}.0"*) ;; *) _rt_fail "Dockerfile base image $image is not .NET ${DOTNET_MAJOR_REQUIRED}.0"; return 1 ;; esac
    done < <(grep -oE 'mcr\.microsoft\.com/dotnet/[a-z]+(:[^ ]+)?' "$root/CampertronConsole/Dockerfile")
    [ "$count" -ge 2 ] || { _rt_fail "Dockerfile: expected .NET SDK and runtime base images, found $count"; return 1; }
  fi
  return 0
}

# corelib_major COREL_DLL -> the distinct major versions found in System.Private.CoreLib's informational
# version strings (e.g. "11.0.0-rtm.26529.118+<commit>"), one per line. This is the version that
# RuntimeInformation.FrameworkDescription reports.
corelib_major() {
  tr -d '\000' < "$1" | grep -aoE '[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?\+[0-9a-f]{40}' | cut -d. -f1 | sort -u
}

# check_publish_runtime DIR PROJECT RID
# A self-contained `dotnet publish` output: <PROJECT>.runtimeconfig.json targets net11.0 and includes only
# Microsoft.NETCore.App 11.x; <PROJECT>.deps.json targets .NETCoreApp v11.0/<RID> with an 11.x runtime pack;
# System.Private.CoreLib.dll is a .NET 11 build; and no JSON file in the output names another .NET major as a target
# framework ("netN.0"), .NETCoreApp version or runtime pack. Package asset paths such as lib/net8.0/x.dll are allowed.
check_publish_runtime() {
  local dir="$1" project="$2" rid="$3" m="$DOTNET_MAJOR_REQUIRED" rc deps corelib majors hits
  rc="$dir/$project.runtimeconfig.json"; deps="$dir/$project.deps.json"; corelib="$dir/System.Private.CoreLib.dll"
  for f in "$rc" "$deps" "$corelib"; do [ -f "$f" ] || { _rt_fail "$rid $project: $(basename "$f") is missing (not a self-contained .NET output)"; return 1; }; done
  jq -e --arg t "net$m.0" '.runtimeOptions.tfm == $t' "$rc" > /dev/null \
    || { _rt_fail "$rid $project: runtimeconfig tfm is $(jq -c '.runtimeOptions.tfm' "$rc"), expected net$m.0"; return 1; }
  jq -e '(.runtimeOptions.framework // .runtimeOptions.frameworks) == null' "$rc" > /dev/null \
    || { _rt_fail "$rid $project: runtimeconfig references a shared framework; the release must be self-contained"; return 1; }
  jq -e --arg p "$m." '(.runtimeOptions.includedFrameworks // []) as $f | ($f | length) > 0
        and ($f | any(.name == "Microsoft.NETCore.App"))
        and ($f | all(.version | startswith($p)))' "$rc" > /dev/null \
    || { _rt_fail "$rid $project: included frameworks are $(jq -c '.runtimeOptions.includedFrameworks' "$rc"), expected Microsoft.NETCore.App $m.x only"; return 1; }
  jq -e --arg n ".NETCoreApp,Version=v$m.0/$rid" '.runtimeTarget.name == $n' "$deps" > /dev/null \
    || { _rt_fail "$rid $project: deps.json runtime target is $(jq -c '.runtimeTarget.name' "$deps"), expected .NETCoreApp,Version=v$m.0/$rid"; return 1; }
  jq -e --arg p "runtimepack.Microsoft.NETCore.App.Runtime.$rid/" --arg v "$m." \
      '[.libraries | keys[] | select(startswith($p))] as $k | ($k | length) == 1 and ($k[0] | ltrimstr($p) | startswith($v))' "$deps" > /dev/null \
    || { _rt_fail "$rid $project: deps.json runtime pack is $(jq -c '[.libraries | keys[] | select(startswith("runtimepack."))]' "$deps"), expected Microsoft.NETCore.App.Runtime.$rid/$m.x"; return 1; }
  majors="$(corelib_major "$corelib" | tr '\n' ' ')"
  [ "$majors" = "$m " ] || { _rt_fail "$rid $project: System.Private.CoreLib.dll is .NET '${majors% }', expected $m"; return 1; }
  hits="$(grep -ohE '"net[0-9]+\.0|NETCoreApp,Version=v[0-9]+\.|Microsoft\.NETCore\.App\.Runtime\.[^/"]+/[0-9]+\.' "$dir"/*.json 2> /dev/null \
          | grep -oE '[0-9]+\.0?$' | cut -d. -f1 | sort -u | grep -vx "$m" | tr '\n' ' ' || true)"
  [ -z "$hits" ] || { _rt_fail "$rid $project: its JSON files reference .NET ${hits% }, expected only .NET $m"; return 1; }
  return 0
}

# check_framework_dependent_runtimeconfig FILE
# The Docker image's framework-dependent console: tfm net11.0 and the Microsoft.NETCore.App 11.x shared framework.
check_framework_dependent_runtimeconfig() {
  local rc="$1" m="$DOTNET_MAJOR_REQUIRED"
  [ -f "$rc" ] || { _rt_fail "$rc is missing"; return 1; }
  jq -e --arg t "net$m.0" --arg p "$m." '.runtimeOptions.tfm == $t
        and ([.runtimeOptions.framework // empty, (.runtimeOptions.frameworks // [])[]] as $f
             | ($f | length) > 0 and ($f | any(.name == "Microsoft.NETCore.App")) and ($f | all(.version | startswith($p))))' "$rc" > /dev/null \
    || { _rt_fail "$(basename "$rc"): expected tfm net$m.0 on the Microsoft.NETCore.App $m.x framework, got $(jq -c '.runtimeOptions' "$rc")"; return 1; }
}

# check_framework_report TEXT [EXACT_RUNTIME_VERSION]
# TEXT is the output of `CampertronConsole --version` or `CampertronGUI --version`: exactly one
# "Framework => .NET 11.x.y (<arch>)" line, i.e. RuntimeInformation.FrameworkDescription of the running process.
# With EXACT_RUNTIME_VERSION (e.g. the includedFrameworks version of a self-contained output), the process must
# report exactly that runtime, which shows it ran on the bundled runtime and not on an installed one.
check_framework_report() {
  local text="$1" exact="${2:-}" lines
  lines="$(grep -c '^Framework => ' <<<"$text" || true)"
  [ "$lines" -eq 1 ] || { _rt_fail "expected one 'Framework => ' line, got $lines: $text"; return 1; }
  grep -qE "^Framework => \.NET ${DOTNET_MAJOR_REQUIRED}\.[0-9]+\.[0-9]+[^ ]* \([A-Za-z0-9]+\)\$" <<<"$text" \
    || { _rt_fail "process does not run on .NET $DOTNET_MAJOR_REQUIRED: $(grep '^Framework => ' <<<"$text")"; return 1; }
  if [ -n "$exact" ]; then
    grep -qE "^Framework => \.NET ${exact//./\\.} \(" <<<"$text" \
      || { _rt_fail "process reports $(grep '^Framework => ' <<<"$text"), expected the bundled runtime .NET $exact"; return 1; }
  fi
  return 0
}

# ---------------------------------------------------------------------------------------------------------
# SDK selection, package policy and toolchain provenance (decision D31). global.json makes `dotnet` resolve a stable
# .NET 11 SDK; actions/setup-dotnet only installs one. These checks prove what was actually used.

# check_global_json REPO_ROOT
# The root global.json exists, is strict JSON (no comments) and is exactly the approved policy: sdk.version a stable
# 11.0.Fnn, rollForward "latestFeature" (newest installed 11.0 SDK, never another major), allowPrerelease false and a
# non-empty errorMessage, with no other keys.
check_global_json() {
  local f="$1/global.json"
  [ -f "$f" ] || { _rt_fail "global.json is missing; dotnet would use the newest installed SDK"; return 1; }
  jq -e . "$f" > /dev/null 2>&1 || { _rt_fail "global.json is not strict JSON"; return 1; }
  jq -e --arg p "$SDK_VERSION_PATTERN" '
      (keys == ["sdk"]) and (.sdk | type == "object") and (.sdk | keys == ["allowPrerelease","errorMessage","rollForward","version"])
      and (.sdk.version | type == "string" and test($p))
      and .sdk.rollForward == "latestFeature" and .sdk.allowPrerelease == false
      and (.sdk.errorMessage | type == "string" and length > 0)' "$f" > /dev/null \
    || { _rt_fail "global.json is not the approved .NET $DOTNET_MAJOR_REQUIRED SDK policy: $(jq -c . "$f")"; return 1; }
}

# check_selected_sdk VERSION
# VERSION is the output of `dotnet --version` in the repository: a stable .NET 11.0 SDK.
check_selected_sdk() {
  [[ "$1" =~ $SDK_VERSION_PATTERN ]] || { _rt_fail "selected .NET SDK is '$1', expected a stable ${DOTNET_MAJOR_REQUIRED}.0.x SDK"; return 1; }
}

# check_package_policy ASSETS_JSON...
# Restored obj/project.assets.json files: no prerelease package, and no Microsoft.EntityFrameworkCore*,
# Microsoft.Data.Sqlite* or Microsoft.Extensions.* package of another major (EF Core 9 and 10 and the .NET 9/10
# Microsoft.Extensions packages are out). Other packages may target older compatible frameworks.
check_package_policy() {
  local f found bad
  [ "$#" -gt 0 ] || { _rt_fail "package policy: no project.assets.json files given"; return 1; }
  for f in "$@"; do [ -f "$f" ] || { _rt_fail "package policy: $f does not exist"; return 1; }; done
  found="$(jq -r --arg m "$DOTNET_MAJOR_REQUIRED" '.libraries | to_entries[] | select(.value.type == "package") | .key
           | select(test("-") or (test("^Microsoft\\.(EntityFrameworkCore|Data\\.Sqlite|Extensions)") and (split("/")[1] | split(".")[0] != $m)))' "$@")" \
    || { _rt_fail "package policy: cannot read $*"; return 1; }
  bad="$(sort -u <<<"$found" | tr '\n' ' ' | sed 's/^ *//; s/ *$//')"
  [ -z "$bad" ] || { _rt_fail "package policy violated by: $bad"; return 1; }
}

# pdb_option PDB KEY -> one value of a portable PDB's compilation options (null-separated key/value pairs that
# Roslyn writes for deterministic builds, e.g. compiler-version, runtime-version, language-version).
pdb_option() { LC_ALL=C tr '\000' '\n' < "$1" | grep -m1 -A1 -x "$2" | tail -n 1; }

# check_pdb_provenance DIR PROJECT
# CampertronLibrary.pdb and PROJECT.pdb in DIR were produced by a compiler running on .NET 11, i.e. by an SDK 11
# (official "11.0.0-rtm.<build>+<commit>" and source-built "11.0.0-dev" stamps are both accepted). Only Campertron's
# own PDBs are checked; the native PDBs excluded from Windows GUI packages (TD-35) are not involved. Prints
# "<compiler-version> <runtime-version>" of the library on success.
check_pdb_provenance() {
  local dir="$1" project="$2" pdb rt cv
  for pdb in "$dir/CampertronLibrary.pdb" "$dir/$project.pdb"; do
    [ -f "$pdb" ] || { _rt_fail "$(basename "$pdb") is missing in $dir; compiler provenance cannot be checked"; return 1; }
    rt="$(pdb_option "$pdb" runtime-version)"; cv="$(pdb_option "$pdb" compiler-version)"
    [ -n "$cv" ] && [[ "$rt" =~ ^${DOTNET_MAJOR_REQUIRED}\.[0-9]+\.[0-9]+ ]] \
      || { _rt_fail "$(basename "$pdb"): compiler '$cv' ran on .NET '$rt', expected an SDK $DOTNET_MAJOR_REQUIRED compiler"; return 1; }
  done
  echo "$(pdb_option "$dir/CampertronLibrary.pdb" compiler-version) $(pdb_option "$dir/CampertronLibrary.pdb" runtime-version)"
}

# check_single_toolchain FILE
# FILE has one "<archive> <compiler-version> <runtime-version>" line per release archive. All archives must have been
# compiled by the same toolchain; otherwise every toolchain is listed with its archives.
check_single_toolchain() {
  local n
  [ -s "$1" ] || { _rt_fail "no archive toolchains to compare"; return 1; }
  n="$(awk '{print $2" "$3}' "$1" | sort -u | wc -l)"
  [ "$n" -eq 1 ] || { _rt_fail "release archives were built by $n toolchains: $(awk '{t=$2" on "$3; a[t]=a[t]" "$1} END {for (t in a) printf "[%s:%s] ", t, a[t]}' "$1")"; return 1; }
}
