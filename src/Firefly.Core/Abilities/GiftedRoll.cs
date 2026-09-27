using System;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Abilities
{
    /// <summary>
    /// Printed River Tam Gifted bands (card text):
    /// Before each Test, roll: 1-2 Return to Ship; 3 [FIGHT][FIGHT]; 4 [TECH][TECH];
    /// 5 [NEGOTIATE][NEGOTIATE]; 6: 3 of any chosen Skill.
    /// FAQ 4.1: roll after choosing the option / before the test; River never meets Needs;
    /// Simon's +2 to Gifted rolls is mandatory and applied <em>after</em> the die (band from
    /// die face only).
    /// </summary>
    public static class GiftedRoll
    {
        public const string RiverTamName = "River Tam";
        public const string RiverTamId = "crew_river-tam";

        /// <summary>Printed skill icons on bands 3–5.</summary>
        public const int BandSkillIcons = 2;

        /// <summary>Printed "3 of any chosen Skill" on band 6.</summary>
        public const int AnySkillIcons = 3;

        public static bool HasGiftedCrew(PlayerState player, string crewName = RiverTamName) =>
            player != null && player.Roster.HasName(crewName);

        /// <summary>
        /// River is on the Job for this test: named crew present and not Returned to Ship.
        /// </summary>
        public static bool IsGiftedCrewOnJob(PlayerState player, string crewName = RiverTamName)
        {
            if (!HasGiftedCrew(player, crewName))
                return false;
            var member = FindGiftedMember(player, crewName);
            return member != null && !JobWorkCrew.IsUnavailable(player, member);
        }

        public static CrewMember? FindGiftedMember(PlayerState player, string crewName = RiverTamName)
        {
            if (player == null)
                return null;
            foreach (var member in player.Roster.Members)
            {
                if (string.Equals(member.Name, crewName, StringComparison.OrdinalIgnoreCase))
                    return member;
            }
            return null;
        }

        /// <summary>Band lookup uses the die face only (1–6).</summary>
        public static GiftedOutcome OutcomeFromDie(int die)
        {
            if (die <= 2)
                return GiftedOutcome.ReturnToShip;
            if (die == 3)
                return GiftedOutcome.Fight;
            if (die == 4)
                return GiftedOutcome.Tech;
            if (die == 5)
                return GiftedOutcome.Negotiate;
            return GiftedOutcome.AnySkill;
        }

        /// <summary>
        /// Roll Gifted. Band from die; Simon <c>giftedRollBonus</c> adds to skill icons after.
        /// Band 6 requires <paramref name="chosenSkill"/> (PendingChoice when null needed).
        /// </summary>
        public static GiftedRollResult Roll(
            PlayerState player,
            IRng rng,
            Skill? chosenSkill = null,
            string giftedCrewName = RiverTamName,
            AbilityContext? context = null)
        {
            var die = Dice.D6(rng);
            return FromDie(player, die, chosenSkill, giftedCrewName, context);
        }

        public static GiftedRollResult FromDie(
            PlayerState player,
            int die,
            Skill? chosenSkill = null,
            string giftedCrewName = RiverTamName,
            AbilityContext? context = null)
        {
            if (die < 1 || die > 6)
                throw new ArgumentOutOfRangeException(nameof(die), die, "Gifted die must be 1–6.");

            var bonus = AbilityDispatcher.GiftedRollBonus(player, giftedCrewName, context);
            var outcome = OutcomeFromDie(die);
            Skill? skill = null;
            var amount = 0;
            var needsSkillChoice = false;

            switch (outcome)
            {
                case GiftedOutcome.ReturnToShip:
                    break;
                case GiftedOutcome.Fight:
                    skill = Skill.Fight;
                    amount = BandSkillIcons + bonus;
                    break;
                case GiftedOutcome.Tech:
                    skill = Skill.Tech;
                    amount = BandSkillIcons + bonus;
                    break;
                case GiftedOutcome.Negotiate:
                    skill = Skill.Talk;
                    amount = BandSkillIcons + bonus;
                    break;
                case GiftedOutcome.AnySkill:
                    if (chosenSkill == null)
                        needsSkillChoice = true;
                    else
                    {
                        skill = chosenSkill;
                        amount = AnySkillIcons + bonus;
                    }
                    break;
            }

            return new GiftedRollResult(die, bonus, outcome, skill, amount, needsSkillChoice);
        }

        /// <summary>
        /// Complete band 6 after the player picks Fight / Tech / Negotiate.
        /// </summary>
        public static GiftedRollResult WithChosenSkill(GiftedRollResult pending, Skill chosen)
        {
            if (pending == null)
                throw new ArgumentNullException(nameof(pending));
            if (pending.Outcome != GiftedOutcome.AnySkill)
                throw new InvalidOperationException("Chosen skill applies only to Gifted band 6.");
            var amount = AnySkillIcons + pending.Bonus;
            return new GiftedRollResult(
                pending.Die, pending.Bonus, GiftedOutcome.AnySkill, chosen, amount, needsSkillChoice: false);
        }

        /// <summary>Extra dice this Gifted result adds to a matching Skill Test.</summary>
        public static int ExtraDiceFor(GiftedRollResult? gifted, Skill testSkill)
        {
            if (gifted == null || gifted.NeedsSkillChoice || gifted.Skill == null)
                return 0;
            return gifted.Skill == testSkill ? gifted.SkillAmount : 0;
        }
    }

    public enum GiftedOutcome
    {
        ReturnToShip,
        Fight,
        Tech,
        Negotiate,
        AnySkill
    }

    public sealed class GiftedRollResult
    {
        public int Die { get; }
        /// <summary>Simon (etc.) giftedRollBonus — applied to skill icons after the die.</summary>
        public int Bonus { get; }
        public GiftedOutcome Outcome { get; }
        public Skill? Skill { get; }
        /// <summary>Skill icons after Simon bonus (0 when ReturnToShip or awaiting skill pick).</summary>
        public int SkillAmount { get; }
        /// <summary>Band 6 awaiting Fight/Tech/Negotiate PendingChoice.</summary>
        public bool NeedsSkillChoice { get; }

        /// <summary>Die + bonus for logging; band lookup uses <see cref="Die"/> only.</summary>
        public int Total => Die + Bonus;

        public GiftedRollResult(
            int die,
            int bonus,
            GiftedOutcome outcome,
            Skill? skill,
            int skillAmount,
            bool needsSkillChoice)
        {
            Die = die;
            Bonus = bonus;
            Outcome = outcome;
            Skill = skill;
            SkillAmount = skillAmount;
            NeedsSkillChoice = needsSkillChoice;
        }
    }
}
