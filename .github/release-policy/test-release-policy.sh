#!/usr/bin/env bash
# Tests for release-policy.sh (prerelease and stable behaviour, .NET 11 platform, SDK, package and toolchain guards). Run by the "Release verify" job of
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

# --- .NET 11 guards: project files, global.json, Dockerfile, publish output, --version report
here_root="$(cd "$here/../.." && pwd)"
check_project_frameworks "$here_root" > "$work/out.txt"; expect_rc "repository projects, props and Dockerfile are .NET 11" 0 $?

proj_fixture() { # creates $work/p with one valid project, global.json, Dockerfile and nothing else
  rm -rf "$work/p"; mkdir -p "$work/p/App" "$work/p/CampertronConsole"
  printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>\n' > "$work/p/App/App.csproj"
  printf '{"sdk":{"version":"11.0.100","rollForward":"latestFeature","allowPrerelease":false,"errorMessage":"Install a .NET 11 SDK."}}\n' > "$work/p/global.json"
  printf 'FROM mcr.microsoft.com/dotnet/runtime:11.0 AS base\nFROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build\n' > "$work/p/CampertronConsole/Dockerfile"
}
proj_case() { # description expected-rc setup-command
  proj_fixture; eval "$3"
  check_project_frameworks "$work/p" > "$work/out.txt"; expect_rc "projects: $1" "$2" $?
}
proj_case "valid fixture passes"                    0       ':'
proj_case "net9.0 project rejected"                 nonzero "sed -i 's/net11.0/net9.0/' '$work/p/App/App.csproj'"
proj_case "second project on net9.0 rejected"       nonzero "mkdir -p '$work/p/B' && sed 's/net11.0/net9.0/' '$work/p/App/App.csproj' > '$work/p/B/B.csproj'"
proj_case "TargetFrameworks rejected"               nonzero "sed -i 's#<TargetFramework>net11.0</TargetFramework>#<TargetFramework>net11.0</TargetFramework><TargetFrameworks>net9.0;net11.0</TargetFrameworks>#' '$work/p/App/App.csproj'"
proj_case "RuntimeFrameworkVersion rejected"        nonzero "sed -i 's#</PropertyGroup>#<RuntimeFrameworkVersion>9.0.0</RuntimeFrameworkVersion></PropertyGroup>#' '$work/p/App/App.csproj'"
proj_case "RollForward in Directory.Build.props rejected" nonzero "printf '<Project><PropertyGroup><RollForward>Major</RollForward></PropertyGroup></Project>\n' > '$work/p/Directory.Build.props'"
proj_case "global.json pinning SDK 9 rejected"      nonzero "printf '{\"sdk\":{\"version\":\"9.0.100\"}}\n' > '$work/p/global.json'"
proj_case "net10.0 project rejected"                nonzero "sed -i 's/net11.0/net10.0/' '$work/p/App/App.csproj'"
proj_case "Dockerfile sdk:10.0 rejected"            nonzero "sed -i 's/sdk:11.0/sdk:10.0/' '$work/p/CampertronConsole/Dockerfile'"
gj_case() { proj_case "global.json: $1" "$2" "jq -c '$3' '$work/p/global.json' > '$work/g.json' && mv '$work/g.json' '$work/p/global.json'"; }
gj_case "approved policy accepted"                0       '.'
proj_case "global.json: missing rejected"         nonzero "rm '$work/p/global.json'"
proj_case "global.json: with comments rejected"   nonzero "printf '// x\\n{\"sdk\":{}}\\n' > '$work/p/global.json'"
gj_case "SDK 10 rejected"                         nonzero '.sdk.version = "10.0.100"'
gj_case "SDK 12 rejected"                         nonzero '.sdk.version = "12.0.100"'
gj_case "prerelease version rejected"             nonzero '.sdk.version = "11.0.100-rc.3.26522.148"'
gj_case "partial version 11.0.x rejected"         nonzero '.sdk.version = "11.0.x"'
gj_case "rollForward latestMajor rejected"        nonzero '.sdk.rollForward = "latestMajor"'
gj_case "rollForward major rejected"              nonzero '.sdk.rollForward = "major"'
gj_case "rollForward missing rejected"            nonzero 'del(.sdk.rollForward)'
gj_case "allowPrerelease true rejected"           nonzero '.sdk.allowPrerelease = true'
gj_case "allowPrerelease missing rejected"        nonzero 'del(.sdk.allowPrerelease)'
gj_case "empty errorMessage rejected"             nonzero '.sdk.errorMessage = ""'
gj_case "extra paths key rejected"                nonzero '.sdk.paths = [".dotnet"]'

proj_case "Dockerfile runtime:9.0 rejected"         nonzero "sed -i 's/runtime:11.0/runtime:9.0/' '$work/p/CampertronConsole/Dockerfile'"
proj_case "Dockerfile untagged image rejected"      nonzero "sed -i 's/sdk:11.0/sdk/' '$work/p/CampertronConsole/Dockerfile'"

commit="0123456789abcdef0123456789abcdef01234567"
pub_fixture() { # creates $work/o, a minimal self-contained linux-x64 output of App on runtime 11.0.0
  rm -rf "$work/o"; mkdir -p "$work/o"
  printf '{"runtimeOptions":{"tfm":"net11.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"11.0.0"}]}}\n' > "$work/o/App.runtimeconfig.json"
  printf '{"runtimeTarget":{"name":".NETCoreApp,Version=v11.0/linux-x64"},"libraries":{"App/1.0.0":{},"runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/11.0.0":{}}}\n' > "$work/o/App.deps.json"
  { printf 'MZ\0\0'; printf '11.0.0-rtm.26529.118+%s' "$commit" | iconv -f UTF-8 -t UTF-16LE; printf '\0\0'; } > "$work/o/System.Private.CoreLib.dll"
}
pub_case() { # description expected-rc setup-command
  pub_fixture; eval "$3"
  check_publish_runtime "$work/o" App linux-x64 > "$work/out.txt"; expect_rc "publish output: $1" "$2" $?
}
pub_case "valid .NET 11 self-contained output passes" 0     ':'
pub_case "runtimeconfig tfm net9.0 rejected"        nonzero "sed -i 's/net11.0/net9.0/' '$work/o/App.runtimeconfig.json'"
pub_case "included runtime 9.0.0 rejected"          nonzero "sed -i 's/11.0.0/9.0.0/' '$work/o/App.runtimeconfig.json'"
pub_case "framework-dependent output rejected"      nonzero "printf '{\"runtimeOptions\":{\"tfm\":\"net11.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"11.0.0\"}}}\n' > '$work/o/App.runtimeconfig.json'"
pub_case "deps.json targets v9.0 rejected"          nonzero "sed -i 's/Version=v11.0/Version=v9.0/' '$work/o/App.deps.json'"
pub_case "deps.json for another RID rejected"       nonzero "sed -i 's#v11.0/linux-x64#v11.0/win-x64#' '$work/o/App.deps.json'"
pub_case "runtime pack 9.0.0 rejected"              nonzero "sed -i 's#linux-x64/11.0.0#linux-x64/9.0.0#' '$work/o/App.deps.json'"
pub_case "CoreLib from .NET 9 rejected"             nonzero "{ printf 'MZ'; printf '9.0.0+%s' '$commit' | iconv -f UTF-8 -t UTF-16LE; } > '$work/o/System.Private.CoreLib.dll'"
pub_case "CoreLib with mixed 9 and 10 rejected"     nonzero "printf '9.0.0+%s' '$commit' >> '$work/o/System.Private.CoreLib.dll'"
pub_case "CoreLib without a version rejected"       nonzero "printf 'MZ' > '$work/o/System.Private.CoreLib.dll'"
pub_case "missing CoreLib rejected"                 nonzero "rm '$work/o/System.Private.CoreLib.dll'"
pub_case "missing runtimeconfig rejected"           nonzero "rm '$work/o/App.runtimeconfig.json'"
pub_case "another JSON file naming net9.0 rejected" nonzero "printf '{\"tfm\":\"net9.0\"}\n' > '$work/o/Other.runtimeconfig.json'"

pub_case "runtimeconfig tfm net10.0 rejected"       nonzero "sed -i 's/net11.0/net10.0/' '$work/o/App.runtimeconfig.json'"
pub_case "runtime pack 10.0.15 rejected"            nonzero "sed -i 's#linux-x64/11.0.0#linux-x64/10.0.15#' '$work/o/App.deps.json'"
pub_case "another JSON file naming net10.0 rejected" nonzero "printf '{\"tfm\":\"net10.0\"}\n' > '$work/o/Other.runtimeconfig.json'"
pub_case "package lib/net8.0 asset path accepted"   0       "sed -i 's#\"App/1.0.0\":{}#\"App/1.0.0\":{},\"Y/1\":{\"runtime\":{\"lib/net8.0/Y.dll\":{}}}#' '$work/o/App.deps.json'"

