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

    /// <summary>
    /// Thin skill-test hooks until the shared PendingChoice layer.
    /// Bribes: dollars paid before the roll ($100 = +1). Default 0 = decline.
    /// Do not invent auto-pay when the player is rich.
    /// </summary>
    public sealed class SkillCheckChoice
    {
        /// <summary>
        /// Dollars to pay as Bribes before rolling. Must be a non-negative multiple of 100
        /// and not exceed cash. Ignored when the test is not printed Bribes.
        /// </summary>
        public int BribeDollars { get; set; }
    }

    public sealed class SkillCheck
    {
        private static readonly Regex Pattern = new Regex(
            @"\b(Fight|Tech|Talk|Negotiate)\s+(\d+)(?<tail>(?:\s+Kosherized(?:\s+Rules)?|\s+Bribes)*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InvertedPattern = new Regex(
            @"\b(\d+)\+\s*(Fight|Tech|Talk|Negotiate)(?<tail>(?:\s+Kosherized(?:\s+Rules)?|\s+Bribes)*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Skill Skill { get; }
        public int Target { get; }
        /// <summary>
        /// GF9 p.6 / Director's Cut p.14: exclude Fight Skill from Gear; crew Fight only.
        /// </summary>
        public bool Kosherized { get; }
        /// <summary>
        /// GF9 p.6 / Director's Cut p.14: Negotiate/Talk tests may pay $100 = +1 before the roll.
        /// </summary>
        public bool BribesAllowed { get; }

        public SkillCheck(Skill skill, int target, bool kosherized = false, bool bribesAllowed = false)
        {
            Skill = skill;
            Target = target;
            Kosherized = kosherized;
            BribesAllowed = bribesAllowed;
        }

        public static bool TryParse(string? text, out SkillCheck check)
        {
            check = null!;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var match = Pattern.Match(text);
            string label;
            int target;
            string tail;
            if (match.Success)
            {
                label = match.Groups[1].Value;
                target = int.Parse(match.Groups[2].Value);
                tail = match.Groups["tail"].Value;
            }
            else
            {
                match = InvertedPattern.Match(text);
                if (!match.Success)
                    return false;
                target = int.Parse(match.Groups[1].Value);
                label = match.Groups[2].Value;
                tail = match.Groups["tail"].Value;
            }

            Skill skill;
            if (label.Equals("Negotiate", StringComparison.OrdinalIgnoreCase))
                skill = Skill.Talk;
            else if (!Enum.TryParse(label, true, out skill))
                return false;

            var kosherized = Contains(tail, "Kosherized");
            // Printed Bribes apply to Negotiate (Talk) tests. Fight+Bribes is not a rulebook case.
            var bribes = Contains(tail, "Bribes") && skill == Skill.Talk;
            check = new SkillCheck(skill, target, kosherized, bribes);
            return true;
        }

        /// <summary>
        /// Dice count for this test. Kosherized Fights use crew Fight only
        /// (exclude <see cref="PlayerState.FightBonus"/> gear proxy).
        /// </summary>
        public int DiceCount(PlayerState player)
        {
            if (Skill == Skill.Fight)
                return Kosherized ? player.Roster.Fight : player.Fight;
            if (Skill == Skill.Tech)
                return player.Tech;
            return player.Talk;
        }

        /// <summary>
        /// Resolve the test. When Bribes are allowed, pays <see cref="SkillCheckChoice.BribeDollars"/>
        /// before rolling ($100 = +1). Fails closed if the bribe amount is invalid.
        /// </summary>
        public bool TryResolve(
            PlayerState player,
            IRng rng,
            out SkillCheckResult result,
            out string? error,
            SkillCheckChoice? choice = null)
        {
            result = null!;
            error = null;

            var bribeDollars = 0;
            var bribeBonus = 0;
            if (BribesAllowed && choice != null && choice.BribeDollars != 0)
            {
                if (choice.BribeDollars < 0)
                {
                    error = "Bribe dollars cannot be negative.";
                    return false;
                }
                if (choice.BribeDollars % 100 != 0)
                {
                    error = "Bribes must be paid in $100 increments.";
                    return false;
                }
                if (player.Cash < choice.BribeDollars)
                {
                    error = $"Need ${choice.BribeDollars} to pay Bribes.";
                    return false;
                }
                bribeDollars = choice.BribeDollars;
                bribeBonus = bribeDollars / 100;
                player.Cash -= bribeDollars;
            }

            var roll = Dice.RollD6(DiceCount(player), rng);
            var total = roll.Sum + bribeBonus;
            var success = total >= Target;
            result = new SkillCheckResult(this, roll, success, bribeDollars, bribeBonus);
            return true;
        }

        public SkillCheckResult Resolve(PlayerState player, IRng rng, SkillCheckChoice? choice = null)
        {
            if (!TryResolve(player, rng, out var result, out var error, choice))
                throw new InvalidOperationException(error ?? "Skill check failed.");
            return result;
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
        public int BribeDollarsPaid { get; }
        public int BribeBonus { get; }
        /// <summary>Roll sum plus Bribes (+ callers may add further printed modifiers).</summary>
        public int Total => Roll.Sum + BribeBonus;

        public SkillCheckResult(
            SkillCheck check,
            DiceRoll roll,
            bool success,
            int bribeDollarsPaid = 0,
            int bribeBonus = 0)
        {
            Check = check;
            Roll = roll;
            Success = success;
            BribeDollarsPaid = bribeDollarsPaid;
            BribeBonus = bribeBonus;
        }

        /// <summary>Rebuild success after adding post-roll modifiers (e.g. Misbehave +N with gear).</summary>
        public SkillCheckResult WithTotal(int total) =>
            new SkillCheckResult(Check, Roll, total >= Check.Target, BribeDollarsPaid, BribeBonus);
    }
}
