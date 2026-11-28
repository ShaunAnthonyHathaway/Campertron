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
  # the Downloads sentence says what each program does: the console searches, notifies by email or, since D67, ntfy, and
  # since D113 names the web UI it hosts (D92); the GUI is the campground lookup, edits the settings and search files (D43,
  # D44) and searches on request (D46), so "lookup" alone, "email notifications" alone and a console without the web UI are stale
  grep -qxF 'Each platform has a **console** archive (search, notifications by email or ntfy, and the web UI) and a **GUI** archive (campground lookup, settings and search editing, and searches on request). Extract an archive and run `CampertronConsole` or `CampertronGUI`. No .NET installation is needed.' "$work/notes-$1.md"
  expect_rc "notes for $1 describe the console archive (email or ntfy, the web UI) and the GUI archive (lookup, editing, searches on request)" 0 $?
  grep -qF '(search and email notifications)' "$work/notes-$1.md"; expect_rc "notes for $1 no longer say the console notifies by email only" nonzero $?
  grep -qF '(search, and notifications by email or ntfy)' "$work/notes-$1.md"; expect_rc "notes for $1 no longer describe a console archive without the web UI" nonzero $?
  # the Docker section names both unattended outputs the container runs with (Email, and Ntfy since D67), and since D113
  # the two folders, the user that must write to them, and --web with port 8790 for the web UI
  grep -qxF '`docker pull '"$image:$1"'` (linux/amd64 and linux/arm64). Both images are run in CI before publication. Mount two folders: `/config` for your settings, searches, `Camp.History` and the campground list (back it up), and `/cache`, into which a refresh of the campground data downloads. The container refuses to start without them, and its user, ID 1654, must be able to write to both. Without `--web` it runs the monitoring headless, with `outputTo: Email` or `outputTo: Ntfy`. For the web UI, add `--web` and publish port 8790:' "$work/notes-$1.md"
  expect_rc "notes for $1 say the container needs /config and /cache, runs the monitoring with outputTo: Email or Ntfy, and the web UI with --web on port 8790" 0 $?
  grep -qF 'The container runs headless with' "$work/notes-$1.md"; expect_rc "notes for $1 no longer say the container only runs headless" nonzero $?
  grep -qF '(campground lookup)' "$work/notes-$1.md"; expect_rc "notes for $1 no longer call the GUI archive a campground lookup only" nonzero $?
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
tamper "non-native platform marked as smoke-tested" 0.1.0-beta.5 true '/`linux-arm` |/s/no: supported and cross-built, not run natively/yes: console clean install and GUI smoke test/'
tamper "win-x86 (run on 64-bit Windows) marked as natively smoke-tested" 0.1.0-beta.5 true '/`win-x86` |/s/on 64-bit Windows: console clean install/yes: console clean install/'
tamper "win-x86 marked as not run"               1.2.3 false         '/`win-x86` |/s/on 64-bit Windows: console clean install and GUI smoke test/no: supported and cross-built, not run natively/'
tamper "linux-arm marked as run on 64-bit Windows" 0.1.0-beta.5 true '/`linux-arm` |/s/no: supported and cross-built, not run natively/on 64-bit Windows: console clean install and GUI smoke test/'
tamper "linux-arm64 marked as not run"           0.1.0-beta.5 true   '/`linux-arm64` |/s/yes: console clean install and GUI smoke test/no: supported and cross-built, not run natively/'
tamper "native platform marked as not smoke-tested" 1.2.3 false     '/`osx-arm64` |/s/yes: console clean install and GUI smoke test/no: supported and cross-built, not run natively/'
tamper "native smoke-test statement removed"      0.1.0-beta.5 true  's/linux-arm is supported and built from the same sources, but is not run in CI/Some platforms are supported/'
tamper "win-x86 statement claims a native run"    0.1.0-beta.5 true  's/win-x86 runs the same tests as a 32-bit process on 64-bit Windows, not on a 32-bit system/win-x86 is run natively/'
tamper "container smoke-test statement removed"   1.2.3 false        's/ Both images are run in CI before publication\.//'
tamper "wrong Docker pull tag"                    0.1.0-beta.5 true  's/campertronconsole:0.1.0-beta.5/campertronconsole:latest/'

tamper "compatibility section removed"            0.1.0-beta.5 true  '/^## Compatibility with older versions$/d'
tamper "old-version warning removed"              1.2.3 false        's/Do not run versions older than 0.1.0-beta.4/Avoid old versions/'
tamper "--version instructions removed"           0.1.0-beta.5 true  's/`CampertronConsole --version` and `CampertronGUI --version`/the menu/'
tamper "attestation instructions removed"         1.2.3 false        '/^- Build provenance:/d'

# --- the web UI in the generated sections (D113): how to start and reach it, natively and in the container, its plain-HTTP
# warning, its links, and nothing false, secret, transient or invented. web_problems reads the generated sections only (from
# "## Downloads" on; a curated fragment may say, for example, that several users aren't supported) and is independent of
# render_notes and validate_notes; tampered bodies below must be refused by validate_notes (the gate of every stage: the dry
# run, the assembled notes, the tagged commit's reconstruction, the draft and the published body), by web_problems or by both.
notes_root="$(cd "$here/../.." && pwd)"
web_problems() { # NOTES_FILE VERSION -> one line per problem, nothing when the generated sections are right
  local f="$1" v="$2" gen="$work/web-gen.md" block expected_block link path anchor text t
  sed -n '/^## Downloads$/,$p' "$f" > "$gen"
  # web mode, its command, its address and port, local-only listening, the password and the setup code
  grep -qxF -- '**The web UI:** run `CampertronConsole --web` (on Windows `CampertronConsole.exe --web`), then open `http://<this computer'"'"'s name or address>:8790/` in a browser, or `http://localhost:8790/` on the same computer. Web mode listens on port 8790 on every network interface; `--listen 127.0.0.1` (or `--listen localhost`) keeps it to this computer, and `--port` chooses another port. A password is on by default: the first start writes a one-time setup code to its output; enter it in the browser, choose the password and continue the setup there.' "$gen" \
    || echo "the web UI: CampertronConsole --web, its address on port 8790, every interface by default, --listen 127.0.0.1, the password and the setup code"
  grep -qF '(search, notifications by email or ntfy, and the web UI)' "$gen" || echo "the console archive's description without the web UI"
  # the plain-HTTP limitation, no exposure to the Internet, an HTTPS reverse proxy the user runs; HTTPS named only there
  grep -qxF -- '**It speaks plain HTTP only.** Campertron has no HTTPS server of its own, and the password protects the pages, not the connection: whoever can watch the network traffic can read the password and the session. Use it on this computer or on a home network you trust, and never forward a router port to it or expose it to the Internet. For access from outside your network, put it behind an HTTPS reverse proxy that you run and set up as the guide describes, and keep the web UI itself off the network.' "$gen" \
    || echo "the plain-HTTP statement: no HTTPS of its own, the password not the connection's protection, never forwarded or exposed, an HTTPS reverse proxy"
  [ "$(grep -o 'HTTPS' "$gen" | wc -l)" -eq 2 ] && [ "$(grep -c 'no HTTPS server of its own' "$gen")" -eq 1 ] && [ "$(grep -c 'an HTTPS reverse proxy that you run' "$gen")" -eq 1 ] \
    || echo "HTTPS named other than as 'no HTTPS server of its own' and 'an HTTPS reverse proxy that you run'"
  ! grep -qiE 'https://(<|localhost|127\.|\[::1\])' "$gen" || echo "the web UI's own address given as https://"
  [ "$(grep -oi 'internet' "$gen" | wc -l)" -eq 1 ] && [ "$(grep -oi 'forward' "$gen" | wc -l)" -eq 1 ] && grep -qF 'never forward a router port to it or expose it to the Internet' "$gen" \
    || echo "the Internet or port forwarding named other than in the warning against them"
  ! grep -qiE '\b(encrypt[a-z]*|tls|ssl|certificates?|secure connection)\b' "$gen" || echo "encryption or certificates claimed"
  ! grep -qiE 'built[- ]?in (https|tls|ssl|reverse proxy|proxy|certificate|server)|reverse proxy (is )?(included|built)|\b(any|every|all) (https )?reverse prox' "$gen" \
    || echo "a built-in HTTPS, certificate or reverse proxy, or every reverse proxy, claimed"
  ! grep -qiE '\b(multiple|several|many|more) (users|passwords|accounts)\b|multi-?user|user accounts' "$gen" || echo "several users, passwords or accounts claimed"
  ! grep -qiE '\bPWA\b|progressive web app|installable|home screen' "$gen" || echo "an installable web app claimed"
  ! grep -qF '/healthz' "$gen" || echo "/healthz described (the guide's subject)"
  # the container: the folders, its user, --web, port 8790, this version's image, the setup code in its log, local-only publishing
  grep -qF 'Mount two folders: `/config` for your settings, searches, `Camp.History` and the campground list (back it up), and `/cache`, into which a refresh of the campground data downloads. The container refuses to start without them, and its user, ID 1654, must be able to write to both. Without `--web` it runs the monitoring headless, with `outputTo: Email` or `outputTo: Ntfy`. For the web UI, add `--web` and publish port 8790:' "$gen" \
    || echo "the Docker statement: /config and /cache required, user ID 1654, the monitoring without --web, --web and port 8790"
  block="$(awk '/^```sh$/ {f = 1; next} /^```$/ {f = 0} f' "$gen")"
  expected_block="docker run -d --name campertron \\
  --volume ~/campertron/config:/config \\
  --volume ~/campertron/cache:/cache \\
  --publish 8790:8790 \\
  $image:$v --web"
  [ "$block" = "$expected_block" ] || echo "the Docker example is not exactly docker run with /config, /cache, --publish 8790:8790 and $image:$v --web"
  printf '%s\n' "$block" | grep -xF '  --publish 8790:8790 \' > /dev/null || echo "the Docker example does not publish port 8790"
  printf '%s\n' "$block" | grep -E '^  --volume [^ ]+:/config \\$' > /dev/null || echo "the Docker example does not mount /config"
  printf '%s\n' "$block" | grep -E '^  --volume [^ ]+:/cache \\$' > /dev/null || echo "the Docker example does not mount /cache"
  [ "$(printf '%s\n' "$block" | tail -n 1)" = "  $image:$v --web" ] || echo "the Docker example does not start this version's image with --web"
  [ "$(grep -oE 'campertronconsole:[^ `)]*' "$gen" | sort -u | tr '\n' ' ')" = "campertronconsole:$v " ] || echo "an image tag other than this version's"
  grep -qF 'Then read the setup code with `docker logs campertron` and open `http://<the computer'"'"'s name or address>:8790/`. To keep the web UI to the computer Docker runs on, publish `127.0.0.1:8790:8790` instead.' "$gen" \
    || echo "the container's setup code (docker logs), its address and publishing on 127.0.0.1"
  ! grep -qF '0.0.0.0' "$gen" || echo "listening on every address recommended by name (0.0.0.0)"
  # the existing download, checksum and attestation guidance
  grep -qF 'Extract an archive and run `CampertronConsole` or `CampertronGUI`. No .NET installation is needed.' "$gen" || echo "the archive instructions"
  [ "$(grep -c '^| `' "$gen")" -eq 8 ] || echo "the archive table"
  grep -qxF -- '- Checksums: `sha256sum -c SHA256SUMS.txt --ignore-missing`' "$gen" || echo "the checksum instructions"
  grep -qxF -- "- Build provenance: \`gh attestation verify <file> --repo $repository\`" "$gen" || echo "the attestation instructions"
  # Markdown: the four headings in order, one fenced shell block between blank lines with its continuation lines, no tab and no
  # trailing space (a hard line break)
  [ "$(grep '^#' "$gen" | tr '\n' '|')" = '## Downloads|## Compatibility with older versions|## Docker|## Verify your download|' ] || echo "the generated headings"
  [ "$(grep -c '^```' "$gen")" -eq 2 ] && [ "$(grep -B1 -A0 '^```sh$' "$gen" | head -n 1)" = '' ] && [ "$(grep -A1 '^```$' "$gen" | tail -n 1)" = '' ] \
    && [ "$(printf '%s\n' "$block" | sed '$d' | grep -vc ' \\$')" -eq 0 ] || echo "the fenced shell block"
  ! { grep -q $'\t' "$gen" || grep -qE ' +$' "$gen"; } || echo "a tab or a trailing space"
  # links: absolute, to this repository's files on master that exist, with an anchor that is a heading of the file; no other URL
  [ "$(grep -oE '\]\([^)]*\)' "$gen" | wc -l)" -eq 2 ] || echo "not exactly the two links (the web UI guide and the README's Docker section)"
  while read -r link; do
    link="${link#](}"; link="${link%)}"
    [[ "$link" == "https://github.com/$repository/blob/master/"* ]] || { echo "a link that is not absolute to this repository's master: $link"; continue; }
    path="${link#https://github.com/$repository/blob/master/}"; anchor=""
    [[ "$path" == *"#"* ]] && { anchor="${path#*#}"; path="${path%%#*}"; }
    [ -f "$notes_root/$path" ] || { echo "a link to a missing file: $path"; continue; }
    [ -z "$anchor" ] || sed -n 's/^#\{1,6\} \(.*\)$/\1/p' "$notes_root/$path" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9 _-]//g; s/ /-/g' | grep -xF -- "$anchor" > /dev/null \
      || echo "a link to a missing heading: $path#$anchor"
  done < <(grep -oE '\]\([^)]*\)' "$gen")
  [ -z "$(grep -oE 'https://[^ `)]+' "$gen" | grep -v "^https://github.com/$repository/blob/master/")" ] || echo "another https:// address"
  ! grep -qiE 'campertron\.com|github\.io|/releases/(download|latest)' "$gen" || echo "the website or an invented download address"
  # nothing secret or private: no setup code, path of a computer, password or token setting
  ! grep -qE '>>>|<<<|[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}' "$gen" || echo "a setup code"
  ! grep -qE '/home/|/Users/|/root/|/tmp/|[A-Z]:\\|/run/secrets|\.local/share' "$gen" || echo "a private or computer-specific path"
  ! grep -qiE 'smtpPassword|CAMPERTRON_(EMAIL_PASSWORD|NTFY_TOKEN)|passwordProtection|token|password: |secret:' "$gen" || echo "a password, token or secret setting"
  # nothing transient or version-specific but this version: no Latest wording after the status, no other version than this one
  # and the compatibility section's 0.1.0-beta.4, no candidate or dry-run wording (the dry run's own version left out)
  ! grep -qi 'latest' "$gen" || echo "Latest wording in the generated sections"
  t="$(grep -v '^| `' "$gen" | grep -oE '(^|[^0-9.])[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?' | sed -E 's/^[^0-9]//' | grep -vE '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+' | sort -u | grep -vxF -e "$v" -e 0.1.0-beta.4 || true)"
  [ -z "$t" ] || echo "another version named: $(echo "$t" | tr '\n' ' ')"
  text="$(cat -- "$f")"; text="${text//"$v"/}"
  ! grep -qiE 'dry[ -]?run|candidate|synthetic|template|draft|unpublished|unreleased|not (yet )?(published|released)' <<< "$text" || echo "candidate, dry-run or unreleased wording"
}
# the template itself: no image, owner, repository or version written into render_notes or its texts
{ declare -f render_notes web_ui_guide_url readme_docker_url; printf '%s\n' "$WEB_UI_STATEMENT" "$WEB_HTTP_STATEMENT" "$CONTAINER_MOUNT_STATEMENT" "$CONTAINER_WEB_STATEMENT"; } > "$work/template-src.txt"
! { grep -qE 'ghcr\.io|campertronconsole|github\.com/[A-Za-z]' "$work/template-src.txt" || grep -qi 'shaunanthonyhathaway' "$work/template-src.txt"; }; expect_rc "template: no image, owner or repository written into render_notes or its web texts" 0 $?
[ "$(grep -oE '[0-9]+(\.[0-9]+){2,3}' "$work/template-src.txt" | grep -vE '^[0-9]+(\.[0-9]+){3}$' | sort -u | tr '\n' ' ')" = '0.1.0 ' ]; expect_rc "template: no version written into render_notes or its web texts but the compatibility section's 0.1.0-beta.4" 0 $?
grep -qF 'echo "  $image:$version --web"' "$work/template-src.txt"; expect_rc "template: the Docker example's image is the release's image and version" 0 $?
for spec in "0.0.0-dryrun.7 true" "0.1.0-beta.5 true" "1.2.3 false" "0.3.0 false"; do
  set -- $spec
  expected_archives "$1" > "$work/expected-$1.txt"
  render_notes "$1" "$2" "$image" "$repository" > "$work/web-$1.md"
  validate_notes "$work/web-$1.md" "$1" "$2" "$image" "$work/expected-$1.txt" > "$work/out.txt"; expect_rc "web: notes for $1 (prerelease=$2) validate" 0 $?
  expect_eq "web: the generated sections for $1 start and reach the web UI and the container correctly, and claim nothing false" "" "$(web_problems "$work/web-$1.md" "$1")"
done
# the real 0.3.0 body: the curated fragment composed with the generated sections, as the tag run would publish it
compose_notes 0.3.0 false "$image" "$repository" "$here/../release-notes/v0.3.0.md" > "$work/web-composed.md" 2> "$work/err.txt"; expect_rc "web: the curated 0.3.0 notes compose" 0 $?
validate_notes "$work/web-composed.md" 0.3.0 false "$image" "$work/expected-0.3.0.txt" > "$work/out.txt"; expect_rc "web: the composed 0.3.0 body validates" 0 $?
expect_eq "web: the composed 0.3.0 body's generated sections are right and it has no candidate or dry-run wording" "" "$(web_problems "$work/web-composed.md" 0.3.0)"
[ "$(sed -n '/^## Downloads$/,$p' "$work/web-composed.md")" = "$(sed -n '/^## Downloads$/,$p' "$work/web-0.3.0.md")" ]; expect_rc "web: the composed body's generated sections are the template's for its version" 0 $?
# tampered bodies (from the stable 1.2.3 body): refused by validate_notes (validate), by web_problems (claims) or by both
web_tamper() { # description sed-expression validate|claims|both
  cp "$work/web-1.2.3.md" "$work/wt.md"; sed -i "$2" "$work/wt.md"
  if cmp -s "$work/web-1.2.3.md" "$work/wt.md"; then failed=$((failed+1)); echo "FAIL  web tamper changed nothing: $1"; return; fi
  case "$3" in validate|both) validate_notes "$work/wt.md" 1.2.3 false "$image" "$work/expected-1.2.3.txt" > "$work/out.txt"; expect_rc "web: validate_notes refuses: $1" nonzero $?;; esac
  case "$3" in claims|both) [ -n "$(web_problems "$work/wt.md" 1.2.3)" ]; expect_rc "web: the generated-text check refuses: $1" 0 $?;; esac
}
web_tamper "web mode omitted"                         '/^\*\*The web UI:\*\*/d' both
web_tamper "--web omitted from the start command"     's/run `CampertronConsole --web` (on Windows `CampertronConsole.exe --web`)/run `CampertronConsole`/' both
web_tamper "port 8790 omitted"                        's/:8790\//\//g; s/ on port 8790 on every/ on every/' both
web_tamper "the console archive without the web UI"   's/(search, notifications by email or ntfy, and the web UI)/(search, and notifications by email or ntfy)/' claims
web_tamper "local-only listening wrong (0.0.0.0)"     's/`--listen 127.0.0.1` (or `--listen localhost`) keeps it/`--listen 0.0.0.0` keeps it/' both
web_tamper "the web UI's address as https://"         's#`http://localhost:8790/`#`https://localhost:8790/`#' both
web_tamper "the setup code step omitted"              's/ A password is on by default: the first start writes a one-time setup code to its output; enter it in the browser, choose the password and continue the setup there\.//' both
web_tamper "the port mapping omitted"                 '/^  --publish 8790:8790 \\$/d' both
web_tamper "/config omitted"                          '/^  --volume ~\/campertron\/config:\/config \\$/d' both
web_tamper "/cache omitted"                           '/^  --volume ~\/campertron\/cache:\/cache \\$/d' both
web_tamper "/cache called optional"                   's/The container refuses to start without them/The container can start without `\/cache`/' both
web_tamper "user ID 1654 omitted"                     's/ and its user, ID 1654, must be able to write to both//' both
web_tamper "the wrong image tag (latest)"             's/campertronconsole:1\.2\.3 --web$/campertronconsole:latest --web/' both
web_tamper "a hard-coded 0.3.0 image"                 's/campertronconsole:1\.2\.3 --web$/campertronconsole:0.3.0 --web/' both
web_tamper "--web omitted from the container"         's/^\(  .*campertronconsole:1\.2\.3\) --web$/\1/' both
web_tamper "the Docker example's fence removed"       '/^```sh$/d' both
web_tamper "the plain-HTTP statement as HTTPS"        's/It speaks plain HTTP only\./It speaks HTTPS./' both
web_tamper "HTTPS claimed for web mode itself"        's/Campertron has no HTTPS server of its own/Campertron serves HTTPS by itself/' both
web_tamper "the password described as encryption"     's/the password protects the pages, not the connection/the password encrypts the connection/' both
web_tamper "direct public exposure recommended"       's/never forward a router port to it or expose it to the Internet/forward a router port to it to open it from the Internet/' both
web_tamper "the Internet warning removed"             's/, and never forward a router port to it or expose it to the Internet//' both
web_tamper "every reverse proxy claimed"              's/an HTTPS reverse proxy that you run/any reverse proxy/' both
web_tamper "a built-in reverse proxy claimed"         '$a Campertron has a built-in reverse proxy for HTTPS.' claims
web_tamper "certificates claimed"                     '/^\*\*It speaks plain HTTP only/a It manages its certificate by itself.' claims
web_tamper "several users claimed"                    '/^\*\*The web UI:\*\*/a It supports multiple users, each with an account.' claims
web_tamper "an installable web app (PWA) claimed"     '/^\*\*The web UI:\*\*/a Install it as a PWA on your phone.' claims
web_tamper "/healthz described"                       '$a Its /healthz address shows the monitoring.' claims
web_tamper "a setup code shown"                       '/^Then read the setup code/a Web UI setup code: >>> ABCD-EFGH-JKMN <<<' claims
web_tamper "a private path"                           's#~/campertron/config:/config#/home/example/campertron/config:/config#' both
web_tamper "a password setting"                       '$a Set passwordProtection: false to skip the password.' claims
web_tamper "the archive instructions lost"            's/Extract an archive and run `CampertronConsole` or `CampertronGUI`. //' claims
web_tamper "the checksums lost"                       '/^- Checksums:/d' both
web_tamper "the attestation guidance lost"            '/^- Build provenance:/d' both
web_tamper "the web UI guide link broken"             's#/docs/web-ui\.md)#/docs/web-guide.md)#' both
web_tamper "the web UI guide link removed"            's/^The \[web UI guide\](.*) covers /The web UI guide covers /' both
web_tamper "a relative link (fails in a release body)" 's#(https://github\.com/example-owner/Campertron/blob/master/docs/web-ui\.md)#(docs/web-ui.md)#' both
web_tamper "the README link to a missing heading"     's/#run-it-with-docker)/#docker)/' both
web_tamper "the unpublished website linked"           's#(https://github\.com/example-owner/Campertron/blob/master/README\.md\#run-it-with-docker)#(https://campertron.com/docker)#' both
web_tamper "an invented download address"            '/^## Verify your download$/i Download: https://github.com/example-owner/Campertron/releases/download/v1.2.3/campertron.zip' claims
web_tamper "candidate wording"                        '/^## Verify your download$/i This is a release candidate built by a dry run.' claims
web_tamper "transient Latest wording"                 '/^## Docker$/a v0.2.0 is Latest until this version is published.' claims
web_tamper "another version named"                    's/Delete old copies instead of running them\./Delete old copies, such as 0.2.0, instead of running them./' claims
web_tamper "a trailing space (a hard line break)"     's/^\(\*\*The web UI:\*\*.*\)$/\1  /' claims

