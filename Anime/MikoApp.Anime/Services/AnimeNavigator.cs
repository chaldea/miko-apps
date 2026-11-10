using Miko.Routing;

namespace MikoApp.Anime.Services;

public sealed class AnimeNavigator(NavigationManager navigation)
{
    public int SelectedId { get; private set; } = 1;
    public event Action? SelectionChanged;
    public void Open(int id)
    {
        SelectedId = id;
        if (navigation.CurrentPath == "/detail") SelectionChanged?.Invoke();
        else navigation.NavigateTo("/detail");
    }
}
