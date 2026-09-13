using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class CorvetteTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string CorvetteStart = "rim-cortex-relay-2-r1-11";
        private const string AdjacentRim = "rim-space-r1-10";
        private const string AdjacentPlanet = "rim-penglai-r1-02"; // Beylix
        private const string ReaverStart = "rim-burnham-r2-01";
        private const string ReaverStartAlt = "rim-burnham-r2-02";

        [Fact]
        public void Corvette_Contact_removes_Wanted_discards_non_stash_Fugitives_Full_Stops()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(operativeCorvetteSectorId: Pelorum);
            var catalog = CrewCatalog.LoadDefault();
            var player = new PlayerState("p1", "Mal", Pelorum)
            {
                Warrants = 1,
                Fugitives = 6,
                StashHold = 4
            };
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _)); // Wanted
            Assert.True(player.Roster.TryHire(catalog.Get("crew_zoe"), out _));
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.OperativeCorvette;
            game.PendingEncounterSectorId = Pelorum;
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));

            Assert.True(CorvetteContact.TryResolve(
                game,
                out var result,
                out var error,
                new CorvetteContactChoice { RemoveWantedCrewId = "crew_jayne" }), error);

            Assert.Equal("crew_jayne", result!.WantedRemovedId);
            Assert.Equal(2, result.FugitivesDiscarded);
            Assert.Equal(4, result.FugitivesKeptInStash);
            Assert.Equal(4, player.Fugitives);
            Assert.DoesNotContain(player.Roster.Members, m => m.Id == "crew_jayne");
            Assert.Contains("crew_jayne", game.RemovedFromPlay);
            Assert.Null(game.PendingEncounter);
            Assert.Empty(game.PendingNavDraws);
        }

        [Fact]
        public void Corvette_Contact_Wanted_removal_blocked_by_protecting_gear_flag()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var catalog = CrewCatalog.LoadDefault();
            var player = new PlayerState("p1", "Mal", Pelorum) { Fugitives = 1, StashHold = 0 };
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            var game = new GameState(map, new[] { player }, new MapTokens(operativeCorvetteSectorId: Pelorum));
            game.PendingEncounter = TokenKind.OperativeCorvette;
            game.PendingEncounterSectorId = Pelorum;

            Assert.True(CorvetteContact.TryResolve(
                game,
                out var result,
                out var error,
                new CorvetteContactChoice { ProtectedByWantedRollGear = true }), error);

            Assert.True(result!.WantedRemovalPrevented);
            Assert.Null(result.WantedRemovedId);
            Assert.Equal(1, player.Roster.WantedCount);
            Assert.Equal(1, result.FugitivesDiscarded);
            Assert.Equal(0, player.Fugitives);
        }

        [Fact]
        public void Outlaw_Fly_into_Corvette_queues_Contact_Legal_does_not()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(operativeCorvetteSectorId: Pelorum);
            var outlaw = new PlayerState("p1", "Mal", Persephone, fuel: 3) { Warrants = 1 };
            var game = new GameState(map, new[] { outlaw }, tokens);
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(fly.TryFullBurnTo(game, "p1", Pelorum, out var result, out var error), error);
            Assert.True(result!.StoppedForEncounter);
            Assert.Equal(TokenKind.OperativeCorvette, game.PendingEncounter);
            // FAQ Contact-before-Nav pattern: Full Stop Contact skips Nav for the entered Sector.
            Assert.Empty(game.PendingNavDraws);

            var legal = new PlayerState("p2", "Zoe", Persephone, fuel: 3);
            var game2 = new GameState(map, new[] { legal }, tokens);
            Assert.True(fly.TryFullBurnTo(game2, "p2", Pelorum, out var legalResult, out _), error);
            Assert.False(legalResult!.StoppedForEncounter);
            Assert.Null(game2.PendingEncounter);
            Assert.Single(game2.PendingNavDraws);
        }

        [Fact]
        public void Nav_Sideways_moves_Corvette_adjacent_Keep_Flying_no_Contact_off_ship()
        {
            var (game, resolver, player) = RimGameWithCorvette();
            game.Decks!.Rim.PlaceOnTop(game.Decks.Catalog.Get("nav_hell-come-at-you-sideways"));
            // Player is at CorvetteStart; move Corvette to adjacent unoccupied sector (not onto player).
            game.Tokens = game.Tokens.WithOperativeCorvette(AdjacentRim);
            player.SectorId = CorvetteStart;
            game.PendingNavDraws.Clear();
            game.PendingNavDraws.Add(new PendingNavDraw(CorvetteStart, NavRegion.Rim));

            var choice = new NavResolveChoice { OperativeCorvetteToSectorId = AdjacentPlanet };
            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error, choice: choice), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.False(resolution.Stopped);
            Assert.Null(resolution.CorvetteContact);
            Assert.Equal(AdjacentPlanet, game.Tokens.OperativeCorvetteSectorId);
            Assert.Equal(CorvetteStart, player.SectorId);
            Assert.Null(game.PendingEncounter);
        }

        [Fact]
        public void Nav_Corvette_onto_Outlaw_triggers_Contact_and_Full_Stop()
        {
            var (game, resolver, player) = RimGameWithCorvette();
            player.Warrants = 1;
            player.Fugitives = 2;
            player.StashHold = 0;
            var catalog = CrewCatalog.LoadDefault();
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            // Persistent Pursuit (no "unoccupied") can move onto the Outlaw's Sector.
            game.Tokens = game.Tokens.WithOperativeCorvette(AdjacentRim);
            game.Decks!.Rim.PlaceOnTop(game.Decks.Catalog.Get("nav_persistent-pursuit"));

            var choice = new NavResolveChoice
            {
                OperativeCorvetteToSectorId = CorvetteStart,
                CorvetteContact = new CorvetteContactChoice { RemoveWantedCrewId = "crew_jayne" }
            };
            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error, choice: choice), error);
            Assert.Equal(FlightOutcome.FullStop, resolution!.Outcome);
            Assert.True(resolution.Stopped);
            Assert.NotNull(resolution.CorvetteContact);
            Assert.Equal(CorvetteStart, game.Tokens.OperativeCorvetteSectorId);
            Assert.Equal(0, player.Fugitives);
            Assert.Empty(game.PendingNavDraws);
            Assert.Null(game.PendingEncounter);
        }

        [Fact]
        public void Persistent_Pursuit_requires_1_or_2_sector_move_from_Corvette()
        {
            var (game, resolver, player) = RimGameWithCorvette();
            game.Decks!.Rim.PlaceOnTop(game.Decks.Catalog.Get("nav_persistent-pursuit"));
            resolver.DrawNext(game);

            // Too far / zero: stay put rejected
            Assert.False(resolver.TryResolve(
                game,
                0,
                out _,
                out var error,
                choice: new NavResolveChoice { OperativeCorvetteToSectorId = CorvetteStart }));
            Assert.Contains("1 or 2", error);

            Assert.True(resolver.TryResolve(
                game,
                0,
                out var resolution,
                out error,
                choice: new NavResolveChoice { OperativeCorvetteToSectorId = AdjacentRim }), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Equal(AdjacentRim, game.Tokens.OperativeCorvetteSectorId);
        }

        [Fact]
        public void Corvette_rejects_ending_in_Reaver_Starting_Zone()
        {
            var tokens = new MapTokens(operativeCorvetteSectorId: CorvetteStart);
            Assert.False(tokens.TryMoveOperativeCorvette(ReaverStart, out _, out var error));
            Assert.Contains("Reaver Starting", error);
        }

        [Fact]
        public void Corvette_trumps_Cutter_and_clears_Reaver_Alert_Tokens()
        {
            var tokens = new MapTokens(
                    operativeCorvetteSectorId: CorvetteStart,
                    reaverCutterSectorIds: new[] { AdjacentRim })
                .PlaceAlertToken(AdjacentRim, AlertTokenKind.Reaver, 2)
                .PlaceAlertToken(AdjacentRim, AlertTokenKind.Alliance, 1);

            Assert.True(tokens.TryMoveOperativeCorvette(
                AdjacentRim,
                out var updated,
                out var error,
                driveOffReaverToSectorId: ReaverStartAlt), error);

            Assert.Equal(AdjacentRim, updated.OperativeCorvetteSectorId);
            Assert.Equal(ReaverStartAlt, updated.ReaverCutterSectorIds[0]);
            Assert.Equal(0, updated.RemovableAlertCount(AdjacentRim, AlertTokenKind.Reaver));
            Assert.Equal(1, updated.RemovableAlertCount(AdjacentRim, AlertTokenKind.Alliance));
        }

        [Fact]
        public void Reaver_Cutter_Nav_in_Corvette_Sector_is_blocked_and_reshuffles()
        {
            var (game, resolver, player) = RimGameWithCorvette();
            // Player Full Burning into Corvette's sector draws Reaver Cutter.
            player.SectorId = AdjacentRim;
            game.Tokens = game.Tokens.WithOperativeCorvette(CorvetteStart);
            game.PendingNavDraws.Clear();
            game.PendingNavDraws.Add(new PendingNavDraw(CorvetteStart, NavRegion.Rim));
            game.Decks!.Rim.PlaceOnTop(game.Decks.Catalog.Get("nav_reaver-cutter"));
            var drawBefore = game.Decks.Rim.DrawCount;

            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error), error);
            Assert.True(resolution!.ReaverCutterBlockedByCorvette);
            Assert.Equal(FlightOutcome.KeepFlying, resolution.Outcome);
            Assert.Null(resolution.ReaverContact);
            Assert.Equal(CorvetteStart, game.Tokens.OperativeCorvetteSectorId);
            // Reshuffle card returned discard into draw.
            Assert.Equal(0, game.Decks.Rim.DiscardCount);
            Assert.True(game.Decks.Rim.DrawCount >= drawBefore);
        }

        [Fact]
        public void Alliance_Alert_in_Border_moves_Corvette_not_Cruiser()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(
                    allianceCruiserSectorId: Persephone,
                    operativeCorvetteSectorId: CorvetteStart)
                .PlaceAlertToken(AdjacentRim, AlertTokenKind.Alliance, 2);
            var player = new PlayerState("p1", "Mal", AdjacentRim, fuel: 3) { Warrants = 1 };
            var game = new GameState(map, new[] { player }, tokens) { UseAlertTokens = true };
            game.PendingAlertSectors.Add(AdjacentRim);
            game.PendingNavDraws.Add(new PendingNavDraw(AdjacentRim, NavRegion.Border));

            Assert.True(AlertTokenResolver.TryResolvePending(
                game,
                ScriptedRng.FromDieFaces(1),
                out var resolution,
                out var error,
                new AlertResolveChoice
                {
                    AllianceShip = TokenKind.OperativeCorvette,
                    CorvetteContact = new CorvetteContactChoice()
                }), error);

            Assert.True(resolution!.EndedFly);
            Assert.Equal(TokenKind.OperativeCorvette, resolution.Rolls[0].ArrivedShip);
            Assert.Equal(AdjacentRim, game.Tokens.OperativeCorvetteSectorId);
            Assert.Equal(Persephone, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(TokenKind.OperativeCorvette, game.PendingEncounter);
        }

        [Fact]
        public void Alliance_Alert_in_Alliance_Space_may_choose_Corvette()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(
                    allianceCruiserSectorId: Persephone,
                    operativeCorvetteSectorId: CorvetteStart)
                .PlaceAlertToken(Pelorum, AlertTokenKind.Alliance, 3);
            var player = new PlayerState("p1", "Mal", Pelorum) { Contraband = 1 };
            var game = new GameState(map, new[] { player }, tokens) { UseAlertTokens = true };
            game.PendingAlertSectors.Add(Pelorum);

            Assert.True(AlertTokenResolver.TryResolvePending(
                game,
                ScriptedRng.FromDieFaces(2),
                out var resolution,
                out var error,
                new AlertResolveChoice { AllianceShip = TokenKind.OperativeCorvette }), error);

            Assert.Equal(TokenKind.OperativeCorvette, resolution!.Rolls[0].ArrivedShip);
            Assert.Equal(Pelorum, game.Tokens.OperativeCorvetteSectorId);
            Assert.Equal(TokenKind.OperativeCorvette, game.PendingEncounter);
        }

        private static (GameState Game, NavResolver Resolver, PlayerState Player) RimGameWithCorvette()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var tokens = new MapTokens(operativeCorvetteSectorId: CorvetteStart);
            var player = new PlayerState("p1", "Mal", CorvetteStart, fuel: 3);
            var game = new GameState(map, new[] { player }, tokens, decks);
            game.PendingNavDraws.Add(new PendingNavDraw(CorvetteStart, NavRegion.Rim));
            return (game, new NavResolver(), player);
        }
    }
}
