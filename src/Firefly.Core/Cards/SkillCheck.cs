using System;
using System.Text.RegularExpressions;
using Firefly.Core.State;

namespace Firefly.Core.Cards
{
    public enum Skill
    {
        Fight,
        Tech,
        Talk
    }

    public sealed class SkillCheck
    {
        private static readonly Regex Pattern = new Regex(
            @"\b(Fight|Tech|Talk)\s+(\d+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Skill Skill { get; }
        public int Target { get; }

        public SkillCheck(Skill skill, int target)
        {
            Skill = skill;
            Target = target;
        }

        public static bool TryParse(string? text, out SkillCheck check)
        {
            check = null!;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var match = Pattern.Match(text);
            if (!match.Success)
                return false;
            if (!Enum.TryParse(match.Groups[1].Value, true, out Skill skill))
                return false;
            check = new SkillCheck(skill, int.Parse(match.Groups[2].Value));
            return true;
        }

        public int DiceCount(PlayerState player) =>
            Skill == Skill.Fight ? player.Fight
            : Skill == Skill.Tech ? player.Tech
            : player.Talk;

        public SkillCheckResult Resolve(PlayerState player, IRng rng)
        {
            var roll = Dice.RollD6(DiceCount(player), rng);
            var success = roll.Sum >= Target;
            return new SkillCheckResult(this, roll, success);
        }

        public static FlightOutcome OutcomeFor(string? details, bool success)
        {
            var text = details ?? "";
            if (success)
            {
                if (Contains(text, "Keep Flying"))
                    return FlightOutcome.KeepFlying;
                if (Contains(text, "Evade"))
                    return FlightOutcome.Evade;
                return FlightOutcome.FullStop;
            }

            if (Contains(text, "Full Stop"))
                return FlightOutcome.FullStop;
            if (Contains(text, "Evade"))
                return FlightOutcome.Evade;
            return FlightOutcome.FullStop;
        }

        private static bool Contains(string text, string value) =>
            text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public sealed class SkillCheckResult
    {
        public SkillCheck Check { get; }
        public DiceRoll Roll { get; }
        public bool Success { get; }

        public SkillCheckResult(SkillCheck check, DiceRoll roll, bool success)
        {
            Check = check;
            Roll = roll;
            Success = success;
        }
    }
}
