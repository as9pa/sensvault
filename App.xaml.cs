using System.Windows;

namespace SensVault;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Honours the Windows "Show animations" setting before the first window is built.
    ///
    /// MotionDuration is declared in App.xaml so the designer has a value; overwriting the
    /// entry here is what turns every hover and press transition off at once for someone who
    /// has asked the OS for no animation. Only written when animations are off, because the
    /// declared 120ms is already the answer in every other case.
    ///
    /// Compiled resources are realised lazily, so a template built after this point picks up
    /// the replacement.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
            Resources["MotionDuration"] = new Duration(TimeSpan.Zero);

        base.OnStartup(e);
    }
}
