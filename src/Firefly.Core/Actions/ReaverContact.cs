using System.Collections.Generic;
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
    /// Kill N victim picks suspend via <see cref="PendingChoiceKinds.KillVictim"/>.
    /// </summary>
    public static class ReaverContact
    {
        public const int FightTarget = 8;

        private static bool _resumingKillVictims;
        private static string? _pendingEvadeToSectorId;
        private static SkillCheckResult? _pendingFight;
        private static int _pendingKillCount;
        private static int _pendingPassengers;
        private static int _pendingFugitives;
        private static KillChoice? _pendingKillChoice;
        private static bool _pendingIsEncounterResolve;

        public static bool TryResolve(
            GameState game,
            IRng rng,
            string evadeToSectorId,
            out ReaverContactResult? result,
            out string? error,
            KillChoice? killChoice = null)
        {
            result = null;
            error = null;
            if (game.PendingEncounter != TokenKind.ReaverCutter)
            {
                error = "No Reaver Cutter encounter is pending.";
                return false;
            }
            if (game.PendingChoice != null && !_resumingKillVictims)
            {
                error = "Resolve the pending choice before continuing Reaver Contact.";
                return false;
            }

            var player = game.CurrentPlayer;
            var sector = game.PendingEncounterSectorId ?? player.SectorId;
            player.SectorId = sector;

            if (!TryApply(
                    game,
                    player,
                    rng,
                    evadeToSectorId,
                    out result,
                    out error,
                    killChoice,
                    isEncounterResolve: true,
                    allowSuspend: true))
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
            out string? error,
            KillChoice? killChoice = null)
        {
            if (game.PendingChoice != null && !_resumingKillVictims)
            {
                result = null;
                error = "Resolve the pending choice before continuing Reaver Contact.";
                return false;
            }
            return TryApply(
                game,
                game.CurrentPlayer,
                rng,
                evadeToSectorId,
                out result,
                out error,
                killChoice,
                isEncounterResolve: false,
                allowSuspend: true);
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.KillVictim"/> during Reaver Contact.
        /// </summary>
        public static bool TryResumeKillVictims(
            GameState game,
            ChoiceSubmission submission,
            IRng rng,
            out ReaverContactResult? result,
            out string? error)
        {
            result = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.KillVictim,
                    System.StringComparison.Ordinal))
            {
                error = "No kill-victim choice is pending.";
                return false;
            }
            if (_pendingFight == null || string.IsNullOrWhiteSpace(_pendingEvadeToSectorId))
            {
                error = "No Reaver Contact kill-victim resume state is stored.";
                return false;
            }
            if (!CrewKill.TryParseKillCount(game.PendingChoice.ContextId, out var count))
            {
                error = "Kill-victim context is missing the kill count.";
                return false;
            }

            var player = game.GetPlayer(game.PendingChoice.PlayerId);
            if (!CrewKill.TryMergeVictimSubmission(
                    player, count, submission, _pendingKillChoice, out var merged, out error))
                return false;

            if (!game.TrySubmitChoice(player.Id, submission, out _, out error))
                return false;

            _pendingKillChoice = merged;

            // Optional Med Foam before mutating passengers / crew.
            if (CrewKill.NeedsMedFoamChoice(game, player, _pendingKillCount, merged))
            {
                if (!CrewKill.TrySuspendMedFoamChoice(game, player, _pendingKillCount, out error))
                {
                    ClearPendingResume();
                    return false;
                }
                error = "Choose whether to discard Med Foam for a successful Medic Check.";
                return false;
            }

            return FinishPendingContact(game, player, rng, merged, out result, out error);
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.MedFoamDiscard"/> during Reaver Contact.
        /// </summary>
        public static bool TryResumeMedFoam(
            GameState game,
            ChoiceSubmission submission,
            IRng rng,
            out ReaverContactResult? result,
            out string? error)
        {
            result = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.MedFoamDiscard,
                    System.StringComparison.Ordinal))
            {
                error = "No Med Foam choice is pending.";
                return false;
            }
            if (_pendingFight == null || string.IsNullOrWhiteSpace(_pendingEvadeToSectorId))
            {
                error = "No Reaver Contact Med Foam resume state is stored.";
                return false;
            }

            var player = game.GetPlayer(game.PendingChoice.PlayerId);
            if (!CrewKill.TryMergeMedFoamSubmission(
                    submission, _pendingKillChoice, out var merged, out error))
                return false;

            if (!game.TrySubmitChoice(player.Id, submission, out _, out error))
                return false;

            return FinishPendingContact(game, player, rng, merged, out result, out error);
        }

        private static bool FinishPendingContact(
            GameState game,
            PlayerState player,
            IRng rng,
            KillChoice merged,
            out ReaverContactResult? result,
            out string? error)
        {
            result = null;
            var evadeTo = _pendingEvadeToSectorId!;
            var fight = _pendingFight!;
            var killCount = _pendingKillCount;
            var passengers = _pendingPassengers;
            var fugitives = _pendingFugitives;
            var isEncounter = _pendingIsEncounterResolve;
            ClearPendingResume();

            _resumingKillVictims = true;
            try
            {
                BoundFugitives.RemoveAllFromPlay(game, player);
                player.Passengers = 0;
                player.Fugitives = 0;
                if (!CrewKill.TryKillUpTo(
                        game, player, killCount, rng, out var crewKilled, out error, merged))
                    return false;
                if (!FlightEvade.TryMove(game, player, evadeTo, out error))
                    return false;

                // Mid-Nav immediate Contact and encounter resolve both end the Fly Nav queue.
                game.PendingEncounter = null;
                game.PendingEncounterSectorId = null;
                game.PendingNavDraws.Clear();

                result = new ReaverContactResult(passengers, fugitives, fight, crewKilled, evadeTo);
                return true;
            }
            finally
            {
                _resumingKillVictims = false;
            }
        }

        private static bool TryApply(
            GameState game,
            PlayerState player,
            IRng rng,
            string evadeToSectorId,
            out ReaverContactResult? result,
            out string? error,
            KillChoice? killChoice,
            bool isEncounterResolve,
            bool allowSuspend)
        {
            result = null;
            error = null;

            if (!FlightEvade.CanMove(game, player, evadeToSectorId, out error))
                return false;

            // Fight first so Kill N is known before mutating passengers / crew.
            var check = new SkillCheck(Skill.Fight, FightTarget);
            var fight = check.Resolve(player, rng);
            var killCount = fight.Success ? 1 : 2;

            if (CrewKill.NeedsVictimChoice(player, killCount, killChoice))
            {
                if (!allowSuspend)
                {
                    error =
                        "Reaver Contact Kill N requires KillChoice.VictimCrewIds.";
                    return false;
                }

                // Defer passenger / fugitive / crew removal until victims are chosen.
                _pendingEvadeToSectorId = evadeToSectorId;
                _pendingFight = fight;
                _pendingKillCount = killCount;
                _pendingPassengers = player.Passengers;
                _pendingFugitives = player.Fugitives + BoundFugitives.Count(player);
                _pendingKillChoice = killChoice;
                _pendingIsEncounterResolve = isEncounterResolve;
                if (!CrewKill.TrySuspendVictimChoice(game, player, killCount, out error))
                {
                    ClearPendingResume();
                    return false;
                }
                error = "Choose which crew are killed.";
                return false;
            }

            if (CrewKill.NeedsMedFoamChoice(game, player, killCount, killChoice))
            {
                if (!allowSuspend)
                {
                    error =
                        "Reaver Contact Med Foam requires KillChoice.UseMedFoam.";
                    return false;
                }

                _pendingEvadeToSectorId = evadeToSectorId;
                _pendingFight = fight;
                _pendingKillCount = killCount;
                _pendingPassengers = player.Passengers;
                _pendingFugitives = player.Fugitives + BoundFugitives.Count(player);
                _pendingKillChoice = killChoice;
                _pendingIsEncounterResolve = isEncounterResolve;
                if (!CrewKill.TrySuspendMedFoamChoice(game, player, killCount, out error))
                {
                    ClearPendingResume();
                    return false;
                }
                error = "Choose whether to discard Med Foam for a successful Medic Check.";
                return false;
            }

            var passengersKilled = player.Passengers;
            // PBH p.12: if Reavers Kill Passenger and Fugitive tokens, Bound Fugitives leave play.
            var fugitivesKilled = player.Fugitives + BoundFugitives.RemoveAllFromPlay(game, player);
            player.Passengers = 0;
            player.Fugitives = 0;

            if (!CrewKill.TryKillUpTo(
                    game, player, killCount, rng, out var crewKilled, out error, killChoice))
                return false;

            if (!FlightEvade.TryMove(game, player, evadeToSectorId, out error))
                return false;

            result = new ReaverContactResult(
                passengersKilled, fugitivesKilled, fight, crewKilled, evadeToSectorId);
            return true;
        }

        private static void ClearPendingResume()
        {
            _pendingEvadeToSectorId = null;
            _pendingFight = null;
            _pendingKillCount = 0;
            _pendingPassengers = 0;
            _pendingFugitives = 0;
            _pendingKillChoice = null;
            _pendingIsEncounterResolve = false;
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
