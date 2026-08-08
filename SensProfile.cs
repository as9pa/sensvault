using System.ComponentModel;
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

    /// <summary>What to call this profile in a picker. A name is optional -- the game and
    /// numbers already identify a row -- so a nameless profile falls back to its game
    /// rather than showing as a blank line.</summary>
    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Name) ? Game : Name;

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

    [JsonIgnore]
    public double Cm360 => SensMath.Cm360(Yaw, Sens, Dpi);

    [JsonIgnore]
    public double In360 => SensMath.In360(Yaw, Sens, Dpi);

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
            Order = Order,
            Added = DateTime.Now,
        };

    private void Recalc()
    {
        OnChanged(nameof(Cm360));
        OnChanged(nameof(In360));
        OnChanged(nameof(Edpi));
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