# --- .NET 11 guards: project files, global.json, Dockerfile, publish output, --version report
here_root="$(cd "$here/../.." && pwd)"
check_project_frameworks "$here_root" > "$work/out.txt"; expect_rc "repository projects, props and Dockerfile are .NET 11" 0 $?
# The image's description label names both unattended outputs (D67): the container notifies by email or ntfy.
grep -qxF '      org.opencontainers.image.description="Polls recreation.gov for campsite availability and notifies you of new matches by email or ntfy (headless, outputTo: Email or Ntfy)." \' "$here_root/CampertronConsole/Dockerfile"
expect_rc "Dockerfile: the image description names email and ntfy (outputTo: Email or Ntfy)" 0 $?

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
# the console hosts the web UI (D80): the ASP.NET Core framework of the same .NET is included with it, and no other framework
web_frameworks='[{"name":"Microsoft.NETCore.App","version":"11.0.0"},{"name":"Microsoft.AspNetCore.App","version":"11.0.0"}]'
pub_case "console with the ASP.NET Core 11 framework passes" 0 "printf '{\"runtimeOptions\":{\"tfm\":\"net11.0\",\"includedFrameworks\":%s}}\n' '$web_frameworks' > '$work/o/App.runtimeconfig.json'"
pub_case "ASP.NET Core framework 10.0.15 rejected"   nonzero "printf '{\"runtimeOptions\":{\"tfm\":\"net11.0\",\"includedFrameworks\":%s}}\n' '${web_frameworks/AspNetCore.App\",\"version\":\"11.0.0/AspNetCore.App\",\"version\":\"10.0.15}' > '$work/o/App.runtimeconfig.json'"
pub_case "a framework that is neither rejected"      nonzero "printf '{\"runtimeOptions\":{\"tfm\":\"net11.0\",\"includedFrameworks\":%s}}\n' '${web_frameworks/AspNetCore/WindowsDesktop}' > '$work/o/App.runtimeconfig.json'"
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
  [ -n "$(fxv "noweb_$arch" "")" ] || printf 'web UI %s' "$arch" > "$a/CampertronWeb.dll"
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
    --argjson ports "$(fxv ports '{"8790/tcp":{}}')" --argjson cmd "$(fxv cmd null)" \
    '{architecture: $arch, os: "linux", config: {User: $user, Env: ["PATH=/usr/bin", ("DOTNET_VERSION=" + $dn)], Entrypoint: ["dotnet", "CampertronConsole.dll"],
      Cmd: $cmd, ExposedPorts: $ports,
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
# the natively run linux/arm64 image (S2): identified by its saved config and its layers, whatever ID Docker gives it
arm_layers="$(jq -c .rootfs.diff_ids "$work/fx-layout/blobs/sha256/${arm_c#sha256:}")"; amd_layers="$(jq -c .rootfs.diff_ids "$work/fx-layout/blobs/sha256/${amd_c#sha256:}")"
check_loaded_platform_image arm64 arm64 "$arm_c" "$arm_layers" "$work/fx-out" "$arm_m" "$arm_c" > "$work/out.txt"; rc=$?
{ [ "$rc" -eq 0 ] && grep -qx "Loaded linux/arm64 image is the validated image: the same config $arm_c, 2 identical layer diff_ids" "$work/out.txt"; }
expect_rc "loaded platform image: the validated arm64 config and layers accepted" 0 $?
check_loaded_platform_image arm64 amd64 "$arm_c" "$arm_layers" "$work/fx-out" "$arm_m" "$arm_c" > "$work/out.txt"
{ [ $? -ne 0 ] && grep -q '^::error::Container image: the loaded image is a amd64 image, expected arm64' "$work/out.txt"; }
expect_rc "loaded platform image: an image of another architecture rejected" 0 $?
check_loaded_platform_image arm64 arm64 "$amd_c" "$arm_layers" "$work/fx-out" "$arm_m" "$arm_c" > "$work/out.txt"
{ [ $? -ne 0 ] && grep -q '^::error::Container image: the loaded linux/arm64 image has the config .* not the validated config' "$work/out.txt"; }
expect_rc "loaded platform image: another config rejected" 0 $?
check_loaded_platform_image arm64 arm64 "$arm_c" "$amd_layers" "$work/fx-out" "$arm_m" "$arm_c" > "$work/out.txt"
{ [ $? -ne 0 ] && grep -q "^::error::Container image: the loaded linux/arm64 image's layer diff_ids differ" "$work/out.txt"; }
expect_rc "loaded platform image: other layers rejected" 0 $?
check_loaded_platform_image arm64 arm64 "$amd_c" "$amd_layers" "$work/fx-out" "$amd_m" "$amd_c" > "$work/out.txt"
{ [ $? -ne 0 ] && grep -q '^::error::Container image: validated config .* is not a linux/arm64 image' "$work/out.txt"; }
expect_rc "loaded platform image: the amd64 image passed off as the arm64 image rejected" 0 $?
check_loaded_platform_image arm64 arm64 "$arm_c" "$arm_layers" "$work/fx-out" "$arm_m" "$amd_c" > "$work/out.txt"
expect_rc "loaded platform image: a config the validated manifest does not reference rejected" nonzero $?
saved_fixture() { # FILE CONFIG_PATH... -> a `docker save`-like tar whose manifest.json names those configs (none: no manifest.json)
  local f="$1" d="$work/saved"; shift; rm -rf "$d"; mkdir -p "$d"; printf 'layer' > "$d/layer.tar"
  if [ "$#" -gt 0 ]; then printf '%s\n' "$@" | jq -R '{Config: ., RepoTags: ["campertron-smoke-arm64:local"], Layers: ["layer.tar"]}' | jq -cs . > "$d/manifest.json"; fi
  tar -C "$d" -cf "$f" $(ls "$d"); }
hex="${arm_c#sha256:}"
saved_fixture "$work/saved.tar" "blobs/sha256/$hex"; expect_eq "saved image: the config named as an OCI blob (Docker 25 and later)" "$arm_c" "$(saved_image_config "$work/saved.tar")"
saved_fixture "$work/saved.tar" "$hex.json"; expect_eq "saved image: the config named <hex>.json (older Docker)" "$arm_c" "$(saved_image_config "$work/saved.tar")"
saved_fixture "$work/saved.tar" "blobs/sha256/$hex" "blobs/sha256/${amd_c#sha256:}"; saved_image_config "$work/saved.tar" > "$work/out.txt"
{ [ $? -ne 0 ] && grep -q '^::error::Container image: the saved image does not name exactly one image config' "$work/out.txt"; }
expect_rc "saved image: two images rejected" 0 $?
saved_fixture "$work/saved.tar"; saved_image_config "$work/saved.tar" > "$work/out.txt"; expect_rc "saved image: no manifest.json rejected" nonzero $?
saved_fixture "$work/saved.tar" "../../etc/passwd"; saved_image_config "$work/saved.tar" > "$work/out.txt"; expect_rc "saved image: a config name that is not a digest rejected" nonzero $?
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
oci_case "image without the web UI's assembly rejected"        nonzero "linux/arm64: /app/CampertronWeb.dll is missing" noweb_arm64=1
oci_case "image that does not expose the web UI's port rejected" nonzero "the exposed ports are \[\], expected only 8790/tcp" ports='{}'
oci_case "image that exposes another port too rejected"        nonzero "expected only 8790/tcp" ports='{"8080/tcp":{},"8790/tcp":{}}'
oci_case "image whose default command is web mode rejected"    nonzero "the default command is \[\"--web\"\], expected none" cmd='["--web"]'
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
# skopeo stub: a registry kept in files, without a network. $STUB_REG has one file per tagged image reference holding the raw
# bytes the tag points to (an image index); "$STUB_REG.blobs" is a content-addressed store (one file per SHA-256) of every
# index, manifest and config blob copied or seeded, so a manifest and its config can be read by digest. Every call is logged.
#   inspect --raw docker://REPO:TAG           the tag's raw bytes, or "manifest unknown"
#   inspect --raw docker://REPO@sha256:HEX     the blob HEX (a manifest), or "manifest unknown"
#   inspect --config --raw docker://REPO@sha256:HEX   the config blob the manifest HEX names
#   copy --all --preserve-digests … oci-archive:FILE docker://REPO:TAG   stores every blob and points the tag at the index
# Test switches: STUB_TAMPER=1 adds a byte to a copied tag; STUB_FAIL_COPY=<suffix> fails a copy to a reference ending in it;
# STUB_RACE_REF=<ref> and STUB_RACE_FROM=<file> make the first copy also point <ref> at the file's bytes (another publisher).
echo "skopeo $*" >> "$STUB_LOG"
blobs="$STUB_REG.blobs"
ref_file() { echo "$STUB_REG/$(printf '%s' "$1" | tr '/:' '__')"; }
target="${@: -1}"; target="${target#docker://}"; config=""
for arg in "$@"; do [ "$arg" != --config ] || config=1; done
case "$1" in
  inspect)
    case "$target" in
      *@sha256:*) hex="${target##*@sha256:}"
                  [ -f "$blobs/$hex" ] || { echo "reading manifest sha256:$hex in ${target%@*}: manifest unknown" >&2; exit 1; }
                  if [ -z "$config" ]; then cat "$blobs/$hex"; exit 0; fi
                  c="$(jq -r '.config.digest // empty' "$blobs/$hex" 2> /dev/null)"
                  [ -n "$c" ] && [ -f "$blobs/${c#sha256:}" ] || { echo "reading blob $c: blob unknown" >&2; exit 1; }
                  cat "$blobs/${c#sha256:}" ;;
      *) [ -z "$config" ] || { echo "skopeo stub: unexpected call: skopeo $*" >&2; exit 99; }
         f="$(ref_file "$target")"; if [ -f "$f" ]; then cat "$f"; else echo "reading manifest ${target##*:} in ${target%:*}: manifest unknown" >&2; exit 1; fi ;;
    esac ;;
  copy) a="${@: -2:1}"; a="${a#oci-archive:}"
        if [ -n "${STUB_FAIL_COPY:-}" ] && [[ "$target" == *"$STUB_FAIL_COPY" ]]; then echo "copying to $target: simulated failure" >&2; exit 1; fi
        if [ -n "${STUB_RACE_REF:-}" ] && [ ! -e "$STUB_REG.raced" ]; then cp "$STUB_RACE_FROM" "$(ref_file "$STUB_RACE_REF")"; : > "$STUB_REG.raced"; fi
        mkdir -p "$blobs"; tar -xf "$a" -C "$blobs" --strip-components=2 --wildcards 'blobs/sha256/?*'
        ix="$(tar -xOf "$a" index.json | jq -r '.manifests[0].digest')"
        cp "$blobs/${ix#sha256:}" "$(ref_file "$target")"
        [ -z "${STUB_TAMPER:-}" ] || printf ' ' >> "$(ref_file "$target")" ;;
  *) echo "skopeo stub: unexpected call: skopeo $*" >&2; exit 99 ;;
