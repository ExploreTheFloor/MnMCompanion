using System.Net.Http.Headers;
using System.Text;

namespace MnMCompanion.Update;

/// <summary>Where the community wiki's tradeskill recipes are. The wiki is a fan wiki, not the game's own site.</summary>
public static class RecipeWiki
{
    public const string Origin = "https://monstersandmemories.wiki";

    /// <summary>The JSON the wiki's own tradeskill pages load: every skill, its recipe groups, recipes and ingredients.</summary>
    public const string DataUrl = Origin + "/api/tradeskills";

    /// <summary>The wiki's tradeskills page, for the player's own browser to open when they ask.</summary>
    public const string PageUrl = Origin + "/tradeskills";
}

/// <summary>
/// What asking the wiki for the recipes came to. <see cref="Json"/> is the wiki's list, set only for a new one; <see cref="NotModified"/> (with <see cref="Ok"/>)
/// is the wiki saying the list is the one we hold, with no list sent. <see cref="ETag"/> is the new list's version tag, to ask "has it changed?" with next time.
/// <see cref="RetryAfter"/> is how long the wiki asked to be left alone (or an hour when it was limiting requests and didn't say).
/// </summary>
public sealed record RecipeDownload(bool Ok, string Json, string Message, bool NotModified = false, string? ETag = null, TimeSpan? RetryAfter = null);

/// <summary>
/// Fetches the wiki's recipe list, and nothing else: one GET of one address, with Companion's name and version as the only identification, no cookies, no
/// redirects followed, compressed, and no more than a few megabytes taken. Given the version tag of the list it holds it asks "only if it changed", which the
/// wiki answers with nothing but a 304 when it hasn't. It never throws; every problem comes back as a sentence a person can read. When it is asked, and how
/// rarely, is RecipeRefresh's business (at most once a day by itself).
/// </summary>
public sealed class RecipeWikiClient : IDisposable
{
    private const int MaxBytes = 8_000_000;

    private readonly HttpClient _http;
    private readonly string _url;

    /// <param name="handler">Only for tests; the real thing uses the system's, without following redirects.</param>
    /// <param name="url">Only for tests.</param>
    public RecipeWikiClient(Version app, HttpMessageHandler? handler = null, string? url = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(45) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MnMCompanion", AppVersion.Show(app)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _url = url ?? RecipeWiki.DataUrl;
    }

    /// <param name="etag">The version tag of the list already held: the question becomes "only if it changed".</param>
    public async Task<RecipeDownload> FetchAsync(string? etag = null, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _url);
            if (!string.IsNullOrWhiteSpace(etag)) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            switch ((int)response.StatusCode)
            {
                case 304:
                    return new RecipeDownload(true, "", "", NotModified: true, ETag: etag);
                case 200:
                    var text = await ReadLimited(response, cancel).ConfigureAwait(false);
                    if (text is null) return new RecipeDownload(false, "", "The wiki's answer was far larger than expected, so it was ignored.");
                    // A cheap look before the real parse: it has to be the tradeskills document, not an error page that happens to say 200
                    var start = text.AsSpan().TrimStart();
                    return start.StartsWith("{") && text.Contains("\"tradeskills\"", StringComparison.Ordinal)
                        ? new RecipeDownload(true, text, "", ETag: response.Headers.ETag?.ToString())
                        : new RecipeDownload(false, "", "The wiki's answer wasn't the recipe list.");
                case 429 or 503:
                    var asked = response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero && delta < TimeSpan.FromDays(1) ? delta : (TimeSpan?)null;
                    var wait = asked is { } d ? $" Try again in about {Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))} minutes." : " Try again in a few minutes.";
                    return new RecipeDownload(false, "", "The wiki is limiting requests just now." + wait, RetryAfter: asked ?? TimeSpan.FromHours(1));
                default:
                    return new RecipeDownload(false, "", $"The wiki answered with an error ({(int)response.StatusCode}).");
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new RecipeDownload(false, "", "The wiki didn't answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return new RecipeDownload(false, "", "Couldn't reach the wiki (" + ex.Message + ").");
        }
    }

    /// <summary>The body as text, or null when it is longer than <see cref="MaxBytes"/>.</summary>
    private static async Task<string?> ReadLimited(HttpResponseMessage response, CancellationToken cancel)
    {
        if (response.Content.Headers.ContentLength > MaxBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16_384];
        int n;
        while ((n = await stream.ReadAsync(chunk, cancel).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + n > MaxBytes) return null;
            buffer.Write(chunk, 0, n);
        }
        return new UTF8Encoding(false).GetString(buffer.ToArray());
    }

    public void Dispose() => _http.Dispose();
}
