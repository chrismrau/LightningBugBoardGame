using System.Collections.Generic;
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
    /// Hull-Mounted Flak Gun, Reaver-Flage, and Blue Sun Desperate Times Mosey.
    /// GF9 p.8 / Blue Sun Desperate Times / Supplies.tsv card text.
    /// </summary>
    public class FlakReaverFlageTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string CutterStart = "border-space-r2-06";
        private const string CutterAdjacent = "border-space-r2-05";

        [Fact]
        public void BlueSun_Mosey_allows_entering_cutter_sector_FullBurn_still_blocked()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { Pelorum });
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            var game = new GameState(map, new[] { player }, tokens) { UseBlueSun = true };
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(fly.TryMosey(game, "p1", Pelorum, out _, out var moseyErr), moseyErr);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Null(game.PendingEncounter); // Contact is start-of-turn, not mid-Fly

            // Fresh turn: Blue Sun still blocks Full Burn into occupied without Flage.
            player.SectorId = Persephone;
            game.EndTurn();
            Assert.False(fly.TryFullBurnTo(game, "p1", Pelorum, out _, out var burnErr));
            Assert.Contains("Reaver Cutter", burnErr);
        }

        [Fact]
        public void ReaverFlage_allows_FullBurn_and_Evade_into_cutter_sector()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { Pelorum });
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            player.ShipUpgrades.Add(ReaverEntryRules.FlageCardId);
            var game = new GameState(map, new[] { player }, tokens);
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(fly.TryFullBurnTo(game, "p1", Pelorum, out _, out var burnErr), burnErr);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Null(game.PendingEncounter);

            player.SectorId = Persephone;
            Assert.True(FlightEvade.CanMove(game, player, Pelorum, out var evadeErr), evadeErr);
        }

        [Fact]
        public void Flak_deploys_clears_pending_Contact_moves_cutter_discards_upgrade()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            var player = new PlayerState("p1", "Mal", CutterStart);
            player.ShipUpgrades.Add(FlakGunAction.CardId);
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.ReaverCutter;
            game.PendingEncounterSectorId = CutterStart;
            game.PendingEncounterPlayerId = player.Id;

            Assert.True(FlakGunAction.TryDeploy(game, "p1", CutterAdjacent, out var error), error);
            Assert.Null(game.PendingEncounter);
            Assert.Equal(CutterAdjacent, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.DoesNotContain(FlakGunAction.CardId, player.ShipUpgrades);
            Assert.Equal(CutterStart, player.SectorId);
        }

        [Fact]
        public void Flak_rejects_Alliance_destination_and_missing_upgrade()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            // Place cutter on Border near Alliance so Alliance neighbor exists as illegal dest.
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { "border-space-r1-04" });
            var player = new PlayerState("p1", "Mal", "border-space-r1-04");
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.ReaverCutter;

            Assert.False(FlakGunAction.TryDeploy(game, "p1", Persephone, out var missing));
            Assert.Contains("not installed", missing);

            player.ShipUpgrades.Add(FlakGunAction.CardId);
            Assert.False(FlakGunAction.TryDeploy(game, "p1", Persephone, out var alliance));
            Assert.Contains("Rim or Border", alliance);
            Assert.Contains(FlakGunAction.CardId, player.ShipUpgrades);
        }

        [Fact]
        public void Flak_omitted_destination_suspends_SectorDestination()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            var player = new PlayerState("p1", "Mal", CutterStart);
            player.ShipUpgrades.Add(FlakGunAction.CardId);
            var game = new GameState(map, new[] { player }, tokens);

            Assert.False(FlakGunAction.TryDeploy(game, "p1", null, out var err));
            Assert.Equal(PendingChoiceKinds.SectorDestination, game.PendingChoice!.Kind);
            Assert.Equal(SectorDestinationContexts.FlakGun, game.PendingChoice.ContextId);
            Assert.Contains(FlakGunAction.CardId, player.ShipUpgrades);

            Assert.True(
                FlakGunAction.TryResumeDestination(
                    game,
                    new ChoiceSubmission { SelectedOptionId = CutterAdjacent },
                    out var resumeErr),
                resumeErr);
            Assert.Equal(CutterAdjacent, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.DoesNotContain(FlakGunAction.CardId, player.ShipUpgrades);
        }

        [Fact]
        public void ReaverFlage_discards_after_Contact_kills_Crew_not_passengers_only()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var catalog = CrewCatalog.LoadDefault();
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            var player = new PlayerState("p1", "Mal", CutterStart)
            {
                Passengers = 2,
                FightBonus = 99
            };
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_zoe"), out _));
            player.ShipUpgrades.Add(ReaverEntryRules.FlageCardId);
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.ReaverCutter;
            game.PendingEncounterSectorId = CutterStart;

            // Fight succeeds → Kill 1 Crew
            Assert.True(ReaverContact.TryResolve(
                game,
                ScriptedRng.FromDieFaces(6),
                CutterAdjacent,
                out var result,
                out var error,
                new KillChoice { VictimCrewIds = new List<string> { "crew_zoe" } }), error);
            Assert.True(result!.CrewKilled >= 1);
            Assert.DoesNotContain(ReaverEntryRules.FlageCardId, player.ShipUpgrades);
        }

        [Fact]
        public void Core_without_BlueSun_still_blocks_Mosey_into_cutter()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { Pelorum });
            var player = new PlayerState("p1", "Mal", Persephone);
            var game = new GameState(map, new[] { player }, tokens); // UseBlueSun false
            var fly = new FlyAction(new MovementEngine(map));

            Assert.False(fly.TryMosey(game, "p1", Pelorum, out _, out var error));
            Assert.Contains("Reaver Cutter", error);
        }
    }
}
