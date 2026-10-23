using System.Text;
using System.Windows;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace Multiboxing;

internal sealed class NotificationService : IDisposable
{
    private const string AppTitle = "多窗口键鼠同步器";

    private ToastWindow? _toast;
    private bool _disposed;

    /// <summary>在 UI 线程创建并预热浮层提示窗口，避免第一条提示延迟。</summary>
    public void Initialize()
    {
        if (_toast != null || _disposed)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Initialize);
            return;
        }

        _toast = new ToastWindow();
        _toast.EnsureHandle();
        WindowPicker.ExcludedWindowHandle = _toast.Handle;
    }

    /// <summary>
    /// 显示自定义浮层提示。新消息立即覆盖旧消息，不排队、不阻塞、不抢焦点。
    /// 可从任意线程调用，内部切到 UI 线程。
    /// </summary>
    public void ShowToast(ToastKind kind, string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            dispatcher.BeginInvoke(() =>
            {
                if (_disposed)
                {
                    return;
                }

                Initialize();
                _toast?.Present(kind, title, message);
            });
        }
        catch (InvalidOperationException)
        {
            // Dispatcher 正在关闭
        }
        catch (TaskCanceledException)
        {
            // Dispatcher 关闭可能取消迟到的回调
        }
    }

    public void ShowWarning(WinForms.IWin32Window owner, string message)
    {
        ShowToast(ToastKind.Warning, "需要注意", message);
    }

    public void ShowWarning(Window owner, string message)
    {
        ShowToast(ToastKind.Warning, "需要注意", message);
    }

    // 以下长文本报告保留模态 TaskDialog（诊断摘要、消息测试报告）

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_toast != null)
            {
                if (_toast.Dispatcher.CheckAccess())
                {
                    _toast.Close();
                }
                else
                {
                    _toast.Dispatcher.BeginInvoke(() => _toast.Close());
                }
            }
        }
        catch
        {
            // 关闭阶段提示窗口可能已销毁
        }
        finally
        {
            _toast = null;
            if (WindowPicker.ExcludedWindowHandle != IntPtr.Zero)
            {
                WindowPicker.ExcludedWindowHandle = IntPtr.Zero;
            }
        }
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

    private sealed class WpfWindowOwner : WinForms.IWin32Window
    {
        public WpfWindowOwner(Window window)
        {
            Handle = new WindowInteropHelper(window).Handle;
        }

        public IntPtr Handle { get; }
    }
}
