using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Thin applicator args. Carries PendingChoice resume state (kill victims / Med Foam /
    /// Goods mix) without redesigning choice UX. Source distinguishes Nav vs Misbehave call sites.
    /// </summary>
    public sealed class CardEffectContext
    {
        public CardEffectSource Source { get; }
        /// <summary>Kill / Medic hooks from <see cref="NavResolveChoice"/> or <see cref="MisbehaveChoice"/>.</summary>
        public KillChoice? Kill { get; }
        /// <summary>
        /// When true (Nav), Load Cargo/Contraband/Parts/Goods must fit hold packing before apply.
        /// Misbehave prose historically increments Cargo/Contraband without a prefight Fits check;
        /// LoadParts / LoadGoods still validate via HoldSpace (fail) unless
        /// <see cref="SkipLoadIfNoSpace"/>.
        /// </summary>
        public bool EnforceHoldSpace { get; }
        /// <summary>
        /// Split Crew nested team: Kill N victims must come from this set (null = whole roster).
        /// Christopher lock PR #55.
        /// </summary>
        public IReadOnlyCollection<string>? OnlyCrewIds { get; }
        /// <summary>
        /// Nav Take-Parts prose historically skipped the load when the hold was full (no error).
        /// When true, LoadParts / LoadGoods that do not fit are no-ops instead of failures.
        /// Misbehave keeps false (fail via HoldSpace.TryExplain).
        /// </summary>
        public bool SkipLoadIfNoSpace { get; }
        /// <summary>Goods mix for <see cref="CardEffectType.LoadGoods"/> (Fuel).</summary>
        public int LoadGoodsFuel { get; }
        /// <summary>Goods mix for <see cref="CardEffectType.LoadGoods"/> (Parts).</summary>
        public int LoadGoodsParts { get; }
        /// <summary>Goods mix for <see cref="CardEffectType.LoadGoods"/> (Cargo).</summary>
        public int LoadGoodsCargo { get; }
        /// <summary>Goods mix for <see cref="CardEffectType.LoadGoods"/> (Contraband).</summary>
        public int LoadGoodsContraband { get; }
        /// <summary>
        /// Chosen amount for <see cref="CardEffectType.LoadUpTo"/> (0..max).
        /// Null = load the maximum that still fits (capped at the effect Count).
        /// </summary>
        public int? LoadAmount { get; }

        public CardEffectContext(
            CardEffectSource source,
            KillChoice? kill = null,
            bool enforceHoldSpace = false,
            IReadOnlyCollection<string>? onlyCrewIds = null,
            bool skipLoadIfNoSpace = false,
            int loadGoodsFuel = 0,
            int loadGoodsParts = 0,
            int loadGoodsCargo = 0,
            int loadGoodsContraband = 0,
            int? loadAmount = null)
        {
            Source = source;
            Kill = kill;
            EnforceHoldSpace = enforceHoldSpace;
            OnlyCrewIds = onlyCrewIds;
            SkipLoadIfNoSpace = skipLoadIfNoSpace;
            LoadGoodsFuel = loadGoodsFuel;
            LoadGoodsParts = loadGoodsParts;
            LoadGoodsCargo = loadGoodsCargo;
            LoadGoodsContraband = loadGoodsContraband;
            LoadAmount = loadAmount;
        }
    }

    /// <summary>
    /// Shared Nav + Misbehave effect applicator for <see cref="CardEffectType"/>.
    /// Director's Cut p.14 / GF9: Skill Tests list results under the target; band text applies
    /// Kill / Warrant / Load / Take $ / Disgruntle / clear Disgruntled.
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

            if (context.EnforceHoldSpace && !CanLoad(player, effects, context, out error))
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
                                game, player, killCount, rng, out var killedNow, out error, context.Kill,
                                context.OnlyCrewIds))
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

                    case CardEffectType.LoadParts:
                        if (!TryApplyLoadParts(player, effect.Count, context, result, out error))
                            return false;
                        break;

                    case CardEffectType.LoadGoods:
                        if (!TryApplyLoadGoods(player, effect.Count, context, result, out error))
                            return false;
                        break;

                    case CardEffectType.LoadUpTo:
                        if (!TryApplyLoadUpTo(player, effect, context, result, out error))
                            return false;
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

                    case CardEffectType.ClearDisgruntledMoral:
                        result.DisgruntledCleared += player.Roster.ClearDisgruntledMoral();
                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// Prefight hold packing for Load Cargo / Contraband / Parts / Goods when
        /// <see cref="CardEffectContext.EnforceHoldSpace"/> (skipped when
        /// <see cref="CardEffectContext.SkipLoadIfNoSpace"/>).
        /// </summary>
        public static bool CanApply(
            PlayerState player,
            IReadOnlyList<CardEffect> effects,
            CardEffectContext context,
            out string? error)
        {
            error = null;
            if (!context.EnforceHoldSpace || context.SkipLoadIfNoSpace
                || effects == null || effects.Count == 0)
                return true;
            return CanLoad(player, effects, context, out error);
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

        public static int PlannedGoodsLoadCount(IReadOnlyList<CardEffect>? effects)
        {
            if (effects == null)
                return 0;
            foreach (var effect in effects)
            {
                if (effect.Type == CardEffectType.LoadGoods)
                    return effect.Count > 0 ? effect.Count : 1;
            }
            return 0;
        }

        private static bool TryApplyLoadParts(
            PlayerState player,
            int count,
            CardEffectContext context,
            CardEffectApplyResult result,
            out string? error)
        {
            error = null;
            var parts = count > 0 ? count : 1;
            if (!HoldSpace.Fits(player, addParts: parts))
            {
                if (context.SkipLoadIfNoSpace)
                    return true;
                HoldSpace.TryExplain(player, out error, addParts: parts);
                return false;
            }
            player.Parts += parts;
            result.PartsLoaded += parts;
            return true;
        }

        private static bool TryApplyLoadGoods(
            PlayerState player,
            int count,
            CardEffectContext context,
            CardEffectApplyResult result,
            out string? error)
        {
            error = null;
            var n = count > 0 ? count : 1;
            var fuel = context.LoadGoodsFuel;
            var parts = context.LoadGoodsParts;
            var cargo = context.LoadGoodsCargo;
            var contra = context.LoadGoodsContraband;
            var sum = fuel + parts + cargo + contra;
            if (sum != n)
            {
                error = $"Load {n} Goods requires a Goods composition choice totaling {n}.";
                return false;
            }
            if (!HoldSpace.Fits(
                    player,
                    addFuel: fuel,
                    addParts: parts,
                    addCargo: cargo,
                    addContraband: contra))
            {
                if (context.SkipLoadIfNoSpace)
                    return true;
                HoldSpace.TryExplain(
                    player,
                    out error,
                    addFuel: fuel,
                    addParts: parts,
                    addCargo: cargo,
                    addContraband: contra);
                return false;
            }
            player.Fuel += fuel;
            player.Parts += parts;
            player.Cargo += cargo;
            player.Contraband += contra;
            result.FuelLoaded += fuel;
            result.PartsLoaded += parts;
            result.CargoLoaded += cargo;
            result.ContrabandLoaded += contra;
            return true;
        }

        /// <summary>
        /// Printed "Load up to N {Cargo|Contraband|Parts|Fuel}".
        /// Misbehave Everything That's Not Nailed Down / Nav Hollowed Out Space-Liner.
        /// </summary>
        private static bool TryApplyLoadUpTo(
            PlayerState player,
            CardEffect effect,
            CardEffectContext context,
            CardEffectApplyResult result,
            out string? error)
        {
            error = null;
            if (effect.Kind == null)
            {
                error = "LoadUpTo requires a Kind (Cargo / Contraband / Parts / Fuel).";
                return false;
            }
            var kind = effect.Kind.Value;
            var cap = effect.Count > 0 ? effect.Count : 1;
            int count;
            if (context.LoadAmount != null)
            {
                count = context.LoadAmount.Value;
                if (count < 0 || count > cap)
                {
                    error = $"Load up to {cap} {kind} requires LoadAmount between 0 and {cap}.";
                    return false;
                }
            }
            else
            {
                // Unset → max that fits (preserves prior exact-N / Hollowed Out defaults).
                count = cap;
                while (count > 0 && !TypedLoadFits(player, kind, count))
                    count--;
            }
            if (count == 0)
                return true;
            if (!TypedLoadFits(player, kind, count))
            {
                TypedLoadExplain(player, kind, count, out error);
                return false;
            }
            ApplyTypedLoad(player, kind, count, result);
            return true;
        }

        private static bool TypedLoadFits(PlayerState player, CardLoadKind kind, int count)
        {
            if (count <= 0)
                return true;
            return kind switch
            {
                CardLoadKind.Fuel => HoldSpace.Fits(player, addFuel: count),
                CardLoadKind.Parts => HoldSpace.Fits(player, addParts: count),
                CardLoadKind.Cargo => HoldSpace.Fits(player, addCargo: count),
                _ => HoldSpace.Fits(player, addContraband: count)
            };
        }

        private static void TypedLoadExplain(
            PlayerState player, CardLoadKind kind, int count, out string? error)
        {
            switch (kind)
            {
                case CardLoadKind.Fuel:
                    HoldSpace.TryExplain(player, out error, addFuel: count);
                    break;
                case CardLoadKind.Parts:
                    HoldSpace.TryExplain(player, out error, addParts: count);
                    break;
                case CardLoadKind.Cargo:
                    HoldSpace.TryExplain(player, out error, addCargo: count);
                    break;
                default:
                    HoldSpace.TryExplain(player, out error, addContraband: count);
                    break;
            }
        }

        private static void ApplyTypedLoad(
            PlayerState player, CardLoadKind kind, int count, CardEffectApplyResult result)
        {
            switch (kind)
            {
                case CardLoadKind.Fuel:
                    player.Fuel += count;
                    result.FuelLoaded += count;
                    break;
                case CardLoadKind.Parts:
                    player.Parts += count;
                    result.PartsLoaded += count;
                    break;
                case CardLoadKind.Cargo:
                    player.Cargo += count;
                    result.CargoLoaded += count;
                    break;
                default:
                    player.Contraband += count;
                    result.ContrabandLoaded += count;
                    break;
            }
        }

        private static bool CanLoad(
            PlayerState player,
            IReadOnlyList<CardEffect> effects,
            CardEffectContext context,
            out string? error)
        {
            error = null;
            var addCargo = 0;
            var addContra = 0;
            var addParts = 0;
            var addFuel = 0;
            foreach (var effect in effects)
            {
                if (effect.Type == CardEffectType.LoadCargo)
                    addCargo += effect.Count > 0 ? effect.Count : 1;
                else if (effect.Type == CardEffectType.LoadContraband)
                    addContra += effect.Count > 0 ? effect.Count : 1;
                else if (effect.Type == CardEffectType.LoadParts)
                    addParts += effect.Count > 0 ? effect.Count : 1;
                else if (effect.Type == CardEffectType.LoadGoods)
                {
                    addFuel += context.LoadGoodsFuel;
                    addParts += context.LoadGoodsParts;
                    addCargo += context.LoadGoodsCargo;
                    addContra += context.LoadGoodsContraband;
                }
                else if (effect.Type == CardEffectType.LoadUpTo)
                {
                    // Prefight only when an explicit LoadAmount is chosen; null auto-clamps.
                    if (context.LoadAmount == null || context.LoadAmount.Value <= 0
                        || effect.Kind == null)
                        continue;
                    var n = context.LoadAmount.Value;
                    switch (effect.Kind.Value)
                    {
                        case CardLoadKind.Fuel:
                            addFuel += n;
                            break;
                        case CardLoadKind.Parts:
                            addParts += n;
                            break;
                        case CardLoadKind.Cargo:
                            addCargo += n;
                            break;
                        default:
                            addContra += n;
                            break;
                    }
                }
            }
            if (addCargo == 0 && addContra == 0 && addParts == 0 && addFuel == 0)
                return true;
            if (HoldSpace.Fits(
                    player,
                    addFuel: addFuel,
                    addParts: addParts,
                    addCargo: addCargo,
                    addContraband: addContra))
                return true;
            error = "Not enough cargo/stash space for Load.";
            return false;
        }
    }
}
