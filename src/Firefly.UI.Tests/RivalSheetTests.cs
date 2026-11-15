using Firefly.Core.State;
using Firefly.UI.Services;
using Xunit;

namespace Firefly.UI.Tests;

/// <summary>
/// P8 rival public sheets: Active Jobs + board resources visible; Job hand hidden (prefs lock).
/// </summary>
public sealed class RivalSheetTests
{
    [Fact]
    public void One_player_smoke_has_empty_rival_list()
    {
        var session = new HotseatGameSession();
        var snap = PlayViewSnapshot.From(session, session.Game.Map);

        Assert.Equal(1, snap.SeatCount);
        Assert.Empty(snap.RivalSheets);
        Assert.True(snap.ShipSheet.IncludeJobHand);
        Assert.Equal(3, snap.ShipSheet.JobHand.Count);
    }

    [Fact]
    public void Two_player_smoke_exposes_rival_without_job_hand()
    {
        var session = new HotseatGameSession();
        session.StartNew(SmokeScenarios.DefaultId, seatCount: 2);

        Assert.Equal(2, session.SeatCount);
        Assert.Equal(2, session.Game.Players.Count);

        var current = session.Game.CurrentPlayer;
        var rival = session.Game.Players.First(p => p.Id != current.Id);
        Assert.Equal("p2", rival.Id);
        Assert.Equal("Zoe", rival.Name);
        Assert.Equal(HotseatGameSession.SecondShipId, rival.ShipId);
        Assert.NotEmpty(rival.JobHand);

        var snap = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.Equal(2, snap.SeatCount);
        Assert.Single(snap.RivalSheets);

        var publicSheet = snap.RivalSheets[0];
        Assert.Equal(rival.Id, publicSheet.PlayerId);
        Assert.False(publicSheet.IncludeJobHand);
        Assert.Empty(publicSheet.JobHand);
        Assert.Equal(rival.Cash, publicSheet.Cash);
        Assert.Equal(rival.Fuel, publicSheet.Fuel);
        Assert.Equal(rival.ShipId, publicSheet.ShipId);

        // Own sheet still includes Job hand.
        Assert.True(snap.ShipSheet.IncludeJobHand);
        Assert.Equal(current.JobHand.Count, snap.ShipSheet.JobHand.Count);
    }

    [Fact]
    public void Rival_sheet_shows_active_jobs_not_hand()
    {
        var session = new HotseatGameSession();
        session.StartNew(SmokeScenarios.DefaultId, seatCount: 2);
        var rival = session.Game.Players.First(p => p.Id == "p2");

        Assert.True(rival.JobHand.Count >= 1);
        var jobId = rival.JobHand[0];
        rival.JobHand.RemoveAt(0);
        rival.ActiveJobs.Add(new ActiveJob(jobId));

        var sheet = ShipSheetBuilder.FromRival(session.Game, rival);
        Assert.False(sheet.IncludeJobHand);
        Assert.Empty(sheet.JobHand);
        Assert.Single(sheet.ActiveJobs);
        Assert.Equal(jobId, sheet.ActiveJobs[0].Id);

        // Builder must not leak remaining hand ids even when Active is populated.
        Assert.True(rival.JobHand.Count >= 1);
        Assert.DoesNotContain(sheet.ActiveJobs, j => rival.JobHand.Contains(j.Id));
    }

    [Fact]
    public void FromRival_never_copies_job_hand_ids()
    {
        var session = new HotseatGameSession();
        session.StartNew(SmokeScenarios.DefaultId, seatCount: 2);
        var rival = session.Game.GetPlayer("p2");
        var handIds = rival.JobHand.ToList();
        Assert.NotEmpty(handIds);

        var sheet = ShipSheetBuilder.FromRival(session.Game, rival);
        Assert.Empty(sheet.JobHand);
        Assert.All(handIds, id => Assert.DoesNotContain(sheet.ActiveJobs, j => j.Id == id));
    }

    [Fact]
    public void EndTurn_swaps_own_and_rival_privacy()
    {
        var session = new HotseatGameSession();
        session.StartNew(SmokeScenarios.DefaultId, seatCount: 2);

        var before = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.Equal("p1", before.SeatId);
        Assert.Equal("p2", before.RivalSheets[0].PlayerId);
        Assert.True(before.ShipSheet.IncludeJobHand);
        Assert.False(before.RivalSheets[0].IncludeJobHand);

        session.EndTurn();

        var after = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.Equal("p2", after.SeatId);
        Assert.Equal("p1", after.RivalSheets[0].PlayerId);
        Assert.True(after.ShipSheet.IncludeJobHand);
        Assert.Equal(session.Game.CurrentPlayer.JobHand.Count, after.ShipSheet.JobHand.Count);
        Assert.False(after.RivalSheets[0].IncludeJobHand);
        Assert.Empty(after.RivalSheets[0].JobHand);
    }
}
