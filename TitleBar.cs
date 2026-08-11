using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SensVault;

/// <summary>
/// WPF only paints the client area; the caption bar, its buttons and the window border are
/// drawn by the desktop window manager. These DWM attributes are the only way to pull that
/// chrome into the app's palette instead of leaving a white bar above a dark window.
/// </summary>
internal static class TitleBar
{
    private const int UseImmersiveDarkMode = 20; // DWMWA_USE_IMMERSIVE_DARK_MODE
    private const int UseImmersiveDarkModePre20H1 = 19; // same flag, older Win10 builds
    private const int BorderColor = 34; // DWMWA_BORDER_COLOR,  Win11 only
    private const int CaptionColor = 35; // DWMWA_CAPTION_COLOR, Win11 only
    private const int TextColor = 36; // DWMWA_TEXT_COLOR,    Win11 only

    /// <summary>
    /// Paints the caption bar, its border and its text in a theme's colours.
    ///
    /// Safe to call before the window has a handle -- it simply does nothing, and the caller
    /// runs it again from SourceInitialized. That matters because a theme can be picked at any
    /// point, including from the constructor, long before there is an hwnd to talk to.
    /// </summary>
    public static void Apply(Window window, Theme theme)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        // Immersive dark mode is what colours the minimise/maximise/close glyphs, and it is
        // the only part of this that Windows 10 understands. A light theme has to turn it back
        // off rather than just leave it: the explicit colours below are Windows 11 only, so on
        // 10 this flag is the whole story.
        var dark = theme.Light ? 0 : 1;
        if (Set(hwnd, UseImmersiveDarkMode, ref dark) != 0)
            Set(hwnd, UseImmersiveDarkModePre20H1, ref dark);

        // Unsupported on Windows 10; the call just returns a failure HRESULT and the
        // immersive dark mode above already carries that case.
        var caption = ColorRef(theme.Mantle);
        var border = ColorRef(theme.Surface1);
        var text = ColorRef(theme.Text);
        Set(hwnd, CaptionColor, ref caption);
        Set(hwnd, BorderColor, ref border);
        Set(hwnd, TextColor, ref text);
    }

    /// <summary>A COLORREF is 0x00BBGGRR -- the channel order is the reverse of the #RRGGBB
    /// the palettes are written in, which the old pure-grey palette hid.</summary>
    private static int ColorRef(string hex)
    {
        var c = Theme.Parse(hex);
        return c.R | (c.G << 8) | (c.B << 16);
    }

    private static int Set(IntPtr hwnd, int attribute, ref int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    // DllImport rather than LibraryImport: the latter needs AllowUnsafeBlocks turned on
    // project-wide, which is a lot of blast radius for one call.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int size
    );
}
