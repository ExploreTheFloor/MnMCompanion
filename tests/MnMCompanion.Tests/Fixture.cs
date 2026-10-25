using System.Text;
using System.Text.Json;
using MnMCompanion.Ledger;

namespace MnMCompanion.Tests;

/// <summary>
/// Builds ledger files the way the game writes them (a JSON document with a "c01" array of rows whose "f03" is a JSON string of
/// detail fields, most strings base64), from invented names. Times are local, so a row's time reads back as written.
/// </summary>
internal static class Fixture
{
    public static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    public static string Stamp(DateTime local) => new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToString("o");

    public static string Row(string act, string f02, object detail, DateTime when, string actor, string owner, string cls = "ftr", int level = 30, string zone = "testzone")
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["f01"] = act, ["f02"] = f02, ["f03"] = JsonSerializer.Serialize(detail), ["f04"] = Stamp(when), ["f05"] = "zone_" + B64(zone),
            ["f06"] = level, ["f07"] = actor, ["f08"] = 2, ["f09"] = owner, ["f10"] = cls,
        });

    /// <summary>An item looted (act_13).</summary>
    public static string Item(DateTime when, string looter, string owner, string mob, string item, string cls = "ftr", int level = 30)
        => Row("act_13", "ref_100", new { d01 = 1, d02 = B64(mob), d03 = "", d04 = item, d05 = "9a818e77c3484fe98efd9081" }, when, looter, owner, cls, level);

    /// <summary>A purse looted (act_14); <paramref name="coin"/> as "pp,gp,sp,cp", <paramref name="split"/> the owner's share the same way.</summary>
    public static string Purse(DateTime when, string looter, string owner, string mob, string coin, string split = "0,0,0,0", string cls = "ftr", int level = 30)
        => Row("act_14", "name_" + B64(mob), new { d11 = 1, d12 = B64(coin), d13 = B64(mob), d14 = B64("party_split"), d15 = B64(split) }, when, looter, owner, cls, level);

    /// <summary>An item sold to a vendor (act_24); <paramref name="coin"/> is the words the game writes for the whole stack.</summary>
    public static string Sale(DateTime when, string owner, string item, int count, string coin)
        => Row("act_24", item, new { d01 = count, d02 = "", d03 = B64(coin), d04 = item, d05 = "" }, when, owner, owner);

    public static string File(params string[] rows) => "{\"c01\":[" + string.Join(",", rows) + "]}";

    /// <summary>A scratch folder that is removed afterwards.</summary>
    public sealed class Scratch : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mnmcompanion-tests-" + Guid.NewGuid().ToString("N"));

        public Scratch() => Directory.CreateDirectory(Path);

        /// <summary>Writes a file under the folder (making the folders on the way).</summary>
        public string Write(string relative, string text)
        {
            var full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllText(full, text);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static List<LedgerRow> Parse(string json, LedgerFileKind kind = LedgerFileKind.Character) => LedgerParser.Parse(json, kind);
}
