using System;

namespace FFXIV.Venues.Directory.Infrastructure.Commands;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class CommandBindingAttribute : Attribute
{
    public string CommandName { get; }
    public string? CommandDescription { get; }

    public CommandBindingAttribute(string commandName, string? commandDescription = null)
    {
        CommandName = commandName ?? throw new ArgumentNullException(nameof(commandName));
        CommandDescription = commandDescription;
    }
}

