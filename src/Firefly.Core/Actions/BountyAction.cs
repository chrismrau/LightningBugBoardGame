using System;
using System.Text.RegularExpressions;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public enum BountyHuntKind
    {
        Confrontation,
        LoneTarget,
        Betrayal,
        Jump
    }

    public sealed class BountyResult
    {
        public BountyHuntKind Kind { get; }
        public string BountyId { get; }
        public bool Success { get; }
        public bool BoardingFailed { get; }
        public int Pay { get; }
        public int MoralDisgruntled { get; }
        public int CrewKilled { get; }
        public bool Rescued { get; }
        public ShowdownResult? Showdown { get; }

        public BountyResult(
            BountyHuntKind kind,
            string bountyId,
            bool success,
            bool boardingFailed = false,
            int pay = 0,
            int moralDisgruntled = 0,
            int crewKilled = 0,
            bool rescued = false,
            ShowdownResult? showdown = null)
        {
            Kind = kind;
            BountyId = bountyId;
            Success = success;
            BoardingFailed = boardingFailed;
            Pay = pay;
            MoralDisgruntled = moralDisgruntled;
            CrewKilled = crewKilled;
            Rescued = rescued;
            Showdown = showdown;
        }
    }

    /// <summary>
    /// Pirates & Bounty Hunters bounty hunting (PBH pp.5–8).
    /// Work action. Face-up Most Wanted only. Does not use a job-hand
    /// or active-job slot. One fugitive per Work action (Cortex jump
    /// is the printed exception).
    /// </summary>
    public sealed class BountyAction
    {
        public const int BoardingTarget = 6;
        private static readonly Regex BonusPattern = new Regex(
            @"Bounty Bonus[:\s]*\+?\$(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public bool TryApprehendRival(
            GameState game,
            string playerId,
            string bountyId,
            string rivalId,
            string crewId,
            Skill attackSkill,
            Skill defendSkill,
            Skill boardSkill,
            IRng rng,
            out BountyResult? result,
            out string? error)
        {
            result = null;
            if (!BeginWork(game, playerId, bountyId, out var player, out var bounty, out error))
                return false;
            if (string.Equals(playerId, rivalId, StringComparison.Ordinal))
            {
                error = "Use betrayal to nab your own crew.";
                return false;
            }

            var rival = game.GetPlayer(rivalId);
            if (player.SectorId != rival.SectorId)
            {
                error = "Must share a sector with that ship.";
                return false;
            }
            var member = rival.Roster.Find(crewId);
            if (member == null || !bounty.MatchesCrewName(member.Name))
            {
                error = "That crew is not the wanted fugitive on that ship.";
                return false;
            }
            if (member.IsLeader)
            {
                error = "Cannot bind a Leader.";
                return false;
            }

            if (!PassBoarding(player, boardSkill, rng))
            {
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Confrontation, bounty.Id, success: false, boardingFailed: true);
                return true;
            }

            var showdown = Showdown.Resolve(Showdown.Of(player, attackSkill), Showdown.Of(rival, defendSkill), rng);
            if (!showdown.AttackerWins)
            {
                var killed = ApplyBotch(player, bounty);
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Confrontation, bounty.Id, false, crewKilled: killed, showdown: showdown);
                return true;
            }

            rival.Roster.Remove(member.Id);
            Bind(game, player, bounty, member.Card);
            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.Confrontation, bounty.Id, true, showdown: showdown);
            return true;
        }

        public bool TryApprehendLone(
            GameState game,
            string playerId,
            string bountyId,
            string crewId,
            Skill attackSkill,
            IRng rng,
            out BountyResult? result,
            out string? error)
        {
            result = null;
            if (!BeginWork(game, playerId, bountyId, out var player, out var bounty, out error))
                return false;
            if (game.Crew == null || !game.Crew.TryGet(crewId, out var crew))
            {
                error = $"Unknown crew '{crewId}'.";
                return false;
            }
            if (!bounty.MatchesCrewName(crew.Name))
            {
                error = $"{crew.Name} is not this bounty.";
                return false;
            }
            if (HiredAnywhere(game, crew.Id) || BoundAnywhere(game, crew.Id))
            {
                error = $"{crew.Name} is already in play.";
                return false;
            }
            if (!bounty.IsCortex)
            {
                if (string.IsNullOrWhiteSpace(bounty.PickupPlanet)
                    || !AtPlanet(game, player, bounty.PickupPlanet))
                {
                    error = $"Must be at {bounty.PickupPlanet} to nab {crew.Name}.";
                    return false;
                }
            }

            var showdown = Showdown.Resolve(Showdown.Of(player, attackSkill), Showdown.BestSkill(crew), rng);
            if (!showdown.AttackerWins)
            {
                var killed = ApplyBotch(player, bounty);
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.LoneTarget, bounty.Id, false, crewKilled: killed, showdown: showdown);
                return true;
            }

            PullCrewFromSupply(game, crew);
            Bind(game, player, bounty, crew);
            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.LoneTarget, bounty.Id, true, showdown: showdown);
            return true;
        }

        public bool TryBetray(
            GameState game,
            string playerId,
            string bountyId,
            string crewId,
            out BountyResult? result,
            out string? error)
        {
            result = null;
            if (!BeginWork(game, playerId, bountyId, out var player, out var bounty, out error))
                return false;
            var member = player.Roster.Find(crewId);
            if (member == null || !bounty.MatchesCrewName(member.Name))
            {
                error = "That crew is not the wanted fugitive on your ship.";
                return false;
            }
            if (member.IsLeader)
            {
                error = "Cannot bind a Leader.";
                return false;
            }

            player.Roster.Remove(member.Id);
            var disgruntled = player.Roster.DisgruntleWhere(m => !m.IsLeader);
            Bind(game, player, bounty, member.Card);
            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.Betrayal, bounty.Id, true, moralDisgruntled: disgruntled);
            return true;
        }

        public bool TryDeliver(
            GameState game,
            string playerId,
            string bountyId,
            out BountyResult? result,
            out string? error)
        {
            result = null;
            if (!CanStart(game, playerId, out var player, out error))
                return false;
            if (game.Bounties == null || !game.Bounties.TryResolve(bountyId, out var bounty))
            {
                error = $"Unknown bounty '{bountyId}'.";
                return false;
            }
            var bound = FindBound(player, bounty.Id);
            if (bound == null || bound.Count == 0)
            {
                error = "You are not transporting that bounty.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(bounty.DropoffPlanet)
                || !AtPlanet(game, player, bounty.DropoffPlanet))
            {
                error = $"Must deliver at {bounty.DropoffPlanet}.";
                return false;
            }

            var pay = bounty.Pay * bound.Count + LawmanBonus(player) * bound.Count;
            player.Cash += pay;
            var moral = 0;
            if (bounty.Immoral)
                moral = player.Roster.DisgruntleMoral();
            foreach (var id in bound.CrewIds)
            {
                if (game.Crew != null && game.Crew.TryGet(id, out var crew))
                    game.RemovedFromPlay.Add(crew.Name);
            }
            player.BoundBounties.Remove(bound);
            game.BountyDeck?.RemoveFromGame(bounty);
            game.RemovedFromPlay.Add(bounty.Name);
            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.LoneTarget, bounty.Id, true, pay: pay, moralDisgruntled: moral);
            return true;
        }

        public bool TryJump(
            GameState game,
            string playerId,
            string fromPlayerId,
            string bountyId,
            Skill attackSkill,
            Skill defendSkill,
            Skill boardSkill,
            IRng rng,
            bool rescue,
            out BountyResult? result,
            out string? error)
        {
            result = null;
            if (!CanStart(game, playerId, out var player, out error))
                return false;
            if (string.Equals(playerId, fromPlayerId, StringComparison.Ordinal))
            {
                error = "Cannot jump your own bounty.";
                return false;
            }
            var rival = game.GetPlayer(fromPlayerId);
            if (player.SectorId != rival.SectorId)
            {
                error = "Must share a sector with that ship.";
                return false;
            }
            if (game.Bounties == null || !game.Bounties.TryResolve(bountyId, out var bounty))
            {
                error = $"Unknown bounty '{bountyId}'.";
                return false;
            }
            var bound = FindBound(rival, bounty.Id);
            if (bound == null)
            {
                error = "That ship is not transporting that bounty.";
                return false;
            }

            if (!PassBoarding(player, boardSkill, rng))
            {
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Jump, bounty.Id, false, boardingFailed: true);
                return true;
            }

            var showdown = Showdown.Resolve(Showdown.Of(player, attackSkill), Showdown.Of(rival, defendSkill), rng);
            if (!showdown.AttackerWins)
            {
                var killed = ApplyBotch(player, bounty);
                if (!game.TryConsumeAction(TurnAction.Work, out error))
                    return false;
                result = new BountyResult(BountyHuntKind.Jump, bounty.Id, false, crewKilled: killed, showdown: showdown);
                return true;
            }

            rival.BoundBounties.Remove(bound);
            if (rescue)
            {
                foreach (var id in bound.CrewIds)
                {
                    if (game.Crew != null && game.Crew.TryGet(id, out var crew))
                        player.Roster.TryHire(crew, out _);
                }
                game.BountyDeck?.ReturnToBottom(bounty);
            }
            else
            {
                player.BoundBounties.Add(bound);
            }

            if (!game.TryConsumeAction(TurnAction.Work, out error))
                return false;
            result = new BountyResult(BountyHuntKind.Jump, bounty.Id, true, rescued: rescue, showdown: showdown);
            return true;
        }

        private static bool BeginWork(
            GameState game,
            string playerId,
            string bountyId,
            out PlayerState player,
            out BountyCard bounty,
            out string? error)
        {
            bounty = null!;
            if (!CanStart(game, playerId, out player, out error))
                return false;
            if (game.BountyDeck == null)
            {
                error = "Bounty deck is not in play.";
                return false;
            }
            var face = game.BountyDeck.FindWanted(bountyId);
            var already = FindBound(player, bountyId);
            if (face == null && already == null)
            {
                error = "That bounty is not on the Most Wanted List.";
                return false;
            }
            bounty = face ?? game.Bounties!.Get(already!.BountyId);
            return true;
        }

        private static bool CanStart(GameState game, string playerId, out PlayerState player, out string? error)
        {
            player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            return game.CanTakeAction(TurnAction.Work, out error);
        }

        private static void Bind(GameState game, PlayerState player, BountyCard bounty, CrewCard crew)
        {
            var bound = FindBound(player, bounty.Id);
            if (bound == null)
            {
                if (game.BountyDeck != null && game.BountyDeck.FindWanted(bounty.Id) != null)
                    game.BountyDeck.TryClaimWanted(bounty.Id, out _);
                bound = new BoundBounty(bounty.Id, bounty.Name);
                player.BoundBounties.Add(bound);
            }
            bound.CrewIds.Add(crew.Id);
        }

        private static BoundBounty? FindBound(PlayerState player, string bountyId)
        {
            foreach (var bound in player.BoundBounties)
            {
                if (string.Equals(bound.BountyId, bountyId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(bound.BountyName, bountyId, StringComparison.OrdinalIgnoreCase))
                    return bound;
            }
            return null;
        }

        private static bool PassBoarding(PlayerState player, Skill boardSkill, IRng rng)
        {
            if (boardSkill == Skill.Fight)
                boardSkill = Skill.Talk;
            var total = Showdown.Of(player, boardSkill) + Dice.D6(rng);
            return total >= BoardingTarget;
        }

        private static int ApplyBotch(PlayerState player, BountyCard bounty) =>
            player.Roster.KillUpTo(bounty.BotchKill);

        private static bool AtPlanet(GameState game, PlayerState player, string planet)
        {
            if (planet.Equals("Command Cruiser", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrEmpty(game.Tokens.AllianceCruiserSectorId)
                    && game.Tokens.AllianceCruiserSectorId == player.SectorId;
            if (!game.Map.TryGet(player.SectorId, out var sector))
                return false;
            var here = BuyAction.ShopPlanet(sector);
            return string.Equals(here, planet, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sector.DisplayName, planet, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HiredAnywhere(GameState game, string crewId)
        {
            foreach (var player in game.Players)
            {
                if (player.Roster.Find(crewId) != null)
                    return true;
            }
            return false;
        }

        private static bool BoundAnywhere(GameState game, string crewId)
        {
            foreach (var player in game.Players)
            {
                foreach (var bound in player.BoundBounties)
                {
                    foreach (var id in bound.CrewIds)
                    {
                        if (id == crewId)
                            return true;
                    }
                }
            }
            return false;
        }

        private static void PullCrewFromSupply(GameState game, CrewCard crew)
        {
            if (game.SupplyDecks == null)
                return;
            foreach (var market in game.SupplyDecks.Markets)
            {
                Strip(market.Deck, crew.Id);
                Strip(market.FaceUp, crew.Id);
                Strip(market.Discard, crew.Id);
                market.Refill();
            }
        }

        private static void Strip(System.Collections.Generic.IList<SupplyCard> pile, string crewId)
        {
            for (var i = pile.Count - 1; i >= 0; i--)
            {
                if (pile[i].Id == crewId)
                    pile.RemoveAt(i);
            }
        }

        public static int LawmanBonus(PlayerState player)
        {
            var total = 0;
            foreach (var member in player.Roster.Members)
            {
                var text = member.Card.Description;
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var match = BonusPattern.Match(text);
                if (match.Success)
                    total += int.Parse(match.Groups[1].Value);
            }
            return total;
        }
    }
}
