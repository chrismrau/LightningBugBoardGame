using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class DriveCoreCard
    {
        public string Id { get; }
        public string Name { get; }
        public int Range { get; }
        public bool RequiresFuel { get; }
        public bool Locked { get; }
        public int MoseyRange { get; }
        public string? Description { get; }

        public DriveCoreCard(string id, string name, int range, bool requiresFuel, bool locked, int moseyRange, string? description)
        {
            Id = id;
            Name = name;
            Range = range > 0 ? range : 5;
            RequiresFuel = requiresFuel;
            Locked = locked;
            MoseyRange = moseyRange > 0 ? moseyRange : 1;
            Description = description;
        }
    }

    public sealed class DriveCoreCatalog
    {
        private static readonly Regex RangeRx = new Regex(@"Range\s*:?\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MoseyRx = new Regex(@"Mosey up to (\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly Dictionary<string, DriveCoreCard> _byId;
        private readonly Dictionary<string, DriveCoreCard> _byName;
        public IReadOnlyDictionary<string, DriveCoreCard> Cards => _byId;

        public DriveCoreCatalog(IEnumerable<DriveCoreCard> cores)
        {
            _byId = new Dictionary<string, DriveCoreCard>(StringComparer.Ordinal);
            _byName = new Dictionary<string, DriveCoreCard>(StringComparer.OrdinalIgnoreCase);
            foreach (var core in cores)
            {
                _byId[core.Id] = core;
                _byName[core.Name] = core;
            }
        }

        public bool TryGet(string id, out DriveCoreCard core) => _byId.TryGetValue(id, out core!);
        public DriveCoreCard? FindByName(string name) =>
            string.IsNullOrWhiteSpace(name) ? null : (_byName.TryGetValue(name.Trim(), out var core) ? core : null);
        public bool TryResolve(string idOrName, out DriveCoreCard core)
        {
            if (TryGet(idOrName, out core)) return true;
            core = FindByName(idOrName)!;
            return core != null;
        }

        public static DriveCoreCatalog LoadDefault() => LoadFromFile(GameData.DriveCoresPath);

        public static DriveCoreCatalog LoadFromFile(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<DriveCoreCard>();
            foreach (var el in doc.RootElement.GetProperty("driveCores").EnumerateArray())
            {
                var name = el.GetProperty("name").GetString() ?? "";
                var text = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var range = 5;
                var rm = RangeRx.Match(text);
                if (rm.Success) range = int.Parse(rm.Groups[1].Value);
                var mosey = 1;
                var mm = MoseyRx.Match(text);
                if (mm.Success) mosey = int.Parse(mm.Groups[1].Value);
                var requiresFuel = text.IndexOf("no fuel", StringComparison.OrdinalIgnoreCase) < 0;
                var locked = text.IndexOf("cannot be replaced", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("may not be replaced", StringComparison.OrdinalIgnoreCase) >= 0;
                list.Add(new DriveCoreCard(el.GetProperty("id").GetString() ?? "", name, range, requiresFuel, locked, mosey, text));
            }
            return new DriveCoreCatalog(list);
        }
    }
}
