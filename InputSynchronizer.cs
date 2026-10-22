using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Channels;

namespace KeyMouseSyncReplica;

public sealed class InputSynchronizer : IDisposable
{
    private const int MoveIntervalMs = 12;
    private const int RectCacheMs = 150;

    private readonly NativeMethods.HookProc _keyboardProc;
    private readonly NativeMethods.HookProc _mouseProc;
    private readonly object _gate = new();

    private HookThread? _hookThread;
    private Thread? _worker;
    private ManualResetEventSlim? _signal;
    private Channel<InputEvent>? _events;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private IntPtr _mainWindow;
    private IntPtr[] _targets = Array.Empty<IntPtr>();
    private NativeMethods.RECT _mainRect;
    private NativeMethods.RECT[] _targetRects = Array.Empty<NativeMethods.RECT>();
    private bool[] _targetRectOk = Array.Empty<bool>();
    private bool _mainRectOk;
    private bool _rectsReady;
    private long _rectsAt;
    private bool _syncKeyboard;
    private bool _syncMouse;
    private int _state;
    private int _sequence;
    private int _moveVersion;
    private int _moveX;
    private int _moveY;
    private int _moveEventSeq;
    private int _publishedMoveVersion;
    private long _lastMoveSignalAt;
    private long _lastMovePostedAt;
    private int _lastPostedX = int.MinValue;
    private int _lastPostedY = int.MinValue;
    private SyncDiagnostics? _diagnostics;

    public SyncDiagnosticReport? LastDiagnosticReport { get; private set; }

    public InputSynchronizer()
    {
        _keyboardProc = KeyboardHookProc;
        _mouseProc = MouseHookProc;
    }

    public bool IsRunning => Volatile.Read(ref _state) == 1;

