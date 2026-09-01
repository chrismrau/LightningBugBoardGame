using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class SupplyCatalog
    {
        private readonly Dictionary<string, SupplyCard> _byId;

        public IReadOnlyDictionary<string, SupplyCard> Cards => _byId;

        public SupplyCatalog(IEnumerable<SupplyCard> cards)
        {
            _byId = new Dictionary<string, SupplyCard>(StringComparer.Ordinal);
            foreach (var card in cards)
                _byId[card.Id] = card;
        }

        public bool TryGet(string id, out SupplyCard card) => _byId.TryGetValue(id, out card!);

        public IReadOnlyList<SupplyCard> ForPlanet(string planet)
        {
            var list = new List<SupplyCard>();
            foreach (var card in _byId.Values)
            {
                if (card.CopiesAt(planet) > 0)
                    list.Add(card);
            }
            return list;
        }

        public static SupplyCatalog LoadDefault()
        {
            var cards = new List<SupplyCard>();
            LoadFile(GameData.GearPath, "gear", SupplyKind.Gear, cards);
            LoadFile(GameData.CrewPath, "crew", SupplyKind.Crew, cards);
            LoadFile(GameData.ShipUpgradesPath, "shipUpgrades", SupplyKind.ShipUpgrade, cards);
            LoadFile(GameData.DriveCoresPath, "driveCores", SupplyKind.DriveCore, cards);
            return new SupplyCatalog(cards);
        }

        private static void LoadFile(string path, string arrayName, SupplyKind kind, List<SupplyCard> into)
        {
            if (!File.Exists(path))
                return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
                return;
            foreach (var item in array.EnumerateArray())
            {
                var id = GetString(item, "id");
                var name = GetString(item, "name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;
                if (!TryGetCost(item, out var cost))
                    continue;
                var copies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (item.TryGetProperty("locations", out var locations) && locations.ValueKind == JsonValueKind.Object)
                {
                    foreach (var loc in locations.EnumerateObject())
                    {
                        if (loc.Value.ValueKind == JsonValueKind.Number && loc.Value.TryGetInt32(out var n) && n > 0)
                            copies[loc.Name] = n;
                    }
                }
                into.Add(new SupplyCard(id, name, cost, kind, copies));
            }
        }

        private static bool TryGetCost(JsonElement item, out int cost)
        {
            cost = 0;
            if (!item.TryGetProperty("cost", out var node) || node.ValueKind == JsonValueKind.Null)
                return false;
            if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out cost))
                return cost >= 0;
            return false;
        }

        private static string GetString(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String)
                return "";
            return node.GetString() ?? "";
        }
    }
}