esac
STUB
chmod +x "$work/bin-load/docker" "$work/bin-skopeo/skopeo"
reset_registry() { rm -rf "$work/reg" "$work/reg.blobs" "$work/reg.raced"; mkdir -p "$work/reg"; }  # an empty stub registry
printf '{"auths":{"ghcr.io":{"auth":"%s"}}}\n' "$(printf 'user:token' | base64)" > "$work/home/.docker/config.json"
check_image_step() { # archive [version] -> runs "Check both platform images" with a fresh RUNNER_TEMP ($work/rt-img) holding that archive
  rm -rf "$work/rt-img"; mkdir -p "$work/rt-img"; cp "$1" "$work/rt-img/container-image"; : > "$work/rt-img/out"; : > "$work/rt-img/summary"
  run_step "Check both platform images" RUNNER_TEMP="$work/rt-img" VERSION="${2:-$oci_ver}" GITHUB_SHA="$oci_rev" GITHUB_REPOSITORY=example-owner/Campertron \
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
reset_registry; publish_steps; rc=$?
{ [ "$rc" -eq 0 ] && [ "$(grep -c '^skopeo copy ' "$work/skopeo.log")" = 1 ] \
  && grep -qx "skopeo copy --all --preserve-digests --authfile $work/home/.docker/config.json oci-archive:image/container-image docker://ghcr.io/example-owner/campertronconsole:0.1.0-beta.10" "$work/skopeo.log" \
  && [ "$(ls "$work/reg")" = "ghcr.io_example-owner_campertronconsole_0.1.0-beta.10" ] && grep -qx "digest=$good_ix" "$work/rt-pub/out"; }
expect_rc "workflow docker-publish: pre-release copies the validated archive with --all --preserve-digests to the exact tag only" 0 $?
stub_skopeo() { PATH="$work/bin-skopeo:$PATH" STUB_REG="$work/reg" STUB_LOG="$work/stub-self.log" skopeo "$@"; }
amd_c="$(sed -n 's/^amd64_config=//p' "$work/rt-img/out")"
{ [ "sha256:$(stub_skopeo inspect --raw "docker://ghcr.io/example-owner/campertronconsole:0.1.0-beta.10" | sha256sum | cut -d' ' -f1)" = "$good_ix" ] \
  && [ "sha256:$(stub_skopeo inspect --raw "docker://ghcr.io/example-owner/campertronconsole@$amd_m" | sha256sum | cut -d' ' -f1)" = "$amd_m" ] \
  && [ "sha256:$(stub_skopeo inspect --config --raw "docker://ghcr.io/example-owner/campertronconsole@$amd_m" | sha256sum | cut -d' ' -f1)" = "$amd_c" ] \
  && ! stub_skopeo inspect --raw "docker://ghcr.io/example-owner/campertronconsole:latest" 2> "$work/out.txt" && grep -q 'manifest unknown' "$work/out.txt"; }
expect_rc "stub registry: a copied image's tag, platform manifest and config read back by tag and by digest; an absent tag is manifest unknown" 0 $?
reset_registry; publish_steps IMAGE_SHA256="$(printf 'f%.0s' {1..64})"
{ [ $? -ne 0 ] && grep -q '^::error::Downloaded image archive has SHA-256' "$work/step.txt" && [ ! -s "$work/skopeo.log" ]; } && \
{ reset_registry; publish_steps IMAGE_DIGEST="sha256:$(printf 'e%.0s' {1..64})"; [ $? -ne 0 ] && grep -q "^::error::Downloaded image does not match the docker job's validated image_digest" "$work/step.txt" && [ ! -s "$work/skopeo.log" ]; }
expect_rc "workflow docker-publish: archive file or OCI digest mismatch fails before any registry call" 0 $?
reset_registry; mkdir -p "$work/reg"; printf '{"schemaVersion":2}' > "$work/reg/ghcr.io_example-owner_campertronconsole_0.1.0-beta.10"; publish_steps
{ [ $? -ne 0 ] && grep -q '^::error::Container image: ghcr.io/example-owner/campertronconsole:0.1.0-beta.10 already exists with .*refusing to move it' "$work/step.txt" && ! grep -q '^skopeo copy' "$work/skopeo.log"; }
expect_rc "workflow docker-publish: an existing tag with another digest is refused, not moved" 0 $?
reset_registry; publish_steps STUB_TAMPER=1
{ [ $? -ne 0 ] && grep -q '^::error::Container image: ghcr.io/example-owner/campertronconsole:0.1.0-beta.10 is .* after copying, expected the validated' "$work/step.txt"; }
expect_rc "workflow docker-publish: a registry digest that differs after the copy fails" 0 $?

# --- TD-57 (decision D59): an exact version is never moved; a stable version's MAJOR.MINOR and latest move only forward.
# Against the stub registry only (no network, no registry): seeded images stand for what a registry already holds.
for v in "0.1.0 0.1.1 -1" "0.1.9 0.1.10 -1" "0.9.0 0.10.0 -1" "1.0.0 0.99.99 1" "10.0.0 9.0.0 1" "0.1.1 0.1.1 0" \
         "123456789012345678901.0.0 123456789012345678902.0.0 -1"; do
  set -- $v; expect_eq "stable versions compare as numbers: $1 vs $2" "$3" "$(semver_stable_compare "$1" "$2")"
done
for v in 0.1.0-beta.10 01.1.0 v0.1.0 0.1 0.1.0+build.5 latest ""; do
  semver_stable_compare "$v" 0.1.1 > "$work/out.txt"; rc=$?
  { [ "$rc" -ne 0 ] && [ ! -s "$work/out.txt" ]; }; expect_rc "stable version comparison refuses '$v'" 0 $?
done
pub_image="ghcr.io/example-owner/campertronconsole"
seed_blob() { local h; h="$(sha256sum < "$1" | cut -d' ' -f1)"; mkdir -p "$work/reg.blobs"; cp "$1" "$work/reg.blobs/$h"; echo "sha256:$h"; }  # FILE -> digest, stored
tag_file() { echo "$work/reg/$(printf '%s' "$1" | tr '/:' '__')"; }
tag_digest() { if [ -f "$(tag_file "$1")" ]; then echo "sha256:$(sha256sum < "$(tag_file "$1")" | cut -d' ' -f1)"; else echo absent; fi; }
reg_snapshot() { (cd "$work/reg" && for f in $(ls); do echo "$f $(sha256sum < "$f" | cut -d' ' -f1)"; done); }
seed_alias() { cp "$(tag_file "$2")" "$(tag_file "$1")"; }  # REF FROM_REF: REF points at the same bytes
seed_image() { # REF AMD64_LABEL [ARM64_LABEL] [MUTATION] -> REF points at a new two-platform image index; "-" leaves out the label.
  # MUTATION: no-arm64 (no linux/arm64 image in the index), bad-config or bad-manifest (the amd64 blob no longer matches its
  # digest), manifest (REF points at the amd64 manifest instead of an index)
  local ref="$1" la="$2" lb="${3:-$2}" mut="${4:-}" d="$work/seed" arch lab cfg man ents="[]"
  rm -rf "$d"; mkdir -p "$d" "$work/reg"
  for arch in amd64 arm64; do
    if [ "$arch" = arm64 ] && [ "$mut" = no-arm64 ]; then continue; fi
    if [ "$arch" = amd64 ]; then lab="$la"; else lab="$lb"; fi
    jq -cn --arg a "$arch" --arg v "$lab" --arg r "$ref" '{architecture: $a, os: "linux", rootfs: {type: "layers", diff_ids: []},
      config: {Labels: ({"org.opencontainers.image.revision": $r} + (if $v == "-" then {} else {"org.opencontainers.image.version": $v} end))}}' > "$d/config-$arch.json"
    cfg="$(seed_blob "$d/config-$arch.json")"
    jq -cn --arg mt "$OCI_MANIFEST_TYPE" --arg ct "$OCI_CONFIG_TYPE" --arg c "$cfg" --argjson s "$(wc -c < "$d/config-$arch.json")" \
      '{schemaVersion: 2, mediaType: $mt, config: {mediaType: $ct, digest: $c, size: $s}, layers: []}' > "$d/manifest-$arch.json"
    man="$(seed_blob "$d/manifest-$arch.json")"
    if [ "$arch" = amd64 ]; then
      case "$mut" in bad-config) printf ' ' >> "$work/reg.blobs/${cfg#sha256:}" ;; bad-manifest) printf ' ' >> "$work/reg.blobs/${man#sha256:}" ;; esac
    fi
    ents="$(jq -c --arg mt "$OCI_MANIFEST_TYPE" --arg m "$man" --arg a "$arch" --argjson s "$(wc -c < "$d/manifest-$arch.json")" \
      '. + [{mediaType: $mt, digest: $m, size: $s, platform: {architecture: $a, os: "linux"}}]' <<< "$ents")"
  done
  jq -cn --arg mt "$OCI_INDEX_TYPE" --argjson m "$ents" '{schemaVersion: 2, mediaType: $mt, manifests: $m}' > "$d/index.json"
  if [ "$mut" = manifest ]; then cp "$d/manifest-amd64.json" "$d/index.json"; fi
  seed_blob "$d/index.json" > /dev/null; cp "$d/index.json" "$(tag_file "$ref")"
}
seed_v010() { reset_registry; seed_image "$pub_image:0.1.0" 0.1.0; seed_alias "$pub_image:0.1" "$pub_image:0.1.0"; seed_alias "$pub_image:latest" "$pub_image:0.1.0"; }  # GHCR as v0.1.0 left it
fx_archive "$work/fx-pub.tar" label_version=0.1.1
pub_ix="$(jq -r '.manifests[0].digest' "$work/fx-layout/index.json")"; pub_ixf="$work/fx-layout/blobs/sha256/${pub_ix#sha256:}"
pub_amd="$(jq -r '.manifests[] | select(.platform.architecture == "amd64") | .digest' "$pub_ixf")"; pub_arm="$(jq -r '.manifests[] | select(.platform.architecture == "arm64") | .digest' "$pub_ixf")"
: > "$work/skopeo-all.log"
pub_call() { # [VAR=value...] VERSION PRERELEASE REF... -> publish_oci_archive of the validated archive against the stub registry; sets pub_rc
  : > "$work/skopeo.log"
  ( while [[ "${1:-}" =~ ^[A-Z_]+= ]]; do export "$1"; shift; done
    export PATH="$work/bin-skopeo:$PATH" STUB_REG="$work/reg" STUB_LOG="$work/skopeo.log"
    publish_oci_archive "$work/fx-pub.tar" "$pub_ix" "$pub_amd" "$pub_arm" "$work/home/.docker/config.json" "$@" ) > "$work/pub.txt" 2>&1
  pub_rc=$?; cat "$work/skopeo.log" >> "$work/skopeo-all.log"; }
refs_of() { docker_tag_refs "$pub_image" "$1" "$(semver_prerelease "$1")"; }
copies() { grep -c '^skopeo copy ' "$work/skopeo.log"; }
copied_tags() { sed -n 's#^skopeo copy .* docker://##p' "$work/skopeo.log" | tr '\n' ' ' | sed 's/ $//'; }
refused() { # description error-regex: the last pub_call failed with that ::error:: line, copied nothing and left every tag as it was
  { [ "$pub_rc" -ne 0 ] && grep -qE "^::error::Container image: .*$2" "$work/pub.txt" && [ "$(copies)" = 0 ] && [ "$(reg_snapshot)" = "$snap" ]; }
  expect_rc "$1" 0 $?; }
read_back_after_each_copy() { awk '/^skopeo copy /{ ref = $NF; if ((getline nx) <= 0) bad = 1; n = split(nx, w, " "); if (w[2] != "inspect" || w[n] != ref) bad = 1 } END { exit bad }' "$work/skopeo.log"; }
read_before_first_copy() { # every REF was read before the first copy
  local first r l; first="$(grep -n '^skopeo copy ' "$work/skopeo.log" | head -n 1 | cut -d: -f1)"; [ -n "$first" ] || return 1
  for r in "$@"; do l="$(grep -n -F " docker://$r" "$work/skopeo.log" | grep -v ':skopeo copy ' | head -n 1 | cut -d: -f1)"; [ -n "$l" ] && [ "$l" -lt "$first" ] || return 1; done; }
final_reads() { tail -n "$#" "$work/skopeo.log" | awk '{ print $1, $2, $3, $NF }' | tr '\n' '|'; }
expected_final_reads() { local r; for r in "$@"; do echo "skopeo inspect --raw docker://$r"; done | tr '\n' '|'; }

reset_registry; pub_call 0.1.1 false $(refs_of 0.1.1)
{ [ "$pub_rc" -eq 0 ] && [ "$(copied_tags)" = "$pub_image:0.1.1 $pub_image:0.1 $pub_image:latest" ] && read_back_after_each_copy && read_before_first_copy $(refs_of 0.1.1) \
  && [ "$(final_reads $(refs_of 0.1.1))" = "$(expected_final_reads $(refs_of 0.1.1))" ] \
  && [ "$(tag_digest "$pub_image:0.1.1") $(tag_digest "$pub_image:0.1") $(tag_digest "$pub_image:latest")" = "$pub_ix $pub_ix $pub_ix" ]; }
expect_rc "publish (stable, empty registry): exact, MAJOR.MINOR and latest created in that order, all read before the first copy, each read back after its copy and again at the end" 0 $?
pub_call 0.1.1 false $(refs_of 0.1.1)
{ [ "$pub_rc" -eq 0 ] && [ "$(copies)" = 0 ] && [ "$(grep -c 'already has the validated image index' "$work/pub.txt")" -ge 3 ]; }
expect_rc "publish (stable): every tag already at the validated digest is kept, nothing is copied again (idempotent re-run)" 0 $?
seed_v010; old_ix="$(tag_digest "$pub_image:0.1.0")"; pub_call 0.1.1 false $(refs_of 0.1.1)
{ [ "$pub_rc" -eq 0 ] && [ "$(copied_tags)" = "$pub_image:0.1.1 $pub_image:0.1 $pub_image:latest" ] && read_back_after_each_copy \
  && grep -qx "Moved $pub_image:0.1 forward from version 0.1.0 ($old_ix) to 0.1.1 = $pub_ix (linux/amd64 $pub_amd, linux/arm64 $pub_arm)" "$work/pub.txt" \
  && grep -q "^Moved $pub_image:latest forward from version 0.1.0 " "$work/pub.txt" && [ "$(grep -c -- '--config --raw' "$work/skopeo.log")" = 4 ] \
  && [ "$(tag_digest "$pub_image:0.1.0") $(tag_digest "$pub_image:0.1.1") $(tag_digest "$pub_image:0.1") $(tag_digest "$pub_image:latest")" = "$old_ix $pub_ix $pub_ix $pub_ix" ]; }
expect_rc "publish (stable, v0.1.0's tags present): 0.1.1 created, 0.1 and latest moved forward from the older stable version (both platforms' labels read by digest), 0.1.0 untouched" 0 $?
reset_registry; seed_image "$pub_image:0.1.1" 0.1.1; snap="$(reg_snapshot)"; pub_call 0.1.1 false $(refs_of 0.1.1)
refused "publish: an exact version at another digest is refused in the preflight, never moved, and nothing is copied" "$pub_image:0.1.1 already exists with .*refusing to move it .*an exact version is never moved"
seed_v010; seed_image "$pub_image:latest" 0.2.0; snap="$(reg_snapshot)"; pub_call 0.1.1 false $(refs_of 0.1.1)
refused "publish: latest at a newer stable version is not moved backward, and the absent exact version is not created either (preflight)" "$pub_image:latest carries the newer version 0.2.0 .*refusing to move it backward to 0.1.1"
seed_v010; seed_image "$pub_image:latest" -; snap="$(reg_snapshot)"; pub_call 0.1.1 false $(refs_of 0.1.1)
refused "publish: a floating tag whose image has no version label is not moved" "no usable version label"
for bad in v0.1.0 0.1 01.1.0 0.1.0+build.5 " 0.1.0" latest; do
  seed_v010; seed_image "$pub_image:latest" "$bad"; snap="$(reg_snapshot)"; pub_call 0.1.1 false $(refs_of 0.1.1)
  refused "publish: a floating tag whose image has the malformed version label '$bad' is not moved" "is not a stable version; refusing to move it"
done
seed_v010; seed_image "$pub_image:latest" 0.1.0-beta.10; snap="$(reg_snapshot)"; pub_call 0.1.1 false $(refs_of 0.1.1)
refused "publish: a floating tag whose image has a pre-release version label is not moved" "version label '0.1.0-beta.10' is not a stable version"
seed_v010; seed_image "$pub_image:latest" 0.1.1; snap="$(reg_snapshot)"; pub_call 0.1.1 false $(refs_of 0.1.1)
refused "publish: a floating tag whose image already carries this version (another digest) is not moved: the version is not newer" "already carries version 0.1.1: 0.1.1 is not newer"
seed_v010; seed_image "$pub_image:0.1" 0.0.9; snap="$(reg_snapshot)"; pub_call 0.1.1 false $(refs_of 0.1.1)
refused "publish: MAJOR.MINOR is moved only from an image of the same MAJOR.MINOR" "$pub_image:0.1 exists with .*version label 0.0.9 is not a 0.1 version"
seed_v010; seed_image "$pub_image:latest" 0.1.0 0.0.9; snap="$(reg_snapshot)"; pub_call 0.1.1 false $(refs_of 0.1.1)
refused "publish: a floating tag whose two platform images carry different version labels is not moved" "version labels differ \(linux/amd64 0.1.0, linux/arm64 0.0.9\)"
for m in no-arm64 bad-config bad-manifest manifest; do
  seed_v010; seed_image "$pub_image:latest" 0.1.0 0.1.0 "$m"; snap="$(reg_snapshot)"; pub_call 0.1.1 false $(refs_of 0.1.1)
  refused "publish: untrusted version evidence ($m) refuses the move" "the version of its image cannot be established"
done
seed_v010; pub_call 0.2.0-beta.1 true "$pub_image:0.2.0-beta.1"
{ [ "$pub_rc" -eq 0 ] && [ "$(copied_tags)" = "$pub_image:0.2.0-beta.1" ] && ! grep -qE "docker://$pub_image:(0\.1|latest)\$" "$work/skopeo.log" \
  && [ "$(tag_digest "$pub_image:0.1") $(tag_digest "$pub_image:latest")" = "$(tag_digest "$pub_image:0.1.0") $(tag_digest "$pub_image:0.1.0")" ]; }
expect_rc "publish (pre-release): only the exact version is created; latest and MAJOR.MINOR are neither read nor moved" 0 $?
seed_v010; snap="$(reg_snapshot)"
pub_call 0.2.0-beta.1 true "$pub_image:0.2.0-beta.1" "$pub_image:0.2" "$pub_image:latest"; { [ "$pub_rc" -ne 0 ] && [ ! -s "$work/skopeo.log" ]; } \
  && { pub_call 0.1.1 false "$pub_image:0.1.1" "$pub_image:0.1"; [ "$pub_rc" -ne 0 ] && [ ! -s "$work/skopeo.log" ]; } \
  && { pub_call 0.1.1 false "$pub_image:0.1.1" "$pub_image:latest" "$pub_image:0.1"; [ "$pub_rc" -ne 0 ] && [ ! -s "$work/skopeo.log" ]; } \
  && { pub_call 0.1.1 true $(refs_of 0.1.1); [ "$pub_rc" -ne 0 ] && grep -q 'prerelease=true does not match version 0.1.1' "$work/pub.txt" && [ ! -s "$work/skopeo.log" ]; } \
  && [ "$(reg_snapshot)" = "$snap" ]
expect_rc "publish: references other than the version's own (floating tags for a pre-release, one missing, another order) or a wrong pre-release flag fail before any registry request" 0 $?
reset_registry; pub_call STUB_TAMPER=1 0.1.1 false $(refs_of 0.1.1)
{ [ "$pub_rc" -ne 0 ] && grep -q "^::error::Container image: $pub_image:0.1.1 is .* after copying, expected the validated" "$work/pub.txt" && [ "$(copied_tags)" = "$pub_image:0.1.1" ]; }
expect_rc "publish: the exact version is read back after its copy, and a mismatch stops the run before any floating tag" 0 $?
seed_v010; pub_call STUB_FAIL_COPY=:latest 0.1.1 false $(refs_of 0.1.1)
{ [ "$pub_rc" -ne 0 ] && grep -q "^::error::Container image: skopeo copy to $pub_image:latest failed" "$work/pub.txt" \
  && [ "$(tag_digest "$pub_image:0.1.1") $(tag_digest "$pub_image:0.1") $(tag_digest "$pub_image:latest")" = "$pub_ix $pub_ix $(tag_digest "$pub_image:0.1.0")" ]; } \
  && { pub_call 0.1.1 false $(refs_of 0.1.1); [ "$pub_rc" -eq 0 ] && [ "$(copied_tags)" = "$pub_image:latest" ] && [ "$(grep -c 'already has the validated image index' "$work/pub.txt")" -ge 2 ] \
       && [ "$(tag_digest "$pub_image:0.1.1") $(tag_digest "$pub_image:0.1") $(tag_digest "$pub_image:latest")" = "$pub_ix $pub_ix $pub_ix" ]; }
expect_rc "publish: after a partial failure (latest's copy failed), a repetition keeps what is published and completes the rest" 0 $?
seed_v010; seed_image "$pub_image:race" 0.2.0; cp "$(tag_file "$pub_image:race")" "$work/race.json"; rm -f "$(tag_file "$pub_image:race")"
pub_call STUB_RACE_REF="$pub_image:latest" STUB_RACE_FROM="$work/race.json" 0.1.1 false $(refs_of 0.1.1)
{ [ "$pub_rc" -ne 0 ] && grep -q "^::error::Container image: $pub_image:latest changed after the preflight .*not overwritten" "$work/pub.txt" \
  && cmp -s "$(tag_file "$pub_image:latest")" "$work/race.json" && [ "$(copied_tags)" = "$pub_image:0.1.1 $pub_image:0.1" ]; }
expect_rc "publish: a floating tag that another publisher changed after the preflight is not overwritten" 0 $?

# The docker-publish steps of release.yml, run as the runner runs them, through a sequence of releases against one stub registry.
stable_publish() { # ARCHIVE VERSION [VAR=value...] -> the docker job's image check for VERSION, then docker-publish's three steps
  local archive="$1" version="$2" rc=0; shift 2
  check_image_step "$archive" "$version" > /dev/null || return 98
  rm -rf "$work/rt-pub" "$work/ws/image"; mkdir -p "$work/rt-pub" "$work/ws/image" "$work/reg"; cp "$archive" "$work/ws/image/container-image"
  : > "$work/rt-pub/env"; : > "$work/rt-pub/out"; : > "$work/skopeo.log"
  local common=(PATH="$work/bin-skopeo:$PATH" HOME="$work/home" RUNNER_TEMP="$work/rt-pub" GITHUB_ENV="$work/rt-pub/env" GITHUB_OUTPUT="$work/rt-pub/out"
    VERSION="$version" PRERELEASE="$(semver_prerelease "$version")" GITHUB_SHA="$oci_rev" GITHUB_REPOSITORY=example-owner/Campertron GITHUB_REPOSITORY_OWNER=Example-Owner
    STUB_REG="$work/reg" STUB_LOG="$work/skopeo.log" IMAGE_DIGEST="$(sed -n 's/^image_digest=//p' "$work/rt-img/out")"
    AMD64_MANIFEST="$(sed -n 's/^amd64_manifest=//p' "$work/rt-img/out")" ARM64_MANIFEST="$(sed -n 's/^arm64_manifest=//p' "$work/rt-img/out")"
    IMAGE_SHA256="$(sha256sum < "$archive" | cut -d' ' -f1)" "$@")
  run_step "Determine image name and tags" "${common[@]}" && run_step "Check the validated image before registry contact" "${common[@]}" \
    && run_step "Push the validated image" "${common[@]}" || rc=$?
  cat "$work/skopeo.log" >> "$work/skopeo-all.log"; return "$rc"; }
for v in 0.1.0 0.1.1 0.2.0-beta.1; do fx_archive "$work/fx-$v.tar" label_version="$v"; done
reset_registry; stable_publish "$work/fx-0.1.0.tar" 0.1.0; rc=$?; ix_010="$(sed -n 's/^image_digest=//p' "$work/rt-img/out")"
{ [ "$rc" -eq 0 ] && [ "$(copied_tags)" = "$pub_image:0.1.0 $pub_image:0.1 $pub_image:latest" ] && grep -qx "digest=$ix_010" "$work/rt-pub/out" \
  && [ "$(tag_digest "$pub_image:0.1.0") $(tag_digest "$pub_image:0.1") $(tag_digest "$pub_image:latest")" = "$ix_010 $ix_010 $ix_010" ]; }
expect_rc "workflow docker-publish: the first stable version creates its exact version, MAJOR.MINOR and latest, in that order" 0 $?
stable_publish "$work/fx-0.1.1.tar" 0.1.1; rc=$?; ix_011="$(sed -n 's/^image_digest=//p' "$work/rt-img/out")"
{ [ "$rc" -eq 0 ] && [ "$(copied_tags)" = "$pub_image:0.1.1 $pub_image:0.1 $pub_image:latest" ] && [ "$ix_011" != "$ix_010" ] \
  && grep -q "^Moved $pub_image:0.1 forward from version 0.1.0 ($ix_010) to 0.1.1 = $ix_011 " "$work/step.txt" && grep -q "^Moved $pub_image:latest forward from version 0.1.0 " "$work/step.txt" \
  && [ "$(tag_digest "$pub_image:0.1.0") $(tag_digest "$pub_image:0.1.1") $(tag_digest "$pub_image:0.1") $(tag_digest "$pub_image:latest")" = "$ix_010 $ix_011 $ix_011 $ix_011" ]; }
expect_rc "workflow docker-publish: the next stable version creates its exact version and moves 0.1 and latest forward; 0.1.0 is untouched" 0 $?
stable_publish "$work/fx-0.1.1.tar" 0.1.1; rc=$?
{ [ "$rc" -eq 0 ] && [ "$(copies)" = 0 ] && grep -qx "digest=$ix_011" "$work/rt-pub/out"; }
expect_rc "workflow docker-publish: a re-run for the same validated image copies nothing and succeeds" 0 $?
stable_publish "$work/fx-0.2.0-beta.1.tar" 0.2.0-beta.1; rc=$?
{ [ "$rc" -eq 0 ] && [ "$(copied_tags)" = "$pub_image:0.2.0-beta.1" ] && ! grep -qE "docker://$pub_image:(0\.1|0\.2|latest)\$" "$work/skopeo.log" \
  && [ "$(tag_digest "$pub_image:0.1") $(tag_digest "$pub_image:latest") $(tag_digest "$pub_image:0.2")" = "$ix_011 $ix_011 absent" ]; }
expect_rc "workflow docker-publish: a pre-release publishes only its exact version; 0.1 and latest are neither read nor moved" 0 $?
snap="$(reg_snapshot)"; stable_publish "$work/fx-0.1.0.tar" 0.1.0; rc=$?
{ [ "$rc" -ne 0 ] && grep -q "^::error::Container image: $pub_image:0.1 carries the newer version 0.1.1 .*refusing to move it backward to 0.1.0" "$work/step.txt" \
  && [ "$(copies)" = 0 ] && [ "$(reg_snapshot)" = "$snap" ]; }
expect_rc "workflow docker-publish: publishing an older stable version again is refused before anything is copied (0.1 would move backward)" 0 $?
# The next minor version (v0.2.0 after v0.1.1): MAJOR.MINOR is new, latest moves forward across the minor version, 0.1 stays.
fx_archive "$work/fx-0.2.0.tar" label_version=0.2.0; fx_archive "$work/fx-0.1.2.tar" label_version=0.1.2; ix_beta="$(tag_digest "$pub_image:0.2.0-beta.1")"
stable_publish "$work/fx-0.2.0.tar" 0.2.0; rc=$?; ix_020="$(sed -n 's/^image_digest=//p' "$work/rt-img/out")"
{ [ "$rc" -eq 0 ] && [ "$(copied_tags)" = "$pub_image:0.2.0 $pub_image:0.2 $pub_image:latest" ] && [ "$ix_020" != "$ix_011" ] \
  && grep -q "^Published $pub_image:0.2 = $ix_020 " "$work/step.txt" && grep -q "^Moved $pub_image:latest forward from version 0.1.1 ($ix_011) to 0.2.0 = $ix_020 " "$work/step.txt" \
  && [ "$(tag_digest "$pub_image:0.1.0") $(tag_digest "$pub_image:0.1.1") $(tag_digest "$pub_image:0.1") $(tag_digest "$pub_image:0.2.0-beta.1")" = "$ix_010 $ix_011 $ix_011 $ix_beta" ] \
  && [ "$(tag_digest "$pub_image:0.2.0") $(tag_digest "$pub_image:0.2") $(tag_digest "$pub_image:latest")" = "$ix_020 $ix_020 $ix_020" ]; }
expect_rc "workflow docker-publish: the next minor version (0.2.0 after 0.1.1) creates 0.2.0 and 0.2 and moves latest forward from 0.1.1; 0.1, 0.1.0, 0.1.1 and the pre-release are untouched" 0 $?
snap="$(reg_snapshot)"; stable_publish "$work/fx-0.1.2.tar" 0.1.2; rc=$?
{ [ "$rc" -ne 0 ] && grep -q "^::error::Container image: $pub_image:latest carries the newer version 0.2.0 .*refusing to move it backward to 0.1.2" "$work/step.txt" \
  && [ "$(copies)" = 0 ] && [ "$(reg_snapshot)" = "$snap" ]; }
expect_rc "workflow docker-publish: after 0.2.0, an older-minor stable version (0.1.2) is refused before anything is copied (latest would move backward)" 0 $?
{ grep -o 'docker://[^ ]*' "$work/skopeo-all.log" | grep -vqE "^docker://$pub_image[:@]"; [ $? -ne 0 ] && [ -s "$work/skopeo-all.log" ] && ! grep -qi 'shaunanthonyhathaway' "$work/skopeo-all.log"; }
expect_rc "publish tests: every registry request went to the stub registry's example image, none to a real registry" 0 $?

# --- release.yml: the arm64 container job's steps (S2), executed like the runner with docker and skopeo stubs
mkdir -p "$work/bin-arm"
cat > "$work/bin-arm/docker" <<'STUB'
#!/usr/bin/env bash
# docker stub for the linux/arm64 container job: the loaded image's architecture, layers, saved config and its runs
echo "docker $*" >> "$STUB_LOG"
image=campertron-smoke-arm64:local
case "$*" in
  "image inspect $image --format {{.Architecture}}") echo "${STUB_ARCH:-arm64}" ;;
  "image inspect $image --format {{json .RootFS.Layers}}") echo "$STUB_LAYERS" ;;
  "save $image --output "*) d="$(mktemp -d)"; printf '[{"Config":"blobs/sha256/%s","RepoTags":["%s"],"Layers":[]}]' "${STUB_CONFIG#sha256:}" "$image" > "$d/manifest.json"
                           tar -C "$d" -cf "${@: -1}" manifest.json; rm -rf "$d" ;;
  "run --rm $image") echo "Error: Config path does not exist" >&2; exit "${STUB_NOCONFIG_EXIT:-1}" ;;
  "run --rm --volume "*" $image") config="${4%%:*}"; truncate -s "${STUB_DB_SIZE:-226471936}" "$config/RecreationDotOrg.db" ;;
  "image inspect $image --format {{ index .Config.Labels \"org.opencontainers.image.version\" }}") echo "${STUB_LABEL:-0.1.0-beta.10}" ;;
  "image inspect $image --format {{ range .Config.Env }}{{ println . }}{{ end }}") printf 'PATH=/usr/bin\nDOTNET_VERSION=11.0.0\n\n' ;;
  "run --rm $image --version") printf 'CampertronConsole 0.1.0-beta.10+abc\nFramework => .NET 11.0.0 (%s)\n' "${STUB_PROCESS:-Arm64}" ;;
  "run --rm --entrypoint uname $image -m") echo "${STUB_MACHINE:-aarch64}" ;;
  *) echo "docker stub: unexpected call: docker $*" >&2; exit 99 ;;
