using Firefly.Core.State;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Thin hooks for Showdown may re-rolls (Guardian / Chari). Null = undecided → PendingChoice.
    /// </summary>
    public sealed class ShowdownChoice
    {
        /// <summary>Attacker Guardian: re-roll own die.</summary>
        public bool? AttackerAcceptReroll { get; set; }
        /// <summary>Defender Guardian: re-roll own die.</summary>
        public bool? DefenderAcceptReroll { get; set; }
        /// <summary>Attacker Chari: force defender to re-roll.</summary>
        public bool? AttackerForceRivalReroll { get; set; }
        /// <summary>Defender Chari: force attacker to re-roll.</summary>
        public bool? DefenderForceRivalReroll { get; set; }
    }

    public sealed class ShowdownResult
    {
        public int AttackerTotal { get; }
        public int DefenderTotal { get; }
        public int AttackerDie { get; }
        public int DefenderDie { get; }
        public bool AttackerWins { get; }

        public ShowdownResult(int attackerSkill, int attackerDie, int defenderSkill, int defenderDie)
        {
            AttackerDie = attackerDie;
            DefenderDie = defenderDie;
            AttackerTotal = attackerSkill + attackerDie;
            DefenderTotal = defenderSkill + defenderDie;
            AttackerWins = AttackerTotal > DefenderTotal;
        }

        public ShowdownResult WithDice(int attackerDie, int defenderDie, int attackerSkill, int defenderSkill) =>
            new ShowdownResult(attackerSkill, attackerDie, defenderSkill, defenderDie);
    }

    /// <summary>
    /// PBH Showdown: attacker and defender each add 1d6 to a chosen skill.
    /// Defender wins ties. Thrillin' Heroics apply at the call site.
    /// Guardian / Chari may re-rolls suspend via PendingChoice (FAQ 4.1 p.8).
    /// </summary>
    public static class Showdown
    {
        public static ShowdownResult Resolve(int attackerSkill, int defenderSkill, IRng rng)
        {
            var attackDie = Dice.D6(rng);
            var defendDie = Dice.D6(rng);
            return new ShowdownResult(attackerSkill, attackDie, defenderSkill, defendDie);
        }

        public static int BestSkill(CrewCard card)
        {
            var best = card.Fight;
            if (card.Tech > best)
                best = card.Tech;
            if (card.Talk > best)
                best = card.Talk;
            return best;
        }

        public static int Of(State.PlayerState player, Skill skill) =>
            skill == Skill.Fight ? player.Fight
            : skill == Skill.Tech ? player.Tech
            : player.Talk;

        /// <summary>
        /// Next Showdown may that still needs a PendingChoice, or null when fully decided.
        /// Order: attacker own → defender own → attacker force rival → defender force rival.
        /// </summary>
        public static string? NextRerollContext(
            State.PlayerState attacker,
            State.PlayerState defender,
            ShowdownChoice? choice)
        {
            choice ??= new ShowdownChoice();
            if (Abilities.AbilityDispatcher.HasShowdownReroll(attacker)
                && choice.AttackerAcceptReroll == null)
                return State.ShowdownRerollContexts.AttackerOwn;
            if (Abilities.AbilityDispatcher.HasShowdownReroll(defender)
                && choice.DefenderAcceptReroll == null)
                return State.ShowdownRerollContexts.DefenderOwn;
            if (Abilities.AbilityDispatcher.HasShowdownForceRivalReroll(attacker)
                && choice.AttackerForceRivalReroll == null)
                return State.ShowdownRerollContexts.AttackerForceRival;
            if (Abilities.AbilityDispatcher.HasShowdownForceRivalReroll(defender)
                && choice.DefenderForceRivalReroll == null)
                return State.ShowdownRerollContexts.DefenderForceRival;
            return null;
        }

        public static ShowdownResult ApplyRerolls(
            ShowdownResult initial,
            int attackerSkill,
            int defenderSkill,
            ShowdownChoice choice,
            IRng rng)
        {
            var attackDie = initial.AttackerDie;
            var defendDie = initial.DefenderDie;
            if (choice.AttackerAcceptReroll == true)
                attackDie = Dice.D6(rng);
            if (choice.DefenderAcceptReroll == true)
                defendDie = Dice.D6(rng);
            if (choice.AttackerForceRivalReroll == true)
                defendDie = Dice.D6(rng);
            if (choice.DefenderForceRivalReroll == true)
                attackDie = Dice.D6(rng);
            return new ShowdownResult(attackerSkill, attackDie, defenderSkill, defendDie);
        }

        public static bool TryMergeRerollSubmission(
            string contextId,
            ChoiceSubmission submission,
            ShowdownChoice? existing,
            out ShowdownChoice merged,
            out string? error)
        {
            merged = existing ?? new ShowdownChoice();
            error = null;
            if (submission == null)
            {
                error = "A choice submission is required.";
                return false;
            }

            bool accept;
            if (submission.Accepted != null)
                accept = submission.Accepted.Value;
            else if (!string.IsNullOrWhiteSpace(submission.SelectedOptionId))
            {
                if (string.Equals(
                        submission.SelectedOptionId,
                        State.SkillRerollOptions.Reroll,
                        System.StringComparison.Ordinal))
                    accept = true;
                else if (string.Equals(
                             submission.SelectedOptionId,
                             State.SkillRerollOptions.Keep,
                             System.StringComparison.Ordinal))
                    accept = false;
                else
                {
                    error = $"Unknown Showdown re-roll option '{submission.SelectedOptionId}'.";
                    return false;
                }
            }
            else
            {
                error = "Accept (re-roll) or decline (keep), or select keep/reroll.";
                return false;
            }

            switch (contextId)
            {
                case State.ShowdownRerollContexts.AttackerOwn:
                    merged.AttackerAcceptReroll = accept;
                    break;
                case State.ShowdownRerollContexts.DefenderOwn:
                    merged.DefenderAcceptReroll = accept;
                    break;
                case State.ShowdownRerollContexts.AttackerForceRival:
                    merged.AttackerForceRivalReroll = accept;
                    break;
                case State.ShowdownRerollContexts.DefenderForceRival:
                    merged.DefenderForceRivalReroll = accept;
                    break;
                default:
                    error = $"Unknown Showdown re-roll context '{contextId}'.";
                    return false;
            }
            return true;
        }
    }
}
