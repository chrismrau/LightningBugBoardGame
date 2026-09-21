namespace Firefly.Core.State
{
    /// <summary>
    /// Card-driven changes to Deal. Gear/crew apply these when equipped;
    /// the kernel only reads the resolved modifiers.
    /// </summary>
    public sealed class DealModifiers
    {
        public int ExtraConsider { get; set; }
        public int? ConsiderUpTo { get; set; }
        public bool CanDealFromAnySector { get; set; }
        public bool ConsiderTopCardFromAnyContact { get; set; }
        public int MaxKeepFromConsider { get; set; } = DealActionDefaults.MaxKeepFromConsider;
        /// <summary>Fess: remote Deal only with this Contact name (e.g. Higgins).</summary>
        public string? NamedRemoteContact { get; set; }
        /// <summary>Universal Encyclopedia: peek/reorder this many Misbehave cards (0 = off).</summary>
        public int ReorderMisbehaveTop { get; set; }
    }

    public static class DealActionDefaults
    {
        public const int BaseConsider = 3;
        public const int MaxKeepFromConsider = 2;
        public const int PatienceSolidConsider = 4;
        public const int FineHatConsiderUpTo = 4;
        public const int CortexUplinkConsider = 1;
        public const int BadgerWarrantClearCost = 1000;
    }

    /// <summary>GF9 Buy Actions: Consider 3 / Buy 2 (Dress may raise Take max).</summary>
    public static class BuyActionDefaults
    {
        public const int MaxBuyCards = 2;
        public const int DressBuyUpTo = 3;
    }
}
