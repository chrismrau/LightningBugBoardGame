using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Abilities;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    /// <summary>Typed abilities for installed ship upgrades (JSON <c>abilities</c>).</summary>
    public sealed class ShipUpgradeEntry
    {
        public string Id { get; }
        public string Name { get; }
        public string? Description { get; }
        public IReadOnlyList<AbilityDefinition> Abilities { get; }

        public ShipUpgradeEntry(
            string id,
            string name,
            string? description = null,
            IReadOnlyList<AbilityDefinition>? abilities = null)
        {
            Id = id;
            Name = name;
            Description = description;
            Abilities = abilities ?? AbilityDefinition.Empty;
        }
    }

    public sealed class ShipUpgradeIndex
    {
        private readonly Dictionary<string, ShipUpgradeEntry> _byId;

        public IReadOnlyDictionary<string, ShipUpgradeEntry> Items => _byId;

        public ShipUpgradeIndex(IEnumerable<ShipUpgradeEntry> items)
        {
            _byId = new Dictionary<string, ShipUpgradeEntry>(StringComparer.Ordinal);
            foreach (var item in items)
                _byId[item.Id] = item;
        }

        public bool TryGet(string id, out ShipUpgradeEntry entry) =>
            _byId.TryGetValue(id, out entry!);

        public static ShipUpgradeIndex LoadDefault()
        {
            var path = GameData.ShipUpgradesPath;
            var items = new List<ShipUpgradeEntry>();
            if (!File.Exists(path))
                return new ShipUpgradeIndex(items);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("shipUpgrades", out var array))
                return new ShipUpgradeIndex(items);
            foreach (var node in array.EnumerateArray())
            {
                var id = node.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? "" : "";
                var name = node.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var description = node.TryGetProperty("description", out var desc)
                    ? desc.GetString()
                    : null;
                items.Add(new ShipUpgradeEntry(id, name, description, AbilityJson.Read(node)));
            }
            return new ShipUpgradeIndex(items);
        }
    }
}
