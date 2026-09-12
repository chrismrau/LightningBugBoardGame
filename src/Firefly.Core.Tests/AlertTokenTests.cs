using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class AlertTokenTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string CutterStart = "border-space-r2-06";
        private const string CutterAdjacent = "border-space-r2-05";
        private const string CutterAdjacentAlt = "border-space-r2-07";
        private const string ReaverSpace = "rim-burnham-r2-01";

        [Fact]
        public void Moving_Reaver_Cutter_places_Reaver_Alert_Token_in_vacated_sector()
        {
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            Assert.True(tokens.TryMoveReaverCutter(
                CutterAdjacent,
                out var updated,
                out var error,
                leaveReaverAlertToken: true), error);
            Assert.Equal(CutterAdjacent, updated.ReaverCutterSectorIds[0]);
            Assert.Equal(1, updated.RemovableAlertCount(CutterStart, AlertTokenKind.Reaver));
            Assert.Equal(0, updated.RemovableAlertCount(CutterAdjacent, AlertTokenKind.Reaver));
        }

        [Fact]
        public void Multiple_Reaver_moves_stack_Alert_Tokens()
        {
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            Assert.True(tokens.TryMoveReaverCutter(CutterAdjacent, out tokens, out _, leaveReaverAlertToken: true));
            Assert.True(tokens.TryMoveReaverCutter(CutterStart, out tokens, out _, leaveReaverAlertToken: true));
            Assert.True(tokens.TryMoveReaverCutter(CutterAdjacent, out tokens, out _, leaveReaverAlertToken: true));
            Assert.Equal(2, tokens.RemovableAlertCount(CutterStart, AlertTokenKind.Reaver));
            Assert.Equal(1, tokens.RemovableAlertCount(CutterAdjacent, AlertTokenKind.Reaver));
        }

        [Fact]
        public void Permanent_Reaver_Space_counts_plus_removable_and_is_never_cleared()
        {
            Assert.True(AlertTokenRules.HasPermanentReaverAlert(ReaverSpace));
            var tokens = new MapTokens().PlaceAlertToken(ReaverSpace, AlertTokenKind.Reaver, 2);
            Assert.Equal(3, AlertTokenRules.EffectiveCount(
                tokens, ReaverSpace, AlertTokenKind.Reaver, includePermanentReaverSpace: true));

            tokens = tokens.ClearRemovableAlerts(ReaverSpace);
            Assert.Equal(0, tokens.RemovableAlertCount(ReaverSpace, AlertTokenKind.Reaver));
            Assert.Equal(1, AlertTokenRules.EffectiveCount(
                tokens, ReaverSpace, AlertTokenKind.Reaver, includePermanentReaverSpace: true));
        }

        [Fact]
        public void Fly_into_Alert_Tokens_queues_resolution_before_Nav()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens().PlaceAlertToken(Pelorum, AlertTokenKind.Reaver, 2);
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            var game = new GameState(map, new[] { player }, tokens) { UseAlertTokens = true };
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(fly.TryFullBurnTo(game, "p1", Pelorum, out var result, out var error), error);
            Assert.Contains(Pelorum, game.PendingAlertSectors);
            Assert.Single(game.PendingNavDraws);
            Assert.True(NavResolver.MustResolveAlertsBeforeNav(game));
            Assert.True(game.HasPendingEvents);
        }

        [Fact]
        public void Mosey_into_Alert_Tokens_queues_resolution_without_Nav()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens().PlaceAlertToken(Pelorum, AlertTokenKind.Reaver, 1);
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            var game = new GameState(map, new[] { player }, tokens) { UseAlertTokens = true };
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(fly.TryMosey(game, "p1", Pelorum, out _, out var error), error);
            Assert.Equal(Pelorum, Assert.Single(game.PendingAlertSectors));
            Assert.Empty(game.PendingNavDraws);
        }

        [Fact]
        public void Resolving_Reaver_Alert_success_moves_Cutter_leaves_token_clears_alerts_defers_Contact()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterAdjacentAlt })
                .PlaceAlertToken(CutterStart, AlertTokenKind.Reaver, 3);
            var player = new PlayerState("p1", "Mal", CutterStart, fuel: 3);
            var game = new GameState(map, new[] { player }, tokens) { UseAlertTokens = true };
            game.PendingAlertSectors.Add(CutterStart);
            game.PendingNavDraws.Add(new PendingNavDraw(CutterStart, NavRegion.Border));

            // Die 2 ≤ 3 → Reavers arrive
            Assert.True(AlertTokenResolver.TryResolvePending(
                game,
                ScriptedRng.FromDieFaces(2),
                out var resolution,
                out var error,
                new AlertResolveChoice { ReaverCutterIndex = 0 }), error);

            Assert.False(resolution!.EndedFly);
            Assert.True(Assert.Single(resolution.Rolls).ShipArrived);
            Assert.Equal(TokenKind.ReaverCutter, resolution.Rolls[0].ArrivedShip);
            Assert.Equal(CutterStart, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Equal(1, game.Tokens.RemovableAlertCount(CutterAdjacentAlt, AlertTokenKind.Reaver));
            Assert.Equal(0, game.Tokens.RemovableAlertCount(CutterStart, AlertTokenKind.Reaver));
            Assert.Empty(game.PendingAlertSectors);
            Assert.Null(game.PendingEncounter);
            Assert.Single(game.PendingNavDraws);
            Assert.False(NavResolver.MustResolveAlertsBeforeNav(game));
        }

        [Fact]
        public void Resolving_Reaver_Alert_failure_still_removes_tokens()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterAdjacent })
                .PlaceAlertToken(CutterStart, AlertTokenKind.Reaver, 1);
            var player = new PlayerState("p1", "Mal", CutterStart);
            var game = new GameState(map, new[] { player }, tokens) { UseAlertTokens = true };
            game.PendingAlertSectors.Add(CutterStart);

            // Die 6 > 1 → no ship
            Assert.True(AlertTokenResolver.TryResolvePending(
                game,
                ScriptedRng.FromDieFaces(6),
                out var resolution,
                out var error), error);

            Assert.False(resolution!.Rolls[0].ShipArrived);
            Assert.Equal(CutterAdjacent, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Equal(0, game.Tokens.RemovableAlertCount(CutterStart, AlertTokenKind.Reaver));
        }

        [Fact]
        public void Alliance_Alert_on_Outlaw_ends_Fly_and_sets_Cruiser_encounter()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(allianceCruiserSectorId: Persephone)
                .PlaceAlertToken(Pelorum, AlertTokenKind.Alliance, 2);
            var player = new PlayerState("p1", "Mal", Pelorum, fuel: 3) { Warrants = 1 };
            var game = new GameState(map, new[] { player }, tokens) { UseAlertTokens = true };
            game.PendingAlertSectors.Add(Pelorum);
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.PendingNavDraws.Add(new PendingNavDraw(Persephone, NavRegion.Alliance));

            Assert.True(AlertTokenResolver.TryResolvePending(
                game,
                ScriptedRng.FromDieFaces(1),
                out var resolution,
                out var error), error);

            Assert.True(resolution!.EndedFly);
            Assert.Equal(Pelorum, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.Equal(Pelorum, game.PendingEncounterSectorId);
            Assert.Empty(game.PendingNavDraws);
            Assert.Empty(game.PendingAlertSectors);
        }

        [Fact]
        public void Nav_Reaver_Booby_Trap_places_Reaver_Token()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", CutterStart, fuel: 3);
            var game = new GameState(map, new[] { player }, MapTokens.None, decks) { UseAlertTokens = true };
            game.PendingNavDraws.Add(new PendingNavDraw(CutterStart, NavRegion.Rim));
            game.Decks!.Rim.PlaceOnTop(game.Decks.Catalog.Get("nav_reaver-booby-trap"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var error), error);
            Assert.Equal(FlightOutcome.FullStop, resolution!.Outcome);
            Assert.Equal(1, game.Tokens.RemovableAlertCount(CutterStart, AlertTokenKind.Reaver));
        }

        [Fact]
        public void Nav_Reaver_Cutter_move_with_AlertTokens_leaves_token_in_vacated_sector()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", CutterStart, fuel: 3);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterAdjacentAlt });
            var game = new GameState(map, new[] { player }, tokens, decks) { UseAlertTokens = true };
            game.PendingNavDraws.Add(new PendingNavDraw(CutterStart, NavRegion.Border));
            game.Decks!.Border.PlaceOnTop(game.Decks.Catalog.Get("nav_reavers-on-the-hunt"));

            var resolver = new NavResolver();
            var choice = new NavResolveChoice { ReaverCutterToSectorId = CutterStart };
            Assert.True(resolver.TryAutoResolve(game, out _, out var error, choice: choice), error);
            Assert.Equal(CutterStart, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Equal(1, game.Tokens.RemovableAlertCount(CutterAdjacentAlt, AlertTokenKind.Reaver));
        }

        [Fact]
        public void BlueSun_setup_enables_Alert_Tokens()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    UseBlueSun = true,
                    DealStartingJobs = false,
                    Rng = new SystemRng(1)
                });
            Assert.True(game.UseAlertTokens);
            Assert.Equal(3, game.Tokens.ReaverCutterSectorIds.Count);
        }

        [Fact]
        public void DrawNext_throws_when_Alert_Tokens_must_be_resolved_first()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", Pelorum);
            var game = new GameState(map, new[] { player }, MapTokens.None, decks) { UseAlertTokens = true };
            game.PendingAlertSectors.Add(Pelorum);
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));

            var resolver = new NavResolver();
            Assert.Throws<System.InvalidOperationException>(() => resolver.DrawNext(game));
        }

        [Fact]
        public void Permanent_Reaver_Space_alone_still_requires_resolution_on_Fly()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            // Path into Reaver Space from a neighbor without a Cutter sitting on the destination.
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            Assert.True(map.TryGet(ReaverSpace, out _));
            string? from = null;
            foreach (var neighbor in map.Neighbors(ReaverSpace))
            {
                from = neighbor;
                break;
            }
            Assert.False(string.IsNullOrEmpty(from));
            // If from is another permanent Reaver Space sector that also has alerts, Mosey still queues.
            var player = new PlayerState("p1", "Mal", from!, fuel: 3);
            var game = new GameState(map, new[] { player }, tokens) { UseAlertTokens = true };
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(fly.TryMosey(game, "p1", ReaverSpace, out _, out var error), error);
            Assert.Equal(ReaverSpace, Assert.Single(game.PendingAlertSectors));
            Assert.Equal(
                1,
                AlertTokenRules.EffectiveCount(
                    game.Tokens, ReaverSpace, AlertTokenKind.Reaver, includePermanentReaverSpace: true));
        }
    }
}
