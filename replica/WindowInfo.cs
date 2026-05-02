using System.ComponentModel;

namespace KeyMouseSyncReplica;

public sealed class WindowInfo : INotifyPropertyChanged
{
    private bool _isMain;
    private string _bindState = "未就绪";
    private string _syncState = "未同步";
    private string _currentMode = "未同步";
    private string _lastError = string.Empty;

    public WindowInfo(IntPtr handle, int processId, string title)
    {
        Handle = handle;
        ProcessId = processId;
        Title = string.IsNullOrWhiteSpace(title) ? "(无标题窗口)" : title;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IntPtr Handle { get; }

    public int ProcessId { get; }

    public string Title { get; }

    public bool IsMain
    {
        get => _isMain;
        set
        {
            if (_isMain == value)
            {
                return;
            }

            _isMain = value;
            OnPropertyChanged(nameof(IsMain));
            OnPropertyChanged(nameof(SyncState));
            OnPropertyChanged(nameof(CurrentMode));
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public string BindState
    {
        get => _bindState;
        set
        {
            if (_bindState == value)
            {
                return;
            }

            _bindState = value;
            OnPropertyChanged(nameof(BindState));
        }
    }

    public string SyncState
    {
        get => IsMain ? "主操作窗口" : _syncState;
        set
        {
            if (_syncState == value)
            {
                return;
            }

            _syncState = value;
            OnPropertyChanged(nameof(SyncState));
        }
    }

    public string HandleHex => $"0x{Handle.ToInt64():X}";

    public string DisplayTitle => IsMain ? $"{Title} [主]" : Title;

    public string CurrentMode
    {
        get => IsMain ? "输入源" : _currentMode;
        set
        {
            if (_currentMode == value)
            {
                return;
            }

            _currentMode = value;
            OnPropertyChanged(nameof(CurrentMode));
        }
    }

    public string LastError
    {
        get => _lastError;
        set
        {
            if (_lastError == value)
            {
                return;
            }

            _lastError = value;
            OnPropertyChanged(nameof(LastError));
        }
    }

    public void ResetRuntimeState()
    {
        BindState = "未就绪";
        SyncState = "未同步";
        CurrentMode = "未同步";
        LastError = string.Empty;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
