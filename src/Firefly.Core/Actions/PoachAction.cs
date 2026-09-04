using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class PoachResult
    {
        public string CrewId { get; }
        public string CrewName { get; }
        public string FromPlayerId { get; }
        public int CashSpent { get; }

        public PoachResult(string crewId, string crewName, string fromPlayerId, int cashSpent)
        {
            CrewId = crewId;
            CrewName = crewName;
            FromPlayerId = fromPlayerId;
            CashSpent = cashSpent;
        }
    }

    /// <summary>
    /// A Better Offer (GF9 p.15-17, FAQ 4.1 p.1): on your turn, while sharing a
    /// sector with a rival, pay that Disgruntled crew's hiring cost to the bank.
    /// They jump ship, the Disgruntled token comes off. Does not use an Action.
    /// Leaders cannot be hired away.
    /// </summary>
    public sealed class PoachAction
    {
        public bool TryPoach(
            GameState game,
            string playerId,
            string fromPlayerId,
            string crewId,
            out PoachResult? result,
            out string? error)
        {
            result = null;
            var player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            if (game.HasPendingEvents)
            {
                error = "Resolve pending Nav cards, encounters, or Misbehave first.";
                return false;
            }
            if (string.Equals(playerId, fromPlayerId, System.StringComparison.Ordinal))
            {
                error = "Cannot poach your own crew.";
                return false;
            }

            var rival = game.GetPlayer(fromPlayerId);
            if (player.SectorId != rival.SectorId)
            {
                error = "Must share a sector with that ship.";
                return false;
            }

            var member = rival.Roster.Find(crewId);
            if (member == null)
            {
                error = "That crew is not on that ship.";
                return false;
            }
            if (member.IsLeader)
            {
                error = "Cannot hire a Leader away.";
                return false;
            }
            if (!member.Disgruntled)
            {
                error = $"{member.Name} is not Disgruntled.";
                return false;
            }

            var cost = System.Math.Max(0, member.Card.Cost);
            if (player.Cash < cost)
            {
                error = $"Need ${cost} to hire {member.Name}.";
                return false;
            }
            if (player.Roster.Count >= player.Roster.MaxCrew)
            {
                error = $"Roster is full ({player.Roster.MaxCrew}).";
                return false;
            }

            if (!rival.Roster.Remove(member.Id))
            {
                error = "Could not take that crew off the rival ship.";
                return false;
            }
            member.Disgruntled = false;
            if (!player.Roster.TryAccept(member, out error))
            {
                rival.Roster.TryAccept(member, out _);
                return false;
            }

            player.Cash -= cost;
            result = new PoachResult(member.Id, member.Name, rival.Id, cost);
            return true;
        }
    }
}
