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

        /// <summary>
        /// Nandi: Hire Crew at no cost (permission — always-on Buy cost; not mid-resolve may).
        /// </summary>
        public static bool HasFreeHireCrew(
            PlayerState player,
            AbilityContext? context = null) =>
            HasAbility(player, AbilityTypes.FreeHireCrew, context, allowOptional: true);

        /// <summary>Board Game Collection: Shore Leave allowed in any Sector via Buy.</summary>
        public static bool HasShoreLeaveAnySector(
            GameState game,
            PlayerState player,
            AbilityContext? context = null)
        {
            context ??= AbilityContext.None;
            var catalog = game.ShipUpgradeCatalog;
            if (catalog == null)
                return false;
            foreach (var upgradeId in player.ShipUpgrades)
            {
                if (!catalog.TryGet(upgradeId, out var upgrade))
                    continue;
                foreach (var ability in AllFromShipUpgrade(upgrade))
                {
                    if (ability.MatchesType(AbilityTypes.ShoreLeaveAnySector)
                        && Applies(ability, context, allowOptional: true))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Emma / Helen / Lucy Morale Booster on the roster.</summary>
        public static bool HasMoraleBooster(
            PlayerState player,
            AbilityContext? context = null) =>
            HasAbility(player, AbilityTypes.MoraleBooster, context);

        /// <summary>Love Bot (or similar) clear-Disgruntled action from carried gear.</summary>
        public static bool HasClearDisgruntledAction(
            GameState game,
            PlayerState player,
            AbilityContext? context = null) =>
            FindCarriedGearAbility(game, player, AbilityTypes.ClearDisgruntledAction, context) != null;

        /// <summary>True when a Morale Booster or Love Bot action is available.</summary>
        public static bool CanClearDisgruntledAction(
            GameState game,
            PlayerState player,
            AbilityContext? context = null) =>
            HasMoraleBooster(player, context) || HasClearDisgruntledAction(game, player, context);

        /// <summary>
        /// Crew ids legal for Morale Booster / Love Bot: Disgruntled, and not the Morale Booster
        /// source crew (printed “other than Emma/Helen/Lucy”). Love Bot may clear any.
        /// </summary>
        public static IReadOnlyList<string> LegalMoraleBoosterTargets(
            GameState game,
            PlayerState player,
            AbilityContext? context = null)
        {
            var moraleExcluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in player.Roster.Members)
            {
                foreach (var ability in AllFromCrew(member.Card))
                {
                    if (!ability.MatchesType(AbilityTypes.MoraleBooster)
                        || !Applies(ability, context, allowOptional: true))
                        continue;
                    moraleExcluded.Add(member.Id);
                    if (string.IsNullOrWhiteSpace(ability.Subject))
                        continue;
                    foreach (var other in player.Roster.Members)
                    {
                        if (string.Equals(other.Name, ability.Subject, StringComparison.OrdinalIgnoreCase))
                            moraleExcluded.Add(other.Id);
                    }
                }
            }

            var loveBot = HasClearDisgruntledAction(game, player, context);
            var list = new List<string>();
            foreach (var member in player.Roster.Members)
            {
                if (!member.Disgruntled)
                    continue;
                if (loveBot || !moraleExcluded.Contains(member.Id))
                    list.Add(member.Id);
            }
            return list;
        }

        /// <summary>
        /// Carried discard-to-reroll gear for the printed skill (Fight for Extra Ammo / Yolonda's).
        /// FAQ 4.1 p.2: Onboard unused. Returns first matching gear id or null.
        /// </summary>
        public static string? FindDiscardToRerollGear(
            GameState game,
            PlayerState player,
            Skill skill,
            AbilityContext? context = null)
        {
            if (game.Gear == null)
                return null;
            foreach (var gearId in player.Gear)
            {
                if (!GearCarriage.IsCarried(player, gearId))
                    continue;
                if (!game.Gear.TryGet(gearId, out var gear))
                    continue;
                foreach (var ability in AllFromGear(gear))
                {
                    if (!ability.MatchesType(AbilityTypes.DiscardToReroll)
                        || !Applies(ability, context, allowOptional: true))
                        continue;
                    if (!SkillMatches(ability.Skill, skill))
                        continue;
                    return gearId;
                }
            }
            return null;
        }

        /// <summary>
        /// FAQ 4.1 p.8 may: after a matching Fight roll, always suspend discard/decline when
        /// carried discard-to-reroll gear is present and undecided.
        /// </summary>
        public static bool NeedsDiscardToRerollChoice(
            GameState game,
            PlayerState player,
            Skill skill,
            SkillCheckChoice? choice,
            AbilityContext? context = null)
        {
            if (choice?.AcceptDiscardReroll != null)
                return false;
            return FindDiscardToRerollGear(game, player, skill, context) != null;
        }

        public static bool HasShowdownReroll(
            PlayerState player,
            AbilityContext? context = null) =>
            HasAbility(player, AbilityTypes.ShowdownReroll, context);

        public static bool HasShowdownForceRivalReroll(
            PlayerState player,
            AbilityContext? context = null) =>
            HasAbility(player, AbilityTypes.ShowdownForceRivalReroll, context);

        public static IEnumerable<AbilityDefinition> AllFromShipUpgrade(ShipUpgradeEntry upgrade)
        {
            if (upgrade?.Abilities == null)
                yield break;
            foreach (var ability in upgrade.Abilities)
                yield return ability;
        }

        private static AbilityDefinition? FindCarriedGearAbility(
            GameState game,
            PlayerState player,
            string type,
            AbilityContext? context)
        {
            if (game.Gear == null)
                return null;
            foreach (var gearId in player.Gear)
            {
                if (!GearCarriage.IsCarried(player, gearId))
                    continue;
                if (!game.Gear.TryGet(gearId, out var gear))
                    continue;
                foreach (var ability in AllFromGear(gear))
                {
                    if (ability.MatchesType(type) && Applies(ability, context, allowOptional: true))
                        return ability;
                }
            }
            return null;
        }

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
