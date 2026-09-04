using System;
using System.Collections.Generic;
using Firefly.Core.Cards;

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
        public string? LeaderId { get; set; }
        public CrewRoster Roster { get; }
        public int FightBonus { get; set; }
        public int TechBonus { get; set; }
        public int TalkBonus { get; set; }
        public int Warrants { get; set; }
        public int Contraband { get; set; }
        public int Cargo { get; set; }
        public int Fugitives { get; set; }
        public int Passengers { get; set; }
        public int JobHandLimit { get; set; }
        public int ActiveJobLimit { get; set; }
        public IList<string> JobHand { get; }
        public IList<ActiveJob> ActiveJobs { get; }
        public ISet<string> SolidWith { get; }
        public DealModifiers Deal { get; }
        public IList<string> Gear { get; }
        public IList<string> ShipUpgrades { get; }
        public string? DriveCoreId { get; set; }
        public int CargoHold { get; set; } = 8;
        public int StashHold { get; set; } = 4;
        public int FuelStash { get; set; }
        public int UpgradeSlots { get; set; } = 3;

        public void ApplyShip(ShipCard ship)
        {
            if (ship == null)
                throw new ArgumentNullException(nameof(ship));
            ShipId = ship.Id;
            Roster.MaxCrew = ship.MaxCrew;
            CargoHold = ship.CargoHolds;
            StashHold = ship.Stash;
            FuelStash = ship.FuelStash;
            UpgradeSlots = ship.UpgradeSlots;
        }

        public int Fight => Roster.Fight + FightBonus;
        public int Tech => Roster.Tech + TechBonus;
        public int Talk => Roster.Talk + TalkBonus;
        public int WantedCrew => Roster.WantedCount;

        public PlayerState(
            string id,
            string name,
            string sectorId,
            int fuel = 3,
            int parts = 2,
            int cash = 0,
            int driveRange = 5,
            bool fullBurnRequiresFuel = true,
            string? shipId = null,
            int maxCrew = 6)
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
            Roster = new CrewRoster(maxCrew);
            JobHandLimit = 3;
            ActiveJobLimit = 1;
            JobHand = new List<string>();
            ActiveJobs = new List<ActiveJob>();
            SolidWith = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Deal = new DealModifiers();
            Gear = new List<string>();
            ShipUpgrades = new List<string>();
        }

        public bool IsSolidWith(string contactIdOrName) => SolidWith.Contains(contactIdOrName);

        public void BecomeSolid(string contactId)
        {
            if (!string.IsNullOrWhiteSpace(contactId))
                SolidWith.Add(contactId);
        }

        public int SolidCount => SolidWith.Count;

        public bool TryLoseSolid(string? contactIdOrName = null)
        {
            if (!string.IsNullOrWhiteSpace(contactIdOrName) && SolidWith.Remove(contactIdOrName))
                return true;
            foreach (var id in SolidWith)
            {
                SolidWith.Remove(id);
                return true;
            }
            return false;
        }

        public ActiveJob? FindActive(string jobId)
        {
            foreach (var job in ActiveJobs)
            {
                if (job.JobId == jobId)
                    return job;
            }
            return null;
        }

        public bool RemoveActive(string jobId)
        {
            for (var i = 0; i < ActiveJobs.Count; i++)
            {
                if (ActiveJobs[i].JobId == jobId)
                {
                    ActiveJobs.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
    }
}
