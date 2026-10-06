using System;
using Dalamud.Utility;

namespace FFXIV.Venues.Directory.Infrastructure.Net;

// Opens addresses in the browser. Only web addresses are opened: descriptions are written by venue owners and event hosts, and the shell would also open files, network shares and other protocols.
internal static class WebLink
{
    // Returns true when the text is an absolute http or https address.
    public static bool IsWebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    // Opens a web address in the browser; anything else is logged and ignored.
    public static void Open(string? url)
    {
        if (!IsWebAddress(url))
        {
            DalamudServices.PluginLog.Warning("Could not open {Url}: not a web address.", url ?? string.Empty);
            return;
        }

        Util.OpenLink(url!);
    }
}
