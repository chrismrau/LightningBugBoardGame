using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Abilities;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class CrewCatalog
    {
        private readonly Dictionary<string, CrewCard> _byId;
        private readonly Dictionary<string, List<CrewCard>> _byName;
        public IReadOnlyDictionary<string, CrewCard> Cards => _byId;

        public CrewCatalog(IEnumerable<CrewCard> cards)
        {
            _byId = new Dictionary<string, CrewCard>(StringComparer.Ordinal);
            _byName = new Dictionary<string, List<CrewCard>>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in cards)
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

        public CrewCard? FindByName(string name, string? preferredSourceSuffix = null)
        {
            if (!_byName.TryGetValue(name, out var list) || list.Count == 0) return null;
            if (preferredSourceSuffix != null)
            {
                foreach (var card in list)
                {
                    if (card.Id.EndsWith(preferredSourceSuffix, StringComparison.OrdinalIgnoreCase))
                        return card;
                }
            }
            foreach (var card in list)
            {
                if (!card.Id.EndsWith("_promo", StringComparison.OrdinalIgnoreCase)) return card;
            }
            return list[0];
        }

        public static CrewCatalog LoadFromFile(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("crew", out var array))
                throw new InvalidDataException("Crew.json did not deserialize.");
            var cards = new List<CrewCard>();
            foreach (var node in array.EnumerateArray())
            {
                var id = Str(node, "id");
                var name = Str(node, "name");
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var skills = node.TryGetProperty("skills", out var sk) ? sk : default;
                cards.Add(new CrewCard(
                    id,
                    name,
                    AbilityJson.ReadSkill(skills, "fight"),
                    AbilityJson.ReadSkill(skills, "tech"),
                    AbilityJson.ReadSkill(skills, "talk"),
                    Bool(node, "moral"),
                    Bool(node, "wanted"),
                    Int(node, "cost"),
                    StringList(node, "professions"),
                    StrOrNull(node, "description"),
                    StringList(node, "keywords"),
                    abilities: AbilityJson.Read(node)));
            }
            return new CrewCatalog(cards);
        }

        public static CrewCatalog LoadDefault() => LoadFromFile(GameData.CrewPath);

        private static string Str(JsonElement node, string name) =>
            node.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

        private static string? StrOrNull(JsonElement node, string name) =>
            node.TryGetProperty(name, out var v) ? v.GetString() : null;

        private static bool Bool(JsonElement node, string name) =>
            node.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static int Int(JsonElement node, string name) =>
            node.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;

        private static List<string> StringList(JsonElement node, string name)
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
