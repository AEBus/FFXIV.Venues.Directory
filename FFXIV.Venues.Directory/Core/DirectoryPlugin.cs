using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using FFXIV.Venues.Directory.Features.Directory.Data;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Features.Directory.Places;
using FFXIV.Venues.Directory.Features.Directory.Ui;
using FFXIV.Venues.Directory.Features.Events;
using FFXIV.Venues.Directory.Features.Notifications;
using FFXIV.Venues.Directory.Features.PartyFinder;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Infrastructure.Media;
using FFXIV.Venues.Directory.Infrastructure.Net;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using FFXIV.Venues.Directory.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace FFXIV.Venues.Directory.Core;

// The plugin's entry point: builds the services, owns the directory window and the chat command, and opens the window from the plugin installer, the command and notifications.
public sealed class DirectoryPlugin : IDalamudPlugin
{
    private const string OpenCommand = "/ffxivvenues";

    // How often the lists are reloaded while something uses them; statuses are recomputed from the clock in between.
    private static readonly TimeSpan VenueRefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EventRefreshInterval = TimeSpan.FromMinutes(15);

    // How far ahead events are listed.
    private static readonly TimeSpan EventsAhead = TimeSpan.FromDays(14);

    // xivpf.com sends every Party Finder listing at once, so ads are fetched rarely, and only while the window is open.
    private static readonly TimeSpan PartyFinderRefreshInterval = TimeSpan.FromMinutes(10);

    private readonly IDalamudPluginInterface _pluginInterface;

    // The HTTP clients of the services, disposed with the plugin so their connections close when it unloads.
    private readonly List<HttpClient> _httpClients = [];
    private readonly ServiceProvider _services;
    private readonly WindowSystem _windows = new("FFXIV.Venues.Directory");
    private readonly PluginUiFont _font;
    private readonly FavoriteAlerts _favoriteAlerts;
    private DirectoryBrowserWindow? _directoryWindow;

    public DirectoryPlugin(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        pluginInterface.Create<DalamudServices>();

        var configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _services = BuildServiceProvider(pluginInterface, configuration);
        _font = _services.GetRequiredService<PluginUiFont>();

        pluginInterface.UiBuilder.Draw += DrawWindows;
        pluginInterface.UiBuilder.OpenMainUi += ToggleDirectory;
        pluginInterface.UiBuilder.OpenConfigUi += OpenSettings;

        _favoriteAlerts = _services.GetRequiredService<FavoriteAlerts>();
        _favoriteAlerts.VenueRequested += ShowVenue;
        _favoriteAlerts.EventRequested += ShowEvent;

        DalamudServices.CommandManager.AddHandler(OpenCommand, new CommandInfo(OnOpenCommand) { HelpMessage = "Open the venue directory" });
    }

    public string Name => "FFXIV Venues Directory";

    // The directory window, created the first time it is needed so that loading the plugin does no window work.
    private DirectoryBrowserWindow DirectoryWindow
    {
        get
        {
            if (_directoryWindow == null)
            {
                _directoryWindow = _services.GetRequiredService<DirectoryBrowserWindow>();
                _windows.AddWindow(_directoryWindow);
                _windows.AddWindow(_directoryWindow.Settings);
            }

            return _directoryWindow;
        }
    }

    public void Dispose()
    {
        DalamudServices.CommandManager.RemoveHandler(OpenCommand);
        _pluginInterface.UiBuilder.Draw -= DrawWindows;
        _pluginInterface.UiBuilder.OpenMainUi -= ToggleDirectory;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        _favoriteAlerts.VenueRequested -= ShowVenue;
        _favoriteAlerts.EventRequested -= ShowEvent;
        _windows.RemoveAllWindows();
        _services.Dispose();
        foreach (var client in _httpClients)
        {
            client.Dispose();
        }
    }

