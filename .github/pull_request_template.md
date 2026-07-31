## What this changes

<!--
What it does and why it is worth doing. The reasoning is the useful part — the
diff is already in the tab next door. If the change is a fix, say what the old
behaviour actually was, not just that it was wrong.
-->

## Verification

<!--
Paste the real numbers rather than "builds fine".

minify.py is the gate. The in-game editor refuses a script over ~100,000
characters, so a build with no spare left is a build nobody can paste. If spare
has gone negative, say what you did about it — --aggressive buys roughly 9.6k
more at the cost of readable in-game stack traces.
-->

```
python3 tools/validate.py    #
python3 tools/build.py       #            chars
python3 tools/minify.py      #            chars,          spare
```

## Compatibility

<!--
Both of these break existing worlds silently, which is why they are checkboxes
rather than prose.
-->

- [ ] **`STORAGE_REV` bumped**, or nothing persisted changed. Any change to a
      saved field's order, count or meaning needs the bump in `00_Header.cs` —
      without it the loader reads the old layout into the new fields and flies
      the ship on it.
- [ ] **IGC wire format unchanged**, or every role ships together. The protocol
      has no version field, so an old drone and a new dispatcher will not detect
      that they disagree. `MinerState`'s integer values travel on the wire too,
      so reordering the enum counts as a wire change.
- [ ] **New config keys documented** in `docs/CONFIG.md` with their defaults, and
      written back by `WriteConfig` so the block keeps documenting itself.

## Tested in game?

<!--
Say plainly which of these it is:

  - flown in a world, and what it did
  - static verification only

Space Engineers scripts cannot be compiled outside the game, so validate.py is a
linter standing in for a compiler and everything else is inspection. Changes to
flight behaviour, docking, or fleet coordination want a world and sometimes two
drones. Saying so is not a weakness in the PR — it is the honest state of it,
and it tells a reviewer where to look hardest.
-->
