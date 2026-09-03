using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class MisbehaveCatalog
    {
        private readonly Dictionary<string, MisbehaveCard> _byId;
        public IReadOnlyDictionary<string, MisbehaveCard> Cards => _byId;

        public MisbehaveCatalog(IEnumerable<MisbehaveCard> cards)
        {
            _byId = new Dictionary<string, MisbehaveCard>(StringComparer.Ordinal);
            foreach (var card in cards)
                _byId[card.Id] = card;
        }

        public MisbehaveCard Get(string id) => _byId[id];
        public bool TryGet(string id, out MisbehaveCard card) => _byId.TryGetValue(id, out card!);
        public static MisbehaveCatalog LoadDefault() => LoadFromFile(GameData.MisbehavePath);

        public static MisbehaveCatalog LoadFromFile(string path)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<MisbehaveFile>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("Misbehave.json did not deserialize.");
            var cards = new List<MisbehaveCard>();
            foreach (var dto in file.MisbehaveCards ?? new List<MisbehaveDto>())
            {
                var opts = new List<MisbehaveOption>();
                foreach (var option in dto.Options ?? new List<OptionDto>())
                    opts.Add(new MisbehaveOption(option.Name, option.Details));
                cards.Add(new MisbehaveCard(dto.Id, dto.Name, dto.Suit, dto.Ace, dto.Keyword, dto.IsReshuffle, opts));
            }
            return new MisbehaveCatalog(cards);
        }

        private sealed class MisbehaveFile { public List<MisbehaveDto>? MisbehaveCards { get; set; } }
        private sealed class MisbehaveDto
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string? Suit { get; set; }
            public string? Ace { get; set; }
            public string? Keyword { get; set; }
            public bool IsReshuffle { get; set; }
            public List<OptionDto>? Options { get; set; }
        }
        private sealed class OptionDto { public string Name { get; set; } = ""; public string Details { get; set; } = ""; }
    }
}
