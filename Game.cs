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
    // Yaw = degrees turned per mouse count at in-game sensitivity 1.0.
    //
    // Constants below come from KovaaK's own scale table (FovSensConfig.json, shipped
    // with the game), which states each scale as an explicit formula. Games whose
    // formula is not a plain "sens x constant" are deliberately absent -- see the
    // Excluded note at the bottom -- because this app models yaw as a single scalar.
    private const double Source = 0.022; // KovaaK's "Quake/Source" scale
    private const double CodOw = 0.0066; // KovaaK's "Call of Duty" and "Overwatch" scales

    private static readonly double Siege = 0.018 / Math.PI; // stated as 0.018 / pi

    public static List<Game> BuiltIns() =>
        [
            // --- Source / idTech family, 0.022 ---
            G("Apex Legends", Source),
            G("Counter-Strike 2", Source),
            G("Counter-Strike: GO", Source),
            G("Counter-Strike: Source", Source),
            G("Counter-Strike 1.6", Source),
            G("DOOM (2016)", Source),
            G("DOOM Eternal", Source),
            G("Garry's Mod", Source),
            G("Half-Life 2", Source),
            G("Left 4 Dead 2", Source),
            G("Portal 2", Source),
            G("Quake Champions", Source),
            G("Quake Live", Source),
            G("Quake III Arena", Source),
            G("Team Fortress 2", Source),
            G("Titanfall 2", Source),
            // --- Call of Duty / Overwatch family, 0.0066 ---
            G("Call of Duty (MW/Warzone/BO6)", CodOw),
            G("Overwatch 2", CodOw),
            G("Destiny 2", CodOw),
            // --- everything else, one constant each ---
            G("Battalion 1944", 0.017501),
            G("Deadlock", 0.044),
            G("Delta Force", 0.03),
            G("Diabotical", 1.0 / 60.0),
            G("Fortnite", 0.005555),
            G("Fragpunk", 0.05555),
            G("Halo", 0.022222),
            G("Hunt: Showdown", 0.0429718162181364),
            G("Marvel Rivals", 0.0175),
            G("Rainbow Six Siege", Siege),
            G("Reflex Arena", Siege),
            G("Roblox", 1.01061008),
            G("Roblox (Arsenal)", 0.375),
            G("Rust", 0.1125),
            G("Strinova", 0.01388194363),
            G("The Finals", 0.001),
            G("Unreal Engine 4 (generic)", 0.07),
            G("Valorant", 0.07),
        ];

    // Excluded on purpose, because a single yaw cannot express them:
    //   Splitgate, Paladins  -- yaw scales with the FOV setting
    //   PUBG                 -- non-linear, 10^(sens/50), and FOV-scaled
    //   Battlefield 1/V/6    -- affine, sens*k + offset rather than proportional
    //   GTA 5                -- affine, same reason
    // SensMath.YawFromCm360 can still fit a constant to a single known cm/360 at one FOV
    // if a custom-game entry point comes back; there is no UI for it right now.

    private static Game G(string name, double yaw) =>
        new()
        {
            Name = name,
            Yaw = yaw,
            BuiltIn = true,
        };
}
