using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
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

    /// <summary>Each vault column, the box that shows it, and the key it saves under.</summary>
    private readonly (CheckBox Box, DataGridColumn Column, string Key)[] _columns;

    /// <summary>Widths as the XAML declared them, keyed like <see cref="_columns"/>.</summary>
    private readonly Dictionary<string, DataGridLength> _declared = [];

    /// <summary>The column currently stretched to take up the leftover width.</summary>
    private DataGridColumn? _stretched;

    private bool _ready;
    private bool _editing;
    private bool _syncing; // set while writing a linked box, so its TextChanged is ignored
    private bool _drivenByCm; // true when cm/360 was the field the user last typed into

    private readonly RowReorder _reorder;
    private bool _reordering; // set while a drag owns the collection's order
    private SensProfile? _pressedProfile; // the copyable cell the current press landed on
    private DataGridColumn? _pressedColumn; // and which column it was, so the release matches
    private (SensProfile Item, int Index)? _dragOrigin; // where the dragged row came from

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

        _columns =
        [
            (ColName, NameColumn, "Name"),
            (ColGame, GameColumn, "Game"),
            (ColSens, SensColumn, "Sens"),
            (ColDpi, DpiColumn, "Dpi"),
            (ColCm, CmColumn, "Cm360"),
            (ColDate, DateColumn, "Date"),
        ];
        // What the XAML asked for, taken before anything saved is laid over it. It is what a
        // column falls back to when it stops being the one holding the leftover width.
        foreach (var (_, column, key) in _columns)
            _declared[key] = column.Width;

        ApplyHiddenColumns();
        ApplyColumnWidths();
        FitColumns();

        // Both DPI boxes are the same number -- your mouse's -- so both start on it.
        // SetText is silent, so the draft has to be told separately.
        var dpi =
            _data.LastDpi > 0 ? _data.LastDpi.ToString("G6", CultureInfo.CurrentCulture) : "800";
        SetText(DpiBox, dpi);
        SetText(ToDpi, dpi);
        _draft.Dpi = ParseOrZero(dpi);

        ApplyZoom(_data.VaultZoom);
        ShowLeftPanel(!_data.PanelCollapsed);
        ShowFilterBar(!_data.BarCollapsed);

        Grid_.BeginningEdit += Grid_BeginningEdit;
        Grid_.CellEditEnding += (_, _) => _editing = false;
        Grid_.RowEditEnding += (_, _) => _editing = false;

        _reorder = new RowReorder(
            Grid_,
            source =>
                _editing
                || Rows.Parent<TextBox>(source) is not null
                || Rows.Parent<ComboBox>(source) is not null,
            BeginReorder,
            MoveLive,
            FinishReorder
        );

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
        DpiEntered(DpiBox, ToDpi);
    }

    private void ToDpi_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
            return;
        DpiEntered(ToDpi, DpiBox);
    }

    /// <summary>
    /// One DPI, two boxes: the Create tab's and the Convert tab's target are the same
    /// number, your mouse's. Whichever was typed into wins and the other is mirrored through
    /// SetText, which raises the _syncing flag the two handlers above bail on -- that is what
    /// stops the pair from writing to each other forever.
    /// </summary>
    private void DpiEntered(TextBox source, TextBox mirror)
    {
        SetText(mirror, source.Text);
        _draft.Dpi = ParseOrZero(source.Text);
        Resync();
        RefreshConvert();
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

    /// <summary>Removes the selected rows. Callers check that there is a selection first --
    /// see <see cref="TryDeleteSelection"/> for why an empty one is not worth a complaint.</summary>
    private void DeleteSelected()
    {
        var doomed = Grid_.SelectedItems.OfType<SensProfile>().ToList();
        if (doomed.Count == 0)
            return;

        foreach (var p in doomed)
            _profiles.Remove(p);
        Renumber();
        SetStatus($"Deleted {doomed.Count} profile{(doomed.Count == 1 ? "" : "s")}.");
    }

    /// <summary>
    /// Delete is answered on the window rather than on the grid, and this is why: clicking a
    /// row leaves keyboard focus on the window, not inside the grid, so the grid is not on
    /// the key event's route and a handler there never runs. That is the same thing the
    /// reorder's Esc has to work around, and it is what had left the vault with no way to
    /// delete anything at all.
    ///
    /// Sitting on the window means the keystroke arrives from everywhere, so the checks the
    /// grid's own position used to give for free are made here instead. A press with nothing
    /// selected is one of them, and it passes quietly -- from up here that is just Delete
    /// pressed somewhere else in the app, not a mistake worth a warning.
    /// </summary>
    private bool TryDeleteSelection()
    {
        if (_reorder.IsReordering || Grid_.SelectedItems.Count == 0)
            return false;

        // Wherever text is being typed -- the entry panel's boxes, the game cell's editable
        // combo, an open cell editor -- Delete belongs to the caret and not to the row.
        if (Keyboard.FocusedElement is TextBoxBase)
            return false;
        if (Rows.Parent<DataGridCell>(Keyboard.FocusedElement)?.IsEditing == true)
            return false;

        DeleteSelected();
        return true;
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
        if (Rows.Parent<DataGridRow>(origin) is not null)
            return;

        // Scrolling is navigation, not a change of mind about what is selected.
        if (Rows.Parent<ScrollBar>(origin) is not null)
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

    // ---------- right-click menu ----------
    //
    // Multi-row aware throughout: every action reads the selection, not the one row the
    // cursor happens to be over, so a menu opened on a five-row pick acts on all five.

    /// <summary>Private clipboard format carrying whole profiles, so a copy can be pasted
    /// back as a row rather than as the line of text that rides along for everywhere else.
    /// </summary>
    private const string ProfileFormat = "SensVaultProfiles";

    /// <summary>
    /// Right-click selects what it is about to act on. A click already inside the selection
    /// leaves it alone -- otherwise opening the menu on a multi-row pick would silently throw
    /// away everything but the row under the cursor.
    /// </summary>
    private void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Rows.Parent<DataGridRow>(e.OriginalSource)?.Item is not SensProfile row)
            return;

        if (!Grid_.SelectedItems.Contains(row))
        {
            Grid_.SelectedItems.Clear();
            Grid_.SelectedItem = row;
        }
    }

    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Right-clicking the empty area below the last row still opens the menu, with only
        // Paste live -- that is the one way to get a row into an empty vault from a copy.
        var picked = Grid_.SelectedItems.Count;

        DuplicateItem.IsEnabled = picked > 0;
        CopyItem.IsEnabled = picked > 0;
        DeleteItem.IsEnabled = picked > 0;

        // Renaming is an edit in the Name cell, so it needs one row and a column to put the
        // caret in. With Name hidden there is nowhere for the editor to open.
        RenameItem.IsEnabled = picked == 1 && NameColumn.Visibility == Visibility.Visible;

        PasteItem.IsEnabled = HasProfilesOnClipboard();
    }

    private void RowRename_Click(object sender, RoutedEventArgs e)
    {
        if (Grid_.SelectedItem is not SensProfile row)
            return;

        Grid_.CurrentCell = new DataGridCellInfo(row, NameColumn);
        Grid_.ScrollIntoView(row, NameColumn);

        // Queued behind the menu's own close. Opening an editor while the menu still holds
        // focus lands the caret nowhere and the cell drops straight back out of edit mode.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                Grid_.Focus();
                Grid_.BeginEdit();
            })
        );
    }

    private void RowDuplicate_Click(object sender, RoutedEventArgs e)
    {
        var picked = Grid_.SelectedItems.OfType<SensProfile>().ToList();
        if (picked.Count == 0)
            return;

        // Each copy lands directly under the row it came from rather than at the end, which
        // is where you look for it. Walked back to front so inserting cannot shift an index
        // that has not been used yet.
        SensProfile? landed = null;
        foreach (var source in picked.OrderByDescending(_profiles.IndexOf))
        {
            var copy = source.Clone();
            if (!string.IsNullOrWhiteSpace(copy.Name))
                copy.Name += " copy";

            _profiles.Insert(_profiles.IndexOf(source) + 1, copy);
            landed = copy;
        }

        Renumber();
        SelectOnly(landed);
        SetStatus($"Duplicated {Count(picked.Count)}.");
    }

    private void RowDelete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void RowCopy_Click(object sender, RoutedEventArgs e)
    {
        var picked = Grid_.SelectedItems.OfType<SensProfile>().ToList();
        if (picked.Count == 0)
            return;

        // Two payloads on the one clipboard entry. The private format is what Paste reads
        // back; the text is what every other program gets, so pasting into a chat box gives
        // a readable line rather than a wall of JSON.
        var text = string.Join(Environment.NewLine, picked.Select(Describe));

        try
        {
            var payload = new DataObject();
            payload.SetData(DataFormats.UnicodeText, text);
            payload.SetData(ProfileFormat, JsonSerializer.Serialize(picked));
            Clipboard.SetDataObject(payload, copy: true);
        }
        catch (Exception ex)
        {
            Warn("Could not copy: " + ex.Message);
            return;
        }

        ToastText.Text = $"Copied  {Count(picked.Count)}";
        ((Storyboard)FindResource("ToastPop")).Begin(this, isControllable: true);
    }

    private void RowPaste_Click(object sender, RoutedEventArgs e)
    {
        var pasted = ProfilesFromClipboard();
        if (pasted.Count == 0)
        {
            Warn("Nothing on the clipboard to paste.");
            return;
        }

        SensProfile? landed = null;
        foreach (var p in pasted)
        {
            p.Added = DateTime.Now;
            p.Order = _profiles.Count;
            _profiles.Add(p);
            landed = p;
        }

        Renumber();
        SelectOnly(landed);
        SetStatus($"Pasted {Count(pasted.Count)}.");
    }

    private bool HasProfilesOnClipboard()
    {
        try
        {
            return Clipboard.GetDataObject()?.GetDataPresent(ProfileFormat) == true;
        }
        catch
        {
            // Another process can hold the clipboard open. A menu opening is not the place
            // to complain about it -- Paste just shows as unavailable.
            return false;
        }
    }

    private static List<SensProfile> ProfilesFromClipboard()
    {
        try
        {
            if (Clipboard.GetDataObject()?.GetData(ProfileFormat) is not string json)
                return [];
            return JsonSerializer.Deserialize<List<SensProfile>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>One line per profile, in the same formats the grid shows.</summary>
    private static string Describe(SensProfile p) =>
        string.Format(
            CultureInfo.CurrentCulture,
            "{0} — {1:G6} @ {2:G6} DPI — {3:F1} cm/360",
            p.Label,
            p.Sens,
            p.Dpi,
            p.Cm360
        );

    private static string Count(int n) => $"{n} profile{(n == 1 ? "" : "s")}";

    private void SelectOnly(SensProfile? p)
    {
        if (p is null)
            return;

        Grid_.SelectedItems.Clear();
        Grid_.SelectedItem = p;
        Grid_.ScrollIntoView(p);
    }

    // ---------- click a number to copy it ----------
    //
    // Both the sens and the cm/360 copy: they are the two numbers you actually paste
    // somewhere. Copying happens on release, not on press, because those cells are also
    // drag handles and grabbing a row must not put anything on the clipboard on the way.

    private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedProfile = null;
        _pressedColumn = null;

        // A press that lands while the last drop is still gliding in is swallowed by
        // the reorder; it must not arm a copy either.
        if (_reorder.IsReordering || e.ClickCount != 1)
            return;

        var column = Rows.Parent<DataGridCell>(e.OriginalSource)?.Column;
        if (column != SensColumn && column != CmColumn)
            return;

        _pressedColumn = column;
        _pressedProfile = Rows.Parent<DataGridRow>(e.OriginalSource)?.Item as SensProfile;
    }

    private void Grid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pressed = _pressedProfile;
        var column = _pressedColumn;
        _pressedProfile = null;
        _pressedColumn = null;

        // DidDrag rather than IsReordering: it stays true for the whole press, so this
        // does not depend on which handler the reorder gets to run first.
        if (pressed is null || _reorder.DidDrag)
            return;

        // Released somewhere else — a click that changed its mind is not a copy.
        if (Rows.Parent<DataGridCell>(e.OriginalSource)?.Column != column)
            return;
        if (!ReferenceEquals(Rows.Parent<DataGridRow>(e.OriginalSource)?.Item, pressed))
            return;

        // Matches each column's own formatting, so what lands on the clipboard is what the
        // row shows rather than a full-precision double.
        Copy(
            column == CmColumn
                ? pressed.Cm360.ToString("F1", CultureInfo.CurrentCulture)
                : pressed.Sens.ToString("G6", CultureInfo.CurrentCulture)
        );
    }

    private void Copy(string text)
    {
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
    //
    // RowReorder owns the gesture and its visuals and speaks only in view indices;
    // these three callbacks are the whole of the model's side of it. Nothing reaches
    // disk until the drop lands, so a cancelled drag leaves the file untouched.

    /// <summary>Takes the grab. A manual order is only visible when no column sort is
    /// overriding it, so a sorted grid drops its sort here — the rows re-settle into
    /// their saved order and the grab carries on with the same row, wherever it went.</summary>
    private int BeginReorder(int pressedIndex)
    {
        if (pressedIndex < 0 || pressedIndex >= Grid_.Items.Count)
            return -1;
        if (Grid_.Items[pressedIndex] is not SensProfile item)
            return -1;

        if (_view.SortDescriptions.Count > 0)
        {
            _view.SortDescriptions.Clear();
            foreach (var column in Grid_.Columns)
                column.SortDirection = null;
            Grid_.ScrollIntoView(item);
        }

        _reordering = true;
        _dragOrigin = null;
        Grid_.UpdateLayout();
        return Grid_.Items.IndexOf(item);
    }

    /// <summary>
    /// Puts the dragged profile in the slot the row at <paramref name="toView"/> holds
    /// now. The view can be filtered, so these are not model indices — moving to the
    /// target's own position is what reads as "swap places" either way, and it leaves
    /// any hidden profiles around it where they were.
    /// </summary>
    private void MoveLive(int fromView, int toView)
    {
        if (
            Grid_.Items[fromView] is not SensProfile item
            || Grid_.Items[toView] is not SensProfile target
        )
            return;

        // The first hop records where the row started, which is what a cancel restores.
        _dragOrigin ??= (item, _profiles.IndexOf(item));
        _profiles.Move(_profiles.IndexOf(item), _profiles.IndexOf(target));
    }

    private void FinishReorder(bool keep)
    {
        var origin = _dragOrigin;
        _dragOrigin = null;

        // Only the dragged row ever moved, so putting it back at its old index restores
        // the collection exactly — hidden rows included.
        if (origin is not null && !keep)
            _profiles.Move(_profiles.IndexOf(origin.Value.Item), origin.Value.Index);

        _reordering = false;
        if (origin is null || !keep)
            return;

        Renumber();
        var moved = origin.Value.Item;
        var label = string.IsNullOrWhiteSpace(moved.Name) ? moved.Game : $"\"{moved.Name}\"";
        SetStatus($"Moved {label} to position {Grid_.Items.IndexOf(moved) + 1}.");
    }

    private void Renumber()
    {
        for (var i = 0; i < _profiles.Count; i++)
            _profiles[i].Order = i;
        Save();
    }

    // ---------- convert ----------

    private void Convert_Changed(object sender, RoutedEventArgs e) => RefreshConvert();

    private void RefreshConvert()
    {
        if (!_ready)
            return;

        ConvResult.Text = "--";
        ConvCm.Text = "";
        ConvDot.Visibility = Visibility.Collapsed;
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

        // The conversion preserves cm/360 by definition, so the source's is the result's.
        ConvResult.Text = sens.ToString("G6");
        ConvCm.Text = $"{src.Cm360:F1} cm/360";
        ConvDot.Visibility = Visibility.Visible;
        ConvDetail.Text = $"{dst.Name} for {dpi:F0} DPI";
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
                Name = $"{dst.Name} (from {src.Label})",
                Game = dst.Name,
                Yaw = dst.Yaw,
                Dpi = dpi,
                Sens = sens,
                Order = _profiles.Count,
                Notes = $"Converted from {src.Label}",
            }
        );
        SetStatus($"Converted to {dst.Name} at {src.Cm360:F1} cm/360.");
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

        // Keep whatever is selected now; on the first build there is nothing selected yet,
        // so fall back to the game the app was closed on. A game that no longer has any
        // profiles is not in the list, and that falls through to all games.
        FilterBox.SelectedItem =
            previous is not null && items.Contains(previous) ? previous
            : items.Contains(_data.LastFilter) ? _data.LastFilter
            : AllGames;
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        _view?.Refresh();
        Save();
    }

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

    // ---------- which columns show ----------

    private void ApplyHiddenColumns()
    {
        foreach (var (box, column, key) in _columns)
        {
            var shown = !_data.HiddenColumns.Contains(key, StringComparer.OrdinalIgnoreCase);
            box.IsChecked = shown;
            column.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        // A file claiming every column is hidden would open on an empty grid with no way
        // to tell what went wrong. Treat it as "show everything" instead.
        if (_columns.All(c => c.Box.IsChecked != true))
            foreach (var (box, column, _) in _columns)
            {
                box.IsChecked = true;
                column.Visibility = Visibility.Visible;
            }

        GuardLastColumn();
    }

    // Wired to Click, not Checked: setting IsChecked from code (restoring saved state)
    // must not look like the user toggling it.
    private void Column_Toggled(object sender, RoutedEventArgs e)
    {
        foreach (var (box, column, _) in _columns)
            column.Visibility = box.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        GuardLastColumn();
        FitColumns();
        Save();
    }

    /// <summary>
    /// Hands the leftover width to the rightmost column that is showing, so the columns
    /// always end where the grid does.
    ///
    /// Without it two things sit in that gap. The grid pads it with a dead filler strip, and
    /// any column still holding a pixel width it was given on a wider window goes on holding
    /// it -- which is what put a horizontal scrollbar under a grid whose columns were plainly
    /// all in view. The room to scroll into was inside the last column, not past it, so it
    /// read as scrolling into empty table. A star column cannot do that: it is only ever
    /// exactly the space that is left, and the scrollbar comes back only once the columns
    /// really do not fit, each pinned at its MinWidth.
    ///
    /// Only ever two columns are touched -- the one giving the job up and the one taking it
    /// on -- so a width dragged onto any other column is left alone.
    /// </summary>
    private void FitColumns()
    {
        var last = _columns
            .Where(c => c.Column.Visibility == Visibility.Visible)
            .Select(c => c.Column)
            .LastOrDefault();

        if (ReferenceEquals(_stretched, last))
            return;

        if (_stretched is not null)
        {
            var key = _columns.First(c => ReferenceEquals(c.Column, _stretched)).Key;
            _stretched.Width =
                _data.ColumnWidths.TryGetValue(key, out var saved) && saved.Value > 0
                    ? new DataGridLength(
                        saved.Value,
                        saved.Star ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel
                    )
                    : _declared[key];
        }

        _stretched = last;
        if (last is not null)
            last.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    /// <summary>The last column standing disables its own box, so the grid cannot be
    /// emptied — and it stays visible, so it is obvious why it will not turn off.</summary>
    private void GuardLastColumn()
    {
        var shown = _columns.Where(c => c.Box.IsChecked == true).ToList();
        foreach (var (box, _, _) in _columns)
            box.IsEnabled = true;
        if (shown.Count == 1)
            shown[0].Box.IsEnabled = false;
    }

    private void ApplyColumnWidths()
    {
        foreach (var (_, column, key) in _columns)
        {
            if (!_data.ColumnWidths.TryGetValue(key, out var saved) || saved.Value <= 0)
                continue;

            column.Width = new DataGridLength(
                saved.Value,
                saved.Star ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel
            );
        }
    }

    /// <summary>
    /// Reads the current widths back for saving. There is no event for "the user finished
    /// dragging a header", so this is sampled on every save instead; the last one runs on
    /// close, which is what makes the widths survive a restart.
    /// </summary>
    private void CaptureColumnWidths()
    {
        foreach (var (_, column, key) in _columns)
        {
            // A hidden column measures zero wide. Leave its stored width alone so unticking
            // and reticking a column does not collapse it to nothing.
            if (column.Visibility != Visibility.Visible)
                continue;

            // The stretched column is saved as the star it currently is, not as the pixels it
            // came in with. That is deliberate: a column that once got stranded holding a
            // wider window's width gives it up for good the first time it ends up last.

            var star = column.Width.IsStar;
            _data.ColumnWidths[key] = new ColumnWidth
            {
                Value = star ? column.Width.Value : column.ActualWidth,
                Star = star,
            };
        }
    }

    // ---------- settings ----------

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles, so every ComboBox and list inside a tab raises one
        // that arrives here. Only the TabControl's own pick means "switch mode".
        if (!ReferenceEquals(e.OriginalSource, Tabs))
            return;

        // Fires once while the tabs are still being built, before the right-hand panes exist.
        if (VaultView is null || SettingsView is null)
            return;

        var settings = ReferenceEquals(Tabs.SelectedItem, SettingsTab);
        VaultView.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsTitle is null || SettingsBlurb is null)
            return;

        SettingsTitle.Text =
            (SettingsNav.SelectedItem as ListBoxItem)?.Content as string ?? "Themes";
        SettingsBlurb.Text = "Nothing here yet.";
    }

    // ---------- zoom ----------
    //
    // Scales the vault side only. The entry panel is a fixed 340px column of forms whose
    // job does not change with how much of the list you want on screen, and letting both
    // sides grow would just have them fight over the window's width.

    private const double ZoomMin = 0.7;
    private const double ZoomMax = 2.0;
    private const double ZoomStep = 0.1;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Unmodified, and nothing to do with zoom -- but the window is the only place the
        // keystroke reliably arrives. See TryDeleteSelection.
        if (e.Key == Key.Delete && TryDeleteSelection())
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        // Both rows of keys: OemPlus/OemMinus on the main block, Add/Subtract on the numpad.
        // D0 and NumPad0 reset, matching what every browser does.
        var step = e.Key switch
        {
            Key.OemPlus or Key.Add => ZoomStep,
            Key.OemMinus or Key.Subtract => -ZoomStep,
            _ => 0,
        };

        if (step != 0)
            ApplyZoom(Zoom.ScaleX + step);
        else if (e.Key is Key.D0 or Key.NumPad0)
            ApplyZoom(1);
        else
            return;

        Save();
        e.Handled = true;
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        ApplyZoom(Zoom.ScaleX + (e.Delta > 0 ? ZoomStep : -ZoomStep));
        Save();
        e.Handled = true;
    }

    private void ApplyZoom(double scale)
    {
        // Rounded before clamping: repeated += 0.1 on a double drifts, and the status line
        // would start reporting 109% where it should say 110%.
        var next = Math.Clamp(Math.Round(scale, 2), ZoomMin, ZoomMax);
        Zoom.ScaleX = next;
        Zoom.ScaleY = next;

        // Display mode hints and pixel-snaps each glyph at its unscaled em size; a scale
        // transform then just magnifies that raster, which is why the numbers went soft at
        // anything but 100%. Ideal keeps the real outlines and re-renders them at the
        // effective device size. Display is still the sharper of the two at 1:1 though, so
        // it stays in charge there and only hands over once the grid is actually scaled.
        TextOptions.SetTextFormattingMode(
            VaultRoot,
            Math.Abs(next - 1) < 0.001 ? TextFormattingMode.Display : TextFormattingMode.Ideal
        );

        HideGridWhileItSettles();

        if (_ready)
            SetStatus($"Zoom {next:P0}");
    }

    /// <summary>
    /// Blanks the grid for one dispatcher turn while a zoom change works through it.
    ///
    /// A star-sized column's width is not recomputed during the layout pass that changes the
    /// viewport — DataGrid posts that work back to the dispatcher and picks it up afterwards.
    /// So the first frame after a scale change arranges the cells against the *old* Game
    /// width, which throws everything right of it sideways by the difference, and the next
    /// frame snaps it back. Hidden, not Collapsed: the grid keeps measuring and arranging, it
    /// just is not drawn, so only the wrong frame is skipped and nothing else reflows.
    /// </summary>
    private void HideGridWhileItSettles()
    {
        Grid_.Visibility = Visibility.Hidden;

        // Background sits below both Render (layout) and Loaded, so by the time this runs
        // the deferred width pass has already been and gone.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => Grid_.Visibility = Visibility.Visible)
        );
    }

    // ---------- collapse the entry panel ----------

    private void ToggleLeftPanel_Click(object sender, RoutedEventArgs e) =>
        ShowLeftPanel(LeftPanel.Visibility != Visibility.Visible);

    private void ShowLeftPanel(bool show)
    {
        // Settings only makes sense next to its own category list. Collapsing while it is
        // open would leave the right pane showing a settings page with no way to navigate
        // it, so fall back to the vault.
        if (!show && ReferenceEquals(Tabs.SelectedItem, SettingsTab))
            Tabs.SelectedIndex = 0;

        LeftPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LeftColumn.Width = show ? new GridLength(PanelWidth) : new GridLength(0);

        ExpandButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;

        UpdateMinWidth();
        Save();
    }

    // ---------- collapse the filter bar ----------

    private void ToggleFilterBar_Click(object sender, RoutedEventArgs e) =>
        ShowFilterBar(FilterBar.Visibility != Visibility.Visible);

    private void ShowFilterBar(bool show)
    {
        // A hidden bar is still a live filter -- the search text and the game it is pinned to
        // keep applying to the grid. Clearing both would be the bigger surprise: rows would
        // appear out of nowhere on a click that only said "give me the height back".
        // The panel's expand chevron lives inside the bar, so folding the bar takes it down
        // too. Collapsing both leaves the panel one click further away -- show the bar, then
        // the panel -- rather than parking a second copy of the same button in the rail.
        FilterBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ShowBarButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;

        UpdateMinWidth();
        Save();
    }

    // ---------- how narrow the window may get ----------

    /// <summary>A fixed column of forms. It clips rather than reflows, so it is a hard floor
    /// while it is on screen -- and none of it while it is not.</summary>
    private const double PanelWidth = 340;

    /// <summary>
    /// What the filter bar cannot go under: its two chevrons, the filter box and the columns
    /// button, all fixed-width and all of which would clip. The search box is not in the sum
    /// -- it is the star column that gives up its width first, and it is still legible with
    /// very little of it left.
    /// </summary>
    private const double BarFloor = 318;

    /// <summary>The rail is one button wide, and it is all that is left of the bar.</summary>
    private const double RailFloor = 34;

    /// <summary>VaultRoot's own left and right margins, outside either of the above.</summary>
    private const double VaultMargins = 32;

    /// <summary>MinWidth is an outer size, so it also has to cover the resize border.</summary>
    private const double WindowChrome = 16;

    /// <summary>Below this the caption buttons run out of room and the window stops being a
    /// window. Nothing in the app asks for it -- it only bites once the bar is hidden.</summary>
    private const double AbsoluteFloor = 180;

    /// <summary>
    /// The floor follows what is actually on screen instead of sitting in XAML as one number.
    /// A single value has to assume everything is open, which is what left a collapsed window
    /// pinned three times wider than the columns it was showing, with dead space beside the
    /// grid that no amount of dragging could close.
    ///
    /// The grid itself never enters into it. It scrolls horizontally rather than clipping, so
    /// which columns are showing, and how wide they are, has no say in how small it can get.
    /// </summary>
    private void UpdateMinWidth()
    {
        var vault = FilterBar.Visibility == Visibility.Visible ? BarFloor : RailFloor;
        var panel = LeftPanel.Visibility == Visibility.Visible ? PanelWidth : 0;

        MinWidth = Math.Max(WindowChrome + VaultMargins + vault + panel, AbsoluteFloor);
    }

    // ---------- persistence ----------

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (SensProfile p in e.OldItems)
                p.PropertyChanged -= OnProfileEdited;
        if (e.NewItems is not null)
            foreach (SensProfile p in e.NewItems)
                p.PropertyChanged += OnProfileEdited;

        // A drag hops the row a slot at a time. Rebuilding the filter mid-drag would
        // refresh the view out from under the gesture, and there is nothing worth
        // saving until the drop lands — no game came or went, only the order.
        if (_reordering)
            return;

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
        _data.HiddenColumns = [.. _columns.Where(c => c.Box.IsChecked != true).Select(c => c.Key)];
        _data.LastFilter = FilterBox.SelectedItem as string ?? "";
        _data.VaultZoom = Zoom.ScaleX;
        _data.PanelCollapsed = LeftPanel.Visibility != Visibility.Visible;
        _data.BarCollapsed = FilterBar.Visibility != Visibility.Visible;
        CaptureColumnWidths();
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
