using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using System.Windows.Input;

namespace Multiboxing.Views;

public partial class SyncPage : UserControl
{
    public SyncPage()
    {
        InitializeComponent();
    }

    private void TargetPicker_PickCompleted(object? sender, TargetPickedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.AddWindowFromPoint(e.ScreenPoint);
    }

    private void WindowsListBoxItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && !item.IsSelected) item.IsSelected = true;
    }
}
