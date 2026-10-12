using Microsoft.AspNetCore.SignalR;

namespace Firefly.UI.Hubs;

/// <summary>
/// Stub SignalR hub so lobby rooms can attach later without rewriting the rules host.
/// Hotseat P1 does not require clients to connect here.
/// </summary>
public sealed class PlayHub : Hub
{
    public Task<string> Ping() => Task.FromResult("pong");
}
