using System.Security.Principal;
using System.Text;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace KeyMouseSyncReplica;

internal sealed class NotificationService : IDisposable
{
    private const string AppTitle = "多窗口键鼠同步器";

    private readonly NotifyIcon _fallbackNotifyIcon = new();
    private bool _systemNotificationsAvailable;
    private bool _systemNotificationsRegistered;
    private Form? _activationForm;
    private string _systemNotificationUnavailableReason = "系统通知尚未初始化。";

    public NotificationService()
    {
        _fallbackNotifyIcon.Icon = SystemIcons.Application;
        _fallbackNotifyIcon.Text = AppTitle;
        _fallbackNotifyIcon.Visible = false;
        _fallbackNotifyIcon.BalloonTipClicked += (_, _) => ActivateForm();
        _fallbackNotifyIcon.Click += (_, _) => ActivateForm();
    }

    public void SetActivationForm(Form form)
    {
        _activationForm = form;
    }

    public void InitializeSystemNotifications()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            _systemNotificationUnavailableReason = "当前 Windows 版本不支持应用通知。";
            return;
        }

        if (IsProcessElevated())
        {
            _systemNotificationUnavailableReason = "Windows App SDK 不支持管理员权限进程发送系统通知。";
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += HandleNotificationInvoked;
            AppNotificationManager.Default.Register();
            _systemNotificationsRegistered = true;
            _systemNotificationsAvailable = true;
            _systemNotificationUnavailableReason = string.Empty;
        }
        catch (Exception ex)
        {
            _systemNotificationsAvailable = false;
            _systemNotificationUnavailableReason = $"系统通知初始化失败：{ex.Message}";
            TryDetachNotificationHandler();
        }
    }

    public void ShowWarning(IWin32Window owner, string message)
    {
        ShowTaskDialog(
            owner,
            AppTitle,
            "需要注意",
            message,
            TaskDialogIcon.Warning,
            MessageBoxIcon.Warning);
    }

    public void ShowInformation(IWin32Window owner, string title, string message)
    {
        ShowTaskDialog(
            owner,
            title,
            title,
            message,
            TaskDialogIcon.Information,
            MessageBoxIcon.Information);
    }

    public void ShowMessageTestReport(IWin32Window owner, MessageFallbackTestReport report)
    {
        var heading = report.CanPostMessage
            ? "目标窗口接受消息投递"
            : "目标窗口无法接受消息投递";
        var icon = report.CanPostMessage ? TaskDialogIcon.Information : TaskDialogIcon.Warning;
        var fallbackIcon = report.CanPostMessage ? MessageBoxIcon.Information : MessageBoxIcon.Warning;

        ShowTaskDialog(
            owner,
            "消息同步测试结果",
            heading,
            FormatMessageFallbackTestReport(report),
            icon,
            fallbackIcon);
    }

    public bool ShowSystemNotification(string title, string message)
    {
        if (!_systemNotificationsAvailable)
        {
            return ShowFallbackBalloon(title, message);
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddArgument("action", "activate")
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
            return true;
        }
        catch (Exception ex)
        {
            _systemNotificationsAvailable = false;
            _systemNotificationUnavailableReason = $"系统通知发送失败：{ex.Message}";
            return ShowFallbackBalloon(title, message);
        }
    }

    public void ShowSystemNotificationOrInformation(IWin32Window owner, string title, string message)
    {
        if (ShowSystemNotification(title, message))
        {
            return;
        }

        ShowInformation(owner, title, $"{message}\r\n\r\n{_systemNotificationUnavailableReason}");
    }

    public void ShowSystemNotificationOrWarning(IWin32Window owner, string title, string message)
    {
        if (ShowSystemNotification(title, message))
        {
            return;
        }

        ShowWarning(owner, $"{message}\r\n\r\n{_systemNotificationUnavailableReason}");
    }

    public void Dispose()
    {
        if (_systemNotificationsRegistered)
        {
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch
            {
                // Notification cleanup should not block application shutdown.
            }
            finally
            {
                TryDetachNotificationHandler();
                _systemNotificationsRegistered = false;
                _systemNotificationsAvailable = false;
            }
        }

        _fallbackNotifyIcon.Visible = false;
        _fallbackNotifyIcon.Dispose();
    }

    private static void ShowTaskDialog(
        IWin32Window owner,
        string caption,
        string heading,
        string text,
        TaskDialogIcon icon,
        MessageBoxIcon fallbackIcon)
    {
        try
        {
            var page = new TaskDialogPage
            {
                Caption = caption,
                Heading = heading,
                Text = text,
                Icon = icon,
                Buttons = { TaskDialogButton.OK }
            };

            TaskDialog.ShowDialog(owner, page);
        }
        catch
        {
            MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, fallbackIcon);
        }
    }

    private static string FormatMessageFallbackTestReport(MessageFallbackTestReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"主窗口：0x{report.MainWindowHandle.ToInt64():X}  {FormatTitle(report.MainWindowTitle)}");
        builder.AppendLine($"目标窗口：0x{report.TargetWindowHandle.ToInt64():X}  {FormatTitle(report.TargetWindowTitle)}");
        builder.AppendLine();
        builder.AppendLine($"消息投递探针（WM_NULL）：{(report.CanPostMessage ? "可用" : "失败")}");
        if (!report.CanPostMessage)
        {
            builder.AppendLine($"Win32 错误码：{report.LastWin32Error}");
        }

        builder.AppendLine();
        builder.AppendLine("正式同步路径：");
        builder.AppendLine($"- 键盘消息：{(report.SyncKeyboard ? "启用，将转发 WM_KEY*/WM_SYSKEY*" : "未启用")}");
        builder.AppendLine($"- 鼠标消息：{(report.SyncMouse ? "启用，将转发 WM_MOUSE*" : "未启用")}");
        builder.AppendLine();
        builder.AppendLine(report.CanPostMessage
            ? "目标窗口接受 Windows 消息投递。真实键鼠效果仍取决于目标程序是否处理这些消息。"
            : "目标窗口当前无法接受 PostMessage 投递，正式同步可能无效。");
        return builder.ToString();
    }

    private static string FormatTitle(string title)
    {
        return string.IsNullOrWhiteSpace(title) ? "(无标题窗口)" : title;
    }

    private static bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void HandleNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        ActivateForm();
    }

    private void ActivateForm()
    {
        var form = _activationForm;
        if (form == null || form.IsDisposed || form.Disposing)
        {
            return;
        }

        try
        {
            form.BeginInvoke((MethodInvoker)(() =>
            {
                if (form.WindowState == FormWindowState.Minimized)
                {
                    form.WindowState = FormWindowState.Normal;
                }

                form.Activate();
            }));
        }
        catch (InvalidOperationException)
        {
            // The form handle may not be available during startup or shutdown.
        }
    }

    private bool ShowFallbackBalloon(string title, string message)
    {
        try
        {
            _fallbackNotifyIcon.Visible = true;
            _fallbackNotifyIcon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
            return true;
        }
        catch (Exception ex)
        {
            _systemNotificationUnavailableReason = $"系统托盘通知发送失败：{ex.Message}";
            return false;
        }
    }

    private void TryDetachNotificationHandler()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked -= HandleNotificationInvoked;
        }
        catch
        {
            // The notification manager may be unavailable if registration failed early.
        }
    }
}
