using System.Collections.Generic;

namespace Firefly.Core.State
{
    /// <summary>
    /// PBH p.11–12 / FAQ 4.1 p.14: Bound-by-Law fugitives are not Crew, Max Crew, hold
    /// cargo, or Fugitive Tokens. They never count toward Active Jobs. They are not
    /// seized by Alliance Cruiser / Corvette / Customs (tokens only — FAQ 4.1 p.14).
    /// They do not make an Outlaw ship. The printed exception: Reavers that Kill
    /// Passenger and Fugitive tokens also remove Bound Fugitives (PBH p.12).
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

        public static bool HasBound(PlayerState player) => Count(player) > 0;

        /// <summary>
        /// PBH p.12: when Reavers Kill Passenger and Fugitive tokens, Bound Fugitives
        /// are removed from play (Crew + Bounty leave the game).
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
