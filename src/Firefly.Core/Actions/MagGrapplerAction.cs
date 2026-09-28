using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Mag-Grappler Launchers (PBH): +3 Tech for Boarding Tests; after Salvage Ops,
    /// Roll Tech 8 — 1–7 nothing; 8+ take 1 Ship Upgrade from any discard pile.
    /// FAQ 4.1: Drive Cores are not Ship Upgrades. Supplies.tsv / ShipUpgrades.json.
    /// </summary>
    public static class MagGrapplerAction
    {
        public const string CardId = "ship-upgrade_mag-grappler-launchers_piratesbountyhunters";
        public const string CardName = "Mag-Grappler Launchers";
        public const int BoardingTechBonus = 3;
        public const int SalvageTechTarget = 8;

        public static bool HasMagGrappler(PlayerState player)
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

        /// <summary>+3 Tech dice when the owner uses Tech for a Boarding Test.</summary>
        public static int BoardingTechDiceModifier(PlayerState? attacker, Skill boardSkill)
        {
            if (boardSkill != Skill.Tech || attacker == null || !HasMagGrappler(attacker))
                return 0;
            return BoardingTechBonus;
        }

        /// <summary>
        /// After any Salvage Op: Tech 8 roll. On 8+, take a Ship Upgrade from any discard
        /// (<paramref name="takeUpgradeId"/>) or suspend
        /// <see cref="PendingChoiceKinds.MagGrapplerUpgrade"/> when options exist and id is unset.
        /// </summary>
        public static bool TryAfterSalvage(
            GameState game,
            PlayerState player,
            IRng rng,
            out SkillCheckResult? roll,
            out string? takenUpgradeId,
            out string? error,
            string? takeUpgradeId = null)
        {
            roll = null;
            takenUpgradeId = null;
            error = null;
            if (!HasMagGrappler(player))
                return true;

            var check = new SkillCheck(Skill.Tech, SalvageTechTarget);
            if (!check.TryResolve(player, rng, out var result, out error, game: game))
                return false;
            roll = result;
            if (!result.Success)
                return true;

            var options = ListShipUpgradesInDiscard(game);
            if (options.Count == 0)
                return true;

            if (string.IsNullOrWhiteSpace(takeUpgradeId))
            {
                if (options.Count == 1)
                    takeUpgradeId = options[0];
                else
                {
                    // Suspend pick after Salvage resolves; Nav/Work finish normally.
                    var pending = new PendingChoice(
                        player.Id,
                        PendingChoiceKinds.MagGrapplerUpgrade,
                        options: options,
                        prompt: "Mag-Grappler: take 1 Ship Upgrade from any discard pile.");
                    if (!game.TrySetPendingChoice(pending, out error))
                        return false;
                    return true;
                }
            }

            return TryTakeShipUpgradeFromDiscard(game, player, takeUpgradeId!, out takenUpgradeId, out error);
        }

        /// <summary>Resume after <see cref="PendingChoiceKinds.MagGrapplerUpgrade"/>.</summary>
        public static bool TryResumeUpgradePick(
            GameState game,
            ChoiceSubmission submission,
            out string? takenUpgradeId,
            out string? error)
        {
            takenUpgradeId = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.MagGrapplerUpgrade,
                    StringComparison.Ordinal))
            {
                error = "No Mag-Grappler upgrade choice is pending.";
                return false;
            }

            var player = game.GetPlayer(game.PendingChoice.PlayerId);
            var upgradeId = submission.SelectedOptionId ?? submission.Value;
            if (string.IsNullOrWhiteSpace(upgradeId))
            {
                error = "Select a Ship Upgrade from a discard pile.";
                return false;
            }

            if (game.PendingChoice.Options != null)
            {
                var legal = false;
                foreach (var option in game.PendingChoice.Options)
                {
                    if (string.Equals(option, upgradeId, StringComparison.OrdinalIgnoreCase))
                    {
                        legal = true;
                        break;
                    }
                }
                if (!legal)
                {
                    error = $"'{upgradeId}' is not a legal Mag-Grappler discard pick.";
                    return false;
                }
            }

            if (!game.TrySubmitChoice(player.Id, submission, out _, out error))
                return false;

            return TryTakeShipUpgradeFromDiscard(game, player, upgradeId!, out takenUpgradeId, out error);
        }

        public static IReadOnlyList<string> ListShipUpgradesInDiscard(GameState game)
        {
            var ids = new List<string>();
            if (game.SupplyDecks == null)
                return ids;
            foreach (var market in game.SupplyDecks.Markets)
            {
                foreach (var card in market.Discard)
                {
                    if (card.Kind != SupplyKind.ShipUpgrade)
                        continue;
                    ids.Add(card.Id);
                }
            }
            return ids;
        }

        public static bool TryTakeShipUpgradeFromDiscard(
            GameState game,
            PlayerState player,
            string cardId,
            out string? takenUpgradeId,
            out string? error)
        {
            takenUpgradeId = null;
            error = null;
            if (game.SupplyDecks == null
                || !game.SupplyDecks.TryFindInAnyDiscard(cardId, out var market, out var card))
            {
                error = $"Ship Upgrade '{cardId}' is not in any discard pile.";
                return false;
            }

            if (card.Kind != SupplyKind.ShipUpgrade)
            {
                // FAQ 4.1: Drive Cores are not Ship Upgrades.
                error = "Mag-Grappler may only take a Ship Upgrade from a discard pile (not a Drive Core).";
                return false;
            }

            if (!market.TryTakeFromDiscard(card.Id, out var taken))
            {
                error = $"Could not take '{cardId}' from {market.Planet}'s discard.";
                return false;
            }

            ShipUpgradeApply.Install(game, player, taken.Id);
            takenUpgradeId = taken.Id;
            return true;
        }
    }
}
