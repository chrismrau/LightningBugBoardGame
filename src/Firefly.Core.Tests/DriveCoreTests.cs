using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class DriveCoreTests
    {
        private const string Persephone = "alliance-lux-r1-01";

        [Fact]
        public void Catalog_parses_printed_range_and_fuel_rules()
        {
            var cores = DriveCoreCatalog.LoadDefault();
            Assert.True(cores.TryResolve("Radion Accelerator Mark I", out var markI));
            Assert.Equal(5, markI.Range);
            Assert.True(markI.RequiresFuel);
            Assert.False(markI.Locked);

            Assert.True(cores.TryResolve("Echelon LR-8", out var echelon));
            Assert.Equal(8, echelon.Range);
            Assert.False(echelon.RequiresFuel);
            Assert.True(echelon.Locked);

            Assert.True(cores.TryResolve("Tri-Capissen 28HD", out var walden));
            Assert.Equal(4, walden.Range);
            Assert.True(walden.Locked);
        }

        [Fact]
        public void Starting_serenity_gets_mark_i_range_five()
        {
            var game = GameSetup.Standard(new PlayerSeat("p1", "Mal", Persephone, shipId: "Serenity"));
            Assert.Equal(5, game.CurrentPlayer.DriveRange);
            Assert.True(game.CurrentPlayer.FullBurnRequiresFuel);
            Assert.Contains("radion-accelerator-mark-i", game.CurrentPlayer.DriveCoreId);
        }

        [Fact]
        public void Interceptor_full_burns_without_fuel_at_range_eight()
        {
            var game = GameSetup.Standard(new PlayerSeat("p1", "Mal", Persephone, shipId: "Interceptor"));
            var player = game.CurrentPlayer;
            player.Fuel = 0;
            Assert.Equal(8, player.EffectiveDriveRange);
            Assert.False(player.FullBurnRequiresFuel);
            var fly = new FlyAction(new MovementEngine(game.Map));
            Assert.True(fly.TryFullBurnTo(game, "p1", "alliance-lux-r1-02", out _, out var error), error);
            Assert.Equal(0, player.Fuel);
        }

        [Fact]
        public void Full_burn_cannot_exceed_drive_range()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 6, driveRange: 2);
            var game = new GameState(map, new[] { player });
            var engine = new MovementEngine(map);
            var fly = new FlyAction(engine);
            string? dest = null;
            foreach (var id in engine.FullBurnDestinations(Persephone, 5))
            {
                var hops = engine.Pathfinder.Distance(Persephone, id);
                if (hops == 5)
                {
                    dest = id;
                    break;
                }
            }
            Assert.False(string.IsNullOrEmpty(dest));
            Assert.False(fly.TryFullBurnTo(game, "p1", dest!, out _, out var error));
            Assert.Contains("drive range", error);
        }

        [Fact]
        public void Interceptor_upgrade_reduces_effective_range()
        {
            var player = new PlayerState("p1", "Mal", Persephone, driveRange: 8);
            var ships = ShipCatalog.LoadDefault();
            Assert.True(ships.TryResolve("Interceptor", out var ship));
            player.ApplyShip(ship);
            player.DriveRange = 8;
            Assert.Equal(8, player.EffectiveDriveRange);
            player.ShipUpgrades.Add("upgrade_dummy");
            Assert.Equal(7, player.EffectiveDriveRange);
        }
    }
}
