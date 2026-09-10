namespace SensVault;

/// <summary>
/// The palettes, in the order the Themes page lays them out.
///
/// The published themes are quoted from their own specs wherever the spec has a colour for
/// the job. Two kinds of departure are marked in the comments where they happen:
///
///   * a ramp that runs out. Most editor themes define two or three surface steps because a
///     text editor needs two or three; this app asks for four plus two below the page. The
///     missing rungs are interpolated in the theme's own hue rather than borrowed from grey.
///
///   * Overlay0. It is the app's "here, but not for you" grey -- read-only text, placeholder,
///     the date column -- and several themes' nearest colour is a comment grey chosen to sit
///     *under* the reading line, landing near 2.5:1 against the page. Those are lifted to
///     roughly 4.5:1. Dim is a look; unreadable is a bug.
/// </summary>
public static class ThemeLibrary
{
    public static Theme Default => All[0];

    /// <summary>Falls back to the default rather than throwing: a name in data.json that no
    /// longer matches a theme just opens on the default, the same way a dropped game does.</summary>
    public static Theme ByName(string? name) => All.FirstOrDefault(t => t.Name == name) ?? Default;

    public static IReadOnlyList<Theme> All { get; } =
    [
        // ---------- neutral ----------
        new Theme
        {
            Name = "Vault Dark",
            Group = "Neutral",
            Crust = "#080808",
            Mantle = "#0F0F0F",
            Base = "#141414",
            Surface0 = "#1E1E1E",
            Surface1 = "#2E2E2E",
            Surface2 = "#3A3A3A",
            Surface3 = "#4A4A4A",
            Overlay0 = "#808080",
            Subtext = "#9C9C9C",
            Text = "#E8E8E8",
            TextStrong = "#FFFFFF",
            ButtonText = "#FFFFFF",
            Accent = "#E8E8E8",
            Red = "#E07070",
            Selection = "#8A8A8A",
        },
        // The dark theme read back through the ramp, not inverted channel by channel:
        // Surface2 and Surface3 stay dark so the commit button is still the one thing on
        // the page that reads as pressable.
        new Theme
        {
            Name = "Vault Light",
            Group = "Neutral",
            Light = true,
            Crust = "#EAEAEA",
            Mantle = "#F4F4F4",
            Base = "#FFFFFF",
            Surface0 = "#F0F0F0",
            Surface1 = "#DCDCDC",
            Surface2 = "#3A3A3A",
            Surface3 = "#4A4A4A",
            Overlay0 = "#8A8A8A",
            Subtext = "#5E5E5E",
            Text = "#1A1A1A",
            TextStrong = "#000000",
            ButtonText = "#FFFFFF",
            Accent = "#1A1A1A",
            Red = "#C03030",
            Selection = "#9A9A9A",
        },
        // The dialog you get today off Advanced mouse options: #F0F0F0 face, white fields,
        // one flat #ADADAD hairline round everything. Both bevel brushes are that same
        // grey, so the chisel flattens into the single outline this era draws.
        new Theme
        {
            Name = "Classic",
            Group = "Neutral",
            Light = true,
            Square = true,
            Bevel = true,
            BevelLight = "#ADADAD",
            BevelDark = "#ADADAD",
            Crust = "#E9E9E9",
            Mantle = "#F0F0F0",
            Base = "#F0F0F0",
            Surface0 = "#FFFFFF",
            Surface1 = "#CFCFCF",
            Surface2 = "#E1E1E1",
            Surface3 = "#E5F1FB",
            Overlay0 = "#6D6D6D",
            Subtext = "#444444",
            Text = "#000000",
            TextStrong = "#000000",
            ButtonText = "#000000",
            Accent = "#0078D7",
            Red = "#C42B1C",
            Selection = "#0078D7",
        },
        // The same dialog fifteen years earlier. #D4D0C8 face, navy accent, and the bevel
        // doing what it was drawn to do: white to the top-left, #808080 to the bottom-right.
        new Theme
        {
            Name = "Classic 98",
            Group = "Neutral",
            Light = true,
            Square = true,
            Bevel = true,
            BevelLight = "#FFFFFF",
            BevelDark = "#808080",
            Crust = "#C8C4BC",
            Mantle = "#D4D0C8",
            Base = "#D4D0C8",
            Surface0 = "#FFFFFF",
            Surface1 = "#C0C0C0",
            Surface2 = "#D4D0C8",
            Surface3 = "#DFDCD5",
            Overlay0 = "#6E6A64",
            Subtext = "#3D3A35",
            Text = "#000000",
            TextStrong = "#000000",
            ButtonText = "#000000",
            Accent = "#000080",
            Red = "#800000",
            Selection = "#000080",
        },
        // ---------- warm ----------
        // Catppuccin names the ramp this app already uses, so it maps one to one. Overlay0
        // is the spec's Overlay1: the real Overlay0 is 4.0:1 here and this grey has to
        // carry read-only text.
        new Theme
        {
            Name = "Catppuccin Mocha",
            Group = "Warm",
            Crust = "#11111B",
            Mantle = "#181825",
            Base = "#1E1E2E",
            Surface0 = "#313244",
            Surface1 = "#45475A",
            Surface2 = "#585B70",
            Surface3 = "#6C7086",
            Overlay0 = "#7F849C",
            Subtext = "#A6ADC8",
            Text = "#CDD6F4",
            TextStrong = "#FFFFFF",
            ButtonText = "#FFFFFF",
            Accent = "#CBA6F7",
            Red = "#F38BA8",
            Selection = "#585B70",
        },
        // Latte's own Surface2 is #ACB0BE, which cannot hold a button label at any ink.
        // Surface2 and Surface3 take the theme's Text and a step off it instead, so the
        // button is the one dark shape on the page.
        new Theme
        {
            Name = "Catppuccin Latte",
            Group = "Warm",
            Light = true,
            Crust = "#DCE0E8",
            Mantle = "#E6E9EF",
            Base = "#EFF1F5",
            Surface0 = "#CCD0DA",
            Surface1 = "#BCC0CC",
            Surface2 = "#4C4F69",
            Surface3 = "#5C5F7A",
            Overlay0 = "#7C7F93",
            Subtext = "#6C6F85",
            Text = "#4C4F69",
            TextStrong = "#000000",
            ButtonText = "#EFF1F5",
            Accent = "#8839EF",
            Red = "#D20F39",
            Selection = "#ACB0BE",
        },
        new Theme
        {
            Name = "Gruvbox Dark",
            Group = "Warm",
            Crust = "#141617",
            Mantle = "#1D2021",
            Base = "#282828",
            Surface0 = "#3C3836",
            Surface1 = "#504945",
            Surface2 = "#665C54",
            Surface3 = "#7C6F64",
            Overlay0 = "#928374",
            Subtext = "#A89984",
            Text = "#EBDBB2",
            TextStrong = "#FBF1C7",
            ButtonText = "#FBF1C7",
            Accent = "#FABD2F",
            Red = "#FB4934",
            Selection = "#665C54",
        },
        new Theme
        {
            Name = "Gruvbox Light",
            Group = "Warm",
            Light = true,
            Crust = "#E4D5A8",
            Mantle = "#F2E5BC",
            Base = "#FBF1C7",
            Surface0 = "#EBDBB2",
            Surface1 = "#D5C4A1",
            Surface2 = "#504945",
            Surface3 = "#665C54",
            Overlay0 = "#7C6F64",
            Subtext = "#665C54",
            Text = "#3C3836",
            TextStrong = "#282828",
            ButtonText = "#FBF1C7",
            Accent = "#B57614",
            Red = "#9D0006",
            Selection = "#BDAE93",
        },
        // Rosé Pine's Surface and the app's Mantle are the same #1F1D2E, which would leave
        // the left panel and the cards on it indistinguishable. Mantle takes a step back
        // toward Base so the panel still reads as behind them.
        new Theme
        {
            Name = "Rosé Pine",
            Group = "Warm",
            Crust = "#14121F",
            Mantle = "#1B1927",
            Base = "#191724",
            Surface0 = "#1F1D2E",
            Surface1 = "#26233A",
            Surface2 = "#403D52",
            Surface3 = "#524F67",
            Overlay0 = "#6E6A86",
            Subtext = "#908CAA",
            Text = "#E0DEF4",
            TextStrong = "#FFFFFF",
            ButtonText = "#FFFFFF",
            Accent = "#C4A7E7",
            Red = "#EB6F92",
            Selection = "#403D52",
        },
        // Dawn raises rather than recesses: its Surface (#FFFAF3) is lighter than its Base,
        // so cards sit above the warm page as near-white. Kept, because that inversion is
        // most of what the theme looks like.
        new Theme
        {
            Name = "Rosé Pine Dawn",
            Group = "Warm",
            Light = true,
            Crust = "#EAE0D8",
            Mantle = "#F2E9E1",
            Base = "#FAF4ED",
            Surface0 = "#FFFAF3",
            Surface1 = "#DFDAD9",
            Surface2 = "#575279",
            Surface3 = "#6B6590",
            Overlay0 = "#8A8598",
            Subtext = "#797593",
            Text = "#575279",
            TextStrong = "#3A3550",
            ButtonText = "#FFFAF3",
            Accent = "#907AA9",
            Red = "#B4637A",
            Selection = "#CECACD",
        },
        new Theme
        {
            Name = "Everforest Dark",
            Group = "Warm",
            Crust = "#1E2326",
            Mantle = "#272E33",
            Base = "#2D353B",
            Surface0 = "#343F44",
            Surface1 = "#3D484D",
            Surface2 = "#475258",
            Surface3 = "#4F585E",
            Overlay0 = "#859289",
            Subtext = "#9DA9A0",
            Text = "#D3C6AA",
            TextStrong = "#F2EFDF",
            ButtonText = "#D3C6AA",
            Accent = "#A7C080",
            Red = "#E67E80",
            Selection = "#4F585E",
        },
        // ---------- cool ----------
        // Overlay0 is lifted off the spec's comment blue (#565F89, 3.6:1); the rest is
        // Tokyo Night as published.
        new Theme
        {
            Name = "Tokyo Night",
            Group = "Cool",
            Crust = "#101014",
            Mantle = "#16161E",
            Base = "#1A1B26",
            Surface0 = "#24283B",
            Surface1 = "#292E42",
            Surface2 = "#3B4261",
            Surface3 = "#414868",
            Overlay0 = "#6E7899",
            Subtext = "#A9B1D6",
            Text = "#C0CAF5",
            TextStrong = "#FFFFFF",
            ButtonText = "#FFFFFF",
            Accent = "#7AA2F7",
            Red = "#F7768E",
            Selection = "#3B4261",
        },
        // Nord is four background steps and four foreground ones, so Crust and Mantle are
        // interpolated below nord0 and Surface3 takes nord10, the theme's own deep blue.
        new Theme
        {
            Name = "Nord",
            Group = "Cool",
            Crust = "#232831",
            Mantle = "#292E39",
            Base = "#2E3440",
            Surface0 = "#3B4252",
            Surface1 = "#434C5E",
            Surface2 = "#4C566A",
            Surface3 = "#5E81AC",
            Overlay0 = "#7B88A1",
            Subtext = "#D8DEE9",
            Text = "#ECEFF4",
            TextStrong = "#FFFFFF",
            ButtonText = "#ECEFF4",
            Accent = "#88C0D0",
            Red = "#BF616A",
            Selection = "#4C566A",
        },
        // Dracula publishes one surface (Current Line, #44475A); Surface0 and Surface2 are
        // interpolated either side of it, and Subtext is drawn toward the foreground since
        // the spec has no dimmed body colour.
        new Theme
        {
            Name = "Dracula",
            Group = "Cool",
            Crust = "#191A21",
            Mantle = "#21222C",
            Base = "#282A36",
            Surface0 = "#343746",
            Surface1 = "#44475A",
            Surface2 = "#565A71",
            Surface3 = "#6272A4",
            Overlay0 = "#6272A4",
            Subtext = "#BFC7D5",
            Text = "#F8F8F2",
            TextStrong = "#FFFFFF",
            ButtonText = "#FFFFFF",
            Accent = "#BD93F9",
            Red = "#FF5555",
            Selection = "#44475A",
        },
        new Theme
        {
            Name = "One Dark",
            Group = "Cool",
            Crust = "#1B1F23",
            Mantle = "#21252B",
            Base = "#282C34",
            Surface0 = "#2C313C",
            Surface1 = "#3E4451",
            Surface2 = "#4B5263",
            Surface3 = "#5C6370",
            Overlay0 = "#7F848E",
            Subtext = "#ABB2BF",
            Text = "#D7DAE0",
            TextStrong = "#FFFFFF",
            ButtonText = "#FFFFFF",
            Accent = "#61AFEF",
            Red = "#E06C75",
            Selection = "#3E4451",
        },
        // Solarized is two backgrounds per mode, base03 and base02, and this app wants six
        // steps. The extra rungs are mixed along the same blue-green the pair already sits
        // on rather than pulled toward grey, which is what keeps the cast intact.
        new Theme
        {
            Name = "Solarized Dark",
            Group = "Cool",
            Crust = "#00181E",
            Mantle = "#002028",
            Base = "#002B36",
            Surface0 = "#073642",
            Surface1 = "#0E4553",
            Surface2 = "#1A5A6B",
            Surface3 = "#256F80",
            Overlay0 = "#657B83",
            Subtext = "#839496",
            Text = "#93A1A1",
            TextStrong = "#FDF6E3",
            ButtonText = "#FDF6E3",
            Accent = "#268BD2",
            Red = "#DC322F",
            Selection = "#073642",
        },
        new Theme
        {
            Name = "Solarized Light",
            Group = "Cool",
            Light = true,
            Crust = "#E6DFC8",
            Mantle = "#F5EED9",
            Base = "#FDF6E3",
            Surface0 = "#EEE8D5",
            Surface1 = "#DDD6C1",
            Surface2 = "#073642",
            Surface3 = "#12495A",
            Overlay0 = "#657B83",
            Subtext = "#586E75",
            Text = "#073642",
            TextStrong = "#002B36",
            ButtonText = "#FDF6E3",
            Accent = "#268BD2",
            Red = "#DC322F",
            Selection = "#DDD6C1",
        },
    ];
}
