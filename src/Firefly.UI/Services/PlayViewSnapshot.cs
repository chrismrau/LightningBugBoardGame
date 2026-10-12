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
    public required string? SelectedSectorId { get; init; }
    public required SectorInspectInfo? Inspect { get; init; }
    public required IReadOnlyList<ShipTokenView> ShipTokens { get; init; }
    public required IReadOnlyList<MarkerTokenView> Markers { get; init; }
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
            .Select(p => new ShipTokenView(
                p.Id,
                p.Name,
                p.ShipId ?? "ship",
                p.SectorId,
                p.Id == player.Id))
            .ToList();

        var markers = new List<MarkerTokenView>();
        if (!string.IsNullOrEmpty(game.Tokens.AllianceCruiserSectorId))
        {
            markers.Add(new MarkerTokenView(
                "alliance-cruiser",
                "Alliance Cruiser",
                game.Tokens.AllianceCruiserSectorId,
                MarkerKind.AllianceCruiser));
        }
        if (!string.IsNullOrEmpty(game.Tokens.OperativeCorvetteSectorId))
        {
            markers.Add(new MarkerTokenView(
                "operative-corvette",
                "Operative's Corvette",
                game.Tokens.OperativeCorvetteSectorId,
                MarkerKind.Corvette));
        }
        foreach (var cutter in game.Tokens.ReaverCutterSectorIds)
        {
            markers.Add(new MarkerTokenView(
                "reaver-" + cutter,
                "Reaver Cutter",
                cutter,
                MarkerKind.ReaverCutter));
        }

        var pendingSummary = BuildPendingSummary(game);

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
            HasPendingEvents = game.HasPendingEvents,
            PendingChoiceKind = game.PendingChoice?.Kind,
            PendingSummary = pendingSummary,
            SelectedSectorId = session.SelectedSectorId,
            Inspect = inspect,
            ShipTokens = ships,
            Markers = markers,
            UseBlueSun = session.UseBlueSun,
            UseBountyDeck = session.UseBountyDeck,
            UseOperativesCorvette = session.UseOperativesCorvette
        };
    }

    private static string? BuildPendingSummary(GameState game)
    {
        if (!game.HasPendingEvents)
            return null;
        if (game.PendingChoice != null)
            return $"PendingChoice: {game.PendingChoice.Kind}";
        if (game.PendingAlertSectors.Count > 0)
            return $"Alert Tokens ({game.PendingAlertSectors.Count})";
        if (game.PendingEncounter.HasValue)
            return $"Encounter: {game.PendingEncounter}";
        if (game.PendingNavDraws.Count > 0)
            return $"Nav draws ({game.PendingNavDraws.Count})";
        if (game.PendingMisbehave != null)
            return "Misbehave in progress";
        if (game.PendingGoalWork != null)
            return "Goal Work in progress";
        return "Pending events";
    }
}

public sealed record ShipTokenView(
    string PlayerId,
    string Name,
    string ShipId,
    string SectorId,
    bool IsCurrent);

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
    MarkerKind Kind);

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
