using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>
/// How the PendingChoice host should render a kernel kind.
/// Do not invent new kinds — only map ids from <see cref="PendingChoiceKinds"/>.
/// </summary>
public enum ChoiceWidgetMode
{
    /// <summary>Prompt + discrete option buttons → <see cref="ChoiceSubmission.SelectedOptionId"/>.</summary>
    Options,
    /// <summary>Accept / decline → SelectedOptionId when options exist, else Accepted.</summary>
    AcceptDecline,
    /// <summary>Numeric amount → <see cref="ChoiceSubmission.Amount"/>.</summary>
    Amount,
    /// <summary>Single free-form value (sector / crew / rival id) → <see cref="ChoiceSubmission.Value"/>.</summary>
    SingleValue,
    /// <summary>Multi-select ids → <see cref="ChoiceSubmission.Values"/>.</summary>
    MultiValue,
    /// <summary>Goods mix stub (fuel/parts/cargo/contraband counts).</summary>
    GoodsMix,
    /// <summary>Split-crew team assignment stub.</summary>
    SplitCrew,
    /// <summary>Unknown / unconstrained — generic prompt + free SelectedOptionId or Value.</summary>
    Generic
}

/// <summary>
/// Kind → widget registry. Known kernel kinds get a real or stub mode;
/// anything else falls back to Options (when present) or Generic.
/// </summary>
public static class ChoiceWidgetRegistry
{
    private static readonly Dictionary<string, ChoiceWidgetMode> Modes =
        new(StringComparer.Ordinal)
        {
            // Discrete option / accept-decline families
            [PendingChoiceKinds.NavPayOrDecline] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.MedFoamDiscard] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.SkillReroll] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.DiscardToReroll] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.ShowdownReroll] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.MedicReroll] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.MeadowsRedirect] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.WantedRollIgnoreGear] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.SkillSwitch] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.MakeWorkFugitive] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.FugitiveDeliverBonus] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.DiscardOrLoseSolid] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.MisbehaveDiscardRedraw] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.EmissionsFuel] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.NavFakeIdSalvage] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.GoalPayOrBotch] = ChoiceWidgetMode.AcceptDecline,
            [PendingChoiceKinds.AlertAllianceShip] = ChoiceWidgetMode.Options,
            [PendingChoiceKinds.AlertReaverCutter] = ChoiceWidgetMode.Options,
            [PendingChoiceKinds.MisbehaveOption] = ChoiceWidgetMode.Options,
            [PendingChoiceKinds.MisbehaveDiscardCargoOrContraband] = ChoiceWidgetMode.Options,
            [PendingChoiceKinds.MisbehaveChooseCrew] = ChoiceWidgetMode.Options,
            [PendingChoiceKinds.GiftedSkill] = ChoiceWidgetMode.Options,
            [PendingChoiceKinds.MoraleBoosterTarget] = ChoiceWidgetMode.Options,
            [PendingChoiceKinds.DiscardShipUpgrade] = ChoiceWidgetMode.Options,
            [PendingChoiceKinds.MagGrapplerUpgrade] = ChoiceWidgetMode.Options,
            [PendingChoiceKinds.MisbehaveWarrantOrWanted] = ChoiceWidgetMode.Options,

            // Amount
            [PendingChoiceKinds.BribeAmount] = ChoiceWidgetMode.Amount,
            [PendingChoiceKinds.HavenFuelAmount] = ChoiceWidgetMode.Amount,
            [PendingChoiceKinds.DealSellParts] = ChoiceWidgetMode.Amount,

            // Single value
            [PendingChoiceKinds.HavenSector] = ChoiceWidgetMode.SingleValue,
            [PendingChoiceKinds.RivalPlayer] = ChoiceWidgetMode.SingleValue,
            [PendingChoiceKinds.SectorDestination] = ChoiceWidgetMode.SingleValue,

            // Discrete pick among crew / upgrade ids (kernel Options → SelectedOptionId)
            [PendingChoiceKinds.KillVictim] = ChoiceWidgetMode.Options,

            // Multi value
            [PendingChoiceKinds.WantedRollIgnoreCrew] = ChoiceWidgetMode.MultiValue,
            [PendingChoiceKinds.MisbehaveReorder] = ChoiceWidgetMode.MultiValue,

            // Structured stubs
            [PendingChoiceKinds.GoodsMix] = ChoiceWidgetMode.GoodsMix,
            [PendingChoiceKinds.MisbehaveSplitCrew] = ChoiceWidgetMode.SplitCrew,
        };

    public static ChoiceWidgetMode ModeFor(string kind, IReadOnlyList<string>? options)
    {
        if (!string.IsNullOrEmpty(kind) && Modes.TryGetValue(kind, out var mode))
            return mode;

        if (options != null && options.Count > 0)
            return ChoiceWidgetMode.Options;

        return ChoiceWidgetMode.Generic;
    }

    /// <summary>All kernel kinds currently registered (for tests / diagnostics).</summary>
    public static IReadOnlyCollection<string> RegisteredKinds => Modes.Keys;
}
