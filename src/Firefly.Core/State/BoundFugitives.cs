using System.Collections.Generic;
using Firefly.Core.Cards;

namespace Firefly.Core.State
{
    /// <summary>
    /// PBH p.11–12 / user: Bound-by-Law fugitives are not Crew / Max Crew / hold cargo,
    /// but count as Fugitives for effects that affect Fugitives (Nav, Alliance, Reavers…).
    /// </summary>
    public static class BoundFugitives
    {
        public static int Count(PlayerState player)
        {
            var n = 0;
            foreach (var bound in player.BoundBounties)
                n += bound.Count;
            return n;
        }

        /// <summary>Token Fugitives plus Bound-by-Law crew.</summary>
        public static int EffectiveCount(PlayerState player) =>
            player.Fugitives + Count(player);

        public static bool HasAny(PlayerState player) =>
            player.Fugitives > 0 || Count(player) > 0;

        /// <summary>
        /// PBH p.12: when effects kill/seize/discard Fugitives, Bound Fugitives are
        /// removed from play (Crew + Bounty leave the game).
        /// </summary>
        public static int RemoveAllFromPlay(GameState game, PlayerState player)
        {
            if (player.BoundBounties.Count == 0)
                return 0;

            var removed = 0;
            foreach (var bound in new List<BoundBounty>(player.BoundBounties))
            {
                foreach (var crewId in bound.CrewIds)
                {
                    game.RemovedFromPlay.Add(crewId);
                    if (game.Crew != null && game.Crew.TryGet(crewId, out var crew))
                        game.RemovedFromPlay.Add(crew.Name);
                    removed++;
                }

                if (game.Bounties != null && game.Bounties.TryResolve(bound.BountyId, out var bounty))
                {
                    game.BountyDeck?.RemoveFromGame(bounty);
                    game.RemovedFromPlay.Add(bounty.Name);
                }
            }

            player.BoundBounties.Clear();
            return removed;
        }
    }
}
