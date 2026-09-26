using System;
using System.Collections.Generic;

namespace Firefly.Core.State
{
    public sealed class ActiveJob
    {
        public string JobId { get; }
        public bool PickedUp { get; set; }
        public int Cargo { get; set; }
        public int Contraband { get; set; }
        public int Fugitives { get; set; }
        public int Passengers { get; set; }
        public int Parts { get; set; }
        /// <summary>Sheydra / Stitch: once-per-job skill switch already used on this Active Job.</summary>
        public bool SkillSwitchUsedThisJob { get; set; }
        /// <summary>
        /// Director's Cut C&amp;P p.49 I'll Be in my Bunk: Misbehave returned these crew
        /// (and their Gear) — unusable for the remainder of the Job.
        /// </summary>
        public ISet<string> ReturnedToShipCrewIds { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ActiveJob(string jobId)
        {
            JobId = jobId;
        }

        public bool IsReturnedToShip(string crewId) =>
            !string.IsNullOrWhiteSpace(crewId) && ReturnedToShipCrewIds.Contains(crewId);

        public void ReturnToShip(string crewId)
        {
            if (!string.IsNullOrWhiteSpace(crewId))
                ReturnedToShipCrewIds.Add(crewId);
        }
    }

    public enum WorkSite
    {
        Pickup,
        Dropoff
    }

    public sealed class PendingMisbehave
    {
        public string PlayerId { get; }
        public string JobId { get; }
        public WorkSite Site { get; }
        public int Remaining { get; set; }
        public Cards.MisbehaveCard? FaceUp { get; set; }

        /// <summary>Option chosen for the face-up card (set after option PendingChoice).</summary>
        public int? SelectedOptionIndex { get; set; }
        /// <summary>Current FIRST–NEXT step index while the face-up card is in progress.</summary>
        public int CurrentStepIndex { get; set; }
        /// <summary>
        /// True after a FIRST step Continues — waiting on
        /// <see cref="PendingChoiceKinds.MisbehaveOption"/> for the NEXT step.
        /// </summary>
        public bool AwaitingNextStep { get; set; }
        /// <summary>Printed "next Fight Test is Kosherized" carry from a prior step.</summary>
        public bool NextFightKosherized { get; set; }
        /// <summary>Printed "+N Negotiate to next Test" carry from a prior step.</summary>
        public int NextTalkBonus { get; set; }
        /// <summary>
        /// Dalin Intel Broker: once per Work Action may pay to discard/redraw Misbehave.
        /// </summary>
        public bool DalinUsedThisWork { get; set; }
        /// <summary>Null = undecided Dalin may; true = pay+redraw; false = decline.</summary>
        public bool? AcceptDalinRedraw { get; set; }

        public PendingMisbehave(string playerId, string jobId, WorkSite site, int remaining)
        {
            PlayerId = playerId;
            JobId = jobId;
            Site = site;
            Remaining = remaining;
        }

        public void ClearStepProgress()
        {
            SelectedOptionIndex = null;
            CurrentStepIndex = 0;
            AwaitingNextStep = false;
            NextFightKosherized = false;
            NextTalkBonus = 0;
        }
    }
}
