using System;

namespace Firefly.Core.State
{
    public enum GoalWorkPhase
    {
        /// <summary>Misbehave cards still outstanding (see <see cref="GameState.PendingMisbehave"/>).</summary>
        Misbehave,
        /// <summary>Jail Break boarding die / preamble.</summary>
        Boarding,
        /// <summary>Main Goal Skill Test (and optional band pay-or-botch).</summary>
        Skill,
        /// <summary>Waiting on Evade destination after skill effects.</summary>
        Evade
    }

    /// <summary>
    /// In-flight Story Goal Work Action after (or without) Misbehave.
    /// GF9 / Director's Cut Working Goals.
    /// </summary>
    public sealed class PendingGoalWork
    {
        public string PlayerId { get; }
        public int GoalNumber { get; }
        public GoalWorkPhase Phase { get; set; }
        /// <summary>True after boarding succeeds; Fight skill may proceed.</summary>
        public bool BoardingPassed { get; set; }
        /// <summary>Skill sum when suspended for pay-or-botch / kill / evade.</summary>
        public int? FrozenSkillSum { get; set; }
        public string? FrozenBandText { get; set; }
        public bool SkillResolved { get; set; }
        public bool GoalCompleted { get; set; }
        public int Killed { get; set; }
        public int WarrantsIssued { get; set; }
        public bool NeedsEvade { get; set; }
        public string? EvadeToSectorId { get; set; }
        /// <summary>Null = undecided pay-or-botch; true = pay to complete; false = botch.</summary>
        public bool? AcceptPayToComplete { get; set; }
        public int PayAmount { get; set; }

        public PendingGoalWork(string playerId, int goalNumber, GoalWorkPhase phase)
        {
            PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
            GoalNumber = goalNumber;
            Phase = phase;
        }
    }
}
