using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<LeaderFile>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("Leaders.json did not deserialize.");

            var cards = new List<CrewCard>();
            foreach (var dto in file.Leaders ?? new List<LeaderDto>())
            {
                var skills = dto.Skills ?? new SkillDto();
                cards.Add(new CrewCard(
                    dto.Id,
                    dto.Name,
                    skills.Fight ?? 0,
                    skills.Tech ?? 0,
                    skills.Talk ?? 0,
                    dto.Moral,
                    dto.Wanted,
                    dto.Cost ?? 0,
                    dto.Professions ?? new List<string>(),
                    dto.Description,
                    dto.Keywords ?? new List<string>(),
                    isLeader: true));
            }
            return new LeaderCatalog(cards);
        }

        private sealed class LeaderFile
        {
            public List<LeaderDto>? Leaders { get; set; }
        }

        private sealed class LeaderDto
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public bool Moral { get; set; }
            public bool Wanted { get; set; }
            public int? Cost { get; set; }
            public string? Description { get; set; }
            public List<string>? Professions { get; set; }
            public List<string>? Keywords { get; set; }
            public SkillDto? Skills { get; set; }
        }

        private sealed class SkillDto
        {
            public int? Fight { get; set; }
            public int? Tech { get; set; }
            public int? Talk { get; set; }
        }
    }
}
