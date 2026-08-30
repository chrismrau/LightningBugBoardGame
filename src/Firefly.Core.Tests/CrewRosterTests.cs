using Firefly.Core.Cards;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class CrewRosterTests
    {
        private static CrewCatalog Catalog => CrewCatalog.LoadDefault();

        [Fact]
        public void Catalog_loads_named_crew()
        {
            var kaylee = Catalog.FindByName("Kaylee");
            Assert.NotNull(kaylee);
            Assert.Equal(3, kaylee!.Tech);
            Assert.True(kaylee.Moral);
            Assert.False(kaylee.Wanted);
            Assert.True(kaylee.HasProfession("Mechanic"));
        }

        [Fact]
        public void Roster_sums_skills_and_flags()
        {
            var catalog = Catalog;
            var player = new PlayerState("p1", "Mal", "alliance-lux-r1-01");
            Assert.True(player.Roster.TryHire(catalog.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_inara"), out _));
            Assert.Equal(2, player.Fight);
            Assert.Equal(3, player.Tech);
            Assert.Equal(3, player.Talk);
            Assert.Equal(1, player.WantedCrew);
            Assert.Equal(2, player.Roster.MoralCount);
            Assert.True(player.Roster.HasProfession("Mechanic"));
            Assert.True(player.Roster.HasProfession("Merc"));
            Assert.False(player.Roster.HasProfession("Pilot"));
        }

        [Fact]
        public void Leader_bonus_adds_to_crew_totals()
        {
            var player = new PlayerState("p1", "Mal", "alliance-lux-r1-01") { FightBonus = 2, TalkBonus = 1 };
            Assert.True(player.Roster.TryHire(Catalog.Get("crew_zoe"), out _));
            Assert.Equal(4, player.Fight);
            Assert.Equal(1, player.Talk);
        }

        [Fact]
        public void Hire_fails_when_ship_is_full()
        {
            var roster = new CrewRoster(maxCrew: 1);
            Assert.True(roster.TryHire(Catalog.Get("crew_kaylee"), out _));
            Assert.False(roster.TryHire(Catalog.Get("crew_jayne"), out var error));
            Assert.Contains("full", error);
        }

        [Fact]
        public void Remove_wanted_drops_that_crew_from_the_ship()
        {
            var roster = new CrewRoster();
            roster.TryHire(Catalog.Get("crew_kaylee"), out _);
            roster.TryHire(Catalog.Get("crew_jayne"), out _);
            var removed = roster.RemoveFirstWanted();
            Assert.Equal("crew_jayne", removed!.Id);
            Assert.Equal(1, roster.Count);
            Assert.Equal(0, roster.WantedCount);
            Assert.Equal(3, roster.Tech);
        }
    }
}
