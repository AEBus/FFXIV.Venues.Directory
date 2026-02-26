using System;
using System.Net.Http;
using Dalamud.Interface;
using Dalamud.Plugin;
using FFXIV.Venues.Directory;
using FFXIV.Venues.Directory.Infrastructure.Commands;
using FFXIV.Venues.Directory.Features.Directory.Ui;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Features.Directory.Media;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace FFXIV.Venues.Directory.Core;

public sealed class DirectoryPlugin : IDalamudPlugin
{
    private const string ApiBaseAddress = "https://api.ffxivvenues.com/v1/";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ServiceProvider _services;
    private readonly WindowRegistry _windowBroker;
    private DirectoryBrowserWindow? _mainWindow;

    public DirectoryPlugin(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        pluginInterface.Create<DalamudServices>();

        var configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _services = BuildServiceProvider(pluginInterface, configuration);
        _windowBroker = _services.GetRequiredService<WindowRegistry>();

        pluginInterface.UiBuilder.OpenMainUi += OnToggleUiRequested;
        pluginInterface.UiBuilder.OpenConfigUi += OnOpenUiRequested;

        _services.GetRequiredService<CommandRouter>().ScanForCommands();
    }

    public string Name => "FFXIV Venues Directory";

    public void Dispose()
    {
        _pluginInterface.UiBuilder.OpenMainUi -= OnToggleUiRequested;
        _pluginInterface.UiBuilder.OpenConfigUi -= OnOpenUiRequested;
        _services.Dispose();
    }

    private static ServiceProvider BuildServiceProvider(IDalamudPluginInterface pluginInterface, Configuration configuration)
    {
        var serviceCollection = new ServiceCollection();
        var apiClient = new HttpClient { BaseAddress = new Uri(ApiBaseAddress) };

        serviceCollection.AddSingleton(pluginInterface);
        serviceCollection.AddSingleton<IUiBuilder>(_ => pluginInterface.UiBuilder);
        serviceCollection.AddSingleton(DalamudServices.CommandManager);
        serviceCollection.AddSingleton(DalamudServices.ChatGui);
        serviceCollection.AddSingleton(DalamudServices.DataManager);
        serviceCollection.AddSingleton(DalamudServices.TextureProvider);

        serviceCollection.AddSingleton(configuration);
        serviceCollection.AddSingleton(apiClient);

        serviceCollection.AddSingleton<CommandRouter>();
        serviceCollection.AddSingleton<WindowRegistry>();
        serviceCollection.AddSingleton<LifestreamNavigator>();
        serviceCollection.AddSingleton<PlotSizeLookup>();
        serviceCollection.AddSingleton<VenueBannerCache>();

        return serviceCollection.BuildServiceProvider();
    }

    private void OnToggleUiRequested()
    {
        var window = GetOrCreateMainWindow();
        if (window == null)
        {
            return;
        }

        window.IsOpen = !window.IsOpen;
    }

    private void OnOpenUiRequested()
    {
        var window = GetOrCreateMainWindow();
        if (window == null)
        {
            return;
        }

        window.IsOpen = true;
    }

    private DirectoryBrowserWindow? GetOrCreateMainWindow()
    {
        _mainWindow ??= _windowBroker.Create<DirectoryBrowserWindow>();
        return _mainWindow;
    }
}
