using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Abilities;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class LeaderCatalog
    {
        private readonly Dictionary<string, CrewCard> _byId;
        private readonly Dictionary<string, List<CrewCard>> _byName;

        public IReadOnlyDictionary<string, CrewCard> Cards => _byId;

        public LeaderCatalog(IEnumerable<CrewCard> leaders)
        {
            _byId = new Dictionary<string, CrewCard>(StringComparer.Ordinal);
            _byName = new Dictionary<string, List<CrewCard>>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in leaders)
            {
                _byId[card.Id] = card;
                if (!_byName.TryGetValue(card.Name, out var list))
                {
                    list = new List<CrewCard>();
                    _byName[card.Name] = list;
                }
                list.Add(card);
            }
        }

        public CrewCard Get(string id) => _byId[id];

        public bool TryGet(string id, out CrewCard card) => _byId.TryGetValue(id, out card!);

        public CrewCard? FindByName(string name)
        {
            if (!_byName.TryGetValue(name, out var list) || list.Count == 0)
                return null;
            foreach (var card in list)
            {
                if (!card.Id.EndsWith("_promo", StringComparison.OrdinalIgnoreCase))
                    return card;
            }
            return list[0];
        }

        public bool TryResolve(string idOrName, out CrewCard card)
        {
            if (TryGet(idOrName, out card))
                return true;
            card = FindByName(idOrName)!;
            return card != null;
        }

        public static LeaderCatalog LoadDefault() => LoadFromFile(GameData.LeadersPath);

        public static LeaderCatalog LoadFromFile(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("leaders", out var array))
                throw new InvalidDataException("Leaders.json did not deserialize.");

            var cards = new List<CrewCard>();
            foreach (var node in array.EnumerateArray())
            {
                var id = node.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? "" : "";
                var name = node.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var skills = node.TryGetProperty("skills", out var sk) ? sk : default;
                var cost = 0;
                if (node.TryGetProperty("cost", out var costNode)
                    && costNode.ValueKind == JsonValueKind.Number
                    && costNode.TryGetInt32(out var c))
                    cost = c;
                cards.Add(new CrewCard(
                    id,
                    name,
                    AbilityJson.ReadSkill(skills, "fight"),
                    AbilityJson.ReadSkill(skills, "tech"),
                    AbilityJson.ReadSkill(skills, "talk"),
                    node.TryGetProperty("moral", out var moral) && moral.ValueKind == JsonValueKind.True,
                    node.TryGetProperty("wanted", out var wanted) && wanted.ValueKind == JsonValueKind.True,
                    cost,
                    ReadStrings(node, "professions"),
                    node.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    ReadStrings(node, "keywords"),
                    isLeader: true,
                    abilities: AbilityJson.Read(node)));
            }
            return new LeaderCatalog(cards);
        }

        private static List<string> ReadStrings(JsonElement node, string name)
        {
            var list = new List<string>();
            if (!node.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var item in arr.EnumerateArray())
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    list.Add(text);
            }
            return list;
        }
    }
}
