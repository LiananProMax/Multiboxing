namespace Multiboxing;

internal sealed class HookThread : IDisposable
{
    private const uint WmQuit = 0x0012;
    private const uint WmUser = 0x0400;
    private const uint PmNoremove = 0x0000;

    private readonly Thread _thread;
    private readonly Func<int> _install;
    private readonly Action _uninstall;
    private readonly ManualResetEventSlim _ready = new(false);
    private uint _threadId;
    private int _installResult = -1;
    private Exception? _installException;
    private int _stopRequested;

    public HookThread(string name, Func<int> install, Action uninstall)
    {
        _install = install;
        _uninstall = uninstall;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = name,
            Priority = ThreadPriority.Highest
        };
    }

    public bool IsAlive => _thread.IsAlive;

    public int Start()
    {
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("钩子线程启动超时。");
        }

        if (_installException != null)
        {
            throw new InvalidOperationException("安装钩子失败。", _installException);
        }

        return _installResult;
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
        {
            if (_thread.IsAlive)
            {
                _thread.Join(TimeSpan.FromSeconds(3));
            }

            return;
        }

        var threadId = Volatile.Read(ref _threadId);
        if (threadId != 0 && _thread.IsAlive)
        {
            NativeMethods.PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(3));
        }
    }

    public void Dispose()
    {
        Stop();
        if (!_thread.IsAlive)
        {
            _ready.Dispose();
        }
    }

    private void Run()
    {
        try
        {
            // PeekMessage creates this thread's message queue before any hook is installed.
            NativeMethods.PeekMessage(out _, IntPtr.Zero, WmUser, WmUser, PmNoremove);
            Volatile.Write(ref _threadId, NativeMethods.GetCurrentThreadId());

            try
            {
                _installResult = _install();
            }
            catch (Exception ex)
            {
                _installException = ex;
                _installResult = -1;
            }

            SignalReady();
            if (_installResult != 0 || Volatile.Read(ref _stopRequested) != 0)
            {
                SafeUninstall();
                return;
            }

            while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }

            SafeUninstall();
        }
        finally
        {
            SignalReady();
        }
    }

    private void SignalReady()
    {
        try
        {
            _ready.Set();
        }
        catch (ObjectDisposedException)
        {
            // Start already gave up and disposed the wait handle.
        }
    }

    private void SafeUninstall()
    {
        try
        {
            _uninstall();
        }
        catch
        {
            // Unhook failures must not leave the hook thread running.
        }
    }
}
