using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SensVault;

/// <summary>
/// The DPI preset list: drag a row by its handle to reorder, edit a value in place, cross
/// it out to remove it, plus to add one. Hosted twice over the same collection -- as the
/// overlay the picker's "+" opens, and as the Settings > DPI Presets pane.
/// </summary>
public partial class DpiPresetEditor : UserControl
{
    private ObservableCollection<DpiPreset> _presets = [];
    private readonly RowReorder _reorder;
    private (DpiPreset Item, int Index)? _dragOrigin;

    public DpiPresetEditor()
    {
        InitializeComponent();

        _reorder = new RowReorder(
            Rows_,
            // The value box and the delete cross own their own clicks; everywhere else on
            // the row -- handle, label, blank space -- is a grab.
            source =>
                Rows.Parent<TextBox>(source) is not null || Rows.Parent<Button>(source) is not null,
            BeginReorder,
            MoveLive,
            FinishReorder
        );
    }

    /// <summary>True while a drag owns the order, so the owner can hold off persisting
    /// until the drop lands rather than writing the file once per hop.</summary>
    public bool IsReordering => _reorder.IsReordering;

    /// <summary>Points the editor at the shared preset list. Both instances edit the same
    /// collection, so a change in one shows up in the other with no plumbing.</summary>
    public void Bind(ObservableCollection<DpiPreset> presets)
    {
        _presets = presets;
        Rows_.ItemsSource = presets;
    }

    // ---------- reorder ----------
    //
    // Nothing filters or sorts this list, so a view index is a model index. The three
    // callbacks keep RowReorder's shape anyway: it is the grid's contract, not a
    // simplification this control gets to make.

    private int BeginReorder(int pressedIndex)
    {
        _dragOrigin = null;
        return pressedIndex >= 0 && pressedIndex < Rows_.Items.Count ? pressedIndex : -1;
    }

    private void MoveLive(int fromView, int toView)
    {
        if (
            Rows_.Items[fromView] is not DpiPreset item
            || Rows_.Items[toView] is not DpiPreset target
        )
            return;

        _dragOrigin ??= (item, _presets.IndexOf(item));
        _presets.Move(_presets.IndexOf(item), _presets.IndexOf(target));
    }

    private void FinishReorder(bool keep)
    {
        var origin = _dragOrigin;
        _dragOrigin = null;

        if (origin is null)
            return;

        // Only the dragged row moved, so putting it back at its old index restores the
        // list exactly.
        if (!keep)
            _presets.Move(_presets.IndexOf(origin.Value.Item), origin.Value.Index);
        else
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised once a drop has committed. Adds and deletes are visible through the
    /// collection itself; a reorder is too, but only its final position is worth saving.</summary>
    public event EventHandler? Changed;

    // ---------- add / remove ----------

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // Doubling continues the 400/800/1600/3200 progression the defaults set, which
        // lands on a real DPI far more often than an empty row would be useful.
        var seed = _presets.Count == 0 ? 800 : _presets[^1].Value * 2;
        var added = new DpiPreset(seed);
        _presets.Add(added);

        // The container does not exist until the grid has laid out the new item.
        Rows_.UpdateLayout();
        Rows_.ScrollIntoView(added);
        Rows_.UpdateLayout();
        FocusValueBox(added);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DpiPreset preset })
            return;

        // An empty list would leave the picker with nothing to cycle through and no way
        // back in except the manager itself.
        if (_presets.Count <= 1)
            return;

        _presets.Remove(preset);
    }

    /// <summary>Puts the caret in a row's value box, so "+" is one click and then typing.</summary>
    private void FocusValueBox(DpiPreset preset)
    {
        if (Rows_.ItemContainerGenerator.ContainerFromItem(preset) is not DataGridRow row)
            return;
        if (FindChild<TextBox>(row) is not TextBox box)
            return;

        box.Focus();
        box.SelectAll();
    }

    private static T? FindChild<T>(DependencyObject node)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T hit)
                return hit;
            if (FindChild<T>(child) is T deeper)
                return deeper;
        }
        return null;
    }
}
