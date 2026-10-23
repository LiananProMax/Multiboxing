using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EveOPreview.Services;
using DrawingPoint = System.Drawing.Point;

namespace Multiboxing;

internal sealed class MainViewModel : INotifyPropertyChanged, IDisposable, ISyncTargetRegistry
{
    private readonly NotificationService _notifications;
    private readonly Window _owner;
    private readonly Dispatcher _dispatcher;
    private readonly Func<IntPtr> _getOwnerHandle;
    private readonly SyncManager _syncManager = new();
    private readonly MouseSideButtonToggleHook _sideButtonToggleHook = new();
    private WindowInfo? _mainWindow;
    private WindowInfo? _selectedWindow;
    private bool _syncKeyboard = true;
    private bool _syncMouse = true;
    private bool _enableDiagnostics;
    private bool _disposed;

    public MainViewModel(NotificationService notifications, Window owner, Func<IntPtr> getOwnerHandle)
    {
        _notifications = notifications;
        _owner = owner;
        _dispatcher = owner.Dispatcher;
        _getOwnerHandle = getOwnerHandle;

        AddWindowCommand = new RelayCommand(() => AddWindowFromCursor(setAsMain: false));
        DeleteWindowCommand = new RelayCommand(DeleteSelectedWindow, () => SelectedWindow != null);
        SetMainWindowCommand = new RelayCommand(SetSelectedOrCursorWindowAsMain);
        ToggleSyncCommand = new RelayCommand(() => ToggleSync());
        TestMessageCommand = new RelayCommand(TestSelectedWindowMessage, () => SelectedWindow != null);

        _sideButtonToggleHook.ToggleRequested += HandleSideButtonToggleRequested;
        _sideButtonToggleHook.AddWindowRequested += HandleSideButtonAddWindowRequested;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WindowInfo> Windows { get; } = new();

    public WindowInfo? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            if (_selectedWindow == value)
            {
                return;
            }

            _selectedWindow = value;
            OnPropertyChanged(nameof(SelectedWindow));
            RaiseCommandStateChanged();
        }
    }

    public bool SyncKeyboard
    {
        get => _syncKeyboard;
        set
        {
            if (_syncKeyboard == value)
            {
                return;
            }

            _syncKeyboard = value;
            OnPropertyChanged(nameof(SyncKeyboard));
        }
    }

    public bool SyncMouse
    {
        get => _syncMouse;
        set
        {
            if (_syncMouse == value)
            {
                return;
            }

            _syncMouse = value;
            OnPropertyChanged(nameof(SyncMouse));
        }
    }

    public bool EnableDiagnostics
    {
        get => _enableDiagnostics;
        set
        {
            if (_enableDiagnostics == value)
            {
                return;
            }

            _enableDiagnostics = value;
            OnPropertyChanged(nameof(EnableDiagnostics));
        }
    }

    public bool IsSyncRunning => _syncManager.IsRunning;

    public string ToggleSyncText => IsSyncRunning ? "关闭同步" : "开启同步";

    public string SyncStatusText => IsSyncRunning ? "同步运行中" : "同步未开启";

    public string MainWindowSummary => _mainWindow == null
        ? "未设置主操作窗口"
        : $"{_mainWindow.HandleHex}  {_mainWindow.Title}";

    public int WindowCount => Windows.Count;

    public int TargetCount => Windows.Count(window => _mainWindow == null || window.Handle != _mainWindow.Handle);

    public ICommand AddWindowCommand { get; }

    public ICommand DeleteWindowCommand { get; }

    public ICommand SetMainWindowCommand { get; }

    public ICommand ToggleSyncCommand { get; }

    public ICommand TestMessageCommand { get; }

    public void StartSideButtonHook()
    {
        if (!_sideButtonToggleHook.Start(out var error))
        {
            ShowWarning(error);
        }
    }

    public void AddWindowFromPoint(DrawingPoint screenPoint)
    {
        var picked = WindowPicker.PickWindowAtPoint(_getOwnerHandle(), screenPoint, out var error);
        AddPickedWindow(picked, error, setAsMain: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sideButtonToggleHook.Dispose();
        _syncManager.Dispose();
    }

    private void AddWindowFromCursor(bool setAsMain, bool notifyResult = false)
    {
        var picked = WindowPicker.PickWindowUnderCursor(_getOwnerHandle(), out var error);
        AddPickedWindow(picked, error, setAsMain, notifyResult);
    }

    private void AddPickedWindow(WindowInfo? picked, string error, bool setAsMain, bool notifyResult = false)
    {
        if (picked == null)
        {
            if (notifyResult)
            {
                _notifications.ShowToast(ToastKind.Error, "添加窗口失败", error);
            }
            else
            {
                ShowWarning(error);
            }

            return;
        }

        var existing = Windows.FirstOrDefault(window => window.Handle == picked.Handle);
        if (existing != null)
        {
            if (setAsMain)
            {
                SetMainWindow(existing);
                if (notifyResult)
                {
                    _notifications.ShowToast(ToastKind.Info, "主窗口已设置", $"{existing.DisplayTitle} 已设为主操作窗口。");
                }
            }
            else if (notifyResult)
            {
                _notifications.ShowToast(ToastKind.Warning, "窗口已存在", "当前窗口已存在于同步列表。");
            }
            else
            {
                ShowWarning("当前窗口已存在！");
            }

            return;
        }

        Windows.Add(picked);
        SelectedWindow = picked;
        if (setAsMain)
        {
            SetMainWindow(picked);
        }

        RefreshSummary();
        if (notifyResult && setAsMain)
        {
            _notifications.ShowToast(
                ToastKind.Info,
                "主窗口已设置",
                $"{picked.DisplayTitle} 已设为主操作窗口。");
        }
    }

    private void SetSelectedOrCursorWindowAsMain()
    {
        if (SelectedWindow != null)
        {
            SetMainWindow(SelectedWindow);
            return;
        }

        AddWindowFromCursor(setAsMain: true);
    }

    private void SetMainWindow(WindowInfo window)
    {
        foreach (var item in Windows)
        {
            item.IsMain = false;
        }

        window.IsMain = true;
        _mainWindow = window;
        SelectedWindow = window;
        RefreshSummary();
    }

    private void DeleteSelectedWindow()
    {
        if (_syncManager.IsRunning)
        {
            ShowWarning("请先关闭同步,再删除！");
            return;
        }

        var selected = SelectedWindow;
        if (selected == null)
        {
            ShowWarning("请先选择要删除的窗口。");
            return;
        }

        RemoveWindow(selected);
        RefreshSummary();
    }

    private void RemoveWindow(WindowInfo window)
    {
        Windows.Remove(window);
        if (_mainWindow?.Handle == window.Handle)
        {
            _mainWindow = null;
        }

        if (SelectedWindow?.Handle == window.Handle)
        {
            SelectedWindow = Windows.FirstOrDefault();
        }
    }

    #region ISyncTargetRegistry（缩略图 Ctrl+点击）

    public bool IsSyncTarget(IntPtr clientHandle)
    {
        return clientHandle != IntPtr.Zero
            && Windows.Any(window => IsSameClient(window, clientHandle));
    }

    public bool IsMainSyncTarget(IntPtr clientHandle)
    {
        return clientHandle != IntPtr.Zero
            && _mainWindow != null
            && IsSameClient(_mainWindow, clientHandle);
    }

    public void Toggle(IntPtr clientHandle, string title)
    {
        // 同步运行中目标集合已在 Start 时固定，改列表不会生效，直接拦截避免误导
        if (_syncManager.IsRunning)
        {
            _notifications.ShowToast(ToastKind.Warning, "同步运行中", "请先关闭同步，再修改同步列表。");
            return;
        }

        var existing = Windows.Where(window => IsSameClient(window, clientHandle)).ToArray();
        if (existing.Length > 0)
        {
            foreach (var window in existing)
            {
                RemoveWindow(window);
            }

            RefreshSummary();
            return;
        }

        // clientHandle 是游戏窗口句柄（缩略图的 Id），不要按鼠标位置取窗口
        var picked = WindowPicker.FromHandle(clientHandle, title, out var error);
        if (picked == null)
        {
            _notifications.ShowToast(ToastKind.Error, "加入同步列表失败", error);
            return;
        }

        Windows.Add(picked);
        SelectedWindow = picked;
        RefreshSummary();
    }

    public void SetMain(IntPtr clientHandle, string title)
    {
        // 同步运行中主窗口/目标集合已在 Start 时固定，切换不会生效，直接拦截避免误导
        if (_syncManager.IsRunning)
        {
            _notifications.ShowToast(ToastKind.Warning, "同步运行中", "请先关闭同步，再修改主窗口。");
            return;
        }

        if (clientHandle == IntPtr.Zero)
        {
            return;
        }

        var existing = Windows.FirstOrDefault(window => IsSameClient(window, clientHandle));
        if (existing != null)
        {
            if (_mainWindow != null && IsSameClient(_mainWindow, clientHandle))
            {
                return;
            }

            SetMainWindow(existing);
            return;
        }

        // clientHandle 是游戏窗口句柄（缩略图的 Id），不要按鼠标位置取窗口
        var picked = WindowPicker.FromHandle(clientHandle, title, out var error);
        if (picked == null)
        {
            _notifications.ShowToast(ToastKind.Error, "设置主窗口失败", error);
            return;
        }

        Windows.Add(picked);
        SetMainWindow(picked);
    }

    private static bool IsSameClient(WindowInfo window, IntPtr clientHandle)
    {
        if (window.Handle == clientHandle)
        {
            return true;
        }

        if (window.Handle == IntPtr.Zero)
        {
            return false;
        }

        // 兼容之前用十字拖拽/光标位置添加、取到子窗口句柄的条目
        return NativeMethods.GetAncestor(window.Handle, (uint)NativeMethods.GA_ROOT) == clientHandle;
    }

    #endregion

    private void ToggleSync(bool notifyResult = false)
    {
        if (_syncManager.IsRunning)
        {
            _syncManager.Stop();
            foreach (var window in Windows)
            {
                window.ResetRuntimeState();
            }

            RefreshSummary();
            if (notifyResult)
            {
                _notifications.ShowToast(ToastKind.Info, "同步已关闭", "已通过鼠标侧键关闭键鼠同步。");
            }

            ShowDiagnosticReportIfAny();
            return;
        }

        if (!_syncManager.Start(
                Windows.ToArray(),
                _mainWindow,
                SyncKeyboard,
                SyncMouse,
                out var error,
                enableDiagnostics: EnableDiagnostics))
        {
            if (notifyResult)
            {
                _notifications.ShowToast(ToastKind.Error, "开启同步失败", error);
            }
            else
            {
                ShowWarning(error);
            }

            RefreshSummary();
            return;
        }

        RefreshSummary();
        if (notifyResult)
        {
            _notifications.ShowToast(ToastKind.Success, "同步已开启", $"正在向 {TargetCount} 个目标窗口同步输入。");
        }
    }

    private void ShowDiagnosticReportIfAny()
    {
        if (!EnableDiagnostics)
        {
            return;
        }

        var report = _syncManager.LastDiagnosticReport;
        if (report == null)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(report.Summary);
        }
        catch
        {
            // 剪贴板可能被其他程序占用,失败不影响弹窗展示。
        }

        _notifications.ShowInformation(_owner, "同步诊断摘要(已复制到剪贴板)", report.Summary);
    }

    private void TestSelectedWindowMessage()
    {
        var selected = SelectedWindow;
        if (selected == null)
        {
            ShowWarning("请先选择一个要测试的同步目标窗口。");
            return;
        }

        if (!_syncManager.TestMessageFallback(
                _mainWindow,
                selected,
                SyncKeyboard,
                SyncMouse,
                out var report,
                out var error) || report == null)
        {
            ShowWarning(error);
            return;
        }

        _notifications.ShowMessageTestReport(_owner, report);
    }

    private void HandleSideButtonToggleRequested(object? sender, EventArgs e)
    {
        DispatchToUi(() => ToggleSync(notifyResult: true));
    }

    private void HandleSideButtonAddWindowRequested(object? sender, EventArgs e)
    {
        DispatchToUi(() => AddWindowFromCursor(setAsMain: false, notifyResult: true));
    }

    private void DispatchToUi(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window can be closing while the low-level hook is unwinding.
        }
        catch (TaskCanceledException)
        {
            // Dispatcher shutdown may cancel late side-button callbacks.
        }
    }

    private void ShowWarning(string message)
    {
        _notifications.ShowWarning(_owner, message);
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(IsSyncRunning));
        OnPropertyChanged(nameof(ToggleSyncText));
        OnPropertyChanged(nameof(SyncStatusText));
        OnPropertyChanged(nameof(MainWindowSummary));
        OnPropertyChanged(nameof(WindowCount));
        OnPropertyChanged(nameof(TargetCount));
        RaiseCommandStateChanged();
    }

    private void RaiseCommandStateChanged()
    {
        if (DeleteWindowCommand is RelayCommand deleteCommand)
        {
            deleteCommand.RaiseCanExecuteChanged();
        }

        if (TestMessageCommand is RelayCommand testCommand)
        {
            testCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
