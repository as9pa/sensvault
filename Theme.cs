using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace SensVault;

/// <summary>
/// One palette, as the seventeen colours the app actually asks for by name plus the two
/// shape decisions a theme is allowed to make.
///
/// Colours are held as strings rather than as <see cref="Brush"/>es on purpose. A theme is
/// static data that has to survive being applied and un-applied all session, so it must never
/// hand out a live object that the app might then own; <see cref="ThemeManager"/> builds a
/// fresh brush per apply instead.
/// </summary>
public sealed class Theme
{
    public required string Name { get; init; }

    /// <summary>Heading the Themes page files this swatch under.</summary>
    public required string Group { get; init; }

    /// <summary>Light enough that the OS should draw a light caption bar and dark glyphs.
    /// Only <see cref="TitleBar"/> reads this; everything inside the window is driven by the
    /// colours themselves.</summary>
    public bool Light { get; init; }

    /// <summary>Square corners everywhere, for the two Classic themes. A Win32 dialog with
    /// rounded buttons is not a Win32 dialog.</summary>
    public bool Square { get; init; }

    /// <summary>
    /// Draw the two 1px bevel layers that every themeable control template carries. Off for
    /// every flat theme, where the layers collapse to zero thickness and cost nothing.
    ///
    /// The same two layers serve both Classic themes: with <see cref="BevelLight"/> and
    /// <see cref="BevelDark"/> set to the same grey they render as the flat 1px outline
    /// modern Win32 dialogs use, and with white against grey they chisel the way 98 did.
    /// </summary>
    public bool Bevel { get; init; }

    // --- the ramp: recessed below Base, raised above it ---
    public required string Crust { get; init; } // deepest recess; dead inputs
    public required string Mantle { get; init; } // the left panel, the grid header
    public required string Base { get; init; } // the page
    public required string Surface0 { get; init; } // cards, fields, popups
    public required string Surface1 { get; init; } // hairlines, hover fills, the toast
    public required string Surface2 { get; init; } // the commit button on each tab
    public required string Surface3 { get; init; } // that button, hovered

    // --- ink ---
    public required string Overlay0 { get; init; } // "here, but not for you"
    public required string Subtext { get; init; } // labels and footnotes
    public required string Text { get; init; } // body
    public required string TextStrong { get; init; } // the one figure a row is really about
    public required string ButtonText { get; init; } // label on Surface2, which is not always dark
    public required string Accent { get; init; }
    public required string Red { get; init; }
    public required string Selection { get; init; } // text-selection fill, composited at 45%

    public string BevelLight { get; init; } = "#FFFFFF";
    public string BevelDark { get; init; } = "#808080";

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

/// <summary>
/// Applies a <see cref="Theme"/> by replacing the resources App.xaml declared.
///
/// Every palette key is referenced from the XAML as a DynamicResource, and that is a
/// requirement rather than a preference. The tempting shortcut is to leave the references
/// static and write new colours into the brush objects already in the dictionary, since a
/// StaticResource holds the instance it resolved to at parse time and would repaint for free.
/// It does not work: a ResourceDictionary freezes any Freezable it is handed, so the brushes
/// are read-only by the time the first window is built. Thawing them does not help either,
/// because the dictionary simply freezes the replacement on the way back in. Swapping the
/// entry and letting DynamicResource notice is the supported route.
///
/// This is also why <see cref="MainWindow"/> sets the status line's brush with
/// SetResourceReference rather than assigning the result of FindResource: an assigned brush is
/// a snapshot of one theme, and the next apply would leave it behind.
/// </summary>
public static class ThemeManager
{
    public static Theme Current { get; private set; } = ThemeLibrary.Default;

    public static void Apply(Theme theme)
    {
        Current = theme;
        var r = Application.Current.Resources;

        SetBrush(r, "Crust", theme.Crust);
        SetBrush(r, "Mantle", theme.Mantle);
        SetBrush(r, "Base", theme.Base);
        SetBrush(r, "Surface0", theme.Surface0);
        SetBrush(r, "Surface1", theme.Surface1);
        SetBrush(r, "Surface2", theme.Surface2);
        SetBrush(r, "Surface3", theme.Surface3);
        SetBrush(r, "Overlay0", theme.Overlay0);
        SetBrush(r, "Subtext", theme.Subtext);
        SetBrush(r, "Text", theme.Text);
        SetBrush(r, "TextStrong", theme.TextStrong);
        SetBrush(r, "ButtonText", theme.ButtonText);
        SetBrush(r, "Accent", theme.Accent);
        SetBrush(r, "Red", theme.Red);
        SetBrush(r, "Selection", theme.Selection);
        SetBrush(r, "BevelLight", theme.BevelLight);
        SetBrush(r, "BevelDark", theme.BevelDark);

        // Derived rather than declared. The accent is the one colour a theme is free to make
        // anything, so the label printed on top of it cannot be a constant: white reads on
        // Catppuccin's lavender and vanishes on Gruvbox's yellow. Rather than ask seventeen
        // palettes for an eighteenth colour, take whichever ink the theme already declares
        // stands furthest from its own accent.
        //
        // Frozen, unlike the palette brushes: nothing writes to it, the next apply replaces
        // the whole entry, and freezing says so.
        var accent = Theme.Parse(theme.Accent);
        var ink = new SolidColorBrush(
            new[] { theme.Crust, theme.TextStrong, theme.ButtonText }
                .Select(Theme.Parse)
                .MaxBy(c => Contrast(accent, c))
        );
        ink.Freeze();
        r["AccentText"] = ink;

        // Square is all-or-nothing rather than per-token: a dialog that rounded its buttons
        // but not its fields would read as a bug, not as a style.
        var round = !theme.Square;
        r["RadSm"] = new CornerRadius(round ? 3 : 0);
        r["RadMd"] = new CornerRadius(round ? 4 : 0);
        r["RadLg"] = new CornerRadius(round ? 5 : 0);
        r["RadCard"] = new CornerRadius(round ? 6 : 0);
        r["RadPill"] = new CornerRadius(round ? 16 : 0);

        // Top-left and bottom-right of a 1px frame, as two separate layers so each side can
        // take its own brush -- a Border has one BorderBrush for all four edges, so a bevel
        // cannot be done in one.
        var on = theme.Bevel;
        r["BevelTL"] = new Thickness(on ? 1 : 0, on ? 1 : 0, 0, 0);
        r["BevelBR"] = new Thickness(0, 0, on ? 1 : 0, on ? 1 : 0);
    }

