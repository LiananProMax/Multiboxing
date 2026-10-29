using System.Runtime.InteropServices;

namespace Multiboxing;

public sealed class MouseSideButtonToggleHook : IDisposable
{
    private static readonly IntPtr ConsumeMessage = new(1);

    private readonly NativeMethods.HookProc _mouseProc;
    private HookThread? _hookThread;
    private IntPtr _mouseHook;
    private bool _disposed;

    public MouseSideButtonToggleHook()
    {
        _mouseProc = MouseHookProc;
    }

    public event EventHandler? ToggleRequested;

    public bool IsRunning => _hookThread != null;

    public bool Start(out string error)
    {
        error = string.Empty;
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning)
        {
            return true;
        }

        var thread = new HookThread("SideButtonHook", InstallHook, UninstallHook);
        try
        {
            var result = thread.Start();
            if (result != 0)
            {
                thread.Dispose();
                error = $"注册鼠标侧键快捷开关失败：Win32错误 {result}";
                return false;
            }
        }
        catch (Exception ex)
        {
            thread.Dispose();
            error = $"注册鼠标侧键快捷开关失败：{ex.Message}";
            return false;
        }

        _hookThread = thread;
        return true;
    }

    public void Stop()
    {
        var thread = _hookThread;
        if (thread == null)
        {
            return;
        }

        thread.Dispose();
        _hookThread = null;
    }

    public void Dispose()
    {
        Stop();
        _disposed = true;
    }

    private int InstallHook()
    {
        var module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_mouseHook != IntPtr.Zero)
        {
            return 0;
        }

        return Marshal.GetLastWin32Error();
    }

    private void UninstallHook()
    {
        if (_mouseHook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            if (message is NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP)
            {
                var data = NativeMethods.ReadStruct<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var xButton = NativeMethods.GetXButton(data.MouseData);
                if (xButton == NativeMethods.XBUTTON1)
                {
                    if (message == NativeMethods.WM_XBUTTONDOWN)
                    {
                        try
                        {
                            ToggleRequested?.Invoke(this, EventArgs.Empty);
                        }
                        catch
                        {
                            // A handler fault must not break the low-level hook chain.
                        }
                    }

                    return ConsumeMessage;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }
}
