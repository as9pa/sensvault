using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace SensVault;

/// <summary>
/// Live drag-to-reorder for the vault grid, with uniform-height rows. Past the drag
/// threshold a floating snapshot of the grabbed row rides with the cursor, the row
/// left behind becomes a translucent placeholder, and displaced neighbours slide one
/// slot with a short ease-out. On release the floating row glides into its slot
/// before the drop commits. Esc cancels; losing capture (alt-tab, popup) commits
/// what is shown.
///
/// The grid is bound to a filtered, sortable view, so nothing here can reorder
/// items itself: every index is a <em>view</em> index, and the owner translates it
/// onto the model in <c>move</c>. The sequence per drag is
/// <c>begin</c> → <c>move</c>* → <c>finish</c>.
/// </summary>
internal sealed class RowReorder
{
    private const double GhostOpacity = 0.30;

    /// <summary>One shared duration for every reorder motion — neighbours sliding a
    /// slot and the released row gliding into place.</summary>
    internal const double SlideMilliseconds = 130;

    private readonly DataGrid grid;
    private readonly Func<object, bool> blocksDrag;
    private readonly Func<int, int> begin;
    private readonly Action<int, int> move;
    private readonly Action<bool> finish;

    private Point pressPoint;
    private int pressIndex = -1;
    private int currentIndex = -1;
    private RowDragAdorner? adorner;
    private AdornerLayer? layer; // the layer the float went onto, kept for the removal
    private double grabOffsetY;
    private double rowPitch;
    private double viewTop;
    private double viewBottom;
    private bool isDragging; // capture held, the row rides with the cursor
    private bool isSettling; // released, the row is gliding into its slot

    /// <summary>True from grab until the drop lands (drag + settle glide).</summary>
    public bool IsReordering => isDragging || isSettling;

    /// <summary>True once the press being handled turned into a drag, and stays true
    /// until the next press. Lets a click-only handler (copy-on-release) tell a click
    /// from the tail of a drag without depending on handler registration order.</summary>
    public bool DidDrag { get; private set; }

    /// <param name="blocksDrag">Given the press's OriginalSource, true when a drag
    /// must not start there (e.g. an open cell editor).</param>
    /// <param name="begin">Called with the pressed row's view index the moment the
    /// drag threshold is crossed, before any visuals. Returns the view index to drag
    /// from — the owner may have changed the view's order first (dropping a column
    /// sort), which moves the row — or -1 to abandon the drag.</param>
    /// <param name="move">Reorder the model so the item at view index <c>from</c>
    /// takes the slot at view index <c>to</c>. Must not persist; the drag may be
    /// undone.</param>
    /// <param name="finish">True to keep what is shown, false to put the row back.</param>
    public RowReorder(
        DataGrid grid,
        Func<object, bool> blocksDrag,
        Func<int, int> begin,
        Action<int, int> move,
        Action<bool> finish
    )
    {
        this.grid = grid;
        this.blocksDrag = blocksDrag;
        this.begin = begin;
        this.move = move;
        this.finish = finish;

        grid.PreviewMouseLeftButtonDown += OnMouseDown;
        grid.PreviewMouseMove += OnMouseMove;
        grid.PreviewMouseLeftButtonUp += OnMouseUp;

        // Esc has to be listened for where the keystroke actually lands. Clicking a row
        // leaves keyboard focus on the window rather than on a cell, so the grid is not
        // on the key event's route at all and its own PreviewKeyDown never fires. The
        // grid is still hooked for when focus *is* inside it; whichever runs first marks
        // the key handled, so the cancel cannot happen twice.
        grid.PreviewKeyDown += OnKeyDown;
        if (Window.GetWindow(grid) is Window host)
            host.PreviewKeyDown += OnKeyDown;
        else
            grid.Loaded += HookHostWindow;

        // Losing capture mid-drag (alt-tab, popup) commits what is shown. Checks
        // isDragging, not IsReordering: Finish itself releases capture, and this must
        // not re-enter it during the settle.
        grid.LostMouseCapture += (_, _) =>
        {
            if (isDragging)
                Finish(commitDrop: true);
        };
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        DidDrag = false;

        // While the previous drop is still gliding in, the grid's visual order is
        // ahead of what the user can see settle — swallow the press so a fast click
        // cannot land on the wrong row.
        if (isSettling)
        {
            pressIndex = -1;
            e.Handled = true;
            return;
        }

        pressPoint = e.GetPosition(grid);
        pressIndex = blocksDrag(e.OriginalSource) ? -1 : IndexOfRow(e.OriginalSource);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        // Already reordering: the floating row follows the cursor continuously; the
        // placeholder hops a slot whenever the cursor crosses a neighbour, which
        // slides over animated.
        if (isDragging)
        {
            var point = e.GetPosition(grid);
            adorner?.MoveTo(point.Y - grabOffsetY);

            var target = TargetAt(point);
            if (target >= 0 && target != currentIndex)
                MoveGhost(currentIndex, target);

            // The grid extends its selection across rows on a held-button move.
            // While we own the gesture it must not.
            e.Handled = true;
            return;
        }

        if (pressIndex < 0 || e.LeftButton != MouseButtonState.Pressed)
            return;

        var position = e.GetPosition(grid);
        if (
            Math.Abs(position.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance
        )
            return;

        // Threshold crossed: hand over to the owner, then enter live-reorder mode at
        // whatever index the row ended up on.
        //
        // Claim the press *before* calling out. begin() relayouts the grid, and a WPF
        // relayout can pump a nested message loop — which delivers another MouseMove
        // into this same method with the press still live and isDragging still false.
        // That re-entry used to start a second drag: two floating rows, of which only
        // the last was tracked, leaving the other orphaned on the adorner layer.
        var pressed = pressIndex;
        pressIndex = -1;

        var start = begin(pressed);
        if (start < 0)
            return;

        currentIndex = start;
        if (!StartVisuals(start))
        {
            // The row is not realised (scrolled out from under us) — nothing to lift,
            // so hand the grab straight back.
            currentIndex = -1;
            finish(false);
            return;
        }

        DidDrag = true;
        isDragging = true;
        grid.SelectedIndex = start;
        grid.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (isDragging)
        {
            Finish(commitDrop: true);
            e.Handled = true;
        }
        pressIndex = -1;
    }

    /// <summary>Fallback for when the grid has no window yet at construction time.</summary>
    private void HookHostWindow(object sender, RoutedEventArgs e)
    {
        grid.Loaded -= HookHostWindow;
        if (Window.GetWindow(grid) is Window host)
            host.PreviewKeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && isDragging)
        {
            Finish(commitDrop: false);
            e.Handled = true;
        }
    }

