using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SensVault;

public partial class MainWindow : Window
{
    // Editing any of these is worth a write to disk; the computed ones are not.
    private static readonly HashSet<string> Persisted =
    [
        "Name",
        "Game",
        "AimType",
        "Notes",
        "Yaw",
        "Dpi",
        "Sens",
        "Multiplier",
    ];

    private readonly AppData _data;
    private readonly ObservableCollection<SensProfile> _profiles;
    private readonly ObservableCollection<Game> _games = [];
    private readonly ICollectionView _view;
    private readonly SensProfile _draft = new();
    private bool _ready;
    private bool _editing;

    public MainWindow()
    {
        InitializeComponent();

        _data = Store.Load();
        _profiles = new ObservableCollection<SensProfile>(_data.Profiles);

        _view = CollectionViewSource.GetDefaultView(_profiles);
        _view.Filter = FilterRow;
        Grid_.ItemsSource = _view;

        RebuildGames();
        DraftPanel.DataContext = _draft;
        FromBox.ItemsSource = _profiles;

        _profiles.CollectionChanged += OnProfilesChanged;
        foreach (var p in _profiles)
            p.PropertyChanged += OnProfileEdited;

        Grid_.BeginningEdit += (_, _) => _editing = true;
        Grid_.CellEditEnding += (_, _) => _editing = false;
        Closing += (_, _) => Save();

        _ready = true;
        SetStatus(Store.FilePath);
    }

    // ---------- game library ----------

    private void RebuildGames()
    {
        var prevAdd = GameBox.SelectedItem as Game;
        var prevTo = ToBox.SelectedItem as Game;

        _games.Clear();
        foreach (
            var g in GameLibrary
                .BuiltIns()
                .Concat(_data.CustomGames)
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
        )
            _games.Add(g);

        if (GameBox.ItemsSource is null)
        {
            GameBox.ItemsSource = _games;
            ToBox.ItemsSource = _games;
            GameList.ItemsSource = _games;
        }

        Reselect(GameBox, prevAdd);
        Reselect(ToBox, prevTo);
    }

    private void Reselect(Selector box, Game? previous)
    {
        if (previous is null)
            return;
        box.SelectedItem = _games.FirstOrDefault(g =>
            g.Name.Equals(previous.Name, StringComparison.OrdinalIgnoreCase)
        );
    }

