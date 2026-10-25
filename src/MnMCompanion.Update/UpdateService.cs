using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MnMCompanion.Update;

/// <summary>A newer release that can be installed: where its files are and what it says about itself.</summary>
public sealed record UpdateOffer(Version Version, string Tag, string Notes, string PageUrl, string ExeUrl, string ChecksumUrl, long Size);

public enum CheckOutcome
{
    /// <summary>The newest release is the one running (or older).</summary>
    UpToDate,
    /// <summary>A newer release with an installable file and its checksum.</summary>
    Available,
    /// <summary>GitHub has no release to compare with (none yet, or the project page is private), or the newest has no usable file.</summary>
    NoReleaseInfo,
    /// <summary>Couldn't find out: no network, a timeout, GitHub limiting requests, an answer that made no sense.</summary>
    Failed,
}

public sealed record CheckResult(CheckOutcome Outcome, string Message, UpdateOffer? Offer = null);

/// <summary>An update that was refused or failed, with a sentence a person can read.</summary>
public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

public static class AppVersion
{
    private static readonly Regex Number = new(@"^\s*v?(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>A version with all four parts set, so "0.1.0" and "0.1.0.0" compare as the same.</summary>
    public static Version Normalize(Version v) => new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>"v0.2.0", "0.2", "1.0.3-beta.1" → a four-part version (anything after the numbers is ignored); null if it isn't one.</summary>
    public static Version? Parse(string? text)
    {
        if (text is null || Number.Match(text) is not { Success: true } m) return null;
        int Part(int i) => m.Groups[i].Success && int.TryParse(m.Groups[i].Value, out var n) ? n : 0;
        return new Version(Part(1), Part(2), Part(3), Part(4));
    }

    /// <summary>The running program's version, from the number set in the project file.</summary>
    public static Version Current(Assembly? assembly = null) =>
        Parse((assembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new Version(0, 0, 0, 0);

    public static string Show(Version v) => v.Revision > 0 ? v.ToString(4) : v.ToString(3);
}

/// <summary>
/// Asks GitHub whether a newer Companion has been released, and downloads it when the player agrees. It talks to two places only
/// (<c>api.github.com</c> for the release list, <c>github.com</c> for the files, which GitHub then serves from its own storage), sends
/// nothing about the player or their ledgers (a plain GET with the program's name and version), never installs by itself, and refuses
/// any download whose SHA-256 doesn't match the checksum published beside it.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string Owner = "ExploreTheFloor", Repo = "MnMCompanion", ExeName = "MnMCompanion.exe", ChecksumName = "MnMCompanion.exe.sha256";

    public static string ProjectUrl => $"https://github.com/{Owner}/{Repo}";
    public static string ReleasesUrl => $"{ProjectUrl}/releases/latest";
    public static string LatestUrl => $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private static string DownloadPrefix => $"{ProjectUrl}/releases/download/";

    /// <summary>The program must be at least this big and start with the "MZ" of a Windows program, or it is not installed.</summary>
    public long MinimumBytes { get; init; } = 1_000_000;

    private readonly HttpClient _http;

    public Version Current { get; }

    /// <param name="handler">Only for tests; the real thing uses the system's.</param>
    public UpdateService(Version current, HttpMessageHandler? handler = null)
    {
        Current = AppVersion.Normalize(current);
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MnMCompanion", AppVersion.Show(current)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    // ------------------------------------------------------------------ checking

    public async Task<CheckResult> CheckAsync(CancellationToken cancel = default)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await _http.GetAsync(LatestUrl, limit.Token).ConfigureAwait(false);
            switch ((int)response.StatusCode)
            {
                case 404: return new CheckResult(CheckOutcome.NoReleaseInfo, "No release was found on GitHub.");
                case 403 or 429: return new CheckResult(CheckOutcome.Failed, "GitHub is limiting requests right now. Try again later.");
                case >= 400: return new CheckResult(CheckOutcome.Failed, $"GitHub answered with an error ({(int)response.StatusCode}).");
            }
            return Interpret(await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new CheckResult(CheckOutcome.Failed, "GitHub didn't answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return new CheckResult(CheckOutcome.Failed, "Couldn't reach GitHub (" + ex.Message + ").");
        }
        catch (JsonException)
        {
            return new CheckResult(CheckOutcome.Failed, "GitHub's answer wasn't something this version understands.");
        }
    }

    /// <summary>Reads a GitHub "latest release" answer.</summary>
    public CheckResult Interpret(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("not an object");
        string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        var tag = Text(root, "tag_name");
        if (Flag("draft") || Flag("prerelease")) return new CheckResult(CheckOutcome.UpToDate, "You have the newest version.");
        if (AppVersion.Parse(tag) is not { } version) return new CheckResult(CheckOutcome.Failed, $"The newest release's name ({tag}) isn't a version number.");
        if (version <= Current) return new CheckResult(CheckOutcome.UpToDate, "You have the newest version.");

        string exe = "", sum = "";
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Text(asset, "name");
                var url = Text(asset, "browser_download_url");
                if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;   // only this project's own release files
                if (name == ExeName)
                {
                    exe = url;
                    size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                }
                else if (name == ChecksumName) sum = url;
            }
        if (exe.Length == 0 || sum.Length == 0)
            return new CheckResult(CheckOutcome.NoReleaseInfo, $"Version {AppVersion.Show(version)} is out, but its program file or checksum isn't attached to the release.");

        var page = Text(root, "html_url");
        return new CheckResult(CheckOutcome.Available, $"Version {AppVersion.Show(version)} is available.",
            new UpdateOffer(version, tag, Text(root, "body"), page.StartsWith(ProjectUrl, StringComparison.Ordinal) ? page : ReleasesUrl, exe, sum, size));
    }

    // ------------------------------------------------------------------ downloading

    /// <summary>
    /// Downloads the offered program into <paramref name="folder"/> and checks it against the published SHA-256. Returns the path of
    /// the checked file; on any problem nothing is left behind and an <see cref="UpdateException"/> says why in plain words.
    /// <paramref name="progress"/> gets 0..1.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateOffer offer, string folder, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(folder);
        var part = Path.Combine(folder, $"MnMCompanion-{AppVersion.Show(offer.Version)}.exe.part");
        var done = Path.Combine(folder, $"MnMCompanion-{AppVersion.Show(offer.Version)}.exe");
        try
        {
            var expected = await FetchChecksum(offer.ChecksumUrl, cancel).ConfigureAwait(false);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(TimeSpan.FromMinutes(10));
            using var response = await _http.GetAsync(offer.ExeUrl, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new UpdateException($"The download failed (GitHub answered {(int)response.StatusCode}).");
            RequireGitHub(response);

            long total = response.Content.Headers.ContentLength ?? offer.Size, read = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var head = new byte[2];
            int headLength = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false))
            await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await source.ReadAsync(buffer, limit.Token).ConfigureAwait(false)) > 0)
                {
                    for (int i = 0; i < n && headLength < 2; i++) head[headLength++] = buffer[i];
                    hash.AppendData(buffer, 0, n);
                    await target.WriteAsync(buffer.AsMemory(0, n), limit.Token).ConfigureAwait(false);
                    read += n;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)read / total));
                }
            }
            if (read < MinimumBytes || headLength < 2 || head[0] != (byte)'M' || head[1] != (byte)'Z')
                throw new UpdateException("What was downloaded isn't a program file, so it was thrown away.");
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("The download doesn't match its checksum (it may be damaged or tampered with), so it was thrown away.");

            File.Move(part, done, overwrite: true);
            progress?.Report(1.0);
            return done;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new UpdateException("The download took too long.");
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("The download failed (" + ex.Message + ").", ex);
        }
        catch (IOException ex)
        {
            throw new UpdateException("The download couldn't be saved (" + ex.Message + ").", ex);
        }
        finally
        {
            TryDelete(part);
        }
    }

    private async Task<string> FetchChecksum(string url, CancellationToken cancel)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _http.GetAsync(url, limit.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new UpdateException($"The checksum couldn't be fetched (GitHub answered {(int)response.StatusCode}).");
        RequireGitHub(response);
        var text = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
        if (text.Length > 4096 || Regex.Match(text, @"\b[0-9a-fA-F]{64}\b") is not { Success: true } m)
            throw new UpdateException("The release's checksum file isn't readable, so the download can't be checked.");
        return m.Value;
    }

    /// <summary>Whatever the redirects led to must still be GitHub's.</summary>
    private static void RequireGitHub(HttpResponseMessage response)
    {
        var uri = response.RequestMessage?.RequestUri;
        if (uri is null) return;
        var host = uri.Host.ToLowerInvariant();
        if (uri.Scheme != Uri.UriSchemeHttps || !(host == "github.com" || host.EndsWith(".github.com") || host.EndsWith(".githubusercontent.com")))
            throw new UpdateException("The download was sent somewhere other than GitHub, so it was refused.");
    }

    // ------------------------------------------------------------------ installing

    /// <summary>
    /// Whether the running program can be replaced: a real file, not the .NET host that runs a development build, in a folder the
    /// player can write to.
    /// </summary>
    public static bool CanReplace(string? exe)
    {
        try
        {
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            if (Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;
            var probe = Path.Combine(Path.GetDirectoryName(exe)!, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Puts the checked download in place of the running program (which Windows lets be renamed while it runs: the old one becomes
    /// <c>.old</c>) and starts the new one with <paramref name="launch"/>. If that fails the old program is put back.
    /// </summary>
    public static void Apply(string downloaded, string currentExe, Func<string, bool> launch)
    {
        var old = currentExe + ".old";
        TryDelete(old);
        try
        {
            File.Move(currentExe, old);
            File.Move(downloaded, currentExe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Restore(old, currentExe);
            throw new UpdateException("The new version couldn't be put in place (" + ex.Message + "). Nothing was changed.", ex);
        }
        bool started;
        try { started = launch(currentExe); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Restore(old, currentExe);
            throw new UpdateException("The new version wouldn't start (" + ex.Message + "). The old one was put back.", ex);
        }
        if (!started)
        {
            Restore(old, currentExe);
            throw new UpdateException("The new version wouldn't start. The old one was put back.");
        }
    }

    /// <summary>After an update: removes the previous program and any leftover downloads.</summary>
    public static void CleanUp(string? currentExe, string downloadFolder)
    {
        if (!string.IsNullOrEmpty(currentExe)) TryDelete(currentExe + ".old");
        try
        {
            if (Directory.Exists(downloadFolder)) Directory.Delete(downloadFolder, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void Restore(string old, string current)
    {
        try
        {
            if (!File.Exists(old)) return;
            TryDelete(current);
            File.Move(old, current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => _http.Dispose();
}
