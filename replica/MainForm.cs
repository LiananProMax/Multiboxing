using System.ComponentModel;
using System.Text;

namespace KeyMouseSyncReplica;

public sealed class MainForm : Form
{
    private readonly ConfigService _configService = new();
    private readonly SyncManager _syncManager = new();
    private readonly MouseSideButtonToggleHook _sideButtonToggleHook = new();
    private readonly BindingList<WindowInfo> _windows = new();
    private readonly ComboBox _dmComboBox = new();
    private readonly ComboBox _displayComboBox = new();
    private readonly ComboBox _mouseComboBox = new();
    private readonly ComboBox _keypadComboBox = new();
    private readonly ComboBox _publicComboBox = new();
    private readonly ComboBox _modeComboBox = new();
    private readonly CheckBox _keyboardCheckBox = new();
    private readonly CheckBox _mouseCheckBox = new();
    private readonly Label _mainWindowLabel = new();
    private readonly ListView _windowList = new();
    private readonly Button _toggleSyncButton = new();
    private readonly Button _testModesButton = new();
    private readonly Label _pickerHintLabel = new();
    private readonly TargetPickerControl _targetPicker = new();
    private WindowInfo? _mainWindow;
    private bool _isTestingModes;

    public MainForm()
    {
        Text = "多窗口键鼠同步器 - MessageFallback";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 760);
        Size = new Size(1120, 800);

        BuildUi();
        PopulateChoices();
        LoadConfigToControls();
        _sideButtonToggleHook.ToggleRequested += HandleSideButtonToggleRequested;
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
        SaveConfigFromControls();
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "多窗口键鼠同步--基于dm插件(免注册)\r\n注: 鼠标在主操作窗口上时才同步操作",
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
        _windowList.Columns.Add("绑定状态", 110);
        _windowList.Columns.Add("同步状态", 120);
        _windowList.Columns.Add("当前模式", 320);
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

