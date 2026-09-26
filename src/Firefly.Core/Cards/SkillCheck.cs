using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Firefly.Core.Abilities;
using Firefly.Core.State;

namespace Firefly.Core.Cards
{
    public enum Skill
    {
        Fight,
        Tech,
        Talk
    }

    /// <summary>
    /// Skill-test Bribes / ability re-roll choices. Null means undecided —
    /// Misbehave / Nav suspend via PendingChoice when required.
    /// </summary>
    public sealed class SkillCheckChoice
    {
        /// <summary>
        /// Dollars to pay as Bribes before rolling. Null = not yet chosen (PendingChoice).
        /// 0 = decline. Positive must be a multiple of 100 and not exceed cash.
        /// Ignored when the test is not Bribes-allowed.
        /// </summary>
        public int? BribeDollars { get; set; }

        /// <summary>
        /// Kaylee / Zoe / Inara <c>skillReroll</c> may: null = undecided (PendingChoice),
        /// true = re-roll once, false = keep the first roll. FAQ 4.1 p.8 — always ask.
        /// </summary>
        public bool? AcceptReroll { get; set; }

        /// <summary>
        /// Discard-to-reroll gear may: null = undecided, true = discard gear and re-roll,
        /// false = decline. FAQ 4.1 p.8 — always ask when carried matching gear exists.
        /// </summary>
        public bool? AcceptDiscardReroll { get; set; }

        /// <summary>Gear id discarded when <see cref="AcceptDiscardReroll"/> is true.</summary>
        public string? DiscardRerollGearId { get; set; }

        /// <summary>
        /// Sheydra / Stitch once-per-job skill switch: null = undecided, true = switch, false = keep.
        /// </summary>
        public bool? AcceptSkillSwitch { get; set; }
    }

