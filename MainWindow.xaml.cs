using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        "Direct",
    ];

    private readonly AppData _data;
    private readonly ObservableCollection<SensProfile> _profiles;
    private readonly ObservableCollection<Game> _games = [];

    /// <summary>The Settings &gt; Games checklist: every game in the library, ticked or not.
    /// <see cref="_games"/> is what survives it, so this one is the longer of the two.</summary>
    private readonly ObservableCollection<GameToggle> _toggles = [];

    private readonly ICollectionView _view;

    /// <summary>Set while All or None is walking the checklist, so the rebuild that every
    /// tick would otherwise touch off happens once at the end instead of forty times.</summary>
    private bool _bulkToggling;

    /// <summary>The Settings &gt; Themes grid, one swatch per palette.</summary>
    private readonly ObservableCollection<ThemeCard> _themes = [];

    private readonly SensProfile _draft = new();

    /// <summary>Each vault column, the box that shows it, and the key it saves under.</summary>
    private readonly (CheckBox Box, DataGridColumn Column, string Key)[] _columns;

    /// <summary>Widths as the XAML declared them, keyed like <see cref="_columns"/>.</summary>
    private readonly Dictionary<string, DataGridLength> _declared = [];

    /// <summary>The column currently stretched to take up the leftover width.</summary>
    private DataGridColumn? _stretched;

    private bool _ready;
    private bool _editing;
    private bool _filterStale; // a game changed mid-edit; the filter list owes a rebuild
    private bool _viewStale; // and the grid's view owes a refresh, once the edit is over
    private bool _renaming; // set only while the menu's Rename opens an editor deliberately
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

        // Before anything is measured or drawn. Applying a theme only writes colours into
        // brushes the window already holds, so a later change repaints just as well -- but
        // doing it first is what stops the default flashing up behind the saved one.
        BuildThemes();

        _profiles = new ObservableCollection<SensProfile>(_data.Profiles.OrderBy(p => p.Order));

        _view = CollectionViewSource.GetDefaultView(_profiles);
        _view.Filter = FilterRow;
        Grid_.ItemsSource = _view;

        // The checklist first: RebuildGames reads it to know what to leave out.
        BuildToggles();
        GameList.ItemsSource = _toggles;

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

        NoScrollBars.IsChecked = _data.HideScrollBars;
        ApplyScrollBars();
        NoStatusBar.IsChecked = _data.HideStatusBar;
        ApplyStatusBar();
        DataPath.Text = Store.Folder;
        ShowSettingsPage();
        ApplyDirectMode();

        Grid_.BeginningEdit += Grid_BeginningEdit;
        Grid_.CellEditEnding += (_, _) => FinishEditing();
        Grid_.RowEditEnding += (_, _) => FinishEditing();

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

        // The non-client area is drawn by the OS, not WPF, so it keeps the system's colours
        // unless asked. There is no hwnd to ask until the source exists, which is why the
        // theme applied above could not do this itself.
        SourceInitialized += (_, _) => TitleBar.Apply(this, ThemeManager.Current);
        Closing += (_, _) => Save();

        _ready = true;
        SetStatus(Store.FilePath);

        // TEMP probe
        Loaded += (_, _) =>
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    // Armed only when the log path is handed in, and only if the two rows it
                    // drives are actually there. Unguarded this is an unhandled exception on
                    // the dispatcher, which is a silent instant close for anyone who just
                    // double-clicks the app -- which is everyone who is not running the probe.
                    var log = Environment.GetEnvironmentVariable("SV_PROBE_LOG");
                    if (log is null)
                        return;

                    var from = _profiles.FirstOrDefault(p => p.Name == "static 60-70");
                    var to = _games.FirstOrDefault(g => g.Name == "Valorant");
                    if (from is null || to is null)
                        return;

                    Clipboard.Clear();
                    Tabs.SelectedIndex = 1;
                    FromBox.SelectedItem = from;
                    ToBox.SelectedItem = to;
                    Grid_.UpdateLayout();
                    File.AppendAllText(
                        log,
                        $"result={ConvResult.Text} cursor={ConvCard.Cursor} tip={ConvCard.ToolTip}\n"
                    );
                    ConvCard_Click(ConvCard, null!);
                    File.AppendAllText(log, $"clipboard=[{Clipboard.GetText()}]\n");
                })
            );
    }

    // ---------- game library ----------

    /// <summary>
    /// Every game the app knows of, in the order the pickers want them, before Settings &gt;
    /// Games has had its say. Both the checklist and the pickers are built from this, so the
    /// two always agree on what exists and on what order to list it in.
    /// </summary>
    private IEnumerable<Game> Library() =>
        GameLibrary
            .BuiltIns()
            .Concat(_data.CustomGames)
            // cm/360 is not a game and does not belong buried between Call of Duty and
            // Counter-Strike, which is where its name alone would put it. It leads.
            .OrderBy(g => g.Direct ? 0 : 1)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase);

    private void RebuildGames()
    {
        var prevAdd = GameBox.SelectedItem as Game;
        var prevTo = ToBox.SelectedItem as Game;

        var hidden = _toggles
            .Where(t => !t.Shown)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _games.Clear();
        foreach (var g in Library().Where(g => !hidden.Contains(g.Name)))
            _games.Add(g);

        if (GameBox.ItemsSource is null)
        {
            GameBox.ItemsSource = _games;
            ToBox.ItemsSource = _games;
        }

        Reselect(GameBox, prevAdd);
        Reselect(ToBox, prevTo);

        GameCount.Text = $"{_games.Count} of {_toggles.Count} shown";
    }

    // ---------- settings > games ----------

    /// <summary>
    /// Builds the checklist from the whole library, ticking everything the saved list does
    /// not name. Matching is by name and case-insensitive, and a saved name that matches
    /// nothing just falls on the floor -- which is what lets a built-in be renamed or
    /// dropped in a later version without the file having to be migrated.
    /// </summary>
    private void BuildToggles()
    {
        var hidden = _data.HiddenGames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        _toggles.Clear();
        foreach (var g in Library())
        {
            var t = new GameToggle { Name = g.Name, Shown = !hidden.Contains(g.Name) };

            // Watching the toggle rather than the checkbox's Click. A tick can be set by
            // mouse, by the keyboard, or by an assistive tool going through UI Automation --
            // and that last one moves IsChecked without ever raising Click, so a Click
            // handler would let the box and the pickers drift apart. The property is the one
            // thing every route has to go through.
            t.PropertyChanged += ToggleChanged;
            _toggles.Add(t);
        }
    }

    private void ToggleChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Matches is only ever the search box talking to itself; nothing outside this page
        // cares which rows are on screen.
        if (e.PropertyName != nameof(GameToggle.Shown) || _bulkToggling)
            return;

        RebuildGames();
        Save();
    }

    private void GameSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var needle = GameSearch.Text.Trim();

        foreach (var t in _toggles)
            t.Matches =
                needle.Length == 0
                || t.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ShowAllGames_Click(object sender, RoutedEventArgs e) => SetAllShown(true);

    private void HideAllGames_Click(object sender, RoutedEventArgs e) => SetAllShown(false);

    /// <summary>
    /// Ticks or unticks everything the search has left on screen. Rows the search has
    /// collapsed are left alone: with "counter" typed, None means the four Counter-Strikes
    /// and not the other thirty-five.
    /// </summary>
    private void SetAllShown(bool shown)
    {
        _bulkToggling = true;
        try
        {
            foreach (var t in _toggles.Where(t => t.Matches))
                t.Shown = shown;
        }
        finally
        {
            _bulkToggling = false;
        }

        RebuildGames();
        Save();
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
            _draft.Direct = g.Direct;
        }
        else
        {
            _draft.Game = "";
            _draft.Yaw = 0;
            _draft.Direct = false;
        }

        ApplyDirectMode();
        Resync();
    }

    /// <summary>
    /// Locks the sensitivity box while a 1:1 game is picked. Those have no in-game
    /// sensitivity to type -- the number is a distance -- so the cm/360 box below becomes the
    /// only way in, and the sens box goes read-only and drops to the dead shade the TextBox
    /// style keeps for exactly that.
    ///
    /// It is not blanked, because for a 1:1 game the two boxes really do hold the same
    /// number. Left showing it, the pair says what the game means: type 34.5 centimetres and
    /// the sensitivity is 34.5.
    /// </summary>
    private void ApplyDirectMode()
    {
        if (SensBox is null)
            return;

        SensBox.IsReadOnly = _draft.Direct;

        // The cm/360 box is the live one now, so it has to be the one Resync computes from.
        // Without this the panel would go on deriving cm/360 from a sens nobody can reach.
        if (_draft.Direct)
            _drivenByCm = true;
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

        // What a row means is the distance it turns through, not the number in its box: the
        // same 0.4 is a different sweep in every game. So the cm/360 is what is held onto and
        // the sensitivity is re-solved for whatever game was just picked -- the row keeps
        // pointing at the same feel instead of quietly becoming a different one.
        //
        // This is also what stops a 1:1 game from wrecking the row. Its Sens *is* the
        // cm/360, so carrying a game's 0.0076 straight across used to land it at 0.0 cm; now
        // it arrives as the 37 it always was.
        var cm = p.Cm360;

        p.Game = g.Name;
        p.Yaw = g.Yaw;
        p.Direct = g.Direct;

        // A row with no game to begin with has no distance to carry, so it keeps its number.
        var sens = SensMath.SensFromCm360(g.Direct, cm, g.Yaw, p.Dpi);
        if (cm > 0 && sens > 0)
            p.Sens = sens;
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
            var sens = SensMath.SensFromCm360(
                _draft.Direct,
                ParseOrZero(CmBox.Text),
                _draft.Yaw,
                _draft.Dpi
            );
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
        // The game is optional. Without one there is no yaw, so the row simply carries no
        // cm/360 -- it is still a sens and a DPI worth writing down, and the vault shows the
        // cm/360 cell empty rather than pretending to a number it cannot work out.
        if (_draft.Sens <= 0)
        {
            Warn(_draft.Direct ? "Enter a cm/360 above zero." : "Sensitivity must be above zero.");
            return;
        }
        // Only checked where it is actually used: a distance you typed in centimetres does
        // not go through the mouse's DPI to get there.
        if (!_draft.Direct && _draft.Dpi <= 0)
        {
            Warn("DPI must be above zero.");
            return;
        }

        // A blank name is allowed; the game and numbers already identify the row.
        var p = _draft.Clone();
        p.Order = _profiles.Count;

        _profiles.Add(p);

        // Clear what belongs to the one sensitivity just saved -- its name and its two number
        // boxes -- and keep what belongs to the session. The DPI is the mouse's and does not
        // change between entries, and the game is usually the same for a run of them, so
        // asking for both again every time is just retyping.
        _draft.Name = "";
        SetText(SensBox, "");
        SetText(CmBox, "");
        _draft.Sens = 0;

        // Neither box was the one typed into any more; without this the next keystroke in
        // one of them would be treated as a correction to whichever led last time. On a 1:1
        // game there is no choice to reset to -- the sens box is locked, so cm/360 stays in
        // charge for the next entry.
        _drivenByCm = _draft.Direct;

        var label = string.IsNullOrWhiteSpace(p.Name) ? p.Label : $"\"{p.Name}\"";
        SetStatus(p.Cm360 > 0 ? $"Saved {label} at {p.Cm360:F1} cm/360." : $"Saved {label}.");
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
    /// would fight click-to-copy and make drag-reordering trip into edit mode. Only a double
    /// click, F2, or the menu's own Rename may edit.
    ///
    /// Stated as what is allowed rather than what is refused, which is the part that was
    /// wrong: refusing single clicks let everything that was not a click straight through.
    /// Closing the context menu hands focus back into the grid, the cell that lands on
    /// raises this with no mouse args behind it, and an editor opened on its own -- so
    /// picking Delete deleted the row and then dropped the row beneath it into a rename.
    /// </summary>
    private void Grid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        var wanted = e.EditingEventArgs switch
        {
            MouseButtonEventArgs m => m.ClickCount >= 2,
            KeyEventArgs k => k.Key is Key.F2,
            // Everything else -- focus changes, the grid's own housekeeping -- is only ever
            // an edit if this app asked for one.
            _ => _renaming,
        };

        if (!wanted)
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
                _renaming = true;
                try
                {
                    Grid_.BeginEdit();
                }
                finally
                {
                    _renaming = false;
                }
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

    /// <summary>One line per profile, in the same formats the grid shows. Each part is left
    /// out when it would say nothing: a cm/360 row has no meaningful sens-and-DPI behind it,
    /// and a row saved with no game has no cm/360 in front of it.</summary>
    private static string Describe(SensProfile p)
    {
        if (p.Direct)
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} — {1:F1} cm/360",
                p.Label,
                p.Cm360
            );

        var head = string.Format(
            CultureInfo.CurrentCulture,
            "{0} — {1:G6} @ {2:G6} DPI",
            p.Label,
            p.Sens,
            p.Dpi
        );
        return p.Cm360Text.Length == 0 ? head : $"{head} — {p.Cm360Text} cm/360";
    }

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
        var text =
            column == CmColumn
                ? pressed.Cm360Text
                : pressed.Sens.ToString("G6", CultureInfo.CurrentCulture);

        // An empty cm/360 cell -- a row with no game -- has nothing to put on the clipboard,
        // and a toast reading "Copied" with nothing after it would be worse than no toast.
        if (text.Length == 0)
            return;

        Copy(text);
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
        var label = string.IsNullOrWhiteSpace(moved.Name) ? moved.Label : $"\"{moved.Name}\"";
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
        ArmCopy(false);

        if (FromBox.SelectedItem is not SensProfile src)
            return;
        if (ToBox.SelectedItem is not Game dst)
            return;

        // A target of cm/360 needs no DPI to get there, so a blank one does not stop it.
        var hasDpi = TryNum(ToDpi.Text, out var dpi);
        if (!hasDpi && !dst.Direct)
            return;

        var sens = SensMath.SensFromCm360(dst.Direct, src.Cm360, dst.Yaw, dpi);
        if (sens <= 0)
            return;

        // The conversion preserves cm/360 by definition, so the source's is the result's.
        ConvResult.Text = sens.ToString("G6");
        ConvDetail.Text = dst.Direct ? dst.Name : $"{dst.Name} for {dpi:F0} DPI";
        ArmCopy(true);

        // Restating the distance next to a result that already is the distance would just be
        // the same number twice.
        if (dst.Direct)
            return;

        ConvCm.Text = $"{src.Cm360:F1} cm/360";
        ConvDot.Visibility = Visibility.Visible;
    }

    /// <summary>Turns the result card's click-to-copy affordances on and off. A hand cursor
    /// over a card reading "--" would be promising something there is nothing behind.</summary>
    private void ArmCopy(bool live)
    {
        ConvCard.Cursor = live ? Cursors.Hand : Cursors.Arrow;
        ConvCard.ToolTip = live ? "Click to copy" : null;
    }

    private void ConvCard_Click(object sender, MouseButtonEventArgs e)
    {
        // Same guard the cursor uses: nothing converted, nothing to put on the clipboard.
        if (ConvCard.Cursor != Cursors.Hand)
            return;

        Copy(ConvResult.Text);
    }

    private void SaveConverted_Click(object sender, RoutedEventArgs e)
    {
        if (FromBox.SelectedItem is not SensProfile src || ToBox.SelectedItem is not Game dst)
        {
            Warn("Pick a source profile and a target game.");
            return;
        }
        if (!TryNum(ToDpi.Text, out var dpi) && !dst.Direct)
        {
            Warn("Target DPI must be a positive number.");
            return;
        }

        var sens = SensMath.SensFromCm360(dst.Direct, src.Cm360, dst.Yaw, dpi);
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
                Direct = dst.Direct,
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
        RefreshView();
        Save();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => RefreshView();

    /// <summary>
    /// Re-runs the filter over the vault, or books it in for later if a cell is being edited.
    ///
    /// A CollectionView refuses outright to refresh inside an edit transaction -- it throws
    /// rather than returning -- and the grid opens one for the whole time a cell editor is
    /// up. The in-cell game picker types-to-search, so it writes a new game on any keystroke
    /// that matches one, which reaches here through OnProfileEdited while that editor is
    /// still open. That is the crash: changing a row's game from inside the grid took the
    /// app down, and it did it most reliably on the last row of a filtered game, where the
    /// game leaving the list moves the filter box's selection as well.
    /// </summary>
    private void RefreshView()
    {
        if (_editing)
        {
            _viewStale = true;
            return;
        }
        _view?.Refresh();
    }

    /// <summary>Closes out an edit and pays off whatever it deferred.</summary>
    private void FinishEditing()
    {
        _editing = false;
        if (!_filterStale && !_viewStale)
            return;

        // Queued, because CellEditEnding fires from *inside* the transaction it is ending --
        // the commit has not happened yet, so a refresh from here would throw exactly as it
        // did before. Background priority sits after the commit and after layout.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
                if (_filterStale)
                {
                    _filterStale = false;
                    RebuildFilter();
                }
                if (_viewStale)
                {
                    _viewStale = false;
                    _view?.Refresh();
                }
            })
        );
    }

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

    private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowSettingsPage();

    /// <summary>
    /// Called from the constructor as well as on every pick. The list's SelectedIndex="0"
    /// raises its SelectionChanged while the tab is still being parsed, when the panes it
    /// wants to show do not exist yet -- so the opening page has to be settled once more
    /// after the window is built, or General comes up reading "nothing here yet".
    /// </summary>
    private void ShowSettingsPage()
    {
        if (
            SettingsTitle is null
            || SettingsBlurb is null
            || GeneralSettings is null
            || GamesSettings is null
            || ThemeSettings is null
        )
            return;

        var page = (SettingsNav.SelectedItem as ListBoxItem)?.Content as string ?? "General";
        SettingsTitle.Text = page;

        var general = page == "General";
        var games = page == "Games";
        var themes = page == "Themes";
        GeneralSettings.Visibility = general ? Visibility.Visible : Visibility.Collapsed;
        GamesSettings.Visibility = games ? Visibility.Visible : Visibility.Collapsed;
        ThemeSettings.Visibility = themes ? Visibility.Visible : Visibility.Collapsed;

        // The blurb is the placeholder for a page with no controls yet; a page that has some
        // does not need to be told it is empty.
        SettingsBlurb.Visibility =
            general || games || themes ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- general settings ----------

    private void NoScrollBars_Click(object sender, RoutedEventArgs e)
    {
        ApplyScrollBars();
        Save();
    }

    /// <summary>
    /// Opens the folder holding data.json -- the vault and any custom games are both in that
    /// one file -- so it can be backed up or hand-edited.
    ///
    /// UseShellExecute is what makes this Explorer rather than an attempt to run a directory
    /// as a program: it hands the path to the shell, which resolves the association. .NET
    /// defaults it to false outside of .NET Framework, so it has to be asked for. The folder
    /// is created first because it does not exist until the first save.
    /// </summary>
    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Store.Folder);
            Process.Start(new ProcessStartInfo(Store.Folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Warn("Could not open the folder: " + ex.Message);
        }
    }

    /// <summary>
    /// Hidden rather than Disabled: Disabled tells the panel it has no room to scroll into,
    /// so the content is measured against the viewport and simply gets cut off. Hidden keeps
    /// the scrolling -- wheel, keyboard, ScrollIntoView all still work -- and only stops the
    /// bar being drawn, which is the part that was in the way.
    /// </summary>
    private void ApplyScrollBars()
    {
        var bars =
            NoScrollBars.IsChecked == true ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;

        Grid_.VerticalScrollBarVisibility = bars;
        Grid_.HorizontalScrollBarVisibility = bars;
        CreateScroll.VerticalScrollBarVisibility = bars;
        ConvertScroll.VerticalScrollBarVisibility = bars;
        GamesScroll.VerticalScrollBarVisibility = bars;
    }

    private void NoStatusBar_Click(object sender, RoutedEventArgs e)
    {
        ApplyStatusBar();
        Save();
    }

    // ---------- themes ----------

    /// <summary>
    /// Builds the swatch grid and applies whichever theme the vault file asked for.
    ///
    /// Grouped through a CollectionView rather than by sorting the list into three, because
    /// the headings on the page are the group names -- they come from the view, so there is
    /// nowhere for a heading and its contents to drift apart.
    /// </summary>
    private void BuildThemes()
    {
        foreach (var theme in ThemeLibrary.All)
            _themes.Add(new ThemeCard { Theme = theme });

        var view = CollectionViewSource.GetDefaultView(_themes);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ThemeCard.Group)));
        ThemeList.ItemsSource = view;

        ApplyTheme(ThemeLibrary.ByName(_data.Theme));
    }

    private void ThemeCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ThemeCard card })
        {
            ApplyTheme(card.Theme);
            Save();
        }
    }

    /// <summary>Paints the app in a theme and moves the ring to its swatch.</summary>
    private void ApplyTheme(Theme theme)
    {
        ThemeManager.Apply(theme);

        foreach (var card in _themes)
            card.Selected = ReferenceEquals(card.Theme, theme);

        // No-op until the window has a handle; the SourceInitialized hook covers the first
        // call, and every later one comes through here.
        TitleBar.Apply(this, theme);
    }

    /// <summary>
    /// Collapsed rather than Hidden, unlike the scrollbars: the status line sits in an Auto
    /// row, so collapsing it hands its height back to the grid above. Hidden would leave the
    /// blank strip behind, which is the part being asked for.
    /// </summary>
    private void ApplyStatusBar() =>
        Status.Visibility =
            NoStatusBar.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;

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
            {
                // Held back mid-edit: rebuilding swaps the filter box's list, which moves its
                // selection, which refreshes the view -- see RefreshView for why that is fatal
                // while a cell editor is open.
                if (_editing)
                    _filterStale = true;
                else
                    RebuildFilter();
            }
            Save();
        }
    }

    private void Save()
    {
        if (!_ready)
            return;

        _data.Profiles = [.. _profiles];
        _data.HiddenColumns = [.. _columns.Where(c => c.Box.IsChecked != true).Select(c => c.Key)];
        _data.HiddenGames = [.. _toggles.Where(t => !t.Shown).Select(t => t.Name)];
        _data.LastFilter = FilterBox.SelectedItem as string ?? "";
        _data.VaultZoom = Zoom.ScaleX;
        _data.PanelCollapsed = LeftPanel.Visibility != Visibility.Visible;
        _data.BarCollapsed = FilterBar.Visibility != Visibility.Visible;
        _data.HideScrollBars = NoScrollBars.IsChecked == true;
        _data.HideStatusBar = NoStatusBar.IsChecked == true;
        _data.Theme = ThemeManager.Current.Name;
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

    // SetResourceReference, not an assignment of what FindResource returned. FindResource
    // hands back the brush that is in the dictionary now, and a theme change replaces it --
    // so an assigned brush is a snapshot, and the status line would be the one piece of text
    // still painted in the theme it was written under. A resource reference is the code-behind
    // spelling of DynamicResource and re-resolves on every swap, which is also what lets the
    // line keep reading as a warning across a theme change rather than quietly reverting.

    private void SetStatus(string text)
    {
        Status.SetResourceReference(TextBlock.ForegroundProperty, "Overlay0");
        Status.Text = $"{text}   │   {_profiles.Count} saved";
    }

    private void Warn(string text)
    {
        Status.SetResourceReference(TextBlock.ForegroundProperty, "Red");
        Status.Text = text;
    }

    private static double ParseOrZero(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ? v : 0;

    private static bool TryNum(string? s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value > 0;
}
