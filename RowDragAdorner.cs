using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SensVault;

/// <summary>
/// A floating snapshot of the dragged row that rides with the cursor. Rendered on
/// the adorner layer so it floats above the grid without being part of it, and
/// clamped to the rows viewport so it never rides over the column headers.
/// </summary>
internal sealed class RowDragAdorner(
    UIElement adorned,
    ImageSource snapshot,
    Size size,
    double top,
    double bottom
) : Adorner(adorned)
{
    private static readonly Brush Backing = new SolidColorBrush(
        Color.FromArgb(0xF2, 0x1E, 0x1E, 0x1E)
    );
    private static readonly Pen Outline = new(
        new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
        1
    );
    private const double Radius = 4;

    // A dependency property (not a plain field) so SettleTo can drive it through
    // WPF's animation system.
    private static readonly DependencyProperty YProperty = DependencyProperty.Register(
        "Y",
        typeof(double),
        typeof(RowDragAdorner),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender)
    );

    /// <summary>Named to avoid hiding the inherited Initialized event (CS0108).</summary>
    public RowDragAdorner AsFloating()
    {
        IsHitTestVisible = false;
        return this;
    }

    /// <summary>Moves the floating row; clamped to the rows viewport.</summary>
    public void MoveTo(double newY)
    {
        var lowest = Math.Max(top, bottom - size.Height);
        SetValue(YProperty, Math.Clamp(newY, top, lowest));
    }

    /// <summary>Glides the floating row from wherever it is into its slot, then runs
    /// <paramref name="landed"/> (which swaps in the real row).</summary>
    public void SettleTo(double targetY, Action landed)
    {
        var glide = new DoubleAnimation(
            targetY,
            TimeSpan.FromMilliseconds(RowReorder.SlideMilliseconds)
        )
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        glide.Completed += (_, _) => landed();
        BeginAnimation(YProperty, glide);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(new Point(0, (double)GetValue(YProperty)), size);

        // The snapshot is a plain rectangle, so the rounded edge has to come from a
        // clip; the outline is then stroked over the same geometry.
        var shape = new RectangleGeometry(rect, Radius, Radius);
        dc.PushClip(shape);
        dc.DrawRectangle(Backing, null, rect);
        dc.DrawImage(snapshot, rect);
        dc.Pop();
        dc.DrawGeometry(null, Outline, shape);
    }
}
