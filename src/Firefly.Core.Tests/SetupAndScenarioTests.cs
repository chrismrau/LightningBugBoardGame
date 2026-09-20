using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class SetupAndScenarioTests
    {
        [Fact]
        public void Setup_catalog_has_eight_cards()
        {
            var catalog = SetupCatalog.LoadDefault();
            Assert.Equal(8, catalog.Cards.Count);
            var standard = catalog.Get("setup_standard");
            Assert.Equal(3000, standard.StartingCash);
            Assert.Equal(6, standard.StartingFuel);
            Assert.Equal(2, standard.StartingParts);
        }

        [Fact]
        public void Browncoat_way_starts_with_twelve_thousand_and_no_free_fuel()
        {
            var card = SetupCatalog.LoadDefault().Get("setup_the-browncoat-way");
            Assert.Equal(12000, card.StartingCash);
            Assert.Equal(0, card.StartingFuel);
            Assert.Equal(0, card.StartingParts);
            Assert.True(card.PurchaseShipsFromBank);
            Assert.Equal(100, card.BuyFuelCost);
            Assert.Equal(300, card.BuyPartsCost);
        }

        [Fact]
        public void Scenario_catalog_has_nineteen_cards()
        {
            var catalog = ScenarioCatalog.LoadDefault();
            Assert.Equal(19, catalog.Cards.Count);
            Assert.Equal("First Time in the Captain's Chair", catalog.Get("scenario_first-time-in-the-captains-chair").Name);
            Assert.Equal("firstToCompleteGoal", catalog.Get("scenario_first-time-in-the-captains-chair").WinType);
        }

        [Fact]
        public void Blitz_steps_are_numbered_one_through_eight()
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(GameData.SetupCardsPath));
            var blitz = doc.RootElement.GetProperty("setupCards").EnumerateArray()
                .First(c => c.GetProperty("id").GetString() == "setup_the-blitz");
            var orders = blitz.GetProperty("steps").EnumerateArray()
                .Select(s => s.GetProperty("order").GetInt32()).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, orders);
        }

        [Fact]
        public void Scavengers_verse_is_deferred()
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(GameData.ScenarioCardsPath));
            var card = doc.RootElement.GetProperty("scenarioCards").EnumerateArray()
                .First(c => c.GetProperty("id").GetString() == "scenario_the-scavengers-verse");
            Assert.True(card.GetProperty("deferred").GetBoolean());
        }

        [Fact]
        public void Standard_setup_loads_nav_reshuffle_discard_at_three_players()
        {
            var standard = SetupCatalog.LoadDefault().Get("setup_standard");
            Assert.Equal(NavReshuffleSetupMode.DiscardIfPlayerCountAtLeast, standard.NavReshuffleMode);
            Assert.Equal(3, standard.NavReshufflePlayerCountThreshold);
            Assert.False(standard.PlacesNavReshuffleInDiscard(1));
            Assert.False(standard.PlacesNavReshuffleInDiscard(2));
            Assert.True(standard.PlacesNavReshuffleInDiscard(3));
            Assert.True(standard.PlacesNavReshuffleInDiscard(4));
        }

        [Fact]
        public void Clearer_skies_keeps_reshuffle_in_deck_regardless_of_player_count()
        {
            var card = SetupCatalog.LoadDefault().Get("setup_clearer-skies-better-days");
            Assert.Equal(NavReshuffleSetupMode.ShuffleIntoDecksRegardlessOfPlayerCount, card.NavReshuffleMode);
            Assert.False(card.PlacesNavReshuffleInDiscard(1));
            Assert.False(card.PlacesNavReshuffleInDiscard(4));
        }

        [Fact]
        public void Standard_one_player_keeps_reshuffle_in_nav_draw_piles()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(20) });

            AssertReshufflesStayInDraw(game);
        }

        [Fact]
        public void Standard_two_players_keeps_reshuffle_in_nav_draw_piles()
        {
            // GF9 p.4: discard step only applies with 3 or more players.
            var game = GameSetup.Standard(
                new PlayerSeat("p1", "Mal", Persephone),
                new PlayerSeat("p2", "Zoe", Santo));

            AssertReshufflesStayInDraw(game);
        }

        [Fact]
        public void Standard_three_players_places_reshuffle_in_nav_discard_piles()
        {
            // FAQ 4.1 p.1 / GF9 p.4: "In games with 3 or more players, the reshuffle cards
            // from both Nav Decks are placed in the discard pile at the start of the game"
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo),
                    new PlayerSeat("p3", "Wash", Bernadette)
                },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(21) });

            AssertReshufflesStartInDiscard(game);
        }

        [Fact]
        public void Standard_four_players_places_reshuffle_in_nav_discard_piles()
        {
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo),
                    new PlayerSeat("p3", "Wash", Bernadette),
                    new PlayerSeat("p4", "Kaylee", Regina)
                },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(22) });

            AssertReshufflesStartInDiscard(game);
        }

        [Fact]
        public void Clearer_skies_three_players_still_shuffles_reshuffle_into_draw()
        {
            // SetupCards.json: "Shuffle the RESHUFFLE Nav Cards into the Nav Decks before the game begins."
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo),
                    new PlayerSeat("p3", "Wash", Bernadette)
                },
                new GameSetupOptions
                {
                    SetupCardId = "setup_clearer-skies-better-days",
                    DealStartingJobs = false,
                    Rng = new SystemRng(23)
                });

            AssertReshufflesStayInDraw(game);
        }

        [Fact]
        public void Setup_discarded_reshuffle_enters_draw_only_after_first_exhaustion()
        {
            // GF9 p.4 / Director's Cut p.12: "When either Nav Deck becomes exhausted for the
            // first time, reshuffle the discard pile - including the “RESHUFFLE” card."
            // Mid-game exhaustion always includes RESHUFFLE regardless of player count;
            // setup merely parks it in discard until that first reshuffle.
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo),
                    new PlayerSeat("p3", "Wash", Bernadette)
                },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(24) });

            AssertReshufflesStartInDiscard(game);
            AssertMidgameExhaustionIncludesReshuffle(game.Decks!.Alliance, "nav_alliance-cruiser");
            AssertMidgameExhaustionIncludesReshuffle(game.Decks.Border, "nav_reaver-cutter");
            AssertMidgameExhaustionIncludesReshuffle(game.Decks.Rim, "nav_reaver-cutter");
        }

        [Fact]
        public void Standard_one_or_two_players_midgame_exhaustion_still_includes_reshuffle()
        {
            // 1–2 players: RESHUFFLE starts in the draw pile (GF9 p.4 discard step is 3+ only).
            // After it is resolved mid-game, later exhaustion of the draw pile still reshuffles
            // every discard card — including RESHUFFLE — back into the draw pile.
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo)
                },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(25) });

            AssertReshufflesStayInDraw(game);
            AssertMidgameResolveThenExhaustionIncludesReshuffle(game.Decks!.Alliance, "nav_alliance-cruiser");
            AssertMidgameResolveThenExhaustionIncludesReshuffle(game.Decks.Border, "nav_reaver-cutter");
            AssertMidgameResolveThenExhaustionIncludesReshuffle(game.Decks.Rim, "nav_reaver-cutter");
        }

        private static void AssertReshufflesStayInDraw(GameState game)
        {
            Assert.NotNull(game.Decks);
            foreach (var deck in new[] { game.Decks!.Alliance, game.Decks.Border, game.Decks.Rim })
            {
                Assert.Equal(60, deck.DrawCount);
                Assert.Equal(0, deck.DiscardCount);
                Assert.True(deck.DrawContainsReshuffle());
            }
        }

        private static void AssertReshufflesStartInDiscard(GameState game)
        {
            Assert.NotNull(game.Decks);
            AssertRegionalReshuffleInDiscard(game.Decks!.Alliance, "nav_alliance-cruiser");
            AssertRegionalReshuffleInDiscard(game.Decks.Border, "nav_reaver-cutter");
            AssertRegionalReshuffleInDiscard(game.Decks.Rim, "nav_reaver-cutter");
        }

        private static void AssertRegionalReshuffleInDiscard(NavDeck deck, string expectedId)
        {
            Assert.Equal(59, deck.DrawCount);
            Assert.Equal(1, deck.DiscardCount);
            Assert.False(deck.DrawContainsReshuffle());
            Assert.True(deck.DiscardPile[0].IsReshuffle);
            Assert.Equal(expectedId, deck.DiscardPile[0].Id);
        }

        /// <summary>
        /// Drain draw into discard (RESHUFFLE already sitting in discard from Set Up), then
        /// draw once — empty-draw path must reshuffle discard including RESHUFFLE.
        /// </summary>
        private static void AssertMidgameExhaustionIncludesReshuffle(NavDeck deck, string reshuffleId)
        {
            Assert.Equal(1, deck.DiscardCount);
            Assert.True(deck.DiscardPile[0].IsReshuffle);
            Assert.Equal(reshuffleId, deck.DiscardPile[0].Id);
            Assert.False(deck.DrawContainsReshuffle());

            var drawBefore = deck.DrawCount;
            for (var i = 0; i < drawBefore; i++)
                deck.ResolveIntoDiscard(deck.Draw());

            Assert.Equal(0, deck.DrawCount);
            Assert.Equal(drawBefore + 1, deck.DiscardCount); // non-RESHUFFLE discards + RESHUFFLE
            var next = deck.Draw();
            Assert.Equal(drawBefore, deck.DrawCount); // 60 reshuffled, one drawn → 59
            Assert.Equal(0, deck.DiscardCount);
            Assert.False(string.IsNullOrWhiteSpace(next.Id));
            Assert.True(next.IsReshuffle || deck.DrawContainsReshuffle());
            Assert.Equal(60, deck.DrawCount + 1); // drawn card + remaining draw = full deck
        }

        /// <summary>
        /// 1–2 player path: pull RESHUFFLE to top, resolve (immediate reshuffle), drain the
        /// new draw pile into discard without drawing RESHUFFLE again by leaving it buried —
        /// then empty-draw reshuffle must still bring RESHUFFLE back.
        /// </summary>
        private static void AssertMidgameResolveThenExhaustionIncludesReshuffle(NavDeck deck, string reshuffleId)
        {
            Assert.True(deck.DrawContainsReshuffle());
            Assert.Equal(0, deck.DiscardCount);

            // Resolve RESHUFFLE mid-game: card enters discard then immediately reshuffles.
            deck.PlaceOnTop(FindAndRemoveReshuffle(deck, reshuffleId));
            var reshuffle = deck.Draw();
            Assert.Equal(reshuffleId, reshuffle.Id);
            Assert.True(reshuffle.IsReshuffle);
            deck.ResolveIntoDiscard(reshuffle);
            Assert.Equal(0, deck.DiscardCount);
            Assert.True(deck.DrawContainsReshuffle());

            // Park RESHUFFLE in discard without resolving (simulates it already being among
            // discards when the draw pile later empties — same inclusion rule as GF9 p.4).
            var parked = FindAndRemoveReshuffle(deck, reshuffleId);
            // Use ResolveIntoDiscard only for non-reshuffle; parking uses the public discard
            // path via MoveReshufflesToDiscardForSetup-equivalent: discard via exhaustion
            // after moving RESHUFFLE aside with PlaceOnTop of a known non-reshuffle drain.
            ParkReshuffleInDiscardWithoutResolving(deck, parked);

            Assert.False(deck.DrawContainsReshuffle());
            Assert.Equal(1, deck.DiscardCount);
            Assert.True(deck.DiscardPile[0].IsReshuffle);

            var drawBefore = deck.DrawCount;
            for (var i = 0; i < drawBefore; i++)
                deck.ResolveIntoDiscard(deck.Draw());

            var next = deck.Draw();
            Assert.Equal(0, deck.DiscardCount);
            Assert.True(next.IsReshuffle || deck.DrawContainsReshuffle());
            Assert.Equal(60, deck.DrawCount + 1);
        }

        private static NavCard FindAndRemoveReshuffle(NavDeck deck, string reshuffleId)
        {
            // Draw until we find it, holding others aside, then restore non-matches on top.
            var held = new List<NavCard>();
            NavCard? found = null;
            while (deck.DrawCount > 0)
            {
                var card = deck.Draw();
                if (found == null && card.Id == reshuffleId)
                {
                    found = card;
                    break;
                }
                held.Add(card);
            }
            Assert.NotNull(found);
            for (var i = held.Count - 1; i >= 0; i--)
                deck.PlaceOnTop(held[i]);
            return found!;
        }

        private static void ParkReshuffleInDiscardWithoutResolving(NavDeck deck, NavCard reshuffle)
        {
            // Mirror Set Up parking: MoveReshufflesToDiscardForSetup puts IsReshuffle in discard
            // without ResolveIntoDiscard. Re-place then move for an isolated mid-game park.
            deck.PlaceOnTop(reshuffle);
            Assert.Equal(1, deck.MoveReshufflesToDiscardForSetup());
        }

        [Fact]
        public void Blitz_catalog_exposes_strip_mine_and_double_dip_prime()
        {
            var card = SetupCatalog.LoadDefault().Get("setup_the-blitz");
            Assert.True(card.StripMineOneSupplyDeck);
            Assert.Equal(6, card.PrimeSupplyReveal);
            Assert.False(SetupCatalog.LoadDefault().Get("setup_standard").StripMineOneSupplyDeck);
            Assert.Equal(3, SetupCatalog.LoadDefault().Get("setup_standard").PrimeSupplyReveal);
        }

        [Fact]
        public void Blitz_requires_strip_mine_planet()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                GameSetup.Create(
                    new[]
                    {
                        new PlayerSeat("p1", "Mal", Persephone),
                        new PlayerSeat("p2", "Zoe", Santo)
                    },
                    new GameSetupOptions
                    {
                        SetupCardId = "setup_the-blitz",
                        DealStartingJobs = false,
                        Rng = new SystemRng(40)
                    }));
            Assert.Contains("StripMinePlanet", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Blitz_strip_mines_one_deck_and_primes_six_into_discard()
        {
            // SetupCards.json / printed The Blitz:
            // "Choose 1 Supply Deck to be Strip Mined… Reveal a number of cards… equal to
            // the number of players… claiming one revealed Supply Card, at no cost…
            // Repeat until all players have had the Dinosaur…"
            // Director's Cut p.47: "Players will start with a number of free Supply Cards
            // equal to the number of players"
            // Double Dip: "Reveal the top 6 cards of each Supply deck. Place the revealed
            // cards in their discard piles."
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone, leaderId: "leader_malcolm"),
                    new PlayerSeat("p2", "Zoe", Santo, leaderId: "leader_zoe_jetwash")
                },
                new GameSetupOptions
                {
                    SetupCardId = "setup_the-blitz",
                    StripMinePlanet = "Persephone",
                    DinosaurStartIndex = 0,
                    DealStartingJobs = false,
                    Rng = new SystemRng(41)
                });

            Assert.Equal("setup_the-blitz", game.Setup!.Id);
            Assert.Equal(
                NavReshuffleSetupMode.ShuffleIntoDecksRegardlessOfPlayerCount,
                game.Setup.NavReshuffleMode);

            // 2 players → 2 rounds × 1 pick each = 2 free cards per player (plus Leader).
            var p1 = game.GetPlayer("p1");
            var p2 = game.GetPlayer("p2");
            Assert.Equal(2, CountStripMineGrants(game, p1));
            Assert.Equal(2, CountStripMineGrants(game, p2));

            Assert.NotNull(game.SupplyDecks);
            foreach (var planet in GameSetup.CoreSupplyPlanets)
            {
                Assert.True(game.SupplyDecks!.TryGet(planet, out var market), planet);
                Assert.Equal(6, market.Discard.Count);
                Assert.Equal(3, market.FaceUp.Count);
            }

            // 4 free cards claimed from Persephone (shared card-id copies may remain in-market).
            Assert.Equal(4,
                CountStripMineGrants(game, p1) + CountStripMineGrants(game, p2));
        }

        [Fact]
        public void Blitz_dinosaur_start_index_controls_first_pick()
        {
            // Same RNG → same revealed sequence; Dinosaur holder changes who claims first.
            GameSetupOptions Opts(int dino) => new GameSetupOptions
            {
                SetupCardId = "setup_the-blitz",
                StripMinePlanet = "Persephone",
                DinosaurStartIndex = dino,
                DealStartingJobs = false,
                Rng = new SystemRng(42)
            };

            var dino0 = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone, leaderId: "Malcolm"),
                    new PlayerSeat("p2", "Zoe", Santo, leaderId: "Zoe")
                },
                Opts(0));
            var dino1 = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone, leaderId: "Malcolm"),
                    new PlayerSeat("p2", "Zoe", Santo, leaderId: "Zoe")
                },
                Opts(1));

            var p1At0 = StripMineGrantIds(dino0, dino0.GetPlayer("p1"));
            var p1At1 = StripMineGrantIds(dino1, dino1.GetPlayer("p1"));
            Assert.Equal(2, p1At0.Count);
            Assert.Equal(2, p1At1.Count);
            // Same pool of four cards, redistributed by Dinosaur seating.
            var pool0 = p1At0.Concat(StripMineGrantIds(dino0, dino0.GetPlayer("p2"))).OrderBy(x => x).ToList();
            var pool1 = p1At1.Concat(StripMineGrantIds(dino1, dino1.GetPlayer("p2"))).OrderBy(x => x).ToList();
            Assert.Equal(pool0, pool1);
            Assert.NotEqual(p1At0.OrderBy(x => x).ToList(), p1At1.OrderBy(x => x).ToList());
        }

        [Fact]
        public void Blitz_strip_mine_honors_explicit_claim_order()
        {
            // Stack four Persephone gear cards, then claim in draft order:
            // round1 dino=p1 → pickOrder[0], p2 → [1]; round2 dino=p2 → [2], p1 → [3].
            var pickOrder = SupplyCatalog.LoadDefault().Cards.Values
                .Where(c => c.Kind == SupplyKind.Gear && c.CopiesByPlanet.ContainsKey("Persephone"))
                .Take(4)
                .Select(c => c.Id)
                .ToList();
            Assert.Equal(4, pickOrder.Count);

            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone, leaderId: "Malcolm"),
                    new PlayerSeat("p2", "Zoe", Santo, leaderId: "Zoe")
                },
                new GameSetupOptions
                {
                    SetupCardId = "setup_the-blitz",
                    StripMinePlanet = "Persephone",
                    DinosaurStartIndex = 0,
                    StripMineClaims = pickOrder,
                    DealStartingJobs = false,
                    Rng = new SystemRng(42),
                    AfterLeadersHired = g =>
                    {
                        Assert.True(g.SupplyDecks!.TryGet("Persephone", out var market));
                        Assert.NotNull(g.Supply);
                        foreach (var id in pickOrder)
                        {
                            Assert.True(g.Supply.TryGet(id, out var card));
                            RemoveAllCopies(market.Deck, id);
                            RemoveAllCopies(market.FaceUp, id);
                            RemoveAllCopies(market.Discard, id);
                        }
                        for (var i = pickOrder.Count - 1; i >= 0; i--)
                        {
                            Assert.True(g.Supply.TryGet(pickOrder[i], out var card));
                            market.Deck.Insert(0, card);
                        }
                    }
                });

            Assert.True(PlayerOwnsSupply(game.GetPlayer("p1"), pickOrder[0]));
            Assert.True(PlayerOwnsSupply(game.GetPlayer("p2"), pickOrder[1]));
            Assert.True(PlayerOwnsSupply(game.GetPlayer("p2"), pickOrder[2]));
            Assert.True(PlayerOwnsSupply(game.GetPlayer("p1"), pickOrder[3]));
        }

        [Fact]
        public void Blitz_does_not_run_when_another_setup_is_selected()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, leaderId: "Malcolm") },
                new GameSetupOptions
                {
                    SetupCardId = "setup_standard",
                    StripMinePlanet = "Persephone",
                    DealStartingJobs = false,
                    Rng = new SystemRng(43)
                });

            // Leader only — no free strip-mine gear/crew/upgrades beyond the Leader hire.
            Assert.Equal(1, game.CurrentPlayer.Roster.Count);
            Assert.Empty(game.CurrentPlayer.Gear);
            Assert.Empty(game.CurrentPlayer.ShipUpgrades);
            Assert.True(game.SupplyDecks!.TryGet("Persephone", out var market));
            Assert.Equal(3, market.Discard.Count);
        }

        private static int CountStripMineGrants(GameState game, PlayerState player)
        {
            return StripMineGrantIds(game, player).Count;
        }

        private static List<string> StripMineGrantIds(GameState game, PlayerState player)
        {
            var ids = new List<string>();
            ids.AddRange(player.Gear);
            ids.AddRange(player.ShipUpgrades);
            foreach (var member in player.Roster.Members)
            {
                if (!member.IsLeader)
                    ids.Add(member.Id);
            }
            if (DriveCoreWasReplaced(game, player))
                ids.Add(player.DriveCoreId!);
            return ids;
        }

        private static bool DriveCoreWasReplaced(GameState game, PlayerState player)
        {
            if (string.IsNullOrWhiteSpace(player.DriveCoreId) || game.Ships == null)
                return false;
            if (!game.Ships.TryResolve(player.ShipId!, out var ship))
                return false;
            if (game.DriveCores != null && game.DriveCores.TryResolve(ship.MainDrive, out var start))
                return !string.Equals(player.DriveCoreId, start.Id, StringComparison.Ordinal);
            return !string.Equals(player.DriveCoreId, ship.MainDrive, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PlayerOwnsSupply(PlayerState player, string cardId)
        {
            if (player.Gear.Contains(cardId) || player.ShipUpgrades.Contains(cardId))
                return true;
            if (string.Equals(player.DriveCoreId, cardId, StringComparison.Ordinal))
                return true;
            foreach (var member in player.Roster.Members)
            {
                if (string.Equals(member.Id, cardId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void RemoveAllCopies(IList<SupplyCard> pile, string cardId)
        {
            for (var i = pile.Count - 1; i >= 0; i--)
            {
                if (string.Equals(pile[i].Id, cardId, StringComparison.Ordinal))
                    pile.RemoveAt(i);
            }
        }

        private const string Persephone = "alliance-lux-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Bernadette = "alliance-white-sun-r1-01";
        private const string Regina = "border-georgia-r1-01";

        [Fact]
        public void Standard_setup_wires_misbehave_and_supply_decks()
        {
            var game = GameSetup.Standard(
                new PlayerSeat("p1", "Mal", Persephone),
                new PlayerSeat("p2", "Zoe", Santo));

            Assert.Equal("setup_standard", game.Setup!.Id);
            Assert.NotNull(game.Misbehave);
            Assert.NotNull(game.MisbehaveCatalog);
            Assert.True(game.Misbehave!.DrawCount >= 70);
            Assert.Equal(0, game.Misbehave.DiscardCount);

            Assert.NotNull(game.Supply);
            Assert.NotNull(game.SupplyDecks);
            foreach (var planet in GameSetup.CoreSupplyPlanets)
            {
                Assert.True(game.SupplyDecks!.TryGet(planet, out var market), planet);
                // GF9 / Director's Cut Priming the Pump: top 3 into discard, then FaceUp for Buy.
                Assert.Equal(3, market.Discard.Count);
                Assert.Equal(3, market.FaceUp.Count);
                Assert.True(market.Deck.Count > 0, planet);
            }

            Assert.NotNull(game.Decks);
            Assert.NotNull(game.Jobs);
            Assert.NotNull(game.ContactDecks);
            Assert.NotNull(game.Crew);
            Assert.NotNull(game.Leaders);
            Assert.NotNull(game.Ships);
            Assert.NotNull(game.Gear);
            Assert.Equal("ship_bonanza", game.GetPlayer("p1").ShipId);
            Assert.Equal("ship_bonnie-mae", game.GetPlayer("p2").ShipId);
        }

        [Fact]
        public void Starting_leader_is_hired_for_free()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, leaderId: "leader_malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(8) });

            var player = game.CurrentPlayer;
            Assert.Equal("leader_malcolm", player.LeaderId);
            Assert.Equal(1, player.Roster.Count);
            Assert.True(player.Roster.Members[0].IsLeader);
            Assert.Equal("Malcolm", player.Roster.Members[0].Name);
            Assert.Equal(2, player.Fight);
            Assert.Equal(1, player.Talk);
            Assert.True(player.Roster.HasProfession("Pilot"));
            Assert.Equal(3000, player.Cash);
        }

        [Fact]
        public void Leader_can_be_chosen_by_printed_name()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Nandi", Persephone, leaderId: "Nandi") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(9) });

            Assert.Equal("leader_nandi", game.CurrentPlayer.LeaderId);
            Assert.True(game.CurrentPlayer.Roster.HasProfession("Companion"));
        }

        [Fact]
        public void Two_players_cannot_share_a_leader()
        {
            Assert.Throws<ArgumentException>(() =>
                GameSetup.Standard(
                    new PlayerSeat("p1", "Mal", Persephone, leaderId: "leader_malcolm"),
                    new PlayerSeat("p2", "Also Mal", Santo, leaderId: "Malcolm")));
        }

        [Fact]
        public void Standard_setup_gives_printed_starting_supplies_and_a_job_hand()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { Rng = new SystemRng(4) });

            var player = game.CurrentPlayer;
            Assert.Equal(3000, player.Cash);
            Assert.Equal(6, player.Fuel);
            Assert.Equal(2, player.Parts);
            Assert.True(player.JobHand.Count > 0);
            Assert.True(player.JobHand.Count <= player.JobHandLimit);
            Assert.Equal(GameSetup.AllianceCruiserStartSectorId, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal("alliance-white-sun-r1-02", game.Tokens.AllianceCruiserSectorId);
            Assert.True(game.Map.TryGet(game.Tokens.AllianceCruiserSectorId!, out var londinium));
            Assert.Equal("Londinium", londinium.Planet);
            Assert.Null(game.Tokens.OperativeCorvetteSectorId);
            Assert.Equal(new[] { GameSetup.CoreReaverCutterStartSectorId }, game.Tokens.ReaverCutterSectorIds);
            Assert.Equal("border-space-r2-06", game.Tokens.ReaverCutterSectorIds[0]);
        }

        [Fact]
        public void Operative_corvette_starts_at_cortex_relay_2_when_in_play()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    DealStartingJobs = false,
                    UseOperativesCorvette = true,
                    Rng = new SystemRng(4)
                });

            Assert.Equal(GameSetup.OperativeCorvetteStartSectorId, game.Tokens.OperativeCorvetteSectorId);
            Assert.Equal("rim-cortex-relay-2-r1-11", game.Tokens.OperativeCorvetteSectorId);
            Assert.True(game.Map.TryGet(game.Tokens.OperativeCorvetteSectorId!, out var relay));
            Assert.Equal("Cortex Relay 2", relay.Planet);
            Assert.Equal(GameSetup.AllianceCruiserStartSectorId, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Blue_sun_places_three_reaver_cutters_on_burnham()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    DealStartingJobs = false,
                    UseBlueSun = true,
                    Rng = new SystemRng(4)
                });

            Assert.Equal(GameSetup.BlueSunReaverCutterStartSectorIds, game.Tokens.ReaverCutterSectorIds);
            Assert.Equal(
                new[] { "rim-burnham-r2-01", "rim-burnham-r2-02", "rim-burnham-r2-03" },
                game.Tokens.ReaverCutterSectorIds);
            foreach (var id in game.Tokens.ReaverCutterSectorIds)
                Assert.True(game.Map.TryGet(id, out _));
            Assert.Null(game.Tokens.OperativeCorvetteSectorId);
            Assert.Equal(GameSetup.AllianceCruiserStartSectorId, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Browncoat_setup_starts_rich_and_dry()
        {
            // SetupCards.json: $12,000; no free Fuel/Parts; purchase ship at list price.
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "Serenity", leaderId: "Malcolm") },
                new GameSetupOptions
                {
                    SetupCardId = "setup_the-browncoat-way",
                    DealStartingJobs = false,
                    Rng = new SystemRng(5)
                });

            Assert.Equal("ship_serenity", game.CurrentPlayer.ShipId);
            Assert.Equal("leader_malcolm", game.CurrentPlayer.LeaderId);
            Assert.Equal(12000 - 7800, game.CurrentPlayer.Cash);
            Assert.Equal(0, game.CurrentPlayer.Fuel);
            Assert.Equal(0, game.CurrentPlayer.Parts);
            Assert.Empty(game.CurrentPlayer.JobHand);
        }

        [Fact]
        public void Browncoat_snake_draft_follows_forward_last_both_then_reverse()
        {
            // SetupCards.json Choose Ships and Leaders:
            // highest roller → leftward one each → last takes both → reverse remaining.
            // Seats: p1 start, p2, p3. Explicit picks:
            // p1 ship, p2 leader, p3 ship+leader, p2 ship, p1 leader.
            var picks = new List<BrowncoatDraftPick>
            {
                BrowncoatDraftPick.Ship("p1", "Serenity"),
                BrowncoatDraftPick.Leader("p2", "Zoe"),
                BrowncoatDraftPick.Ship("p3", "Bonanza"),
                BrowncoatDraftPick.Leader("p3", "Monty"),
                BrowncoatDraftPick.Ship("p2", "Yun Qi"),
                BrowncoatDraftPick.Leader("p1", "Malcolm")
            };

            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone, leaderId: "Malcolm"),
                    new PlayerSeat("p2", "Zoe", Santo, leaderId: "Zoe"),
                    new PlayerSeat("p3", "Monty", Regina, leaderId: "Monty")
                },
                new GameSetupOptions
                {
                    SetupCardId = "setup_the-browncoat-way",
                    BrowncoatDraftStartIndex = 0,
                    BrowncoatDraftPicks = picks,
                    DealStartingJobs = false,
                    Rng = new SystemRng(51)
                });

            Assert.Equal("ship_serenity", game.GetPlayer("p1").ShipId);
            Assert.Equal("leader_malcolm", game.GetPlayer("p1").LeaderId);
            Assert.Equal(12000 - 7800, game.GetPlayer("p1").Cash);

            Assert.Equal("ship_yun-qi", game.GetPlayer("p2").ShipId);
            Assert.Equal("leader_zoe_jetwash", game.GetPlayer("p2").LeaderId);
            Assert.Equal(12000 - 7800, game.GetPlayer("p2").Cash);

            Assert.Equal("ship_bonanza", game.GetPlayer("p3").ShipId);
            Assert.Equal("leader_monty", game.GetPlayer("p3").LeaderId);
            Assert.Equal(12000 - 7800, game.GetPlayer("p3").Cash);
        }

        [Fact]
        public void Browncoat_post_purchase_fuel_and_parts_use_printed_prices()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "Serenity", leaderId: "Malcolm") },
                new GameSetupOptions
                {
                    SetupCardId = "setup_the-browncoat-way",
                    BrowncoatResourceBuys = new Dictionary<string, BrowncoatResourceBuy>
                    {
                        ["p1"] = new BrowncoatResourceBuy { Fuel = 2, Parts = 1 }
                    },
                    DealStartingJobs = false,
                    Rng = new SystemRng(52)
                });

            // 12000 - 7800 ship - 2*100 fuel - 1*300 parts
            Assert.Equal(12000 - 7800 - 200 - 300, game.CurrentPlayer.Cash);
            Assert.Equal(2, game.CurrentPlayer.Fuel);
            Assert.Equal(1, game.CurrentPlayer.Parts);
        }

        [Fact]
        public void Browncoat_esmeralda_list_price_includes_starting_upgrades()
        {
            // Director's Cut p.47: "When playing “The Browncoat Way” Set Up Card, the list
            // prices on the ships include the cost of the starting Ship Upgrades; do not
            // pay for them again." Esmeralda $9300 includes Caravan Pods + Full Mess Deck.
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "Esmeralda", leaderId: "Malcolm") },
                new GameSetupOptions
                {
                    SetupCardId = "setup_the-browncoat-way",
                    DealStartingJobs = false,
                    Rng = new SystemRng(53)
                });

            Assert.Equal("ship_esmeralda", game.CurrentPlayer.ShipId);
            Assert.Equal(12000 - 9300, game.CurrentPlayer.Cash);
            Assert.Contains("ship-upgrade_caravan-pods_esmeralda", game.CurrentPlayer.ShipUpgrades);
            Assert.Contains("ship-upgrade_full-mess-deck_esmeralda", game.CurrentPlayer.ShipUpgrades);
            // Would be $9300 + $400 + $400 if upgrades were charged again.
            Assert.NotEqual(12000 - 9300 - 400 - 400, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Browncoat_draft_does_not_run_on_standard_setup()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "Serenity", leaderId: "Malcolm") },
                new GameSetupOptions
                {
                    SetupCardId = "setup_standard",
                    BrowncoatDraftPicks = new List<BrowncoatDraftPick>
                    {
                        BrowncoatDraftPick.Ship("p1", "Serenity"),
                        BrowncoatDraftPick.Leader("p1", "Malcolm")
                    },
                    DealStartingJobs = false,
                    Rng = new SystemRng(54)
                });

            // Free starting ship — cash stays at $3000.
            Assert.Equal(3000, game.CurrentPlayer.Cash);
            Assert.Equal(6, game.CurrentPlayer.Fuel);
            Assert.Equal(2, game.CurrentPlayer.Parts);
        }

        [Fact]
        public void Setup_game_can_buy_at_persephone()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(6) });

            Assert.True(game.SupplyDecks!.TryGet("Persephone", out var market));
            var cardId = market.FaceUp[0].Id;
            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1, SupplyCardIds = { cardId } }, out var result, out var error), error);
            Assert.Equal(1, result!.FuelBought);
            Assert.Single(result.CardsBought);
            Assert.Equal(3, market.FaceUp.Count);
        }

        [Fact]
        public void Setup_game_can_draw_a_misbehave_card()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Santo) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(7) });

            game.CurrentPlayer.JobHand.Add("job_badger_badgers-11-casino-caper");
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", "job_badger_badgers-11-casino-caper", out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);

            var resolver = new MisbehaveResolver();
            var card = resolver.DrawNext(game);
            Assert.False(string.IsNullOrWhiteSpace(card.Id));
            Assert.NotNull(game.PendingMisbehave!.FaceUp);
        }

        [Fact]
        public void Starting_ship_is_assigned_by_id()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "ship_serenity") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(11) });

            var player = game.CurrentPlayer;
            Assert.Equal("ship_serenity", player.ShipId);
            Assert.Equal(6, player.Roster.MaxCrew);
            Assert.Equal(8, player.CargoHold);
            Assert.Equal(4, player.StashHold);
            Assert.Equal(3, player.UpgradeSlots);
        }

        [Fact]
        public void Starting_ship_can_be_chosen_by_printed_name()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "Artful Dodger") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(12) });

            Assert.Equal("ship_artful-dodger", game.CurrentPlayer.ShipId);
            Assert.Equal(7, game.CurrentPlayer.Roster.MaxCrew);
            Assert.Equal(6, game.CurrentPlayer.CargoHold);
        }

        [Fact]
        public void Two_players_cannot_share_a_ship()
        {
            Assert.Throws<ArgumentException>(() =>
                GameSetup.Standard(
                    new PlayerSeat("p1", "Mal", Persephone, shipId: "Serenity"),
                    new PlayerSeat("p2", "Zoe", Santo, shipId: "ship_serenity")));
        }

        [Fact]
        public void Interceptor_starts_with_a_four_crew_limit()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "Interceptor") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(13) });

            Assert.Equal("ship_interceptor", game.CurrentPlayer.ShipId);
            Assert.Equal(4, game.CurrentPlayer.Roster.MaxCrew);
            Assert.Equal(4, game.CurrentPlayer.CargoHold);
            Assert.Equal(2, game.CurrentPlayer.UpgradeSlots);
        }

        [Fact]
        public void Seating_leader_zoe_removes_her_crew_cards_from_supply()
        {
            var without = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, leaderId: "Malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(1) });
            var withZoe = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Zoe", Persephone, shipId: "Jetwash", leaderId: "Zoe") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(1) });

            Assert.True(CountCrewNamed(without.SupplyDecks!, "Zoe") >= 1);
            Assert.Equal(0, CountCrewNamed(withZoe.SupplyDecks!, "Zoe"));
            Assert.Equal("leader_zoe_jetwash", withZoe.CurrentPlayer.LeaderId);
        }

        [Fact]
        public void Times_not_catalog_exposes_twenty_game_length_tokens()
        {
            var card = SetupCatalog.LoadDefault().Get("setup_times-not-on-our-side");
            Assert.Equal(20, card.GameLengthTokenCount);
            Assert.Null(SetupCatalog.LoadDefault().Get("setup_standard").GameLengthTokenCount);
        }

        [Fact]
        public void Times_not_setup_starts_with_nineteen_after_opening_discard()
        {
            // SetupCards.json: "Give a pile of 20 Disgruntled Tokens to the player taking the
            // first turn… Each time that player takes a turn, discard one…"
            var game = CreateTimesNotTwoPlayers();

            Assert.Equal(19, game.GameLengthTokensRemaining);
            Assert.Equal(0, game.FirstPlayerIndex);
            Assert.False(game.GameLengthFinalRound);
            Assert.Equal("p1", game.CurrentPlayer.Id);
        }

        [Fact]
        public void Standard_setup_does_not_track_game_length_tokens()
        {
            var game = GameSetup.Standard(
                new PlayerSeat("p1", "Mal", Persephone),
                new PlayerSeat("p2", "Zoe", Santo));

            Assert.Null(game.GameLengthTokensRemaining);
            Assert.False(game.GameLengthFinalRound);
            game.EndTurn();
            Assert.Null(game.GameLengthTokensRemaining);
        }

        [Fact]
        public void Times_not_discards_only_when_first_player_begins_a_turn()
        {
            var game = CreateTimesNotTwoPlayers();
            Assert.Equal(19, game.GameLengthTokensRemaining);

            game.EndTurn(); // p2 begins — no discard
            Assert.Equal("p2", game.CurrentPlayer.Id);
            Assert.Equal(19, game.GameLengthTokensRemaining);

            game.EndTurn(); // p1 begins — discard
            Assert.Equal("p1", game.CurrentPlayer.Id);
            Assert.Equal(18, game.GameLengthTokensRemaining);
            Assert.False(game.GameLengthFinalRound);
        }

        [Fact]
        public void Times_not_final_token_starts_final_round_then_most_credits_wins()
        {
            // "When the final token is discarded, everyone gets one final turn, then the game
            // is over. If time runs out before the Story Card is completed, the player with
            // the most credits wins."
            var game = CreateTimesNotTwoPlayers(scenarioId: "scenario_first-time-in-the-captains-chair");
            game.GameLengthTokensRemaining = 1;
            game.GetPlayer("p1").Cash = 1000;
            game.GetPlayer("p2").Cash = 5000;

            Assert.Equal("p1", game.CurrentPlayer.Id);
            game.GameLengthTokensRemaining = 1;
            game.BeginOpeningTurn(); // first player begins turn → discard final token
            Assert.Equal(0, game.GameLengthTokensRemaining);
            Assert.True(game.GameLengthFinalRound);
            Assert.False(game.GameOver);

            game.EndTurn(); // p2's final turn
            Assert.Equal("p2", game.CurrentPlayer.Id);
            Assert.False(game.GameOver);

            game.EndTurn(); // wrap to p1 → time expired
            Assert.True(game.GameOver);
            Assert.Equal("p2", game.WinnerId);
            Assert.Contains("most credits", game.WinReason!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Times_not_story_win_during_final_round_is_not_overridden()
        {
            var game = CreateTimesNotTwoPlayers(scenarioId: "scenario_first-time-in-the-captains-chair");
            game.GameLengthTokensRemaining = 0;
            game.GameLengthFinalRound = true;
            game.GetPlayer("p1").Cash = 100;
            game.GetPlayer("p2").Cash = 9000;

            Assert.True(WinCheck.TryCompleteGoal(game, game.GetPlayer("p1"), 3, out var error), error);
            Assert.Equal("p1", game.WinnerId);

            game.EndTurn(); // would wrap toward timer end, but story already won
            Assert.Equal("p1", game.WinnerId);
        }

        private static GameState CreateTimesNotTwoPlayers(string? scenarioId = null)
        {
            return GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo)
                },
                new GameSetupOptions
                {
                    SetupCardId = "setup_times-not-on-our-side",
                    ScenarioCardId = scenarioId,
                    DealStartingJobs = false,
                    Rng = new SystemRng(30)
                });
        }

        private const string Londinium = "alliance-white-sun-r1-02";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string Albion = "alliance-white-sun-r4-11";
        private const string Osiris = "alliance-white-sun-r3-07";

        [Fact]
        public void Any_port_catalog_exposes_havens_tokens_warrants_and_specials()
        {
            var card = ScenarioCatalog.LoadDefault().Get("scenario_any-port-in-a-storm");
            Assert.Equal("firstAtHavenWithCash", card.WinType);
            Assert.Equal(12000, card.WinCash);
            Assert.Equal(1, card.StartingWarrants);
            Assert.NotNull(card.ChooseHavens);
            Assert.True(card.ChooseHavens!.Required);
            Assert.Equal("Alliance", card.ChooseHavens.Space);
            Assert.Contains("Londinium", card.ChooseHavens.ExcludeLocations);
            Assert.True(card.AllianceAlertTokensOnNonHavenAlliancePlanets);
            Assert.True(card.IncreasedEnforcement);
            Assert.True(card.SafeHarbor);
            Assert.True(card.FriendsInLowPlaces);
        }

        [Fact]
        public void Any_port_setup_chooses_havens_starts_there_with_warrant_and_alert_tokens()
        {
            // ScenarioCards.json Any Port: Alliance Havens (not Londinium); Alliance Alert
            // Tokens on non-Haven Alliance planets; starting Warrant. Blue Sun tokens ≠ Alert deck.
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
                    Rng = new SystemRng(40)
                });

            Assert.Equal(Bernadette, game.Players[0].HavenSectorId);
            Assert.Equal(Bernadette, game.Players[0].SectorId);
            Assert.Equal(Pelorum, game.Players[1].HavenSectorId);
            Assert.Equal(Pelorum, game.Players[1].SectorId);
            Assert.Equal(1, game.Players[0].Warrants);
            Assert.Equal(1, game.Players[1].Warrants);
            Assert.True(game.UseAlertTokens);

            // Non-Haven Alliance planets get Alliance Alert Tokens (incl. Londinium, Osiris).
            Assert.Equal(1, game.Tokens.RemovableAlertCount(Londinium, AlertTokenKind.Alliance));
            Assert.Equal(1, game.Tokens.RemovableAlertCount(Osiris, AlertTokenKind.Alliance));
            Assert.Equal(1, game.Tokens.RemovableAlertCount(Santo, AlertTokenKind.Alliance));
            // Havens do not.
            Assert.Equal(0, game.Tokens.RemovableAlertCount(Bernadette, AlertTokenKind.Alliance));
            Assert.Equal(0, game.Tokens.RemovableAlertCount(Pelorum, AlertTokenKind.Alliance));
            // Alert deck is separate — still loaded, not auto-drawn by this setup.
            Assert.NotNull(game.AllianceAlertDeck);
            Assert.Null(game.AllianceAlertDeck!.Active);
        }

        [Fact]
        public void Any_port_rejects_supply_londinium_and_duplicate_havens()
        {
            Assert.Throws<ArgumentException>(() => GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    ScenarioCardId = "scenario_any-port-in-a-storm",
                    DealStartingJobs = false,
                    HavenChoices = new Dictionary<string, string> { ["p1"] = Persephone },
                    Rng = new SystemRng(41)
                }));

            Assert.Throws<ArgumentException>(() => GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Londinium) },
                new GameSetupOptions
                {
                    ScenarioCardId = "scenario_any-port-in-a-storm",
                    DealStartingJobs = false,
                    HavenChoices = new Dictionary<string, string> { ["p1"] = Londinium },
                    Rng = new SystemRng(42)
                }));

            Assert.Throws<ArgumentException>(() => GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Bernadette),
                    new PlayerSeat("p2", "Zoe", Santo)
                },
                new GameSetupOptions
                {
                    ScenarioCardId = "scenario_any-port-in-a-storm",
                    DealStartingJobs = false,
                    HavenChoices = new Dictionary<string, string>
                    {
                        ["p1"] = Bernadette,
                        ["p2"] = Bernadette
                    },
                    Rng = new SystemRng(43)
                }));
        }

        [Fact]
        public void Any_port_does_not_run_when_another_scenario_is_selected()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    ScenarioCardId = "scenario_first-time-in-the-captains-chair",
                    DealStartingJobs = false,
                    HavenChoices = new Dictionary<string, string> { ["p1"] = Bernadette },
                    Rng = new SystemRng(44)
                });

            Assert.Equal(Persephone, game.CurrentPlayer.HavenSectorId);
            Assert.Equal(Persephone, game.CurrentPlayer.SectorId);
            Assert.Equal(0, game.CurrentPlayer.Warrants);
            Assert.Equal(0, game.Tokens.RemovableAlertCount(Londinium, AlertTokenKind.Alliance));
        }

        [Fact]
        public void Any_port_illegal_job_issues_warrant_legal_does_not()
        {
            var game = CreateAnyPortAt(Albion);
            var player = game.CurrentPlayer;
            player.Warrants = 0;
            player.JobHand.Add("job_amnon-duul_feeding-alliance-fat-cats");
            player.ActiveJobs.Add(new ActiveJob("job_amnon-duul_feeding-alliance-fat-cats")
            {
                PickedUp = true,
                Cargo = 2
            });
            player.Cargo = 2;
            Assert.True(new WorkAction().TryWork(
                game, "p1", "job_amnon-duul_feeding-alliance-fat-cats", out _, out var legalErr), legalErr);
            Assert.Equal(0, player.Warrants);

            game.EndTurn();
            player.SectorId = Santo;
            player.JobHand.Add("job_badger_badgers-11-casino-caper");
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", "job_badger_badgers-11-casino-caper", out _, out var err), err);
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out var done, out var last), last);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(1, player.Warrants);
        }

        [Fact]
        public void Any_port_safe_harbor_blocks_cruiser_choice_onto_haven()
        {
            var game = CreateAnyPortAt(Bernadette);
            Assert.False(HavenRules.CanChooseCruiserDestination(game, Bernadette, out var error));
            Assert.Contains("Safe Harbor", error);
            Assert.True(HavenRules.CanChooseCruiserDestination(game, Londinium, out _));
        }

        [Fact]
        public void Any_port_safe_harbor_redirects_forced_cruiser_off_haven()
        {
            var game = CreateAnyPortAt(Bernadette);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            Assert.False(HavenRules.TryPlaceAllianceCruiser(game, Bernadette, null, out var needAdj));
            Assert.Contains("adjacent", needAdj);

            Assert.True(HavenRules.TryPlaceAllianceCruiser(game, Bernadette, Londinium, out var error), error);
            Assert.Equal(Londinium, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Any_port_safe_harbor_blocks_cruiser_patrol_onto_haven()
        {
            var game = CreateAnyPortAt(Bernadette);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.PendingNavDraws.Add(new PendingNavDraw(Londinium, NavRegion.Alliance));
            var resolver = new NavResolver();
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_cruiser-patrol"));
            Assert.False(resolver.TryAutoResolve(
                game,
                out _,
                out var error,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = Bernadette }));
            Assert.Contains("Safe Harbor", error);
            Assert.Equal(Londinium, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Any_port_friends_in_low_places_free_shore_leave_and_fuel_at_own_haven()
        {
            var game = CreateAnyPortAt(Bernadette);
            var player = game.CurrentPlayer;
            player.Cash = 3000;
            player.Fuel = 0;
            player.Roster.Disgruntle(player.Roster.Leader!);
            var shore = new ShoreLeaveAction();
            Assert.True(shore.TryHavenFuelAndShoreLeave(
                game,
                "p1",
                new HavenBuyRequest { ShoreLeave = true, Fuel = 4 },
                out var result,
                out var error), error);
            Assert.Equal(0, result!.CashSpent);
            Assert.Equal(4, result.FuelLoaded);
            Assert.Equal(1, result.TokensCleared);
            Assert.Equal(4, player.Fuel);
            Assert.Equal(3000, player.Cash);
            Assert.False(player.Roster.Leader!.Disgruntled);
            Assert.Equal(TurnAction.Buy, game.LastAction);
        }

        [Fact]
        public void Any_port_friends_in_low_places_inactive_on_other_scenarios()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Bernadette, shipId: "Serenity", leaderId: "Malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(46) });
            game.CurrentPlayer.HavenSectorId = Bernadette;
            Assert.False(new ShoreLeaveAction().TryHavenFuelAndShoreLeave(
                game,
                "p1",
                new HavenBuyRequest { ShoreLeave = true, Fuel = 1 },
                out _,
                out var error));
            Assert.Contains("Friends in Low Places", error);
        }

        private static GameState CreateAnyPortAt(string havenSectorId)
        {
            return GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", havenSectorId, shipId: "Serenity", leaderId: "Malcolm") },
                new GameSetupOptions
                {
                    ScenarioCardId = "scenario_any-port-in-a-storm",
                    DealStartingJobs = false,
                    UseBlueSun = true,
                    Rng = new SystemRng(45)
                });
        }

        private static int CountCrewNamed(SupplyDecks decks, string name)
        {
            var n = 0;
            foreach (var market in decks.Markets)
            {
                foreach (var pile in new[] { market.Deck, market.FaceUp, market.Discard })
                {
                    foreach (var card in pile)
                    {
                        if (card.Kind == SupplyKind.Crew &&
                            string.Equals(card.Name, name, System.StringComparison.OrdinalIgnoreCase))
                            n++;
                    }
                }
            }
            return n;
        }
    }
}
