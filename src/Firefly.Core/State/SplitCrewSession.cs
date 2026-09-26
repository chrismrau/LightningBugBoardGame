using System;
using System.Collections.Generic;
using Firefly.Core.Cards;

namespace Firefly.Core.State
{
    /// <summary>
    /// Director's Cut C&amp;P p.49 Splitting Up: Misbehave cards that divide the roster into
    /// two teams (≥1 Crew each; one Crew left Working → Botched).
    /// </summary>
    public enum SplitCrewMode
    {
        /// <summary>Fork in the Road: draw/resolve one Misbehave per team, then aggregate.</summary>
        Fork,
        /// <summary>They're right on our Tails: non-Leader team Fight 12, then Proceed with Leader team.</summary>
        Tails
    }

    /// <summary>Per-team result of a nested Misbehave (Fork) or Fight band (Tails non-Leader).</summary>
    public enum SplitTeamOutcome
    {
        None,
        Proceeded,
        Botched,
        /// <summary>Nested card Issued a Warrant (Fork aggregation gate).</summary>
        Warrant
    }

    /// <summary>
    /// Live Split Crew state on <see cref="PendingMisbehave"/> while Fork / Tails resolves.
    /// Parent Fork/Tails card stays <see cref="PendingMisbehave.FaceUp"/> until aggregation
    /// finishes; nested team cards use <see cref="NestedFaceUp"/> (or a temporary FaceUp swap
    /// while <see cref="Actions.MisbehaveResolver"/> is resolving a team card).
    /// </summary>
    public sealed class SplitCrewSession
    {
        public SplitCrewMode Mode { get; }
        public MisbehaveCard ParentCard { get; }
        public int ParentOptionIndex { get; }

        /// <summary>
        /// Fork: Team A (player-chosen). Tails: non-Leader team.
        /// </summary>
        public IReadOnlyList<string> Team0 { get; private set; }

        /// <summary>
        /// Fork: Team B (remainder). Tails: Leader's team (must include Leader).
        /// </summary>
        public IReadOnlyList<string> Team1 { get; private set; }

        public bool TeamsAssigned { get; private set; }
        public int CurrentTeamIndex { get; set; }
        public MisbehaveCard? NestedFaceUp { get; set; }
        public SplitTeamOutcome[] Outcomes { get; }

        /// <summary>True while FaceUp has been swapped to a nested team Misbehave.</summary>
        public bool ResolvingNested { get; set; }

        /// <summary>Parent FaceUp saved while ResolvingNested (restore after each team).</summary>
        public MisbehaveCard? SavedParentFaceUp { get; set; }

        public int? NestedSelectedOptionIndex { get; set; }
        public int NestedCurrentStepIndex { get; set; }
        public bool NestedAwaitingNextStep { get; set; }
        public bool NestedNextFightKosherized { get; set; }
        public int NestedNextTalkBonus { get; set; }

        public SplitCrewSession(SplitCrewMode mode, MisbehaveCard parentCard, int parentOptionIndex)
        {
            Mode = mode;
            ParentCard = parentCard ?? throw new ArgumentNullException(nameof(parentCard));
            ParentOptionIndex = parentOptionIndex;
            Team0 = Array.Empty<string>();
            Team1 = Array.Empty<string>();
            Outcomes = new[] { SplitTeamOutcome.None, SplitTeamOutcome.None };
        }

        public IReadOnlyList<string> CurrentTeam =>
            CurrentTeamIndex == 0 ? Team0 : Team1;

        public void AssignTeams(IReadOnlyList<string> team0, IReadOnlyList<string> team1)
        {
            Team0 = team0 ?? throw new ArgumentNullException(nameof(team0));
            Team1 = team1 ?? throw new ArgumentNullException(nameof(team1));
            TeamsAssigned = true;
        }

        public void ClearNestedStepProgress()
        {
            NestedSelectedOptionIndex = null;
            NestedCurrentStepIndex = 0;
            NestedAwaitingNextStep = false;
            NestedNextFightKosherized = false;
            NestedNextTalkBonus = 0;
        }

        public bool AnyWarrant =>
            Outcomes[0] == SplitTeamOutcome.Warrant || Outcomes[1] == SplitTeamOutcome.Warrant;
    }
}
