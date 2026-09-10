using System.Windows;
using System.Windows.Media;

namespace SensVault;

/// <summary>
/// The interaction paint a control template asks for by name, held on the control rather than
/// baked into the template.
///
/// The three button templates in App.xaml bind their hover and pressed washes to these, so a
/// style that wants different interaction colours -- a primary button, say -- sets a couple of
/// Setters and inherits the template unchanged. The alternative is copying a ControlTemplate
/// per variant, and two copies of a template drift apart the first time one of them is fixed.
///
/// Focus is not here: it is a FocusVisualStyle, which WPF draws only for keyboard navigation
/// and never for a click, so the ring a control wears is picked by swapping that style.
///
/// The opacities are here because the washes are painted with palette brushes, which are
/// shared and frozen: a variant that wants a 10% white sheen cannot dim the brush, only the
/// element holding it.
/// </summary>
public static class Chrome
{
    /// <summary>Wash painted over the control while the mouse is on it.</summary>
    public static readonly DependencyProperty HoverBrushProperty =
        DependencyProperty.RegisterAttached(
            "HoverBrush",
            typeof(Brush),
            typeof(Chrome),
            new PropertyMetadata(null)
        );

    public static Brush? GetHoverBrush(DependencyObject o) =>
        (Brush?)o.GetValue(HoverBrushProperty);

    public static void SetHoverBrush(DependencyObject o, Brush? value) =>
        o.SetValue(HoverBrushProperty, value);

    /// <summary>How much of <see cref="HoverBrushProperty"/> the hover wash reaches.</summary>
    public static readonly DependencyProperty HoverOpacityProperty =
        DependencyProperty.RegisterAttached(
            "HoverOpacity",
            typeof(double),
            typeof(Chrome),
            new PropertyMetadata(1.0)
        );

    public static double GetHoverOpacity(DependencyObject o) =>
        (double)o.GetValue(HoverOpacityProperty);

    public static void SetHoverOpacity(DependencyObject o, double value) =>
        o.SetValue(HoverOpacityProperty, value);

    /// <summary>Wash painted over the control while it is held down.</summary>
    public static readonly DependencyProperty PressedBrushProperty =
        DependencyProperty.RegisterAttached(
            "PressedBrush",
            typeof(Brush),
            typeof(Chrome),
            new PropertyMetadata(null)
        );

    public static Brush? GetPressedBrush(DependencyObject o) =>
        (Brush?)o.GetValue(PressedBrushProperty);

    public static void SetPressedBrush(DependencyObject o, Brush? value) =>
        o.SetValue(PressedBrushProperty, value);

    /// <summary>How much of <see cref="PressedBrushProperty"/> the pressed wash reaches.</summary>
    public static readonly DependencyProperty PressedOpacityProperty =
        DependencyProperty.RegisterAttached(
            "PressedOpacity",
            typeof(double),
            typeof(Chrome),
            new PropertyMetadata(1.0)
        );

    public static double GetPressedOpacity(DependencyObject o) =>
        (double)o.GetValue(PressedOpacityProperty);

    public static void SetPressedOpacity(DependencyObject o, double value) =>
        o.SetValue(PressedOpacityProperty, value);

    /// <summary>Prompt shown in an empty field. Read by the text box templates.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached(
            "Placeholder",
            typeof(string),
            typeof(Chrome),
            new PropertyMetadata(null)
        );

    public static string? GetPlaceholder(DependencyObject o) =>
        (string?)o.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject o, string? value) =>
        o.SetValue(PlaceholderProperty, value);
}