        _testModesButton.Dock = DockStyle.Fill;
        _testModesButton.Text = "测试模式";
        _testModesButton.Click += async (_, _) => await TestSelectedWindowModesAsync();
        commandPanel.SetColumnSpan(_testModesButton, 2);
        commandPanel.Controls.Add(_testModesButton, 0, 3);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "提示：public 为空时使用 BindWindow，非空时使用 BindWindowEx。",
            TextAlign = ContentAlignment.TopLeft
        };
        commandPanel.SetColumnSpan(hint, 2);
        commandPanel.Controls.Add(hint, 0, 4);

        var configGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "绑定配置"
        };
        operationGrid.Controls.Add(configGroup, 2, 0);

        var configGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4,
            Padding = new Padding(10)
        };
        configGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        configGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        configGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        configGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        configGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        configGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        configGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        configGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        configGroup.Controls.Add(configGrid);

        AddLabel(configGrid, "dm：", 0, 0);
        _dmComboBox.Dock = DockStyle.Fill;
        _dmComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        configGrid.SetColumnSpan(_dmComboBox, 2);
        configGrid.Controls.Add(_dmComboBox, 1, 0);

        var registerButton = new Button { Dock = DockStyle.Fill, Text = "注册dm" };
        registerButton.Click += (_, _) => RegisterSelectedDm();
        configGrid.Controls.Add(registerButton, 3, 0);

        AddLabel(configGrid, "display：", 0, 1);
        _displayComboBox.Dock = DockStyle.Fill;
        _displayComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        configGrid.Controls.Add(_displayComboBox, 1, 1);

        AddLabel(configGrid, "mouse：", 2, 1);
        _mouseComboBox.Dock = DockStyle.Fill;
        _mouseComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        configGrid.Controls.Add(_mouseComboBox, 3, 1);

        AddLabel(configGrid, "keypad：", 0, 2);
        _keypadComboBox.Dock = DockStyle.Fill;
        _keypadComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        configGrid.Controls.Add(_keypadComboBox, 1, 2);

        AddLabel(configGrid, "mode：", 2, 2);
        _modeComboBox.Dock = DockStyle.Fill;
        _modeComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        configGrid.Controls.Add(_modeComboBox, 3, 2);

        AddLabel(configGrid, "public：", 0, 3);
        _publicComboBox.Dock = DockStyle.Fill;
        _publicComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        configGrid.SetColumnSpan(_publicComboBox, 3);
        configGrid.Controls.Add(_publicComboBox, 1, 3);

        _mainWindowLabel.Dock = DockStyle.Fill;
        _mainWindowLabel.TextAlign = ContentAlignment.MiddleLeft;
        _mainWindowLabel.Text = "主操作窗口句柄：未设置";
        root.Controls.Add(_mainWindowLabel, 0, 3);
    }

    private void PopulateChoices()
    {
        _dmComboBox.Items.Clear();
        foreach (var dll in FindDmDlls())
        {
            _dmComboBox.Items.Add(new DmDllChoice(dll));
        }

        if (_dmComboBox.Items.Count > 0)
        {
            _dmComboBox.SelectedIndex = 0;
        }

        _displayComboBox.Items.AddRange(new object[] { "normal", "gdi", "gdi2", "dx", "dx2" });
        _mouseComboBox.Items.AddRange(new object[] { "windows", "windows2", "windows3", "dx", "dx2" });
        _keypadComboBox.Items.AddRange(new object[] { "windows", "dx" });
        _publicComboBox.Items.AddRange(new object[]
        {
            string.Empty,
            "dx.public.active.api|dx.public.active.message",
            "dx.public.hide.dll",
            "dx.public.anti.api",
            "dx.public.km.protect"
        });
        _modeComboBox.Items.AddRange(new object[] { "0", "1", "2", "3", "4", "5", "6", "7", "101", "103" });
    }

    private void LoadConfigToControls()
    {
        var config = _configService.Load();
        SelectDm(config.Dm);
        _displayComboBox.Text = NormalizeDisplayMode(config.Display);
        _mouseComboBox.Text = NormalizeInputMode(config.Mouse);
        _keypadComboBox.Text = NormalizeInputMode(config.Keypad);
        _publicComboBox.Text = config.Public;
        _modeComboBox.Text = config.Mode;
    }

    private void SaveConfigFromControls()
    {
        _configService.Save(BuildConfigFromControls());
    }

    private AppConfig BuildConfigFromControls()
    {
        return new AppConfig
        {
            Dm = _dmComboBox.SelectedIndex >= 0 ? _dmComboBox.SelectedIndex.ToString() : "0",
            Display = NormalizeDisplayMode(_displayComboBox.Text),
            Mouse = _mouseComboBox.Text.Trim(),
            Keypad = _keypadComboBox.Text.Trim(),
            Public = _publicComboBox.Text.Trim(),
            Mode = _modeComboBox.Text.Trim()
        };
    }

    private void AddWindowFromCursor(bool setAsMain)
    {
        var picked = WindowPicker.PickWindowUnderCursor(Handle, out var error);
        AddPickedWindow(picked, error, setAsMain);
    }

    private void AddWindowFromPoint(Point screenPoint, bool setAsMain)
    {
        var picked = WindowPicker.PickWindowAtPoint(Handle, screenPoint, out var error);
        AddPickedWindow(picked, error, setAsMain);
    }

    private void AddPickedWindow(WindowInfo? picked, string error, bool setAsMain)
    {
        if (picked == null)
        {
            ShowWarning(error);
            return;
        }

        var existing = _windows.FirstOrDefault(window => window.Handle == picked.Handle);
        if (existing != null)
        {
            if (setAsMain)
            {
                SetMainWindow(existing);
                RefreshWindowList();
            }
            else
            {
                ShowWarning("当前窗口已存在！");
            }

            return;
        }

        _windows.Add(picked);
        if (setAsMain)
        {
            SetMainWindow(picked);
        }

        RefreshWindowList();
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

    private void ToggleSync()
    {
        if (_isTestingModes)
        {
            ShowWarning("正在测试模式，请等待完成。");
            return;
        }

        if (_syncManager.IsRunning)
        {
            _syncManager.Stop();
            foreach (var window in _windows)
            {
                window.ResetRuntimeState();
            }

            _toggleSyncButton.Text = "开启同步";
            RefreshWindowList();
            return;
        }

        var config = BuildConfigFromControls();
        SaveConfigFromControls();

        var dmPath = GetSelectedDmDllPath();
        if (!_syncManager.Start(
                _windows.ToArray(),
                _mainWindow,
                config,
                dmPath,
                _keyboardCheckBox.Checked,
                _mouseCheckBox.Checked,
                out var error))
        {
            ShowWarning(error);
            RefreshWindowList();
            return;
        }

        _toggleSyncButton.Text = "关闭同步";
        RefreshWindowList();
    }

    private async Task TestSelectedWindowModesAsync()
    {
        if (_isTestingModes)
        {
            return;
        }

        if (_syncManager.IsRunning)
        {
            ShowWarning("请先关闭同步，再测试模式。");
            return;
        }

        var selected = GetSelectedWindow();
        if (selected == null)
        {
            ShowWarning("请先选择一个要测试的同步目标窗口。");
            return;
        }

        if (_mainWindow?.Handle == selected.Handle || selected.IsMain)
        {
            ShowWarning("主操作窗口不需要测试，请选择一个同步目标窗口。");
            return;
        }

        var dmPath = GetSelectedDmDllPath();
        if (string.IsNullOrWhiteSpace(dmPath))
        {
            ShowWarning("请先设置dm路径");
            return;
        }

        var config = BuildConfigFromControls();
        _isTestingModes = true;
        _testModesButton.Enabled = false;
        _toggleSyncButton.Enabled = false;
        _testModesButton.Text = "测试中...";

        try
        {
            var result = await TestBindModesOnStaThreadAsync(selected, config, dmPath);
            if (!result.Success || result.Report == null)
            {
                ShowWarning(result.Error);
                return;
            }

            MessageBox.Show(
                this,
                FormatBindModeTestReport(result.Report, IsMessageFallbackAvailable(selected)),
                "模式测试结果",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        finally
        {
            _isTestingModes = false;
            _testModesButton.Enabled = true;
            _toggleSyncButton.Enabled = true;
            _testModesButton.Text = "测试模式";
        }
    }

    private Task<(bool Success, BindModeTestReport? Report, string Error)> TestBindModesOnStaThreadAsync(
        WindowInfo window,
        AppConfig config,
        string dmPath)
    {
        var completion = new TaskCompletionSource<(bool Success, BindModeTestReport? Report, string Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                var success = _syncManager.TestBindModes(window, config, dmPath, out var report, out var error);
                completion.SetResult((success, report, error));
            }
            catch (Exception ex)
            {
                completion.SetResult((false, null, $"测试模式失败：{ex.Message}"));
            }
        })
        {
            IsBackground = true,
            Name = "BindModeTest"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private bool IsMessageFallbackAvailable(WindowInfo target)
    {
        return _mainWindow != null
            && _mainWindow.Handle != target.Handle
            && NativeMethods.IsWindow(_mainWindow.Handle)
            && NativeMethods.IsWindow(target.Handle)
            && (_keyboardCheckBox.Checked || _mouseCheckBox.Checked);
    }

    private static string FormatBindModeTestReport(BindModeTestReport report, bool messageFallbackAvailable)
    {
        var successes = report.Results.Where(result => result.Success).ToArray();
        var failures = report.Results.Count - successes.Length;
        var title = string.IsNullOrWhiteSpace(report.WindowTitle) ? "(无标题窗口)" : report.WindowTitle;

        var builder = new StringBuilder();
        builder.AppendLine($"窗口：0x{report.WindowHandle.ToInt64():X}  {title}");
        builder.AppendLine($"dm：{report.DmCreationMode}");
        builder.AppendLine($"初始化：{report.InitInfo}");
        builder.AppendLine($"dm测试组合：{report.Results.Count}，dm可用：{successes.Length}，失败：{failures}");
        builder.AppendLine();

        if (successes.Length == 0)
        {
            builder.AppendLine("没有找到可用的 dm 后台绑定组合。");
        }
        else
        {
            builder.AppendLine("可用 dm 组合：");
            for (var i = 0; i < successes.Length; i++)
            {
                var result = successes[i];
                var testCase = result.TestCase;
                builder.AppendLine(
                    $"{i + 1}. display={testCase.Display}, mouse={testCase.Mouse}, keypad={testCase.Keypad}, " +
                    $"public={FormatPublicMode(testCase.Public)}, mode={testCase.Mode} " +
                    $"[{testCase.ApiName}, {result.ElapsedMs}ms]");
            }
        }

        if (messageFallbackAvailable)
        {
            builder.AppendLine();
            builder.AppendLine("可用同步方式：");
            builder.AppendLine("- Windows 消息同步兜底：可用。正式同步在 dm 绑定失败时仍会使用它转发键鼠消息。");
        }

        if (failures > 0)
        {
            builder.AppendLine();
            builder.AppendLine("失败摘要：");
            foreach (var group in report.Results
                         .Where(result => !result.Success)
                         .GroupBy(DescribeFailure)
                         .OrderByDescending(group => group.Count())
                         .ThenBy(group => group.Key, StringComparer.Ordinal)
                         .Take(8))
            {
                builder.AppendLine($"- {group.Key}：{group.Count()} 个组合");
            }
        }

        builder.AppendLine();
        builder.AppendLine("当前配置不会自动修改，请按可用组合手动调整绑定配置。");
        return builder.ToString();
    }

    private static string DescribeFailure(BindModeTestResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return $"异常={result.Error}";
        }

        return $"返回={result.Result}, LastError={FormatLastError(result.LastError)}";
    }

    private static string FormatPublicMode(string publicMode)
    {
        return string.IsNullOrWhiteSpace(publicMode) ? "(空)" : publicMode;
    }

    private static string FormatLastError(int lastError)
    {
        return lastError == int.MinValue ? "读取失败" : lastError.ToString();
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

    private void ToggleSyncFromShortcut()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        ToggleSync();
    }

    private void RegisterSelectedDm()
    {
        var dmPath = GetSelectedDmDllPath();
        if (string.IsNullOrWhiteSpace(dmPath))
        {
            ShowWarning("请先设置dm路径");
            return;
        }

        var ok = DmPlugin.RegisterDll(dmPath, elevated: true, out var message);
        MessageBox.Show(this, message, ok ? "注册dm" : "注册dm失败", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
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

    private string GetSelectedDmDllPath()
    {
        return _dmComboBox.SelectedItem is DmDllChoice choice ? choice.Path : string.Empty;
    }

    private void SelectDm(string value)
    {
        if (int.TryParse(value, out var index) && index >= 0 && index < _dmComboBox.Items.Count)
        {
            _dmComboBox.SelectedIndex = index;
            return;
        }

        for (var i = 0; i < _dmComboBox.Items.Count; i++)
        {
            if (_dmComboBox.Items[i] is DmDllChoice choice
                && (string.Equals(choice.Path, value, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(choice.FileName, value, StringComparison.OrdinalIgnoreCase)))
            {
                _dmComboBox.SelectedIndex = i;
                return;
            }
        }

        if (_dmComboBox.Items.Count > 0)
        {
            _dmComboBox.SelectedIndex = 0;
        }
    }

    private static void AddLabel(TableLayoutPanel panel, string text, int column, int row)
    {
        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleRight
        }, column, row);
    }

    private static string NormalizeInputMode(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Trim() == "0" ? "windows" : value.Trim();
    }

    private static string NormalizeDisplayMode(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Trim() == "0" ? "normal" : value.Trim();
    }

    private static IEnumerable<string> FindDmDlls()
    {
        var directories = new[]
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName
        }
        .Where(directory => !string.IsNullOrWhiteSpace(directory))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        return directories
            .SelectMany(directory => Directory.Exists(directory!) ? Directory.GetFiles(directory!, "dm*.dll") : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ShowWarning(string message)
    {
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private sealed class DmDllChoice
    {
        public DmDllChoice(string path)
        {
            Path = path;
            FileName = System.IO.Path.GetFileName(path);
        }

        public string Path { get; }

        public string FileName { get; }

        public override string ToString()
        {
            return FileName;
        }
    }
}
