using System;
using System.Text.RegularExpressions;
using Firefly.Core.Abilities;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public enum BountyHuntKind
    {
        Confrontation,
        LoneTarget,
        Betrayal,
        Jump
    }

    public sealed class BountyResult
    {
        public BountyHuntKind Kind { get; }
        public string BountyId { get; }
        public bool Success { get; }
        public bool BoardingFailed { get; }
        public int Pay { get; }
        public int MoralDisgruntled { get; }
        public int CrewKilled { get; }
        public bool Rescued { get; }
        public ShowdownResult? Showdown { get; }

        public BountyResult(
            BountyHuntKind kind,
            string bountyId,
            bool success,
            bool boardingFailed = false,
            int pay = 0,
            int moralDisgruntled = 0,
            int crewKilled = 0,
            bool rescued = false,
            ShowdownResult? showdown = null)
        {
            Kind = kind;
            BountyId = bountyId;
            Success = success;
            BoardingFailed = boardingFailed;
            Pay = pay;
            MoralDisgruntled = moralDisgruntled;
            CrewKilled = crewKilled;
            Rescued = rescued;
            Showdown = showdown;
        }
    }

    /// <summary>
    /// Pirates &amp; Bounty Hunters bounty hunting (PBH pp.8–12).
    /// Work action. Face-up Most Wanted only. Does not use a job-hand
    /// or active-job slot. One fugitive per Work action (Cortex jump
    /// is the printed exception).
    /// Thin choice hooks: rival/crew ids and rescue flag are caller-supplied
    /// until PendingChoice.
    /// </summary>
    public sealed class BountyAction
    {
        public const int BoardingTarget = 6;
        private static readonly Regex BonusPattern = new Regex(
            @"Bounty Bonus[:\s]*\+?\$(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private enum PendingBountyKind
        {
            None,
            Confront,
            Jump
        }

        private PendingBountyKind _pendingKind;
        private string? _pendingPlayerId;
        private string? _pendingBountyId;
        private string? _pendingRivalId;
        private string? _pendingCrewId;
        private Skill _pendingAttack;
        private Skill _pendingDefend;
        private Skill _pendingBoard;
        private bool _pendingRescue;
        private SkillCheckChoice? _pendingBoardingSkillCheck;
        private KillChoice? _pendingKillChoice;
        private bool _resuming;

        /// <summary>
        /// Confrontation: fugitive is in a rival's Crew — same sector, Boarding then Showdown (PBH p.10).
        /// Cortland Negotiate Boarding suspends Bribes via PendingChoice (same as Piracy).
        /// </summary>
        public bool TryApprehendRival(
            GameState game,
            string playerId,
            string bountyId,
            string rivalId,
            string crewId,
            Skill attackSkill,
            Skill defendSkill,
            Skill boardSkill,
            IRng rng,
            out BountyResult? result,
            out string? error,
            KillChoice? killChoice = null,
            SkillCheckChoice? boardingSkillCheck = null,
            bool? acceptMeadowsRedirect = null)
        {
            result = null;
            if (!_resuming)
                ClearPending();

            if (!BeginWork(game, playerId, bountyId, out var player, out var bounty, out error))
                return false;
            if (string.Equals(playerId, rivalId, StringComparison.Ordinal))
            {
                error = "Use betrayal to nab your own crew.";
                return false;
            }

            var rival = game.GetPlayer(rivalId);
            if (player.SectorId != rival.SectorId)
            {
                error = "Must share a sector with that ship.";
                return false;
            }
            var member = rival.Roster.Find(crewId);
            if (member == null || !bounty.MatchesCrewName(member.Name))
            {
                error = "That crew is not the wanted fugitive on that ship.";
                return false;
            }
            if (member.IsLeader)
            {
                error = "Cannot bind a Leader.";
                return false;
            }

            if (!BoardingTest.IsAllowedSkill(boardSkill))
            {
                error = "Boarding Test uses Tech or Negotiate only (PBH p.3).";
                return false;
            }

            if (!TryPassBoarding(
                    game, player, boardSkill, boardingSkillCheck, rng,
                    contextId: $"bounty-board:confront:{bounty.Id}",
                    out var boarded, out error))
            {
                if (game.PendingChoice != null)
                {
                    RememberConfront(
                        playerId, bounty.Id, rivalId, crewId,
                        attackSkill, defendSkill, boardSkill,
                        boardingSkillCheck, killChoice);
                }
                return false;
            }

            if (!boarded)
            {
                ClearPending();
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Confrontation, bounty.Id, success: false, boardingFailed: true);
                return true;
            }

            ClearPending();
            var showdown = Showdown.Resolve(Showdown.Of(player, attackSkill), Showdown.Of(rival, defendSkill), rng);
            if (!showdown.AttackerWins)
            {
                var killed = ApplyBotch(game, player, bounty, rng, killChoice);
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Confrontation, bounty.Id, false, crewKilled: killed, showdown: showdown);
                return true;
            }

            // Meadows on the defender: may Kill Meadows instead of Apprehend.
            if (MeadowsRedirect.NeedsChoice(rival, acceptMeadowsRedirect))
            {
                RememberMeadowsConfront(
                    playerId, bounty.Id, rivalId, crewId,
                    attackSkill, defendSkill, boardSkill, killChoice, boardingSkillCheck);
                if (!MeadowsRedirect.TrySuspend(
                        game, rival, $"bounty-apprehend:{bounty.Id}", out error))
                    return false;
                error = "Choose whether to Kill Meadows instead of Apprehend.";
                return false;
            }
            if (acceptMeadowsRedirect == true)
            {
                MeadowsRedirect.KillMeadowsInstead(game, rival, rng, killChoice);
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Confrontation, bounty.Id, false, showdown: showdown);
                return true;
            }

            rival.Roster.Remove(member.Id);
            Bind(game, player, bounty, member.Card);
            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.Confrontation, bounty.Id, true, showdown: showdown);
            return true;
        }

        private PendingBountyKind _pendingMeadowsKind;

        private void RememberMeadowsConfront(
            string playerId,
            string bountyId,
            string rivalId,
            string crewId,
            Skill attack,
            Skill defend,
            Skill board,
            KillChoice? killChoice,
            SkillCheckChoice? boarding)
        {
            _pendingMeadowsKind = PendingBountyKind.Confront;
            _pendingPlayerId = playerId;
            _pendingBountyId = bountyId;
            _pendingRivalId = rivalId;
            _pendingCrewId = crewId;
            _pendingAttack = attack;
            _pendingDefend = defend;
            _pendingBoard = board;
            _pendingKillChoice = killChoice;
            _pendingBoardingSkillCheck = boarding;
        }

        /// <summary>Resume Meadows redirect on Confrontation Apprehend (after Showdown already won).</summary>
        public bool TryResumeMeadowsRedirect(
            GameState game,
            ChoiceSubmission submission,
            IRng rng,
            out BountyResult? result,
            out string? error)
        {
            result = null;
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
            if (_pendingMeadowsKind != PendingBountyKind.Confront
                || string.IsNullOrWhiteSpace(_pendingPlayerId)
                || string.IsNullOrWhiteSpace(_pendingBountyId)
                || string.IsNullOrWhiteSpace(_pendingRivalId)
                || string.IsNullOrWhiteSpace(_pendingCrewId))
            {
                error = "Meadows bounty resume state is missing.";
                return false;
            }
            if (!MeadowsRedirect.TryParseAccept(submission, out var accept, out error))
                return false;
            if (!game.TrySubmitChoice(game.PendingChoice.PlayerId, submission, out _, out error))
                return false;

            var player = game.GetPlayer(_pendingPlayerId!);
            var rival = game.GetPlayer(_pendingRivalId!);
            if (game.Bounties == null || !game.Bounties.TryResolve(_pendingBountyId!, out var bounty))
            {
                error = $"Unknown bounty '{_pendingBountyId}'.";
                return false;
            }

            if (accept)
            {
                MeadowsRedirect.KillMeadowsInstead(game, rival, rng, _pendingKillChoice);
                ClearPending();
                _pendingMeadowsKind = PendingBountyKind.None;
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Confrontation, bounty.Id, false);
                return true;
            }

            var member = rival.Roster.Find(_pendingCrewId!);
            if (member == null)
            {
                error = "Wanted crew is no longer on that ship.";
                return false;
            }
            rival.Roster.Remove(member.Id);
            Bind(game, player, bounty, member.Card);
            ClearPending();
            _pendingMeadowsKind = PendingBountyKind.None;
            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.Confrontation, bounty.Id, true);
            return true;
        }

        /// <summary>
        /// Resume after Cortland/Bribes PendingChoice on a Negotiate Boarding Test.
        /// </summary>
        public bool TryResumeBoardingBribe(
            GameState game,
            ChoiceSubmission submission,
            IRng rng,
            out BountyResult? result,
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
            if (_pendingKind == PendingBountyKind.None
                || string.IsNullOrWhiteSpace(_pendingPlayerId)
                || string.IsNullOrWhiteSpace(_pendingBountyId)
                || string.IsNullOrWhiteSpace(_pendingRivalId))
            {
                error = "Bounty boarding bribe resume state is missing.";
                return false;
            }

            var player = game.GetPlayer(_pendingPlayerId!);
            if (!SkillCheck.TryMergeBribeSubmission(
                    player, submission, _pendingBoardingSkillCheck, out var merged, out error))
                return false;
            _pendingBoardingSkillCheck = merged;

            if (!game.TrySubmitChoice(_pendingPlayerId!, submission, out _, out error))
                return false;

            _resuming = true;
            try
            {
                if (_pendingKind == PendingBountyKind.Jump)
                {
                    return TryJump(
                        game,
                        _pendingPlayerId!,
                        _pendingRivalId!,
                        _pendingBountyId!,
                        _pendingAttack,
                        _pendingDefend,
                        _pendingBoard,
                        rng,
                        _pendingRescue,
                        out result,
                        out error,
                        _pendingKillChoice,
                        _pendingBoardingSkillCheck);
                }

                return TryApprehendRival(
                    game,
                    _pendingPlayerId!,
                    _pendingBountyId!,
                    _pendingRivalId!,
                    _pendingCrewId!,
                    _pendingAttack,
                    _pendingDefend,
                    _pendingBoard,
                    rng,
                    out result,
                    out error,
                    _pendingKillChoice,
                    _pendingBoardingSkillCheck);
            }
            finally
            {
                _resuming = false;
            }
        }

        public bool TryApprehendLone(
            GameState game,
            string playerId,
            string bountyId,
            string crewId,
            Skill attackSkill,
            IRng rng,
            out BountyResult? result,
            out string? error,
            KillChoice? killChoice = null)
        {
            result = null;
            if (!BeginWork(game, playerId, bountyId, out var player, out var bounty, out error))
                return false;
            if (game.Crew == null || !game.Crew.TryGet(crewId, out var crew))
            {
                error = $"Unknown crew '{crewId}'.";
                return false;
            }
            if (!bounty.MatchesCrewName(crew.Name))
            {
                error = $"{crew.Name} is not this bounty.";
                return false;
            }
            if (HiredAnywhere(game, crew.Id) || BoundAnywhere(game, crew.Id))
            {
                if (FindHiredOnRival(game, playerId, crew.Id, out var rivalId))
                {
                    error = $"{crew.Name} is on another ship — use confrontation in the same sector as {rivalId}.";
                    return false;
                }
                error = $"{crew.Name} is already in play.";
                return false;
            }

            // PBH p.10: Work while in the same sector as the Target Fugitive.
            // Lone Target = discard pile of a Supply Planet; otherwise Last Seen (printed pickup).
            if (!TryResolveLoneTargetPlanet(game, bounty, crew, out var targetPlanet, out error))
                return false;
            if (!AtPlanet(game, player, targetPlanet))
            {
                error = $"Must be at {targetPlanet} to nab {crew.Name}.";
                return false;
            }

            var showdown = Showdown.Resolve(Showdown.Of(player, attackSkill), Showdown.BestSkill(crew), rng);
            if (!showdown.AttackerWins)
            {
                var killed = ApplyBotch(game, player, bounty, rng, killChoice);
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.LoneTarget, bounty.Id, false, crewKilled: killed, showdown: showdown);
                return true;
            }

            PullCrewFromSupply(game, crew);
            Bind(game, player, bounty, crew);
            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.LoneTarget, bounty.Id, true, showdown: showdown);
            return true;
        }

        public bool TryBetray(
            GameState game,
            string playerId,
            string bountyId,
            string crewId,
            out BountyResult? result,
            out string? error,
            IRng? rng = null,
            bool? acceptMeadowsRedirect = null)
        {
            result = null;
            if (!BeginWork(game, playerId, bountyId, out var player, out var bounty, out error))
                return false;
            var member = player.Roster.Find(crewId);
            if (member == null || !bounty.MatchesCrewName(member.Name))
            {
                error = "That crew is not the wanted fugitive on your ship.";
                return false;
            }
            if (member.IsLeader)
            {
                error = "Cannot bind a Leader.";
                return false;
            }

            if (MeadowsRedirect.NeedsChoice(player, acceptMeadowsRedirect))
            {
                if (!MeadowsRedirect.TrySuspend(
                        game, player, $"bounty-betray:{bounty.Id}:{crewId}", out error))
                    return false;
                error = "Choose whether to Kill Meadows instead of Betrayal Apprehend.";
                return false;
            }
            if (acceptMeadowsRedirect == true)
            {
                MeadowsRedirect.KillMeadowsInstead(game, player, rng ?? new SystemRng(), null);
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Betrayal, bounty.Id, false);
                return true;
            }

            player.Roster.Remove(member.Id);
            var disgruntled = player.Roster.DisgruntleWhere(m => !m.IsLeader);
            Bind(game, player, bounty, member.Card);
            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.Betrayal, bounty.Id, true, moralDisgruntled: disgruntled);
            return true;
        }

        public bool TryDeliver(
            GameState game,
            string playerId,
            string bountyId,
            out BountyResult? result,
            out string? error)
        {
            result = null;
            if (!CanStart(game, playerId, out var player, out error))
                return false;
            if (game.Bounties == null || !game.Bounties.TryResolve(bountyId, out var bounty))
            {
                error = $"Unknown bounty '{bountyId}'.";
                return false;
            }
            var bound = FindBound(player, bounty.Id);
            if (bound == null || bound.Count == 0)
            {
                error = "You are not transporting that bounty.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(bounty.DropoffPlanet)
                || !AtPlanet(game, player, bounty.DropoffPlanet))
            {
                error = $"Must deliver at {bounty.DropoffPlanet}.";
                return false;
            }

            var pay = bounty.Pay * bound.Count + LawmanBonus(player) * bound.Count;
            player.Cash += pay;
            var moral = 0;
            if (bounty.Immoral)
                moral = player.Roster.DisgruntleMoral();
            foreach (var id in bound.CrewIds)
            {
                if (game.Crew != null && game.Crew.TryGet(id, out var crew))
                    game.RemovedFromPlay.Add(crew.Name);
            }
            player.BoundBounties.Remove(bound);
            game.BountyDeck?.RemoveFromGame(bounty);
            game.RemovedFromPlay.Add(bounty.Name);
            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.LoneTarget, bounty.Id, true, pay: pay, moralDisgruntled: moral);
            return true;
        }

        public bool TryJump(
            GameState game,
            string playerId,
            string fromPlayerId,
            string bountyId,
            Skill attackSkill,
            Skill defendSkill,
            Skill boardSkill,
            IRng rng,
            bool rescue,
            out BountyResult? result,
            out string? error,
            KillChoice? killChoice = null,
            SkillCheckChoice? boardingSkillCheck = null)
        {
            result = null;
            if (!_resuming)
                ClearPending();

            if (!CanStart(game, playerId, out var player, out error))
                return false;
            if (string.Equals(playerId, fromPlayerId, StringComparison.Ordinal))
            {
                error = "Cannot jump your own bounty.";
                return false;
            }
            var rival = game.GetPlayer(fromPlayerId);
            if (player.SectorId != rival.SectorId)
            {
                error = "Must share a sector with that ship.";
                return false;
            }
            if (game.Bounties == null || !game.Bounties.TryResolve(bountyId, out var bounty))
            {
                error = $"Unknown bounty '{bountyId}'.";
                return false;
            }
            var bound = FindBound(rival, bounty.Id);
            if (bound == null)
            {
                error = "That ship is not transporting that bounty.";
                return false;
            }

            if (!BoardingTest.IsAllowedSkill(boardSkill))
            {
                error = "Boarding Test uses Tech or Negotiate only (PBH p.3).";
                return false;
            }

            if (!TryPassBoarding(
                    game, player, boardSkill, boardingSkillCheck, rng,
                    contextId: $"bounty-board:jump:{bounty.Id}",
                    out var boarded, out error))
            {
                if (game.PendingChoice != null)
                {
                    RememberJump(
                        playerId, fromPlayerId, bounty.Id,
                        attackSkill, defendSkill, boardSkill, rescue,
                        boardingSkillCheck, killChoice);
                }
                return false;
            }

            if (!boarded)
            {
                ClearPending();
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Jump, bounty.Id, false, boardingFailed: true);
                return true;
            }

            ClearPending();
            var showdown = Showdown.Resolve(Showdown.Of(player, attackSkill), Showdown.Of(rival, defendSkill), rng);
            if (!showdown.AttackerWins)
            {
                var killed = ApplyBotch(game, player, bounty, rng, killChoice);
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Jump, bounty.Id, false, crewKilled: killed, showdown: showdown);
                return true;
            }

            rival.BoundBounties.Remove(bound);
            if (rescue)
            {
                foreach (var id in bound.CrewIds)
                {
                    if (game.Crew != null && game.Crew.TryGet(id, out var crew))
                        player.Roster.TryHire(crew, out _);
                }
                game.BountyDeck?.ReturnToBottom(bounty);
            }
            else
            {
                player.BoundBounties.Add(bound);
            }

            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.Jump, bounty.Id, true, rescued: rescue, showdown: showdown);
            return true;
        }

        private static bool BeginWork(
            GameState game,
            string playerId,
            string bountyId,
            out PlayerState player,
            out BountyCard bounty,
            out string? error)
        {
            bounty = null!;
            if (!CanStart(game, playerId, out player, out error))
                return false;
            if (game.BountyDeck == null)
            {
                error = "Bounty deck is not in play.";
                return false;
            }
            var face = game.BountyDeck.FindWanted(bountyId);
            var already = FindBound(player, bountyId);
            if (face == null && already == null)
            {
                error = "That bounty is not on the Most Wanted List.";
                return false;
            }
            bounty = face ?? game.Bounties!.Get(already!.BountyId);
            return true;
        }

        private static bool CanStart(GameState game, string playerId, out PlayerState player, out string? error)
        {
            player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            return game.CanTakeAction(TurnAction.Work, out error);
        }

        private static void Bind(GameState game, PlayerState player, BountyCard bounty, CrewCard crew)
        {
            var bound = FindBound(player, bounty.Id);
            if (bound == null)
            {
                if (game.BountyDeck != null && game.BountyDeck.FindWanted(bounty.Id) != null)
                    game.BountyDeck.TryClaimWanted(bounty.Id, out _);
                bound = new BoundBounty(bounty.Id, bounty.Name);
                player.BoundBounties.Add(bound);
            }
            bound.CrewIds.Add(crew.Id);
        }

        private static BoundBounty? FindBound(PlayerState player, string bountyId)
        {
            foreach (var bound in player.BoundBounties)
            {
                if (string.Equals(bound.BountyId, bountyId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(bound.BountyName, bountyId, StringComparison.OrdinalIgnoreCase))
                    return bound;
            }
            return null;
        }

        private static bool TryPassBoarding(
            GameState game,
            PlayerState player,
            Skill boardSkill,
            SkillCheckChoice? choice,
            IRng rng,
            string contextId,
            out bool success,
            out string? error)
        {
            success = false;
            var boardCheck = BoardingTest.BuildCheck(player, boardSkill, BoardingTarget);
            if (SkillCheck.NeedsBribeChoice(player, boardCheck, choice))
            {
                if (!SkillCheck.TrySuspendBribeChoice(game, player, contextId: contextId, out error))
                    return false;
                error = "Choose how many Bribes to pay before the Boarding Test.";
                return false;
            }

            if (!boardCheck.TryResolve(player, rng, out var result, out error, choice))
                return false;
            success = result.Success;
            return true;
        }

        private void RememberConfront(
            string playerId,
            string bountyId,
            string rivalId,
            string crewId,
            Skill attack,
            Skill defend,
            Skill board,
            SkillCheckChoice? boarding,
            KillChoice? kill)
        {
            _pendingKind = PendingBountyKind.Confront;
            _pendingPlayerId = playerId;
            _pendingBountyId = bountyId;
            _pendingRivalId = rivalId;
            _pendingCrewId = crewId;
            _pendingAttack = attack;
            _pendingDefend = defend;
            _pendingBoard = board;
            _pendingBoardingSkillCheck = boarding;
            _pendingKillChoice = kill;
            _pendingRescue = false;
        }

        private void RememberJump(
            string playerId,
            string rivalId,
            string bountyId,
            Skill attack,
            Skill defend,
            Skill board,
            bool rescue,
            SkillCheckChoice? boarding,
            KillChoice? kill)
        {
            _pendingKind = PendingBountyKind.Jump;
            _pendingPlayerId = playerId;
            _pendingBountyId = bountyId;
            _pendingRivalId = rivalId;
            _pendingCrewId = null;
            _pendingAttack = attack;
            _pendingDefend = defend;
            _pendingBoard = board;
            _pendingRescue = rescue;
            _pendingBoardingSkillCheck = boarding;
            _pendingKillChoice = kill;
        }

        private void ClearPending()
        {
            _pendingKind = PendingBountyKind.None;
            _pendingPlayerId = null;
            _pendingBountyId = null;
            _pendingRivalId = null;
            _pendingCrewId = null;
            _pendingBoardingSkillCheck = null;
            _pendingKillChoice = null;
            _pendingRescue = false;
        }

        private static int ApplyBotch(
            GameState game,
            PlayerState player,
            BountyCard bounty,
            IRng rng,
            KillChoice? killChoice = null)
        {
            if (!CrewKill.TryKillUpTo(
                    game, player, bounty.BotchKill, rng, out var killed, out var error, killChoice))
            {
                throw new System.InvalidOperationException(
                    error ?? "Bounty botch Kill N requires KillChoice.VictimCrewIds or PendingChoice.");
            }
            return killed;
        }

        private static bool AtPlanet(GameState game, PlayerState player, string planet)
        {
            if (planet.Equals("Command Cruiser", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrEmpty(game.Tokens.AllianceCruiserSectorId)
                    && game.Tokens.AllianceCruiserSectorId == player.SectorId;
            if (!game.Map.TryGet(player.SectorId, out var sector))
                return false;
            var here = BuyAction.ShopPlanet(sector);
            return string.Equals(here, planet, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sector.DisplayName, planet, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HiredAnywhere(GameState game, string crewId)
        {
            foreach (var player in game.Players)
            {
                if (player.Roster.Find(crewId) != null)
                    return true;
            }
            return false;
        }

        private static bool BoundAnywhere(GameState game, string crewId)
        {
            foreach (var player in game.Players)
            {
                foreach (var bound in player.BoundBounties)
                {
                    foreach (var id in bound.CrewIds)
                    {
                        if (id == crewId)
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool TryResolveLoneTargetPlanet(
            GameState game,
            BountyCard bounty,
            CrewCard crew,
            out string targetPlanet,
            out string? error)
        {
            targetPlanet = "";
            error = null;

            // PBH p.10 Lone Target: fugitive in a Supply Planet discard (prefer actual card location).
            if (TryFindCrewSupplyPlanet(game, crew, out var supplyPlanet))
            {
                targetPlanet = supplyPlanet;
                return true;
            }

            // Wanted Last Seen when the card is not tracked on a market (or still "out there").
            if (!bounty.IsCortex && !string.IsNullOrWhiteSpace(bounty.PickupPlanet))
            {
                targetPlanet = bounty.PickupPlanet!;
                return true;
            }

            // Cortex Alert has no Last Seen — subject location is wherever matching crew currently is.
            error = bounty.IsCortex
                ? $"{crew.Name} is not on a Supply Planet — nab them via confrontation or betrayal where they are."
                : $"Must be at {bounty.PickupPlanet} to nab {crew.Name}.";
            return false;
        }

        private static bool TryFindCrewSupplyPlanet(GameState game, CrewCard crew, out string planet)
        {
            planet = "";
            if (game.SupplyDecks == null)
                return false;

            // Prefer discard (printed Lone Target), then face-up / deck at that market.
            foreach (var market in game.SupplyDecks.Markets)
            {
                if (ContainsCrew(market.Discard, crew.Id))
                {
                    planet = market.Planet;
                    return true;
                }
            }
            foreach (var market in game.SupplyDecks.Markets)
            {
                if (ContainsCrew(market.FaceUp, crew.Id) || ContainsCrew(market.Deck, crew.Id))
                {
                    planet = market.Planet;
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsCrew(System.Collections.Generic.IList<SupplyCard> pile, string crewId)
        {
            foreach (var card in pile)
            {
                if (card.Id == crewId)
                    return true;
            }
            return false;
        }

        private static bool FindHiredOnRival(GameState game, string playerId, string crewId, out string rivalId)
        {
            rivalId = "";
            foreach (var other in game.Players)
            {
                if (string.Equals(other.Id, playerId, StringComparison.Ordinal))
                    continue;
                if (other.Roster.Find(crewId) != null)
                {
                    rivalId = other.Id;
                    return true;
                }
            }
            return false;
        }

        private static void PullCrewFromSupply(GameState game, CrewCard crew)
        {
            if (game.SupplyDecks == null)
                return;
            foreach (var market in game.SupplyDecks.Markets)
            {
                Strip(market.Deck, crew.Id);
                Strip(market.FaceUp, crew.Id);
                Strip(market.Discard, crew.Id);
                market.Refill();
            }
        }

        private static void Strip(System.Collections.Generic.IList<SupplyCard> pile, string crewId)
        {
            for (var i = pile.Count - 1; i >= 0; i--)
            {
                if (pile[i].Id == crewId)
                    pile.RemoveAt(i);
            }
        }

        public static int LawmanBonus(PlayerState player)
        {
            var total = 0;
            foreach (var member in player.Roster.Members)
            {
                var text = member.Card.Description;
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var match = BonusPattern.Match(text);
                if (match.Success)
                    total += int.Parse(match.Groups[1].Value);
            }
            return total;
        }
    }
}
