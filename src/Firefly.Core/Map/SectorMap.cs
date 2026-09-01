using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Firefly.Core.Map
{
    public sealed class SectorMap
    {
        private readonly Dictionary<string, Sector> _byId;
        private readonly Dictionary<string, HashSet<string>> _neighbors;
        private readonly Dictionary<string, HashSet<string>> _destinationToSectors;
        private readonly Dictionary<string, string> _nameToId;

        public IReadOnlyDictionary<string, Sector> Sectors => _byId;
        public int SectorCount => _byId.Count;
        public int EdgeCount { get; }

        public SectorMap(
            IEnumerable<Sector> sectors,
            IEnumerable<(string A, string B)> edges,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? destinationIndex = null)
        {
            _byId = sectors.ToDictionary(s => s.Id, StringComparer.Ordinal);
            _neighbors = _byId.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal));

            var edgeCount = 0;
            foreach (var pair in edges)
            {
                var a = SectorIds.Canonical(pair.A);
                var b = SectorIds.Canonical(pair.B);
                if (!_byId.ContainsKey(a) || !_byId.ContainsKey(b) || a == b)
                    continue;
                if (_neighbors[a].Add(b))
                    _neighbors[b].Add(a);
                edgeCount++;
            }
            EdgeCount = edgeCount;

            _destinationToSectors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (destinationIndex != null)
            {
                foreach (var kv in destinationIndex)
                    _destinationToSectors[kv.Key] = new HashSet<string>(kv.Value.Select(SectorIds.Canonical), StringComparer.Ordinal);
            }

            foreach (var sector in _byId.Values)
            {
                foreach (var region in sector.DestinationRegions)
                {
                    if (!_destinationToSectors.TryGetValue(region, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        _destinationToSectors[region] = set;
                    }
                    set.Add(sector.Id);
                }
            }

            _nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sector in _byId.Values)
            {
                void MapName(string? name)
                {
                    if (string.IsNullOrWhiteSpace(name)) return;
                    if (!_nameToId.ContainsKey(name))
                        _nameToId[name] = sector.Id;
                }
                MapName(sector.Planet);
                MapName(sector.DisplayName);
                MapName(sector.Id);
                if (sector.Id.IndexOf("qin-shi-huang", StringComparison.Ordinal) >= 0)
                    MapName(sector.Id.Replace("qin-shi-huang", "quin-shi-huang"));
                foreach (var alias in sector.Aliases)
                    MapName(alias);
            }
        }

        public static SectorMap LoadFromDirectory(string mapDirectory)
        {
            return LoadFromFiles(Path.Combine(mapDirectory, "Sectors.json"), Path.Combine(mapDirectory, "Adjacency.json"));
        }

        public static SectorMap LoadFromFiles(string sectorsPath, string adjacencyPath)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var sectorsDoc = JsonSerializer.Deserialize<SectorsFile>(File.ReadAllText(sectorsPath), options)
                ?? throw new InvalidDataException("Sectors.json did not deserialize.");
            var adjDoc = JsonSerializer.Deserialize<AdjacencyFile>(File.ReadAllText(adjacencyPath), options)
                ?? throw new InvalidDataException("Adjacency.json did not deserialize.");
            var sectors = (sectorsDoc.Sectors ?? new List<SectorDto>()).Select(dto => dto.ToSector()).ToList();
            var edges = (adjDoc.Edges ?? new List<EdgeDto>()).Select(e => (SectorIds.Canonical(e.A), SectorIds.Canonical(e.B)));
            Dictionary<string, IReadOnlyList<string>>? dest = null;
            if (sectorsDoc.Meta?.DestinationRegions != null)
            {
                dest = sectorsDoc.Meta.DestinationRegions.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<string>)(kv.Value.SectorIds ?? new List<string>()).Select(SectorIds.Canonical).ToList(),
                    StringComparer.OrdinalIgnoreCase);
            }
            return new SectorMap(sectors, edges, dest);
        }

        public Sector Get(string sectorId)
        {
            sectorId = SectorIds.Canonical(sectorId);
            if (!_byId.TryGetValue(sectorId, out var sector))
                throw new KeyNotFoundException($"Unknown sector '{sectorId}'.");
            return sector;
        }

        public bool TryGet(string sectorId, out Sector sector) =>
            _byId.TryGetValue(SectorIds.Canonical(sectorId), out sector!);

        public bool TryResolveName(string nameOrId, out Sector sector)
        {
            sector = null!;
            nameOrId = SectorIds.Canonical(nameOrId);
            if (_byId.TryGetValue(nameOrId, out sector!)) return true;
            if (_nameToId.TryGetValue(nameOrId, out var id) && _byId.TryGetValue(id, out sector!)) return true;
            return false;
        }

        public IReadOnlyCollection<string> Neighbors(string sectorId)
        {
            sectorId = SectorIds.Canonical(sectorId);
            if (!_neighbors.TryGetValue(sectorId, out var set))
                throw new KeyNotFoundException($"Unknown sector '{sectorId}'.");
            return set;
        }

        public NavRegion NavDeckFor(string sectorId) => Get(sectorId).NavRegion;

        public bool SatisfiesDestination(string sectorId, string destination)
        {
            sectorId = SectorIds.Canonical(sectorId);
            if (!_byId.ContainsKey(sectorId)) return false;
            if (_destinationToSectors.TryGetValue(destination, out var ids) && ids.Contains(sectorId)) return true;
            if (TryResolveName(destination, out var named) && named.Id == sectorId) return true;
            var sector = _byId[sectorId];
            if (string.Equals(sector.Planet, destination, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(sector.DisplayName, destination, StringComparison.OrdinalIgnoreCase)) return true;
            if (sector.Aliases.Any(a => a.Equals(destination, StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }

        public IReadOnlyCollection<string> SectorsForDestination(string destination)
        {
            if (_destinationToSectors.TryGetValue(destination, out var ids)) return ids;
            if (TryResolveName(destination, out var named)) return new[] { named.Id };
            return Array.Empty<string>();
        }

        private sealed class SectorsFile
        {
            public SectorsMeta? Meta { get; set; }
            public List<SectorDto>? Sectors { get; set; }
        }

        private sealed class SectorsMeta
        {
            public Dictionary<string, DestinationRegionDto>? DestinationRegions { get; set; }
        }

        private sealed class DestinationRegionDto
        {
            public List<string>? SectorIds { get; set; }
        }

        private sealed class SectorDto
        {
            public string Id { get; set; } = "";
            public string Region { get; set; } = "";
            public string Zone { get; set; } = "";
            public int Ring { get; set; }
            public int Index { get; set; }
            public string? Planet { get; set; }
            public string? Contact { get; set; }
            public string? DisplayName { get; set; }
            public bool HasSupplyDeck { get; set; }
            public bool IsPlanetary { get; set; }
            public bool IsRelay { get; set; }
            public List<string>? Aliases { get; set; }
            public List<string>? DestinationRegions { get; set; }
            public Sector ToSector() => new Sector(Id, Region, Zone, Ring, Index, Planet, Contact, DisplayName, HasSupplyDeck, IsPlanetary, IsRelay, Aliases, DestinationRegions);
        }

        private sealed class AdjacencyFile { public List<EdgeDto>? Edges { get; set; } }
        private sealed class EdgeDto { public string A { get; set; } = ""; public string B { get; set; } = ""; }
    }
}
