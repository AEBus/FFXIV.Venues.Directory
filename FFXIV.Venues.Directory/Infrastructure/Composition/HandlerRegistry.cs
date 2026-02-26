using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace FFXIV.Venues.Directory.Infrastructure.Composition;

internal sealed class HandlerRegistry<TService> where TService : class
{
    private readonly Dictionary<string, Type> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider _serviceProvider;

    public HandlerRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IReadOnlyCollection<string> Keys => _map.Keys.ToArray();

    public HandlerRegistry<TService> Add(string key, Type implementationType)
    {
        if (!implementationType.IsAssignableTo(typeof(TService)))
        {
            throw new ArgumentException($"Type {implementationType.Name} must implement {typeof(TService).Name}");
        }

        _map[key] = implementationType;
        return this;
    }

    public bool ContainsKey(string key) => _map.ContainsKey(key);

    public TService? Activate(string key, IServiceProvider? serviceProvider = null)
    {
        if (!_map.TryGetValue(key, out var implementationType))
        {
            return null;
        }

        return ActivatorUtilities.CreateInstance(serviceProvider ?? _serviceProvider, implementationType) as TService;
    }
}
