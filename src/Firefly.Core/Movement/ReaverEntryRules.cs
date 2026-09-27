using System;
using Firefly.Core.State;

namespace Firefly.Core.Movement
{
    /// <summary>
    /// Who may enter a Sector occupied by a Reaver Cutter.
    /// GF9 p.8: no ship may move in. Blue Sun Desperate Times: Mosey allowed when
    /// playing with Blue Sun. Reaver-Flage: ship may move in (Mosey / Full Burn / Evade).
    /// </summary>
    public readonly struct ReaverEntryAllowance
    {
        public bool AllowMosey { get; }
        public bool AllowFullBurn { get; }
        public bool AllowEvade { get; }

        public ReaverEntryAllowance(bool allowMosey, bool allowFullBurn, bool allowEvade)
        {
            AllowMosey = allowMosey;
            AllowFullBurn = allowFullBurn;
            AllowEvade = allowEvade;
        }

        public static ReaverEntryAllowance None { get; } = new ReaverEntryAllowance(false, false, false);

        public static ReaverEntryAllowance For(GameState game, PlayerState player)
        {
            if (player != null && ReaverEntryRules.HasReaverFlage(player))
                return new ReaverEntryAllowance(true, true, true);
            if (game != null && game.UseBlueSun)
                return new ReaverEntryAllowance(allowMosey: true, allowFullBurn: false, allowEvade: false);
            return None;
        }

        public bool Allows(MovementKind kind) =>
            kind == MovementKind.Mosey ? AllowMosey : AllowFullBurn;
    }

    public static class ReaverEntryRules
    {
        public const string FlageCardId = "ship-upgrade_reaver-flage_bluesun";
        public const string FlageCardName = "Reaver-Flage";

        public static bool HasReaverFlage(PlayerState player)
        {
            if (player?.ShipUpgrades == null)
                return false;
            foreach (var id in player.ShipUpgrades)
            {
                if (string.Equals(id, FlageCardId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, FlageCardName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
