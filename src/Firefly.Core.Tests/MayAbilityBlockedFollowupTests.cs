using Firefly.Core.Abilities;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class MayAbilityBlockedFollowupTests
    {
        private const string Persephone = "alliance-lux-r1-01";

        private static readonly CrewCatalog Crew = CrewCatalog.LoadDefault();
        private static readonly LeaderCatalog Leaders = LeaderCatalog.LoadDefault();
        private static readonly GearIndex Gear = GearIndex.LoadDefault();

        [Fact]
        public void Card_json_loads_blocked_followup_ability_batch()
        {
            Assert.Contains(
                Crew.Get("crew_meadows_piratesbountyhunters").Abilities,
                a => a.Type == AbilityTypes.RedirectKillApprehendSeize && !a.Mandatory);
            Assert.Contains(
                Crew.Get("crew_sheydra_bluesun").Abilities,
                a => a.Type == AbilityTypes.OncePerJobSkillSwitch
                     && a.Skill == "Fight"
                     && a.Subject == "Talk"
                     && a.JobOnly);
            Assert.Contains(
                Crew.Get("crew_stitch").Abilities,
                a => a.Type == AbilityTypes.OncePerJobSkillSwitch
                     && a.Skill == "Talk"
                     && a.Subject == "Fight");
            Assert.Contains(
                Crew.Get("crew_fess_kalidasa").Abilities,
                a => a.Type == AbilityTypes.DealWithNamedContact && a.Subject == "Higgins");
            Assert.Contains(
                Crew.Get("crew_holder_kalidasa").Abilities,
                a => a.Type == AbilityTypes.MakeWorkTakeFugitive);
            Assert.Contains(
                Leaders.Get("leader_wright_kalidasa").Abilities,
                a => a.Type == AbilityTypes.FugitiveDeliverBonus && a.Amount == 100);
            Assert.True(Gear.TryGet("gear_kaylees-fluffy-pink-dress", out var dress));
            Assert.Contains(dress.Abilities, a => a.Type == AbilityTypes.BuySupplyCardsUpTo && a.Amount == 3);
            Assert.True(Gear.TryGet("gear_earlys-datascope_piratesbountyhunters", out var scope));
            Assert.Contains(scope.Abilities, a => a.Type == AbilityTypes.WorkRevealDiscardSupply);
            Assert.True(Gear.TryGet("gear_universal-encyclopedia_breakinatmo", out var enc));
            Assert.Contains(enc.Abilities, a => a.Type == AbilityTypes.DealReorderMisbehave);
        }

        [Fact]
        public void Buy_defaults_to_max_2_cards_Dress_allows_3()
        {
            // GF9: Consider 3 / Buy 2. Dress: "may Buy up to 3 cards."
            var a = SupplyGear("gear_a", 100);
            var b = SupplyGear("gear_b", 100);
            var c = SupplyGear("gear_c", 100);
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 1000, fuel: 0);
            var game = new GameState(map, new[] { player }) { Gear = Gear };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            var market = new SupplyMarket("Persephone", System.Array.Empty<SupplyCard>());
            market.FaceUp.Add(a);
            market.FaceUp.Add(b);
            market.FaceUp.Add(c);
            game.SupplyDecks = new SupplyDecks(new[] { market });
            game.Supply = new SupplyCatalog(new[] { a, b, c });

            var buy = new BuyAction();
            Assert.False(buy.TryBuy(
                game, "p1",
                new BuyRequest { SupplyCardIds = { a.Id, b.Id, c.Id } },
                out _, out var error));
            Assert.Contains("at most 2", error);

            player.Gear.Add("gear_kaylees-fluffy-pink-dress");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_kaylees-fluffy-pink-dress", player.Roster.Leader!.Id, out _));
            Assert.Equal(3, AbilityDispatcher.BuySupplyCardsUpTo(game, player));
            Assert.True(buy.TryBuy(
                game, "p1",
                new BuyRequest { SupplyCardIds = { a.Id, b.Id, c.Id } },
                out var result, out error), error);
            Assert.Equal(3, result!.CardsBought.Count);
        }

        [Fact]
        public void Med_Bay_offers_reroll_per_Medic_Check_in_Kill_N()
        {
            // GF9: "Only make one Medic Check per Crew Killed."
            // Med Bay: "May re-roll Medic Checks." — each check may re-roll.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player })
            {
                Crew = Crew,
                Leaders = Leaders,
                ShipUpgradeCatalog = ShipUpgradeIndex.LoadDefault()
            };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_simon-tam"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_zoe"), out _));
            player.ShipUpgrades.Add("ship-upgrade_fully-equipped-med-bay");

            var choice = new KillChoice
            {
                VictimCrewIds = new[] { "crew_kaylee", "crew_zoe" }
            };
            Assert.False(CrewKill.TryKillUpTo(
                game, player, 2, ScriptedRng.FromDieFaces(2, 2), out _, out _, choice));
            Assert.Equal(PendingChoiceKinds.MedicReroll, game.PendingChoice!.Kind);
            Assert.Equal("crew_kaylee", choice.MedicPendingVictimId);

            Assert.False(CrewKill.TryResumeMedicRerollKillUpTo(
                game,
                new ChoiceSubmission { SelectedOptionId = SkillRerollOptions.Keep },
                ScriptedRng.FromDieFaces(2),
                out var killed1, out var error, choice), error);
            Assert.Equal(PendingChoiceKinds.MedicReroll, game.PendingChoice!.Kind);
            Assert.Equal("crew_zoe", choice.MedicPendingVictimId);

            Assert.True(CrewKill.TryResumeMedicRerollKillUpTo(
                game,
                new ChoiceSubmission { SelectedOptionId = SkillRerollOptions.Keep },
                ScriptedRng.FromDieFaces(1),
                out var killed2, out error, choice), error);
            Assert.Equal(2, killed1 + killed2);
        }

        [Fact]
        public void Meadows_Kill_N_offers_redirect_once()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player }) { Crew = Crew, Leaders = Leaders };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_meadows_piratesbountyhunters"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_zoe"), out _));

            var choice = new KillChoice
            {
                VictimCrewIds = new[] { "crew_kaylee", "crew_zoe" },
                AttemptMedicCheck = false
            };
            Assert.False(CrewKill.TryKillUpTo(
                game, player, 2, ScriptedRng.FromDieFaces(), out _, out _, choice));
            Assert.Equal(PendingChoiceKinds.MeadowsRedirect, game.PendingChoice!.Kind);

            Assert.True(CrewKill.TryResumeMeadowsRedirectKillUpTo(
                game,
                new ChoiceSubmission { SelectedOptionId = MeadowsRedirectOptions.KillMeadows },
                ScriptedRng.FromDieFaces(),
                out var killed, out var error, choice), error);
            Assert.Equal(2, killed);
            Assert.Null(player.Roster.Find("crew_meadows_piratesbountyhunters"));
            Assert.NotNull(player.Roster.Find("crew_kaylee")); // spared
            Assert.Null(player.Roster.Find("crew_zoe"));
        }

        [Fact]
        public void Fess_allows_remote_Deal_with_Higgins_only()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player })
            {
                Crew = Crew,
                Leaders = Leaders,
                Contacts = ContactCatalog.LoadDefault()
            };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_fess_kalidasa"), out _));
            AbilityDispatcher.RefreshDealModifiers(game, player);
            Assert.Equal("Higgins", player.Deal.NamedRemoteContact);

            Assert.True(game.Contacts!.TryFindByName("Magistrate Higgins", out var higgins));
            Assert.True(higgins.IsHiggins);
            Assert.True(DealAction.CanReachContact(game, player, higgins, atLocation: false, out _));
            Assert.True(game.Contacts.TryFindByName("Badger", out var badger));
            Assert.False(DealAction.CanReachContact(game, player, badger, atLocation: false, out var error));
            Assert.Contains("Badger", error);
        }

        [Fact]
        public void Make_Work_pays_200_Holder_may_take_Fugitive()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player }) { Crew = Crew, Leaders = Leaders };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_holder_kalidasa"), out _));

            var work = new WorkAction();
            Assert.False(work.TryMakeWork(game, "p1", out _, out _));
            Assert.Equal(PendingChoiceKinds.MakeWorkFugitive, game.PendingChoice!.Kind);

            Assert.True(work.TryResumeMakeWorkFugitive(
                game,
                new ChoiceSubmission { SelectedOptionId = HolderFugitiveOptions.TakeFugitive },
                out var result, out var error), error);
            Assert.Equal(200, result!.Pay);
            Assert.Equal(1, result.FugitivesGained);
            Assert.Equal(1, player.Fugitives);
            Assert.Equal(200, player.Cash);
        }

        [Fact]
        public void Datascope_Work_discards_top_3_Supply()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player }) { Gear = Gear, Leaders = Leaders };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            player.Gear.Add("gear_earlys-datascope_piratesbountyhunters");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_earlys-datascope_piratesbountyhunters",
                player.Roster.Leader!.Id, out _));

            var deckCards = new[]
            {
                SupplyGear("g1", 100), SupplyGear("g2", 100), SupplyGear("g3", 100), SupplyGear("g4", 100)
            };
            var market = new SupplyMarket("Persephone", deckCards);
            game.SupplyDecks = new SupplyDecks(new[] { market });
            Assert.Equal(4, market.Deck.Count);

            var work = new WorkAction();
            Assert.True(work.TryDatascope(game, "p1", out var result, out var error), error);
            Assert.Equal(3, result!.SupplyDiscarded);
            Assert.Equal(3, market.Discard.Count);
            Assert.Equal(1, market.Deck.Count);
        }

        [Fact]
        public void Encyclopedia_sets_remote_Deal_and_reorders_Misbehave()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var mb = MisbehaveCatalog.LoadDefault();
            var deck = MisbehaveDeck.FromCatalog(mb, new SystemRng(3));
            var game = new GameState(map, new[] { player })
            {
                Gear = Gear,
                Leaders = Leaders,
                Misbehave = deck,
                Contacts = ContactCatalog.LoadDefault(),
                ContactDecks = new ContactDecks(JobCatalog.LoadDefault(), new SystemRng(3)),
                Jobs = JobCatalog.LoadDefault()
            };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            player.Gear.Add("gear_universal-encyclopedia_breakinatmo");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_universal-encyclopedia_breakinatmo",
                player.Roster.Leader!.Id, out _));
            AbilityDispatcher.RefreshDealModifiers(game, player);
            Assert.Equal(3, player.Deal.ReorderMisbehaveTop);

            var top = game.Misbehave!.PeekTop(3);
            Assert.Equal(3, top.Count);
            var reversed = new[] { top[2].Id, top[1].Id, top[0].Id };

            Assert.True(game.Contacts!.TryFindByName("Badger", out var badger));
            player.SectorId = map.TryResolveName("Persephone", out var sec) ? sec.Id : Persephone;
            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = badger.Name,
                ConsiderCount = 0,
                MisbehaveReorderIds = reversed
            }, out _, out var error), error);

            var after = game.Misbehave.PeekTop(3);
            Assert.Equal(reversed[0], after[0].Id);
            Assert.Equal(reversed[1], after[1].Id);
            Assert.Equal(reversed[2], after[2].Id);
        }

        [Fact]
        public void OncePerJobSkillSwitch_Sheydra_Fight_to_Talk()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player }) { Crew = Crew, Leaders = Leaders };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_sheydra_bluesun"), out _));
            var active = new ActiveJob("job_test");
            player.ActiveJobs.Add(active);

            var check = new SkillCheck(Skill.Fight, 5);
            var choice = new SkillCheckChoice();
            Assert.True(SkillCheck.NeedsSkillSwitchChoice(
                player, check, choice, active, AbilityContext.Misbehaving));
            choice.AcceptSkillSwitch = true;
            var switched = SkillCheck.ApplySkillSwitchIfChosen(
                player, check, choice, active, AbilityContext.Misbehaving);
            Assert.Equal(Skill.Talk, switched.Skill);
            Assert.True(active.SkillSwitchUsedThisJob);
            Assert.False(SkillCheck.NeedsSkillSwitchChoice(
                player, switched, new SkillCheckChoice(), active, AbilityContext.Misbehaving));
        }

        private static SupplyCard SupplyGear(string id, int cost) =>
            new SupplyCard(
                id, id, cost, SupplyKind.Gear,
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Persephone"] = 1 });
    }
}
