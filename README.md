# SensVault

A small Windows desktop app for storing mouse sensitivities across games and converting
between them, using **cm/360** — the physical distance the mouse travels for one full
in-game turn — as the common unit.

Built to replace the browser-tab-plus-notepad routine for keeping sensitivities in sync.

## Why cm/360

eDPI (DPI × in-game sens) only compares within a single game, because every engine picks
its own sensitivity scale. cm/360 is a physical measurement, so it is directly comparable
between any two titles.

Every conversion derives from one number per game: the **yaw constant**, the degrees the
camera rotates per mouse count at in-game sensitivity 1.0.

```
counts/360 = 360 / (yaw × sens)
cm/360     = counts/360 ÷ DPI × 2.54
```

## Features

- Named profiles storing game, DPI, sensitivity and notes, with cm/360, in/360 and eDPI
  computed for you
- Sort by any column — cm/360, game, eDPI, date added
- Convert a saved profile to the sensitivity value for any other game at any DPI
- Edit cells in place; every change is written to disk immediately
- Search across name, game and notes
- Type-to-search game picker backed by a built-in library of verified yaw constants
- Any game not in the library can be added by entering a known cm/360 at any sens/DPI
  pair — the yaw constant is back-solved from it

Profiles live in `%APPDATA%\SensVault\data.json` as plain JSON.

## Build

Requires the .NET 10 SDK.

```
dotnet run --project SensVault.csproj
```

To produce a standalone copy:

```
dotnet publish -c Release -o dist
```

## Caveat

Conversions match 360 distance only; field of view is not modelled. Two games at the same
cm/360 but different FOVs will track differently on screen. Games whose sensitivity scale
is FOV-dependent or non-linear are deliberately left out of the built-in library rather
than approximated with a single constant.
