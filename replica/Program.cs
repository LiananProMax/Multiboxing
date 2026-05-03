namespace KeyMouseSyncReplica;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var notifications = new NotificationService();
        notifications.InitializeSystemNotifications();

        Application.Run(new MainForm(notifications));
    }
}