    /// <summary>Snapshots the grabbed row into a floating adorner and turns the row
    /// in the grid into a translucent placeholder.</summary>
    private bool StartVisuals(int index)
    {
        if (grid.ItemContainerGenerator.ContainerFromIndex(index) is not DataGridRow row)
            return false;
        if (row.ActualHeight <= 0 || row.ActualWidth <= 0)
            return false;

        rowPitch = PitchAt(index, row);
        var rowTop = TopOf(row);
        grabOffsetY = Math.Clamp(pressPoint.Y - rowTop, 0, row.ActualHeight);

        // Snapshot before ghosting, so the floating copy is full-strength. Rendering
        // the row directly would include its layout offset inside the grid — the
        // content lands below a row-sized bitmap and comes out blank — so paint it
        // through a VisualBrush into a fresh visual at the origin.
        var atOrigin = new DrawingVisual();
        using (var ctx = atOrigin.RenderOpen())
            ctx.DrawRectangle(
                new VisualBrush(row),
                null,
                new Rect(0, 0, row.ActualWidth, row.ActualHeight)
            );

        var snapshot = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(row.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(row.ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32
        );
        snapshot.Render(atOrigin);

        // Clamp the float to the scrolling rows area rather than the whole control, so
        // it cannot ride up over the column headers. TargetAt clamps to the same band,
        // which keeps the chosen slot on screen (and so realised) at the edges.
        var viewport = Rows.Parent<ScrollContentPresenter>(row);
        viewTop = viewport is null ? 0 : TopOf(viewport);
        viewBottom = viewTop + (viewport?.ActualHeight ?? grid.RenderSize.Height);

        adorner = new RowDragAdorner(
            grid,
            snapshot,
            new Size(row.ActualWidth, row.ActualHeight),
            viewTop,
            viewBottom
        ).AsFloating();

        // Hold the layer rather than looking it up again at removal time, and sweep any
        // float a previous gesture left behind: an orphan is invisible to the field that
        // normally owns it, so without this it would sit over the grid until restart.
        layer = AdornerLayer.GetAdornerLayer(grid);
        foreach (var stray in layer?.GetAdorners(grid)?.OfType<RowDragAdorner>() ?? [])
            layer!.Remove(stray);
        layer?.Add(adorner);
        adorner.MoveTo(rowTop);

        Ghost(index);
        return true;
    }

    /// <summary>Fades the placeholder row and leaves every other realised row solid.
    /// Containers can be recreated by a reorder, so this re-states both halves rather
    /// than tracking one row.</summary>
    private void Ghost(int index)
    {
        for (var i = 0; i < grid.Items.Count; i++)
        {
            if (grid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                row.Opacity = i == index ? GhostOpacity : 1;
        }
    }

    /// <summary>Moves the placeholder to a new slot; the rows it displaces slide one
    /// slot with a short ease-out (FLIP: start at the old offset, animate to zero).</summary>
    private void MoveGhost(int from, int to)
    {
        move(from, to);
        currentIndex = to;
        grid.UpdateLayout();
        Ghost(to);

        var (lo, hi, fromOffset) =
            to > from
                ? (from, to - 1, rowPitch) // the row went down; these slid up
                : (to + 1, from, -rowPitch); // the row went up; these slid down

        for (var i = lo; i <= hi; i++)
        {
            if (grid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row)
                continue;

            var slide = new TranslateTransform(0, fromOffset);
            row.RenderTransform = slide;
            slide.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(SlideMilliseconds))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                }
            );
        }
    }

    private void Finish(bool commitDrop)
    {
        isDragging = false;
        Mouse.OverrideCursor = null;
        grid.ReleaseMouseCapture();

        var to = currentIndex;
        currentIndex = -1;

        // Runs once the floating row is gone: hand the outcome back and put the grid's
        // own rows back to full strength.
        void Land()
        {
            isSettling = false;
            if (adorner is not null)
            {
                layer?.Remove(adorner);
                adorner = null;
                layer = null;
            }
            finish(commitDrop);
            ClearRowVisuals();
        }

        // On a drop, glide the floating row into its slot before landing; it covers
        // the placeholder exactly, so the swap is seamless. Esc (and any path without
        // a visible slot) lands immediately.
        if (
            commitDrop
            && adorner is not null
            && to >= 0
            && grid.ItemContainerGenerator.ContainerFromIndex(to) is DataGridRow slot
        )
        {
            isSettling = true;
            adorner.SettleTo(TopOf(slot), Land);
        }
        else
        {
            Land();
        }
    }

    private void ClearRowVisuals()
    {
        for (var i = 0; i < grid.Items.Count; i++)
        {
            if (grid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row)
                continue;
            row.Opacity = 1;
            row.RenderTransform = null;
        }
    }

    /// <summary>
    /// Target slot for the dragged row, from pure grid arithmetic anchored to the
    /// placeholder (which never animates). Hit-testing visual bounds here would see
    /// mid-slide rows still overlapping the cursor and flip-flop forever — rows are
    /// uniform height, so the math is exact.
    /// </summary>
    private int TargetAt(Point point)
    {
        var count = grid.Items.Count;
        if (count == 0 || rowPitch <= 0 || currentIndex < 0)
            return -1;

        if (grid.ItemContainerGenerator.ContainerFromIndex(currentIndex) is not DataGridRow ghost)
            return -1;

        // Read the slot off the floating row's clamped top edge, not the raw cursor:
        // the two then always agree, including when the cursor runs off the grid.
        var floatTop = Math.Clamp(
            point.Y - grabOffsetY,
            viewTop,
            Math.Max(viewTop, viewBottom - rowPitch)
        );

        var firstTop = TopOf(ghost) - currentIndex * rowPitch;
        var target = (int)Math.Round((floatTop - firstTop) / rowPitch);
        return Math.Clamp(target, 0, count - 1);
    }

    /// <summary>Distance from one row's top to the next. Measured off a neighbour
    /// rather than taken from ActualHeight, so a gridline or margin between rows
    /// cannot accumulate into drift down a long list.</summary>
    private double PitchAt(int index, DataGridRow row)
    {
        var top = TopOf(row);

        if (
            index + 1 < grid.Items.Count
            && grid.ItemContainerGenerator.ContainerFromIndex(index + 1) is DataGridRow next
        )
            return Math.Abs(TopOf(next) - top);

        if (
            index > 0
            && grid.ItemContainerGenerator.ContainerFromIndex(index - 1) is DataGridRow previous
        )
            return Math.Abs(top - TopOf(previous));

        return row.ActualHeight;
    }

    private double TopOf(UIElement element) => element.TranslatePoint(new Point(0, 0), grid).Y;

    private int IndexOfRow(object? source) =>
        Rows.Parent<DataGridRow>(source) is DataGridRow row
            ? grid.ItemContainerGenerator.IndexFromContainer(row)
            : -1;
}

/// <summary>Visual-tree lookups shared by the grid interactions.</summary>
internal static class Rows
{
    /// <summary>Nearest ancestor of type <typeparamref name="T"/>, itself included.</summary>
    internal static T? Parent<T>(object? source)
        where T : DependencyObject
    {
        var node = source as DependencyObject;
        while (node is not null && node is not T)
        {
            // Content elements (a Run inside a TextBlock) sit outside the visual tree;
            // walking further needs a different helper than we have callers for.
            if (node is not Visual)
                return null;
            node = VisualTreeHelper.GetParent(node);
        }
        return node as T;
    }
}
