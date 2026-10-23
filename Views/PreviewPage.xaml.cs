using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using System.Windows.Media;
using EveOPreview.View;

namespace Multiboxing.Views;

public partial class PreviewPage : UserControl
{
    public PreviewPage()
    {
        InitializeComponent();
        AnimationStyleCombo.ItemsSource = Enum.GetValues(typeof(ViewAnimationStyle));
        FontFamilyCombo.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(x => x).ToList();
    }
}
