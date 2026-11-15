using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>
/// Own-seat ship sheet (P4). Packing mirrors kernel <see cref="HoldSpace"/>
/// (GF9 setup / Buy: 1 cargo|contraband|passenger|fugitive per space, or up to 2 fuel/parts;
/// Jetwash/Esmeralda fuel-only stash = 1 fuel each).
/// </summary>
public sealed class ShipSheetView
{
    public required string SeatName { get; init; }
    public required string ShipId { get; init; }
    public required string ShipName { get; init; }
    public required string? ShipCardArtUrl { get; init; }
    public required string SectorId { get; init; }
    public required string HavenSectorId { get; init; }
    public required int Cash { get; init; }
    public required int Warrants { get; init; }
    public required int Fuel { get; init; }
    public required int Parts { get; init; }
    public required int Cargo { get; init; }
    public required int Contraband { get; init; }
    public required int Passengers { get; init; }
    public required int Fugitives { get; init; }
    public required int UsedGeneralHolds { get; init; }
    public required int GeneralHoldSlots { get; init; }
    public required int MaxCrew { get; init; }
    public required int CrewCount { get; init; }
    public required int UpgradeSlots { get; init; }
    public required DriveCoreSlotView DriveCore { get; init; }
    public required IReadOnlyList<UpgradeSlotView> UpgradeSlotsFilled { get; init; }
    public required IReadOnlyList<HoldCellView> CargoHoldCells { get; init; }
    public required IReadOnlyList<HoldCellView> StashCells { get; init; }
    public required IReadOnlyList<HoldCellView> FuelOnlyStashCells { get; init; }
    public required IReadOnlyList<HoldCellView> PassengerOnlyCells { get; init; }
    public required IReadOnlyList<CrewSheetEntry> Crew { get; init; }
    public required IReadOnlyList<GearSheetEntry> OnboardGear { get; init; }
    public required IReadOnlyList<JobSheetEntry> ActiveJobs { get; init; }
    public required IReadOnlyList<JobSheetEntry> JobHand { get; init; }
    public required IReadOnlyList<SolidContactView> SolidWith { get; init; }
}

public enum HoldCellKind
{
    Empty,
    Fuel,
    Parts,
    FuelAndParts,
    Cargo,
    Contraband,
    Passenger,
    Fugitive
}

public sealed record HoldCellView(HoldCellKind Kind, string Label, int FuelCount = 0, int PartsCount = 0);

public sealed record DriveCoreSlotView(
    string? Id,
    string Name,
    int Range,
    bool RequiresFuel,
    string? PortraitUrl);

public sealed record UpgradeSlotView(
    string? Id,
    string Name,
    bool Empty,
    string? PortraitUrl);

public sealed record CrewSheetEntry(
    string Id,
    string Name,
    bool IsLeader,
    bool Disgruntled,
    bool Wanted,
    bool Moral,
    int Fight,
    int Tech,
    int Talk,
    string? PortraitUrl,
    IReadOnlyList<GearSheetEntry> EquippedGear);

public sealed record GearSheetEntry(
    string Id,
    string Name,
    string? CarrierCrewId,
    string CarrierLabel,
    string? PortraitUrl);

public sealed record JobSheetEntry(
    string Id,
    string Name,
    string ContactName,
    bool Legal,
    bool Immoral,
    bool PickedUp,
    string? PayLabel);

public sealed record SolidContactView(string Id, string Name);

