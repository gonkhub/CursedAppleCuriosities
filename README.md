# Cursed Apple Curiosities

A BepInEx plugin for **Risk of Rain 2** that adds custom items, equipment and survivors adapted
from Deadlock. See [HANDOFF.md](HANDOFF.md) for the full roster, architecture, and build notes.

## Building

```
dotnet build "CursedAppleCuriosities\CursedAppleCuriosities.csproj" -c Release
```

The csproj references the game's `Managed` DLLs and an r2modman profile (BepInEx, R2API, MMHOOK);
adjust `GameManagedPath` / `ProfilePath` in the csproj for your machine.

## Icons

Item icons (`CursedAppleCuriosities/Resources/icon_*.png`) are derived from Deadlock's item art,
which belongs to Valve, so they are **not in this repo**. To build, drop the 128px icons into
`CursedAppleCuriosities/Resources/` (the names are listed in the csproj's `<EmbeddedResource>` entries).

Deadlock models, animations and sounds for survivors are read at runtime from the player's own
Deadlock install and are never shipped.
