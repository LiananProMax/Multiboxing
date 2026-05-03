using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Wpf.Ui.Controls;

namespace KeyMouseSyncReplica;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    internal MainWindow(NotificationService notifications)
    {
        InitializeComponent();

        notifications.SetActivationWindow(this);
        _viewModel = new MainViewModel(
            notifications,
            this,
            () => new WindowInteropHelper(this).Handle);
        DataContext = _viewModel;

        SourceInitialized += HandleSourceInitialized;
    }

    protected override void OnClosed(EventArgs e)
    {
        SourceInitialized -= HandleSourceInitialized;
        _viewModel.Dispose();

        base.OnClosed(e);
    }

    private void HandleSourceInitialized(object? sender, EventArgs e)
    {
        _viewModel.StartSideButtonHook();
    }

    private void TargetPicker_PickCompleted(object? sender, TargetPickedEventArgs e)
    {
        _viewModel.AddWindowFromPoint(e.ScreenPoint);
    }

    private void WindowsDataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && !row.IsSelected)
        {
            row.IsSelected = true;
        }
    }
}
