using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class AlertResolveChoice
    {
        /// <summary>
        /// Which Reaver Cutter the player to the right moves when a Reaver Alert succeeds.
        /// Null → PendingChoice when more than one Cutter is on the board.
        /// </summary>
        public int? ReaverCutterIndex { get; set; }

        /// <summary>
        /// Alliance Space: Cruiser or Corvette. Border/Rim: Corvette only when in play.
        /// Null → PendingChoice when both ships are choosable in Alliance Space.
        /// </summary>
        public TokenKind? AllianceShip { get; set; }

        /// <summary>
        /// When Alliance Alert moves the Corvette onto a Cutter, drive-off destination.
        /// Null → PendingChoice when a drive-off is required.
        /// </summary>
        public string? DriveOffReaverToSectorId { get; set; }

        /// <summary>
        /// Any Port Safe Harbor: when Alliance Alert would place the Cruiser on a Haven,
        /// adjacent Sector chosen by the player to the right.
        /// Null → PendingChoice when Safe Harbor redirect is required.
        /// </summary>
        public string? AllianceCruiserToSectorId { get; set; }

        public CorvetteContactChoice? CorvetteContact { get; set; }
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
    /// Blue Sun / Director's Cut / Kalidasa: resolve physical Alert Tokens before drawing a Nav Card.
    /// Roll a die; if ≤ token count, the player to the right moves the matching ship.
    /// Whatever the roll, remove all removable tokens from the Sector (permanent Reaver Space stays).
    /// </summary>
    public static class AlertTokenResolver
    {
        /// <summary>
        /// Per-game mid-resolve stash. Must not be process-wide static fields — parallel
        /// games / xUnit classes were clearing each other's Alliance/Reaver die + sector.
        /// </summary>
        private static readonly ConditionalWeakTable<GameState, SuspendBag> Bags = new ConditionalWeakTable<GameState, SuspendBag>();

        private sealed class SuspendBag
        {
            public bool Resuming;
            public string? SectorId;
            public int? AllianceDie;
            public int? ReaverDie;
            public AlertResolveChoice? Choice;
            public List<AlertKindResolution>? CompletedRolls;

            public void Clear()
            {
                Resuming = false;
                SectorId = null;
                AllianceDie = null;
                ReaverDie = null;
                Choice = null;
                CompletedRolls = null;
            }
        }

        private static SuspendBag Bag(GameState game) => Bags.GetOrCreateValue(game);

        /// <summary>
        /// Drop mid-resolve stash (EndTurn / ClearPendingEvents / abort).
        /// </summary>
        public static void ClearSuspend(GameState game)
        {
            if (game == null)
                return;
            if (Bags.TryGetValue(game, out var bag))
                bag.Clear();
        }

        public static bool TryResolvePending(
            GameState game,
            IRng rng,
            out AlertResolution? result,
            out string? error,
            AlertResolveChoice? choice = null)
        {
            result = null;
            error = null;
            var bag = Bag(game);
            if (!game.UseAlertTokens)
            {
                error = "Alert Tokens are not in use.";
                return false;
            }
            if (game.PendingChoice != null && !bag.Resuming)
            {
                error = "Resolve the pending choice before continuing Alert Token resolution.";
                return false;
            }
            if (game.PendingAlertSectors.Count == 0)
            {
                error = "No Alert Tokens are pending resolution.";
                return false;
            }

            var sectorId = game.PendingAlertSectors[0];
            if (!TryResolveSector(game, sectorId, rng, choice ?? bag.Choice, out result, out error))
                return false;

            if (game.PendingAlertSectors.Count > 0
                && string.Equals(game.PendingAlertSectors[0], sectorId, StringComparison.OrdinalIgnoreCase))
            {
                game.PendingAlertSectors.RemoveAt(0);
            }
            return true;
        }

        /// <summary>
        /// Resume after Alliance ship / Reaver cutter / Safe Harbor / drive-off PendingChoice.
        /// Blue Sun p.5 / Kalidasa p.4: player to the right chooses and moves the ship.
        /// </summary>
        public static bool TryResume(
            GameState game,
            ChoiceSubmission submission,
            IRng rng,
            out AlertResolution? result,
            out string? error)
        {
            result = null;
            error = null;
            var bag = Bag(game);
            if (game.PendingChoice == null || string.IsNullOrWhiteSpace(bag.SectorId))
            {
                error = "No Alert Token choice is pending.";
                return false;
            }

            var kind = game.PendingChoice.Kind;
            var choice = bag.Choice ?? new AlertResolveChoice();
            var chooserId = game.PendingChoice.PlayerId;

            if (string.Equals(kind, PendingChoiceKinds.AlertAllianceShip, StringComparison.Ordinal))
            {
                if (!TryMergeAllianceShip(submission, choice, out error))
                    return false;
            }
            else if (string.Equals(kind, PendingChoiceKinds.AlertReaverCutter, StringComparison.Ordinal))
            {
                if (!TryMergeReaverCutter(submission, choice, out error))
                    return false;
            }
            else if (string.Equals(kind, PendingChoiceKinds.SectorDestination, StringComparison.Ordinal))
            {
                var contextId = game.PendingChoice.ContextId ?? "";
                var sector = submission.Value ?? submission.SelectedOptionId;
                if (string.IsNullOrWhiteSpace(sector))
                {
                    error = "Sector destination requires a Sector id.";
                    return false;
                }
                if (SectorDestinationContexts.TryParseAlertSafeHarbor(contextId, out _))
                    choice.AllianceCruiserToSectorId = sector;
                else if (string.Equals(
                             contextId,
                             SectorDestinationContexts.AlertDriveOffReaver,
                             StringComparison.Ordinal))
                    choice.DriveOffReaverToSectorId = sector;
                else
                {
                    error = "Unexpected Alert Token sector-destination context.";
                    return false;
                }
            }
            else
            {
                error = "No Alert Token choice is pending.";
                return false;
            }

            if (!game.TrySubmitChoice(chooserId, submission, out _, out error))
                return false;

            bag.Choice = choice;
            bag.Resuming = true;
            try
            {
                return TryResolvePending(game, rng, out result, out error, choice);
            }
            finally
            {
                bag.Resuming = false;
            }
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
            var bag = Bag(game);
            if (rng == null)
            {
                error = "Alert Token resolution requires a die roll.";
                return false;
            }
            if (!AlertTokenRules.SectorHasAlerts(game.Tokens, sectorId, game.UseAlertTokens)
                && !bag.Resuming)
            {
                error = "That Sector has no Alert Tokens to resolve.";
                return false;
            }

            choice ??= new AlertResolveChoice();
            var rolls = bag.CompletedRolls != null
                ? new List<AlertKindResolution>(bag.CompletedRolls)
                : new List<AlertKindResolution>();
            var endedFly = false;
            var allianceDone = rolls.Exists(r => r.Kind == AlertTokenKind.Alliance);

            var allianceCount = AlertTokenRules.EffectiveCount(
                game.Tokens, sectorId, AlertTokenKind.Alliance, includePermanentReaverSpace: false);
            if (allianceCount > 0 && !allianceDone)
            {
                if (!TryResolveKind(
                    game,
                    sectorId,
                    AlertTokenKind.Alliance,
                    allianceCount,
                    rng,
                    choice,
                    rolls,
                    out var allianceEndedFly,
                    out error))
                {
                    return false;
                }
                if (allianceEndedFly)
                    endedFly = true;
            }

            var reaverCount = AlertTokenRules.EffectiveCount(
                game.Tokens, sectorId, AlertTokenKind.Reaver, includePermanentReaverSpace: true);
            var reaverDone = rolls.Exists(r => r.Kind == AlertTokenKind.Reaver);
            if (reaverCount > 0 && !endedFly && !reaverDone)
            {
                if (!TryResolveKind(
                    game,
                    sectorId,
                    AlertTokenKind.Reaver,
                    reaverCount,
                    rng,
                    choice,
                    rolls,
                    out _,
                    out error))
                {
                    return false;
                }
            }

            game.Tokens = game.Tokens.ClearRemovableAlerts(sectorId);
            bag.Clear();
            result = new AlertResolution(sectorId, rolls, endedFly);
            return true;
        }

        private static bool TryResolveKind(
            GameState game,
            string sectorId,
            AlertTokenKind kind,
            int tokenCount,
            IRng rng,
            AlertResolveChoice choice,
            List<AlertKindResolution> rolls,
            out bool endedFly,
            out string? error)
        {
            endedFly = false;
            error = null;
            var bag = Bag(game);

            int die;
            if (kind == AlertTokenKind.Alliance && bag.AllianceDie != null)
                die = bag.AllianceDie.Value;
            else if (kind == AlertTokenKind.Reaver && bag.ReaverDie != null)
                die = bag.ReaverDie.Value;
            else
                die = Dice.D6(rng);

            var arrived = die <= tokenCount;
            TokenKind? ship = null;

            if (arrived)
            {
                if (kind == AlertTokenKind.Alliance)
                {
                    if (NeedsAllianceShipChoice(game, sectorId, choice))
                    {
                        StashForSuspend(bag, sectorId, kind, die, rolls, choice);
                        return SuspendAllianceShip(game, sectorId, out error);
                    }

                    if (!TryMoveAllianceAlertShip(game, sectorId, choice, out ship, out error))
                    {
                        if (game.PendingChoice != null)
                            StashForSuspend(bag, sectorId, kind, die, rolls, choice);
                        return false;
                    }

                    if (AlertTokenRules.IsOutlawShip(game.CurrentPlayer))
                    {
                        game.CurrentPlayer.SectorId = sectorId;
                        game.PendingNavDraws.Clear();
                        game.PendingAlertSectors.Clear();
                        game.PendingEncounter = ship;
                        game.PendingEncounterSectorId = sectorId;
                        endedFly = true;
                    }
                }
                else
                {
                    if (NeedsReaverCutterChoice(game, choice))
                    {
                        StashForSuspend(bag, sectorId, kind, die, rolls, choice);
                        return SuspendReaverCutter(game, sectorId, out error);
                    }

                    if (!TryMoveReaverAlertShip(game, sectorId, choice, out error))
                        return false;
                    ship = TokenKind.ReaverCutter;
                }
            }

            rolls.Add(new AlertKindResolution(kind, tokenCount, die, arrived, ship));
            // Kind complete — clear that die so a later kind rolls fresh.
            if (kind == AlertTokenKind.Alliance)
                bag.AllianceDie = null;
            else
                bag.ReaverDie = null;
            return true;
        }

        private static bool NeedsAllianceShipChoice(
            GameState game,
            string sectorId,
            AlertResolveChoice choice)
        {
            if (choice.AllianceShip != null)
                return false;
            if (!game.Map.TryGet(sectorId, out var sector))
                return false;
            if (sector.NavRegion != NavRegion.Alliance)
                return false;
            // Kalidasa p.4: In Alliance Space, PTR may choose Cruiser or Corvette when both in play.
            return game.Tokens.OperativeCorvetteSectorId != null;
        }

        private static bool NeedsReaverCutterChoice(GameState game, AlertResolveChoice choice) =>
            choice.ReaverCutterIndex == null && game.Tokens.ReaverCutterSectorIds.Count > 1;

        private static void StashForSuspend(
            SuspendBag bag,
            string sectorId,
            AlertTokenKind kind,
            int die,
            List<AlertKindResolution> rolls,
            AlertResolveChoice choice)
        {
            bag.SectorId = sectorId;
            bag.Choice = choice;
            bag.CompletedRolls = new List<AlertKindResolution>(rolls);
            if (kind == AlertTokenKind.Alliance)
                bag.AllianceDie = die;
            else
                bag.ReaverDie = die;
        }

        private static bool SuspendAllianceShip(GameState game, string sectorId, out string? error)
        {
            var ptr = game.PlayerToTheRightOf(game.CurrentPlayer.Id);
            var pending = new PendingChoice(
                ptr.Id,
                PendingChoiceKinds.AlertAllianceShip,
                contextId: sectorId,
                options: new[]
                {
                    AlertAllianceShipOptions.AllianceCruiser,
                    AlertAllianceShipOptions.OperativeCorvette
                },
                prompt: "Player to the right: choose Alliance Cruiser or Operative's Corvette.");
            if (!game.TrySetPendingChoice(pending, out error))
                return false;
            error = pending.Prompt;
            return false;
        }

        private static bool SuspendReaverCutter(GameState game, string sectorId, out string? error)
        {
            var options = new List<string>(game.Tokens.ReaverCutterSectorIds.Count);
            for (var i = 0; i < game.Tokens.ReaverCutterSectorIds.Count; i++)
                options.Add(i.ToString());
            var ptr = game.PlayerToTheRightOf(game.CurrentPlayer.Id);
            var pending = new PendingChoice(
                ptr.Id,
                PendingChoiceKinds.AlertReaverCutter,
                contextId: sectorId,
                options: options,
                prompt: "Player to the right: choose which Reaver Cutter to move.");
            if (!game.TrySetPendingChoice(pending, out error))
                return false;
            error = pending.Prompt;
            return false;
        }

        private static bool TryMergeAllianceShip(
            ChoiceSubmission submission,
            AlertResolveChoice choice,
            out string? error)
        {
            error = null;
            var id = submission.SelectedOptionId ?? submission.Value;
            if (string.Equals(id, AlertAllianceShipOptions.AllianceCruiser, StringComparison.OrdinalIgnoreCase))
            {
                choice.AllianceShip = TokenKind.AllianceCruiser;
                return true;
            }
            if (string.Equals(id, AlertAllianceShipOptions.OperativeCorvette, StringComparison.OrdinalIgnoreCase))
            {
                choice.AllianceShip = TokenKind.OperativeCorvette;
                return true;
            }
            error = "Choose alliance-cruiser or operative-corvette.";
            return false;
        }

        private static bool TryMergeReaverCutter(
            ChoiceSubmission submission,
            AlertResolveChoice choice,
            out string? error)
        {
            error = null;
            var id = submission.SelectedOptionId ?? submission.Value;
            if (!int.TryParse(id, out var index) || index < 0)
            {
                error = "Choose a Reaver Cutter index.";
                return false;
            }
            choice.ReaverCutterIndex = index;
            return true;
        }

        private static bool TryMoveReaverAlertShip(
            GameState game,
            string sectorId,
            AlertResolveChoice choice,
            out string? error)
        {
            var index = choice.ReaverCutterIndex ?? 0;
            if (!game.Tokens.TryMoveReaverCutter(
                    sectorId,
                    out var moved,
                    out error,
                    index,
                    leaveReaverAlertToken: game.UseAlertTokens))
            {
                if (game.Tokens.EncounterAt(sectorId) != TokenKind.ReaverCutter)
                    return false;
                error = null;
                return true;
            }
            game.Tokens = moved;
            return true;
        }

        private static bool TryMoveAllianceAlertShip(
            GameState game,
            string sectorId,
            AlertResolveChoice choice,
            out TokenKind? ship,
            out string? error)
        {
            ship = null;
            error = null;
            if (!game.Map.TryGet(sectorId, out var sector))
            {
                error = $"Unknown sector '{sectorId}'.";
                return false;
            }

            var preferred = choice.AllianceShip;
            TokenKind selected;
            if (sector.NavRegion == NavRegion.Alliance)
            {
                if (preferred == TokenKind.OperativeCorvette)
                {
                    if (game.Tokens.OperativeCorvetteSectorId == null)
                    {
                        error = "Operative's Corvette is not on the board.";
                        return false;
                    }
                    selected = TokenKind.OperativeCorvette;
                }
                else if (preferred == TokenKind.AllianceCruiser || preferred == null)
                {
                    selected = TokenKind.AllianceCruiser;
                }
                else
                {
                    error = "Alliance Alert in Alliance Space must choose the Cruiser or Corvette.";
                    return false;
                }
            }
            else
            {
                if (preferred == TokenKind.AllianceCruiser
                    && game.Tokens.OperativeCorvetteSectorId != null)
                {
                    error = "In Border or Rim Space, only the Operative's Corvette may be chosen.";
                    return false;
                }
                if (game.Tokens.OperativeCorvetteSectorId != null)
                    selected = TokenKind.OperativeCorvette;
                else
                    selected = TokenKind.AllianceCruiser;
            }

            if (selected == TokenKind.AllianceCruiser)
            {
                if (HavenRules.NeedsSafeHarborRedirect(game, sectorId, choice.AllianceCruiserToSectorId))
                {
                    var ptr = game.PlayerToTheRightOf(game.CurrentPlayer.Id);
                    var options = HavenRules.EligibleSafeHarborRedirects(game, sectorId);
                    var pending = new PendingChoice(
                        ptr.Id,
                        PendingChoiceKinds.SectorDestination,
                        contextId: SectorDestinationContexts.AlertSafeHarbor(sectorId),
                        options: options.Count > 0 ? options : null,
                        prompt: "Safe Harbor: player to the right places the Cruiser in an adjacent Sector.");
                    if (!game.TrySetPendingChoice(pending, out error))
                        return false;
                    error = pending.Prompt;
                    return false;
                }

                if (!HavenRules.TryPlaceAllianceCruiser(
                    game,
                    sectorId,
                    choice.AllianceCruiserToSectorId,
                    out error))
                    return false;
                ship = TokenKind.AllianceCruiser;
                return true;
            }

            if (game.Tokens.EncounterAt(sectorId) == TokenKind.ReaverCutter
                && string.IsNullOrWhiteSpace(choice.DriveOffReaverToSectorId))
            {
                var ptr = game.PlayerToTheRightOf(game.CurrentPlayer.Id);
                var pending = new PendingChoice(
                    ptr.Id,
                    PendingChoiceKinds.SectorDestination,
                    contextId: SectorDestinationContexts.AlertDriveOffReaver,
                    prompt: "Choose a Reaver Starting Zone for the driven-off Cutter.");
                if (!game.TrySetPendingChoice(pending, out error))
                    return false;
                error = pending.Prompt;
                return false;
            }

            if (!game.Tokens.TryMoveOperativeCorvette(
                sectorId,
                out var moved,
                out error,
                choice.DriveOffReaverToSectorId))
                return false;
            game.Tokens = moved;
            ship = TokenKind.OperativeCorvette;
            return true;
        }
    }
}
