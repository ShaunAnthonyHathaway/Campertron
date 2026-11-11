#!/usr/bin/env bash
# Tests for release-policy.sh (prerelease and stable behaviour, .NET 10 runtime guards). Run by the "Release verify" job of
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

tamper "compatibility section removed"            0.1.0-beta.5 true  '/^## Compatibility with older versions$/d'
tamper "old-version warning removed"              1.2.3 false        's/Do not run versions older than 0.1.0-beta.4/Avoid old versions/'
tamper "--version instructions removed"           0.1.0-beta.5 true  's/`CampertronConsole --version` and `CampertronGUI --version`/the menu/'

# --- .NET 10 guards: project files, Dockerfile, publish output, --version report
here_root="$(cd "$here/../.." && pwd)"
check_project_frameworks "$here_root" > "$work/out.txt"; expect_rc "repository projects, props and Dockerfile are .NET 10" 0 $?

proj_fixture() { # creates $work/p with one valid project, Dockerfile and nothing else
  rm -rf "$work/p"; mkdir -p "$work/p/App" "$work/p/CampertronConsole"
  printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n' > "$work/p/App/App.csproj"
  printf 'FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base\nFROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build\n' > "$work/p/CampertronConsole/Dockerfile"
}
proj_case() { # description expected-rc setup-command
  proj_fixture; eval "$3"
  check_project_frameworks "$work/p" > "$work/out.txt"; expect_rc "projects: $1" "$2" $?
}
proj_case "valid fixture passes"                    0       ':'
proj_case "net9.0 project rejected"                 nonzero "sed -i 's/net10.0/net9.0/' '$work/p/App/App.csproj'"
proj_case "second project on net9.0 rejected"       nonzero "mkdir -p '$work/p/B' && sed 's/net10.0/net9.0/' '$work/p/App/App.csproj' > '$work/p/B/B.csproj'"
proj_case "TargetFrameworks rejected"               nonzero "sed -i 's#<TargetFramework>net10.0</TargetFramework>#<TargetFramework>net10.0</TargetFramework><TargetFrameworks>net9.0;net10.0</TargetFrameworks>#' '$work/p/App/App.csproj'"
proj_case "RuntimeFrameworkVersion rejected"        nonzero "sed -i 's#</PropertyGroup>#<RuntimeFrameworkVersion>9.0.0</RuntimeFrameworkVersion></PropertyGroup>#' '$work/p/App/App.csproj'"
proj_case "RollForward in Directory.Build.props rejected" nonzero "printf '<Project><PropertyGroup><RollForward>Major</RollForward></PropertyGroup></Project>\n' > '$work/p/Directory.Build.props'"
proj_case "global.json pinning SDK 9 rejected"      nonzero "printf '{\"sdk\":{\"version\":\"9.0.100\"}}\n' > '$work/p/global.json'"
proj_case "global.json pinning SDK 10 accepted"     0       "printf '{\"sdk\":{\"version\":\"10.0.100\"}}\n' > '$work/p/global.json'"
proj_case "Dockerfile runtime:9.0 rejected"         nonzero "sed -i 's/runtime:10.0/runtime:9.0/' '$work/p/CampertronConsole/Dockerfile'"
proj_case "Dockerfile untagged image rejected"      nonzero "sed -i 's/sdk:10.0/sdk/' '$work/p/CampertronConsole/Dockerfile'"

