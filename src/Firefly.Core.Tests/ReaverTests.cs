using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class ReaverTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string CutterStart = "border-space-r2-06";
        private const string CutterAdjacent = "border-space-r2-05";
        private const string CutterAdjacentAlt = "border-space-r2-07";

        [Fact]
        public void Mosey_and_FullBurn_reject_entering_cutter_sector()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var engine = new MovementEngine(map);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { Pelorum });

            Assert.False(engine.TryMosey(Persephone, Pelorum, tokens, out _, out var moseyError));
            Assert.Contains("Reaver Cutter", moseyError);

            Assert.False(engine.TryFullBurnTo(Persephone, Pelorum, driveRange: 5, tokens, out _, out var burnError));
            Assert.Contains("Reaver Cutter", burnError);
        }

        [Fact]
        public void Fly_rejects_FullBurn_into_cutter_occupied_sector()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { Pelorum });
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            var game = new GameState(map, new[] { player }, tokens);
            var fly = new FlyAction(new MovementEngine(map));

            Assert.False(fly.TryFullBurnTo(game, "p1", Pelorum, out _, out var error));
            Assert.Contains("Reaver Cutter", error);
            Assert.Equal(Persephone, player.SectorId);
            Assert.False(game.ActionTaken);
        }

        [Fact]
        public void Start_of_turn_queues_Reaver_Contact_when_sharing_cutter_sector()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            var p1 = new PlayerState("p1", "Mal", Persephone);
            var p2 = new PlayerState("p2", "Zoe", CutterStart);
            var game = new GameState(map, new[] { p1, p2 }, tokens);

            game.EndTurn();
            Assert.Equal("p2", game.CurrentPlayer.Id);
            Assert.Equal(TokenKind.ReaverCutter, game.PendingEncounter);
            Assert.Equal(CutterStart, game.PendingEncounterSectorId);
            Assert.True(game.HasPendingEvents);
            Assert.False(game.CanTakeAction(TurnAction.Fly, out var error));
            Assert.Contains("pending", error);
        }

        [Fact]
        public void Reaver_Contact_kills_passengers_fails_fight_kills_two_crew_and_evades()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var catalog = CrewCatalog.LoadDefault();
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            var player = new PlayerState("p1", "Mal", CutterStart)
            {
                Passengers = 2,
                Fugitives = 1,
                FightBonus = 0
            };
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_zoe"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_wash"), out _));
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.ReaverCutter;
            game.PendingEncounterSectorId = CutterStart;
            game.PendingNavDraws.Add(new PendingNavDraw(CutterStart, NavRegion.Border));

            // Fight 0 dice → sum 0 → fail → kill 2
            Assert.True(ReaverContact.TryResolve(
                game,
                ScriptedRng.FromDieFaces(),
                CutterAdjacent,
                out var result,
                out var error), error);
            Assert.Equal(2, result!.PassengersKilled);
            Assert.Equal(1, result.FugitivesKilled);
            Assert.False(result.Fight.Success);
            Assert.Equal(2, result.CrewKilled);
            Assert.Equal(0, player.Passengers);
            Assert.Equal(0, player.Fugitives);
            Assert.Equal(1, player.Roster.Count);
            Assert.Equal(CutterAdjacent, player.SectorId);
            Assert.Null(game.PendingEncounter);
            Assert.Empty(game.PendingNavDraws);
        }

        [Fact]
        public void Reaver_Contact_successful_fight_kills_one_crew()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var catalog = CrewCatalog.LoadDefault();
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            var player = new PlayerState("p1", "Mal", CutterStart) { FightBonus = 2 };
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_zoe"), out _));
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.ReaverCutter;
            game.PendingEncounterSectorId = CutterStart;

            Assert.True(ReaverContact.TryResolve(
                game,
                ScriptedRng.FromDieFaces(6, 6),
                CutterAdjacent,
                out var result,
                out _));
            Assert.True(result!.Fight.Success);
            Assert.Equal(1, result.CrewKilled);
            Assert.Equal(1, player.Roster.Count);
            Assert.Equal(CutterAdjacent, player.SectorId);
        }

        [Fact]
        public void Reaver_Cutter_Nav_moves_cutter_resolves_Contact_and_Evades()
        {
            var (game, resolver, player) = BorderGameWithCutter(queuedDraws: 2);
            game.Decks!.Border.PlaceOnTop(game.Decks.Catalog.Get("nav_reaver-cutter"));
            player.Passengers = 1;
            player.Fugitives = 1;
            player.FightBonus = 0;
            var catalog = CrewCatalog.LoadDefault();
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_zoe"), out _));

            resolver.DrawNext(game);
            var choice = new NavResolveChoice { EvadeToSectorId = CutterAdjacent };
            Assert.True(resolver.TryResolve(
                game,
                0,
                out var resolution,
                out var error,
                ScriptedRng.FromDieFaces(),
                choice), error);

            Assert.Equal(FlightOutcome.Evade, resolution!.Outcome);
            Assert.True(resolution.Stopped);
            Assert.NotNull(resolution.ReaverContact);
            Assert.Equal(CutterStart, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Equal(CutterAdjacent, player.SectorId);
            Assert.Equal(0, player.Passengers);
            Assert.Equal(0, player.Fugitives);
            Assert.Empty(game.PendingNavDraws);
            Assert.Null(game.PendingEncounter);
        }

        [Fact]
        public void Reaver_Cutter_Crazy_Ivan_moves_cutter_spends_fuel_and_Evades_without_Contact()
        {
            var (game, resolver, player) = BorderGameWithCutter(queuedDraws: 2);
            game.Decks!.Border.PlaceOnTop(game.Decks.Catalog.Get("nav_reaver-cutter"));
            player.Fuel = 3;
            var catalog = CrewCatalog.LoadDefault();
            Assert.True(player.Roster.TryHire(catalog.Get("crew_wash"), out _)); // Pilot
            Assert.True(player.Roster.TryHire(catalog.Get("crew_kaylee"), out _)); // Mechanic
            var crewBefore = player.Roster.Count;

            resolver.DrawNext(game);
            var choice = new NavResolveChoice { EvadeToSectorId = CutterAdjacent };
            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var error, choice: choice), error);
            Assert.Equal(FlightOutcome.Evade, resolution!.Outcome);
            Assert.Null(resolution.ReaverContact);
            Assert.Equal(2, player.Fuel);
            Assert.Equal(crewBefore, player.Roster.Count);
            Assert.Equal(CutterStart, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Equal(CutterAdjacent, player.SectorId);
            Assert.Empty(game.PendingNavDraws);
        }

        [Fact]
        public void Reavers_on_the_Hunt_moves_cutter_adjacent_without_Contact()
        {
            var (game, resolver, player) = BorderGameWithCutter(queuedDraws: 1);
            // Place cutter next door; Hunt moves it onto the player sector — no Contact yet.
            game.Tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterAdjacent });
            game.Decks!.Border.PlaceOnTop(game.Decks.Catalog.Get("nav_reavers-on-the-hunt"));

            var choice = new NavResolveChoice { ReaverCutterToSectorId = CutterStart };
            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error, choice: choice), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.False(resolution.Stopped);
            Assert.Null(resolution.ReaverContact);
            Assert.Null(game.PendingEncounter);
            Assert.Equal(CutterStart, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Equal(CutterStart, player.SectorId);
        }

        [Fact]
        public void Evade_Nav_outcome_moves_adjacent_and_clears_remaining_draws()
        {
            var (game, resolver, player) = BorderGameWithCutter(queuedDraws: 2);
            game.Decks!.Border.PlaceOnTop(game.Decks.Catalog.Get("nav_reaver-bait"));
            resolver.DrawNext(game);
            var choice = new NavResolveChoice
            {
                EvadeToSectorId = CutterAdjacent,
                ReaverCutterToSectorId = CutterStart
            };
            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var error, choice: choice), error);
            Assert.Equal(FlightOutcome.Evade, resolution!.Outcome);
            Assert.True(resolution.Stopped);
            Assert.Null(resolution.ReaverContact);
            Assert.Equal(CutterAdjacent, player.SectorId);
            Assert.Equal(CutterStart, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Empty(game.PendingNavDraws);
        }

        private static (GameState Game, NavResolver Resolver, PlayerState Player) BorderGameWithCutter(int queuedDraws)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", CutterStart, fuel: 3);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterAdjacentAlt });
            var game = new GameState(map, new[] { player }, tokens, decks);
            for (var i = 0; i < queuedDraws; i++)
                game.PendingNavDraws.Add(new PendingNavDraw(CutterStart, NavRegion.Border));
            return (game, new NavResolver(), player);
        }
    }
}
