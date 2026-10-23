using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Multiboxing;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// 常驻单例浮层提示：新消息立即覆盖旧消息，不排队、不阻塞、不抢焦点、点击穿透。
/// </summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _hideTimer = new();
    private ToastKind _lastKind;
    private string _lastTitle = string.Empty;
    private string _lastMessage = string.Empty;
    private int _repeatCount;
    private int _presentVersion;
    private bool _closed;

    public ToastWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += HandleHideTimerTick;
        SourceInitialized += HandleSourceInitialized;
        Closed += (_, _) => _closed = true;
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public void EnsureHandle()
    {
        if (_closed)
        {
            return;
        }

        _ = new WindowInteropHelper(this).EnsureHandle();
    }

    public void Present(ToastKind kind, string title, string message)
    {
        if (_closed)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Present(kind, title, message));
            return;
        }

        EnsureHandle();
        var version = ++_presentVersion;

        // 相同内容连发时只更新计数，避免视觉上“一直弹新框”
        var sameAsLast = IsVisible
            && kind == _lastKind
            && string.Equals(title, _lastTitle, StringComparison.Ordinal)
            && string.Equals(message, _lastMessage, StringComparison.Ordinal);
        _repeatCount = sameAsLast ? _repeatCount + 1 : 1;
        _lastKind = kind;
        _lastTitle = title;
        _lastMessage = message;

        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        CountBadge.Visibility = _repeatCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = $"×{_repeatCount}";
        ApplyKind(kind);

        var wasVisible = IsVisible;
        // 取消上一轮可能正在进行的淡出，避免旧动画把新消息隐藏
        BeginAnimation(OpacityProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
        Opacity = 1;

        if (!wasVisible)
        {
            Show();
            PlayEnterAnimation();
        }
        else
        {
            PlayPulseAnimation();
        }

        UpdateLayout();
        PositionBottomRight();

        _hideTimer.Stop();
        _hideTimer.Interval = kind is ToastKind.Warning or ToastKind.Error
            ? TimeSpan.FromMilliseconds(4000)
            : TimeSpan.FromMilliseconds(2500);
        _hideTimer.Tag = version;
        _hideTimer.Start();
    }

    private void ApplyKind(ToastKind kind)
    {
        var (brushKey, symbol) = kind switch
        {
            ToastKind.Success => ("SuccessBrush", "✓"),
            ToastKind.Warning => ("WarningBrush", "!"),
            ToastKind.Error => (null, "✕"),
            _ => ("AccentBrush", "i"),
        };

        IconBadge.Background = brushKey != null && TryFindResource(brushKey) is System.Windows.Media.Brush brush
            ? brush
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71)); // Error 红
        IconText.Text = symbol;
    }

    private void PlayEnterAnimation()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
        BeginAnimation(OpacityProperty, fade);

        SlideTransform.Y = 8;
        var slide = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void PlayPulseAnimation()
    {
        var scale = new DoubleAnimation(1, 1.03, TimeSpan.FromMilliseconds(80))
        {
            AutoReverse = true,
        };
        PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    private void HandleHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        var version = _presentVersion;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            // 淡出期间若有新消息到达（版本号已变），不要隐藏新消息
            if (version != _presentVersion || _closed)
            {
                return;
            }

            Hide();
            Opacity = 1;
            _repeatCount = 0;
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void PositionBottomRight()
    {
        var handle = Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var workingArea = WinForms.Screen.FromPoint(WinForms.Cursor.Position).WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            var widthPx = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
            var heightPx = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            if (widthPx <= 0 || heightPx <= 0)
            {
                return;
            }

            const int margin = 16;
            var x = workingArea.Right - widthPx - margin;
            var y = workingArea.Bottom - heightPx - margin;
            NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HWND_TOPMOST,
                x,
                y,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
                    | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER);
        }
        catch
        {
            // 定位失败不影响提示本身显示
        }
    }

    private void HandleSourceInitialized(object? sender, EventArgs e)
    {
        var handle = Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
    }
}
