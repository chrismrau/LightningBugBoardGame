namespace Firefly.UI.Services;

/// <summary>P0 smoke scenarios for the P1 scenario chip.</summary>
public static class SmokeScenarios
{
    public static readonly SmokeScenarioOption[] All =
    {
        new("scenario_king-of-all-londinium", "The King of All Londinium"),
        new("scenario_first-time-in-the-captains-chair", "First Time in the Captain's Chair"),
        new("scenario_awful-lonely-in-the-big-black", "Awful Lonely in the Big Black"),
    };

    public const string DefaultId = "scenario_king-of-all-londinium";
}

public sealed record SmokeScenarioOption(string Id, string Name);
