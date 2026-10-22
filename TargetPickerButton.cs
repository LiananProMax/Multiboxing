using System.Windows;
using System.Windows.Input;
using DrawingPoint = System.Drawing.Point;
using Media = System.Windows.Media;
using WinFormsControl = System.Windows.Forms.Control;
using WpfCursors = System.Windows.Input.Cursors;
using WpfPoint = System.Windows.Point;

namespace KeyMouseSyncReplica;

internal sealed class TargetPickerButton : FrameworkElement
{
    private bool _dragging;

    public TargetPickerButton()
    {
        Width = 48;
        Height = 48;
        MinWidth = 48;
        MinHeight = 48;
        Cursor = WpfCursors.Hand;
        Focusable = false;
    }

    public event EventHandler<TargetPickedEventArgs>? PickCompleted;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        _dragging = true;
        CaptureMouse();
        Cursor = WpfCursors.Cross;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        Cursor = WpfCursors.Hand;
        InvalidateVisual();
        PickCompleted?.Invoke(this, new TargetPickedEventArgs(WinFormsControl.MousePosition));
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Cursor = WpfCursors.Hand;
        InvalidateVisual();
    }

    protected override void OnRender(Media.DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var bounds = new Rect(2, 2, Math.Max(0, ActualWidth - 4), Math.Max(0, ActualHeight - 4));

        // Liquid-glass tile: translucent white gradient fill with a bright rim.
        var background = new Media.LinearGradientBrush(
            _dragging
                ? Media.Color.FromArgb(0x59, 0x7D, 0xD3, 0xFC)
                : Media.Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF),
            _dragging
                ? Media.Color.FromArgb(0x26, 0x7D, 0xD3, 0xFC)
                : Media.Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF),
            new WpfPoint(0, 0),
            new WpfPoint(0, 1));
        var border = _dragging
            ? new Media.SolidColorBrush(Media.Color.FromRgb(125, 211, 252))
            : new Media.SolidColorBrush(Media.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));

        drawingContext.DrawRoundedRectangle(background, new Media.Pen(border, _dragging ? 2 : 1.5), bounds, 14, 14);

        // Top sheen, like light running across the upper edge of the glass.
        var sheen = new Rect(bounds.Left + 6, bounds.Top + 3, Math.Max(0, bounds.Width - 12), bounds.Height / 3);
        var sheenBrush = new Media.LinearGradientBrush(
            Media.Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF),
            Media.Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
            new WpfPoint(0, 0),
            new WpfPoint(0, 1));
        drawingContext.DrawRoundedRectangle(sheenBrush, null, sheen, 8, 8);

        var center = new WpfPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var radius = Math.Min(bounds.Width, bounds.Height) / 4;
        var crossColor = _dragging
            ? Media.Color.FromRgb(186, 230, 253)
            : Media.Color.FromRgb(241, 245, 249);
        var crossPen = new Media.Pen(new Media.SolidColorBrush(crossColor), 3.5)
        {
            StartLineCap = Media.PenLineCap.Round,
            EndLineCap = Media.PenLineCap.Round
        };
        var thinPen = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), 1.5);

        drawingContext.DrawEllipse(null, thinPen, center, radius, radius);
        drawingContext.DrawLine(crossPen, new WpfPoint(center.X, bounds.Top + 8), new WpfPoint(center.X, bounds.Bottom - 8));
        drawingContext.DrawLine(crossPen, new WpfPoint(bounds.Left + 8, center.Y), new WpfPoint(bounds.Right - 8, center.Y));
        drawingContext.DrawEllipse(new Media.SolidColorBrush(Media.Color.FromArgb(0xE6, 0x0F, 0x17, 0x2A)), thinPen, center, 4, 4);
    }
}

internal sealed class TargetPickedEventArgs : EventArgs
{
    public TargetPickedEventArgs(DrawingPoint screenPoint)
    {
        ScreenPoint = screenPoint;
    }

    public DrawingPoint ScreenPoint { get; }
}
