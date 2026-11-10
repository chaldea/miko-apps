// The Chaldea licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace MikoApp.Anime.Models;

public sealed record AnimeTitle(
    int Id,
    string Title,
    string Kind,
    string Genre,
    int Year,
    int Season,
    int Weekday,
    int Episodes,
    double Rating,
    string Description,
    string Poster,
    string UpdateTime)
{
    public bool Preview { get; init; }
    public string Remarks { get; init; } = "";
    public string Author { get; init; } = "";
    public string PosterSource => Poster.Contains("://") || Poster.StartsWith("data:") ? Poster : $"res://Assets/{Poster}";
    public string ThumbnailSource => Preview ? $"res://Assets/thumbnail-{Id}.jpg" : PosterSource;
    public string Status => string.IsNullOrEmpty(Remarks) ? $"全 {Episodes} 集" : Remarks;
}

public sealed record FeaturedAnime(int AnimeId, string ImageSource);
public sealed record UserProfile(string Nickname, string Email, string Avatar, bool SignedIn = true);
public sealed record WatchRecord(int AnimeId, int Episode, DateTimeOffset WatchedAt);
public sealed record AnimeComment(string Author, string Text, string Avatar, string TimeLabel);
public sealed record Reward(string Id, string Title, int Cost, string Description);
public sealed record Redemption(string Title, int Cost, DateTimeOffset CreatedAt);
public sealed record AnimeCategory(int Id, string Name, string[] Genres, string[] Years, IReadOnlyList<AnimeTitle> Titles);
public sealed record Episode(string Title, string ResourceId, bool GuestAllowed);
public sealed record PlayLine(string Player, string Name, IReadOnlyList<Episode> Episodes);
public sealed record AnimeDetail(AnimeTitle Title, IReadOnlyList<PlayLine> Lines);
public sealed record RankGroup(string Name, IReadOnlyList<AnimeTitle> Titles);
