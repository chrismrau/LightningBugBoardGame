using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Firefly.Core.Abilities;
using Firefly.Core.Cards;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public enum GoalWorkKind
    {
        Attempt,
        Completed,
        Botched
    }

    public sealed class GoalWorkResult
    {
        public GoalWorkKind Kind { get; }
        public int GoalNumber { get; }
        public bool AwaitingMisbehave { get; }
        public bool AwaitingChoice { get; }
        public int Killed { get; }
        public int Warrants { get; }
        public bool Completed { get; }
        public string? EvadedToSectorId { get; }

        public GoalWorkResult(
            GoalWorkKind kind,
            int goalNumber,
            bool awaitingMisbehave = false,
            bool awaitingChoice = false,
            int killed = 0,
            int warrants = 0,
            bool completed = false,
            string? evadedToSectorId = null)
        {
            Kind = kind;
            GoalNumber = goalNumber;
            AwaitingMisbehave = awaitingMisbehave;
            AwaitingChoice = awaitingChoice;
            Killed = killed;
            Warrants = warrants;
            Completed = completed;
            EvadedToSectorId = evadedToSectorId;
        }
    }

    /// <summary>
    /// Scripted choices for Goal Work skill / boarding / evade / pay-or-botch.
    /// </summary>
    public sealed class GoalWorkChoice
    {
        public SkillCheckChoice? SkillCheck { get; set; }
        public KillChoice? Kill { get; set; }
        public string? EvadeToSectorId { get; set; }
        /// <summary>Null = undecided; true = pay to complete; false = Attempt Botched.</summary>
        public bool? AcceptPayToComplete { get; set; }
        public IRng? Rng { get; set; }
        /// <summary>Zero Tolerance / Roberta thin hooks when a Goal issues a Warrant.</summary>
        public SolidRepChoice? SolidRep { get; set; }
    }

    /// <summary>
    /// GF9 / Director's Cut Working Goals: Work Action → Misbehave (if any) → Goal instructions.
    /// Job-only abilities do not apply (<see cref="AbilityContext.IsWorkingGoal"/>).
    /// </summary>
    public sealed class GoalWorkAction
    {
        private static readonly Regex PayAmountPattern = new Regex(
            @"Pay\s+\$([0-9,]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private GoalWorkChoice? _choice;

        public bool TryWorkGoal(
            GameState game,
            string playerId,
            int goalNumber,
            out GoalWorkResult? result,
            out string? error,
            GoalWorkChoice? choice = null)
        {
            result = null;
            _choice = choice;
            if (!CanStart(game, playerId, out var player, out error))
                return false;
            if (game.PendingMisbehave != null || game.PendingGoalWork != null)
            {
                error = "Finish the pending Work before starting a Goal.";
                return false;
            }
            if (game.Scenario == null)
            {
                error = "No Story Card is in play.";
                return false;
            }
            var goal = game.Scenario.Goal(goalNumber);
            if (goal == null)
            {
                error = $"Story Card has no Goal {goalNumber}.";
                return false;
            }
            if (!goal.IsWorkable)
            {
                error = "That Goal does not require a Work Action (FAQ 4.1 p.7).";
                return false;
            }
            if (player.CompletedGoals.Contains(goalNumber))
            {
                error = $"Goal {goalNumber} is already complete.";
                return false;
            }
            if (!PriorGoalsComplete(player, game.Scenario, goalNumber, out error))
                return false;
            if (!MeetsSolidGates(game, player, goal, out error))
                return false;
            if (!AtGoalSite(game, player, goal, out error))
                return false;

            // FAQ 4.1 p.2: cannot switch Gear during a Work Action (Job or Goal — user 2026-09-27).
            game.WorkGearLocked = true;

            if (goal.Misbehave > 0)
            {
                game.PendingMisbehave = new PendingMisbehave(player.Id, goalNumber, goal.Misbehave);
                game.PendingGoalWork = new PendingGoalWork(player.Id, goalNumber, GoalWorkPhase.Misbehave);
                result = new GoalWorkResult(GoalWorkKind.Attempt, goalNumber, awaitingMisbehave: true);
                error = null;
                return true;
            }

            // No Misbehave — pause in Boarding / Skill so dice and kills stay scriptable.
            game.PendingGoalWork = new PendingGoalWork(
                player.Id,
                goalNumber,
                goal.Boarding != null ? GoalWorkPhase.Boarding : GoalWorkPhase.Skill);
            result = new GoalWorkResult(GoalWorkKind.Attempt, goalNumber, awaitingChoice: true);
            error = null;
            return true;
        }

        /// <summary>
        /// Called when Goal Misbehave finishes (all Proceed) via <see cref="WorkAction.TryProceedMisbehave"/>.
        /// </summary>
        public bool TryContinueAfterMisbehave(
            GameState game,
            string playerId,
            out GoalWorkResult? result,
            out string? error,
            GoalWorkChoice? choice = null)
        {
            result = null;
            _choice = choice ?? _choice;
            var pending = game.PendingGoalWork;
            if (pending == null || pending.PlayerId != playerId)
            {
                error = "No Goal Work is pending after Misbehave.";
                return false;
            }
            var player = game.GetPlayer(playerId);
            var goal = game.Scenario?.Goal(pending.GoalNumber);
            if (goal == null)
            {
                error = $"Story Card has no Goal {pending.GoalNumber}.";
                return false;
            }

            pending.Phase = goal.Boarding != null && !pending.BoardingPassed
                ? GoalWorkPhase.Boarding
                : GoalWorkPhase.Skill;
            return ContinueGoal(game, player, goal, out result, out error);
        }

        public bool TryResume(
            GameState game,
            string playerId,
            out GoalWorkResult? result,
            out string? error,
            GoalWorkChoice? choice = null)
        {
            result = null;
            _choice = choice ?? _choice;
            var pending = game.PendingGoalWork;
            if (pending == null || pending.PlayerId != playerId)
            {
                error = "No Goal Work is pending.";
                return false;
            }
            var player = game.GetPlayer(playerId);
            var goal = game.Scenario?.Goal(pending.GoalNumber);
            if (goal == null)
            {
                error = $"Story Card has no Goal {pending.GoalNumber}.";
                return false;
            }
            return ContinueGoal(game, player, goal, out result, out error);
        }

        public bool TryResumePayOrBotch(
            GameState game,
            ChoiceSubmission submission,
            out GoalWorkResult? result,
            out string? error)
        {
            result = null;
            if (game.PendingChoice == null
                || !string.Equals(game.PendingChoice.Kind, PendingChoiceKinds.GoalPayOrBotch, StringComparison.Ordinal))
            {
                error = "No Goal pay-or-botch choice is pending.";
                return false;
            }

            var pay = string.Equals(
                submission.SelectedOptionId, GoalPayOrBotchOptions.PayComplete, StringComparison.Ordinal);
            var botch = string.Equals(
                submission.SelectedOptionId, GoalPayOrBotchOptions.Botch, StringComparison.Ordinal);
            if (!pay && !botch)
            {
                error = "Choose pay-to-complete or Attempt Botched.";
                return false;
            }

            if (!int.TryParse(game.PendingChoice.ContextId, out var amount))
                amount = 0;
            if (!game.TrySubmitChoice(game.PendingChoice.PlayerId, submission, out _, out error))
                return false;

            var pending = game.PendingGoalWork;
            if (pending == null)
            {
                error = "No Goal Work is pending.";
                return false;
            }
            pending.AcceptPayToComplete = pay;
            pending.PayAmount = amount;
            _choice = new GoalWorkChoice { AcceptPayToComplete = pay };
            return TryResume(game, pending.PlayerId, out result, out error, _choice);
        }

        public bool TryResumeEvade(
            GameState game,
            ChoiceSubmission submission,
            out GoalWorkResult? result,
            out string? error)
        {
            result = null;
            if (game.PendingChoice == null
                || !string.Equals(game.PendingChoice.Kind, PendingChoiceKinds.SectorDestination, StringComparison.Ordinal)
                || game.PendingChoice.ContextId == null
                || !game.PendingChoice.ContextId.StartsWith("goal-evade", StringComparison.Ordinal))
            {
                error = "No Goal Evade choice is pending.";
                return false;
            }

            var dest = submission.SelectedOptionId;
            if (string.IsNullOrWhiteSpace(dest) && submission.Values != null && submission.Values.Count > 0)
                dest = submission.Values[0];
            if (string.IsNullOrWhiteSpace(dest))
            {
                error = "Evade requires an adjacent destination sector.";
                return false;
            }
            if (!game.TrySubmitChoice(game.PendingChoice.PlayerId, submission, out _, out error))
                return false;

            var pending = game.PendingGoalWork;
            if (pending == null)
            {
                error = "No Goal Work is pending.";
                return false;
            }
            pending.EvadeToSectorId = dest;
            pending.Phase = GoalWorkPhase.Evade;
            _choice = new GoalWorkChoice { EvadeToSectorId = dest };
            return TryResume(game, pending.PlayerId, out result, out error, _choice);
        }

        private bool ContinueGoal(
            GameState game,
            PlayerState player,
            ScenarioGoal goal,
            out GoalWorkResult? result,
            out string? error)
        {
            result = null;
            var pending = game.PendingGoalWork
                ?? throw new InvalidOperationException("PendingGoalWork required.");
            var choice = _choice ?? new GoalWorkChoice();
            var rng = choice.Rng ?? new SystemRng();

            if (pending.Phase == GoalWorkPhase.Boarding && goal.Boarding != null && !pending.BoardingPassed)
            {
                if (!TryResolveBoarding(game, player, goal.Boarding, rng, out var boarded, out error))
                    return false;
                if (!boarded)
                    return EndBotched(game, player, goal.Number, 0, 0, out result, out error);
                pending.BoardingPassed = true;
                pending.Phase = GoalWorkPhase.Skill;
            }

            if (pending.Phase == GoalWorkPhase.Evade || (pending.NeedsEvade && pending.SkillResolved))
            {
                return FinishWithEvade(game, player, goal, pending, choice, out result, out error);
            }

            if (!pending.SkillResolved)
            {
                if (goal.Skill == null && goal.Bands.Count == 0)
                {
                    // Boarding-only success path without printed skill should not happen for our inventory.
                    error = "Goal has no Skill Test.";
                    return false;
                }

                if (!TryResolveSkill(game, player, goal, pending, choice, rng, out error))
                    return false;
            }

            return ApplyResolvedOutcome(game, player, goal, pending, choice, rng, out result, out error);
        }

        private static bool TryResolveBoarding(
            GameState game,
            PlayerState player,
            ScenarioGoalBoarding boarding,
            IRng rng,
            out bool passed,
            out string? error)
        {
            error = null;
            // Jail Break prints a 1-die 1–5 / 6+ boarding preamble (not full Tech/Negotiate Boarding Test).
            var sum = 0;
            for (var i = 0; i < boarding.Dice; i++)
                sum += Dice.D6(rng);

            if (ScenarioGoalWork.RangeContains(boarding.SuccessRange, sum))
            {
                passed = true;
                return true;
            }
            if (ScenarioGoalWork.RangeContains(boarding.BotchedRange, sum))
            {
                passed = false;
                return true;
            }
            // Fallback: treat as botched when outside printed success.
            passed = false;
            return true;
        }

        private bool TryResolveSkill(
            GameState game,
            PlayerState player,
            ScenarioGoal goal,
            PendingGoalWork pending,
            GoalWorkChoice choice,
            IRng rng,
            out string? error)
        {
            error = null;
            if (pending.FrozenSkillSum != null)
            {
                pending.SkillResolved = true;
                return true;
            }

            var skill = goal.Skill ?? Skill.Talk;
            var target = goal.Target > 0 ? goal.Target : 1;
            var check = new SkillCheck(skill, target);
            // Goal Talk tests: Bribes only when printed (King bands are plain Talk). Cortland may still apply.
            check = SkillCheck.WithAbilityBribes(check, player);

            if (SkillCheck.NeedsBribeChoice(player, check, choice.SkillCheck))
            {
                if (!TrySuspendBribe(game, player, goal.Number, out error))
                    return false;
                error = "Choose Bribe amount before the Goal Skill Test.";
                return false;
            }

            if (!check.TryResolve(
                    player, rng, out var skillResult, out error, choice.SkillCheck, game,
                    AbilityContext.WorkingGoal, job: null))
                return false;

            // Optional may re-rolls use AbilityContext — Job-only abilities skip via IsWorkingGoal.
            // SkillCheck.TryResolve already consults abilities with provided context when wired;
            // Goal path uses defaults. Re-roll PendingChoice from Misbehave/Nav patterns: if needed,
            // scripted tests pass AcceptReroll.

            pending.FrozenSkillSum = skillResult.Total;
            if (goal.Bands.Count > 0)
                pending.FrozenBandText = ScenarioGoalWork.BandTextForSum(goal.Bands, skillResult.Total);
            else if (skillResult.Total >= target)
                pending.FrozenBandText = goal.Success ?? "Goal Complete.";
            else
                pending.FrozenBandText = goal.Fail ?? "Attempt Botched.";

            pending.SkillResolved = true;
            return true;
        }

        private bool ApplyResolvedOutcome(
            GameState game,
            PlayerState player,
            ScenarioGoal goal,
            PendingGoalWork pending,
            GoalWorkChoice choice,
            IRng rng,
            out GoalWorkResult? result,
            out string? error)
        {
            result = null;
            var text = pending.FrozenBandText ?? "";
            var sum = pending.FrozenSkillSum ?? 0;

            // Band: "Pay $N to Complete Goal or Attempt Botched"
            if (IsPayOrBotch(text))
            {
                var amount = ParsePayAmount(text);
                var accept = choice.AcceptPayToComplete ?? pending.AcceptPayToComplete;
                if (accept == null)
                {
                    if (player.Cash < amount)
                    {
                        // Cannot pay → Attempt Botched (no Complete path).
                        return EndBotched(game, player, goal.Number, pending.Killed, pending.WarrantsIssued, out result, out error);
                    }
                    pending.PayAmount = amount;
                    if (!TrySuspendPayOrBotch(game, player, goal.Number, amount, out error))
                        return false;
                    error = $"Pay ${amount} to Complete Goal, or Attempt Botched.";
                    result = new GoalWorkResult(GoalWorkKind.Attempt, goal.Number, awaitingChoice: true);
                    return false;
                }

                if (accept == true)
                {
                    if (player.Cash < amount)
                    {
                        error = $"Need ${amount} to Complete Goal.";
                        return false;
                    }
                    player.Cash -= amount;
                    return CompleteGoal(game, player, goal, pending, choice, rng, text, out result, out error);
                }

                return EndBotched(game, player, goal.Number, pending.Killed, pending.WarrantsIssued, out result, out error);
            }

            // Band: "Pay $N. Attempt Botched" (mandatory pay then botch)
            if (Contains(text, "Pay") && Contains(text, "Attempt Botched") && !Contains(text, " to Complete"))
            {
                var amount = ParsePayAmount(text);
                if (player.Cash >= amount)
                    player.Cash -= amount;
                if (!TryApplyKillWarrantEvadeFlags(game, player, text, pending, choice, rng, out error))
                    return false;
                if (pending.NeedsEvade)
                    return FinishWithEvade(game, player, goal, pending, choice, out result, out error);
                return EndBotched(game, player, goal.Number, pending.Killed, pending.WarrantsIssued, out result, out error);
            }

            var success = IsSuccessText(text, goal, sum);
            if (!TryApplyKillWarrantEvadeFlags(game, player, text, pending, choice, rng, out error))
                return false;

            if (success)
            {
                if (Contains(text, "Move Alliance Cruiser") && Contains(text, "Valentine"))
                {
                    if (!TryMoveCruiserTo(game, "Valentine", out error))
                        return false;
                }
                if (Contains(text, "Rescue Bound Fugitive"))
                    TryRescuePrisoner(game, player);

                if (pending.NeedsEvade)
                {
                    pending.GoalCompleted = true;
                    return FinishWithEvade(game, player, goal, pending, choice, out result, out error);
                }

                return CompleteGoal(game, player, goal, pending, choice, rng, text, out result, out error);
            }

            // Fail / botch path
            if (pending.NeedsEvade)
                return FinishWithEvade(game, player, goal, pending, choice, out result, out error);
            return EndBotched(game, player, goal.Number, pending.Killed, pending.WarrantsIssued, out result, out error);
        }

        private bool CompleteGoal(
            GameState game,
            PlayerState player,
            ScenarioGoal goal,
            PendingGoalWork pending,
            GoalWorkChoice choice,
            IRng rng,
            string text,
            out GoalWorkResult? result,
            out string? error)
        {
            result = null;
            if (!TryApplyKillWarrantEvadeFlags(game, player, text, pending, choice, rng, out error))
                return false;

            if (pending.NeedsEvade && string.IsNullOrWhiteSpace(pending.EvadeToSectorId)
                && string.IsNullOrWhiteSpace(choice.EvadeToSectorId))
            {
                pending.GoalCompleted = true;
                return FinishWithEvade(game, player, goal, pending, choice, out result, out error);
            }

            if (!string.IsNullOrWhiteSpace(choice.EvadeToSectorId) || !string.IsNullOrWhiteSpace(pending.EvadeToSectorId))
            {
                var dest = choice.EvadeToSectorId ?? pending.EvadeToSectorId!;
                if (!FlightEvade.TryMove(game, player, dest, out error))
                    return false;
                pending.EvadeToSectorId = dest;
            }

            if (!WinCheck.TryCompleteGoal(game, player, goal.Number, out error))
                return false;

            var killed = pending.Killed;
            var warrants = pending.WarrantsIssued;
            var evadeTo = pending.EvadeToSectorId;
            EndAttempt(game, consumeAction: true);
            result = new GoalWorkResult(
                GoalWorkKind.Completed, goal.Number,
                killed: killed, warrants: warrants, completed: true, evadedToSectorId: evadeTo);
            error = null;
            return true;
        }

        private bool FinishWithEvade(
            GameState game,
            PlayerState player,
            ScenarioGoal goal,
            PendingGoalWork pending,
            GoalWorkChoice choice,
            out GoalWorkResult? result,
            out string? error)
        {
            result = null;
            var dest = choice.EvadeToSectorId ?? pending.EvadeToSectorId;
            if (string.IsNullOrWhiteSpace(dest))
            {
                pending.Phase = GoalWorkPhase.Evade;
                pending.NeedsEvade = true;
                if (!TrySuspendEvade(game, player, goal.Number, out error))
                    return false;
                error = "Choose an adjacent sector to Evade.";
                result = new GoalWorkResult(GoalWorkKind.Attempt, goal.Number, awaitingChoice: true);
                return false;
            }

            if (!FlightEvade.TryMove(game, player, dest, out error))
                return false;
            pending.EvadeToSectorId = dest;

            if (pending.GoalCompleted
                || (pending.FrozenBandText != null && IsSuccessText(pending.FrozenBandText, goal, pending.FrozenSkillSum ?? 0)
                    && !Contains(pending.FrozenBandText ?? "", "Attempt Botched")))
            {
                if (!player.CompletedGoals.Contains(goal.Number))
                {
                    if (!WinCheck.TryCompleteGoal(game, player, goal.Number, out error))
                        return false;
                }
                var killed = pending.Killed;
                var warrants = pending.WarrantsIssued;
                EndAttempt(game, consumeAction: true);
                result = new GoalWorkResult(
                    GoalWorkKind.Completed, goal.Number,
                    killed: killed, warrants: warrants, completed: true, evadedToSectorId: dest);
                error = null;
                return true;
            }

            return EndBotched(game, player, goal.Number, pending.Killed, pending.WarrantsIssued, dest, out result, out error);
        }

        private bool TryApplyKillWarrantEvadeFlags(
            GameState game,
            PlayerState player,
            string text,
            PendingGoalWork pending,
            GoalWorkChoice choice,
            IRng rng,
            out string? error)
        {
            error = null;
            if (pending.Killed == 0 && Contains(text, "Kill"))
            {
                var n = ParseKillCount(text);
                if (n > 0)
                {
                    if (CrewKill.NeedsVictimChoice(player, n, choice.Kill))
                    {
                        // Suspend via CrewKill path — thin: require VictimCrewIds in tests for now,
                        // or set PendingChoice KillVictim like Misbehave.
                        if (!TrySuspendKill(game, player, n, out error))
                            return false;
                        error = "Choose crew to Kill.";
                        return false;
                    }
                    if (!CrewKill.TryKillUpTo(game, player, n, rng, out var killed, out error, choice.Kill))
                        return false;
                    pending.Killed += killed;
                }
            }

            if (Contains(text, "Warrant Issued") || Contains(text, "Warrant issued"))
            {
                if (pending.WarrantsIssued == 0)
                {
                    // Patience's War: Goal warrants do not drop Solid with Patience / Mr. Universe.
                    // GF9 p.16 Zero Tolerance: any Warrant still drops Harken Solid (shared hook).
                    if (!WarrantIssuer.TryIssue(
                            game, player, out error,
                            choice.SolidRep, choice.SolidRep?.DiscardRobertaInstead))
                        return false;
                    pending.WarrantsIssued = 1;
                }
            }

            if (Contains(text, "Evade"))
                pending.NeedsEvade = true;

            return true;
        }

        private static bool EndBotched(
            GameState game,
            PlayerState player,
            int goalNumber,
            int killed,
            int warrants,
            out GoalWorkResult? result,
            out string? error) =>
            EndBotched(game, player, goalNumber, killed, warrants, null, out result, out error);

        private static bool EndBotched(
            GameState game,
            PlayerState player,
            int goalNumber,
            int killed,
            int warrants,
            string? evadeTo,
            out GoalWorkResult? result,
            out string? error)
        {
            EndAttempt(game, consumeAction: true);
            result = new GoalWorkResult(
                GoalWorkKind.Botched, goalNumber, killed: killed, warrants: warrants, evadedToSectorId: evadeTo);
            error = null;
            return true;
        }

        private static void EndAttempt(GameState game, bool consumeAction)
        {
            game.PendingMisbehave = null;
            game.PendingGoalWork = null;
            game.WorkGearLocked = false;
            if (consumeAction)
                game.TryConsumeAction(TurnAction.Work, out _);
        }

        private static bool IsSuccessText(string text, ScenarioGoal goal, int sum)
        {
            if (Contains(text, "Attempt Botched"))
                return false;
            if (Contains(text, "Goal Complete") || Contains(text, "You Win")
                || Contains(text, "Intel Gathered") || Contains(text, "crown is yours")
                || Contains(text, "Rescue Bound"))
                return true;
            if (goal.Target > 0 && sum >= goal.Target && goal.Bands.Count == 0)
                return true;
            if (!string.IsNullOrWhiteSpace(goal.Success)
                && text.IndexOf(goal.Success, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static bool IsPayOrBotch(string text) =>
            Contains(text, "to Complete Goal or Attempt Botched")
            || (Contains(text, "to Complete Goal") && Contains(text, "or Attempt Botched"));

        private static int ParsePayAmount(string text)
        {
            var m = PayAmountPattern.Match(text);
            if (!m.Success)
                return 0;
            var raw = m.Groups[1].Value.Replace(",", "");
            return int.TryParse(raw, out var n) ? n : 0;
        }

        private static int ParseKillCount(string text)
        {
            var m = Regex.Match(text, @"Kill\s+(\d+)\s+Crew", RegexOptions.IgnoreCase);
            if (m.Success)
                return int.Parse(m.Groups[1].Value);
            if (Contains(text, "Kill a Crew") || Contains(text, "Kill 1 Crew"))
                return 1;
            return 0;
        }

        private static bool TryMoveCruiserTo(GameState game, string planetName, out string? error)
        {
            error = null;
            string? sectorId = null;
            foreach (var sector in game.Map.Sectors.Values)
            {
                if (!string.IsNullOrWhiteSpace(sector.Planet)
                    && sector.Planet.Equals(planetName, StringComparison.OrdinalIgnoreCase))
                {
                    sectorId = sector.Id;
                    break;
                }
            }
            if (sectorId == null)
            {
                error = $"No planet sector named {planetName}.";
                return false;
            }
            game.Tokens = game.Tokens.WithAllianceCruiser(sectorId);
            return true;
        }

        private static void TryRescuePrisoner(GameState game, PlayerState player)
        {
            var bountyId = game.JailBreakPrisonerBountyId;
            if (string.IsNullOrWhiteSpace(bountyId) || game.Bounties == null)
                return;
            if (!game.Bounties.TryResolve(bountyId, out var bounty))
                return;
            BoundBounty? existing = null;
            foreach (var bound in player.BoundBounties)
            {
                if (bound.BountyId.Equals(bounty.Id, StringComparison.OrdinalIgnoreCase))
                {
                    existing = bound;
                    break;
                }
            }
            if (existing == null)
            {
                existing = new BoundBounty(bounty.Id, bounty.Name);
                player.BoundBounties.Add(existing);
            }
            // Bound Fugitive token: use matching crew card id when present, else bounty id.
            var crewId = "crew_" + bounty.Name.ToLowerInvariant().Replace(' ', '-');
            if (game.Crew != null && game.Crew.TryGet(crewId, out _))
            {
                if (!existing.CrewIds.Contains(crewId))
                    existing.CrewIds.Add(crewId);
            }
            else if (existing.CrewIds.Count == 0)
                existing.CrewIds.Add(bounty.Id);
        }

        private static bool PriorGoalsComplete(
            PlayerState player,
            ScenarioCard scenario,
            int goalNumber,
            out string? error)
        {
            error = null;
            foreach (var g in scenario.Goals)
            {
                if (g.Number >= goalNumber)
                    continue;
                if (!player.CompletedGoals.Contains(g.Number))
                {
                    error = $"Must complete Goal {g.Number} before Goal {goalNumber}.";
                    return false;
                }
            }
            return true;
        }

        private static bool MeetsSolidGates(
            GameState game,
            PlayerState player,
            ScenarioGoal goal,
            out string? error)
        {
            error = null;
            foreach (var name in goal.RequiresSolidWith)
            {
                if (!IsSolidWithNameOrId(game, player, name))
                {
                    error = $"Must be Solid with {name} to Work this Goal.";
                    return false;
                }
            }
            if (game.Scenario != null && game.Scenario.GoalWorkRequiresSolidPatienceAndMrUniverse)
            {
                if (!IsSolidWithNameOrId(game, player, "Patience")
                    || !IsSolidWithNameOrId(game, player, "Mr. Universe"))
                {
                    error = "Must be Solid with Patience and Mr. Universe to Work Goals.";
                    return false;
                }
            }
            return true;
        }

        private static bool IsSolidWithNameOrId(GameState game, PlayerState player, string nameOrId)
        {
            if (ActiveAlertRules.CountsAsSolidWith(game, player, nameOrId))
                return true;
            if (game.Contacts != null && game.Contacts.TryFindByName(nameOrId, out var contact))
                return ActiveAlertRules.CountsAsSolidWith(game, player, contact.Id)
                    || ActiveAlertRules.CountsAsSolidWith(game, player, contact.Name);
            return false;
        }

        private static bool AtGoalSite(GameState game, PlayerState player, ScenarioGoal goal, out string? error)
        {
            error = null;
            var location = goal.Location;
            if (string.IsNullOrWhiteSpace(location))
            {
                error = "Goal has no location.";
                return false;
            }
            if (location.Equals("Alliance Cruiser", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(game.Tokens.AllianceCruiserSectorId)
                    || game.Tokens.AllianceCruiserSectorId != player.SectorId)
                {
                    error = "Must be in the Alliance Cruiser's Sector.";
                    return false;
                }
                return true;
            }
            if (!game.Map.SatisfiesDestination(player.SectorId, location))
            {
                error = $"Must be at {location} to Work this Goal.";
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

        private static bool TrySuspendBribe(GameState game, PlayerState player, int goalNumber, out string? error)
        {
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.BribeAmount,
                contextId: $"goal:{goalNumber}",
                prompt: "Choose Bribe amount for Goal Skill Test.");
            return game.TrySetPendingChoice(pending, out error);
        }

        private static bool TrySuspendPayOrBotch(
            GameState game,
            PlayerState player,
            int goalNumber,
            int amount,
            out string? error)
        {
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.GoalPayOrBotch,
                contextId: amount.ToString(),
                options: new[] { GoalPayOrBotchOptions.PayComplete, GoalPayOrBotchOptions.Botch },
                prompt: $"Pay ${amount} to Complete Goal, or Attempt Botched.");
            return game.TrySetPendingChoice(pending, out error);
        }

        private static bool TrySuspendEvade(GameState game, PlayerState player, int goalNumber, out string? error)
        {
            var options = new List<string>();
            foreach (var neighbor in game.Map.Neighbors(player.SectorId))
            {
                if (FlightEvade.CanMove(game, player, neighbor, out _))
                    options.Add(neighbor);
            }
            if (options.Count == 0)
            {
                error = "No legal Evade destinations.";
                return false;
            }
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.SectorDestination,
                contextId: $"goal-evade:{goalNumber}",
                options: options,
                prompt: "Choose an adjacent sector to Evade.");
            return game.TrySetPendingChoice(pending, out error);
        }

        private static bool TrySuspendKill(GameState game, PlayerState player, int count, out string? error)
        {
            var options = new List<string>();
            foreach (var member in player.Roster.Members)
            {
                if (!member.IsLeader)
                    options.Add(member.Id);
            }
            // Leaders can be "killed" → Disgruntle; include all roster ids.
            if (options.Count == 0)
            {
                foreach (var member in player.Roster.Members)
                    options.Add(member.Id);
            }
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.KillVictim,
                contextId: $"goal-kill:{count}",
                options: options,
                prompt: $"Choose {count} crew to Kill.");
            return game.TrySetPendingChoice(pending, out error);
        }

        private static bool Contains(string? text, string fragment) =>
            !string.IsNullOrEmpty(text)
            && text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
