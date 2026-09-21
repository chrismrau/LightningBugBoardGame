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
    /// Bree Black Market Ties: May sell Parts to any Solid Contact for $300.
    /// Supplies.tsv / Crew.json sellPartsToSolidContact.
    /// </summary>
    public class BreeDealSellTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private static readonly CrewCatalog Crew = CrewCatalog.LoadDefault();
        private static readonly LeaderCatalog Leaders = LeaderCatalog.LoadDefault();

        private static GameState NewGame()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 2000, fuel: 0);
            var game = new GameState(map, new[] { player })
            {
                Crew = Crew,
                Leaders = Leaders,
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault()
            };
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            return game;
        }

        [Fact]
        public void Bree_ability_is_typed_optional_sellPartsToSolidContact()
        {
            var bree = Crew.Get("crew_bree");
            Assert.Contains(
                bree.Abilities,
                a => a.Type == AbilityTypes.SellPartsToSolidContact
                    && a.Amount == 300
                    && !a.Mandatory);
        }

        [Fact]
        public void Bree_Solid_Deal_suspends_for_Parts_sell_then_pays_300_each()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_bree"), out _));
            Assert.True(game.Contacts!.TryFindByName("Badger", out var badger));
            ContactSolidBenefits.BecomeSolid(game, player, badger.Id);
            player.Parts = 2;

            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger" }, out _, out _));
            Assert.Equal(PendingChoiceKinds.DealSellParts, game.PendingChoice!.Kind);
            Assert.False(game.ActionWasUsed(TurnAction.Deal));

            Assert.True(deal.TryResumeDealSellParts(
                game,
                new ChoiceSubmission { Amount = 2 },
                out var result, out var error), error);
            Assert.Equal(2, result!.PartsSold);
            Assert.Equal(600, result.CashFromSales);
            Assert.Equal(0, player.Parts);
            Assert.Equal(2600, player.Cash);
            Assert.True(game.ActionWasUsed(TurnAction.Deal));
            Assert.Null(game.PendingChoice);
        }

        [Fact]
        public void Bree_may_decline_Parts_sell_with_Amount_zero()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_bree"), out _));
            Assert.True(game.Contacts!.TryFindByName("Badger", out var badger));
            ContactSolidBenefits.BecomeSolid(game, player, badger.Id);
            player.Parts = 1;

            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger" }, out _, out _));
            Assert.True(deal.TryResumeDealSellParts(
                game,
                new ChoiceSubmission { Amount = 0 },
                out var result, out var error), error);
            Assert.Equal(0, result!.PartsSold);
            Assert.Equal(0, result.CashFromSales);
            Assert.Equal(1, player.Parts);
            Assert.Equal(2000, player.Cash);
        }

        [Fact]
        public void Bree_scripted_SellParts_skips_PendingChoice()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_bree"), out _));
            Assert.True(game.Contacts!.TryFindByName("Badger", out var badger));
            ContactSolidBenefits.BecomeSolid(game, player, badger.Id);
            player.Parts = 3;

            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Badger",
                SellParts = 1
            }, out var result, out var error), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(1, result!.PartsSold);
            Assert.Equal(300, result.CashFromSales);
            Assert.Equal(2, player.Parts);
            Assert.Equal(2300, player.Cash);
        }

        [Fact]
        public void Without_Solid_Bree_does_not_suspend_and_cannot_sell_Parts()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_bree"), out _));
            player.Parts = 2;

            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger" }, out _, out var ok), ok);
            Assert.Null(game.PendingChoice);
            Assert.Equal(2, player.Parts);

            game.EndTurn();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Badger",
                SellParts = 1
            }, out _, out var error));
            Assert.Contains("Solid Contact", error);
        }

        [Fact]
        public void Without_Bree_Parts_sell_is_rejected()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(game.Contacts!.TryFindByName("Badger", out var badger));
            ContactSolidBenefits.BecomeSolid(game, player, badger.Id);
            player.Parts = 1;

            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger" }, out _, out _));
            Assert.Null(game.PendingChoice);

            game.EndTurn();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Badger",
                SellParts = 1
            }, out _, out var error));
            Assert.Contains("Bree", error);
        }

        [Fact]
        public void Remote_Deal_cannot_sell_Parts_even_with_Bree_Solid()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_bree"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_fess_kalidasa"), out _));
            AbilityDispatcher.RefreshDealModifiers(game, player);
            Assert.True(game.Contacts!.TryFindByName("Magistrate Higgins", out var higgins));
            ContactSolidBenefits.BecomeSolid(game, player, higgins.Id);
            player.Parts = 1;
            Assert.False(DealAction.IsAtContact(game, player, higgins));

            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Magistrate Higgins",
                SellParts = 1
            }, out _, out var error));
            Assert.Contains("Contact's sector", error);
        }
    }
}
