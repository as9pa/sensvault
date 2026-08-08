using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SensVault;

/// <summary>
/// A DPI entry box that doubles as a preset cycler. A single click steps to the next
/// preset, a double click drops a caret in for a one-off value, and the arrow opens the
/// full list plus a way into the preset manager.
/// </summary>
public partial class DpiPicker : UserControl
{
    private ObservableCollection<DpiPreset> _presets = [];
    private bool _echo; // set while mirroring another picker, so Changed is not re-raised

    public DpiPicker() => InitializeComponent();

    /// <summary>Raised when the user changed the value, but not when <see cref="SetText"/>
    /// wrote it -- that is what stops two mirrored pickers from ping-ponging.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the drop-down's "+" is used. The owner decides what a preset
    /// manager looks like; the picker only asks for one.</summary>
    public event EventHandler? ManageRequested;

    public string Text => Box.Text;

    /// <summary>Points the picker at the shared preset list. Live, so edits made in the
    /// manager show up in every open picker without re-binding.</summary>
    public void Bind(ObservableCollection<DpiPreset> presets)
    {
        _presets = presets;
        PresetList.ItemsSource = presets;
    }

    /// <summary>Writes the box without raising <see cref="Changed"/>.</summary>
    public void SetText(string text)
    {
        if (Box.Text == text)
            return;
        _echo = true;
        Box.Text = text;
        _echo = false;
    }

    private void Box_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_echo)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    // A single click cycles; a double click is the way in to typing, so it has to reach
    // the TextBox untouched. Focus and select-all on the way past means the click that
    // cycled has also armed the box -- start typing and the preset is replaced.
    private void Box_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
            return;

        Cycle();
        Box.Focus();
        Box.SelectAll();
        e.Handled = true;
    }

    private void Cycle()
    {
        if (_presets.Count == 0)
            return;

        // Positional rather than numeric: the list is hand-ordered in the manager and the
        // cycle follows that order. A typed-in value matches nothing and starts over at
        // the top, which is the only sensible place to resume from.
        var current = Parse(Box.Text);
        var at = -1;
        for (var i = 0; i < _presets.Count; i++)
        {
            if (!_presets[i].Value.Equals(current))
                continue;
            at = i;
            break;
        }

        Write(_presets[at < 0 ? 0 : (at + 1) % _presets.Count]);
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DpiPreset preset })
            Write(preset);
        Menu.IsOpen = false;
    }

    private void Manage_Click(object sender, RoutedEventArgs e)
    {
        Menu.IsOpen = false;
        ManageRequested?.Invoke(this, EventArgs.Empty);
    }

    // Matching the frame's width has to wait for the popup to open: before that the card
    // has never been measured, and Frame may not have been laid out either.
    private void Menu_Opened(object sender, EventArgs e) => Card.MinWidth = Frame.ActualWidth;

    /// <summary>Sets the box the way a user would, so Changed fires and mirrors follow.</summary>
    private void Write(DpiPreset preset) =>
        Box.Text = preset.Value.ToString("G6", CultureInfo.CurrentCulture);

    private static double Parse(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ? v : 0;
}
