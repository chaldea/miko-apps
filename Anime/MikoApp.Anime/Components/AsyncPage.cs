using Miko.Components;
using MikoApp.Anime.Services;

namespace MikoApp.Anime.Components;

public abstract class AsyncPage : ComponentBase
{
    protected CancellationTokenSource Lifetime { get; } = new();
    protected bool Busy { get; private set; }
    protected string? Error { get; set; }
    protected async Task Run(Func<Task> action)
    {
        if (Busy || Lifetime.IsCancellationRequested) return;
        Busy = true; Error = null; StateHasChanged();
        try { await action(); }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Error = AnimeStore.Error(ex); }
        finally { Busy = false; if (!Lifetime.IsCancellationRequested) StateHasChanged(); }
    }
    protected override void OnDispose() { Lifetime.Cancel(); base.OnDispose(); }
}