public static class ShipSheetBuilder
{
    public static ShipSheetView From(GameState game, PlayerState player)
    {
        var shipName = player.ShipId ?? "Ship";
        if (!string.IsNullOrWhiteSpace(player.ShipId)
            && game.Ships != null
            && game.Ships.TryResolve(player.ShipId, out var ship))
        {
            shipName = ship.Name;
        }

        var packing = HoldPacker.Pack(player);

        var driveName = player.DriveCoreId ?? "—";
        var driveRange = player.DriveRange;
        var requiresFuel = player.FullBurnRequiresFuel;
        string? drivePortrait = null;
        if (!string.IsNullOrWhiteSpace(player.DriveCoreId)
            && game.DriveCores != null
            && game.DriveCores.TryResolve(player.DriveCoreId, out var core))
        {
            driveName = core.Name;
            driveRange = core.Range;
            requiresFuel = core.RequiresFuel;
            drivePortrait = CardArtUrls.DriveCore(player.DriveCoreId);
        }

        var upgrades = new List<UpgradeSlotView>();
        foreach (var upgradeId in player.ShipUpgrades)
        {
            var name = upgradeId;
            if (game.ShipUpgradeCatalog != null
                && game.ShipUpgradeCatalog.TryGet(upgradeId, out var entry))
            {
                name = entry.Name;
            }
            upgrades.Add(new UpgradeSlotView(
                upgradeId,
                name,
                Empty: false,
                CardArtUrls.ShipUpgrade(upgradeId)));
        }
        while (upgrades.Count < Math.Max(0, player.UpgradeSlots))
        {
            upgrades.Add(new UpgradeSlotView(null, "Empty slot", Empty: true, null));
        }

        var gearByCrew = new Dictionary<string, List<GearSheetEntry>>(StringComparer.OrdinalIgnoreCase);
        var onboard = new List<GearSheetEntry>();
        foreach (var gearId in player.Gear)
        {
            var name = gearId;
            if (game.Gear != null && game.Gear.TryGet(gearId, out var gear))
                name = gear.Name;
            player.GearCarriers.TryGetValue(gearId, out var carrierId);
            var carrierLabel = "Onboard";
            if (!string.IsNullOrWhiteSpace(carrierId))
            {
                var carrier = player.Roster.Members.FirstOrDefault(m =>
                    string.Equals(m.Id, carrierId, StringComparison.OrdinalIgnoreCase));
                carrierLabel = carrier?.Name ?? carrierId;
            }
            var entry = new GearSheetEntry(
                gearId,
                name,
                string.IsNullOrWhiteSpace(carrierId) ? null : carrierId,
                carrierLabel,
                CardArtUrls.Gear(gearId));
            if (string.IsNullOrWhiteSpace(carrierId))
                onboard.Add(entry);
            else
            {
                if (!gearByCrew.TryGetValue(carrierId, out var list))
                {
                    list = new List<GearSheetEntry>();
                    gearByCrew[carrierId] = list;
                }
                list.Add(entry);
            }
        }

        var crew = player.Roster.Members
            .Select(m =>
            {
                gearByCrew.TryGetValue(m.Id, out var equipped);
                return new CrewSheetEntry(
                    m.Id,
                    m.Name,
                    m.IsLeader,
                    m.Disgruntled,
                    m.Wanted,
                    m.Moral,
                    m.Card.Fight,
                    m.Card.Tech,
                    m.Card.Talk,
                    CardArtUrls.Crew(m.Id),
                    equipped ?? (IReadOnlyList<GearSheetEntry>)Array.Empty<GearSheetEntry>());
            })
            .OrderByDescending(c => c.IsLeader)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var activeJobs = player.ActiveJobs
            .Select(j => ToJob(game, j.JobId, j.PickedUp))
            .ToList();
        var jobHand = player.JobHand
            .Select(id => ToJob(game, id, pickedUp: false))
            .ToList();

        var solid = new List<SolidContactView>();
        foreach (var id in player.SolidWith.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var name = id;
            if (game.Contacts != null)
            {
                if (game.Contacts.TryGet(id, out var contact)
                    || game.Contacts.TryFindByName(id, out contact))
                {
                    name = contact.Name;
                }
            }
            solid.Add(new SolidContactView(id, name));
        }

        return new ShipSheetView
        {
            SeatName = player.Name,
            ShipId = player.ShipId ?? "",
            ShipName = shipName,
            ShipCardArtUrl = string.IsNullOrWhiteSpace(player.ShipId)
                ? null
                : CardArtUrls.ShipCard(player.ShipId),
            SectorId = player.SectorId,
            HavenSectorId = player.HavenSectorId,
            Cash = player.Cash,
            Warrants = player.Warrants,
            Fuel = player.Fuel,
            Parts = player.Parts,
            Cargo = player.Cargo,
            Contraband = player.Contraband,
            Passengers = player.Passengers,
            Fugitives = player.Fugitives,
            UsedGeneralHolds = HoldSpace.UsedGeneral(player),
            GeneralHoldSlots = HoldSpace.GeneralSlots(player),
            MaxCrew = player.Roster.MaxCrew,
            CrewCount = player.Roster.Count,
            UpgradeSlots = player.UpgradeSlots,
            DriveCore = new DriveCoreSlotView(
                player.DriveCoreId,
                driveName,
                driveRange,
                requiresFuel,
                drivePortrait),
            UpgradeSlotsFilled = upgrades,
            CargoHoldCells = packing.CargoHold,
            StashCells = packing.Stash,
            FuelOnlyStashCells = packing.FuelOnly,
            PassengerOnlyCells = packing.PassengerOnly,
            Crew = crew,
            OnboardGear = onboard,
            ActiveJobs = activeJobs,
            JobHand = jobHand,
            SolidWith = solid
        };
    }

