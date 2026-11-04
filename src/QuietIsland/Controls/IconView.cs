using System.Windows;
using System.Windows.Controls;

namespace QuietIsland.Controls;

public sealed class IconView : ContentControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(IconView), new PropertyMetadata("chevron-down", (d, _) => ((IconView)d).Update()));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IconView() { IsHitTestVisible = false; Focusable = false; Update(); }
    private void Update() => Content = Icons.Make(Kind, 14, "Muted");
}
