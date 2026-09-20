namespace Firefly.Core.Abilities
{
    /// <summary>
    /// Typed ability DSL identifiers. Engines dispatch on these strings from JSON/TSV —
    /// never by parsing English <c>description</c>. Grow incrementally.
    /// </summary>
    public static class AbilityTypes
    {
        /// <summary>FAQ 4.1 p.8: if Leader would become Disgruntled, Disgruntle this crew instead.</summary>
        public const string RedirectLeaderDisgruntle = "redirectLeaderDisgruntle";

        /// <summary>Simon: +N to Medic Checks (also used by gear later).</summary>
        public const string MedicCheckBonus = "medicCheckBonus";

        /// <summary>Simon: +N to a named crew's Gifted rolls (subject = River Tam).</summary>
        public const string GiftedRollBonus = "giftedRollBonus";

        /// <summary>Passive skill addend (Fight/Tech/Talk) while the source is usable.</summary>
        public const string SkillAddend = "skillAddend";

        /// <summary>Big Damn Heroes: take $N when you Proceed while Misbehaving.</summary>
        public const string MisbehaveProceedCash = "misbehaveProceedCash";

        /// <summary>Wash Hard Burn: +N to Full Burn range.</summary>
        public const string FullBurnRangeBonus = "fullBurnRangeBonus";

        /// <summary>Max Gear this crew/Leader may carry (default 1). Amount = limit.</summary>
        public const string GearCarryLimit = "gearCarryLimit";

        /// <summary>Gear does not count toward the carrier's Gear Limit.</summary>
        public const string ExemptFromGearLimit = "exemptFromGearLimit";

        /// <summary>
        /// May re-roll a skill test of the printed skill (Kaylee Tech / Zoe Fight / Inara Negotiate).
        /// Optional — suspends PendingChoiceKinds.SkillReroll.
        /// </summary>
        public const string SkillReroll = "skillReroll";

        /// <summary>
        /// Cortland: may pay Bribes before any Negotiate Test (not Showdowns).
        /// Optional — enables Bribes on Talk tests and always suspends BribeAmount.
        /// </summary>
        public const string BribesOnAnyNegotiate = "bribesOnAnyNegotiate";

        /// <summary>
        /// Barkeep: Shore Leave at Supply Planets is free (printed; no “may” — always-on cost).
        /// </summary>
        public const string FreeShoreLeaveAtSupply = "freeShoreLeaveAtSupply";

        /// <summary>
        /// Nandi Heart of Gold: Hire Crew at no cost (permission — always-on Buy cost when present).
        /// </summary>
        public const string FreeHireCrew = "freeHireCrew";

        /// <summary>
        /// Emma / Helen / Lucy: use a Crew Action to clear Disgruntled from one other crew.
        /// Optional — target pick always suspends when unset.
        /// </summary>
        public const string MoraleBooster = "moraleBooster";

        /// <summary>
        /// Love Bot: use a Crew Action to clear Disgruntled from any one crew (carried gear).
        /// Optional — target pick always suspends when unset.
        /// </summary>
        public const string ClearDisgruntledAction = "clearDisgruntledAction";

        /// <summary>
        /// Board Game Collection: Buy Action Shore Leave in any Sector (permission — planet not required).
        /// </summary>
        public const string ShoreLeaveAnySector = "shoreLeaveAnySector";

        /// <summary>
        /// Extra Ammo Clips / Yolonda's Pistol: discard carried gear to re-roll a Fight test.
        /// Optional — always suspends after the Fight roll when carried.
        /// </summary>
        public const string DiscardToReroll = "discardToReroll";

        /// <summary>
        /// The Guardian: may re-roll your own SHOWDOWN die.
        /// Optional — always suspends after the Showdown roll.
        /// </summary>
        public const string ShowdownReroll = "showdownReroll";

        /// <summary>
        /// Chari: in a SHOWDOWN, may force a Rival to re-roll.
        /// Optional — always suspends after the Showdown roll.
        /// </summary>
        public const string ShowdownForceRivalReroll = "showdownForceRivalReroll";

        /// <summary>
        /// Fully Equipped Med Bay: may re-roll Medic Checks.
        /// Optional — always suspends after the first Medic die.
        /// </summary>
        public const string MedicCheckReroll = "medicCheckReroll";

        /// <summary>
        /// Mandatory re-roll of faces showing 1 (Wash's Dinosaurs / Jayne's Hat / Bow / Guns / Carbine).
        /// No printed “may” — auto-reroll ones. Location = Flying | Misbehaving when scoped;
        /// Subject = Companion when carrier profession required.
        /// </summary>
        public const string RerollOnes = "rerollOnes";

        /// <summary>
        /// Dalin: once per Work Action, pay $Amount to discard and re-draw a Misbehave card.
        /// Optional — always suspends when legal.
        /// </summary>
        public const string MisbehaveDiscardRedraw = "misbehaveDiscardRedraw";

        /// <summary>
        /// Roberta: may discard this crew instead of losing Solid Rep.
        /// Optional — always suspends when Solid would be lost.
        /// </summary>
        public const string DiscardInsteadOfLoseSolid = "discardInsteadOfLoseSolid";

        /// <summary>
        /// Labor Contract: Hire 1 Crew from the named Supply discard pile for free (any sector).
        /// Location = planet name. Permission — Buy path when present.
        /// </summary>
        public const string HireFromSupplyDiscard = "hireFromSupplyDiscard";

        /// <summary>
        /// The Salesman: Buy Action — discard self to buy Upgrade/Drive from any discard at half price.
        /// Optional — Buy path when chosen.
        /// </summary>
        public const string DiscardBuyUpgradeHalf = "discardBuyUpgradeHalf";

        /// <summary>
        /// Corbin: Buy Drive Cores and Ship Upgrades at half price (permission).
        /// </summary>
        public const string HalfPriceDriveAndUpgrade = "halfPriceDriveAndUpgrade";

        /// <summary>
        /// Marco: Buy Explosives/Firearm Gear at half price (permission).
        /// </summary>
        public const string HalfPriceExplosiveFirearmGear = "halfPriceExplosiveFirearmGear";

        /// <summary>
        /// A Very Fine Hat: when Dealing, Consider up to Amount jobs (permission; Amount default 4).
        /// </summary>
        public const string ConsiderJobsUpTo = "considerJobsUpTo";

        /// <summary>
        /// Cortex Uplink: Deal from any location — Consider top face-down of any Contact.
        /// </summary>
        public const string ConsiderTopAnyContact = "considerTopAnyContact";
    }
}
