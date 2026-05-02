using System.Runtime.InteropServices;

namespace KeyMouseSyncReplica;

public sealed class SyncManager : IDisposable
{
    private readonly InputSynchronizer _inputSynchronizer = new();

    public bool IsRunning { get; private set; }

    public bool Start(
        IReadOnlyList<WindowInfo> windows,
        WindowInfo? mainWindow,
        bool syncKeyboard,
        bool syncMouse,
        out string error)
    {
        error = string.Empty;

        if (IsRunning)
        {
            return true;
        }

        if (!Validate(windows, mainWindow, syncKeyboard, syncMouse, out error))
        {
            return false;
        }

        var targets = windows.Where(window => window.Handle != mainWindow!.Handle).ToArray();
        try
        {
            mainWindow!.BindState = "输入源";
            mainWindow.SyncState = "主操作窗口";
            mainWindow.CurrentMode = "输入源";
            mainWindow.LastError = string.Empty;

            foreach (var window in targets)
            {
                window.BindState = "消息就绪";
                window.SyncState = "消息同步中";
                window.CurrentMode = DescribeMessageMode(syncKeyboard, syncMouse);
                window.LastError = string.Empty;
            }

            _inputSynchronizer.Start(mainWindow!.Handle, targets.Select(target => target.Handle), syncKeyboard, syncMouse);
            IsRunning = true;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            Stop();
            error = $"开启同步失败：{ex.Message}";
            return false;
        }
    }

    public void Stop()
    {
        _inputSynchronizer.Stop();
        IsRunning = false;
    }

    public bool TestMessageFallback(
        WindowInfo? mainWindow,
        WindowInfo target,
        bool syncKeyboard,
        bool syncMouse,
        out MessageFallbackTestReport? report,
        out string error)
    {
        report = null;
        error = string.Empty;

        if (mainWindow == null)
        {
            error = "请先选择主操作窗口,再测试消息同步。";
            return false;
        }

        if (mainWindow.Handle == target.Handle)
        {
            error = "主操作窗口不需要测试，请选择一个同步目标窗口。";
            return false;
        }

        if (!syncKeyboard && !syncMouse)
        {
            error = "请至少勾选同步键盘操作或同步鼠标操作。";
            return false;
        }

        if (!NativeMethods.IsWindow(mainWindow.Handle))
        {
            error = "主操作窗口句柄无效。";
            return false;
        }

        if (target.Handle == IntPtr.Zero || !NativeMethods.IsWindow(target.Handle))
        {
            error = "目标窗口句柄无效。";
            return false;
        }

        var canPostMessage = NativeMethods.PostMessage(target.Handle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        report = new MessageFallbackTestReport(
            mainWindow.Handle,
            mainWindow.Title,
            target.Handle,
            target.Title,
            syncKeyboard,
            syncMouse,
            canPostMessage,
            canPostMessage ? 0 : Marshal.GetLastWin32Error());
        return true;
    }

    public void Dispose()
    {
        Stop();
        _inputSynchronizer.Dispose();
    }

    private static bool Validate(
        IReadOnlyList<WindowInfo> windows,
        WindowInfo? mainWindow,
        bool syncKeyboard,
        bool syncMouse,
        out string error)
    {

        if (mainWindow == null)
        {
            error = "请先选择主操作窗口,再开启同步！";
            return false;
        }

        if (!NativeMethods.IsWindow(mainWindow.Handle))
        {
            error = "主操作窗口句柄无效。";
            return false;
        }

        var targets = windows.Where(window => window.Handle != mainWindow.Handle).ToArray();
        if (targets.Length == 0)
        {
            error = "请至少添加一个同步目标窗口。";
            return false;
        }

        if (targets.Any(window => window.Handle == IntPtr.Zero || !NativeMethods.IsWindow(window.Handle)))
        {
            error = "同步目标窗口中存在无效句柄，请删除后重新添加。";
            return false;
        }

        if (!syncKeyboard && !syncMouse)
        {
            error = "请至少勾选同步键盘操作或同步鼠标操作。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string DescribeMessageMode(bool syncKeyboard, bool syncMouse)
    {
        return (syncKeyboard, syncMouse) switch
        {
            (true, true) => "Windows 消息同步（键盘+鼠标）",
            (true, false) => "Windows 消息同步（键盘）",
            (false, true) => "Windows 消息同步（鼠标）",
            _ => "Windows 消息同步"
        };
    }
}

public sealed record MessageFallbackTestReport(
    IntPtr MainWindowHandle,
    string MainWindowTitle,
    IntPtr TargetWindowHandle,
    string TargetWindowTitle,
    bool SyncKeyboard,
    bool SyncMouse,
    bool CanPostMessage,
    int LastWin32Error);
