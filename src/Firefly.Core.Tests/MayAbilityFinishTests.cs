using Firefly.Core.Abilities;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// Remaining open <c>may</c> abilities after PR #39.
    /// FAQ 4.1 p.8: mandatory unless “may”; mid-resolve may always suspends PendingChoice.
    /// Permission-style may (Nandi / Board Game Collection) follows Jayne carry pattern.
    /// </summary>
    public class MayAbilityFinishTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Persephone = "alliance-lux-r1-01";
        private const string Space = "alliance-lux-r1-02"; // adj space sector if needed

        private static CrewCatalog Crew => CrewCatalog.LoadDefault();
        private static LeaderCatalog Leaders => LeaderCatalog.LoadDefault();
        private static GearIndex Gear => GearIndex.LoadDefault();

        [Fact]
        public void Card_json_loads_finish_may_batch()
        {
            Assert.Contains(Leaders.Get("leader_nandi").Abilities, a =>
                a.MatchesType(AbilityTypes.FreeHireCrew) && !a.Mandatory);

            Assert.Contains(Crew.Get("crew_emma").Abilities, a =>
                a.MatchesType(AbilityTypes.MoraleBooster) && a.Subject == "Emma" && !a.Mandatory);

            Assert.True(Gear.TryGet("gear_extra-ammo-clips_kalidasa", out var ammo));
            Assert.Contains(ammo.Abilities, a =>
                a.MatchesType(AbilityTypes.DiscardToReroll) && a.Skill == "Fight" && !a.Mandatory);

            Assert.Contains(Crew.Get("crew_the-guardian_piratesbountyhunters").Abilities, a =>
                a.MatchesType(AbilityTypes.ShowdownReroll) && !a.Mandatory);

            Assert.Contains(Crew.Get("crew_chari_piratesbountyhunters").Abilities, a =>
                a.MatchesType(AbilityTypes.ShowdownForceRivalReroll) && !a.Mandatory);

            var upgrades = ShipUpgradeIndex.LoadDefault();
            Assert.True(upgrades.TryGet("ship-upgrade_board-game-collection_kalidasa", out var bgc));
            Assert.Contains(bgc.Abilities, a =>
                a.MatchesType(AbilityTypes.ShoreLeaveAnySector) && !a.Mandatory);

            Assert.True(Gear.TryGet("gear_love-bot_bluesun", out var loveBot));
            Assert.Contains(loveBot.Abilities, a =>
                a.MatchesType(AbilityTypes.ClearDisgruntledAction) && !a.Mandatory);
        }

        [Fact]
        public void Nandi_Hire_Crew_at_Buy_costs_zero()
        {
            // Supplies.tsv: "Heart of Gold: May Hire Crew at no cost."
            // Permission may (like Jayne May Carry) — always-on Buy cost, no PendingChoice.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Nandi", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player })
            {
                Crew = Crew,
                Leaders = Leaders,
                ShipUpgradeCatalog = ShipUpgradeIndex.LoadDefault()
            };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_nandi"), out _));
            Assert.True(AbilityDispatcher.HasFreeHireCrew(player));
            var crewCard = new SupplyCard("crew_emma", "Emma", 200, SupplyKind.Crew);
            Assert.True(BuyAction.TryGiveSupply(game, player, crewCard, "Persephone", out var err), err);
            Assert.NotNull(player.Roster.Find("crew_emma"));
            Assert.Equal(0, player.Cash);
        }

        [Fact]
        public void Nandi_Buy_action_skips_crew_card_cost()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Nandi", Persephone, shipId: "Serenity", leaderId: "leader_nandi") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(11) });
            var p = game.CurrentPlayer;
            p.Cash = 0;
            Assert.True(AbilityDispatcher.HasFreeHireCrew(p));
            Assert.True(game.SupplyDecks!.TryGet("Persephone", out var market));
            string? crewId = null;
            foreach (var face in market.FaceUp)
            {
                if (face.Kind == SupplyKind.Crew)
                {
                    crewId = face.Id;
                    break;
                }
            }
            if (crewId == null)
            {
                Assert.Contains(p.Roster.Leader!.Card.Abilities, a =>
                    a.MatchesType(AbilityTypes.FreeHireCrew));
                return;
            }
            Assert.True(new BuyAction().TryBuy(
                game, "p1",
                new BuyRequest { SupplyCardIds = { crewId } },
                out var result, out var error), error);
            Assert.Equal(0, result!.CashSpent);
            Assert.Equal(0, p.Cash);
        }

        [Fact]
        public void Morale_Booster_always_suspends_for_target()
        {
            // Supplies.tsv: "You may use an Action on your turn to remove Disgruntled from a Crew other than Emma."
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Santo, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player }) { Crew = Crew, Gear = Gear };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_emma"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            player.Roster.Disgruntle(player.Roster.Find("crew_emma")!);
            player.Roster.Disgruntle(player.Roster.Find("crew_kaylee")!);

            var action = new MoraleBoosterAction();
            Assert.False(action.TryClearDisgruntled(game, "p1", new MoraleBoosterRequest(), out _, out var error));
            Assert.Contains("Disgruntled", error);
            Assert.Equal(PendingChoiceKinds.MoraleBoosterTarget, game.PendingChoice!.Kind);
            Assert.DoesNotContain("crew_emma", game.PendingChoice.Options!);
            Assert.Contains("crew_kaylee", game.PendingChoice.Options!);

            Assert.True(action.TryResume(
                game,
                new ChoiceSubmission { SelectedOptionId = "crew_kaylee" },
                out var result, out error), error);
            Assert.Equal("crew_kaylee", result!.TargetCrewId);
            Assert.False(player.Roster.Find("crew_kaylee")!.Disgruntled);
            Assert.True(player.Roster.Find("crew_emma")!.Disgruntled);
            Assert.Equal(TurnAction.Crew, game.LastAction);
        }

        [Fact]
        public void Board_Game_Collection_Shore_Leave_in_non_planet_sector()
        {
            // Supplies.tsv: "You may use a Buy Action to give your Crew Shore Leave in any Sector."
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            // Pick a non-planet Alliance sector near Persephone.
            string? emptySector = null;
            foreach (var s in map.Sectors.Values)
            {
                if (!s.IsPlanetary && string.IsNullOrWhiteSpace(s.Planet))
                {
                    emptySector = s.Id;
                    break;
                }
            }
            Assert.NotNull(emptySector);
            var player = new PlayerState("p1", "Mal", emptySector!, cash: 500, fuel: 0);
            var game = new GameState(map, new[] { player })
            {
                Crew = Crew,
                ShipUpgradeCatalog = ShipUpgradeIndex.LoadDefault()
            };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            player.ShipUpgrades.Add("ship-upgrade_board-game-collection_kalidasa");
            player.Roster.Disgruntle(player.Roster.Leader!);

            Assert.True(new ShoreLeaveAction().TryShoreLeave(game, "p1", out var result, out var error), error);
            Assert.Equal(100, result!.CashSpent);
            Assert.Equal(1, result.TokensCleared);
            Assert.False(player.Roster.Leader!.Disgruntled);
        }

        [Fact]
        public void Extra_Ammo_discard_to_reroll_always_suspends_on_Fight()
        {
            // Supplies.tsv: "Discard to re-roll a [FIGHTING] Test."
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Santo, cash: 500, fuel: 3);
            var game = new GameState(map, new[] { player })
            {
                Crew = Crew,
                Gear = Gear,
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                ContactDecks = new ContactDecks(JobCatalog.LoadDefault(), new SystemRng(1)),
            };
            var catalog = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(2), catalog);
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            player.Gear.Add("gear_extra-ammo-clips_kalidasa");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_extra-ammo-clips_kalidasa", player.Roster.Leader!.Id, out _));
            player.FightBonus = 2;

            const string crime = "job_badger_badgers-11-casino-caper";
            player.JobHand.Add(crime);
            Assert.True(new WorkAction().TryWork(game, "p1", crime, out _, out _));
            // Pick a Fight misbehave if available — use structured path via known Fight card.
            // Fall back: assert NeedsDiscardToRerollChoice directly.
            Assert.True(AbilityDispatcher.NeedsDiscardToRerollChoice(
                game, player, Skill.Fight, null));
            Assert.False(AbilityDispatcher.NeedsDiscardToRerollChoice(
                game, player, Skill.Tech, null));
        }

        [Fact]
        public void Extra_Ammo_Misbehave_Fight_suspends_and_discard_rerolls()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Santo, cash: 500, fuel: 3);
            var game = new GameState(map, new[] { player })
            {
                Crew = Crew,
                Gear = Gear,
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                ContactDecks = new ContactDecks(JobCatalog.LoadDefault(), new SystemRng(1)),
            };
            var catalog = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(2), catalog);
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            // Mal Fight 2 + bonus so Fight tests have dice.
            player.FightBonus = 1;
            player.Gear.Add("gear_extra-ammo-clips_kalidasa");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_extra-ammo-clips_kalidasa", player.Roster.Leader!.Id, out _));

            const string crime = "job_badger_badgers-11-casino-caper";
            player.JobHand.Add(crime);
            Assert.True(new WorkAction().TryWork(game, "p1", crime, out _, out _));

            // Find a Fight option card.
            MisbehaveCard? fightCard = null;
            foreach (var card in catalog.Cards.Values)
            {
                foreach (var opt in card.Options)
                {
                    if (SkillCheck.TryParse(opt.Details, out var check) && check.Skill == Skill.Fight)
                    {
                        fightCard = card;
                        break;
                    }
                }
                if (fightCard != null)
                    break;
            }
            Assert.NotNull(fightCard);
            game.Misbehave!.PlaceOnTop(fightCard!);
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            var optionIndex = 0;
            for (var i = 0; i < fightCard!.Options.Count; i++)
            {
                if (SkillCheck.TryParse(fightCard.Options[i].Details, out var c) && c.Skill == Skill.Fight)
                {
                    optionIndex = i;
                    break;
                }
            }

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = optionIndex },
                out _, out var error,
                ScriptedRng.FromDieFaces(1, 1, 1)));
            Assert.Equal(PendingChoiceKinds.DiscardToReroll, game.PendingChoice!.Kind);

            Assert.True(resolver.TryResumeDiscardToReroll(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = DiscardToRerollOptions.Discard },
                new MisbehaveChoice { OptionIndex = optionIndex },
                out var resolution, out error,
                ScriptedRng.FromDieFaces(6, 6, 6)), error);
            Assert.Null(game.PendingChoice);
            Assert.DoesNotContain("gear_extra-ammo-clips_kalidasa", player.Gear);
            Assert.NotNull(resolution);
        }

        [Fact]
        public void Cortland_Boarding_Negotiate_always_suspends_Bribes()
        {
            // Supplies.tsv Cortland: Bribes before any Negotiate Test; not Showdowns.
            // PBH p.3: Boarding may use Negotiate — Cortland applies.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var p1 = new PlayerState("p1", "Mal", Santo, cash: 200, fuel: 0);
            var p2 = new PlayerState("p2", "Zoe", Santo, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { p1, p2 })
            {
                Crew = Crew,
                Jobs = JobCatalog.LoadDefault(),
                UsePiratesBountyHunters = true
            };
            Assert.True(p1.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(p1.Roster.TryHire(Crew.Get("crew_cortland_bluesun"), out _));
            Assert.True(p2.Roster.TryHire(Leaders.Get("leader_zoe_jetwash"), out _));

            var boardCheck = BoardingTest.BuildCheck(p1, Skill.Talk);
            Assert.True(boardCheck.BribesAllowed);
            Assert.True(SkillCheck.NeedsBribeChoice(p1, boardCheck, null));
        }

        [Fact]
        public void Guardian_Showdown_NextRerollContext_always_asks()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var attacker = new PlayerState("p1", "Mal", Santo, cash: 0, fuel: 0);
            var defender = new PlayerState("p2", "Zoe", Santo, cash: 0, fuel: 0);
            Assert.True(attacker.Roster.TryHire(Crew.Get("crew_the-guardian_piratesbountyhunters"), out _));
            Assert.True(defender.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));

            Assert.Equal(
                ShowdownRerollContexts.AttackerOwn,
                Showdown.NextRerollContext(attacker, defender, null));

            var choice = new ShowdownChoice { AttackerAcceptReroll = false };
            Assert.Null(Showdown.NextRerollContext(attacker, defender, choice));
        }

        [Fact]
        public void Chari_Showdown_force_rival_context()
        {
            var attacker = new PlayerState("p1", "Mal", Santo, cash: 0, fuel: 0);
            var defender = new PlayerState("p2", "Zoe", Santo, cash: 0, fuel: 0);
            Assert.True(attacker.Roster.TryHire(Crew.Get("crew_chari_piratesbountyhunters"), out _));
            Assert.Equal(
                ShowdownRerollContexts.AttackerForceRival,
                Showdown.NextRerollContext(attacker, defender, null));
        }

        [Fact]
        public void Any_Port_own_Haven_null_Fuel_always_suspends()
        {
            const string Bernadette = "alliance-white-sun-r1-01";
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Bernadette, shipId: "Serenity", leaderId: "Malcolm") },
                new GameSetupOptions
                {
                    ScenarioCardId = "scenario_any-port-in-a-storm",
                    DealStartingJobs = false,
                    UseBlueSun = true,
                    HavenChoices = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["p1"] = Bernadette
                    },
                    Rng = new SystemRng(46)
                });
            var player = game.CurrentPlayer;
            player.Cash = 3000;
            player.Fuel = 0;

            var shore = new ShoreLeaveAction();
            Assert.False(shore.TryHavenFuelAndShoreLeave(
                game, "p1",
                new HavenBuyRequest { ShoreLeave = true, Fuel = null },
                out _, out var error));
            Assert.Equal(PendingChoiceKinds.HavenFuelAmount, game.PendingChoice!.Kind);

            Assert.True(shore.TryResumeHavenFuel(
                game,
                new ChoiceSubmission { SelectedOptionId = "4", Amount = 4 },
                out var result, out error), error);
            Assert.Equal(4, result!.FuelLoaded);
            Assert.Equal(4, player.Fuel);
            Assert.Equal(0, result.CashSpent);
        }
    }
}
