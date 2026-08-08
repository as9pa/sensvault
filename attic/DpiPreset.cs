using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace SensVault;

/// <summary>
/// One entry in the DPI preset list. On disk these are plain numbers; in memory each one
/// is wrapped so the list can be edited and drag-reordered in place. The wrapper is what
/// makes reordering work at all -- the reorder moves items by reference identity, and two
/// presets both reading 800 would be indistinguishable as bare doubles.
/// </summary>
public class DpiPreset : INotifyPropertyChanged
{
    private double _value;

    public DpiPreset() { }

    public DpiPreset(double value) => _value = value;

    public double Value
    {
        get => _value;
        set
        {
            if (_value.Equals(value))
                return;
            _value = value;
            Raise(nameof(Value));
            Raise(nameof(Text));
        }
    }

    /// <summary>
    /// The row editor binds here rather than to Value. Parsing in the setter means a
    /// rejected entry leaves the preset alone, and the unconditional notify snaps the box
    /// back to what was actually accepted instead of leaving junk sitting in it.
    /// </summary>
    public string Text
    {
        get => _value > 0 ? _value.ToString("G6", CultureInfo.CurrentCulture) : "";
        set
        {
            if (
                double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out var parsed
                )
                && parsed > 0
            )
                Value = parsed;

            Raise(nameof(Text));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
