using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MnMCompanion.Update;

namespace MnMCompanion.Tests;

/// <summary>The suggestion box: what the words are tidied into, and what leaves the PC when Send is pressed (and nothing else).</summary>
public class SuggestionTests
{
    private const string Id = "a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancel));
            var response = respond(request);
            response.RequestMessage ??= request;
            return response;
        }
    }

    private static HttpResponseMessage Reply(int status, string json = "{}", int? retryAfter = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (retryAfter is { } seconds) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    [Fact]
    public void The_words_are_tidied_the_way_the_service_wants_them()
    {
        Assert.Equal("one\ntwo\tthree", Suggestion.Clean("  one\r\ntwo\tthree \u0000\u0007 \n\n"));
        Assert.Equal("", Suggestion.Clean(null));
        Assert.Equal("", Suggestion.Clean("  \r\n \u001b "));
        Assert.Equal(Suggestion.MaxChars, Suggestion.Clean(new string('x', Suggestion.MaxChars + 50)).Length);
        Assert.Equal("ünïcödé and emoji 🙂 stay", Suggestion.Clean("ünïcödé and emoji 🙂 stay"));
    }

    [Fact]
    public async Task Send_posts_the_words_the_version_and_the_id_to_the_service_and_nothing_else()
    {
        var handler = new FakeHandler(_ => Reply(202, "{\"status\":\"received\"}"));
        using var client = new CommunityClient(new Version(0, 4, 8), handler);

        var result = await client.SuggestAsync(Id, "  A page for NPCs, please.\r\nAnd a dark map.  ", new Version(0, 4, 8));

        Assert.Equal(SendOutcome.Sent, result.Outcome);
        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "mnm-intake.mnm-companion.workers.dev", "/v1/suggestion"), (request.Method, request.RequestUri!.Host, request.RequestUri.AbsolutePath));
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["app", "contributor", "text"], keys);
        Assert.Equal(Id, doc.RootElement.GetProperty("contributor").GetString());
        Assert.Equal("MnMCompanion 0.4.8", doc.RootElement.GetProperty("app").GetString());
        Assert.Equal("A page for NPCs, please.\nAnd a dark map.", doc.RootElement.GetProperty("text").GetString());
        Assert.False(request.Headers.Contains("X-Contributor-Id"));
    }

    [Fact]
    public async Task Nothing_is_sent_without_words_or_without_an_id()
    {
        var handler = new FakeHandler(_ => Reply(202));
        using var client = new CommunityClient(new Version(0, 4, 8), handler);
        Assert.Equal(SendOutcome.Refused, (await client.SuggestAsync(Id, "  \r\n ", new Version(0, 4, 8))).Outcome);
        Assert.Equal(SendOutcome.Failed, (await client.SuggestAsync("", "words", new Version(0, 4, 8))).Outcome);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(429, 45, SendOutcome.TooSoon, "try again in 45 s")]
    [InlineData(429, 86400, SendOutcome.TooSoon, "enough suggestions for one day")]
    [InlineData(503, 3600, SendOutcome.Busy, "full for today")]
    [InlineData(403, null, SendOutcome.Refused, "doesn't take suggestions from this copy")]
    [InlineData(422, null, SendOutcome.Refused, "refused it: text is empty")]
    [InlineData(500, null, SendOutcome.Failed, "error (500)")]
    public async Task Every_answer_comes_back_as_a_sentence(int status, int? retryAfter, SendOutcome outcome, string says)
    {
        var handler = new FakeHandler(_ => Reply(status, "{\"error\":\"the suggestion is not valid\",\"problems\":[\"text is empty\"]}", retryAfter));
        using var client = new CommunityClient(new Version(0, 4, 8), handler);
        var result = await client.SuggestAsync(Id, "words", new Version(0, 4, 8));
        Assert.Equal(outcome, result.Outcome);
        Assert.Contains(says, result.Message);
    }

    [Fact]
    public async Task No_network_is_a_sentence_too_never_an_exception()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("no route"));
        using var client = new CommunityClient(new Version(0, 4, 8), handler);
        var result = await client.SuggestAsync(Id, "words", new Version(0, 4, 8));
        Assert.Equal(SendOutcome.Failed, result.Outcome);
        Assert.Contains("Couldn't reach the service", result.Message);
    }
}
