using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace FFXIV.Venues.Directory.UI.Abstractions;

internal class WindowBroker
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, Window> _windows = new();

    public WindowBroker(IServiceProvider serviceProvider)
    {
        this._serviceProvider = serviceProvider;
    }

    public T? Create<T>() where T : Window
    {
        var type = typeof(T);
        if (this._windows.TryGetValue(type, out var existing))
            return existing as T;

        var created = ActivatorUtilities.CreateInstance(this._serviceProvider, type) as T;
        if (created != null)
            this._windows[type] = created;

        return created;
    }
}