    public sealed class SkillCheck
    {
        private static readonly Regex Pattern = new Regex(
            @"\b(Fight|Tech|Talk|Negotiate)\s+(\d+)(?<tail>(?:\s+Kosherized(?:\s+Rules)?|\s+Bribes)*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InvertedPattern = new Regex(
            @"\b(\d+)\+\s*(Fight|Tech|Talk|Negotiate)(?<tail>(?:\s+Kosherized(?:\s+Rules)?|\s+Bribes)*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Skill Skill { get; }
        public int Target { get; }
        /// <summary>
        /// GF9 p.6 / Director's Cut p.14: exclude Fight Skill from Gear; crew Fight only.
        /// </summary>
        public bool Kosherized { get; }
        /// <summary>
        /// GF9 p.6 / Director's Cut p.14: Negotiate/Talk tests may pay $100 = +1 before the roll.
        /// </summary>
        public bool BribesAllowed { get; }

        public SkillCheck(Skill skill, int target, bool kosherized = false, bool bribesAllowed = false)
        {
            Skill = skill;
            Target = target;
            Kosherized = kosherized;
            BribesAllowed = bribesAllowed;
        }

        /// <summary>Copy with a substituted skill (Sheydra / Stitch once-per-job switch).</summary>
        public SkillCheck WithSkill(Skill skill) =>
            new SkillCheck(skill, Target, Kosherized, skill == Skill.Talk && BribesAllowed);

        public static bool TryParse(string? text, out SkillCheck check)
        {
            check = null!;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var match = Pattern.Match(text);
            string label;
            int target;
            string tail;
            if (match.Success)
            {
                label = match.Groups[1].Value;
                target = int.Parse(match.Groups[2].Value);
                tail = match.Groups["tail"].Value;
            }
            else
            {
                match = InvertedPattern.Match(text);
                if (!match.Success)
                    return false;
                target = int.Parse(match.Groups[1].Value);
                label = match.Groups[2].Value;
                tail = match.Groups["tail"].Value;
            }

            Skill skill;
            if (label.Equals("Negotiate", StringComparison.OrdinalIgnoreCase))
                skill = Skill.Talk;
            else if (!Enum.TryParse(label, true, out skill))
                return false;

            var kosherized = Contains(tail, "Kosherized");
            // Printed Bribes apply to Negotiate (Talk) tests. Fight+Bribes is not a rulebook case.
            var bribes = Contains(tail, "Bribes") && skill == Skill.Talk;
            check = new SkillCheck(skill, target, kosherized, bribes);
            return true;
        }

        /// <summary>
        /// Dice count for this test. Kosherized Fights use crew Fight only
        /// (exclude <see cref="PlayerState.FightBonus"/> gear proxy).
        /// When <paramref name="job"/> is Illegal and Lawmen are present, they stay onboard (PBH)
        /// and do not add dice; otherwise the normal roster + bonus path is used.
        /// Director's Cut C&amp;P p.49: crew Returned to Ship (and their Gear) do not count.
        /// </summary>
        public int DiceCount(PlayerState player, GameState? game = null, JobCard? job = null)
        {
            if (JobWorkCrew.HasAnyoneReturnedToShip(player))
                return DiceCountExcludingReturned(player, game, job);

            if (job == null || job.Legal || !HasOnboardLawman(player, job))
            {
                if (Skill == Skill.Fight)
                    return Kosherized ? player.Roster.Fight : player.Fight;
                if (Skill == Skill.Tech)
                    return player.Tech;
                return player.Talk;
            }

            var crew = LawmanRules.CrewSkillForJob(player, job, Skill);
            if (Skill == Skill.Fight && Kosherized)
                return crew;
            if (game != null)
                return crew + AbilityDispatcher.CarriedSkillAddend(game, player, Skill, job: job);
            return crew;
        }

        private int DiceCountExcludingReturned(PlayerState player, GameState? game, JobCard? job)
        {
            var crew = 0;
            foreach (var member in player.Roster.Members)
            {
                if (JobWorkCrew.IsUnavailable(player, member))
                    continue;
                if (job != null && LawmanRules.StaysOnboardForJob(member, job))
                    continue;
                crew += Skill switch
                {
                    Skill.Fight => member.Card.Fight,
                    Skill.Tech => member.Card.Tech,
                    _ => member.Card.Talk
                };
            }

            if (Skill == Skill.Fight && Kosherized)
                return crew;
            if (game != null)
                return crew + AbilityDispatcher.CarriedSkillAddend(game, player, Skill, job: job);
            // Fallback without game: use ship bonuses only when no returned-crew gear path.
            if (Skill == Skill.Fight)
                return crew + player.FightBonus;
            if (Skill == Skill.Tech)
                return crew + player.TechBonus;
            return crew + player.TalkBonus;
        }

        private static bool HasOnboardLawman(PlayerState player, JobCard job)
        {
            foreach (var member in player.Roster.Members)
            {
                if (LawmanRules.StaysOnboardForJob(member, job))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when Bribes need a PendingChoice amount.
        /// Printed Bribes: auto-decline when cash &lt; $100 (PR #34).
        /// Cortland <c>bribesOnAnyNegotiate</c> may: always suspend (FAQ 4.1 p.8) even if
        /// unaffordable (only $0 is sensible).
        /// </summary>
        public static bool NeedsBribeChoice(
            PlayerState player,
            SkillCheck check,
            SkillCheckChoice? choice)
        {
            if (check == null || !check.BribesAllowed)
                return false;
            if (choice?.BribeDollars != null)
                return false;
            if (Abilities.AbilityDispatcher.HasBribesOnAnyNegotiate(player))
                return true;
            return player.Cash >= 100;
        }

        /// <summary>
        /// Apply Cortland (or future) "any Negotiate" Bribes enablement to a Talk test.
        /// Showdowns are not SkillCheck paths — excluded by not wiring Showdown.
        /// </summary>
        public static SkillCheck WithAbilityBribes(SkillCheck check, PlayerState player)
        {
            if (check == null)
                throw new ArgumentNullException(nameof(check));
            if (check.Skill != Skill.Talk || check.BribesAllowed)
                return check;
            if (!Abilities.AbilityDispatcher.HasBribesOnAnyNegotiate(player))
                return check;
            return new SkillCheck(check.Skill, check.Target, check.Kosherized, bribesAllowed: true);
        }

        /// <summary>
        /// Suspend with <see cref="State.PendingChoiceKinds.BribeAmount"/>.
        /// Submission uses <see cref="State.ChoiceSubmission.Amount"/> ($0 or $100 increments).
        /// </summary>
        public static bool TrySuspendBribeChoice(
            GameState game,
            PlayerState player,
            string? contextId,
            out string? error,
            string? prompt = null)
        {
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.BribeAmount,
                contextId: contextId,
                options: null,
                prompt: prompt
                    ?? $"Pay Bribes before rolling ($0–${(player.Cash / 100) * 100} in $100 increments)?");
            return game.TrySetPendingChoice(pending, out error);
        }

        /// <summary>
        /// Suspend with <see cref="State.PendingChoiceKinds.SkillReroll"/> after the first roll.
        /// Options: keep / reroll. FAQ 4.1 p.8 — always suspend for printed may re-roll.
        /// </summary>
        public static bool TrySuspendSkillReroll(
            GameState game,
            PlayerState player,
            string? contextId,
            out string? error,
            string? prompt = null)
        {
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.SkillReroll,
                contextId: contextId,
                options: new[] { SkillRerollOptions.Keep, SkillRerollOptions.Reroll },
                prompt: prompt ?? "Re-roll this skill test?");
            return game.TrySetPendingChoice(pending, out error);
        }

        /// <summary>
        /// Sheydra / Stitch: suspend once-per-job skill switch may.
        /// FAQ 4.1 p.9: never both Bribes and Stitch switch.
        /// </summary>
        public static bool NeedsSkillSwitchChoice(
            PlayerState player,
            SkillCheck check,
            SkillCheckChoice? choice,
            ActiveJob? activeJob,
            AbilityContext? context = null)
        {
            if (choice?.AcceptSkillSwitch != null)
                return false;
            if (activeJob != null && activeJob.SkillSwitchUsedThisJob)
                return false;
            // FAQ: either bribable Negotiate or Fight switch — never both.
            if (choice?.BribeDollars != null && choice.BribeDollars > 0)
                return false;
            return AbilityDispatcher.FindOncePerJobSkillSwitch(player, check.Skill, context) != null;
        }

        public static bool TrySuspendSkillSwitch(
            GameState game,
            PlayerState player,
            string? contextId,
            out string? error,
            string? prompt = null)
        {
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.SkillSwitch,
                contextId: contextId,
                options: new[] { SkillSwitchOptions.Switch, SkillSwitchOptions.Keep },
                prompt: prompt ?? "Switch this skill test once per job?");
            return game.TrySetPendingChoice(pending, out error);
        }

