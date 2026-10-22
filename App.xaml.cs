using System.Windows;
using System.Windows.Threading;

namespace KeyMouseSyncReplica;

public partial class App : System.Windows.Application
{
    private NotificationService? _notifications;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += HandleDispatcherUnhandledException;

        _notifications = new NotificationService();
        _notifications.InitializeSystemNotifications();

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
            var owner = MainWindow;
            if (_notifications != null && owner != null)
            {
                _notifications.ShowWarning(owner, $"发生未预期的错误，程序已尝试继续运行：{e.Exception.Message}");
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

        base.OnExit(e);
    }
}
