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

    // COLORREF is 0x00BBGGRR. The palette is pure gray, so the channel order is moot.
    private const int Mantle = 0x000F0F0F;
    private const int Surface1 = 0x002E2E2E;
    private const int Text = 0x00E8E8E8;

    public static void MakeDark(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var on = 1;
        if (Set(hwnd, UseImmersiveDarkMode, ref on) != 0)
            Set(hwnd, UseImmersiveDarkModePre20H1, ref on);

        // Unsupported on Windows 10; the call just returns a failure HRESULT and the
        // immersive dark mode above already carries that case.
        var caption = Mantle;
        var border = Surface1;
        var text = Text;
        Set(hwnd, CaptionColor, ref caption);
        Set(hwnd, BorderColor, ref border);
        Set(hwnd, TextColor, ref text);
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
