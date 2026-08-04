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
    private const string AllGames = "All games";

    // Editing any of these is worth a write to disk; the computed ones are not.
    private static readonly HashSet<string> Persisted =
    [
        "Name",
        "Game",
        "Notes",
        "Yaw",
        "Dpi",
        "Sens",
    ];

    private readonly AppData _data;
    private readonly ObservableCollection<SensProfile> _profiles;
    private readonly ObservableCollection<Game> _games = [];
    private readonly ICollectionView _view;
    private readonly SensProfile _draft = new();

    private bool _ready;
    private bool _editing;
    private bool _syncing; // set while writing a linked box, so its TextChanged is ignored
    private bool _drivenByCm; // true when cm/360 was the field the user last typed into

    private Point _dragStart;
    private SensProfile? _dragItem;

    public MainWindow()
    {
        InitializeComponent();

        _data = Store.Load();
        _profiles = new ObservableCollection<SensProfile>(_data.Profiles.OrderBy(p => p.Order));

        _view = CollectionViewSource.GetDefaultView(_profiles);
        _view.Filter = FilterRow;
        Grid_.ItemsSource = _view;

        RebuildGames();
        RebuildFilter();
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
        // The box is editable, so typing text that matches nothing leaves SelectedItem null.
        // Clear the draft in that case rather than silently keeping the previous game's yaw.
        if (GameBox.SelectedItem is Game g)
        {
            _draft.Game = g.Name;
            _draft.Yaw = g.Yaw;
        }
        else
        {
            _draft.Game = "";
            _draft.Yaw = 0;
        }

        Resync();
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

    // ---------- linked sens / cm-360 entry ----------
    //
    // These boxes are parsed one-way rather than two-way bound. A PropertyChanged binding
    // on a double pushes the reformatted value back into the TextBox on every keystroke,
    // which resets the caret to position 0 and makes "3.4" land as ".43".

    private void DpiBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
            return;
        _draft.Dpi = ParseOrZero(DpiBox.Text);
        Resync();
    }

    private void SensBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
            return;
        _drivenByCm = false;
        _draft.Sens = ParseOrZero(SensBox.Text);
        Resync();
    }

    private void CmBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
            return;
        _drivenByCm = true;
        Resync();
    }

    /// <summary>
    /// Keeps sens and cm/360 consistent. Whichever box was typed into last wins; the other
    /// is recomputed. Changing DPI or game re-derives whichever one is not authoritative.
    /// </summary>
    private void Resync()
    {
        if (SensBox is null || CmBox is null)
            return;

        if (_drivenByCm)
        {
            var sens = SensMath.SensFromCm360(ParseOrZero(CmBox.Text), _draft.Yaw, _draft.Dpi);
            _draft.Sens = sens;
            SetText(SensBox, sens > 0 ? sens.ToString("G6", CultureInfo.CurrentCulture) : "");
        }
        else
        {
            var cm = _draft.Cm360;
            SetText(CmBox, cm > 0 ? cm.ToString("F1", CultureInfo.CurrentCulture) : "");
        }
    }

    private void SetText(TextBox box, string text)
    {
        if (box.Text == text)
            return;
        _syncing = true;
        box.Text = text;
        _syncing = false;
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
            p.Name = p.Game;
        p.Order = _profiles.Count;

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
        Renumber();
        SetStatus($"Deleted {doomed.Count} profile{(doomed.Count == 1 ? "" : "s")}.");
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || _editing)
            return;
        Delete_Click(sender, e);
        e.Handled = true;
    }

    // ---------- drag to reorder ----------

    private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = FindParent<DataGridRow>(e.OriginalSource)?.Item as SensProfile;
    }

    private void Grid_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null || _editing)
            return;

        var moved = _dragStart - e.GetPosition(null);
        if (
            Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance
        )
            return;

        DragDrop.DoDragDrop(Grid_, _dragItem, DragDropEffects.Move);
    }

    private void Grid_Drop(object sender, DragEventArgs e)
    {
        var dragged = _dragItem;
        _dragItem = null;
        if (dragged is null)
            return;

        if (FindParent<DataGridRow>(e.OriginalSource)?.Item is not SensProfile target)
            return;
        if (ReferenceEquals(target, dragged))
            return;

        var from = _profiles.IndexOf(dragged);
        var to = _profiles.IndexOf(target);
        if (from < 0 || to < 0)
            return;

        // A manual order is only visible when no column sort is overriding it.
        _view.SortDescriptions.Clear();
        _profiles.Move(from, to);
        Renumber();

        Grid_.SelectedItem = dragged;
        SetStatus($"Moved \"{dragged.Name}\" to position {to + 1}.");
    }

    private void Renumber()
    {
        for (var i = 0; i < _profiles.Count; i++)
            _profiles[i].Order = i;
        Save();
    }

    private static T? FindParent<T>(object? source)
        where T : DependencyObject
    {
        var d = source as DependencyObject;
        while (d is not null && d is not T)
        {
            if (d is not Visual)
                return null;
            d = VisualTreeHelper.GetParent(d);
        }
        return d as T;
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
                Yaw = dst.Yaw,
                Dpi = dpi,
                Sens = sens,
                Order = _profiles.Count,
                Notes = $"Converted from {src.Name}",
            }
        );
        SetStatus($"Saved converted profile at {src.Cm360:F1} cm/360.");
    }

    // ---------- filter ----------

    private void RebuildFilter()
    {
        var previous = FilterBox.SelectedItem as string;

        var items = new List<string> { AllGames };
        items.AddRange(
            _profiles
                .Select(p => p.Game)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
        );

        FilterBox.ItemsSource = items;
        FilterBox.SelectedItem =
            previous is not null && items.Contains(previous) ? previous : AllGames;
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => _view?.Refresh();

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

    private bool FilterRow(object item)
    {
        if (item is not SensProfile p)
            return false;

        if (
            FilterBox?.SelectedItem is string game
            && game != AllGames
            && !string.Equals(p.Game, game, StringComparison.OrdinalIgnoreCase)
        )
            return false;

        var q = Search?.Text?.Trim();
        if (string.IsNullOrEmpty(q))
            return true;

        return Has(p.Name, q) || Has(p.Game, q) || Has(p.Notes, q);
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

        RebuildFilter();
        Save();
    }

    private void OnProfileEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && Persisted.Contains(e.PropertyName))
        {
            if (e.PropertyName == nameof(SensProfile.Game))
                RebuildFilter();
            Save();
        }
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

    private static double ParseOrZero(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ? v : 0;

    private static bool TryNum(string? s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value > 0;
}
