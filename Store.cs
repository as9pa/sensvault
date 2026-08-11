using System.IO;
using System.Text.Json;

namespace SensVault;

public class AppData
{
    public List<SensProfile> Profiles { get; set; } = [];
    public List<Game> CustomGames { get; set; } = [];

    /// <summary>DPI the entry form was left on, so a new session starts on your mouse.</summary>
    public double LastDpi { get; set; } = 800;

    /// <summary>Vault columns currently hidden, by key. Absent or empty shows everything,
    /// so a file written before the column picker existed opens with all six.</summary>
    public List<string> HiddenColumns { get; set; } = [];

    /// <summary>Games left out of every picker, by name. Stored as the names rather than as
    /// whole <see cref="Game"/> entries because the library itself is compiled in and gets
    /// rebuilt on each launch -- only the choice about it belongs in the file. A name here
    /// that no longer matches any game is simply ignored, so a renamed or dropped built-in
    /// costs nothing. Empty shows the whole library, which is what a file written before
    /// Settings &gt; Games existed opens as.</summary>
    public List<string> HiddenGames { get; set; } = [];

    /// <summary>Hand-ordered DPI presets. Nothing reads or writes these right now -- the
    /// picker that used them is shelved in attic/ -- but the property stays so a saved list
    /// round-trips through the file untouched and survives until the feature comes back.</summary>
    public List<double> DpiPresets { get; set; } = [400, 800, 1600, 3200];

    /// <summary>Zoom on the vault side of the window, 0.7 to 2.0.</summary>
    public double VaultZoom { get; set; } = 1;

    /// <summary>Whether the left entry panel was hidden, leaving only the vault.</summary>
    public bool PanelCollapsed { get; set; }

    /// <summary>Whether the filter bar above the grid was folded away to its rail. The filter
    /// and search it holds still apply while it is down, so this travels with
    /// <see cref="LastFilter"/> rather than replacing it.</summary>
    public bool BarCollapsed { get; set; }

    /// <summary>Hand-dragged column widths, keyed the same as <see cref="HiddenColumns"/>.
    /// A column with no entry keeps whatever the XAML asked for.</summary>
    public Dictionary<string, ColumnWidth> ColumnWidths { get; set; } = [];

    /// <summary>Game the vault was filtered to at close. Empty means all games.</summary>
    public string LastFilter { get; set; } = "";

    /// <summary>Whether the scrollbars are hidden throughout. The wheel still scrolls -- this
    /// takes away the bars, not the scrolling.</summary>
    public bool HideScrollBars { get; set; }

    /// <summary>Whether the status line under the vault is folded away. It is also where
    /// warnings surface, so hiding it is a real trade and the settings page says so.</summary>
    public bool HideStatusBar { get; set; }
}

/// <summary>
/// A column's width plus whether it was proportional. Star and pixel widths are not
/// interchangeable -- restoring a star column as a fixed pixel width would freeze it and
/// stop it sharing the leftover space when the window resizes -- so the unit travels with
/// the number.
/// </summary>
public class ColumnWidth
{
    public double Value { get; set; }
    public bool Star { get; set; }
}

public static class Store
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Folder { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SensVault"
        );

    public static string FilePath { get; } = Path.Combine(Folder, "data.json");

    public static AppData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppData();
            return JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath)) ?? new AppData();
        }
        catch (Exception)
        {
            // A corrupt file must not cost you the whole vault -- park it and start clean.
            try
            {
                File.Move(FilePath, FilePath + ".bad", overwrite: true);
            }
            catch { }
            return new AppData();
        }
    }

    public static void Save(AppData data)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data, Options));
    }
}
