using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Printed Skill-Test band on a Story Goal (e.g. King of All Londinium Goal 1).
    /// </summary>
    public sealed class ScenarioGoalBand
    {
        public string Range { get; }
        public string Text { get; }

        public ScenarioGoalBand(string range, string text)
        {
            Range = range ?? "";
            Text = text ?? "";
        }
    }

    /// <summary>
    /// Jail Break–style boarding preamble printed on a Goal (1 die, 1–5 Botched / 6+ board).
    /// </summary>
    public sealed class ScenarioGoalBoarding
    {
        public int Dice { get; }
        public string BotchedRange { get; }
        public string SuccessRange { get; }
        public Skill? Skill { get; }

        public ScenarioGoalBoarding(int dice, string botchedRange, string successRange, Skill? skill = null)
        {
            Dice = dice < 1 ? 1 : dice;
            BotchedRange = botchedRange ?? "";
            SuccessRange = successRange ?? "";
            Skill = skill;
        }
    }

    public static class ScenarioGoalWork
    {
        /// <summary>
        /// Goals that need a Work Action: Misbehave count, Skill Test, and/or boarding preamble.
        /// FAQ 4.1 p.7: only Skill-Test Goals need Work; Misbehave+skill Goals are the Work engine.
        /// </summary>
        public static bool IsWorkable(ScenarioGoal goal)
        {
            if (goal == null)
                return false;
            return goal.Misbehave > 0
                || goal.Skill != null
                || goal.Boarding != null;
        }

        public static bool TryParseSkill(string? name, out Skill skill)
        {
            skill = Skill.Fight;
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (name.Equals("Fight", StringComparison.OrdinalIgnoreCase))
            {
                skill = Skill.Fight;
                return true;
            }
            if (name.Equals("Tech", StringComparison.OrdinalIgnoreCase))
            {
                skill = Skill.Tech;
                return true;
            }
            if (name.Equals("Talk", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Negotiate", StringComparison.OrdinalIgnoreCase))
            {
                skill = Skill.Talk;
                return true;
            }
            return false;
        }

        public static string BandTextForSum(IReadOnlyList<ScenarioGoalBand> bands, int sum)
        {
            if (bands == null || bands.Count == 0)
                return "";
            foreach (var band in bands)
            {
                if (RangeContains(band.Range, sum))
                    return band.Text;
            }
            return bands[bands.Count - 1].Text;
        }

        public static bool RangeContains(string? range, int value)
        {
            if (string.IsNullOrWhiteSpace(range))
                return false;
            range = range.Trim();
            if (range.EndsWith("+", StringComparison.Ordinal))
            {
                if (int.TryParse(range.TrimEnd('+').Trim(), out var min))
                    return value >= min;
                return false;
            }
            var dash = range.IndexOf('-');
            if (dash > 0
                && int.TryParse(range.Substring(0, dash).Trim(), out var lo)
                && int.TryParse(range.Substring(dash + 1).Trim(), out var hi))
                return value >= lo && value <= hi;
            if (int.TryParse(range, out var exact))
                return value == exact;
            return false;
        }
    }
}
