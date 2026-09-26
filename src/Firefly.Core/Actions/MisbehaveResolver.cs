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
        /// <summary>
        /// Card option index. Null when the card has multiple options and the player has not
        /// chosen yet — suspends via <see cref="PendingChoiceKinds.MisbehaveOption"/>
        /// (GF9 p.14: most Misbehave cards have 2 options; you may attempt either).
        /// </summary>
        public int? OptionIndex { get; set; }
        /// <summary>
        /// FIRST–NEXT step index. Null means start at step 0, or accept a pending NEXT
        /// via <see cref="PendingChoiceKinds.MisbehaveOption"/> after a Continue.
        /// </summary>
        public int? StepIndex { get; set; }
        public bool UseAce { get; set; }
        public bool PayDisgruntledCuts { get; set; }
        public bool AcceptPay { get; set; } = true;
        public string? TargetCrewId { get; set; }
        public string? LoseSolidId { get; set; }
        public int DiscardWarrants { get; set; }
        /// <summary>
        /// Blue Sun Goods mix for Take/Load N Goods (Fuel/Parts/Cargo/Contraband).
        /// Sum must equal the printed N; omit all zeros to suspend GoodsMix.
        /// </summary>
        public int LoadGoodsFuel { get; set; }
        public int LoadGoodsParts { get; set; }
        public int LoadGoodsCargo { get; set; }
        public int LoadGoodsContraband { get; set; }
        /// <summary>
        /// Dead to Rights path: <see cref="MisbehaveWarrantOrWantedOptions"/> id, or null to suspend.
        /// Exclusive or — warrants path and wanted-tokens path cannot mix.
        /// </summary>
        public string? DiscardWarrantsOrWantedPath { get; set; }
        /// <summary>Crew ids to clear Wanted on (wanted-tokens path; player chooses).</summary>
        public IList<string>? ClearWantedCrewIds { get; set; }
        /// <summary>
        /// Kill / Medic hooks. Victim ids come from PendingChoice resume or tests.
        /// </summary>
        public KillChoice? Kill { get; set; }
        /// <summary>Thin skill-test Bribes hook; null BribeDollars suspends when affordable.</summary>
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
    /// Prefer structured option.SkillCheck / Bands / Effects when present; else prose/regex
    /// via SkillCheck.TryParse and SkillCheck.BandText (shared banding — no local BandPattern).
    /// </summary>
    public sealed class MisbehaveResolver
    {
        private static readonly Regex RequiresPattern = new Regex(
            @"Requires\s*:?\s*([^.;]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PlusWithPattern = new Regex(
            @"\+(\d+)\s+(Fight|Tech|Talk|Negotiate)\s+with\s+([A-Za-z][A-Za-z ']+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly WorkAction _work = new WorkAction();

        /// <summary>True while <see cref="TryResumeKillVictims"/> re-enters <see cref="TryResolve"/>.</summary>
        private bool _resumingKillVictims;
        /// <summary>True while bribe / Med Foam resume re-enters <see cref="TryResolve"/>.</summary>
        private bool _resumingBribeOrMedFoam;
        /// <summary>True while option / FIRST–NEXT step resume re-enters <see cref="TryResolve"/>.</summary>
        private bool _resumingMisbehaveOption;
        /// <summary>True while Dalin redraw resume re-enters <see cref="TryResolve"/>.</summary>
        private bool _resumingDalin;
        /// <summary>True while GoodsMix resume re-enters <see cref="TryResolve"/>.</summary>
        private bool _resumingGoodsMix;
        /// <summary>True while warrant/Wanted discard resume re-enters <see cref="TryResolve"/>.</summary>
        private bool _resumingWarrantOrWanted;
        private MisbehaveChoice? _pendingGoodsMixChoice;
        private MisbehaveChoice? _pendingWarrantOrWantedChoice;
        private bool _frozenSkillReady;
        private SkillCheckResult? _frozenSkillCheck;
        private string? _frozenBandText;
        private IReadOnlyList<MisbehaveEffect>? _frozenStructuredEffects;
        private int _frozenBribeCash;
        /// <summary>Kill choice held across Med Foam suspend after victim pick.</summary>
        private KillChoice? _pendingKillAfterVictims;
        /// <summary>First skill roll held while <see cref="PendingChoiceKinds.SkillReroll"/> is pending.</summary>
        private SkillCheckResult? _pendingRerollResult;

        public MisbehaveCard DrawNext(GameState game)
        {
            var pending = game.PendingMisbehave ?? throw new InvalidOperationException("No Misbehave is pending.");
            if (pending.FaceUp != null)
                return pending.FaceUp;
            if (game.Misbehave == null)
                throw new InvalidOperationException("Misbehave deck is not loaded.");
            pending.FaceUp = game.Misbehave.Draw();
            pending.ClearStepProgress();
            pending.AcceptDalinRedraw = null;
            return pending.FaceUp;
        }

        /// <summary>
        /// Resume Dalin Intel Broker pay/redraw PendingChoice.
        /// </summary>
        public bool TryResumeDalinRedraw(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.MisbehaveDiscardRedraw,
                    StringComparison.Ordinal))
            {
                error = "No Dalin redraw choice is pending.";
                return false;
            }

            var pending = game.PendingMisbehave;
            if (pending == null)
            {
                error = "No Misbehave is pending.";
                return false;
            }

            bool accept;
            if (submission.Accepted != null)
                accept = submission.Accepted.Value;
            else if (!string.IsNullOrWhiteSpace(submission.SelectedOptionId))
            {
                if (string.Equals(
                        submission.SelectedOptionId,
                        DalinRedrawOptions.PayRedraw,
                        StringComparison.Ordinal))
                    accept = true;
                else if (string.Equals(
                             submission.SelectedOptionId,
                             DalinRedrawOptions.Decline,
                             StringComparison.Ordinal))
                    accept = false;
                else
                {
                    error = $"Unknown Dalin option '{submission.SelectedOptionId}'.";
                    return false;
                }
            }
            else
            {
                error = "Choose pay-redraw or decline.";
                return false;
            }

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            pending.AcceptDalinRedraw = accept;
            _resumingDalin = true;
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingDalin = false;
            }
        }

        /// <summary>
        /// Returns true when a redraw was applied (caller continues with new FaceUp).
        /// Returns false with error set when suspended or failed; false with error null when no-op.
        /// </summary>
        private static bool TryOfferOrApplyDalin(
            GameState game,
            PlayerState player,
            PendingMisbehave pending,
            out string? error)
        {
            error = null;
            if (!AbilityDispatcher.HasMisbehaveDiscardRedraw(player, AbilityContext.WorkingJob))
                return false;
            if (pending.DalinUsedThisWork)
                return false;
            if (pending.FaceUp == null)
                return false;

            var cost = AbilityDispatcher.MisbehaveDiscardRedrawCost(player, AbilityContext.WorkingJob);

            if (pending.AcceptDalinRedraw == true)
            {
                if (player.Cash < cost)
                {
                    error = $"Need ${cost} for Dalin's Intel Broker redraw.";
                    return false;
                }
                player.Cash -= cost;
                game.Misbehave?.ResolveIntoDiscard(pending.FaceUp);
                pending.FaceUp = null;
                pending.DalinUsedThisWork = true;
                pending.AcceptDalinRedraw = null;
                if (game.Misbehave == null)
                {
                    error = "Misbehave deck is not loaded.";
                    return false;
                }
                pending.FaceUp = game.Misbehave.Draw();
                pending.ClearStepProgress();
                return true;
            }

            if (pending.AcceptDalinRedraw == false)
            {
                pending.DalinUsedThisWork = true;
                return false;
            }

            // Undecided: always suspend when affordable (FAQ 4.1 p.8 may).
            if (player.Cash < cost)
                return false;

            var pendingChoice = new PendingChoice(
                player.Id,
                PendingChoiceKinds.MisbehaveDiscardRedraw,
                contextId: pending.JobId,
                options: new[] { DalinRedrawOptions.PayRedraw, DalinRedrawOptions.Decline },
                prompt: $"Pay ${cost} to discard and re-draw this Misbehave?");
            if (!game.TrySetPendingChoice(pendingChoice, out error))
                return false;
            error = "Choose whether to pay for Dalin's Misbehave redraw.";
            return false;
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.MisbehaveOption"/>: card option (0/1/…)
        /// or FIRST–NEXT step id (<c>step:N</c>). GF9 p.14 — choose between options.
        /// </summary>
        public bool TryResumeMisbehaveOption(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.MisbehaveOption,
                    StringComparison.Ordinal))
            {
                error = "No Misbehave option choice is pending.";
                return false;
            }

            var pending = game.PendingMisbehave;
            if (pending == null || pending.FaceUp == null)
            {
                error = "No Misbehave is pending.";
                return false;
            }

            var selected = submission.SelectedOptionId;
            if (string.IsNullOrWhiteSpace(selected))
            {
                error = "Select a Misbehave option or step.";
                return false;
            }

            if (MisbehaveSteps.TryParseStepOptionId(selected, out var stepIndex))
            {
                choice.StepIndex = stepIndex;
                if (pending.SelectedOptionIndex != null)
                    choice.OptionIndex = pending.SelectedOptionIndex;
                // Fresh bribe / kill hooks for the next skill test.
                choice.SkillCheck = null;
                choice.Kill = null;
                pending.AwaitingNextStep = false;
                pending.CurrentStepIndex = stepIndex;
            }
            else if (int.TryParse(selected, out var optionIndex))
            {
                choice.OptionIndex = optionIndex;
                pending.SelectedOptionIndex = optionIndex;
                pending.CurrentStepIndex = 0;
                pending.AwaitingNextStep = false;
            }
            else
            {
                error = "Unknown Misbehave option id.";
                return false;
            }

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            _resumingMisbehaveOption = true;
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingMisbehaveOption = false;
            }
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.KillVictim"/>: merge victim ids into
        /// <paramref name="choice"/> and re-enter resolve with the frozen skill band.
        /// </summary>
        public bool TryResumeKillVictims(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.KillVictim,
                    StringComparison.Ordinal))
            {
                error = "No kill-victim choice is pending.";
                return false;
            }
            if (!CrewKill.TryParseKillCount(game.PendingChoice.ContextId, out var count))
            {
                error = "Kill-victim context is missing the kill count.";
                return false;
            }

            var player = game.GetPlayer(playerId);
            if (!CrewKill.TryMergeVictimSubmission(
                    player, count, submission, choice.Kill, out var merged, out error))
                return false;
            choice.Kill = merged;

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            _pendingKillAfterVictims = merged;
            _resumingKillVictims = true;
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingKillVictims = false;
            }
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.BribeAmount"/>: merge dollars and re-enter
        /// before the Negotiate roll (GF9 p.6).
        /// </summary>
        public bool TryResumeBribeAmount(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.BribeAmount,
                    StringComparison.Ordinal))
            {
                error = "No bribe-amount choice is pending.";
                return false;
            }

            var player = game.GetPlayer(playerId);
            if (!SkillCheck.TryMergeBribeSubmission(
                    player, submission, choice.SkillCheck, out var merged, out error))
                return false;
            choice.SkillCheck = merged;

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            _resumingBribeOrMedFoam = true;
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingBribeOrMedFoam = false;
            }
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.SkillSwitch"/> (Sheydra / Stitch).
        /// </summary>
        public bool TryResumeSkillSwitch(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.SkillSwitch,
                    StringComparison.Ordinal))
            {
                error = "No skill-switch choice is pending.";
                return false;
            }

            if (!SkillCheck.TryMergeSkillSwitchSubmission(
                    submission, choice.SkillCheck, out var merged, out error))
                return false;
            choice.SkillCheck = merged;

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            _resumingBribeOrMedFoam = true;
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingBribeOrMedFoam = false;
            }
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.SkillReroll"/>: keep or re-roll the first attempt
        /// (Kaylee / Zoe / Inara). FAQ 4.1 p.8 — may abilities always suspend.
        /// </summary>
        public bool TryResumeSkillReroll(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.SkillReroll,
                    StringComparison.Ordinal))
            {
                error = "No skill re-roll choice is pending.";
                return false;
            }
            if (_pendingRerollResult == null)
            {
                error = "Skill re-roll context is missing the first roll.";
                return false;
            }

            if (!SkillCheck.TryMergeSkillRerollSubmission(
                    submission, choice.SkillCheck, out var merged, out error))
                return false;
            choice.SkillCheck = merged;

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            _resumingBribeOrMedFoam = true;
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingBribeOrMedFoam = false;
            }
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.DiscardToReroll"/>: discard gear + re-roll
        /// or decline (Extra Ammo Clips / Yolonda's Pistol).
        /// </summary>
        public bool TryResumeDiscardToReroll(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.DiscardToReroll,
                    StringComparison.Ordinal))
            {
                error = "No discard-to-reroll choice is pending.";
                return false;
            }
            if (_pendingRerollResult == null)
            {
                error = "Discard-to-reroll context is missing the first roll.";
                return false;
            }

            var gearId = game.PendingChoice.ContextId;
            if (!SkillCheck.TryMergeDiscardToRerollSubmission(
                    submission, gearId, choice.SkillCheck, out var merged, out error))
                return false;
            choice.SkillCheck = merged;

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            _resumingBribeOrMedFoam = true;
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingBribeOrMedFoam = false;
            }
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.MedFoamDiscard"/>: merge discard/decline
        /// and re-enter with the frozen skill band.
        /// </summary>
        public bool TryResumeMedFoam(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.MedFoamDiscard,
                    StringComparison.Ordinal))
            {
                error = "No Med Foam choice is pending.";
                return false;
            }

            if (!CrewKill.TryMergeMedFoamSubmission(submission, choice.Kill, out var merged, out error))
                return false;
            if (_pendingKillAfterVictims?.VictimCrewIds != null
                && (merged.VictimCrewIds == null || merged.VictimCrewIds.Count == 0))
            {
                merged.VictimCrewIds = _pendingKillAfterVictims.VictimCrewIds;
            }
            choice.Kill = merged;

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            _pendingKillAfterVictims = null;
            _resumingBribeOrMedFoam = true;
            _resumingKillVictims = true; // allow frozen skill path
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingBribeOrMedFoam = false;
                _resumingKillVictims = false;
            }
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.GoodsMix"/>: merge Fuel/Parts/Cargo/Contraband
        /// into <see cref="MisbehaveChoice"/> and re-enter with the frozen skill band.
        /// Blue Sun: "Goods are Cargo, Contraband, Fuel and Parts... you may choose to Load a mix."
        /// </summary>
        public bool TryResumeGoodsMix(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.GoodsMix,
                    StringComparison.Ordinal))
            {
                error = "No Goods mix choice is pending.";
                return false;
            }

            var contextId = game.PendingChoice.ContextId ?? "";
            var merged = _pendingGoodsMixChoice ?? choice;
            if (!TryMergeGoodsMixSubmission(contextId, submission, merged, out error))
                return false;

            // Copy mix onto the caller's choice for re-enter.
            choice.LoadGoodsFuel = merged.LoadGoodsFuel;
            choice.LoadGoodsParts = merged.LoadGoodsParts;
            choice.LoadGoodsCargo = merged.LoadGoodsCargo;
            choice.LoadGoodsContraband = merged.LoadGoodsContraband;
            if (choice.OptionIndex == null)
                choice.OptionIndex = merged.OptionIndex;
            if (choice.StepIndex == null)
                choice.StepIndex = merged.StepIndex;

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            _pendingGoodsMixChoice = null;
            _resumingGoodsMix = true;
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingGoodsMix = false;
            }
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.MisbehaveWarrantOrWanted"/>: merge path
        /// (none / warrants / wanted-tokens) and re-enter with the frozen skill band.
        /// </summary>
        public bool TryResumeWarrantOrWanted(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out MisbehaveResolution? resolution,
            out string? error,
            IRng? rng = null)
        {
            resolution = null;
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.MisbehaveWarrantOrWanted,
                    StringComparison.Ordinal))
            {
                error = "No Warrant/Wanted discard choice is pending.";
                return false;
            }

            var max = 2;
            if (!string.IsNullOrWhiteSpace(game.PendingChoice.ContextId)
                && int.TryParse(game.PendingChoice.ContextId, out var parsed)
                && parsed > 0)
                max = parsed;

            var merged = _pendingWarrantOrWantedChoice ?? choice;
            if (!TryMergeWarrantOrWantedSubmission(submission, merged, max, out error))
                return false;

            choice.DiscardWarrantsOrWantedPath = merged.DiscardWarrantsOrWantedPath;
            choice.DiscardWarrants = merged.DiscardWarrants;
            choice.ClearWantedCrewIds = merged.ClearWantedCrewIds;
            if (choice.OptionIndex == null)
                choice.OptionIndex = merged.OptionIndex;
            if (choice.StepIndex == null)
                choice.StepIndex = merged.StepIndex;

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            _pendingWarrantOrWantedChoice = null;
            _resumingWarrantOrWanted = true;
            try
            {
                return TryResolve(game, playerId, choice, out resolution, out error, rng);
            }
            finally
            {
                _resumingWarrantOrWanted = false;
            }
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
            if (game.PendingChoice != null
                && !_resumingKillVictims
                && !_resumingBribeOrMedFoam
                && !_resumingMisbehaveOption
                && !_resumingDalin
                && !_resumingGoodsMix
                && !_resumingWarrantOrWanted)
            {
                error = "Resolve the pending choice before continuing Misbehave.";
                return false;
            }

            var player = game.GetPlayer(playerId);
            if (pending.FaceUp == null)
                DrawNext(game);

            // Dalin: once per Work, may pay $200 to discard and re-draw (Supplies.tsv).
            if (TryOfferOrApplyDalin(game, player, pending, out error))
            {
                // Applied redraw — FaceUp is the new card; continue.
            }
            else if (error != null)
            {
                return false;
            }

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
                pending.ClearStepProgress();
                return Finish(game, playerId, card, null, MisbehaveOutcome.Proceed, null, 0, 0, 0, aceCash, true, out resolution, out error);
            }

            // GF9 p.14: "most Misbehave Cards have 2 options on each card. You may attempt either option."
            if (NeedsOptionChoice(pending, card, choice))
            {
                if (!TrySuspendOptionChoice(game, player, card, out error))
                    return false;
                error = "Choose a Misbehave option.";
                return false;
            }

            var optionIndex = choice.OptionIndex
                ?? pending.SelectedOptionIndex
                ?? (card.Options.Count == 1 ? 0 : (int?)null);
            if (optionIndex == null || optionIndex < 0 || optionIndex >= card.Options.Count)
            {
                error = "Invalid Misbehave option.";
                return false;
            }

            pending.SelectedOptionIndex = optionIndex;
            var option = card.Options[optionIndex.Value];
            if (!MeetsRequirement(game, player, option.Details, out error))
                return false;

            var steps = MisbehaveSteps.ForOption(option);
            if (NeedsNextStepChoice(pending, choice, steps))
            {
                if (!TrySuspendNextStepChoice(game, player, card, option, steps, pending.CurrentStepIndex, out error))
                    return false;
                error = "Choose the next Misbehave step.";
                return false;
            }

            var stepIndex = choice.StepIndex ?? pending.CurrentStepIndex;
            if (stepIndex < 0 || stepIndex >= steps.Count)
            {
                error = "Invalid Misbehave step.";
                return false;
            }

            pending.CurrentStepIndex = stepIndex;
            pending.AwaitingNextStep = false;
            var step = steps[stepIndex];
            // Prefer structured step overlay; fall back to option-level structured then prose.
            var stepOption = BuildStepOption(option, step, stepIndex);
            var details = stepOption.Details ?? "";
            if (IsAllianceAlertUpdate(card, details))
            {
                CycleAllianceAlert(game);
                var die = Dice.D6(rng);
                var alertOutcome = die <= player.Warrants
                    ? MisbehaveOutcome.Botched
                    : MisbehaveOutcome.Proceed;
                pending.ClearStepProgress();
                return Finish(game, playerId, card, option, alertOutcome, null, 0, 0, 0, 0, false, out resolution, out error);
            }

            // Structured overlay preferred when present; otherwise prose/regex path.
            if (!TryResolveSkillAndBand(
                game, player, card, stepOption, details, choice, rng, pending,
                out var check, out var bandText, out var structuredEffects, out var bribeCash, out error))
                return false;

            if (HasLocalEffect(structuredEffects, MisbehaveLocalEffectType.ReplaceCard) || IsReplaceCard(details))
            {
                var extra = Contains(details, "Draw two") || Contains(details, "Draw 2") ? 1 : 0;
                pending.Remaining += extra;
                ClearFrozenSkill();
                pending.ClearStepProgress();
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

            var useStructured = structuredEffects != null && structuredEffects.Count > 0;
            var effectBand = bandText ?? details;
            var effectText = check == null ? details : effectBand;

            if (optionalPay && !paying)
            {
                effectBand = "Attempt Botched";
                effectText = effectBand;
                useStructured = false;
            }

            // Suspend for Kill N victim pick before mutating cash / warrants / crew.
            var plannedKill = PlannedKillCount(
                useStructured ? structuredEffects : null,
                effectText);
            if (CrewKill.NeedsVictimChoice(player, plannedKill, choice.Kill))
            {
                FreezeSkill(check, bandText, structuredEffects, bribeCash);
                if (!CrewKill.TrySuspendVictimChoice(game, player, plannedKill, out error))
                {
                    ClearFrozenSkill();
                    return false;
                }
                error = "Choose which crew are killed.";
                return false;
            }

            // Optional Med Foam discard before mutating (after victims known).
            if (CrewKill.NeedsMedFoamChoice(game, player, plannedKill, choice.Kill))
            {
                FreezeSkill(check, bandText, structuredEffects, bribeCash);
                if (!CrewKill.TrySuspendMedFoamChoice(game, player, plannedKill, out error))
                {
                    ClearFrozenSkill();
                    return false;
                }
                error = "Choose whether to discard Med Foam for a successful Medic Check.";
                return false;
            }

            // Blue Sun Goods: Take/Load N Goods → PendingChoice GoodsMix (Fuel/Parts/Cargo/Contraband).
            if (NeedsGoodsMixChoice(
                    useStructured ? structuredEffects : null,
                    effectText,
                    choice,
                    out var goodsContext,
                    out var goodsPrompt))
            {
                FreezeSkill(check, bandText, structuredEffects, bribeCash);
                if (!TrySuspendGoodsMix(game, player, choice, goodsContext, goodsPrompt, out error))
                {
                    ClearFrozenSkill();
                    return false;
                }
                error = goodsPrompt;
                return false;
            }

            // Optional Warrant / Wanted discard (Dead to Rights; Improbably Complex may discard Warrant).
            if (NeedsWarrantOrWantedChoice(
                    useStructured ? structuredEffects : null,
                    choice,
                    out var discardMax,
                    out var discardPrompt,
                    out var warrantsOnly))
            {
                FreezeSkill(check, bandText, structuredEffects, bribeCash);
                if (!TrySuspendWarrantOrWanted(
                        game, player, choice, discardMax, discardPrompt, warrantsOnly, out error))
                {
                    ClearFrozenSkill();
                    return false;
                }
                error = discardPrompt;
                return false;
            }

            // Validate Solid-loss discard hooks before mutating crew / warrants.
            // LoseSolidIfAble skips when not Solid (printed "if able").
            if (WouldLoseSolid(details)
                || WouldLoseSolid(effectBand)
                || (useStructured && HasLocalEffect(structuredEffects, MisbehaveLocalEffectType.LoseSolid)))
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
            else if (useStructured
                && HasLocalEffect(structuredEffects, MisbehaveLocalEffectType.LoseSolidIfAble))
            {
                var lostId = ResolveLoseSolidId(player, choice.LoseSolidId);
                if (lostId != null
                    && !ContactSolidBenefits.CanDiscardDownAfterLosing(
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

            ClearFrozenSkill();

            int warrants;
            int killed;
            int loaded;
            MisbehaveOutcome outcome;
            if (useStructured)
            {
                if (!TryApplyStructuredEffects(
                    game, player, structuredEffects!, rng, choice,
                    ref cashDelta, out warrants, out killed, out loaded, out outcome, out error))
                    return false;
            }
            else
            {
                warrants = 0;
                if (Contains(effectBand, "Warrant Issued"))
                {
                    player.Warrants++;
                    warrants = 1;
                }

                if (!TryKillCrew(game, player, effectText, rng, choice.Kill, out killed, out error))
                    return false;
                var goodsN = PlannedGoodsLoadCount(null, effectText);
                if (goodsN > 0)
                {
                    if (!TryApplyLoadGoods(player, goodsN, choice, out loaded, out error))
                        return false;
                }
                else
                    loaded = LoadGoods(player, effectText);
                cashDelta += TakeCash(player, effectText);
                ApplyWanted(player, effectText, choice.TargetCrewId);
                ApplyDisgruntle(player, effectText);
                ApplyClearDisgruntled(player, effectText);
                if (Contains(effectText, "Discard all Jobs in Hand")
                    || Contains(details, "Discard all Jobs in Hand"))
                    DiscardAllInactiveJobsInHand(game, player);
                if (!TryApplySolidLoss(game, player, effectText, effectText, choice, out error))
                    return false;
                ApplyWarrantDiscard(player, effectText, choice.DiscardWarrants);
                outcome = Contains(effectBand, "Attempt Botched")
                    ? MisbehaveOutcome.Botched
                    : MisbehaveOutcome.Proceed;
            }

            // GF9 / FAQ: Warrant Issued while Working discards the Job. Niska Pound of Flesh: Kill a Crew.
            // Mid-card Continue bands that also issue a Warrant still discard (warrant ends the Job).
            if (warrants > 0 && game.PendingMisbehave != null)
            {
                if (!TryAbandonJobForWarrant(
                    game, player, rng, choice.Kill, ref killed, out var abandonedWork, out error))
                    return false;
                game.Misbehave?.ResolveIntoDiscard(card);
                if (game.PendingMisbehave != null)
                {
                    game.PendingMisbehave.FaceUp = null;
                    game.PendingMisbehave.ClearStepProgress();
                }
                resolution = new MisbehaveResolution(
                    card, option, MisbehaveOutcome.Botched, check, warrants, killed, loaded, cashDelta, false, abandonedWork);
                error = null;
                return true;
            }

            // FIRST–NEXT: Continue / non-final Proceed → suspend for the NEXT step.
            if (outcome != MisbehaveOutcome.Botched
                && MisbehaveSteps.IsContinueToNext(effectBand, stepIndex, steps.Count))
            {
                ApplyStepCarryForward(pending, effectBand, useStructured ? structuredEffects : null);
                pending.CurrentStepIndex = stepIndex + 1;
                pending.AwaitingNextStep = true;
                choice.SkillCheck = null;
                choice.Kill = null;
                if (!TrySuspendNextStepChoice(game, player, card, option, steps, pending.CurrentStepIndex, out error))
                {
                    pending.AwaitingNextStep = false;
                    return false;
                }
                error = "Choose the next Misbehave step.";
                resolution = null;
                return false;
            }

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

            pending.ClearStepProgress();
            return Finish(game, playerId, card, option, outcome, check, warrants, killed, loaded, cashDelta, false, out resolution, out error);
        }

        /// <summary>
        /// Prefer option.SkillCheck / option.Bands when present; else SkillCheck.TryParse + BandText.
        /// Card-level Bribes/Kosherized flags fill structured specs that omitted them.
        /// </summary>
        private bool TryResolveSkillAndBand(
            GameState game,
            PlayerState player,
            MisbehaveCard card,
            MisbehaveOption option,
            string details,
            MisbehaveChoice choice,
            IRng rng,
            PendingMisbehave pending,
            out SkillCheckResult? check,
            out string bandText,
            out IReadOnlyList<MisbehaveEffect>? structuredEffects,
            out int bribeCash,
            out string? error)
        {
            check = null;
            bandText = details;
            structuredEffects = null;
            bribeCash = 0;
            error = null;

            // Resume after kill-victim PendingChoice: reuse the already-rolled skill band.
            if (_frozenSkillReady)
            {
                check = _frozenSkillCheck;
                bandText = _frozenBandText ?? details;
                structuredEffects = _frozenStructuredEffects;
                bribeCash = _frozenBribeCash;
                return true;
            }

            // Printed "If you have X, Proceed. Otherwise, …" — structured proceedIfTag overlay.
            var job = game.Jobs != null && game.Jobs.TryGet(pending.JobId, out var jobCard)
                ? jobCard
                : null;
            if (!string.IsNullOrWhiteSpace(option.ProceedIfTag)
                && HasTag(game, player, option.ProceedIfTag, job))
            {
                structuredEffects = option.HasStructuredEffects
                    ? option.Effects
                    : new[] { MisbehaveEffect.Of(MisbehaveLocalEffectType.Proceed) };
                return true;
            }

            if (!TryGetSkillCheck(option, card, details, pending, player, out var skillCheck))
            {
                if (option.HasStructuredEffects)
                    structuredEffects = option.Effects;
                return true;
            }

            var activeJob = player.FindActive(pending.JobId);
            // Sheydra / Stitch: once per job, before Bribes (FAQ 4.1 p.9 — never both).
            if (SkillCheck.NeedsSkillSwitchChoice(
                    player, skillCheck, choice.SkillCheck, activeJob, AbilityContext.Misbehaving))
            {
                if (!SkillCheck.TrySuspendSkillSwitch(
                        game,
                        player,
                        contextId: $"{card.Id}:{pending.SelectedOptionIndex}:{pending.CurrentStepIndex}",
                        out error))
                    return false;
                error = "Choose whether to switch this skill test.";
                return false;
            }

            skillCheck = SkillCheck.ApplySkillSwitchIfChosen(
                player, skillCheck, choice.SkillCheck, activeJob, AbilityContext.Misbehaving);

            // GF9 p.6 / Cortland: "Before you roll a dice, you may choose to pay Bribes."
            if (SkillCheck.NeedsBribeChoice(player, skillCheck, choice.SkillCheck))
            {
                if (!SkillCheck.TrySuspendBribeChoice(
                        game,
                        player,
                        contextId: $"{card.Id}:{pending.SelectedOptionIndex}:{pending.CurrentStepIndex}",
                        out error))
                    return false;
                error = "Choose how many Bribes to pay before rolling.";
                return false;
            }

            // Resume after discard-to-reroll may, or skillReroll may.
            if (_pendingRerollResult != null
                && choice.SkillCheck?.AcceptDiscardReroll is bool acceptDiscard)
            {
                if (acceptDiscard)
                {
                    var gearId = choice.SkillCheck.DiscardRerollGearId
                        ?? AbilityDispatcher.FindDiscardToRerollGear(
                            game, player, _pendingRerollResult.Check.Skill, AbilityContext.WorkingJob);
                    if (gearId == null
                        || !GearCarriage.TryDiscardGear(player, gearId, out error))
                    {
                        _pendingRerollResult = null;
                        return false;
                    }
                    check = _pendingRerollResult.Check.RerollKeepingBribes(
                        player, rng, _pendingRerollResult, game, AbilityContext.Misbehaving);
                }
                else
                    check = _pendingRerollResult;
                _pendingRerollResult = null;
            }
            else if (_pendingRerollResult != null && choice.SkillCheck?.AcceptReroll is bool acceptReroll)
            {
                check = acceptReroll
                    ? _pendingRerollResult.Check.RerollKeepingBribes(
                        player, rng, _pendingRerollResult, game, AbilityContext.Misbehaving)
                    : _pendingRerollResult;
                _pendingRerollResult = null;

                // After crew skillReroll, discard-to-reroll gear may still apply.
                if (AbilityDispatcher.NeedsDiscardToRerollChoice(
                        game, player, check.Check.Skill, choice.SkillCheck, AbilityContext.WorkingJob))
                {
                    var gearId = AbilityDispatcher.FindDiscardToRerollGear(
                        game, player, check.Check.Skill, AbilityContext.WorkingJob)!;
                    _pendingRerollResult = check;
                    if (!SkillCheck.TrySuspendDiscardToReroll(game, player, gearId, out error))
                    {
                        _pendingRerollResult = null;
                        return false;
                    }
                    error = "Choose whether to discard gear to re-roll this Fight test.";
                    return false;
                }
            }
            else
            {
                if (!skillCheck.TryResolve(
                        player, rng, out check, out error, choice.SkillCheck,
                        game, AbilityContext.Misbehaving, job))
                    return false;

                // FAQ 4.1 p.8 may: always suspend take/decline re-roll when skillReroll matches.
                if (AbilityDispatcher.NeedsSkillRerollChoice(
                        player, skillCheck.Skill, choice.SkillCheck, AbilityContext.WorkingJob))
                {
                    _pendingRerollResult = check;
                    if (!SkillCheck.TrySuspendSkillReroll(
                            game,
                            player,
                            contextId: $"{card.Id}:{pending.SelectedOptionIndex}:{pending.CurrentStepIndex}",
                            out error))
                    {
                        _pendingRerollResult = null;
                        return false;
                    }
                    error = "Choose whether to re-roll this skill test.";
                    return false;
                }

                if (AbilityDispatcher.NeedsDiscardToRerollChoice(
                        game, player, skillCheck.Skill, choice.SkillCheck, AbilityContext.WorkingJob))
                {
                    var gearId = AbilityDispatcher.FindDiscardToRerollGear(
                        game, player, skillCheck.Skill, AbilityContext.WorkingJob)!;
                    _pendingRerollResult = check;
                    if (!SkillCheck.TrySuspendDiscardToReroll(game, player, gearId, out error))
                    {
                        _pendingRerollResult = null;
                        return false;
                    }
                    error = "Choose whether to discard gear to re-roll this Fight test.";
                    return false;
                }
            }

            bribeCash = check.BribeDollarsPaid;
            var sum = check.Total
                + BonusFromSkillCheck(game, player, option, details)
                + pending.NextTalkBonus;
            check = check.WithTotal(sum);
            // Next-test talk bonus is consumed by this roll.
            pending.NextTalkBonus = 0;
            pending.NextFightKosherized = false;

            if (option.HasStructuredBands)
            {
                var band = MisbehaveBand.Pick(option.Bands, sum);
                if (band != null)
                {
                    if (band.Effects.Count > 0)
                        structuredEffects = band.Effects;
                    bandText = !string.IsNullOrWhiteSpace(band.Text) ? band.Text! : details;
                }
                else
                    bandText = details;
            }
            else
            {
                // When printed bands omit the success line (C&P FIRST steps sometimes list
                // only the fail band), fall back to the skill-test target: hit → Proceed.
                bandText = SkillCheck.BandText(details, sum)
                    ?? (check.Success ? "Proceed" : "Attempt Botched");
                if (option.HasStructuredEffects)
                    structuredEffects = option.Effects;
            }

            return true;
        }

        private static bool TryGetSkillCheck(
            MisbehaveOption option,
            MisbehaveCard card,
            string details,
            PendingMisbehave pending,
            PlayerState player,
            out SkillCheck skillCheck)
        {
            if (option.SkillCheck != null)
            {
                var spec = option.SkillCheck;
                var kosherized = spec.Kosherized || card.Kosherized
                    || (pending.NextFightKosherized && spec.Skill == Skill.Fight);
                var bribes = (spec.BribesAllowed || card.Bribes) && spec.Skill == Skill.Talk;
                skillCheck = new SkillCheck(spec.Skill, spec.Target, kosherized, bribes);
                skillCheck = SkillCheck.WithAbilityBribes(skillCheck, player);
                return true;
            }

            if (!SkillCheck.TryParse(details, out skillCheck))
                return false;

            var kosher = skillCheck.Kosherized || card.Kosherized
                || (pending.NextFightKosherized && skillCheck.Skill == Skill.Fight);
            var bribe = (skillCheck.BribesAllowed || card.Bribes) && skillCheck.Skill == Skill.Talk;
            if (kosher != skillCheck.Kosherized || bribe != skillCheck.BribesAllowed)
                skillCheck = new SkillCheck(skillCheck.Skill, skillCheck.Target, kosher, bribe);
            skillCheck = SkillCheck.WithAbilityBribes(skillCheck, player);
            return true;
        }

        private void FreezeSkill(
            SkillCheckResult? check,
            string? bandText,
            IReadOnlyList<MisbehaveEffect>? structuredEffects,
            int bribeCash)
        {
            _frozenSkillReady = true;
            _frozenSkillCheck = check;
            _frozenBandText = bandText;
            _frozenStructuredEffects = structuredEffects;
            _frozenBribeCash = bribeCash;
        }

        private void ClearFrozenSkill()
        {
            _frozenSkillReady = false;
            _frozenSkillCheck = null;
            _frozenBandText = null;
            _frozenStructuredEffects = null;
            _frozenBribeCash = 0;
            _pendingKillAfterVictims = null;
            _pendingRerollResult = null;
        }

        private static bool NeedsOptionChoice(
            PendingMisbehave pending,
            MisbehaveCard card,
            MisbehaveChoice choice)
        {
            if (choice.OptionIndex != null || pending.SelectedOptionIndex != null)
                return false;
            return card.Options.Count > 1;
        }

        private static bool NeedsNextStepChoice(
            PendingMisbehave pending,
            MisbehaveChoice choice,
            IReadOnlyList<MisbehaveStep> steps)
        {
            if (steps.Count <= 1)
                return false;
            if (choice.StepIndex != null)
                return false;
            return pending.AwaitingNextStep;
        }

        private static MisbehaveOption BuildStepOption(
            MisbehaveOption parent,
            MisbehaveStep step,
            int stepIndex)
        {
            // Structured step overlay wins; else inherit parent structured fields only on
            // single-step options (multi-step prose must not use option-level skill for NEXT).
            if (step.SkillCheck != null || step.HasStructuredBands || step.HasStructuredEffects)
                return step.AsOption(stepIndex == 0 ? parent.ProceedIfTag : null);

            if (parent.HasStructuredSteps)
                return step.AsOption(stepIndex == 0 ? parent.ProceedIfTag : null);

            // Single-step: keep parent structured skill/bands/effects.
            var steps = MisbehaveSteps.ForOption(parent);
            if (steps.Count == 1)
            {
                return new MisbehaveOption(
                    parent.Name,
                    string.IsNullOrWhiteSpace(step.Details) ? parent.Details : step.Details,
                    parent.SkillCheck,
                    parent.Bands,
                    parent.Effects,
                    parent.ProceedIfTag);
            }

            // Multi-step prose: resolve against this step's details only.
            return step.AsOption(stepIndex == 0 ? parent.ProceedIfTag : null);
        }

        private static bool TrySuspendOptionChoice(
            GameState game,
            PlayerState player,
            MisbehaveCard card,
            out string? error)
        {
            error = null;
            var legal = new List<string>();
            for (var i = 0; i < card.Options.Count; i++)
            {
                if (MeetsRequirement(game, player, card.Options[i].Details, out _))
                    legal.Add(i.ToString());
            }

            if (legal.Count == 0)
            {
                error = "No legal Misbehave options.";
                return false;
            }

            // One legal option among many still needs a pick? GF9 says choose; if only one
            // meets Requires, auto-select would skip the prompt — still suspend so the player
            // confirms (assignment: multiple options → suspend).
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.MisbehaveOption,
                contextId: card.Id,
                options: legal,
                prompt: "Choose a Misbehave option.");
            return game.TrySetPendingChoice(pending, out error);
        }

        private static bool TrySuspendNextStepChoice(
            GameState game,
            PlayerState player,
            MisbehaveCard card,
            MisbehaveOption option,
            IReadOnlyList<MisbehaveStep> steps,
            int stepIndex,
            out string? error)
        {
            error = null;
            if (stepIndex < 0 || stepIndex >= steps.Count)
            {
                error = "Invalid Misbehave step.";
                return false;
            }

            var step = steps[stepIndex];
            var id = MisbehaveSteps.StepOptionId(stepIndex);
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.MisbehaveOption,
                contextId: $"{card.Id}:step:{stepIndex}",
                options: new[] { id },
                prompt: string.IsNullOrWhiteSpace(step.Name)
                    ? "Continue to the next Misbehave step."
                    : $"Continue: {step.Name}");
            return game.TrySetPendingChoice(pending, out error);
        }

        private static void ApplyStepCarryForward(
            PendingMisbehave pending,
            string effectBand,
            IReadOnlyList<MisbehaveEffect>? structuredEffects)
        {
            if (Contains(effectBand, "next Fight Test is Kosherized")
                || HasLocalEffect(structuredEffects, MisbehaveLocalEffectType.NextFightKosherized))
                pending.NextFightKosherized = true;

            // Prefer structured nextTalkBonus so band.Text can keep the printed phrase
            // without double-counting the prose regex.
            if (HasLocalEffect(structuredEffects, MisbehaveLocalEffectType.NextTalkBonus))
            {
                foreach (var effect in structuredEffects!)
                {
                    if (!effect.Is(MisbehaveLocalEffectType.NextTalkBonus))
                        continue;
                    pending.NextTalkBonus += effect.Count > 0 ? effect.Count : 1;
                }
                return;
            }

            var bonus = Regex.Match(
                effectBand,
                @"\+(\d+)\s+Negotiate\s+to\s+next\s+Test",
                RegexOptions.IgnoreCase);
            if (bonus.Success)
                pending.NextTalkBonus += int.Parse(bonus.Groups[1].Value);
        }

        private static bool TryApplyStructuredEffects(
            GameState game,
            PlayerState player,
            IReadOnlyList<MisbehaveEffect> effects,
            IRng rng,
            MisbehaveChoice choice,
            ref int cashDelta,
            out int warrants,
            out int killed,
            out int loaded,
            out MisbehaveOutcome outcome,
            out string? error)
        {
            warrants = 0;
            killed = 0;
            loaded = 0;
            outcome = MisbehaveOutcome.Proceed;
            error = null;

            var context = new CardEffectContext(CardEffectSource.Misbehave, choice.Kill);

            foreach (var effect in effects)
            {
                if (effect.Shared != null)
                {
                    if (!CardEffectApplicator.TryApply(
                            game,
                            player,
                            new[] { effect.Shared },
                            rng,
                            context,
                            out var sharedResult,
                            out error))
                        return false;
                    warrants += sharedResult.WarrantsIssued;
                    killed += sharedResult.CrewKilled;
                    loaded += sharedResult.GoodsLoaded;
                    cashDelta += sharedResult.CashGained;
                    continue;
                }

                if (effect.Local == null)
                    continue;
                switch (effect.Local.Value)
                {
                    case MisbehaveLocalEffectType.Proceed:
                        outcome = MisbehaveOutcome.Proceed;
                        break;
                    case MisbehaveLocalEffectType.Botched:
                        outcome = MisbehaveOutcome.Botched;
                        break;
                    case MisbehaveLocalEffectType.KillAllCrew:
                        killed += CrewKill.KillAll(game, player, rng, choice.Kill);
                        break;
                    case MisbehaveLocalEffectType.Wanted:
                        ApplyWanted(player, "Wanted", choice.TargetCrewId);
                        break;
                    case MisbehaveLocalEffectType.DisgruntleMercs:
                        player.Roster.DisgruntleWhere(
                            m => m.Card.HasProfession("Merc") || m.Card.HasProfession("Soldier"));
                        break;
                    case MisbehaveLocalEffectType.DisgruntleTech:
                        player.Roster.DisgruntleWhere(m => m.Card.Tech > 0);
                        break;
                    case MisbehaveLocalEffectType.DisgruntleAllCrew:
                        player.Roster.DisgruntleWhere(_ => true);
                        break;
                    case MisbehaveLocalEffectType.LoseSolid:
                        break;
                    case MisbehaveLocalEffectType.DiscardWarrants:
                        var discard = effect.Count > 0 ? effect.Count : choice.DiscardWarrants;
                        if (discard <= 0)
                            discard = 1;
                        if (discard > player.Warrants)
                            discard = player.Warrants;
                        player.Warrants -= discard;
                        break;
                    case MisbehaveLocalEffectType.LoadGoods:
                        if (!TryApplyLoadGoods(player, effect.Count, choice, out var goodsLoaded, out error))
                            return false;
                        loaded += goodsLoaded;
                        break;
                    case MisbehaveLocalEffectType.MayDiscardWarrantsOrWanted:
                        if (!TryApplyMayDiscardWarrantsOrWanted(player, effect.Count, choice, out error))
                            return false;
                        break;
                    case MisbehaveLocalEffectType.DiscardCargo:
                        var cargoN = effect.Count > 0 ? effect.Count : 1;
                        if (player.Cargo < cargoN)
                        {
                            error = cargoN == 1
                                ? "Need 1 Cargo to discard."
                                : $"Need {cargoN} Cargo to discard.";
                            return false;
                        }
                        player.Cargo -= cargoN;
                        break;
                    case MisbehaveLocalEffectType.DiscardJobHand:
                        DiscardAllInactiveJobsInHand(game, player);
                        break;
                    case MisbehaveLocalEffectType.MayDiscardWarrants:
                        if (!TryApplyMayDiscardWarrants(player, effect.Count, choice, out error))
                            return false;
                        break;
                    case MisbehaveLocalEffectType.ClearDisgruntledMoral:
                        player.Roster.ClearDisgruntledMoral();
                        break;
                    case MisbehaveLocalEffectType.SeizeGear:
                        ApplySeizeGear(game, player, effect);
                        break;
                    case MisbehaveLocalEffectType.WantedCrewRoll:
                        if (!TryApplyWantedCrewRoll(
                                game, player, rng, choice, out var anySeized, out error))
                            return false;
                        if (anySeized)
                        {
                            player.Warrants++;
                            warrants++;
                        }
                        else
                            outcome = MisbehaveOutcome.Proceed;
                        break;
                    case MisbehaveLocalEffectType.DisgruntleWanted:
                        player.Roster.DisgruntleWhere(m => m.Wanted);
                        break;
                    case MisbehaveLocalEffectType.ReturnWantedToShip:
                        ReturnWantedCrewToShip(game, player);
                        break;
                    case MisbehaveLocalEffectType.DisgruntleNonDisgruntled:
                        player.Roster.DisgruntleWhere(m => !m.Disgruntled);
                        break;
                    case MisbehaveLocalEffectType.ReturnHighestFightToShip:
                        ReturnHighestFightToShip(game, player);
                        break;
                    case MisbehaveLocalEffectType.LoseSolidIfAble:
                        break;
                    case MisbehaveLocalEffectType.ReplaceCard:
                        break;
                    case MisbehaveLocalEffectType.NextFightKosherized:
                    case MisbehaveLocalEffectType.NextTalkBonus:
                        // Carry-forward applied in ApplyStepCarryForward on Continue.
                        break;
                }
            }

            if (HasLocalEffect(effects, MisbehaveLocalEffectType.LoseSolid)
                || HasLocalEffect(effects, MisbehaveLocalEffectType.LoseSolidIfAble))
            {
                var requireSolid = HasLocalEffect(effects, MisbehaveLocalEffectType.LoseSolid);
                var solidId = ResolveLoseSolidId(player, choice.LoseSolidId);
                if (solidId == null)
                {
                    if (requireSolid)
                    {
                        error = "Not Solid with a Contact to lose.";
                        return false;
                    }
                }
                else
                {
                    var solidChoice = choice.SolidRep ?? new SolidRepChoice();
                    if (solidChoice.Kill == null && choice.Kill != null)
                        solidChoice.Kill = choice.Kill;
                    if (!ContactSolidBenefits.TryLoseSolid(
                            game, player, choice.LoseSolidId, solidChoice, out error))
                        return false;
                }
            }

            // Director's Cut C&P p.49 No one left: all Crew Killed or Returned to Ship → Botched.
            if (game.PendingMisbehave != null && JobWorkCrew.AvailableCount(player) == 0)
                outcome = MisbehaveOutcome.Botched;

            return true;
        }

        private static bool HasLocalEffect(
            IReadOnlyList<MisbehaveEffect>? effects,
            MisbehaveLocalEffectType type)
        {
            if (effects == null)
                return false;
            foreach (var effect in effects)
            {
                if (effect.Is(type))
                    return true;
            }
            return false;
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

        public static bool HasTag(GameState game, PlayerState player, string? tag, JobCard? job = null)
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
            {
                // Returned-to-Ship crew do not satisfy name tags while Working.
                var named = false;
                foreach (var member in player.Roster.Members)
                {
                    if (!NamesMatch(member.Name, tag) && !NamesMatch(member.Id, tag))
                        continue;
                    if (JobWorkCrew.IsUnavailable(player, member))
                        continue;
                    named = true;
                    break;
                }
                if (named)
                    return true;
                // Fall through — profession/keyword/gear may still match.
            }
            if (LawmanRules.HasProfessionForJob(player, job, tag))
                return true;
            if (LawmanRules.HasKeywordForJob(player, job, tag))
                return true;

            // When no Illegal Job filter, fall back to full roster keywords (HasKeywordForJob
            // already covers Legal / null job). Profession covered above.

            if (game.Gear != null)
            {
                // FAQ 4.1 p.2 / GF9 p.14: Onboard Ship Gear may not be used — only carried Gear.
                // PBH: Lawmen stay onboard on Illegal Jobs — their carried gear is unused.
                // Director's Cut C&P p.49: Returned to Ship crew's Gear unused.
                if (job != null && !job.Legal)
                {
                    foreach (var gearId in player.Gear)
                    {
                        if (!GearCarriage.IsCarried(player, gearId))
                            continue;
                        var carrierId = GearCarriage.CarrierOf(player, gearId);
                        if (carrierId != null)
                        {
                            if (JobWorkCrew.IsReturnedToShip(player, carrierId))
                                continue;
                            var carrier = player.Roster.Find(carrierId);
                            if (carrier != null && LawmanRules.StaysOnboardForJob(carrier, job))
                                continue;
                        }
                        if (game.Gear.TryGet(gearId, out var gear)
                            && (NamesMatch(gear.Id, tag) || NamesMatch(gear.Name, tag)))
                            return true;
                        foreach (var keyword in gear.Keywords)
                        {
                            if (NamesMatch(keyword, tag))
                                return true;
                        }
                    }
                }
                else if (GearCarriage.HasUsableGearTag(game, player, tag))
                {
                    // Still exclude Returned-to-Ship carriers.
                    foreach (var gearId in player.Gear)
                    {
                        if (!GearCarriage.IsCarried(player, gearId))
                            continue;
                        var carrierId = GearCarriage.CarrierOf(player, gearId);
                        if (carrierId != null && JobWorkCrew.IsReturnedToShip(player, carrierId))
                            continue;
                        if (!game.Gear.TryGet(gearId, out var gear))
                            continue;
                        if (NamesMatch(gear.Id, tag) || NamesMatch(gear.Name, tag))
                            return true;
                        foreach (var keyword in gear.Keywords)
                        {
                            if (NamesMatch(keyword, tag))
                                return true;
                        }
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

        private static int BonusFromSkillCheck(
            GameState game,
            PlayerState player,
            MisbehaveOption option,
            string details)
        {
            if (option.SkillCheck != null
                && (option.SkillCheck.Bonuses.Count > 0 || option.SkillCheck.TargetModifiers.Count > 0))
            {
                var bonus = 0;
                foreach (var mod in option.SkillCheck.TargetModifiers)
                {
                    if (mod.Type == MisbehaveTargetModifierType.MinusPerWarrant && mod.Amount != 0)
                        bonus += mod.Amount * player.Warrants;
                }
                foreach (var entry in option.SkillCheck.Bonuses)
                {
                    if (entry.Amount != 0 && HasTag(game, player, entry.Tag))
                        bonus += entry.Amount;
                }
                return bonus;
            }
            return BonusFromGear(game, player, details) + BonusFromWarrantTargetMod(player, details);
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

        /// <summary>
        /// Prose fallback for printed "Negotiate 8 - 1 for each of your Warrants":
        /// add (amount × warrants) to the total so absolute bands (8+) stay correct.
        /// </summary>
        private static int BonusFromWarrantTargetMod(PlayerState player, string details)
        {
            var match = Regex.Match(
                details ?? "",
                @"-\s*(\d+)\s+for each of your Warrants",
                RegexOptions.IgnoreCase);
            if (!match.Success || player.Warrants <= 0)
                return 0;
            return int.Parse(match.Groups[1].Value) * player.Warrants;
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

        private static bool TryKillCrew(
            GameState game,
            PlayerState player,
            string text,
            IRng rng,
            KillChoice? killChoice,
            out int killed,
            out string? error)
        {
            killed = 0;
            error = null;
            if (Contains(text, "Kill all Crew"))
            {
                killed = CrewKill.KillAll(game, player, rng, killChoice);
                return true;
            }

            var count = ParseKillCrewCount(text);
            if (count <= 0)
                return true;
            return CrewKill.TryKillUpTo(game, player, count, rng, out killed, out error, killChoice);
        }

        private static int ParseKillCrewCount(string text)
        {
            if (Contains(text, "Kill all Crew"))
                return int.MaxValue;
            var numbered = Regex.Match(text, @"Kill\s+(\d+)\s+Crew", RegexOptions.IgnoreCase);
            if (numbered.Success)
                return int.Parse(numbered.Groups[1].Value);
            if (Regex.IsMatch(text, @"Kill\s+(a|1)\s+Crew", RegexOptions.IgnoreCase))
                return 1;
            return 0;
        }

        /// <summary>
        /// Kill N from structured effects or prose band (Kill all → no victim choice).
        /// </summary>
        private static int PlannedKillCount(
            IReadOnlyList<MisbehaveEffect>? structuredEffects,
            string effectText)
        {
            if (structuredEffects != null && structuredEffects.Count > 0)
            {
                foreach (var effect in structuredEffects)
                {
                    if (effect.Is(MisbehaveLocalEffectType.KillAllCrew))
                        return int.MaxValue;
                    if (effect.Is(CardEffectType.KillCrew))
                        return effect.Count > 0 ? effect.Count : 1;
                }
                return 0;
            }
            return ParseKillCrewCount(effectText);
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

        /// <summary>
        /// Structured / choice-backed Take N Goods. Blue Sun: Cargo, Contraband, Fuel, Parts;
        /// mix allowed via <see cref="MisbehaveChoice"/> Goods fields.
        /// </summary>
        private static bool TryApplyLoadGoods(
            PlayerState player,
            int count,
            MisbehaveChoice choice,
            out int loaded,
            out string? error)
        {
            loaded = 0;
            error = null;
            var n = count > 0 ? count : 1;
            var fuel = choice.LoadGoodsFuel;
            var parts = choice.LoadGoodsParts;
            var cargo = choice.LoadGoodsCargo;
            var contra = choice.LoadGoodsContraband;
            var sum = fuel + parts + cargo + contra;
            if (sum != n)
            {
                error = $"Load {n} Goods requires a Goods composition choice totaling {n}.";
                return false;
            }
            if (!HoldSpace.TryExplain(
                    player,
                    out error,
                    addFuel: fuel,
                    addParts: parts,
                    addCargo: cargo,
                    addContraband: contra))
                return false;
            player.Fuel += fuel;
            player.Parts += parts;
            player.Cargo += cargo;
            player.Contraband += contra;
            loaded = n;
            return true;
        }

        private static bool NeedsGoodsMixChoice(
            IReadOnlyList<MisbehaveEffect>? effects,
            string effectText,
            MisbehaveChoice choice,
            out string contextId,
            out string prompt)
        {
            contextId = "";
            prompt = "";
            var n = PlannedGoodsLoadCount(effects, effectText);
            if (n <= 0)
                return false;
            var sum = choice.LoadGoodsFuel
                + choice.LoadGoodsParts
                + choice.LoadGoodsCargo
                + choice.LoadGoodsContraband;
            if (sum == n)
                return false;
            if (sum != 0)
                return false; // invalid partial — TryApplyLoadGoods will error
            contextId = GoodsMixContexts.Load(n);
            prompt = $"Choose a mix of {n} Goods (Fuel/Parts/Cargo/Contraband).";
            return true;
        }

        private static int PlannedGoodsLoadCount(
            IReadOnlyList<MisbehaveEffect>? effects,
            string effectText)
        {
            if (effects != null && effects.Count > 0)
            {
                foreach (var effect in effects)
                {
                    if (effect.Is(MisbehaveLocalEffectType.LoadGoods))
                        return effect.Count > 0 ? effect.Count : 1;
                }
                return 0;
            }
            var match = Regex.Match(
                effectText ?? "",
                @"(?:Take|Load)\s+(\d+)\s+Goods",
                RegexOptions.IgnoreCase);
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private bool TrySuspendGoodsMix(
            GameState game,
            PlayerState player,
            MisbehaveChoice choice,
            string contextId,
            string prompt,
            out string? error)
        {
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.GoodsMix,
                contextId: contextId,
                prompt: prompt);
            if (!game.TrySetPendingChoice(pending, out error))
                return false;
            _pendingGoodsMixChoice = choice;
            return true;
        }

        private static bool TryMergeGoodsMixSubmission(
            string contextId,
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            out string? error)
        {
            error = null;
            if (submission.Values == null || submission.Values.Count < 4
                || !int.TryParse(submission.Values[0], out var fuel)
                || !int.TryParse(submission.Values[1], out var parts)
                || !int.TryParse(submission.Values[2], out var cargo)
                || !int.TryParse(submission.Values[3], out var contra))
            {
                error = "Goods mix Values must be fuel, parts, cargo, contraband counts.";
                return false;
            }

            if (!GoodsMixContexts.TryParseLoad(contextId, out var n))
            {
                error = "Unsupported Misbehave Goods mix context.";
                return false;
            }

            if (fuel + parts + cargo + contra != n)
            {
                error = $"Load {n} Goods requires Values totaling {n}.";
                return false;
            }

            choice.LoadGoodsFuel = fuel;
            choice.LoadGoodsParts = parts;
            choice.LoadGoodsCargo = cargo;
            choice.LoadGoodsContraband = contra;
            return true;
        }

        private static bool NeedsWarrantOrWantedChoice(
            IReadOnlyList<MisbehaveEffect>? effects,
            MisbehaveChoice choice,
            out int max,
            out string prompt,
            out bool warrantsOnly)
        {
            max = 0;
            prompt = "";
            warrantsOnly = false;
            if (effects == null || effects.Count == 0)
                return false;
            foreach (var effect in effects)
            {
                if (effect.Is(MisbehaveLocalEffectType.MayDiscardWarrantsOrWanted))
                {
                    if (!string.IsNullOrWhiteSpace(choice.DiscardWarrantsOrWantedPath))
                        return false;
                    max = effect.Count > 0 ? effect.Count : 2;
                    prompt =
                        $"You may discard up to {max} Warrants or up to {max} Wanted Tokens (exclusive), or none.";
                    warrantsOnly = false;
                    return true;
                }

                if (effect.Is(MisbehaveLocalEffectType.MayDiscardWarrants))
                {
                    if (!string.IsNullOrWhiteSpace(choice.DiscardWarrantsOrWantedPath))
                        return false;
                    max = effect.Count > 0 ? effect.Count : 1;
                    prompt = max == 1
                        ? "You may discard a Warrant, or none."
                        : $"You may discard up to {max} Warrants, or none.";
                    warrantsOnly = true;
                    return true;
                }
            }
            return false;
        }

        private bool TrySuspendWarrantOrWanted(
            GameState game,
            PlayerState player,
            MisbehaveChoice choice,
            int max,
            string prompt,
            bool warrantsOnly,
            out string? error)
        {
            var options = warrantsOnly
                ? new[]
                {
                    MisbehaveWarrantOrWantedOptions.None,
                    MisbehaveWarrantOrWantedOptions.Warrants
                }
                : new[]
                {
                    MisbehaveWarrantOrWantedOptions.None,
                    MisbehaveWarrantOrWantedOptions.Warrants,
                    MisbehaveWarrantOrWantedOptions.WantedTokens
                };
            var pending = new PendingChoice(
                player.Id,
                PendingChoiceKinds.MisbehaveWarrantOrWanted,
                contextId: max.ToString(),
                options: options,
                prompt: prompt);
            if (!game.TrySetPendingChoice(pending, out error))
                return false;
            _pendingWarrantOrWantedChoice = choice;
            return true;
        }

        private static bool TryMergeWarrantOrWantedSubmission(
            ChoiceSubmission submission,
            MisbehaveChoice choice,
            int max,
            out string? error)
        {
            error = null;
            var path = submission.SelectedOptionId;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Choose none, warrants, or wanted-tokens.";
                return false;
            }

            if (string.Equals(path, MisbehaveWarrantOrWantedOptions.None, StringComparison.Ordinal))
            {
                choice.DiscardWarrantsOrWantedPath = MisbehaveWarrantOrWantedOptions.None;
                choice.DiscardWarrants = 0;
                choice.ClearWantedCrewIds = null;
                return true;
            }

            if (string.Equals(path, MisbehaveWarrantOrWantedOptions.Warrants, StringComparison.Ordinal))
            {
                var amount = submission.Amount ?? 0;
                if (amount < 0 || amount > max)
                {
                    error = $"Warrant discard Amount must be between 0 and {max}.";
                    return false;
                }
                if (submission.Values != null && submission.Values.Count > 0)
                {
                    error = "Cannot mix Warrants and Wanted Tokens in one discard.";
                    return false;
                }
                choice.DiscardWarrantsOrWantedPath = MisbehaveWarrantOrWantedOptions.Warrants;
                choice.DiscardWarrants = amount;
                choice.ClearWantedCrewIds = null;
                return true;
            }

            if (string.Equals(path, MisbehaveWarrantOrWantedOptions.WantedTokens, StringComparison.Ordinal))
            {
                var ids = submission.Values ?? Array.Empty<string>();
                if (ids.Count > max)
                {
                    error = $"Wanted Token discard allows at most {max} crew.";
                    return false;
                }
                if (submission.Amount is int warrantAmount && warrantAmount > 0)
                {
                    error = "Cannot mix Warrants and Wanted Tokens in one discard.";
                    return false;
                }
                choice.DiscardWarrantsOrWantedPath = MisbehaveWarrantOrWantedOptions.WantedTokens;
                choice.DiscardWarrants = 0;
                choice.ClearWantedCrewIds = new List<string>(ids);
                return true;
            }

            error = $"Unknown Warrant/Wanted option '{path}'.";
            return false;
        }

        private static bool TryApplyMayDiscardWarrantsOrWanted(
            PlayerState player,
            int count,
            MisbehaveChoice choice,
            out string? error)
        {
            error = null;
            var max = count > 0 ? count : 2;
            var path = choice.DiscardWarrantsOrWantedPath;
            if (string.IsNullOrWhiteSpace(path)
                || string.Equals(path, MisbehaveWarrantOrWantedOptions.None, StringComparison.Ordinal))
            {
                if (choice.DiscardWarrants > 0
                    || (choice.ClearWantedCrewIds != null && choice.ClearWantedCrewIds.Count > 0))
                {
                    error = "Cannot mix Warrants and Wanted Tokens in one discard.";
                    return false;
                }
                return true;
            }

            if (string.Equals(path, MisbehaveWarrantOrWantedOptions.Warrants, StringComparison.Ordinal))
            {
                if (choice.ClearWantedCrewIds != null && choice.ClearWantedCrewIds.Count > 0)
                {
                    error = "Cannot mix Warrants and Wanted Tokens in one discard.";
                    return false;
                }
                var discard = choice.DiscardWarrants;
                if (discard < 0 || discard > max)
                {
                    error = $"Warrant discard must be between 0 and {max}.";
                    return false;
                }
                if (discard > player.Warrants)
                    discard = player.Warrants;
                player.Warrants -= discard;
                return true;
            }

            if (string.Equals(path, MisbehaveWarrantOrWantedOptions.WantedTokens, StringComparison.Ordinal))
            {
                if (choice.DiscardWarrants > 0)
                {
                    error = "Cannot mix Warrants and Wanted Tokens in one discard.";
                    return false;
                }
                var ids = choice.ClearWantedCrewIds;
                if (ids == null || ids.Count == 0)
                    return true;
                if (ids.Count > max)
                {
                    error = $"Wanted Token discard allows at most {max} crew.";
                    return false;
                }
                foreach (var id in ids)
                {
                    var member = player.Roster.Find(id);
                    if (member == null)
                    {
                        error = $"Crew '{id}' is not on the roster.";
                        return false;
                    }
                    if (!member.Wanted)
                    {
                        error = $"Crew '{id}' is not Wanted.";
                        return false;
                    }
                    if (!player.Roster.TryClearWanted(id))
                    {
                        error = $"Cannot clear Wanted on '{id}'.";
                        return false;
                    }
                }
                return true;
            }

            error = $"Unknown Warrant/Wanted path '{path}'.";
            return false;
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

        /// <summary>
        /// Director's Cut C&amp;P p.49 Equipment Seizures: matching carried Gear is removed from
        /// the game and may not be repurchased.
        /// </summary>
        private static int ApplySeizeGear(GameState game, PlayerState player, MisbehaveEffect effect)
        {
            if (game.Gear == null || effect.Tags.Count == 0)
                return 0;

            var toSeize = new List<(string GearId, string? CarrierId)>();
            foreach (var gearId in player.Gear)
            {
                if (!GearCarriage.IsCarried(player, gearId))
                    continue;
                if (!game.Gear.TryGet(gearId, out var gear))
                    continue;
                if (!GearMatchesAnyTag(gear, effect.Tags))
                    continue;
                toSeize.Add((gearId, GearCarriage.CarrierOf(player, gearId)));
            }

            var carrierIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, carrierId) in toSeize)
            {
                if (!string.IsNullOrWhiteSpace(carrierId))
                    carrierIds.Add(carrierId!);
            }

            foreach (var (gearId, _) in toSeize)
            {
                if (GearCarriage.TryDiscardGear(player, gearId, out _))
                    game.RemovedFromPlay.Add(gearId);
            }

            if (toSeize.Count > 0)
                GearCarriage.RefreshSkillBonuses(game, player);

            if (effect.DisgruntleCarriers)
            {
                foreach (var carrierId in carrierIds)
                {
                    var member = player.Roster.Find(carrierId);
                    if (member != null && !member.Disgruntled)
                        member.Disgruntled = true;
                }
            }

            if (effect.DisgruntleWantedIfAny && toSeize.Count > 0)
                player.Roster.DisgruntleWhere(m => m.Wanted);

            return toSeize.Count;
        }

        private static bool GearMatchesAnyTag(GearEntry gear, IReadOnlyList<string> tags)
        {
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                    continue;
                if (NamesMatch(gear.Id, tag) || NamesMatch(gear.Name, tag))
                    return true;
                foreach (var keyword in gear.Keywords)
                {
                    if (NamesMatch(keyword, tag))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Alliance Wanted Crew Roll (same capture table as Cruiser Contact / Background Checks).
        /// Seized crew are removed from play. Meadows redirect deferred (thin PendingChoice).
        /// </summary>
        private static bool TryApplyWantedCrewRoll(
            GameState game,
            PlayerState player,
            IRng rng,
            MisbehaveChoice choice,
            out bool anySeized,
            out string? error)
        {
            anySeized = false;
            error = null;
            _ = choice;
            var warrantsAtRoll = player.Warrants;
            var wanted = player.Roster.WantedMembers();
            for (var i = 0; i < wanted.Count; i++)
            {
                var member = wanted[i];
                if (JobWorkCrew.IsUnavailable(player, member))
                    continue;
                var die = Dice.D6(rng);
                if (!ActiveAlertRules.WantedCrewCaptured(game, die, warrantsAtRoll))
                    continue;

                if (member.IsLeader)
                {
                    // Leaders are REALLY Lucky: Disgruntle instead of remove (FAQ 4.1 p.3).
                    if (!member.Disgruntled)
                        member.Disgruntled = true;
                    anySeized = true;
                    continue;
                }

                player.Roster.Remove(member.Id);
                game.RemovedFromPlay.Add(member.Id);
                anySeized = true;
            }

            return true;
        }

        private static void ReturnWantedCrewToShip(GameState game, PlayerState player)
        {
            var pending = game.PendingMisbehave;
            if (pending == null)
                return;
            foreach (var member in player.Roster.WantedMembers())
            {
                if (JobWorkCrew.IsUnavailable(player, member))
                    continue;
                JobWorkCrew.ReturnToShip(player, pending.JobId, member.Id);
            }
        }

        private static void ReturnHighestFightToShip(GameState game, PlayerState player)
        {
            var pending = game.PendingMisbehave;
            if (pending == null)
                return;

            CrewMember? best = null;
            var bestFight = int.MinValue;
            foreach (var member in player.Roster.Members)
            {
                if (JobWorkCrew.IsUnavailable(player, member))
                    continue;
                var fight = member.Card.Fight;
                if (game.Gear != null)
                {
                    foreach (var gearId in GearCarriage.CarriedBy(player, member.Id))
                    {
                        if (game.Gear.TryGet(gearId, out var gear))
                            fight += gear.Fight;
                    }
                }
                if (best == null || fight > bestFight)
                {
                    best = member;
                    bestFight = fight;
                }
            }

            if (best == null)
                return;
            JobWorkCrew.ReturnToShip(player, pending.JobId, best.Id);
            if (!best.Disgruntled)
                best.Disgruntled = true;
        }

        private static void ApplyDisgruntle(PlayerState player, string text)
        {
            if (Contains(text, "Disgruntle all Crew with Tech"))
                player.Roster.DisgruntleWhere(m => m.Card.Tech > 0);

            if (Contains(text, "Disgruntle Moral") || Contains(text, "Disgruntle all Moral"))
                player.Roster.DisgruntleMoral();

            if (Contains(text, "Disgruntle all Mercs"))
                player.Roster.DisgruntleWhere(m => m.Card.HasProfession("Merc") || m.Card.HasProfession("Soldier"));

            // "Disgruntle all Crew" / "Disgruntled all Crew" — full roster (Gun Play, etc.).
            // Narrower "all Crew with Tech" / Moral / Mercs handled above; skip those here.
            if ((Contains(text, "Disgruntle all Crew") || Contains(text, "Disgruntled all Crew"))
                && !Contains(text, "all Crew with Tech")
                && !Contains(text, "all Moral")
                && !Contains(text, "all Mercs"))
                player.Roster.DisgruntleWhere(_ => true);
        }

        private static bool TryApplyMayDiscardWarrants(
            PlayerState player,
            int maxCount,
            MisbehaveChoice choice,
            out string? error)
        {
            error = null;
            var max = maxCount > 0 ? maxCount : 1;
            var path = choice.DiscardWarrantsOrWantedPath;
            if (string.IsNullOrWhiteSpace(path)
                || string.Equals(path, MisbehaveWarrantOrWantedOptions.None, StringComparison.Ordinal))
            {
                if (choice.DiscardWarrants > 0
                    || (choice.ClearWantedCrewIds != null && choice.ClearWantedCrewIds.Count > 0))
                {
                    error = "Cannot mix Warrants and Wanted Tokens in one discard.";
                    return false;
                }
                return true;
            }

            if (string.Equals(path, MisbehaveWarrantOrWantedOptions.Warrants, StringComparison.Ordinal))
            {
                if (choice.ClearWantedCrewIds != null && choice.ClearWantedCrewIds.Count > 0)
                {
                    error = "Cannot mix Warrants and Wanted Tokens in one discard.";
                    return false;
                }
                var discard = choice.DiscardWarrants;
                if (discard < 0 || discard > max)
                {
                    error = $"Warrant discard must be between 0 and {max}.";
                    return false;
                }
                if (discard > player.Warrants)
                    discard = player.Warrants;
                player.Warrants -= discard;
                return true;
            }

            error = $"Unknown Warrant discard path '{path}'.";
            return false;
        }

        private static void DiscardAllInactiveJobsInHand(GameState game, PlayerState player)
        {
            // FAQ 4.1: Jobs in hand are Inactive; Active Jobs on the table cannot be discarded
            // this way (only complete or Warrant Issued while working).
            if (player.JobHand.Count == 0)
                return;
            var ids = new List<string>(player.JobHand);
            player.JobHand.Clear();
            if (game.Jobs == null)
                return;
            foreach (var id in ids)
            {
                if (!game.Jobs.TryGet(id, out var job))
                    continue;
                if (game.ContactDecks != null && game.ContactDecks.TryGet(job.ContactName, out var deck))
                    deck.MoveToDiscard(job);
            }
        }

        private static void ApplyClearDisgruntled(PlayerState player, string text)
        {
            if (!Contains(text, "Remove Disgruntled"))
                return;
            // Printed "Moral Crew" → moral only (Thrillin' Heroics / Locals in Need).
            if (Contains(text, "Moral"))
            {
                player.Roster.ClearDisgruntledMoral();
                return;
            }
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

            if (ContactSolidBenefits.IsNiskaJob(job))
            {
                // Prefer KillChoice.VictimCrewIds before abandoning — PendingChoice mid-abandon
                // would leave the Job already discarded.
                if (CrewKill.NeedsVictimChoice(player, 1, killChoice))
                {
                    error =
                        "Niska Pound of Flesh requires KillChoice.VictimCrewIds (or choose victims before the Warrant resolves).";
                    return false;
                }
                if (!CrewKill.TryKillUpTo(
                        game, player, 1, rng ?? new SystemRng(), out var niskaKilled, out error, killChoice))
                    return false;
                killed += niskaKilled;
            }

            player.JobHand.Remove(job.Id);
            player.RemoveActive(job.Id);
            if (game.ContactDecks != null && game.ContactDecks.TryGet(job.ContactName, out var deck))
                deck.MoveToDiscard(job);

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
