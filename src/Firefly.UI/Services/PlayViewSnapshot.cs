using Firefly.Core.Map;
using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>Immutable UI snapshot so Blazor does not hold live mutable GameState references across renders.</summary>
public sealed class PlayViewSnapshot
{
    public required string ScenarioCardId { get; init; }
    public required string ScenarioName { get; init; }
    public required string SeatName { get; init; }
    public required string SeatId { get; init; }
    public required string ShipId { get; init; }
    public required string SectorId { get; init; }
    public required int Cash { get; init; }
    public required int Fuel { get; init; }
    public required int Parts { get; init; }
    public required int Warrants { get; init; }
    public required int ActionsUsed { get; init; }
    public required int ActionsPerTurn { get; init; }
    public required bool HasPendingEvents { get; init; }
    public required string? PendingChoiceKind { get; init; }
    public required string? PendingSummary { get; init; }
    public required PendingHostView Pending { get; init; }
    public required IReadOnlyList<ActionButtonView> ActionButtons { get; init; }
    public required string? SelectedSectorId { get; init; }
    public required SectorInspectInfo? Inspect { get; init; }
    public required IReadOnlyList<ShipTokenView> ShipTokens { get; init; }
    public required IReadOnlyList<MarkerTokenView> Markers { get; init; }
    public required ShipSheetView ShipSheet { get; init; }
    public required DiscardBrowserView Discards { get; init; }
    public required bool UseBlueSun { get; init; }
    public required bool UseBountyDeck { get; init; }
    public required bool UseOperativesCorvette { get; init; }

    public static PlayViewSnapshot From(IGameSession session, SectorMap map)
    {
        var game = session.Game;
        var player = game.CurrentPlayer;
        var scenarioName = game.Scenario?.Name
            ?? SmokeScenarios.All.FirstOrDefault(s => s.Id == session.ScenarioCardId)?.Name
            ?? session.ScenarioCardId;

        SectorInspectInfo? inspect = null;
        if (!string.IsNullOrEmpty(session.SelectedSectorId)
            && map.Sectors.TryGetValue(session.SelectedSectorId, out var sector))
        {
            inspect = SectorInspectInfo.From(sector);
        }

        var ships = game.Players
            .Select(p =>
            {
                var shipId = p.ShipId ?? "ship";
                return new ShipTokenView(
                    p.Id,
                    p.Name,
                    shipId,
                    p.SectorId,
                    p.Id == player.Id,
                    ShipIconUrl(shipId));
            })
            .ToList();

        var markers = new List<MarkerTokenView>();
        if (!string.IsNullOrEmpty(game.Tokens.AllianceCruiserSectorId))
        {
            markers.Add(new MarkerTokenView(
                "alliance-cruiser",
                "Alliance Cruiser",
                game.Tokens.AllianceCruiserSectorId,
                MarkerKind.AllianceCruiser,
                MarkerIconUrl(MarkerKind.AllianceCruiser)));
        }
        if (!string.IsNullOrEmpty(game.Tokens.OperativeCorvetteSectorId))
        {
            markers.Add(new MarkerTokenView(
                "operative-corvette",
                "Operative's Corvette",
                game.Tokens.OperativeCorvetteSectorId,
                MarkerKind.Corvette,
                MarkerIconUrl(MarkerKind.Corvette)));
        }
        foreach (var cutter in game.Tokens.ReaverCutterSectorIds)
        {
            markers.Add(new MarkerTokenView(
                "reaver-" + cutter,
                "Reaver Cutter",
                cutter,
                MarkerKind.ReaverCutter,
                MarkerIconUrl(MarkerKind.ReaverCutter)));
        }

        var navFaceUp = session.NavFaceUp;
        var pending = PendingHostBuilder.From(game, navFaceUp);
        // Face-up Nav is session-owned; treat it like HasPendingEvents for chrome.
        var hasPending = game.HasPendingEvents || navFaceUp != null;

        return new PlayViewSnapshot
        {
            ScenarioCardId = session.ScenarioCardId,
            ScenarioName = scenarioName,
            SeatName = player.Name,
            SeatId = player.Id,
            ShipId = player.ShipId ?? "",
            SectorId = player.SectorId,
            Cash = player.Cash,
            Fuel = player.Fuel,
            Parts = player.Parts,
            Warrants = player.Warrants,
            ActionsUsed = game.ActionsUsedThisTurn,
            ActionsPerTurn = game.ActionsPerTurn,
            HasPendingEvents = hasPending,
            PendingChoiceKind = pending.Choice?.Kind,
            PendingSummary = pending.HasPending ? pending.Summary : null,
            Pending = pending,
            ActionButtons = ActionBarBuilder.Build(game, navFaceUpBlocking: navFaceUp != null),
            SelectedSectorId = session.SelectedSectorId,
            Inspect = inspect,
            ShipTokens = ships,
            Markers = markers,
            ShipSheet = ShipSheetBuilder.From(game, player),
            Discards = DiscardBrowserBuilder.From(game),
            UseBlueSun = session.UseBlueSun,
            UseBountyDeck = session.UseBountyDeck,
            UseOperativesCorvette = session.UseOperativesCorvette
        };
    }

    /// <summary>Map token URL under <c>/ships</c> (served from <c>reference/Ships/{id}.webp</c>).</summary>
    public static string ShipIconUrl(string shipId) =>
        $"/ships/{shipId}.webp";

    public static string MarkerIconUrl(MarkerKind kind) => kind switch
    {
        MarkerKind.AllianceCruiser => "/ships/alliance-cruiser.webp",
        MarkerKind.Corvette => "/ships/operative-corvette.webp",
        MarkerKind.ReaverCutter => "/ships/reaver-cutter.webp",
        _ => "/ships/firefly.webp"
    };
}

public sealed record ShipTokenView(
    string PlayerId,
    string Name,
    string ShipId,
    string SectorId,
    bool IsCurrent,
    string IconSrc);

public enum MarkerKind
{
    AllianceCruiser,
    ReaverCutter,
    Corvette
}

public sealed record MarkerTokenView(
    string Id,
    string Label,
    string SectorId,
    MarkerKind Kind,
    string IconSrc);

public sealed record SectorInspectInfo(
    string SectorId,
    string Title,
    string Region,
    string Zone,
    string? Planet,
    string? Contact,
    bool HasSupply,
    bool IsPlanetary,
    bool IsRelay)
{
    public static SectorInspectInfo From(Sector sector) =>
        new(
            sector.Id,
            sector.DisplayName ?? sector.Planet ?? sector.Id,
            sector.Region,
            sector.Zone,
            sector.Planet,
            sector.Contact,
            sector.HasSupplyDeck,
            sector.IsPlanetary,
            sector.IsRelay);
}
