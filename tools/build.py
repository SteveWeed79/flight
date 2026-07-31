#!/usr/bin/env python3
"""
Assemble the VEIN source modules into the two artefacts that matter.

  dist/VEIN.cs      Paste straight into a Programmable Block. This is the body
                    of the Program class, which is exactly what the in-game
                    editor expects.

  dist/VEIN.mdk.cs  The same code wrapped in the namespace/partial-class harness
                    that MDK2 and Visual Studio want, so you get IntelliSense and
                    real whitelist checking while editing.

Usage:  python3 tools/build.py
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
DIST = os.path.join(ROOT, "dist")

# The usings a Programmable Block script needs. In game these are implicit; MDK
# needs them spelled out.
USINGS = """using System;
using System.Text;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
"""


def module_files():
    if not os.path.isdir(SRC):
        sys.exit("No src/ directory at %s" % SRC)
    files = sorted(f for f in os.listdir(SRC) if f.endswith(".cs"))
    if not files:
        sys.exit("No .cs modules found in src/")
    return [os.path.join(SRC, f) for f in files]


def combine(paths):
    parts = []
    for p in paths:
        with open(p, "r", encoding="utf-8") as fh:
            body = fh.read().rstrip()
        name = os.path.basename(p)
        parts.append("#region %s\n%s\n#endregion\n" % (name, body))
    return "\n".join(parts)


def indent(text, spaces):
    pad = " " * spaces
    out = []
    for line in text.split("\n"):
        out.append(pad + line if line.strip() else line)
    return "\n".join(out)


def main():
    paths = module_files()
    body = combine(paths)

    os.makedirs(DIST, exist_ok=True)

    flat = os.path.join(DIST, "VEIN.cs")
    with open(flat, "w", encoding="utf-8") as fh:
        fh.write(body)
        fh.write("\n")

    wrapped = (
        USINGS
        + "\nnamespace VEIN\n{\n"
        + "    public partial class Program : MyGridProgram\n    {\n"
        + indent(body, 8)
        + "\n    }\n}\n"
    )
    mdk = os.path.join(DIST, "VEIN.mdk.cs")
    with open(mdk, "w", encoding="utf-8") as fh:
        fh.write(wrapped)

    lines = body.count("\n") + 1
    chars = len(body)
    print("Built from %d modules" % len(paths))
    print("  %-22s %6d lines  %7d chars" % ("dist/VEIN.cs", lines, chars))
    print("  %-22s (MDK2 / Visual Studio harness)" % "dist/VEIN.mdk.cs")

    # The in-game editor starts to struggle well before this, and the real limit
    # is the instruction count rather than the source size, but a script this
    # large is worth flagging.
    if chars > 100000:
        print("  note: %d chars is large for the in-game editor" % chars)


if __name__ == "__main__":
    main()
