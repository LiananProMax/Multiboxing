using System.Text;
using System.Windows;
using System.Windows.Interop;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using WinForms = System.Windows.Forms;

namespace KeyMouseSyncReplica;

internal sealed class NotificationService : IDisposable
{
    private const string AppTitle = "多窗口键鼠同步器";

    private readonly WinForms.NotifyIcon _fallbackNotifyIcon = new();
    private WinForms.Form? _activationForm;
    private Window? _activationWindow;
    private string _systemNotificationUnavailableReason = "系统通知尚未初始化。";

    public NotificationService()
    {
        _fallbackNotifyIcon.Icon = DrawingSystemIcons.Application;
        _fallbackNotifyIcon.Text = AppTitle;
        _fallbackNotifyIcon.Visible = false;
        _fallbackNotifyIcon.BalloonTipClicked += (_, _) => ActivateForm();
        _fallbackNotifyIcon.Click += (_, _) => ActivateForm();
    }

    public void SetActivationForm(WinForms.Form form)
    {
        _activationForm = form;
        _activationWindow = null;
    }

    public void SetActivationWindow(Window window)
    {
        _activationWindow = window;
        _activationForm = null;
    }

    public void InitializeSystemNotifications()
    {
        _systemNotificationUnavailableReason = string.Empty;
    }

    public void ShowWarning(WinForms.IWin32Window owner, string message)
    {
        ShowTaskDialog(
            owner,
            AppTitle,
            "需要注意",
            message,
            WinForms.TaskDialogIcon.Warning,
            WinForms.MessageBoxIcon.Warning);
    }

    public void ShowWarning(Window owner, string message)
    {
        ShowWarning(new WpfWindowOwner(owner), message);
    }

    public void ShowInformation(WinForms.IWin32Window owner, string title, string message)
    {
        ShowTaskDialog(
            owner,
            title,
            title,
            message,
            WinForms.TaskDialogIcon.Information,
            WinForms.MessageBoxIcon.Information);
    }

    public void ShowInformation(Window owner, string title, string message)
    {
        ShowInformation(new WpfWindowOwner(owner), title, message);
    }

    public void ShowMessageTestReport(WinForms.IWin32Window owner, MessageFallbackTestReport report)
    {
        var heading = report.CanPostMessage
            ? "目标窗口接受消息投递"
            : "目标窗口无法接受消息投递";
        var icon = report.CanPostMessage ? WinForms.TaskDialogIcon.Information : WinForms.TaskDialogIcon.Warning;
        var fallbackIcon = report.CanPostMessage ? WinForms.MessageBoxIcon.Information : WinForms.MessageBoxIcon.Warning;

        ShowTaskDialog(
            owner,
            "消息同步测试结果",
            heading,
            FormatMessageFallbackTestReport(report),
            icon,
            fallbackIcon);
    }

    public void ShowMessageTestReport(Window owner, MessageFallbackTestReport report)
    {
        ShowMessageTestReport(new WpfWindowOwner(owner), report);
    }

    public bool ShowSystemNotification(string title, string message)
    {
        return ShowFallbackBalloon(title, message);
    }

    public void ShowSystemNotificationOrInformation(WinForms.IWin32Window owner, string title, string message)
    {
        if (ShowSystemNotification(title, message))
        {
            return;
        }

        ShowInformation(owner, title, $"{message}\r\n\r\n{_systemNotificationUnavailableReason}");
    }

    public void ShowSystemNotificationOrInformation(Window owner, string title, string message)
    {
        if (ShowSystemNotification(title, message))
        {
            return;
        }

        ShowInformation(owner, title, $"{message}\r\n\r\n{_systemNotificationUnavailableReason}");
    }

    public void ShowSystemNotificationOrWarning(WinForms.IWin32Window owner, string title, string message)
    {
        if (ShowSystemNotification(title, message))
        {
            return;
        }

        ShowWarning(owner, $"{message}\r\n\r\n{_systemNotificationUnavailableReason}");
    }

    public void ShowSystemNotificationOrWarning(Window owner, string title, string message)
    {
        if (ShowSystemNotification(title, message))
        {
            return;
        }

        ShowWarning(owner, $"{message}\r\n\r\n{_systemNotificationUnavailableReason}");
    }

    public void Dispose()
    {
        _fallbackNotifyIcon.Visible = false;
        _fallbackNotifyIcon.Dispose();
    }

    private static void ShowTaskDialog(
        WinForms.IWin32Window owner,
        string caption,
        string heading,
        string text,
        WinForms.TaskDialogIcon icon,
        WinForms.MessageBoxIcon fallbackIcon)
    {
        try
        {
            var page = new WinForms.TaskDialogPage
            {
                Caption = caption,
                Heading = heading,
                Text = text,
                Icon = icon,
                Buttons = { WinForms.TaskDialogButton.OK }
            };

            WinForms.TaskDialog.ShowDialog(owner, page);
        }
        catch
        {
            WinForms.MessageBox.Show(owner, text, caption, WinForms.MessageBoxButtons.OK, fallbackIcon);
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

    private void ActivateForm()
    {
        if (_activationWindow != null)
        {
            ActivateWindow(_activationWindow);
            return;
        }

        var form = _activationForm;
        if (form == null || form.IsDisposed || form.Disposing)
        {
            return;
        }

        try
        {
            form.BeginInvoke((WinForms.MethodInvoker)(() =>
            {
                if (form.WindowState == WinForms.FormWindowState.Minimized)
                {
                    form.WindowState = WinForms.FormWindowState.Normal;
                }

                form.Activate();
            }));
        }
        catch (InvalidOperationException)
        {
            // The form handle may not be available during startup or shutdown.
        }
    }

    private static void ActivateWindow(Window window)
    {
        if (window.Dispatcher.HasShutdownStarted || window.Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            window.Dispatcher.BeginInvoke(() =>
            {
                if (!window.IsVisible)
                {
                    window.Show();
                }

                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                window.Activate();
            });
        }
        catch (InvalidOperationException)
        {
            // The WPF window may be closing while a notification callback arrives.
        }
        catch (TaskCanceledException)
        {
            // Dispatcher shutdown may cancel late notification activation.
        }
    }

    private bool ShowFallbackBalloon(string title, string message)
    {
        try
        {
            _fallbackNotifyIcon.Visible = true;
            _fallbackNotifyIcon.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Info);
            return true;
        }
        catch (Exception ex)
        {
            _systemNotificationUnavailableReason = $"系统托盘通知发送失败：{ex.Message}";
            return false;
        }
    }

    private sealed class WpfWindowOwner : WinForms.IWin32Window
    {
        public WpfWindowOwner(Window window)
        {
            Handle = new WindowInteropHelper(window).Handle;
        }

        public IntPtr Handle { get; }
    }
}
