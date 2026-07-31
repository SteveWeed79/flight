#!/usr/bin/env python3
"""
Shrink the built script to fit the Programmable Block editor.

The in-game editor tops out around 100,000 characters — PAM ships at 92k for
exactly this reason. VEIN's source is heavily commented, so the comments are the
overwhelming majority of the bulk and stripping them is enough on its own.

Two levels:

  default     Strip comments, collapse indentation and blank lines. Identifiers
              are untouched, so an in-game stack trace still names real methods
              and you can read the thing if you have to.

  --aggressive
              Additionally shorten private identifiers. Smaller, but error
              messages become meaningless. Only reach for this if the default
              output still does not fit.

String and character literals are preserved exactly; a naive regex-based
comment stripper eats the "//" in a URL inside a string and silently truncates
the rest of the line.

Usage:  python3 tools/minify.py [--aggressive]
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, "dist")
SRC_FILE = os.path.join(DIST, "VEIN.cs")
OUT_FILE = os.path.join(DIST, "VEIN.min.cs")

# Never rename these: the game calls them, or they are part of an API contract.
PROTECTED = {
    "Program", "Main", "Save", "Echo", "Storage", "Runtime", "Me",
    "GridTerminalSystem", "IGC", "Runtime",
}

BANNER = """// VEIN — Vectored Extraction & Intelligent Navigation
// Minified build. Readable source: src/ in the project repository.
// Commands: start | stop | home | clear | record start|stop | job set <w> <h> <d>
"""


def strip_comments(text):
    """Remove comments while leaving every literal byte-for-byte intact."""
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""

        # Line comment
        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            i = n if j == -1 else j
            continue

        # Block comment
        if c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            i = n if j == -1 else j + 2
            out.append(" ")
            continue

        # Verbatim string: @"..." where "" is an escaped quote
        if c == "@" and nxt == '"':
            out.append(text[i : i + 2])
            i += 2
            while i < n:
                if text[i] == '"' and text[i + 1 : i + 2] == '"':
                    out.append('""')
                    i += 2
                    continue
                out.append(text[i])
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue

        # Regular string
        if c == '"':
            out.append(c)
            i += 1
            while i < n:
                if text[i] == "\\":
                    out.append(text[i : i + 2])
                    i += 2
                    continue
                out.append(text[i])
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue

        # Character literal
        if c == "'":
            out.append(c)
            i += 1
            while i < n:
                if text[i] == "\\":
                    out.append(text[i : i + 2])
                    i += 2
                    continue
                out.append(text[i])
                if text[i] == "'":
                    i += 1
                    break
                i += 1
            continue

        out.append(c)
        i += 1

    return "".join(out)


def collapse(text):
    """Drop indentation, blank lines and #region markers."""
    lines = []
    for line in text.split("\n"):
        s = line.strip()
        if not s:
            continue
        if s.startswith("#region") or s.startswith("#endregion"):
            continue
        lines.append(s)
    return "\n".join(lines)


def collect_renamable(text):
    """Identifiers declared in this script that are safe to shorten."""
    names = set()

    patterns = [
        r"\bvoid\s+([A-Za-z_]\w*)\s*\(",
        r"\bbool\s+([A-Za-z_]\w*)\s*[\(;=]",
        r"\bint\s+([A-Za-z_]\w*)\s*[\(;=]",
        r"\bdouble\s+([A-Za-z_]\w*)\s*[\(;=]",
        r"\bfloat\s+([A-Za-z_]\w*)\s*[\(;=]",
        r"\bstring\s+([A-Za-z_]\w*)\s*[\(;=]",
        r"\blong\s+([A-Za-z_]\w*)\s*[\(;=]",
    ]
    for p in patterns:
        for m in re.finditer(p, text):
            names.add(m.group(1))

    return {n for n in names if n not in PROTECTED and len(n) > 3}


def short_names():
    alphabet = "abcdefghijklmnopqrstuvwxyz"
    for a in alphabet:
        yield "_" + a
    for a in alphabet:
        for b in alphabet:
            yield "_" + a + b


def rename(text, names):
    gen = short_names()
    used = 0
    for name in sorted(names, key=len, reverse=True):
        short = next(gen)
        text = re.sub(r"(?<![\w\.])" + re.escape(name) + r"(?![\w])", short, text)
        used += 1
    return text, used


def main():
    aggressive = "--aggressive" in sys.argv

    if not os.path.exists(SRC_FILE):
        sys.exit("dist/VEIN.cs not found — run tools/build.py first")

    with open(SRC_FILE, "r", encoding="utf-8") as fh:
        original = fh.read()

    text = collapse(strip_comments(original))
    renamed = 0

    if aggressive:
        text, renamed = rename(text, collect_renamable(text))

    text = BANNER + text + "\n"

    with open(OUT_FILE, "w", encoding="utf-8") as fh:
        fh.write(text)

    before = len(original)
    after = len(text)
    print("Minified dist/VEIN.cs -> dist/VEIN.min.cs")
    print("  %7d chars  ->  %7d chars  (%.0f%% smaller)"
          % (before, after, 100.0 * (before - after) / before))
    if aggressive:
        print("  %d identifiers shortened" % renamed)

    limit = 100000
    if after <= limit:
        print("  fits the in-game editor (limit ~%d)" % limit)
    else:
        print("  STILL OVER the ~%d character editor limit" % limit)
        if not aggressive:
            print("  try: python3 tools/minify.py --aggressive")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
