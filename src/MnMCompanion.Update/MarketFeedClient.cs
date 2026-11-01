using System.Net.Http.Headers;
using System.Text;

namespace MnMCompanion.Update;

/// <summary>Where mnm-db.com's auction feed is. The site is a fan-made economy database, not the game's own; it reads the prices off the LiveMNM stream.</summary>
public static class MarketSite
{
    public const string Origin = "https://mnm-db.com";

    /// <summary>The JSON the site's own Auctions pages load: every current listing, the gear requests, item cards and stack sizes.</summary>
    public const string FeedUrl = Origin + "/auctions.json";

    /// <summary>The site's market page, for the player's own browser to open when they ask.</summary>
    public const string PageUrl = Origin + "/#/auctions";

    /// <summary>The site's listings for one item.</summary>
    public static string ItemUrl(string item) => Origin + "/#/auctions/" + Uri.EscapeDataString(item);
}

/// <summary>
/// What asking the site for the feed came to. <see cref="Json"/> is the feed, set only for a new one; <see cref="NotModified"/> (with <see cref="Ok"/>)
/// is the site saying the feed is the one we hold, with nothing sent. <see cref="ETag"/> is the new feed's version tag, to ask "has it changed?" with
/// next time. <see cref="RetryAfter"/> is how long the site asked to be left alone (or an hour when it was limiting requests and didn't say).
/// </summary>
public sealed record MarketDownload(bool Ok, string Json, string Message, bool NotModified = false, string? ETag = null, TimeSpan? RetryAfter = null);

/// <summary>
/// Fetches mnm-db.com's auction feed, and nothing else: one GET of one address, with Companion's name and version as the only identification, no
/// cookies, no redirects followed, compressed, and no more than a few tens of megabytes taken. Given the version tag of the feed it holds it asks
/// "only if it changed", which the site answers with nothing but a 304 when it hasn't. It never throws; every problem comes back as a sentence a
/// person can read. When it is asked, and how rarely, is MarketRefresh's business (once per six-hourly publish).
/// </summary>
public sealed class MarketFeedClient : IDisposable
{
    private const int MaxBytes = 40_000_000;

    private readonly HttpClient _http;
    private readonly string _url;

    /// <param name="handler">Only for tests; the real thing uses the system's, without following redirects.</param>
    /// <param name="url">Only for tests.</param>
    public MarketFeedClient(Version app, HttpMessageHandler? handler = null, string? url = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MnMCompanion", AppVersion.Show(app)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _url = url ?? MarketSite.FeedUrl;
    }

    /// <param name="etag">The version tag of the feed already held: the question becomes "only if it changed".</param>
    public async Task<MarketDownload> FetchAsync(string? etag = null, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _url);
            if (!string.IsNullOrWhiteSpace(etag)) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            switch ((int)response.StatusCode)
            {
                case 304:
                    return new MarketDownload(true, "", "", NotModified: true, ETag: etag);
                case 200:
                    var text = await ReadLimited(response, cancel).ConfigureAwait(false);
                    if (text is null) return new MarketDownload(false, "", "The site's answer was far larger than expected, so it was ignored.");
                    // A cheap look before the real parse: it has to be the feed, not an error page that happens to say 200
                    var start = text.AsSpan().TrimStart();
                    return start.StartsWith("{") && text.Contains("\"listings\"", StringComparison.Ordinal)
                        ? new MarketDownload(true, text, "", ETag: response.Headers.ETag?.ToString())
                        : new MarketDownload(false, "", "The site's answer wasn't the auction feed.");
                case 429 or 503:
                    var asked = response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero && delta < TimeSpan.FromDays(1) ? delta : (TimeSpan?)null;
                    var wait = asked is { } d ? $" Try again in about {Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))} minutes." : " Try again in a few minutes.";
                    return new MarketDownload(false, "", "mnm-db.com is limiting requests just now." + wait, RetryAfter: asked ?? TimeSpan.FromHours(1));
                default:
                    return new MarketDownload(false, "", $"mnm-db.com answered with an error ({(int)response.StatusCode}).");
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new MarketDownload(false, "", "mnm-db.com didn't answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return new MarketDownload(false, "", "Couldn't reach mnm-db.com (" + ex.Message + ").");
        }
    }

    /// <summary>The body as text, or null when it is longer than <see cref="MaxBytes"/>.</summary>
    private static async Task<string?> ReadLimited(HttpResponseMessage response, CancellationToken cancel)
    {
        if (response.Content.Headers.ContentLength > MaxBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[65_536];
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
