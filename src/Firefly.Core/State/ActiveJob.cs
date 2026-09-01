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

        public ActiveJob(string jobId)
        {
            JobId = jobId;
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

        public PendingMisbehave(string playerId, string jobId, WorkSite site, int remaining)
        {
            PlayerId = playerId;
            JobId = jobId;
            Site = site;
            Remaining = remaining;
        }
    }
}
