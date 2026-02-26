using System.Threading.Tasks;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Infrastructure.Commands;
using FFXIV.Venues.Directory.Features.Directory.Ui;
using FFXIV.Venues.Directory.Infrastructure.Ui;

namespace FFXIV.Venues.Directory.Features.Directory.Commands;

[CommandBinding("/ffxivvenues", "Open venues directory")]
internal sealed class OpenDirectoryCommand : ICommandAction
{
    private readonly WindowRegistry _windowBroker;

    public OpenDirectoryCommand(WindowRegistry windowBroker)
    {
        _windowBroker = windowBroker;
    }

    public Task Handle(string args)
    {
        if (!string.IsNullOrWhiteSpace(args))
        {
            DalamudServices.ChatGui.PrintError("Unknown argument. Use /ffxivvenues.");
            return Task.CompletedTask;
        }

        var window = _windowBroker.Create<DirectoryBrowserWindow>();
        if (window != null)
        {
            window.IsOpen = true;
        }

        return Task.CompletedTask;
    }
}
