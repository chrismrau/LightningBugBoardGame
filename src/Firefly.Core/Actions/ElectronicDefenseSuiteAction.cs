using System;
using Firefly.Core.Cards;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Electronic Defense Suite (PBH): rivals boarding your ship may not use Tech for
    /// Boarding Tests; spend 1 Fuel to Evade Reaver Cutter and ignore either the Reaver
    /// Cutter Nav Card or Contact Event. Supplies.tsv / ShipUpgrades.json card text.
    /// </summary>
    public static class ElectronicDefenseSuiteAction
    {
        public const string CardId = "ship-upgrade_electronic-defense-suite_piratesbountyhunters";
        public const string CardName = "Electronic Defense Suite";

        public static bool HasEds(PlayerState player)
        {
            if (player?.ShipUpgrades == null)
                return false;
            foreach (var id in player.ShipUpgrades)
            {
                if (string.Equals(id, CardId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, CardName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Printed: "Rivals boarding your ship may not use Tech Skill for Boarding Tests."
        /// </summary>
        public static bool BlocksTechBoarding(PlayerState defender) => HasEds(defender);

        /// <summary>
        /// Reaver Cutter Nav Card (type or printed name). Matches Corvette cancel / Decoy mover check.
        /// </summary>
        public static bool IsReaverCutterNavCard(NavCard card)
        {
            var type = card.Type ?? "";
            var name = card.Name ?? "";
            return type.Equals("Reaver Cutter", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Reaver Cutter", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Spend 1 Fuel to ignore a pending Reaver Contact Event and Evade to an adjacent Sector.
        /// Does not discard the upgrade. Passengers / Fight / Crew kills are skipped.
        /// </summary>
        public static bool TryEvadeContact(
            GameState game,
            string playerId,
            string evadeToSectorId,
            out string? error)
        {
            error = null;
            PlayerState? player = null;
            foreach (var p in game.Players)
            {
                if (string.Equals(p.Id, playerId, StringComparison.OrdinalIgnoreCase))
                {
                    player = p;
                    break;
                }
            }

            if (player == null)
            {
                error = $"Unknown player '{playerId}'.";
                return false;
            }

            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }

            if (!HasEds(player))
            {
                error = "Electronic Defense Suite ship upgrade is not installed.";
                return false;
            }

            if (game.PendingEncounter != TokenKind.ReaverCutter)
            {
                error = "No Reaver Cutter Contact Event is pending.";
                return false;
            }

            if (game.PendingChoice != null)
            {
                error = "Resolve the pending choice before using Electronic Defense Suite.";
                return false;
            }

            if (player.Fuel < 1)
            {
                error = "Electronic Defense Suite requires 1 Fuel to Evade Reaver Cutter.";
                return false;
            }

            var sector = game.PendingEncounterSectorId ?? player.SectorId;
            player.SectorId = sector;

            if (!FlightEvade.CanMove(game, player, evadeToSectorId, out error))
                return false;

            player.Fuel -= 1;
            if (!FlightEvade.TryMove(game, player, evadeToSectorId, out error))
            {
                player.Fuel += 1;
                return false;
            }

            game.PendingEncounter = null;
            game.PendingEncounterSectorId = null;
            game.PendingEncounterPlayerId = null;
            game.PendingNavDraws.Clear();
            return true;
        }
    }
}
