using System;
using System.Globalization;
using Microsoft.Win32;

namespace FFXIV.Venues.Directory.Features.Directory.Text;

// How the user wants times written in the directory: as Windows writes them, or always 24- or 12-hour.
public enum ClockFormat
{
    System,
    TwentyFourHour,
    TwelveHour,
}

// Whether Windows shows times with a 12-hour clock, read from the regional short time format; the process culture is used when the registry has no value, as under Wine.
internal static class SystemClock
{
    public static bool Uses12Hour()
    {
        try
        {
            using var international = Registry.CurrentUser.OpenSubKey(@"Control Panel\International");
            var pattern = international?.GetValue("sShortTime") as string ?? international?.GetValue("sTimeFormat") as string;
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                return Is12HourPattern(pattern);
            }
        }
        catch (Exception)
        {
            // Unreadable: the culture decides.
        }

        return Is12HourPattern(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern);
    }

    // "h" is the 12-hour hour and "H" the 24-hour one; text in quotes is literal.
    public static bool Is12HourPattern(string pattern)
    {
        var quoted = false;
        foreach (var c in pattern)
        {
            if (c is '\'' or '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c == 'h')
            {
                return true;
            }
            else if (!quoted && c == 'H')
            {
                return false;
            }
        }

        return false;
    }
}
