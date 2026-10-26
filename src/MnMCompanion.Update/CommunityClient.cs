using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MnMCompanion.Update;

/// <summary>Where the project's community service lives: a small service on Cloudflare that keeps the shared totals privately.</summary>
public static class CommunityService
{
    public const string BaseUrl = "https://mnm-intake.mnm-companion.workers.dev";
}

public enum SendOutcome
{
    /// <summary>The service took the file.</summary>
    Sent,
    /// <summary>This id sent a file recently (the service takes one every 6 hours); try again after <see cref="SendResult.RetryAfter"/>.</summary>
    TooSoon,
    /// <summary>The service is busy or limiting requests; try again later.</summary>
    Busy,
    /// <summary>The service refused the file (it did not pass its checks, or the id is not accepted). Resending the same file will not help.</summary>
    Refused,
    /// <summary>No network, a timeout, or an answer that made no sense.</summary>
    Failed,
}

public sealed record SendResult(SendOutcome Outcome, string Message, TimeSpan? RetryAfter = null);

public enum AskOutcome { Ok, NotAllowed, TooMany, Failed }

/// <summary>What asking the service for a figure came to. <see cref="Data"/> is set only for <see cref="AskOutcome.Ok"/>.</summary>
public sealed record Asked<T>(AskOutcome Outcome, T? Data, string Message) where T : class;

public sealed record CommunityDropItem(string Item, double Share, double UnitsPerTake, int Takers);
public sealed record CommunityDropZone(string Zone, int Corpses, int Purses, long Copper, int Contributors, IReadOnlyList<CommunityDropItem> Items);
public sealed record CommunityDrops(string Mob, IReadOnlyList<CommunityDropZone> Zones);

public sealed record CommunityPrice(string Direction, long Median, long Low, long High, int Stacks, int Contributors);
public sealed record CommunityPrices(string Item, IReadOnlyList<CommunityPrice> Prices);

public sealed record CommunityYield(string Zone, double PerHarvest, int Harvests, int Contributors);
public sealed record CommunityHarvests(string Item, IReadOnlyList<CommunityYield> Zones);

public sealed record CommunityNpcLine(string Text, int Contributors);
public sealed record CommunityNpc(string Npc, bool AlsoMonster, IReadOnlyList<CommunityNpcLine> Lines);