# --- SDK selection (dotnet --version)
sdk_case() { check_selected_sdk "$2" > "$work/out.txt"; expect_rc "selected SDK: $1" "$3" $?; }
sdk_case "11.0.100 accepted"            11.0.100                 0
sdk_case "11.0.405 accepted"            11.0.405                 0
sdk_case "10.0.404 rejected"            10.0.404                 nonzero
sdk_case "12.0.100 rejected"            12.0.100                 nonzero
sdk_case "prerelease 11 rejected"       11.0.100-rc.3.26522.148  nonzero
sdk_case "11.1.100 rejected"            11.1.100                 nonzero
sdk_case "empty rejected"               ""                       nonzero

# --- portable PDB provenance and one toolchain per release
pdb_fixture() { # $1 file, $2 compiler-version, $3 runtime-version
  { printf 'BSJB\0\0junk\0'; printf 'language\0C#\0compiler-version\0%s\0runtime-version\0%s\0optimization\0release\0' "$2" "$3"; } > "$1"; }
prov_case() { # description expected-rc compiler runtime [project-pdb-runtime]
  rm -rf "$work/pd"; mkdir -p "$work/pd"
  pdb_fixture "$work/pd/CampertronLibrary.pdb" "$3" "$4"; pdb_fixture "$work/pd/App.pdb" "$3" "${5:-$4}"
  check_pdb_provenance "$work/pd" App > "$work/out.txt"; expect_rc "pdb provenance: $1" "$2" $?; }
prov_case "official SDK 11 stamp accepted"      0       "5.12.0-1.26529.118+297acf9" "11.0.0-rtm.26529.118+297acf9"
prov_case "source-built SDK 11 stamp accepted"  0       "5.12.0-dev" "11.0.0-dev"
prov_case "SDK 10 compiler rejected"            nonzero "5.9.0-1.26529.119+2ab2854" "10.0.15-servicing.26529.108+8e0f8ed"
prov_case "SDK 12 compiler rejected"            nonzero "6.0.0-1" "12.0.0-rtm"
prov_case "app PDB from another SDK rejected"   nonzero "5.12.0-1" "11.0.0-rtm" "10.0.15-servicing"
rm -f "$work/pd/App.pdb"; check_pdb_provenance "$work/pd" App > "$work/out.txt"; expect_rc "pdb provenance: missing PDB rejected" nonzero $?
printf 'a.tar.gz 5.12.0-1+x 11.0.0-rtm+x\nb.zip 5.12.0-1+x 11.0.0-rtm+x\n' > "$work/tc.txt"
check_single_toolchain "$work/tc.txt" > "$work/out.txt"; expect_rc "toolchain: one compiler for all archives accepted" 0 $?
printf 'c.zip 5.9.0-1+y 10.0.15-servicing+y\n' >> "$work/tc.txt"
check_single_toolchain "$work/tc.txt" > "$work/out.txt"; expect_rc "toolchain: mixed SDK 10/11 archives rejected" nonzero $?
printf 'a.zip 5.12.0-1+x 11.0.0-rtm+x\nb.zip 5.12.1-1+z 11.0.1-servicing+z\n' > "$work/tc.txt"
check_single_toolchain "$work/tc.txt" > "$work/out.txt"; expect_rc "toolchain: two SDK 11 patches rejected" nonzero $?
: > "$work/tc.txt"; check_single_toolchain "$work/tc.txt" > "$work/out.txt"; expect_rc "toolchain: no archives rejected" nonzero $?

# --- release.yml PDB-provenance steps, executed the way the runner executes them (bash -e) on fixtures, with a docker stub
# (no Docker, network or publication). A failed check must fail the step and leave an ::error:: annotation in the log.
workflow_step() { # step-name -> that step's `run: |` block from release.yml, dedented; non-zero if there is no such step
  awk -v name="$1" '
    function indent(s) { match(s, /^ */); return RLENGTH }
    !found && $0 ~ /^ *- name: / && substr($0, index($0, "- name: ") + 8) == name { found = 1; step = indent($0); next }
    found && !inrun { if ($0 ~ /^ *- name: / && indent($0) <= step) exit; if ($0 ~ /^ *run: [|]$/) { inrun = 1; key = indent($0) }; next }
    inrun { if ($0 ~ /^ *$/) { blank = blank "\n"; next }; if (indent($0) <= key) exit
            if (!body) body = indent($0); printf "%s%s\n", blank, substr($0, body + 1); blank = "" }
    END { exit !(found && inrun) }' "$here_root/.github/workflows/release.yml"
}
run_step() { # step-name [VAR=value...] -> runs the step in $work/ws, which has the policy at the path the step sources; output in $work/step.txt
  local name="$1"; shift
  workflow_step "$name" > "$work/step.sh" || { echo "release.yml has no step '$name'" > "$work/step.txt"; return 97; }
  (cd "$work/ws" && env "$@" bash -e "$work/step.sh") > "$work/step.txt" 2>&1
}
rm -rf "$work/ws" "$work/bin"; mkdir -p "$work/ws/.github/release-policy" "$work/bin" "$work/rt"; cp "$here/release-policy.sh" "$work/ws/.github/release-policy/"
cat > "$work/bin/docker" <<'STUB'
#!/usr/bin/env bash
# docker stub for the "Container runs on .NET 11" step: the image's environment, `--version` and /app files come from $STUB_IMAGE
case "$1 $2 $3 $4" in
  "image inspect campertron-smoke:local --format") printf 'PATH=/usr/bin\nDOTNET_VERSION=11.0.0\n\n' ;;
  "run --rm campertron-smoke:local --version") printf 'CampertronConsole 0.0.0-test+abc\nFramework => .NET 11.0.0 (X64)\n' ;;
  "run --rm --entrypoint cat") cat "$STUB_IMAGE/$(basename "$6")" ;;
  *) echo "docker stub: unexpected call: docker $*" >&2; exit 99 ;;
esac
STUB
chmod +x "$work/bin/docker"
good_rt="11.0.0-rtm.26529.118+297acf9"; sdk10_rt="10.0.15-servicing.26529.108+8e0f8ed"; cv="5.12.0-1.26529.118+297acf9"
img_fixture() { # library-PDB runtime-version
  rm -rf "$work/img"; mkdir -p "$work/img"
  printf '{"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"}}}\n' > "$work/img/CampertronConsole.runtimeconfig.json"
  pdb_fixture "$work/img/CampertronLibrary.pdb" "$cv" "$1"; pdb_fixture "$work/img/CampertronConsole.pdb" "$cv" "$good_rt"; }
docker_step() { run_step "Container runs on .NET 11" PATH="$work/bin:$PATH" RUNNER_TEMP="$work/rt" STUB_IMAGE="$work/img"; }
img_fixture "$good_rt"; docker_step; expect_rc "workflow docker step: SDK 11 image PDBs accepted" 0 $?
expect_eq "workflow docker step: logs the image compiler" "Image compiler: $cv $good_rt" "$(grep '^Image compiler: ' "$work/step.txt")"
img_fixture "$sdk10_rt"; docker_step; expect_rc "workflow docker step: SDK 10 image PDB fails the step" nonzero $?
grep -q '^::error::CampertronLibrary.pdb: ' "$work/step.txt"; expect_rc "workflow docker step: failure is reported as an ::error:: annotation" 0 $?
pkg_fixture() { # library-PDB runtime-version -> $work/ws/publish/CampertronConsole, a minimal linux-x64 publish output
  rm -rf "$work/ws/publish"; local o="$work/ws/publish/CampertronConsole"; mkdir -p "$o"
  printf '{"runtimeOptions":{"tfm":"net11.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"11.0.0"}]}}\n' > "$o/CampertronConsole.runtimeconfig.json"
  printf '{"runtimeTarget":{"name":".NETCoreApp,Version=v11.0/linux-x64"},"libraries":{"CampertronConsole/1.0.0":{},"runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/11.0.0":{}}}\n' > "$o/CampertronConsole.deps.json"
  { printf 'MZ\0\0'; printf '11.0.0-rtm.26529.118+%s' "$commit" | iconv -f UTF-8 -t UTF-16LE; printf '\0\0'; } > "$o/System.Private.CoreLib.dll"
  pdb_fixture "$o/CampertronLibrary.pdb" "$cv" "$1"; pdb_fixture "$o/CampertronConsole.pdb" "$cv" "$good_rt"; }
package_step() { run_step "Check .NET 11 runtime and compiler provenance" PROJECT=CampertronConsole RID=linux-x64 APP=console; }
pkg_fixture "$good_rt"; package_step; expect_rc "workflow package step: SDK 11 PDBs accepted" 0 $?
expect_eq "workflow package step: logs the runtime and compiler" "linux-x64 console: .NET 11 runtime 11.0.0, compiler $cv $good_rt" "$(cat "$work/step.txt")"
pkg_fixture "$sdk10_rt"; package_step; expect_rc "workflow package step: SDK 10 PDB fails the step" nonzero $?
grep -q '^::error::CampertronLibrary.pdb: ' "$work/step.txt"; expect_rc "workflow package step: failure is reported as an ::error:: annotation" 0 $?