        public static bool TryMergeSkillSwitchSubmission(
            ChoiceSubmission submission,
            SkillCheckChoice? existing,
            out SkillCheckChoice merged,
            out string? error)
        {
            merged = existing ?? new SkillCheckChoice();
            error = null;
            if (submission == null)
            {
                error = "A choice submission is required.";
                return false;
            }

            bool accept;
            if (submission.Accepted != null)
                accept = submission.Accepted.Value;
            else if (string.Equals(
                         submission.SelectedOptionId,
                         SkillSwitchOptions.Switch,
                         StringComparison.Ordinal))
                accept = true;
            else if (string.Equals(
                         submission.SelectedOptionId,
                         SkillSwitchOptions.Keep,
                         StringComparison.Ordinal))
                accept = false;
            else
            {
                error = "Switch or keep the printed skill.";
                return false;
            }

            merged.AcceptSkillSwitch = accept;
            return true;
        }

        /// <summary>
        /// Apply once-per-job skill switch when accepted; marks ActiveJob latch; clears Bribes.
        /// </summary>
        public static SkillCheck ApplySkillSwitchIfChosen(
            PlayerState player,
            SkillCheck check,
            SkillCheckChoice? choice,
            ActiveJob? activeJob,
            AbilityContext? context = null)
        {
            if (choice?.AcceptSkillSwitch != true)
                return check;
            var ability = AbilityDispatcher.FindOncePerJobSkillSwitch(player, check.Skill, context);
            if (ability == null || !AbilityDispatcher.TryParseSkillLabel(ability.Subject, out var to))
                return check;
            if (activeJob != null)
                activeJob.SkillSwitchUsedThisJob = true;
            // FAQ 4.1 p.9: switched Fight is not a bribable Negotiate.
            return check.WithSkill(to);
        }