esac
STUB
cat > "$work/bin-arm/skopeo" <<'STUB'
#!/usr/bin/env bash
echo "skopeo $*" >> "$STUB_LOG"
case "$1" in --version) echo "skopeo version 0.0.0-stub" ;; copy) ;; *) echo "skopeo stub: unexpected call: skopeo $*" >&2; exit 99 ;; esac
STUB
chmod +x "$work/bin-arm/docker" "$work/bin-arm/skopeo"
fx_archive "$work/fx-good.tar" > /dev/null; check_image_step "$work/fx-good.tar" > /dev/null; good_ix="$(sed -n 's/^image_digest=//p' "$work/rt-img/out")"
arm_m="$(sed -n 's/^arm64_manifest=//p' "$work/rt-img/out")"; arm_c="$(sed -n 's/^arm64_config=//p' "$work/rt-img/out")"; amd_c="$(sed -n 's/^amd64_config=//p' "$work/rt-img/out")"
arm_layers="$(jq -c .rootfs.diff_ids "$work/rt-img/container-image.oci/blobs/sha256/${arm_c#sha256:}")"
arm_steps() { # [VAR=value...] -> the arm64 container job's check, load, correlation and run steps against the stubs
  rm -rf "$work/rt-arm" "$work/ws/image"; mkdir -p "$work/rt-arm" "$work/ws/image"; cp "$work/fx-good.tar" "$work/ws/image/container-image"; : > "$work/arm.log"
  local common=(PATH="$work/bin-arm:$PATH" RUNNER_TEMP="$work/rt-arm" VERSION="$oci_ver" GITHUB_SHA="$oci_rev" GITHUB_REPOSITORY=example-owner/Campertron
    IMAGE_DIGEST="$good_ix" ARM64_MANIFEST="$arm_m" ARM64_CONFIG="$arm_c" IMAGE_SHA256="$(sha256sum < "$work/fx-good.tar" | cut -d' ' -f1)"
    STUB_LOG="$work/arm.log" STUB_LAYERS="$arm_layers" STUB_CONFIG="$arm_c" "$@")
  run_step "Check the validated image before the arm64 run" "${common[@]}" || return 1
  run_step "Load the linux/arm64 image from the validated archive" "${common[@]}" || return 1
  run_step "Check the loaded image is the validated arm64 image" "${common[@]}" || return 1
  run_step "Container smoke tests (linux/arm64)" "${common[@]}"; }
arm_steps; rc=$?
{ [ "$rc" -eq 0 ] && grep -qx 'skopeo copy --override-os linux --override-arch arm64 oci-archive:image/container-image docker-daemon:campertron-smoke-arm64:local' "$work/arm.log" \
  && ! grep -qE 'docker://|docker (login|push|pull|build|buildx)' "$work/arm.log" && grep -q '^Container machine: aarch64$' "$work/step.txt"; }
expect_rc "workflow arm64 container job: the validated archive's arm64 image is loaded, identified and run, without a registry" 0 $?
arm_steps IMAGE_SHA256="$(printf 'f%.0s' {1..64})"
{ [ $? -ne 0 ] && grep -q '^::error::Downloaded image archive has SHA-256' "$work/step.txt" && [ ! -s "$work/arm.log" ]; }
expect_rc "workflow arm64 container job: another archive fails before anything is loaded" 0 $?
arm_steps ARM64_CONFIG="$amd_c"
{ [ $? -ne 0 ] && grep -q "^::error::Downloaded image does not match the docker job's validated arm64_config" "$work/step.txt" && [ ! -s "$work/arm.log" ]; }
expect_rc "workflow arm64 container job: a config other than the docker job's fails before anything is loaded" 0 $?
arm_steps STUB_CONFIG="$amd_c"
{ [ $? -ne 0 ] && grep -q '^::error::Container image: the loaded linux/arm64 image has the config' "$work/step.txt" && ! grep -q '^docker run' "$work/arm.log"; }
expect_rc "workflow arm64 container job: a loaded image with another config is not run" 0 $?
arm_steps STUB_ARCH=amd64
{ [ $? -ne 0 ] && grep -q '^::error::Container image: the loaded image is a amd64 image, expected arm64' "$work/step.txt" && ! grep -q '^docker run' "$work/arm.log"; }
expect_rc "workflow arm64 container job: a loaded amd64 image is not run" 0 $?
arm_steps STUB_LAYERS='["sha256:0"]'; expect_rc "workflow arm64 container job: a loaded image with other layers fails" nonzero $?
arm_steps STUB_PROCESS=X64
{ [ $? -ne 0 ] && grep -q '^::error::The containerized console is not an Arm64 process' "$work/step.txt"; }
expect_rc "workflow arm64 container job: an x64 process in the container fails" 0 $?
arm_steps STUB_MACHINE=x86_64; expect_rc "workflow arm64 container job: a container that is not aarch64 fails" nonzero $?
arm_steps STUB_NOCONFIG_EXIT=0; expect_rc "workflow arm64 container job: a start without /config that succeeds fails" nonzero $?
arm_steps STUB_DB_SIZE=1000; expect_rc "workflow arm64 container job: a database that was not extracted fails" nonzero $?
arm_steps STUB_LABEL=9.9.9; expect_rc "workflow arm64 container job: another version label fails" nonzero $?

# --- release.yml: the vulnerability scan fails when it finds no project (as ci.yml's does)
mkdir -p "$work/bin-scan"
cat > "$work/bin-scan/dotnet" <<'STUB'
#!/usr/bin/env bash
case "$1 $2" in
  "sln Campertron.sln") printf '%s\n' "Project(s)" "----------" $STUB_PROJECTS ;;
  "list "*) extra=""; [ -z "${STUB_VULNERABLE:-}" ] || extra=',"topLevelPackages":[{"id":"Bad"}]'
            printf '{"version":1,"projects":[{"path":"%s","frameworks":[{"framework":"net11.0"%s}]}]}\n' "$2" "$extra" ;;
  *) echo "dotnet stub: unexpected call: dotnet $*" >&2; exit 99 ;;
esac
STUB
chmod +x "$work/bin-scan/dotnet"
run_step "Vulnerability scan" PATH="$work/bin-scan:$PATH" STUB_PROJECTS="A/A.csproj B/B.csproj docker-compose.dcproj"; rc=$?
{ [ "$rc" -eq 0 ] && [ "$(grep -c '^Vulnerable packages in .*: 0$' "$work/step.txt")" = 2 ]; }
expect_rc "workflow vulnerability scan: every .csproj of the solution is scanned and a clean result passes" 0 $?
run_step "Vulnerability scan" PATH="$work/bin-scan:$PATH" STUB_PROJECTS=""
{ [ $? -ne 0 ] && grep -q '^::error::No .csproj projects found in Campertron.sln' "$work/step.txt"; }
expect_rc "workflow vulnerability scan: no project found fails instead of passing with a count of 0" 0 $?
run_step "Vulnerability scan" PATH="$work/bin-scan:$PATH" STUB_PROJECTS="A/A.csproj" STUB_VULNERABLE=1
{ [ $? -ne 0 ] && grep -q '^::error::1 vulnerable package(s) found' "$work/step.txt"; }
expect_rc "workflow vulnerability scan: a vulnerable package fails" 0 $?

# --- release.yml job structure for the container image (static: job-level YAML cannot be executed here)
workflow_job() { awk -v id="$1" '$0 == "  " id ":" {f = 1; print; next} f && /^  [A-Za-z0-9_-]+:$/ {exit} f {print}' "$here_root/.github/workflows/release.yml"; }
pub_job="$(workflow_job docker-publish)"; docker_job="$(workflow_job docker)"
publish_if="if: needs.verify.outputs.publish == 'true' && github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v')"
{ [ -n "$pub_job" ] && ! grep -qE 'buildx (build|create|bake)|docker (build|push)|--push' <<< "$pub_job"; }
expect_rc "workflow structure: docker-publish builds no image (no rebuild)" 0 $?
{ grep -qxF "    needs: [verify, binaries, smoke, native-smoke, emulated-smoke, docker, container-smoke, assemble]" <<< "$pub_job" && grep -qxF "    $publish_if" <<< "$pub_job"; }
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
{ grep -qxF "    needs: [verify, binaries, smoke, native-smoke, emulated-smoke, docker, container-smoke, assemble]" <<< "$pub_job" && grep -qxF "    $publish_if" <<< "$(workflow_job release)" \
  && grep -qxF "    needs: [verify, binaries, smoke, native-smoke, emulated-smoke, docker, container-smoke, assemble, docker-publish]" <<< "$(workflow_job release)"; }
expect_rc "workflow structure: the image is published only after every validation job, before the GitHub release" 0 $?
arm_job="$(workflow_job container-smoke)"
{ grep -qxF '    needs: [verify, docker]' <<< "$arm_job" && grep -qxF '    runs-on: ubuntu-24.04-arm' <<< "$arm_job" && grep -qxF '          name: container-image' <<< "$arm_job" \
  && grep -qxF '      ARM64_CONFIG: ${{ needs.docker.outputs.arm64_config }}' <<< "$arm_job" && grep -qxF '      arm64_config: ${{ steps.image.outputs.arm64_config }}' <<< "$docker_job"; }
expect_rc "workflow structure: the arm64 container job runs the docker job's archive on the pinned standard arm64 runner" 0 $?
{ [ -n "$arm_job" ] && ! grep -qE '^    permissions:|: write|docker://|docker login|skopeo login|buildx|docker (build|push|pull)|--push|setup-dotnet' <<< "$arm_job"; }
expect_rc "workflow structure: the arm64 container job builds nothing, has no write permission and contacts no registry" 0 $?

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
printf '{"runtimeOptions":{"tfm":"net11.0","frameworks":%s}}\n' "$web_frameworks" > "$work/fd.json"
check_framework_dependent_runtimeconfig "$work/fd.json" > "$work/out.txt"; expect_rc "image runtimeconfig with the ASP.NET Core 11 framework passes" 0 $?
printf '{"runtimeOptions":{"tfm":"net11.0","frameworks":%s}}\n' "${web_frameworks/AspNetCore.App\",\"version\":\"11.0.0/AspNetCore.App\",\"version\":\"10.0.15}" > "$work/fd.json"
check_framework_dependent_runtimeconfig "$work/fd.json" > "$work/out.txt"; expect_rc "image runtimeconfig with ASP.NET Core 10 rejected" nonzero $?
printf '{"runtimeOptions":{"tfm":"net11.0","frameworks":%s}}\n' "${web_frameworks/AspNetCore/WindowsDesktop}" > "$work/fd.json"
check_framework_dependent_runtimeconfig "$work/fd.json" > "$work/out.txt"; expect_rc "image runtimeconfig with another framework rejected" nonzero $?

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

# --- GUI smoke test (R8): CampertronGUI --smoke-test reports, their validation and the smoke steps of release.yml
smoke_report() { # [config-path] -> a passing report for version 0.1.0-beta.11
  printf '%s\n' "SMOKE startup=CampertronGUI 0.1.0-beta.11+0123abc" "SMOKE window-opened=yes" "SMOKE native-handle=XID:0x40000f" \
    "SMOKE ready=yes" "SMOKE config-path=${1:-/data/CampertronConfig}" "SMOKE rendered-frame=yes" \
    "SMOKE navigation=start=Monitoring/Guided setup tabs=Searches,Results,Monitoring,Settings,Campgrounds monitoring=Status,Guided setup settings=Campertron,System campsites=By Park,By City,By State" \
    "SMOKE parks=463" "SMOKE states=51" "SMOKE window-size=1024x768" "SMOKE render-scaling=1.25" "SMOKE theme=Dark" \
    "SMOKE theme-probe=pass default=System saved=Dark reloaded=Dark kept-keys=yes malformed=System unknown=System applied=Light,Dark,System" \
    "SMOKE settings-probe=pass general=saved email=created,saved ntfy=created,saved backups=exact line-endings=crlf bom=kept comments=kept permissions=640,600" \
    "SMOKE settings-tab=yes general=editable email=editable ntfy=new web=new" "SMOKE system-tab=yes choices=System,Light,Dark selected=System preference=missing configuration=8 paths=startup copy=bound open-folder=bound actions=idle campground-data=shown last-refreshed=shown refresh=bound confirmation=ready refresh-state=idle restart=apphost" \
    "SMOKE search-probe=pass created=disabled auto-run=toggled-alone lists=kept comments=kept line-endings=crlf bom=kept save-as=created,no-overwrite campground-id=exact permissions=640" \
    "SMOKE searches-tab=yes files=0 campgrounds=4966" \
    "SMOKE filter-probe=pass campground=232445 campsites=192 equipment=11 types=5 names=192 attributes=270 thread=worker index=yes selector=toggled" \
    "SMOKE results-tab=yes files=0 paths=startup start=bound cancel=bound" \
    "SMOKE run-probe=pass planned=1 completed=openings:20 results=bound canceled=prompt blocked=no-exit cooldown=on network=none" \
    "SMOKE monitoring-tab=yes state=idle generation=0 start=bound stop=unavailable restart=unavailable timer=on" \
    "SMOKE guided-setup=yes led=yes step=method state=unconfigured methods=email,ntfy continue=bound" "SMOKE close-requested=yes" \
    "SMOKE closing=programmatic=yes reason=WindowClosing" "SMOKE shutdown=clean exit-code=0" "SMOKE result=pass"
}
smoke_case() { # description expected-rc sed-expression [expected-path] [version]
  smoke_report > "$work/smoke.out"; [ -n "$3" ] && sed -i "$3" "$work/smoke.out"
  check_gui_smoke_report "$work/smoke.out" "${4:-/data/CampertronConfig}" "${5-0.1.0-beta.11}" > "$work/out.txt"; expect_rc "GUI smoke report: $1" "$2" $?
}
smoke_case "a passing report is accepted"                          0       ''
smoke_case "Windows line endings are accepted"                     0       's/$/\r/'
smoke_case "accepted without a version check"                      0       '' "" ""
smoke_case "a release version without a commit is accepted"        0       's/beta.11+0123abc/beta.11/'
smoke_case "a missing step is rejected"                            nonzero '/rendered-frame=/d'
smoke_case "a repeated step is rejected"                           nonzero '/^SMOKE parks=/p'
smoke_case "steps out of order are rejected"                       nonzero '/^SMOKE parks=/{h;d};/^SMOKE states=/G'
smoke_case "a line that is not a SMOKE line is rejected"           nonzero '$a Unhandled exception. System.Exception: boom'
smoke_case "a zero native handle is rejected"                      nonzero 's/native-handle=XID:0x40000f/native-handle=XID:0x0/'
smoke_case "a handle without a value is rejected"                  nonzero 's/native-handle=XID:0x40000f/native-handle=XID:/'
smoke_case "no parks is rejected"                                  nonzero 's/parks=463/parks=0/'
smoke_case "no states is rejected"                                 nonzero 's/states=51/states=/'
smoke_case "no rendered frame is rejected"                         nonzero 's/rendered-frame=yes/rendered-frame=no/'
smoke_case "another data folder is rejected"                       nonzero '' "/other/CampertronConfig"
smoke_case "another version is rejected"                           nonzero '' "/data/CampertronConfig" "0.1.0-beta.12"
smoke_case "a close that was not programmatic is rejected"         nonzero 's/programmatic=yes/programmatic=no/'
smoke_case "an unclean shutdown is rejected"                       nonzero 's/exit-code=0/exit-code=1/'
smoke_case "a failed result is rejected"                           nonzero 's/result=pass/result=fail/'
smoke_case "a zero render scaling is rejected"                     nonzero 's/render-scaling=1.25/render-scaling=0/'
smoke_case "an unknown theme is rejected"                          nonzero 's/theme=Dark/theme=HighContrast/'
smoke_case "Windows access rules in the settings probe are accepted" 0     's/permissions=640,600/permissions=access-rules-kept/'
smoke_case "a failed settings probe is rejected"                   nonzero 's/settings-probe=pass .*/settings-probe=fail General.yaml could not be saved/'
smoke_case "a settings probe without a check is rejected"          nonzero 's/ bom=kept//'
smoke_case "other permissions in the settings probe are rejected"  nonzero 's/permissions=640,600/permissions=644,644/'
smoke_case "a missing settings probe is rejected"                  nonzero '/settings-probe=/d'
smoke_case "a settings file that is not editable is rejected"      nonzero 's/email=editable/email=not-editable/'
smoke_case "a Settings tab that was not shown is rejected"         nonzero 's/settings-tab=.*/settings-tab=no/'
smoke_case "a settings probe without Ntfy.yaml is rejected"       nonzero 's/ ntfy=created,saved//'
smoke_case "a settings probe that did not create Ntfy.yaml is rejected" nonzero 's/ntfy=created,saved/ntfy=saved/'
smoke_case "a Settings tab without the Ntfy.yaml editor is rejected" nonzero 's/ ntfy=new//'
smoke_case "a Settings tab whose clean start had a Ntfy.yaml is rejected" nonzero 's/ntfy=new/ntfy=editable/'
smoke_case "a Settings tab without the Web.yaml editor is rejected" nonzero 's/ web=new//'
smoke_case "a Settings tab whose clean start had a Web.yaml is rejected" nonzero 's/web=new/web=editable/'
smoke_case "Windows access rules in the search probe are accepted" 0       's/campground-id=exact permissions=640/campground-id=exact permissions=access-rules-kept/'
smoke_case "a failed search probe is rejected"                     nonzero 's/search-probe=pass .*/search-probe=fail Save As did not write the new file alone, or wrote over a file/'
smoke_case "a search probe without a check is rejected"            nonzero 's/ save-as=created,no-overwrite//'
smoke_case "other permissions in the search probe are rejected"    nonzero 's/campground-id=exact permissions=640/campground-id=exact permissions=644/'
smoke_case "a missing search probe is rejected"                    nonzero '/search-probe=/d'
smoke_case "a Searches tab that was not shown is rejected"         nonzero 's/searches-tab=.*/searches-tab=no/'
smoke_case "a Searches tab without campgrounds is rejected"        nonzero 's/campgrounds=4966/campgrounds=0/'
smoke_case "a Searches tab after the close is rejected"            nonzero '/^SMOKE searches-tab=/{h;d};/^SMOKE close-requested=/G'
smoke_case "a missing filter probe is rejected"                    nonzero '/filter-probe=/d'
smoke_case "a failed filter probe is rejected"                     nonzero 's/filter-probe=pass .*/filter-probe=fail the campground data could not be read/'
smoke_case "a filter probe read on the UI thread is rejected"      nonzero 's/thread=worker/thread=ui/'
smoke_case "a filter probe without the index is rejected"          nonzero 's/index=yes/index=no/'
smoke_case "a filter probe whose selector did not toggle is rejected" nonzero 's/selector=toggled/selector=unchanged/'
smoke_case "a filter probe without campsites is rejected"          nonzero 's/campsites=192/campsites=0/'
smoke_case "a filter probe before the Searches tab is rejected"    nonzero '/^SMOKE filter-probe=/{h;d};/^SMOKE search-probe=/G'
smoke_case "a filter probe without a check is rejected"            nonzero 's/ thread=worker//'
smoke_case "a missing Results tab is rejected"                     nonzero '/results-tab=/d'
smoke_case "a Results tab that was not shown is rejected"          nonzero 's/results-tab=.*/results-tab=no/'
smoke_case "a Results tab with other folders is rejected"          nonzero 's/paths=startup/paths=other/'
smoke_case "a Results tab without a bound Cancel is rejected"      nonzero 's/cancel=bound/cancel=unbound/'
smoke_case "a missing run probe is rejected"                       nonzero '/run-probe=/d'
smoke_case "a failed run probe is rejected"                        nonzero 's/run-probe=pass .*/run-probe=fail the fixture run did not complete/'
smoke_case "a run probe without openings is rejected"              nonzero 's/openings:20/openings:0/'
smoke_case "a run probe without a prompt cancel is rejected"       nonzero 's/ canceled=prompt//'
smoke_case "a run probe whose block ended the app is rejected"     nonzero 's/blocked=no-exit/blocked=exit/'
smoke_case "a run probe that reached the network is rejected"      nonzero 's/network=none/network=used/'
smoke_case "a run probe before the Results tab is rejected"        nonzero '/^SMOKE results-tab=/{h;d};/^SMOKE run-probe=/G'
smoke_case "a missing Monitoring tab is rejected"                  nonzero '/^SMOKE monitoring-tab=/d'
smoke_case "a Monitoring tab that was not shown is rejected"       nonzero 's/monitoring-tab=.*/monitoring-tab=no/'
smoke_case "a monitoring the start started is rejected"            nonzero 's/state=idle generation=0/state=active generation=1/'
smoke_case "a monitoring started before is rejected"               nonzero 's/generation=0 start=bound/generation=1 start=bound/'
smoke_case "a Monitoring tab without a bound Start is rejected"    nonzero 's/start=bound stop=unavailable/start=unbound stop=unavailable/'
smoke_case "a Monitoring tab whose Stop is available is rejected"  nonzero 's/stop=unavailable/stop=bound/'
smoke_case "a Monitoring tab without a bound Restart is rejected"  nonzero 's/restart=unavailable timer/restart=unbound timer/'
smoke_case "a Monitoring tab that reads nothing while shown is rejected" nonzero 's/timer=on/timer=off/'
smoke_case "a Monitoring tab before the run probe is rejected"     nonzero '/^SMOKE run-probe=/{h;d};/^SMOKE monitoring-tab=/G'
smoke_case "a missing navigation step is rejected"                  nonzero '/^SMOKE navigation=/d'
smoke_case "another top-level tab order is rejected"               nonzero 's/tabs=Searches,Results,Monitoring,Settings,Campgrounds/tabs=Campgrounds,Searches,Results,Monitoring,Settings/'
smoke_case "the six former top-level tabs are rejected"            nonzero 's/tabs=Searches,Results,Monitoring,Settings,Campgrounds/tabs=By Park,By City,By State,Settings,Searches,Results/'
smoke_case "the lookup tab's label before D55 is rejected"         nonzero 's/tabs=Searches,Results,Monitoring,Settings,Campgrounds/tabs=Searches,Results,Monitoring,Settings,Campsites/'
smoke_case "the four top-level tabs before the Monitoring tab are rejected" nonzero 's/tabs=Searches,Results,Monitoring,Settings,Campgrounds/tabs=Searches,Results,Settings,Campgrounds/'
smoke_case "the Monitoring tab after Settings is rejected"         nonzero 's/tabs=Searches,Results,Monitoring,Settings,Campgrounds/tabs=Searches,Results,Settings,Monitoring,Campgrounds/'
smoke_case "a Monitoring tab without its Guided setup row is rejected" nonzero 's/ monitoring=Status,Guided setup / /'
smoke_case "a Monitoring row in another order is rejected"         nonzero 's/monitoring=Status,Guided setup/monitoring=Guided setup,Status/'
smoke_case "a Monitoring row without Status is rejected"           nonzero 's/monitoring=Status,Guided setup/monitoring=Guided setup/'
smoke_case "another tab shown at the start is rejected"            nonzero 's/start=Monitoring\/Guided setup/start=Campgrounds/'
smoke_case "Searches at the start of a clean data folder is rejected (no lead)" nonzero 's/start=Monitoring\/Guided setup/start=Searches/'
smoke_case "the monitoring's Status at the start is rejected"      nonzero 's/start=Monitoring\/Guided setup/start=Monitoring\/Status/'
smoke_case "a missing guided setup is rejected"                    nonzero '/^SMOKE guided-setup=/d'
smoke_case "a guided setup that was not shown is rejected"         nonzero 's/guided-setup=.*/guided-setup=no/'
smoke_case "a clean data folder without the lead is rejected"      nonzero 's/guided-setup=yes led=yes/guided-setup=yes led=no/'
smoke_case "a guided setup at another step is rejected"            nonzero 's/step=method state/step=notifications state/'
smoke_case "a clean data folder that is not unconfigured is rejected" nonzero 's/state=unconfigured/state=partial/'
smoke_case "a guided setup without both methods is rejected"       nonzero 's/methods=email,ntfy/methods=missing/'
smoke_case "a guided setup without a bound Continue is rejected"   nonzero 's/continue=bound/continue=unbound/'
smoke_case "a guided setup before the Monitoring tab is rejected"  nonzero '/^SMOKE monitoring-tab=/{h;d};/^SMOKE guided-setup=/G'
smoke_case "another Campgrounds row is rejected"                   nonzero 's/campsites=By Park,By City,By State/campsites=By City,By Park,By State/'
smoke_case "a Settings row without System is rejected"             nonzero 's/settings=Campertron,System/settings=Campertron/'
smoke_case "a navigation step after the parks is rejected"         nonzero '/^SMOKE navigation=/{h;d};/^SMOKE parks=/G'
smoke_case "a missing theme probe is rejected"                     nonzero '/^SMOKE theme-probe=/d'
smoke_case "a failed theme probe is rejected"                      nonzero 's/theme-probe=pass .*/theme-probe=fail a malformed preferences file did not give System/'
smoke_case "a theme probe whose default is not System is rejected" nonzero 's/default=System saved/default=Light saved/'
smoke_case "a theme probe without the fallback is rejected"        nonzero 's/ malformed=System//'
smoke_case "a theme probe that applied no Dark is rejected"        nonzero 's/applied=Light,Dark,System/applied=Light,System/'
smoke_case "a missing System tab is rejected"                      nonzero '/^SMOKE system-tab=/d'
smoke_case "a System tab that was not shown is rejected"           nonzero 's/system-tab=.*/system-tab=no/'
smoke_case "a System tab with other choices is rejected"           nonzero 's/choices=System,Light,Dark/choices=Light,Dark/'
smoke_case "a clean start that is not System is rejected"          nonzero 's/selected=System preference=missing/selected=Dark preference=saved/'
smoke_case "a clean start with a preferences file is rejected"     nonzero 's/preference=missing/preference=malformed/'
smoke_case "a System tab before the Settings tab is rejected"      nonzero '/^SMOKE settings-tab=/{h;d};/^SMOKE system-tab=/G'
smoke_case "a System tab without the current configuration is rejected" nonzero 's/ configuration=8 paths=startup copy=bound open-folder=bound actions=idle//'
smoke_case "a current configuration of other items is rejected"   nonzero 's/configuration=8/configuration=7/'
smoke_case "a current configuration of other folders is rejected" nonzero 's/paths=startup copy/paths=other copy/'
smoke_case "a current configuration without a bound Copy is rejected" nonzero 's/copy=bound/copy=unbound/'
smoke_case "a current configuration without Open settings folder is rejected" nonzero 's/open-folder=bound/open-folder=unbound/'
smoke_case "a current configuration whose action ran is rejected" nonzero 's/actions=idle/actions=ran/'
smoke_case "a System tab without the campground data is rejected" nonzero 's/ campground-data=shown last-refreshed=shown refresh=bound confirmation=ready refresh-state=idle restart=apphost//'
smoke_case "a campground data card that was not shown is rejected" nonzero 's/campground-data=shown/campground-data=missing/'
smoke_case "a campground data card without its last refresh is rejected" nonzero 's/last-refreshed=shown/last-refreshed=missing/'
smoke_case "a campground data card without a bound Refresh is rejected" nonzero 's/refresh=bound/refresh=unbound/'
smoke_case "a campground data card without its confirmation is rejected" nonzero 's/confirmation=ready/confirmation=missing/'
smoke_case "a campground data card whose refresh started is rejected" nonzero 's/refresh-state=idle/refresh-state=started/'
smoke_case "a campground data card before the current configuration is rejected" nonzero 's/ actions=idle campground-data=shown/ campground-data=shown actions=idle/'
smoke_case "a System tab without the automatic restart's launcher is rejected" nonzero 's/ restart=apphost//'
smoke_case "an automatic restart through the dotnet host is rejected" nonzero 's/restart=apphost/restart=dotnet/'
smoke_case "an automatic restart without a launcher is rejected" nonzero 's/restart=apphost/restart=unavailable/'
smoke_case "an automatic restart without the window's capture is rejected" nonzero 's/restart=apphost/restart=unbound/'
smoke_report 'C:\Users\runneradmin\AppData\Local\CampertronConfig' > "$work/smoke.out"
check_gui_smoke_report "$work/smoke.out" 'C:\Users\runneradmin\AppData\Local\CampertronConfig' 0.1.0-beta.11 > "$work/out.txt"; expect_rc "GUI smoke report: a Windows data folder is compared exactly" 0 $?
: > "$work/empty.out"; check_gui_smoke_report "$work/empty.out" /data/CampertronConfig > "$work/out.txt"; expect_rc "GUI smoke report: an empty report is rejected" nonzero $?
check_gui_smoke_report "$work/missing.out" /data/CampertronConfig > "$work/out.txt"; expect_rc "GUI smoke report: a missing report is rejected" nonzero $?
smoke_report > "$work/smoke.out"; : > "$work/smoke.err"
check_gui_smoke_run 0 "$work/smoke.out" "$work/smoke.err" /data/CampertronConfig 0.1.0-beta.11 > "$work/out.txt"; expect_rc "GUI smoke run: exit 0, no stderr and a valid report pass" 0 $?
check_gui_smoke_run 2 "$work/smoke.out" "$work/smoke.err" /data/CampertronConfig > "$work/out.txt"; expect_rc "GUI smoke run: a non-zero exit code is rejected" nonzero $?
echo "CampertronGUI smoke test failed: timed out" > "$work/smoke.err"
check_gui_smoke_run 0 "$work/smoke.out" "$work/smoke.err" /data/CampertronConfig > "$work/out.txt"; expect_rc "GUI smoke run: output on standard error is rejected" nonzero $?
grep -q '^::error::GUI smoke test wrote to standard error' "$work/out.txt"; expect_rc "GUI smoke run: the stderr failure is reported as an ::error:: line" 0 $?

