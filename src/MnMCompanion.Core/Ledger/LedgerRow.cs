using System.Globalization;
using System.Text.Json;

namespace MnMCompanion.Ledger;

/// <summary>Which of a character's ledger files a row came from.</summary>
public enum LedgerFileKind
{
    /// <summary><c>&lt;name&gt;_Character_&lt;date&gt;.json</c>: what this character did.</summary>
    Character,
    /// <summary><c>&lt;name&gt;_Social_&lt;date&gt;.json</c> and <c>Archive\*.json</c>: what the party around them did.</summary>
    Social,
}

/// <summary>
/// One row of a ledger: <c>f01</c> the act ("act_13" = an item looted), <c>f02</c> a name or reference, <c>f03</c> a JSON
/// string of detail fields (<c>d01</c>…), <c>f04</c> the time, <c>f05</c> the zone, <c>f06</c> the actor's level, <c>f07</c> the
/// actor, <c>f09</c> the owner of the file, <c>f10</c> the actor's class code. <see cref="Raw"/> holds every field as written
/// (detail fields included); <see cref="Text"/> decodes one.
/// </summary>
public sealed class LedgerRow
{
    public required string Act { get; init; }
    public required DateTime Time { get; init; }
    public string Name { get; init; } = "";
    public string Zone { get; init; } = "";
    public int Level { get; init; }
    public string Actor { get; init; } = "";
    public string Owner { get; init; } = "";
    public string Class { get; init; } = "";
    public LedgerFileKind File { get; init; }
    public required IReadOnlyDictionary<string, string> Raw { get; init; }

    /// <summary>The row as written, for telling two copies of one row apart from two rows.</summary>
    public required string Identity { get; init; }

    /// <summary>The whole row as compact JSON, every field as the game wrote it (nothing decoded, nothing left out).</summary>
    public string RawJson { get; init; } = "";

    /// <summary>
    /// The row's detail fields (<c>f03</c>) as a JSON object with the base64 strings decoded: <c>{"d01":1,"d02":"a small cellar rat",
    /// "d04":"Rat Tooth",…}</c>. Numbers and lists stay as they were. Empty when the row has no detail object.
    /// </summary>
    public string DetailJson { get; init; } = "";

    public string Get(string field) => Raw.TryGetValue(field, out var v) ? v : "";

    /// <summary>A detail field with base64 decoded ("d02" is a creature's name, "d12" the coin as digits).</summary>
    public string Text(string field) => LedgerText.Decode(Get(field));

    public int Int(string field) => int.TryParse(Get(field), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;

    /// <summary>The file's owner did this themselves.</summary>
    public bool ByOwner => Actor.Length > 0 && Actor.Equals(Owner, StringComparison.OrdinalIgnoreCase);
}

public static class LedgerParser
{
    /// <summary>
    /// The rows of one ledger file's text. The event list is whichever top-level array holds objects with an "f01" ("c01"
    /// today). Throws <see cref="JsonException"/> on text that isn't complete JSON: the game rewrites the file as it goes, so
    /// a read can catch it half written, and the caller tries again later.
    /// </summary>
    public static List<LedgerRow> Parse(string json, LedgerFileKind kind)
    {
        using var doc = JsonDocument.Parse(json);
        var rows = new List<LedgerRow>();
        foreach (var row in Elements(doc.RootElement))
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("f01", out _)) continue;
            var raw = new Dictionary<string, string>(StringComparer.Ordinal);
            string f01 = "", f02 = "", f03 = "", f04 = "", f07 = "", f09 = "";
            foreach (var p in row.EnumerateObject())
            {
                var text = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
                switch (p.Name)
                {
                    case "f01": f01 = text; break;
                    case "f02": f02 = text; break;
                    case "f03": f03 = text; break;
                    case "f04": f04 = text; break;
                    case "f07": f07 = text; break;
                    case "f09": f09 = text; break;
                }
                if (p.Name == "f03" && text.StartsWith('{'))
                {
                    try
                    {
                        using var inner = JsonDocument.Parse(text);
                        foreach (var q in inner.RootElement.EnumerateObject())
                            raw[q.Name] = q.Value.ValueKind == JsonValueKind.String ? q.Value.GetString() ?? "" : q.Value.GetRawText();
                    }
                    catch (JsonException) { raw[p.Name] = text; }
                }
                else raw[p.Name] = text;
            }
            if (!DateTimeOffset.TryParse(f04, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)) continue;
            int.TryParse(raw.GetValueOrDefault("f06"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level);
            var cls = raw.GetValueOrDefault("f10") ?? "";
            rows.Add(new LedgerRow
            {
                RawJson = JsonSerializer.Serialize(row),
                DetailJson = Detail(f03),
                Act = f01,
                Time = when.LocalDateTime,
                Name = LedgerText.Prefixed(f02),
                Zone = LedgerText.Prefixed(raw.GetValueOrDefault("f05") ?? ""),
                Level = level,
                Actor = f07,
                Owner = f09,
                Class = cls.Equals("None", StringComparison.OrdinalIgnoreCase) ? "" : cls,
                File = kind,
                Raw = raw,
                Identity = string.Join('\u0001', f01, f02, f03, f04, f07, f09),
            });
        }
        return rows;
    }

    /// <summary>
    /// The top-level fields of a file other than its row list (<c>c02</c>…<c>c06</c> today: two timestamps, the character's name and
    /// two numbers whose meaning isn't known), as written. Throws <see cref="JsonException"/> like <see cref="Parse"/>.
    /// </summary>
    public static Dictionary<string, string> Envelope(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return fields;
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind != JsonValueKind.Array)
                fields[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
        return fields;
    }

    /// <summary>A row's <c>f03</c> text as a JSON object with its base64 strings decoded, or "" when it isn't an object.</summary>
    private static string Detail(string f03)
    {
        if (!f03.StartsWith('{')) return "";
        try
        {
            using var inner = JsonDocument.Parse(f03);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var q in inner.RootElement.EnumerateObject())
                {
                    if (q.Value.ValueKind == JsonValueKind.String) writer.WriteString(q.Name, LedgerText.Decode(q.Value.GetString() ?? ""));
                    else { writer.WritePropertyName(q.Name); q.Value.WriteTo(writer); }
                }
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException) { return ""; }
    }

    private static IEnumerable<JsonElement> Elements(JsonElement root) => root.ValueKind switch
    {
        JsonValueKind.Array => root.EnumerateArray(),
        JsonValueKind.Object => root.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array).SelectMany(p => p.Value.EnumerateArray()),
        _ => [],
    };
}
