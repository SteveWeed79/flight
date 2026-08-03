#!/usr/bin/env python3
"""
Structural checks for the VEIN source.

You cannot compile a Space Engineers script without the game's assemblies, so
this stands in for the compiler on the class of mistakes that actually happen
when a script is assembled from modules:

  * unbalanced braces, parentheses or brackets
  * a method or field defined twice across modules
  * a bare call to a method that nothing defines
  * use of API that the Programmable Block sandbox blocks

It is a linter, not a compiler. It will not catch a type error. It will catch
the reason your script says "caught exception during load".

Usage:  python3 tools/validate.py
Exit code is non-zero if ANYTHING is reported, warnings included. A duplicate
member and a call to a method nothing defines are both hard compile errors in
game; reporting them and then exiting 0 made this useless in a build script.
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")

# ---------------------------------------------------------------------------
# Things the in-game sandbox refuses to run. Reflection is the big one: it
# compiles fine in an IDE and throws the moment the block runs.
# ---------------------------------------------------------------------------
FORBIDDEN = [
    (r"\bSystem\.Reflection\b", "System.Reflection is blocked in the PB sandbox"),
    (r"\bGetType\s*\(\s*\)\s*\.\s*Get", "reflection via GetType() is blocked"),
    (r"\bActivator\s*\.", "System.Activator is blocked"),
    (r"\bSystem\.IO\b", "System.IO is blocked"),
    (r"\bSystem\.Net\b", "System.Net is blocked"),
    (r"\bThread\s*\.", "threading is blocked"),
    (r"\bTask\s*<", "System.Threading.Tasks is blocked"),
    (r"\bFile\s*\.(Read|Write|Open)", "file access is blocked"),
    (r"\bDateTime\s*\.\s*Now", "DateTime.Now is blocked; use Runtime.TimeSinceLastRun"),
    # The locale hazard the whole codebase is built around: a client whose
    # decimal separator is a comma turns "12.5" into "125" on a round trip.
    (r'\.ToString\s*\(\s*"[FfNnGgEe]\d*"', 'culture-dependent ToString(format); use Fmt()'),
    (r"\bdouble\s*\.\s*Parse\b", "double.Parse is culture-dependent; use DecD/ParseInt"),
    (r"\bfloat\s*\.\s*Parse\b", "float.Parse is culture-dependent; use DecD/ParseInt"),
]

# Bare-call names that are provided by the harness or the BCL rather than by us.
KNOWN_EXTERNAL = {
    # MyGridProgram surface
    "Echo", "Main", "Save", "Program",
    # BCL / VRage statics used unqualified
    "nameof", "typeof", "sizeof", "default", "new", "return", "if", "while",
    "for", "foreach", "switch", "catch", "lock", "using", "get", "set",
    "Math", "string", "int", "float", "double", "bool", "long", "char", "var",
}

DEF_RE = re.compile(
    r"^\s*(?:public\s+|private\s+|protected\s+|internal\s+|static\s+|readonly\s+|"
    r"override\s+|virtual\s+|sealed\s+|partial\s+|new\s+|const\s+)*"
    r"(?P<type>[A-Za-z_][\w<>,\[\]\?\. ]*?)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*"
    r"(?P<paren>\()",
    re.MULTILINE,
)

FIELD_RE = re.compile(
    r"^\s*(?:public\s+|private\s+|protected\s+|internal\s+|static\s+|readonly\s+|const\s+)*"
    r"(?P<type>[A-Za-z_][\w<>,\[\]\?\. ]*?)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*(?:=[^;]*)?;",
    re.MULTILINE,
)

CALL_RE = re.compile(r"(?<![\w\.])(?P<name>[A-Za-z_]\w*)\s*\(")

PROP_RE = re.compile(
    r"^\s*(?:public\s+|private\s+|protected\s+|internal\s+|static\s+|readonly\s+)*"
    r"(?P<type>[A-Za-z_][\w<>,\[\]\?\. ]*?)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*\{\s*(?:get|set)",
    re.MULTILINE,
)


def strip_noise(text):
    """Remove comments and string literals so they cannot skew the analysis."""
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""

        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            i = n if j == -1 else j
            continue
        if c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            i = n if j == -1 else j + 2
            out.append(" ")
            continue
        if c == '"':
            # Verbatim strings do not honour backslash escapes.
            verbatim = i > 0 and text[i - 1] == "@"
            i += 1
            while i < n:
                if verbatim:
                    if text[i] == '"' and text[i + 1 : i + 2] == '"':
                        i += 2
                        continue
                    if text[i] == '"':
                        i += 1
                        break
                else:
                    if text[i] == "\\":
                        i += 2
                        continue
                    if text[i] == '"':
                        i += 1
                        break
                i += 1
            out.append('""')
            continue
        if c == "'":
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == "'":
                    i += 1
                    break
                i += 1
            out.append("' '")
            continue

        out.append(c)
        i += 1
    return "".join(out)


def depth_map(clean):
    """Brace depth at every character offset.

    Our modules are class-body fragments, so depth 0 is the class level. Only
    declarations there are real members; everything deeper is a local variable
    and must not be treated as a duplicate definition.
    """
    depths = [0] * (len(clean) + 1)
    d = 0
    for i, ch in enumerate(clean):
        depths[i] = d
        if ch == "{":
            d += 1
        elif ch == "}":
            d -= 1
    depths[len(clean)] = d
    return depths


def check_balance(name, clean, problems):
    pairs = {")": "(", "]": "[", "}": "{"}
    opens = set("([{")
    stack = []
    line = 1
    for ch in clean:
        if ch == "\n":
            line += 1
        elif ch in opens:
            stack.append((ch, line))
        elif ch in pairs:
            if not stack:
                problems.append("%s:%d unmatched closing '%s'" % (name, line, ch))
                return
            got, at = stack.pop()
            if got != pairs[ch]:
                problems.append(
                    "%s:%d closing '%s' does not match '%s' opened at line %d"
                    % (name, line, ch, got, at)
                )
                return
    for got, at in stack:
        problems.append("%s:%d '%s' is never closed" % (name, at, got))


def main():
    files = sorted(f for f in os.listdir(SRC) if f.endswith(".cs"))
    if not files:
        sys.exit("No modules found in src/")

    problems = []
    warnings = []

    defined = {}
    fields = {}
    calls = {}
    # Every declared name at any nesting level. Members of the helper classes in
    # 02_Types.cs live at depth 1, so they never appear in `defined`, but a call
    # to one of them is still perfectly legitimate.
    all_names = set()

    for fname in files:
        path = os.path.join(SRC, fname)
        with open(path, "r", encoding="utf-8") as fh:
            raw = fh.read()
        clean = strip_noise(raw)

        check_balance(fname, clean, problems)

        for pattern, why in FORBIDDEN:
            for m in re.finditer(pattern, clean):
                line = clean[: m.start()].count("\n") + 1
                problems.append("%s:%d %s" % (fname, line, why))

        depths = depth_map(clean)

        for rx in (DEF_RE, PROP_RE, FIELD_RE):
            for m in rx.finditer(clean):
                all_names.add(m.group("name"))
        for m in re.finditer(r"\b(?:class|struct|enum|interface)\s+([A-Za-z_]\w*)", clean):
            all_names.add(m.group(1))

        for m in DEF_RE.finditer(clean):
            # Class level only. A match inside a method body is a local, or a
            # call expression that happens to look like a declaration.
            if depths[m.start()] != 0:
                continue
            name = m.group("name")
            if name in ("if", "while", "for", "foreach", "switch", "catch", "using", "lock", "return"):
                continue
            if m.group("type").strip() in ("return", "else", "case"):
                continue
            if name in defined and defined[name] != fname:
                warnings.append(
                    "%s defines '%s', already defined in %s" % (fname, name, defined[name])
                )
            defined.setdefault(name, fname)

        for m in PROP_RE.finditer(clean):
            if depths[m.start()] != 0:
                continue
            defined.setdefault(m.group("name"), fname)

        for m in FIELD_RE.finditer(clean):
            if depths[m.start()] != 0:
                continue
            name = m.group("name")
            if name in fields and fields[name] != fname:
                warnings.append(
                    "%s declares field '%s', already declared in %s"
                    % (fname, name, fields[name])
                )
            fields.setdefault(name, fname)

        for m in CALL_RE.finditer(clean):
            # "new Foo(" is a constructor, not a call to a method of ours.
            before = clean[max(0, m.start() - 4) : m.start()]
            if before.endswith("new "):
                continue
            calls.setdefault(m.group("name"), set()).add(fname)

    # Bare calls to something nothing defines. This is the check that earns the
    # tool its keep: a method renamed in one module and still called from another
    # is otherwise invisible until the game refuses to compile the block.
    unknown = []
    for name, where in sorted(calls.items()):
        if name in defined or name in fields or name in all_names or name in KNOWN_EXTERNAL:
            continue
        unknown.append("'%s' called in %s but never defined" % (name, ", ".join(sorted(where))))

    print("VEIN structural check")
    print("  modules:  %d" % len(files))
    print("  members:  %d methods/properties, %d fields" % (len(defined), len(fields)))
    print()

    for w in warnings:
        print("  WARN  %s" % w)
    for u in unknown:
        print("  WARN  %s" % u)
    for p in problems:
        print("  FAIL  %s" % p)

    if not problems and not warnings and not unknown:
        print("  clean")

    return 1 if (problems or warnings or unknown) else 0


if __name__ == "__main__":
    sys.exit(main())
