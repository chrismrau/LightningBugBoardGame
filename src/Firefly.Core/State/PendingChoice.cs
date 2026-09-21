using System;
using System.Collections.Generic;

namespace Firefly.Core.State
{
    /// <summary>
    /// Shared player-decision wait state on <see cref="GameState"/>.
    /// At most one pending at a time (v1). Actions may suspend mid-resolve by
    /// setting this; play resumes via <see cref="GameState.TrySubmitChoice"/>.
    /// Consumers migrate later — auto-resolve paths stay until each site is wired.
    /// </summary>
    public sealed class PendingChoice
    {
        public string PlayerId { get; }
        /// <summary>
        /// Stable kind id for the waiting decision (see <see cref="PendingChoiceKinds"/>).
        /// </summary>
        public string Kind { get; }
        /// <summary>Optional opaque resume token (card id, job id, sector, etc.).</summary>
        public string? ContextId { get; }
        /// <summary>Legal option ids when the prompt is discrete; null means unconstrained.</summary>
        public IReadOnlyList<string>? Options { get; }
        /// <summary>Optional prompt for tests / future UI.</summary>
        public string? Prompt { get; }

        public PendingChoice(
            string playerId,
            string kind,
            string? contextId = null,
            IReadOnlyList<string>? options = null,
            string? prompt = null)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("Player id is required.", nameof(playerId));
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("Kind is required.", nameof(kind));
            PlayerId = playerId;
            Kind = kind;
            ContextId = contextId;
            Options = options;
            Prompt = prompt;
        }
    }

    /// <summary>
    /// Answer payload for <see cref="GameState.TrySubmitChoice"/>.
    /// Fields are optional; each consumer kind reads what it needs.
    /// </summary>
    public sealed class ChoiceSubmission
    {
        /// <summary>Selected discrete option id when <see cref="PendingChoice.Options"/> is set.</summary>
        public string? SelectedOptionId { get; set; }
        /// <summary>Free-form single value (sector id, crew id, etc.).</summary>
        public string? Value { get; set; }
        /// <summary>Multi-value payload (victim crew ids, goods mix, etc.).</summary>
        public IList<string>? Values { get; set; }
        /// <summary>Numeric amount (bribe dollars, pay cost, fuel count, etc.).</summary>
        public int? Amount { get; set; }
        /// <summary>Binary accept/decline (pay vs Full Stop, Medic Foam, etc.).</summary>
        public bool? Accepted { get; set; }
    }

    /// <summary>
    /// Reserved kind ids. Wired: <see cref="NavPayOrDecline"/>, <see cref="KillVictim"/>,
    /// <see cref="BribeAmount"/>, <see cref="MedFoamDiscard"/>, <see cref="MisbehaveOption"/>,
    /// <see cref="HavenSector"/>, <see cref="RivalPlayer"/>, <see cref="SectorDestination"/>,
    /// <see cref="SkillReroll"/>, <see cref="DiscardToReroll"/>, <see cref="MoraleBoosterTarget"/>,
    /// <see cref="ShowdownReroll"/>, <see cref="HavenFuelAmount"/>, <see cref="MedicReroll"/>,
    /// <see cref="DiscardOrLoseSolid"/>, <see cref="MisbehaveDiscardRedraw"/>.
    /// </summary>
    public static class PendingChoiceKinds
    {
        public const string NavPayOrDecline = "nav-pay-or-decline";
        public const string KillVictim = "kill-victim";
        /// <summary>Marked Negotiate Bribes: choose $0 / $100 / … before the roll (GF9 p.6).</summary>
        public const string BribeAmount = "bribe-amount";
        /// <summary>Optional Med Foam discard to succeed a Medic Check (Supplies.tsv / Gear).</summary>
        public const string MedFoamDiscard = "med-foam-discard";
        /// <summary>
        /// Misbehave card option and/or FIRST–NEXT step (GF9 p.14; C&amp;P 2-step cards).
        /// Option ids are <c>0</c>/<c>1</c>/… or <c>step:N</c>.
        /// </summary>
        public const string MisbehaveOption = "misbehave-option";
        /// <summary>
        /// Story / Blue Sun Choosing Havens: pick an eligible Haven sector (Blue Sun p.14;
        /// Any Port setup). <see cref="ChoiceSubmission.Value"/> = sector id.
        /// </summary>
        public const string HavenSector = "haven-sector";
        /// <summary>
        /// Any Rival piracy: choose a same-sector rival ship (PBH pp.3–5).
        /// <see cref="ChoiceSubmission.Value"/> or <see cref="ChoiceSubmission.SelectedOptionId"/> = rival player id.
        /// </summary>
        public const string RivalPlayer = "rival-player";
        /// <summary>
        /// Cruiser / Reaver / Corvette / ship-nudge / Safe Harbor destination.
        /// PTR or drawer per card text. <see cref="ChoiceSubmission.Value"/> = sector id;
        /// ship nudge uses <see cref="ChoiceSubmission.Values"/> = via, destination.
        /// ContextId discriminates the resume site (see <see cref="SectorDestinationContexts"/>).
        /// </summary>
        public const string SectorDestination = "sector-destination";
        /// <summary>
        /// Crew/gear <c>skillReroll</c> may: keep the first roll or re-roll once
        /// (Kaylee Tech / Zoe Fight / Inara Negotiate). FAQ 4.1 p.8 — always suspend.
        /// Options: <see cref="SkillRerollOptions"/>.
        /// </summary>
        public const string SkillReroll = "skill-reroll";
        /// <summary>
        /// Discard carried gear to re-roll a Fight test (Extra Ammo Clips / Yolonda's Pistol).
        /// Options: <see cref="DiscardToRerollOptions"/>. ContextId = gear id.
        /// </summary>
        public const string DiscardToReroll = "discard-to-reroll";
        /// <summary>
        /// Morale Booster / Love Bot: pick which Disgruntled crew to clear.
        /// Options = legal crew ids. <see cref="ChoiceSubmission.SelectedOptionId"/> or Value.
        /// </summary>
        public const string MoraleBoosterTarget = "morale-booster-target";
        /// <summary>
        /// Showdown may re-roll (Guardian own die / Chari force rival).
        /// Options: <see cref="SkillRerollOptions"/>. ContextId discriminates side
        /// (see <see cref="ShowdownRerollContexts"/>).
        /// </summary>
        public const string ShowdownReroll = "showdown-reroll";
        /// <summary>
        /// Any Port Friends in Low Places: choose how many free Fuel to Load (0–4) at own Haven.
        /// <see cref="ChoiceSubmission.Amount"/> = fuel count.
        /// </summary>
        public const string HavenFuelAmount = "haven-fuel-amount";
        /// <summary>
        /// Fully Equipped Med Bay: keep or re-roll a Medic Check die.
        /// Options: <see cref="SkillRerollOptions"/>. ContextId = kill count (or victim id).
        /// </summary>
        public const string MedicReroll = "medic-reroll";
        /// <summary>
        /// Meadows: Kill Meadows instead of the threatened crew (Kill / Apprehend / Seize).
        /// Options: <see cref="MeadowsRedirectOptions"/>.
        /// </summary>
        public const string MeadowsRedirect = "meadows-redirect";
        /// <summary>
        /// Sheydra / Stitch: switch skill for this test (once per job).
        /// Options: <see cref="SkillSwitchOptions"/>.
        /// </summary>
        public const string SkillSwitch = "skill-switch";
        /// <summary>
        /// Holder Make-Work: also take a Fugitive Token.
        /// Options: <see cref="HolderFugitiveOptions"/>.
        /// </summary>
        public const string MakeWorkFugitive = "make-work-fugitive";
        /// <summary>
        /// Wright: take Immoral +$100 per Fugitive on deliver.
        /// Options: <see cref="WrightBonusOptions"/>.
        /// </summary>
        public const string FugitiveDeliverBonus = "fugitive-deliver-bonus";
        /// <summary>
        /// Universal Encyclopedia: reorder top Misbehave ids in <see cref="ChoiceSubmission.Values"/>.
        /// </summary>
        public const string MisbehaveReorder = "misbehave-reorder";
        /// <summary>
        /// Roberta: discard Roberta or lose Solid Rep.
        /// Options: <see cref="DiscardOrLoseSolidOptions"/>. ContextId = contact id.
        /// </summary>
        public const string DiscardOrLoseSolid = "discard-or-lose-solid";
        /// <summary>
        /// Dalin: pay to discard/redraw Misbehave, or decline.
        /// Options: <see cref="DalinRedrawOptions"/>.
        /// </summary>
        public const string MisbehaveDiscardRedraw = "misbehave-discard-redraw";
        /// <summary>
        /// Bree: sell Parts to a Solid Contact during Deal ($Amount each).
        /// <see cref="ChoiceSubmission.Amount"/> = Parts to sell (0 = decline).
        /// ContextId = Contact name.
        /// </summary>
        public const string DealSellParts = "deal-sell-parts";
    }

    /// <summary>Discrete option ids for <see cref="PendingChoiceKinds.MeadowsRedirect"/>.</summary>
    public static class MeadowsRedirectOptions
    {
        public const string KillMeadows = "kill-meadows";
        public const string Decline = "decline";
    }

    /// <summary>Discrete option ids for <see cref="PendingChoiceKinds.SkillSwitch"/>.</summary>
    public static class SkillSwitchOptions
    {
        public const string Switch = "switch";
        public const string Keep = "keep";
    }

    /// <summary>Discrete option ids for <see cref="PendingChoiceKinds.MakeWorkFugitive"/>.</summary>
    public static class HolderFugitiveOptions
    {
        public const string TakeFugitive = "take-fugitive";
        public const string Decline = "decline";
    }

    /// <summary>Discrete option ids for <see cref="PendingChoiceKinds.FugitiveDeliverBonus"/>.</summary>
    public static class WrightBonusOptions
    {
        public const string TakeBonus = "take-bonus";
        public const string Decline = "decline";
    }

    /// <summary>Discrete option ids for <see cref="PendingChoiceKinds.DiscardOrLoseSolid"/>.</summary>
    public static class DiscardOrLoseSolidOptions
    {
        public const string DiscardCrew = "discard-crew";
        public const string LoseSolid = "lose-solid";
    }

    /// <summary>Discrete option ids for <see cref="PendingChoiceKinds.MisbehaveDiscardRedraw"/>.</summary>
    public static class DalinRedrawOptions
    {
        public const string PayRedraw = "pay-redraw";
        public const string Decline = "decline";
    }

    /// <summary>Discrete option ids for <see cref="PendingChoiceKinds.DiscardToReroll"/>.</summary>
    public static class DiscardToRerollOptions
    {
        public const string Discard = "discard";
        public const string Decline = "decline";
    }

    /// <summary>ContextId values for <see cref="PendingChoiceKinds.ShowdownReroll"/>.</summary>
    public static class ShowdownRerollContexts
    {
        public const string AttackerOwn = "attacker-own";
        public const string DefenderOwn = "defender-own";
        public const string AttackerForceRival = "attacker-force-rival";
        public const string DefenderForceRival = "defender-force-rival";
    }

    /// <summary>Discrete option ids for <see cref="PendingChoiceKinds.SkillReroll"/>.</summary>
    public static class SkillRerollOptions
    {
        public const string Keep = "keep";
        public const string Reroll = "reroll";
    }

    /// <summary>ContextId prefixes / ids for <see cref="PendingChoiceKinds.SectorDestination"/>.</summary>
    public static class SectorDestinationContexts
    {
        public const string CruiserPatrol = "cruiser-patrol";
        public const string AllianceEntanglements = "alliance-entanglements";
        public const string SafeHarborPrefix = "safe-harbor:";
        public const string ShipNudge = "ship-nudge";
        public const string ReaverCutter = "reaver-cutter";
        public const string OperativeCorvette = "operative-corvette";

        public static string SafeHarbor(string intendedSectorId) =>
            SafeHarborPrefix + intendedSectorId;

        public static bool TryParseSafeHarbor(string? contextId, out string intendedSectorId)
        {
            intendedSectorId = "";
            if (string.IsNullOrWhiteSpace(contextId)
                || !contextId.StartsWith(SafeHarborPrefix, StringComparison.Ordinal))
                return false;
            intendedSectorId = contextId.Substring(SafeHarborPrefix.Length);
            return !string.IsNullOrWhiteSpace(intendedSectorId);
        }
    }

    /// <summary>Discrete option ids for <see cref="PendingChoiceKinds.MedFoamDiscard"/>.</summary>
    public static class MedFoamDiscardOptions
    {
        public const string Discard = "discard";
        public const string Decline = "decline";
    }
}
