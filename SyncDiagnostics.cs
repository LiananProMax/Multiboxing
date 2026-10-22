using System.IO;
using System.Text;

namespace KeyMouseSyncReplica;

/// <summary>
/// 同步诊断记录:只在工作线程写入,停止同步时落盘。
/// 用于区分"被我们自己的过滤丢弃 / 已投递但目标忽略 / 坐标或 DPI 问题"。
/// </summary>
internal sealed class SyncDiagnostics
{
    private const int MaxLines = 4000;
    private const long MoveSampleIntervalMs = 200;

    // 计数器键(摘要按此顺序输出)
    public const string KeyMoveSeen = "move.seen";
    public const string KeyMovePosted = "move.posted";
    public const string KeyMoveDropOutside = "move.drop.outsideMainRect";
    public const string KeyMoveDropHit = "move.drop.hitNotMain";
    public const string KeyMoveDropDuplicate = "move.drop.duplicate";
    public const string KeyDiscreteSeen = "discrete.seen";
    public const string KeyDiscretePosted = "discrete.posted";
    public const string KeyDiscreteDropOutside = "discrete.drop.outsideMainRect";
    public const string KeyDiscreteDropHit = "discrete.drop.hitNotMain";
    public const string KeyPostFailed = "post.failed";
    public const string KeyPostCursorOutsideTarget = "post.cursorOutsideTargetRect";
    public const string KeyPostCursorOutsideClient = "post.cursorOutsideTargetClient";
    public const string KeyPostMappedOutsideClient = "post.mappedOutsideTargetClient";
    public const string KeyKeySeen = "key.seen";
    public const string KeyKeyPosted = "key.posted";
    public const string KeyKeyDropInactive = "key.drop.mainNotActive";

    private static readonly (string Key, string Label)[] CounterLabels =
    {
        (KeyMoveSeen, "鼠标移动-收到"),
        (KeyMovePosted, "鼠标移动-已投递"),
        (KeyMoveDropOutside, "鼠标移动-丢弃:光标在主窗口矩形外"),
        (KeyMoveDropHit, "鼠标移动-丢弃:命中窗口不是主窗口"),
        (KeyMoveDropDuplicate, "鼠标移动-丢弃:与上次投递位置相同"),
        (KeyDiscreteSeen, "点击/滚轮-收到"),
        (KeyDiscretePosted, "点击/滚轮-已投递(至少一个目标)"),
        (KeyDiscreteDropOutside, "点击/滚轮-丢弃:光标在主窗口矩形外"),
        (KeyDiscreteDropHit, "点击/滚轮-丢弃:命中窗口不是主窗口"),
        (KeyPostFailed, "PostMessage 失败次数"),
        (KeyPostCursorOutsideTarget, "投递时真实光标不在目标窗口矩形内"),
        (KeyPostCursorOutsideClient, "投递时真实光标不在目标客户区内"),
        (KeyPostMappedOutsideClient, "投递时映射点不在目标客户区内"),
        (KeyKeySeen, "键盘-收到"),
        (KeyKeyPosted, "键盘-已投递"),
        (KeyKeyDropInactive, "键盘-丢弃:主窗口未激活"),
    };

    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private readonly Dictionary<string, long> _counters = new();
    private readonly DateTime _startedAt = DateTime.Now;
    private long _lastMoveSampleAt;
    private bool _finalized;
    private string? _logPath;

    public void Log(string line)
    {
        lock (_gate)
        {
            if (_finalized)
            {
                return;
            }

            if (_lines.Count >= MaxLines)
            {
                _lines.Dequeue();
            }

            _lines.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {line}");
        }
    }

    public void Bump(string key, long delta = 1)
    {
        lock (_gate)
        {
            _counters.TryGetValue(key, out var current);
            _counters[key] = current + delta;
        }
    }

    public long Get(string key)
    {
        lock (_gate)
        {
            return _counters.TryGetValue(key, out var value) ? value : 0;
        }
    }

    /// <summary>鼠标移动抽样节流:约 200ms 允许记录一行明细。</summary>
    public bool ShouldSampleMove()
    {
        var now = Environment.TickCount64;
        lock (_gate)
        {
            if (now - _lastMoveSampleAt < MoveSampleIntervalMs)
            {
                return false;
            }

            _lastMoveSampleAt = now;
            return true;
        }
    }

    public string BuildSummary()
    {
        lock (_gate)
        {
            return BuildSummaryNoLockWithPath();
        }
    }

