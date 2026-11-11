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
  echo "To identify a copy: \`CampertronConsole --version\` and \`CampertronGUI --version\` print the version and the .NET runtime (added after 0.1.0-beta.4). In any version, the console's View Config shows \`Framework => .NET 10\` for 0.1.0-beta.4 and later, and .NET 9 or older for the old versions."
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
# .NET 10 runtime guards. Every project targets net10.0 and every release output must contain and run on
# .NET 10; a .NET 9 (or any other) runtime anywhere fails the release. Requires jq. Each check prints
# "::error::..." and returns 1 on the first problem, or returns 0.
DOTNET_MAJOR_REQUIRED=10

_rt_fail() { echo "::error::$1"; return 1; }

# check_project_frameworks REPO_ROOT
# Every .csproj (outside bin/ and obj/) sets <TargetFramework>net10.0</TargetFramework> and nothing else;
# no project or Directory.Build.props/.targets sets TargetFrameworks, RuntimeFrameworkVersion or RollForward;
# a global.json, if one is added, pins a 10.x SDK; every .NET base image in CampertronConsole/Dockerfile is 10.0.
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
  if [ -f "$root/global.json" ]; then
    jq -e --arg m "$DOTNET_MAJOR_REQUIRED." '(.sdk.version // "") | startswith($m)' "$root/global.json" > /dev/null \
      || { _rt_fail "global.json does not pin a .NET $DOTNET_MAJOR_REQUIRED SDK"; return 1; }
  fi
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
# version strings (e.g. "10.0.15-servicing.26529.108+<commit>"), one per line. This is the version that
# RuntimeInformation.FrameworkDescription reports.
corelib_major() {
  tr -d '\000' < "$1" | grep -aoE '[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?\+[0-9a-f]{40}' | cut -d. -f1 | sort -u
}

# check_publish_runtime DIR PROJECT RID
# A self-contained `dotnet publish` output: <PROJECT>.runtimeconfig.json targets net10.0 and includes only
# Microsoft.NETCore.App 10.x; <PROJECT>.deps.json targets .NETCoreApp v10.0/<RID> with a 10.x runtime pack;
# System.Private.CoreLib.dll is a .NET 10 build; and no JSON file in the output mentions net9.0 or a 9.x runtime pack.
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
  hits="$(grep -lE '"net9\.0|NETCoreApp,Version=v9\.|Microsoft\.NETCore\.App\.Runtime\.[^/"]+/9\.' "$dir"/*.json 2> /dev/null || true)"
  [ -z "$hits" ] || { _rt_fail "$rid $project: .NET 9 references in $(echo $hits)"; return 1; }
  return 0
}

# check_framework_dependent_runtimeconfig FILE
# The Docker image's framework-dependent console: tfm net10.0 and the Microsoft.NETCore.App 10.x shared framework.
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
# "Framework => .NET 10.x.y (<arch>)" line, i.e. RuntimeInformation.FrameworkDescription of the running process.
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
