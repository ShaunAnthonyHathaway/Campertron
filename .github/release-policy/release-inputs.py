#!/usr/bin/env python3
"""The release inputs of the pull-request Release dry run (open question #53, decision D105).

Used by test-release-policy.sh, never by a workflow. Python's standard library only, like web-smoke.py: no YAML
package, no network. Each reader refuses what it does not understand (exit code 2) instead of guessing, so a test
built on it fails rather than passes on a misread file.

  release-inputs.py triggers WORKFLOW     the workflow's top-level `on:` block as canonical JSON
  release-inputs.py paths WORKFLOW        its pull_request path filter, one pattern per line, in order
  release-inputs.py match WORKFLOW        repository paths on standard input; prints those that filter matches
  release-inputs.py filter PATTERN...     the same for the given patterns, in order
  release-inputs.py project-inputs ROOT PROJECT...
      the MSBuild inputs of the given project files (paths relative to ROOT) and of every project they reference:
      "project <folder>" for each project's folder and "file <path>" for each file one of them reads from outside
      its own folder (an item, a property such as ApplicationIcon, an Import, Directory.Build.*, NuGet.config)

The `on:` reader reads block mappings, block sequences of scalars, `{}`, `[]` and flow sequences of scalars, plain,
'single-' or "double-quoted" (without escapes), and comments. It refuses anchors, aliases, tags, multi-line and block
scalars, flow mappings with content, tabs, duplicate keys, quoted or complex keys, a sequence at its key's indentation,
plain values YAML would read as booleans, null or numbers, and any `on` key other than one plain `on:`.

The filter follows GitHub's rules for paths: `*` matches any characters but `/`, `**` any characters (and `**/` also
no folder at all), a leading `!` excludes, and a later pattern overrides an earlier one. It refuses `?`, `+`, `[`, `]`,
`{`, `}` and `\\`, which GitHub reads as quantifiers or character classes and this check does not implement.
"""
import json
import os
import posixpath
import re
import sys
import xml.etree.ElementTree as ET


class Refused(Exception):
    """The input is outside what this reader understands."""


KEY = re.compile(r"[A-Za-z_][A-Za-z0-9_-]*\Z")
PLAIN = re.compile(r"[A-Za-z_./][A-Za-z0-9_./-]*\Z")
NOT_A_STRING = re.compile(r"(?i)(true|false|yes|no|on|off|y|n|null)\Z")
KEY_LINE = re.compile(r"([^\s:]+):(?: (.*))?\Z")


def strip_comment(text):
    """The text without a trailing comment: a '#' at its start or after a space, outside quotes."""
    quote = None
    for i, c in enumerate(text):
        if quote:
            if c == quote:
                quote = None
        elif c in "\"'":
            quote = c
        elif c == "#" and (i == 0 or text[i - 1] == " "):
            return text[:i].rstrip()
    if quote:
        raise Refused(f"an unterminated quoted value: {text}")
    return text.rstrip()


def scalar(text):
    if len(text) >= 2 and text[0] == text[-1] == '"':
        body = text[1:-1]
        if '"' in body or "\\" in body:
            raise Refused(f"a double-quoted value with a quote or an escape: {text}")
        return body
    if len(text) >= 2 and text[0] == text[-1] == "'":
        body = text[1:-1]
        if "'" in body.replace("''", ""):
            raise Refused(f"a single-quoted value with a lone quote: {text}")
        return body.replace("''", "'")
    if PLAIN.match(text) and not NOT_A_STRING.match(text):
        return text
    raise Refused(f"a value this reader does not read: {text}")


def value(text):
    if text == "{}":
        return {}
    if text == "[]":
        return []
    if text.startswith("["):
        if not text.endswith("]"):
            raise Refused(f"a flow sequence that does not end on its line: {text}")
        items = [item.strip() for item in text[1:-1].split(",")]
        if any(not item or item[0] in "[]{}" for item in items):
            raise Refused(f"a flow sequence with an empty or nested item: {text}")
        return [scalar(item) for item in items]
    return scalar(text)


