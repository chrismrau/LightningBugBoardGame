using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class AlertResolveChoice
    {
        /// <summary>
        /// Which Reaver Cutter the player to the right moves when a Reaver Alert succeeds.
        /// </summary>
        public int ReaverCutterIndex { get; set; }
    }

    public sealed class AlertKindResolution
    {
        public AlertTokenKind Kind { get; }
        public int TokenCount { get; }
        public int Die { get; }
        public bool ShipArrived { get; }
        public TokenKind? ArrivedShip { get; }

        public AlertKindResolution(
            AlertTokenKind kind,
            int tokenCount,
            int die,
            bool shipArrived,
            TokenKind? arrivedShip)
        {
            Kind = kind;
            TokenCount = tokenCount;
            Die = die;
            ShipArrived = shipArrived;
            ArrivedShip = arrivedShip;
        }
    }

    public sealed class AlertResolution
    {
        public string SectorId { get; }
        public IReadOnlyList<AlertKindResolution> Rolls { get; }
        public bool EndedFly { get; }

        public AlertResolution(string sectorId, IReadOnlyList<AlertKindResolution> rolls, bool endedFly)
        {
            SectorId = sectorId;
            Rolls = rolls;
            EndedFly = endedFly;
        }
    }

    /// <summary>
    /// Blue Sun / Director's Cut: resolve physical Alert Tokens before drawing a Nav Card.
    /// Roll a die; if ≤ token count, the player to the right moves the matching ship.
    /// Whatever the roll, remove all removable tokens from the Sector (permanent Reaver Space stays).
    /// </summary>
    public static class AlertTokenResolver
    {
        public static bool TryResolvePending(
            GameState game,
            IRng rng,
            out AlertResolution? result,
            out string? error,
            AlertResolveChoice? choice = null)
        {
            result = null;
            error = null;
            if (!game.UseAlertTokens)
            {
                error = "Alert Tokens are not in use.";
                return false;
            }
            if (game.PendingAlertSectors.Count == 0)
            {
                error = "No Alert Tokens are pending resolution.";
                return false;
            }

            var sectorId = game.PendingAlertSectors[0];
            if (!TryResolveSector(game, sectorId, rng, choice, out result, out error))
                return false;

            // Ended-Fly (Alliance/Outlaw) may have cleared the whole queue already.
            if (game.PendingAlertSectors.Count > 0
                && string.Equals(game.PendingAlertSectors[0], sectorId, System.StringComparison.OrdinalIgnoreCase))
            {
                game.PendingAlertSectors.RemoveAt(0);
            }
            return true;
        }

        public static bool TryResolveSector(
            GameState game,
            string sectorId,
            IRng rng,
            AlertResolveChoice? choice,
            out AlertResolution? result,
            out string? error)
        {
            result = null;
            error = null;
            if (rng == null)
            {
                error = "Alert Token resolution requires a die roll.";
                return false;
            }
            if (!AlertTokenRules.SectorHasAlerts(game.Tokens, sectorId, game.UseAlertTokens))
            {
                error = "That Sector has no Alert Tokens to resolve.";
                return false;
            }

            var rolls = new System.Collections.Generic.List<AlertKindResolution>();
            var endedFly = false;

            // Kalidasa / Director's Cut: when both kinds share a Sector, roll Alliance first.
            var allianceCount = AlertTokenRules.EffectiveCount(
                game.Tokens, sectorId, AlertTokenKind.Alliance, includePermanentReaverSpace: false);
            if (allianceCount > 0)
            {
                if (!TryResolveKind(
                    game,
                    sectorId,
                    AlertTokenKind.Alliance,
                    allianceCount,
                    rng,
                    choice,
                    out var allianceRoll,
                    out var allianceEndedFly,
                    out error))
                {
                    return false;
                }
                rolls.Add(allianceRoll!);
                if (allianceEndedFly)
                    endedFly = true;
            }

            var reaverCount = AlertTokenRules.EffectiveCount(
                game.Tokens, sectorId, AlertTokenKind.Reaver, includePermanentReaverSpace: true);
            if (reaverCount > 0 && !endedFly)
            {
                if (!TryResolveKind(
                    game,
                    sectorId,
                    AlertTokenKind.Reaver,
                    reaverCount,
                    rng,
                    choice,
                    out var reaverRoll,
                    out _,
                    out error))
                {
                    return false;
                }
                rolls.Add(reaverRoll!);
            }

            // "Whatever the die roll, remove all the tokens from the Sector."
            game.Tokens = game.Tokens.ClearRemovableAlerts(sectorId);
            result = new AlertResolution(sectorId, rolls, endedFly);
            return true;
        }

        private static bool TryResolveKind(
            GameState game,
            string sectorId,
            AlertTokenKind kind,
            int tokenCount,
            IRng rng,
            AlertResolveChoice? choice,
            out AlertKindResolution? roll,
            out bool endedFly,
            out string? error)
        {
            roll = null;
            endedFly = false;
            error = null;
            var die = Dice.D6(rng);
            var arrived = die <= tokenCount;
            TokenKind? ship = null;

            if (arrived)
            {
                if (kind == AlertTokenKind.Alliance)
                {
                    game.Tokens = game.Tokens.WithAllianceCruiser(sectorId);
                    ship = TokenKind.AllianceCruiser;
                    // Outlaw + Alliance Alert calls Cruiser: Fly Action over; no Nav if Full Burning.
                    if (AlertTokenRules.IsOutlawShip(game.CurrentPlayer))
                    {
                        game.CurrentPlayer.SectorId = sectorId;
                        game.PendingNavDraws.Clear();
                        game.PendingAlertSectors.Clear();
                        game.PendingEncounter = TokenKind.AllianceCruiser;
                        game.PendingEncounterSectorId = sectorId;
                        endedFly = true;
                    }
                }
                else
                {
                    if (!game.Tokens.TryMoveReaverCutter(
                        sectorId,
                        out var moved,
                        out error,
                        choice?.ReaverCutterIndex ?? 0,
                        leaveReaverAlertToken: game.UseAlertTokens))
                    {
                        // Already occupied: do not move another Cutter (same note as Reaver Cutter Nav).
                        if (game.Tokens.EncounterAt(sectorId) != TokenKind.ReaverCutter)
                            return false;
                        error = null;
                    }
                    else
                    {
                        game.Tokens = moved;
                    }
                    ship = TokenKind.ReaverCutter;
                    // Full Burn example: Contact is deferred (Keep Flying can escape; else start-of-turn).
                }
            }

            roll = new AlertKindResolution(kind, tokenCount, die, arrived, ship);
            return true;
        }
    }
}
