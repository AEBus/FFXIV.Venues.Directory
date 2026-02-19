using System.Threading.Tasks;
using FFXIV.Venues.Directory.Commands.Brokerage;
using FFXIV.Venues.Directory.UI;
using FFXIV.Venues.Directory.UI.Abstractions;

namespace FFXIV.Venues.Directory.Commands;

[Command("/ffxivvenues", "Open venues directory or /ffxivvenues settings")]
internal class ShowUiCommand : ICommandHandler
{
    private readonly WindowBroker _windowBroker;

    public ShowUiCommand(WindowBroker windowBroker)
    {
        _windowBroker = windowBroker;
    }

    public Task Handle(string args)
    {
        var argument = args?.Trim() ?? string.Empty;
        if (argument.Equals("settings", System.StringComparison.OrdinalIgnoreCase))
        {
            _windowBroker.Create<SettingsWindow>()?.Show();
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(argument))
        {
            PluginService.ChatGui.PrintError("Unknown argument. Use /ffxivvenues or /ffxivvenues settings.");
            return Task.CompletedTask;
        }

        _windowBroker.Create<VenueDirectoryWindow>()?.Show();
        return Task.CompletedTask;
    }
}