# --- container image (decision D32): OCI checks on synthetic two-platform OCI layouts (no Docker, registry or network)
oci_ver="0.1.0-beta.10"; oci_rev="$commit"; oci_src="https://github.com/example-owner/Campertron"
declare -A FX
fxv() { if [ -n "${FX[$1]+set}" ]; then echo "${FX[$1]}"; else echo "$2"; fi; }
fx_blob() { # LAYOUT FILE MEDIATYPE -> descriptor JSON; the file is stored as LAYOUT/blobs/sha256/<digest>
  local h s; h="$(sha256sum < "$2" | cut -d' ' -f1)"; s="$(wc -c < "$2" | tr -d ' ')"
  mkdir -p "$1/blobs/sha256"; cp "$2" "$1/blobs/sha256/$h"
  jq -cn --arg m "$3" --arg d "sha256:$h" --argjson s "$s" '{mediaType: $m, digest: $d, size: $s}'; }
fx_layer() { # LAYOUT SRC_DIR -> layer descriptor JSON plus "diff" (its diff_id)
  local t="$work/fx-layer.tar" diff
  (cd "$2" && tar --sort=name --owner=0 --group=0 --numeric-owner --mtime=@0 -cf "$t" $(ls -A))
  diff="sha256:$(sha256sum < "$t" | cut -d' ' -f1)"; gzip -n -c "$t" > "$t.gz"
  fx_blob "$1" "$t.gz" "$OCI_LAYER_TYPE" | jq -c --arg d "$diff" '. + {diff: $d}'; }
fx_elf() { local m; case "$1" in x86-64) m='\076\000' ;; aarch64) m='\267\000' ;; *) m='\050\000' ;; esac
  printf "\177ELF\002\001\001\000\000\000\000\000\000\000\000\000\003\000${m}\001\000\000\000"; }
fx_image() { # LAYOUT ARCH -> descriptor (with platform) of one image: base layer (Ubuntu), app layer, optional upper layer
  local lay="$1" arch="$2" b="$work/fx-src/$2" a elf d layers="[]" diffs="[]" cfg
  rm -rf "$b"; mkdir -p "$b/base/usr/lib" "$b/base/etc" "$b/app/app" "$b/upper/app"; a="$b/app/app"
  printf 'PRETTY_NAME="Ubuntu"\nID=ubuntu\nVERSION_ID="%s"\n' "$(fxv ubuntu 26.04)" > "$b/base/usr/lib/os-release"
  ln -s ../usr/lib/os-release "$b/base/etc/os-release"
  printf 'console %s' "$arch" > "$a/CampertronConsole.dll"; printf '{"runtimeTarget":{}}' > "$a/CampertronConsole.deps.json"
  printf '{"runtimeOptions":{"tfm":"%s","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"}}}\n' "$(fxv "tfm_$arch" net11.0)" > "$a/CampertronConsole.runtimeconfig.json"
  printf 'library with the embedded database %s' "$(fxv "lib_$arch" same)" > "$a/CampertronLibrary.dll"
  pdb_fixture "$a/CampertronLibrary.pdb" "$(fxv "cv_$arch" "$cv")" "$(fxv "rt_$arch" "$good_rt")"
  pdb_fixture "$a/CampertronConsole.pdb" "$(fxv "cv_$arch" "$cv")" "$(fxv "rt_$arch" "$good_rt")"
  if [ "$arch" = amd64 ]; then elf=x86-64; else elf=aarch64; fi
  fx_elf "$(fxv "elf_$arch" "$elf")" > "$a/libe_sqlite3.so"
  case "$(fxv "upper_$arch" "")" in
    pdb) cp "$a/CampertronLibrary.pdb" "$b/upper/app/"; pdb_fixture "$a/CampertronLibrary.pdb" "$cv" "$sdk10_rt" ;;  # lower SDK 10, upper SDK 11
    whiteout) : > "$b/upper/app/.wh.libe_sqlite3.so" ;;
  esac
  for d in base app $( [ -n "$(ls -A "$b/upper/app")" ] && echo upper ); do
    desc="$(fx_layer "$lay" "$b/$d")"
    layers="$(jq -c --argjson x "$desc" '. + [$x | del(.diff)]' <<< "$layers")"; diffs="$(jq -c --arg x "$(jq -r .diff <<< "$desc")" '. + [$x]' <<< "$diffs")"
  done
  jq -n --arg arch "$(fxv "cfgarch_$arch" "$arch")" --arg user "$(fxv user app)" --arg dn "$(fxv "dotnet_$arch" 11.0.0)" \
    --arg v "$(fxv label_version "$oci_ver")" --arg r "$(fxv label_revision "$oci_rev")" --arg s "$(fxv label_source "$oci_src")" --argjson diffs "$diffs" \
    '{architecture: $arch, os: "linux", config: {User: $user, Env: ["PATH=/usr/bin", ("DOTNET_VERSION=" + $dn)], Entrypoint: ["dotnet", "CampertronConsole.dll"],
      Labels: {"org.opencontainers.image.version": $v, "org.opencontainers.image.revision": $r, "org.opencontainers.image.source": $s}},
      rootfs: {type: "layers", diff_ids: $diffs}}' > "$b/config.json"
  cfg="$(fx_blob "$lay" "$b/config.json" "$OCI_CONFIG_TYPE")"
  jq -n --arg mt "$OCI_MANIFEST_TYPE" --argjson c "$cfg" --argjson l "$layers" '{schemaVersion: 2, mediaType: $mt, config: $c, layers: $l}' > "$b/manifest.json"
  fx_blob "$lay" "$b/manifest.json" "$OCI_MANIFEST_TYPE" | jq -c --arg a "$arch" '. + {platform: {architecture: $a, os: "linux"}}'; }
fx_attestation() { # LAYOUT IMAGE_DIGEST -> a BuildKit-style attestation manifest descriptor for that image
  local b="$work/fx-src/att-${2:7:12}" c l; rm -rf "$b"; mkdir -p "$b"
  printf '{"_type":"https://in-toto.io/Statement/v0.1","predicateType":"https://slsa.dev/provenance/v1","subject":"%s"}' "$2" > "$b/stmt.json"
  printf '{}' > "$b/config.json"   # BuildKit attestation manifests use the OCI empty config
  c="$(fx_blob "$1" "$b/config.json" application/vnd.oci.empty.v1+json)"
  l="$(fx_blob "$1" "$b/stmt.json" application/vnd.in-toto+json | jq -c '. + {annotations: {"in-toto.io/predicate-type": "https://slsa.dev/provenance/v1"}}')"
  jq -n --arg mt "$OCI_MANIFEST_TYPE" --argjson c "$c" --argjson l "$l" '{schemaVersion: 2, mediaType: $mt, config: $c, layers: [$l]}' > "$b/manifest.json"
  fx_blob "$1" "$b/manifest.json" "$OCI_MANIFEST_TYPE" | jq -c --arg d "$2" '. + {platform: {architecture: "unknown", os: "unknown"}, annotations: {"vnd.docker.reference.digest": $d, "vnd.docker.reference.type": "attestation-manifest"}}'; }
