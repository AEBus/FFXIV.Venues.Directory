using System;
using System.Collections.Generic;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace FFXIV.Venues.Directory.Infrastructure.Ui;

internal sealed class WindowRegistry : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IUiBuilder _uiBuilder;
    private readonly WindowSystem _windowSystem;
    private readonly Dictionary<Type, Window> _instances = new();
    private bool _disposed;

    public WindowRegistry(IServiceProvider serviceProvider, IUiBuilder uiBuilder)
    {
        _serviceProvider = serviceProvider;
        _uiBuilder = uiBuilder;
        _windowSystem = new WindowSystem("FFXIV.Venues.Directory");

        _uiBuilder.Draw += OnDraw;
    }

    public TWindow? Create<TWindow>() where TWindow : Window
    {
        if (_instances.TryGetValue(typeof(TWindow), out var existing))
        {
            return (TWindow)existing;
        }

        if (ActivatorUtilities.CreateInstance(_serviceProvider, typeof(TWindow)) is not TWindow created)
        {
            return null;
        }

        _instances[typeof(TWindow)] = created;
        _windowSystem.AddWindow(created);
        return created;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _uiBuilder.Draw -= OnDraw;
        _windowSystem.RemoveAllWindows();
        _instances.Clear();
    }

    private void OnDraw()
    {
        if (_disposed)
        {
            return;
        }

        _windowSystem.Draw();
    }
}
