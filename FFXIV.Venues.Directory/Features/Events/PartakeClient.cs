using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXIV.Venues.Directory.Features.Directory.Catalog;

namespace FFXIV.Venues.Directory.Features.Events;

internal sealed class PartakeException(string message) : Exception(message);

// Partake's public GraphQL API, the one its website uses. Requests go one at a time and a couple of seconds apart, because Partake throttles bursts, and stay small, since large responses can stall on some networks: lists are fetched without descriptions, and a description only for the event being viewed.
internal sealed class PartakeClient : IDisposable
{
    public const string ApiUrl = "https://api.partake.gg/";
    public const string WebsiteEventUrl = "https://www.partake.gg/events/";
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt;

    public PartakeClient(HttpClient httpClient) => _httpClient = httpClient;

    // Must match the limit in EventsQuery.
    private const int PageSize = 50;
    private const int MaxPages = 8;
    private const string EventsQuery =
        "query($offset:Int,$from:DateTime,$to:DateTime){ events(game:\"final-fantasy-xiv\", offset:$offset, limit:50, sortBy:STARTS_AT_ASC, endsBetween:{start:$from, end:$to}) { id title startsAt endsAt tags ageRating attendeeCount isRecurring " +
        "location locationData{ dataCenter{ name location } server{ name } } attachments team{ name iconUrl } } }";

    // FFXIV events that end between from and to (so events already running are included), soonest first, fetched in pages without descriptions.
    public async Task<IReadOnlyList<CommunityEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var events = new List<CommunityEvent>();
        for (var page = 0; page < MaxPages; page++)
        {
            var variables = new { offset = page * PageSize, from = from.UtcDateTime.ToString("O"), to = to.UtcDateTime.ToString("O") };
            var data = await QueryAsync(EventsQuery, variables, cancellationToken).ConfigureAwait(false);
            var count = 0;
            foreach (var node in data.GetProperty("events").EnumerateArray())
            {
                count++;
                if (ReadEvent(node) is { } communityEvent)
                {
                    events.Add(communityEvent);
                }
            }

            if (count < PageSize)
            {
                break;
            }
        }

        return events;
    }

    private static CommunityEvent? ReadEvent(JsonElement node)
    {
        if (!node.TryGetProperty("id", out var idNode) || !idNode.TryGetInt32(out var id))
        {
            return null;
        }

        var locationData = node.TryGetProperty("locationData", out var data) && data.ValueKind == JsonValueKind.Object ? data : default;
        var dataCenter = locationData.ValueKind == JsonValueKind.Object && locationData.TryGetProperty("dataCenter", out var dc) && dc.ValueKind == JsonValueKind.Object ? dc : default;
        var server = locationData.ValueKind == JsonValueKind.Object && locationData.TryGetProperty("server", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        var team = node.TryGetProperty("team", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
        var dataCenterName = String(dataCenter, "name");
        var worldName = String(server, "name");
        var locationText = String(node, "location") ?? string.Empty;
        string? banner = null;
        if (node.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var attachment in attachments.EnumerateArray())
            {
                if (attachment.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(attachment.GetString()))
                {
                    // Attachments are asset ids; the CDN serves them as .webp.
                    var asset = attachment.GetString()!;
                    banner = $"https://cdn.partake.gg/assets/{asset}{(asset.Contains('.') ? string.Empty : ".webp")}";
                    break;
                }
            }
        }

        var tags = new List<string>();
        if (node.TryGetProperty("tags", out var tagNodes) && tagNodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagNodes.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tag.GetString()))
                {
                    tags.Add(tag.GetString()!);
                }
            }
        }

        return new CommunityEvent(
            id,
            String(node, "title") ?? $"Event {id}",
            node.GetProperty("startsAt").GetDateTimeOffset(),
            node.GetProperty("endsAt").GetDateTimeOffset(),
            tags,
            String(node, "ageRating") ?? "EVERYONE",
            node.TryGetProperty("attendeeCount", out var attendees) && attendees.TryGetInt32(out var going) ? going : 0,
            node.TryGetProperty("isRecurring", out var recurring) && recurring.ValueKind == JsonValueKind.True,
            locationText,
            dataCenterName,
            RegionName(String(dataCenter, "location")) ?? VenueAddresses.ResolveRegion(dataCenterName),
            worldName,
            banner,
            String(team, "name"),
            String(team, "iconUrl"))
        {
            Address = PartakeLocationParser.Parse(locationText, dataCenterName, worldName),
        };
    }

    // Partake's data center locations, as the directory names regions; for a location not known here, the region comes from the data center's name.
    private static string? RegionName(string? location) => location switch
    {
        "us" or "na" => "North America",
        "eu" => "Europe",
        "oce" => "Oceania",
        "jp" => "Japan",
        _ => null,
    };

    private static string? String(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public async Task<PartakeEventDescription?> GetEventDescriptionAsync(int eventId, CancellationToken cancellationToken)
    {
        const string query = "query($id:Int!){ event(id:$id){ id title startsAt endsAt description(type: RAW) } }";
        var data = await QueryAsync(query, new { id = eventId }, cancellationToken).ConfigureAwait(false);
        if (!data.TryGetProperty("event", out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new PartakeEventDescription(
            node.GetProperty("id").GetInt32(),
            node.GetProperty("title").GetString() ?? string.Empty,
            node.GetProperty("startsAt").GetDateTimeOffset(),
            node.GetProperty("endsAt").GetDateTimeOffset(),
            node.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
                ? description.GetString()
                : null);
    }

    public void Dispose() => _gate.Dispose();

    private async Task<JsonElement> QueryAsync(string query, object variables, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _lastRequestAt + RequestSpacing - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            using var content = JsonContent.Create(new { query, variables });
            using var response = await _httpClient.PostAsync(ApiUrl, content, cancellationToken).ConfigureAwait(false);
            _lastRequestAt = DateTimeOffset.UtcNow;
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                throw new PartakeException(errors[0].TryGetProperty("message", out var message) ? message.GetString() ?? "GraphQL error" : "GraphQL error");
            }

            return root.GetProperty("data").Clone();
        }
        finally
        {
            _gate.Release();
        }
    }
}

internal sealed record PartakeEventDescription(int Id, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? RawDescription);
