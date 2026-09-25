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
    /// Nav/Fly polish leftovers after #45: Alert Token PTR, Cry Baby dest,
    /// mid-Nav Reaver VictimCrewIds, Goods mix PendingChoice, Lawman Illegal Jobs gate.
    /// </summary>
    public class NavFlyPolishFinishTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string Londinium = "alliance-white-sun-r1-02";
        private const string CutterStart = "border-space-r2-06";
        private const string CutterAdjacent = "border-space-r2-05";
        private const string CutterAdjacentAlt = "border-space-r2-07";
        private const string CorvetteStart = "rim-kalidasa-r4-01";

        [Fact]
        public void Alliance_Alert_in_Alliance_Space_with_Corvette_suspends_PTR_ship_choice()
        {
            // Kalidasa p.4: In Alliance Space, PTR may choose Cruiser or Corvette.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(
                    allianceCruiserSectorId: Persephone,
                    operativeCorvetteSectorId: CorvetteStart)
                .PlaceAlertToken(Pelorum, AlertTokenKind.Alliance, 3);
            var p1 = new PlayerState("p1", "Mal", Pelorum) { Contraband = 1 };
            var p2 = new PlayerState("p2", "Zoe", Londinium);
            var game = new GameState(map, new[] { p1, p2 }, tokens) { UseAlertTokens = true };
            game.PendingAlertSectors.Add(Pelorum);

            Assert.False(AlertTokenResolver.TryResolvePending(
                game, ScriptedRng.FromDieFaces(2), out _, out var err));
            Assert.Contains("choose", err, System.StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(game.PendingChoice);
            Assert.Equal(PendingChoiceKinds.AlertAllianceShip, game.PendingChoice!.Kind);
            Assert.Equal("p2", game.PendingChoice.PlayerId);

            Assert.True(
                AlertTokenResolver.TryResume(
                    game,
                    new ChoiceSubmission { SelectedOptionId = AlertAllianceShipOptions.OperativeCorvette },
                    ScriptedRng.FromDieFaces(2),
                    out var resolution,
                    out var error),
                error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(TokenKind.OperativeCorvette, resolution!.Rolls[0].ArrivedShip);
            Assert.Equal(Pelorum, game.Tokens.OperativeCorvetteSectorId);
        }

        [Fact]
        public void Alert_Token_suspend_stash_is_isolated_per_GameState()
        {
            // Regression: process-wide static stash was cleared by parallel Alert resolves
            // (CorvetteTests / AlertTokenTests), breaking TryResume with
            // "No Alert Token choice is pending."
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var allianceTokens = new MapTokens(
                    allianceCruiserSectorId: Persephone,
                    operativeCorvetteSectorId: CorvetteStart)
                .PlaceAlertToken(Pelorum, AlertTokenKind.Alliance, 3);
            var alliance = new GameState(
                map,
                new[]
                {
                    new PlayerState("a1", "Mal", Pelorum) { Contraband = 1 },
                    new PlayerState("a2", "Zoe", Londinium)
                },
                allianceTokens) { UseAlertTokens = true };
            alliance.PendingAlertSectors.Add(Pelorum);

            var reaverTokens = new MapTokens(reaverCutterSectorIds: new[] { CutterAdjacent, CutterAdjacentAlt })
                .PlaceAlertToken(CutterStart, AlertTokenKind.Reaver, 3);
            var reaver = new GameState(
                map,
                new[]
                {
                    new PlayerState("r1", "Wash", CutterStart),
                    new PlayerState("r2", "Kaylee", Persephone)
                },
                reaverTokens) { UseAlertTokens = true };
            reaver.PendingAlertSectors.Add(CutterStart);

            Assert.False(AlertTokenResolver.TryResolvePending(
                alliance, ScriptedRng.FromDieFaces(2), out _, out _));
            Assert.False(AlertTokenResolver.TryResolvePending(
                reaver, ScriptedRng.FromDieFaces(1), out _, out _));

            // Completing an unrelated game must not wipe the other suspend bag.
            var noiseTokens = new MapTokens(allianceCruiserSectorId: Londinium)
                .PlaceAlertToken(Pelorum, AlertTokenKind.Alliance, 1);
            var noise = new GameState(
                map,
                new[] { new PlayerState("n1", "Book", Pelorum) },
                noiseTokens) { UseAlertTokens = true };
            Assert.True(AlertTokenResolver.TryResolveSector(
                noise,
                Pelorum,
                ScriptedRng.FromDieFaces(6),
                new AlertResolveChoice(),
                out _,
                out var noiseErr),
                noiseErr);

            Assert.True(
                AlertTokenResolver.TryResume(
                    alliance,
                    new ChoiceSubmission { SelectedOptionId = AlertAllianceShipOptions.OperativeCorvette },
                    ScriptedRng.FromDieFaces(2),
                    out var allianceRes,
                    out var allianceErr),
                allianceErr);
            Assert.Equal(TokenKind.OperativeCorvette, allianceRes!.Rolls[0].ArrivedShip);
            Assert.Equal(Pelorum, alliance.Tokens.OperativeCorvetteSectorId);

            Assert.True(
                AlertTokenResolver.TryResume(
                    reaver,
                    new ChoiceSubmission { SelectedOptionId = "1" },
                    ScriptedRng.FromDieFaces(1),
                    out var reaverRes,
                    out var reaverErr),
                reaverErr);
            Assert.True(reaverRes!.Rolls[0].ShipArrived);
            Assert.Equal(CutterStart, reaver.Tokens.ReaverCutterSectorIds[1]);
        }

        [Fact]
        public void Reaver_Alert_with_multiple_Cutters_suspends_PTR_index_choice()
        {
            // Blue Sun p.5: player to the right chooses and moves a Reaver ship.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterAdjacent, CutterAdjacentAlt })
                .PlaceAlertToken(CutterStart, AlertTokenKind.Reaver, 3);
            var p1 = new PlayerState("p1", "Mal", CutterStart);
            var p2 = new PlayerState("p2", "Zoe", Persephone);
            var game = new GameState(map, new[] { p1, p2 }, tokens) { UseAlertTokens = true };
            game.PendingAlertSectors.Add(CutterStart);

            Assert.False(AlertTokenResolver.TryResolvePending(
                game, ScriptedRng.FromDieFaces(1), out _, out _));
            Assert.Equal(PendingChoiceKinds.AlertReaverCutter, game.PendingChoice!.Kind);
            Assert.Equal("p2", game.PendingChoice.PlayerId);
            Assert.Equal(new[] { "0", "1" }, game.PendingChoice.Options);

            Assert.True(
                AlertTokenResolver.TryResume(
                    game,
                    new ChoiceSubmission { SelectedOptionId = "1" },
                    ScriptedRng.FromDieFaces(1),
                    out var resolution,
                    out var error),
                error);
            Assert.True(resolution!.Rolls[0].ShipArrived);
            Assert.Equal(CutterStart, game.Tokens.ReaverCutterSectorIds[1]);
            Assert.Equal(CutterAdjacent, game.Tokens.ReaverCutterSectorIds[0]);
        }

        [Fact]
        public void Cry_Baby_missing_destination_suspends_SectorDestination()
        {
            // Supplies.tsv: move the Cruiser 1 Sector within Alliance Space.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(allianceCruiserSectorId: Pelorum);
            var player = new PlayerState("p1", "Mal", Pelorum) { Warrants = 1 };
            player.ShipUpgrades.Add(CryBabyAction.CardId);
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.AllianceCruiser;
            game.PendingEncounterSectorId = Pelorum;

            Assert.False(CryBabyAction.TryDeploy(game, "p1", null, out var err));
            Assert.Equal(PendingChoiceKinds.SectorDestination, game.PendingChoice!.Kind);
            Assert.Equal(SectorDestinationContexts.CryBaby, game.PendingChoice.ContextId);
            Assert.Contains(CryBabyAction.CardId, player.ShipUpgrades);

            Assert.True(
                CryBabyAction.TryResumeDestination(
                    game,
                    new ChoiceSubmission { SelectedOptionId = Persephone },
                    out var error),
                error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(Persephone, game.Tokens.AllianceCruiserSectorId);
            Assert.DoesNotContain(CryBabyAction.CardId, player.ShipUpgrades);
        }

        [Fact]
        public void Mid_Nav_Reaver_Cutter_Contact_suspends_KillVictim_PendingChoice()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var catalog = CrewCatalog.LoadDefault();
            var player = new PlayerState("p1", "Mal", CutterStart, fuel: 3) { FightBonus = 0 };
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_zoe"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_wash"), out _));
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterAdjacentAlt });
            var game = new GameState(map, new[] { player }, tokens, decks);
            game.PendingNavDraws.Add(new PendingNavDraw(CutterStart, NavRegion.Border));
            game.Decks!.Border.PlaceOnTop(game.Decks.Catalog.Get("nav_reaver-cutter"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            // Fail Fight 8 → Kill 2 of 4 (Leader+3) → PendingChoice.
            Assert.False(resolver.TryResolve(
                game,
                0,
                out _,
                out var suspendErr,
                ScriptedRng.FromDieFaces(1, 1, 1, 1),
                new NavResolveChoice
                {
                    EvadeToSectorId = CutterAdjacent,
                    SkillCheck = new SkillCheckChoice { AcceptReroll = false }
                }));
            Assert.Equal(PendingChoiceKinds.KillVictim, game.PendingChoice!.Kind);
            Assert.NotNull(resolver.FaceUp);

            Assert.True(
                resolver.TryResumeMidNavReaverKillVictims(
                    game,
                    new ChoiceSubmission
                    {
                        Values = new List<string> { "crew_jayne", "crew_wash" }
                    },
                    ScriptedRng.FromDieFaces(1, 1, 1, 1),
                    out var resolution,
                    out var error),
                error);
            Assert.Null(game.PendingChoice);
            Assert.NotNull(resolution!.ReaverContact);
            Assert.Equal(FlightOutcome.Evade, resolution.Outcome);
            Assert.Equal(CutterAdjacent, player.SectorId);
            Assert.Equal(CutterStart, game.Tokens.ReaverCutterSectorIds[0]);
            Assert.Equal(1, player.Roster.Count); // zoe remains (no Leader hired)
        }

        [Fact]
        public void Orphaned_Cargo_Pod_Load_Goods_suspends_GoodsMix()
        {
            // Blue Sun: "you may choose which type of Goods you'd like to Load."
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1));
            var player = new PlayerState("p1", "Mal", Pelorum, fuel: 0, parts: 0);
            var game = new GameState(map, new[] { player }, decks: decks);
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_orphaned-cargo-pod"));
            var resolver = new NavResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, 1, out _, out var err));
            Assert.Equal(PendingChoiceKinds.GoodsMix, game.PendingChoice!.Kind);
            Assert.StartsWith("load:", game.PendingChoice.ContextId);

            Assert.True(
                resolver.TryResumeGoodsMix(
                    game,
                    new ChoiceSubmission
                    {
                        Values = new List<string> { "0", "1", "0", "1" }
                    },
                    out var resolution,
                    out var error),
                error);
            Assert.Equal(2, resolution!.GoodsLoaded);
            Assert.Equal(1, player.Parts);
            Assert.Equal(1, player.Contraband);
        }

        [Fact]
        public void Lawman_does_not_add_Fight_dice_on_Illegal_Misbehave()
        {
            // PBH / Director's Cut: Lawmen stay onboard on Illegal Jobs.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var catalog = CrewCatalog.LoadDefault();
            var jobs = JobCatalog.LoadDefault();
            var leaders = LeaderCatalog.LoadDefault();
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0) { FightBonus = 0 };
            Assert.True(player.Roster.TryHire(leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_dobson_piratesbountyhunters"), out _));
            Assert.True(player.Roster.Find("crew_dobson_piratesbountyhunters")!.Card.HasProfession("Lawman"));

            var job = jobs.Get("job_amnon-duul_courting-aphrodite");
            Assert.False(job.Legal);

            var game = new GameState(map, new[] { player }) { Jobs = jobs, Crew = catalog, Leaders = leaders };

            var check = new SkillCheck(Skill.Fight, 5);
            var withoutLawman = LawmanRules.CrewSkillForJob(player, job, Skill.Fight);
            var withEveryone = player.Roster.Fight;
            Assert.True(withEveryone > withoutLawman);
            Assert.Equal(withoutLawman, check.DiceCount(player, game, job));
            Assert.Equal(player.Fight, check.DiceCount(player, game, job: null));
        }
    }
}