        /// <summary>
        /// Suspend discard-to-reroll gear may. ContextId = gear id to discard on accept.
        /// </summary>
        public static bool TrySuspendDiscardToReroll(
            GameState game,
            PlayerState player,
            string gearId,
            out string? error,
            string? prompt = null)
        {
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.DiscardToReroll,
                contextId: gearId,
                options: new[] { DiscardToRerollOptions.Discard, DiscardToRerollOptions.Decline },
                prompt: prompt ?? "Discard gear to re-roll this Fight test?");
            return game.TrySetPendingChoice(pending, out error);
        }

        /// <summary>
        /// Validate discard/decline into <see cref="SkillCheckChoice.AcceptDiscardReroll"/>.
        /// </summary>
        public static bool TryMergeDiscardToRerollSubmission(
            ChoiceSubmission submission,
            string? gearId,
            SkillCheckChoice? existing,
            out SkillCheckChoice merged,
            out string? error)
        {
            merged = existing ?? new SkillCheckChoice();
            error = null;
            if (submission == null)
            {
                error = "A choice submission is required.";
                return false;
            }

            bool accept;
            if (submission.Accepted != null)
                accept = submission.Accepted.Value;
            else if (!string.IsNullOrWhiteSpace(submission.SelectedOptionId))
            {
                if (string.Equals(
                        submission.SelectedOptionId,
                        DiscardToRerollOptions.Discard,
                        StringComparison.Ordinal))
                    accept = true;
                else if (string.Equals(
                             submission.SelectedOptionId,
                             DiscardToRerollOptions.Decline,
                             StringComparison.Ordinal))
                    accept = false;
                else
                {
                    error = $"Unknown discard-to-reroll option '{submission.SelectedOptionId}'.";
                    return false;
                }
            }
            else
            {
                error = "Accept (discard) or decline, or select discard/decline.";
                return false;
            }

            merged.AcceptDiscardReroll = accept;
            if (accept)
                merged.DiscardRerollGearId = gearId;
            return true;
        }

        /// <summary>
        /// Validate keep/reroll submission into <see cref="SkillCheckChoice.AcceptReroll"/>.
        /// </summary>
        public static bool TryMergeSkillRerollSubmission(
            ChoiceSubmission submission,
            SkillCheckChoice? existing,
            out SkillCheckChoice merged,
            out string? error)
        {
            merged = existing ?? new SkillCheckChoice();
            error = null;
            if (submission == null)
            {
                error = "A choice submission is required.";
                return false;
            }

            bool accept;
            if (submission.Accepted != null)
                accept = submission.Accepted.Value;
            else if (!string.IsNullOrWhiteSpace(submission.SelectedOptionId))
            {
                if (string.Equals(
                        submission.SelectedOptionId,
                        SkillRerollOptions.Reroll,
                        StringComparison.Ordinal))
                    accept = true;
                else if (string.Equals(
                             submission.SelectedOptionId,
                             SkillRerollOptions.Keep,
                             StringComparison.Ordinal))
                    accept = false;
                else
                {
                    error = $"Unknown skill re-roll option '{submission.SelectedOptionId}'.";
                    return false;
                }
            }
            else
            {
                error = "Accept (re-roll) or decline (keep), or select keep/reroll.";
                return false;
            }

            merged.AcceptReroll = accept;
            return true;
        }

