using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class BountyTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Londinium = "alliance-white-sun-r1-02";
        private const string Santo = "alliance-qin-shi-huang-r1-01";

        private static CrewCatalog Crew => CrewCatalog.LoadDefault();
        private static BountyCatalog Bounties => BountyCatalog.LoadDefault();

        private static (GameState Game, PlayerState Mal, PlayerState Zoe) TwoShips()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var mal = new PlayerState("p1", "Mal", Persephone, cash: 0);
            var zoe = new PlayerState("p2", "Zoe", Persephone);
            var game = new GameState(map, new[] { mal, zoe })
            {
                Bounties = Bounties,
                Crew = Crew
            };
            game.BountyDeck = BountyDeck.FromCatalog(game.Bounties, new SystemRng(1));
            return (game, mal, zoe);
        }

        private static void PutOnWanted(GameState game, string bountyId)
        {
            var card = game.Bounties!.Get(bountyId);
            game.BountyDeck = new BountyDeck(new[] { card }, new SystemRng(2), game.Bounties);
        }

        [Fact]
        public void Catalog_loads_twenty_bounties()
        {
            Assert.Equal(20, Bounties.Cards.Count);
            Assert.True(Bounties.TryResolve("bounty_river-tam", out var river));
            Assert.Equal(4000, river.Pay);
            Assert.True(river.Immoral);
            Assert.Equal("Command Cruiser", river.DropoffPlanet);
            Assert.True(Bounties.TryResolve("Enforcers", out var alert));
            Assert.Equal(BountyKind.CortexAlert, alert.Kind);
        }

        [Fact]
        public void Setup_reveals_three_most_wanted()
        {
            // PBH p.8: reveal the top 3 Bounty Cards at Set Up (gated on UseBountyDeck).
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, UseBountyDeck = true, Rng = new SystemRng(3) });
            Assert.NotNull(game.Bounties);
            Assert.NotNull(game.BountyDeck);
            Assert.Equal(3, game.BountyDeck!.FaceUp.Count);
            Assert.Equal(17, game.BountyDeck.DrawCount);
        }

        [Fact]
        public void Claiming_a_bounty_refills_most_wanted_from_the_draw_pile()
        {
            // PBH p.8: "When a Fugitive is apprehended and their Bounty is claimed,
            // reveal a new Bounty Card. There should always be three face-up Bounties."
            var (game, _, _) = TwoShips();
            Assert.Equal(3, game.BountyDeck!.FaceUp.Count);
            Assert.Equal(17, game.BountyDeck.DrawCount);
            var claimedId = game.BountyDeck.FaceUp[0].Id;

            Assert.True(game.BountyDeck.TryClaimWanted(claimedId, out _));

            Assert.Equal(3, game.BountyDeck.FaceUp.Count);
            Assert.Equal(16, game.BountyDeck.DrawCount);
            Assert.DoesNotContain(game.BountyDeck.FaceUp, c => c.Id == claimedId);
        }

        [Fact]
        public void Apprehend_claim_refills_most_wanted_via_Bind()
        {
            // Bind → TryClaimWanted is the apprehend path (PBH p.8 / p.11 Bound by Law).
            var (game, mal, _) = TwoShips();
            game.BountyDeck = BountyDeck.FromCatalog(game.Bounties!, new SystemRng(7));
            // Ensure Jayne is face-up by cycling the list until she appears.
            for (var i = 0; i < 20 && game.BountyDeck.FindWanted("bounty_jayne") == null; i++)
                game.BountyDeck.CycleWantedList();
            Assert.NotNull(game.BountyDeck.FindWanted("bounty_jayne"));
            var drawBefore = game.BountyDeck.DrawCount;

            Assert.True(mal.Roster.TryHire(Crew.Get("crew_jayne"), out _));
            Assert.True(mal.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.True(new BountyAction().TryBetray(
                game, "p1", "bounty_jayne", "crew_jayne", out var result, out var error), error);
            Assert.True(result!.Success);
            Assert.Equal(3, game.BountyDeck.FaceUp.Count);
            Assert.Null(game.BountyDeck.FindWanted("bounty_jayne"));
            Assert.Equal(drawBefore - 1, game.BountyDeck.DrawCount);
        }

        [Fact]
        public void Most_wanted_refill_stops_when_draw_pile_is_exhausted()
        {
            var catalog = Bounties;
            var three = new[]
            {
                catalog.Get("bounty_billy"),
                catalog.Get("bounty_jayne"),
                catalog.Get("bounty_helen"),
            };
            var deck = new BountyDeck(three, new SystemRng(4), catalog);
            Assert.Equal(3, deck.FaceUp.Count);
            Assert.Equal(0, deck.DrawCount);

            Assert.True(deck.TryClaimWanted("bounty_billy", out _));
            Assert.Equal(2, deck.FaceUp.Count);
            Assert.Equal(0, deck.DrawCount);

            Assert.True(deck.TryClaimWanted("bounty_jayne", out _));
            Assert.Equal(1, deck.FaceUp.Count);

            Assert.True(deck.TryClaimWanted("bounty_helen", out _));
            Assert.Empty(deck.FaceUp);
        }

        [Fact]
        public void Setup_omits_bounty_deck_unless_selected()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            Assert.Null(game.BountyDeck);
        }

        [Fact]
        public void Lone_target_rejects_when_subject_is_on_a_rival_ship()
        {
            var (game, mal, zoe) = TwoShips();
            PutOnWanted(game, "bounty_billy");
            Assert.True(zoe.Roster.TryHire(Crew.Get("crew_billy"), out _));
            var hunt = new BountyAction();
            Assert.False(hunt.TryApprehendLone(
                game, "p1", "bounty_billy", "crew_billy", Skill.Fight, ScriptedRng.FromDieFaces(6),
                out _, out var error));
            Assert.Contains("confrontation", error);
            Assert.Contains("same sector", error);

            // Same sector + confrontation succeeds (PBH p.10).
            var rng = ScriptedRng.FromDieFaces(6, 6, 1);
            Assert.True(hunt.TryApprehendRival(
                game, "p1", "bounty_billy", "p2", "crew_billy",
                Skill.Fight, Skill.Talk, Skill.Talk, rng,
                out var result, out error), error);
            Assert.True(result!.Success);
            Assert.Contains("crew_billy", mal.BoundBounties[0].CrewIds);
        }

        [Fact]
        public void Lone_target_follows_supply_discard_planet_not_only_last_seen()
        {
            var (game, mal, _) = TwoShips();
            PutOnWanted(game, "bounty_billy");
            game.Supply = SupplyCatalog.LoadDefault();
            game.SupplyDecks = SupplyDecks.FromCatalog(game.Supply, new SystemRng(9));
            // Move Billy into Regina discard — nab requires Regina, not Persephone (Last Seen).
            Assert.True(game.Crew!.TryGet("crew_billy", out var billy));
            PullIntoDiscard(game, billy!, "Regina");
            mal.SectorId = Persephone;
            var hunt = new BountyAction();
            Assert.False(hunt.TryApprehendLone(
                game, "p1", "bounty_billy", "crew_billy", Skill.Fight, ScriptedRng.FromDieFaces(6),
                out _, out var error));
            Assert.Contains("Regina", error);

            mal.SectorId = "border-georgia-r1-01"; // Regina
            Assert.True(hunt.TryApprehendLone(
                game, "p1", "bounty_billy", "crew_billy", Skill.Fight, ScriptedRng.FromDieFaces(6, 1),
                out var result, out error), error);
            Assert.True(result!.Success);
        }

        private static void PullIntoDiscard(GameState game, CrewCard crew, string planet)
        {
            foreach (var market in game.SupplyDecks!.Markets)
            {
                Strip(market.Deck, crew.Id);
                Strip(market.FaceUp, crew.Id);
                Strip(market.Discard, crew.Id);
                market.Refill();
            }
            Assert.True(game.SupplyDecks.TryGet(planet, out var target));
            target!.Discard.Add(new SupplyCard(crew.Id, crew.Name, 0, SupplyKind.Crew));
        }

        private static void Strip(System.Collections.Generic.IList<SupplyCard> pile, string crewId)
        {
            for (var i = pile.Count - 1; i >= 0; i--)
            {
                if (pile[i].Id == crewId)
                    pile.RemoveAt(i);
            }
        }

        [Fact]
        public void Cortex_lone_target_requires_subject_supply_location()
        {
            var (game, mal, _) = TwoShips();
            PutOnWanted(game, "bounty_enforcers");
            // No supply location → cannot lone-target a Cortex Alert from nowhere.
            Assert.False(new BountyAction().TryApprehendLone(
                game, "p1", "bounty_enforcers", "crew_enforcer", Skill.Fight, ScriptedRng.FromDieFaces(6),
                out _, out var error));
            Assert.Contains("confrontation or betrayal", error);
            Assert.Equal(Persephone, mal.SectorId);
        }

        [Fact]
        public void Betrayal_binds_without_a_showdown_and_disgruntles_the_crew()
        {
            var (game, mal, _) = TwoShips();
            Assert.True(mal.Roster.TryHire(Crew.Get("crew_jayne"), out _));
            Assert.True(mal.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            PutOnWanted(game, "bounty_jayne");

            var hunt = new BountyAction();
            Assert.True(hunt.TryBetray(game, "p1", "bounty_jayne", "crew_jayne", out var result, out var error), error);
            Assert.True(result!.Success);
            Assert.Equal(1, mal.BoundBounties.Count);
            Assert.Contains("crew_jayne", mal.BoundBounties[0].CrewIds);
            Assert.False(mal.Roster.HasName("Jayne"));
            Assert.True(mal.Roster.Find("crew_kaylee")!.Disgruntled);
            Assert.Equal(TurnAction.Work, game.LastAction);
        }

        [Fact]
        public void Lone_target_win_binds_the_fugitive()
        {
            var (game, mal, _) = TwoShips();
            PutOnWanted(game, "bounty_billy");
            mal.Roster.TryHire(Crew.Get("crew_jayne"), out _);
            var hunt = new BountyAction();
            var rng = ScriptedRng.FromDieFaces(6, 1);
            Assert.True(hunt.TryApprehendLone(
                game, "p1", "bounty_billy", "crew_billy", Skill.Fight, rng,
                out var result, out var error), error);
            Assert.True(result!.Success);
            Assert.Equal("crew_billy", mal.BoundBounties[0].CrewIds[0]);
        }

        [Fact]
        public void Lone_target_loss_applies_botch_kills()
        {
            var (game, mal, _) = TwoShips();
            PutOnWanted(game, "bounty_billy");
            mal.Roster.TryHire(Crew.Get("crew_kaylee"), out _);
            var hunt = new BountyAction();
            var rng = ScriptedRng.FromDieFaces(1, 6);
            Assert.True(hunt.TryApprehendLone(
                game, "p1", "bounty_billy", "crew_billy", Skill.Fight, rng,
                out var result, out var error), error);
            Assert.False(result!.Success);
            Assert.Equal(1, result.CrewKilled);
            Assert.Equal(0, mal.Roster.Count);
            Assert.Empty(mal.BoundBounties);
        }

        [Fact]
        public void Deliver_pays_and_disgruntles_moral_crew_on_immoral_bounty()
        {
            var (game, mal, _) = TwoShips();
            PutOnWanted(game, "bounty_helen");
            mal.Roster.TryHire(Crew.Get("crew_kaylee"), out _);
            mal.Roster.TryHire(Crew.Get("crew_helen"), out _);
            var hunt = new BountyAction();
            Assert.True(hunt.TryBetray(game, "p1", "bounty_helen", "crew_helen", out _, out var error), error);
            game.EndTurn();
            game.EndTurn();
            mal.SectorId = "border-georgia-r3-02";

            Assert.True(hunt.TryDeliver(game, "p1", "bounty_helen", out var paid, out error), error);
            Assert.Equal(2600, paid!.Pay);
            Assert.Equal(2600, mal.Cash);
            Assert.Null(mal.Roster.Find("crew_kaylee"));
            Assert.Empty(mal.BoundBounties);
        }

        [Fact]
        public void Lawman_adds_printed_bounty_bonus()
        {
            var player = new PlayerState("p1", "Mal", Persephone);
            player.Roster.TryHire(Crew.Get("crew_deputy_piratesbountyhunters"), out _);
            player.Roster.TryHire(Crew.Get("crew_agent-mcginnis_piratesbountyhunters"), out _);
            Assert.Equal(800, BountyAction.LawmanBonus(player));
        }

        [Fact]
        public void Cortex_alert_stacks_and_pays_per_fugitive()
        {
            var (game, mal, zoe) = TwoShips();
            PutOnWanted(game, "bounty_scrappers");
            mal.Roster.TryHire(Crew.Get("crew_scrapper"), out _);
            zoe.Roster.TryHire(Crew.Get("crew_scrapper_2"), out _);

            var hunt = new BountyAction();
            Assert.True(hunt.TryBetray(game, "p1", "bounty_scrappers", "crew_scrapper", out _, out var error), error);
            game.EndTurn();
            game.EndTurn();
            var rng = ScriptedRng.FromDieFaces(6, 6, 1);
            Assert.True(hunt.TryApprehendRival(
                game, "p1", "bounty_scrappers", "p2",
                "crew_scrapper_2",
                Skill.Fight, Skill.Talk, Skill.Talk, rng,
                out _, out error), error);
            Assert.Equal(2, mal.BoundBounties[0].Count);

            mal.SectorId = "alliance-white-sun-r3-07";
            game.EndTurn();
            game.EndTurn();
            Assert.True(hunt.TryDeliver(game, "p1", "bounty_scrappers", out var paid, out error), error);
            Assert.Equal(4000, paid!.Pay);
        }

        [Fact]
        public void Jump_can_rescue_the_fugitive_onto_your_crew()
        {
            var (game, mal, zoe) = TwoShips();
            PutOnWanted(game, "bounty_billy");
            mal.Roster.TryHire(Crew.Get("crew_billy"), out _);
            var hunt = new BountyAction();
            Assert.True(hunt.TryBetray(game, "p1", "bounty_billy", "crew_billy", out _, out var error), error);
            game.EndTurn();
            var rng = ScriptedRng.FromDieFaces(6, 6, 1);
            Assert.True(hunt.TryJump(
                game, "p2", "p1", "bounty_billy",
                Skill.Fight, Skill.Talk, Skill.Talk, rng, rescue: true,
                out var result, out error), error);
            Assert.True(result!.Rescued);
            Assert.True(zoe.Roster.HasName("Billy"));
            Assert.Empty(mal.BoundBounties);
        }
    }
}
