using Firefly.UI.Components;
using Firefly.UI.Hubs;
using Firefly.UI.Services;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();
builder.Services.AddSingleton<RepoPaths>();
builder.Services.AddSingleton<SectorLayoutService>();
builder.Services.AddSingleton<IGameSession, HotseatGameSession>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

var repo = app.Services.GetRequiredService<RepoPaths>();
if (Directory.Exists(repo.BoardArtDirectory))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(repo.BoardArtDirectory),
        RequestPath = "/board"
    });
}

if (Directory.Exists(repo.ShipIconsDirectory))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(repo.ShipIconsDirectory),
        RequestPath = "/ships"
    });
}

// Option B plates + full ship card scans under reference/Cards/{…}
void MapCardArt(string directory, string requestPath)
{
    if (Directory.Exists(directory))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(directory),
            RequestPath = requestPath
        });
    }
}

MapCardArt(repo.ShipCardsDirectory, "/cards/ships");
MapCardArt(repo.CrewArtDirectory, "/cards/crew");
MapCardArt(repo.GearArtDirectory, "/cards/gear");
MapCardArt(repo.DriveCoreArtDirectory, "/cards/drive-cores");
MapCardArt(repo.ShipUpgradeArtDirectory, "/cards/ship-upgrades");

app.UseAntiforgery();
app.MapStaticAssets();
app.MapHub<PlayHub>("/hubs/play");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
