using System.Windows;
using System.Windows.Threading;

namespace Multiboxing;

public partial class App : System.Windows.Application
{
    private NotificationService? _notifications;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ToastWindow 等辅助窗口同样被 WPF 记作“打开的窗口”：
        // 若用默认的 OnLastWindowClose，主窗口关掉后 Toast 的 HWND 仍
        // 保持进程存活（缩略图也就跟着残留）。改为跟随主窗口关闭退出。
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        DispatcherUnhandledException += HandleDispatcherUnhandledException;

        _notifications = new NotificationService();
        _notifications.Initialize();

        var mainWindow = new MainWindow(_notifications);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Mark handled first so a UI-thread fault (e.g. a binding error while
        // realizing a list item) cannot terminate the process and take the
        // global low-level hooks down with it.
        e.Handled = true;

        try
        {
            if (_notifications != null)
            {
                _notifications.ShowToast(ToastKind.Error, "发生未预期的错误", $"程序已尝试继续运行：{e.Exception.Message}");
            }
        }
        catch
        {
            // The notification path itself must never re-enter the dispatcher
            // exception handler.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifications?.Dispose();
        _notifications = null;
        SingleInstance.Release();

        base.OnExit(e);
    }
}
