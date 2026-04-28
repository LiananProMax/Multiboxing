using System.Text;

namespace KeyMouseSyncReplica;

public sealed class SyncManager : IDisposable
{
    private readonly InputSynchronizer _inputSynchronizer = new();
    private readonly Dictionary<IntPtr, DmPlugin> _sessions = new();

    public bool IsRunning { get; private set; }

    public bool Start(
        IReadOnlyList<WindowInfo> windows,
        WindowInfo? mainWindow,
        AppConfig config,
        string dmDllPath,
        bool syncKeyboard,
        bool syncMouse,
        out string error)
    {
        error = string.Empty;

        if (IsRunning)
        {
            return true;
        }

        if (!Validate(windows, mainWindow, config, dmDllPath, syncKeyboard, syncMouse, out error))
        {
            return false;
        }

        var targets = windows.Where(window => window.Handle != mainWindow!.Handle).ToArray();
        try
        {
            foreach (var window in targets)
            {
                if (!DmPlugin.TryCreate(dmDllPath, out var plugin, out error) || plugin == null)
                {
                    window.BindState = "dm创建失败";
                    window.SyncState = "消息同步";
                    continue;
                }
                var initInfo = plugin.Initialize(Path.GetDirectoryName(dmDllPath) ?? AppContext.BaseDirectory);

                var mode = int.Parse(config.Mode);
                var display = NormalizeDisplay(config.Display);
                ActivateWindow(window.Handle);
                try
                {
                    plugin.ForceUnBindWindow(window.Handle);
                }
                catch
                {
                    // If another stale binding exists, ForceUnBindWindow helps; failure should not block BindWindow.
                }

                try
                {
                    plugin.SetWindowState(window.Handle, 1);
                }
                catch
                {
                    // Some dm builds do not need SetWindowState; native activation above is still useful.
                }

                var attempts = TryBindWithFallbacks(plugin, window, display, config.Mouse, config.Keypad, config.Public, mode);
                var bindResult = attempts.LastOrDefault()?.Result ?? 0;

                if (bindResult != 1)
                {
                    var version = plugin.Version();
                    var diagnostic = BuildWindowDiagnostic(window.Handle);
                    plugin.Dispose();
                    window.BindState = "dm绑定失败";
                    window.SyncState = "消息同步";
                    window.LastError = $"dm 返回 {bindResult}; {diagnostic}; {Path.GetFileName(dmDllPath)}"
                        + (string.IsNullOrWhiteSpace(version) ? string.Empty : $" Ver={version}")
                        + $" {plugin.CreationMode}; init={initInfo}; "
                        + string.Join("; ", attempts.Select(attempt => $"{attempt.Name}={attempt.Result}/LE={FormatLastError(attempt.LastError)}"));
                    continue;
                }

                if (syncKeyboard)
                {
                    plugin.EnableKeypadSync(true);
                }

                if (syncMouse)
                {
                    plugin.EnableMouseSync(true);
                }

                _sessions[window.Handle] = plugin;
                window.BindState = "已绑定";
                window.SyncState = "同步中";
            }

            mainWindow!.BindState = "输入源";
            mainWindow.SyncState = "主操作窗口";

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

        foreach (var session in _sessions.Values)
        {
            try
            {
                session.EnableKeypadSync(false);
                session.EnableMouseSync(false);
                session.UnBindWindow();
            }
            catch
            {
                // Cleanup must be best-effort; original program also tolerates dm cleanup failures.
            }
            finally
            {
                session.Dispose();
            }
        }

        _sessions.Clear();
        IsRunning = false;
    }

    public void Dispose()
    {
        Stop();
        _inputSynchronizer.Dispose();
    }

    private static bool Validate(
        IReadOnlyList<WindowInfo> windows,
        WindowInfo? mainWindow,
        AppConfig config,
        string dmDllPath,
        bool syncKeyboard,
        bool syncMouse,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(dmDllPath) || !File.Exists(dmDllPath))
        {
            error = "请先设置dm路径";
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.Mouse) || config.Mouse == "0")
        {
            error = "请先设置mouse，例如 windows";
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.Keypad) || config.Keypad == "0")
        {
            error = "请先设置keypad，例如 windows";
            return false;
        }

        if (!int.TryParse(config.Mode, out _))
        {
            error = "请先设置mode";
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.Display))
        {
            error = "请先设置display，例如 normal";
            return false;
        }

        if (mainWindow == null)
        {
            error = "请先选择主操作窗口,再开启同步！";
            return false;
        }

        if (windows.Count(window => window.Handle != mainWindow.Handle) == 0)
        {
            error = "请至少添加一个同步目标窗口。";
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

    private static string NormalizeDisplay(string display)
    {
        return string.IsNullOrWhiteSpace(display) || display == "0" ? "normal" : display.Trim();
    }

    private static void ActivateWindow(IntPtr hwnd)
    {
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.BringWindowToTop(hwnd);
        NativeMethods.SetForegroundWindow(hwnd);
        Thread.Sleep(180);
    }

    private static List<BindAttempt> TryBindWithFallbacks(
        DmPlugin plugin,
        WindowInfo window,
        string display,
        string mouse,
        string keypad,
        string publicMode,
        int mode)
    {
        var attempts = new List<BindAttempt>();
        var hasPublic = !string.IsNullOrWhiteSpace(publicMode);

        if (!hasPublic)
        {
            attempts.Add(TryBind("BindWindow", plugin, () => plugin.BindWindow(window.Handle, display, mouse, keypad, mode)));
            if (attempts[^1].Result == 1)
            {
                return attempts;
            }

            if (string.Equals(mouse, "windows", StringComparison.OrdinalIgnoreCase))
            {
                attempts.Add(TryBind("BindWindow(windows3)", plugin, () => plugin.BindWindow(window.Handle, display, "windows3", keypad, mode)));
                if (attempts[^1].Result == 1)
                {
                    return attempts;
                }
            }

            attempts.Add(TryBind("BindWindowEx(empty-public)", plugin, () => plugin.BindWindowEx(window.Handle, display, mouse, keypad, string.Empty, mode)));
            if (attempts[^1].Result == 1)
            {
                return attempts;
            }

            if (string.Equals(mouse, "windows", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(keypad, "windows", StringComparison.OrdinalIgnoreCase))
            {
                attempts.Add(TryBind("BindWindow(dx2/dx)", plugin, () => plugin.BindWindow(window.Handle, display, "dx2", "dx", mode)));
                if (attempts[^1].Result == 1)
                {
                    return attempts;
                }

                attempts.Add(TryBind("BindWindow(dx/dx)", plugin, () => plugin.BindWindow(window.Handle, display, "dx", "dx", mode)));
                if (attempts[^1].Result == 1)
                {
                    return attempts;
                }

                attempts.Add(TryBind("BindWindowEx(dx2/dx active)", plugin, () => plugin.BindWindowEx(
                    window.Handle,
                    display,
                    "dx2",
                    "dx",
                    "dx.public.active.api|dx.public.active.message",
                    mode)));
            }
            return attempts;
        }

        attempts.Add(TryBind("BindWindowEx", plugin, () => plugin.BindWindowEx(window.Handle, display, mouse, keypad, publicMode, mode)));
        if (attempts[^1].Result == 1)
        {
            return attempts;
        }

        attempts.Add(TryBind("BindWindow", plugin, () => plugin.BindWindow(window.Handle, display, mouse, keypad, mode)));
        return attempts;
    }

    private static BindAttempt TryBind(string name, DmPlugin plugin, Func<int> bind)
    {
        var result = bind();
        return new BindAttempt(name, result, plugin.LastError());
    }

    private static string BuildWindowDiagnostic(IntPtr hwnd)
    {
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return $"hwnd=0x{hwnd.ToInt64():X}, class={GetClassName(hwnd)}, root=0x{root.ToInt64():X}, rootClass={GetClassName(root)}, pid={processId}";
    }

    private static string GetClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "(空)";
        }

        var builder = new StringBuilder(256);
        var length = NativeMethods.GetClassName(hwnd, builder, builder.Capacity);
        return length <= 0 ? "(未知)" : builder.ToString();
    }

    private static string FormatLastError(int lastError)
    {
        return lastError == int.MinValue ? "读取失败" : lastError.ToString();
    }

    private sealed record BindAttempt(string Name, int Result, int LastError);
}
