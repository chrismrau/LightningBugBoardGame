using Microsoft.Extensions.Hosting;

namespace Firefly.UI.Services;

/// <summary>
/// Locates the Lightning Bug repo root (Firefly.sln) so we can serve
/// <c>reference/board</c> art and resolve Data paths outside bin/.
/// </summary>
public sealed class RepoPaths
{
    public string Root { get; }
    public string BoardArtDirectory { get; }
    public string SectorLayoutPath { get; }
    public string SectorsPath { get; }

    public RepoPaths(IHostEnvironment env)
    {
        Root = FindRepoRoot(env.ContentRootPath)
            ?? FindRepoRoot(AppContext.BaseDirectory)
            ?? env.ContentRootPath;

        BoardArtDirectory = Path.Combine(Root, "reference", "board");
        SectorLayoutPath = FirstExisting(
            Path.Combine(AppContext.BaseDirectory, "Data", "Map", "SectorLayout.json"),
            Path.Combine(Root, "Data", "Map", "SectorLayout.json"),
            Path.Combine(Root, "data", "Map", "SectorLayout.json"))
            ?? Path.Combine(Root, "Data", "Map", "SectorLayout.json");
        SectorsPath = FirstExisting(
            Path.Combine(AppContext.BaseDirectory, "Data", "Map", "Sectors.json"),
            Path.Combine(Root, "Data", "Map", "Sectors.json"),
            Path.Combine(Root, "data", "Map", "Sectors.json"))
            ?? Path.Combine(Root, "Data", "Map", "Sectors.json");
    }

    public bool BoardArtAvailable =>
        File.Exists(Path.Combine(BoardArtDirectory, "GameBoardMarkup.png"));

    private static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Firefly.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? FirstExisting(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}
