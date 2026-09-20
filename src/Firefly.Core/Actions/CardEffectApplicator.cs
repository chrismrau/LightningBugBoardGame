using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Thin applicator args. Carries PendingChoice resume state (kill victims / Med Foam)
    /// without redesigning choice UX. Source distinguishes Nav vs Misbehave call sites.
    /// </summary>
    public sealed class CardEffectContext
    {
        public CardEffectSource Source { get; }
        /// <summary>Kill / Medic hooks from <see cref="NavResolveChoice"/> or <see cref="MisbehaveChoice"/>.</summary>
        public KillChoice? Kill { get; }
        /// <summary>
        /// When true (Nav), Load Cargo/Contraband must fit hold packing before apply.
        /// Misbehave prose historically increments without a prefight Fits check.
        /// </summary>
        public bool EnforceHoldSpace { get; }

        public CardEffectContext(
            CardEffectSource source,
            KillChoice? kill = null,
            bool enforceHoldSpace = false)
        {
            Source = source;
            Kill = kill;
            EnforceHoldSpace = enforceHoldSpace;
        }
    }

    /// <summary>
    /// Shared Nav + Misbehave effect applicator for the intersection vocabulary in
    /// <see cref="CardEffectType"/>. Director's Cut p.14 / GF9: Skill Tests list results
    /// under the target; band text applies Kill / Warrant / Load / Take $ / Disgruntle.
    /// </summary>
    public static class CardEffectApplicator
    {
        public static bool TryApply(
            GameState game,
            PlayerState player,
            IReadOnlyList<CardEffect> effects,
            IRng rng,
            CardEffectContext context,
            out CardEffectApplyResult result,
            out string? error)
        {
            result = new CardEffectApplyResult();
            error = null;
            if (effects == null || effects.Count == 0)
                return true;

            if (context.EnforceHoldSpace && !CanLoad(player, effects, out error))
                return false;

            foreach (var effect in effects)
            {
                switch (effect.Type)
                {
                    case CardEffectType.WarrantIssued:
                        player.Warrants++;
                        result.WarrantsIssued++;
                        break;

                    case CardEffectType.KillCrew:
                        var killCount = effect.Count > 0 ? effect.Count : 1;
                        if (!CrewKill.TryKillUpTo(
                                game, player, killCount, rng, out var killedNow, out error, context.Kill))
                            return false;
                        result.CrewKilled += killedNow;
                        break;

                    case CardEffectType.LoadCargo:
                        var cargo = effect.Count > 0 ? effect.Count : 1;
                        if (context.EnforceHoldSpace && !HoldSpace.Fits(player, addCargo: cargo))
                        {
                            error = "Not enough cargo/stash space for Cargo.";
                            return false;
                        }
                        player.Cargo += cargo;
                        result.CargoLoaded += cargo;
                        break;

                    case CardEffectType.LoadContraband:
                        var contra = effect.Count > 0 ? effect.Count : 1;
                        if (context.EnforceHoldSpace && !HoldSpace.Fits(player, addContraband: contra))
                        {
                            error = "Not enough cargo/stash space for Contraband.";
                            return false;
                        }
                        player.Contraband += contra;
                        result.ContrabandLoaded += contra;
                        break;

                    case CardEffectType.TakeCash:
                        var cash = effect.Count;
                        player.Cash += cash;
                        result.CashGained += cash;
                        break;

                    case CardEffectType.DisgruntleMoral:
                        result.MoralDisgruntled += player.Roster.DisgruntleMoral();
                        break;

                    case CardEffectType.ClearDisgruntled:
                        result.DisgruntledCleared += player.Roster.ClearDisgruntled();
                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// Prefight hold packing for Load Cargo / Contraband when <see cref="CardEffectContext.EnforceHoldSpace"/>.
        /// </summary>
        public static bool CanApply(
            PlayerState player,
            IReadOnlyList<CardEffect> effects,
            CardEffectContext context,
            out string? error)
        {
            error = null;
            if (!context.EnforceHoldSpace || effects == null || effects.Count == 0)
                return true;
            return CanLoad(player, effects, out error);
        }

        public static int PlannedKillCount(IReadOnlyList<CardEffect>? effects)
        {
            if (effects == null)
                return 0;
            foreach (var effect in effects)
            {
                if (effect.Type == CardEffectType.KillCrew)
                    return effect.Count > 0 ? effect.Count : 1;
            }
            return 0;
        }

        private static bool CanLoad(
            PlayerState player,
            IReadOnlyList<CardEffect> effects,
            out string? error)
        {
            error = null;
            var addCargo = 0;
            var addContra = 0;
            foreach (var effect in effects)
            {
                if (effect.Type == CardEffectType.LoadCargo)
                    addCargo += effect.Count > 0 ? effect.Count : 1;
                else if (effect.Type == CardEffectType.LoadContraband)
                    addContra += effect.Count > 0 ? effect.Count : 1;
            }
            if (addCargo == 0 && addContra == 0)
                return true;
            if (HoldSpace.Fits(player, addCargo: addCargo, addContraband: addContra))
                return true;
            error = "Not enough cargo/stash space for Load.";
            return false;
        }
    }
}
