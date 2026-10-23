namespace Multiboxing;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 单实例：第二个进程只唤醒已运行的实例后退出，
        // 否则新旧两个进程会各画一套缩略图（且各持有一套全局钩子/热键）。
        if (!SingleInstance.TryAcquire())
        {
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var app = new App();
        app.InitializeComponent();

        app.Run();
    }
}

/// <summary>
/// 基于命名 Mutex + EventWaitHandle 的单实例保护。
/// 名称与 exe 路径无关，因此 Debug/Release/publish 不同目录的构建也能互斥。
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\Multiboxing.SingleInstance";
    private const string ShowEventName = @"Local\Multiboxing.ShowWindow";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private static RegisteredWaitHandle? _showWait;

    /// <summary>已有实例收到“再次启动”信号时触发（在 ThreadPool 线程上，订阅者需自行切回 UI 线程）。</summary>
    public static event Action? ShowRequested;

    /// <returns>true 表示当前是第一个实例，可以继续启动；false 表示已有实例运行。</returns>
    public static bool TryAcquire()
    {
        try
        {
            if (Mutex.TryOpenExisting(MutexName, out var existing))
            {
                existing.Dispose();
                SignalShow();
                return false;
            }
        }
        catch
        {
            // 探测失败时继续走创建路径，由 WaitOne 的结果决定
        }

        var mutex = new Mutex(false, MutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // 上一个实例异常退出留下了废弃的 Mutex，此时所有权已转移给当前实例
            acquired = true;
        }
        catch
        {
            mutex.Dispose();
            SignalShow();
            return false;
        }

        if (!acquired)
        {
            mutex.Dispose();
            SignalShow();
            return false;
        }

        _mutex = mutex;
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                static (_, _) =>
                {
                    try { ShowRequested?.Invoke(); } catch { }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch
        {
            // 唤醒信号注册失败不影响单实例本身，忽略
        }

        return true;
    }

    public static void Release()
    {
        try { _showWait?.Unregister(null); } catch { }
        _showWait = null;
        try { _showEvent?.Dispose(); } catch { }
        _showEvent = null;

        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
    }

    private static void SignalShow()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch
        {
            // 已有实例可能正在退出，信号发不出去就算了
        }
    }
}
