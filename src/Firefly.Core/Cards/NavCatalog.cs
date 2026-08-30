using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Firefly.Core.Cards
{
    public sealed class NavCatalog
    {
        private readonly Dictionary<string, NavCard> _byId;

        public IReadOnlyDictionary<string, NavCard> Cards => _byId;

        public NavCatalog(IEnumerable<NavCard> cards)
        {
            _byId = cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
        }

        public NavCard Get(string id) => _byId[id];

        public static NavCatalog LoadFromFile(string path)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<NavFile>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("NavCards.json did not deserialize.");

            var cards = new List<NavCard>();
            foreach (var dto in file.NavCards ?? new List<NavCardDto>())
            {
                var opts = new List<NavOption>();
                foreach (var option in dto.Options ?? new List<NavOptionDto>())
                    opts.Add(new NavOption(option.Name, option.Details ?? "", NavCard.ParseOutcome(option.Details)));
                cards.Add(new NavCard(dto.Id, dto.Name, dto.Type ?? "", dto.IsReshuffle, opts));
            }

            return new NavCatalog(cards);
        }

        public static NavDecks BuildDecks(string navCardsPath, IRng? rng = null)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<NavFile>(File.ReadAllText(navCardsPath), options)
                ?? throw new InvalidDataException("NavCards.json did not deserialize.");

            var catalog = LoadFromFile(navCardsPath);
            var alliance = new List<NavCard>();
            var border = new List<NavCard>();
            var rim = new List<NavCard>();

            foreach (var dto in file.NavCards ?? new List<NavCardDto>())
            {
                var card = catalog.Get(dto.Id);
                var counts = dto.Counts ?? new NavCountsDto();
                AddCopies(alliance, card, counts.Alliance);
                AddCopies(border, card, counts.Border);
                AddCopies(rim, card, counts.Rim);
            }

            rng ??= new SystemRng();
            return new NavDecks(
                new NavDeck(alliance, rng),
                new NavDeck(border, rng),
                new NavDeck(rim, rng),
                catalog);
        }

        private static void AddCopies(List<NavCard> pile, NavCard card, int count)
        {
            for (var i = 0; i < count; i++)
                pile.Add(card);
        }

        private sealed class NavFile
        {
            public List<NavCardDto>? NavCards { get; set; }
        }

        private sealed class NavCardDto
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string? Type { get; set; }
            public bool IsReshuffle { get; set; }
            public List<NavOptionDto>? Options { get; set; }
            public NavCountsDto? Counts { get; set; }
        }

        private sealed class NavOptionDto
        {
            public string? Name { get; set; }
            public string? Details { get; set; }
        }

        private sealed class NavCountsDto
        {
            public int Alliance { get; set; }
            public int Border { get; set; }
            public int Rim { get; set; }
        }
    }
}
