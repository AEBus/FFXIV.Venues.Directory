using System;
using System.Collections.Generic;

namespace FFXIV.Venues.Directory.Infrastructure.Media;

internal static class ImageAddress
{
    // Pictures are only fetched over https: a plain http one could be read or swapped on the way.
    public static bool CanFetch(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    // Whether the address is on one of the domains or their subdomains.
    public static bool IsOnDomain(Uri uri, IEnumerable<string> domains)
    {
        foreach (var domain in domains)
        {
            if (uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
