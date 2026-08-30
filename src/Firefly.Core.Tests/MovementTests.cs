using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Xunit;

namespace Firefly.Core.Tests
{
    public class MovementTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";

        private static MovementEngine Engine()
        {
            return new MovementEngine(SectorMap.LoadFromDirectory(GameData.MapDirectory));
        }

        [Fact]
        public void Mosey_enters_adjacent_sector_with_no_fuel_or_nav()
        {
            var engine = Engine();
            Assert.True(engine.TryMosey(Persephone, Pelorum, MapTokens.None, out var plan, out var error));
            Assert.Null(error);
            Assert.NotNull(plan);
            Assert.Equal(MovementKind.Mosey, plan!.Kind);
            Assert.Equal(1, plan.Distance);
            Assert.Equal(0, plan.FuelCost);
            Assert.Equal(0, plan.NavCardsToDraw);
            Assert.Single(plan.EnteredSteps);
            Assert.False(plan.EnteredSteps[0].DrawsNavCard);
            Assert.Equal(NavRegion.Alliance, plan.EnteredSteps[0].NavRegion);
        }

        [Fact]
        public void Mosey_rejects_same_sector_and_non_adjacent()
        {
            var engine = Engine();
            Assert.False(engine.TryMosey(Persephone, Persephone, MapTokens.None, out _, out var sameError));
            Assert.Contains("different sector", sameError);

            Assert.False(engine.TryMosey(Persephone, "rim-blue-sun-r3-01", MapTokens.None, out _, out var farError));
            Assert.Contains("not adjacent", farError);
        }

        [Fact]
        public void FullBurn_shortest_path_costs_one_fuel_and_draws_nav_per_hop()
        {
            var engine = Engine();
            Assert.True(engine.TryFullBurnTo(Persephone, Pelorum, driveRange: 5, MapTokens.None, out var plan, out var error));
            Assert.Null(error);
            Assert.NotNull(plan);
            Assert.Equal(MovementKind.FullBurn, plan!.Kind);
            Assert.Equal(1, plan.FuelCost);
            Assert.Equal(1, plan.NavCardsToDraw);
            Assert.True(plan.EnteredSteps[0].DrawsNavCard);
        }

        [Fact]
        public void FullBurn_rejects_path_longer_than_drive_range()
        {
            var engine = Engine();
            var path = engine.Pathfinder.ShortestPath(Persephone, "rim-blue-sun-r3-01");
            Assert.NotNull(path);
            Assert.True(path!.Count - 1 > 2);
            Assert.False(engine.TryFullBurn(path, driveRange: 2, MapTokens.None, out _, out var error));
            Assert.Contains("exceeds drive range", error);
        }

        [Fact]
        public void FullBurn_flags_cruiser_encounter_on_entered_sector()
        {
            var engine = Engine();
            var tokens = new MapTokens(allianceCruiserSectorId: Pelorum);
            Assert.True(engine.TryMosey(Persephone, Pelorum, tokens, out var plan, out _));
            Assert.Equal(TokenKind.AllianceCruiser, plan!.EnteredSteps[0].Encounter);
        }

        [Fact]
        public void FullBurn_destinations_exclude_origin()
        {
            var engine = Engine();
            var dests = engine.FullBurnDestinations(Persephone, 1);
            Assert.Contains(Pelorum, dests);
            Assert.DoesNotContain(Persephone, dests);
            Assert.Equal(engine.MoseyDestinations(Persephone).Count, dests.Count);
        }

        [Fact]
        public void PathsWithinRange_finds_direct_lux_hop()
        {
            var engine = Engine();
            var paths = engine.PathsWithinRange(Persephone, Pelorum, driveRange: 5);
            Assert.NotEmpty(paths);
            Assert.Contains(paths, p => p.Count == 2 && p[0] == Persephone && p[1] == Pelorum);
        }
    }
}
