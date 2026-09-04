using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class ShipCatalog
    {
        private readonly Dictionary<string, ShipCard> _byId;
        private readonly Dictionary<string, ShipCard> _byName;

        public IReadOnlyDictionary<string, ShipCard> Cards => _byId;

        public ShipCatalog(IEnumerable<ShipCard> ships)
        {
            _byId = new Dictionary<string, ShipCard>(StringComparer.Ordinal);
            _byName = new Dictionary<string, ShipCard>(StringComparer.OrdinalIgnoreCase);
            foreach (var ship in ships)
            {
                _byId[ship.Id] = ship;
                _byName[ship.Name] = ship;
            }
        }

        public ShipCard Get(string id) => _byId[id];

        public bool TryGet(string id, out ShipCard ship) => _byId.TryGetValue(id, out ship!);

        public ShipCard? FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            return _byName.TryGetValue(name.Trim(), out var ship) ? ship : null;
        }

        public bool TryResolve(string idOrName, out ShipCard ship)
        {
            if (TryGet(idOrName, out ship))
                return true;
            ship = FindByName(idOrName)!;
            return ship != null;
        }

        public IReadOnlyList<ShipCard> CoreStartingShips()
        {
            var list = new List<ShipCard>();
            foreach (var ship in _byId.Values)
            {
                if (ship.IsCoreFirefly)
                    list.Add(ship);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static ShipCatalog LoadDefault() => LoadFromFile(GameData.ShipsPath);

        public static ShipCatalog LoadFromFile(string path)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<ShipFile>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("Ships.json did not deserialize.");

            var cards = new List<ShipCard>();
            foreach (var dto in file.Ships ?? new List<ShipDto>())
            {
                cards.Add(new ShipCard(
                    dto.Id,
                    dto.Name,
                    dto.Source ?? "",
                    dto.ShipClass ?? "",
                    dto.MainDrive ?? "",
                    dto.Cost ?? 0,
                    dto.CanUpgradeDrive,
                    dto.CargoHolds ?? 8,
                    dto.Stash ?? 4,
                    dto.FuelStash ?? 0,
                    dto.MaxCrew ?? 6,
                    dto.UpgradeSlots ?? 3,
                    dto.SpecialRules));
            }
            return new ShipCatalog(cards);
        }

        private sealed class ShipFile
        {
            public List<ShipDto>? Ships { get; set; }
        }

        private sealed class ShipDto
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string? Source { get; set; }
            public string? ShipClass { get; set; }
            public string? MainDrive { get; set; }
            public int? Cost { get; set; }
            public bool CanUpgradeDrive { get; set; }
            public int? CargoHolds { get; set; }
            public int? Stash { get; set; }
            public int? FuelStash { get; set; }
            public int? MaxCrew { get; set; }
            public int? UpgradeSlots { get; set; }
            public string? SpecialRules { get; set; }
        }
    }
}
