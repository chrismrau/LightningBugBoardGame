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
    /// Electronic Defense Suite: no Tech boarding for rivals; spend 1 Fuel to Evade /
    /// ignore Reaver Cutter Nav or Contact. Supplies.tsv / ShipUpgrades.json.
    /// </summary>
    public class ElectronicDefenseSuiteTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string CutterStart = "border-space-r2-06";
        private const string CutterAdjacent = "border-space-r2-05";
        private const string Beaumonde = "border-georgia-r1-03";
        private const string ContractJumper = "job_amnon-duul_contract-jumper";

        private static (GameState Game, PlayerState Mal, PlayerState Zoe) TwoShips()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var mal = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 2);
            var zoe = new PlayerState("p2", "Zoe", Persephone, fuel: 2);
            var game = new GameState(map, new[] { mal, zoe })
            {
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                Crew = CrewCatalog.LoadDefault(),
                Leaders = LeaderCatalog.LoadDefault(),
                UsePiratesBountyHunters = true
            };
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1), BountyCatalog.LoadDefault());
            return (game, mal, zoe);
        }

        [Fact]
        public void Boarding_rejects_Tech_when_defender_has_EDS_allows_Negotiate()
        {
            var (game, mal, zoe) = TwoShips();
            zoe.ShipUpgrades.Add(ElectronicDefenseSuiteAction.CardId);
            mal.JobHand.Add(ContractJumper);
            mal.TechBonus = 5;
            mal.TalkBonus = 5;

            Assert.False(BoardingTest.IsAllowedSkill(Skill.Tech, zoe, out var techErr));
            Assert.Contains("Electronic Defense Suite", techErr);

            Assert.False(new PiracyAction().TryPirate(
                game, "p1", ContractJumper,
                new PiracyChoice
                {
                    RivalId = "p2",
                    BoardSkill = Skill.Tech,
                    AttackSkill = Skill.Fight,
                    DefendSkill = Skill.Talk
                },
                ScriptedRng.FromDieFaces(6, 6, 1),
                out _, out var pirateErr));
            Assert.Contains("Electronic Defense Suite", pirateErr);
            Assert.Null(mal.FindActive(ContractJumper));

            Assert.True(BoardingTest.IsAllowedSkill(Skill.Talk, zoe, out var talkErr), talkErr);
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", ContractJumper,
                new PiracyChoice
                {
                    RivalId = "p2",
                    BoardSkill = Skill.Talk,
                    AttackSkill = Skill.Fight,
                    DefendSkill = Skill.Talk
                },
                ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 1),
                out var result, out var okErr), okErr);
            Assert.False(result!.BoardingFailed);
        }

        [Fact]
        public void Boarding_Tech_still_allowed_without_EDS()
        {
            var (game, mal, _) = TwoShips();
            mal.JobHand.Add(ContractJumper);
            mal.TechBonus = 5;
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", ContractJumper,
                new PiracyChoice
                {
                    RivalId = "p2",
                    BoardSkill = Skill.Tech,
                    AttackSkill = Skill.Fight,
                    DefendSkill = Skill.Talk
                },
                ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 1),
                out var result, out var error), error);
            Assert.False(result!.BoardingFailed);
        }

        [Fact]
        public void Contact_spend_fuel_Evades_without_kills_keeps_upgrade()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var catalog = CrewCatalog.LoadDefault();
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            var player = new PlayerState("p1", "Mal", CutterStart, fuel: 2)
            {
                Passengers = 2,
                Fugitives = 1
            };
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_zoe"), out _));
            player.ShipUpgrades.Add(ElectronicDefenseSuiteAction.CardId);
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.ReaverCutter;
            game.PendingEncounterSectorId = CutterStart;

            Assert.True(
                ElectronicDefenseSuiteAction.TryEvadeContact(game, "p1", CutterAdjacent, out var error),
                error);
            Assert.Null(game.PendingEncounter);
            Assert.Equal(CutterAdjacent, player.SectorId);
            Assert.Equal(1, player.Fuel);
            Assert.Equal(2, player.Passengers);
            Assert.Equal(1, player.Fugitives);
            Assert.Equal(2, player.Roster.Count);
            Assert.Contains(ElectronicDefenseSuiteAction.CardId, player.ShipUpgrades);
            Assert.Equal(CutterStart, game.Tokens.ReaverCutterSectorIds[0]);
        }

        [Fact]
        public void Contact_EDS_rejects_without_fuel_or_upgrade()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            var player = new PlayerState("p1", "Mal", CutterStart, fuel: 0);
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.ReaverCutter;
            game.PendingEncounterSectorId = CutterStart;

            Assert.False(ElectronicDefenseSuiteAction.TryEvadeContact(game, "p1", CutterAdjacent, out var missing));
            Assert.Contains("not installed", missing);

            player.ShipUpgrades.Add(ElectronicDefenseSuiteAction.CardId);
            Assert.False(ElectronicDefenseSuiteAction.TryEvadeContact(game, "p1", CutterAdjacent, out var noFuel));
            Assert.Contains("1 Fuel", noFuel);
            Assert.Equal(TokenKind.ReaverCutter, game.PendingEncounter);
            Assert.Equal(CutterStart, player.SectorId);
        }

        [Fact]
        public void Nav_EDS_ignores_Reaver_Cutter_card_spends_fuel_and_Evades()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", CutterAdjacent, fuel: 2);
            player.ShipUpgrades.Add(ElectronicDefenseSuiteAction.CardId);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { Beaumonde });
            var game = new GameState(map, new[] { player }, tokens, decks);
            game.PendingNavDraws.Add(new PendingNavDraw(CutterAdjacent, NavRegion.Border));
            game.Decks!.Border.PlaceOnTop(game.Decks.Catalog.Get("nav_reaver-cutter"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.True(
                resolver.TryResolve(
                    game,
                    0,
                    out var resolution,
                    out var error,
                    choice: new NavResolveChoice
                    {
                        IgnoreReaverCutterWithEds = true,
                        EvadeToSectorId = CutterStart
                    }),
                error);
            Assert.Equal(FlightOutcome.Evade, resolution!.Outcome);
            Assert.True(resolution.Stopped);
            Assert.Null(resolution.ReaverContact);
            Assert.Equal(Beaumonde, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Equal(CutterStart, player.SectorId);
            Assert.Equal(1, player.Fuel);
            Assert.Contains(ElectronicDefenseSuiteAction.CardId, player.ShipUpgrades);
            Assert.Null(resolver.FaceUp);
            Assert.Empty(game.PendingNavDraws);
        }

        [Fact]
        public void Nav_EDS_mid_Contact_skips_Fight_after_cutter_moves()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(4));
            var catalog = CrewCatalog.LoadDefault();
            var player = new PlayerState("p1", "Mal", CutterAdjacent, fuel: 2)
            {
                Passengers = 1
            };
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            player.ShipUpgrades.Add(ElectronicDefenseSuiteAction.CardId);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { Beaumonde });
            var game = new GameState(map, new[] { player }, tokens, decks);
            game.PendingNavDraws.Add(new PendingNavDraw(CutterAdjacent, NavRegion.Border));
            game.Decks!.Border.PlaceOnTop(game.Decks.Catalog.Get("nav_reaver-cutter"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.True(
                resolver.TryResolve(
                    game,
                    0,
                    out var resolution,
                    out var error,
                    choice: new NavResolveChoice
                    {
                        UseEdsToIgnoreContact = true,
                        EvadeToSectorId = CutterStart
                    }),
                error);
            Assert.Equal(FlightOutcome.Evade, resolution!.Outcome);
            Assert.Null(resolution.ReaverContact);
            Assert.Equal(CutterAdjacent, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Equal(CutterStart, player.SectorId);
            Assert.Equal(1, player.Fuel);
            Assert.Equal(1, player.Passengers);
            Assert.Equal(1, player.Roster.Count);
            Assert.Contains(ElectronicDefenseSuiteAction.CardId, player.ShipUpgrades);
        }

        [Fact]
        public void IsReaverCutterNavCard_matches_type_or_name_like_Corvette()
        {
            // House pattern: Corvette cancel / Decoy Type — not card-id-only (Christopher #79).
            var catalog = NavCatalog.LoadFromFile(GameData.NavCardsPath);
            Assert.True(ElectronicDefenseSuiteAction.IsReaverCutterNavCard(catalog.Get("nav_reaver-cutter")));
            Assert.True(ElectronicDefenseSuiteAction.IsReaverCutterNavCard(catalog.Get("nav_reaver-bait")));
            Assert.False(ElectronicDefenseSuiteAction.IsReaverCutterNavCard(catalog.Get("nav_the-big-black")));
            Assert.False(ElectronicDefenseSuiteAction.IsReaverCutterNavCard(catalog.Get("nav_alliance-cruiser")));
        }
    }
}