fx_archive() { # ARCHIVE [FX mutations as key=value...] -> a two-platform OCI archive like `buildx --output type=oci`
  local archive="$1" lay="$work/fx-layout" e="[]" d arch ix kv layer; shift; FX=(); for kv in "$@"; do FX["${kv%%=*}"]="${kv#*=}"; done
  rm -rf "$lay" "$work/fx-src"; mkdir -p "$lay/blobs/sha256"; printf '{"imageLayoutVersion":"1.0.0"}' > "$lay/oci-layout"
  for arch in $(fxv archs "amd64 arm64"); do e="$(jq -c --argjson x "$(fx_image "$lay" "$arch")" '. + [$x]' <<< "$e")"; done
  [ -z "$(fxv extra "")" ] || e="$(jq -c --argjson x "$(fx_image "$lay" amd64 | jq -c '.platform = {architecture: "arm", os: "linux", variant: "v7"}')" '. + [$x]' <<< "$e")"
  for d in $(jq -r '.[] | select(.platform.os == "linux") | .digest' <<< "$e"); do e="$(jq -c --argjson x "$(fx_attestation "$lay" "$d")" '. + [$x]' <<< "$e")"; done
  [ -z "$(fxv dangling "")" ] || e="$(jq -c --argjson x "$(fx_attestation "$lay" "sha256:$(printf '0%.0s' {1..64})")" '. + [$x]' <<< "$e")"
  jq -n --arg mt "$OCI_INDEX_TYPE" --argjson m "$e" '{schemaVersion: 2, mediaType: $mt, manifests: $m}' > "$work/fx-index.json"
  ix="$(fx_blob "$lay" "$work/fx-index.json" "$OCI_INDEX_TYPE")"
  jq -n --arg mt "$OCI_INDEX_TYPE" --argjson ix "$ix" '{schemaVersion: 2, mediaType: $mt, manifests: [$ix]}' > "$lay/index.json"
  layer="$(jq -r '.manifests[0].digest' "$work/fx-index.json")"; layer="$(jq -r '.layers[-1].digest' "$lay/blobs/sha256/${layer#sha256:}")"
  case "$(fxv post "")" in
    rm-layer) rm -f "$lay/blobs/sha256/${layer#sha256:}" ;;
    corrupt-layer) printf 'x' >> "$lay/blobs/sha256/${layer#sha256:}" ;;
  esac
  rm -f "$archive"; tar -C "$lay" -cf "$archive" oci-layout index.json blobs
  [ "$(fxv post "")" != unsafe-member ] || (cd "$lay/blobs" && tar -P --append -f "$archive" ../oci-layout)
}
oci_case() { # description expected(0|nonzero) error-pattern [FX mutations...]
  local desc="$1" want="$2" pattern="$3" rc; shift 3
  fx_archive "$work/fx.tar" "$@"; rm -rf "$work/fx-out"
  check_oci_archive "$work/fx.tar" "$work/fx-out" "$oci_ver" "$oci_rev" "$oci_src" > "$work/oci.txt"; rc=$?
  if [ "$want" = 0 ]; then expect_rc "oci image: $desc" 0 "$rc"
  else { [ "$rc" -ne 0 ] && grep -qE "^::error::Container image.*$pattern" "$work/oci.txt"; }; expect_rc "oci image: $desc" 0 $?; fi
}
oci_case "valid amd64 + arm64 image accepted"                  0       ""
ixd="$(jq -r '.manifests[0].digest' "$work/fx-layout/index.json")"; ixf="$work/fx-layout/blobs/sha256/${ixd#sha256:}"
amd_m="$(jq -r '.manifests[] | select(.platform.architecture == "amd64") | .digest' "$ixf")"; arm_m="$(jq -r '.manifests[] | select(.platform.architecture == "arm64") | .digest' "$ixf")"
amd_c="$(jq -r .config.digest "$work/fx-layout/blobs/sha256/${amd_m#sha256:}")"; arm_c="$(jq -r .config.digest "$work/fx-layout/blobs/sha256/${arm_m#sha256:}")"
expect_eq "oci image: output names the index, both platform manifests and configs and the compilers" \
  "$(printf 'image_digest=%s\namd64_manifest=%s\namd64_config=%s\namd64_compiler=%s %s\narm64_manifest=%s\narm64_config=%s\narm64_compiler=%s %s\ndotnet_version=11.0.0\nattestation_manifests=2\narchive_sha256=%s' \
     "$ixd" "$amd_m" "$amd_c" "$cv" "$good_rt" "$arm_m" "$arm_c" "$cv" "$good_rt" "$(sha256sum < "$work/fx.tar" | cut -d' ' -f1)")" "$(cat "$work/oci.txt")"
check_loaded_image "$amd_m" "$(jq -c .rootfs.diff_ids "$work/fx-layout/blobs/sha256/${amd_c#sha256:}")" "$work/fx-out" "$amd_m" "$amd_c" > "$work/out.txt"
expect_rc "loaded image: ID = validated manifest digest (containerd store) accepted" 0 $?
check_loaded_image "$amd_c" "$(jq -c .rootfs.diff_ids "$work/fx-layout/blobs/sha256/${amd_c#sha256:}")" "$work/fx-out" "$amd_m" "$amd_c" > "$work/out.txt"
expect_rc "loaded image: ID = validated config digest (classic store) accepted" 0 $?
rm -rf "$work/fx-copy"; cp -r "$work/fx-out" "$work/fx-copy"
jq --arg d "$ixd" '.manifests[0].annotations = {"org.opencontainers.image.ref.name": "rehearsal"}' "$work/fx-out/index.json" > "$work/fx-copy/index.json"
check_oci_copy "$work/fx-copy" rehearsal "$ixd" > "$work/out.txt"; expect_rc "rehearsal copy: identical index and blobs accepted" 0 $?
att_layer="$(jq -r '.manifests[] | select(.platform.os == "unknown") | .digest' "$ixf" | head -n 1)"; att_layer="$(jq -r '.layers[0].digest' "$work/fx-copy/blobs/sha256/${att_layer#sha256:}")"
rm -f "$work/fx-copy/blobs/sha256/${att_layer#sha256:}"
check_oci_copy "$work/fx-copy" rehearsal "$ixd" > "$work/out.txt"; { [ $? -ne 0 ] && grep -q "^::error::Container image: blob $att_layer is missing" "$work/out.txt"; }
expect_rc "rehearsal copy: a dropped attestation blob rejected" 0 $?
oci_case "SDK 10 PDB on amd64 rejected"                         nonzero "linux/amd64: CampertronLibrary.pdb" rt_amd64="$sdk10_rt"
oci_case "SDK 10 PDB on arm64 rejected"                         nonzero "linux/arm64: CampertronLibrary.pdb" rt_arm64="$sdk10_rt"
oci_case "platforms from different SDK 11 compilers rejected"  nonzero "different compilers" cv_arm64="5.12.1-1.26600.1+abc" rt_arm64="11.0.1-servicing.26600.1+abc"
oci_case "missing amd64 image rejected"                        nonzero "exactly one linux/amd64" archs=arm64
oci_case "missing arm64 image rejected"                        nonzero "exactly one linux/arm64" archs=amd64
oci_case "extra runnable platform rejected"                    nonzero "unexpected entry: linux/arm" extra=1
oci_case "attestation for an unknown image rejected"           nonzero "unexpected entry: unknown/unknown" dangling=1
oci_case "config architecture mismatch rejected"               nonzero "linux/arm64: the config is for linux/amd64" cfgarch_arm64=amd64
oci_case "DOTNET_VERSION 10 rejected"                          nonzero "DOTNET_VERSION is '10.0.15'" dotnet_amd64=10.0.15 dotnet_arm64=10.0.15
oci_case "different DOTNET_VERSION per platform rejected"      nonzero "DOTNET_VERSION differs" dotnet_arm64=11.0.1
oci_case "net10.0 runtimeconfig rejected"                      nonzero "runtimeconfig|expected tfm net11.0" tfm_arm64=net10.0
oci_case "version label mismatch rejected"                     nonzero "image.version is '0.1.0-beta.9'" label_version=0.1.0-beta.9
oci_case "revision label mismatch rejected"                    nonzero "image.revision" label_revision=0000000000000000000000000000000000000000
oci_case "source label mismatch rejected"                      nonzero "image.source" label_source=https://github.com/someone/else
oci_case "root user rejected"                                  nonzero "user is 'root'" user=root
oci_case "Ubuntu other than 26.04 rejected"                    nonzero "expected Ubuntu 26.04" ubuntu=24.04
oci_case "amd64 native SQLite of another architecture rejected" nonzero "linux/amd64: /app/libe_sqlite3.so is aarch64" elf_amd64=aarch64
oci_case "arm64 native SQLite of another architecture rejected" nonzero "linux/arm64: /app/libe_sqlite3.so is x86-64" elf_arm64=x86-64
oci_case "CampertronLibrary.dll differing per platform rejected" nonzero "CampertronLibrary.dll differs" lib_arm64=other
oci_case "upper layer replaces a lower file (SDK 10 PDB below, SDK 11 above) accepted" 0 "" upper_arm64=pdb
oci_case "upper-layer whiteout removes a required file"        nonzero "linux/arm64: /app/libe_sqlite3.so is missing" upper_arm64=whiteout
oci_case "missing blob rejected"                               nonzero "is missing" post=rm-layer
oci_case "blob that does not match its digest rejected"        nonzero "does not have the size|does not match its digest" post=corrupt-layer
oci_case "archive member outside the OCI layout rejected"      nonzero "unexpected member '../oci-layout'" post=unsafe-member
expect_eq "image refs: pre-release gets only the exact tag" "$image:0.1.0-beta.10" "$(docker_tag_refs "$image" 0.1.0-beta.10 true)"
expect_eq "image refs: stable gets exact, MAJOR.MINOR and latest" "$(printf '%s\n' "$image:1.2.3" "$image:1.2" "$image:latest")" "$(docker_tag_refs "$image" 1.2.3 false)"

# --- release.yml container steps, executed like the runner (bash -e) with docker and skopeo stubs (no Docker or registry)
mkdir -p "$work/bin-load" "$work/bin-skopeo" "$work/home/.docker"
cat > "$work/bin-load/docker" <<'STUB'
#!/usr/bin/env bash
# docker stub for "Check the loaded image is the validated amd64 image": the loaded image's ID and layers
case "$*" in
  "image inspect campertron-smoke:local --format {{.Id}}") echo "$STUB_ID" ;;
  "image inspect campertron-smoke:local --format {{json .RootFS.Layers}}") echo "$STUB_LAYERS" ;;
  *) echo "docker stub: unexpected call: docker $*" >&2; exit 99 ;;
esac
STUB
cat > "$work/bin-skopeo/skopeo" <<'STUB'
#!/usr/bin/env bash
# skopeo stub: a registry kept in $STUB_REG (one file per image reference with its raw index); every call is logged
echo "skopeo $*" >> "$STUB_LOG"
ref_file() { echo "$STUB_REG/$(printf '%s' "${1#docker://}" | tr '/:' '__')"; }
case "$1" in
  inspect) f="$(ref_file "${@: -1}")"; if [ -f "$f" ]; then cat "$f"; else echo "reading manifest: manifest unknown" >&2; exit 1; fi ;;
  copy) a="${@: -2:1}"; a="${a#oci-archive:}"; ix="$(tar -xOf "$a" index.json | jq -r '.manifests[0].digest')"
        tar -xOf "$a" "blobs/sha256/${ix#sha256:}" > "$(ref_file "${@: -1}")"
        [ -z "${STUB_TAMPER:-}" ] || printf ' ' >> "$(ref_file "${@: -1}")" ;;
  *) echo "skopeo stub: unexpected call: skopeo $*" >&2; exit 99 ;;
