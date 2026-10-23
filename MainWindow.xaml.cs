using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Multiboxing.Views;
using Wpf.Ui.Controls;

namespace Multiboxing;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly PreviewViewModel _previewViewModel = new();
    private readonly TrayService _trayService = new();
    private PreviewHost? _previewHost;
    private bool _previewActivated;
    private bool _allowClose;

    internal MainWindow(NotificationService notifications)
    {
        InitializeComponent();

        _viewModel = new MainViewModel(
            notifications,
            this,
            () => new WindowInteropHelper(this).Handle);
        SyncPageControl.DataContext = _viewModel;
        PreviewPageControl.DataContext = _previewViewModel;

        _previewViewModel.HideWindowRequested = Hide;
        _previewViewModel.MinimizeWindowRequested = () => WindowState = WindowState.Minimized;
        _previewViewModel.CloseWindowRequested = RequestCloseFromPreview;

        _trayService.RestoreRequested += (_, _) => RestoreFromTray();
        _trayService.ExitRequested += (_, _) =>
        {
            if (_previewActivated) _previewViewModel.RequestExit();
            else { _allowClose = true; Close(); }
        };

        SingleInstance.ShowRequested += HandleShowRequested;
        SourceInitialized += HandleSourceInitialized;
        StateChanged += HandleStateChanged;
    }

    private void HandleShowRequested()
    {
        // 信号来自 ThreadPool 线程，切回 UI 线程恢复窗口（可能正藏在托盘里）
        try
        {
            Dispatcher.BeginInvoke(RestoreFromTray);
        }
        catch
        {
            // Dispatcher 正在关闭
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && _previewActivated && _previewHost != null)
        {
            if (!_previewHost.RequestClose())
            {
                e.Cancel = true;
                return;
            }
        }
        _allowClose = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SingleInstance.ShowRequested -= HandleShowRequested;
        SourceInitialized -= HandleSourceInitialized;
        StateChanged -= HandleStateChanged;
        // Dispose 内部会 SaveAndStop 关闭全部缩略图；这里显式再走一次保证幂等清理
        try { _previewHost?.SaveAndStop(); } catch { }
        _previewHost?.Dispose();
        _previewHost = null;
        _trayService.Dispose();
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void RequestCloseFromPreview()
    {
        // Presenter 已决定退出，OnClosing 中会再次走 RequestClose 并得到 Allow=true。
        Close();
    }

    private void RestoreFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void HandleStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _previewActivated)
        {
            _previewViewModel.NotifyFormMinimized();
        }
    }

    private void HandleSourceInitialized(object? sender, EventArgs e)
    {
        ApplyBackdropFallbackIfNeeded();
        _viewModel.StartSideButtonHook();
        ActivatePreview();
    }

    private void ActivatePreview()
    {
        if (_previewActivated) return;
        _previewActivated = true;
        try
        {
            _previewHost = new PreviewHost(_previewViewModel, _viewModel);
            _previewHost.Activate();
        }
        catch (Exception ex)
        {
            _previewActivated = false;
            System.Diagnostics.Debug.WriteLine($"EVE-O-Preview 启动失败: {ex}");
        }
    }

    private void ApplyBackdropFallbackIfNeeded()
    {
        if (WindowBackdrop.IsSupported(WindowBackdropType.Acrylic))
        {
            return;
        }
        WindowBackdropType = WindowBackdropType.None;
        if (TryFindResource("FallbackBackgroundBrush") is System.Windows.Media.Brush fallback)
        {
            Background = fallback;
        }
    }
}
