using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DrawingPoint = System.Drawing.Point;

namespace KeyMouseSyncReplica;

internal sealed class MainViewModel : INotifyPropertyChanged, IDisposable
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
                _notifications.ShowSystemNotificationOrWarning(_owner, "添加窗口失败", error);
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
                    _notifications.ShowSystemNotificationOrInformation(_owner, "主窗口已设置", $"{existing.DisplayTitle} 已设为主操作窗口。");
                }
            }
            else if (notifyResult)
            {
                _notifications.ShowSystemNotificationOrWarning(_owner, "窗口已存在", "当前窗口已存在于同步列表。");
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
            _notifications.ShowSystemNotificationOrInformation(
                _owner,
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

        Windows.Remove(selected);
        if (_mainWindow?.Handle == selected.Handle)
        {
            _mainWindow = null;
        }

        SelectedWindow = Windows.FirstOrDefault();
        RefreshSummary();
    }

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
                _notifications.ShowSystemNotificationOrInformation(_owner, "同步已关闭", "已通过鼠标侧键关闭键鼠同步。");
            }

            return;
        }

        if (!_syncManager.Start(
                Windows.ToArray(),
                _mainWindow,
                SyncKeyboard,
                SyncMouse,
                out var error))
        {
            if (notifyResult)
            {
                _notifications.ShowSystemNotificationOrWarning(_owner, "开启同步失败", error);
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
            _notifications.ShowSystemNotificationOrInformation(_owner, "同步已开启", $"正在向 {TargetCount} 个目标窗口同步输入。");
        }
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
