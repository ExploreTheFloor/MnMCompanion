using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MnMCompanion.Update;

namespace MnMCompanion.Tests;

/// <summary>Updating: finding a newer release, refusing anything that isn't GitHub's or doesn't match its checksum, and swapping safely.</summary>
public class UpdateTests
{
    private const string Download = "https://github.com/ExploreTheFloor/MnMCompanion/releases/download/";

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string> Agents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add(request.RequestUri!);
            Agents.Add(request.Headers.UserAgent.ToString());
            var response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private sealed class Reports : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }

    private static string Release(string tag = "v0.2.0", bool draft = false, bool prerelease = false, bool exe = true, bool sum = true, string prefix = Download)
    {
        var assets = new List<object>();
        if (exe) assets.Add(new { name = "MnMCompanion.exe", browser_download_url = prefix + tag + "/MnMCompanion.exe", size = 2000 });
        if (sum) assets.Add(new { name = "MnMCompanion.exe.sha256", browser_download_url = prefix + tag + "/MnMCompanion.exe.sha256", size = 90 });
        return JsonSerializer.Serialize(new
        {
            tag_name = tag, draft, prerelease, body = "What is new: it works.", html_url = "https://github.com/ExploreTheFloor/MnMCompanion/releases/tag/" + tag, assets,
        });
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static UpdateService Service(string current, HttpMessageHandler handler) => new(Version.Parse(current), handler) { MinimumBytes = 1000 };

    private static byte[] Program(int size = 2000, bool mz = true)
    {
        var bytes = new byte[size];
        new Random(7).NextBytes(bytes);
        if (mz) { bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; }
        return bytes;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static UpdateOffer Offer(string tag = "v0.2.0") =>
        new UpdateService(new Version(0, 1, 0), new FakeHandler(_ => Json("{}"))).Interpret(Release(tag)).Offer!;

    // ------------------------------------------------------------------ versions

    [Theory]
    [InlineData("v0.2.0", 0, 2, 0, 0)]
    [InlineData("0.2", 0, 2, 0, 0)]
    [InlineData("V1.10.3", 1, 10, 3, 0)]
    [InlineData("1.0.3-beta.1", 1, 0, 3, 0)]
    [InlineData("v2.0.0.4+abc", 2, 0, 0, 4)]
    public void Release_names_are_read_as_versions(string tag, int major, int minor, int build, int revision)
        => Assert.Equal(new Version(major, minor, build, revision), AppVersion.Parse(tag));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("1")]
    public void Names_that_are_not_versions_are_not_read_as_one(string tag) => Assert.Null(AppVersion.Parse(tag));

    [Fact]
    public void A_version_equal_in_every_part_is_equal_however_it_is_written()
        => Assert.Equal(AppVersion.Parse("0.1"), AppVersion.Parse("v0.1.0.0"));

    [Fact]
    public void The_running_programs_version_comes_from_its_assembly()
        => Assert.True(AppVersion.Current(typeof(UpdateService).Assembly) >= new Version(0, 0, 0, 0));

    // ------------------------------------------------------------------ checking

    [Fact]
    public async Task A_newer_release_with_its_file_and_checksum_is_offered()
    {
        var handler = new FakeHandler(_ => Json(Release("v0.2.0")));
        using var service = Service("0.1.0", handler);

        var result = await service.CheckAsync();

        Assert.Equal(CheckOutcome.Available, result.Outcome);
        var offer = result.Offer!;
        Assert.Equal(new Version(0, 2, 0, 0), offer.Version);
        Assert.Equal(Download + "v0.2.0/MnMCompanion.exe", offer.ExeUrl);
        Assert.Equal(Download + "v0.2.0/MnMCompanion.exe.sha256", offer.ChecksumUrl);
        Assert.Equal("What is new: it works.", offer.Notes);
        Assert.Equal("https://github.com/ExploreTheFloor/MnMCompanion/releases/tag/v0.2.0", offer.PageUrl);
        Assert.Equal(2000, offer.Size);
        Assert.Equal(["https://api.github.com/repos/ExploreTheFloor/MnMCompanion/releases/latest"], handler.Requests.Select(r => r.ToString()));
        Assert.Equal("MnMCompanion/0.1.0", handler.Agents.Single());   // GitHub insists on a name; nothing about the player is sent
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("0.1")]
    [InlineData("v0.0.9")]
    public async Task The_same_or_an_older_release_is_not_offered(string tag)
    {
        using var service = Service("0.1.0", new FakeHandler(_ => Json(Release(tag))));
        Assert.Equal(CheckOutcome.UpToDate, (await service.CheckAsync()).Outcome);
    }

    [Fact]
    public async Task Drafts_and_prereleases_are_never_offered()
    {
        using var draft = Service("0.1.0", new FakeHandler(_ => Json(Release("v9.0.0", draft: true))));
        using var pre = Service("0.1.0", new FakeHandler(_ => Json(Release("v9.0.0", prerelease: true))));
        Assert.Equal(CheckOutcome.UpToDate, (await draft.CheckAsync()).Outcome);
        Assert.Equal(CheckOutcome.UpToDate, (await pre.CheckAsync()).Outcome);
    }

    [Theory]
    [InlineData(404, CheckOutcome.NoReleaseInfo)]   // none yet, or the project page is private
    [InlineData(403, CheckOutcome.Failed)]          // GitHub limiting requests
    [InlineData(429, CheckOutcome.Failed)]
    [InlineData(500, CheckOutcome.Failed)]
    public async Task GitHubs_error_answers_are_told_apart_and_never_throw(int status, CheckOutcome expected)
    {
        using var service = Service("0.1.0", new FakeHandler(_ => Json("{}", (HttpStatusCode)status)));
        var result = await service.CheckAsync();
        Assert.Equal(expected, result.Outcome);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task No_network_is_a_failed_check_not_a_crash()
    {
        using var service = Service("0.1.0", new FakeHandler(_ => throw new HttpRequestException("no route to host")));
        var result = await service.CheckAsync();
        Assert.Equal(CheckOutcome.Failed, result.Outcome);
        Assert.Contains("no route to host", result.Message);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"tag_name\":\"latest\"}")]
    public async Task An_answer_that_makes_no_sense_is_a_failed_check(string body)
    {
        using var service = Service("0.1.0", new FakeHandler(_ => Json(body)));
        Assert.Equal(CheckOutcome.Failed, (await service.CheckAsync()).Outcome);
    }

    [Fact]
    public async Task A_release_without_its_checksum_or_program_is_not_offered()
    {
        using var noSum = Service("0.1.0", new FakeHandler(_ => Json(Release(sum: false))));
        using var noExe = Service("0.1.0", new FakeHandler(_ => Json(Release(exe: false))));
        Assert.Equal(CheckOutcome.NoReleaseInfo, (await noSum.CheckAsync()).Outcome);
        Assert.Equal(CheckOutcome.NoReleaseInfo, (await noExe.CheckAsync()).Outcome);
    }

    [Fact]
    public async Task Files_that_are_not_this_projects_own_release_files_are_not_offered()
    {
        foreach (var prefix in new[] { "https://evil.example/", "http://github.com/ExploreTheFloor/MnMCompanion/releases/download/", "https://github.com/someone-else/MnMCompanion/releases/download/" })
        {
            using var service = Service("0.1.0", new FakeHandler(_ => Json(Release(prefix: prefix))));
            Assert.Equal(CheckOutcome.NoReleaseInfo, (await service.CheckAsync()).Outcome);
        }
    }

    [Fact]
    public async Task A_release_page_somewhere_else_is_replaced_by_the_projects_own()
    {
        var json = Release().Replace("https://github.com/ExploreTheFloor/MnMCompanion/releases/tag/v0.2.0", "https://evil.example/page");
        using var service = Service("0.1.0", new FakeHandler(_ => Json(json)));
        Assert.Equal(UpdateService.ReleasesUrl, (await service.CheckAsync()).Offer!.PageUrl);
    }

    // ------------------------------------------------------------------ downloading

    private static FakeHandler Serving(byte[] program, string? checksum = null) => new(request =>
    {
        var url = request.RequestUri!.ToString();
        if (url.EndsWith(".sha256")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(checksum ?? Hex(program) + "  MnMCompanion.exe\n") };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(program) };
    });

    [Fact]
    public async Task A_download_that_matches_its_checksum_is_kept_and_only_github_is_asked()
    {
        using var scratch = new Fixture.Scratch();
        var program = Program();
        var handler = Serving(program);
        using var service = Service("0.1.0", handler);
        var progress = new Reports();

        var file = await service.DownloadAsync(Offer(), scratch.Path, progress);

        Assert.Equal(program, await File.ReadAllBytesAsync(file));
        Assert.EndsWith("MnMCompanion-0.2.0.exe", file);
        Assert.Equal(1.0, progress.Values[^1]);
        Assert.Empty(Directory.GetFiles(scratch.Path, "*.part"));
        Assert.All(handler.Requests, r => Assert.True(r.Scheme == "https" && r.Host is "github.com" or "api.github.com", r.ToString()));
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_checksum_is_thrown_away()
    {
        using var scratch = new Fixture.Scratch();
        using var service = Service("0.1.0", Serving(Program(), checksum: new string('a', 64)));

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(Offer(), scratch.Path));

        Assert.Contains("checksum", ex.Message);
        Assert.Empty(Directory.GetFiles(scratch.Path));
    }

    [Fact]
    public async Task Something_that_is_not_a_windows_program_or_is_too_small_is_thrown_away()
    {
        using var scratch = new Fixture.Scratch();
        var notExe = Program(mz: false);
        using var a = Service("0.1.0", Serving(notExe));
        await Assert.ThrowsAsync<UpdateException>(() => a.DownloadAsync(Offer(), scratch.Path));

        var tiny = Program(size: 100);
        using var b = Service("0.1.0", Serving(tiny));
        await Assert.ThrowsAsync<UpdateException>(() => b.DownloadAsync(Offer(), scratch.Path));

        Assert.Empty(Directory.GetFiles(scratch.Path));
    }

    [Fact]
    public async Task A_checksum_file_that_cannot_be_read_stops_the_update()
    {
        using var scratch = new Fixture.Scratch();
        using var service = Service("0.1.0", Serving(Program(), checksum: "this is not a checksum"));
        await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(Offer(), scratch.Path));
        Assert.Empty(Directory.GetFiles(scratch.Path));
    }

    [Fact]
    public async Task A_download_redirected_away_from_github_is_refused()
    {
        using var scratch = new Fixture.Scratch();
        var program = Program();
        var handler = new FakeHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith(".sha256")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Hex(program)) };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(program),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/MnMCompanion.exe"),   // where the redirects ended
            };
        });
        using var service = Service("0.1.0", handler);

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(Offer(), scratch.Path));

        Assert.Contains("GitHub", ex.Message);
        Assert.Empty(Directory.GetFiles(scratch.Path));
    }

    [Fact]
    public async Task GitHubs_own_storage_after_a_redirect_is_accepted()
    {
        using var scratch = new Fixture.Scratch();
        var program = Program();
        var handler = new FakeHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith(".sha256")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Hex(program)) };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(program),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://release-assets.githubusercontent.com/github-production-release-asset/1/x"),
            };
        });
        using var service = Service("0.1.0", handler);
        Assert.True(File.Exists(await service.DownloadAsync(Offer(), scratch.Path)));
    }

    [Fact]
    public async Task A_failed_request_is_reported_in_plain_words()
    {
        using var scratch = new Fixture.Scratch();
        using var service = Service("0.1.0", new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(Offer(), scratch.Path));
        Assert.Contains("404", ex.Message);
    }

    // ------------------------------------------------------------------ installing

    private static (string Exe, string Download) Installed(Fixture.Scratch scratch, string current = "old program", string next = "new program")
    {
        var exe = scratch.Write("app/MnMCompanion.exe", current);
        var download = scratch.Write("update/MnMCompanion-0.2.0.exe", next);
        return (exe, download);
    }

    [Fact]
    public void The_new_program_takes_the_old_ones_place_and_is_started()
    {
        using var scratch = new Fixture.Scratch();
        var (exe, download) = Installed(scratch);
        string? started = null;

        UpdateService.Apply(download, exe, path => { started = path; return true; });

        Assert.Equal("new program", File.ReadAllText(exe));
        Assert.Equal("old program", File.ReadAllText(exe + ".old"));
        Assert.False(File.Exists(download));
        Assert.Equal(exe, started);
    }

    [Fact]
    public void If_the_new_program_will_not_start_the_old_one_is_put_back()
    {
        using var scratch = new Fixture.Scratch();
        var (exe, download) = Installed(scratch);

        Assert.Throws<UpdateException>(() => UpdateService.Apply(download, exe, _ => false));
        Assert.Equal("old program", File.ReadAllText(exe));
        Assert.False(File.Exists(exe + ".old"));

        var (exe2, download2) = Installed(new Fixture.Scratch());
        Assert.Throws<UpdateException>(() => UpdateService.Apply(download2, exe2, _ => throw new InvalidOperationException("boom")));
        Assert.Equal("old program", File.ReadAllText(exe2));
    }

    [Fact]
    public void A_missing_download_changes_nothing()
    {
        using var scratch = new Fixture.Scratch();
        var (exe, download) = Installed(scratch);
        File.Delete(download);

        Assert.Throws<UpdateException>(() => UpdateService.Apply(download, exe, _ => true));

        Assert.Equal("old program", File.ReadAllText(exe));
        Assert.False(File.Exists(exe + ".old"));
    }

    [Fact]
    public void After_an_update_the_old_program_and_leftover_downloads_are_removed()
    {
        using var scratch = new Fixture.Scratch();
        var (exe, _) = Installed(scratch);
        File.WriteAllText(exe + ".old", "previous");

        UpdateService.CleanUp(exe, Path.Combine(scratch.Path, "update"));

        Assert.False(File.Exists(exe + ".old"));
        Assert.False(Directory.Exists(Path.Combine(scratch.Path, "update")));
        Assert.True(File.Exists(exe));
        UpdateService.CleanUp(null, Path.Combine(scratch.Path, "never-was"));   // nothing to clean is fine
    }

    [Fact]
    public void Only_a_real_program_in_a_writable_folder_can_be_replaced()
    {
        using var scratch = new Fixture.Scratch();
        Assert.True(UpdateService.CanReplace(scratch.Write("a/MnMCompanion.exe", "x")));
        Assert.False(UpdateService.CanReplace(scratch.Write("b/dotnet.exe", "x")));
        Assert.False(UpdateService.CanReplace(scratch.Write("c/MnMCompanion.dll", "x")));
        Assert.False(UpdateService.CanReplace(Path.Combine(scratch.Path, "nowhere", "MnMCompanion.exe")));
        Assert.False(UpdateService.CanReplace(null));
    }
}
