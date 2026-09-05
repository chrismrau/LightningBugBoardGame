using System.Collections.Generic;
using Firefly.Core.Cards;

namespace Firefly.Core.State
{
    public sealed class BoundBounty
    {
        public string BountyId { get; }
        public string BountyName { get; }
        public IList<string> CrewIds { get; }

        public BoundBounty(string bountyId, string bountyName)
        {
            BountyId = bountyId;
            BountyName = bountyName;
            CrewIds = new List<string>();
        }

        public int Count => CrewIds.Count;
    }
}
