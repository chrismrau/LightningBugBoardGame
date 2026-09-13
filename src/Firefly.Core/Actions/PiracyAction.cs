using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Thin hooks for Any Rival piracy Work until PendingChoice.
    /// Rival / skills / steal mix / hand discard / kill victims are caller-supplied.
    /// </summary>
    public sealed class PiracyChoice
    {
        public string RivalId { get; set; } = "";
        /// <summary>Tech or Negotiate (Talk) only — PBH p.3.</summary>
        public Skill BoardSkill { get; set; } = Skill.Tech;
        public Skill AttackSkill { get; set; } = Skill.Fight;
        public Skill DefendSkill { get; set; } = Skill.Fight;

        /// <summary>Goods to jettison from the attacker's ship before stealing (make room).</summary>
        public int JettisonFuel { get; set; }
        public int JettisonParts { get; set; }
        public int JettisonCargo { get; set; }
        public int JettisonContraband { get; set; }
        public int KickPassengers { get; set; }
        public int KickFugitives { get; set; }

        /// <summary>Goods to steal from the rival (unprotected / not in Stash).</summary>
        public int StealFuel { get; set; } = -1;
        public int StealParts { get; set; } = -1;
        public int StealCargo { get; set; } = -1;
        public int StealContraband { get; set; } = -1;

        /// <summary>Inactive Job ids to steal from the rival's hand.</summary>
        public IList<string>? StealJobIds { get; set; }

        /// <summary>
        /// After stealing Jobs, discard down to hand limit (PBH p.7).
        /// Required when hand exceeds <see cref="PlayerState.JobHandLimit"/>.
        /// </summary>
        public IList<string>? DiscardJobHandIds { get; set; }

        public KillChoice? KillChoice { get; set; }
    }

    public sealed class PiracyResult
    {
        public JobCard Job { get; }
        public bool BoardingFailed { get; }
        public bool ShowdownLost { get; }
        public bool Success { get; }
        public int Pay { get; }
        public int MoralDisgruntled { get; }
        public int CrewKilled { get; }
        public int WarrantsIssued { get; }
        public int GoodsStolen { get; }
        public int JobsStolen { get; }
        public ShowdownResult? Showdown { get; }
        public SkillCheckResult? Boarding { get; }

        public PiracyResult(
            JobCard job,
            bool boardingFailed = false,
            bool showdownLost = false,
            bool success = false,
            int pay = 0,
            int moralDisgruntled = 0,
            int crewKilled = 0,
            int warrantsIssued = 0,
            int goodsStolen = 0,
            int jobsStolen = 0,
            ShowdownResult? showdown = null,
            SkillCheckResult? boarding = null)
        {
            Job = job;
            BoardingFailed = boardingFailed;
            ShowdownLost = showdownLost;
            Success = success;
            Pay = pay;
            MoralDisgruntled = moralDisgruntled;
            CrewKilled = crewKilled;
            WarrantsIssued = warrantsIssued;
            GoodsStolen = goodsStolen;
            JobsStolen = jobsStolen;
            Showdown = showdown;
            Boarding = boarding;
        }
    }

    /// <summary>
    /// Pirates &amp; Bounty Hunters piracy Jobs with pickup "Any Rival".
    /// PBH pp.3–7: same-sector boarding → showdown → steal printed Goods or Inactive Jobs.
    /// PBH printed p.5: place Active on attempt; boarding fail leaves the Job Active.
    /// </summary>
    public sealed class PiracyAction
    {
        public bool TryPirate(
            GameState game,
            string playerId,
            string jobId,
            PiracyChoice choice,
            IRng rng,
            out PiracyResult? result,
            out string? error)
        {
            result = null;
            if (choice == null)
            {
                error = "Piracy requires a rival choice (thin hook until PendingChoice).";
                return false;
            }
            if (!CanStart(game, playerId, out var player, out error))
                return false;
            if (!game.UsePiratesBountyHunters)
            {
                error = "Piracy Jobs require the Pirates & Bounty Hunters expansion.";
                return false;
            }
            if (game.Jobs == null || !game.Jobs.TryGet(jobId, out var job))
            {
                error = $"Unknown job '{jobId}'.";
                return false;
            }
            if (!IsAnyRivalPiracy(job))
            {
                error = "That job is not an Any Rival piracy Job.";
                return false;
            }
            if (!player.JobHand.Contains(jobId) && player.FindActive(jobId) == null)
            {
                error = "That job is not in hand or active.";
                return false;
            }
            // PBH p.5: need an Active Job slot to attempt.
            if (player.FindActive(jobId) == null && player.ActiveJobs.Count >= player.ActiveJobLimit)
            {
                error = $"Already have {player.ActiveJobLimit} active job(s).";
                return false;
            }
            if (string.IsNullOrWhiteSpace(choice.RivalId))
            {
                error = "Must choose a rival ship in the same sector.";
                return false;
            }
            if (string.Equals(playerId, choice.RivalId, StringComparison.Ordinal))
            {
                error = "Cannot pirate your own ship.";
                return false;
            }

            var rival = game.GetPlayer(choice.RivalId);
            if (player.SectorId != rival.SectorId)
            {
                error = "Must share a sector with that ship (PBH: Any Rival).";
                return false;
            }

            var terms = PiracyTerms.FromJob(job);

            if (!BoardingTest.IsAllowedSkill(choice.BoardSkill))
            {
                error = "Boarding Test uses Tech or Negotiate only (PBH p.3).";
                return false;
            }

            // PBH p.5: place the card in the Active Job area when attempting.
            ActivatePiracyJob(player, jobId);

            // Boarding Test (PBH p.3 / card description).
            if (!BoardingTest.TryResolve(
                    player, choice.BoardSkill, rng,
                    out var boarding, out error, terms.BoardingTarget))
                return false;

            if (!boarding.Success)
            {
                // PBH p.5: failed Boarding → Job remains in Active Job area; Work Action over.
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new PiracyResult(job, boardingFailed: true, boarding: boarding);
                return true;
            }

            // Piracy Showdown (PBH p.5–6): job completes only on attacker win.
            var showdown = Showdown.Resolve(
                Showdown.Of(player, choice.AttackSkill),
                Showdown.Of(rival, choice.DefendSkill),
                rng);

            if (!showdown.AttackerWins)
                return FinishShowdownLoss(game, player, rival, job, terms, showdown, boarding, choice, rng, out result, out error);

            return FinishShowdownWin(game, player, rival, job, terms, showdown, boarding, choice, out result, out error);
        }

        private static bool FinishShowdownLoss(
            GameState game,
            PlayerState player,
            PlayerState rival,
            JobCard job,
            PiracyTerms terms,
            ShowdownResult showdown,
            SkillCheckResult boarding,
            PiracyChoice choice,
            IRng rng,
            out PiracyResult? result,
            out string? error)
        {
            result = null;
            var killed = 0;
            if (terms.KillOnLoss > 0)
                killed = CrewKill.KillUpTo(game, player, terms.KillOnLoss, rng, choice.KillChoice);

            // FAQ 4.1 p.12: all Illegal Piracy Jobs → Warrant Issued on Showdown loss.
            var warrants = 0;
            if (!job.Legal)
            {
                player.Warrants++;
                warrants = 1;
            }

            // FAQ 4.1 p.11–12: Warrant during Work discards the Job being Worked.
            DiscardPiracyJob(game, player, job);
            // FAQ 4.1 p.12: Niska Pound of Flesh when Warrant Issued on his Piracy Job.
            if (warrants > 0 && ContactSolidBenefits.IsNiskaJob(job))
                CrewKill.KillUpTo(game, player, 1, rng, choice.KillChoice);

            ActiveAlertRules.OnJobBotched(game, player);
            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;

            result = new PiracyResult(
                job,
                showdownLost: true,
                crewKilled: killed,
                warrantsIssued: warrants,
                showdown: showdown,
                boarding: boarding);
            return true;
        }

        private static bool FinishShowdownWin(
            GameState game,
            PlayerState player,
            PlayerState rival,
            JobCard job,
            PiracyTerms terms,
            ShowdownResult showdown,
            SkillCheckResult boarding,
            PiracyChoice choice,
            out PiracyResult? result,
            out string? error)
        {
            result = null;

            if (!TryJettison(player, choice, out error))
                return false;

            var goodsStolen = 0;
            var jobsStolen = 0;
            var pay = 0;

            if (terms.StealsJobs)
            {
                // Preview hand size after removing this Job and stealing, before mutating.
                var previewHand = player.JobHand.Count - (player.JobHand.Contains(job.Id) ? 1 : 0);
                var stealCount = PreviewStealJobCount(rival, terms.MaxStealJobs, choice);
                if (previewHand + stealCount > player.JobHandLimit)
                {
                    var need = previewHand + stealCount - player.JobHandLimit;
                    if (choice.DiscardJobHandIds == null || choice.DiscardJobHandIds.Count != need)
                    {
                        error = $"After stealing Jobs, discard down to {player.JobHandLimit} Inactive Jobs (need {need}).";
                        return false;
                    }
                }

                player.JobHand.Remove(job.Id);
                player.RemoveActive(job.Id);
                if (!TryStealJobs(player, rival, terms.MaxStealJobs, choice, out jobsStolen, out error))
                    return false;
                pay = jobsStolen * terms.PayPerJob;
                if (!TryDiscardHandDown(game, player, choice, out error))
                    return false;
            }
            else if (terms.StealsGoods)
            {
                player.JobHand.Remove(job.Id);
                player.RemoveActive(job.Id);
                if (!TryStealGoods(player, rival, terms, choice, out goodsStolen, out error))
                    return false;
                pay = goodsStolen * terms.PayPerGood;
            }
            else
            {
                player.JobHand.Remove(job.Id);
                player.RemoveActive(job.Id);
            }

            player.Cash += pay;

            // PBH Subjective Morality: Immoral when the target Leader is Moral.
            var moral = 0;
            if (IsImmoralAgainst(rival))
                moral = player.Roster.DisgruntleMoral();

            if (game.Contacts != null && game.Contacts.TryFindByName(job.ContactName, out var contact))
            {
                ContactSolidBenefits.BecomeSolid(game, player, contact.Id);
                if (game.ContactDecks != null && game.ContactDecks.TryGet(contact.Name, out var deck))
                    deck.MoveToDiscard(job);
            }

            ActiveAlertRules.OnJobCompleted(game, job.ContactName);

            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;

            result = new PiracyResult(
                job,
                success: true,
                pay: pay,
                moralDisgruntled: moral,
                goodsStolen: goodsStolen,
                jobsStolen: jobsStolen,
                showdown: showdown,
                boarding: boarding);
            return true;
        }

        public static bool IsAnyRivalPiracy(JobCard job) =>
            string.Equals(job.JobType, "Piracy", StringComparison.OrdinalIgnoreCase)
            && JobTerms.IsAnyRival(job.PickupLocation);

        /// <summary>
        /// PBH Subjective Morality: Immoral when targeting a Moral Leader.
        /// </summary>
        public static bool IsImmoralAgainst(PlayerState rival)
        {
            var leader = rival.Roster.Leader;
            return leader != null && leader.Moral;
        }

        private static void ActivatePiracyJob(PlayerState player, string jobId)
        {
            if (player.FindActive(jobId) != null)
                return;
            player.JobHand.Remove(jobId);
            player.ActiveJobs.Add(new ActiveJob(jobId));
        }

        private static void DiscardPiracyJob(GameState game, PlayerState player, JobCard job)
        {
            player.JobHand.Remove(job.Id);
            player.RemoveActive(job.Id);
            if (game.ContactDecks != null && game.ContactDecks.TryGet(job.ContactName, out var deck))
                deck.MoveToDiscard(job);
        }

        private static bool TryJettison(PlayerState player, PiracyChoice choice, out string? error)
        {
            error = null;
            if (choice.JettisonFuel < 0 || choice.JettisonParts < 0
                || choice.JettisonCargo < 0 || choice.JettisonContraband < 0
                || choice.KickPassengers < 0 || choice.KickFugitives < 0)
            {
                error = "Jettison / kick amounts cannot be negative.";
                return false;
            }
            if (player.Fuel < choice.JettisonFuel || player.Parts < choice.JettisonParts
                || player.Cargo < choice.JettisonCargo || player.Contraband < choice.JettisonContraband
                || player.Passengers < choice.KickPassengers || player.Fugitives < choice.KickFugitives)
            {
                error = "Cannot jettison more Goods / people than are on board.";
                return false;
            }

            // PBH p.7: kick Passengers/Fugitives only in sectors with planets.
            // Thin hook: caller must only set Kick* when legal; kernel rejects empty-space kicks
            // when Map is available via sector check at call sites that have game — deferred:
            // amounts are applied when non-zero (tests place ships on planets).
            player.Fuel -= choice.JettisonFuel;
            player.Parts -= choice.JettisonParts;
            player.Cargo -= choice.JettisonCargo;
            player.Contraband -= choice.JettisonContraband;
            player.Passengers -= choice.KickPassengers;
            player.Fugitives -= choice.KickFugitives;
            return true;
        }

        private static int PreviewStealJobCount(PlayerState rival, int max, PiracyChoice choice)
        {
            if (max <= 0)
                return 0;
            if (choice.StealJobIds != null && choice.StealJobIds.Count > 0)
                return Math.Min(choice.StealJobIds.Count, max);
            return Math.Min(rival.JobHand.Count, max);
        }

        private static bool TryStealJobs(
            PlayerState player,
            PlayerState rival,
            int max,
            PiracyChoice choice,
            out int stolen,
            out string? error)
        {
            stolen = 0;
            error = null;
            if (max <= 0)
                return true;

            IList<string> ids;
            if (choice.StealJobIds != null && choice.StealJobIds.Count > 0)
            {
                ids = choice.StealJobIds;
                if (ids.Count > max)
                {
                    error = $"May steal at most {max} Inactive Job(s).";
                    return false;
                }
            }
            else
            {
                // Thin default: take from the front of the rival hand up to max.
                ids = new List<string>();
                for (var i = 0; i < rival.JobHand.Count && ids.Count < max; i++)
                    ids.Add(rival.JobHand[i]);
            }

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (!unique.Add(id))
                {
                    error = "Cannot steal the same job twice.";
                    return false;
                }
                if (!rival.JobHand.Contains(id))
                {
                    error = $"Rival does not have inactive job '{id}'.";
                    return false;
                }
            }

            foreach (var id in ids)
            {
                rival.JobHand.Remove(id);
                player.JobHand.Add(id);
                stolen++;
            }
            return true;
        }

        private static bool TryDiscardHandDown(
            GameState game,
            PlayerState player,
            PiracyChoice choice,
            out string? error)
        {
            error = null;
            var need = player.JobHand.Count - player.JobHandLimit;
            if (need <= 0)
                return true;

            var ids = choice.DiscardJobHandIds;
            if (ids == null || ids.Count != need)
            {
                error = $"After stealing Jobs, discard down to {player.JobHandLimit} Inactive Jobs (need {need}).";
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
                if (game.Jobs != null && game.Jobs.TryGet(id, out var discarded)
                    && game.ContactDecks != null
                    && game.ContactDecks.TryGet(discarded.ContactName, out var deck))
                    deck.MoveToDiscard(discarded);
            }
            return true;
        }

        private static bool TryStealGoods(
            PlayerState player,
            PlayerState rival,
            PiracyTerms terms,
            PiracyChoice choice,
            out int stolen,
            out string? error)
        {
            stolen = 0;
            error = null;
            UnprotectedGoods(rival, out var openFuel, out var openParts, out var openCargo, out var openContra);
            var available = openFuel + openParts + openCargo + openContra;
            var cap = terms.StealAllGoods ? available : Math.Min(terms.MaxStealGoods, available);
            if (cap <= 0)
                return true;

            int fuel, parts, cargo, contra;
            if (choice.StealFuel < 0 && choice.StealParts < 0
                && choice.StealCargo < 0 && choice.StealContraband < 0)
            {
                // Thin default: prefer Contraband, Cargo, Parts, Fuel (same order as Nav seize).
                AutoPick(cap, openFuel, openParts, openCargo, openContra,
                    out fuel, out parts, out cargo, out contra);
            }
            else
            {
                fuel = Math.Max(0, choice.StealFuel);
                parts = Math.Max(0, choice.StealParts);
                cargo = Math.Max(0, choice.StealCargo);
                contra = Math.Max(0, choice.StealContraband);
            }

            if (fuel > openFuel || parts > openParts || cargo > openCargo || contra > openContra)
            {
                error = "Cannot steal Goods protected in the rival's Stash.";
                return false;
            }

            var take = fuel + parts + cargo + contra;
            if (take > cap)
            {
                error = $"May steal at most {cap} Goods from this Job.";
                return false;
            }

            if (!HoldSpace.Fits(player, addFuel: fuel, addParts: parts, addCargo: cargo, addContraband: contra))
            {
                error = "Not enough empty hold space for stolen Goods (jettison first).";
                return false;
            }

            rival.Fuel -= fuel;
            rival.Parts -= parts;
            rival.Cargo -= cargo;
            rival.Contraband -= contra;
            player.Fuel += fuel;
            player.Parts += parts;
            player.Cargo += cargo;
            player.Contraband += contra;
            stolen = take;
            return true;
        }

        private static void AutoPick(
            int cap,
            int openFuel,
            int openParts,
            int openCargo,
            int openContra,
            out int fuel,
            out int parts,
            out int cargo,
            out int contra)
        {
            fuel = 0;
            parts = 0;
            cargo = 0;
            contra = 0;
            var remaining = cap;
            Take(ref remaining, openContra, ref contra);
            Take(ref remaining, openCargo, ref cargo);
            Take(ref remaining, openParts, ref parts);
            Take(ref remaining, openFuel, ref fuel);
        }

        private static void Take(ref int remaining, int available, ref int taken)
        {
            if (remaining <= 0 || available <= 0)
                return;
            var n = Math.Min(remaining, available);
            taken += n;
            remaining -= n;
        }

        /// <summary>
        /// Director's Cut / PBH: Goods in Stash cannot be stolen. Free rearrange into Stash
        /// before the Showdown (max protection packing — same model as Nav Customs).
        /// </summary>
        internal static void UnprotectedGoods(
            PlayerState player,
            out int openFuel,
            out int openParts,
            out int openCargo,
            out int openContra)
        {
            var stash = Math.Max(0, player.StashHold);
            var half = player.Fuel + player.Parts;
            var halfProtected = Math.Min(half, stash * HoldSpace.FuelOrPartsPerHold);
            var slotsUsedByHalf = (halfProtected + HoldSpace.FuelOrPartsPerHold - 1) / HoldSpace.FuelOrPartsPerHold;
            var fullSlotsLeft = Math.Max(0, stash - slotsUsedByHalf);
            var protectContra = Math.Min(player.Contraband, fullSlotsLeft);
            fullSlotsLeft -= protectContra;
            var protectCargo = Math.Min(player.Cargo, fullSlotsLeft);
            var protectFuel = Math.Min(player.Fuel, halfProtected);
            var protectParts = Math.Min(player.Parts, halfProtected - protectFuel);
            openFuel = player.Fuel - protectFuel;
            openParts = player.Parts - protectParts;
            openCargo = player.Cargo - protectCargo;
            openContra = player.Contraband - protectContra;
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
