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
                    opts.Add(ParseOption(option));
                var thresholds = ParseThresholds(dto.SkillThresholds);
                cards.Add(new MisbehaveCard(
                    dto.Id,
                    dto.Name,
                    dto.Suit,
                    dto.Ace,
                    dto.Keyword,
                    dto.IsReshuffle,
                    opts,
                    thresholds,
                    dto.Other,
                    dto.Bribes,
                    dto.Kosherized));
            }
            return new MisbehaveCatalog(cards);
        }

        private static MisbehaveOption ParseOption(OptionDto option)
        {
            var skill = ParseSkillCheck(option.SkillCheck);
            var bands = ParseBands(option.Bands);
            var effects = ParseEffects(option.Effects);
            return new MisbehaveOption(option.Name, option.Details, skill, bands, effects);
        }

        private static MisbehaveSkillThresholds? ParseThresholds(SkillThresholdsDto? dto)
        {
            if (dto == null)
                return null;
            if (dto.Fight == null && dto.Tech == null && dto.Talk == null)
                return new MisbehaveSkillThresholds(null, null, null);
            return new MisbehaveSkillThresholds(dto.Fight, dto.Tech, dto.Talk);
        }

        private static MisbehaveSkillCheckSpec? ParseSkillCheck(SkillCheckDto? dto)
        {
            if (dto == null)
                return null;
            if (string.IsNullOrWhiteSpace(dto.Skill))
                throw new InvalidDataException("Misbehave option skillCheck.skill is required.");
            var label = dto.Skill.Trim();
            Skill skill;
            if (label.Equals("Negotiate", StringComparison.OrdinalIgnoreCase))
                skill = Skill.Talk;
            else if (!Enum.TryParse(label, true, out skill))
                throw new InvalidDataException($"Unknown Misbehave skillCheck.skill '{dto.Skill}'.");
            var bribes = dto.Bribes == true && skill == Skill.Talk;
            return new MisbehaveSkillCheckSpec(skill, dto.Target, dto.Kosherized == true, bribes);
        }

        private static List<MisbehaveBand>? ParseBands(List<BandDto>? dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return null;
            var bands = new List<MisbehaveBand>(dtos.Count);
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
                    throw new InvalidDataException("Misbehave band requires min or range.");
                bands.Add(new MisbehaveBand(min, max, dto.Text, ParseEffects(dto.Effects)));
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
                throw new InvalidDataException($"Invalid Misbehave band range '{range}'.");
            min = int.Parse(range.Substring(0, dash).Trim());
            max = int.Parse(range.Substring(dash + 1).Trim());
        }

        private static List<MisbehaveEffect>? ParseEffects(List<EffectDto>? dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return null;
            var effects = new List<MisbehaveEffect>(dtos.Count);
            foreach (var dto in dtos)
            {
                if (string.IsNullOrWhiteSpace(dto.Type))
                    throw new InvalidDataException("Misbehave effect.type is required.");
                if (!TryParseEffectType(dto.Type, out var type))
                    throw new InvalidDataException($"Unknown Misbehave effect.type '{dto.Type}'.");
                effects.Add(new MisbehaveEffect(type, dto.Count ?? dto.Amount ?? 0));
            }
            return effects;
        }

        private static bool TryParseEffectType(string raw, out MisbehaveEffectType type)
        {
            var key = raw.Trim().Replace("_", "").Replace("-", "");
            foreach (MisbehaveEffectType candidate in Enum.GetValues(typeof(MisbehaveEffectType)))
            {
                if (candidate.ToString().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    type = candidate;
                    return true;
                }
            }
            // Accept common aliases used in planning notes / PR 2 drafts.
            if (key.Equals("attemptBotched", StringComparison.OrdinalIgnoreCase)
                || key.Equals("botch", StringComparison.OrdinalIgnoreCase))
            {
                type = MisbehaveEffectType.Botched;
                return true;
            }
            if (key.Equals("warrant", StringComparison.OrdinalIgnoreCase))
            {
                type = MisbehaveEffectType.WarrantIssued;
                return true;
            }
            type = default;
            return false;
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
            public SkillThresholdsDto? SkillThresholds { get; set; }
            public string? Other { get; set; }
            public bool? Bribes { get; set; }
            public bool? Kosherized { get; set; }
        }
        private sealed class SkillThresholdsDto
        {
            public int? Fight { get; set; }
            public int? Tech { get; set; }
            public int? Talk { get; set; }
        }
        private sealed class OptionDto
        {
            public string Name { get; set; } = "";
            public string Details { get; set; } = "";
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
    }
}
