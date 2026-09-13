using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Firefly.Core.Abilities;
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
        /// <summary>Thin kill / Medic hooks until PendingChoice.</summary>
        public KillChoice? Kill { get; set; }
        /// <summary>Thin skill-test Bribes hook until PendingChoice.</summary>
        public SkillCheckChoice? SkillCheck { get; set; }
        /// <summary>Discard-down when losing Solid (Mr. Universe hand / Higgins active).</summary>
        public SolidRepChoice? SolidRep { get; set; }
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

    /// <summary>
    /// Draws and resolves Misbehave cards against a pending Work site.
    /// TryProceedMisbehave remains the force/skip path used by Work tests.
    /// Ace auto-succeeds. Replace-card options discard without spending a step.
    /// Skill bands pick the printed effect; Attempt Botched ends the Work site.
    /// </summary>
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
                if (IsAllianceAlertUpdate(card, null))
                    CycleAllianceAlert(game);
                var aceCash = AbilityDispatcher.MisbehaveProceedCash(player, AbilityContext.WorkingJob);
                if (aceCash > 0)
                    player.Cash += aceCash;
                return Finish(game, playerId, card, null, MisbehaveOutcome.Proceed, null, 0, 0, 0, aceCash, true, out resolution, out error);
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
            if (IsAllianceAlertUpdate(card, details))
            {
                CycleAllianceAlert(game);
                var die = Dice.D6(rng);
                var alertOutcome = die <= player.Warrants
                    ? MisbehaveOutcome.Botched
                    : MisbehaveOutcome.Proceed;
                return Finish(game, playerId, card, option, alertOutcome, null, 0, 0, 0, 0, false, out resolution, out error);
            }
            SkillCheckResult? check = null;
            var bandText = details;
            var bribeCash = 0;
            if (SkillCheck.TryParse(details, out var skillCheck))
            {
                if (!skillCheck.TryResolve(player, rng, out check, out error, choice.SkillCheck))
                    return false;
                bribeCash = check.BribeDollarsPaid;
                var sum = check.Total + BonusFromGear(game, player, details);
                bandText = BandText(details, sum) ?? details;
                check = check.WithTotal(sum);
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

            // Validate Solid-loss discard hooks before mutating crew / warrants.
            if (WouldLoseSolid(details) || WouldLoseSolid(bandText ?? ""))
            {
                var lostId = ResolveLoseSolidId(player, choice.LoseSolidId);
                if (lostId == null)
                {
                    error = "Not Solid with a Contact to lose.";
                    return false;
                }
                if (!ContactSolidBenefits.CanDiscardDownAfterLosing(
                    game, player, lostId, choice.SolidRep, out error))
                    return false;
            }

            var cashDelta = -bribeCash;
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
            var killed = KillCrew(game, player, effectText, rng, choice.Kill);
            var loaded = LoadGoods(player, effectText);
            cashDelta += TakeCash(player, effectText);
            ApplyWanted(player, effectText, choice.TargetCrewId);
            ApplyDisgruntle(player, effectText);
            ApplyClearDisgruntled(player, effectText);
            if (!TryApplySolidLoss(game, player, effectText, effectText, choice, out error))
                return false;
            ApplyWarrantDiscard(player, effectText, choice.DiscardWarrants);

            // GF9 / FAQ: Warrant Issued while Working discards the Job. Niska Pound of Flesh: Kill a Crew.
            if (warrants > 0 && game.PendingMisbehave != null)
            {
                if (!TryAbandonJobForWarrant(
                    game, player, rng, choice.Kill, ref killed, out var abandonedWork, out error))
                    return false;
                game.Misbehave?.ResolveIntoDiscard(card);
                if (game.PendingMisbehave != null)
                    game.PendingMisbehave.FaceUp = null;
                resolution = new MisbehaveResolution(
                    card, option, MisbehaveOutcome.Botched, check, warrants, killed, loaded, cashDelta, false, abandonedWork);
                error = null;
                return true;
            }

            var outcome = Contains(bandText, "Attempt Botched")
                ? MisbehaveOutcome.Botched
                : MisbehaveOutcome.Proceed;

            if (outcome == MisbehaveOutcome.Proceed)
            {
                // Big Damn Heroes / typed misbehaveProceedCash (Job-only; Goals Work not implemented).
                var proceedCash = AbilityDispatcher.MisbehaveProceedCash(
                    player, AbilityContext.WorkingJob);
                if (proceedCash > 0)
                {
                    player.Cash += proceedCash;
                    cashDelta += proceedCash;
                }
            }

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
                if (ActiveAlertRules.CountsAsSolidWith(game, player, name))
                    return true;
                if (game.Contacts != null && game.Contacts.TryFindByName(name, out var contact))
                    return ActiveAlertRules.CountsAsSolidWith(game, player, contact.Id)
                        || ActiveAlertRules.CountsAsSolidWith(game, player, contact.Name);
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
                // FAQ 4.1 p.2 / GF9 p.14: Onboard Ship Gear may not be used — only carried Gear.
                if (GearCarriage.HasUsableGearTag(game, player, tag))
                    return true;
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

        private static int KillCrew(
            GameState game,
            PlayerState player,
            string text,
            IRng rng,
            KillChoice? killChoice)
        {
            if (Contains(text, "Kill all Crew"))
                return CrewKill.KillAll(game, player, rng, killChoice);

            var numbered = Regex.Match(text, @"Kill\s+(\d+)\s+Crew", RegexOptions.IgnoreCase);
            var count = 0;
            if (numbered.Success)
                count = int.Parse(numbered.Groups[1].Value);
            else if (Regex.IsMatch(text, @"Kill\s+(a|1)\s+Crew", RegexOptions.IgnoreCase))
                count = 1;

            if (count <= 0)
                return 0;
            return CrewKill.KillUpTo(game, player, count, rng, killChoice);
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

        private static bool TryApplySolidLoss(
            GameState game,
            PlayerState player,
            string details,
            string bandText,
            MisbehaveChoice choice,
            out string? error)
        {
            error = null;
            if (!WouldLoseSolid(details) && !WouldLoseSolid(bandText))
                return true;

            var solidChoice = choice.SolidRep ?? new SolidRepChoice();
            if (solidChoice.Kill == null && choice.Kill != null)
                solidChoice.Kill = choice.Kill;
            return ContactSolidBenefits.TryLoseSolid(game, player, choice.LoseSolidId, solidChoice, out error);
        }

        private static bool WouldLoseSolid(string text) =>
            Contains(text, "Lose 1 Solid") || Contains(text, "Loose 1 Solid") || Contains(text, "Discard 1 Solid");

        private static string? ResolveLoseSolidId(PlayerState player, string? contactIdOrName)
        {
            if (!string.IsNullOrWhiteSpace(contactIdOrName))
            {
                foreach (var id in player.SolidWith)
                {
                    if (id.Equals(contactIdOrName, StringComparison.OrdinalIgnoreCase)
                        || ContactNames.EqualsName(id, contactIdOrName))
                        return id;
                }
                return null;
            }
            foreach (var id in player.SolidWith)
                return id;
            return null;
        }

        /// <summary>
        /// FAQ 4.1 / GF9: Warrant while Working discards that Job.
        /// Niska Pound of Flesh: Kill a Crew when Warrant Issued while working a Niska Job.
        /// </summary>
        private static bool TryAbandonJobForWarrant(
            GameState game,
            PlayerState player,
            IRng? rng,
            KillChoice? killChoice,
            ref int killed,
            out WorkResult? work,
            out string? error)
        {
            work = null;
            error = null;
            var pending = game.PendingMisbehave;
            if (pending == null || game.Jobs == null || !game.Jobs.TryGet(pending.JobId, out var job))
            {
                error = "No Work Job to abandon for Warrant.";
                return false;
            }

            player.JobHand.Remove(job.Id);
            player.RemoveActive(job.Id);
            if (game.ContactDecks != null && game.ContactDecks.TryGet(job.ContactName, out var deck))
                deck.MoveToDiscard(job);

            if (ContactSolidBenefits.IsNiskaJob(job))
                killed += CrewKill.KillUpTo(game, player, 1, rng ?? new SystemRng(), killChoice);

            game.PendingMisbehave = null;
            game.WorkGearLocked = false;
            game.TryConsumeAction(TurnAction.Work, out _);
            work = new WorkResult(
                pending.Site == WorkSite.Pickup ? WorkKind.Pickup : WorkKind.Complete,
                job, false, false, 0, 0);
            return true;
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

        private static bool IsAllianceAlertUpdate(MisbehaveCard card, string? details)
        {
            if (Contains(details ?? "", "Draw a new Alliance Alert"))
                return true;
            if (card.Name != null && card.Name.Equals("Alliance Alert!", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static void CycleAllianceAlert(GameState game) =>
            game.AllianceAlertDeck?.DrawAndActivate();
    }
}
