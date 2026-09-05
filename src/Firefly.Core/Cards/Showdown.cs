namespace Firefly.Core.Cards
{
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
    }

    /// <summary>
    /// PBH Showdown: attacker and defender each add 1d6 to a chosen skill.
    /// Defender wins ties. Thrillin' Heroics apply at the call site.
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
    }
}
