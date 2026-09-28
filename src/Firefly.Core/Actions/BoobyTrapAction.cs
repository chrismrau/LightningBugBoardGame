using System;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Booby Trap (PBH): −2 to Rival's Tech Boarding Tests; if Rival rolls a 1 when
    /// Boarding your ship, 1 of their Crew is Killed; Discard to count as EXPLOSIVES.
    /// Supplies.tsv / ShipUpgrades.json.
    /// </summary>
    public static class BoobyTrapAction
    {
        public const string CardId = "ship-upgrade_booby-trap_piratesbountyhunters";
        public const string CardName = "Booby Trap";

        /// <summary>Printed −2 to Rival's Tech Boarding Tests.</summary>
        public const int TechBoardingPenalty = -2;

        public static bool HasBoobyTrap(PlayerState player)
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
        /// Dice modifier for a Rival's Tech Boarding Test against this defender.
        /// Negotiate boarding is unaffected.
        /// </summary>
        public static int BoardingTechDiceModifier(PlayerState? defender, Skill boardSkill)
        {
            if (boardSkill != Skill.Tech || defender == null || !HasBoobyTrap(defender))
                return 0;
            return TechBoardingPenalty;
        }

        /// <summary>
        /// Printed: "If Rival rolls a 1 when Boarding your ship, 1 of their Crew is Killed."
        /// House lock: any die face of 1 on the Boarding Test (FAQ 4.1 die-face reading for "rolls a 1").
        /// </summary>
        public static bool RolledAOne(SkillCheckResult boarding)
        {
            if (boarding?.Roll?.Faces == null)
                return false;
            foreach (var face in boarding.Roll.Faces)
            {
                if (face == 1)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// After a Boarding Test against a Booby-Trapped ship: kill 1 boarder Crew when a 1 was rolled.
        /// </summary>
        public static bool TryApplyRollOneKill(
            GameState game,
            PlayerState boarder,
            PlayerState defender,
            SkillCheckResult boarding,
            IRng rng,
            out int killed,
            out string? error,
            KillChoice? killChoice = null)
        {
            killed = 0;
            error = null;
            if (!HasBoobyTrap(defender) || !RolledAOne(boarding))
                return true;

            return CrewKill.TryKillUpTo(
                game, boarder, 1, rng, out killed, out error, killChoice);
        }

        /// <summary>
        /// Discard Booby Trap to count as EXPLOSIVES (printed / Explosives:D).
        /// </summary>
        public static bool TryDiscardAsExplosives(
            GameState game,
            PlayerState player,
            out string? error)
        {
            if (!HasBoobyTrap(player))
            {
                error = "Booby Trap ship upgrade is not installed.";
                return false;
            }

            if (!ShipUpgradeApply.TryRemove(game, player, CardId, out error)
                && !ShipUpgradeApply.TryRemove(game, player, CardName, out error))
                return false;

            ReturnToSupplyDiscard(game);
            return true;
        }

        /// <summary>
        /// True when Booby Trap is installed and can be discarded to meet an Explosives need.
        /// </summary>
        public static bool CanCountAsExplosives(PlayerState player) => HasBoobyTrap(player);

        private static void ReturnToSupplyDiscard(GameState game)
        {
            if (game.SupplyDecks == null)
                return;

            SupplyCard card;
            if (game.Supply != null && game.Supply.TryGet(CardId, out var fromCatalog))
                card = fromCatalog;
            else
                card = new SupplyCard(CardId, CardName, 600, SupplyKind.ShipUpgrade);

            SupplyMarket? market = null;
            foreach (var candidate in game.SupplyDecks.Markets)
            {
                market = candidate;
                break;
            }

            market?.Discard.Add(card);
        }
    }
}
