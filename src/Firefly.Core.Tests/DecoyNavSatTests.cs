using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// Decoy Nav Sat Cluster: discard at Move start → Reaver/Alliance Ship Nav as Big Black.
    /// Supplies.tsv / ShipUpgrades.json.
    /// </summary>
    public class DecoyNavSatTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string Londinium = "alliance-white-sun-r1-02";
        private const string CutterStart = "border-space-r2-06";
        private const string CutterAdjacent = "border-space-r2-05";
        private const string Beaumonde = "border-georgia-r1-03";

        [Fact]
        public void FullBurn_with_Decoy_discards_upgrade_and_sets_active_flag()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1));
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            player.ShipUpgrades.Add(DecoyNavSatAction.CardId);
            var game = new GameState(map, new[] { player }, decks: decks);
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(
                fly.TryFullBurnTo(game, "p1", Pelorum, out _, out var error, useDecoyNavSat: true),
                error);
            Assert.True(game.DecoyNavSatActiveThisFly);
            Assert.DoesNotContain(DecoyNavSatAction.CardId, player.ShipUpgrades);
            Assert.True(game.ActionWasUsed(TurnAction.Fly));
        }

        [Fact]
        public void FullBurn_Decoy_rejects_when_upgrade_missing()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            var game = new GameState(map, new[] { player });
            var fly = new FlyAction(new MovementEngine(map));

            Assert.False(fly.TryFullBurnTo(game, "p1", Pelorum, out _, out var error, useDecoyNavSat: true));
            Assert.Contains("not installed", error);
            Assert.False(game.DecoyNavSatActiveThisFly);
            Assert.Equal(Persephone, player.SectorId);
            Assert.False(game.ActionWasUsed(TurnAction.Fly));
        }

        [Fact]
        public void Decoy_Alliance_Cruiser_Nav_keeps_flying_without_moving_Cruiser()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1));
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3) { Warrants = 2 };
            player.ShipUpgrades.Add(DecoyNavSatAction.CardId);
            var tokens = new MapTokens(allianceCruiserSectorId: Londinium);
            var game = new GameState(map, new[] { player }, tokens, decks);
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(
                fly.TryFullBurnTo(game, "p1", Pelorum, out _, out var flyErr, useDecoyNavSat: true),
                flyErr);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-cruiser"));
            // Replace queued draw sector with Alliance space for this card.
            game.PendingNavDraws.Clear();
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var error), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.False(resolution.Stopped);
            Assert.Equal(Londinium, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(2, player.Warrants);
            Assert.Null(game.PendingEncounter);
            Assert.Null(resolver.FaceUp);
        }

        [Fact]
        public void Decoy_Cruiser_Patrol_skips_destination_and_keeps_Cruiser()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(2));
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            player.ShipUpgrades.Add(DecoyNavSatAction.CardId);
            var tokens = new MapTokens(allianceCruiserSectorId: Londinium);
            var game = new GameState(map, new[] { player }, tokens, decks);
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(
                fly.TryFullBurnTo(game, "p1", Pelorum, out _, out var flyErr, useDecoyNavSat: true),
                flyErr);
            game.PendingNavDraws.Clear();
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_cruiser-patrol"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            // Without Decoy this would suspend SectorDestination for player-to-the-right.
            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var error), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Null(game.PendingChoice);
            Assert.Equal(Londinium, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Decoy_Reaver_Cutter_Nav_does_not_move_cutter_or_Contact()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            // Start one Border hop from CutterAdjacent so Mosey is a legal Move.
            var player = new PlayerState("p1", "Mal", CutterStart, fuel: 3);
            player.ShipUpgrades.Add(DecoyNavSatAction.CardId);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { Beaumonde });
            var game = new GameState(map, new[] { player }, tokens, decks);
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(
                fly.TryMosey(game, "p1", CutterAdjacent, out _, out var moseyErr, useDecoyNavSat: true),
                moseyErr);
            Assert.True(game.DecoyNavSatActiveThisFly);
            Assert.DoesNotContain(DecoyNavSatAction.CardId, player.ShipUpgrades);

            game.PendingNavDraws.Clear();
            game.PendingNavDraws.Add(new PendingNavDraw(CutterAdjacent, NavRegion.Border));
            game.Decks!.Border.PlaceOnTop(game.Decks.Catalog.Get("nav_reaver-cutter"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var error), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Null(resolution.ReaverContact);
            Assert.Equal(Beaumonde, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Null(game.PendingEncounter);
        }

        [Fact]
        public void Decoy_Operative_Corvette_Nav_keeps_flying_without_moving_Corvette()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(4));
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            player.ShipUpgrades.Add(DecoyNavSatAction.CardId);
            var tokens = new MapTokens(
                allianceCruiserSectorId: Londinium,
                operativeCorvetteSectorId: Londinium);
            var game = new GameState(map, new[] { player }, tokens, decks);
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(
                fly.TryFullBurnTo(game, "p1", Pelorum, out _, out var flyErr, useDecoyNavSat: true),
                flyErr);
            game.PendingNavDraws.Clear();
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_persistent-pursuit"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var error), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Null(game.PendingChoice);
            Assert.Equal(Londinium, game.Tokens.OperativeCorvetteSectorId);
        }

        [Fact]
        public void Decoy_inactive_Alliance_Cruiser_still_snaps_and_Contacts()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(5));
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3) { Warrants = 1 };
            var tokens = new MapTokens(allianceCruiserSectorId: Londinium);
            var game = new GameState(map, new[] { player }, tokens, decks);
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-cruiser"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var error), error);
            Assert.Equal(Pelorum, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.True(resolution!.Stopped);
        }

        [Fact]
        public void Decoy_flag_clears_on_EndTurn()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            player.ShipUpgrades.Add(DecoyNavSatAction.CardId);
            var game = new GameState(map, new[] { player });
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(
                fly.TryMosey(game, "p1", Pelorum, out _, out var error, useDecoyNavSat: true),
                error);
            Assert.True(game.DecoyNavSatActiveThisFly);
            game.EndTurn();
            Assert.False(game.DecoyNavSatActiveThisFly);
        }

        [Fact]
        public void IsReaverOrAllianceShipMover_matches_types()
        {
            var catalog = NavCatalog.LoadFromFile(GameData.NavCardsPath);
            Assert.True(DecoyNavSatAction.IsReaverOrAllianceShipMover(catalog.Get("nav_alliance-cruiser")));
            Assert.True(DecoyNavSatAction.IsReaverOrAllianceShipMover(catalog.Get("nav_cruiser-patrol")));
            Assert.True(DecoyNavSatAction.IsReaverOrAllianceShipMover(catalog.Get("nav_alliance-entanglements")));
            Assert.True(DecoyNavSatAction.IsReaverOrAllianceShipMover(catalog.Get("nav_reaver-bait")));
            Assert.True(DecoyNavSatAction.IsReaverOrAllianceShipMover(catalog.Get("nav_reaver-cutter")));
            Assert.True(DecoyNavSatAction.IsReaverOrAllianceShipMover(catalog.Get("nav_persistent-pursuit")));
            Assert.True(DecoyNavSatAction.IsReaverOrAllianceShipMover(catalog.Get("nav_hell-come-at-you-sideways")));
            Assert.False(DecoyNavSatAction.IsReaverOrAllianceShipMover(catalog.Get("nav_the-big-black")));
            Assert.False(DecoyNavSatAction.IsReaverOrAllianceShipMover(catalog.Get("nav_a-rogue-trader")));
        }
    }
}
