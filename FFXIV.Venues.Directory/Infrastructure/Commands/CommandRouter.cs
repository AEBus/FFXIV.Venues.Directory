using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIV.Venues.Directory.Infrastructure.Composition;

namespace FFXIV.Venues.Directory.Infrastructure.Commands;

internal sealed class CommandRouter : IDisposable
{
    private readonly ICommandManager _commandManager;
    private readonly HandlerRegistry<ICommandAction> _handlers;
    private readonly HashSet<string> _registeredCommands = new(StringComparer.OrdinalIgnoreCase);

    public CommandRouter(IServiceProvider serviceProvider, ICommandManager commandManager)
    {
        _commandManager = commandManager;
        _handlers = new HandlerRegistry<ICommandAction>(serviceProvider);
    }

    public void ScanForCommands(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();

        var commandTypes = assembly
            .GetTypes()
            .Where(type => typeof(ICommandAction).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface);

        foreach (var handlerType in commandTypes)
        {
            foreach (var commandAttribute in handlerType.GetCustomAttributes<CommandBindingAttribute>())
            {
                Register(commandAttribute, handlerType);
            }
        }
    }

    public void Dispose()
    {
        foreach (var command in _registeredCommands)
        {
            _commandManager.RemoveHandler(command);
        }

        _registeredCommands.Clear();
    }

    private void Register(CommandBindingAttribute commandAttribute, Type handlerType)
    {
        var command = commandAttribute.CommandName?.Trim();
        if (string.IsNullOrWhiteSpace(command) || _registeredCommands.Contains(command))
        {
            return;
        }

        _commandManager.AddHandler(command, new CommandInfo(HandleCommand)
        {
            HelpMessage = commandAttribute.CommandDescription ?? string.Empty,
        });

        _handlers.Add(command, handlerType);
        _registeredCommands.Add(command);
    }

    private void HandleCommand(string command, string arguments)
    {
        if (!_handlers.ContainsKey(command))
        {
            return;
        }

        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        _handlers.Activate(command)?.Handle(arguments);
    }
}
