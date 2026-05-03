using System.Windows;

namespace KeyMouseSyncReplica;

public partial class App : System.Windows.Application
{
    private NotificationService? _notifications;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _notifications = new NotificationService();
        _notifications.InitializeSystemNotifications();

        var mainWindow = new MainWindow(_notifications);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifications?.Dispose();
        _notifications = null;

        base.OnExit(e);
    }
}
