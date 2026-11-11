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
  grep -qF "docker pull $image:$version" "$notes" || { _vn_fail "Docker pull command for $image:$version missing"; return 1; }
  grep -qF "linux/amd64 and linux/arm64" "$notes" || { _vn_fail "Docker platforms missing"; return 1; }
  grep -qF "sha256sum -c SHA256SUMS.txt" "$notes" || { _vn_fail "checksum instructions missing"; return 1; }
  return 0
}