# release.yml: the smoke steps run verbatim against a stub GUI (no display, no .NET).
stub_gui() { # path -> a stub CampertronGUI: --version, or --smoke-test that extracts a sparse "database" and reports
  mkdir -p "$(dirname "$1")"
  cat > "$1" <<'STUB'
#!/usr/bin/env bash
if [ "$1" = "--version" ]; then printf 'CampertronGUI 0.1.0-beta.11+0123abc\nFramework => .NET 11.0.0 (%s)\n' "${STUB_ARCH:-X64}"; exit 0; fi
config="${STUB_CONFIG:-$XDG_DATA_HOME/CampertronConfig}"
mkdir -p "$config"; truncate -s 226471936 "$config/RecreationDotOrg.db"
printf '%s\n' "SMOKE startup=CampertronGUI 0.1.0-beta.11+0123abc" "SMOKE window-opened=yes" "SMOKE native-handle=XID:0x40000f" \
  "SMOKE ready=yes" "SMOKE config-path=${STUB_REPORTED_CONFIG:-$config}" "SMOKE rendered-frame=yes" \
  "SMOKE navigation=start=Monitoring/Guided setup tabs=Searches,Results,Monitoring,Settings,Campgrounds monitoring=Status,Guided setup settings=Campertron,System campsites=By Park,By City,By State" \
  "SMOKE parks=463" "SMOKE states=51" "SMOKE window-size=1024x768" "SMOKE render-scaling=1" "SMOKE theme=Light" \
  "SMOKE theme-probe=pass default=System saved=Dark reloaded=Dark kept-keys=yes malformed=System unknown=System applied=Light,Dark,System" \
  "SMOKE settings-probe=pass general=saved email=created,saved ntfy=created,saved backups=exact line-endings=crlf bom=kept comments=kept permissions=640,600" \
  "SMOKE settings-tab=yes general=editable email=editable ntfy=new web=new" "SMOKE system-tab=yes choices=System,Light,Dark selected=System preference=missing configuration=8 paths=startup copy=bound open-folder=bound actions=idle campground-data=shown last-refreshed=shown refresh=bound confirmation=ready refresh-state=idle restart=apphost" \
  "SMOKE search-probe=pass created=disabled auto-run=toggled-alone lists=kept comments=kept line-endings=crlf bom=kept save-as=created,no-overwrite campground-id=exact permissions=640" \
  "SMOKE searches-tab=yes files=0 campgrounds=4966" \
    "SMOKE filter-probe=pass campground=232445 campsites=192 equipment=11 types=5 names=192 attributes=270 thread=worker index=yes selector=toggled" \
    "SMOKE results-tab=yes files=0 paths=startup start=bound cancel=bound" \
  "SMOKE run-probe=pass planned=1 completed=openings:20 results=bound canceled=prompt blocked=no-exit cooldown=on network=none" \
  "SMOKE monitoring-tab=yes state=idle generation=0 start=bound stop=unavailable restart=unavailable timer=on" \
    "SMOKE guided-setup=yes led=yes step=method state=unconfigured methods=email,ntfy continue=bound" "SMOKE close-requested=yes" \
  "SMOKE closing=programmatic=yes reason=WindowClosing" "SMOKE shutdown=clean exit-code=0" "SMOKE result=pass"
[ -n "${STUB_STDERR:-}" ] && echo "$STUB_STDERR" >&2
exit "${STUB_EXIT:-0}"
STUB
  chmod +x "$1"
  printf '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"11.0.0"}]}}\n' > "$(dirname "$1")/CampertronGUI.runtimeconfig.json"
}
native_gui() { # [VAR=value...] -> runs the native GUI step for osx-arm64 against the stub
  rm -rf "$work/rt-nat"; mkdir -p "$work/rt-nat"
  stub_gui "$work/rt-nat/apps/campertron-gui-0.1.0-beta.11-osx-arm64/CampertronGUI"
  run_step "GUI --version and smoke test (native)" RUNNER_TEMP="$work/rt-nat" HOME="$work/rt-nat/home" RID=osx-arm64 VERSION=0.1.0-beta.11 \
    EXPECTED_PROCESS_ARCH=Arm64 STUB_ARCH=Arm64 STUB_CONFIG="$work/rt-nat/home/Library/Application Support/CampertronConfig" "$@"
}
native_gui; expect_rc "workflow native GUI step: a passing smoke test on macOS (~/Library/Application Support) passes" 0 $?
mkdir -p "$work/rt-nat/home/Library/Application Support/CampertronConfig"; run_step "GUI --version and smoke test (native)" RUNNER_TEMP="$work/rt-nat" HOME="$work/rt-nat/home" \
  RID=osx-arm64 VERSION=0.1.0-beta.11 EXPECTED_PROCESS_ARCH=Arm64 STUB_ARCH=Arm64 STUB_CONFIG="$work/rt-nat/home/Library/Application Support/CampertronConfig"; rc=$?
{ [ "$rc" -ne 0 ] && grep -q 'exists before the GUI smoke test' "$work/step.txt"; }; expect_rc "workflow native GUI step: an existing data folder fails before the GUI starts" 0 $?
native_gui STUB_EXIT=1; rc=$?
{ [ "$rc" -ne 0 ] && grep -q '^::error::GUI smoke test exited with code 1' "$work/step.txt"; }; expect_rc "workflow native GUI step: a failing smoke test fails the step with an ::error:: line" 0 $?
native_gui STUB_STDERR="unexpected output"; expect_rc "workflow native GUI step: output on standard error fails the step" nonzero $?
native_gui STUB_REPORTED_CONFIG=/elsewhere/CampertronConfig; expect_rc "workflow native GUI step: a report naming another data folder fails" nonzero $?
native_gui STUB_ARCH=X64; rc=$?
{ [ "$rc" -ne 0 ] && grep -q '^::error::CampertronGUI is not a native Arm64 process' "$work/step.txt"; }; expect_rc "workflow native GUI step: an emulated (x64) process on an Arm64 runner fails" 0 $?
linux_gui() { # [VAR=value...] -> runs the linux-x64 GUI smoke step against the stub archive, with an xvfb-run stub
  rm -rf "$work/rt-lin" "$work/ws/dist" "$work/bin-xvfb"; mkdir -p "$work/rt-lin" "$work/ws/dist" "$work/bin-xvfb" "$work/pkg"
  rm -rf "$work/pkg/campertron-gui-0.1.0-beta.11-linux-x64"; stub_gui "$work/pkg/campertron-gui-0.1.0-beta.11-linux-x64/CampertronGUI"
  tar -C "$work/pkg" -czf "$work/ws/dist/campertron-gui-0.1.0-beta.11-linux-x64.tar.gz" campertron-gui-0.1.0-beta.11-linux-x64
  printf '#!/usr/bin/env bash\n[ "$1" = "-a" ] && shift\nexec "$@"\n' > "$work/bin-xvfb/xvfb-run"; chmod +x "$work/bin-xvfb/xvfb-run"
  run_step "GUI smoke test" PATH="$work/bin-xvfb:$PATH" RUNNER_TEMP="$work/rt-lin" VERSION=0.1.0-beta.11 RID=linux-x64 "$@"
}
linux_gui; expect_rc "workflow linux-x64 GUI step: a passing smoke test passes" 0 $?
linux_gui STUB_EXIT=2; expect_rc "workflow linux-x64 GUI step: a timed-out smoke test (exit 2) fails" nonzero $?
linux_gui RID=linux-arm64; { [ $? -ne 0 ] && grep -q 'campertron-gui-0.1.0-beta.11-linux-arm64.tar.gz' "$work/step.txt"; }
expect_rc "workflow Linux GUI step: the step tests the archive of its own platform (linux-arm64 asks for the linux-arm64 archive)" 0 $?
linux_runtime() { # RID EXPECTED_PROCESS_ARCH STUB_ARCH -> the Linux "--version" step against stub apps of that platform
  rm -rf "$work/rt-ver"; mkdir -p "$work/rt-ver"
  for app in console gui; do
    case "$app" in console) project=CampertronConsole ;; *) project=CampertronGUI ;; esac
    stub_gui "$work/rt-ver/campertron-$app-0.1.0-beta.11-$1/CampertronGUI"; d="$work/rt-ver/campertron-$app-0.1.0-beta.11-$1"
    sed "s/CampertronGUI 0.1.0-beta.11/$project 0.1.0-beta.11/" "$d/CampertronGUI" > "$d/$project.tmp"; mv "$d/$project.tmp" "$d/$project"; chmod +x "$d/$project"
    cp "$d/CampertronGUI.runtimeconfig.json" "$d/$project.runtimeconfig.json"
  done
  run_step "Runtime reported by the published apps" RUNNER_TEMP="$work/rt-ver" VERSION=0.1.0-beta.11 RID="$1" EXPECTED_PROCESS_ARCH="$2" STUB_ARCH="$3"; }
linux_runtime linux-arm64 Arm64 Arm64; expect_rc "workflow Linux runtime step: Arm64 processes on the arm64 runner pass" 0 $?
linux_runtime linux-arm64 Arm64 X64; rc=$?
{ [ "$rc" -ne 0 ] && grep -q '^::error::CampertronConsole is not a native Arm64 process' "$work/step.txt"; }; expect_rc "workflow Linux runtime step: an x64 process for linux-arm64 fails" 0 $?
linux_runtime linux-x64 X64 X64; expect_rc "workflow Linux runtime step: X64 processes for linux-x64 pass" 0 $?

# release.yml job structure for the native smoke tests (static)
native_job="$(workflow_job native-smoke)"; smoke_job="$(workflow_job smoke)"
{ grep -qxF '          - { rid: win-x64, runner: windows-2025, runner_arch: X64, process_arch: X64 }' <<< "$native_job" \
  && grep -qxF '          - { rid: win-arm64, runner: windows-11-arm, runner_arch: ARM64, process_arch: Arm64 }' <<< "$native_job" \
  && grep -qxF '          - { rid: osx-x64, runner: macos-26-intel, runner_arch: X64, process_arch: X64 }' <<< "$native_job" \
  && grep -qxF '          - { rid: osx-arm64, runner: macos-26, runner_arch: ARM64, process_arch: Arm64 }' <<< "$native_job" \
  && grep -qxF '          - { rid: win-x86, runner: windows-2025, runner_arch: X64, process_arch: X86, execution: 64-bit Windows }' <<< "$native_job" \
  && [ "$(grep -c '^          - { rid: ' <<< "$native_job")" = 5 ] && grep -qxF '    runs-on: ${{ matrix.runner }}' <<< "$native_job"; }
expect_rc "workflow structure: native smoke runs win-x64, win-arm64, osx-x64 and osx-arm64, and win-x86 on 64-bit Windows, on pinned standard runner labels" 0 $?
{ grep -qxF "    name: Smoke test \${{ matrix.rid }} (\${{ matrix.execution || 'native' }})" <<< "$native_job" \
  && [ "$(grep -c 'execution: ' <<< "$native_job")" = 1 ]; }
expect_rc "workflow structure: only win-x86 is named as something other than a native run" 0 $?
{ grep -qxF '          - { rid: linux-x64, runner: ubuntu-latest, runner_arch: X64, process_arch: X64 }' <<< "$smoke_job" \
  && grep -qxF '          - { rid: linux-arm64, runner: ubuntu-24.04-arm, runner_arch: ARM64, process_arch: Arm64 }' <<< "$smoke_job" \
  && [ "$(grep -c '^          - { rid: ' <<< "$smoke_job")" = 2 ] && grep -qxF '    runs-on: ${{ matrix.runner }}' <<< "$smoke_job" \
  && grep -qxF '      fail-fast: false' <<< "$smoke_job" && grep -q 'must run on a native $EXPECTED_RUNNER_ARCH runner' <<< "$smoke_job" \
  && ! grep -qE '^    permissions:|: write|qemu|binfmt' <<< "$smoke_job"; }
expect_rc "workflow structure: the Linux smoke job runs linux-x64 and linux-arm64 on runners of their own architecture, without emulation" 0 $?
{ grep -qxF '      fail-fast: false' <<< "$native_job" && grep -qxF '    needs: [verify, binaries]' <<< "$native_job" \
  && grep -qxF '          skip-decompress: true' <<< "$native_job" && ! grep -qE '^    permissions:|: write|setup-dotnet|dotnet (build|publish|restore)' <<< "$native_job"; }
expect_rc "workflow structure: native smoke tests the built archives without an SDK, rebuild or write permission" 0 $?
{ [ "$(grep -c -- '--smoke-test' <<< "$native_job")" -ge 1 ] && grep -q 'check_gui_smoke_run "$rc"' <<< "$native_job" \
  && grep -qxF '        if: failure()' <<< "$native_job" && [ "$(grep -c 'timeout-minutes: 5' <<< "$native_job")" = 4 ]; }
expect_rc "workflow structure: native GUI and web mode smoke tests, checked report, step timeouts and failure-only log upload" 0 $?
{ grep -q -- '--smoke-test' <<< "$smoke_job" && grep -q 'check_gui_smoke_run "$rc"' <<< "$smoke_job" \
  && ! grep -qE 'timeout --kill-after=10 30|-ne 124' <<< "$smoke_job"; }
expect_rc "workflow structure: linux-x64 uses the GUI smoke test instead of the 30-second liveness check" 0 $?
grep -qxF '      - "CampertronGUI/**"' "$here_root/.github/workflows/release.yml"; expect_rc "workflow structure: GUI pull requests get a release dry run" 0 $?
matrix_rids() { sed -n 's/^          - { rid: \([a-z0-9-]*\), runner: .*/\1/p' | sort | tr '\n' ' ' | sed 's/ $//'; }
{ grep -qF 'campertron-gui-$VERSION-$RID/CampertronGUI" --smoke-test' <<< "$smoke_job" \
  && [ "$( { echo "$smoke_job"; grep -v 'execution: ' <<< "$native_job"; } | matrix_rids)" = "$NATIVE_SMOKE_RIDS_DEFAULT" ]; }
expect_rc "workflow structure: the jobs run natively exactly the platforms the release notes list as smoke-tested" 0 $?
[ "$(grep 'execution: ' <<< "$native_job" | matrix_rids)" = "$COMPAT_SMOKE_RIDS_DEFAULT" ]
expect_rc "workflow structure: the jobs run on 64-bit Windows exactly the platforms the release notes list as run there" 0 $?
render_notes 0.1.0-beta.11 true "$image" "$repository" > "$work/notes-native.md"
{ for r in $NATIVE_SMOKE_RIDS_DEFAULT; do grep -F "| \`$r\` |" "$work/notes-native.md" | grep -qF "$NATIVE_SMOKE_TEXT" || exit 1; done; \
  [ "$(grep -cF "$NATIVE_SMOKE_TEXT" "$work/notes-native.md")" = 6 ] && [ "$(grep -cF "$NOT_NATIVE_TEXT" "$work/notes-native.md")" = 1 ] \
  && grep -F '| `linux-arm` |' "$work/notes-native.md" | grep -qF "$NOT_NATIVE_TEXT" && grep -F '| `win-x86` |' "$work/notes-native.md" | grep -qxF "| \`win-x86\` | \`campertron-console-0.1.0-beta.11-win-x86.zip\` | \`campertron-gui-0.1.0-beta.11-win-x86.zip\` | $COMPAT_SMOKE_TEXT |" \
  && grep -qxF "$NATIVE_SMOKE_STATEMENT" "$work/notes-native.md" && grep -qF "(linux/amd64 and linux/arm64). $CONTAINER_SMOKE_STATEMENT" "$work/notes-native.md" \
  && grep -qxF '| Platform | Console | GUI | Smoke test in CI |' "$work/notes-native.md"; }
expect_rc "generated notes: the six natively smoke-tested platforms say so, win-x86 says 64-bit Windows, linux-arm says not run, both images are run" 0 $?

