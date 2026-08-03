namespace SensVault;

/// <summary>
/// All conversions hang off one number per game: the yaw constant, meaning the
/// degrees the camera rotates per single mouse count at in-game sensitivity 1.0.
/// Given that, cm/360 is pure arithmetic and is comparable across any two games.
/// </summary>
public static class SensMath
{
    public const double CmPerInch = 2.54;

    public static double Counts360(double yaw, double sens) =>
        yaw <= 0 || sens <= 0 ? 0 : 360.0 / (yaw * sens);

    public static double In360(double yaw, double sens, double dpi) =>
        dpi <= 0 ? 0 : Counts360(yaw, sens) / dpi;

    public static double Cm360(double yaw, double sens, double dpi) =>
        In360(yaw, sens, dpi) * CmPerInch;

    /// <summary>In-game sensitivity needed to hit a target cm/360 on a given game and DPI.</summary>
    public static double SensFromCm360(double cm360, double yaw, double dpi)
    {
        if (cm360 <= 0 || yaw <= 0 || dpi <= 0)
            return 0;
        return 360.0 / (yaw * (cm360 / CmPerInch * dpi));
    }

    /// <summary>
    /// Back-solve a game's yaw from a cm/360 you already know. This is how you add a game
    /// that isn't built in: look its cm/360 up once on mouse-sensitivity.com for any
    /// sens/DPI pair, and the constant falls out.
    /// </summary>
    public static double YawFromCm360(double cm360, double sens, double dpi)
    {
        if (cm360 <= 0 || sens <= 0 || dpi <= 0)
            return 0;
        return 360.0 / (cm360 / CmPerInch * dpi * sens);
    }
}