    public void Start(IntPtr mainWindow, IEnumerable<IntPtr> targetWindows, bool syncKeyboard, bool syncMouse, bool enableDiagnostics = false)
    {
        lock (_gate)
        {
            StopCore();
            if (_worker is { IsAlive: true })
            {
                throw new InvalidOperationException("上一次同步尚未完全停止。");
            }

            _mainWindow = mainWindow;
            _targets = targetWindows.Where(hwnd => hwnd != IntPtr.Zero && hwnd != mainWindow).Distinct().ToArray();
            _targetRects = new NativeMethods.RECT[_targets.Length];
            _targetRectOk = new bool[_targets.Length];
            _syncKeyboard = syncKeyboard;
            _syncMouse = syncMouse;
            _rectsReady = false;
            _rectsAt = 0;
            _sequence = 0;
            _moveVersion = 0;
            _moveX = 0;
            _moveY = 0;
            _moveEventSeq = 0;
            _publishedMoveVersion = 0;
            _lastMoveSignalAt = 0;
            _lastMovePostedAt = 0;
            _lastPostedX = int.MinValue;
            _lastPostedY = int.MinValue;
            LastDiagnosticReport = null;
            _diagnostics = enableDiagnostics ? new SyncDiagnostics() : null;
            if (_diagnostics != null)
            {
                _diagnostics.Log($"SESSION START syncKeyboard={syncKeyboard} syncMouse={syncMouse}");
                _diagnostics.Log(
                    $"process: pid={Environment.ProcessId} elevated={IsElevated()} " +
                    $"dpiAwareness={SyncDiagnostics.FormatAwareness(NativeMethods.GetThreadDpiAwarenessContext())}");
                _diagnostics.Log(SyncDiagnostics.DescribeWindow("main", _mainWindow));
                for (var i = 0; i < _targets.Length; i++)
                {
                    _diagnostics.Log(SyncDiagnostics.DescribeWindow($"target[{i}]", _targets[i]));
                }
            }

            _events = Channel.CreateUnbounded<InputEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
            _signal = new ManualResetEventSlim(false);
            Volatile.Write(ref _state, 1);

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "InputSyncWorker",
                Priority = ThreadPriority.AboveNormal
            };
            _worker.Start();

            HookThread? hookThread = null;
            try
            {
                hookThread = new HookThread("InputSyncHook", InstallHooks, UninstallHooks);
                var error = hookThread.Start();
                if (error != 0)
                {
                    throw new Win32Exception(error);
                }

                _hookThread = hookThread;
            }
            catch
            {
                hookThread?.Dispose();
                StopCore();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void StopCore()
    {
        Volatile.Write(ref _state, 0);
        _hookThread?.Stop();
        if (_hookThread is { IsAlive: true })
        {
            _signal?.Set();
            return;
        }

        _hookThread?.Dispose();
        _hookThread = null;

        _signal?.Set();
        var worker = _worker;
        if (worker != null && Thread.CurrentThread != worker && !worker.Join(TimeSpan.FromSeconds(3)))
        {
            return;
        }

        _worker = null;
        var signal = _signal;
        _signal = null;
        _events = null;
        signal?.Dispose();

        if (_diagnostics != null)
        {
            _diagnostics.Log("SESSION STOP");
            LastDiagnosticReport = _diagnostics.FinalizeReport();
            _diagnostics = null;
        }
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private int InstallHooks()
    {
        var module = NativeMethods.GetModuleHandle(null);
        if (_syncKeyboard)
        {
            _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
            if (_keyboardHook == IntPtr.Zero)
            {
                return Marshal.GetLastWin32Error();
            }
        }

        if (_syncMouse)
        {
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
            if (_mouseHook == IntPtr.Zero)
            {
                return Marshal.GetLastWin32Error();
            }
        }

        return 0;
    }

    private void UninstallHooks()
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

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Volatile.Read(ref _state) == 1 && _syncKeyboard)
        {
            var message = wParam.ToInt32();
            if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_KEYUP
                or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_SYSKEYUP)
            {
                var data = NativeMethods.ReadStruct<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                Enqueue(new InputEvent(
                    isKeyboard: true,
                    message: message,
                    sequence: NextSequence(),
                    vkCode: data.VkCode,
                    scanCode: data.ScanCode,
                    flags: data.Flags,
                    mouseData: 0,
                    x: 0,
                    y: 0));
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Volatile.Read(ref _state) == 1 && _syncMouse)
        {
            var message = wParam.ToInt32();
            if (IsTrackedMouseMessage(message))
            {
                var data = NativeMethods.ReadStruct<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (!IsShortcutSideButtonMessage(message, data.MouseData))
                {
                    if (message == NativeMethods.WM_MOUSEMOVE)
                    {
                        StorePendingMove(data.Pt.X, data.Pt.Y);
                    }
                    else
                    {
                        Enqueue(new InputEvent(
                            isKeyboard: false,
                            message: message,
                            sequence: NextSequence(),
                            vkCode: 0,
                            scanCode: 0,
                            flags: data.Flags,
                            mouseData: data.MouseData,
                            x: data.Pt.X,
                            y: data.Pt.Y));
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private int NextSequence()
    {
        return ++_sequence;
    }

    private void StorePendingMove(int x, int y)
    {
        var eventSeq = NextSequence();
        var version = Volatile.Read(ref _moveVersion) + 1;
        Volatile.Write(ref _moveVersion, version);
        Volatile.Write(ref _moveX, x);
        Volatile.Write(ref _moveY, y);
        Volatile.Write(ref _moveEventSeq, eventSeq);
        Volatile.Write(ref _moveVersion, version + 1);

        var signal = _signal;
        if (signal == null)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _lastMoveSignalAt < MoveIntervalMs)
        {
            return;
        }

        _lastMoveSignalAt = now;
        signal.Set();
    }

    private void Enqueue(InputEvent inputEvent)
    {
        var events = _events;
        var signal = _signal;
        if (events == null || signal == null || Volatile.Read(ref _state) != 1)
        {
            return;
        }

        if (events.Writer.TryWrite(inputEvent))
        {
            signal.Set();
        }
    }

    private void WorkerLoop()
    {
        var signal = _signal;
        if (signal == null)
        {
            return;
        }

        while (Volatile.Read(ref _state) == 1)
        {
            signal.Reset();
            RefreshRectsIfDue();
            DrainDiscrete();
            PublishPendingMove();

            if (Volatile.Read(ref _state) != 1)
            {
                break;
            }

            var waitMs = MoveIntervalMs;
            if (HasFreshMove())
            {
                var elapsed = Environment.TickCount64 - _lastMovePostedAt;
                waitMs = elapsed >= MoveIntervalMs ? 0 : (int)(MoveIntervalMs - elapsed);
            }

            if (waitMs > 0)
            {
                signal.Wait(waitMs);
            }
        }
    }

    private void DrainDiscrete()
    {
        var events = _events;
        if (events == null)
        {
            return;
        }

        while (events.Reader.TryRead(out var inputEvent))
        {
            if (inputEvent.IsKeyboard)
            {
                PublishKey(inputEvent);
                continue;
            }

            ConsumeMoveThrough(inputEvent.Sequence);

            var diag = _diagnostics;
            diag?.Bump(SyncDiagnostics.KeyDiscreteSeen);
            var hitResult = ClassifyPointOnMainWindow(inputEvent.X, inputEvent.Y, out var hit);
            if (hitResult != MainHitResult.OnMain)
            {
                if (diag != null)
                {
                    diag.Bump(hitResult == MainHitResult.OutsideRect
                        ? SyncDiagnostics.KeyDiscreteDropOutside
                        : SyncDiagnostics.KeyDiscreteDropHit);
                    diag.Log(
                        $"DROP {SyncDiagnostics.MessageName(inputEvent.Message)} seq={inputEvent.Sequence} " +
                        $"pt=({inputEvent.X},{inputEvent.Y}) reason={hitResult} " +
                        $"mainRect={(_mainRectOk ? SyncDiagnostics.FormatRect(_mainRect) : "(无)")} " +
                        $"{SyncDiagnostics.DescribeHitWindow(hit)}");
                }

                continue;
            }

            PostMouseToTargets(NativeMethods.WM_MOUSEMOVE, inputEvent.X, inputEvent.Y, 0);
            _lastPostedX = inputEvent.X;
            _lastPostedY = inputEvent.Y;
            _lastMovePostedAt = Environment.TickCount64;
            if (PostMouseToTargets(inputEvent.Message, inputEvent.X, inputEvent.Y, inputEvent.MouseData) > 0)
            {
                diag?.Bump(SyncDiagnostics.KeyDiscretePosted);
            }
        }
    }

    private bool HasFreshMove()
    {
        return TryReadMove(out _, out _, out var version, out _) && version != _publishedMoveVersion;
    }

    private void ConsumeMoveThrough(int eventSequence)
    {
        if (!TryReadMove(out _, out _, out var version, out var moveEventSeq))
        {
            return;
        }

        if (moveEventSeq <= eventSequence)
        {
            _publishedMoveVersion = version;
        }
    }

    private bool TryReadMove(out int x, out int y, out int version, out int eventSeq)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var version1 = Volatile.Read(ref _moveVersion);
            if (version1 == 0)
            {
                x = 0;
                y = 0;
                version = 0;
                eventSeq = 0;
                return false;
            }

            if ((version1 & 1) != 0)
            {
                continue;
            }

            x = Volatile.Read(ref _moveX);
            y = Volatile.Read(ref _moveY);
            eventSeq = Volatile.Read(ref _moveEventSeq);
            if (version1 == Volatile.Read(ref _moveVersion))
            {
                version = version1;
                return true;
            }
        }

        x = 0;
        y = 0;
        version = 0;
        eventSeq = 0;
        return false;
    }

    private void PublishPendingMove()
    {
        if (!TryReadMove(out var x, out var y, out var version, out _))
        {
            return;
        }

        if (version == _publishedMoveVersion)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _lastMovePostedAt < MoveIntervalMs)
        {
            return;
        }

        var diag = _diagnostics;
        diag?.Bump(SyncDiagnostics.KeyMoveSeen);
        var hitResult = ClassifyPointOnMainWindow(x, y, out var hit);
        if (hitResult != MainHitResult.OnMain)
        {
            if (diag != null)
            {
                diag.Bump(hitResult == MainHitResult.OutsideRect
                    ? SyncDiagnostics.KeyMoveDropOutside
                    : SyncDiagnostics.KeyMoveDropHit);
                if (diag.ShouldSampleMove())
                {
                    diag.Log(
                        $"DROP WM_MOUSEMOVE pt=({x},{y}) reason={hitResult} " +
                        $"mainRect={(_mainRectOk ? SyncDiagnostics.FormatRect(_mainRect) : "(无)")} " +
                        $"{SyncDiagnostics.DescribeHitWindow(hit)}");
                }
            }

            _publishedMoveVersion = version;
            _lastMovePostedAt = now;
            return;
        }

        if (x == _lastPostedX && y == _lastPostedY)
        {
            diag?.Bump(SyncDiagnostics.KeyMoveDropDuplicate);
            _publishedMoveVersion = version;
            _lastMovePostedAt = now;
            return;
        }

        if (PostMouseToTargets(NativeMethods.WM_MOUSEMOVE, x, y, 0) > 0)
        {
            diag?.Bump(SyncDiagnostics.KeyMovePosted);
        }
        _lastPostedX = x;
        _lastPostedY = y;
        _lastMovePostedAt = now;
        if (TryReadMove(out _, out _, out var latest, out _) && latest == version)
        {
            _publishedMoveVersion = version;
        }
    }

    private void PublishKey(InputEvent inputEvent)
    {
        var diag = _diagnostics;
        diag?.Bump(SyncDiagnostics.KeyKeySeen);
        if (!IsMainWindowActive())
        {
            if (diag != null)
            {
                diag.Bump(SyncDiagnostics.KeyKeyDropInactive);
                if (diag.ShouldSampleMove())
                {
                    diag.Log($"DROP KEY {SyncDiagnostics.MessageName(inputEvent.Message)} vk=0x{inputEvent.VkCode:X} reason=mainNotActive");
                }
            }

            return;
        }

        var key = new IntPtr(inputEvent.VkCode);
        var packed = BuildKeyboardLParam(inputEvent);
        var postedAny = false;
        for (var i = 0; i < _targets.Length; i++)
        {
            if (!_targetRectOk[i])
            {
                continue;
            }

            var ok = NativeMethods.PostMessage(_targets[i], inputEvent.Message, key, packed);
            if (ok)
            {
                postedAny = true;
            }
            else
            {
                diag?.Bump(SyncDiagnostics.KeyPostFailed);
                diag?.Log($"POST FAIL KEY {SyncDiagnostics.MessageName(inputEvent.Message)} -> target=0x{_targets[i].ToInt64():X} err={Marshal.GetLastWin32Error()}");
            }
        }

        if (postedAny)
        {
            diag?.Bump(SyncDiagnostics.KeyKeyPosted);
        }
    }

    private void RefreshRectsIfDue()
    {
        var now = Environment.TickCount64;
        if (_rectsReady && now - _rectsAt < RectCacheMs)
        {
            return;
        }

        _mainRectOk = NativeMethods.IsWindow(_mainWindow) && NativeMethods.GetWindowRect(_mainWindow, out _mainRect);
        for (var i = 0; i < _targets.Length; i++)
        {
            _targetRectOk[i] = NativeMethods.IsWindow(_targets[i]) && NativeMethods.GetWindowRect(_targets[i], out _targetRects[i]);
        }

        _rectsAt = now;
        _rectsReady = true;
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

    private enum MainHitResult
    {
        OnMain,
        OutsideRect,
        HitNotMain
    }

    private MainHitResult ClassifyPointOnMainWindow(int x, int y, out IntPtr hit)
    {
        hit = IntPtr.Zero;
        if (!_mainRectOk || x < _mainRect.Left || x > _mainRect.Right || y < _mainRect.Top || y > _mainRect.Bottom)
        {
            return MainHitResult.OutsideRect;
        }

        hit = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = x, Y = y });
        if (hit == IntPtr.Zero)
        {
            return MainHitResult.HitNotMain;
        }

        return hit == _mainWindow || NativeMethods.IsChild(_mainWindow, hit)
            ? MainHitResult.OnMain
            : MainHitResult.HitNotMain;
    }

    private static bool IsTrackedMouseMessage(int message)
    {
        return message is NativeMethods.WM_MOUSEMOVE or NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP
            or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP or NativeMethods.WM_MBUTTONDOWN
            or NativeMethods.WM_MBUTTONUP or NativeMethods.WM_MOUSEWHEEL or NativeMethods.WM_XBUTTONDOWN
            or NativeMethods.WM_XBUTTONUP;
    }

    private static bool IsShortcutSideButtonMessage(int message, uint mouseData)
    {
        return message is NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP
            && NativeMethods.GetXButton(mouseData) is NativeMethods.XBUTTON1 or NativeMethods.XBUTTON2;
    }

    private int PostMouseToTargets(int message, int x, int y, uint mouseData)
    {
        if (!_mainRectOk)
        {
            return 0;
        }

        var source = new NativeMethods.POINT { X = x, Y = y };
        var wParam = message == NativeMethods.WM_MOUSEWHEEL
            ? new IntPtr(unchecked((int)(mouseData & 0xffff0000)))
            : IntPtr.Zero;

        var diag = _diagnostics;
        var isMove = message == NativeMethods.WM_MOUSEMOVE;
        var posted = 0;
        for (var i = 0; i < _targets.Length; i++)
        {
            if (!_targetRectOk[i] || !TryMapToClient(_targets[i], source, _mainRect, _targetRects[i], out var client, out var mappedScreen))
            {
                continue;
            }

            var ok = NativeMethods.PostMessage(_targets[i], message, wParam, MakeLParam(client.X, client.Y));
            var error = ok ? 0 : Marshal.GetLastWin32Error();
            if (ok)
            {
                posted++;
            }

            if (diag != null)
            {
                if (!ok)
                {
                    diag.Bump(SyncDiagnostics.KeyPostFailed);
                }

                var cursorInTargetRect = SyncDiagnostics.Contains(_targetRects[i], x, y);
                var hasClientScreen = SyncDiagnostics.TryGetClientRectScreen(_targets[i], out var clientScreen);
                var cursorInClient = hasClientScreen && SyncDiagnostics.Contains(clientScreen, x, y);
                var hasClientLocal = NativeMethods.GetClientRect(_targets[i], out var clientLocal);
                var mappedInClient = hasClientLocal && SyncDiagnostics.Contains(clientLocal, client.X, client.Y);

                if (!cursorInTargetRect)
                {
                    diag.Bump(SyncDiagnostics.KeyPostCursorOutsideTarget);
                }

                if (!cursorInClient)
                {
                    diag.Bump(SyncDiagnostics.KeyPostCursorOutsideClient);
                }

                if (!mappedInClient)
                {
                    diag.Bump(SyncDiagnostics.KeyPostMappedOutsideClient);
                }

                if (!isMove || diag.ShouldSampleMove())
                {
                    diag.Log(
                        $"POST {SyncDiagnostics.MessageName(message)} -> target[{i}]=0x{_targets[i].ToInt64():X} " +
                        $"pt=({x},{y}) mainRect={SyncDiagnostics.FormatRect(_mainRect)} " +
                        $"targetRect={SyncDiagnostics.FormatRect(_targetRects[i])} " +
                        $"clientRectScreen={(hasClientScreen ? SyncDiagnostics.FormatRect(clientScreen) : "(无)")} " +
                        $"mappedScreen=({mappedScreen.X},{mappedScreen.Y}) mappedClient=({client.X},{client.Y}) " +
                        $"cursorInTargetRect={cursorInTargetRect} cursorInClient={cursorInClient} mappedInClient={mappedInClient} " +
                        $"post={(ok ? "ok" : $"fail err={error}")}");
                }
            }
        }

        return posted;
    }

    private static bool TryMapToClient(
        IntPtr target,
        NativeMethods.POINT source,
        NativeMethods.RECT mainRect,
        NativeMethods.RECT targetRect,
        out NativeMethods.POINT client,
        out NativeMethods.POINT mappedScreen)
    {
        var mainWidth = Math.Max(1, mainRect.Right - mainRect.Left);
        var mainHeight = Math.Max(1, mainRect.Bottom - mainRect.Top);
        var targetWidth = Math.Max(1, targetRect.Right - targetRect.Left);
        var targetHeight = Math.Max(1, targetRect.Bottom - targetRect.Top);
        var ratioX = (source.X - mainRect.Left) / (double)mainWidth;
        var ratioY = (source.Y - mainRect.Top) / (double)mainHeight;
        mappedScreen = new NativeMethods.POINT
        {
            X = targetRect.Left + (int)Math.Round(targetWidth * ratioX),
            Y = targetRect.Top + (int)Math.Round(targetHeight * ratioY)
        };
        client = mappedScreen;
        return NativeMethods.ScreenToClient(target, ref client);
    }

    private static IntPtr BuildKeyboardLParam(InputEvent inputEvent)
    {
        var repeatCount = 1;
        var scanCode = ((int)inputEvent.ScanCode & 0xff) << 16;
        var extended = (inputEvent.Flags & 0x01) != 0 ? 1 << 24 : 0;
        var transition = inputEvent.Message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP
            ? unchecked((int)0xC0000000)
            : 0;
        return new IntPtr(repeatCount | scanCode | extended | transition);
    }

    private static IntPtr MakeLParam(int low, int high)
    {
        return new IntPtr((high << 16) | (low & 0xffff));
    }

    private readonly struct InputEvent
    {
        public InputEvent(
            bool isKeyboard,
            int message,
            int sequence,
            uint vkCode,
            uint scanCode,
            uint flags,
            uint mouseData,
            int x,
            int y)
        {
            IsKeyboard = isKeyboard;
            Message = message;
            Sequence = sequence;
            VkCode = vkCode;
            ScanCode = scanCode;
            Flags = flags;
            MouseData = mouseData;
            X = x;
            Y = y;
        }

        public bool IsKeyboard { get; }
        public int Message { get; }
        public int Sequence { get; }
        public uint VkCode { get; }
        public uint ScanCode { get; }
        public uint Flags { get; }
        public uint MouseData { get; }
        public int X { get; }
        public int Y { get; }
    }
}
