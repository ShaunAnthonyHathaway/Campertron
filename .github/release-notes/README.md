# Curated release notes

Each release's reviewed, version-specific notes live here. A publishing tag run of
`.github/workflows/release.yml` combines the file with the generated sections and publishes
exactly that body in the draft, before the release becomes public and immutable (decision D30,
`docs/ai-context/build-and-test.md` §Release workflow).

## File names
`v<VERSION>.md`, where `<VERSION>` is the SemVer version of the tag without the leading `v`:

- beta: `v0.1.0-beta.10.md` for tag `v0.1.0-beta.10`
- stable: `v0.1.0.md` for tag `v0.1.0`, `v1.2.3.md` for tag `v1.2.3`

Only `README.md` and `v<MAJOR.MINOR.PATCH[-prerelease]>.md` files may be here. Names that differ only by
case, other files and subdirectories fail the check.

## Content: a fragment, not a complete body
- The first line is exactly `## What's new in <VERSION>`, without the `v`
  (for example `## What's new in 0.1.0-beta.10`).
- Write only the curated sections. `## Upgrading from ...` and `## Known limitations` are optional.
- Don't repeat what is generated: the `# Campertron <VERSION>` title or any other level-one heading,
  the status line (`**Pre-release version.**` / `**Stable release.**`), statements about the GitHub
  release status (for example "marked Latest" or "published as a GitHub prerelease"), the
  `## Downloads`, `## Compatibility with older versions`, `## Docker` and `## Verify your download`
  sections, download-table rows, and this version's archive file names. Ordinary prose such as
  "Upgrading from 0.1.0-beta.9" or "there is no `latest` tag for beta versions" is fine.
- UTF-8, LF line endings, ending with exactly one newline and no trailing blank line. The bytes are
  published as they are; nothing normalizes them.

The published body is the generated title and status (lines 1-4), this file byte for byte, one blank line,
then the generated sections. It must stay under GitHub's 125,000-character limit.

## Review and approval
1. Open a pull request that adds only `v<VERSION>.md`. Make it the last content PR before tagging, so the
   notes describe everything in the tagged commit.
2. CI (`Restore, build, test, vulnerability scan`) validates every file here and shows each composed body
   and its SHA-256 in the job summary. Review the diff and that preview.
3. Merging the PR approves these exact bytes.
4. Pushing the tag `v<VERSION>` on that merge commit is a separate approval: the approval to publish.

A publishing tag requires its exact file in the tagged commit. Without a valid file the release workflow
fails before anything is built or published; it never publishes template-only notes. Pull requests and
manual dry runs never publish (a dry run's synthetic `0.0.0-dryrun.<n>` version uses the template).

Editing the notes of a published release (`gh release edit --notes-file`) is a recovery step only and needs
the maintainer's explicit approval.

The notes of `v0.1.0-beta.4` to `v0.1.0-beta.9` were installed by hand and are intentionally not
backfilled here; their published bodies are the record.
