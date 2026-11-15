using Firefly.Core.Cards;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>Face-up Most Wanted row (PBH p.8) — portrait reuses Crew plate by name.</summary>
public sealed record MostWantedCardView(
    string Id,
    string Name,
    bool Immoral,
    bool IsCortex,
    string? PickupPlanet,
    string? DropoffPlanet,
    int Pay,
    int BotchKill,
    string? Description,
    string? PortraitUrl,
    string? CrewId);

/// <summary>Active Alliance Alert — text-only JSON chrome (prefs 2026-11-15).</summary>
public sealed record AllianceAlertView(
    string Id,
    string Name,
    string? Detail,
    bool SystemWide,
    string? SystemWideText,
    string? SourceLabel,
    string? ParkedLabel,
    int DrawCount);

/// <summary>P9 expansions strip: Most Wanted + Alliance Alert deck state.</summary>
public sealed record ExpansionChromeView(
    bool BountyDeckInPlay,
    IReadOnlyList<MostWantedCardView> MostWanted,
    int BountyDrawCount,
    int BountyOutOfGameCount,
    bool AllianceAlertDeckInPlay,
    AllianceAlertView? ActiveAlert,
    bool UseAlertTokens,
    int AlertTokenSectorCount,
    bool CorvetteInPlay,
    string? CorvetteSectorId);

/// <summary>Builds expansion chrome from kernel state only — no invented rules.</summary>
public static class ExpansionChromeBuilder
{
    public static ExpansionChromeView From(GameState game)
    {
        var mostWanted = new List<MostWantedCardView>();
        var bountyDeck = game.BountyDeck;
        if (bountyDeck != null)
        {
            foreach (var card in bountyDeck.FaceUp)
                mostWanted.Add(ToMostWanted(game, card));
        }

        AllianceAlertView? alert = null;
        var alertDeck = game.AllianceAlertDeck;
        if (alertDeck != null)
        {
            var active = alertDeck.Active;
            if (active != null)
            {
                string? parked = null;
                if (alertDeck.IsParked && alertDeck.ParkedOn != null)
                    parked = alertDeck.ParkedOn.ToString();

                alert = new AllianceAlertView(
                    active.Id,
                    active.Name,
                    active.Detail,
                    active.SystemWide,
                    active.SystemWideText,
                    active.SourceLabel,
                    parked,
                    alertDeck.DrawCount);
            }
            else
            {
                alert = new AllianceAlertView(
                    "",
                    "(no active alert)",
                    "Deck is wired; no Active card until StartingAlertCard / Misbehave Alliance Alert! / Cruiser Nav cycles.",
                    false,
                    null,
                    null,
                    null,
                    alertDeck.DrawCount);
            }
        }

        var alertSectors = 0;
        if (game.UseAlertTokens)
        {
            foreach (var kv in game.Tokens.AlertTokens)
            {
                if (!kv.Value.IsEmpty)
                    alertSectors++;
            }
            foreach (var id in AlertTokenRules.PermanentReaverSpaceSectorIds)
            {
                if (!game.Tokens.AlertTokens.ContainsKey(id))
                    alertSectors++;
            }
        }

        return new ExpansionChromeView(
            bountyDeck != null,
            mostWanted,
            bountyDeck?.DrawCount ?? 0,
            bountyDeck?.OutOfGameCount ?? 0,
            alertDeck != null,
            alert,
            game.UseAlertTokens,
            alertSectors,
            !string.IsNullOrEmpty(game.Tokens.OperativeCorvetteSectorId),
            game.Tokens.OperativeCorvetteSectorId);
    }

    private static MostWantedCardView ToMostWanted(GameState game, BountyCard card)
    {
        string? crewId = null;
        string? portrait = null;
        if (game.Crew != null)
        {
            var crew = ResolveCrewForBounty(game.Crew, card);
            if (crew != null)
            {
                crewId = crew.Id;
                portrait = CardArtUrls.Crew(crew.Id);
            }
        }

        return new MostWantedCardView(
            card.Id,
            card.Name,
            card.Immoral,
            card.IsCortex,
            card.PickupPlanet,
            card.DropoffPlanet,
            card.Pay,
            card.BotchKill,
            card.Description,
            portrait,
            crewId);
    }

    /// <summary>
    /// Prefs: Bounty cards reuse the Crew portrait for the same named crew.
    /// Cortex Alerts match Enforcer / Scrapper / Bandit singular names via <see cref="BountyCard.MatchesCrewName"/>.
    /// </summary>
    public static CrewCard? ResolveCrewForBounty(CrewCatalog crew, BountyCard bounty)
    {
        var byName = crew.FindByName(bounty.Name);
        if (byName != null)
            return byName;

        // Cortex plural titles → singular crew definitions.
        if (bounty.IsCortex)
        {
            foreach (var card in crew.Cards.Values)
            {
                if (bounty.MatchesCrewName(card.Name))
                    return card;
            }
        }

        return null;
    }
}
