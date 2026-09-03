using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<CrewFile>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("Crew.json did not deserialize.");
            var cards = new List<CrewCard>();
            foreach (var dto in file.Crew ?? new List<CrewDto>())
            {
                var skills = dto.Skills ?? new SkillDto();
                cards.Add(new CrewCard(
                    dto.Id, dto.Name, skills.Fight ?? 0, skills.Tech ?? 0, skills.Talk ?? 0,
                    dto.Moral, dto.Wanted, dto.Cost,
                    dto.Professions ?? new List<string>(), dto.Description,
                    dto.Keywords ?? new List<string>()));
            }
            return new CrewCatalog(cards);
        }

        public static CrewCatalog LoadDefault() => LoadFromFile(GameData.CrewPath);

        private sealed class CrewFile { public List<CrewDto>? Crew { get; set; } }
        private sealed class CrewDto
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public bool Moral { get; set; }
            public bool Wanted { get; set; }
            public int Cost { get; set; }
            public string? Description { get; set; }
            public List<string>? Professions { get; set; }
            public List<string>? Keywords { get; set; }
            public SkillDto? Skills { get; set; }
        }
        private sealed class SkillDto { public int? Fight { get; set; } public int? Tech { get; set; } public int? Talk { get; set; } }
    }
}
