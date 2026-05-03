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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 380));
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

        var windowListMenu = new ContextMenuStrip();
        var setMainMenuItem = new ToolStripMenuItem("设置为主窗口");
        setMainMenuItem.Click += (_, _) => SetSelectedWindowAsMain();
        var deleteMenuItem = new ToolStripMenuItem("删除");
        deleteMenuItem.Click += (_, _) => DeleteSelectedWindow();
        windowListMenu.Items.AddRange([setMainMenuItem, deleteMenuItem]);
        windowListMenu.Opening += (_, args) => args.Cancel = GetSelectedWindow() == null;
        _windowList.ContextMenuStrip = windowListMenu;
        _windowList.MouseDown += HandleWindowListMouseDown;

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
        operationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
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

        _pickerHintLabel.AutoSize = false;
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
            RowCount = 4,
            Padding = new Padding(10)
        };
        commandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        commandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        commandGroup.Controls.Add(commandPanel);

        static void ConfigureCommandControl(Control control)
        {
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(4);
            control.MinimumSize = new Size(96, 38);
            if (control is ButtonBase buttonBase)
            {
                buttonBase.TextAlign = ContentAlignment.MiddleCenter;
            }
        }

        var addButton = new Button { Text = "添加窗口" };
        ConfigureCommandControl(addButton);
        addButton.Click += (_, _) => AddWindowFromCursor(setAsMain: false);
        commandPanel.Controls.Add(addButton, 0, 0);

        var deleteButton = new Button { Text = "删除" };
        ConfigureCommandControl(deleteButton);
        deleteButton.Click += (_, _) => DeleteSelectedWindow();
        commandPanel.Controls.Add(deleteButton, 1, 0);

        var setMainButton = new Button { Text = "设为主窗口" };
        ConfigureCommandControl(setMainButton);
        setMainButton.Click += (_, _) => SetSelectedOrCursorWindowAsMain();
        commandPanel.SetColumnSpan(setMainButton, 2);
        commandPanel.Controls.Add(setMainButton, 0, 1);

        _toggleSyncButton.Text = "开启同步";
        ConfigureCommandControl(_toggleSyncButton);
        _toggleSyncButton.Click += (_, _) => ToggleSync();
        commandPanel.Controls.Add(_toggleSyncButton, 0, 2);

        _testMessageButton.Text = "测试同步";
        ConfigureCommandControl(_testMessageButton);
        _testMessageButton.Click += (_, _) => TestSelectedWindowMessage();
        commandPanel.Controls.Add(_testMessageButton, 1, 2);

        _keyboardCheckBox.Text = "键盘同步";
        ConfigureCommandControl(_keyboardCheckBox);
        _keyboardCheckBox.Checked = true;
        commandPanel.Controls.Add(_keyboardCheckBox, 0, 3);

        _mouseCheckBox.Text = "鼠标同步";
        ConfigureCommandControl(_mouseCheckBox);
        _mouseCheckBox.Checked = true;
        commandPanel.Controls.Add(_mouseCheckBox, 1, 3);

        var messageGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "程序操作指南"
        };
        operationGrid.Controls.Add(messageGroup, 2, 0);

        var guideHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        var guideText =
            "1. 按住左侧十字拖到要同步的窗口，松开后加入列表；也可以把鼠标移到目标窗口上，再点「添加窗口」。\r\n\r\n" +
            "2. 在列表中选择你实际要操作输入的那一个窗口，点「设为主窗口」。\r\n\r\n" +
            "3. 勾选「键盘同步」「鼠标同步」等需要的内容；可先点「测试同步」确认目标窗口是否可用。\r\n\r\n" +
            "4. 点「开启同步」后，在主操作窗口里正常打字、点击即可同步到列表中的其它窗口。\r\n\r\n" +
            "提示：鼠标侧键1（后退键/XButton1）可快捷开启或关闭同步；侧键2（前进键/XButton2）可添加光标所在窗口。";
        var guideBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            TabStop = false,
            BackColor = guideHost.BackColor,
            ForeColor = SystemColors.ControlText,
            Text = guideText
        };
        guideHost.Controls.Add(guideBox);
        messageGroup.Controls.Add(guideHost);

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

    private void SetSelectedWindowAsMain()
    {
        var selected = GetSelectedWindow();
        if (selected == null)
        {
            ShowWarning("请先选择要设置为主窗口的窗口。");
            return;
        }

        SetMainWindow(selected);
        RefreshWindowList();
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

    private void HandleWindowListMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        var item = _windowList.GetItemAt(e.X, e.Y);
        _windowList.SelectedItems.Clear();
        if (item == null)
        {
            return;
        }

        item.Selected = true;
        item.Focused = true;
        _windowList.Focus();
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
