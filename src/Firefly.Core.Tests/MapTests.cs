using Firefly.Core.Data;
using Firefly.Core.Map;
using Xunit;

namespace Firefly.Core.Tests
{
    public class MapTests
    {
        private static SectorMap LoadMap()
        {
            return SectorMap.LoadFromDirectory(GameData.MapDirectory);
        }

        [Fact]
        public void Loads_all_sectors_and_edges()
        {
            var map = LoadMap();
            Assert.Equal(155, map.SectorCount);
            Assert.Equal(397, map.EdgeCount);
        }

        [Fact]
        public void Persephone_and_Pelorum_are_adjacent()
        {
            var map = LoadMap();
            var path = new Pathfinder(map);
            Assert.Contains("alliance-lux-r1-02", path.Neighbors("alliance-lux-r1-01"));
            Assert.True(path.CanMosey("alliance-lux-r1-01", "alliance-lux-r1-02"));
            Assert.Equal(1, path.Distance("alliance-lux-r1-01", "alliance-lux-r1-02"));
        }

        [Fact]
        public void Motherlode_and_Uroboros_are_multi_sector_destinations()
        {
            var map = LoadMap();
            Assert.True(map.SatisfiesDestination("border-red-sun-r1-01", "Motherlode"));
            Assert.True(map.SatisfiesDestination("border-red-sun-r1-02", "Motherlode"));
            Assert.True(map.SatisfiesDestination("rim-blue-sun-r3-01", "Uroboros Belt"));
            Assert.False(map.SatisfiesDestination("alliance-lux-r1-01", "Motherlode"));
        }

        [Fact]
        public void Planet_name_resolves_to_sector()
        {
            var map = LoadMap();
            Assert.True(map.TryResolveName("Persephone", out var persephone));
            Assert.Equal("alliance-lux-r1-01", persephone.Id);
            Assert.True(map.SatisfiesDestination(persephone.Id, "Persephone"));
        }

        [Fact]
        public void Nav_region_follows_sector_region()
        {
            var map = LoadMap();
            Assert.Equal(NavRegion.Alliance, map.NavDeckFor("alliance-lux-r1-01"));
            Assert.Equal(NavRegion.Border, map.NavDeckFor("border-georgia-r2-01"));
            Assert.Equal(NavRegion.Rim, map.NavDeckFor("rim-blue-sun-r3-01"));
        }
    }
}
