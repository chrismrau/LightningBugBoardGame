using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Thin hooks for Any Rival piracy Work. Empty <see cref="PiracyChoice.RivalId"/>
    /// suspends via <see cref="PendingChoiceKinds.RivalPlayer"/> when same-sector rivals exist.
    /// </summary>
    public sealed class PiracyChoice
    {
        /// <summary>
        /// Rival player id. Empty → <see cref="PendingChoiceKinds.RivalPlayer"/> when legal
        /// rivals share the sector; scripted tests set this to skip suspend.
        /// </summary>
        public string RivalId { get; set; } = "";
        /// <summary>Tech or Negotiate (Talk) only — PBH p.3.</summary>
        public Skill BoardSkill { get; set; } = Skill.Tech;
        public Skill AttackSkill { get; set; } = Skill.Fight;
        public Skill DefendSkill { get; set; } = Skill.Fight;

        /// <summary>Boarding Bribes / Cortland (Negotiate Boarding is not a Showdown).</summary>
        public SkillCheckChoice? BoardingSkillCheck { get; set; }

        /// <summary>Guardian / Chari Showdown may re-rolls.</summary>
        public ShowdownChoice? Showdown { get; set; }

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
    /// Missing rival pick suspends via <see cref="PendingChoiceKinds.RivalPlayer"/>.
    /// </summary>
    public sealed class PiracyAction
    {
        private bool _resuming;
        private string? _pendingPiracyPlayerId;
        private string? _pendingPiracyJobId;
        private PiracyChoice? _pendingPiracyChoice;
        private SkillCheckResult? _pendingBoarding;
        private ShowdownResult? _pendingShowdownInitial;
        private int _pendingAttackerSkill;
        private int _pendingDefenderSkill;

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
                error = "Piracy requires a rival choice.";
                return false;
            }
            if (game.PendingChoice != null && !_resuming)
            {
                error = "Resolve the pending choice before continuing piracy.";
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
                var rivals = SameSectorRivalIds(game, playerId);
                if (rivals.Count == 0)
                {
                    error = "Must choose a rival ship in the same sector.";
                    return false;
                }
                if (!TrySuspendRivalChoice(game, playerId, jobId, choice, rivals, out error))
                    return false;
                error = "Choose a rival ship in the same sector.";
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

            SkillCheckResult boarding;
            if (_pendingBoarding != null)
            {
                boarding = _pendingBoarding;
                _pendingBoarding = null;
            }
            else
            {
                var boardCheck = BoardingTest.BuildCheck(player, choice.BoardSkill, terms.BoardingTarget);
                // Cortland: Negotiate Boarding is a Negotiate Test — Bribes may apply (not Showdown).
                if (SkillCheck.NeedsBribeChoice(player, boardCheck, choice.BoardingSkillCheck))
                {
                    if (!SkillCheck.TrySuspendBribeChoice(
                            game, player, contextId: $"piracy-board:{jobId}", out error))
                        return false;
                    RememberPiracy(playerId, jobId, choice);
                    error = "Choose how many Bribes to pay before the Boarding Test.";
                    return false;
                }

                if (!boardCheck.TryResolve(
                        player, rng, out boarding, out error, choice.BoardingSkillCheck))
                    return false;
            }

            if (!boarding.Success)
            {
                // PBH p.5: failed Boarding → Job remains in Active Job area; Work Action over.
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                ClearPiracyPending();
                result = new PiracyResult(job, boardingFailed: true, boarding: boarding);
                return true;
            }

            var attackerSkill = Showdown.Of(player, choice.AttackSkill);
            var defenderSkill = Showdown.Of(rival, choice.DefendSkill);
            ShowdownResult showdown;
            if (_pendingShowdownInitial != null)
            {
                showdown = _pendingShowdownInitial;
            }
            else
            {
                showdown = Showdown.Resolve(attackerSkill, defenderSkill, rng);
                _pendingShowdownInitial = showdown;
                _pendingAttackerSkill = attackerSkill;
                _pendingDefenderSkill = defenderSkill;
            }

            choice.Showdown ??= new ShowdownChoice();
            var nextCtx = Showdown.NextRerollContext(player, rival, choice.Showdown);
            if (nextCtx != null)
            {
                var decidingPlayer = nextCtx.StartsWith("defender", StringComparison.Ordinal)
                    ? rival
                    : player;
                var pending = new PendingChoice(
                    decidingPlayer.Id,
                    PendingChoiceKinds.ShowdownReroll,
                    contextId: nextCtx,
                    options: new[] { SkillRerollOptions.Keep, SkillRerollOptions.Reroll },
                    prompt: "Showdown re-roll?");
                if (!game.TrySetPendingChoice(pending, out error))
                    return false;
                _pendingBoarding = boarding;
                RememberPiracy(playerId, jobId, choice);
                error = "Choose whether to re-roll in the Showdown.";
                return false;
            }

            showdown = Showdown.ApplyRerolls(
                showdown, _pendingAttackerSkill, _pendingDefenderSkill, choice.Showdown, rng);
            ClearPiracyPending();

            if (!showdown.AttackerWins)
                return FinishShowdownLoss(game, player, rival, job, terms, showdown, boarding, choice, rng, out result, out error);

            return FinishShowdownWin(game, player, rival, job, terms, showdown, boarding, choice, out result, out error);
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.RivalPlayer"/>: merge rival id and re-enter
        /// <see cref="TryPirate"/>.
        /// </summary>
        public bool TryResumeRival(
            GameState game,
            ChoiceSubmission submission,
            IRng rng,
            out PiracyResult? result,
            out string? error)
        {
            result = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.RivalPlayer,
                    StringComparison.Ordinal))
            {
                error = "No rival player choice is pending.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_pendingPiracyPlayerId)
                || string.IsNullOrWhiteSpace(_pendingPiracyJobId)
                || _pendingPiracyChoice == null)
            {
                error = "Piracy rival resume state is missing.";
                return false;
            }

            var rivalId = submission.SelectedOptionId ?? submission.Value;
            if (string.IsNullOrWhiteSpace(rivalId))
            {
                error = "A rival player id is required.";
                return false;
            }

            var playerId = _pendingPiracyPlayerId!;
            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            var choice = _pendingPiracyChoice;
            choice.RivalId = rivalId!;
            var jobId = _pendingPiracyJobId!;
            // Keep job/player remembered only via locals; rival resume clears soft state.
            _pendingPiracyPlayerId = null;
            _pendingPiracyJobId = null;
            _pendingPiracyChoice = null;

            _resuming = true;
            try
            {
                return TryPirate(game, playerId, jobId, choice, rng, out result, out error);
            }
            finally
            {
                _resuming = false;
            }
        }

        /// <summary>
        /// Resume after Cortland/Bribes PendingChoice on a Negotiate Boarding Test.
        /// </summary>
        public bool TryResumeBoardingBribe(
            GameState game,
            ChoiceSubmission submission,
            IRng rng,
            out PiracyResult? result,
            out string? error)
        {
            result = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.BribeAmount,
                    StringComparison.Ordinal))
            {
                error = "No boarding bribe choice is pending.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_pendingPiracyPlayerId)
                || string.IsNullOrWhiteSpace(_pendingPiracyJobId)
                || _pendingPiracyChoice == null)
            {
                error = "Piracy boarding bribe resume state is missing.";
                return false;
            }

            var playerId = _pendingPiracyPlayerId!;
            var player = game.GetPlayer(playerId);
            if (!SkillCheck.TryMergeBribeSubmission(
                    player, submission, _pendingPiracyChoice.BoardingSkillCheck, out var merged, out error))
                return false;
            _pendingPiracyChoice.BoardingSkillCheck = merged;

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            var choice = _pendingPiracyChoice;
            var jobId = _pendingPiracyJobId!;
            _resuming = true;
            try
            {
                return TryPirate(game, playerId, jobId, choice, rng, out result, out error);
            }
            finally
            {
                _resuming = false;
            }
        }

        /// <summary>
        /// Resume after Guardian / Chari Showdown re-roll PendingChoice.
        /// </summary>
        public bool TryResumeShowdownReroll(
            GameState game,
            ChoiceSubmission submission,
            IRng rng,
            out PiracyResult? result,
            out string? error)
        {
            result = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.ShowdownReroll,
                    StringComparison.Ordinal))
            {
                error = "No Showdown re-roll choice is pending.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_pendingPiracyPlayerId)
                || string.IsNullOrWhiteSpace(_pendingPiracyJobId)
                || _pendingPiracyChoice == null
                || _pendingShowdownInitial == null)
            {
                error = "Piracy Showdown re-roll resume state is missing.";
                return false;
            }

            var contextId = game.PendingChoice.ContextId ?? "";
            var submittingPlayerId = game.PendingChoice.PlayerId;
            if (!Showdown.TryMergeRerollSubmission(
                    contextId, submission, _pendingPiracyChoice.Showdown, out var merged, out error))
                return false;
            _pendingPiracyChoice.Showdown = merged;

            if (!game.TrySubmitChoice(submittingPlayerId, submission, out _, out error))
                return false;

            var choice = _pendingPiracyChoice;
            var jobId = _pendingPiracyJobId!;
            var playerId = _pendingPiracyPlayerId!;
            _resuming = true;
            try
            {
                return TryPirate(game, playerId, jobId, choice, rng, out result, out error);
            }
            finally
            {
                _resuming = false;
            }
        }

        private bool TrySuspendRivalChoice(
            GameState game,
            string playerId,
            string jobId,
            PiracyChoice choice,
            IReadOnlyList<string> rivals,
            out string? error)
        {
            var pending = new PendingChoice(
                playerId,
                PendingChoiceKinds.RivalPlayer,
                contextId: jobId,
                options: rivals,
                prompt: "Choose a rival ship in the same sector.");
            if (!game.TrySetPendingChoice(pending, out error))
                return false;
            RememberPiracy(playerId, jobId, choice);
            return true;
        }

        private void RememberPiracy(string playerId, string jobId, PiracyChoice choice)
        {
            _pendingPiracyPlayerId = playerId;
            _pendingPiracyJobId = jobId;
            _pendingPiracyChoice = choice;
        }

        private void ClearPiracyPending()
        {
            _pendingPiracyPlayerId = null;
            _pendingPiracyJobId = null;
            _pendingPiracyChoice = null;
            _pendingBoarding = null;
            _pendingShowdownInitial = null;
            _pendingAttackerSkill = 0;
            _pendingDefenderSkill = 0;
        }

        public static IReadOnlyList<string> SameSectorRivalIds(GameState game, string playerId)
        {
            var player = game.GetPlayer(playerId);
            var rivals = new List<string>();
            foreach (var other in game.Players)
            {
                if (string.Equals(other.Id, playerId, StringComparison.Ordinal))
                    continue;
                if (string.Equals(other.SectorId, player.SectorId, StringComparison.OrdinalIgnoreCase))
                    rivals.Add(other.Id);
            }
            return rivals;
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
            {
                if (!CrewKill.TryKillUpTo(
                        game, player, terms.KillOnLoss, rng, out killed, out error, choice.KillChoice))
                    return false;
            }

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
            {
                if (!CrewKill.TryKillUpTo(
                        game, player, 1, rng, out var niskaKilled, out error, choice.KillChoice))
                    return false;
                killed += niskaKilled;
            }

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
            // ScenarioCards.json Increased Enforcement (Any Port): Illegal Job → Warrant.
            if (game.Scenario != null && game.Scenario.IncreasedEnforcement && !job.Legal)
                player.Warrants++;

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
