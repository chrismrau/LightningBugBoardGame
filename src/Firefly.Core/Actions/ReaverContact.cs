using Firefly.Core.Cards;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class ReaverContactResult
    {
        public int PassengersKilled { get; }
        public int FugitivesKilled { get; }
        public SkillCheckResult Fight { get; }
        public int CrewKilled { get; }
        public string EvadedToSectorId { get; }

        public ReaverContactResult(
            int passengersKilled,
            int fugitivesKilled,
            SkillCheckResult fight,
            int crewKilled,
            string evadedToSectorId)
        {
            PassengersKilled = passengersKilled;
            FugitivesKilled = fugitivesKilled;
            Fight = fight;
            CrewKilled = crewKilled;
            EvadedToSectorId = evadedToSectorId;
        }
    }

    /// <summary>
    /// Reaver Contact: kill all Passengers &amp; Fugitives; Fight 8
    /// (1–7 Kill 2 Crew + Evade; 8+ Kill 1 Crew + Evade).
    /// GF9 p.8 / Director's Cut p.17 — resolve at start of turn in the Cutter's Sector,
    /// or immediately when the "Reaver Cutter" Nav Card moves the Cutter onto you.
    /// </summary>
    public static class ReaverContact
    {
        public const int FightTarget = 8;

        public static bool TryResolve(
            GameState game,
            IRng rng,
            string evadeToSectorId,
            out ReaverContactResult? result,
            out string? error)
        {
            result = null;
            error = null;
            if (game.PendingEncounter != TokenKind.ReaverCutter)
            {
                error = "No Reaver Cutter encounter is pending.";
                return false;
            }

            var player = game.CurrentPlayer;
            var sector = game.PendingEncounterSectorId ?? player.SectorId;
            player.SectorId = sector;

            if (!TryApply(game, player, rng, evadeToSectorId, out result, out error))
                return false;

            game.PendingEncounter = null;
            game.PendingEncounterSectorId = null;
            game.PendingNavDraws.Clear();
            return true;
        }

        /// <summary>
        /// Applies Contact effects at the player's current sector without requiring PendingEncounter.
        /// Used when the "Reaver Cutter" Nav Card moves the Cutter onto the ship.
        /// </summary>
        public static bool TryApplyImmediate(
            GameState game,
            IRng rng,
            string evadeToSectorId,
            out ReaverContactResult? result,
            out string? error) =>
            TryApply(game, game.CurrentPlayer, rng, evadeToSectorId, out result, out error);

        private static bool TryApply(
            GameState game,
            PlayerState player,
            IRng rng,
            string evadeToSectorId,
            out ReaverContactResult? result,
            out string? error)
        {
            result = null;
            error = null;

            if (!FlightEvade.CanMove(game, player, evadeToSectorId, out error))
                return false;

            var passengers = player.Passengers;
            var fugitives = player.Fugitives;
            player.Passengers = 0;
            player.Fugitives = 0;

            var check = new SkillCheck(Skill.Fight, FightTarget);
            var fight = check.Resolve(player, rng);
            var killCount = fight.Success ? 1 : 2;
            var crewKilled = player.Roster.KillUpTo(killCount);

            if (!FlightEvade.TryMove(game, player, evadeToSectorId, out error))
                return false;

            result = new ReaverContactResult(passengers, fugitives, fight, crewKilled, evadeToSectorId);
            return true;
        }
    }

    /// <summary>
    /// Evade — Move your ship to an adjacent Sector. Do not draw an additional Nav Card.
    /// No further movement is possible. (GF9 p.7 / Director's Cut p.16)
    /// </summary>
    public static class FlightEvade
    {
        public static bool CanMove(GameState game, PlayerState player, string toSectorId, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(toSectorId))
            {
                error = "Evade requires an adjacent destination sector.";
                return false;
            }
            if (!game.Map.TryGet(toSectorId, out _))
            {
                error = $"Unknown sector '{toSectorId}'.";
                return false;
            }
            if (toSectorId == player.SectorId)
            {
                error = "Evade must enter a different sector.";
                return false;
            }

            var adjacent = false;
            foreach (var neighbor in game.Map.Neighbors(player.SectorId))
            {
                if (neighbor == toSectorId)
                {
                    adjacent = true;
                    break;
                }
            }
            if (!adjacent)
            {
                error = $"'{toSectorId}' is not adjacent to '{player.SectorId}'.";
                return false;
            }

            if (game.Tokens.EncounterAt(toSectorId) == TokenKind.ReaverCutter)
            {
                error = "No ship may move into a Sector occupied by the Reaver Cutter.";
                return false;
            }

            return true;
        }

        public static bool TryMove(GameState game, PlayerState player, string toSectorId, out string? error)
        {
            if (!CanMove(game, player, toSectorId, out error))
                return false;
            player.SectorId = toSectorId;
            if (AlertTokenRules.SectorHasAlerts(game.Tokens, toSectorId, game.UseAlertTokens))
            {
                var already = false;
                foreach (var pending in game.PendingAlertSectors)
                {
                    if (string.Equals(pending, toSectorId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        already = true;
                        break;
                    }
                }
                if (!already)
                    game.PendingAlertSectors.Add(toSectorId);
            }
            return true;
        }
    }
}
