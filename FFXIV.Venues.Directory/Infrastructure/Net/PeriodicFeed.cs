using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FFXIV.Venues.Directory.Infrastructure.Net;

// Data loaded from a web service and kept fresh while something uses it: loaded on the first Tick, again once it is older than the refresh interval, and after a failure retried on its own, waiting longer after each failure in a row. The last good data stays in use through refreshes and failures, so a refresh never empties what is shown. Tick, RefreshNow and the properties belong to the framework thread; only the load runs in the background.
internal sealed class PeriodicFeed<T> : IDisposable
    where T : class
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
    ];

    // The refresh button starts a load only when the last one started at least this long ago, however often it is pressed and whether or not that load failed.
    private static readonly TimeSpan MinimumRefreshAge = TimeSpan.FromMinutes(1);

    private readonly Func<CancellationToken, Task<T>> _load;
    private readonly TimeSpan _refreshInterval;
    private readonly string _serviceName;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task<T>? _task;
    private DateTimeOffset _startedAt;
    private int _failuresInARow;
    private bool _disposed;

    public PeriodicFeed(Func<CancellationToken, Task<T>> load, TimeSpan refreshInterval, string serviceName)
    {
        _load = load;
        _refreshInterval = refreshInterval;
        _serviceName = serviceName;
    }

    // The last data loaded, or null before the first load succeeds.
    public T? Value { get; private set; }

    public DateTimeOffset LoadedAt { get; private set; }

    // Why the last attempt failed, or null once one succeeds.
    public string? Error { get; private set; }

    // When the next attempt starts after a failure.
    public DateTimeOffset RetryAt { get; private set; }

    public bool IsLoading => _task != null;

    // Goes up with every new Value.
    public int Version { get; private set; }

    // Picks up a finished load and starts the next one when it is due.
    public void Tick()
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_task is { IsCompleted: true } task)
        {
            _task = null;
            Complete(task, now);
        }

        if (_task == null && IsDue(now))
        {
            Start();
        }
    }

    // Whether RefreshNow would start a load: none is running and the last one started at least MinimumRefreshAge ago.
    public bool CanRefreshNow => !_disposed && _task == null && DateTimeOffset.UtcNow - _startedAt >= MinimumRefreshAge;

    // Loads now, ahead of the schedule (the refresh button), when CanRefreshNow allows it. Failures in a row still count, so a service that keeps failing is retried no faster than its retry delays and this limit allow.
    public void RefreshNow()
    {
        if (CanRefreshNow)
        {
            Start();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    private bool IsDue(DateTimeOffset now) =>
        Error != null
            ? now >= RetryAt
            : Value == null || now - LoadedAt >= _refreshInterval;

    private void Start()
    {
        var token = _disposeCts.Token;
        _startedAt = DateTimeOffset.UtcNow;
        _task = Task.Run(() => _load(token), token);
    }

    private void Complete(Task<T> task, DateTimeOffset now)
    {
        if (task.IsCompletedSuccessfully)
        {
            if (_failuresInARow > 0)
            {
                DalamudServices.PluginLog.Information("Loaded {Service} again after {Failures} failed attempts.", _serviceName, _failuresInARow);
            }

            Value = task.Result;
            LoadedAt = now;
            Error = null;
            _failuresInARow = 0;
            Version++;
            return;
        }

        if (task.IsCanceled && _disposeCts.IsCancellationRequested)
        {
            return;
        }

        var delay = RetryDelays[Math.Min(_failuresInARow, RetryDelays.Length - 1)];
        _failuresInARow++;
        RetryAt = now + delay;
        // A request that times out ends as a cancelled task, without an exception.
        var exception = task.Exception?.GetBaseException();
        Error = task.IsCanceled ? $"{_serviceName} did not answer in time." : Describe(exception);
        if (_failuresInARow > 1)
        {
            DalamudServices.PluginLog.Debug("Could not load {Service} ({Failures} in a row); retrying in {Delay}s: {Reason}", _serviceName, _failuresInARow, delay.TotalSeconds, Error);
        }
        else if (exception == null || NetworkFailure.IsExpected(exception))
        {
            DalamudServices.PluginLog.Warning("Could not load {Service}; retrying in {Delay}s: {Reason}", _serviceName, delay.TotalSeconds, Error);
        }
        else
        {
            DalamudServices.PluginLog.Warning(exception, "Could not load {Service}; retrying in {Delay}s.", _serviceName, delay.TotalSeconds);
        }
    }

    private string Describe(Exception? error) => error switch
    {
        null => $"{_serviceName} could not be reached.",
        ServiceRefusedException refused => refused.Message,
        TaskCanceledException or TimeoutException => $"{_serviceName} did not answer in time.",
        HttpRequestException { StatusCode: { } status } => $"{_serviceName} answered with an error ({(int)status}).",
        HttpRequestException => $"{_serviceName} could not be reached.",
        _ => $"{_serviceName} sent data the plugin could not read.",
    };
}

// A service turned the request down for a reason worth showing to the user as is, such as a blocked network or region.
internal sealed class ServiceRefusedException(string message) : Exception(message);