esac
STUB
chmod +x "$work/bin-load/docker" "$work/bin-skopeo/skopeo"
printf '{"auths":{"ghcr.io":{"auth":"%s"}}}\n' "$(printf 'user:token' | base64)" > "$work/home/.docker/config.json"
check_image_step() { # archive -> runs "Check both platform images" with a fresh RUNNER_TEMP ($work/rt-img) holding that archive
  rm -rf "$work/rt-img"; mkdir -p "$work/rt-img"; cp "$1" "$work/rt-img/container-image"; : > "$work/rt-img/out"; : > "$work/rt-img/summary"
  run_step "Check both platform images" RUNNER_TEMP="$work/rt-img" VERSION="$oci_ver" GITHUB_SHA="$oci_rev" GITHUB_REPOSITORY=example-owner/Campertron \
    GITHUB_OUTPUT="$work/rt-img/out" GITHUB_STEP_SUMMARY="$work/rt-img/summary"; }
fx_archive "$work/fx-good.tar"; good_ix="$(jq -r '.manifests[0].digest' "$work/fx-layout/index.json")"
check_image_step "$work/fx-good.tar"; rc=$?
{ [ "$rc" -eq 0 ] && grep -qx "image_digest=$good_ix" "$work/rt-img/out" && grep -qx "archive_sha256=$(sha256sum < "$work/fx-good.tar" | cut -d' ' -f1)" "$work/rt-img/out"; }
expect_rc "workflow docker job: both-platform image check passes and records the index digest" 0 $?
amd_m="$(sed -n 's/^amd64_manifest=//p' "$work/rt-img/out")"; amd_c="$(sed -n 's/^amd64_config=//p' "$work/rt-img/out")"
run_step "Check the loaded image is the validated amd64 image" PATH="$work/bin-load:$PATH" RUNNER_TEMP="$work/rt-img" AMD64_MANIFEST="$amd_m" AMD64_CONFIG="$amd_c" \
  STUB_ID="sha256:$(printf '1%.0s' {1..64})" STUB_LAYERS='["sha256:0"]'
{ [ $? -ne 0 ] && grep -q '^::error::Container image: the loaded amd64 image .* is neither the validated manifest' "$work/step.txt"; }
expect_rc "workflow docker job: a loaded image that is not the validated amd64 image fails" 0 $?
fx_archive "$work/fx-bad.tar" rt_arm64="$sdk10_rt"; check_image_step "$work/fx-bad.tar"
{ [ $? -ne 0 ] && grep -q '^::error::Container image linux/arm64: CampertronLibrary.pdb: compiler' "$work/step.txt"; }
expect_rc "workflow docker job: arm64 SDK 10 PDB fails the step with an ::error:: line" 0 $?
publish_steps() { # [VAR=value...] -> runs docker-publish's tag, pre-registry check and push steps against the stub registry
  rm -rf "$work/rt-pub" "$work/ws/image"; mkdir -p "$work/rt-pub" "$work/ws/image" "$work/reg"; cp "$work/fx-good.tar" "$work/ws/image/container-image"
  : > "$work/rt-pub/env"; : > "$work/rt-pub/out"; : > "$work/skopeo.log"
  local common=(PATH="$work/bin-skopeo:$PATH" HOME="$work/home" RUNNER_TEMP="$work/rt-pub" GITHUB_ENV="$work/rt-pub/env" GITHUB_OUTPUT="$work/rt-pub/out"
    VERSION="$oci_ver" PRERELEASE=true GITHUB_SHA="$oci_rev" GITHUB_REPOSITORY=example-owner/Campertron GITHUB_REPOSITORY_OWNER=Example-Owner
    STUB_REG="$work/reg" STUB_LOG="$work/skopeo.log" IMAGE_DIGEST="$good_ix" AMD64_MANIFEST="$amd_m" ARM64_MANIFEST="$(sed -n 's/^arm64_manifest=//p' "$work/rt-img/out")"
    IMAGE_SHA256="$(sha256sum < "$work/fx-good.tar" | cut -d' ' -f1)" "$@")
  run_step "Determine image name and tags" "${common[@]}" || return 1
  run_step "Check the validated image before registry contact" "${common[@]}" || return 1
  run_step "Push the validated image" "${common[@]}"; }
fx_archive "$work/fx-good.tar" > /dev/null; check_image_step "$work/fx-good.tar" > /dev/null; good_ix="$(sed -n 's/^image_digest=//p' "$work/rt-img/out")"; amd_m="$(sed -n 's/^amd64_manifest=//p' "$work/rt-img/out")"
rm -rf "$work/reg"; publish_steps; rc=$?
{ [ "$rc" -eq 0 ] && [ "$(grep -c '^skopeo copy ' "$work/skopeo.log")" = 1 ] \
  && grep -qx "skopeo copy --all --preserve-digests --authfile $work/home/.docker/config.json oci-archive:image/container-image docker://ghcr.io/example-owner/campertronconsole:0.1.0-beta.10" "$work/skopeo.log" \
  && [ "$(ls "$work/reg")" = "ghcr.io_example-owner_campertronconsole_0.1.0-beta.10" ] && grep -qx "digest=$good_ix" "$work/rt-pub/out"; }
expect_rc "workflow docker-publish: pre-release copies the validated archive with --all --preserve-digests to the exact tag only" 0 $?
rm -rf "$work/reg"; publish_steps IMAGE_SHA256="$(printf 'f%.0s' {1..64})"
{ [ $? -ne 0 ] && grep -q '^::error::Downloaded image archive has SHA-256' "$work/step.txt" && [ ! -s "$work/skopeo.log" ]; } && \
{ rm -rf "$work/reg"; publish_steps IMAGE_DIGEST="sha256:$(printf 'e%.0s' {1..64})"; [ $? -ne 0 ] && grep -q "^::error::Downloaded image does not match the docker job's validated image_digest" "$work/step.txt" && [ ! -s "$work/skopeo.log" ]; }
expect_rc "workflow docker-publish: archive file or OCI digest mismatch fails before any registry call" 0 $?
rm -rf "$work/reg"; mkdir -p "$work/reg"; printf '{"schemaVersion":2}' > "$work/reg/ghcr.io_example-owner_campertronconsole_0.1.0-beta.10"; publish_steps
{ [ $? -ne 0 ] && grep -q '^::error::Container image: ghcr.io/example-owner/campertronconsole:0.1.0-beta.10 already exists with .*refusing to move it' "$work/step.txt" && ! grep -q '^skopeo copy' "$work/skopeo.log"; }
expect_rc "workflow docker-publish: an existing tag with another digest is refused, not moved" 0 $?
rm -rf "$work/reg"; publish_steps STUB_TAMPER=1
{ [ $? -ne 0 ] && grep -q '^::error::Container image: ghcr.io/example-owner/campertronconsole:0.1.0-beta.10 is .* after copying, expected the validated' "$work/step.txt"; }
expect_rc "workflow docker-publish: a registry digest that differs after the copy fails" 0 $?

