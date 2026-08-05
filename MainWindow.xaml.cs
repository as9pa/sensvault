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
using System.Windows.Media.Animation;

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

    /// <summary>Bound to by the in-cell game picker, which cannot reach a private field.</summary>
    public ObservableCollection<Game> Games => _games;

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

        DpiBox.Text =
            _data.LastDpi > 0 ? _data.LastDpi.ToString("G6", CultureInfo.CurrentCulture) : "800";

        Grid_.BeginningEdit += Grid_BeginningEdit;
        Grid_.CellEditEnding += (_, _) => _editing = false;
        Grid_.RowEditEnding += (_, _) => _editing = false;

        // The non-client area is drawn by the OS, not WPF, so it stays light unless asked.
        SourceInitialized += (_, _) => TitleBar.MakeDark(this);
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

    // ---------- in-cell game picker ----------

    private void GameCell_Loaded(object sender, RoutedEventArgs e)
    {
        var box = (ComboBox)sender;
        if (box.DataContext is SensProfile p)
            box.SelectedItem = _games.FirstOrDefault(g =>
                g.Name.Equals(p.Game, StringComparison.OrdinalIgnoreCase)
            );

        // Deferred: the box is still being wired up during Loaded, and opening the drop-down
        // from inside that pass leaves it unpopulated.
        box.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                box.Focus();
                box.IsDropDownOpen = true;
            })
        );
    }

    private void GameCell_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only a real pick counts. Typing text that matches nothing leaves SelectedItem null,
        // and writing that through would strip the row of the yaw its numbers depend on.
        if (sender is not ComboBox { SelectedItem: Game g, DataContext: SensProfile p })
            return;

        p.Game = g.Name;
        p.Yaw = g.Yaw;
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

        // A blank name is allowed; the game and numbers already identify the row.
        var p = _draft.Clone();
        p.Order = _profiles.Count;

        _profiles.Add(p);
        _draft.Name = "";

        var label = string.IsNullOrWhiteSpace(p.Name) ? p.Game : $"\"{p.Name}\"";
        SetStatus($"Saved {label} at {p.Cm360:F1} cm/360.");
    }

    private void DeleteSelected()
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
        if (e.Key != Key.Delete)
            return;

        // Inside an open cell editor Delete means "remove a character", not "remove the row".
        // Asking the focused cell directly beats trusting a flag that can desync.
        if (FindParent<DataGridCell>(Keyboard.FocusedElement)?.IsEditing == true)
            return;

        DeleteSelected();
        e.Handled = true;
    }

    /// <summary>
    /// Clicking anywhere that is not a profile row drops the selection, so the vault never
    /// keeps a row highlighted that you have moved on from. Tunnels from the window, so it
    /// covers the blank area under the last row as well as the whole left-hand panel.
    /// </summary>
    // PreviewMouseDown, not PreviewMouseLeftButtonDown: the latter is a Direct event that
    // WPF re-raises on each element as this one tunnels past. Only this one truly tunnels.
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            ClearSelectionUnlessRow(e.OriginalSource);
    }

    private void ClearSelectionUnlessRow(object? origin)
    {
        if (FindParent<DataGridRow>(origin) is not null)
            return;

        // Scrolling is navigation, not a change of mind about what is selected.
        if (FindParent<ScrollBar>(origin) is not null)
            return;

        if (Grid_.SelectedItems.Count == 0)
            return;

        // An open editor has to be put away first, and a rejected commit means the click
        // should not take the row out from under a half-finished edit.
        if (!Grid_.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true))
            return;

        Grid_.UnselectAll();
        Grid_.CurrentCell = default;
    }

    /// <summary>
    /// Out of the box a second single click on the current cell opens its editor, which
    /// would fight click-to-copy and make drag-reordering trip into edit mode. Only a
    /// double click (or F2, which arrives with no mouse args) may edit.
    /// </summary>
    private void Grid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.EditingEventArgs is MouseButtonEventArgs { ClickCount: < 2 })
        {
            e.Cancel = true;
            return;
        }
        _editing = true;
    }

    // ---------- click a sens to copy it ----------

    private void CopySens(SensProfile p)
    {
        // Matches the column's own formatting, so what lands on the clipboard is what the
        // row shows rather than a full-precision double.
        var text = p.Sens.ToString("G6", CultureInfo.CurrentCulture);
        try
        {
            Clipboard.SetDataObject(text, copy: true);
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open; nothing to do but say so.
            Warn("Could not copy: " + ex.Message);
            return;
        }

        ToastText.Text = $"Copied  {text}";
        ((Storyboard)FindResource("ToastPop")).Begin(this, isControllable: true);
    }

    // ---------- drag to reorder ----------

    private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = FindParent<DataGridRow>(e.OriginalSource)?.Item as SensProfile;

        if (
            e.ClickCount == 1
            && _dragItem is not null
            && FindParent<DataGridCell>(e.OriginalSource)?.Column == SensColumn
        )
            CopySens(_dragItem);
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

        return Has(p.Name, q) || Has(p.Game, q);
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
        if (_draft.Dpi > 0)
            _data.LastDpi = _draft.Dpi;
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