commit="0123456789abcdef0123456789abcdef01234567"
pub_fixture() { # creates $work/o, a minimal self-contained linux-x64 output of App on runtime 10.0.15
  rm -rf "$work/o"; mkdir -p "$work/o"
  printf '{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.15"}]}}\n' > "$work/o/App.runtimeconfig.json"
  printf '{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0/linux-x64"},"libraries":{"App/1.0.0":{},"runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/10.0.15":{}}}\n' > "$work/o/App.deps.json"
  { printf 'MZ\0\0'; printf '10.0.15-servicing.26529.108+%s' "$commit" | iconv -f UTF-8 -t UTF-16LE; printf '\0\0'; } > "$work/o/System.Private.CoreLib.dll"
}
pub_case() { # description expected-rc setup-command
  pub_fixture; eval "$3"
  check_publish_runtime "$work/o" App linux-x64 > "$work/out.txt"; expect_rc "publish output: $1" "$2" $?
}
pub_case "valid .NET 10 self-contained output passes" 0     ':'
pub_case "runtimeconfig tfm net9.0 rejected"        nonzero "sed -i 's/net10.0/net9.0/' '$work/o/App.runtimeconfig.json'"
pub_case "included runtime 9.0.0 rejected"          nonzero "sed -i 's/10.0.15/9.0.0/' '$work/o/App.runtimeconfig.json'"
pub_case "framework-dependent output rejected"      nonzero "printf '{\"runtimeOptions\":{\"tfm\":\"net10.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}\n' > '$work/o/App.runtimeconfig.json'"
pub_case "deps.json targets v9.0 rejected"          nonzero "sed -i 's/Version=v10.0/Version=v9.0/' '$work/o/App.deps.json'"
pub_case "deps.json for another RID rejected"       nonzero "sed -i 's#v10.0/linux-x64#v10.0/win-x64#' '$work/o/App.deps.json'"
pub_case "runtime pack 9.0.0 rejected"              nonzero "sed -i 's#linux-x64/10.0.15#linux-x64/9.0.0#' '$work/o/App.deps.json'"
pub_case "CoreLib from .NET 9 rejected"             nonzero "{ printf 'MZ'; printf '9.0.0+%s' '$commit' | iconv -f UTF-8 -t UTF-16LE; } > '$work/o/System.Private.CoreLib.dll'"
pub_case "CoreLib with mixed 9 and 10 rejected"     nonzero "printf '9.0.0+%s' '$commit' >> '$work/o/System.Private.CoreLib.dll'"
pub_case "CoreLib without a version rejected"       nonzero "printf 'MZ' > '$work/o/System.Private.CoreLib.dll'"
pub_case "missing CoreLib rejected"                 nonzero "rm '$work/o/System.Private.CoreLib.dll'"
pub_case "missing runtimeconfig rejected"           nonzero "rm '$work/o/App.runtimeconfig.json'"
pub_case "another JSON file naming net9.0 rejected" nonzero "printf '{\"tfm\":\"net9.0\"}\n' > '$work/o/Other.runtimeconfig.json'"

printf '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}\n' > "$work/fd.json"
check_framework_dependent_runtimeconfig "$work/fd.json" > "$work/out.txt"; expect_rc "image runtimeconfig on .NET 10 passes" 0 $?
sed -i 's/10.0.0/9.0.0/' "$work/fd.json"
check_framework_dependent_runtimeconfig "$work/fd.json" > "$work/out.txt"; expect_rc "image runtimeconfig on .NET 9 rejected" nonzero $?
printf '{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}\n' > "$work/fd.json"
check_framework_dependent_runtimeconfig "$work/fd.json" > "$work/out.txt"; expect_rc "image runtimeconfig with tfm net9.0 rejected" nonzero $?

report_case() { # description expected-rc text
  check_framework_report "$3" > "$work/out.txt"; expect_rc "--version report: $1" "$2" $?
}
report_case ".NET 10 accepted"            0       $'CampertronConsole 0.1.0-beta.5+abc\nFramework => .NET 10.0.15 (X64)'
report_case ".NET 10 on Arm64 accepted"   0       $'CampertronGUI 0.1.0-beta.5+abc\nFramework => .NET 10.0.15 (Arm64)'
report_case ".NET 9.0.0 rejected"         nonzero $'CampertronConsole 1.0.0\nFramework => .NET 9.0.0 (X64)'
report_case ".NET 100 rejected"           nonzero $'CampertronConsole 1.0.0\nFramework => .NET 100.0.1 (X64)'
report_case "missing Framework line rejected" nonzero $'CampertronConsole 1.0.0'
report_case "two Framework lines rejected" nonzero $'Framework => .NET 10.0.15 (X64)\nFramework => .NET 9.0.0 (X64)'
report_case "menu output instead of --version rejected" nonzero $'-> Search\n   View Config'
check_framework_report $'X 1\nFramework => .NET 10.0.15 (X64)' 10.0.15 > "$work/out.txt"; expect_rc "--version report: matches the bundled runtime 10.0.15" 0 $?
check_framework_report $'X 1\nFramework => .NET 10.0.14 (X64)' 10.0.15 > "$work/out.txt"; expect_rc "--version report: installed runtime 10.0.14 instead of bundled 10.0.15 rejected" nonzero $?
check_framework_report $'X 1\nFramework => .NET 10.0.150 (X64)' 10.0.15 > "$work/out.txt"; expect_rc "--version report: 10.0.150 does not match 10.0.15" nonzero $?

echo "=== $passed passed, $failed failed"
[ "$failed" -eq 0 ]