    private static void SetBrush(ResourceDictionary r, string key, string hex) =>
        r[key] = new SolidColorBrush(Theme.Parse(hex));

    /// <summary>
    /// WCAG 2.x contrast ratio between two sRGB colours: 1 for a pair that matches, 21 for
    /// black against white. Only <see cref="Apply"/> uses it, to pick the ink for the accent.
    /// </summary>
    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a),
            lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);

        // Relative luminance: undo sRGB's gamma per channel, then weight the three by how
        // much of the eye's response each one carries.
        static double Luminance(Color c) =>
            0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

        static double Linear(byte channel)
        {
            var v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }
}

/// <summary>
/// One swatch on the Themes page: a rounded rectangle carrying a theme's name, painted in
/// that theme's own colours rather than the running one's.
///
/// The brushes here are frozen, which is the opposite of the rule <see cref="ThemeManager"/>
/// works by, and deliberately so. A swatch shows a fixed palette that never changes no matter
/// what is applied, so freezing both states the intent and keeps these well clear of the
/// mutable brushes the live app is painted with.
/// </summary>
public sealed class ThemeCard : INotifyPropertyChanged
{
    private bool _selected;

    public required Theme Theme { get; init; }

    public string Name => Theme.Name;

    /// <summary>Heading this swatch groups under. Read by a PropertyGroupDescription, so it
    /// has to be a plain property here rather than a hop through Theme.</summary>
    public string Group => Theme.Group;

    /// <summary>Currently applied. Drives the ring the selected swatch wears.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    /// <summary>The swatch's fill. Base rather than Surface0: it is the colour the app is
    /// mostly made of, so it is the one worth previewing.</summary>
    public Brush Back => _back ??= Frozen(Theme.Base);
    public Brush Fore => _fore ??= Frozen(Theme.Text);

    /// <summary>The hairline, and the ring when this one is applied. Surface3 is the top of
    /// the ramp, so it separates from Base in a light theme and a dark one alike.</summary>
    public Brush Edge => _edge ??= Frozen(Theme.Surface3);
    public Brush Ring => _ring ??= Frozen(Theme.Accent);

    /// <summary>The band down the left of the swatch. Mantle is what the app paints its left
    /// panel and its grid header in, so the band is the stripe the window itself wears.</summary>
    public Brush Band => _band ??= Frozen(Theme.Mantle);

    /// <summary>The swatch's own edge, and the outline on the quietest pip. Surface1 is the
    /// hairline the running app draws its fields and its separators with.</summary>
    public Brush Hairline => _hairline ??= Frozen(Theme.Surface1);

    /// <summary>The accent, for the first pip and for the tick the applied swatch wears. The
    /// same brush as <see cref="Ring"/>: a theme has one accent, and one swatch shows it in
    /// three places.</summary>
    public Brush AccentBrush => Ring;

    /// <summary>A button's face, as the middle pip.</summary>
    public Brush PipMid => _pipMid ??= Frozen(Theme.Surface2);

    /// <summary>A field's face, as the last pip.</summary>
    public Brush PipLow => _pipLow ??= Frozen(Theme.Surface0);

    /// <summary>Square for the Classic pair, matching what picking them does to the app.</summary>
    public CornerRadius Radius => new(Theme.Square ? 0 : 8);

    /// <summary>The selection ring sits 3px outside the swatch, so it has to be rounder by
    /// the same 3 or the two curves will not run parallel.</summary>
    public CornerRadius RingRadius => new(Theme.Square ? 0 : 11);

    private Brush? _back;
    private Brush? _fore;
    private Brush? _edge;
    private Brush? _ring;
    private Brush? _band;
    private Brush? _hairline;
    private Brush? _pipMid;
    private Brush? _pipLow;

    public event PropertyChangedEventHandler? PropertyChanged;

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush(Theme.Parse(hex));
        brush.Freeze();
        return brush;
    }
}
