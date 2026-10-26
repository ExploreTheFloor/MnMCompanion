using MnMCompanion.Community;
using MnMCompanion.Update;

namespace MnMCompanion;

/// <summary>
/// Asks the community service for a figure (a monster's drop rates, what vendors pay for an item) and remembers the answer for an hour, a
/// failed or empty one for five minutes, so opening the same monster twice asks once and a service that is down is not hammered. It asks
/// only if the player said Yes to sharing (<see cref="CommunityPolicy.MayUseCommunityData"/>), only by name (the service has no list), and
/// answers null whenever there is no figure: not sharing, no id yet, too few players behind it, or any trouble. Never throws.
/// </summary>
internal sealed class CommunityReader(Settings settings, HistoryData history, CommunityClient client)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1), Failed = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime At, object? Data)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether figures may be asked for at all: only for a player who shares their own.</summary>
    public bool Enabled => CommunityPolicy.MayUseCommunityData(settings.Sharing);

    public Task<CommunityDrops?> DropsAsync(string mob) => Cached("drop:" + mob, id => client.DropsAsync(id, mob));

    public Task<CommunityPrices?> PricesAsync(string item) => Cached("price:" + item, id => client.PricesAsync(id, item));

    public Task<CommunityHarvests?> HarvestsAsync(string item) => Cached("harvest:" + item, id => client.HarvestsAsync(id, item));

    public Task<CommunityNpc?> NpcAsync(string name) => Cached("npc:" + name, id => client.NpcAsync(id, name));

    private async Task<T?> Cached<T>(string key, Func<string, Task<Asked<T>>> ask) where T : class
    {
        if (!Enabled) return null;
        lock (_gate)
            if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < (hit.Data is null ? Failed : Fresh)) return hit.Data as T;

        var id = await Task.Run(history.ContributorId).ConfigureAwait(false);
        if (id is null) return null;
        var answer = await ask(id).ConfigureAwait(false);
        lock (_gate) _cache[key] = (DateTime.UtcNow, answer.Data);
        return answer.Data;
    }
}

/// <summary>The one reader of the running app (null in a picture-taking or headless run, which never ask).</summary>
internal static class CommunityAccess
{
    public static CommunityReader? Reader { get; set; }
}
