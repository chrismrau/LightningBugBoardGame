namespace Firefly.Core.Cards
{
    public sealed class JobCard
    {
        public string Id { get; }
        public string Name { get; }
        public string ContactName { get; }
        public string? JobType { get; }
        public bool Legal { get; }
        public bool Immoral { get; }
        public string? PickupLocation { get; }
        public string? PickupDetails { get; }
        public string? DropoffLocation { get; }
        public string? DropoffDetails { get; }
        public int? PayBase { get; }
        public string? PayRaw { get; }
        public string? Bonus { get; }
        public string? Special { get; }
        public string? Description { get; }

        public JobCard(
            string id,
            string name,
            string contactName,
            string? jobType,
            bool legal,
            bool immoral,
            string? pickupLocation,
            string? pickupDetails,
            string? dropoffLocation,
            string? dropoffDetails,
            int? payBase,
            string? payRaw,
            string? bonus,
            string? special,
            string? description)
        {
            Id = id;
            Name = name;
            ContactName = contactName;
            JobType = jobType;
            Legal = legal;
            Immoral = immoral;
            PickupLocation = pickupLocation;
            PickupDetails = pickupDetails;
            DropoffLocation = dropoffLocation;
            DropoffDetails = dropoffDetails;
            PayBase = payBase;
            PayRaw = payRaw;
            Bonus = bonus;
            Special = special;
            Description = description;
        }
    }
}