        /// <summary>
        /// Re-roll dice only; keep Bribes already paid on the first attempt.
        /// </summary>
        public SkillCheckResult RerollKeepingBribes(
            PlayerState player,
            IRng rng,
            SkillCheckResult previous,
            GameState? game = null,
            AbilityContext? abilityContext = null)
        {
            if (previous == null)
                throw new ArgumentNullException(nameof(previous));
            var roll = Dice.RollD6(DiceCount(player), rng);
            roll = ApplyRerollOnes(game, player, Skill, roll, rng, abilityContext);
            var total = roll.Sum + previous.BribeBonus;
            return new SkillCheckResult(
                this,
                roll,
                total >= Target,
                previous.BribeDollarsPaid,
                previous.BribeBonus);
        }

        /// <summary>
        /// Mandatory re-roll faces of 1 when carried <c>rerollOnes</c> gear applies.
        /// FAQ 4.1 p.8: no “may” on these cards — always re-roll ones.
        /// </summary>
        public static DiceRoll ApplyRerollOnes(
            GameState? game,
            PlayerState player,
            Skill skill,
            DiceRoll roll,
            IRng rng,
            AbilityContext? abilityContext = null)
        {
            if (game == null || roll == null || roll.Faces.Count == 0)
                return roll;
            if (!AbilityDispatcher.HasRerollOnes(game, player, skill, abilityContext))
                return roll;

            var faces = new int[roll.Faces.Count];
            var changed = false;
            for (var i = 0; i < roll.Faces.Count; i++)
            {
                faces[i] = roll.Faces[i];
                if (faces[i] == 1)
                {
                    faces[i] = Dice.D6(rng);
                    changed = true;
                }
            }
            return changed ? new DiceRoll(faces) : roll;
        }

        /// <summary>
        /// Validate <see cref="ChoiceSubmission.Amount"/> as Bribe dollars and merge into a
        /// <see cref="SkillCheckChoice"/>. Does not clear PendingChoice.
        /// </summary>
        public static bool TryMergeBribeSubmission(
            PlayerState player,
            ChoiceSubmission submission,
            SkillCheckChoice? existing,
            out SkillCheckChoice merged,
            out string? error)
        {
            merged = existing ?? new SkillCheckChoice();
            error = null;
            if (submission == null)
            {
                error = "A choice submission is required.";
                return false;
            }
            if (submission.Amount == null)
            {
                error = "Bribe dollar amount is required (use 0 to decline).";
                return false;
            }

            var dollars = submission.Amount.Value;
            if (dollars < 0)
            {
                error = "Bribe dollars cannot be negative.";
                return false;
            }
            if (dollars % 100 != 0)
            {
                error = "Bribes must be paid in $100 increments.";
                return false;
            }
            if (player.Cash < dollars)
            {
                error = $"Need ${dollars} to pay Bribes.";
                return false;
            }

            merged.BribeDollars = dollars;
            return true;
        }

