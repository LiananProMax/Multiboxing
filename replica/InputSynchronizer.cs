using System.Runtime.InteropServices;

namespace KeyMouseSyncReplica;

public sealed class InputSynchronizer : IDisposable
{
    private readonly NativeMethods.HookProc _keyboardProc;
    private readonly NativeMethods.HookProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private IntPtr _mainWindow;
    private IReadOnlyList<IntPtr> _targets = Array.Empty<IntPtr>();
    private bool _syncKeyboard;
    private bool _syncMouse;

    public InputSynchronizer()
    {
        _keyboardProc = KeyboardHookProc;
        _mouseProc = MouseHookProc;
    }

    public bool IsRunning => _keyboardHook != IntPtr.Zero || _mouseHook != IntPtr.Zero;

    public void Start(IntPtr mainWindow, IEnumerable<IntPtr> targetWindows, bool syncKeyboard, bool syncMouse)
    {
        Stop();

        _mainWindow = mainWindow;
        _targets = targetWindows.Where(hwnd => hwnd != IntPtr.Zero && hwnd != mainWindow).Distinct().ToArray();
        _syncKeyboard = syncKeyboard;
        _syncMouse = syncMouse;

        var module = NativeMethods.GetModuleHandle(null);
        if (_syncKeyboard)
        {
            _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        }

        if (_syncMouse)
        {
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        }
    }

    public void Stop()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _syncKeyboard && IsMainWindowActive())
        {
            var message = wParam.ToInt32();
            if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_KEYUP
                or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_SYSKEYUP)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var key = new IntPtr(data.VkCode);
                var packed = BuildKeyboardLParam(data, message);
                foreach (var target in _targets)
                {
                    NativeMethods.PostMessage(target, message, key, packed);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _syncMouse)
        {
            var message = wParam.ToInt32();
            if (message is NativeMethods.WM_MOUSEMOVE or NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP
                or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP or NativeMethods.WM_MBUTTONDOWN
                or NativeMethods.WM_MBUTTONUP or NativeMethods.WM_MOUSEWHEEL or NativeMethods.WM_XBUTTONDOWN
                or NativeMethods.WM_XBUTTONUP)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (IsShortcutSideButtonMessage(message, data.MouseData))
                {
                    return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                if (PointInsideWindow(_mainWindow, data.Pt))
                {
                    foreach (var target in _targets)
                    {
                        PostMouseMessage(target, message, data);
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsMainWindowActive()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var root = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        return root == _mainWindow || NativeMethods.IsChild(_mainWindow, foreground);
    }

    private static bool IsShortcutSideButtonMessage(int message, uint mouseData)
    {
        return message is NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP
            && NativeMethods.GetXButton(mouseData) is NativeMethods.XBUTTON1 or NativeMethods.XBUTTON2;
    }

    private static bool PointInsideWindow(IntPtr hwnd, NativeMethods.POINT point)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;
    }

    private void PostMouseMessage(IntPtr target, int message, NativeMethods.MSLLHOOKSTRUCT data)
    {
        var targetPoint = MapPointToTarget(target, data.Pt);
        var lParam = MakeLParam(targetPoint.X, targetPoint.Y);
        var wParam = message == NativeMethods.WM_MOUSEWHEEL
            ? new IntPtr(unchecked((int)(data.MouseData & 0xffff0000)))
            : IntPtr.Zero;

        NativeMethods.PostMessage(target, message, wParam, lParam);
    }

    private NativeMethods.POINT MapPointToTarget(IntPtr target, NativeMethods.POINT sourcePoint)
    {
        if (!NativeMethods.GetWindowRect(_mainWindow, out var mainRect)
            || !NativeMethods.GetWindowRect(target, out var targetRect))
        {
            return sourcePoint;
        }

        var mainWidth = Math.Max(1, mainRect.Right - mainRect.Left);
        var mainHeight = Math.Max(1, mainRect.Bottom - mainRect.Top);
        var targetWidth = Math.Max(1, targetRect.Right - targetRect.Left);
        var targetHeight = Math.Max(1, targetRect.Bottom - targetRect.Top);

        var ratioX = (sourcePoint.X - mainRect.Left) / (double)mainWidth;
        var ratioY = (sourcePoint.Y - mainRect.Top) / (double)mainHeight;
        var screenPoint = new NativeMethods.POINT
        {
            X = targetRect.Left + (int)Math.Round(targetWidth * ratioX),
            Y = targetRect.Top + (int)Math.Round(targetHeight * ratioY)
        };

        NativeMethods.ScreenToClient(target, ref screenPoint);
        return screenPoint;
    }

    private static IntPtr BuildKeyboardLParam(NativeMethods.KBDLLHOOKSTRUCT data, int message)
    {
        var repeatCount = 1;
        var scanCode = ((int)data.ScanCode & 0xff) << 16;
        var extended = (data.Flags & 0x01) != 0 ? 1 << 24 : 0;
        var transition = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP ? unchecked((int)0xC0000000) : 0;
        return new IntPtr(repeatCount | scanCode | extended | transition);
    }

    private static IntPtr MakeLParam(int low, int high)
    {
        return new IntPtr((high << 16) | (low & 0xffff));
    }
}