    private static JobSheetEntry ToJob(GameState game, string jobId, bool pickedUp)
    {
        if (game.Jobs != null && game.Jobs.TryGet(jobId, out var job))
        {
            var pay = job.PayBase != null
                ? $"${job.PayBase:N0}"
                : (string.IsNullOrWhiteSpace(job.PayRaw) ? null : job.PayRaw);
            return new JobSheetEntry(
                job.Id,
                job.Name,
                job.ContactName,
                job.Legal,
                job.Immoral,
                pickedUp,
                pay);
        }

        return new JobSheetEntry(jobId, jobId, "?", Legal: true, Immoral: false, pickedUp, null);
    }
}

/// <summary>Static URLs for Option B plates / ship card scans served from <c>reference/Cards/</c>.</summary>
public static class CardArtUrls
{
    public static string ShipCard(string shipId) => $"/cards/ships/{shipId}.webp";
    public static string Crew(string crewId) => $"/cards/crew/{crewId}.webp";
    public static string Gear(string gearId) => $"/cards/gear/{gearId}.webp";
    public static string DriveCore(string driveCoreId) => $"/cards/drive-cores/{driveCoreId}.webp";
    public static string ShipUpgrade(string upgradeId) => $"/cards/ship-upgrades/{upgradeId}.webp";
}

/// <summary>
/// Visual hold packing that matches <see cref="HoldSpace"/> capacity math.
/// Fuel-only stash fills first (1 fuel/cell); passenger-only takes people first;
/// remaining goods fill cargo-hold cells then stash cells.
/// </summary>
public static class HoldPacker
{
    public sealed record Result(
        IReadOnlyList<HoldCellView> CargoHold,
        IReadOnlyList<HoldCellView> Stash,
        IReadOnlyList<HoldCellView> FuelOnly,
        IReadOnlyList<HoldCellView> PassengerOnly);

