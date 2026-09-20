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
                    opts.Add(ParseOption(option));
                cards.Add(new NavCard(dto.Id, dto.Name, dto.Type ?? "", dto.IsReshuffle, opts));
            }

            return new NavCatalog(cards);
        }

        public static NavDecks BuildDecks(string navCardsPath, IRng? rng = null)
        {
            var catalog = LoadFromFile(navCardsPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<NavFile>(File.ReadAllText(navCardsPath), options)
                ?? throw new InvalidDataException("NavCards.json did not deserialize.");

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

        private static NavOption ParseOption(NavOptionDto option)
        {
            var details = option.Details ?? "";
            return new NavOption(
                option.Name,
                details,
                NavCard.ParseOutcome(details),
                ParseSkillCheck(option.SkillCheck),
                ParseBands(option.Bands),
                ParseEffects(option.Effects));
        }

        private static MisbehaveSkillCheckSpec? ParseSkillCheck(SkillCheckDto? dto)
        {
            if (dto == null)
                return null;
            if (string.IsNullOrWhiteSpace(dto.Skill))
                throw new InvalidDataException("Nav option skillCheck.skill is required.");
            var label = dto.Skill.Trim();
            Skill skill;
            if (label.Equals("Negotiate", StringComparison.OrdinalIgnoreCase))
                skill = Skill.Talk;
            else if (!Enum.TryParse(label, true, out skill))
                throw new InvalidDataException($"Unknown Nav skillCheck.skill '{dto.Skill}'.");
            var bribes = dto.Bribes == true && skill == Skill.Talk;
            return new MisbehaveSkillCheckSpec(skill, dto.Target, dto.Kosherized == true, bribes);
        }

        private static List<CardEffectBand>? ParseBands(List<BandDto>? dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return null;
            var bands = new List<CardEffectBand>(dtos.Count);
            foreach (var dto in dtos)
            {
                int min;
                int? max;
                if (dto.Min != null)
                {
                    min = dto.Min.Value;
                    max = dto.Max;
                }
                else if (!string.IsNullOrWhiteSpace(dto.Range))
                {
                    ParseRange(dto.Range!, out min, out max);
                }
                else
                    throw new InvalidDataException("Nav band requires min or range.");
                bands.Add(new CardEffectBand(min, max, dto.Text, ParseEffects(dto.Effects)));
            }
            return bands;
        }

        private static void ParseRange(string range, out int min, out int? max)
        {
            range = range.Trim();
            var plus = range.IndexOf('+');
            if (plus >= 0)
            {
                min = int.Parse(range.Substring(0, plus).Trim());
                max = null;
                return;
            }
            var dash = range.IndexOf('-');
            if (dash < 0)
                throw new InvalidDataException($"Invalid Nav band range '{range}'.");
            min = int.Parse(range.Substring(0, dash).Trim());
            max = int.Parse(range.Substring(dash + 1).Trim());
        }

        private static List<CardEffect>? ParseEffects(List<EffectDto>? dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return null;
            var effects = new List<CardEffect>(dtos.Count);
            foreach (var dto in dtos)
            {
                if (string.IsNullOrWhiteSpace(dto.Type))
                    throw new InvalidDataException("Nav effect.type is required.");
                if (!CardEffectParsing.TryParseType(dto.Type, out var type))
                    throw new InvalidDataException(
                        $"Unknown Nav effect.type '{dto.Type}' (shared vocabulary only).");
                effects.Add(new CardEffect(type, dto.Count ?? dto.Amount ?? 0));
            }
            return effects;
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
            public SkillCheckDto? SkillCheck { get; set; }
            public List<BandDto>? Bands { get; set; }
            public List<EffectDto>? Effects { get; set; }
        }

        private sealed class SkillCheckDto
        {
            public string Skill { get; set; } = "";
            public int Target { get; set; }
            public bool? Kosherized { get; set; }
            public bool? Bribes { get; set; }
        }

        private sealed class BandDto
        {
            public int? Min { get; set; }
            public int? Max { get; set; }
            public string? Range { get; set; }
            public string? Text { get; set; }
            public List<EffectDto>? Effects { get; set; }
        }

        private sealed class EffectDto
        {
            public string Type { get; set; } = "";
            public int? Count { get; set; }
            public int? Amount { get; set; }
        }

        private sealed class NavCountsDto
        {
            public int Alliance { get; set; }
            public int Border { get; set; }
            public int Rim { get; set; }
        }
    }
}
