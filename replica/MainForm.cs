using System.ComponentModel;

namespace KeyMouseSyncReplica;

public sealed class MainForm : Form
{
    private readonly NotificationService _notifications;
    private readonly SyncManager _syncManager = new();
    private readonly MouseSideButtonToggleHook _sideButtonToggleHook = new();
    private readonly BindingList<WindowInfo> _windows = new();
    private readonly CheckBox _keyboardCheckBox = new();
    private readonly CheckBox _mouseCheckBox = new();
    private readonly Label _mainWindowLabel = new();
    private readonly ListView _windowList = new();
    private readonly Button _toggleSyncButton = new();
    private readonly Button _testMessageButton = new();
    private readonly Label _pickerHintLabel = new();
    private readonly TargetPickerControl _targetPicker = new();
    private WindowInfo? _mainWindow;

    internal MainForm(NotificationService notifications)
    {
        _notifications = notifications;
        _notifications.SetActivationForm(this);

        Text = "多窗口键鼠同步器 - Windows Message";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 720);
        Size = new Size(1120, 760);

        BuildUi();
        _sideButtonToggleHook.ToggleRequested += HandleSideButtonToggleRequested;
        _sideButtonToggleHook.AddWindowRequested += HandleSideButtonAddWindowRequested;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (!_sideButtonToggleHook.Start(out var error))
        {
            ShowWarning(error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _sideButtonToggleHook.Dispose();
        _syncManager.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "多窗口键鼠同步--Windows 消息同步\r\n注：鼠标在主操作窗口上时才同步操作",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold)
        };
        root.Controls.Add(title, 0, 0);

