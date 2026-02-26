using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace FFXIV.Venues.Directory.Integrations;

internal sealed class LifestreamNavigator
{
    private readonly ICallGateSubscriber<string, object> _executeCommand;

    public LifestreamNavigator(IDalamudPluginInterface pluginInterface)
    {
        _executeCommand = pluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
    }

    public bool IsAvailable => _executeCommand.HasAction;

    public bool TryExecuteCommand(string arguments, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(arguments))
        {
            error = "Destination is empty.";
            return false;
        }

        if (!IsAvailable)
        {
            error = "Lifestream is not available.";
            return false;
        }

        try
        {
            _executeCommand.InvokeAction(arguments);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
