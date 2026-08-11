using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SensVault;

public class SensProfile : INotifyPropertyChanged
{
    private string _name = "";
    private string _game = "";
    private double _yaw;
    private double _dpi = 800;
    private double _sens = 1;
    private string _notes = "";
    private bool _direct;

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value))
                OnChanged(nameof(Label));
        }
    }

    public string Game
    {
        get => _game;
        set
        {
            if (Set(ref _game, value))
                OnChanged(nameof(Label));
        }
    }

    /// <summary>What to call this profile in a picker. Both a name and a game are optional --
    /// the numbers alone already identify a row -- so each step falls through to the next
    /// rather than leaving a blank line in a drop-down.</summary>
    [JsonIgnore]
    public string Label =>
        !string.IsNullOrWhiteSpace(Name) ? Name
        : !string.IsNullOrWhiteSpace(Game) ? Game
        : Cm360 > 0 ? string.Format(CultureInfo.CurrentCulture, "{0:F1} cm/360", Cm360)
        : string.Format(CultureInfo.CurrentCulture, "{0:G6} @ {1:G6} DPI", Sens, Dpi);

    public string Notes
    {
        get => _notes;
        set => Set(ref _notes, value);
    }

    public DateTime Added { get; set; } = DateTime.Now;

    /// <summary>Manual position in the list, set by drag-reordering.</summary>
    public int Order { get; set; }

    public double Yaw
    {
        get => _yaw;
        set
        {
            if (Set(ref _yaw, value))
                Recalc();
        }
    }

    public double Dpi
    {
        get => _dpi;
        set
        {
            if (Set(ref _dpi, value))
                Recalc();
        }
    }

    public double Sens
    {
        get => _sens;
        set
        {
            if (Set(ref _sens, value))
                Recalc();
        }
    }

    /// <summary>Set on rows saved under the "cm/360" pseudo-game: <see cref="Sens"/> is
    /// already the distance, so no yaw or DPI enters into it.</summary>
    public bool Direct
    {
        get => _direct;
        set
        {
            if (Set(ref _direct, value))
                Recalc();
        }
    }

    [JsonIgnore]
    public double Cm360 => SensMath.Cm360(Direct, Yaw, Sens, Dpi);

    /// <summary>What the vault's cm/360 column shows. A row saved with no game has no yaw to
    /// work from, and a blank cell says that far better than a hard "0.0" does.</summary>
    [JsonIgnore]
    public string Cm360Text => Cm360 > 0 ? Cm360.ToString("F1", CultureInfo.CurrentCulture) : "";

    [JsonIgnore]
    public double In360 => SensMath.In360(Direct, Yaw, Sens, Dpi);

    [JsonIgnore]
    public double Edpi => Dpi * Sens;

    public SensProfile Clone() =>
        new()
        {
            Name = Name,
            Game = Game,
            Notes = Notes,
            Yaw = Yaw,
            Dpi = Dpi,
            Sens = Sens,
            Direct = Direct,
            Order = Order,
            Added = DateTime.Now,
        };

    private void Recalc()
    {
        OnChanged(nameof(Cm360));
        OnChanged(nameof(Cm360Text));
        OnChanged(nameof(In360));
        OnChanged(nameof(Edpi));
        OnChanged(nameof(Label));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? prop = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnChanged(prop);
        return true;
    }

    private void OnChanged(string? prop) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
