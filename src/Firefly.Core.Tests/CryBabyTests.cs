using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class CryBabyTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string Londinium = "alliance-white-sun-r1-02";
        private const string Bernadette = "alliance-white-sun-r1-01";

        [Fact]
        public void Outlaw_enters_Cruiser_deploys_Cry_Baby_skips_Contact_restores_Nav()
        {
            // FAQ 4.1 p.14: Outlaw enters Cruiser Sector, deploys Cry Baby → Cruiser moved → Nav as normal.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1));
            var tokens = new MapTokens(allianceCruiserSectorId: Pelorum);
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3) { Warrants = 1 };
            player.ShipUpgrades.Add(CryBabyAction.CardId);
            var game = new GameState(map, new[] { player }, tokens, decks: decks);
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(fly.TryFullBurnTo(game, "p1", Pelorum, out var result, out var flyErr), flyErr);
            Assert.True(result!.StoppedForEncounter);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.True(game.PendingEncounterDeferredNav);
            Assert.Empty(game.PendingNavDraws);

            Assert.True(CryBabyAction.TryDeploy(game, "p1", Persephone, out var error), error);
            Assert.Null(game.PendingEncounter);
            Assert.False(game.PendingEncounterDeferredNav);
            Assert.Equal(Persephone, game.Tokens.AllianceCruiserSectorId);
            Assert.DoesNotContain(CryBabyAction.CardId, player.ShipUpgrades);
            Assert.Single(game.PendingNavDraws);
            Assert.Equal(Pelorum, game.PendingNavDraws[0].SectorId);

            var resolver = new NavResolver();
            Assert.NotNull(resolver.DrawNext(game));
        }

        [Fact]
        public void Cry_Baby_rejects_missing_upgrade_and_illegal_destination()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(allianceCruiserSectorId: Pelorum);
            var player = new PlayerState("p1", "Mal", Pelorum) { Warrants = 1 };
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.AllianceCruiser;
            game.PendingEncounterSectorId = Pelorum;
            game.PendingEncounterPlayerId = player.Id;

            Assert.False(CryBabyAction.TryDeploy(game, "p1", Persephone, out var missing));
            Assert.Contains("not installed", missing);

            player.ShipUpgrades.Add(CryBabyAction.CardId);
            Assert.False(CryBabyAction.TryDeploy(game, "p1", Londinium, out var far));
            Assert.Contains("1 Sector", far);
            Assert.Contains(CryBabyAction.CardId, player.ShipUpgrades);
        }

        [Fact]
        public void Cry_Baby_on_shared_Sector_clears_Contact_for_all_ships()
        {
            // FAQ 4.1 p.13–14: Cruiser enters a multi-ship Sector; one captain deploys Cry Baby →
            // no one resolves Alliance Contact.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var flyer = new PlayerState("p1", "Mal", Pelorum);
            var outlawA = new PlayerState("p2", "Zoe", Bernadette) { Warrants = 1, Cash = 2000 };
            var outlawB = new PlayerState("p3", "Wash", Bernadette) { Warrants = 1, Cash = 2000 };
            outlawA.ShipUpgrades.Add(CryBabyAction.CardId);
            var game = new GameState(map, new[] { flyer, outlawA, outlawB }, decks: decks);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_cruiser-patrol"));

            var resolver = new NavResolver();
            Assert.True(resolver.TryAutoResolve(
                game,
                out _,
                out var error,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = Bernadette }), error);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.Equal(2, 1 + game.PendingAllianceContactQueue.Count); // head + one queued

            Assert.True(CryBabyAction.TryDeploy(game, "p2", Londinium, out var cryErr), cryErr);
            Assert.Null(game.PendingEncounter);
            Assert.Empty(game.PendingAllianceContactQueue);
            Assert.Equal(Londinium, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(1, outlawA.Warrants);
            Assert.Equal(1, outlawB.Warrants);
        }
    }
}