    /// <summary>落盘并返回报告;重复调用返回同一份报告。</summary>
    public SyncDiagnosticReport FinalizeReport()
    {
        lock (_gate)
        {
            if (!_finalized)
            {
                _finalized = true;
                try
                {
                    var directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "KeyMouseSyncReplica");
                    Directory.CreateDirectory(directory);
                    _logPath = Path.Combine(directory, "sync-diagnostics.log");

                    var builder = new StringBuilder();
                    foreach (var line in _lines)
                    {
                        builder.AppendLine(line);
                    }

                    builder.AppendLine("===== 摘要 =====");
                    builder.Append(BuildSummaryNoLockWithPath());

                    File.WriteAllText(_logPath, builder.ToString(), Encoding.UTF8);
                }
                catch
                {
                    _logPath = null;
                }
            }

            return new SyncDiagnosticReport(BuildSummaryNoLockWithPath(), _logPath);
        }
    }

    private long GetNoLock(string key)
    {
        return _counters.TryGetValue(key, out var value) ? value : 0;
    }

    private string BuildSummaryNoLockWithPath()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"诊断开始:{_startedAt:yyyy-MM-dd HH:mm:ss}  结束:{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine("计数:");
        foreach (var (key, label) in CounterLabels)
        {
            builder.AppendLine($"- {label}:{GetNoLock(key)}");
        }

        foreach (var pair in _counters)
        {
            if (CounterLabels.All(item => item.Key != pair.Key))
            {
                builder.AppendLine($"- {pair.Key}:{pair.Value}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("判读提示:");
        builder.AppendLine("- 点击大量计入\"丢弃\" => 问题在我们自己的过滤逻辑。");
        builder.AppendLine("- 点击已投递且 PostMessage 成功,但\"真实光标不在目标窗口/客户区内\"很多且游戏无反应 => 目标程序很可能校验真实光标位置。");
        builder.AppendLine("- \"映射点不在目标客户区内\"很多,或主/目标 DPI 不一致 => 坐标换算或 DPI 问题。");
        if (!string.IsNullOrEmpty(_logPath))
        {
            builder.AppendLine();
            builder.AppendLine($"完整日志:{_logPath}");
        }

        return builder.ToString();
    }

    // ---- 窗口描述工具(供头部信息与明细行复用) ----

    public static string DescribeWindow(string role, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return $"{role}: hwnd=0x{hwnd.ToInt64():X} (无效)";
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var className = GetClassNameText(hwnd);
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        var rectText = NativeMethods.GetWindowRect(hwnd, out var rect)
            ? FormatRect(rect)
            : "(GetWindowRect 失败)";
        var clientText = TryGetClientRectScreen(hwnd, out var clientRect)
            ? FormatRect(clientRect)
            : "(GetClientRect 失败)";

        return $"{role}: hwnd=0x{hwnd.ToInt64():X} pid={pid} class=\"{className}\" dpi={dpi} windowRect={rectText} clientRectScreen={clientText}";
    }

    public static string GetClassNameText(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(256);
        var length = NativeMethods.GetClassName(hwnd, builder, builder.Capacity);
        return length <= 0 ? "(未知)" : builder.ToString();
    }

    public static string DescribeHitWindow(IntPtr hit)
    {
        if (hit == IntPtr.Zero)
        {
            return "hit=0x0";
        }

        NativeMethods.GetWindowThreadProcessId(hit, out var pid);
        return $"hit=0x{hit.ToInt64():X} pid={pid} class=\"{GetClassNameText(hit)}\"";
    }

    public static bool TryGetClientRectScreen(IntPtr hwnd, out NativeMethods.RECT clientScreen)
    {
        clientScreen = default;
        if (!NativeMethods.GetClientRect(hwnd, out var client))
        {
            return false;
        }

        var origin = new NativeMethods.POINT { X = client.Left, Y = client.Top };
        if (!NativeMethods.ClientToScreen(hwnd, ref origin))
        {
            return false;
        }

        clientScreen = new NativeMethods.RECT
        {
            Left = origin.X,
            Top = origin.Y,
            Right = origin.X + (client.Right - client.Left),
            Bottom = origin.Y + (client.Bottom - client.Top)
        };
        return true;
    }

    public static string FormatRect(NativeMethods.RECT rect)
    {
        return $"[{rect.Left},{rect.Top} - {rect.Right},{rect.Bottom}] ({rect.Right - rect.Left}x{rect.Bottom - rect.Top})";
    }

    public static bool Contains(NativeMethods.RECT rect, int x, int y)
    {
        return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
    }

    public static string FormatAwareness(IntPtr context)
    {
        if (context == IntPtr.Zero)
        {
            return "unknown(0)";
        }

        return NativeMethods.GetAwarenessFromDpiAwarenessContext(context) switch
        {
            -1 => "invalid",
            0 => "unaware",
            1 => "system",
            2 => "per-monitor",
            3 => "per-monitor-v2",
            4 => "unaware-gdi-scaled",
            var other => $"other({other})"
        };
    }

    public static string MessageName(int message)
    {
        return message switch
        {
            NativeMethods.WM_MOUSEMOVE => "WM_MOUSEMOVE",
            NativeMethods.WM_LBUTTONDOWN => "WM_LBUTTONDOWN",
            NativeMethods.WM_LBUTTONUP => "WM_LBUTTONUP",
            NativeMethods.WM_RBUTTONDOWN => "WM_RBUTTONDOWN",
            NativeMethods.WM_RBUTTONUP => "WM_RBUTTONUP",
            NativeMethods.WM_MBUTTONDOWN => "WM_MBUTTONDOWN",
            NativeMethods.WM_MBUTTONUP => "WM_MBUTTONUP",
            NativeMethods.WM_MOUSEWHEEL => "WM_MOUSEWHEEL",
            NativeMethods.WM_XBUTTONDOWN => "WM_XBUTTONDOWN",
            NativeMethods.WM_XBUTTONUP => "WM_XBUTTONUP",
            NativeMethods.WM_KEYDOWN => "WM_KEYDOWN",
            NativeMethods.WM_KEYUP => "WM_KEYUP",
            NativeMethods.WM_SYSKEYDOWN => "WM_SYSKEYDOWN",
            NativeMethods.WM_SYSKEYUP => "WM_SYSKEYUP",
            _ => $"0x{message:X4}"
        };
    }
}

public sealed record SyncDiagnosticReport(string Summary, string? LogPath);
