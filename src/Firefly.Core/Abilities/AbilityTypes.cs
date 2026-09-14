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
    }
}