        /// <summary>
        /// Resolve the test. When Bribes are allowed, pays <see cref="SkillCheckChoice.BribeDollars"/>
        /// before rolling ($100 = +1). Null / unset bribe dollars are treated as decline ($0) —
        /// callers that need PendingChoice must suspend first via <see cref="NeedsBribeChoice"/>.
        /// Fails closed if the bribe amount is invalid.
        /// Optional <paramref name="game"/> enables mandatory <c>rerollOnes</c> gear.
        /// </summary>
        public bool TryResolve(
            PlayerState player,
            IRng rng,
            out SkillCheckResult result,
            out string? error,
            SkillCheckChoice? choice = null,
            GameState? game = null,
            AbilityContext? abilityContext = null,
            JobCard? job = null)
        {
            result = null!;
            error = null;

            var bribeDollars = 0;
            var bribeBonus = 0;
            if (BribesAllowed && choice?.BribeDollars is int requested && requested != 0)
            {
                if (requested < 0)
                {
                    error = "Bribe dollars cannot be negative.";
                    return false;
                }
                if (requested % 100 != 0)
                {
                    error = "Bribes must be paid in $100 increments.";
                    return false;
                }
                if (player.Cash < requested)
                {
                    error = $"Need ${requested} to pay Bribes.";
                    return false;
                }
                bribeDollars = requested;
                bribeBonus = bribeDollars / 100;
                player.Cash -= bribeDollars;
            }

            var roll = Dice.RollD6(DiceCount(player, game, job), rng);
            roll = ApplyRerollOnes(game, player, Skill, roll, rng, abilityContext);
            var total = roll.Sum + bribeBonus;
            var success = total >= Target;
            result = new SkillCheckResult(this, roll, success, bribeDollars, bribeBonus);
            return true;
        }

        public SkillCheckResult Resolve(
            PlayerState player,
            IRng rng,
            SkillCheckChoice? choice = null,
            GameState? game = null,
            AbilityContext? abilityContext = null,
            JobCard? job = null)
        {
            if (!TryResolve(player, rng, out var result, out var error, choice, game, abilityContext, job))
                throw new InvalidOperationException(error ?? "Skill check failed.");
            return result;
        }

        public static FlightOutcome OutcomeFor(string? details, bool success)
        {
            var text = details ?? "";
            if (success)
            {
                if (Contains(text, "Keep Flying"))
                    return FlightOutcome.KeepFlying;
                if (Contains(text, "Evade"))
                    return FlightOutcome.Evade;
                return FlightOutcome.FullStop;
            }

            if (Contains(text, "Full Stop"))
                return FlightOutcome.FullStop;
            if (Contains(text, "Evade"))
                return FlightOutcome.Evade;
            return FlightOutcome.FullStop;
        }

        /// <summary>
        /// Director's Cut p.14 / GF9: Skill Tests list results under the target; the rolled
        /// total selects the matching printed band (e.g. 1-4 … / 5+ …).
        /// </summary>
        public static string? BandText(string? details, int sum)
        {
            if (string.IsNullOrWhiteSpace(details))
                return null;
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

        private static readonly Regex BandPattern = new Regex(
            @"(\d+)\s*(?:-\s*(\d+)|\+)\s*[:;,]?\s*(.*?)(?=(?:\s+\d+\s*(?:-\s*\d+|\+))|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static bool Contains(string text, string value) =>
            text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public sealed class SkillCheckResult
    {
        public SkillCheck Check { get; }
        public DiceRoll Roll { get; }
        public bool Success { get; }
        public int BribeDollarsPaid { get; }
        public int BribeBonus { get; }
        /// <summary>Roll sum plus Bribes (+ callers may add further printed modifiers).</summary>
        public int Total => Roll.Sum + BribeBonus;

        public SkillCheckResult(
            SkillCheck check,
            DiceRoll roll,
            bool success,
            int bribeDollarsPaid = 0,
            int bribeBonus = 0)
        {
            Check = check;
            Roll = roll;
            Success = success;
            BribeDollarsPaid = bribeDollarsPaid;
            BribeBonus = bribeBonus;
        }

        /// <summary>Rebuild success after adding post-roll modifiers (e.g. Misbehave +N with gear).</summary>
        public SkillCheckResult WithTotal(int total) =>
            new SkillCheckResult(Check, Roll, total >= Check.Target, BribeDollarsPaid, BribeBonus);
    }
}
