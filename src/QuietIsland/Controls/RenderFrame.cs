using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuietIsland.Controls;

public sealed class RenderFrame : Grid
{
    internal int MeasurePasses { get; private set; }
    internal int ArrangePasses { get; private set; }
    protected override Size MeasureOverride(Size available) { MeasurePasses++; return base.MeasureOverride(available); }
    protected override Size ArrangeOverride(Size finalSize) { ArrangePasses++; return base.ArrangeOverride(finalSize); }
}