# --- The settings-transfer package's compatibility self-test (TD-69): check_transfer_selftest, the five workflow steps that run
# it (Linux, Windows and macOS, the emulated linux-arm, both images) verbatim against stubs, and the jobs' structure (static).
st_checks="aes-gcm-supported pbkdf2-key-length aes-gcm-round-trip associated-data-enforced ciphertext-tamper-refused tag-tamper-refused secret-package-round-trip secrets-absent-from-envelope wrong-passphrase-refused header-tamper-refused fresh-salt-and-nonce"
expect_eq "transfer self-test: the checker requires the self-test's eleven checks in their order" "$st_checks" "$TRANSFER_SELFTEST_CHECKS"
st_good() { # RUNTIME OS -> a passing self-test's lines
  printf '%s\n' "transfer-selftest runtime=$1" "transfer-selftest os-architecture=$2" "transfer-selftest format=1"
  for c in $st_checks; do echo "transfer-selftest $c=ok"; done
  echo "transfer-selftest result=ok"
}
: > "$work/st.err"
st_case() { # description expected-rc rc RID PROCESS OS EXECUTION RUNNER (the output in $work/st.out)
  check_transfer_selftest "$3" "$work/st.out" "$work/st.err" "$4" "$5" "$6" "$7" "$8" > "$work/st.txt" 2>&1; expect_rc "transfer self-test: $1" "$2" $?
}
st_good "linux-x64 X64" X64 > "$work/st.out"; st_case "linux-x64 on its own runner passes (native)" 0 0 linux-x64 X64 X64 native X64
grep -qxF 'Transfer self-test evidence: rid linux-x64, process X64, OS X64, native execution, runner X64: 11 checks ok, format 1, result ok' "$work/st.txt"
expect_rc "transfer self-test: a pass prints one evidence line" 0 $?
st_good "osx-arm64 Arm64" Arm64 > "$work/st.out"; st_case "osx-arm64 on an ARM64 runner passes (native)" 0 0 osx-arm64 Arm64 Arm64 native ARM64
st_good "win-x86 X86" X64 > "$work/st.out"; st_case "win-x86 on 64-bit Windows passes as compatibility execution" 0 0 win-x86 X86 X64 compatibility X64
st_case "win-x86 on 64-bit Windows can't be called native" nonzero 0 win-x86 X86 X64 native X64
st_good "linux-arm Arm" Arm > "$work/st.out"; st_case "linux-arm under an emulator on an x64 runner passes as emulated execution" 0 0 linux-arm Arm Arm emulated X64
st_case "an emulated Arm process can't be called native (emulated labeled native)" nonzero 0 linux-arm Arm Arm native X64
st_case "an emulated run can't be called compatibility execution" nonzero 0 linux-arm Arm Arm compatibility X64
st_case "an unknown kind of execution is refused" nonzero 0 linux-arm Arm Arm virtual X64
st_good "linux-x64 X64" X64 > "$work/st.out"; st_case "a native process can't be called emulated" nonzero 0 linux-x64 X64 X64 emulated X64
st_case "a compatibility claim for another platform is refused" nonzero 0 linux-x64 X64 X64 compatibility X64
st_good "linux-x64 X64" X64 > "$work/st.out"; st_case "the host's own build instead of the linux-arm archive fails (another runtime)" nonzero 0 linux-arm Arm Arm emulated X64
st_good "linux-x64 X64" X64 > "$work/st.out"; st_case "an x64 process for linux-arm64 on an ARM64 runner fails (architecture mislabeled)" nonzero 0 linux-arm64 Arm64 Arm64 native ARM64
st_good "linux-arm64 Arm64" X64 > "$work/st.out"; st_case "another operating-system architecture fails" nonzero 0 linux-arm64 Arm64 Arm64 native ARM64
st_good "linux-x64 X64" X64 | sed 's/format=1/format=2/' > "$work/st.out"; st_case "another package format fails" nonzero 0 linux-x64 X64 X64 native X64
st_good "linux-x64 X64" X64 | sed 's/tag-tamper-refused=ok/tag-tamper-refused=failed/' > "$work/st.out"; st_case "a failed check fails, even with exit code 0" nonzero 0 linux-x64 X64 X64 native X64
st_case "a failed check with exit code 1 fails" nonzero 1 linux-x64 X64 X64 native X64
grep -q '^::error::transfer self-test exited with code 1 (tag-tamper-refused=failed )$' "$work/st.txt"; expect_rc "transfer self-test: a failure names only the failed checks" 0 $?
for c in wrong-passphrase-refused header-tamper-refused associated-data-enforced ciphertext-tamper-refused tag-tamper-refused aes-gcm-round-trip; do
  st_good "linux-x64 X64" X64 | grep -vxF "transfer-selftest $c=ok" > "$work/st.out"; st_case "a run without its $c check fails" nonzero 0 linux-x64 X64 X64 native X64
done
printf '%s\n' "transfer-selftest runtime=linux-x64 X64" "transfer-selftest os-architecture=X64" "transfer-selftest format=1" "transfer-selftest aes-gcm-supported=failed" \
  "transfer-selftest encryption-refused-without-aes-gcm=ok" "transfer-selftest result=ok" > "$work/st.out"
st_case "a run that passes without AES-GCM fails" nonzero 0 linux-x64 X64 X64 native X64
st_good "linux-x64 X64" X64 > "$work/st.out"; echo "transfer-selftest passphrase=Q7wE9rT2yU4iO6pA8sD0" >> "$work/st.out"
st_case "a line that is not one of its fixed facts fails (a secret printed)" nonzero 0 linux-x64 X64 X64 native X64
! grep -q 'Q7wE9rT2yU4iO6pA8sD0' "$work/st.txt"; expect_rc "transfer self-test: a line that is not a fixed fact is never shown" 0 $?
st_good "linux-x64 X64" X64 | sed 's/^transfer-selftest result=ok$/transfer-selftest result=ok\ntransfer-selftest result=ok/' > "$work/st.out"
st_case "a line after the result fails" nonzero 0 linux-x64 X64 X64 native X64
st_good "linux-x64 X64" X64 | sed '/result=ok/d' > "$work/st.out"; st_case "a run without its result fails" nonzero 0 linux-x64 X64 X64 native X64
: > "$work/st.out"; st_case "an empty output fails" nonzero 0 linux-x64 X64 X64 native X64
st_good "win-x64 X64" X64 | sed 's/$/\r/' > "$work/st.out"; st_case "Windows line endings are accepted" 0 0 win-x64 X64 X64 native X64
st_good "linux-x64 X64" X64 > "$work/st.out"; echo "warning" > "$work/st.err"; st_case "anything on standard error fails" nonzero 0 linux-x64 X64 X64 native X64; : > "$work/st.err"
printf '%s\n' "transfer-selftest runtime=linux-x64 X64" "transfer-selftest os-architecture=X64" "transfer-selftest unexpected-error=System.DllNotFoundException" "transfer-selftest result=failed" > "$work/st.out"
st_case "an unexpected error fails" nonzero 1 linux-x64 X64 X64 native X64
grep -qxF 'transfer-selftest unexpected-error=System.DllNotFoundException' "$work/st.txt"; expect_rc "transfer self-test: an unexpected error's type is shown" 0 $?
rm -f "$work/st-missing.out"; check_transfer_selftest 0 "$work/st-missing.out" "$work/st.err" linux-x64 X64 X64 native X64 > "$work/st.txt" 2>&1
expect_rc "transfer self-test: a missing output file fails" nonzero $?

# The steps, verbatim (bash -e), against a stub console (and a stub emulator, cygpath and docker), with no .NET.
stub_selftest() { # path -> a stub CampertronConsole --transfer-selftest: STUB_RUNTIME, STUB_OS, STUB_MODE (ok, failed, secret, stderr) and STUB_WRITE
  mkdir -p "$(dirname "$1")"
  cat > "$1" <<'STUB'
#!/usr/bin/env bash
[ "$*" = "--transfer-selftest" ] || { echo "stub: not the self-test: $*" >&2; exit 64; }
printf '%s\n' "transfer-selftest runtime=${STUB_RUNTIME:-linux-x64 X64}" "transfer-selftest os-architecture=${STUB_OS:-X64}" "transfer-selftest format=1"
for c in aes-gcm-supported pbkdf2-key-length aes-gcm-round-trip associated-data-enforced ciphertext-tamper-refused tag-tamper-refused secret-package-round-trip secrets-absent-from-envelope wrong-passphrase-refused header-tamper-refused fresh-salt-and-nonce; do
  if [ "${STUB_MODE:-ok}:$c" = "failed:tag-tamper-refused" ]; then echo "transfer-selftest $c=failed"; else echo "transfer-selftest $c=ok"; fi
done
[ "${STUB_MODE:-ok}" = secret ] && echo "transfer-selftest passphrase=Q7wE9rT2yU4iO6pA8sD0"
[ "${STUB_MODE:-ok}" = stderr ] && echo "warning" >&2
[ -n "${STUB_WRITE:-}" ] && mkdir -p "$STUB_WRITE"
if [ "${STUB_MODE:-ok}" = failed ]; then echo "transfer-selftest result=failed"; exit 1; fi
echo "transfer-selftest result=ok"
STUB
  chmod +x "$1"
}
st_ver=0.1.0-beta.11
linux_selftest() { # RID PROCESS [VAR=value...] -> the Linux smoke job's self-test step against a stub of that platform's console archive
  local rid="$1" process="$2"; shift 2
  rm -rf "$work/rt-st" "$work/ws/dist"; mkdir -p "$work/rt-st" "$work/ws/dist"; : > "$work/rt-st/summary"
  stub_selftest "$work/rt-st/campertron-console-$st_ver-$rid/CampertronConsole"; echo archive > "$work/ws/dist/campertron-console-$st_ver-$rid.tar.gz"
  run_step "Transfer self-test (Linux, native)" RUNNER_TEMP="$work/rt-st" GITHUB_STEP_SUMMARY="$work/rt-st/summary" VERSION="$st_ver" RID="$rid" \
    EXPECTED_PROCESS_ARCH="$process" RUNNER_ARCH="$(_runner_arch_of "$process")" STUB_RUNTIME="$rid $process" STUB_OS="$process" "$@"
}
linux_selftest linux-x64 X64; expect_rc "workflow Linux self-test step: linux-x64's archive passes natively" 0 $?
grep -qF -- "- \`linux-x64\` console archive \`$(sha256sum < "$work/ws/dist/campertron-console-$st_ver-linux-x64.tar.gz" | cut -d' ' -f1)\`: Transfer self-test evidence: rid linux-x64, process X64, OS X64, native execution, runner X64" "$work/rt-st/summary"
expect_rc "workflow Linux self-test step: the job summary names the archive's SHA-256 and the evidence" 0 $?
linux_selftest linux-arm64 Arm64; expect_rc "workflow Linux self-test step: linux-arm64's archive passes natively on an ARM64 runner" 0 $?
linux_selftest linux-arm64 Arm64 STUB_RUNTIME="linux-x64 X64" STUB_OS=X64; expect_rc "workflow Linux self-test step: an x64 build in place of linux-arm64's fails" nonzero $?
linux_selftest linux-arm64 Arm64 RUNNER_ARCH=X64; expect_rc "workflow Linux self-test step: an Arm64 process on an x64 runner is not native and fails" nonzero $?
linux_selftest linux-x64 X64 STUB_MODE=failed; rc=$?
{ [ "$rc" -ne 0 ] && grep -q '^::error::transfer self-test exited with code 1' "$work/step.txt"; }; expect_rc "workflow Linux self-test step: a failed self-test fails the step (no failure is ignored)" 0 $?
linux_selftest linux-x64 X64 STUB_MODE=secret; expect_rc "workflow Linux self-test step: a printed value fails the step" nonzero $?
! grep -q 'Q7wE9rT2yU4iO6pA8sD0' "$work/step.txt"; expect_rc "workflow Linux self-test step: a printed value never reaches the log" 0 $?
linux_selftest linux-x64 X64 STUB_MODE=stderr; expect_rc "workflow Linux self-test step: output on standard error fails the step" nonzero $?
linux_selftest linux-x64 X64 STUB_WRITE="$work/rt-st/selftest-data/CampertronConfig"; rc=$?
{ [ "$rc" -ne 0 ] && grep -q '^::error::The transfer self-test created a file or folder' "$work/step.txt"; }; expect_rc "workflow Linux self-test step: a settings folder created by the self-test fails the step" 0 $?
native_selftest() { # RID PROCESS [VAR=value...] -> the Windows and macOS job's self-test step against a stub console (and a cygpath stub)
  local rid="$1" process="$2" exe="" ext=tar.gz; shift 2
  case "$rid" in win-*) exe=.exe; ext=zip ;; esac
  rm -rf "$work/rt-st" "$work/ws/dist" "$work/bin-cyg"; mkdir -p "$work/rt-st/home" "$work/rt-st/local" "$work/ws/dist" "$work/bin-cyg"; : > "$work/rt-st/summary"
  printf '#!/usr/bin/env bash\n[ "$1" = -u ] && echo "$2"\n' > "$work/bin-cyg/cygpath"; chmod +x "$work/bin-cyg/cygpath"
  stub_selftest "$work/rt-st/apps/campertron-console-$st_ver-$rid/CampertronConsole$exe"; echo archive > "$work/ws/dist/campertron-console-$st_ver-$rid.$ext"
  run_step "Transfer self-test (Windows and macOS)" PATH="$work/bin-cyg:$PATH" RUNNER_TEMP="$work/rt-st" HOME="$work/rt-st/home" LOCALAPPDATA="$work/rt-st/local" \
    GITHUB_STEP_SUMMARY="$work/rt-st/summary" VERSION="$st_ver" RID="$rid" EXPECTED_PROCESS_ARCH="$process" STUB_RUNTIME="$rid $process" STUB_OS="$process" "$@"
}
native_selftest osx-arm64 Arm64 RUNNER_ARCH=ARM64; expect_rc "workflow native self-test step: osx-arm64 passes natively" 0 $?
native_selftest osx-x64 X64 RUNNER_ARCH=X64; expect_rc "workflow native self-test step: osx-x64 passes natively" 0 $?
native_selftest win-arm64 Arm64 RUNNER_ARCH=ARM64; expect_rc "workflow native self-test step: win-arm64 passes natively" 0 $?
native_selftest win-x86 X86 RUNNER_ARCH=X64 STUB_OS=X64; rc=$?
{ [ "$rc" -eq 0 ] && grep -q 'process X86, OS X64, compatibility execution, runner X64' "$work/rt-st/summary"; }
expect_rc "workflow native self-test step: win-x86 on 64-bit Windows passes as compatibility execution, never native" 0 $?
native_selftest win-x86 X86 RUNNER_ARCH=X64 STUB_OS=X86; expect_rc "workflow native self-test step: win-x86 reporting a 32-bit system fails" nonzero $?
native_selftest win-x64 X64 RUNNER_ARCH=ARM64; expect_rc "workflow native self-test step: an x64 process on an ARM64 runner (emulated) is not native and fails" nonzero $?
native_selftest osx-arm64 Arm64 RUNNER_ARCH=ARM64 STUB_MODE=failed; expect_rc "workflow native self-test step: a failed self-test fails the step" nonzero $?
native_selftest osx-arm64 Arm64 RUNNER_ARCH=ARM64 STUB_WRITE="$work/rt-st/home/Library/Application Support/CampertronConfig"; rc=$?
{ [ "$rc" -ne 0 ] && grep -q '^::error::The transfer self-test created CampertronConfig' "$work/step.txt"; }; expect_rc "workflow native self-test step: a settings folder created on macOS fails the step" 0 $?
native_selftest win-x64 X64 RUNNER_ARCH=X64 STUB_WRITE="$work/rt-st/local/CampertronCache"; expect_rc "workflow native self-test step: a data folder created on Windows fails the step" nonzero $?
emulated_selftest() { # [VAR=value...] -> the emulated linux-arm self-test step against a stub console and a stub emulator that runs it
  rm -rf "$work/rt-st" "$work/ws/dist"; mkdir -p "$work/rt-st/qemu/usr/bin" "$work/rt-st/rootfs" "$work/ws/dist"; : > "$work/rt-st/summary"
  printf '#!/usr/bin/env bash\necho "$*" > "%s/qemu.log"\n[ "$1" = -L ] || exit 70\nshift 2\nexec "$@"\n' "$work/rt-st" > "$work/rt-st/qemu/usr/bin/qemu-arm-static"; chmod +x "$work/rt-st/qemu/usr/bin/qemu-arm-static"
  stub_selftest "$work/rt-st/campertron-console-$st_ver-linux-arm/CampertronConsole"; echo archive > "$work/ws/dist/campertron-console-$st_ver-linux-arm.tar.gz"
  run_step "Transfer self-test (linux-arm, emulated)" RUNNER_TEMP="$work/rt-st" GITHUB_STEP_SUMMARY="$work/rt-st/summary" VERSION="$st_ver" RID=linux-arm \
    EXPECTED_PROCESS_ARCH=Arm RUNNER_ARCH=X64 STUB_RUNTIME="linux-arm Arm" STUB_OS=Arm "$@"
}
emulated_selftest; rc=$?
{ [ "$rc" -eq 0 ] && grep -qxF -- "-L $work/rt-st/rootfs $work/rt-st/campertron-console-$st_ver-linux-arm/CampertronConsole --transfer-selftest" "$work/rt-st/qemu.log" \
  && grep -q 'rid linux-arm, process Arm, OS Arm, emulated execution, runner X64' "$work/rt-st/summary"; }
expect_rc "workflow emulated self-test step: the archive's console runs under the emulator with the Arm system and passes as emulated" 0 $?
emulated_selftest STUB_RUNTIME="linux-x64 X64" STUB_OS=X64; expect_rc "workflow emulated self-test step: the runner's own x64 build instead of the linux-arm archive fails" nonzero $?
emulated_selftest RUNNER_ARCH=ARM; expect_rc "workflow emulated self-test step: an Arm process on an Arm runner is not emulated and fails" nonzero $?
emulated_selftest STUB_MODE=failed; expect_rc "workflow emulated self-test step: a failed self-test fails the step" nonzero $?
emulated_selftest STUB_WRITE="$work/rt-st/selftest-home/x"; expect_rc "workflow emulated self-test step: a file created by the self-test fails the step" nonzero $?
container_selftest() { # STEP IMAGE [VAR=value...] -> a container job's self-test step against a stub docker that runs a stub console
  local step="$1" image="$2"; shift 2
  rm -rf "$work/rt-st" "$work/bin-st"; mkdir -p "$work/rt-st" "$work/bin-st"; : > "$work/rt-st/summary"
  stub_selftest "$work/rt-st/app/CampertronConsole"
  cat > "$work/bin-st/docker" <<STUB
#!/usr/bin/env bash
case "\$1 \$2" in
  "image inspect") case "\$*" in *Architecture*) echo "\${STUB_PLATFORM}" ;; *) echo "sha256:$(printf '1%.0s' {1..64})" ;; esac ;;
  "run --rm") echo "\$*" > "$work/rt-st/docker.log"; [ "\$*" = "run --rm --network none --read-only $image --transfer-selftest" ] || exit 125; exec "$work/rt-st/app/CampertronConsole" --transfer-selftest ;;
  *) exit 99 ;;
esac
STUB
  chmod +x "$work/bin-st/docker"
  run_step "$step" PATH="$work/bin-st:$PATH" RUNNER_TEMP="$work/rt-st" GITHUB_STEP_SUMMARY="$work/rt-st/summary" "$@"
}
container_selftest "Container transfer self-test" campertron-smoke:local STUB_PLATFORM=linux/amd64 RUNNER_ARCH=X64 STUB_RUNTIME="linux-x64 X64" STUB_OS=X64; rc=$?
{ [ "$rc" -eq 0 ] && grep -qxF 'run --rm --network none --read-only campertron-smoke:local --transfer-selftest' "$work/rt-st/docker.log"; }
expect_rc "workflow container self-test step: the amd64 image runs it read-only without a network and passes natively" 0 $?
container_selftest "Container transfer self-test" campertron-smoke:local STUB_PLATFORM=linux/arm64 RUNNER_ARCH=X64 STUB_RUNTIME="linux-x64 X64" STUB_OS=X64
expect_rc "workflow container self-test step: an image of another platform fails before it runs" nonzero $?
container_selftest "Container transfer self-test" campertron-smoke:local STUB_PLATFORM=linux/amd64 RUNNER_ARCH=X64 STUB_RUNTIME="linux-x64 X64" STUB_OS=X64 STUB_MODE=failed
expect_rc "workflow container self-test step: a failed self-test in the amd64 image fails the step" nonzero $?
container_selftest "Container transfer self-test (linux/arm64)" campertron-smoke-arm64:local STUB_PLATFORM=linux/arm64 RUNNER_ARCH=ARM64 STUB_RUNTIME="linux-arm64 Arm64" STUB_OS=Arm64 ARM64_CONFIG=sha256:0
expect_rc "workflow container self-test step: the arm64 image passes natively on an ARM64 runner" 0 $?
container_selftest "Container transfer self-test (linux/arm64)" campertron-smoke-arm64:local STUB_PLATFORM=linux/arm64 RUNNER_ARCH=ARM64 STUB_RUNTIME="linux-x64 X64" STUB_OS=X64 ARM64_CONFIG=sha256:0
expect_rc "workflow container self-test step: an x64 process in the arm64 image fails" nonzero $?

