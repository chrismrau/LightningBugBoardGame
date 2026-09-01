using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public enum WorkKind { Activate, Pickup, Complete }

    public sealed class WorkResult
    {
        public WorkKind Kind { get; }
        public JobCard Job { get; }
        public bool AwaitingMisbehave { get; }
        public int Pay { get; }
        public int MoralDisgruntled { get; }
        public WorkResult(WorkKind kind, JobCard job, bool awaitingMisbehave, int pay, int moralDisgruntled)
        {
            Kind = kind; Job = job; AwaitingMisbehave = awaitingMisbehave; Pay = pay; MoralDisgruntled = moralDisgruntled;
        }
    }

    public sealed class WorkAction
    {
        public bool TryActivate(GameState game, string playerId, string jobId, out WorkResult? result, out string? error)
        {
            result = null;
            if (!CanStart(game, playerId, out var player, out error)) return false;
            if (game.Jobs == null || !game.Jobs.TryGet(jobId, out var job)) { error = $"Unknown job '{jobId}'."; return false; }
            if (!player.JobHand.Contains(jobId)) { error = "That job is not in hand."; return false; }
            if (player.FindActive(jobId) != null) { error = "That job is already active."; return false; }
            if (player.ActiveJobs.Count >= player.ActiveJobLimit) { error = $"Already have {player.ActiveJobLimit} active job(s)."; return false; }
            if (!CanWorkContact(game, player, job, out error)) return false;
            player.JobHand.Remove(jobId);
            player.ActiveJobs.Add(new ActiveJob(jobId));
            var disgruntled = job.Immoral ? player.Roster.DisgruntleMoral() : 0;
            game.TryConsumeAction(TurnAction.Work, out _);
            result = new WorkResult(WorkKind.Activate, job, false, 0, disgruntled);
            return true;
        }

        public bool TryWorkActive(GameState game, string playerId, string jobId, out WorkResult? result, out string? error)
        {
            result = null;
            if (!CanStart(game, playerId, out var player, out error)) return false;
            if (game.PendingMisbehave != null) { error = "Finish the pending Misbehave before working again."; return false; }
            if (game.Jobs == null || !game.Jobs.TryGet(jobId, out var job)) { error = $"Unknown job '{jobId}'."; return false; }
            var active = player.FindActive(jobId);
            if (active == null) { error = "That job is not active."; return false; }
            if (!CanWorkContact(game, player, job, out error)) return false;
            var pickup = JobTerms.Pickup(job);
            var dropoff = JobTerms.Dropoff(job);
            var hasDropoff = JobTerms.HasDropoff(job);
            if (!active.PickedUp)
            {
                if (JobTerms.LocationIsSpecialCase(pickup.Location)) { error = $"Pickup location '{pickup.Location}' is not handled by the Work kernel yet."; return false; }
                if (!AtSite(game.Map, player.SectorId, pickup.Location)) { error = $"Must be at {JobTerms.PlaceName(pickup.Location)} to pick up this job."; return false; }
                return FinishOrMisbehave(game, player, job, active, pickup, WorkSite.Pickup, WorkKind.Pickup, !hasDropoff, out result, out error);
            }
            if (!hasDropoff) { error = "This job has already been picked up and has no drop-off."; return false; }
            if (JobTerms.LocationIsSpecialCase(dropoff.Location)) { error = $"Drop-off '{dropoff.Location}' is not handled by the Work kernel yet."; return false; }
            if (!AtSite(game.Map, player.SectorId, dropoff.Location)) { error = $"Must be at {JobTerms.PlaceName(dropoff.Location)} to complete this job."; return false; }
            return FinishOrMisbehave(game, player, job, active, dropoff, WorkSite.Dropoff, WorkKind.Complete, true, out result, out error);
        }

        public bool TryProceedMisbehave(GameState game, string playerId, bool proceed, out WorkResult? result, out string? error)
        {
            result = null;
            var pending = game.PendingMisbehave;
            if (pending == null) { error = "No Misbehave is pending."; return false; }
            if (pending.PlayerId != playerId) { error = "This Misbehave belongs to another player."; return false; }
            var player = game.GetPlayer(playerId);
            if (game.Jobs == null || !game.Jobs.TryGet(pending.JobId, out var job)) { error = $"Unknown job '{pending.JobId}'."; return false; }
            var active = player.FindActive(pending.JobId);
            if (active == null) { error = "The job is no longer active."; game.PendingMisbehave = null; return false; }
            if (!proceed)
            {
                game.PendingMisbehave = null;
                game.TryConsumeAction(TurnAction.Work, out _);
                result = new WorkResult(pending.Site == WorkSite.Pickup ? WorkKind.Pickup : WorkKind.Complete, job, false, 0, 0);
                error = null;
                return true;
            }
            pending.Remaining--;
            if (pending.Remaining > 0)
            {
                result = new WorkResult(pending.Site == WorkSite.Pickup ? WorkKind.Pickup : WorkKind.Complete, job, true, 0, 0);
                error = null;
                return true;
            }
            game.PendingMisbehave = null;
            var terms = pending.Site == WorkSite.Pickup ? JobTerms.Pickup(job) : JobTerms.Dropoff(job);
            var completeAfter = pending.Site == WorkSite.Dropoff || !JobTerms.HasDropoff(job);
            return ApplySite(game, player, job, active, terms, pending.Site == WorkSite.Pickup ? WorkKind.Pickup : WorkKind.Complete, completeAfter, out result, out error);
        }

        private static bool FinishOrMisbehave(GameState game, PlayerState player, JobCard job, ActiveJob active, JobSiteTerms terms, WorkSite site, WorkKind kind, bool completeAfter, out WorkResult? result, out string? error)
        {
            if (completeAfter && !CanUnload(player, active, JobTerms.HasDropoff(job) ? JobTerms.Dropoff(job) : terms, out var goodsError))
            {
                result = null; error = goodsError; return false;
            }
            if (terms.Misbehave > 0)
            {
                game.PendingMisbehave = new PendingMisbehave(player.Id, job.Id, site, terms.Misbehave);
                result = new WorkResult(kind, job, true, 0, 0);
                error = null;
                return true;
            }
            return ApplySite(game, player, job, active, terms, kind, completeAfter, out result, out error);
        }

        private static bool ApplySite(GameState game, PlayerState player, JobCard job, ActiveJob active, JobSiteTerms terms, WorkKind kind, bool completeAfter, out WorkResult? result, out string? error)
        {
            result = null;
            if (kind == WorkKind.Pickup || (kind == WorkKind.Complete && !active.PickedUp))
            {
                LoadGoods(player, active, terms);
                active.PickedUp = true;
            }
            if (!completeAfter)
            {
                game.TryConsumeAction(TurnAction.Work, out _);
                result = new WorkResult(WorkKind.Pickup, job, false, 0, 0);
                error = null;
                return true;
            }
            if (!UnloadGoods(player, active, JobTerms.HasDropoff(job) ? JobTerms.Dropoff(job) : terms, out error))
                return false;
            var pay = PayOut(player, job, active);
            player.Cash += pay;
            if (game.Contacts != null && game.Contacts.TryFindByName(job.ContactName, out var contact))
            {
                player.BecomeSolid(contact.Id);
                if (game.ContactDecks != null && game.ContactDecks.TryGet(contact.Name, out var deck))
                    deck.MoveToDiscard(job);
            }
            player.RemoveActive(job.Id);
            game.TryConsumeAction(TurnAction.Work, out _);
            result = new WorkResult(WorkKind.Complete, job, false, pay, 0);
            error = null;
            return true;
        }

        private static void LoadGoods(PlayerState player, ActiveJob active, JobSiteTerms terms)
        {
            player.Cargo += terms.Cargo;
            player.Contraband += terms.Contraband;
            player.Parts += terms.Parts;
            player.Fugitives += terms.FugitivesUnlimited ? 1 : terms.Fugitives;
            player.Passengers += terms.PassengersUnlimited ? 1 : terms.Passengers;
            active.Cargo = terms.Cargo;
            active.Contraband = terms.Contraband;
            active.Parts = terms.Parts;
            active.Fugitives = terms.FugitivesUnlimited ? 1 : terms.Fugitives;
            active.Passengers = terms.PassengersUnlimited ? 1 : terms.Passengers;
        }

        private static bool CanUnload(PlayerState player, ActiveJob active, JobSiteTerms terms, out string? error) =>
            UnloadGoods(player, active, terms, out error, false);

        private static bool UnloadGoods(PlayerState player, ActiveJob active, JobSiteTerms terms, out string? error) =>
            UnloadGoods(player, active, terms, out error, true);

        private static bool UnloadGoods(PlayerState player, ActiveJob active, JobSiteTerms terms, out string? error, bool apply)
        {
            error = null;
            var cargo = terms.HasGoods ? terms.Cargo : active.Cargo;
            var contra = terms.HasGoods ? terms.Contraband : active.Contraband;
            var parts = terms.HasGoods ? terms.Parts : active.Parts;
            var fugi = terms.FugitivesUnlimited ? active.Fugitives : (terms.Fugitives > 0 ? terms.Fugitives : active.Fugitives);
            var pass = terms.PassengersUnlimited ? active.Passengers : (terms.Passengers > 0 ? terms.Passengers : active.Passengers);
            if (player.Cargo < cargo || player.Contraband < contra || player.Parts < parts || player.Fugitives < fugi || player.Passengers < pass)
            {
                error = "Ship is not carrying the goods this job requires.";
                return false;
            }
            if (!apply) return true;
            player.Cargo -= cargo; player.Contraband -= contra; player.Parts -= parts; player.Fugitives -= fugi; player.Passengers -= pass;
            return true;
        }

        private static int PayOut(PlayerState player, JobCard job, ActiveJob active)
        {
            var pay = job.PayBase ?? 0;
            if (JobTerms.PayPerPassenger(job)) pay *= System.Math.Max(1, active.Passengers);
            pay += JobTerms.ProfessionBonus(job, player.Roster.HasProfession);
            return pay;
        }

        private static bool AtSite(SectorMap map, string sectorId, string? location)
        {
            var name = JobTerms.PlaceName(location);
            return !string.IsNullOrEmpty(name) && map.SatisfiesDestination(sectorId, name);
        }

        private static bool CanWorkContact(GameState game, PlayerState player, JobCard job, out string? error)
        {
            error = null;
            if (game.Contacts != null && game.Contacts.TryFindByName(job.ContactName, out var contact) && contact.IsHiggins && player.Roster.HasName("Jayne"))
            {
                error = "Higgins will not Work while Jayne is in the crew.";
                return false;
            }
            return true;
        }

        private static bool CanStart(GameState game, string playerId, out PlayerState player, out string? error)
        {
            player = game.GetPlayer(playerId);
            error = null;
            if (!ReferenceEquals(player, game.CurrentPlayer)) { error = $"It is not {player.Name}'s turn."; return false; }
            return game.CanTakeAction(TurnAction.Work, out error);
        }
    }
}
