#!/usr/bin/env python3
"""
Shrink the built script to fit the Programmable Block editor.

The in-game editor tops out around 100,000 characters — PAM ships at 92k for
exactly this reason. VEIN's source is heavily commented, so the comments are the
overwhelming majority of the bulk and stripping them is enough on its own.

Four passes, cheapest and safest first:

  1. Strip comments.
  2. Drop indentation and blank lines.
  3. Squeeze the remaining whitespace, including the line breaks themselves.
     Whitespace between two word characters is kept as a single space; anywhere
     else it goes, unless dropping it would weld two tokens into a third
     ("--", "++", "//", "/*", "*/").
  4. If it still does not fit, shorten identifiers, biggest saving first, and
     stop the moment it does. Every name left intact is one more that reads
     properly in an in-game error message.

  --aggressive skips the "stop when it fits" part of step 4 and renames
  everything it is allowed to.

Two things are never touched.

String and character literals are preserved byte for byte, in every pass. A
naive regex-based comment stripper eats the "//" in a URL inside a string and
silently truncates the rest of the line; a naive renamer turns the terminal
property name "RaycastTarget" into "_ab" and the mod bridge fails in a way that
looks exactly like the mod not being installed.

Any identifier that is ever reached through a dot is left alone. A field of one
of the helper classes is declared bare (`public int Width`) and used qualified
(`job.Width`), and renaming can only see the declaration — so renaming it
produces a script that is silently, comprehensively broken. Anything dotted is
out of bounds, which costs a little size and removes the entire failure mode.

The output is verified before it is written: brace balance, and every literal
present and unchanged.

Usage:  python3 tools/minify.py [--aggressive]
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, "dist")
SRC_FILE = os.path.join(DIST, "VEIN.cs")
OUT_FILE = os.path.join(DIST, "VEIN.min.cs")

# The in-game script editor refuses anything larger.
LIMIT = 100000

# Aim under the ceiling rather than at it. A build that fits by fifty characters
# is a build the next commit breaks, and the cost of the margin is a handful of
# extra identifiers renamed.
MARGIN = 2000

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


# Anything declared with one of these as its type is ours to rename. Listing the
# types rather than matching "<word> <word>" keeps the collection away from the
# many statements that merely look like declarations.
DECL_TYPES = (
    r"void|bool|int|double|float|string|long|char|byte|var|"
    r"Vector3D|MatrixD|Color|Health|Waypoint|Job|YieldCell|DroneRecord|OreSighting|"
    r"MinerState|CellState|ShaftResult|DepthMode|HoleOrder|EjectMode|Role|"
    r"IMy\w+|My\w+|List<[^>]+>|Dictionary<[^>]+>|Queue<[^>]+>|\w+\[\]|\w+\[,\]"
)

DECL_RE = re.compile(
    r"(?<![\w.])(?:public|private|protected|internal|static|readonly|const|override|new)?"
    r"[ ]*(?:static|readonly|const)?[ ]*"
    r"(?:" + DECL_TYPES + r")[ ]+"
    r"(?P<name>[A-Za-z_]\w*)[ ]*[\(;={]"
)

# Reserved words that a declaration pattern can pick up by accident, plus the
# things the harness calls by name.
KEYWORDS = {
    "if", "else", "for", "foreach", "while", "do", "switch", "case", "default",
    "return", "break", "continue", "new", "this", "base", "null", "true", "false",
    "get", "set", "value", "class", "struct", "enum", "public", "private", "static",
    "readonly", "const", "using", "namespace", "try", "catch", "finally", "throw",
    "out", "ref", "in", "is", "as", "void", "var",
}


WORD = re.compile(r"[A-Za-z0-9_]")

# Pairs that must never be created by deleting the whitespace between them.
#
#   //  /*  */   start a comment and swallow the rest of the script
#   --  ++       change the arithmetic
#   >>           closes two nested generics as a shift: List<List<int> >
#   ?.            a ternary onto a leading-decimal literal, `c ? .5 : 1`,
#                 becomes the null-conditional operator
#
# The last two do not occur in the source today. They are here because the cost
# of listing a pair that never arises is nothing, and the cost of omitting one
# that does is a script that minifies without complaint and misbehaves in game.
#
# Not a general C# tokeniser, and does not pretend to be: the other welds that
# get suggested for this list — `< <`, `& &`, `| |`, `= >` — cannot be produced
# by valid C# with whitespace in the middle, so adding them would buy nothing.
# The real backstop is the verification pass, which checks the output rather
# than reasoning about the input.
WELDS = {"//", "/*", "*/", "--", "++", ">>", "?."}


def squeeze(segments):
    """Delete every space and line break that is not holding two tokens apart.

    Comments are gone by this point, so line breaks carry no meaning of their
    own and the whole script can be one line. That is worth roughly one
    character per line of source, which on a script this size is thousands.
    """
    out = []

    def previous():
        for chunk in reversed(out):
            if chunk:
                return chunk[-1]
        return ""

    for index, (is_code, text) in enumerate(segments):
        if not is_code:
            out.append(text)
            continue

        buf = []
        i = 0
        n = len(text)
        while i < n:
            c = text[i]
            if c not in " \t\r\n":
                buf.append(c)
                i += 1
                continue

            j = i
            while j < n and text[j] in " \t\r\n":
                j += 1

            before = buf[-1] if buf else previous()
            if j < n:
                after = text[j]
            else:
                # A literal follows, or the file ends. A quote is not a word
                # character either way, so the same rule applies.
                after = '"' if index + 1 < len(segments) else ""

            keep = bool(before) and bool(after) and (
                (WORD.match(before) and WORD.match(after)) or (before + after) in WELDS
            )
            if keep:
                buf.append(" ")
            i = j

        out.append("".join(buf))

    return "".join(out)


def collect_renamable(text, segments):
    """Identifiers we declare and can rename everywhere they appear.

    Two exclusions carry all the safety. Anything reached through a dot is out,
    because the use sites are invisible to a renamer that must not touch member
    access. Anything that appears inside a literal is out, because the script
    passes terminal property names and command words around as strings.
    """
    code = "".join(s for is_code, s in segments if is_code)
    literals = "".join(s for is_code, s in segments if not is_code)

    names = {m.group("name") for m in DECL_RE.finditer(code)}

    dotted = {m.group(1) for m in re.finditer(r"\.\s*([A-Za-z_]\w*)", code)}
    quoted = set(re.findall(r"[A-Za-z_]\w*", literals))

    return {
        n for n in names
        if n not in PROTECTED
        and n not in KEYWORDS
        and n not in dotted
        and n not in quoted
        and len(n) > 3
    }


def short_names():
    alphabet = "abcdefghijklmnopqrstuvwxyz"
    for a in alphabet:
        yield "_" + a
    for a in alphabet:
        for b in alphabet:
            yield "_" + a + b


def split_code_and_strings(text):
    """Split into alternating code / literal segments.

    Renaming must never touch string or character literals. The script passes
    terminal property names as strings — "RaycastTarget", "AvailableScanRange" —
    and a blind regex rename would happily corrupt one into "_ab", which fails
    silently at runtime in a way that looks like the mod is missing.
    """
    segments = []          # (is_code, text)
    buf = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c == '"' or c == "'":
            segments.append((True, "".join(buf)))
            buf = []
            quote = c
            lit = [c]
            i += 1
            while i < n:
                if text[i] == "\\":
                    lit.append(text[i : i + 2])
                    i += 2
                    continue
                lit.append(text[i])
                if text[i] == quote:
                    i += 1
                    break
                i += 1
            segments.append((False, "".join(lit)))
            continue
        buf.append(c)
        i += 1
    segments.append((True, "".join(buf)))
    return segments


def literals_of(text):
    """Every string and character literal, in order. The verification key."""
    return [s for is_code, s in split_code_and_strings(text) if not is_code]


def verify(stripped, minified, renamed_names, problems):
    """Refuse to write output that cannot be what we meant.

    Every pass here rewrites code with regular expressions, which is fine until
    the day it is not. These three checks catch essentially every way that goes
    wrong: a mangled literal means the comment stripper or the renamer crossed a
    quote; a brace imbalance means a chunk of code went missing; and a renamed
    identifier still present in the output means it had use sites the renamer
    could not see, which is the failure that produces a script that compiles and
    then behaves like a different program.
    """
    before = literals_of(stripped)
    after = literals_of(minified)
    if before != after:
        lost = len(before) - len(after)
        if lost:
            problems.append("literal count changed: %d -> %d" % (len(before), len(after)))
        else:
            for a, b in zip(before, after):
                if a != b:
                    problems.append("literal corrupted: %r became %r" % (a[:40], b[:40]))
                    break

    code = "".join(s for is_code, s in split_code_and_strings(minified) if is_code)
    for open_ch, close_ch in (("{", "}"), ("(", ")"), ("[", "]")):
        if code.count(open_ch) != code.count(close_ch):
            problems.append("unbalanced '%s': %d open, %d closed"
                            % (open_ch, code.count(open_ch), code.count(close_ch)))

    for name in renamed_names:
        if re.search(r"(?<![\w])" + re.escape(name) + r"(?![\w])", code):
            problems.append("renamed '%s' but it still appears in the output" % name)


def rename(text, names, target):
    """Shorten identifiers, longest-first, stopping as soon as we fit.

    Renaming everything makes in-game stack traces useless. Renaming the fewest
    identifiers that get us under the limit keeps most of the script readable,
    and the ones sacrificed are the long descriptive names that cost the most
    bytes — which are also the easiest to recognise from context.
    """
    segments = split_code_and_strings(text)
    gen = short_names()
    used = 0

    # Biggest saving first: occurrences × characters removed.
    counts = []
    code_only = "".join(s for is_code, s in segments if is_code)
    taken = set(re.findall(r"\b_\w+", code_only))
    for name in names:
        n = len(re.findall(r"(?<![\w\.])" + re.escape(name) + r"(?![\w])", code_only))
        if n:
            counts.append((n * (len(name) - 3), name))
    counts.sort(reverse=True)
    renamed_names = []

    for _saving, name in counts:
        if len("".join(s for _, s in segments)) <= target:
            break
        # The generated names are underscore-prefixed and the source uses no
        # such identifier, but check rather than assume: a collision would
        # silently merge two unrelated members.
        short = next(gen)
        while short in taken:
            short = next(gen)
        taken.add(short)
        pattern = re.compile(r"(?<![\w\.])" + re.escape(name) + r"(?![\w])")
        segments = [
            (is_code, pattern.sub(short, s) if is_code else s) for is_code, s in segments
        ]
        renamed_names.append(name)
        used += 1

    return "".join(s for _, s in segments), used, renamed_names


def main():
    aggressive = "--aggressive" in sys.argv

    if not os.path.exists(SRC_FILE):
        sys.exit("dist/VEIN.cs not found — run tools/build.py first")

    with open(SRC_FILE, "r", encoding="utf-8") as fh:
        original = fh.read()

    # Kept for verification: the same code, still readable, comments gone. Every
    # later pass must leave its literals exactly as they are here.
    stripped = strip_comments(original)
    text = squeeze(split_code_and_strings(collapse(stripped)))

    renamed = 0
    renamed_names = []
    limit = LIMIT
    target = limit - MARGIN - len(BANNER) - 1
    candidates = collect_renamable(text, split_code_and_strings(text))

    # Only rename if we do not already fit, unless forced. Every identifier left
    # intact is one more that reads properly in an in-game error message.
    if aggressive or len(text) > target:
        text, renamed, renamed_names = rename(text, candidates, 0 if aggressive else target)

    problems = []
    verify(stripped, text, renamed_names, problems)

    text = BANNER + text + "\n"
    if problems:
        print("Minify FAILED verification — nothing written:")
        for p in problems:
            print("  %s" % p)
        return 1

    with open(OUT_FILE, "w", encoding="utf-8") as fh:
        fh.write(text)

    before = len(original)
    after = len(text)
    print("Minified dist/VEIN.cs -> dist/VEIN.min.cs")
    print("  %7d chars  ->  %7d chars  (%.0f%% smaller)"
          % (before, after, 100.0 * (before - after) / before))
    if renamed:
        print("  %d of %d renameable identifiers shortened to fit; the rest keep their names"
              % (renamed, len(candidates)))
    else:
        print("  every identifier kept its name")
    print("  verified: literals intact, brackets balanced")

    if after <= limit:
        print("  fits the in-game editor (limit ~%d), %d chars spare" % (limit, limit - after))
    else:
        print("  STILL OVER the ~%d character editor limit by %d" % (limit, after - limit))
        if not aggressive:
            print("  try: python3 tools/minify.py --aggressive")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
