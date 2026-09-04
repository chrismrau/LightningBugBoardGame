using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public enum MisbehaveOutcome
    {
        Proceed,
        Botched,
        Replaced
    }

    public sealed class MisbehaveChoice
    {
        public int OptionIndex { get; set; }
        public bool UseAce { get; set; }
        public bool PayDisgruntledCuts { get; set; }
        public bool AcceptPay { get; set; } = true;
        public string? TargetCrewId { get; set; }
        public string? LoseSolidId { get; set; }
        public int DiscardWarrants { get; set; }
    }

    public sealed class MisbehaveResolution
    {
        public MisbehaveCard Card { get; }
        public MisbehaveOption? Option { get; }
        public MisbehaveOutcome Outcome { get; }
        public SkillCheckResult? SkillCheck { get; }
        public int WarrantsIssued { get; }
        public int CrewKilled { get; }
        public int GoodsLoaded { get; }
        public int CashDelta { get; }
        public bool UsedAce { get; }
        public WorkResult? Work { get; }

        public MisbehaveResolution(
            MisbehaveCard card,
            MisbehaveOption? option,
            MisbehaveOutcome outcome,
            SkillCheckResult? skillCheck,
            int warrantsIssued,
            int crewKilled,
            int goodsLoaded,
            int cashDelta,
            bool usedAce,
            WorkResult? work)
        {
            Card = card;
            Option = option;
            Outcome = outcome;
            SkillCheck = skillCheck;
            WarrantsIssued = warrantsIssued;
            CrewKilled = crewKilled;
            GoodsLoaded = goodsLoaded;
            CashDelta = cashDelta;
            UsedAce = usedAce;
            Work = work;
        }
    }

    public sealed class MisbehaveResolver
    {
        private static readonly Regex RequiresPattern = new Regex(
            @"Requires\s*:?\s*([^.;]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BandPattern = new Regex(
            @"(\d+)\s*(?:-\s*(\d+)|\+)\s*[:;,]?\s*(.*?)(?=(?:\s+\d+\s*(?:-\s*\d+|\+))|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex PlusWithPattern = new Regex(
            @"\+(\d+)\s+(Fight|Tech|Talk|Negotiate)\s+with\s+([A-Za-z][A-Za-z ']+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly WorkAction _work = new WorkAction();

        public MisbehaveCard DrawNext(GameState game)
        {
            var pending = game.PendingMisbehave ?? throw new InvalidOperationException("No Misbehave is pending.");
            if (pending.FaceUp != null)
                return pending.FaceUp;
            if (game.Misbehave == null)
                throw new InvalidOperationException("Misbehave deck is not loaded.");
            pending.FaceUp = game.Misbehave.Draw();
            return pending.FaceUp;
        }

        public bool TryResolve(
            GameState game,
            string playerId,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            var pending = game.PendingMisbehave;
            if (pending == null)
            {
                error = "No Misbehave is pending.";
                return false;
            }
            if (pending.PlayerId != playerId)
            {
                error = "This Misbehave belongs to another player.";
                return false;
            }

            var player = game.GetPlayer(playerId);
            if (pending.FaceUp == null)
                DrawNext(game);
            var card = pending.FaceUp!;
            rng ??= new SystemRng();

            if (choice.UseAce)
            {
                if (!HasTag(game, player, card.Ace))
                {
                    error = $"Cannot use the Ace ({card.Ace}).";
                    return false;
                }
                return Finish(game, playerId, card, null, MisbehaveOutcome.Proceed, null, 0, 0, 0, 0, true, out resolution, out error);
            }

            if (choice.OptionIndex < 0 || choice.OptionIndex >= card.Options.Count)
            {
                error = "Invalid Misbehave option.";
                return false;
            }

            var option = card.Options[choice.OptionIndex];
            if (!MeetsRequirement(game, player, option.Details, out error))
                return false;

            var details = option.Details ?? "";
            SkillCheckResult? check = null;
            var bandText = details;
            if (SkillCheck.TryParse(details, out var skillCheck))
            {
                check = skillCheck.Resolve(player, rng);
                var sum = check.Roll.Sum + BonusFromGear(game, player, details);
                bandText = BandText(details, sum) ?? details;
                check = new SkillCheckResult(skillCheck, check.Roll, sum >= skillCheck.Target);
            }

            if (IsReplaceCard(details))
            {
                var extra = Contains(details, "Draw two") || Contains(details, "Draw 2") ? 1 : 0;
                pending.Remaining += extra;
                return Finish(game, playerId, card, option, MisbehaveOutcome.Replaced, check, 0, 0, 0, 0, false, out resolution, out error);
            }

            var optionalPay = Contains(details, "OR Attempt Botched");
            var pay = PayAmount(player, details, choice.PayDisgruntledCuts);
            var paying = pay > 0 && choice.AcceptPay && (!optionalPay || player.Cash >= pay);
            if (paying && player.Cash < pay)
            {
                error = $"Need ${pay} to pick this option.";
                return false;
            }

            var cashDelta = 0;
            if (paying)
            {
                player.Cash -= pay;
                cashDelta -= pay;
            }

            if (Contains(details, "Pay each Disgruntled") && !choice.PayDisgruntledCuts)
                DiscardDisgruntled(player);

            if (optionalPay && !paying)
                bandText = "Attempt Botched";

            var warrants = 0;
            if (Contains(bandText, "Warrant Issued"))
            {
                player.Warrants++;
                warrants = 1;
            }

            var effectText = check == null ? details : bandText;
            var killed = KillCrew(player, effectText);
            var loaded = LoadGoods(player, effectText);
            cashDelta += TakeCash(player, effectText);
            ApplyWanted(player, effectText, choice.TargetCrewId);
            ApplyDisgruntle(player, effectText);
            ApplyClearDisgruntled(player, effectText);
            ApplySolidLoss(player, effectText, effectText, choice.LoseSolidId);
            ApplyWarrantDiscard(player, effectText, choice.DiscardWarrants);

            var outcome = Contains(bandText, "Attempt Botched")
                ? MisbehaveOutcome.Botched
                : MisbehaveOutcome.Proceed;

            return Finish(game, playerId, card, option, outcome, check, warrants, killed, loaded, cashDelta, false, out resolution, out error);
        }

        private bool Finish(
            GameState game,
            string playerId,
            MisbehaveCard card,
            MisbehaveOption? option,
            MisbehaveOutcome outcome,
            SkillCheckResult? check,
            int warrants,
            int killed,
            int loaded,
            int cashDelta,
            bool usedAce,
            out MisbehaveResolution? resolution,
            out string? error)
        {
            error = null;
            resolution = null;
            game.Misbehave?.ResolveIntoDiscard(card);
            if (game.PendingMisbehave != null)
                game.PendingMisbehave.FaceUp = null;

            WorkResult? work = null;
            if (outcome == MisbehaveOutcome.Replaced)
            {
                resolution = new MisbehaveResolution(card, option, outcome, check, warrants, killed, loaded, cashDelta, usedAce, null);
                return true;
            }

            if (!_work.TryProceedMisbehave(game, playerId, outcome == MisbehaveOutcome.Proceed, out work, out error))
                return false;

            resolution = new MisbehaveResolution(card, option, outcome, check, warrants, killed, loaded, cashDelta, usedAce, work);
            return true;
        }

        public static bool HasTag(GameState game, PlayerState player, string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            tag = tag.Trim();

            if (tag.StartsWith("Solid with ", StringComparison.OrdinalIgnoreCase))
            {
                var name = tag.Substring("Solid with ".Length).Trim();
                if (player.IsSolidWith(name))
                    return true;
                if (game.Contacts != null && game.Contacts.TryFindByName(name, out var contact))
                    return player.IsSolidWith(contact.Id) || player.IsSolidWith(contact.Name);
                return false;
            }

            if (player.Roster.HasName(tag))
                return true;
            if (player.Roster.HasProfession(tag))
                return true;

            foreach (var member in player.Roster.Members)
            {
                foreach (var keyword in member.Card.Keywords)
                {
                    if (NamesMatch(keyword, tag))
                        return true;
                }
            }

            if (game.Gear != null)
            {
                foreach (var id in player.Gear)
                {
                    if (!game.Gear.TryGet(id, out var gear))
                        continue;
                    if (NamesMatch(gear.Name, tag))
                        return true;
                    foreach (var keyword in gear.Keywords)
                    {
                        if (NamesMatch(keyword, tag))
                            return true;
                    }
                }
            }

            foreach (var upgradeId in player.ShipUpgrades)
            {
                if (NamesMatch(upgradeId, tag))
                    return true;
            }

            return false;
        }

        private static bool MeetsRequirement(GameState game, PlayerState player, string details, out string? error)
        {
            error = null;
            var match = RequiresPattern.Match(details ?? "");
            if (!match.Success)
                return true;
            var need = match.Groups[1].Value.Trim();

            if (need.StartsWith("no Disgruntled", StringComparison.OrdinalIgnoreCase))
            {
                if (player.Roster.DisgruntledCount > 0)
                {
                    error = "Requires no Disgruntled crew.";
                    return false;
                }
                return true;
            }

            var crewNeed = Regex.Match(need, @"(\d+)\s+or more Crew", RegexOptions.IgnoreCase);
            if (crewNeed.Success)
            {
                var n = int.Parse(crewNeed.Groups[1].Value);
                if (player.Roster.Count < n)
                {
                    error = $"Requires {n} or more crew.";
                    return false;
                }
                return true;
            }

            var solidNeed = Regex.Match(need, @"at least\s+(\d+)\s+Solid", RegexOptions.IgnoreCase);
            if (solidNeed.Success)
            {
                var n = int.Parse(solidNeed.Groups[1].Value);
                if (player.SolidCount < n)
                {
                    error = $"Requires at least {n} Solid.";
                    return false;
                }
                return true;
            }

            var payNeed = Regex.Match(need, @"Pay\s+\$(\d+)", RegexOptions.IgnoreCase);
            if (payNeed.Success)
            {
                var n = int.Parse(payNeed.Groups[1].Value);
                if (player.Cash < n)
                {
                    error = $"Need ${n} to pick this option.";
                    return false;
                }
                return true;
            }

            if (Contains(need, "Discard 1 Cargo or Contraband"))
            {
                if (player.Cargo + player.Contraband < 1)
                {
                    error = "Requires 1 Cargo or Contraband to discard.";
                    return false;
                }
                if (player.Cargo > 0) player.Cargo--;
                else player.Contraband--;
                return true;
            }

            var fightFrom = Regex.Match(need, @"at least\s+(\d+)\s+Fight from\s+(.+)", RegexOptions.IgnoreCase);
            if (fightFrom.Success)
            {
                var tag = fightFrom.Groups[2].Value.Trim();
                if (!HasTag(game, player, tag))
                {
                    error = $"Requires {tag}.";
                    return false;
                }
                return true;
            }

            if (!HasTag(game, player, need))
            {
                error = $"Requires {need.Trim()}.";
                return false;
            }
            return true;
        }

        private static int BonusFromGear(GameState game, PlayerState player, string details)
        {
            var bonus = 0;
            foreach (Match match in PlusWithPattern.Matches(details))
            {
                if (HasTag(game, player, match.Groups[3].Value.Trim()))
                    bonus += int.Parse(match.Groups[1].Value);
            }
            return bonus;
        }

        private static string? BandText(string details, int sum)
        {
            string? picked = null;
            foreach (Match match in BandPattern.Matches(details))
            {
                var min = int.Parse(match.Groups[1].Value);
                var max = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : int.MaxValue;
                if (sum >= min && sum <= max)
                    picked = match.Groups[3].Value.Trim().TrimEnd('.');
            }
            return string.IsNullOrWhiteSpace(picked) ? null : picked;
        }

        private static int PayAmount(PlayerState player, string details, bool payCuts)
        {
            var cash = Regex.Match(details, @"Pay\s+\$(\d+)", RegexOptions.IgnoreCase);
            if (cash.Success)
                return int.Parse(cash.Groups[1].Value);
            if (Contains(details, "Pay each Disgruntled") && payCuts)
                return 100 * player.Roster.DisgruntledCount;
            return 0;
        }

        private static int TakeCash(PlayerState player, string text)
        {
            var match = Regex.Match(text ?? "", @"Take\s+\$(\d+)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return 0;
            var n = int.Parse(match.Groups[1].Value);
            player.Cash += n;
            return n;
        }

        private static void DiscardDisgruntled(PlayerState player)
        {
            for (var i = player.Roster.Count - 1; i >= 0; i--)
            {
                var member = player.Roster.Members[i];
                if (member.Disgruntled && !member.IsLeader)
                    player.Roster.Remove(member.Id);
            }
        }

        private static int KillCrew(PlayerState player, string text)
        {
            if (Contains(text, "Kill all Crew"))
                return player.Roster.KillAll();

            var numbered = Regex.Match(text, @"Kill\s+(\d+)\s+Crew", RegexOptions.IgnoreCase);
            var count = 0;
            if (numbered.Success)
                count = int.Parse(numbered.Groups[1].Value);
            else if (Regex.IsMatch(text, @"Kill\s+(a|1)\s+Crew", RegexOptions.IgnoreCase))
                count = 1;

            if (count <= 0)
                return 0;
            return player.Roster.KillUpTo(count);
        }

        private static int LoadGoods(PlayerState player, string details)
        {
            var loaded = 0;
            var cargo = Regex.Match(details, @"(?:Load(?: up to)?|Take)\s+(\d+)\s+Cargo", RegexOptions.IgnoreCase);
            if (cargo.Success)
            {
                var n = int.Parse(cargo.Groups[1].Value);
                player.Cargo += n;
                loaded += n;
            }
            var contra = Regex.Match(details, @"(?:Load(?: up to)?|Take)\s+(\d+)\s+Contraband", RegexOptions.IgnoreCase);
            if (contra.Success)
            {
                var n = int.Parse(contra.Groups[1].Value);
                player.Contraband += n;
                loaded += n;
            }
            return loaded;
        }

        private static void ApplyWanted(PlayerState player, string text, string? targetCrewId)
        {
            if (!Contains(text, "Wanted"))
                return;
            if (!string.IsNullOrWhiteSpace(targetCrewId))
            {
                player.Roster.MarkWanted(targetCrewId);
                return;
            }
            if (player.Roster.Count > 0)
                player.Roster.MarkWanted(player.Roster.Members[0].Id);
        }

        private static void ApplyDisgruntle(PlayerState player, string text)
        {
            if (Contains(text, "Disgruntle all Crew with Tech"))
                player.Roster.DisgruntleWhere(m => m.Card.Tech > 0);

            if (Contains(text, "Disgruntle Moral") || Contains(text, "Disgruntle all Moral"))
                player.Roster.DisgruntleMoral();

            if (Contains(text, "Disgruntle all Mercs"))
                player.Roster.DisgruntleWhere(m => m.Card.HasProfession("Merc") || m.Card.HasProfession("Soldier"));
        }

        private static void ApplyClearDisgruntled(PlayerState player, string text)
        {
            if (!Contains(text, "Remove Disgruntled"))
                return;
            foreach (var member in player.Roster.Members)
                member.Disgruntled = false;
        }

        private static void ApplySolidLoss(PlayerState player, string details, string bandText, string? loseSolidId)
        {
            if (!Contains(details, "Lose 1 Solid") && !Contains(details, "Loose 1 Solid")
                && !Contains(bandText, "Lose 1 Solid") && !Contains(bandText, "Loose 1 Solid")
                && !Contains(details, "Discard 1 Solid"))
                return;
            player.TryLoseSolid(loseSolidId);
        }

        private static void ApplyWarrantDiscard(PlayerState player, string text, int requested)
        {
            if (!Contains(text, "discard") || !Contains(text, "Warrant"))
                return;
            var n = requested;
            if (n <= 0)
            {
                var match = Regex.Match(text, @"discard(?: up to)?\s+(?:a|(\d+))\s+Warrant", RegexOptions.IgnoreCase);
                if (match.Success)
                    n = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 1;
                else
                    n = 1;
            }
            if (n > player.Warrants)
                n = player.Warrants;
            player.Warrants -= n;
        }

        private static bool IsReplaceCard(string details) =>
            Contains(details, "Draw another Misbehave")
            || Contains(details, "Draw Another Misbehave")
            || Contains(details, "Draw two Misbehave")
            || Contains(details, "Draw 2 Misbehave");

        private static bool NamesMatch(string left, string right)
        {
            left = Normalize(left);
            right = Normalize(right);
            return left == right || left.Contains(right) || right.Contains(left);
        }

        private static string Normalize(string value) =>
            (value ?? "").Replace("'", "").Replace(".", "").Trim().ToUpperInvariant();

        private static bool Contains(string text, string value) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