# The jobs' structure (static): every release target's console archive and both images run the self-test, each with its
# execution named as it is, always run and blocking, bounded in time; the emulated job pinned, unprivileged and read-only.
emulated_job="$(workflow_job emulated-smoke)"
st_line() { workflow_job "$1" | grep -nxF "      - name: $2" | cut -d: -f1; }
st_step() { workflow_job "$1" | awk -v n="      - name: $2" '$0 == n {f = 1; print; next} f && /^      - name: / {exit} f {print}'; }
for spec in "smoke|Transfer self-test (Linux, native)|native|\"\$RID\" \"\$EXPECTED_PROCESS_ARCH\" \"\$EXPECTED_PROCESS_ARCH\" native \"\$RUNNER_ARCH\"" \
            "native-smoke|Transfer self-test (Windows and macOS)|native|\"\$RID\" \"\$EXPECTED_PROCESS_ARCH\" \"\$os_arch\" \"\$execution\" \"\$RUNNER_ARCH\"" \
            "emulated-smoke|Transfer self-test (linux-arm, emulated)|emulated|\"\$RID\" \"\$EXPECTED_PROCESS_ARCH\" \"\$EXPECTED_PROCESS_ARCH\" emulated \"\$RUNNER_ARCH\"" \
            "docker|Container transfer self-test|native|linux-x64 X64 X64 native \"\$RUNNER_ARCH\"" \
            "container-smoke|Container transfer self-test (linux/arm64)|native|linux-arm64 Arm64 Arm64 native \"\$RUNNER_ARCH\""; do
  IFS='|' read -r job name mode args <<< "$spec"
  step="$(st_step "$job" "$name")"
  { [ -n "$step" ] && [ "$(workflow_job "$job" | grep -cE -- '--transfer-selftest \\$')" = 1 ] && grep -qxF '        timeout-minutes: 5' <<< "$step" \
    && grep -qF -- "--transfer-selftest" <<< "$step" && grep -qF -- "$args > \"\$RUNNER_TEMP/smoke/transfer-selftest.txt\" || { cat" <<< "$step" \
    && [ "$(grep -c 'check_transfer_selftest ' <<< "$step")" = 1 ] && grep -qxF '          set -euo pipefail' <<< "$step"; }
  expect_rc "workflow structure: the $job job runs the transfer self-test once, checked as $mode execution, with a 5-minute step timeout" 0 $?
  ! grep -qE '^        (if|continue-on-error):|\|\| true|dotnet |publish/|secrets\.' <<< "$step"
  expect_rc "workflow structure: the $job job's transfer self-test always runs, blocks the job when it fails and runs no build of the sources" 0 $?
done
{ grep -q 'case "$RID" in win-x86) execution=compatibility; os_arch=X64 ;; \*) execution=native; os_arch="$EXPECTED_PROCESS_ARCH" ;; esac' <<< "$(st_step native-smoke 'Transfer self-test (Windows and macOS)')" \
  && ! grep -qE 'emulated "\$RUNNER_ARCH"|execution=emulated' <<< "$(for j in smoke native-smoke docker container-smoke; do workflow_job "$j"; done)" \
  && [ "$(grep -o 'execution=[a-z]*' <<< "$native_job" | sort -u | tr '\n' ' ')" = 'execution=compatibility execution=native ' ]; }
expect_rc "workflow structure: only win-x86 is compatibility execution, and no native job calls a run emulated" 0 $?
{ [ "$( { echo "$smoke_job"; echo "$native_job"; echo "$emulated_job"; } | matrix_rids)" = "$RELEASE_RIDS_DEFAULT" ] \
  && [ "$(echo "$emulated_job" | matrix_rids)" = linux-arm ]; }
expect_rc "workflow structure: the self-test runs on every release target's console archive, linux-arm emulated (no target omitted)" 0 $?
{ grep -qxF '          - { rid: linux-arm, runner: ubuntu-latest, runner_arch: X64, process_arch: Arm, platform: linux/arm/v7, elf: "ELF 32-bit LSB pie executable, ARM, EABI5" }' <<< "$emulated_job" \
  && [ "$(grep -c '^          - { rid: ' <<< "$emulated_job")" = 1 ] && grep -qxF '    name: Smoke test ${{ matrix.rid }} (emulated)' <<< "$emulated_job" \
  && grep -qxF '    needs: [verify, binaries]' <<< "$emulated_job" && grep -qxF '    runs-on: ${{ matrix.runner }}' <<< "$emulated_job" && grep -qxF '    timeout-minutes: 15' <<< "$emulated_job" \
  && grep -qxF '          pattern: campertron-console-*-${{ matrix.rid }}.tar.gz' <<< "$emulated_job" && grep -qxF '          skip-decompress: true' <<< "$emulated_job"; }
expect_rc "workflow structure: the emulated job runs only linux-arm's console archive, on the x64 runner, named emulated" 0 $?
{ grep -qxE '      QEMU_DEB_URL: https://archive\.ubuntu\.com/ubuntu/pool/universe/q/qemu/qemu-user-static_8\.2\.2\+ds-0ubuntu1_amd64\.deb' <<< "$emulated_job" \
  && grep -qxE '      QEMU_DEB_SHA256: [0-9a-f]{64}' <<< "$emulated_job" && grep -qxE '      TARGET_SYSTEM_IMAGE: mcr\.microsoft\.com/dotnet/runtime-deps@sha256:[0-9a-f]{64}' <<< "$emulated_job" \
  && grep -qF 'if [ "$got" != "$QEMU_DEB_SHA256" ]' <<< "$emulated_job" && grep -qF 'if [ "$version" != "$QEMU_VERSION_LINE" ]' <<< "$emulated_job" \
  && grep -qF 'case "$kind" in "$TARGET_ELF"*) ;;' <<< "$emulated_job" && grep -qF -- "--proto '=https'" <<< "$emulated_job"; }
expect_rc "workflow structure: the emulator's package and the Arm system's image are pinned and checked, and the executable is the archive's Arm program" 0 $?
{ [ -n "$emulated_job" ] && ! grep -qE '^    permissions:|: write|--privileged|binfmt|sudo|apt-get|setup-qemu|docker (run|build|push|login)|buildx|upload-artifact|setup-dotnet|secrets\.|continue-on-error' <<< "$emulated_job"; }
expect_rc "workflow structure: the emulated job is read-only and unprivileged: no write permission, sudo, binfmt registration, privileged or run container, build or upload" 0 $?
{ [ "$(grep -v '^ *#' "$here_root/.github/workflows/release.yml" | grep -c 'qemu-arm-static')" = "$(grep -v '^ *#' <<< "$emulated_job" | grep -c 'qemu-arm-static')" ] \
  && ! grep -qiE 'qemu|binfmt' <<< "$(for j in smoke native-smoke docker container-smoke; do workflow_job "$j" | grep -v '^ *#'; done)"; }
expect_rc "workflow structure: only the emulated job uses an emulator; the native jobs are never emulated" 0 $?
release_yml="$here_root/.github/workflows/release.yml"
{ [ "$(st_line smoke 'Console clean install')" -lt "$(st_line smoke 'Transfer self-test (Linux, native)')" ] \
  && [ "$(st_line native-smoke 'Extract archives')" -lt "$(st_line native-smoke 'Transfer self-test (Windows and macOS)')" ] \
  && [ "$(st_line native-smoke 'Transfer self-test (Windows and macOS)')" -lt "$(st_line native-smoke 'Console --version and clean install (native)')" ] \
  && [ "$(st_line emulated-smoke 'Prepare the emulator and the 32-bit Arm system')" -lt "$(st_line emulated-smoke 'Transfer self-test (linux-arm, emulated)')" ] \
  && [ "$(st_line docker 'Check the loaded image is the validated amd64 image')" -lt "$(st_line docker 'Container transfer self-test')" ] \
  && [ "$(st_line container-smoke 'Check the loaded image is the validated arm64 image')" -lt "$(st_line container-smoke 'Container transfer self-test (linux/arm64)')" ]; }
expect_rc "workflow structure: each self-test runs the extracted archive or the validated, loaded image (native: before the clean install's data check)" 0 $?

# Every workflow (release.yml, ci.yml, pages.yml): third-party actions pinned by a full commit SHA, a time bound on every job.
for wf in release ci pages; do
  f="$here_root/.github/workflows/$wf.yml"
  { [ "$(grep -cE '^ +(- )?uses: ' "$f")" -gt 0 ] && ! grep -E '^ +(- )?uses: ' "$f" | grep -vqE '^ +(- )?uses: [A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40} # v[0-9]'; }
  expect_rc "workflow structure: every action of $wf.yml is pinned by a full commit SHA" 0 $?
  jobs="$(awk '/^jobs:$/ {f = 1; next} f && /^  [A-Za-z0-9_-]+:$/ {print}' "$f" | wc -l | tr -d ' ')"
  timed="$(awk '/^jobs:$/ {f = 1; next} f && /^    timeout-minutes: [1-9][0-9]*$/ {print}' "$f" | wc -l | tr -d ' ')"
  { [ "$jobs" -gt 0 ] && [ "$jobs" = "$timed" ]; }; expect_rc "workflow structure: every job of $wf.yml has a time bound ($timed of $jobs)" 0 $?
done
ci_yml="$here_root/.github/workflows/ci.yml"
{ [ "$(awk '/^permissions:$/{getline; print; exit}' "$ci_yml")" = '  contents: read' ] && ! grep -qE ': write|^    permissions:|secrets\.' "$ci_yml"; }
expect_rc "workflow structure: ci.yml is read-only (contents: read, no job grants itself more, no secret)" 0 $?
ci_windows="$(awk '$0 == "  windows-settings-files:" {f = 1; print; next} f && /^  [A-Za-z0-9_-]+:$/ {exit} f {print}' "$ci_yml")"
st_classes="$(grep -ho 'public \(sealed \)\?class Transfer[A-Za-z0-9]*Tests\b' "$here_root"/CampertronUnitTests/function/Transfer/*.cs | sed 's/.*class //' | sort -u)"
{ [ "$(wc -l <<< "$st_classes")" -ge 16 ] && for c in $st_classes; do grep -qxF "            'FullyQualifiedName~CampertronUnitTests.function.Transfer.$c'" <<< "$ci_windows" || exit 1; done; }
expect_rc "workflow structure: the Windows CI job's list names every transfer test class ($(wc -l <<< "$st_classes" | tr -d ' ') in the sources)" 0 $?
{ grep -qF "Get-ChildItem -LiteralPath 'CampertronUnitTests/function/Transfer' -Filter '*.cs'" <<< "$ci_windows" \
  && grep -qF "the transfer test class \$class did not run on Windows" <<< "$ci_windows" && grep -qF 'a Windows ACL test of the transfer was skipped or failed' <<< "$ci_windows" \
  && for t in $(grep -ho 'public void [A-Za-z_]*' "$here_root/CampertronUnitTests/function/Transfer/TransferWindowsAclTests.cs" | sed 's/public void //'); do grep -qF "'$t'" <<< "$ci_windows" || exit 1; done \
  && grep -qF 'the transfer self-test (AES-GCM) did not pass on Windows' <<< "$ci_windows" && grep -qF 'if ($problems.Count -gt 0) { exit 1 }' <<< "$ci_windows"; }
expect_rc "workflow structure: the Windows job's summary requires every transfer class to run, each Windows ACL test by name to pass (none skipped) and the self-test" 0 $?

# --- release.yml: web mode's release smoke test (v0.3.0's PR 12, D102). The harness's own tests (a fake web mode, a stub
# docker; loopback only), then the workflow's four steps: static structure, and run verbatim like the runner (bash -e).
python3 -I "$here/test-web-smoke.py" > "$work/web-smoke-tests.txt" 2>&1; rc=$?
tail -n 3 "$work/web-smoke-tests.txt" | sed 's/^/      /'
[ "$rc" -eq 0 ] || cat "$work/web-smoke-tests.txt"
expect_rc "web smoke harness: its unit tests pass (input safety, start, HTTP checks, cleanup, platform wrappers, container)" 0 "$rc"

web_step_of() { # job step-name -> the step's lines in that job (name to the next step)
  workflow_job "$1" | awk -v n="      - name: $2" '$0 == n {f = 1; print; next} f && /^      - name: / {exit} f {print}'; }
step_line() { workflow_job "$1" | grep -nxF "      - name: $2" | cut -d: -f1; }
for spec in "smoke|Web mode smoke test|native|\"\$RUNNER_TEMP/campertron-console-\$VERSION-\$RID/CampertronConsole\"" \
            "native-smoke|Web mode smoke test (native)|native|\"\$RUNNER_TEMP/apps/campertron-console-\$VERSION-\$RID/CampertronConsole\$exe\"" \
            "docker|Container web mode smoke test|container|campertron-smoke:local" \
            "container-smoke|Container web mode smoke test (linux/arm64)|container|campertron-smoke-arm64:local"; do
  IFS='|' read -r job name mode target <<< "$spec"
  step="$(web_step_of "$job" "$name")"
  { [ -n "$step" ] && [ "$(workflow_job "$job" | grep -c -- '-I \.github/release-policy/web-smoke\.py ')" = 1 ] && grep -qxF '        timeout-minutes: 5' <<< "$step" \
    && grep -qF -- "-I .github/release-policy/web-smoke.py $mode" <<< "$step" && grep -qF -- "--icon assets/icon/campertron.ico" <<< "$step" \
    && grep -qF -- '--version "$VERSION"' <<< "$step"; }
  expect_rc "workflow structure: the $job job runs web mode's smoke test once ($mode), with a 5-minute step timeout" 0 $?
  case "$mode" in native) grep -qF -- "--app $target" <<< "$step" ;; *) grep -qF -- "--image $target" <<< "$step" ;; esac
  expect_rc "workflow structure: the $job job's web smoke test runs the release artifact ($target), not a build of the sources" 0 $?
  ! grep -qE '^        (if|continue-on-error):|dotnet |--listen|--port|publish/|secrets\.' <<< "$step"
  expect_rc "workflow structure: the $job job's web smoke test always runs, blocks the job when it fails and picks no address or port itself" 0 $?
done
{ [ "$(step_line smoke 'Console clean install')" -lt "$(step_line smoke 'Web mode smoke test')" ] \
  && [ "$(step_line native-smoke 'Console --version and clean install (native)')" -lt "$(step_line native-smoke 'Web mode smoke test (native)')" ] \
  && [ "$(step_line native-smoke 'Web mode smoke test (native)')" -lt "$(step_line native-smoke 'GUI --version and smoke test (native)')" ] \
  && [ "$(step_line docker 'Check the loaded image is the validated amd64 image')" -lt "$(step_line docker 'Container web mode smoke test')" ] \
  && [ "$(step_line container-smoke 'Check the loaded image is the validated arm64 image')" -lt "$(step_line container-smoke 'Container web mode smoke test (linux/arm64)')" ]; }
expect_rc "workflow structure: each web smoke test runs after its archive was extracted or its validated image loaded (native: before the GUI's clean start)" 0 $?
release_yml="$here_root/.github/workflows/release.yml"
! grep -qE -- '--listen[ =](0\.0\.0\.0|::|\[::\])|(--publish|-p)[ =]+(8790|0\.0\.0\.0|\[::\])|0\.0\.0\.0:8790|continue-on-error' "$release_yml"
expect_rc "workflow structure: nothing listens on or publishes to every interface, and no step may fail without failing its job" 0 $?
{ [ "$(grep -c '^    permissions:' "$release_yml")" = 2 ] && [ -n "$(workflow_job docker-publish | grep -x '    permissions:')" ] \
  && [ -n "$(workflow_job release | grep -x '    permissions:')" ] && grep -qxF 'permissions:' "$release_yml" \
  && [ "$(awk '/^permissions:$/{getline; print; exit}' "$release_yml")" = '  contents: read' ] && ! grep -qF 'secrets.' "$release_yml"; }
