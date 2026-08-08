# attic — shelved, not compiled

Excluded from the build in `SensVault.csproj` (`Compile Remove` / `Page Remove` on `attic/**`).
Nothing in here runs. It is kept so a feature can be revived without rewriting it.

## DPI presets (shelved 2026-08-06)

A DPI box that doubled as a preset cycler, plus a manager for the preset list.

- **`DpiPicker.xaml` / `.cs`** — the input itself. Single click cycled to the next preset,
  double click dropped a caret in for a one-off value, and a caret button opened a drop-down
  listing the presets with a `+` row into the manager.
- **`DpiPresetEditor.xaml` / `.cs`** — the manager. Rows of `≡ DPI : 1600 ✕`: drag the handle
  to reorder, edit the number in place, cross it out to remove, `+` to add (seeded by doubling
  the last value). Reordering reuses `RowReorder`, which is generic over any `DataGrid`.
- **`DpiPreset.cs`** — a wrapper around one `double`. The wrapper is what makes reordering
  work: the reorder moves items by reference identity, and two presets both reading 800 would
  be indistinguishable as bare doubles.

**Why it was shelved:** the single-click-to-cycle gesture fought with wanting to just type a
number — the box would take focus and select itself on every click. Plain typing, mirrored
between the Create and Convert tabs, turned out to be all that was needed.

### To revive

1. Delete the `attic/**` removals from `SensVault.csproj`.
2. Swap the two `<TextBox x:Name="DpiBox">` / `ToDpi` elements in `MainWindow.xaml` back to
   `<local:DpiPicker>` with `Changed` and `ManageRequested` handlers.
3. Re-add to `MainWindow.xaml.cs`: the `_dpiPresets` collection built from `AppData.DpiPresets`,
   `Bind(...)` on each picker and editor, `OnPresetsChanged` / `OnPresetEdited`, and
   `_data.DpiPresets = [.. _dpiPresets.Select(p => p.Value)]` inside `Save()`.
4. Re-add the overlay host (a `Grid` spanning both columns with a dimmed backdrop and a centred
   card) and the `DPI Presets` row in `SettingsNav`.

`AppData.DpiPresets` is deliberately still on the model and still round-trips through
`data.json` untouched, so a saved, hand-ordered preset list survives the shelving.
