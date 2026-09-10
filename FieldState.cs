using System.Windows;

namespace SensVault;

/// <summary>
/// Whether a field is holding something the app has just refused.
///
/// It is here rather than in <see cref="Chrome"/> because it is not paint: Chrome holds the
/// colours a template asks for, and this holds a fact about the control that the TextBox and
/// ComboBox styles then paint an edge from. Setting it is all a caller has to do -- which
/// control it is and which template that control wears are not its problem.
///
/// A flag rather than WPF's validation stack, because the entry panel does not bind its
/// numbers: they are parsed by hand on the way in (see the linked sens / cm-360 section in
/// MainWindow for why), so there is no binding for a ValidationRule to hang off.
/// </summary>
public static class FieldState
{
    /// <summary>Set when the app refuses what is in the field, cleared on the next edit.</summary>
    public static readonly DependencyProperty IsInvalidProperty =
        DependencyProperty.RegisterAttached(
            "IsInvalid",
            typeof(bool),
            typeof(FieldState),
            new PropertyMetadata(false)
        );

    public static bool GetIsInvalid(DependencyObject o) => (bool)o.GetValue(IsInvalidProperty);

    public static void SetIsInvalid(DependencyObject o, bool value) =>
        o.SetValue(IsInvalidProperty, value);
}
