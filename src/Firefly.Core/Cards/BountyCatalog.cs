using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class BountyCatalog
    {
        private readonly Dictionary<string, BountyCard> _byId;
        private readonly Dictionary<string, BountyCard> _byName;

        public IReadOnlyDictionary<string, BountyCard> Cards => _byId;

        public BountyCatalog(IEnumerable<BountyCard> cards)
        {
            _byId = new Dictionary<string, BountyCard>(StringComparer.Ordinal);
            _byName = new Dictionary<string, BountyCard>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in cards)
            {
                _byId[card.Id] = card;
                _byName[card.Name] = card;
            }
        }

        public BountyCard Get(string id) => _byId[id];

        public bool TryGet(string id, out BountyCard card) => _byId.TryGetValue(id, out card!);

        public bool TryResolve(string idOrName, out BountyCard card)
        {
            if (TryGet(idOrName, out card))
                return true;
            return _byName.TryGetValue(idOrName, out card!);
        }

        public static BountyCatalog LoadDefault() => LoadFromFile(GameData.BountiesPath);

        public static BountyCatalog LoadFromFile(string path)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<BountyFile>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("Bounties.json did not deserialize.");

            var cards = new List<BountyCard>();
            foreach (var dto in file.Bounties ?? new List<BountyDto>())
            {
                var kind = string.Equals(dto.Type, "Cortex Alert", StringComparison.OrdinalIgnoreCase)
                    ? BountyKind.CortexAlert
                    : BountyKind.Wanted;
                cards.Add(new BountyCard(
                    dto.Id ?? slug(dto.Name),
                    dto.Name ?? dto.Id ?? "bounty",
                    kind,
                    dto.Immoral,
                    dto.Pickup?.Planet,
                    dto.Dropoff?.Planet,
                    dto.BotchKill,
                    dto.Pay,
                    dto.Description));
            }
            return new BountyCatalog(cards);
        }

        private static string slug(string? name) =>
            "bounty_" + (name ?? "unknown").Trim().ToLowerInvariant().Replace(' ', '-');

        private sealed class BountyFile
        {
            public List<BountyDto>? Bounties { get; set; }
        }

        private sealed class BountyDto
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Type { get; set; }
            public bool Immoral { get; set; }
            public PlaceDto? Pickup { get; set; }
            public PlaceDto? Dropoff { get; set; }
            public int BotchKill { get; set; }
            public int Pay { get; set; }
            public string? Description { get; set; }
        }

        private sealed class PlaceDto
        {
            public string? Planet { get; set; }
        }
    }
}
