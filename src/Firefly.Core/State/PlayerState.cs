namespace Firefly.Core.State
{
    public sealed class PlayerState
    {
        public string Id { get; }
        public string Name { get; }
        public string SectorId { get; set; }
        public int Fuel { get; set; }
        public int Parts { get; set; }
        public int Cash { get; set; }
        public int DriveRange { get; set; }
        public bool FullBurnRequiresFuel { get; set; }
        public string? ShipId { get; set; }

        public PlayerState(
            string id,
            string name,
            string sectorId,
            int fuel = 3,
            int parts = 2,
            int cash = 0,
            int driveRange = 5,
            bool fullBurnRequiresFuel = true,
            string? shipId = null)
        {
            Id = id;
            Name = name;
            SectorId = sectorId;
            Fuel = fuel;
            Parts = parts;
            Cash = cash;
            DriveRange = driveRange;
            FullBurnRequiresFuel = fullBurnRequiresFuel;
            ShipId = shipId;
        }
    }
}