    private void GameBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameBox.SelectedItem is not Game g)
            return;
        _draft.Game = g.Name;
        _draft.Yaw = g.Yaw;
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var name = NgName.Text.Trim();
        if (name.Length == 0)
        {
            Warn("Give the game a name.");
            return;
        }

        if (
            !TryNum(NgSens.Text, out var sens)
            || !TryNum(NgDpi.Text, out var dpi)
            || !TryNum(NgCm.Text, out var cm)
        )
        {
            Warn("Sens, DPI and cm/360 all need to be positive numbers.");
            return;
        }

        var yaw = SensMath.YawFromCm360(cm, sens, dpi);
        if (yaw <= 0)
        {
            Warn("Those values don't give a usable yaw constant.");
            return;
        }

        _data.CustomGames.RemoveAll(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _data.CustomGames.Add(new Game { Name = name, Yaw = yaw });
        RebuildGames();
        Save();

        NgName.Clear();
        NgCm.Clear();
        SetStatus($"Added {name} · yaw {yaw:G6}");
    }

    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not Game g)
        {
            Warn("Select a game in the list first.");
            return;
        }
        if (g.BuiltIn)
        {
            Warn($"{g.Name} is built in and can't be removed.");
            return;
        }

        _data.CustomGames.RemoveAll(x => x.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase));
        RebuildGames();
        Save();
        SetStatus($"Removed {g.Name}.");
    }

    // ---------- profiles ----------

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_draft.Yaw <= 0)
        {
            Warn("Pick a game first.");
            return;
        }
        if (_draft.Dpi <= 0 || _draft.Sens <= 0)
        {
            Warn("DPI and sens must both be above zero.");
            return;
        }

        var p = _draft.Clone();
        if (string.IsNullOrWhiteSpace(p.Name))
            p.Name = $"{p.Game} {p.AimType}".Trim();

        _profiles.Add(p);
        _draft.Name = "";
        _draft.Notes = "";
        SetStatus($"Saved \"{p.Name}\" at {p.Cm360:F1} cm/360.");
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var doomed = Grid_.SelectedItems.OfType<SensProfile>().ToList();
        if (doomed.Count == 0)
        {
            Warn("Select a row to delete.");
            return;
        }

        foreach (var p in doomed)
            _profiles.Remove(p);
        SetStatus($"Deleted {doomed.Count} profile{(doomed.Count == 1 ? "" : "s")}.");
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || _editing)
            return;
        Delete_Click(sender, e);
        e.Handled = true;
    }

    // ---------- convert ----------

    private void Convert_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;

        ConvResult.Text = "--";
        ConvDetail.Text = "";

        if (FromBox.SelectedItem is not SensProfile src)
            return;
        if (ToBox.SelectedItem is not Game dst)
            return;
        if (!TryNum(ToDpi.Text, out var dpi))
            return;

        var sens = SensMath.SensFromCm360(src.Cm360, dst.Yaw, dpi);
        if (sens <= 0)
            return;

        ConvResult.Text = sens.ToString("G6");
        ConvDetail.Text =
            $"{dst.Name} at {dpi:F0} DPI — matches {src.Name} ({src.Cm360:F1} cm/360).";
    }

    private void SaveConverted_Click(object sender, RoutedEventArgs e)
    {
        if (FromBox.SelectedItem is not SensProfile src || ToBox.SelectedItem is not Game dst)
        {
            Warn("Pick a source profile and a target game.");
            return;
        }
        if (!TryNum(ToDpi.Text, out var dpi))
        {
            Warn("Target DPI must be a positive number.");
            return;
        }

        var sens = SensMath.SensFromCm360(src.Cm360, dst.Yaw, dpi);
        if (sens <= 0)
        {
            Warn("Nothing to convert yet.");
            return;
        }

        _profiles.Add(
            new SensProfile
            {
                Name = $"{dst.Name} (from {src.Name})",
                Game = dst.Name,
                AimType = src.AimType,
                Yaw = dst.Yaw,
                Dpi = dpi,
                Sens = sens,
                Multiplier = 1,
                Notes = $"Converted from {src.Name}",
            }
        );
        SetStatus($"Saved converted profile at {src.Cm360:F1} cm/360.");
    }

    // ---------- filter ----------

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

    private bool FilterRow(object item)
    {
        if (item is not SensProfile p)
            return false;
        var q = Search.Text?.Trim();
        if (string.IsNullOrEmpty(q))
            return true;
        return Has(p.Name, q) || Has(p.Game, q) || Has(p.AimType, q) || Has(p.Notes, q);
    }

    private static bool Has(string? s, string q) =>
        s?.Contains(q, StringComparison.OrdinalIgnoreCase) == true;

    // ---------- persistence ----------

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (SensProfile p in e.OldItems)
                p.PropertyChanged -= OnProfileEdited;
        if (e.NewItems is not null)
            foreach (SensProfile p in e.NewItems)
                p.PropertyChanged += OnProfileEdited;
        Save();
    }

    private void OnProfileEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && Persisted.Contains(e.PropertyName))
            Save();
    }

    private void Save()
    {
        if (!_ready)
            return;
        _data.Profiles = [.. _profiles];
        try
        {
            Store.Save(_data);
        }
        catch (Exception ex)
        {
            Warn("Could not save: " + ex.Message);
        }
    }

    // ---------- status line ----------

    private void SetStatus(string text)
    {
        Status.Foreground = (Brush)FindResource("Overlay0");
        Status.Text = $"{text}   │   {_profiles.Count} saved";
    }

    private void Warn(string text)
    {
        Status.Foreground = (Brush)FindResource("Red");
        Status.Text = text;
    }

    private static bool TryNum(string? s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value > 0;
}