    public static Result Pack(PlayerState player)
    {
        var fuel = Math.Max(0, player.Fuel);
        var parts = Math.Max(0, player.Parts);
        var cargo = Math.Max(0, player.Cargo);
        var contraband = Math.Max(0, player.Contraband);
        var passengers = Math.Max(0, player.Passengers);
        var fugitives = Math.Max(0, player.Fugitives);
        var fuelOnlySlots = Math.Max(0, player.FuelStash);
        var passengerOnlySlots = Math.Max(0, player.PassengerHold);
        var cargoSlots = Math.Max(0, player.CargoHold);
        var stashSlots = Math.Max(0, player.StashHold);

        var fuelOnly = new List<HoldCellView>(fuelOnlySlots);
        var fuelInFuelOnly = Math.Min(fuel, fuelOnlySlots);
        for (var i = 0; i < fuelInFuelOnly; i++)
            fuelOnly.Add(Cell(HoldCellKind.Fuel, "Fuel", fuelCount: 1));
        while (fuelOnly.Count < fuelOnlySlots)
            fuelOnly.Add(Empty());
        fuel -= fuelInFuelOnly;

        var passengerOnly = new List<HoldCellView>(passengerOnlySlots);
        var people = passengers + fugitives;
        var peopleInPassengerOnly = Math.Min(people, passengerOnlySlots);
        var passIntoPassengerOnly = Math.Min(passengers, peopleInPassengerOnly);
        var fugIntoPassengerOnly = peopleInPassengerOnly - passIntoPassengerOnly;
        for (var i = 0; i < passIntoPassengerOnly; i++)
            passengerOnly.Add(Cell(HoldCellKind.Passenger, "Passenger"));
        for (var i = 0; i < fugIntoPassengerOnly; i++)
            passengerOnly.Add(Cell(HoldCellKind.Fugitive, "Fugitive"));
        while (passengerOnly.Count < passengerOnlySlots)
            passengerOnly.Add(Empty());
        passengers -= passIntoPassengerOnly;
        fugitives -= fugIntoPassengerOnly;

        // Prefer printed-style zoning: cargo/people/fuel-parts → cargo hold; contraband/fugitives → stash.
        var cargoQueue = new Queue<HoldCellView>();
        var stashQueue = new Queue<HoldCellView>();
        for (var i = 0; i < cargo; i++)
            cargoQueue.Enqueue(Cell(HoldCellKind.Cargo, "Cargo"));
        for (var i = 0; i < passengers; i++)
            cargoQueue.Enqueue(Cell(HoldCellKind.Passenger, "Passenger"));
        for (var i = 0; i < contraband; i++)
            stashQueue.Enqueue(Cell(HoldCellKind.Contraband, "Contraband"));
        for (var i = 0; i < fugitives; i++)
            stashQueue.Enqueue(Cell(HoldCellKind.Fugitive, "Fugitive"));

        while (fuel > 0 || parts > 0)
        {
            var takeFuel = Math.Min(2, fuel);
            var remain = 2 - takeFuel;
            var takeParts = Math.Min(remain, parts);
            fuel -= takeFuel;
            parts -= takeParts;
            cargoQueue.Enqueue(MixedFuelParts(takeFuel, takeParts));
        }

        var cargoHold = new List<HoldCellView>(cargoSlots);
        var stash = new List<HoldCellView>(stashSlots);
        Fill(cargoHold, cargoSlots, cargoQueue, stashQueue);
        Fill(stash, stashSlots, cargoQueue, stashQueue);

        // Overflow should never happen when state obeys HoldSpace; dump extras into cargo display if it does.
        while (cargoQueue.Count > 0)
            cargoHold.Add(cargoQueue.Dequeue());
        while (stashQueue.Count > 0)
            stash.Add(stashQueue.Dequeue());

        return new Result(cargoHold, stash, fuelOnly, passengerOnly);
    }

    private static void Fill(
        List<HoldCellView> target,
        int slots,
        Queue<HoldCellView> primary,
        Queue<HoldCellView> secondary)
    {
        while (target.Count < slots && (primary.Count > 0 || secondary.Count > 0))
        {
            if (primary.Count > 0)
                target.Add(primary.Dequeue());
            else
                target.Add(secondary.Dequeue());
        }
        while (target.Count < slots)
            target.Add(Empty());
    }

    private static HoldCellView Empty() => new(HoldCellKind.Empty, "");

    private static HoldCellView Cell(HoldCellKind kind, string label, int fuelCount = 0, int partsCount = 0) =>
        new(kind, label, fuelCount, partsCount);

    private static HoldCellView MixedFuelParts(int fuel, int parts)
    {
        if (fuel > 0 && parts > 0)
            return new HoldCellView(HoldCellKind.FuelAndParts, $"F{fuel}/P{parts}", fuel, parts);
        if (fuel > 0)
            return new HoldCellView(HoldCellKind.Fuel, fuel == 1 ? "Fuel" : $"Fuel×{fuel}", fuel, 0);
        return new HoldCellView(HoldCellKind.Parts, parts == 1 ? "Parts" : $"Parts×{parts}", 0, parts);
    }
}
