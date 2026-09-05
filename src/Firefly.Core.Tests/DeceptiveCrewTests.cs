using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class DeceptiveCrewTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";

        private static CrewCatalog Crew => CrewCatalog.LoadDefault();

        private static (GameState Game, PlayerState Mal, PlayerState Zoe) TwoShips()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var mal = new PlayerState("p1", "Mal", Persephone);
            var zoe = new PlayerState("p2", "Zoe", Santo);
            var game = new GameState(map, new[] { mal, zoe });
            return (game, mal, zoe);
        }

        [Fact]
        public void Hiring_saffron_removes_bridgit_from_any_ship()
        {
            var (game, mal, zoe) = TwoShips();
            Assert.True(mal.Roster.TryHire(Crew.Get("crew_bridgit"), out _));
            Assert.True(zoe.Roster.TryHire(Crew.Get("crew_saffron"), out _));
            Assert.Equal(1, DeceptiveCrew.AfterHired(game, "Saffron"));
            Assert.Equal(0, mal.Roster.Count);
            Assert.True(zoe.Roster.HasName("Saffron"));
        }

        [Fact]
        public void Hiring_yolonda_removes_saffron_and_bridgit()
        {
            var (game, mal, zoe) = TwoShips();
            Assert.True(mal.Roster.TryHire(Crew.Get("crew_bridgit"), out _));
            Assert.True(zoe.Roster.TryHire(Crew.Get("crew_saffron"), out _));
            Assert.True(mal.Roster.TryHire(Crew.Get("crew_yolonda"), out _));
            Assert.Equal(2, DeceptiveCrew.AfterHired(game, "Yolonda"));
            Assert.False(mal.Roster.HasName("Bridgit"));
            Assert.False(zoe.Roster.HasName("Saffron"));
            Assert.True(mal.Roster.HasName("Yolonda"));
        }

        [Fact]
        public void Hiring_unrelated_crew_does_not_touch_the_trio()
        {
            var (game, mal, _) = TwoShips();
            Assert.True(mal.Roster.TryHire(Crew.Get("crew_bridgit"), out _));
            Assert.True(mal.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.Equal(0, DeceptiveCrew.AfterHired(game, "Kaylee"));
            Assert.True(mal.Roster.HasName("Bridgit"));
        }

        [Fact]
        public void Yolanda_spelling_matches_yolonda()
        {
            Assert.True(DeceptiveCrew.SamePerson("Yolanda", "Yolonda"));
            Assert.True(DeceptiveCrew.IsDeceptive("Yolanda"));
        }
    }
}