def parse_block(lines, i, indent):
    """The mapping or sequence whose lines start at lines[i], all at `indent`; returns (node, next index)."""
    if lines[i][2] == "-" or lines[i][2].startswith("- "):
        node = []
        while i < len(lines) and lines[i][1] == indent:
            number, _, text = lines[i]
            if not text.startswith("- "):
                raise Refused(f"line {number}: a sequence item that is not '- value'")
            node.append(value(text[2:].strip()))
            i += 1
    else:
        node = {}
        while i < len(lines) and lines[i][1] == indent:
            number, _, text = lines[i]
            match = KEY_LINE.match(text)
            if not match or not KEY.match(match.group(1)):
                raise Refused(f"line {number}: not a plain 'key:' or 'key: value' line: {text}")
            key, rest = match.group(1), match.group(2)
            if key in node:
                raise Refused(f"line {number}: the key {key} again")
            i += 1
            if rest:
                node[key] = value(rest.strip())
            elif i < len(lines) and lines[i][1] > indent:
                node[key], i = parse_block(lines, i, lines[i][1])
            elif i < len(lines) and lines[i][1] == indent and lines[i][2].startswith("-"):
                raise Refused(f"line {lines[i][0]}: a sequence at its key's indentation")
            else:
                node[key] = None
    if i < len(lines) and lines[i][1] > indent:
        raise Refused(f"line {lines[i][0]}: unexpected indentation")
    return node, i


def read_on(text):
    """The workflow's top-level `on:` mapping."""
    lines = text.split("\n")
    top = [(n, line) for n, line in enumerate(lines, 1) if line[:1] not in ("", " ", "\t", "#")]
    starts = []
    for number, line in top:
        bare = strip_comment(line)
        if bare in ("---", "...") or "\t" in line:
            raise Refused(f"line {number}: a document marker or a tab")
        if bare == "on:":
            starts.append(number)
        elif re.match(r"""(?i)["']?(on|true|yes|y)["']?\s*:""", bare):
            raise Refused(f"line {number}: an 'on' key spelled another way: {bare}")
    if len(starts) != 1:
        raise Refused(f"{len(starts)} plain top-level 'on:' lines, not one")
    block = []
    for number in range(starts[0] + 1, len(lines) + 1):
        line = lines[number - 1]
        if line[:1] not in ("", " ", "\t", "#"):
            break
        if "\t" in line:
            raise Refused(f"line {number}: a tab")
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        content = strip_comment(stripped)
        for indicator in ("&", "*", "!", "|", ">", "%", "@", "`", "?"):
            if content.startswith(indicator) or content.startswith("- " + indicator) or (": " + indicator) in content:
                raise Refused(f"line {number}: an anchor, alias, tag, block scalar or complex key: {content}")
        block.append((number, len(line) - len(line.lstrip(" ")), content))
    if not block:
        raise Refused("an empty 'on:' block")
    node, end = parse_block(block, 0, block[0][1])
    if end != len(block) or not isinstance(node, dict):
        raise Refused("an 'on:' block that is not one mapping")
    return node


def pull_request_paths(text):
    events = read_on(text)
    pull_request = events.get("pull_request")
    paths = pull_request.get("paths") if isinstance(pull_request, dict) else None
    if not isinstance(paths, list) or not paths or not all(isinstance(p, str) and p for p in paths):
        raise Refused("no pull_request path filter, or one that is not a list of patterns")
    return paths


def glob_regex(pattern):
    if not pattern or any(c in pattern for c in "?+[]{}\\"):
        raise Refused(f"the pattern {pattern!r} uses filter syntax this check does not implement")
    out, i = [], 0
    while i < len(pattern):
        if pattern.startswith("**/", i):
            out.append("(?:.*/)?")
            i += 3
        elif pattern.startswith("**", i):
            out.append(".*")
            i += 2
        elif pattern[i] == "*":
            out.append("[^/]*")
            i += 1
        else:
            out.append(re.escape(pattern[i]))
            i += 1
    return re.compile("".join(out) + r"\Z")


def compile_filter(patterns):
    return [(p.startswith("!"), glob_regex(p[1:] if p.startswith("!") else p)) for p in patterns]


def filter_matches(compiled, path):
    matched = False
    for negative, regex in compiled:
        if regex.match(path):
            matched = not negative
    return matched


