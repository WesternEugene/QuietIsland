using System.Windows.Controls;
using QuietIsland.Services;

namespace QuietIsland.Views;
public abstract class IslandView(IslandContext context) : UserControl, IDisposable
{
    protected IslandContext C { get; } = context;
    protected bool Disposed { get; private set; }
    public virtual void Refresh() { }
    public virtual void FocusInput() { }
    public virtual void Dispose() { Disposed = true; }
}
