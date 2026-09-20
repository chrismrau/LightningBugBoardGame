using System.Collections.Generic;
using System.Linq;
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
    /// PendingChoice consumer (e): Haven / Any Rival / PTR sector destinations.
    /// Blue Sun Choosing Havens; PBH Any Rival; GF9 Cruiser Patrol / Safe Harbor / ship nudge.
    /// </summary>
    public class HavenRivalPtrChoiceTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Londinium = "alliance-white-sun-r1-02";
        private const string Bernadette = "alliance-white-sun-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string ContractJumper = "job_amnon-duul_contract-jumper";

        [Fact]
        public void PlayerToTheRightOf_wraps_table_order()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var game = new GameState(
                map,
                new[]
                {
                    new PlayerState("p1", "Mal", Persephone),
                    new PlayerState("p2", "Zoe", Persephone),
                    new PlayerState("p3", "Wash", Persephone)
                });
            Assert.Equal("p2", game.PlayerToTheRightOf("p1").Id);
            Assert.Equal("p3", game.PlayerToTheRightOf("p2").Id);
            Assert.Equal("p1", game.PlayerToTheRightOf("p3").Id);
        }

        [Fact]
        public void Any_port_omitted_HavenChoices_suspends_PendingChoice()
        {
            // Blue Sun Choosing Havens / Any Port setup: choose eligible Alliance Havens.
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone, shipId: "Serenity", leaderId: "Malcolm"),
                    new PlayerSeat("p2", "Zoe", Santo, shipId: "Bonanza", leaderId: "Zoe")
                },
                new GameSetupOptions
                {
                    ScenarioCardId = "scenario_any-port-in-a-storm",
                    DealStartingJobs = false,
                    UseBlueSun = true,
                    Rng = new SystemRng(50)
                });

            Assert.NotNull(game.PendingChoice);
            Assert.Equal(PendingChoiceKinds.HavenSector, game.PendingChoice!.Kind);
            Assert.Equal("p1", game.PendingChoice.PlayerId);
            Assert.NotNull(game.PendingChoice.Options);
            Assert.Contains(Bernadette, game.PendingChoice.Options!);
            Assert.DoesNotContain(Londinium, game.PendingChoice.Options!);
            Assert.Equal("", game.Players[0].HavenSectorId);
            Assert.Equal(0, game.Tokens.RemovableAlertCount(Londinium, AlertTokenKind.Alliance));
        }

        [Fact]
        public void Any_port_Haven_PendingChoice_then_next_seat_then_alert_tokens()
        {
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone, shipId: "Serenity", leaderId: "Malcolm"),
                    new PlayerSeat("p2", "Zoe", Santo, shipId: "Bonanza", leaderId: "Zoe")
                },
                new GameSetupOptions
                {
                    ScenarioCardId = "scenario_any-port-in-a-storm",
                    DealStartingJobs = false,
                    UseBlueSun = true,
                    Rng = new SystemRng(51)
                });

            Assert.True(
                HavenRules.TryResumeHavenSector(
                    game,
                    "p1",
                    new ChoiceSubmission { SelectedOptionId = Bernadette },
                    out var err1),
                err1);
            Assert.Equal(Bernadette, game.Players[0].HavenSectorId);
            Assert.Equal(Bernadette, game.Players[0].SectorId);
            Assert.NotNull(game.PendingChoice);
            Assert.Equal("p2", game.PendingChoice!.PlayerId);
            Assert.DoesNotContain(Bernadette, game.PendingChoice.Options!);

            Assert.True(
                HavenRules.TryResumeHavenSector(
                    game,
                    "p2",
                    new ChoiceSubmission { SelectedOptionId = Pelorum },
                    out var err2),
                err2);
            Assert.Null(game.PendingChoice);
            Assert.Equal(Pelorum, game.Players[1].HavenSectorId);
            Assert.Equal(1, game.Tokens.RemovableAlertCount(Londinium, AlertTokenKind.Alliance));
            Assert.Equal(0, game.Tokens.RemovableAlertCount(Bernadette, AlertTokenKind.Alliance));
        }

        [Fact]
        public void Piracy_empty_RivalId_suspends_same_sector_rivals()
        {
            // PBH pp.3–5: Working a Piracy Job requires a same-sector target ship.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var mal = new PlayerState("p1", "Mal", Persephone, cash: 0);
            var zoe = new PlayerState("p2", "Zoe", Persephone);
            var wash = new PlayerState("p3", "Wash", Santo);
            var game = new GameState(map, new[] { mal, zoe, wash })
            {
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                Crew = CrewCatalog.LoadDefault(),
                Leaders = LeaderCatalog.LoadDefault(),
                UsePiratesBountyHunters = true
            };
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1), BountyCatalog.LoadDefault());
            mal.JobHand.Add(ContractJumper);

            var piracy = new PiracyAction();
            Assert.False(piracy.TryPirate(
                game,
                "p1",
                ContractJumper,
                new PiracyChoice(),
                ScriptedRng.FromDieFaces(6, 6, 1),
                out _,
                out var error));
            Assert.Contains("rival", error, System.StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(game.PendingChoice);
            Assert.Equal(PendingChoiceKinds.RivalPlayer, game.PendingChoice!.Kind);
            Assert.Equal(new[] { "p2" }, game.PendingChoice.Options);
            Assert.DoesNotContain("p3", game.PendingChoice.Options!);
        }

        [Fact]
        public void Piracy_RivalId_via_PendingChoice_completes_boarding_fail_path()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var mal = new PlayerState("p1", "Mal", Persephone, cash: 0);
            var zoe = new PlayerState("p2", "Zoe", Persephone);
            var game = new GameState(map, new[] { mal, zoe })
            {
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                Crew = CrewCatalog.LoadDefault(),
                Leaders = LeaderCatalog.LoadDefault(),
                UsePiratesBountyHunters = true
            };
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1), BountyCatalog.LoadDefault());
            mal.JobHand.Add(ContractJumper);

            var piracy = new PiracyAction();
            Assert.False(piracy.TryPirate(
                game, "p1", ContractJumper, new PiracyChoice { BoardSkill = Skill.Tech },
                ScriptedRng.FromDieFaces(1), out _, out _));

            Assert.True(
                piracy.TryResumeRival(
                    game,
                    new ChoiceSubmission { SelectedOptionId = "p2" },
                    ScriptedRng.FromDieFaces(1),
                    out var result,
                    out var error),
                error);
            Assert.Null(game.PendingChoice);
            Assert.NotNull(result);
            Assert.True(result!.BoardingFailed);
            Assert.NotNull(mal.FindActive(ContractJumper));
        }

        [Fact]
        public void Cruiser_Patrol_missing_destination_suspends_for_PTR()
        {
            // GF9 p.8: "When the Cruiser Patrol card is drawn, the player to the right
            // of the person who drew the card moves the Cruiser 1 Sector within Alliance Space."
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var p1 = new PlayerState("p1", "Mal", Pelorum, fuel: 3);
            var p2 = new PlayerState("p2", "Zoe", Santo);
            var game = new GameState(map, new[] { p1, p2 })
            {
                Decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1)),
                Tokens = MapTokens.None.WithAllianceCruiser(Londinium)
            };
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_cruiser-patrol"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(game, 0, out _, out var error));
            Assert.Contains("Player to the right", error);
            Assert.NotNull(game.PendingChoice);
            Assert.Equal(PendingChoiceKinds.SectorDestination, game.PendingChoice!.Kind);
            Assert.Equal("p2", game.PendingChoice.PlayerId);
            Assert.Equal(SectorDestinationContexts.CruiserPatrol, game.PendingChoice.ContextId);
            Assert.Equal(Londinium, game.Tokens.AllianceCruiserSectorId);
            Assert.NotNull(resolver.FaceUp);
        }

        [Fact]
        public void Cruiser_Patrol_PTR_submit_moves_Cruiser_Keep_Flying()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var p1 = new PlayerState("p1", "Mal", Pelorum, fuel: 3);
            var p2 = new PlayerState("p2", "Zoe", Santo);
            var game = new GameState(map, new[] { p1, p2 })
            {
                Decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1)),
                Tokens = MapTokens.None.WithAllianceCruiser(Londinium)
            };
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_cruiser-patrol"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(game, 0, out _, out _));

            Assert.True(
                resolver.TryResumeSectorDestination(
                    game,
                    new ChoiceSubmission { Value = Bernadette },
                    out var resolution,
                    out var error),
                error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Equal(Bernadette, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(Pelorum, p1.SectorId);
        }

        [Fact]
        public void Safe_Harbor_named_Cruiser_snap_suspends_PTR_redirect()
        {
            // ScenarioCards.json Safe Harbor: "If a Card would move it to a Haven, the player
            // to the right places it in an adjacent Sector."
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Bernadette, shipId: "Serenity", leaderId: "Malcolm"),
                    new PlayerSeat("p2", "Zoe", Santo, shipId: "Bonanza", leaderId: "Zoe")
                },
                new GameSetupOptions
                {
                    ScenarioCardId = "scenario_any-port-in-a-storm",
                    DealStartingJobs = false,
                    UseBlueSun = true,
                    HavenChoices = new Dictionary<string, string>
                    {
                        ["p1"] = Bernadette,
                        ["p2"] = Pelorum
                    },
                    Rng = new SystemRng(52)
                });
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.PendingNavDraws.Add(new PendingNavDraw(Bernadette, NavRegion.Alliance));
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-cruiser"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(game, 0, out _, out var error));
            Assert.Contains("Safe Harbor", error);
            Assert.Equal(PendingChoiceKinds.SectorDestination, game.PendingChoice!.Kind);
            Assert.Equal("p2", game.PendingChoice.PlayerId);
            Assert.Equal(SectorDestinationContexts.SafeHarbor(Bernadette), game.PendingChoice.ContextId);
            Assert.Contains(Londinium, game.PendingChoice.Options!);

            Assert.True(
                resolver.TryResumeSectorDestination(
                    game,
                    new ChoiceSubmission { SelectedOptionId = Londinium },
                    out var resolution,
                    out var resumeError),
                resumeError);
            Assert.Null(game.PendingChoice);
            Assert.Equal(Londinium, game.Tokens.AllianceCruiserSectorId);
            Assert.NotNull(resolution);
        }

        [Fact]
        public void Ship_nudge_missing_path_suspends_for_PTR()
        {
            // NavCards.json: "The player to your right must move your ship two Sectors."
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var p1 = new PlayerState("p1", "Mal", Persephone, fuel: 3);
            var p2 = new PlayerState("p2", "Zoe", Santo);
            var game = new GameState(map, new[] { p1, p2 })
            {
                Decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1))
            };
            // Rim card — queue a rim draw sector adjacent to a 2-step path if needed.
            // Use the printed card with an Alliance/Border draw near Persephone.
            game.PendingNavDraws.Add(new PendingNavDraw(Persephone, NavRegion.Alliance));
            // Fritz is rim-only in counts; place on Alliance deck for the face-up resolve path.
            game.Decks.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_nav-system-on-the-fritz"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(game, 0, out _, out var error));
            Assert.Contains("two-Sector", error);
            Assert.Equal(PendingChoiceKinds.SectorDestination, game.PendingChoice!.Kind);
            Assert.Equal("p2", game.PendingChoice.PlayerId);
            Assert.Equal(SectorDestinationContexts.ShipNudge, game.PendingChoice.ContextId);
        }

        [Fact]
        public void Alliance_Entanglements_missing_destination_suspends_for_drawer()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var p1 = new PlayerState("p1", "Mal", Pelorum, fuel: 3);
            var game = new GameState(map, new[] { p1 })
            {
                Decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1)),
                Tokens = MapTokens.None.WithAllianceCruiser(Londinium)
            };
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-entanglements"));

            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(game, 0, out _, out var error));
            Assert.Contains("Alliance Sector", error);
            Assert.Equal(PendingChoiceKinds.SectorDestination, game.PendingChoice!.Kind);
            Assert.Equal("p1", game.PendingChoice.PlayerId);
            Assert.Equal(SectorDestinationContexts.AllianceEntanglements, game.PendingChoice.ContextId);
        }

        [Fact]
        public void Various_Work_still_rejects_toward_Bounty_deck()
        {
            // Cortex Alert / Various stay on the Bounty deck — no invented site pick.
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(53) });
            var kids = game.Jobs!.Cards.Values.FirstOrDefault(j =>
                JobTerms.IsVarious(j.PickupLocation));
            if (kids == null)
                return; // no Various job in catalog — still covered by JobSitesBountiesTests
            game.CurrentPlayer.JobHand.Add(kids.Id);
            Assert.False(new WorkAction().TryWork(game, "p1", kids.Id, out _, out var error));
            Assert.Contains("Bounty", error);
            Assert.Null(game.PendingChoice);
        }
    }
}
