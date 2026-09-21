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
    /// thin scripted / resume payload. Optional Med Foam discard uses
    /// <see cref="PendingChoiceKinds.MedFoamDiscard"/> (Supplies.tsv: discard to succeed Medic Check).
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
        /// </summary>
        public bool? AttemptMedicCheck { get; set; }

        /// <summary>
        /// Scripted / test hook: count as a successful Medic Check without rolling or discarding.
        /// Prefer <see cref="UseMedFoam"/> for the printed discard path.
        /// </summary>
        public bool CountAsSuccessfulMedicCheck { get; set; }

        /// <summary>
        /// Med Foam discard-to-succeed. Null = undecided (PendingChoice when foam is usable).
        /// True = discard one carried Med Foam and succeed the first Medic Check in this
        /// kill batch (printed: one discard = one successful Medic Check). False = decline
        /// foam and roll Medic Checks normally.
        /// </summary>
        public bool? UseMedFoam { get; set; }

        /// <summary>
        /// Fully Equipped Med Bay may: null = undecided (PendingChoice), true = re-roll the
        /// current Medic Check die once, false = keep the first die.
        /// GF9: one Medic Check per Crew Killed; Med Bay may re-roll each Medic Check.
        /// </summary>
        public bool? AcceptMedicReroll { get; set; }

        /// <summary>First Medic die while awaiting Med Bay re-roll PendingChoice.</summary>
        public int? MedicFirstDie { get; set; }

        /// <summary>Victim id whose Medic Check is awaiting Med Bay re-roll.</summary>
        public string? MedicPendingVictimId { get; set; }

        /// <summary>
        /// Meadows Hero Worship may: null = undecided, true = Kill Meadows instead of the
        /// first threatened victim this batch, false = decline for the whole Kill N batch.
        /// </summary>
        public bool? AcceptMeadowsRedirect { get; set; }

        /// <summary>True after Meadows was offered once this Kill N batch.</summary>
        public bool MeadowsOfferedThisBatch { get; set; }

        /// <summary>Victim ids already fully resolved in this Kill N (Med Bay multi-check).</summary>
        public IList<string>? ResolvedVictimIds { get; set; }
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
    /// Optional Med Foam discard suspends via <see cref="PendingChoiceKinds.MedFoamDiscard"/>.
    /// </summary>
    public static class CrewKill
    {
        public const int MedicSaveTarget = 5;
        public const string MedFoamGearName = "Med Foam";

        public static bool HasMedic(PlayerState player) =>
            player.Roster.HasProfession("Medic");

        /// <summary>
        /// Medic Check bonuses from typed abilities (Simon medicCheckBonus +2). Mandatory.
        /// </summary>
        public static int MedicBonus(PlayerState player) =>
            AbilityDispatcher.MedicCheckBonus(player);

        /// <summary>
        /// Carried Med Foam may be used (FAQ 4.1 p.2: Onboard Ship Gear may not be used).
        /// </summary>
        public static bool HasUsableMedFoam(GameState game, PlayerState player) =>
            FindUsableMedFoamId(game, player) != null;

        public static string? FindUsableMedFoamId(GameState game, PlayerState player)
        {
            if (game.Gear == null)
                return null;
            foreach (var id in player.Gear)
            {
                if (!GearCarriage.IsCarried(player, id))
                    continue;
                if (!game.Gear.TryGet(id, out var gear))
                    continue;
                if (string.Equals(gear.Name, MedFoamGearName, StringComparison.OrdinalIgnoreCase))
                    return id;
            }
            return null;
        }

        /// <summary>
        /// Discard one carried Med Foam (remove from ship + carriers; leave play).
        /// Supplies.tsv: "Discard to count as having made a successful Medic Check."
        /// </summary>
        public static bool TryDiscardMedFoam(GameState game, PlayerState player, out string? error)
        {
            error = null;
            var id = FindUsableMedFoamId(game, player);
            if (id == null)
            {
                error = "No carried Med Foam to discard.";
                return false;
            }

            player.Gear.Remove(id);
            player.GearCarriers.Remove(id);
            game.RemovedFromPlay.Add(id);
            GearCarriage.RefreshSkillBonuses(game, player);
            return true;
        }

        /// <summary>
        /// True when a Medic Check is about to run, Med Foam is usable, and the player has
        /// not yet chosen discard vs decline. Victim selection must already be resolved.
        /// </summary>
        public static bool NeedsMedFoamChoice(
            GameState game,
            PlayerState player,
            int count,
            KillChoice? choice = null)
        {
            if (count <= 0 || player.Roster.Count <= 0)
                return false;
            if (!HasMedic(player))
                return false;
            if (choice?.AttemptMedicCheck == false)
                return false;
            if (choice != null && choice.CountAsSuccessfulMedicCheck)
                return false;
            if (choice?.UseMedFoam != null)
                return false;
            if (NeedsVictimChoice(player, count, choice))
                return false;
            return HasUsableMedFoam(game, player);
        }

        /// <summary>
        /// Suspend with <see cref="PendingChoiceKinds.MedFoamDiscard"/>.
        /// <see cref="PendingChoice.ContextId"/> holds the printed kill count.
        /// </summary>
        public static bool TrySuspendMedFoamChoice(
            GameState game,
            PlayerState player,
            int count,
            out string? error,
            string? prompt = null)
        {
            error = null;
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.MedFoamDiscard,
                contextId: count.ToString(),
                options: new[] { MedFoamDiscardOptions.Discard, MedFoamDiscardOptions.Decline },
                prompt: prompt
                    ?? "Discard Med Foam to count as a successful Medic Check?");
            return game.TrySetPendingChoice(pending, out error);
        }

        /// <summary>
        /// Merge discard/decline into <paramref name="choice"/>. Does not clear PendingChoice
        /// or discard gear (discard happens when applying kills).
        /// </summary>
        public static bool TryMergeMedFoamSubmission(
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

            bool use;
            if (submission.Accepted != null)
                use = submission.Accepted.Value;
            else if (!string.IsNullOrWhiteSpace(submission.SelectedOptionId))
            {
                if (string.Equals(
                        submission.SelectedOptionId,
                        MedFoamDiscardOptions.Discard,
                        StringComparison.Ordinal))
                    use = true;
                else if (string.Equals(
                             submission.SelectedOptionId,
                             MedFoamDiscardOptions.Decline,
                             StringComparison.Ordinal))
                    use = false;
                else
                {
                    error = "Med Foam choice must be discard or decline.";
                    return false;
                }
            }
            else
            {
                error = "Med Foam discard or decline is required.";
                return false;
            }

            merged.UseMedFoam = use;
            return true;
        }

        /// <summary>
        /// Resume a pending Med Foam choice and apply Kill N with Medic / foam.
        /// Parent actions that suspended mid-resolve should prefer their own resume entry.
        /// </summary>
        public static bool TryResumeMedFoamKillUpTo(
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
                    PendingChoiceKinds.MedFoamDiscard,
                    StringComparison.Ordinal))
            {
                error = "No Med Foam choice is pending.";
                return false;
            }

            var player = game.GetPlayer(game.PendingChoice.PlayerId);
            if (!TryParseKillCount(game.PendingChoice.ContextId, out var count))
            {
                error = "Med Foam context is missing the kill count.";
                return false;
            }

            if (!TryMergeMedFoamSubmission(submission, baseChoice, out var merged, out error))
                return false;

            if (!game.TrySubmitChoice(player.Id, submission, out _, out error))
                return false;

            return TryKillUpTo(game, player, count, rng, out killed, out error, merged);
        }

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

            return TryKillUpTo(game, player, count, rng, out killed, out error, merged);
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
                    if (choice?.MedicFirstDie is int first
                        && string.Equals(
                            choice.MedicPendingVictimId, member.Id, StringComparison.Ordinal))
                    {
                        die = first;
                        if (choice.AcceptMedicReroll == true)
                            die = Dice.D6(rng);
                    }
                    else
                    {
                        die = Dice.D6(rng);
                        // Med Bay may: when undecided, callers must suspend via TryKillUpTo.
                        // Direct Apply with null AcceptMedicReroll keeps the first die (decline).
                        if (AbilityDispatcher.HasMedicCheckReroll(game, player)
                            && choice?.AcceptMedicReroll == true)
                            die = Dice.D6(rng);
                    }

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
        /// FAQ 4.1 p.8 may: after the Medic die for the pending victim, suspend when Med Bay present.
        /// GF9: one Medic Check per Crew Killed — each check may be re-rolled.
        /// </summary>
        public static bool NeedsMedicRerollChoice(
            GameState game,
            PlayerState player,
            KillChoice? choice = null)
        {
            if (choice?.AcceptMedicReroll != null)
                return false;
            if (choice?.CountAsSuccessfulMedicCheck == true)
                return false;
            if (choice?.AttemptMedicCheck == false)
                return false;
            if (!HasMedic(player))
                return false;
            return AbilityDispatcher.HasMedicCheckReroll(game, player);
        }

        public static bool NeedsMeadowsRedirectChoice(PlayerState player, KillChoice? choice = null)
        {
            if (choice?.MeadowsOfferedThisBatch == true)
                return false;
            if (choice?.AcceptMeadowsRedirect != null)
                return false;
            var meadows = AbilityDispatcher.FindMeadowsRedirect(player);
            return meadows != null;
        }

        public static bool TrySuspendMedicRerollChoice(
            GameState game,
            PlayerState player,
            int count,
            out string? error,
            string? prompt = null)
        {
            error = null;
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.MedicReroll,
                contextId: count.ToString(),
                options: new[] { SkillRerollOptions.Keep, SkillRerollOptions.Reroll },
                prompt: prompt ?? "Re-roll this Medic Check?");
            return game.TrySetPendingChoice(pending, out error);
        }

        public static bool TrySuspendMeadowsRedirectChoice(
            GameState game,
            PlayerState player,
            int count,
            out string? error,
            string? prompt = null)
        {
            error = null;
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.MeadowsRedirect,
                contextId: count.ToString(),
                options: new[] { MeadowsRedirectOptions.KillMeadows, MeadowsRedirectOptions.Decline },
                prompt: prompt ?? "Kill Meadows instead?");
            return game.TrySetPendingChoice(pending, out error);
        }

        public static bool TryMergeMedicRerollSubmission(
            ChoiceSubmission submission,
            KillChoice? choice,
            out KillChoice merged,
            out string? error)
        {
            merged = CloneKillChoice(choice);
            error = null;
            if (submission == null)
            {
                error = "A choice submission is required.";
                return false;
            }

            bool accept;
            if (submission.Accepted != null)
                accept = submission.Accepted.Value;
            else if (!string.IsNullOrWhiteSpace(submission.SelectedOptionId))
            {
                if (string.Equals(
                        submission.SelectedOptionId,
                        SkillRerollOptions.Reroll,
                        StringComparison.Ordinal))
                    accept = true;
                else if (string.Equals(
                             submission.SelectedOptionId,
                             SkillRerollOptions.Keep,
                             StringComparison.Ordinal))
                    accept = false;
                else
                {
                    error = $"Unknown Medic re-roll option '{submission.SelectedOptionId}'.";
                    return false;
                }
            }
            else
            {
                error = "Accept (re-roll) or decline (keep), or select keep/reroll.";
                return false;
            }

            merged.AcceptMedicReroll = accept;
            return true;
        }

        public static bool TryMergeMeadowsRedirectSubmission(
            ChoiceSubmission submission,
            KillChoice? choice,
            out KillChoice merged,
            out string? error)
        {
            merged = CloneKillChoice(choice);
            error = null;
            if (submission == null)
            {
                error = "A choice submission is required.";
                return false;
            }

            bool accept;
            if (submission.Accepted != null)
                accept = submission.Accepted.Value;
            else if (!string.IsNullOrWhiteSpace(submission.SelectedOptionId))
            {
                if (string.Equals(
                        submission.SelectedOptionId,
                        MeadowsRedirectOptions.KillMeadows,
                        StringComparison.Ordinal))
                    accept = true;
                else if (string.Equals(
                             submission.SelectedOptionId,
                             MeadowsRedirectOptions.Decline,
                             StringComparison.Ordinal))
                    accept = false;
                else
                {
                    error = $"Unknown Meadows option '{submission.SelectedOptionId}'.";
                    return false;
                }
            }
            else
            {
                error = "Accept (Kill Meadows) or decline.";
                return false;
            }

            merged.AcceptMeadowsRedirect = accept;
            merged.MeadowsOfferedThisBatch = true;
            return true;
        }

        /// <summary>
        /// Apply Meadows redirect: replace the first non-Meadows victim with Meadows.
        /// Original victim is spared; remaining victims stay on the list.
        /// </summary>
        public static void ApplyMeadowsRedirectToVictims(PlayerState player, KillChoice choice)
        {
            var meadows = AbilityDispatcher.FindMeadowsRedirect(player);
            if (meadows == null || choice.AcceptMeadowsRedirect != true)
                return;

            var victims = SelectVictims(player, choice.VictimCrewIds?.Count ?? player.Roster.Count, choice);
            if (victims.Count == 0)
            {
                choice.VictimCrewIds = new List<string> { meadows.Id };
                return;
            }

            var ids = new List<string>();
            var replaced = false;
            foreach (var v in victims)
            {
                if (!replaced && v.Id != meadows.Id)
                {
                    ids.Add(meadows.Id);
                    replaced = true;
                }
                else if (v.Id != meadows.Id)
                    ids.Add(v.Id);
            }
            if (!replaced)
                ids.Insert(0, meadows.Id);
            choice.VictimCrewIds = ids;
        }

        /// <summary>
        /// Subject up to <paramref name="count"/> crew to kill events.
        /// Meadows: offer redirect once per Kill N batch, then mandatory resolution.
        /// Med Bay: one re-roll offer per Medic Check (per Crew Killed).
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

            var working = CloneKillChoice(choice);

            if (NeedsMeadowsRedirectChoice(player, working))
            {
                if (!TrySuspendMeadowsRedirectChoice(game, player, count, out error))
                    return false;
                // Mirror die/victim fields back for callers holding the same choice object.
                if (choice != null)
                    choice.MeadowsOfferedThisBatch = working.MeadowsOfferedThisBatch;
                error = "Choose whether to Kill Meadows instead.";
                return false;
            }

            if (working.AcceptMeadowsRedirect == true)
                ApplyMeadowsRedirectToVictims(player, working);
            if (choice != null)
            {
                choice.AcceptMeadowsRedirect = working.AcceptMeadowsRedirect;
                choice.MeadowsOfferedThisBatch = working.MeadowsOfferedThisBatch;
                if (working.VictimCrewIds != null)
                    choice.VictimCrewIds = working.VictimCrewIds;
            }

            if (NeedsMedFoamChoice(game, player, count, working))
            {
                if (!TrySuspendMedFoamChoice(game, player, count, out error))
                    return false;
                error = "Choose whether to discard Med Foam for a successful Medic Check.";
                return false;
            }

            // Process victims sequentially; suspend Med Bay per Medic Check.
            var victims = SelectVictims(player, count, working);
            var resolved = new HashSet<string>(StringComparer.Ordinal);
            if (working.ResolvedVictimIds != null)
            {
                foreach (var id in working.ResolvedVictimIds)
                    resolved.Add(id);
            }

            var foamForFirst = false;
            if (working.UseMedFoam == true && !working.CountAsSuccessfulMedicCheck)
            {
                if (TryDiscardMedFoam(game, player, out _))
                    foamForFirst = true;
            }

            var firstMedicSlot = true;
            foreach (var victim in victims)
            {
                if (resolved.Contains(victim.Id))
                    continue;
                var member = player.Roster.Find(victim.Id);
                if (member == null)
                {
                    MarkResolved(working, choice, victim.Id);
                    continue;
                }

                KillChoice applyChoice = CloneKillChoice(working);
                if (foamForFirst && firstMedicSlot && (working.AttemptMedicCheck ?? true) && HasMedic(player))
                {
                    applyChoice.CountAsSuccessfulMedicCheck = true;
                    applyChoice.UseMedFoam = false;
                    firstMedicSlot = false;
                }
                else if (NeedsMedicRerollChoice(game, player, applyChoice)
                         && (applyChoice.MedicFirstDie == null
                             || !string.Equals(applyChoice.MedicPendingVictimId, member.Id, StringComparison.Ordinal)
                             || applyChoice.AcceptMedicReroll == null))
                {
                    // New Medic Check for this victim — roll and suspend.
                    if (applyChoice.MedicFirstDie == null
                        || !string.Equals(applyChoice.MedicPendingVictimId, member.Id, StringComparison.Ordinal))
                    {
                        applyChoice.MedicFirstDie = Dice.D6(rng);
                        applyChoice.MedicPendingVictimId = member.Id;
                        applyChoice.AcceptMedicReroll = null;
                        if (choice != null)
                        {
                            choice.MedicFirstDie = applyChoice.MedicFirstDie;
                            choice.MedicPendingVictimId = applyChoice.MedicPendingVictimId;
                            choice.AcceptMedicReroll = null;
                            choice.ResolvedVictimIds = working.ResolvedVictimIds;
                            choice.VictimCrewIds = working.VictimCrewIds;
                        }
                    }

                    if (applyChoice.AcceptMedicReroll == null)
                    {
                        if (!TrySuspendMedicRerollChoice(game, player, count, out error))
                            return false;
                        error = "Choose whether to re-roll this Medic Check.";
                        return false;
                    }
                }

                var result = Apply(game, player, member, rng, applyChoice);
                MarkResolved(working, choice, victim.Id);
                // Clear Med Bay pending fields. Keep AcceptMedicReroll=false only for
                // scripted batch-wide decline (no MedicPendingVictimId).
                var keepBatchDecline = applyChoice.AcceptMedicReroll == false
                    && string.IsNullOrEmpty(applyChoice.MedicPendingVictimId);
                working.MedicFirstDie = null;
                working.MedicPendingVictimId = null;
                working.AcceptMedicReroll = keepBatchDecline ? false : (bool?)null;
                if (choice != null)
                {
                    choice.AcceptMedicReroll = working.AcceptMedicReroll;
                    choice.MedicFirstDie = null;
                    choice.MedicPendingVictimId = null;
                    choice.ResolvedVictimIds = working.ResolvedVictimIds;
                }
                if (result.Outcome == CrewOutcome.Killed)
                    killed++;
                firstMedicSlot = false;
            }

            return true;
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.MedicReroll"/>. Continues the Kill N batch
        /// so later victims may each receive their own Med Bay offer.
        /// </summary>
        public static bool TryResumeMedicRerollKillUpTo(
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
                    PendingChoiceKinds.MedicReroll,
                    StringComparison.Ordinal))
            {
                error = "No Medic re-roll choice is pending.";
                return false;
            }

            var player = game.GetPlayer(game.PendingChoice.PlayerId);
            if (!TryParseKillCount(game.PendingChoice.ContextId, out var count))
            {
                error = "Medic re-roll context is missing the kill count.";
                return false;
            }

            if (!TryMergeMedicRerollSubmission(submission, baseChoice, out var merged, out error))
                return false;

            if (!game.TrySubmitChoice(player.Id, submission, out _, out error))
                return false;

            if (baseChoice != null)
            {
                merged.MedicFirstDie ??= baseChoice.MedicFirstDie;
                merged.MedicPendingVictimId ??= baseChoice.MedicPendingVictimId;
                if (baseChoice.VictimCrewIds != null && merged.VictimCrewIds == null)
                    merged.VictimCrewIds = baseChoice.VictimCrewIds;
                if (baseChoice.ResolvedVictimIds != null && merged.ResolvedVictimIds == null)
                    merged.ResolvedVictimIds = baseChoice.ResolvedVictimIds;
                merged.AcceptMeadowsRedirect ??= baseChoice.AcceptMeadowsRedirect;
                merged.MeadowsOfferedThisBatch |= baseChoice.MeadowsOfferedThisBatch;
            }

            // Apply the pending Medic Check victim, then continue the batch.
            if (!string.IsNullOrEmpty(merged.MedicPendingVictimId))
            {
                var member = player.Roster.Find(merged.MedicPendingVictimId);
                if (member != null)
                {
                    var result = Apply(game, player, member, rng, merged);
                    MarkResolved(merged, baseChoice, member.Id);
                    if (result.Outcome == CrewOutcome.Killed)
                        killed++;
                }
                else
                    MarkResolved(merged, baseChoice, merged.MedicPendingVictimId!);
            }

            merged.AcceptMedicReroll = null;
            merged.MedicFirstDie = null;
            merged.MedicPendingVictimId = null;
            if (baseChoice != null)
            {
                baseChoice.AcceptMedicReroll = null;
                baseChoice.MedicFirstDie = null;
                baseChoice.MedicPendingVictimId = null;
                baseChoice.ResolvedVictimIds = merged.ResolvedVictimIds;
            }

            if (!TryKillUpTo(game, player, count, rng, out var moreKilled, out error, merged))
            {
                // Suspended again (next Med Bay / etc.) — killed so far still counts.
                if (baseChoice != null)
                {
                    baseChoice.MedicFirstDie = merged.MedicFirstDie;
                    baseChoice.MedicPendingVictimId = merged.MedicPendingVictimId;
                    baseChoice.AcceptMedicReroll = merged.AcceptMedicReroll;
                    baseChoice.ResolvedVictimIds = merged.ResolvedVictimIds;
                    baseChoice.VictimCrewIds = merged.VictimCrewIds;
                }
                killed += moreKilled;
                return false;
            }

            killed += moreKilled;
            return true;
        }

        /// <summary>Resume after <see cref="PendingChoiceKinds.MeadowsRedirect"/> on Kill N.</summary>
        public static bool TryResumeMeadowsRedirectKillUpTo(
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
                    PendingChoiceKinds.MeadowsRedirect,
                    StringComparison.Ordinal))
            {
                error = "No Meadows redirect choice is pending.";
                return false;
            }

            var player = game.GetPlayer(game.PendingChoice.PlayerId);
            if (!TryParseKillCount(game.PendingChoice.ContextId, out var count))
            {
                error = "Meadows redirect context is missing the kill count.";
                return false;
            }

            if (!TryMergeMeadowsRedirectSubmission(submission, baseChoice, out var merged, out error))
                return false;

            if (!game.TrySubmitChoice(player.Id, submission, out _, out error))
                return false;

            if (baseChoice != null)
            {
                if (baseChoice.VictimCrewIds != null && merged.VictimCrewIds == null)
                    merged.VictimCrewIds = baseChoice.VictimCrewIds;
                baseChoice.AcceptMeadowsRedirect = merged.AcceptMeadowsRedirect;
                baseChoice.MeadowsOfferedThisBatch = true;
            }

            return TryKillUpTo(game, player, count, rng, out killed, out error, merged);
        }

        /// <summary>
        /// Apply Kill N when victims are already chosen or no choice is required.
        /// Scripted path auto-declines undecided Meadows / Med Bay may (use TryKillUpTo to ask).
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
            if (NeedsMedFoamChoice(game, player, count, choice))
            {
                throw new InvalidOperationException(
                    "Med Foam discard requires PendingChoice or KillChoice.UseMedFoam.");
            }

            var working = CloneKillChoice(choice);
            if (NeedsMeadowsRedirectChoice(player, working))
            {
                working.AcceptMeadowsRedirect = false;
                working.MeadowsOfferedThisBatch = true;
            }
            if (NeedsMedicRerollChoice(game, player, working))
                working.AcceptMedicReroll = false;

            if (!TryKillUpTo(game, player, count, rng, out var killed, out var error, working))
            {
                throw new InvalidOperationException(
                    error ?? "KillUpTo suspended unexpectedly; use TryKillUpTo.");
            }
            return killed;
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

        private static void MarkResolved(KillChoice working, KillChoice? mirror, string victimId)
        {
            working.ResolvedVictimIds ??= new List<string>();
            var found = false;
            foreach (var id in working.ResolvedVictimIds)
            {
                if (string.Equals(id, victimId, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                working.ResolvedVictimIds.Add(victimId);
            if (mirror != null)
                mirror.ResolvedVictimIds = working.ResolvedVictimIds;
        }

        private static KillChoice CloneKillChoice(KillChoice? source)
        {
            if (source == null)
                return new KillChoice();
            return new KillChoice
            {
                VictimCrewIds = source.VictimCrewIds,
                AttemptMedicCheck = source.AttemptMedicCheck,
                CountAsSuccessfulMedicCheck = source.CountAsSuccessfulMedicCheck,
                UseMedFoam = source.UseMedFoam,
                AcceptMedicReroll = source.AcceptMedicReroll,
                MedicFirstDie = source.MedicFirstDie,
                MedicPendingVictimId = source.MedicPendingVictimId,
                AcceptMeadowsRedirect = source.AcceptMeadowsRedirect,
                MeadowsOfferedThisBatch = source.MeadowsOfferedThisBatch,
                ResolvedVictimIds = source.ResolvedVictimIds == null
                    ? null
                    : new List<string>(source.ResolvedVictimIds)
            };
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
