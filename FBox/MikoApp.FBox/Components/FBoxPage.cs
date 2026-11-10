using Miko.Components;
using MikoApp.FBox.Services;

namespace MikoApp.FBox.Components;

public abstract class FBoxPage : ComponentBase, IDisposable
{
    [Inject] public FBoxStore Data { get; set; } = null!;
    private SynchronizationContext? _context;
    protected override void OnInitialized() { _context = SynchronizationContext.Current; Data.Changed += Redraw; }
    private void Redraw()
    {
        if (_context is not null && SynchronizationContext.Current != _context) _context.Post(_ => StateHasChanged(), null);
        else StateHasChanged();
    }
    protected void Go(string route) => NavigationManager?.NavigateTo(route);
    protected Task<bool> Run(Func<Task> action, string? message = null) => Data.RunAsync(action, message);
    public virtual void Dispose() { Data.Changed -= Redraw; }
    protected override void OnDispose() => Dispose();
}
