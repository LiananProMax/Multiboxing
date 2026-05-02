using System.Diagnostics;
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
                    window.CurrentMode = "Windows 消息同步兜底";
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
                    window.CurrentMode = "Windows 消息同步兜底";
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
                window.CurrentMode = attempts.Last().ModeDescription;
            }

            mainWindow!.BindState = "输入源";
            mainWindow.SyncState = "主操作窗口";
            mainWindow.CurrentMode = "输入源";

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

    public bool TestBindModes(
        WindowInfo window,
        AppConfig currentConfig,
        string dmDllPath,
        out BindModeTestReport? report,
        out string error)
    {
        report = null;
        error = string.Empty;

        if (IsRunning)
        {
            error = "请先关闭同步，再测试模式。";
            return false;
        }

        if (window.Handle == IntPtr.Zero || !NativeMethods.IsWindow(window.Handle))
        {
            error = "目标窗口句柄无效。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(dmDllPath) || !File.Exists(dmDllPath))
        {
            error = "请先设置dm路径";
            return false;
        }

        var results = new List<BindModeTestResult>();
        var initInfo = string.Empty;
        DmPlugin? plugin = null;
        try
        {
            if (!DmPlugin.TryCreate(dmDllPath, out plugin, out error) || plugin == null)
            {
                return false;
            }

            initInfo = plugin.Initialize(Path.GetDirectoryName(dmDllPath) ?? AppContext.BaseDirectory);
            ActivateWindow(window.Handle);
            try
            {
                plugin.SetWindowState(window.Handle, 1);
            }
            catch
            {
                // Match the formal sync path: activation is helpful, but this dm call is optional.
            }

            foreach (var testCase in BuildBindModeTestCases(currentConfig))
            {
                results.Add(RunBindModeTestCase(plugin, window.Handle, testCase));
            }

            report = new BindModeTestReport(
                window.Handle,
                window.Title,
                plugin.CreationMode,
                initInfo,
                results);
            return true;
        }
        finally
        {
            try
            {
                plugin?.ForceUnBindWindow(window.Handle);
            }
            catch
            {
                // Test cleanup is best-effort; the next formal bind also clears stale bindings.
            }

            plugin?.Dispose();
        }
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

    private static string NormalizeInput(string input)
    {
        return string.IsNullOrWhiteSpace(input) || input == "0" ? "windows" : input.Trim();
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
            attempts.Add(TryBind(
                "BindWindow",
                DescribeMode("BindWindow", display, mouse, keypad, string.Empty, mode),
                plugin,
                () => plugin.BindWindow(window.Handle, display, mouse, keypad, mode)));
            if (attempts[^1].Result == 1)
            {
                return attempts;
            }

            if (string.Equals(mouse, "windows", StringComparison.OrdinalIgnoreCase))
            {
                attempts.Add(TryBind(
                    "BindWindow(windows3)",
                    DescribeMode("BindWindow", display, "windows3", keypad, string.Empty, mode),
                    plugin,
                    () => plugin.BindWindow(window.Handle, display, "windows3", keypad, mode)));
                if (attempts[^1].Result == 1)
                {
                    return attempts;
                }
            }

            attempts.Add(TryBind(
                "BindWindowEx(empty-public)",
                DescribeMode("BindWindowEx", display, mouse, keypad, string.Empty, mode),
                plugin,
                () => plugin.BindWindowEx(window.Handle, display, mouse, keypad, string.Empty, mode)));
            if (attempts[^1].Result == 1)
            {
                return attempts;
            }

            if (string.Equals(mouse, "windows", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(keypad, "windows", StringComparison.OrdinalIgnoreCase))
            {
                attempts.Add(TryBind(
                    "BindWindow(dx2/dx)",
                    DescribeMode("BindWindow", display, "dx2", "dx", string.Empty, mode),
                    plugin,
                    () => plugin.BindWindow(window.Handle, display, "dx2", "dx", mode)));
                if (attempts[^1].Result == 1)
                {
                    return attempts;
                }

                attempts.Add(TryBind(
                    "BindWindow(dx/dx)",
                    DescribeMode("BindWindow", display, "dx", "dx", string.Empty, mode),
                    plugin,
                    () => plugin.BindWindow(window.Handle, display, "dx", "dx", mode)));
                if (attempts[^1].Result == 1)
                {
                    return attempts;
                }

                var activePublic = "dx.public.active.api|dx.public.active.message";
                attempts.Add(TryBind(
                    "BindWindowEx(dx2/dx active)",
                    DescribeMode("BindWindowEx", display, "dx2", "dx", activePublic, mode),
                    plugin,
                    () => plugin.BindWindowEx(
                        window.Handle,
                        display,
                        "dx2",
                        "dx",
                        activePublic,
                        mode)));
            }
            return attempts;
        }

        attempts.Add(TryBind(
            "BindWindowEx",
            DescribeMode("BindWindowEx", display, mouse, keypad, publicMode, mode),
            plugin,
            () => plugin.BindWindowEx(window.Handle, display, mouse, keypad, publicMode, mode)));
        if (attempts[^1].Result == 1)
        {
            return attempts;
        }

        attempts.Add(TryBind(
            "BindWindow",
            DescribeMode("BindWindow", display, mouse, keypad, string.Empty, mode),
            plugin,
            () => plugin.BindWindow(window.Handle, display, mouse, keypad, mode)));
        return attempts;
    }

    private static BindAttempt TryBind(string name, string modeDescription, DmPlugin plugin, Func<int> bind)
    {
        var result = bind();
        return new BindAttempt(name, result, plugin.LastError(), modeDescription);
    }

    private static string DescribeMode(string apiName, string display, string mouse, string keypad, string publicMode, int mode)
    {
        var publicText = string.IsNullOrWhiteSpace(publicMode) ? "(空)" : publicMode;
        return $"dm {apiName}: display={display}, mouse={mouse}, keypad={keypad}, public={publicText}, mode={mode}";
    }

    private static IReadOnlyList<BindModeTestCase> BuildBindModeTestCases(AppConfig currentConfig)
    {
        var cases = new List<BindModeTestCase>();
        var seen = new HashSet<BindModeTestCase>();
        var display = NormalizeDisplay(currentConfig.Display);
        var currentMouse = NormalizeInput(currentConfig.Mouse);
        var currentKeypad = NormalizeInput(currentConfig.Keypad);
        var currentPublic = currentConfig.Public.Trim();
        var currentMode = int.TryParse(currentConfig.Mode, out var parsedMode) ? parsedMode : 0;

        AddCase(new BindModeTestCase(
            display,
            currentMouse,
            currentKeypad,
            currentPublic,
            currentMode,
            UseBindWindowEx: !string.IsNullOrWhiteSpace(currentPublic)));
        if (string.IsNullOrWhiteSpace(currentPublic))
        {
            AddCase(new BindModeTestCase(display, currentMouse, currentKeypad, string.Empty, currentMode, UseBindWindowEx: true));
        }

        var inputPairs = new (string Mouse, string Keypad)[]
        {
            ("windows", "windows"),
            ("windows3", "windows"),
            ("dx2", "dx"),
            ("dx", "dx")
        };
        var publicModes = new[]
        {
            string.Empty,
            "dx.public.active.api|dx.public.active.message",
            "dx.public.anti.api",
            "dx.public.km.protect"
        };
        var modes = new[] { 0, 1, 2, 3, 101, 103 };

        foreach (var mode in modes)
        {
            foreach (var inputPair in inputPairs)
            {
                foreach (var publicMode in publicModes)
                {
                    AddCase(new BindModeTestCase(
                        display,
                        inputPair.Mouse,
                        inputPair.Keypad,
                        publicMode,
                        mode,
                        UseBindWindowEx: !string.IsNullOrWhiteSpace(publicMode)));
                    if (string.IsNullOrWhiteSpace(publicMode))
                    {
                        AddCase(new BindModeTestCase(display, inputPair.Mouse, inputPair.Keypad, string.Empty, mode, UseBindWindowEx: true));
                    }
                }
            }
        }

        return cases;

        void AddCase(BindModeTestCase testCase)
        {
            if (seen.Add(testCase))
            {
                cases.Add(testCase);
            }
        }
    }

    private static BindModeTestResult RunBindModeTestCase(DmPlugin plugin, IntPtr hwnd, BindModeTestCase testCase)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = 0;
        try
        {
            try
            {
                plugin.ForceUnBindWindow(hwnd);
            }
            catch
            {
                // A failed pre-cleanup should not prevent trying the next combination.
            }

            result = testCase.UseBindWindowEx
                ? plugin.BindWindowEx(hwnd, testCase.Display, testCase.Mouse, testCase.Keypad, testCase.Public, testCase.Mode)
                : plugin.BindWindow(hwnd, testCase.Display, testCase.Mouse, testCase.Keypad, testCase.Mode);
            var lastError = plugin.LastError();
            return new BindModeTestResult(testCase, result, lastError, stopwatch.ElapsedMilliseconds, string.Empty);
        }
        catch (Exception ex)
        {
            return new BindModeTestResult(testCase, 0, int.MinValue, stopwatch.ElapsedMilliseconds, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            try
            {
                plugin.ForceUnBindWindow(hwnd);
                if (result == 1)
                {
                    plugin.UnBindWindow();
                }
            }
            catch
            {
                // Keep testing even if a particular mode leaves nothing to unbind.
            }
        }
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

    private sealed record BindAttempt(string Name, int Result, int LastError, string ModeDescription);
}

public sealed record BindModeTestCase(string Display, string Mouse, string Keypad, string Public, int Mode, bool UseBindWindowEx)
{
    public string ApiName => UseBindWindowEx ? "BindWindowEx" : "BindWindow";
}

public sealed record BindModeTestResult(
    BindModeTestCase TestCase,
    int Result,
    int LastError,
    long ElapsedMs,
    string Error)
{
    public bool Success => Result == 1;
}

public sealed record BindModeTestReport(
    IntPtr WindowHandle,
    string WindowTitle,
    string DmCreationMode,
    string InitInfo,
    IReadOnlyList<BindModeTestResult> Results);
