namespace SensVault;

public class Game
{
    public string Name { get; set; } = "";
    public double Yaw { get; set; }
    public bool BuiltIn { get; set; }

    public override string ToString() => Name;
}

public static class GameLibrary
{
    // Engines that share a yaw constant. Only games whose value is well established
    // ship here -- anything else you add yourself via the Games tab, which derives
    // the constant from a known cm/360 rather than guessing it.
    private const double Source = 0.022; // Source / Source 2 / idTech
    private const double CodOw = 0.0066; // Call of Duty and Overwatch share this one

    public static List<Game> BuiltIns() =>
        [
            G("Counter-Strike 2", Source),
            G("Counter-Strike: GO", Source),
            G("Apex Legends", Source),
            G("Titanfall 2", Source),
            G("Team Fortress 2", Source),
            G("Half-Life 2", Source),
            G("Portal 2", Source),
            G("Left 4 Dead 2", Source),
            G("Garry's Mod", Source),
            G("Deadlock", Source),
            G("Quake Champions", Source),
            G("Quake Live", Source),
            G("DOOM Eternal", Source),
            G("DOOM (2016)", Source),
            G("Overwatch 2", CodOw),
            G("Call of Duty (MW/Warzone/BO6)", CodOw),
            G("Valorant", 0.07),
        ];

    private static Game G(string name, double yaw) =>
        new()
        {
            Name = name,
            Yaw = yaw,
            BuiltIn = true,
        };
}
