using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Thin kill / Medic hooks until the shared PendingChoice layer.
    /// Victim order is player-chosen when <see cref="VictimCrewIds"/> is set;
    /// otherwise KillUpTo keeps a temporary end-of-roster auto-pick.
    /// </summary>
    public sealed class KillChoice
    {
        /// <summary>
        /// Ordered crew ids to subject to kill events (length up to the printed Kill N).
        /// Thin hook until PendingChoice — do not treat auto-pick as permanent rules.
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
        /// Thin hook until PendingChoice (discard wiring later).
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
    /// </summary>
    public static class CrewKill
    {
        public const int MedicSaveTarget = 5;

        public static bool HasMedic(PlayerState player) =>
            player.Roster.HasProfession("Medic");

        /// <summary>
        /// Simon Tam: "+2 to Medic Checks" (Supplies.tsv / Crew.json). Mandatory.
        /// </summary>
        public static int MedicBonus(PlayerState player)
        {
            var bonus = 0;
            if (player.Roster.Find("crew_simon-tam") != null || player.Roster.HasName("Simon Tam"))
                bonus += 2;
            return bonus;
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
        /// Subject up to <paramref name="count"/> crew to kill events (one Medic Check each when applicable).
        /// Returns how many were removed from play (not Medic-saved / Leader-Disgruntled).
        /// </summary>
        public static int KillUpTo(
            GameState game,
            PlayerState player,
            int count,
            IRng rng,
            KillChoice? choice = null)
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

            // Temporary auto-pick (end of roster) until PendingChoice owns victim selection.
            for (var i = player.Roster.Count - 1; i >= 0 && list.Count < count; i--)
                list.Add(player.Roster.Members[i]);
            return list;
        }
    }
}
