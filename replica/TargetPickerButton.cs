using System.Globalization;
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
        Width = 96;
        Height = 96;
        MinWidth = 82;
        MinHeight = 82;
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

        var bounds = new Rect(5, 5, Math.Max(0, ActualWidth - 10), Math.Max(0, ActualHeight - 10));
        var background = _dragging ? new Media.SolidColorBrush(Media.Color.FromRgb(232, 241, 255)) : Media.Brushes.White;
        var border = _dragging ? new Media.SolidColorBrush(Media.Color.FromRgb(37, 99, 235)) : new Media.SolidColorBrush(Media.Color.FromRgb(203, 213, 225));

        drawingContext.DrawRoundedRectangle(background, new Media.Pen(border, 2), bounds, 18, 18);

        var center = new WpfPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var radius = Math.Min(bounds.Width, bounds.Height) / 4;
        var crossPen = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromRgb(15, 23, 42)), 5)
        {
            StartLineCap = Media.PenLineCap.Round,
            EndLineCap = Media.PenLineCap.Round
        };
        var thinPen = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromRgb(100, 116, 139)), 2);

        drawingContext.DrawEllipse(null, thinPen, center, radius, radius);
        drawingContext.DrawLine(crossPen, new WpfPoint(center.X, bounds.Top + 15), new WpfPoint(center.X, bounds.Bottom - 15));
        drawingContext.DrawLine(crossPen, new WpfPoint(bounds.Left + 15, center.Y), new WpfPoint(bounds.Right - 15, center.Y));
        drawingContext.DrawEllipse(Media.Brushes.WhiteSmoke, thinPen, center, 8, 8);

        if (_dragging)
        {
            var text = new Media.FormattedText(
                "松开选择",
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                new Media.Typeface("Microsoft YaHei UI"),
                12,
                new Media.SolidColorBrush(Media.Color.FromRgb(37, 99, 235)),
                Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);

            drawingContext.DrawText(text, new WpfPoint((ActualWidth - text.Width) / 2, ActualHeight - 24));
        }
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
