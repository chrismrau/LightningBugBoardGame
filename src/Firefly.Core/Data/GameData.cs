using System;
using System.IO;
using System.Reflection;

namespace Firefly.Core.Data
{
    public static class GameData
    {
        public static string Root => _root.Value;
        public static string MapDirectory => FirstExisting(Path.Combine(Root, "Map"), Path.Combine(Root, "map"));
        public static string CardsDirectory => FirstExisting(Path.Combine(Root, "Cards"), Path.Combine(Root, "cards"));
        public static string NavCardsPath => Path.Combine(CardsDirectory, "NavCards.json");
        public static string CrewPath => Path.Combine(CardsDirectory, "Crew.json");

        private static readonly Lazy<string> _root = new Lazy<string>(FindRoot);

        private static string FindRoot()
        {
            foreach (var start in CandidateStarts())
            {
                var dir = start;
                for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
                {
                    var hit = ExistingDataFolder(dir);
                    if (hit != null)
                        return hit;
                    dir = Directory.GetParent(dir)?.FullName;
                }
            }

            throw new DirectoryNotFoundException(
                "Could not find game Data folder (expected Data/Map and Data/Cards JSON).");
        }

        private static string[] CandidateStarts()
        {
            var assemblyDir = Path.GetDirectoryName(typeof(GameData).GetTypeInfo().Assembly.Location);
            return new[]
            {
                assemblyDir ?? "",
                AppContext.BaseDirectory ?? "",
                Directory.GetCurrentDirectory()
            };
        }

        private static string? ExistingDataFolder(string dir)
        {
            foreach (var name in new[] { "Data", "data" })
            {
                var candidate = Path.Combine(dir, name);
                if (LooksLikeGameData(candidate))
                    return Path.GetFullPath(candidate);
            }
            return LooksLikeGameData(dir) ? Path.GetFullPath(dir) : null;
        }

        private static bool LooksLikeGameData(string dir)
        {
            if (!Directory.Exists(dir))
                return false;
            return File.Exists(Path.Combine(dir, "Map", "Sectors.json"))
                || File.Exists(Path.Combine(dir, "map", "Sectors.json"));
        }

        private static string FirstExisting(params string[] paths)
        {
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                    return path;
            }
            return paths[0];
        }
    }
}
