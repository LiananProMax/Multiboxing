using DrawingSystemIcons = System.Drawing.SystemIcons;
using WinForms = System.Windows.Forms;

namespace Multiboxing;

internal sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon = new();
    private readonly WinForms.ContextMenuStrip _menu = new();
    private bool _disposed;

    public event EventHandler? RestoreRequested;
    public event EventHandler? ExitRequested;

    public TrayService()
    {
        _icon.Icon = DrawingSystemIcons.Application;
        _icon.Text = "多窗口键鼠同步器";
        var restore = new WinForms.ToolStripMenuItem("恢复");
        restore.Click += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
        var exit = new WinForms.ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(restore);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(exit);
        _icon.ContextMenuStrip = _menu;
        _icon.Visible = true;
        _icon.DoubleClick += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
