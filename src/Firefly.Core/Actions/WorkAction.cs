using Firefly.Core.Abilities;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public enum WorkKind
    {
        Pickup,
        Complete,
        MakeWork,
        Datascope
    }

    public sealed class WorkResult
    {
        public WorkKind Kind { get; }
        public JobCard? Job { get; }
        public bool AwaitingMisbehave { get; }
        public bool BecameActive { get; }
        public int Pay { get; }
        public int MoralDisgruntled { get; }
        public int FugitivesGained { get; }
        public int SupplyDiscarded { get; }

        public WorkResult(WorkKind kind, JobCard? job, bool awaitingMisbehave, bool becameActive, int pay, int moralDisgruntled, int fugitivesGained = 0, int supplyDiscarded = 0)
        {
            Kind = kind;
            Job = job;
            AwaitingMisbehave = awaitingMisbehave;
            BecameActive = becameActive;
            Pay = pay;
            MoralDisgruntled = moralDisgruntled;
            FugitivesGained = fugitivesGained;
            SupplyDiscarded = supplyDiscarded;
        }
    }

    public sealed class MakeWorkChoice
    {
        /// <summary>Holder may: null = undecided, true = take Fugitive, false = decline.</summary>
        public bool? TakeFugitive { get; set; }
    }

    public sealed class WrightBonusChoice
    {
        /// <summary>Wright may: null = undecided, true = take Immoral bonus, false = decline.</summary>
        public bool? AcceptBonus { get; set; }
    }

    /// <summary>
    /// Work a job from hand or the active slot.
    /// FAQ 4.1 p.5: a Job becomes Active when you first use a Work Action on it;
    /// it stays Active until completed or discarded because a Warrant is Issued
    /// (botched Misbehave does not return it to hand).
    /// </summary>
    public sealed class WorkAction
    {
        public const int MakeWorkPay = 200;
        private WrightBonusChoice? _wrightBonusChoice;
        private MakeWorkChoice? _makeWorkChoice;

        public bool TryWork(GameState game, string playerId, string jobId, out WorkResult? result, out string? error)
        {
            result = null;
            if (!CanStart(game, playerId, out var player, out error))
                return false;
            if (game.PendingMisbehave != null)
            {
                error = "Finish the pending Misbehave before working again.";
                return false;
            }
            if (game.Jobs == null || !game.Jobs.TryGet(jobId, out var job))
            {
                error = $"Unknown job '{jobId}'.";
                return false;
            }
            if (!CanWorkContact(game, player, job, out error))
                return false;

            var active = player.FindActive(jobId);
            var inHand = player.JobHand.Contains(jobId);
            if (active == null && !inHand)
            {
                error = "That job is not in hand or active.";
                return false;
            }

            if (BountyJobAuthority.IsCoveredByBountyDeck(job, game.Bounties))
            {
                error = "That bounty is worked from the Most Wanted List (Bounty deck), not as a Contact Job.";
                return false;
            }

            var pickup = JobTerms.Pickup(job);
            var dropoff = JobTerms.Dropoff(job);
            var hasDropoff = JobTerms.HasDropoff(job);

            if (active == null)
            {
                if (player.ActiveJobs.Count >= player.ActiveJobLimit)
                {
                    error = $"Already have {player.ActiveJobLimit} active job(s).";
                    return false;
                }
                if (JobTerms.IsVarious(pickup.Location))
                {
                    error = "Cortex Alert / Various pickups use the Bounty deck (PBH), not Work-as-Job.";
                    return false;
                }
                if (JobTerms.IsAnyRival(pickup.Location))
                {
                    error = "Any Rival piracy Jobs use PiracyAction (PBH boarding + showdown).";
                    return false;
                }
                if (!AtSite(game, player.SectorId, pickup.Location))
                {
                    error = $"Must be at {JobTerms.PlaceName(pickup.Location)} to start this job.";
                    return false;
                }
                return FinishOrMisbehave(game, player, job, null, pickup, WorkSite.Pickup, WorkKind.Pickup, !hasDropoff, out result, out error);
            }

            if (!active.PickedUp)
            {
                if (!AtSite(game, player.SectorId, pickup.Location))
                {
                    error = $"Must be at {JobTerms.PlaceName(pickup.Location)} to pick up this job.";
                    return false;
                }
                return FinishOrMisbehave(game, player, job, active, pickup, WorkSite.Pickup, WorkKind.Pickup, !hasDropoff, out result, out error);
            }

            if (!hasDropoff)
            {
                error = "This job has already been picked up and has no drop-off.";
                return false;
            }
            if (JobTerms.IsVarious(dropoff.Location))
            {
                error = "Cortex Alert / Various drop-offs use the Bounty deck (PBH), not Work-as-Job.";
                return false;
            }
            if (JobTerms.IsAnyRival(dropoff.Location))
            {
                error = $"Drop-off '{dropoff.Location}' is not handled by the Work kernel yet.";
                return false;
            }
            if (!AtSite(game, player.SectorId, dropoff.Location))
            {
                error = $"Must be at {JobTerms.PlaceName(dropoff.Location)} to complete this job.";
                return false;
            }
            return FinishOrMisbehave(game, player, job, active, dropoff, WorkSite.Dropoff, WorkKind.Complete, true, out result, out error);
        }

        public bool TryProceedMisbehave(GameState game, string playerId, bool proceed, out WorkResult? result, out string? error)
        {
            result = null;
            var pending = game.PendingMisbehave;
            if (pending == null)
            {
                error = "No Misbehave is pending.";
                return false;
            }
            if (pending.PlayerId != playerId)
            {
                error = "This Misbehave belongs to another player.";
                return false;
            }
            var player = game.GetPlayer(playerId);
            if (game.Jobs == null || !game.Jobs.TryGet(pending.JobId, out var job))
            {
                error = $"Unknown job '{pending.JobId}'.";
                return false;
            }

            if (!proceed)
            {
                // FAQ 4.1 p.5: botched attempt leaves the Job Active until complete / Warrant-discard.
                var disgruntled = ActiveAlertRules.OnJobBotched(game, player);
                game.PendingMisbehave = null;
                game.WorkGearLocked = false;
                game.TryConsumeAction(TurnAction.Work, out _);
                result = new WorkResult(
                    pending.Site == WorkSite.Pickup ? WorkKind.Pickup : WorkKind.Complete,
                    job, false, false, 0, disgruntled);
                error = null;
                return true;
            }

            pending.Remaining--;
            if (pending.Remaining > 0)
            {
                result = new WorkResult(
                    pending.Site == WorkSite.Pickup ? WorkKind.Pickup : WorkKind.Complete,
                    job, true, false, 0, 0);
                error = null;
                return true;
            }

            game.PendingMisbehave = null;
            game.WorkGearLocked = false;
            var active = player.FindActive(pending.JobId);
            var terms = pending.Site == WorkSite.Pickup ? JobTerms.Pickup(job) : JobTerms.Dropoff(job);
            var completeAfter = pending.Site == WorkSite.Dropoff || !JobTerms.HasDropoff(job);
            return ApplySite(
                game, player, job, active, terms,
                pending.Site == WorkSite.Pickup ? WorkKind.Pickup : WorkKind.Complete,
                completeAfter, out result, out error);
        }

        private bool FinishOrMisbehave(
            GameState game,
            PlayerState player,
            JobCard job,
            ActiveJob? active,
            JobSiteTerms terms,
            WorkSite site,
            WorkKind kind,
            bool completeAfter,
            out WorkResult? result,
            out string? error)
        {
            if (!completeAfter && !CanLoad(player, terms, out var loadError))
            {
                result = null;
                error = loadError;
                return false;
            }
            if (completeAfter && !CanUnload(player, active, JobTerms.HasDropoff(job) ? JobTerms.Dropoff(job) : terms, out var goodsError))
            {
                result = null;
                error = goodsError;
                return false;
            }

            // FAQ 4.1 p.5: Active on first Work Action — before Misbehave resolves.
            var becameActive = false;
            var moral = 0;
            if (active == null)
            {
                if (!TryActivate(player, job, out active, out error))
                {
                    result = null;
                    return false;
                }
                becameActive = true;
                if (job.Immoral)
                    moral = player.Roster.DisgruntleMoral();
            }

            var misbehave = terms.Misbehave + ActiveAlertRules.ExtraIllegalMisbehave(game, player, job);
            if (misbehave > 0)
            {
                // FAQ 4.1 p.2: cannot switch Gear during a Work Action.
                game.WorkGearLocked = true;
                game.PendingMisbehave = new PendingMisbehave(player.Id, job.Id, site, misbehave);
                result = new WorkResult(kind, job, true, becameActive, 0, moral);
                error = null;
                return true;
            }

            return ApplySite(game, player, job, active, terms, kind, completeAfter, becameActive, moral, out result, out error);
        }

        private bool ApplySite(
            GameState game,
            PlayerState player,
            JobCard job,
            ActiveJob? active,
            JobSiteTerms terms,
            WorkKind kind,
            bool completeAfter,
            out WorkResult? result,
            out string? error) =>
            ApplySite(game, player, job, active, terms, kind, completeAfter, false, 0, out result, out error);

        private bool ApplySite(
            GameState game,
            PlayerState player,
            JobCard job,
            ActiveJob? active,
            JobSiteTerms terms,
            WorkKind kind,
            bool completeAfter,
            bool alreadyBecameActive,
            int alreadyMoral,
            out WorkResult? result,
            out string? error)
        {
            result = null;
            var becameActive = alreadyBecameActive;
            var disgruntled = alreadyMoral;

            if (kind == WorkKind.Pickup || (kind == WorkKind.Complete && (active == null || !active.PickedUp)))
            {
                if (!CanLoad(player, terms, out error))
                    return false;
                if (active == null)
                {
                    if (!TryActivate(player, job, out active, out error))
                        return false;
                    becameActive = true;
                    if (job.Immoral)
                        disgruntled = player.Roster.DisgruntleMoral();
                }

                LoadGoods(player, active, terms);
                active.PickedUp = true;
            }

            if (!completeAfter)
            {
                game.WorkGearLocked = false;
                game.TryConsumeAction(TurnAction.Work, out _);
                result = new WorkResult(WorkKind.Pickup, job, false, becameActive, 0, disgruntled);
                error = null;
                return true;
            }

            var deliveredActive = active ?? new ActiveJob(job.Id);
            var deliverTerms = JobTerms.HasDropoff(job) ? JobTerms.Dropoff(job) : terms;
            var fugiDelivered = CountFugitivesDelivered(deliveredActive, deliverTerms);
            var wright = AbilityDispatcher.FindFugitiveDeliverBonus(player);
            if (wright != null && fugiDelivered > 0 && _wrightBonusChoice?.AcceptBonus == null)
            {
                if (!TrySuspendWrightBonus(game, player, job.Id, fugiDelivered, out error))
                    return false;
                error = "Choose whether to take Wright's Immoral Fugitive bonus.";
                return false;
            }

            if (!UnloadGoods(player, deliveredActive, deliverTerms, out error))
                return false;

            var pay = PayOut(game, player, job, deliveredActive);
            if (wright != null && fugiDelivered > 0 && _wrightBonusChoice?.AcceptBonus == true)
            {
                var per = wright.Amount > 0 ? wright.Amount : 100;
                pay += per * fugiDelivered;
                disgruntled += player.Roster.DisgruntleMoral();
            }
            _wrightBonusChoice = null;
            player.Cash += pay;
            if (game.Contacts != null && game.Contacts.TryFindByName(job.ContactName, out var contact))
            {
                ContactSolidBenefits.BecomeSolid(game, player, contact.Id);
                if (game.ContactDecks != null && game.ContactDecks.TryGet(contact.Name, out var deck))
                    deck.MoveToDiscard(job);
            }

            player.JobHand.Remove(job.Id);
            player.RemoveActive(job.Id);
            // ScenarioCards.json Increased Enforcement (Any Port): Illegal Job → Warrant.
            // Issued after completion (Job already leaves Active), so FAQ Warrant-discard does not apply.
            if (game.Scenario != null && game.Scenario.IncreasedEnforcement && !job.Legal)
                player.Warrants++;
            ActiveAlertRules.OnJobCompleted(game, job.ContactName);
            game.WorkGearLocked = false;
            game.TryConsumeAction(TurnAction.Work, out _);
            result = new WorkResult(WorkKind.Complete, job, false, false, pay, disgruntled);
            error = null;
            return true;
        }

        /// <summary>
        /// FAQ 4.1 p.5 / Director's Cut: place the Job in the Active Job area on first Work.
        /// </summary>
        private static bool TryActivate(PlayerState player, JobCard job, out ActiveJob active, out string? error)
        {
            error = null;
            var existing = player.FindActive(job.Id);
            if (existing != null)
            {
                active = existing;
                return true;
            }
            if (player.ActiveJobs.Count >= player.ActiveJobLimit)
            {
                active = null!;
                error = $"Already have {player.ActiveJobLimit} active job(s).";
                return false;
            }
            player.JobHand.Remove(job.Id);
            active = new ActiveJob(job.Id);
            player.ActiveJobs.Add(active);
            return true;
        }

        private static bool CanLoad(PlayerState player, JobSiteTerms terms, out string? error) =>
            HoldSpace.TryExplain(
                player,
                out error,
                addParts: terms.Parts,
                addCargo: terms.Cargo,
                addContraband: terms.Contraband,
                addPassengers: terms.PassengersUnlimited ? 1 : terms.Passengers,
                addFugitives: terms.FugitivesUnlimited ? 1 : terms.Fugitives);

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

        private static bool CanUnload(PlayerState player, ActiveJob? active, JobSiteTerms terms, out string? error) =>
            UnloadGoods(player, active ?? new ActiveJob(""), terms, out error, false);

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
            if (player.Cargo < cargo || player.Contraband < contra || player.Parts < parts
                || player.Fugitives < fugi || player.Passengers < pass)
            {
                error = "Ship is not carrying the goods this job requires.";
                return false;
            }
            if (!apply)
                return true;
            player.Cargo -= cargo;
            player.Contraband -= contra;
            player.Parts -= parts;
            player.Fugitives -= fugi;
            player.Passengers -= pass;
            return true;
        }

        private static int PayOut(GameState game, PlayerState player, JobCard job, ActiveJob active)
        {
            var pay = job.PayBase ?? 0;
            if (JobTerms.PayPerPassenger(job))
                pay *= System.Math.Max(1, active.Passengers);
            pay += JobTerms.ProfessionBonus(job, player.Roster.HasProfession);
            // Keyword Bonus Tab (TRANSPORT +200): same availability as Misbehave HasTag —
            // roster (incl. Disgruntled) + carried Gear. Onboard Ship Gear is unused (FAQ 4.1 p.2).
            pay += JobTerms.KeywordBonus(job, kw => MisbehaveResolver.HasTag(game, player, kw));
            pay += ContactSolidBenefits.CompletionBonus(game, player, job);
            var partsBonus = JobTerms.ProfessionPartsBonus(job, player.Roster.HasProfession);
            if (partsBonus > 0 && HoldSpace.Fits(player, addParts: partsBonus))
                player.Parts += partsBonus;
            return pay;
        }

        /// <summary>
        /// GF9 Make-Work: Work Action in a Planetary Sector for $200.
        /// Holder may also take a Fugitive Token.
        /// </summary>
        public bool TryMakeWork(
            GameState game,
            string playerId,
            out WorkResult? result,
            out string? error,
            MakeWorkChoice? choice = null)
        {
            result = null;
            if (!CanStart(game, playerId, out var player, out error))
                return false;
            if (game.PendingMisbehave != null)
            {
                error = "Finish the pending Misbehave before working again.";
                return false;
            }
            if (!game.Map.TryGet(player.SectorId, out var sector) || !sector.IsPlanetary)
            {
                error = "Make-Work requires a Planetary Sector.";
                return false;
            }

            var working = choice ?? _makeWorkChoice ?? new MakeWorkChoice();
            var holder = AbilityDispatcher.HasMakeWorkTakeFugitive(player);
            if (holder && working.TakeFugitive == null)
            {
                _makeWorkChoice = working;
                var pending = new PendingChoice(
                    player.Id,
                    PendingChoiceKinds.MakeWorkFugitive,
                    options: new[] { HolderFugitiveOptions.TakeFugitive, HolderFugitiveOptions.Decline },
                    prompt: "Take a Fugitive Token with Make-Work?");
                if (!game.TrySetPendingChoice(pending, out error))
                    return false;
                error = "Choose whether to take a Fugitive Token.";
                return false;
            }

            var fugitives = 0;
            if (holder && working.TakeFugitive == true)
            {
                if (!HoldSpace.Fits(player, addFugitives: 1))
                {
                    error = "No hold space for a Fugitive Token.";
                    return false;
                }
                player.Fugitives += 1;
                fugitives = 1;
            }

            player.Cash += MakeWorkPay;
            _makeWorkChoice = null;
            game.TryConsumeAction(TurnAction.Work, out _);
            result = new WorkResult(WorkKind.MakeWork, null, false, false, MakeWorkPay, 0, fugitives);
            error = null;
            return true;
        }

        public bool TryResumeMakeWorkFugitive(
            GameState game,
            ChoiceSubmission submission,
            out WorkResult? result,
            out string? error)
        {
            result = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.MakeWorkFugitive,
                    System.StringComparison.Ordinal))
            {
                error = "No Make-Work Fugitive choice is pending.";
                return false;
            }

            var take = false;
            if (submission.Accepted != null)
                take = submission.Accepted.Value;
            else if (string.Equals(
                         submission.SelectedOptionId,
                         HolderFugitiveOptions.TakeFugitive,
                         System.StringComparison.Ordinal))
                take = true;
            else if (string.Equals(
                         submission.SelectedOptionId,
                         HolderFugitiveOptions.Decline,
                         System.StringComparison.Ordinal))
                take = false;
            else
            {
                error = "Take Fugitive or decline.";
                return false;
            }

            if (!game.TrySubmitChoice(game.PendingChoice.PlayerId, submission, out _, out error))
                return false;

            _makeWorkChoice = new MakeWorkChoice { TakeFugitive = take };
            return TryMakeWork(game, game.CurrentPlayer.Id, out result, out error, _makeWorkChoice);
        }

        /// <summary>
        /// Early's Datascope: Work Action — reveal top 3 Supply at current planet into discard.
        /// </summary>
        public bool TryDatascope(
            GameState game,
            string playerId,
            out WorkResult? result,
            out string? error)
        {
            result = null;
            if (!CanStart(game, playerId, out var player, out error))
                return false;
            if (!AbilityDispatcher.HasWorkRevealDiscardSupply(game, player))
            {
                error = "Early's Datascope must be carried to use this Work Action.";
                return false;
            }
            if (!game.Map.TryGet(player.SectorId, out var sector)
                || string.IsNullOrWhiteSpace(sector.Planet)
                || game.SupplyDecks == null
                || !game.SupplyDecks.TryGet(sector.Planet, out var market))
            {
                error = "Datascope requires a Supply planet sector.";
                return false;
            }

            var n = AbilityDispatcher.WorkRevealDiscardSupplyAmount(game, player);
            var before = market.Discard.Count;
            market.PrimeToDiscard(n);
            var discarded = market.Discard.Count - before;
            game.TryConsumeAction(TurnAction.Work, out _);
            result = new WorkResult(WorkKind.Datascope, null, false, false, 0, 0, supplyDiscarded: discarded);
            error = null;
            return true;
        }

        public bool TryResumeWrightBonus(
            GameState game,
            ChoiceSubmission submission,
            out WorkResult? result,
            out string? error)
        {
            result = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.FugitiveDeliverBonus,
                    System.StringComparison.Ordinal))
            {
                error = "No Wright bonus choice is pending.";
                return false;
            }

            var accept = false;
            if (submission.Accepted != null)
                accept = submission.Accepted.Value;
            else if (string.Equals(
                         submission.SelectedOptionId,
                         WrightBonusOptions.TakeBonus,
                         System.StringComparison.Ordinal))
                accept = true;
            else if (string.Equals(
                         submission.SelectedOptionId,
                         WrightBonusOptions.Decline,
                         System.StringComparison.Ordinal))
                accept = false;
            else
            {
                error = "Take Wright bonus or decline.";
                return false;
            }

            var jobId = game.PendingChoice.ContextId;
            if (!game.TrySubmitChoice(game.PendingChoice.PlayerId, submission, out _, out error))
                return false;

            _wrightBonusChoice = new WrightBonusChoice { AcceptBonus = accept };
            return TryWork(game, game.CurrentPlayer.Id, jobId ?? "", out result, out error);
        }

        private static bool TrySuspendWrightBonus(
            GameState game,
            PlayerState player,
            string jobId,
            int fugitives,
            out string? error)
        {
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.FugitiveDeliverBonus,
                contextId: jobId,
                options: new[] { WrightBonusOptions.TakeBonus, WrightBonusOptions.Decline },
                prompt: $"Take ${100 * fugitives} Immoral bonus for delivering Fugitives?");
            return game.TrySetPendingChoice(pending, out error);
        }

        private static int CountFugitivesDelivered(ActiveJob active, JobSiteTerms terms)
        {
            if (terms.FugitivesUnlimited)
                return active.Fugitives;
            if (terms.Fugitives > 0)
                return terms.Fugitives;
            return active.Fugitives;
        }

        /// <summary>
        /// Planet name match, or Operative's Corvette token co-location when that expansion is in play.
        /// </summary>
        private static bool AtSite(GameState game, string sectorId, string? location)
        {
            // Kalidasa / Blue Sun: drop-off at the Corvette token sector when the ship is selected.
            if (JobTerms.IsOperativesCorvette(location))
            {
                return game.Tokens.OperativeCorvetteSectorId != null
                    && game.Tokens.OperativeCorvetteSectorId == sectorId;
            }

            var name = JobTerms.PlaceName(location);
            return !string.IsNullOrEmpty(name) && game.Map.SatisfiesDestination(sectorId, name);
        }

        private static bool CanWorkContact(GameState game, PlayerState player, JobCard job, out string? error)
        {
            error = null;
            if (game.Contacts != null
                && game.Contacts.TryFindByName(job.ContactName, out var contact)
                && contact.IsHiggins
                && player.Roster.HasName("Jayne"))
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
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            return game.CanTakeAction(TurnAction.Work, out error);
        }
    }
}
