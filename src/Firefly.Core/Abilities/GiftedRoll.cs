using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Abilities
{
    /// <summary>
    /// Thin Gifted-roll hook. FAQ 4.1: roll after choosing the option / before the test;
    /// Simon's +2 is mandatory. Full Gifted outcome bands are not in Supplies.tsv
    /// ("Gifted: See card.") — do not invent; callers receive the modified die total only.
    /// </summary>
    public static class GiftedRoll
    {
        public const string RiverTamName = "River Tam";

        public static bool HasGiftedCrew(PlayerState player, string crewName = RiverTamName) =>
            player.Roster.HasName(crewName);

        /// <summary>
        /// Roll 1d6 + mandatory giftedRollBonus abilities (Simon +2). Does not apply Job Needs
        /// (FAQ: River never meets Needs). Outcome → return-to-ship / skill grant waits on printed bands.
        /// </summary>
        public static GiftedRollResult Roll(
            PlayerState player,
            IRng rng,
            string giftedCrewName = RiverTamName,
            AbilityContext? context = null)
        {
            var die = Dice.D6(rng);
            var bonus = AbilityDispatcher.GiftedRollBonus(player, giftedCrewName, context);
            return new GiftedRollResult(die, bonus, die + bonus);
        }
    }

    public sealed class GiftedRollResult
    {
        public int Die { get; }
        public int Bonus { get; }
        public int Total { get; }

        public GiftedRollResult(int die, int bonus, int total)
        {
            Die = die;
            Bonus = bonus;
            Total = total;
        }
    }
}