# --- release.yml job structure for the container image (static: job-level YAML cannot be executed here)
workflow_job() { awk -v id="$1" '$0 == "  " id ":" {f = 1; print; next} f && /^  [A-Za-z0-9_-]+:$/ {exit} f {print}' "$here_root/.github/workflows/release.yml"; }
pub_job="$(workflow_job docker-publish)"; docker_job="$(workflow_job docker)"
publish_if="if: needs.verify.outputs.publish == 'true' && github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v')"
{ [ -n "$pub_job" ] && ! grep -qE 'buildx (build|create|bake)|docker (build|push)|--push' <<< "$pub_job"; }
expect_rc "workflow structure: docker-publish builds no image (no rebuild)" 0 $?
{ grep -qxF "    needs: [verify, binaries, smoke, docker, assemble]" <<< "$pub_job" && grep -qxF "    $publish_if" <<< "$pub_job"; }
expect_rc "workflow structure: docker-publish keeps its docker dependency and publish condition" 0 $?
grep -qxF '          subject-digest: ${{ needs.docker.outputs.image_digest }}' <<< "$pub_job"
expect_rc "workflow structure: the image attestation is for the validated image digest" 0 $?
{ [ -n "$docker_job" ] && ! grep -qE '^    permissions:|: write' <<< "$docker_job"; }
expect_rc "workflow structure: the docker job has no write permission" 0 $?
{ grep -qF -- '--output type=oci,dest="$RUNNER_TEMP/container-image"' <<< "$docker_job" && grep -qF 'check_oci_archive "$RUNNER_TEMP/container-image"' <<< "$docker_job" \
  && [ "$(grep -c 'docker buildx build --platform linux/amd64,linux/arm64' <<< "$docker_job")" = 1 ]; }
expect_rc "workflow structure: one multi-platform build is exported and both platform images are checked" 0 $?
! grep -qE 'docker login|skopeo login|--push|docker://|ghcr\.io' <<< "$docker_job"
expect_rc "workflow structure: the docker job (also run by dry runs) never contacts a registry" 0 $?
{ grep -qxF "    needs: [verify, binaries, smoke, docker, assemble]" <<< "$pub_job" && grep -qxF "    $publish_if" <<< "$(workflow_job release)" \
  && grep -qxF "    needs: [verify, binaries, smoke, docker, assemble, docker-publish]" <<< "$(workflow_job release)"; }
expect_rc "workflow structure: the image is published only after every validation job, before the GitHub release" 0 $?

# --- package policy (restored obj/project.assets.json)
pkg_case() { printf '{"libraries":{%s}}\n' "$2" > "$work/assets.json"; check_package_policy "$work/assets.json" > "$work/out.txt"; expect_rc "packages: $1" "$3" $?; }
pkg_case "EF Core 11 and stable packages accepted" '"Microsoft.EntityFrameworkCore/11.0.0":{"type":"package"},"YamlDotNet/16.3.0":{"type":"package"},"CampertronLibrary/1.0.0":{"type":"project"}' 0
pkg_case "EF Core 9 rejected"                   '"Microsoft.EntityFrameworkCore.Sqlite/9.0.19":{"type":"package"}'  nonzero
pkg_case "EF Core 10 rejected"                  '"Microsoft.EntityFrameworkCore/10.0.15":{"type":"package"}'        nonzero
pkg_case "Microsoft.Extensions 9 rejected"      '"Microsoft.Extensions.FileProviders.Embedded/9.0.8":{"type":"package"}' nonzero
pkg_case "Microsoft.Data.Sqlite 9 rejected"     '"Microsoft.Data.Sqlite.Core/9.0.19":{"type":"package"}'            nonzero
pkg_case "prerelease package rejected"          '"SQLitePCLRaw.lib.e_sqlite3/3.53.4.1-pre1":{"type":"package"}'     nonzero
pkg_case "prerelease EF Core 12 rejected"       '"Microsoft.EntityFrameworkCore/12.0.0-preview.1":{"type":"package"}' nonzero
check_package_policy > "$work/out.txt" 2>&1 < /dev/null; expect_rc "packages: no asset files rejected (fails closed)" nonzero $?
# A non-zero exit counts only with the matching diagnostic (rc forced to 0 otherwise, which fails the test).
rm -f "$work/missing-assets.json"
check_package_policy "$work/missing-assets.json" > "$work/out.txt" 2>&1 < /dev/null; rc=$?
grep -q "does not exist" "$work/out.txt" || rc=0
expect_rc "packages: missing asset file rejected (fails closed)" nonzero "$rc"
printf '{"libraries": {' > "$work/invalid-assets.json"
check_package_policy "$work/invalid-assets.json" > "$work/out.txt" 2>&1 < /dev/null; rc=$?
grep -q "cannot read" "$work/out.txt" || rc=0
expect_rc "packages: invalid asset JSON rejected (fails closed)" nonzero "$rc"

printf '{"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"}}}\n' > "$work/fd.json"
check_framework_dependent_runtimeconfig "$work/fd.json" > "$work/out.txt"; expect_rc "image runtimeconfig on .NET 11 passes" 0 $?
sed -i 's/11.0.0/9.0.0/' "$work/fd.json"
check_framework_dependent_runtimeconfig "$work/fd.json" > "$work/out.txt"; expect_rc "image runtimeconfig on .NET 9 rejected" nonzero $?
printf '{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"}}}\n' > "$work/fd.json"
check_framework_dependent_runtimeconfig "$work/fd.json" > "$work/out.txt"; expect_rc "image runtimeconfig with tfm net9.0 rejected" nonzero $?

report_case() { # description expected-rc text
  check_framework_report "$3" > "$work/out.txt"; expect_rc "--version report: $1" "$2" $?
}
report_case ".NET 11 accepted"            0       $'CampertronConsole 0.1.0-beta.5+abc\nFramework => .NET 11.0.0 (X64)'
report_case ".NET 11 on Arm64 accepted"   0       $'CampertronGUI 0.1.0-beta.5+abc\nFramework => .NET 11.0.0 (Arm64)'
report_case ".NET 9.0.0 rejected"         nonzero $'CampertronConsole 1.0.0\nFramework => .NET 9.0.0 (X64)'
report_case ".NET 100 rejected"           nonzero $'CampertronConsole 1.0.0\nFramework => .NET 100.0.1 (X64)'
report_case "missing Framework line rejected" nonzero $'CampertronConsole 1.0.0'
report_case "two Framework lines rejected" nonzero $'Framework => .NET 11.0.0 (X64)\nFramework => .NET 9.0.0 (X64)'
report_case "menu output instead of --version rejected" nonzero $'-> Search\n   View Config'
check_framework_report $'X 1\nFramework => .NET 11.0.0 (X64)' 11.0.0 > "$work/out.txt"; expect_rc "--version report: matches the bundled runtime 11.0.0" 0 $?
check_framework_report $'X 1\nFramework => .NET 10.0.14 (X64)' 11.0.0 > "$work/out.txt"; expect_rc "--version report: installed runtime 10.0.14 instead of bundled 11.0.0 rejected" nonzero $?
check_framework_report $'X 1\nFramework => .NET 11.0.00 (X64)' 11.0.0 > "$work/out.txt"; expect_rc "--version report: 11.0.00 does not match 11.0.0" nonzero $?

# --- Curated release notes (R7, decision D30)
beta=0.1.0-beta.10; stable=1.2.3
expected_archives "$beta" > "$work/expected-$beta.txt"; expected_archives "$stable" > "$work/expected-$stable.txt"
frag_ok() { # version file -> a valid fragment for version, with ordinary prose that mentions betas and "latest"
  printf '%s\n' \
    "## What's new in $1" \
    "This release improves searches." \
    "" \
    "### Docker" \
    "- The container is unchanged." \
    "" \
    "## Upgrading from 0.1.0-beta.9" \
    "- Extract the new archive. This pre-release keeps your settings. Upgrade to this latest release when ready." \
    "- **Docker:** there is no \`latest\` tag for beta versions, so change the tag by hand; see the latest beta on the releases page." \
    "" \
    "## Known limitations" \
    "- Consecutive-night searches can still miss results (TD-04)." > "$2"
}
mkdir -p "$work/n"
frag_ok "$beta" "$work/n/v$beta.md"; frag_ok "$stable" "$work/n/v$stable.md"
validate_curated_fragment "$work/n/v$beta.md" "$beta" 2> "$work/err.txt"; expect_rc "curated: valid beta fragment (prose mentioning betas, latest and upgrades) passes" 0 $?
validate_curated_fragment "$work/n/v$stable.md" "$stable" 2> "$work/err.txt"; expect_rc "curated: valid stable fragment passes" 0 $?

