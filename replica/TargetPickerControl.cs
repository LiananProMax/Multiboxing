namespace KeyMouseSyncReplica;

public sealed class TargetPickerControl : Control
{
    private bool _dragging;

    public TargetPickerControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(82, 82);
        MinimumSize = new Size(72, 72);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public event EventHandler<ScreenPointEventArgs>? PickCompleted;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        Capture = true;
        Cursor = Cursors.Cross;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging || e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = false;
        Capture = false;
        Cursor = Cursors.Hand;
        Invalidate();
        PickCompleted?.Invoke(this, new ScreenPointEventArgs(Control.MousePosition));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var bounds = ClientRectangle;
        bounds.Inflate(-6, -6);

        using var background = new SolidBrush(_dragging ? Color.FromArgb(235, 245, 255) : Color.White);
        using var border = new Pen(_dragging ? Color.DodgerBlue : Color.Silver, 2);
        e.Graphics.FillRectangle(background, bounds);
        e.Graphics.DrawRectangle(border, bounds);

        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var radius = Math.Min(bounds.Width, bounds.Height) / 4;
        using var crossPen = new Pen(Color.Black, 5)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        using var thinPen = new Pen(Color.DimGray, 2);

        e.Graphics.DrawEllipse(thinPen, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        e.Graphics.DrawLine(crossPen, center.X, bounds.Top + 10, center.X, bounds.Bottom - 10);
        e.Graphics.DrawLine(crossPen, bounds.Left + 10, center.Y, bounds.Right - 10, center.Y);
        e.Graphics.FillEllipse(Brushes.WhiteSmoke, center.X - 8, center.Y - 8, 16, 16);
        e.Graphics.DrawEllipse(thinPen, center.X - 8, center.Y - 8, 16, 16);

        if (_dragging)
        {
            TextRenderer.DrawText(
                e.Graphics,
                "拖到目标窗口后松开",
                Font,
                new Rectangle(0, Height - 19, Width, 18),
                Color.DodgerBlue,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

public sealed class ScreenPointEventArgs : EventArgs
{
    public ScreenPointEventArgs(Point screenPoint)
    {
        ScreenPoint = screenPoint;
    }

    public Point ScreenPoint { get; }
}
