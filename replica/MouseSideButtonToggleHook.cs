using System.Runtime.InteropServices;

namespace KeyMouseSyncReplica;

public sealed class MouseSideButtonToggleHook : IDisposable
{
    private static readonly IntPtr ConsumeMessage = new(1);

    private readonly NativeMethods.HookProc _mouseProc;
    private IntPtr _mouseHook;
    private bool _disposed;

    public MouseSideButtonToggleHook()
    {
        _mouseProc = MouseHookProc;
    }

    public event EventHandler? ToggleRequested;
    public event EventHandler? AddWindowRequested;

    public bool IsRunning => _mouseHook != IntPtr.Zero;

    public bool Start(out string error)
    {
        error = string.Empty;
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning)
        {
            return true;
        }

        var module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_mouseHook != IntPtr.Zero)
        {
            return true;
        }

        error = $"注册鼠标侧键快捷开关失败：Win32错误 {Marshal.GetLastWin32Error()}";
        return false;
    }

    public void Stop()
    {
        if (_mouseHook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
        _disposed = true;
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            if (message is NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var xButton = NativeMethods.GetXButton(data.MouseData);
                if (xButton is NativeMethods.XBUTTON1 or NativeMethods.XBUTTON2)
                {
                    if (message == NativeMethods.WM_XBUTTONDOWN)
                    {
                        if (xButton == NativeMethods.XBUTTON1)
                        {
                            ToggleRequested?.Invoke(this, EventArgs.Empty);
                        }
                        else
                        {
                            AddWindowRequested?.Invoke(this, EventArgs.Empty);
                        }
                    }

                    return ConsumeMessage;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }
}
