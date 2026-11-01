using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MnMCompanion.Market;
using MnMCompanion.Update;

namespace MnMCompanion.Tests;

/// <summary>Asking mnm-db.com for the auction feed: one GET, only our name with it, and every failure a sentence.</summary>
public class MarketFeedClientTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add(request);
            var response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Reply(int status, string body = "{}", int? retryAfter = null, string? etag = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is { } seconds) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        if (etag is not null) response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    private static MarketFeedClient Client(FakeHandler handler) => new(new Version(0, 4, 5), handler);

    [Fact]
    public async Task The_feed_is_one_get_of_the_sites_auctions_file_and_only_our_name_goes_with_it()
    {
        var handler = new FakeHandler(_ => Reply(200, MarketFixture.Json, etag: "\"6ae63ae6-309944\""));
        using var client = Client(handler);

        var result = await client.FetchAsync();

        Assert.True(result.Ok);
        Assert.Equal(MarketFixture.Json, result.Json);
        Assert.Equal("\"6ae63ae6-309944\"", result.ETag);
        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Get, "https://mnm-db.com/auctions.json"), (request.Method, request.RequestUri!.AbsoluteUri));
        Assert.Equal("MnMCompanion/0.4.5", request.Headers.UserAgent.ToString());
        Assert.Empty(request.Headers.Where(h => h.Key is not ("User-Agent" or "Accept")));   // no id, no cookie, no token
        Assert.NotNull(MarketFeed.TryParse(result.Json));
    }

    [Fact]
    public async Task With_a_version_tag_the_question_is_only_if_it_changed_and_an_unchanged_feed_comes_back_as_nothing()
    {
        var handler = new FakeHandler(r => r.Headers.IfNoneMatch.ToString() == "\"abc\"" ? Reply(304, "") : Reply(200, MarketFixture.Json));
        using var client = Client(handler);

        var result = await client.FetchAsync("\"abc\"");

        Assert.True(result.Ok);
        Assert.True(result.NotModified);
        Assert.Equal("", result.Json);
        Assert.Equal("\"abc\"", result.ETag);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Being_limited_comes_back_with_how_long_to_wait(int status)
    {
        using var client = Client(new FakeHandler(_ => Reply(status, "", retryAfter: 600)));
        var result = await client.FetchAsync();
        Assert.False(result.Ok);
        Assert.Equal(TimeSpan.FromMinutes(10), result.RetryAfter);
        Assert.Contains("limiting requests", result.Message);
        Assert.Contains("10 minutes", result.Message);
    }

    [Fact]
    public async Task A_page_that_is_not_the_feed_is_refused()
    {
        using var client = Client(new FakeHandler(_ => Reply(200, "<html>moved</html>")));
        var result = await client.FetchAsync();
        Assert.False(result.Ok);
        Assert.Equal("The site's answer wasn't the auction feed.", result.Message);
    }

    [Fact]
    public async Task An_error_and_an_unreachable_site_are_sentences_not_exceptions()
    {
        using var error = Client(new FakeHandler(_ => Reply(500, "")));
        Assert.Equal("mnm-db.com answered with an error (500).", (await error.FetchAsync()).Message);

        using var down = Client(new FakeHandler(_ => throw new HttpRequestException("no route")));
        var result = await down.FetchAsync();
        Assert.False(result.Ok);
        Assert.StartsWith("Couldn't reach mnm-db.com", result.Message);
    }

    [Fact]
    public void The_sites_item_page_is_addressed_by_name()
    {
        Assert.Equal("https://mnm-db.com/#/auctions/Copper%20Ore", MarketSite.ItemUrl("Copper Ore"));
    }
}