/// <summary>
/// Talks to the project's community service, and only to it: sends a player's totals (once they have said Yes to sharing) and asks for the
/// combined figures (drop rates, prices, harvest yields, what an NPC said), always with the player's own random contributor id. It follows no
/// redirects, sends nothing but the file or the id and a name, and never throws: every problem comes back as a result with a sentence a
/// person can read. The service answers an id only if that id has itself shared, and gives a figure only when several players stand behind it.
/// </summary>
public sealed class CommunityClient : IDisposable
{
    private const int MaxAnswerBytes = 2_000_000;
    private static readonly Regex Id = new("^[0-9a-f]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    /// <param name="handler">Only for tests; the real thing uses the system's, without following redirects.</param>
    /// <param name="baseUrl">Only for tests.</param>
    public CommunityClient(Version app, HttpMessageHandler? handler = null, string? baseUrl = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(baseUrl ?? CommunityService.BaseUrl), Timeout = TimeSpan.FromSeconds(60),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MnMCompanion", AppVersion.Show(app)));
    }

    // ------------------------------------------------------------------ sending

    /// <summary>Sends the player's file (the JSON Companion builds). Returns at once with the outcome; the caller decides when to try again.</summary>
    public async Task<SendResult> SendAsync(string json, CancellationToken cancel = default)
    {
        try
        {
            using var content = new StringContent(json, new UTF8Encoding(false), "application/json");
            using var response = await _http.PostAsync("/v1/contribution", content, cancel).ConfigureAwait(false);
            var (error, problems) = await ReadError(response, cancel).ConfigureAwait(false);
            var wait = RetryAfter(response);
            switch ((int)response.StatusCode)
            {
                case 202:
                    return new SendResult(SendOutcome.Sent, "Your totals were sent.");
                case 429:
                    return new SendResult(SendOutcome.TooSoon, "The service has your last totals already; the next can go a few hours later.", wait ?? TimeSpan.FromHours(6));
                case 503:
                    return new SendResult(SendOutcome.Busy, "The service is busy right now; Companion will try again later.", wait ?? TimeSpan.FromHours(1));
                case 400 or 403 or 413 or 415 or 422:
                    return new SendResult(SendOutcome.Refused, "The service refused the file" + (error.Length > 0 ? ": " + error : "") + (problems.Length > 0 ? " (" + problems + ")" : "") + ".");
                default:
                    return new SendResult(SendOutcome.Failed, $"The service answered with an error ({(int)response.StatusCode}).");
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new SendResult(SendOutcome.Failed, "The service didn't answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return new SendResult(SendOutcome.Failed, "Couldn't reach the service (" + ex.Message + ").");
        }
    }

    // ------------------------------------------------------------------ asking

    /// <summary>One monster's drop rates by zone, as the community's players have them.</summary>
    public Task<Asked<CommunityDrops>> DropsAsync(string contributor, string mob, string? zone = null, CancellationToken cancel = default) =>
        Ask<CommunityDrops>(contributor, "drop?mob=" + Uri.EscapeDataString(mob) + (zone is null ? "" : "&zone=" + Uri.EscapeDataString(zone)), mob, cancel);

    /// <summary>What vendors pay (sold) and charge (bought) for one of an item.</summary>
    public Task<Asked<CommunityPrices>> PricesAsync(string contributor, string item, CancellationToken cancel = default) =>
        Ask<CommunityPrices>(contributor, "price?item=" + Uri.EscapeDataString(item), item, cancel);

    /// <summary>What one harvest of an item yields, by zone.</summary>
    public Task<Asked<CommunityHarvests>> HarvestsAsync(string contributor, string item, CancellationToken cancel = default) =>
        Ask<CommunityHarvests>(contributor, "harvest?item=" + Uri.EscapeDataString(item), item, cancel);

    /// <summary>What an NPC said (lines enough players share, the player's own name shown as {player}) and whether the name is also a monster.</summary>
    public Task<Asked<CommunityNpc>> NpcAsync(string contributor, string name, CancellationToken cancel = default) =>
        Ask<CommunityNpc>(contributor, "npc?name=" + Uri.EscapeDataString(name), name, cancel);

    private async Task<Asked<T>> Ask<T>(string contributor, string query, string name, CancellationToken cancel) where T : class
    {
        if (!Id.IsMatch(contributor)) return new Asked<T>(AskOutcome.Failed, null, "There is no contributor id to ask with yet.");
        if (name.Trim().Length is 0 or > 80) return new Asked<T>(AskOutcome.Failed, null, "That name can't be asked for.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/community/" + query);
            request.Headers.Add("X-Contributor-Id", contributor);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            switch ((int)response.StatusCode)
            {
                case 200:
                    var text = await ReadLimited(response, MaxAnswerBytes, cancel).ConfigureAwait(false);
                    if (text is null) return new Asked<T>(AskOutcome.Failed, null, "The answer was too large.");
                    return JsonSerializer.Deserialize<T>(text, Json) is { } data
                        ? new Asked<T>(AskOutcome.Ok, data, "")
                        : new Asked<T>(AskOutcome.Failed, null, "The answer made no sense.");
                case 403:
                    return new Asked<T>(AskOutcome.NotAllowed, null, "Community figures are for players who share their own totals.");
                case 429:
                    return new Asked<T>(AskOutcome.TooMany, null, "Too many questions just now; try again in a minute.");
                default:
                    return new Asked<T>(AskOutcome.Failed, null, $"The service answered with an error ({(int)response.StatusCode}).");
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new Asked<T>(AskOutcome.Failed, null, "The service didn't answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return new Asked<T>(AskOutcome.Failed, null, "Couldn't reach the service (" + ex.Message + ").");
        }
        catch (JsonException)
        {
            return new Asked<T>(AskOutcome.Failed, null, "The answer made no sense.");
        }
    }

    // ------------------------------------------------------------------ reading answers

    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero && delta < TimeSpan.FromDays(2) ? delta : null;

    /// <summary>The "error" and a short list of "problems" a refusal carries (nothing of what was sent is echoed back by the service).</summary>
    private static async Task<(string Error, string Problems)> ReadError(HttpResponseMessage response, CancellationToken cancel)
    {
        if (response.IsSuccessStatusCode) return ("", "");
        try
        {
            var text = await ReadLimited(response, 64_000, cancel).ConfigureAwait(false);
            if (text is null) return ("", "");
            using var doc = JsonDocument.Parse(text);
            var error = doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
            var problems = doc.RootElement.TryGetProperty("problems", out var p) && p.ValueKind == JsonValueKind.Array
                ? string.Join("; ", p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).Take(3)) : "";
            return (error, problems);
        }
        catch (JsonException) { return ("", ""); }
    }

    /// <summary>The body as text, or null when it is longer than <paramref name="max"/> bytes.</summary>
    private static async Task<string?> ReadLimited(HttpResponseMessage response, int max, CancellationToken cancel)
    {
        if (response.Content.Headers.ContentLength > max) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16_384];
        int n;
        while ((n = await stream.ReadAsync(chunk, cancel).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + n > max) return null;
            buffer.Write(chunk, 0, n);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public void Dispose() => _http.Dispose();
}
