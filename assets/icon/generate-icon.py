#!/usr/bin/env python3
"""Builds campertron.ico, the unified app icon (decision D89), from the approved SVG sources beside this script.

Each ICO frame is rendered directly from the SVG approved for its size: the size-specific drawings for 16, 20, 24 and
32 px, the canonical drawing for 48, 64, 128 and 256 px. Nothing is resized from another raster. Every frame is a
32-bit RGBA PNG; the ICO is written with no other data, so the same renderer always gives the same bytes.

Needs Python 3 (standard library only) and rsvg-convert (librsvg). Never needs the network. Run it from anywhere:
    python3 -I assets/icon/generate-icon.py           writes campertron.ico
    python3 -I assets/icon/generate-icon.py --check   renders again and fails unless campertron.ico is identical
"""
import os
import re
import struct
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
ICO = os.path.join(HERE, "campertron.ico")
# frame size -> approved source (the mapping D89's approved design fixes)
SOURCES = {16: "campertron-icon-small-16.svg", 20: "campertron-icon-small-20.svg",
           24: "campertron-icon-small-24.svg", 32: "campertron-icon-small-32.svg",
           48: "campertron-icon.svg", 64: "campertron-icon.svg", 128: "campertron-icon.svg", 256: "campertron-icon.svg"}
VIEWBOX = {"campertron-icon.svg": 512, "campertron-icon-small-16.svg": 16, "campertron-icon-small-20.svg": 20,
           "campertron-icon-small-24.svg": 24, "campertron-icon-small-32.svg": 32}
# the approved palette; nothing else may appear in a source
PALETTE = {"#3A7C7E", "#134347", "#0B2B2E", "#0F373A", "#557F84", "#436D72", "#4C767B",
           "#0A1C1F", "#FF3B2E", "#FFD9C2", "#E4521B", "#FFB02E"}
NS = "{http://www.w3.org/2000/svg}"
ELEMENTS = {"svg", "defs", "linearGradient", "radialGradient", "stop", "rect", "circle", "path"}
ATTRIBUTES = {"xmlns", "width", "height", "viewBox", "id", "x1", "y1", "x2", "y2", "offset", "stop-color",
              "stop-opacity", "x", "y", "rx", "cx", "cy", "r", "fill", "d"}


def fail(message):
    sys.exit("generate-icon: " + message)


def check_source(name):
    path = os.path.join(HERE, name)
    if not os.path.isfile(path):
        fail(f"the approved source {name} is missing")
    text = open(path, encoding="utf-8").read()
    try:
        root = ET.fromstring(text)
    except ET.ParseError as error:
        fail(f"{name} is not well-formed XML: {error}")
    n = VIEWBOX[name]
    if root.tag != NS + "svg" or root.get("viewBox") != f"0 0 {n} {n}" or root.get("width") != str(n) or root.get("height") != str(n):
        fail(f"{name} must be an SVG with viewBox 0 0 {n} {n} and width and height {n}")
    for element in root.iter():
        tag = element.tag[len(NS):] if element.tag.startswith(NS) else element.tag
        if tag not in ELEMENTS:
            fail(f"{name} has a <{tag}> element (only plain shapes and gradients: no text, image, script, style, group or metadata)")
        unknown = set(element.attrib) - ATTRIBUTES
        if unknown:
            fail(f"{name} has the attribute(s) {sorted(unknown)} (no href, style, class, transform or visibility)")
    if "<!--" in text or "<?" in text or "data:" in text or re.findall(r"https?://", text) != ["http://"]:
        fail(f"{name} has a comment, processing instruction, data URI or a URL other than the SVG namespace")
    defined = {e.get("id") for e in root.iter() if e.get("id")}
    if re.search(r"url\((?!#)", text) or not set(re.findall(r"url\(#([^)]+)\)", text)) <= defined:
        fail(f"{name} refers to something that is not one of its own gradients")
    colours = {c.upper() for c in re.findall(r"#[0-9A-Fa-f]{6}\b", text)}
    if not colours <= PALETTE or re.search(r"#[0-9A-Fa-f]{3}\b|rgb\(|hsl\(", text):
        fail(f"{name} uses a colour outside the approved palette: {sorted(colours - PALETTE)}")
    return path


def render(path, size, folder):
    out = os.path.join(folder, f"{size}.png")
    subprocess.run(["rsvg-convert", "--width", str(size), "--height", str(size), "--format", "png", "--output", out, path], check=True)
    data = open(out, "rb").read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        fail(f"the {size} px render is not a PNG")
    width, height, depth, colour = struct.unpack(">IIBB", data[16:26])
    if (width, height, depth, colour) != (size, size, 8, 6):
        fail(f"the {size} px render is {width}x{height}, depth {depth}, colour type {colour}; expected {size}x{size} 8-bit RGBA")
    # keep only IHDR, IDAT and IEND: ancillary chunks (rsvg-convert writes a bKGD background hint) carry nothing the
    # icon needs, and a critical chunk of another kind (a palette) means the render is not what this script expects
    kept, kinds, i = data[:8], [], 8
    while i < len(data):
        length, kind = struct.unpack(">I4s", data[i:i + 8])
        chunk = data[i:i + 12 + length]
        kinds.append(kind)
        if kind in (b"IHDR", b"IDAT", b"IEND"):
            kept += chunk
        elif kind[0:1].isupper():
            fail(f"the {size} px render has an unexpected critical chunk {kind!r}")
        i += 12 + length
    if kinds[0] != b"IHDR" or kinds[-1] != b"IEND" or b"IDAT" not in kinds:
        fail(f"the {size} px render is not a complete PNG: {kinds}")
    return kept


def build():
    sources = {size: check_source(name) for size, name in SOURCES.items()}
    with tempfile.TemporaryDirectory(prefix="campertron-icon-") as folder:
        frames = [(size, render(sources[size], size, folder)) for size in sorted(SOURCES)]
    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = len(header) + 16 * len(frames)
    entries, images = b"", b""
    for size, png in frames:
        # width and height 0 mean 256; 32 bits per pixel; the image is the PNG itself
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(png), offset)
        images += png
        offset += len(png)
    return header + entries + images


def main():
    check = sys.argv[1:] == ["--check"]
    if sys.argv[1:] not in ([], ["--check"]):
        fail("usage: generate-icon.py [--check]")
    ico = build()
    if check:
        current = open(ICO, "rb").read() if os.path.isfile(ICO) else b""
        if current != ico:
            fail("campertron.ico differs from a fresh render of the approved sources (run without --check to rebuild it)")
        print(f"generate-icon: campertron.ico matches a fresh render ({len(ico)} bytes, {len(SOURCES)} frames)")
        return
    with open(ICO, "wb") as out:
        out.write(ico)
    print(f"generate-icon: wrote campertron.ico ({len(ico)} bytes, frames {', '.join(str(s) for s in sorted(SOURCES))} px)")


if __name__ == "__main__":
    main()