MSBUILD_FILES = ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                 "NuGet.Config", "NuGet.config", "nuget.config")


def project_inputs(root, projects):
    root = os.path.realpath(root)
    queue, seen, folders, files = list(projects), set(), set(), set()

    def outside(folder, reference, what):
        """The repository path of a reference that leaves the project's folder, or None for one inside it."""
        ref = reference.strip().replace("\\", "/")
        if "$(" in ref or "@(" in ref or "%(" in ref:
            raise Refused(f"{folder}: {what} {reference!r} is built from MSBuild properties or items")
        if ref.startswith("/") or re.match(r"[A-Za-z]:", ref):
            raise Refused(f"{folder}: {what} {reference!r} is an absolute path")
        path = posixpath.normpath(posixpath.join(folder, ref))
        if path.startswith(folder + "/"):
            return None
        if path == ".." or path.startswith("../"):
            raise Refused(f"{folder}: {what} {reference!r} leaves the repository")
        if "*" in path:
            raise Refused(f"{folder}: {what} {reference!r} is a wildcard outside its folder")
        if not os.path.isfile(os.path.join(root, path)):
            raise Refused(f"{folder}: {what} {reference!r} is not a file of the repository")
        return path

    while queue:
        project = posixpath.normpath(queue.pop(0).replace("\\", "/"))
        if project in seen:
            continue
        seen.add(project)
        folder = posixpath.dirname(project)
        if not folder or "/" in folder or not project.endswith(".csproj"):
            raise Refused(f"{project}: a released project is a .csproj in a top-level folder of its own")
        if not os.path.isfile(os.path.join(root, project)):
            raise Refused(f"{project}: no such project file")
        folders.add(folder)
        for directory in (folder, ""):
            for name in MSBUILD_FILES:
                candidate = posixpath.join(directory, name) if directory else name
                if os.path.isfile(os.path.join(root, candidate)):
                    files.add(candidate)
        document = ET.parse(os.path.join(root, project)).getroot()
        for element in document.iter():
            tag = element.tag.split("}")[-1]
            if tag == "ProjectReference":
                path = outside(folder, element.get("Include", ""), "ProjectReference")
                if path is None:
                    raise Refused(f"{folder}: a ProjectReference inside its own folder")
                queue.append(path)
            elif tag == "Import" and element.get("Project") is not None:
                path = outside(folder, element.get("Project"), "Import")
                if path:
                    files.add(path)
            elif element.get("Include") is not None:
                path = outside(folder, element.get("Include"), f"the {tag} item")
                if path:
                    files.add(path)
        for group in document.iter():
            if group.tag.split("}")[-1] != "PropertyGroup":
                continue
            for prop in group:
                text = (prop.text or "").strip().replace("\\", "/")
                if text.startswith("../"):
                    path = outside(folder, text, f"the property {prop.tag.split('}')[-1]}")
                    if path:
                        files.add(path)
    return sorted(folders), sorted(files)


def main(argv):
    try:
        if len(argv) == 3 and argv[1] in ("triggers", "paths", "match"):
            with open(argv[2], encoding="utf-8") as handle:
                text = handle.read()
            if argv[1] == "triggers":
                print(json.dumps(read_on(text), sort_keys=True, separators=(",", ":")))
                return 0
            if argv[1] == "paths":
                print("\n".join(pull_request_paths(text)))
                return 0
            compiled = compile_filter(pull_request_paths(text))
        elif len(argv) >= 3 and argv[1] == "filter":
            compiled = compile_filter(argv[2:])
        elif len(argv) >= 4 and argv[1] == "project-inputs":
            folders, files = project_inputs(argv[2], argv[3:])
            for folder in folders:
                print(f"project {folder}")
            for path in files:
                print(f"file {path}")
            return 0
        else:
            print("usage:\n" + __doc__.split("\n\n")[2], file=sys.stderr)
            return 2
        for line in sys.stdin:
            path = line.rstrip("\n")
            if path and filter_matches(compiled, path):
                print(path)
        return 0
    except (Refused, OSError, UnicodeDecodeError, ET.ParseError) as error:
        print(f"release-inputs: refused: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
