using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Firefly.UI.Services;
using Xunit;

namespace Firefly.UI.Tests;

/// <summary>
/// P9 expansions chrome — Most Wanted / Alliance Alert / Corvette / bounty Work hooks.
/// </summary>
public sealed class ExpansionChromeTests
{
    [Fact]
    public void PBH_toggle_wires_BountyDeck_and_MostWanted_face_up()
    {
        var session = new HotseatGameSession();
        session.StartNew(SmokeScenarios.DefaultId, useBountyDeck: true);

        Assert.True(session.UseBountyDeck);
        Assert.NotNull(session.Game.BountyDeck);
        Assert.Equal(BountyDeck.WantedCount, session.Game.BountyDeck!.FaceUp.Count);

        var chrome = ExpansionChromeBuilder.From(session.Game);
        Assert.True(chrome.BountyDeckInPlay);
        Assert.Equal(BountyDeck.WantedCount, chrome.MostWanted.Count);
        Assert.All(chrome.MostWanted, m => Assert.False(string.IsNullOrWhiteSpace(m.Name)));
    }

    [Fact]
    public void Wanted_Men_starts_with_Active_Alliance_Alert()
    {
        var session = new HotseatGameSession();
        session.StartNew("scenario_wanted-men");

        Assert.NotNull(session.Game.AllianceAlertDeck);
        Assert.NotNull(session.Game.AllianceAlertDeck!.Active);

        var chrome = ExpansionChromeBuilder.From(session.Game);
        Assert.True(chrome.AllianceAlertDeckInPlay);
        Assert.NotNull(chrome.ActiveAlert);
        Assert.False(string.IsNullOrWhiteSpace(chrome.ActiveAlert!.Name));
        Assert.NotEqual("(no active alert)", chrome.ActiveAlert.Name);
    }

    [Fact]
    public void Corvette_toggle_places_map_marker()
    {
        var session = new HotseatGameSession();
        session.StartNew(SmokeScenarios.DefaultId, useOperativesCorvette: true);

        Assert.Equal(
            GameSetup.OperativeCorvetteStartSectorId,
            session.Game.Tokens.OperativeCorvetteSectorId);

        var snap = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.Contains(snap.Markers, m => m.Kind == MarkerKind.Corvette);
        Assert.True(snap.Expansions.CorvetteInPlay);
    }

    [Fact]
    public void BlueSun_enables_Alert_Tokens_and_permanent_Reaver_chits()
    {
        var session = new HotseatGameSession();
        session.StartNew(SmokeScenarios.DefaultId, useBlueSun: true);

        Assert.True(session.Game.UseAlertTokens);
        var snap = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.Contains(snap.Markers, m => m.Kind == MarkerKind.ReaverAlertChit);
        Assert.True(snap.Expansions.UseAlertTokens);
    }

    [Fact]
    public void Work_chrome_lists_bounty_deliver_when_bound_at_dropoff()
    {
        var session = new HotseatGameSession();
        session.StartNew(SmokeScenarios.DefaultId, useBountyDeck: true, seatCount: 1);
        var game = session.Game;
        var player = game.CurrentPlayer;

        // Seed Helen on Most Wanted, betray into Bound, then move to dropoff.
        PutOnWanted(game, "bounty_helen");
        Assert.True(game.Crew!.TryGet("crew_helen", out var helen));
        Assert.True(player.Roster.TryHire(helen!, out _));

        Assert.True(session.TryBountyBetray("bounty_helen", "crew_helen", out var err), err);
        Assert.Single(player.BoundBounties);

        // Helen dropoff is printed on the card — move there for Deliver.
        var bounty = game.Bounties!.Get("bounty_helen");
        Assert.False(string.IsNullOrWhiteSpace(bounty.DropoffPlanet));
        MoveToPlanet(game, player, bounty.DropoffPlanet!);

        var view = WorkChromeBuilder.Build(game);
        // Work already consumed by Betray — Deliver disabled with reason, still listed.
        session.EndTurn();
        view = WorkChromeBuilder.Build(session.Game);
        var deliver = Assert.Single(view.Attempts, a => a.Kind == WorkAttemptKind.BountyDeliver);
        Assert.Equal("bounty_helen", deliver.Id);
        Assert.True(deliver.Enabled);
        Assert.True(session.TryBountyDeliver("bounty_helen", out err), err);
        Assert.Empty(session.Game.CurrentPlayer.BoundBounties);
    }

    [Fact]
    public void PendingHost_shows_Corvette_encounter_options()
    {
        var session = new HotseatGameSession();
        session.StartNew(SmokeScenarios.DefaultId, useOperativesCorvette: true);
        var game = session.Game;
        var player = game.CurrentPlayer;
        player.Warrants = 1; // Outlaw
        player.SectorId = game.Tokens.OperativeCorvetteSectorId!;
        Assert.True(CorvetteContact.TryQueueIfOutlaw(game, player.SectorId));
        Assert.Equal(TokenKind.OperativeCorvette, game.PendingEncounter);

        var pending = PendingHostBuilder.From(game);
        Assert.Equal(PendingQueueKind.Encounter, pending.QueueKind);
        Assert.NotNull(pending.Encounter);
        Assert.NotEmpty(pending.Encounter!.Options);

        Assert.True(session.TryResolveEncounter(pending.Encounter.Options[0].Id, out var error), error);
        Assert.Null(session.Game.PendingEncounter);
    }

    [Fact]
    public void Snapshot_includes_ExpansionChrome_when_PBH_on()
    {
        var session = new HotseatGameSession();
        session.StartNew(SmokeScenarios.DefaultId, useBountyDeck: true, useOperativesCorvette: true);
        var snap = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.True(snap.Expansions.BountyDeckInPlay);
        Assert.True(snap.Expansions.CorvetteInPlay);
        Assert.Equal(3, snap.Expansions.MostWanted.Count);
    }

    private static void PutOnWanted(GameState game, string bountyId)
    {
        Assert.NotNull(game.Bounties);
        var card = game.Bounties!.Get(bountyId);
        game.BountyDeck = new BountyDeck(new[] { card }, new SystemRng(2), game.Bounties);
        Assert.NotNull(game.BountyDeck.FindWanted(bountyId));
    }

    private static void MoveToPlanet(GameState game, PlayerState player, string planet)
    {
        if (planet.Equals("Command Cruiser", StringComparison.OrdinalIgnoreCase))
        {
            player.SectorId = game.Tokens.AllianceCruiserSectorId
                ?? GameSetup.AllianceCruiserStartSectorId;
            return;
        }

        foreach (var sector in game.Map.Sectors.Values)
        {
            if (string.Equals(sector.Planet, planet, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sector.DisplayName, planet, StringComparison.OrdinalIgnoreCase))
            {
                player.SectorId = sector.Id;
                return;
            }
        }

        throw new InvalidOperationException($"No sector for planet '{planet}'.");
    }
}
