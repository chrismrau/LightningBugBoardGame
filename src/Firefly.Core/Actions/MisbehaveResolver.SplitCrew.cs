using System;
using System.Collections.Generic;
using Firefly.Core.Abilities;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Director's Cut C&amp;P p.49 Splitting Up / I'll Be in my Bunk / No one left —
    /// Fork in the Road and They're right on our Tails.
    /// </summary>
    public sealed partial class MisbehaveResolver
    {
        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.MisbehaveSplitCrew"/> team assignment.
        /// </summary>
        public bool TryResumeSplitCrew(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.MisbehaveSplitCrew,
                    StringComparison.Ordinal))
            {
                error = "No Split Crew team assignment is pending.";
                return false;
            }

            choice.SplitTeam0CrewIds = submission.Values;
            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            _resumingSplitCrew = true;
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingSplitCrew = false;
            }
        }

        private bool TryRunSplitCrew(
            GameState game,
            string playerId,
            MisbehaveCard card,
            MisbehaveOption option,
            int optionIndex,
            IReadOnlyList<MisbehaveEffect> structuredEffects,
            MisbehaveChoice choice,
            IRng rng,
            out MisbehaveResolution? resolution,
            out string? error)
        {
            resolution = null;
            error = null;
            var pending = game.PendingMisbehave
                ?? throw new InvalidOperationException("No Misbehave is pending.");
            var player = game.GetPlayer(playerId);

            var mode = HasLocalEffect(structuredEffects, MisbehaveLocalEffectType.SplitCrewFork)
                ? SplitCrewMode.Fork
                : SplitCrewMode.Tails;

            _splitCrewRng = rng;

            // Director's Cut C&P p.49 Splitting Up: one Crew left Working → Botched.
            if (JobWorkCrew.AvailableCount(player) < 2)
            {
                pending.ClearStepProgress();
                pending.SplitCrew = null;
                return Finish(
                    game, playerId, card, option, MisbehaveOutcome.Botched,
                    null, 0, 0, 0, 0, false, out resolution, out error);
            }

            if (pending.SplitCrew == null || pending.SplitCrew.ParentCard.Id != card.Id)
            {
                pending.SplitCrew = new SplitCrewSession(mode, card, optionIndex);
            }

            var split = pending.SplitCrew;
            if (!split.TeamsAssigned)
            {
                if (!TryAssignSplitTeams(game, player, split, choice, out error))
                    return false;
            }

            if (split.Mode == SplitCrewMode.Tails)
                return TryResolveTailsFight(game, playerId, card, option, choice, rng, out resolution, out error);

            return TryAdvanceFork(game, playerId, choice, rng, out resolution, out error);
        }

        private static bool TryAssignSplitTeams(
            GameState game,
            PlayerState player,
            SplitCrewSession split,
            MisbehaveChoice choice,
            out string? error)
        {
            error = null;
            if (choice.SplitTeam0CrewIds == null)
            {
                var prompt = split.Mode == SplitCrewMode.Tails
                    ? "Split Crew: choose the team without your Leader (Values = non-Leader team)."
                    : "Split Crew: choose Team A (Values); remaining available Crew form Team B.";
                var pending = new PendingChoice(
                    player.Id,
                    PendingChoiceKinds.MisbehaveSplitCrew,
                    contextId: split.ParentCard.Id,
                    options: null,
                    prompt: prompt);
                if (!game.TrySetPendingChoice(pending, out error))
                    return false;
                error = prompt;
                return false;
            }

            if (!TryBuildTeams(player, split, choice.SplitTeam0CrewIds, out var team0, out var team1, out error))
                return false;

            split.AssignTeams(team0, team1);
            return true;
        }

        private static bool TryBuildTeams(
            PlayerState player,
            SplitCrewSession split,
            IList<string> team0Ids,
            out List<string> team0,
            out List<string> team1,
            out string? error)
        {
            team0 = new List<string>();
            team1 = new List<string>();
            error = null;

            var available = new List<CrewMember>();
            foreach (var member in player.Roster.Members)
            {
                if (!JobWorkCrew.IsUnavailable(player, member))
                    available.Add(member);
            }

            if (available.Count < 2)
            {
                error = "Need at least two Crew Working the Job to Split Crew.";
                return false;
            }

            var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in team0Ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    error = "Split Crew team id is required.";
                    return false;
                }
                if (!chosen.Add(id))
                {
                    error = "Duplicate crew id in Split Crew team.";
                    return false;
                }
                var member = player.Roster.Find(id);
                if (member == null || JobWorkCrew.IsUnavailable(player, member))
                {
                    error = $"Crew '{id}' is not available for Split Crew.";
                    return false;
                }
                team0.Add(id);
            }

            if (team0.Count == 0)
            {
                error = "Each Split Crew team must have at least one Crew.";
                return false;
            }

            foreach (var member in available)
            {
                if (chosen.Contains(member.Id))
                    continue;
                team1.Add(member.Id);
            }

            if (team1.Count == 0)
            {
                error = "Each Split Crew team must have at least one Crew.";
                return false;
            }

            if (split.Mode == SplitCrewMode.Tails)
            {
                // Printed: team without Leader first; then Proceed with Leader's team.
                foreach (var id in team0)
                {
                    var m = player.Roster.Find(id);
                    if (m != null && m.IsLeader)
                    {
                        error = "Tails non-Leader team cannot include the Leader.";
                        return false;
                    }
                }

                var leaderOnTeam1 = false;
                foreach (var id in team1)
                {
                    var m = player.Roster.Find(id);
                    if (m != null && m.IsLeader)
                    {
                        leaderOnTeam1 = true;
                        break;
                    }
                }

                if (!leaderOnTeam1)
                {
                    error = "Tails Leader team must include the Leader.";
                    return false;
                }
            }

            return true;
        }

        private bool TryAdvanceFork(
            GameState game,
            string playerId,
            MisbehaveChoice choice,
            IRng rng,
            out MisbehaveResolution? resolution,
            out string? error)
        {
            resolution = null;
            error = null;
            var pending = game.PendingMisbehave!;
            var split = pending.SplitCrew!;

            if (split.Outcomes[0] == SplitTeamOutcome.None)
            {
                split.CurrentTeamIndex = 0;
                return BeginNestedTeamResolve(game, playerId, choice, rng, out resolution, out error);
            }

            if (split.Outcomes[1] == SplitTeamOutcome.None)
            {
                split.CurrentTeamIndex = 1;
                return BeginNestedTeamResolve(game, playerId, choice, rng, out resolution, out error);
            }

            return AggregateFork(game, playerId, choice, rng, out resolution, out error);
        }

        private bool BeginNestedTeamResolve(
            GameState game,
            string playerId,
            MisbehaveChoice choice,
            IRng rng,
            out MisbehaveResolution? resolution,
            out string? error)
        {
            resolution = null;
            error = null;
            var pending = game.PendingMisbehave!;
            var split = pending.SplitCrew!;
            if (game.Misbehave == null)
            {
                error = "Misbehave deck is not loaded.";
                return false;
            }

            split.SavedParentFaceUp = pending.FaceUp;
            pending.FaceUp = game.Misbehave.Draw();
            split.ResolvingNested = true;
            split.ClearNestedStepProgress();
            pending.ClearStepProgress();
            _activeTeamCrewIds = split.CurrentTeam;

            // Fresh choice for the nested card (option / skill PendingChoices as needed).
            var nestedChoice = new MisbehaveChoice();
            return TryResolve(game, playerId, nestedChoice, out resolution, out error, rng);
        }

        private bool CompleteNestedTeamCard(
            GameState game,
            string playerId,
            MisbehaveCard nestedCard,
            MisbehaveOption? option,
            MisbehaveOutcome outcome,
            SkillCheckResult? check,
            int warrants,
            int killed,
            int loaded,
            int cashDelta,
            bool usedAce,
            out MisbehaveResolution? resolution,
            out string? error)
        {
            resolution = null;
            error = null;
            var pending = game.PendingMisbehave!;
            var split = pending.SplitCrew!;
            var player = game.GetPlayer(playerId);

            game.Misbehave?.ResolveIntoDiscard(nestedCard);

            // Fork: Warrant gate is independent of Botch→Return.
            SplitTeamOutcome teamOutcome;
            if (warrants > 0)
                teamOutcome = SplitTeamOutcome.Warrant;
            else if (outcome == MisbehaveOutcome.Botched)
                teamOutcome = SplitTeamOutcome.Botched;
            else
                teamOutcome = SplitTeamOutcome.Proceeded;

            split.Outcomes[split.CurrentTeamIndex] = teamOutcome;

            // Printed: "Any team with Botches their card must return to the Ship."
            if (outcome == MisbehaveOutcome.Botched)
                ReturnTeamToShip(player, pending.JobId, split.CurrentTeam);

            // Restore parent Fork card as FaceUp.
            pending.FaceUp = split.SavedParentFaceUp;
            split.SavedParentFaceUp = null;
            split.ResolvingNested = false;
            pending.ClearStepProgress();
            pending.SelectedOptionIndex = split.ParentOptionIndex;
            _activeTeamCrewIds = null;

            // Continue with next team or aggregate.
            var advanceRng = _splitCrewRng ?? new SystemRng();
            return TryAdvanceFork(
                game, playerId,
                new MisbehaveChoice { OptionIndex = split.ParentOptionIndex },
                advanceRng,
                out resolution,
                out error);
        }

        private bool AggregateFork(
            GameState game,
            string playerId,
            MisbehaveChoice choice,
            IRng rng,
            out MisbehaveResolution? resolution,
            out string? error)
        {
            resolution = null;
            error = null;
            var pending = game.PendingMisbehave!;
            var split = pending.SplitCrew!;
            var player = game.GetPlayer(playerId);
            var parent = split.ParentCard;
            var option = parent.Options[split.ParentOptionIndex];

            pending.SplitCrew = null;
            _activeTeamCrewIds = null;

            // Printed: "If neither team has a Warrant Issued, Proceed Past this card."
            // FAQ/GF9: Warrant while Working discards the Job — apply when any team Issued.
            if (split.AnyWarrant)
            {
                var killed = 0;
                if (!TryAbandonJobForWarrant(
                        game, player, rng, choice.Kill, ref killed, out var abandonedWork, out error))
                    return false;
                game.Misbehave?.ResolveIntoDiscard(parent);
                if (game.PendingMisbehave != null)
                {
                    game.PendingMisbehave.FaceUp = null;
                    game.PendingMisbehave.ClearStepProgress();
                }
                resolution = new MisbehaveResolution(
                    parent, option, MisbehaveOutcome.Botched, null, 1, killed, 0, 0, false, abandonedWork);
                error = null;
                return true;
            }

            // Director's Cut C&P p.49 No one left (also printed on Fork).
            if (JobWorkCrew.AvailableCount(player) == 0)
            {
                pending.ClearStepProgress();
                return Finish(
                    game, playerId, parent, option, MisbehaveOutcome.Botched,
                    null, 0, 0, 0, 0, false, out resolution, out error);
            }

            pending.ClearStepProgress();
            return Finish(
                game, playerId, parent, option, MisbehaveOutcome.Proceed,
                null, 0, 0, 0, 0, false, out resolution, out error);
        }

        private bool TryResolveTailsFight(
            GameState game,
            string playerId,
            MisbehaveCard card,
            MisbehaveOption option,
            MisbehaveChoice choice,
            IRng rng,
            out MisbehaveResolution? resolution,
            out string? error)
        {
            resolution = null;
            error = null;
            var pending = game.PendingMisbehave!;
            var split = pending.SplitCrew!;
            var player = game.GetPlayer(playerId);
            var job = game.Jobs != null && game.Jobs.TryGet(pending.JobId, out var jobCard)
                ? jobCard
                : null;

            // Printed: First, team without Leader: Fight 12; 1-11 Kill all in Team; 12+ Return Team.
            _activeTeamCrewIds = split.Team0;
            var skillCheck = new SkillCheck(Skill.Fight, 12, kosherized: false, bribesAllowed: false);
            skillCheck = SkillCheck.WithAbilityBribes(skillCheck, player);

            if (SkillCheck.NeedsSkillSwitchChoice(
                    player, skillCheck, choice.SkillCheck, player.FindActive(pending.JobId),
                    AbilityContext.Misbehaving))
            {
                if (!SkillCheck.TrySuspendSkillSwitch(
                        game, player, contextId: $"{card.Id}:tails", out error))
                    return false;
                error = "Choose whether to switch this skill test.";
                return false;
            }

            skillCheck = SkillCheck.ApplySkillSwitchIfChosen(
                player, skillCheck, choice.SkillCheck, player.FindActive(pending.JobId),
                AbilityContext.Misbehaving);

            if (!skillCheck.TryResolve(
                    player, rng, out var check, out error, choice.SkillCheck,
                    game, AbilityContext.Misbehaving, job, onlyCrewIds: split.Team0))
                return false;

            var sum = check.Total;
            var killed = 0;
            if (sum <= 11)
            {
                // 1-11 Kill all Crew in Team.
                killed = KillCrewIds(game, player, rng, choice.Kill, split.Team0);
            }
            else
            {
                // 12+ Return Team to Ship (I'll Be in my Bunk).
                ReturnTeamToShip(player, pending.JobId, split.Team0);
            }

            _activeTeamCrewIds = null;
            pending.SplitCrew = null;

            // Director's Cut C&P p.49 No one left.
            if (JobWorkCrew.AvailableCount(player) == 0)
            {
                pending.ClearStepProgress();
                return Finish(
                    game, playerId, card, option, MisbehaveOutcome.Botched,
                    check, 0, killed, 0, 0, false, out resolution, out error);
            }

            // Printed: Next, Proceed with the Team which includes your Leader.
            pending.ClearStepProgress();
            return Finish(
                game, playerId, card, option, MisbehaveOutcome.Proceed,
                check, 0, killed, 0, 0, false, out resolution, out error);
        }

        private static void ReturnTeamToShip(PlayerState player, string jobId, IReadOnlyList<string> team)
        {
            foreach (var id in team)
            {
                if (player.Roster.Find(id) == null)
                    continue;
                JobWorkCrew.ReturnToShip(player, jobId, id);
            }
        }

        private static int KillCrewIds(
            GameState game,
            PlayerState player,
            IRng rng,
            KillChoice? killChoice,
            IReadOnlyList<string> crewIds)
        {
            var killed = 0;
            foreach (var id in crewIds)
            {
                var member = player.Roster.Find(id);
                if (member == null)
                    continue;
                var result = CrewKill.Apply(game, player, member, rng, killChoice);
                if (result.Outcome == CrewOutcome.Killed)
                    killed++;
            }
            return killed;
        }
    }
}
