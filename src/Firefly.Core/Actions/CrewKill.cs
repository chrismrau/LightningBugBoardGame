using System;
using System.Collections.Generic;
using Firefly.Core.Abilities;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Kill / Medic hooks. Victim selection uses <see cref="PendingChoiceKinds.KillVictim"/>
    /// when Kill N requires a choice among crew; <see cref="VictimCrewIds"/> remains the
    /// thin scripted / resume payload. Medic Check and Med Foam hooks stay on the chosen
    /// victims (GF9 p.18–19; FAQ 4.1 p.3 Leaders).
    /// </summary>
    public sealed class KillChoice
    {
        /// <summary>
        /// Ordered crew ids to subject to kill events (length up to the printed Kill N).
        /// Set by the player via PendingChoice resume, or by tests / thin hooks.
        /// </summary>
        public IList<string>? VictimCrewIds { get; set; }

        /// <summary>
        /// When set false, skip Medic Check even if a Medic is present.
        /// Default (null/true): always attempt when a Medic is on the ship (GF9).
        /// Thin hook for PendingChoice / Med Foam decline flows.
        /// </summary>
        public bool? AttemptMedicCheck { get; set; }

        /// <summary>
        /// Med Foam-style: count as a successful Medic Check without rolling.
        /// Thin hook until PendingChoice consumer (c) (discard wiring later).
        /// </summary>
        public bool CountAsSuccessfulMedicCheck { get; set; }
    }

    public sealed class KillResult
    {
        public CrewOutcome Outcome { get; }
        public int? MedicDie { get; }
        public int MedicTotal { get; }
        public bool MedicAttempted { get; }
        public bool MedicSaved { get; }

        public KillResult(
            CrewOutcome outcome,
            int? medicDie = null,
            int medicTotal = 0,
            bool medicAttempted = false,
            bool medicSaved = false)
        {
            Outcome = outcome;
            MedicDie = medicDie;
            MedicTotal = medicTotal;
            MedicAttempted = medicAttempted;
            MedicSaved = medicSaved;
        }

        public static KillResult None { get; } = new KillResult(CrewOutcome.None);
    }

    /// <summary>
    /// Unified kill path: Medic Check (GF9 p.18–19 / Director's Cut p.27), then
    /// remove-from-play or Leaders are REALLY Lucky (FAQ 4.1 p.3).
    /// Kill N victim selection suspends via <see cref="PendingChoiceKinds.KillVictim"/>.
    /// </summary>
    public static class CrewKill
    {
        public const int MedicSaveTarget = 5;

        public static bool HasMedic(PlayerState player) =>
            player.Roster.HasProfession("Medic");

        /// <summary>
        /// Medic Check bonuses from typed abilities (Simon medicCheckBonus +2). Mandatory.
        /// </summary>
        public static int MedicBonus(PlayerState player) =>
            AbilityDispatcher.MedicCheckBonus(player);

        /// <summary>
        /// True when Kill <paramref name="count"/> requires the player to pick among crew.
        /// False when <see cref="KillChoice.VictimCrewIds"/> is already set, count is 0,
        /// the roster is empty, or count covers the whole roster (no alternatives).
        /// </summary>
        public static bool NeedsVictimChoice(PlayerState player, int count, KillChoice? choice = null)
        {
            if (count <= 0 || player.Roster.Count <= 0)
                return false;
            if (choice?.VictimCrewIds != null && choice.VictimCrewIds.Count > 0)
                return false;
            // No alternatives when every crew on the ship must be subjected to the kill event.
            if (count >= player.Roster.Count)
                return false;
            return true;
        }

        /// <summary>
        /// Crew ids currently eligible for a Kill N pick (whole roster, including Leader).
        /// FAQ 4.1 p.3: Leader may take the hit (Medic / Really Lucky).
        /// </summary>
        public static IReadOnlyList<string> EligibleVictimIds(PlayerState player)
        {
            var ids = new List<string>(player.Roster.Count);
            foreach (var member in player.Roster.Members)
                ids.Add(member.Id);
            return ids;
        }

        /// <summary>
        /// Suspend with <see cref="PendingChoiceKinds.KillVictim"/>. 
        /// <see cref="PendingChoice.ContextId"/> holds the printed kill count.
        /// Options stay null (multi-select via <see cref="ChoiceSubmission.Values"/>).
        /// </summary>
        public static bool TrySuspendVictimChoice(
            GameState game,
            PlayerState player,
            int count,
            out string? error,
            string? prompt = null)
        {
            error = null;
            if (count <= 0)
            {
                error = "Kill count must be positive to suspend victim choice.";
                return false;
            }
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.KillVictim,
                contextId: count.ToString(),
                options: null,
                prompt: prompt ?? $"Choose {count} crew to kill.");
            return game.TrySetPendingChoice(pending, out error);
        }

        /// <summary>
        /// Validate <see cref="ChoiceSubmission.Values"/> as victim ids and merge into
        /// <paramref name="choice"/> (creating one when null). Does not clear PendingChoice.
        /// </summary>
        public static bool TryMergeVictimSubmission(
            PlayerState player,
            int count,
            ChoiceSubmission submission,
            KillChoice? choice,
            out KillChoice merged,
            out string? error)
        {
            merged = choice ?? new KillChoice();
            error = null;
            if (submission == null)
            {
                error = "A choice submission is required.";
                return false;
            }
            if (submission.Values == null || submission.Values.Count == 0)
            {
                error = "Victim crew ids are required.";
                return false;
            }

            var want = count;
            if (want > player.Roster.Count)
                want = player.Roster.Count;
            if (submission.Values.Count != want)
            {
                error = $"Choose exactly {want} crew to kill.";
                return false;
            }

            var ids = new List<string>(want);
            foreach (var id in submission.Values)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    error = "Victim crew id is required.";
                    return false;
                }
                if (player.Roster.Find(id) == null)
                {
                    error = $"Crew '{id}' is not on the ship.";
                    return false;
                }
                foreach (var existing in ids)
                {
                    if (string.Equals(existing, id, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Duplicate victim crew id.";
                        return false;
                    }
                }
                ids.Add(id);
            }

            merged.VictimCrewIds = ids;
            return true;
        }

        /// <summary>
        /// Resume a pending <see cref="PendingChoiceKinds.KillVictim"/>: submit, merge victims,
        /// apply Medic-aware kills. Parent actions that suspended mid-resolve should prefer
        /// their own resume entry (re-enter with <see cref="KillChoice.VictimCrewIds"/>).
        /// </summary>
        public static bool TryResumeKillUpTo(
            GameState game,
            ChoiceSubmission submission,
            IRng rng,
            out int killed,
            out string? error,
            KillChoice? baseChoice = null)
        {
            killed = 0;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.KillVictim,
                    StringComparison.Ordinal))
            {
                error = "No kill-victim choice is pending.";
                return false;
            }

            var player = game.GetPlayer(game.PendingChoice.PlayerId);
            if (!TryParseKillCount(game.PendingChoice.ContextId, out var count))
            {
                error = "Kill-victim context is missing the kill count.";
                return false;
            }

            if (!TryMergeVictimSubmission(player, count, submission, baseChoice, out var merged, out error))
                return false;

            if (!game.TrySubmitChoice(player.Id, submission, out _, out error))
                return false;

            killed = ApplyKillUpTo(game, player, count, rng, merged);
            return true;
        }

        public static KillResult Apply(
            GameState game,
            PlayerState player,
            CrewMember member,
            IRng rng,
            KillChoice? choice = null)
        {
            if (member == null || player.Roster.Find(member.Id) == null)
                return KillResult.None;

            var attemptMedic = choice?.AttemptMedicCheck ?? true;
            if (HasMedic(player) && attemptMedic)
            {
                int die;
                int total;
                bool saved;
                if (choice != null && choice.CountAsSuccessfulMedicCheck)
                {
                    die = MedicSaveTarget;
                    total = MedicSaveTarget;
                    saved = true;
                }
                else
                {
                    die = Dice.D6(rng);
                    total = die + MedicBonus(player);
                    saved = total >= MedicSaveTarget;
                }

                if (saved)
                    return new KillResult(CrewOutcome.ReturnedToShip, die, total, medicAttempted: true, medicSaved: true);

                var failed = FinalizeKill(game, player, member);
                return new KillResult(failed, die, total, medicAttempted: true, medicSaved: false);
            }

            return new KillResult(FinalizeKill(game, player, member));
        }

        /// <summary>
        /// Subject up to <paramref name="count"/> crew to kill events.
        /// When victim selection is required and <see cref="KillChoice.VictimCrewIds"/> is
        /// unset, suspends via PendingChoice and returns false (killed = 0).
        /// </summary>
        public static bool TryKillUpTo(
            GameState game,
            PlayerState player,
            int count,
            IRng rng,
            out int killed,
            out string? error,
            KillChoice? choice = null)
        {
            killed = 0;
            error = null;
            if (count <= 0)
                return true;

            if (NeedsVictimChoice(player, count, choice))
            {
                if (!TrySuspendVictimChoice(game, player, count, out error))
                    return false;
                error = "Choose which crew are killed.";
                return false;
            }

            killed = ApplyKillUpTo(game, player, count, rng, choice);
            return true;
        }

        /// <summary>
        /// Apply Kill N when victims are already chosen or no choice is required.
        /// Throws if a PendingChoice victim pick is still required — use <see cref="TryKillUpTo"/>.
        /// </summary>
        public static int KillUpTo(
            GameState game,
            PlayerState player,
            int count,
            IRng rng,
            KillChoice? choice = null)
        {
            if (NeedsVictimChoice(player, count, choice))
            {
                throw new InvalidOperationException(
                    "Kill victim selection requires PendingChoice or KillChoice.VictimCrewIds.");
            }
            return ApplyKillUpTo(game, player, count, rng, choice);
        }

        public static int KillAll(
            GameState game,
            PlayerState player,
            IRng rng,
            KillChoice? choice = null)
        {
            var snapshot = new List<CrewMember>(player.Roster.Members);
            var killed = 0;
            foreach (var victim in snapshot)
            {
                var member = player.Roster.Find(victim.Id);
                if (member == null)
                    continue;
                var result = Apply(game, player, member, rng, choice);
                if (result.Outcome == CrewOutcome.Killed)
                    killed++;
            }
            return killed;
        }

        public static bool TryParseKillCount(string? contextId, out int count)
        {
            count = 0;
            return !string.IsNullOrWhiteSpace(contextId) && int.TryParse(contextId, out count) && count > 0;
        }

        private static int ApplyKillUpTo(
            GameState game,
            PlayerState player,
            int count,
            IRng rng,
            KillChoice? choice)
        {
            if (count <= 0)
                return 0;

            var victims = SelectVictims(player, count, choice);
            var killed = 0;
            foreach (var victim in victims)
            {
                var member = player.Roster.Find(victim.Id);
                if (member == null)
                    continue;
                var result = Apply(game, player, member, rng, choice);
                if (result.Outcome == CrewOutcome.Killed)
                    killed++;
            }
            return killed;
        }

        private static CrewOutcome FinalizeKill(GameState game, PlayerState player, CrewMember member)
        {
            var outcome = player.Roster.Kill(member);
            if (outcome == CrewOutcome.Killed)
                game.RemovedFromPlay.Add(member.Id);
            return outcome;
        }

        private static List<CrewMember> SelectVictims(PlayerState player, int count, KillChoice? choice)
        {
            var list = new List<CrewMember>();
            if (choice?.VictimCrewIds != null && choice.VictimCrewIds.Count > 0)
            {
                foreach (var id in choice.VictimCrewIds)
                {
                    if (list.Count >= count)
                        break;
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    var member = player.Roster.Find(id);
                    if (member == null)
                        continue;
                    var already = false;
                    foreach (var existing in list)
                    {
                        if (existing.Id == member.Id)
                        {
                            already = true;
                            break;
                        }
                    }
                    if (!already)
                        list.Add(member);
                }
                return list;
            }

            // No player choice: kill count covers the whole roster (or roster is empty).
            for (var i = player.Roster.Count - 1; i >= 0 && list.Count < count; i--)
                list.Add(player.Roster.Members[i]);
            return list;
        }
    }
}
