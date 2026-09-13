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
            @"\b(Fight|Tech|Talk|Negotiate)\s+(\d+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InvertedPattern = new Regex(
            @"\b(\d+)\+\s*(Fight|Tech|Talk|Negotiate)\b",
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
            string label;
            int target;
            if (match.Success)
            {
                label = match.Groups[1].Value;
                target = int.Parse(match.Groups[2].Value);
            }
            else
            {
                match = InvertedPattern.Match(text);
                if (!match.Success)
                    return false;
                target = int.Parse(match.Groups[1].Value);
                label = match.Groups[2].Value;
            }

            Skill skill;
            if (label.Equals("Negotiate", StringComparison.OrdinalIgnoreCase))
                skill = Skill.Talk;
            else if (!Enum.TryParse(label, true, out skill))
                return false;
            check = new SkillCheck(skill, target);
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

        /// <summary>
        /// Director's Cut p.14 / GF9: Skill Tests list results under the target; the rolled
        /// total selects the matching printed band (e.g. 1-4 … / 5+ …).
        /// </summary>
        public static string? BandText(string? details, int sum)
        {
            if (string.IsNullOrWhiteSpace(details))
                return null;
            string? picked = null;
            foreach (Match match in BandPattern.Matches(details))
            {
                var min = int.Parse(match.Groups[1].Value);
                var max = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : int.MaxValue;
                if (sum >= min && sum <= max)
                    picked = match.Groups[3].Value.Trim().TrimEnd('.');
            }
            return string.IsNullOrWhiteSpace(picked) ? null : picked;
        }

        private static readonly Regex BandPattern = new Regex(
            @"(\d+)\s*(?:-\s*(\d+)|\+)\s*[:;,]?\s*(.*?)(?=(?:\s+\d+\s*(?:-\s*\d+|\+))|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

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
