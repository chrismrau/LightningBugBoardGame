using System;
using System.Collections.Generic;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// FAQ 4.1 p.14: when the Alliance Cruiser moves into an Outlaw's Sector (including on another
    /// player's turn), that Outlaw resolves Alliance Contact immediately before the active player
    /// continues. Patrol / Entanglements use this after moving the Cruiser.
    /// </summary>
    public static class AllianceCruiserContact
    {
        public static PlayerState Subject(GameState game)
        {
            if (!string.IsNullOrEmpty(game.PendingEncounterPlayerId))
                return game.GetPlayer(game.PendingEncounterPlayerId!);
            return game.CurrentPlayer;
        }

        /// <summary>
        /// Queue Alliance Cruiser Contact for every Outlaw Ship in <paramref name="sectorId"/>.
        /// Does not replace a non-Cruiser pending encounter.
        /// </summary>
        public static void QueueForOutlawsInSector(GameState game, string sectorId)
        {
            if (string.IsNullOrWhiteSpace(sectorId))
                return;

            var outlaws = new List<PlayerState>();
            foreach (var player in game.Players)
            {
                if (!string.Equals(player.SectorId, sectorId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!AlertTokenRules.IsOutlawShip(player))
                    continue;
                outlaws.Add(player);
            }

            if (outlaws.Count == 0)
                return;

            if (game.PendingEncounter.HasValue
                && game.PendingEncounter != TokenKind.AllianceCruiser)
            {
                return;
            }

            var index = 0;
            if (!game.PendingEncounter.HasValue)
            {
                SetHead(game, outlaws[0].Id, sectorId);
                index = 1;
            }
            else if (string.IsNullOrEmpty(game.PendingEncounterPlayerId))
            {
                // Legacy head (named Cruiser Nav / Fly) targets CurrentPlayer.
                game.PendingEncounterPlayerId = game.CurrentPlayer.Id;
            }

            for (; index < outlaws.Count; index++)
            {
                var id = outlaws[index].Id;
                if (IsAlreadyQueued(game, id))
                    continue;
                game.PendingAllianceContactQueue.Add(id);
            }
        }

        public static void SetHead(GameState game, string playerId, string sectorId)
        {
            game.PendingEncounter = TokenKind.AllianceCruiser;
            game.PendingEncounterSectorId = sectorId;
            game.PendingEncounterPlayerId = playerId;
        }

        /// <summary>
        /// Clear the head Contact, or promote the next queued Outlaw. Returns whether the active
        /// flyer's Nav draws should be wiped (Contact Full Stop applies to the flyer only).
        /// </summary>
        public static bool ClearHeadOrAdvance(GameState game)
        {
            var subjectId = game.PendingEncounterPlayerId ?? game.CurrentPlayer.Id;
            var wipeFlyerNav = string.Equals(
                subjectId,
                game.CurrentPlayer.Id,
                StringComparison.OrdinalIgnoreCase);

            if (game.PendingAllianceContactQueue.Count > 0)
            {
                var nextId = game.PendingAllianceContactQueue[0];
                game.PendingAllianceContactQueue.RemoveAt(0);
                game.PendingEncounterPlayerId = nextId;
                // Sector stays the Cruiser's current location.
                game.PendingEncounter = TokenKind.AllianceCruiser;
                if (string.IsNullOrEmpty(game.PendingEncounterSectorId))
                    game.PendingEncounterSectorId = game.Tokens.AllianceCruiserSectorId;
                return wipeFlyerNav;
            }

            game.PendingEncounter = null;
            game.PendingEncounterSectorId = null;
            game.PendingEncounterPlayerId = null;
            game.PendingEncounterDeferredNav = false;
            return wipeFlyerNav;
        }

        public static void ClearAll(GameState game)
        {
            game.PendingEncounter = null;
            game.PendingEncounterSectorId = null;
            game.PendingEncounterPlayerId = null;
            game.PendingAllianceContactQueue.Clear();
            game.PendingEncounterDeferredNav = false;
        }

        private static bool IsAlreadyQueued(GameState game, string playerId)
        {
            if (string.Equals(
                    game.PendingEncounterPlayerId ?? game.CurrentPlayer.Id,
                    playerId,
                    StringComparison.OrdinalIgnoreCase)
                && game.PendingEncounter == TokenKind.AllianceCruiser)
            {
                return true;
            }

            foreach (var id in game.PendingAllianceContactQueue)
            {
                if (string.Equals(id, playerId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
