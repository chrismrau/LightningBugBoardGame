using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Abilities
{
    /// <summary>
    /// Typed ability dispatcher. Queries crew/gear/leader <see cref="AbilityDefinition"/> lists —
    /// never English description text. Optional <c>may</c> types suspend via PendingChoice.
    /// </summary>
    public static class AbilityDispatcher
    {
        public static IEnumerable<AbilityDefinition> AllFromCrew(CrewCard card)
        {
            if (card?.Abilities == null)
                yield break;
            foreach (var ability in card.Abilities)
                yield return ability;
        }

        public static IEnumerable<AbilityDefinition> AllFromGear(GearEntry gear)
        {
            if (gear?.Abilities == null)
                yield break;
            foreach (var ability in gear.Abilities)
                yield return ability;
        }

        /// <param name="allowOptional">
        /// When false (default), only mandatory abilities apply (passive / auto hooks).
        /// When true, include printed <c>may</c> abilities for PendingChoice trigger checks.
        /// </param>
        public static bool Applies(
            AbilityDefinition ability,
            AbilityContext? context,
            bool allowOptional = false)
        {
            if (ability == null)
                return false;
            if (!ability.Mandatory && !allowOptional)
                return false;
            context ??= AbilityContext.None;
            // GF9 / Director's Cut: Job abilities do not apply while Working Goals.
            if (ability.JobOnly && context.IsWorkingGoal)
                return false;
            return true;
        }

        /// <summary>True when the roster has a matching typed ability (mandatory or optional).</summary>
        public static bool HasAbility(
            PlayerState player,
            string type,
            AbilityContext? context = null,
            Func<AbilityDefinition, bool>? predicate = null,
            bool allowOptional = true)
        {
            foreach (var member in player.Roster.Members)
            {
                foreach (var ability in AllFromCrew(member.Card))
                {
                    if (!ability.MatchesType(type) || !Applies(ability, context, allowOptional))
                        continue;
                    if (predicate != null && !predicate(ability))
                        continue;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Kaylee / Zoe / Inara: may re-roll tests of the printed skill.</summary>
        public static bool HasSkillReroll(
            PlayerState player,
            Skill skill,
            AbilityContext? context = null) =>
            HasAbility(player, AbilityTypes.SkillReroll, context, a => SkillMatches(a.Skill, skill));

        /// <summary>
        /// FAQ 4.1 p.8 may: after a matching skill roll, always suspend take/decline re-roll
        /// (even when only one option looks sensible).
        /// </summary>
        public static bool NeedsSkillRerollChoice(
            PlayerState player,
            Skill skill,
            SkillCheckChoice? choice,
            AbilityContext? context = null)
        {
            if (choice?.AcceptReroll != null)
                return false;
            return HasSkillReroll(player, skill, context);
        }

        /// <summary>
        /// Cortland: may pay Bribes before any Negotiate (Talk) Test — not Showdowns.
        /// </summary>
        public static bool HasBribesOnAnyNegotiate(
            PlayerState player,
            AbilityContext? context = null) =>
            HasAbility(player, AbilityTypes.BribesOnAnyNegotiate, context);

        /// <summary>Barkeep: Shore Leave at Supply Planets costs $0.</summary>
        public static bool HasFreeShoreLeaveAtSupply(
            PlayerState player,
            AbilityContext? context = null) =>
            HasAbility(
                player,
                AbilityTypes.FreeShoreLeaveAtSupply,
                context,
                allowOptional: false);

        public static int SumAmount(
            PlayerState player,
            string type,
            AbilityContext? context = null,
            Func<AbilityDefinition, bool>? predicate = null)
        {
            var total = 0;
            foreach (var member in player.Roster.Members)
            {
                foreach (var ability in AllFromCrew(member.Card))
                {
                    if (!ability.MatchesType(type) || !Applies(ability, context))
                        continue;
                    if (predicate != null && !predicate(ability))
                        continue;
                    total += ability.Amount;
                }
            }
            return total;
        }

        /// <summary>FAQ 4.1 / card text: +N to Medic Checks from typed abilities.</summary>
        public static int MedicCheckBonus(PlayerState player, AbilityContext? context = null) =>
            SumAmount(player, AbilityTypes.MedicCheckBonus, context);

        /// <summary>Simon → River Gifted rolls. Subject must match the Gifted crew name.</summary>
        public static int GiftedRollBonus(PlayerState player, string giftedCrewName, AbilityContext? context = null) =>
            SumAmount(player, AbilityTypes.GiftedRollBonus, context, a =>
                string.IsNullOrWhiteSpace(a.Subject)
                || string.Equals(a.Subject, giftedCrewName, StringComparison.OrdinalIgnoreCase));

        /// <summary>Wash-style Full Burn range addend from roster abilities.</summary>
        public static int FullBurnRangeBonus(PlayerState player, AbilityContext? context = null) =>
            SumAmount(player, AbilityTypes.FullBurnRangeBonus, context);

        /// <summary>
        /// Big Damn Heroes Proceed cash. Job-only (does not apply while Working Goals).
        /// </summary>
        public static int MisbehaveProceedCash(PlayerState player, AbilityContext? context = null)
        {
            context ??= AbilityContext.WorkingJob;
            var total = 0;
            foreach (var member in player.Roster.Members)
            {
                foreach (var ability in AllFromCrew(member.Card))
                {
                    if (!ability.MatchesType(AbilityTypes.MisbehaveProceedCash))
                        continue;
                    // Treat Proceed cash as Job-scoped even if jobOnly omitted on promo JSON.
                    var effective = ability.JobOnly
                        ? ability
                        : new AbilityDefinition(
                            ability.Type, ability.Mandatory, ability.Amount,
                            ability.Skill, ability.Subject, jobOnly: true);
                    if (!Applies(effective, context))
                        continue;
                    total += ability.Amount;
                }
            }
            return total;
        }

        /// <summary>Crew/Leader with redirectLeaderDisgruntle, if present on the ship.</summary>
        public static CrewMember? FindLeaderDisgruntleRedirect(CrewRoster roster)
        {
            foreach (var member in roster.Members)
            {
                if (member.IsLeader)
                    continue;
                foreach (var ability in AllFromCrew(member.Card))
                {
                    if (ability.MatchesType(AbilityTypes.RedirectLeaderDisgruntle) && ability.Mandatory)
                        return member;
                }
            }
            return null;
        }

        public static int GearCarryLimit(CrewMember member)
        {
            var limit = 1;
            foreach (var ability in AllFromCrew(member.Card))
            {
                if (!ability.MatchesType(AbilityTypes.GearCarryLimit) || !ability.Mandatory)
                    continue;
                if (ability.Amount > limit)
                    limit = ability.Amount;
            }
            return limit;
        }

        public static bool GearExemptFromLimit(GearEntry gear)
        {
            foreach (var ability in AllFromGear(gear))
            {
                if (ability.MatchesType(AbilityTypes.ExemptFromGearLimit) && ability.Mandatory)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Passive skill addends from carried gear skills + typed skillAddend abilities on usable gear/crew.
        /// Onboard (unassigned) gear never contributes (FAQ 4.1 p.2).
        /// </summary>
        public static int CarriedSkillAddend(
            GameState game,
            PlayerState player,
            Skill skill,
            AbilityContext? context = null)
        {
            var total = 0;
            foreach (var member in player.Roster.Members)
            {
                foreach (var ability in AllFromCrew(member.Card))
                {
                    if (!ability.MatchesType(AbilityTypes.SkillAddend) || !Applies(ability, context))
                        continue;
                    if (!SkillMatches(ability.Skill, skill))
                        continue;
                    total += ability.Amount;
                }
            }

            if (game.Gear == null)
                return total;

            foreach (var gearId in player.Gear)
            {
                if (!GearCarriage.IsCarried(player, gearId))
                    continue;
                if (!game.Gear.TryGet(gearId, out var gear))
                    continue;

                total += skill switch
                {
                    Skill.Fight => gear.Fight,
                    Skill.Tech => gear.Tech,
                    _ => gear.Talk
                };

                foreach (var ability in AllFromGear(gear))
                {
                    if (!ability.MatchesType(AbilityTypes.SkillAddend) || !Applies(ability, context))
                        continue;
                    if (!SkillMatches(ability.Skill, skill))
                        continue;
                    total += ability.Amount;
                }
            }
            return total;
        }

        private static bool SkillMatches(string? label, Skill skill)
        {
            if (string.IsNullOrWhiteSpace(label))
                return false;
            if (label.Equals("Negotiate", StringComparison.OrdinalIgnoreCase))
                return skill == Skill.Talk;
            return Enum.TryParse(label, true, out Skill parsed) && parsed == skill;
        }
    }
}
