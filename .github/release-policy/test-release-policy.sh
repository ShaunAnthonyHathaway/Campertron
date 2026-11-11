#!/usr/bin/env bash
# Tests for release-policy.sh (prerelease and stable behaviour). Run by the "Release verify" job of
# .github/workflows/release.yml on every trigger, and locally with: bash .github/release-policy/test-release-policy.sh
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=release-policy.sh
source "$here/release-policy.sh"

image="ghcr.io/example-owner/campertronconsole"
repository="example-owner/Campertron"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
passed=0; failed=0

expect_eq() { # description expected actual
  if [ "$2" = "$3" ]; then passed=$((passed+1)); echo "PASS  $1"
  else failed=$((failed+1)); echo "FAIL  $1"; echo "      expected: $2"; echo "      actual:   $3"; fi
}
expect_rc() { # description expected(0|nonzero) actual_rc
  if { [ "$2" = 0 ] && [ "$3" -eq 0 ]; } || { [ "$2" = nonzero ] && [ "$3" -ne 0 ]; }; then passed=$((passed+1)); echo "PASS  $1"
  else failed=$((failed+1)); echo "FAIL  $1 (expected rc $2, got $3)"; fi
}

# --- Docker tags
expect_eq "pre-release image gets only the exact tag" \
  "--tag $image:0.1.0-beta.5" "$(docker_tag_args "$image" 0.1.0-beta.5 true)"
expect_eq "stable image gets exact, MAJOR.MINOR and latest" \
  "--tag $image:1.2.3 --tag $image:1.2 --tag $image:latest" "$(docker_tag_args "$image" 1.2.3 false)"

# --- GitHub release flags: always a normal release marked Latest, never --prerelease
for p in true false; do
  flags="$(github_release_flags "$p")"
  expect_eq "GitHub release flags (prerelease=$p) are exactly --latest" "--latest" "$flags"
  case " $flags " in *" --prerelease"*|*"--latest=false"*) failed=$((failed+1)); echo "FAIL  flags (prerelease=$p) contain a prerelease/not-latest flag";; *) passed=$((passed+1)); echo "PASS  flags (prerelease=$p) contain no --prerelease or --latest=false";; esac
done

# --- expected archives
expected="$work/expected.txt"
expected_archives 0.1.0-beta.5 > "$expected"
expect_eq "16 expected archive names" "16" "$(wc -l < "$expected" | tr -d ' ')"
expect_eq "Windows archives are .zip, others .tar.gz" "6 10" "$(grep -c '\.zip$' "$expected") $(grep -c '\.tar\.gz$' "$expected")"

# --- rendered notes validate, for both kinds of version
for spec in "0.1.0-beta.5 true" "1.2.3 false"; do
  set -- $spec
  expected_archives "$1" > "$work/expected-$1.txt"
  render_notes "$1" "$2" "$image" "$repository" > "$work/notes-$1.md"
  validate_notes "$work/notes-$1.md" "$1" "$2" "$image" "$work/expected-$1.txt" > "$work/out.txt"; rc=$?
  expect_rc "notes for $1 (prerelease=$2) validate" 0 $rc
  grep -q "marked Latest" "$work/notes-$1.md"; expect_rc "notes for $1 say the GitHub release is marked Latest" 0 $?
done
grep -qF '**Pre-release version.**' "$work/notes-0.1.0-beta.5.md"; expect_rc "pre-release notes are worded as a pre-release version" 0 $?
grep -qF 'Docker `latest` and the stable aliases are not updated' "$work/notes-0.1.0-beta.5.md"; expect_rc "pre-release notes say Docker latest/aliases are not updated" 0 $?
grep -qF '`1.2.3`, `1.2` and `latest`' "$work/notes-1.2.3.md"; expect_rc "stable notes list exact, MAJOR.MINOR and latest Docker tags" 0 $?

# --- tampered notes are rejected
tamper() { # description version prerelease sed-expression
  cp "$work/notes-$2.md" "$work/t.md"; sed -i "$4" "$work/t.md"
  validate_notes "$work/t.md" "$2" "$3" "$image" "$work/expected-$2.txt" > "$work/out.txt"; rc=$?
  expect_rc "rejects: $1" nonzero $rc
}
tamper "wrong version title"                      0.1.0-beta.5 true  '1s/.*/# Campertron 9.9.9/'
tamper "old wording: GitHub prerelease, not Latest" 0.1.0-beta.5 true 's/It is published as a normal GitHub release and marked Latest/This version is published as a GitHub prerelease and is not marked as the latest release/'
tamper "old sentence added next to Latest"        0.1.0-beta.5 true  '3a This version is published as a GitHub prerelease and is not marked as the latest release.'
tamper "Latest statement removed (pre-release)"   0.1.0-beta.5 true  's/ and marked Latest//'
tamper "Latest statement removed (stable)"        1.2.3 false        's/ and marked Latest//'
tamper "pre-release claims stable"                0.1.0-beta.5 true  's/\*\*Pre-release version\.\*\*/**Stable release.**/'
tamper "stable claims pre-release"                1.2.3 false        's/\*\*Stable release\.\*\*/**Pre-release version.**/'
tamper "pre-release says Docker latest is updated" 0.1.0-beta.5 true 's/Docker `latest` and the stable aliases are not updated/Docker `latest` is updated/'
tamper "stable omits Docker latest"               1.2.3 false        's/`1.2` and `latest`/`1.2`/'
tamper "archive row removed"                      0.1.0-beta.5 true  '/`osx-arm64`/d'
tamper "non-native platform marked as tested"     0.1.0-beta.5 true  '0,/no: supported and cross-built, not run natively/s//yes/'
tamper "wrong Docker pull tag"                    0.1.0-beta.5 true  's/campertronconsole:0.1.0-beta.5/campertronconsole:latest/'

echo "=== $passed passed, $failed failed"
[ "$failed" -eq 0 ]
