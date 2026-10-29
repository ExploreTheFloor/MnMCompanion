using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MnMCompanion.Recipes;
using MnMCompanion.Update;

namespace MnMCompanion.Tests;

/// <summary>Asking the wiki for the recipe list (one GET, every failure a sentence) and keeping the answer on disk.</summary>
public class RecipeWikiTests
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

    private static HttpResponseMessage Reply(int status, string body = "{}", int? retryAfter = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is { } seconds) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    private static RecipeWikiClient Client(FakeHandler handler) => new(new Version(0, 4, 5), handler);

    [Fact]
    public async Task The_list_is_one_get_of_the_wikis_tradeskills_and_only_our_name_goes_with_it()
    {
        var handler = new FakeHandler(_ => Reply(200, RecipeFixture.Json));
        using var client = Client(handler);

        var result = await client.FetchAsync();

        Assert.True(result.Ok);
        Assert.Equal(RecipeFixture.Json, result.Json);
        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Get, "https://monstersandmemories.wiki/api/tradeskills"), (request.Method, request.RequestUri!.AbsoluteUri));
        Assert.Equal("MnMCompanion/0.4.5", request.Headers.UserAgent.ToString());
        Assert.Empty(request.Headers.Where(h => h.Key is not ("User-Agent" or "Accept")));   // no id, no cookie, no token
    }

    [Fact]
    public async Task What_the_wiki_sends_can_be_read_as_the_recipe_book()
    {
        using var client = Client(new FakeHandler(_ => Reply(200, RecipeFixture.Json)));
        var result = await client.FetchAsync();
        Assert.NotNull(RecipeBook.TryParse(result.Json));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task A_busy_wiki_says_to_try_again_later(int status)
    {
        using var client = Client(new FakeHandler(_ => Reply(status, "slow down", retryAfter: 600)));
        var result = await client.FetchAsync();
        Assert.False(result.Ok);
        Assert.Contains("limiting requests", result.Message);
        Assert.Contains("10 minutes", result.Message);
    }

    [Fact]
    public async Task Other_errors_come_back_as_a_sentence_with_the_code()
    {
        using var client = Client(new FakeHandler(_ => Reply(500)));
        var result = await client.FetchAsync();
        Assert.False(result.Ok);
        Assert.Contains("500", result.Message);
    }

    [Theory]
    [InlineData("<html>maintenance</html>")]
    [InlineData("[]")]
    [InlineData("{\"other\":1}")]
    public async Task An_answer_that_is_not_the_recipe_list_is_not_taken(string body)
    {
        using var client = Client(new FakeHandler(_ => Reply(200, body)));
        var result = await client.FetchAsync();
        Assert.False(result.Ok);
        Assert.Equal("", result.Json);
    }

    [Fact]
    public async Task A_redirect_is_not_followed_and_is_just_an_error()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = Reply(302);
            response.Headers.Location = new Uri("https://example.com/elsewhere");
            return response;
        });
        using var client = Client(handler);
        var result = await client.FetchAsync();
        Assert.False(result.Ok);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_far_too_large_answer_is_ignored()
    {
        using var client = Client(new FakeHandler(_ => Reply(200, "{\"tradeskills\":\"" + new string('x', 9_000_000) + "\"}")));
        var result = await client.FetchAsync();
        Assert.False(result.Ok);
        Assert.Contains("larger than expected", result.Message);
    }

    [Fact]
    public async Task No_network_is_a_sentence_not_an_exception()
    {
        using var client = Client(new FakeHandler(_ => throw new HttpRequestException("no route")));
        var result = await client.FetchAsync();
        Assert.False(result.Ok);
        Assert.Contains("Couldn't reach the wiki", result.Message);
    }

    // ---------------------------------------------------------------- asking "only if it changed", and what the wiki asks of us

    [Fact]
    public async Task Without_a_version_tag_nothing_is_added_to_the_question()
    {
        var handler = new FakeHandler(_ => Reply(200, RecipeFixture.Json));
        using var client = Client(handler);
        await client.FetchAsync();
        Assert.False(Assert.Single(handler.Requests).Headers.Contains("If-None-Match"));
    }

    [Fact]
    public async Task With_a_version_tag_the_question_is_only_if_it_changed_and_an_unchanged_list_comes_back_empty()
    {
        var handler = new FakeHandler(r => r.Headers.Contains("If-None-Match") ? Reply(304, "") : Reply(200, RecipeFixture.Json));
        using var client = Client(handler);

        var result = await client.FetchAsync("W/\"abc123\"");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("W/\"abc123\"", request.Headers.IfNoneMatch.Single().ToString());
        Assert.True(result.Ok);
        Assert.True(result.NotModified);
        Assert.Equal("", result.Json);
        Assert.Equal("W/\"abc123\"", result.ETag);   // still the tag to ask with next time
    }

    [Fact]
    public async Task A_new_list_brings_its_version_tag_for_next_time()
    {
        using var client = Client(new FakeHandler(_ =>
        {
            var response = Reply(200, RecipeFixture.Json);
            response.Headers.ETag = new EntityTagHeaderValue("\"v2\"", isWeak: true);
            return response;
        }));
        var result = await client.FetchAsync("W/\"v1\"");
        Assert.True(result.Ok);
        Assert.False(result.NotModified);
        Assert.Equal("W/\"v2\"", result.ETag);
    }

    [Theory]
    [InlineData(429, 600, 10)]
    [InlineData(503, 120, 2)]
    [InlineData(429, null, 60)]   // no Retry-After: an hour
    public async Task When_the_wiki_asks_to_be_left_alone_the_answer_says_for_how_long(int status, int? seconds, int minutes)
    {
        using var client = Client(new FakeHandler(_ => Reply(status, "slow down", retryAfter: seconds)));
        var result = await client.FetchAsync();
        Assert.False(result.Ok);
        Assert.Equal(TimeSpan.FromMinutes(minutes), result.RetryAfter);
    }

    [Fact]
    public async Task An_ordinary_failure_does_not_make_the_wiki_wait()
    {
        using var client = Client(new FakeHandler(_ => Reply(500)));
        Assert.Null((await client.FetchAsync()).RetryAfter);
    }

    // ---------------------------------------------------------------- the copy kept on disk

    private static string Temp() => Path.Combine(Path.GetTempPath(), "mnm-recipes-" + Guid.NewGuid().ToString("N"), "recipes.json");

    [Fact]
    public void A_kept_list_loads_back_with_the_time_it_was_kept()
    {
        var path = Temp();
        try
        {
            Assert.Null(RecipeCache.Load(path));
            Assert.True(RecipeCache.Save(RecipeFixture.Json, path));
            var (book, fetched) = RecipeCache.Load(path)!.Value;
            Assert.Equal(10, book.Recipes.Count);
            Assert.True((DateTime.UtcNow - fetched).TotalMinutes < 2);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void A_damaged_kept_list_is_no_list()
    {
        var path = Temp();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ half a file");
            Assert.Null(RecipeCache.Load(path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void The_age_of_a_kept_list_is_said_in_days()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["today", "yesterday", "3 days ago"], new[] { now.AddHours(-3), now.AddDays(-1), now.AddDays(-3) }.Select(t => RecipeCache.Age(t, now)));
    }
}
