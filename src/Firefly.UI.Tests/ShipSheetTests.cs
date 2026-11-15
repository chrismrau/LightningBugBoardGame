using Firefly.Core.State;
using Firefly.UI.Services;
using Xunit;

namespace Firefly.UI.Tests;

/// <summary>
/// P4 own ship sheet: live GameState → hold packing / crew / jobs / cash for smoke seat.
/// </summary>
public sealed class ShipSheetTests
{
    [Fact]
    public void Smoke_setup_sheet_shows_Serenity_hold_crew_cash()
    {
        var session = new HotseatGameSession();
        var player = session.Game.CurrentPlayer;
        var sheet = ShipSheetBuilder.From(session.Game, player);

        Assert.Equal("ship_serenity", sheet.ShipId);
        Assert.Equal("Serenity", sheet.ShipName);
        Assert.Equal(3000, sheet.Cash);
        Assert.Equal(6, sheet.Fuel);
        Assert.Equal(2, sheet.Parts);
        Assert.Equal(0, sheet.Warrants);
        Assert.Equal(HotseatGameSession.DefaultStartSectorId, sheet.HavenSectorId);
        Assert.Equal(HotseatGameSession.DefaultStartSectorId, sheet.SectorId);

        Assert.Equal(8, sheet.CargoHoldCells.Count);
        Assert.Equal(4, sheet.StashCells.Count);
        Assert.Empty(sheet.FuelOnlyStashCells);
        Assert.Equal(4, sheet.UsedGeneralHolds);
        Assert.Equal(12, sheet.GeneralHoldSlots);

        // 6 fuel + 2 parts → 4 general cells (HoldSpace: up to 2 fuel/parts per space).
        var filledCargo = sheet.CargoHoldCells.Count(c => c.Kind != HoldCellKind.Empty);
        Assert.Equal(4, filledCargo);
        Assert.All(sheet.StashCells, c => Assert.Equal(HoldCellKind.Empty, c.Kind));

        Assert.Single(sheet.Crew);
        var leader = sheet.Crew[0];
        Assert.True(leader.IsLeader);
        Assert.False(leader.Disgruntled);
        Assert.Equal("Malcolm", leader.Name);
        Assert.Equal("leader_malcolm", leader.Id);

        Assert.Equal(3, sheet.JobHand.Count);
        Assert.Empty(sheet.ActiveJobs);
        Assert.Empty(sheet.SolidWith);

        Assert.Equal("drive-core_radion-accelerator-mark-i", sheet.DriveCore.Id);
        Assert.Equal(5, sheet.DriveCore.Range);
        Assert.True(sheet.DriveCore.RequiresFuel);
        Assert.Equal(3, sheet.UpgradeSlotsFilled.Count);
        Assert.All(sheet.UpgradeSlotsFilled, u => Assert.True(u.Empty));

        Assert.Equal(CardArtUrls.ShipCard("ship_serenity"), sheet.ShipCardArtUrl);
        Assert.Equal(CardArtUrls.Crew("leader_malcolm"), leader.PortraitUrl);
    }

    [Fact]
    public void HoldPacker_fuel_only_stash_fills_before_general()
    {
        var session = new HotseatGameSession();
        var player = session.Game.CurrentPlayer;
        player.FuelStash = 6;
        player.Fuel = 4;
        player.Parts = 0;
        player.CargoHold = 8;
        player.StashHold = 4;

        var packed = HoldPacker.Pack(player);
        Assert.Equal(6, packed.FuelOnly.Count);
        Assert.Equal(4, packed.FuelOnly.Count(c => c.Kind == HoldCellKind.Fuel));
        Assert.Equal(2, packed.FuelOnly.Count(c => c.Kind == HoldCellKind.Empty));
        Assert.All(packed.CargoHold, c => Assert.Equal(HoldCellKind.Empty, c.Kind));
        Assert.Equal(0, HoldSpace.UsedGeneral(player));
    }

    [Fact]
    public void HoldPacker_matches_HoldSpace_used_count()
    {
        var session = new HotseatGameSession();
        var player = session.Game.CurrentPlayer;
        player.Fuel = 3;
        player.Parts = 1;
        player.Cargo = 2;
        player.Contraband = 1;
        player.Passengers = 1;
        player.Fugitives = 1;

        var sheet = ShipSheetBuilder.From(session.Game, player);
        var filledGeneral =
            sheet.CargoHoldCells.Count(c => c.Kind != HoldCellKind.Empty)
            + sheet.StashCells.Count(c => c.Kind != HoldCellKind.Empty);

        Assert.Equal(HoldSpace.UsedGeneral(player), filledGeneral);
        Assert.Equal(HoldSpace.UsedGeneral(player), sheet.UsedGeneralHolds);
    }

    [Fact]
    public void PlayViewSnapshot_includes_ship_sheet()
    {
        var session = new HotseatGameSession();
        var snap = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.Equal("Serenity", snap.ShipSheet.ShipName);
        Assert.Equal(snap.Cash, snap.ShipSheet.Cash);
        Assert.Equal(snap.Fuel, snap.ShipSheet.Fuel);
    }

    [Fact]
    public void Disgruntled_leader_is_visible_on_sheet()
    {
        var session = new HotseatGameSession();
        var leader = session.Game.CurrentPlayer.Roster.Leader!;
        leader.Disgruntled = true;

        var sheet = ShipSheetBuilder.From(session.Game, session.Game.CurrentPlayer);
        Assert.True(sheet.Crew[0].Disgruntled);
        Assert.True(sheet.Crew[0].IsLeader);
    }
}
