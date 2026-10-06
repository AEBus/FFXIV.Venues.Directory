using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace FFXIV.Venues.Directory.Infrastructure.Net;

internal static class NetworkFailure
{
    // Whether a failure comes from the network or the service rather than from the plugin: such failures are logged with their reason only, anything else with its stack trace.
    public static bool IsExpected(Exception? error) =>
        error is HttpRequestException or TaskCanceledException or TimeoutException or ServiceRefusedException;
}
