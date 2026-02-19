using System;
using System.Net.Http;
using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIV.Venues.Directory.Commands.Brokerage;
using FFXIV.Venues.Directory.UI;
using FFXIV.Venues.Directory.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FFXIV.Venues.Directory;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "FFXIV Venues Directory";
    private readonly ServiceProvider _serviceProvider;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly WindowBroker _windowBroker;
    private VenueDirectoryWindow? _directoryWindow;
    private SettingsWindow? _settingsWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        this._pluginInterface = pluginInterface;
        pluginInterface.Create<PluginService>();

        var httpClient = new HttpClient { BaseAddress = new Uri("https://api.ffxivvenues.com/v1/") };
        var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(pluginInterface);
        serviceCollection.AddSingleton<IUiBuilder>(_ => pluginInterface.UiBuilder);
        serviceCollection.AddSingleton(_ => pluginInterface.UiBuilder as UiBuilder ?? throw new InvalidOperationException("Dalamud returned null UiBuilder instance."));
        serviceCollection.AddSingleton(PluginService.CommandManager);
        serviceCollection.AddSingleton(PluginService.ChatGui);
        serviceCollection.AddSingleton(PluginService.TextureProvider);
        serviceCollection.AddSingleton(config);
        serviceCollection.AddSingleton(httpClient);
        serviceCollection.AddSingleton<CommandBroker>();
        serviceCollection.AddSingleton<WindowBroker>();
        serviceCollection.AddSingleton<VenueService>();

        this._serviceProvider = serviceCollection.BuildServiceProvider();
        this._windowBroker = this._serviceProvider.GetRequiredService<WindowBroker>();
        pluginInterface.UiBuilder.OpenMainUi += this.ToggleVenueDirectory;
        pluginInterface.UiBuilder.OpenConfigUi += this.ToggleSettings;
        this._serviceProvider.GetService<CommandBroker>()?.ScanForCommands();
    }

    public void Dispose()
    {
        this._pluginInterface.UiBuilder.OpenMainUi -= this.ToggleVenueDirectory;
        this._pluginInterface.UiBuilder.OpenConfigUi -= this.ToggleSettings;
        this._serviceProvider.Dispose();
    }

    private void ToggleVenueDirectory()
    {
        this._directoryWindow ??= this._windowBroker.Create<VenueDirectoryWindow>();
        if (this._directoryWindow == null)
            return;

        if (this._directoryWindow.Visible)
            this._directoryWindow.Hide();
        else
            this._directoryWindow.Show();
    }

    private void ToggleSettings()
    {
        this._settingsWindow ??= this._windowBroker.Create<SettingsWindow>();
        if (this._settingsWindow == null)
            return;

        if (this._settingsWindow.Visible)
            this._settingsWindow.Hide();
        else
            this._settingsWindow.Show();
    }
}

