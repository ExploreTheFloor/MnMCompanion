using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MnMCompanion.Update;

namespace MnMCompanion.Tests;

/// <summary>Talking to the community service: sending a player's file, asking for figures, and every way it can go wrong coming back as a sentence, never an exception.</summary>
public class CommunityClientTests
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

    private static CommunityClient Client(FakeHandler handler) => new(new Version(0, 2, 0), handler);

    // ---------------------------------------------------------------- sending

    [Fact]
    public async Task A_file_is_posted_as_json_to_the_service_and_nothing_else_goes_with_it()
    {
        var handler = new FakeHandler(_ => Reply(202, "{\"status\":\"received\"}"));
        using var client = Client(handler);

        var result = await client.SendAsync("{\"schema\":1}");

        Assert.Equal(SendOutcome.Sent, result.Outcome);
        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "mnm-intake.mnm-companion.workers.dev", "/v1/contribution"), (request.Method, request.RequestUri!.Host, request.RequestUri.AbsolutePath));
        Assert.Equal("https", request.RequestUri.Scheme);
        Assert.Equal("{\"schema\":1}", handler.Bodies[0]);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("MnMCompanion/0.2.0", request.Headers.UserAgent.ToString());
        Assert.Empty(request.Headers.Where(h => h.Key is not "User-Agent"));   // no id, no cookie, no token: the file is all that is sent
    }

    [Fact]
    public async Task Too_soon_and_busy_say_when_to_try_again()
    {
        using var soon = Client(new FakeHandler(_ => Reply(429, "{\"error\":\"this id sent a file recently\"}", retryAfter: 7200)));
        var tooSoon = await soon.SendAsync("{}");
        Assert.Equal((SendOutcome.TooSoon, TimeSpan.FromHours(2)), (tooSoon.Outcome, tooSoon.RetryAfter));

        using var noHeader = Client(new FakeHandler(_ => Reply(429)));
        Assert.Equal(TimeSpan.FromHours(6), (await noHeader.SendAsync("{}")).RetryAfter);

        using var busy = Client(new FakeHandler(_ => Reply(503)));
        var busyResult = await busy.SendAsync("{}");
        Assert.Equal((SendOutcome.Busy, TimeSpan.FromHours(1)), (busyResult.Outcome, busyResult.RetryAfter));
    }

    [Fact]
    public async Task A_refusal_carries_the_services_reasons_and_never_asks_to_resend()
    {
        using var client = Client(new FakeHandler(_ => Reply(422, "{\"error\":\"the file is not valid\",\"problems\":[\"drops[0]: must have exactly mob\",\"totals.corpses: says 5\",\"a\",\"b\"]}")));

        var result = await client.SendAsync("{}");

        Assert.Equal(SendOutcome.Refused, result.Outcome);
        Assert.Contains("the file is not valid", result.Message);
        Assert.Contains("drops[0]: must have exactly mob", result.Message);
        Assert.DoesNotContain("\"b\"", result.Message);   // only the first few reasons
        Assert.Null(result.RetryAfter);
        foreach (var status in new[] { 400, 403, 413, 415 })
        {
            using var other = Client(new FakeHandler(_ => Reply(status)));
            Assert.Equal(SendOutcome.Refused, (await other.SendAsync("{}")).Outcome);
        }
    }

    [Fact]
    public async Task Every_other_trouble_is_a_failed_send_with_a_message()
    {
        foreach (var status in new[] { 500, 502, 404, 302 })
        {
            using var client = Client(new FakeHandler(_ => Reply(status)));
            var result = await client.SendAsync("{}");
            Assert.Equal(SendOutcome.Failed, result.Outcome);
            Assert.Contains(status.ToString(), result.Message);
        }
        using var unreachable = Client(new FakeHandler(_ => throw new HttpRequestException("no route to host")));
        Assert.Contains("no route to host", (await unreachable.SendAsync("{}")).Message);
        using var slow = Client(new FakeHandler(_ => throw new TaskCanceledException()));
        var timedOut = await slow.SendAsync("{}");
        Assert.Equal(SendOutcome.Failed, timedOut.Outcome);
        Assert.Contains("in time", timedOut.Message);
        using var garbage = Client(new FakeHandler(_ => Reply(500, "not json at all")));
        Assert.Equal(SendOutcome.Failed, (await garbage.SendAsync("{}")).Outcome);
    }

    // ---------------------------------------------------------------- asking

    [Fact]
    public async Task A_monsters_drop_rates_are_asked_for_by_name_with_the_players_id_and_read_back()
    {
        var handler = new FakeHandler(_ => Reply(200, """
            {"mob":"a restless skeleton","zones":[{"zone":"fallenpass","corpses":930,"purses":700,"copper":35000,"contributors":5,
              "items":[{"item":"Bone Chips","share":0.534,"unitsPerTake":1.07,"takers":5},{"item":"Wool Scraps","share":0.2,"unitsPerTake":1,"takers":3}]}]}
            """));
        using var client = Client(handler);

        var asked = await client.DropsAsync(Id, "a restless skeleton", "fallenpass");

        Assert.Equal(AskOutcome.Ok, asked.Outcome);
        var zone = Assert.Single(asked.Data!.Zones);
        Assert.Equal(("fallenpass", 930, 5), (zone.Zone, zone.Corpses, zone.Contributors));
        Assert.Equal(("Bone Chips", 0.534, 1.07, 5), (zone.Items[0].Item, zone.Items[0].Share, zone.Items[0].UnitsPerTake, zone.Items[0].Takers));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v1/community/drop?mob=a%20restless%20skeleton&zone=fallenpass", request.RequestUri!.PathAndQuery);
        Assert.Equal(Id, request.Headers.GetValues("X-Contributor-Id").Single());
    }

    [Fact]
    public async Task Prices_harvests_and_NPCs_read_back_too()
    {
        using var prices = Client(new FakeHandler(_ => Reply(200, "{\"item\":\"Rat Ear\",\"prices\":[{\"direction\":\"sold\",\"median\":4,\"low\":3,\"high\":6,\"stacks\":41,\"contributors\":4}]}")));
        var p = (await prices.PricesAsync(Id, "Rat Ear")).Data!.Prices.Single();
        Assert.Equal(("sold", 4L, 3L, 6L, 41, 4), (p.Direction, p.Median, p.Low, p.High, p.Stacks, p.Contributors));

        using var harvests = Client(new FakeHandler(_ => Reply(200, "{\"item\":\"Wood\",\"zones\":[{\"zone\":\"nightharborw\",\"perHarvest\":1.1,\"harvests\":300,\"contributors\":3}]}")));
        var h = (await harvests.HarvestsAsync(Id, "Wood")).Data!.Zones.Single();
        Assert.Equal(("nightharborw", 1.1, 300, 3), (h.Zone, h.PerHarvest, h.Harvests, h.Contributors));

        var handler = new FakeHandler(_ => Reply(200, "{\"npc\":\"Abbot Harrow\",\"alsoMonster\":true,\"lines\":[{\"text\":\"Well met, {player}.\",\"contributors\":3}]}"));
        using var npc = Client(handler);
        var n = (await npc.NpcAsync(Id, "Abbot Harrow")).Data!;
        Assert.Equal((true, "Well met, {player}.", 3), (n.AlsoMonster, n.Lines[0].Text, n.Lines[0].Contributors));
        Assert.Equal("/v1/community/npc?name=Abbot%20Harrow", handler.Requests[0].RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Asking_without_sharing_or_too_often_or_with_a_bad_id_or_name_comes_back_as_no_data()
    {
        using var refused = Client(new FakeHandler(_ => Reply(403, "{\"error\":\"share your own totals first\"}")));
        Assert.Equal(AskOutcome.NotAllowed, (await refused.DropsAsync(Id, "a rat")).Outcome);

        using var tooMany = Client(new FakeHandler(_ => Reply(429)));
        Assert.Equal(AskOutcome.TooMany, (await tooMany.PricesAsync(Id, "Rat Ear")).Outcome);

        var untouched = new FakeHandler(_ => Reply(200));
        using var local = Client(untouched);
        Assert.Equal(AskOutcome.Failed, (await local.DropsAsync("not-an-id", "a rat")).Outcome);
        Assert.Equal(AskOutcome.Failed, (await local.DropsAsync(Id, new string('x', 81))).Outcome);
        Assert.Equal(AskOutcome.Failed, (await local.DropsAsync(Id, "   ")).Outcome);
        Assert.Empty(untouched.Requests);   // a question that cannot be asked is not sent

        using var broken = Client(new FakeHandler(_ => Reply(200, "this is not json")));
        var asked = await broken.DropsAsync(Id, "a rat");
        Assert.Equal(AskOutcome.Failed, asked.Outcome);
        Assert.Null(asked.Data);

        using var empty = Client(new FakeHandler(_ => Reply(200, "null")));
        Assert.Equal(AskOutcome.Failed, (await empty.DropsAsync(Id, "a rat")).Outcome);

        using var down = Client(new FakeHandler(_ => throw new HttpRequestException("offline")));
        Assert.Contains("offline", (await down.NpcAsync(Id, "Abbot Harrow")).Message);
    }

    [Fact]
    public async Task An_oversized_answer_is_refused_before_it_is_read()
    {
        using var client = Client(new FakeHandler(_ => Reply(200, "{\"mob\":\"" + new string('x', 2_100_000) + "\",\"zones\":[]}")));

        var asked = await client.DropsAsync(Id, "a rat");

        Assert.Equal(AskOutcome.Failed, asked.Outcome);
        Assert.Contains("too large", asked.Message);
    }

    [Fact]
    public void The_client_talks_only_to_the_projects_own_service_and_follows_no_redirects()
    {
        Assert.Equal("https://mnm-intake.mnm-companion.workers.dev", CommunityService.BaseUrl);
        var handler = new HttpClientHandler();
        Assert.True(handler.AllowAutoRedirect);   // the system's default, which the real client switches off (see CommunityClient's constructor)
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MnMCompanion.Update", "CommunityClient.cs"));
        Assert.Contains("AllowAutoRedirect = false", source);
    }
}