frag_case() { # description setup-command (edits $work/f/v$beta.md, starting from a valid fragment)
  rm -rf "$work/f"; mkdir -p "$work/f"; frag_ok "$beta" "$work/f/v$beta.md"; eval "$2"
  validate_curated_fragment "$work/f/v$beta.md" "$beta" 2> "$work/err.txt"; local rc=$?
  expect_rc "curated: rejects $1" nonzero $rc
  if [ $rc -ne 0 ] && ! grep -q '^::error::Curated release notes ' "$work/err.txt"; then failed=$((failed+1)); echo "FAIL  curated: $1 gave no ::error:: message"; fi
}
F="\$work/f/v$beta.md"
frag_case "missing file"                       "rm '$work/f/v$beta.md'"
frag_case "empty file"                         ": > $F"
frag_case "invalid UTF-8"                      "printf 'caf\\xe9\\n' >> $F"
frag_case "NUL byte"                           "printf 'a\\0b\\n' >> $F"
frag_case "CRLF line endings"                  "sed -i 's/\$/\\r/' $F"
frag_case "a lone carriage return"             "sed -i '2s/\$/\\r/' $F"
frag_case "missing final newline"              "printf 'last line' >> $F"
frag_case "trailing blank line"                "echo >> $F"
frag_case "trailing whitespace-only line"      "printf '   \\n' >> $F"
frag_case "missing first heading"              "sed -i '1d' $F"
frag_case "leading blank line"                 "sed -i '1s/^/\\n/' $F"
frag_case "wrong heading text"                 "sed -i \"1s/.*/## What is new in $beta/\" $F"
frag_case "heading for another version"        "sed -i \"1s/.*/## What's new in 0.1.0-beta.9/\" $F"
frag_case "second What's new heading"          "echo \"## What's new in 0.1.0-beta.9\" >> $F"
frag_case "level-one heading"                  "echo '# Campertron $beta' >> $F"
frag_case "setext level-one heading"           "printf 'Title\\n=====\\n' >> $F"
for h in "Downloads" "Compatibility with older versions" "Docker" "Verify your download"; do
  frag_case "generated heading '## $h'"        "echo '## $h' >> $F"
done
frag_case "generated heading in other case"       "echo '## DOWNLOADS' >> $F"
frag_case "pre-release status marker"          "echo '**Pre-release version.** spoofed' >> $F"
frag_case "stable status marker"              "echo '**Stable release.** spoofed' >> $F"
frag_case "prerelease spoof: GitHub prerelease" "echo 'This version is published as a GitHub prerelease.' >> $F"
frag_case "prerelease spoof: released as a pre-release" "echo 'It is released as a pre-release.' >> $F"
frag_case "stable spoof: published as a stable release" "echo 'This build is published as a stable release.' >> $F"
frag_case "Latest spoof: marked Latest"        "echo 'This release is marked Latest.' >> $F"
frag_case "Latest spoof: not marked as the latest" "echo 'It is not marked as the latest release.' >> $F"
frag_case "Latest spoof: is not the latest release" "echo 'This is not the latest release.' >> $F"
frag_case "generated table row"                "echo '| \`linux-x64\` | a | b | c |' >> $F"
frag_case "expected archive file name"         "echo 'Get campertron-gui-$beta-win-x64.zip.' >> $F"
frag_case "symlink instead of a file"          "mv $F '$work/f/real.md' && ln -s real.md $F"
validate_curated_fragment "$work/n/v$beta.md" "$stable" 2> "$work/err.txt"; expect_rc "curated: rejects file name/heading version mismatch" nonzero $?
validate_curated_fragment "$work/n/v$beta.md" "v$beta" 2> "$work/err.txt"; expect_rc "curated: rejects a version with a leading v" nonzero $?
rm -rf "$work/f"; mkdir -p "$work/f"; frag_ok "$beta" "$work/f/v$beta.md"; echo 'Earlier archives were named campertron-gui-0.1.0-beta.9-win-x64.zip.' >> "$work/f/v$beta.md"
validate_curated_fragment "$work/f/v$beta.md" "$beta" 2> "$work/err.txt"; expect_rc "curated: archive names of other versions are allowed" 0 $?
for prose in "Upgrading from 0.1.0-beta.9 keeps your data." "Download the latest beta." "Upgrade to this latest release." "This pre-release fixes searches." "Released as a beta, it is not yet stable."; do
  frag_ok "$beta" "$work/f/v$beta.md"; echo "$prose" >> "$work/f/v$beta.md"
  validate_curated_fragment "$work/f/v$beta.md" "$beta" 2> "$work/err.txt"; expect_rc "curated: permits ordinary prose '$prose'" 0 $?
done

# composition
compose_notes "$beta" true "$image" "$repository" "$work/n/v$beta.md" > "$work/composed-beta.md" 2> "$work/err.txt"; expect_rc "compose: beta fragment composes" 0 $?
compose_notes "$stable" false "$image" "$repository" "$work/n/v$stable.md" > "$work/composed-stable.md" 2> "$work/err.txt"; expect_rc "compose: stable fragment composes" 0 $?
render_notes "$beta" true "$image" "$repository" > "$work/tmpl-beta.md"
lines="$(wc -l < "$work/n/v$beta.md")"
expect_eq "compose: lines 1-4 are the generated title and status" "$(head -n 4 "$work/tmpl-beta.md")" "$(head -n 4 "$work/composed-beta.md")"
cmp -s <(sed -n "5,$((4 + lines))p" "$work/composed-beta.md") "$work/n/v$beta.md"; expect_rc "compose: fragment bytes are preserved unchanged" 0 $?
expect_eq "compose: exactly one blank line between curated and generated sections" "|## Downloads" "$(sed -n "$((5 + lines))p" "$work/composed-beta.md")|$(sed -n "$((6 + lines))p" "$work/composed-beta.md")"
cmp -s <(tail -n +$((5 + lines + 1)) "$work/composed-beta.md") <(tail -n +5 "$work/tmpl-beta.md"); expect_rc "compose: generated sections from line 5 follow unchanged" 0 $?
cmp -s "$work/composed-beta.md" <(head -n 4 "$work/tmpl-beta.md"; cat "$work/n/v$beta.md"; echo; tail -n +5 "$work/tmpl-beta.md"); expect_rc "compose: exact composition boundary (whole body)" 0 $?
validate_notes "$work/composed-beta.md" "$beta" true "$image" "$work/expected-$beta.txt" > "$work/out.txt"; expect_rc "compose: composed beta body passes validate_notes" 0 $?
validate_notes "$work/composed-stable.md" "$stable" false "$image" "$work/expected-$stable.txt" > "$work/out.txt"; expect_rc "compose: composed stable body passes validate_notes" 0 $?
grep -qF '**Pre-release version.**' "$work/composed-beta.md"; expect_rc "compose: beta body has the generated pre-release status" 0 $?
grep -qF "The container image is tagged \`$stable\`, \`1.2\` and \`latest\`." "$work/composed-stable.md"; expect_rc "compose: stable body has the generated alias sentence" 0 $?
sed 's/`1.2` and `latest`/`1.2`/' "$work/composed-stable.md" > "$work/t.md"
validate_notes "$work/t.md" "$stable" false "$image" "$work/expected-$stable.txt" > "$work/out.txt"; expect_rc "compose: stable body without the alias sentence is rejected" nonzero $?
compose_notes "$beta" false "$image" "$repository" "$work/n/v$beta.md" > "$work/t.md" 2> "$work/err.txt"; expect_rc "compose: rejects prerelease flag that contradicts the version" nonzero $?
cp "$work/n/v$beta.md" "$work/f/v$beta.md"; yes "$(printf '%0100d' 0)" | head -n 1240 >> "$work/f/v$beta.md"
validate_curated_fragment "$work/f/v$beta.md" "$beta" 2> "$work/err.txt"; expect_rc "compose: large fragment itself is valid" 0 $?
compose_notes "$beta" true "$image" "$repository" "$work/f/v$beta.md" > "$work/t.md" 2> "$work/err.txt"; expect_rc "compose: rejects a body over $RELEASE_BODY_MAX_BYTES bytes" nonzero $?

# directory check
dir_case() { # description expected-rc setup-command (in $work/d, which starts with README.md only)
  rm -rf "$work/d"; mkdir -p "$work/d"; echo "# Release notes" > "$work/d/README.md"; eval "$3"
  check_curated_notes_dir "$work/d" "$image" "$repository" > "$work/preview.md" 2> "$work/err.txt"; expect_rc "directory: $1" "$2" $?
}
dir_case "README.md only passes (no fragment needed for an unprepared version)" 0 ':'
grep -qF "No curated release-note fragments are tracked" "$work/preview.md"; expect_rc "directory: README-only preview says no fragments" 0 $?
dir_case "valid beta and stable fragments pass"       0 "cp '$work/n/v$beta.md' '$work/n/v$stable.md' '$work/d/'"
expect_eq "directory: preview has one block per fragment" "2" "$(grep -c '^#### ' "$work/preview.md")"
grep -qF "\`v$stable.md\`: version \`$stable\` (stable release)" "$work/preview.md" && grep -qF "\`v$beta.md\`: version \`$beta\` (pre-release version)" "$work/preview.md"
expect_rc "directory: beta/stable derived from the file name" 0 $?
expect_eq "directory: preview shows the exact SHA-256 of the composed body" \
  "$(sha256sum < "$work/composed-beta.md" | cut -d' ' -f1)" "$(grep -A1 "^#### \`v$beta.md\`" "$work/preview.md" | sed -n 's/.*SHA-256: `\([0-9a-f]\{64\}\)`.*/\1/p')"
