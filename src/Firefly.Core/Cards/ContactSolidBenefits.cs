using System;
using System.Collections.Generic;
using Firefly.Core.Actions;
using Firefly.Core.State;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Explicit contact Solid hooks (no ability DSL). Sources: FAQ 4.1, GF9 / Director's Cut,
    /// Blue Sun (Mr. Universe, Harrow), Kalidasa (Higgins, Fanty &amp; Mingo).
    /// </summary>
    public sealed class SolidRepChoice
    {
        /// <summary>Job ids to discard from hand when Max Hand drops (Mr. Universe).</summary>
        public IList<string> DiscardJobHandIds { get; set; } = new List<string>();

        /// <summary>Active job id to discard when Active Job limit drops (Higgins).</summary>
        public string? DiscardActiveJobId { get; set; }

        public KillChoice? Kill { get; set; }
    }

    public static class ContactSolidBenefits
    {
        /// <summary>GF9 p.14 / Director's Cut: up to 3 Active Jobs; 3 Inactive Jobs in hand.</summary>
        public const int BaseActiveJobLimit = 3;
        public const int BaseJobHandLimit = 3;

        public const int FantyTransportBonus = 500;
        public const int HarrowSmugglingShippingBonus = 500;
        public const int FantyBuyContrabandPrice = 400;
        public const int HarrowBuyCargoPrice = 300;

        public static void RefreshLimits(GameState game, PlayerState player)
        {
            if (player == null)
                return;
            player.JobHandLimit = BaseJobHandLimit + HandSizeBonus(game, player);
            player.ActiveJobLimit = BaseActiveJobLimit + ActiveJobsBonus(game, player);
        }

        public static void BecomeSolid(GameState game, PlayerState player, string contactId)
        {
            player.BecomeSolid(contactId);
            RefreshLimits(game, player);
        }

        /// <summary>
        /// Lose Solid and apply printed discard-downs (Blue Sun Mr. Universe hand;
        /// Kalidasa Higgins active job). Thin hooks for which cards to discard.
        /// </summary>
        public static bool TryLoseSolid(
            GameState game,
            PlayerState player,
            string? contactIdOrName,
            SolidRepChoice? choice,
            out string? error)
        {
            error = null;
            if (player == null)
            {
                error = "No player.";
                return false;
            }

            var lostId = ResolveSolidId(player, contactIdOrName);
            if (lostId == null)
            {
                error = "Not Solid with that Contact.";
                return false;
            }

            if (!CanDiscardDownAfterLosing(game, player, lostId, choice, out error))
                return false;

            var beforeHand = player.JobHandLimit;
            var beforeActive = player.ActiveJobLimit;
            player.TryLoseSolid(lostId);
            RefreshLimits(game, player);

            // CanDiscardDownAfterLosing already verified the choice; apply it.
            if (player.JobHand.Count > player.JobHandLimit)
                TryDiscardHandDown(game, player, beforeHand, choice, out _);
            if (player.ActiveJobs.Count > player.ActiveJobLimit)
                TryDiscardActiveDown(game, player, beforeActive, choice, out _);
            return true;
        }

        /// <summary>Validate discard-down hooks before mutating other Misbehave effects.</summary>
        public static bool CanDiscardDownAfterLosing(
            GameState game,
            PlayerState player,
            string lostId,
            SolidRepChoice? choice,
            out string? error)
        {
            error = null;
            var handLimit = BaseJobHandLimit;
            var activeLimit = BaseActiveJobLimit;
            if (game?.Contacts != null)
            {
                foreach (var id in player.SolidWith)
                {
                    if (id.Equals(lostId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!TryGetContact(game, id, out var contact))
                        continue;
                    // CountsAsSolidWith still true for remaining ids (lost not removed yet).
                    if (!CountsAsSolidWith(game, player, contact))
                        continue;
                    if (contact.HandSizeBonus.HasValue)
                        handLimit += contact.HandSizeBonus.Value;
                    if (contact.ActiveJobsBonus.HasValue)
                        activeLimit += contact.ActiveJobsBonus.Value;
                }
            }

            var handNeed = player.JobHand.Count - handLimit;
            if (handNeed > 0)
            {
                var ids = choice?.DiscardJobHandIds ?? new List<string>();
                if (ids.Count != handNeed)
                {
                    error = $"Must discard {handNeed} job(s) from hand after losing Solid (Max Hand → {handLimit}).";
                    return false;
                }
                var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in ids)
                {
                    if (!unique.Add(id) || !player.JobHand.Contains(id))
                    {
                        error = "Invalid job hand discard for Solid loss.";
                        return false;
                    }
                }
            }

            var activeNeed = player.ActiveJobs.Count - activeLimit;
            if (activeNeed > 0)
            {
                if (activeNeed != 1 || string.IsNullOrWhiteSpace(choice?.DiscardActiveJobId)
                    || player.FindActive(choice!.DiscardActiveJobId!) == null)
                {
                    error = $"Must discard {activeNeed} active job(s) after losing Solid (Active Jobs → {activeLimit}).";
                    return false;
                }
            }

            return true;
        }

        public static bool CountsAsSolidWith(GameState game, PlayerState player, ContactCard contact) =>
            contact != null
            && (ActiveAlertRules.CountsAsSolidWith(game, player, contact.Id)
                || ActiveAlertRules.CountsAsSolidWith(game, player, contact.Name));

        public static bool IsSolidAmnon(GameState game, PlayerState player) =>
            game.Contacts != null
            && game.Contacts.TryFindByName("Amnon Duul", out var c)
            && CountsAsSolidWith(game, player, c);

        public static bool IsSolidHarken(GameState game, PlayerState player) =>
            game.Contacts != null
            && game.Contacts.TryFindByName("Harken", out var c)
            && CountsAsSolidWith(game, player, c);

        public static bool IsSolidFanty(GameState game, PlayerState player) =>
            game.Contacts != null
            && game.Contacts.TryFindByName("Fanty & Mingo", out var c)
            && CountsAsSolidWith(game, player, c);

        public static bool IsSolidHarrow(GameState game, PlayerState player) =>
            game.Contacts != null
            && game.Contacts.TryFindByName("Lord Harrow", out var c)
            && CountsAsSolidWith(game, player, c);

        public static bool IsNiskaJob(JobCard job) =>
            job != null && ContactNames.EqualsName(job.ContactName, "Niska");

        public static int CompletionBonus(GameState game, PlayerState player, JobCard job)
        {
            if (job == null || player == null)
                return 0;
            var bonus = 0;
            // Kalidasa p.9–10: When Solid with Fanty and Mingo, $500 on Transport Jobs.
            if (IsSolidFanty(game, player) && JobHasType(job, "Transport"))
                bonus += FantyTransportBonus;
            // Blue Sun p.9: When Solid with Lord Harrow, $500 on Smuggling or Shipping.
            if (IsSolidHarrow(game, player)
                && (JobHasType(job, "Smuggling") || JobHasType(job, "Shipping")))
                bonus += HarrowSmugglingShippingBonus;
            return bonus;
        }

        public static bool JobHasType(JobCard job, string type)
        {
            if (job == null || string.IsNullOrWhiteSpace(job.JobType) || string.IsNullOrWhiteSpace(type))
                return false;
            foreach (var part in job.JobType.Split(new[] { '/', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Trim().Equals(type, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool IsCustomsInspection(NavCard? card) =>
            card != null
            && (card.Id.Equals("nav_customs-inspection", StringComparison.OrdinalIgnoreCase)
                || card.Name.Equals("Customs Inspection", StringComparison.OrdinalIgnoreCase));

        private static int HandSizeBonus(GameState game, PlayerState player)
        {
            var n = 0;
            if (game?.Contacts == null)
                return n;
            foreach (var id in player.SolidWith)
            {
                if (!TryGetContact(game, id, out var contact))
                    continue;
                if (!CountsAsSolidWith(game, player, contact))
                    continue;
                if (contact.HandSizeBonus.HasValue)
                    n += contact.HandSizeBonus.Value;
            }
            return n;
        }

        private static int ActiveJobsBonus(GameState game, PlayerState player)
        {
            var n = 0;
            if (game?.Contacts == null)
                return n;
            foreach (var id in player.SolidWith)
            {
                if (!TryGetContact(game, id, out var contact))
                    continue;
                if (!CountsAsSolidWith(game, player, contact))
                    continue;
                if (contact.ActiveJobsBonus.HasValue)
                    n += contact.ActiveJobsBonus.Value;
            }
            return n;
        }

        private static bool TryGetContact(GameState game, string idOrName, out ContactCard contact)
        {
            contact = null!;
            if (game.Contacts == null)
                return false;
            if (game.Contacts.TryGet(idOrName, out contact))
                return true;
            return game.Contacts.TryFindByName(idOrName, out contact);
        }

        private static string? ResolveSolidId(PlayerState player, string? contactIdOrName)
        {
            if (!string.IsNullOrWhiteSpace(contactIdOrName))
            {
                foreach (var id in player.SolidWith)
                {
                    if (id.Equals(contactIdOrName, StringComparison.OrdinalIgnoreCase)
                        || ContactNames.EqualsName(id, contactIdOrName))
                        return id;
                }
                return null;
            }
            foreach (var id in player.SolidWith)
                return id;
            return null;
        }

        private static bool TryDiscardHandDown(
            GameState game,
            PlayerState player,
            int previousLimit,
            SolidRepChoice? choice,
            out string? error)
        {
            error = null;
            var need = player.JobHand.Count - player.JobHandLimit;
            if (need <= 0)
                return true;

            var ids = choice?.DiscardJobHandIds ?? new List<string>();
            if (ids.Count != need)
            {
                error = $"Must discard {need} job(s) from hand (Max Hand dropped from {previousLimit} to {player.JobHandLimit}).";
                return false;
            }

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (!unique.Add(id))
                {
                    error = "Cannot discard the same hand job twice.";
                    return false;
                }
                if (!player.JobHand.Contains(id))
                {
                    error = $"Job '{id}' is not in hand.";
                    return false;
                }
            }

            foreach (var id in ids)
            {
                player.JobHand.Remove(id);
                ReturnJobToContactDiscard(game, id);
            }
            return true;
        }

        private static bool TryDiscardActiveDown(
            GameState game,
            PlayerState player,
            int previousLimit,
            SolidRepChoice? choice,
            out string? error)
        {
            error = null;
            var need = player.ActiveJobs.Count - player.ActiveJobLimit;
            if (need <= 0)
                return true;
            // Kalidasa: when losing Higgins Solid with four Active Jobs, choose one to discard.
            if (need != 1 || string.IsNullOrWhiteSpace(choice?.DiscardActiveJobId))
            {
                error = $"Must discard {need} active job(s) (Active Job limit dropped from {previousLimit} to {player.ActiveJobLimit}).";
                return false;
            }

            var jobId = choice!.DiscardActiveJobId!;
            if (player.FindActive(jobId) == null)
            {
                error = $"Active job '{jobId}' not found.";
                return false;
            }

            player.RemoveActive(jobId);
            ReturnJobToContactDiscard(game, jobId);
            return true;
        }

        private static void ReturnJobToContactDiscard(GameState game, string jobId)
        {
            if (game.Jobs == null || !game.Jobs.TryGet(jobId, out var job))
                return;
            if (game.ContactDecks != null && game.ContactDecks.TryGet(job.ContactName, out var deck))
                deck.MoveToDiscard(job);
        }
    }
}