    private ServiceProvider BuildServiceProvider(IDalamudPluginInterface pluginInterface, Configuration configuration)
    {
        var serviceCollection = new ServiceCollection();
        var version = typeof(DirectoryPlugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var userAgent = $"FFXIV.Venues.Directory/{version} (+https://github.com/AEBus/FFXIV.Venues.Directory)";

        var apiClient = CreateClient(TimeSpan.FromSeconds(60));
        apiClient.BaseAddress = new Uri(FfxivVenuesClient.ApiBaseAddress);

        // Partake is behind Cloudflare, which refuses requests without a User-Agent. Partake and xivpf.com are the services the plugin identifies itself to, as agreed with their developers.
        var partakeHttpClient = CreateClient(TimeSpan.FromSeconds(30));
        partakeHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        // Images: banners from FFXIV Venues and Partake, and pictures in descriptions from wherever they are hosted. The User-Agent is added per request, for Partake only.
        var imageHttpClient = CreateClient(TimeSpan.FromSeconds(30));
        var partyFinderHttpClient = CreateClient(TimeSpan.FromSeconds(60));
        partyFinderHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        serviceCollection.AddSingleton(pluginInterface);
        serviceCollection.AddSingleton<IUiBuilder>(_ => pluginInterface.UiBuilder);
        serviceCollection.AddSingleton(DalamudServices.DataManager);
        serviceCollection.AddSingleton(DalamudServices.TextureProvider);
        serviceCollection.AddSingleton(DalamudServices.Framework);
        serviceCollection.AddSingleton(DalamudServices.NotificationManager);

        serviceCollection.AddSingleton(configuration);
        serviceCollection.AddSingleton(apiClient);

        serviceCollection.AddSingleton<PluginUiFont>();
        serviceCollection.AddSingleton<LifestreamNavigator>();
        serviceCollection.AddSingleton<PlotSizeLookup>();
        serviceCollection.AddSingleton<OpenWorldPlaces>();
        serviceCollection.AddSingleton<FavoriteAlerts>();
        serviceCollection.AddSingleton<DirectoryBrowserWindow>();
        serviceCollection.AddSingleton(_ => new PartakeClient(partakeHttpClient));
        serviceCollection.AddSingleton(_ => new RemoteImageCache(imageHttpClient, DalamudServices.TextureProvider, userAgent, ["partake.gg"]));
        serviceCollection.AddSingleton(_ => new FfxivVenuesClient(apiClient));
        serviceCollection.AddSingleton(_ => new XivpfClient(partyFinderHttpClient));
        serviceCollection.AddSingleton(services => new PeriodicFeed<IReadOnlyList<PartyFinderAd>>(
            services.GetRequiredService<XivpfClient>().GetAdsAsync,
            PartyFinderRefreshInterval,
            "xivpf.com"));
        serviceCollection.AddSingleton(services => new PeriodicFeed<DirectoryVenue[]>(
            services.GetRequiredService<FfxivVenuesClient>().GetVenuesAsync,
            VenueRefreshInterval,
            "FFXIV Venues"));
        serviceCollection.AddSingleton(services =>
        {
            var partake = services.GetRequiredService<PartakeClient>();
            return new PeriodicFeed<IReadOnlyList<CommunityEvent>>(
                cancellationToken =>
                {
                    var now = DateTimeOffset.UtcNow;
                    return partake.GetEventsAsync(now, now + EventsAhead, cancellationToken);
                },
                EventRefreshInterval,
                "Partake");
        });

        return serviceCollection.BuildServiceProvider();
    }

    // Returns a new HTTP client, disposed with the plugin. The services compress their responses; pooled connections are renewed periodically, so a long session picks up DNS changes.
    private HttpClient CreateClient(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        };
        var client = new HttpClient(handler) { Timeout = timeout };
        _httpClients.Add(client);
        return client;
    }

    // Every window draws with the plugin's UI font.
    private void DrawWindows()
    {
        using (_font.Push())
        {
            _windows.Draw();
        }
    }

    private void OnOpenCommand(string command, string arguments)
    {
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            DalamudServices.ChatGui.PrintError($"{OpenCommand} takes no arguments.");
            return;
        }

        DirectoryWindow.IsOpen = true;
    }

    private void ToggleDirectory() => DirectoryWindow.IsOpen = !DirectoryWindow.IsOpen;

    // The settings open on their own, without the directory.
    private void OpenSettings() => DirectoryWindow.OpenSettings();

    // Opens the window on the venue or event of a clicked notification.
    private void ShowVenue(string venueId)
    {
        DirectoryWindow.IsOpen = true;
        DirectoryWindow.ShowVenue(venueId);
    }

    private void ShowEvent(int eventId)
    {
        DirectoryWindow.IsOpen = true;
        DirectoryWindow.ShowEvent(eventId);
    }
}
