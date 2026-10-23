using System.Windows;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using System.Windows.Controls;
using System.Windows.Media;

namespace Multiboxing.Controls;

public partial class ColorPickerButton : UserControl
{
    private static readonly string[] Presets =
    {
        "#7DD3FC", "#4ADE80", "#FBBF24", "#F472B6", "#818CF8", "#F87171",
        "#F1F5F9", "#A9B6CC", "#38BDF8", "#22C55E", "#EAB308", "#A855F7",
        "#EF4444", "#F97316", "#14B8A6", "#3B82F6", "#111827", "#800080",
    };

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorPickerButton),
        new FrameworkPropertyMetadata(Colors.SkyBlue, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

    public ColorPickerButton()
    {
        InitializeComponent();
        foreach (var hex in Presets)
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var button = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                ToolTip = hex,
            };
            button.Click += (_, _) => { SelectedColor = color; PickerPopup.IsOpen = false; };
            PresetGrid.Children.Add(button);
        }
        RefreshSwatch();
    }

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ColorPickerButton)d).RefreshSwatch();

    private void RefreshSwatch()
    {
        if (Swatch == null) return;
        Swatch.Background = new SolidColorBrush(SelectedColor);
        var text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
        HexLabel.Text = text;
        HexBox.Text = text;
    }

    private void SwatchButton_Click(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = true;

    private void ApplyHex_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SelectedColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(HexBox.Text.Trim());
            PickerPopup.IsOpen = false;
        }
        catch
        {
            HexBox.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
        }
    }
}
