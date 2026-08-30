using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class SkillAndEncounterTests
    {
        private const string Pelorum = "alliance-lux-r1-02";

        [Fact]
        public void Tech_8_keep_flying_on_success()
        {
            Assert.True(SkillCheck.TryParse("Tech 8 Breakdown; 1-7 Full Stop. 8+ Keep Flying.", out var check));
            var player = new PlayerState("p1", "Mal", Pelorum) { TechBonus = 2 };
            var result = check.Resolve(player, ScriptedRng.FromDieFaces(5, 3));
            Assert.Equal(8, result.Roll.Sum);
            Assert.True(result.Success);
        }

        [Fact]
        public void Zero_skill_dice_cannot_meet_a_target()
        {
            var check = new SkillCheck(Skill.Talk, 6);
            var player = new PlayerState("p1", "Mal", Pelorum);
            var result = check.Resolve(player, ScriptedRng.FromDieFaces());
            Assert.Equal(0, result.Roll.Sum);
            Assert.False(result.Success);
        }

        [Fact]
        public void Conditional_nav_option_keep_flying_when_test_succeeds()
        {
            var (game, resolver, player) = GameWithQueuedDraws();
            player.TechBonus = 2;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_ifn-the-coil-busts-were-driftin"));
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, 0, out var resolution, out _, ScriptedRng.FromDieFaces(6, 6)));
            Assert.True(resolution!.SkillCheck!.Success);
            Assert.Equal(FlightOutcome.KeepFlying, resolution.Outcome);
        }

        [Fact]
        public void Conditional_nav_option_full_stop_when_test_fails()
        {
            var (game, resolver, player) = GameWithQueuedDraws();
            player.TechBonus = 1;
            player.SectorId = "rim-blue-sun-r3-01";
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_ifn-the-coil-busts-were-driftin"));
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, 0, out var resolution, out _, ScriptedRng.FromDieFaces(1)));
            Assert.Equal(FlightOutcome.FullStop, resolution!.Outcome);
            Assert.Equal(Pelorum, player.SectorId);
        }

        [Fact]
        public void Cruiser_boarding_collects_fines_seizes_cargo_and_rolls_wanted()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var catalog = CrewCatalog.LoadDefault();
            var player = new PlayerState("p1", "Mal", Pelorum, cash: 2500)
            {
                Warrants = 2,
                Contraband = 3,
                Fugitives = 1
            };
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_zoe"), out _));
            var game = new GameState(map, new[] { player }, new MapTokens());
            game.PendingEncounter = TokenKind.AllianceCruiser;
            game.PendingEncounterSectorId = Pelorum;
            Assert.True(CruiserBoarding.TryResolve(game, ScriptedRng.FromDieFaces(1, 4), out var result, out _));
            Assert.Equal(2000, result!.FinePaid);
            Assert.Equal(1, result.WantedRemoved);
            Assert.Equal(1, player.WantedCrew);
            Assert.Equal(Pelorum, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Cruiser_fine_cannot_exceed_cash()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Pelorum, cash: 400) { Warrants = 1 };
            var game = new GameState(map, new[] { player });
            game.PendingEncounter = TokenKind.AllianceCruiser;
            game.PendingEncounterSectorId = Pelorum;
            Assert.True(CruiserBoarding.TryResolve(game, ScriptedRng.FromDieFaces(), out var result, out _));
            Assert.Equal(400, result!.FinePaid);
            Assert.Equal(0, player.Cash);
        }

        private static (GameState Game, NavResolver Resolver, PlayerState Player) GameWithQueuedDraws()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", Pelorum);
            var game = new GameState(map, new[] { player }, decks: decks);
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            return (game, new NavResolver(), player);
        }
    }
}
