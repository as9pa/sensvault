using System.IO;
using System.Text.Json;

namespace SensVault;

public class AppData
{
    public List<SensProfile> Profiles { get; set; } = [];
    public List<Game> CustomGames { get; set; } = [];

    /// <summary>DPI the entry form was left on, so a new session starts on your mouse.</summary>
    public double LastDpi { get; set; } = 800;
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
