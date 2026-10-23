using System.Windows;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using System.Windows.Controls;
using System.Windows.Media;
using EveOPreview.View;

namespace Multiboxing.Controls;

public partial class AnchorPicker : UserControl
{
    private readonly Dictionary<ViewZoomAnchor, Button> _buttons = new();
    private static readonly ViewZoomAnchor[,] Layout = new[,]
    {
        { ViewZoomAnchor.NW, ViewZoomAnchor.N, ViewZoomAnchor.NE },
        { ViewZoomAnchor.W, ViewZoomAnchor.C, ViewZoomAnchor.E },
        { ViewZoomAnchor.SW, ViewZoomAnchor.S, ViewZoomAnchor.SE },
    };

    public static readonly DependencyProperty SelectedAnchorProperty = DependencyProperty.Register(
        nameof(SelectedAnchor), typeof(ViewZoomAnchor), typeof(AnchorPicker),
        new FrameworkPropertyMetadata(ViewZoomAnchor.NW, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnAnchorChanged));

    public AnchorPicker()
    {
        InitializeComponent();
        for (int r = 0; r < 3; r++)
        {
            RootGrid.RowDefinitions.Add(new RowDefinition());
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int c = 0; c < 3; c++)
            {
                var anchor = Layout[r, c];
                var button = new Button
                {
                    Tag = anchor,
                    Margin = new Thickness(2),
                    Padding = new Thickness(0),
                    Content = "●",
                    FontSize = 9,
                    ToolTip = anchor.ToString(),
                };
                button.Click += (_, _) => SelectedAnchor = anchor;
                Grid.SetRow(button, r);
                Grid.SetColumn(button, c);
                RootGrid.Children.Add(button);
                _buttons[anchor] = button;
            }
        }
        RefreshSelection();
    }

    public ViewZoomAnchor SelectedAnchor
    {
        get => (ViewZoomAnchor)GetValue(SelectedAnchorProperty);
        set => SetValue(SelectedAnchorProperty, value);
    }

    private static void OnAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((AnchorPicker)d).RefreshSelection();

    private void RefreshSelection()
    {
        foreach (var (anchor, button) in _buttons)
        {
            bool selected = anchor == SelectedAnchor;
            button.Background = selected
                ? (System.Windows.Media.Brush)FindResource("PrimaryFillBrush")
                : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            button.Foreground = selected ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("MutedTextBrush");
            button.BorderBrush = selected
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        }
    }
}
