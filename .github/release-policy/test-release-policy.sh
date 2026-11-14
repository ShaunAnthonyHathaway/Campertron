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