check_curated_notes_dir "$work/d" "$image" "$repository" > "$work/preview2.md" 2> /dev/null
cmp -s "$work/preview.md" "$work/preview2.md"; expect_rc "directory: preview is deterministic" 0 $?
dir_case "missing directory is rejected"              nonzero "rm -rf '$work/d'"
dir_case "stray file is rejected"                     nonzero "echo x > '$work/d/notes.txt'"
dir_case "file name without leading v is rejected"    nonzero "cp '$work/n/v$beta.md' '$work/d/$beta.md'"
dir_case "invalid SemVer file name is rejected"       nonzero "cp '$work/n/v$beta.md' '$work/d/v01.2.3.md'"
dir_case "two-part version file name is rejected"     nonzero "cp '$work/n/v$beta.md' '$work/d/v1.2.md'"
dir_case "upper-case .MD is rejected"                 nonzero "cp '$work/n/v$beta.md' '$work/d/v$beta.MD'"
dir_case "names differing only by case are rejected"  nonzero "cp '$work/n/v$beta.md' '$work/d/v$beta.md'; cp '$work/n/v$beta.md' '$work/d/V$beta.md'"
grep -q 'differ only by case' "$work/err.txt"; expect_rc "directory: case duplicate is reported as such" 0 $?
dir_case "subdirectory is rejected"                   nonzero "mkdir '$work/d/old'"
dir_case "invalid fragment is rejected"               nonzero "cp '$work/n/v$beta.md' '$work/d/'; echo '## Downloads' >> '$work/d/v$beta.md'"
grep -qF "$work/d/v$beta.md" "$work/err.txt"; expect_rc "directory: error names the failing file" 0 $?
dir_case "fragment whose heading names another version is rejected" nonzero "sed \"1s/.*/## What's new in $stable/\" '$work/n/v$beta.md' > '$work/d/v$beta.md'"
dir_case "one bad fragment fails even when another is valid" nonzero "cp '$work/n/v$stable.md' '$work/d/'; printf 'x' > '$work/d/v$beta.md'"
check_curated_notes_dir "$here/../release-notes" "$image" "$repository" > "$work/preview.md" 2> "$work/err.txt"
expect_rc "directory: the repository's .github/release-notes passes" 0 $?

# publishing mode versus dry runs
assemble_release_notes true "$beta" true "$image" "$repository" "$work/d-empty" > "$work/t.md" 2> "$work/err.txt"; expect_rc "publish: missing fragment fails (no template fallback)" nonzero $?
grep -qF "template-only notes are never published" "$work/err.txt"; expect_rc "publish: missing-fragment error says template-only notes are never published" 0 $?
rm -rf "$work/d"; mkdir -p "$work/d"; echo "# x" > "$work/d/README.md"
assemble_release_notes true "$beta" true "$image" "$repository" "$work/d" > "$work/t.md" 2> "$work/err.txt"; expect_rc "publish: README-only directory fails for the tagged version" nonzero $?
cp "$work/n/v$stable.md" "$work/d/"
assemble_release_notes true "$beta" true "$image" "$repository" "$work/d" > "$work/t.md" 2> "$work/err.txt"; expect_rc "publish: a fragment for another version does not satisfy the tag" nonzero $?
cp "$work/n/v$beta.md" "$work/d/"
assemble_release_notes true "$beta" true "$image" "$repository" "$work/d" > "$work/t.md" 2> "$work/err.txt"; expect_rc "publish: exact fragment composes" 0 $?
cmp -s "$work/t.md" "$work/composed-beta.md"; expect_rc "publish: body is the composed fragment, not the template" 0 $?
echo '## Downloads' >> "$work/d/v$beta.md"
assemble_release_notes true "$beta" true "$image" "$repository" "$work/d" > "$work/t.md" 2> "$work/err.txt"; expect_rc "publish: invalid exact fragment fails" nonzero $?
assemble_release_notes false 0.0.0-dryrun.7 true "$image" "$repository" "$work/d-empty" > "$work/t.md" 2> "$work/err.txt"; expect_rc "dry run: synthetic version uses the template" 0 $?
cmp -s "$work/t.md" <(render_notes 0.0.0-dryrun.7 true "$image" "$repository"); expect_rc "dry run: synthetic body is exactly the template" 0 $?
assemble_release_notes "" "$beta" true "$image" "$repository" "$work/d" > "$work/t.md" 2> "$work/err.txt"
cmp -s "$work/t.md" "$work/tmpl-beta.md"; expect_rc "dry run: anything but PUBLISH=true never reads a fragment" 0 $?

# release body read-back (gh release view --json body,isDraft,isPrerelease,name,tagName,assets)
view_json() { # body-file draft -> JSON like gh release view
  jq -n --rawfile b "$1" --argjson d "$2" --arg v "$beta" \
    '{body: $b, isDraft: $d, isPrerelease: false, name: ("Campertron " + $v), tagName: ("v" + $v), assets: [range(17) | {name: "a\(.)"}]}'
}
release_body_from_json <(view_json "$work/composed-beta.md" true) > "$work/rb.md"
cmp -s "$work/rb.md" "$work/composed-beta.md"; expect_rc "read-back: body bytes preserved exactly, including the final newline" 0 $?
printf 'caf\xc3\xa9 "q" \\ `x`\n\n' > "$work/u.md"
release_body_from_json <(jq -n --rawfile b "$work/u.md" '{body: $b}') > "$work/rb.md"
cmp -s "$work/rb.md" "$work/u.md"; expect_rc "read-back: UTF-8, quotes, backslashes and a trailing blank line preserved" 0 $?
release_body_from_json <(echo '{"body": null}') > /dev/null 2>&1; expect_rc "read-back: a null body is an error, not the text null" nonzero $?
jq -r .body <(view_json "$work/composed-beta.md" true) > "$work/jq.md"
compare_release_body "$work/composed-beta.md" "$work/jq.md" "pitfall" 2> "$work/err.txt"; expect_rc "read-back: a --jq style extra newline is detected" nonzero $?
grep -q 'SHA-256' "$work/err.txt"; expect_rc "read-back: mismatch error shows both SHA-256 values" 0 $?
sed '6s/searches\./searches!/' "$work/composed-beta.md" > "$work/x.md"
[ "$(wc -c < "$work/x.md")" -eq "$(wc -c < "$work/composed-beta.md")" ] && [ "$(cmp -l "$work/x.md" "$work/composed-beta.md" | wc -l)" -eq 1 ]
expect_rc "read-back: one-byte fixture differs by exactly one byte" 0 $?
compare_release_body "$work/composed-beta.md" "$work/x.md" "tagged reconstruction" 2> "$work/err.txt"; expect_rc "reconstruction: artifact differing from the tagged body by one byte is rejected" nonzero $?
compare_release_body "$work/composed-beta.md" "$work/composed-beta.md" "same" 2> "$work/err.txt"; expect_rc "reconstruction: identical bodies are accepted" 0 $?
view_case() { # description expected-rc body-file is-draft expect-draft [jq-filter]
  view_json "$3" "$4" | jq "${6:-.}" > "$work/view.json"
  check_release_view "$work/view.json" "$work/composed-beta.md" "$beta" "$5" "$image" "$work/expected-$beta.txt" 2> "$work/err.txt"
  expect_rc "$1" "$2" $?
}
view_case "draft: identical draft body and state pass"                  0       "$work/composed-beta.md" true  true
view_case "draft: body differing by one byte is rejected"               nonzero "$work/x.md"             true  true
view_case "draft: body with an extra trailing newline is rejected"      nonzero "$work/jq.md"            true  true
view_case "draft: template-only body is rejected"                       nonzero "$work/tmpl-beta.md"     true  true
view_case "draft: already published (not a draft) is rejected"         nonzero "$work/composed-beta.md" false true
view_case "draft: 16 assets are rejected"                               nonzero "$work/composed-beta.md" true  true  '.assets |= .[1:]'
view_case "draft: wrong title is rejected"                              nonzero "$work/composed-beta.md" true  true  '.name = "Campertron 9.9.9"'
view_case "draft: wrong tag is rejected"                                nonzero "$work/composed-beta.md" true  true  '.tagName = "v9.9.9"'
view_case "draft: missing body is rejected"                             nonzero "$work/composed-beta.md" true  true  'del(.body)'
view_case "public: identical published body and state pass"            0       "$work/composed-beta.md" false false
view_case "public: body differing by one byte is rejected"             nonzero "$work/x.md"             false false
view_case "public: body with an extra trailing newline is rejected"     nonzero "$work/jq.md"            false false
view_case "public: still a draft is rejected"                           nonzero "$work/composed-beta.md" true  false
view_case "public: a GitHub prerelease is rejected"                     nonzero "$work/composed-beta.md" false false '.isPrerelease = true'

echo "=== $passed passed, $failed failed"
[ "$failed" -eq 0 ]
