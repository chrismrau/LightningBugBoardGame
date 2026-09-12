using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class NavTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";

        private static string NavPath => GameData.NavCardsPath;
        private static string MapDir => GameData.MapDirectory;

        [Fact]
        public void Regional_decks_each_have_sixty_cards()
        {
            var decks = NavCatalog.BuildDecks(NavPath, new SystemRng(1));
            Assert.Equal(60, decks.Alliance.DrawCount);
            Assert.Equal(60, decks.Border.DrawCount);
            Assert.Equal(60, decks.Rim.DrawCount);
        }

        [Fact]
        public void Big_Black_is_keep_flying()
        {
            var catalog = NavCatalog.LoadFromFile(NavPath);
            var card = catalog.Get("nav_the-big-black");
            Assert.Equal(FlightOutcome.KeepFlying, card.Options[0].Outcome);
            Assert.False(card.IsReshuffle);
        }

        [Fact]
        public void Alliance_Cruiser_is_full_stop_reshuffle()
        {
            var catalog = NavCatalog.LoadFromFile(NavPath);
            var card = catalog.Get("nav_alliance-cruiser");
            Assert.True(card.IsReshuffle);
            Assert.Equal(FlightOutcome.FullStop, card.Options[0].Outcome);
        }

        [Fact]
        public void Keep_Flying_consumes_one_draw_and_leaves_the_ship()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_the-big-black"));

            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error));
            Assert.Null(error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.False(resolution.Stopped);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Single(game.PendingNavDraws);
        }

        [Fact]
        public void Full_Stop_rewinds_to_that_sector_and_cancels_remaining_draws()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            player.SectorId = "rim-blue-sun-r3-01";
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-cruiser"));

            Assert.True(resolver.TryAutoResolve(game, out var resolution, out _));
            Assert.True(resolution!.Stopped);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Empty(game.PendingNavDraws);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.Equal(Pelorum, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Reshuffle_card_returns_discard_to_the_draw_pile()
        {
            var decks = NavCatalog.BuildDecks(NavPath, new SystemRng(2));
            decks.Alliance.PlaceOnTop(decks.Catalog.Get("nav_alliance-cruiser"));
            var afterPlace = decks.Alliance.DrawCount;
            var card = decks.Alliance.Draw();
            Assert.Equal("nav_alliance-cruiser", card.Id);
            decks.Alliance.ResolveIntoDiscard(card);
            Assert.Equal(0, decks.Alliance.DiscardCount);
            Assert.Equal(afterPlace, decks.Alliance.DrawCount);
        }

        [Fact]
        public void Multi_option_card_stays_face_up_until_chosen()
        {
            var (game, resolver, _) = GameWithQueuedDraws(1);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_ifn-the-coil-busts-were-driftin"));

            Assert.False(resolver.TryAutoResolve(game, out _, out var error));
            Assert.Contains("option choice", error);
            Assert.NotNull(resolver.FaceUp);
            Assert.True(resolver.TryResolve(game, 1, out var resolution, out _));
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Equal("Replace the Whole Damn Mess", resolution.Option.Name);
        }

        [Fact]
        public void Cruiser_Patrol_moves_one_Alliance_Sector_Keep_Flying_no_Contact()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_cruiser-patrol"));

            var choice = new NavResolveChoice { AllianceCruiserToSectorId = Bernadette };
            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error, choice: choice), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.False(resolution.Stopped);
            Assert.Equal(Bernadette, game.Tokens.AllianceCruiserSectorId);
            Assert.Null(game.PendingEncounter);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Single(game.PendingNavDraws);
        }

        [Fact]
        public void Cruiser_Patrol_rejects_non_adjacent_or_non_Alliance_destination()
        {
            var (game, resolver, _) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_cruiser-patrol"));

            Assert.False(resolver.TryAutoResolve(
                game,
                out _,
                out var farError,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = Pelorum }));
            Assert.Contains("1 Sector", farError);

            Assert.False(resolver.TryAutoResolve(
                game,
                out _,
                out var borderError,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = BorderNearPersephone }));
            Assert.Contains("Alliance Space", borderError);
        }

        [Fact]
        public void Alliance_Entanglements_Wild_Gosling_moves_to_empty_Alliance_Sector_no_Contact()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-entanglements"));

            var drawn = resolver.DrawNext(game);
            Assert.Equal("nav_alliance-entanglements", drawn.Card.Id);
            Assert.True(resolver.TryResolve(
                game,
                0,
                out var resolution,
                out var error,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = EmptyAllianceNearPelorum }), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.False(resolution.Stopped);
            Assert.Equal(EmptyAllianceNearPelorum, game.Tokens.AllianceCruiserSectorId);
            Assert.Null(game.PendingEncounter);
            Assert.Equal(Pelorum, player.SectorId);
        }

        [Fact]
        public void Alliance_Entanglements_Wild_Gosling_rejects_Firefly_occupied_Sector()
        {
            var (game, resolver, _) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-entanglements"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game,
                0,
                out _,
                out var error,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = Pelorum }));
            Assert.Contains("Firefly", error);
        }

        [Fact]
        public void Alliance_Entanglements_Legitimate_Tip_requires_Solid_Harken_pays_500()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.Contacts = ContactCatalog.LoadDefault();
            player.Warrants = 1; // Outlaw in Pelorum (destination gate runs before Requires)
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-entanglements"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game,
                1,
                out _,
                out var needSolid,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = Pelorum }));
            Assert.Contains("Solid Harken", needSolid);
            Assert.Equal(Londinium, game.Tokens.AllianceCruiserSectorId);

            player.BecomeSolid("contact_harken");
            Assert.True(resolver.TryResolve(
                game,
                1,
                out var resolution,
                out var error,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = Pelorum }), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Equal(Pelorum, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(500, player.Cash);
            Assert.Null(game.PendingEncounter);
        }

        [Fact]
        public void Alliance_Entanglements_Legitimate_Tip_rejects_Sector_without_Outlaw()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            player.BecomeSolid("contact_harken");
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-entanglements"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game,
                1,
                out _,
                out var error,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = EmptyAllianceNearPelorum }));
            Assert.Contains("Outlaw", error);
        }

        [Fact]
        public void Named_Alliance_Cruiser_still_snaps_and_queues_Contact()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            player.SectorId = "rim-blue-sun-r3-01";
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-cruiser"));

            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error), error);
            Assert.True(resolution!.Stopped);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.Equal(Pelorum, game.Tokens.AllianceCruiserSectorId);
        }

        private const string Londinium = "alliance-white-sun-r1-02";
        private const string Bernadette = "alliance-white-sun-r1-01";
        private const string EmptyAllianceNearPelorum = "alliance-white-sun-r3-02";
        private const string BorderNearPersephone = "border-space-r1-04";

        private static (GameState Game, NavResolver Resolver, PlayerState Player) GameWithQueuedDraws(int draws)
        {
            var map = SectorMap.LoadFromDirectory(MapDir);
            var decks = NavCatalog.BuildDecks(NavPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", Pelorum);
            var game = new GameState(map, new[] { player }, decks: decks);
            for (var i = 0; i < draws; i++)
                game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            return (game, new NavResolver(), player);
        }
    }
}