expect_rc "workflow structure: read-only by default; only docker-publish and release grant themselves more; no secret is used" 0 $?
{ [ "$(grep -c 'docker push\|skopeo copy[^"]*docker://' "$release_yml")" = 0 ] && ! grep -qE 'docker://' <<< "$(for j in verify binaries smoke native-smoke emulated-smoke docker container-smoke assemble; do workflow_job "$j"; done)"; } 
expect_rc "workflow structure: no validation job (all run by dry runs) pushes or copies an image to a registry" 0 $?
[ "$(grep -E '^ +runs-on: ' "$release_yml" | sed 's/^ *runs-on: //' | sort -u | tr '\n' ' ')" = '${{ matrix.runner }} ubuntu-24.04-arm ubuntu-latest ' ]
expect_rc "workflow structure: no other runner (the web smoke tests run on the existing jobs' runners)" 0 $?
# The triggers and the pull-request path filter: the release-input checks before the summary (D105).

# The four steps run verbatim (bash -e) against the fake web mode and the stub docker of test-web-smoke.py.
cp "$here/web-smoke.py" "$work/ws/.github/release-policy/"; mkdir -p "$work/ws/assets/icon"; cp "$here_root/assets/icon/campertron.ico" "$work/ws/assets/icon/"
web_code="WXYZ-2345-6789"
web_env=(FAKE_ICON="$here_root/assets/icon/campertron.ico" FAKE_STATE="$work/web-state" FAKE_CODE="$web_code" FAKE_VERSION=0.1.0-beta.11)
web_fixture() { # folder -> the fake release folder there; a new state folder
  rm -rf "$work/rt-web" "$work/web-state"; mkdir -p "$work/rt-web" "$work/web-state"
  python3 -I "$here/test-web-smoke.py" --write-fake-app "$work/rt-web/$1" > /dev/null; }
web_fixture campertron-console-0.1.0-beta.11-linux-x64
run_step "Web mode smoke test" RUNNER_TEMP="$work/rt-web" VERSION=0.1.0-beta.11 RID=linux-x64 "${web_env[@]}" FAKE_WEB=; rc=$?
{ [ "$rc" -eq 0 ] && grep -qx 'web-smoke: passed' "$work/step.txt"; }
expect_rc "workflow Linux web step: the archive's web mode passes" 0 $?
run_step "Web mode smoke test" RUNNER_TEMP="$work/rt-web" VERSION=0.1.0-beta.11 RID=linux-x64 "${web_env[@]}" FAKE_WEB=no-favicon; rc=$?
{ [ "$rc" -ne 0 ] && grep -q '^::error::Web mode smoke test failed: GET /favicon.ico did not answer 200' "$work/step.txt" && ! grep -qF "$web_code" "$work/step.txt"; }
expect_rc "workflow Linux web step: a missing favicon fails the step with an ::error:: line, and the setup code is not in the log" 0 $?
run_step "Web mode smoke test" RUNNER_TEMP="$work/rt-web" VERSION=0.1.0-beta.11 RID=linux-x64 "${web_env[@]}" FAKE_WEB=listen-any; rc=$?
{ [ "$rc" -ne 0 ] && grep -q 'does not say that it listens where it was told to' "$work/step.txt"; }
expect_rc "workflow Linux web step: a web UI that listens on every interface fails" 0 $?
run_step "Web mode smoke test" RUNNER_TEMP="$work/rt-web" VERSION=0.1.0-beta.11 RID=linux-arm64 "${web_env[@]}" FAKE_WEB=; rc=$?
{ [ "$rc" -ne 0 ] && grep -q '^::error::Web mode smoke test refused: --app does not exist' "$work/step.txt"; }
expect_rc "workflow Linux web step: the step runs the archive of its own platform (linux-arm64 asks for the linux-arm64 folder)" 0 $?
web_fixture apps/campertron-console-0.1.0-beta.11-osx-arm64
run_step "Web mode smoke test (native)" RUNNER_TEMP="$work/rt-web" VERSION=0.1.0-beta.11 RID=osx-arm64 "${web_env[@]}" FAKE_WEB=; rc=$?
{ [ "$rc" -eq 0 ] && grep -qx 'web-smoke: passed' "$work/step.txt"; }
expect_rc "workflow native web step: the extracted archive's web mode passes (here on Linux, as the harness runs it)" 0 $?
run_step "Web mode smoke test (native)" RUNNER_TEMP="$work/rt-web" VERSION=0.1.0-beta.11 RID=osx-arm64 "${web_env[@]}" FAKE_WEB=ignore-term; rc=$?
{ [ "$rc" -ne 0 ] && grep -q 'did not stop within' "$work/step.txt" && ! kill -0 "$(cat "$work/web-state/pid")" 2> /dev/null; }
expect_rc "workflow native web step: a web UI that does not stop fails the step and is stopped by force" 0 $?
mkdir -p "$work/bin-py"; printf '#!/usr/bin/env bash\nprintf "%%s\\n" "$@" > "%s/python-args.txt"\n' "$work" > "$work/bin-py/python"; chmod +x "$work/bin-py/python"
run_step "Web mode smoke test (native)" PATH="$work/bin-py:$PATH" RUNNER_TEMP='D:\a\_temp' VERSION=0.1.0-beta.11 RID=win-arm64
{ [ "$(sed -n '1,3p' "$work/python-args.txt" | tr '\n' ' ')" = '-I .github/release-policy/web-smoke.py native ' ] \
  && grep -qxF 'D:\a\_temp/apps/campertron-console-0.1.0-beta.11-win-arm64/CampertronConsole.exe' "$work/python-args.txt"; }
expect_rc "workflow native web step: on Windows it runs python -I with the extracted archive's CampertronConsole.exe" 0 $?
python3 -I "$here/test-web-smoke.py" --write-fake-docker "$work/bin-webdocker" > /dev/null
web_docker() { # step image [VAR=value...] -> the container step against the stub docker
  local name="$1" image="$2"; shift 2
  web_fixture campertron-console-0.1.0-beta.11-linux-x64; rm -rf "$work/docker-state"; mkdir -p "$work/docker-state"
  run_step "$name" PATH="$work/bin-webdocker:$PATH" VERSION=0.1.0-beta.11 FAKE_DOCKER_STATE="$work/docker-state" FAKE_DOCKER_IMAGE="$image" \
    FAKE_APP="$work/rt-web/campertron-console-0.1.0-beta.11-linux-x64/CampertronConsole" "${web_env[@]}" FAKE_WEB= "$@"; }
for spec in "Container web mode smoke test|campertron-smoke:local" "Container web mode smoke test (linux/arm64)|campertron-smoke-arm64:local"; do
  IFS='|' read -r name image <<< "$spec"
  web_docker "$name" "$image"; rc=$?
  { [ "$rc" -eq 0 ] && grep -qx 'web-smoke: passed' "$work/step.txt" && grep -q '"--publish", "127.0.0.1::8790", "'"$image"'", "--web"' "$work/docker-state/calls.jsonl" \
    && ! grep -qE '^\["(push|pull|login|tag|rmi)"' "$work/docker-state/calls.jsonl" && ! ls "$work/docker-state" | grep -q '\.json$'; }
  expect_rc "workflow container web step ($image): the validated image's web mode passes, published on 127.0.0.1 only, nothing pushed, the container removed" 0 $?
  web_docker "$name" "$image" FAKE_DOCKER=publish-all; rc=$?
  { [ "$rc" -ne 0 ] && grep -q 'published beyond this computer' "$work/step.txt"; }
  expect_rc "workflow container web step ($image): a port published on every interface fails" 0 $?
done

# The console package must hold the web UI (every platform, linux-arm included, which nothing runs).
pkg_contents() { # app rid [file to leave out] -> runs "Check package contents" on a fixture package
  rm -rf "$work/ws/publish"; local project=CampertronConsole; [ "$1" = gui ] && project=CampertronGUI
  mkdir -p "$work/ws/publish/$project"
  for f in "$project" CampertronLibrary.dll libe_sqlite3.so libSystem.IO.Compression.Native.so CampertronWeb.dll Microsoft.AspNetCore.dll; do
    [ "$f" = "${3:-}" ] || : > "$work/ws/publish/$project/$f"; done
  [ "$1" = gui ] && rm -f "$work/ws/publish/$project/CampertronWeb.dll" "$work/ws/publish/$project/Microsoft.AspNetCore.dll"
  run_step "Check package contents" APP="$1" PROJECT="$project" RID="$2"; }
pkg_contents console linux-arm; expect_rc "workflow package step: a console package with the web UI passes" 0 $?
pkg_contents console linux-arm CampertronWeb.dll; rc=$?
{ [ "$rc" -ne 0 ] && grep -q '^::error::linux-arm console package is missing CampertronWeb.dll (the web UI)' "$work/step.txt"; }
expect_rc "workflow package step: a console package without CampertronWeb.dll fails (also linux-arm, which no job runs)" 0 $?
pkg_contents console linux-x64 Microsoft.AspNetCore.dll; expect_rc "workflow package step: a console package without the ASP.NET Core framework fails" nonzero $?
pkg_contents gui linux-x64; expect_rc "workflow package step: the GUI package needs no web UI" 0 $?
rm -rf "$work/ws/publish"

# --- release.yml: the pull-request dry run's release inputs (open question #53, D105). A pull request that changes a
# shipped input runs the dry run: the projects the archives and the image are built from, the files they read from
# outside their folders (the app icon), and the release's own build and policy files. The workflow is read through
# release-inputs.py (Python's standard library: a strict reader of the `on:` block, GitHub's path-filter rules, the
# projects' MSBuild inputs), never by line numbers, and each check must also reject a weakened copy of the workflow.
ri() { python3 -I "$here/release-inputs.py" "$@"; }
release_inputs_before=(".github/workflows/release.yml" "global.json" "CampertronConsole/Dockerfile" ".dockerignore" "**/*.csproj"
  "Campertron.sln" "CampertronLibrary/content/**" ".github/release-policy/**" "CampertronGUI/**")
release_inputs_added=("CampertronWeb/**" "CampertronLibrary/**" "CampertronConsole/**" "assets/icon/**")
release_inputs=("${release_inputs_before[@]}" "${release_inputs_added[@]}")
json_list() { local out="" p; for p in "$@"; do out="$out,\"$p\""; done; printf '[%s]' "${out#,}"; }
expected_triggers="{\"pull_request\":{\"branches\":[\"master\"],\"paths\":$(json_list "${release_inputs[@]}")},\"push\":{\"tags\":[\"v[0-9]+.[0-9]+.[0-9]+*\"]},\"workflow_dispatch\":{}}"
triggers_exact() { [ "$(ri triggers "$1")" = "$expected_triggers" ]; } # workflow
triggers_changed() { local t; t="$(ri triggers "$1")" && [ -n "$t" ] && [ "$t" != "$expected_triggers" ]; } # workflow: read, and different
parses() { ri triggers "$1" > /dev/null; } # workflow: a weakened copy must be read, not refused, for its rejection to count
names_input() { ri paths "$1" > "$work/ri-paths.txt" && grep -qxF -- "$2" "$work/ri-paths.txt"; } # workflow pattern
inputs_exact() { ri paths "$1" > "$work/ri-paths.txt" && printf '%s\n' "${release_inputs[@]}" | cmp -s - "$work/ri-paths.txt"; } # workflow
all_match() { [ -s "$2" ] && ri match "$1" < "$2" > "$work/ri-matched.txt" && cmp -s "$2" "$work/ri-matched.txt"; } # workflow non-empty list
none_match() { [ -s "$2" ] && ri match "$1" < "$2" > "$work/ri-matched.txt" && [ ! -s "$work/ri-matched.txt" ]; } # workflow non-empty list
# Weakened copies of release.yml: without a pattern, with lines added after a given line (a structural one, so that
# they do not depend on any one input), with exactly these patterns.
wf_without() { grep -vxF "      - \"$2\"" "$release_yml" > "$1"; }
wf_adding() { awk -v after="$2" -v add="$3" '{print} $0 == after {print add}' "$release_yml" > "$1"; }
wf_paths() { local out="$1"; shift
  awk -v list="$(printf '      - "%s"\n' "$@")" 'f && /^      [-#]/ {next} {f = 0} {print} $0 == "    paths:" {print list; f = 1}' "$release_yml" > "$out"; }

# The repository's tracked files by kind, and paths that do not exist yet.
git -C "$here_root" ls-files -z | tr '\0' '\n' > "$work/tracked.txt"
folder_files() { grep -F "$1/" "$work/tracked.txt" | grep "^$1/"; printf '%s\n' "$1/New/Deeper/new-file.cs"; }
grep -E '^docs/|^examples/|^\.github/(docs-policy|release-notes)/|^[^/]+\.md$|^LICENSE$' "$work/tracked.txt" > "$work/files-docs.txt"
printf '%s\n' docs/ai-context/new-record.md NEW.md >> "$work/files-docs.txt"
{ grep '^site/' "$work/tracked.txt"; printf '%s\n' site/index.html site/new/page.html; } > "$work/files-site.txt"
{ grep -E '^Campertron(Unit|GUI|Web)Tests/' "$work/tracked.txt" | grep -vE '\.csproj$'; printf '%s\n' .github/workflows/ci.yml .github/workflows/pages.yml; } > "$work/files-dev.txt"
printf '%s\n' CampertronWebTests/New.cs CampertronLibraryTools/New.cs CampertronConsoleExtras/New.cs assets/icon.svg assets/iconography/new.svg \
  assets/other/logo.svg > "$work/files-neighbours.txt"

# 1. The triggers, exactly: a v tag (publishing), a manual run and pull requests to master with the release inputs.
triggers_exact "$release_yml"
expect_rc "release inputs: the triggers are exactly a v tag, a manual run and pull requests to master with the inputs (no other event, types, paths-ignore or inputs)" 0 $?
ri triggers "$release_yml" | python3 -I -c 'import json, sys; t = json.load(sys.stdin)
sys.exit(0 if sorted(t["push"]) == ["tags"] and sorted(t["pull_request"]) == ["branches", "paths"] else 1)'
expect_rc "release inputs: the tag trigger has no path or branch filter, and the pull-request filter is no tag trigger (two separate conditions)" 0 $?
# 2. The four shipped folders that were missing (D105), each by its precise folder pattern, with every file in it.
for p in "${release_inputs_added[@]}"; do
  folder="${p%/\*\*}"; folder_files "$folder" > "$work/files-folder.txt"
  names_input "$release_yml" "$p"; expect_rc "release inputs: the pull-request filter names $p (D105)" 0 $?
  all_match "$release_yml" "$work/files-folder.txt"; expect_rc "release inputs: every file in $folder/, and a new one, starts the dry run" 0 $?
  wf_without "$work/wf.yml" "$p"
  { parses "$work/wf.yml" && ! names_input "$work/wf.yml" "$p" && ! all_match "$work/wf.yml" "$work/files-folder.txt"; }
  expect_rc "release inputs: a filter without $p is rejected (its pattern and its files)" 0 $?
done
# 3. Every earlier release input stays, and the list is exactly the maintained one: no other, broader or negated pattern.
for p in "${release_inputs_before[@]}"; do
  names_input "$release_yml" "$p"; expect_rc "release inputs: the pull-request filter still names $p" 0 $?
  wf_without "$work/wf.yml" "$p"; { triggers_changed "$work/wf.yml" && ! inputs_exact "$work/wf.yml"; }
  expect_rc "release inputs: a filter without $p is rejected" 0 $?
done
inputs_exact "$release_yml"; expect_rc "release inputs: the filter is exactly the maintained list, in order (no other, broader or negated pattern)" 0 $?
# 4. From the projects themselves: every project the archives and the image are built from (the binaries matrix's
# projects, the Dockerfile's, and the projects they reference) is named by its folder's /**, and every file they read
# from outside their folders (the app icon) starts the dry run.
roots="$(workflow_job binaries | sed -n 's/^          - { name: [a-z]*, project: \([A-Za-z]*\) }$/\1/p' | tr '\n' ' ')"
{ [ "$roots" = 'CampertronConsole CampertronGUI ' ] && grep -qF 'RUN dotnet publish "./CampertronConsole.csproj"' "$here_root/CampertronConsole/Dockerfile"; }
expect_rc "release inputs: the archives are built from CampertronConsole and CampertronGUI, and the image from CampertronConsole" 0 $?
ri project-inputs "$here_root" CampertronConsole/CampertronConsole.csproj CampertronGUI/CampertronGUI.csproj > "$work/project-inputs.txt"; rc=$?
unnamed=0; while read -r kind folder; do [ "$kind" != project ] || names_input "$release_yml" "$folder/**" || unnamed=1; done < "$work/project-inputs.txt"
{ [ "$rc" -eq 0 ] && grep -qx 'project CampertronConsole' "$work/project-inputs.txt" && grep -qx 'project CampertronGUI' "$work/project-inputs.txt" && [ "$unnamed" = 0 ]; }
expect_rc "release inputs: every project the archives and the image are built from, with its references, is named by its folder's /**" 0 $?
sed -n 's/^file //p' "$work/project-inputs.txt" > "$work/files-external.txt"
all_match "$release_yml" "$work/files-external.txt"
expect_rc "release inputs: every file those projects read from outside their folders (the app icon) starts the dry run" 0 $?
# 5. What stays outside: documentation, site/ (published by pages.yml), the test projects' sources, the other workflows,
# and folders that only share a prefix with a shipped one.
none_match "$release_yml" "$work/files-docs.txt"
expect_rc "release inputs: documentation (docs/, examples/, the docs policy, the release notes, *.md at the root, LICENSE) does not start the dry run" 0 $?
{ ! names_input "$release_yml" "site/**" && none_match "$release_yml" "$work/files-site.txt"; }
expect_rc "release inputs: site/ (published by pages.yml after its own approval) does not start the dry run" 0 $?
none_match "$release_yml" "$work/files-dev.txt"
expect_rc "release inputs: the test projects' sources (not their project files) and the CI and Pages workflows do not start the dry run" 0 $?
none_match "$release_yml" "$work/files-neighbours.txt"
expect_rc "release inputs: folders that only share a prefix with a shipped one (CampertronWebTests/, assets/icon.svg, assets/other/) do not start it" 0 $?
awk '/^(CampertronConsole|CampertronGUI|CampertronLibrary|CampertronWeb|assets\/icon|\.github\/release-policy)\// || /\.csproj$/ \
  || $0 == ".github/workflows/release.yml" || $0 == "global.json" || $0 == ".dockerignore" || $0 == "Campertron.sln"' "$work/tracked.txt" > "$work/files-expected.txt"
{ [ -s "$work/files-expected.txt" ] && ri match "$release_yml" < "$work/tracked.txt" | cmp -s - "$work/files-expected.txt"; }
expect_rc "release inputs: every tracked file starts the dry run exactly when it is a release input (shipped folders, release policy, project files, solution, global.json, .dockerignore, release.yml)" 0 $?
# 6. Weakened filters are rejected: site/, documentation, a root-wide or imprecise pattern, a negation.
wf_adding "$work/wf.yml" '    paths:' '      - "site/**"'
{ parses "$work/wf.yml" && ! { ! names_input "$work/wf.yml" "site/**" && none_match "$work/wf.yml" "$work/files-site.txt"; }; }; expect_rc "release inputs: site/** added to the filter is rejected" 0 $?
for p in "docs/**" "**/*.md" "README.md"; do
  wf_adding "$work/wf.yml" '    paths:' "      - \"$p\""
  { parses "$work/wf.yml" && ! none_match "$work/wf.yml" "$work/files-docs.txt"; }; expect_rc "release inputs: a documentation pattern ($p) added to the filter is rejected" 0 $?
done
wf_paths "$work/wf.yml" "**"; { parses "$work/wf.yml" && ! inputs_exact "$work/wf.yml" && ! none_match "$work/wf.yml" "$work/files-docs.txt" && ! none_match "$work/wf.yml" "$work/files-dev.txt"; }
expect_rc "release inputs: a root-wide pattern (**) in place of the maintained list is rejected" 0 $?
wf_paths "$work/wf.yml" "${release_inputs_before[@]}" "Campertron*/**" "assets/**"; { parses "$work/wf.yml" && ! none_match "$work/wf.yml" "$work/files-neighbours.txt"; }
expect_rc "release inputs: imprecise patterns (Campertron*/**, assets/**) in place of the four folders are rejected" 0 $?
wf_paths "$work/wf.yml" "${release_inputs[@]}" "!CampertronWeb/**"; folder_files CampertronWeb > "$work/files-folder.txt"; { parses "$work/wf.yml" && ! all_match "$work/wf.yml" "$work/files-folder.txt"; }
expect_rc "release inputs: a negated pattern (!CampertronWeb/**) after the list is rejected" 0 $?
# 7. A changed trigger is rejected: another event, an event type, a branch filter on the tag push, paths-ignore, inputs.
wf_adding "$work/wf.yml" '  workflow_dispatch: {}' $'  pull_request_target:\n    branches: [master]'; triggers_changed "$work/wf.yml"
expect_rc "release inputs: a new trigger (pull_request_target) is rejected" 0 $?
wf_adding "$work/wf.yml" '    branches: [master]' '    types: [opened, synchronize, labeled]'; triggers_changed "$work/wf.yml"
expect_rc "release inputs: event types on the pull-request trigger are rejected" 0 $?
wf_adding "$work/wf.yml" '  push:' '    branches: [master]'; triggers_changed "$work/wf.yml"
expect_rc "release inputs: a branch push added to the tag trigger is rejected" 0 $?
wf_adding "$work/wf.yml" '    branches: [master]' $'    paths-ignore:\n      - "docs/**"'; triggers_changed "$work/wf.yml"
expect_rc "release inputs: paths-ignore on the pull-request trigger is rejected" 0 $?
sed 's/^  workflow_dispatch: {}$/  workflow_dispatch:\n    inputs:\n      publish:\n        type: boolean/' "$release_yml" > "$work/wf.yml"; triggers_changed "$work/wf.yml"
expect_rc "release inputs: an input of the manual run is rejected" 0 $?
# 8. The reader refuses what it does not read, so no check passes on a misread workflow; GitHub's filter rules.
refused() { ri triggers "$1" > /dev/null 2> "$work/ri-err.txt"; [ $? -eq 2 ] && grep -q "$2" "$work/ri-err.txt"; } # workflow message
sed 's/^    paths:$/    paths: *inputs/' "$release_yml" > "$work/wf.yml"; refused "$work/wf.yml" 'an anchor, alias'
expect_rc "release-inputs.py: an alias is refused" 0 $?
sed 's/^  workflow_dispatch: {}$/  workflow_dispatch: {inputs: {}}/' "$release_yml" > "$work/wf.yml"; refused "$work/wf.yml" 'a value this reader does not read'
expect_rc "release-inputs.py: a flow mapping is refused" 0 $?
sed 's/^    branches: \[master\]$/    branches:\t[master]/' "$release_yml" > "$work/wf.yml"; refused "$work/wf.yml" 'a tab'
expect_rc "release-inputs.py: a tab is refused" 0 $?
wf_adding "$work/wf.yml" '  workflow_dispatch: {}' $'  pull_request:\n    branches: [master]'; refused "$work/wf.yml" 'the key pull_request again'
expect_rc "release-inputs.py: a duplicate key is refused" 0 $?
wf_adding "$work/wf.yml" 'name: Release' '"on": [push]'; refused "$work/wf.yml" "an 'on' key spelled another way"
expect_rc "release-inputs.py: a second, quoted on key is refused" 0 $?
awk '$0 == "    paths:" {f = 1; print; next} f && /^      - / {sub(/^  /, ""); print; next} {f = 0; print}' "$release_yml" > "$work/wf.yml"
refused "$work/wf.yml" "a sequence at its key's indentation"; expect_rc "release-inputs.py: a sequence at its key's indentation is refused (not read as no filter)" 0 $?
expect_eq "path filter: docs/** matches what is in docs/ only" "docs/a.md docs/a/b.md " "$(printf '%s\n' docs/a.md docs/a/b.md docs site/docs/a.md | ri filter 'docs/**' | tr '\n' ' ')"
expect_eq "path filter: **/*.csproj matches project files at any depth, the root included" "A.csproj A/B/C.csproj " "$(printf '%s\n' A.csproj A/B/C.csproj A.csproj.user | ri filter '**/*.csproj' | tr '\n' ' ')"
expect_eq "path filter: * does not match a /" "README.md " "$(printf '%s\n' README.md docs/a.md | ri filter '*.md' | tr '\n' ' ')"
expect_eq "path filter: a later ! pattern excludes, a later pattern includes again" "a/c | a/b/c " \
  "$(printf '%s\n' a/c a/b/c | ri filter 'a/**' '!a/b/**' | tr '\n' ' ')| $(printf '%s\n' a/b/c | ri filter '!a/b/**' 'a/**' | tr '\n' ' ')"
{ ! ri filter 'docs/?.md' < /dev/null 2> /dev/null && ! ri filter '[ab]/**' < /dev/null 2> /dev/null && ! ri filter 'a+/**' < /dev/null 2> /dev/null; }
expect_rc "path filter: syntax the check does not implement (?, [], +) is refused" 0 $?
# 9. A pull request can't publish: verify's mode step (run verbatim) gives a dry run for every event but a v tag push,
# which publishes only a SemVer tag on master without a release (stub git and gh).
mkdir -p "$work/bin-mode"
printf '#!/usr/bin/env bash\n[ "$1 $2" = "merge-base --is-ancestor" ] && exit "${STUB_ANCESTOR:-0}"\nexit 99\n' > "$work/bin-mode/git"
printf '#!/usr/bin/env bash\n[ "$1 $2" = "release view" ] && exit "${STUB_RELEASE:-1}"\nexit 99\n' > "$work/bin-mode/gh"; chmod +x "$work/bin-mode/git" "$work/bin-mode/gh"
release_mode() { rm -f "$work/mode-out.txt"; run_step "Determine version and mode" PATH="$work/bin-mode:$PATH" GITHUB_OUTPUT="$work/mode-out.txt" \
  GITHUB_STEP_SUMMARY="$work/mode-summary.txt" RUN_NUMBER=7 GITHUB_SHA=0123abc GITHUB_REPOSITORY=example-owner/Campertron GH_TOKEN= \
  EVENT_NAME=pull_request REF_TYPE=branch REF_NAME=136/merge "$@"; } # [VAR=value...]
mode_is() { [ "$1" -eq 0 ] && [ "$(tr '\n' ' ' < "$work/mode-out.txt")" = "$2" ]; } # rc outputs
release_mode; mode_is $? 'version=0.0.0-dryrun.7 prerelease=true publish=false '
expect_rc "release mode: a pull-request run is a dry run (publish=false, version 0.0.0-dryrun.<run number>)" 0 $?
release_mode REF_TYPE=tag REF_NAME=v1.2.3; mode_is $? 'version=0.0.0-dryrun.7 prerelease=true publish=false '
expect_rc "release mode: a pull-request event never publishes, whatever its ref" 0 $?
release_mode EVENT_NAME=workflow_dispatch REF_NAME=master; mode_is $? 'version=0.0.0-dryrun.7 prerelease=true publish=false '
expect_rc "release mode: a manual run is a dry run" 0 $?
release_mode EVENT_NAME=push REF_TYPE=tag REF_NAME=v1.2.3; mode_is $? 'version=1.2.3 prerelease=false publish=true '
expect_rc "release mode: a SemVer v tag on master without a release publishes (the only publishing mode, unchanged)" 0 $?
{ release_mode EVENT_NAME=push REF_TYPE=tag REF_NAME=v1.2; [ $? -ne 0 ] && [ ! -s "$work/mode-out.txt" ]; } \
  && { release_mode EVENT_NAME=push REF_TYPE=tag REF_NAME=v1.2.3 STUB_ANCESTOR=1; [ $? -ne 0 ] && [ ! -s "$work/mode-out.txt" ]; } \
  && { release_mode EVENT_NAME=push REF_TYPE=tag REF_NAME=v1.2.3 STUB_RELEASE=0; [ $? -ne 0 ] && [ ! -s "$work/mode-out.txt" ]; }
expect_rc "release mode: a tag that is not SemVer, not on master or already released fails verify, so nothing runs" 0 $?
publishing_gated() { local j; [ "$(grep -c '^    if: ' "$release_yml")" = 2 ] || return 1 # both publishing jobs, and no other job, have a job condition
  # grep reads the whole job (no -q): with pipefail, grep -q's early exit can end awk with SIGPIPE and fail the check (§DC)
  for j in docker-publish release; do [ "$(workflow_job "$j" | grep -c '^    if: ')" = 1 ] && workflow_job "$j" | grep -xF "    $publish_if" > /dev/null || return 1; done; }
publishing_gated; expect_rc "release inputs: only the two publishing jobs have a condition, and it needs verify to publish on a v tag push, so every dry run skips them" 0 $?
# 10. Permissions, jobs and secrets are unchanged.
perm_of() { if [ -n "$1" ]; then workflow_job "$1" | awk '$0 == "    permissions:" {f = 1; next} f && /^      [a-z-]+: [a-z]+$/ {sub(/^ +/, ""); print; next} f {exit}'
  else awk '$0 == "permissions:" {f = 1; next} f && /^  [a-z-]+: [a-z]+$/ {sub(/^ +/, ""); print; next} f {exit}' "$release_yml"; fi | tr '\n' ' '; } # [job]
{ [ "$(perm_of '')" = 'contents: read ' ] && [ "$(perm_of docker-publish)" = 'contents: read packages: write id-token: write attestations: write ' ] \
  && [ "$(perm_of release)" = 'contents: write id-token: write attestations: write ' ] && [ "$(grep -cE '^ *permissions:' "$release_yml")" = 3 ]; }
expect_rc "release inputs: the permissions are unchanged (read by default; exactly docker-publish's and release's own; no other)" 0 $?
[ "$(awk '/^jobs:$/ {f = 1; next} f && /^  [^ #]/ {print $1}' "$release_yml" | tr '\n' ' ')" = 'verify: binaries: smoke: native-smoke: emulated-smoke: docker: container-smoke: assemble: docker-publish: release: ' ]
expect_rc "release inputs: the jobs are unchanged (no job added or removed)" 0 $?
! grep -qi 'secrets' "$release_yml"; expect_rc "release inputs: no secret is referenced in any form (secrets.X, secrets[...], secrets: inherit)" 0 $?

echo "=== $passed passed, $failed failed"
[ "$failed" -eq 0 ]