        var listGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "同步窗口列表",
            Padding = new Padding(10)
        };
        _windowList.Dock = DockStyle.Fill;
        _windowList.View = View.Details;
        _windowList.FullRowSelect = true;
        _windowList.GridLines = true;
        _windowList.MultiSelect = false;
        _windowList.Columns.Add("id", 70);
        _windowList.Columns.Add("窗口句柄", 120);
        _windowList.Columns.Add("进程id", 85);
        _windowList.Columns.Add("窗口标题", 350);
        _windowList.Columns.Add("消息状态", 110);
        _windowList.Columns.Add("同步状态", 120);
        _windowList.Columns.Add("当前模式", 300);
        listGroup.Controls.Add(_windowList);
        root.Controls.Add(listGroup, 0, 1);

        var operationGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "操作",
            Padding = new Padding(12)
        };
        root.Controls.Add(operationGroup, 0, 2);

        var operationGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        operationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        operationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        operationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        operationGroup.Controls.Add(operationGrid);

        var pickerGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "选择窗口"
        };
        operationGrid.Controls.Add(pickerGroup, 0, 0);

        var pickerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        pickerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        pickerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        pickerPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pickerGroup.Controls.Add(pickerPanel);

        _targetPicker.Anchor = AnchorStyles.Top;
        _targetPicker.PickCompleted += (_, args) => AddWindowFromPoint(args.ScreenPoint, setAsMain: false);
        pickerPanel.Controls.Add(_targetPicker, 0, 0);

        var crossLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "按住十字拖动",
            TextAlign = ContentAlignment.MiddleCenter
        };
        pickerPanel.Controls.Add(crossLabel, 0, 1);

        _pickerHintLabel.Dock = DockStyle.Fill;
        _pickerHintLabel.Text = "拖到目标窗口后松开，即可加入列表。";
        _pickerHintLabel.TextAlign = ContentAlignment.TopCenter;
        pickerPanel.Controls.Add(_pickerHintLabel, 0, 2);

        var commandGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "同步操作"
        };
        operationGrid.Controls.Add(commandGroup, 1, 0);

        var commandPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(10)
        };
        commandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        commandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        commandGroup.Controls.Add(commandPanel);

        var addButton = new Button { Dock = DockStyle.Fill, Text = "添加鼠标" };
        addButton.Click += (_, _) => AddWindowFromCursor(setAsMain: false);
        commandPanel.Controls.Add(addButton, 0, 0);

        var setMainButton = new Button { Dock = DockStyle.Fill, Text = "设置为主窗口" };
        setMainButton.Click += (_, _) => SetSelectedOrCursorWindowAsMain();
        commandPanel.Controls.Add(setMainButton, 1, 0);

        var deleteButton = new Button { Dock = DockStyle.Fill, Text = "删除" };
        deleteButton.Click += (_, _) => DeleteSelectedWindow();
        commandPanel.Controls.Add(deleteButton, 0, 1);

        _toggleSyncButton.Dock = DockStyle.Fill;
        _toggleSyncButton.Text = "开启同步";
        _toggleSyncButton.Click += (_, _) => ToggleSync();
        commandPanel.Controls.Add(_toggleSyncButton, 1, 1);

        _keyboardCheckBox.Text = "同步键盘操作";
        _keyboardCheckBox.Dock = DockStyle.Fill;
        _keyboardCheckBox.Checked = true;
        commandPanel.Controls.Add(_keyboardCheckBox, 0, 2);

        _mouseCheckBox.Text = "同步鼠标操作";
        _mouseCheckBox.Dock = DockStyle.Fill;
        _mouseCheckBox.Checked = true;
        commandPanel.Controls.Add(_mouseCheckBox, 1, 2);

        _testMessageButton.Dock = DockStyle.Fill;
        _testMessageButton.Text = "测试消息同步";
        _testMessageButton.Click += (_, _) => TestSelectedWindowMessage();
        commandPanel.SetColumnSpan(_testMessageButton, 2);
        commandPanel.Controls.Add(_testMessageButton, 0, 3);

        var shortcutHint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "提示：鼠标侧键1（后退键/XButton1）可快捷开启或关闭同步；鼠标侧键2（前进键/XButton2）可添加当前光标所在窗口。",
            TextAlign = ContentAlignment.TopLeft
        };
        commandPanel.SetColumnSpan(shortcutHint, 2);
        commandPanel.Controls.Add(shortcutHint, 0, 4);

        var messageGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Windows 消息同步"
        };
        operationGrid.Controls.Add(messageGroup, 2, 0);

        var messageHint = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Text = "当前版本只使用 Windows 消息同步：程序捕获主操作窗口上的键鼠事件，" +
                "再通过 PostMessage 转发到目标窗口。\r\n\r\n" +
                "“测试消息同步”会向目标窗口发送 WM_NULL 探针，只检查消息队列是否接受投递；" +
                "真实键鼠效果仍取决于目标程序是否处理对应的键盘和鼠标消息。",
            TextAlign = ContentAlignment.TopLeft
        };
        messageGroup.Controls.Add(messageHint);

        _mainWindowLabel.Dock = DockStyle.Fill;
        _mainWindowLabel.TextAlign = ContentAlignment.MiddleLeft;
        _mainWindowLabel.Text = "主操作窗口句柄：未设置";
        root.Controls.Add(_mainWindowLabel, 0, 3);
    }

    private void AddWindowFromCursor(bool setAsMain, bool notifyResult = false)
    {
        var picked = WindowPicker.PickWindowUnderCursor(Handle, out var error);
        AddPickedWindow(picked, error, setAsMain, notifyResult);
    }

    private void AddWindowFromPoint(Point screenPoint, bool setAsMain)
    {
        var picked = WindowPicker.PickWindowAtPoint(Handle, screenPoint, out var error);
        AddPickedWindow(picked, error, setAsMain);
    }

    private void AddPickedWindow(WindowInfo? picked, string error, bool setAsMain, bool notifyResult = false)
    {
        if (picked == null)
        {
            if (notifyResult)
            {
                _notifications.ShowSystemNotificationOrWarning(this, "添加窗口失败", error);
            }
            else
            {
                ShowWarning(error);
            }

            return;
        }

        var existing = _windows.FirstOrDefault(window => window.Handle == picked.Handle);
        if (existing != null)
        {
            if (setAsMain)
            {
                SetMainWindow(existing);
                RefreshWindowList();
                if (notifyResult)
                {
                    _notifications.ShowSystemNotificationOrInformation(this, "主窗口已设置", $"{existing.DisplayTitle} 已设为主操作窗口。");
                }
            }
            else
            {
                if (notifyResult)
                {
                    _notifications.ShowSystemNotificationOrWarning(this, "窗口已存在", "当前窗口已存在于同步列表。");
                }
                else
                {
                    ShowWarning("当前窗口已存在！");
                }
            }

            return;
        }

        _windows.Add(picked);
        if (setAsMain)
        {
            SetMainWindow(picked);
        }

        RefreshWindowList();
        if (notifyResult && setAsMain)
        {
            _notifications.ShowSystemNotificationOrInformation(
                this,
                "主窗口已设置",
                $"{picked.DisplayTitle} 已设为主操作窗口。");
        }
    }

    private void SetSelectedOrCursorWindowAsMain()
    {
        var selected = GetSelectedWindow();
        if (selected != null)
        {
            SetMainWindow(selected);
            RefreshWindowList();
            return;
        }

        AddWindowFromCursor(setAsMain: true);
    }

    private void SetMainWindow(WindowInfo window)
    {
        foreach (var item in _windows)
        {
            item.IsMain = false;
        }

        window.IsMain = true;
        _mainWindow = window;
        _mainWindowLabel.Text = $"主操作窗口句柄：{window.HandleHex}  {window.Title}";
    }

    private void DeleteSelectedWindow()
    {
        if (_syncManager.IsRunning)
        {
            ShowWarning("请先关闭同步,再删除！");
            return;
        }

        var selected = GetSelectedWindow();
        if (selected == null)
        {
            ShowWarning("请先选择要删除的窗口。");
            return;
        }

        _windows.Remove(selected);
        if (_mainWindow?.Handle == selected.Handle)
        {
            _mainWindow = null;
            _mainWindowLabel.Text = "主操作窗口句柄：未设置";
        }

        RefreshWindowList();
    }

    private void ToggleSync(bool notifyResult = false)
    {
        if (_syncManager.IsRunning)
        {
            _syncManager.Stop();
            foreach (var window in _windows)
            {
                window.ResetRuntimeState();
            }

            _toggleSyncButton.Text = "开启同步";
            RefreshWindowList();
            if (notifyResult)
            {
                _notifications.ShowSystemNotificationOrInformation(this, "同步已关闭", "已通过鼠标侧键关闭键鼠同步。");
            }

            return;
        }

        if (!_syncManager.Start(
                _windows.ToArray(),
                _mainWindow,
                _keyboardCheckBox.Checked,
                _mouseCheckBox.Checked,
                out var error))
        {
            if (notifyResult)
            {
                _notifications.ShowSystemNotificationOrWarning(this, "开启同步失败", error);
            }
            else
            {
                ShowWarning(error);
            }

            RefreshWindowList();
            return;
        }

        _toggleSyncButton.Text = "关闭同步";
        RefreshWindowList();
        if (notifyResult)
        {
            var targetCount = _windows.Count(window => _mainWindow == null || window.Handle != _mainWindow.Handle);
            _notifications.ShowSystemNotificationOrInformation(this, "同步已开启", $"正在向 {targetCount} 个目标窗口同步输入。");
        }
    }

    private void TestSelectedWindowMessage()
    {
        var selected = GetSelectedWindow();
        if (selected == null)
        {
            ShowWarning("请先选择一个要测试的同步目标窗口。");
            return;
        }

        if (!_syncManager.TestMessageFallback(
                _mainWindow,
                selected,
                _keyboardCheckBox.Checked,
                _mouseCheckBox.Checked,
                out var report,
                out var error) || report == null)
        {
            ShowWarning(error);
            return;
        }

        _notifications.ShowMessageTestReport(this, report);
    }

    private void HandleSideButtonToggleRequested(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        try
        {
            BeginInvoke((MethodInvoker)ToggleSyncFromShortcut);
        }
        catch (ObjectDisposedException)
        {
            // Ignore late hook callbacks after the form has started closing.
        }
        catch (InvalidOperationException)
        {
            // The form handle can disappear during shutdown while the low-level hook is unwinding.
        }
    }

    private void HandleSideButtonAddWindowRequested(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        try
        {
            BeginInvoke((MethodInvoker)AddWindowFromSideButton);
        }
        catch (ObjectDisposedException)
        {
            // Ignore late hook callbacks after the form has started closing.
        }
        catch (InvalidOperationException)
        {
            // The form handle can disappear during shutdown while the low-level hook is unwinding.
        }
    }

    private void ToggleSyncFromShortcut()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        ToggleSync(notifyResult: true);
    }

    private void AddWindowFromSideButton()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        AddWindowFromCursor(setAsMain: false, notifyResult: true);
    }

    private WindowInfo? GetSelectedWindow()
    {
        if (_windowList.SelectedItems.Count == 0)
        {
            return null;
        }

        return _windowList.SelectedItems[0].Tag as WindowInfo;
    }

    private void RefreshWindowList()
    {
        _windowList.BeginUpdate();
        try
        {
            _windowList.Items.Clear();
            foreach (var window in _windows)
            {
                var item = new ListViewItem((_windowList.Items.Count + 1).ToString())
                {
                    Tag = window,
                    BackColor = window.IsMain ? Color.LightGoldenrodYellow : SystemColors.Window
                };
                item.SubItems.Add(window.HandleHex);
                item.SubItems.Add(window.ProcessId.ToString());
                item.SubItems.Add(window.DisplayTitle);
                item.SubItems.Add(window.BindState);
                item.SubItems.Add(window.SyncState);
                item.SubItems.Add(window.CurrentMode);
                _windowList.Items.Add(item);
            }
        }
        finally
        {
            _windowList.EndUpdate();
        }
    }

    private void ShowWarning(string message)
    {
        _notifications.ShowWarning(this, message);
    }
}
