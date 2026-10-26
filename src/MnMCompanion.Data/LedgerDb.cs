using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MnMCompanion.Ledger;
using Microsoft.Data.Sqlite;

namespace MnMCompanion.Data;

/// <summary>Which ledger file a batch of rows came from: <c>&lt;server&gt;\&lt;character&gt;\Ledger\&lt;file&gt;</c>, <paramref name="File"/> relative to the Ledger folder.</summary>
public sealed record LedgerFileKey(string Server, string Character, string File);

/// <summary>
/// This app's own database (<c>%LOCALAPPDATA%\MnMCompanion\ledger.db</c>): every row of every ledger file it has read, kept for good.
/// The game moves old rows out of its files (the Social file's oldest 500 go to an archive folder from time to time) and
/// may remove files, so the database is the only complete copy. Nothing is ever deleted or changed in it, and a row read again
/// (the next refresh, the archive repeating a dated file's row) is ignored. Rows are kept exactly as written
/// (<c>raw</c>) beside a decoded copy of their detail fields (<c>detail</c>) and the few columns worth an index, so any later
/// question (drop rates, harvests, net coin, party history, who looted what) can be asked of the table with SQL.
/// Thread-safe.
/// </summary>
public sealed class LedgerDb : IDisposable
{
    public const int SchemaVersion = 1;

    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    public static string DefaultPath => Path.Combine(AppFolder.Folder, "ledger.db");

    /// <summary>
    /// The folder for a run pointed at some other game folder on the command line (a test, a picture, a borrowed copy): its own saved copy and
    /// sessions, so an invented or borrowed ledger never ends up in the player's own database. The same folder always gets the same one.
    /// </summary>
    public static string OtherFolder(string ledgerRoot)
    {
        var full = Path.GetFullPath(ledgerRoot).TrimEnd('\\', '/').ToLowerInvariant();
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12].ToLowerInvariant();
        return Path.Combine(AppFolder.Folder, "other-ledgers", id);
    }

    public string FilePath { get; }

    public LedgerDb(string? path = null)
    {
        FilePath = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FilePath, Pooling = false, DefaultTimeout = 30 }.ToString());
        _db.Open();
        Exec("PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 15000;");
        Exec(Schema);
        SeedActCodes();
        Exec($"INSERT OR IGNORE INTO meta (key, value) VALUES ('schema_version', '{SchemaVersion}'), ('created_utc', '{Iso(DateTime.UtcNow)}');");
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID;

        -- One line per ledger file read: what was imported and when, so an unchanged file is not read again.
        -- envelope = the file's own top-level fields beside its row list (c02..c06), as JSON.
        CREATE TABLE IF NOT EXISTS ledger_file (
            server       TEXT NOT NULL,
            character    TEXT NOT NULL,
            file         TEXT NOT NULL,
            kind         TEXT NOT NULL,
            archived     INTEGER NOT NULL,
            size         INTEGER NOT NULL,
            written_utc  TEXT NOT NULL,
            rows         INTEGER NOT NULL,
            envelope     TEXT NOT NULL,
            imported_utc TEXT NOT NULL,
            PRIMARY KEY (server, character, file)) WITHOUT ROWID;

        -- One line per ledger row. character = the folder (whose ledger it is), kind = 'Character' (their own actions) or 'Social'
        -- (the party and players around them, their own rows included), act = f01, name = f02 decoded, zone = f05 decoded,
        -- level = f06, actor = f07, owner = f09, class = f10, ledger_type = f08, at_utc = f04 in UTC.
        -- detail = f03 as JSON with the base64 strings decoded; raw = the row exactly as written. uid makes a row read twice one row.
        CREATE TABLE IF NOT EXISTS ledger_row (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            uid          TEXT NOT NULL UNIQUE,
            server       TEXT NOT NULL,
            character    TEXT NOT NULL,
            kind         TEXT NOT NULL,
            file         TEXT NOT NULL,
            act          TEXT NOT NULL,
            at_utc       TEXT NOT NULL,
            name         TEXT NOT NULL,
            zone         TEXT NOT NULL,
            level        INTEGER NOT NULL,
            actor        TEXT NOT NULL,
            owner        TEXT NOT NULL,
            class        TEXT NOT NULL,
            ledger_type  INTEGER,
            detail       TEXT NOT NULL,
            raw          TEXT NOT NULL,
            imported_utc TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_ledger_row_act ON ledger_row (act, at_utc);
        CREATE INDEX IF NOT EXISTS ix_ledger_row_who ON ledger_row (server, character, at_utc);
        CREATE INDEX IF NOT EXISTS ix_ledger_row_name ON ledger_row (name);

        -- What each act code means and how sure that is (see ActCodes).
        CREATE TABLE IF NOT EXISTS act_code (act TEXT PRIMARY KEY, meaning TEXT NOT NULL, certainty TEXT NOT NULL, notes TEXT NOT NULL) WITHOUT ROWID;

        -- The event tables below are worked out from ledger_row by EventBuilder and can be dropped and built again at any time
        -- (EventBuilder.Version). Each is keyed by the ledger_row it came from; only a character's own (Character file) rows feed them.
        CREATE TABLE IF NOT EXISTS vendor_trade (
            row_id INTEGER PRIMARY KEY, server TEXT NOT NULL, character TEXT NOT NULL, at_utc TEXT NOT NULL,
            direction TEXT NOT NULL,            -- 'sold' (act_24) or 'bought' (act_28)
            item TEXT NOT NULL, qty INTEGER NOT NULL, total_copper INTEGER NOT NULL,   -- the coin for the whole stack
            zone TEXT NOT NULL, level INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_vendor_trade_who ON vendor_trade (server, character, at_utc);
        CREATE INDEX IF NOT EXISTS ix_vendor_trade_item ON vendor_trade (item, direction);

        CREATE TABLE IF NOT EXISTS harvest (
            row_id INTEGER PRIMARY KEY, server TEXT NOT NULL, character TEXT NOT NULL, at_utc TEXT NOT NULL,
            item TEXT NOT NULL, qty INTEGER NOT NULL, zone TEXT NOT NULL, level INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_harvest_who ON harvest (server, character, at_utc);

        CREATE TABLE IF NOT EXISTS item_move (
            row_id INTEGER PRIMARY KEY, server TEXT NOT NULL, character TEXT NOT NULL, at_utc TEXT NOT NULL,
            kind TEXT NOT NULL,                 -- 'dropped' (act_11), 'picked_up' (act_12), 'destroyed' (act_15)
            item TEXT NOT NULL, qty INTEGER NOT NULL, zone TEXT NOT NULL, level INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_item_move_who ON item_move (server, character, at_utc);

        CREATE TABLE IF NOT EXISTS trade (
            row_id INTEGER PRIMARY KEY, server TEXT NOT NULL, character TEXT NOT NULL, at_utc TEXT NOT NULL,
            direction TEXT NOT NULL,            -- 'gave' (act_16: what you handed over) or 'received' (act_20)
            partner TEXT NOT NULL,              -- an id the game uses ('ref_71358'), never a name
            coin_copper INTEGER NOT NULL, item_count INTEGER NOT NULL, summary TEXT NOT NULL, zone TEXT NOT NULL, level INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_trade_who ON trade (server, character, at_utc);
        CREATE TABLE IF NOT EXISTS trade_item (
            trade_row_id INTEGER NOT NULL, line INTEGER NOT NULL, name TEXT NOT NULL, qty INTEGER NOT NULL, item_id TEXT NOT NULL,
            PRIMARY KEY (trade_row_id, line)) WITHOUT ROWID;

        -- The journal: <character>\journal\<NPC name>, one file per NPC with everything that NPC has said to the character.
        -- journal_file = one line per file read, so an unchanged file is not read again.
        CREATE TABLE IF NOT EXISTS journal_file (
            server TEXT NOT NULL, character TEXT NOT NULL, file TEXT NOT NULL, size INTEGER NOT NULL, written_utc TEXT NOT NULL,
            lines INTEGER NOT NULL, imported_utc TEXT NOT NULL,
            PRIMARY KEY (server, character, file)) WITHOUT ROWID;
        -- npc = every NPC a journal file is named for, once, whatever the number of characters that met them. looted_as_monster = the same name
        -- also appears in the ledger as something killed or looted (so the NPC lines up with a monster of the loot tables).
        CREATE TABLE IF NOT EXISTS npc (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE COLLATE NOCASE,
            first_seen_utc TEXT,
            looted_as_monster INTEGER NOT NULL DEFAULT 0);
        -- npc_line = one entry of a journal file. text = what was said (the speaker and the quotes taken off), raw = the entry as written.
        -- at_utc is null for an entry the game gave no time. uid makes an entry read twice one entry.
        CREATE TABLE IF NOT EXISTS npc_line (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            uid TEXT NOT NULL UNIQUE,
            npc_id INTEGER NOT NULL REFERENCES npc (id),
            server TEXT NOT NULL, character TEXT NOT NULL,
            at_utc TEXT, speaker TEXT NOT NULL, verb TEXT NOT NULL, text TEXT NOT NULL, raw TEXT NOT NULL, imported_utc TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_npc_line_npc ON npc_line (npc_id, at_utc);
        CREATE INDEX IF NOT EXISTS ix_npc_line_who ON npc_line (server, character, at_utc);
        """;

    private void SeedActCodes()
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            using var cmd = _db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO act_code (act, meaning, certainty, notes) VALUES ($a, $m, $c, $n)
                ON CONFLICT (act) DO UPDATE SET meaning = $m, certainty = $c, notes = $n
                """;
            var a = cmd.Parameters.Add("$a", SqliteType.Text);
            var m = cmd.Parameters.Add("$m", SqliteType.Text);
            var c = cmd.Parameters.Add("$c", SqliteType.Text);
            var n = cmd.Parameters.Add("$n", SqliteType.Text);
            foreach (var code in ActCodes.All)
            {
                a.Value = code.Act; m.Value = code.Meaning; c.Value = code.Certainty; n.Value = code.Notes;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    /// <summary>The size and write time (UTC) of <paramref name="key"/> when it was last imported, or null if it never was.</summary>
    public (long Size, string WrittenUtc)? ImportedState(LedgerFileKey key)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT size, written_utc FROM ledger_file WHERE server = $s AND character = $c AND file = $f";
            cmd.Parameters.AddWithValue("$s", key.Server);
            cmd.Parameters.AddWithValue("$c", key.Character);
            cmd.Parameters.AddWithValue("$f", key.File);
            using var r = cmd.ExecuteReader();
            return r.Read() ? (r.GetInt64(0), r.GetString(1)) : null;
        }
    }

    /// <summary>
    /// Stores the rows of one file. A row already held is ignored, so reading a file again, or the archive repeating a dated file's
    /// row, adds nothing; two identical rows inside one file are both kept (they are numbered). Returns how many rows were new.
    /// </summary>
    public int Import(LedgerFileKey key, long size, DateTime writtenUtc, IReadOnlyList<LedgerRow> rows, IReadOnlyDictionary<string, string> envelope)
    {
        var now = Iso(DateTime.UtcNow);
        int added = 0;
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR IGNORE INTO ledger_row
                        (uid, server, character, kind, file, act, at_utc, name, zone, level, actor, owner, class, ledger_type, detail, raw, imported_utc)
                    VALUES ($uid, $server, $character, $kind, $file, $act, $at, $name, $zone, $level, $actor, $owner, $class, $type, $detail, $raw, $now)
                    """;
                var p = new Dictionary<string, SqliteParameter>();
                foreach (var name in new[] { "$uid", "$server", "$character", "$kind", "$file", "$act", "$at", "$name", "$zone", "$level", "$actor", "$owner", "$class", "$type", "$detail", "$raw", "$now" })
                    p[name] = cmd.Parameters.Add(name, SqliteType.Text);
                p["$level"].SqliteType = SqliteType.Integer;
                p["$type"].SqliteType = SqliteType.Integer;
                p["$server"].Value = key.Server;
                p["$character"].Value = key.Character;
                p["$file"].Value = key.File;
                p["$now"].Value = now;

                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var row in rows)
                {
                    seen.TryGetValue(row.RawJson, out var occurrence);
                    seen[row.RawJson] = occurrence + 1;
                    var kind = row.File.ToString();
                    p["$uid"].Value = Uid(key.Server, key.Character, kind, row.RawJson, occurrence);
                    p["$kind"].Value = kind;
                    p["$act"].Value = row.Act;
                    p["$at"].Value = Iso(AtUtc(row));
                    p["$name"].Value = row.Name;
                    p["$zone"].Value = row.Zone;
                    p["$level"].Value = row.Level;
                    p["$actor"].Value = row.Actor;
                    p["$owner"].Value = row.Owner;
                    p["$class"].Value = row.Get("f10");
                    p["$type"].Value = int.TryParse(row.Get("f08"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lt) ? lt : DBNull.Value;
                    p["$detail"].Value = row.DetailJson;
                    p["$raw"].Value = row.RawJson;
                    added += cmd.ExecuteNonQuery();
                }
            }
            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO ledger_file (server, character, file, kind, archived, size, written_utc, rows, envelope, imported_utc)
                    VALUES ($server, $character, $file, $kind, $archived, $size, $written, $rows, $envelope, $now)
                    ON CONFLICT (server, character, file) DO UPDATE SET
                        size = $size, written_utc = $written, rows = $rows, envelope = $envelope, imported_utc = $now
                    """;
                cmd.Parameters.AddWithValue("$server", key.Server);
                cmd.Parameters.AddWithValue("$character", key.Character);
                cmd.Parameters.AddWithValue("$file", key.File);
                cmd.Parameters.AddWithValue("$kind", KindOf(key.File).ToString());
                cmd.Parameters.AddWithValue("$archived", IsArchived(key.File) ? 1 : 0);
                cmd.Parameters.AddWithValue("$size", size);
                cmd.Parameters.AddWithValue("$written", Iso(writtenUtc));
                cmd.Parameters.AddWithValue("$rows", rows.Count);
                cmd.Parameters.AddWithValue("$envelope", System.Text.Json.JsonSerializer.Serialize(envelope));
                cmd.Parameters.AddWithValue("$now", now);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        return added;
    }

    /// <summary>The size and write time (UTC) of a journal file when it was last imported, or null if it never was.</summary>
    public (long Size, string WrittenUtc)? JournalState(LedgerFileKey key)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT size, written_utc FROM journal_file WHERE server = $s AND character = $c AND file = $f";
            cmd.Parameters.AddWithValue("$s", key.Server);
            cmd.Parameters.AddWithValue("$c", key.Character);
            cmd.Parameters.AddWithValue("$f", key.File);
            using var r = cmd.ExecuteReader();
            return r.Read() ? (r.GetInt64(0), r.GetString(1)) : null;
        }
    }

    /// <summary>
    /// Stores one journal file (<see cref="LedgerFileKey.File"/> is the NPC's name). The NPC is added if the name is new (names match
    /// without regard to case); an entry already held is ignored, so reading the file again, or the game putting a new entry at its top,
    /// adds only what is new; two identical entries in one file are both kept (they are numbered). Returns how many NPCs and entries were new.
    /// </summary>
    public (int Npcs, int Lines) ImportJournal(LedgerFileKey key, long size, DateTime writtenUtc, IReadOnlyList<Journal.JournalLine> lines)
    {
        var now = Iso(DateTime.UtcNow);
        int npcs, added = 0;
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            var times = lines.Where(l => l.Time is not null).Select(l => l.Time!.Value.ToUniversalTime()).ToList();
            string? earliest = times.Count == 0 ? null : Iso(times.Min());

            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO npc (name, first_seen_utc) VALUES ($n, $t) ON CONFLICT (name) DO NOTHING";
                cmd.Parameters.AddWithValue("$n", key.File);
                cmd.Parameters.AddWithValue("$t", (object?)earliest ?? DBNull.Value);
                npcs = cmd.ExecuteNonQuery();
            }
            long npcId;
            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT id FROM npc WHERE name = $n";
                cmd.Parameters.AddWithValue("$n", key.File);
                npcId = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            if (earliest is not null)
            {
                using var cmd = _db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE npc SET first_seen_utc = $t WHERE id = $id AND (first_seen_utc IS NULL OR first_seen_utc > $t)";
                cmd.Parameters.AddWithValue("$t", earliest);
                cmd.Parameters.AddWithValue("$id", npcId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR IGNORE INTO npc_line (uid, npc_id, server, character, at_utc, speaker, verb, text, raw, imported_utc)
                    VALUES ($uid, $npc, $server, $character, $at, $speaker, $verb, $text, $raw, $now)
                    """;
                var uid = cmd.Parameters.Add("$uid", SqliteType.Text);
                var at = cmd.Parameters.Add("$at", SqliteType.Text);
                var speaker = cmd.Parameters.Add("$speaker", SqliteType.Text);
                var verb = cmd.Parameters.Add("$verb", SqliteType.Text);
                var text = cmd.Parameters.Add("$text", SqliteType.Text);
                var raw = cmd.Parameters.Add("$raw", SqliteType.Text);
                cmd.Parameters.AddWithValue("$npc", npcId);
                cmd.Parameters.AddWithValue("$server", key.Server);
                cmd.Parameters.AddWithValue("$character", key.Character);
                cmd.Parameters.AddWithValue("$now", now);
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var line in lines)
                {
                    var stamp = line.Time is null ? "" : Iso(line.Time.Value);
                    var identity = stamp + "\n" + line.Raw;
                    seen.TryGetValue(identity, out var occurrence);
                    seen[identity] = occurrence + 1;
                    uid.Value = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{key.Server}\n{key.Character}\n{key.File}\n{occurrence}\n{identity}")));
                    at.Value = line.Time is null ? DBNull.Value : stamp;
                    speaker.Value = line.Speaker;
                    verb.Value = line.Verb;
                    text.Value = line.Text;
                    raw.Value = line.Raw;
                    added += cmd.ExecuteNonQuery();
                }
            }
            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO journal_file (server, character, file, size, written_utc, lines, imported_utc)
                    VALUES ($server, $character, $file, $size, $written, $lines, $now)
                    ON CONFLICT (server, character, file) DO UPDATE SET size = $size, written_utc = $written, lines = $lines, imported_utc = $now
                    """;
                cmd.Parameters.AddWithValue("$server", key.Server);
                cmd.Parameters.AddWithValue("$character", key.Character);
                cmd.Parameters.AddWithValue("$file", key.File);
                cmd.Parameters.AddWithValue("$size", size);
                cmd.Parameters.AddWithValue("$written", Iso(writtenUtc));
                cmd.Parameters.AddWithValue("$lines", lines.Count);
                cmd.Parameters.AddWithValue("$now", now);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        return (npcs, added);
    }

    /// <summary>
    /// Lines the NPCs up with the monsters of the loot tables: an NPC whose name also appears in the ledger as something killed (act_14) or
    /// as what dropped an item (act_13) is marked <c>looted_as_monster</c>. Worked out from the saved ledger rows, so it is cheap to redo.
    /// </summary>
    public void MarkLootedNpcs() => Exec("""
        UPDATE npc SET looted_as_monster = CASE WHEN name IN (
            SELECT name FROM ledger_row WHERE act = 'act_14' AND name <> ''
            UNION SELECT json_extract(detail, '$.d02') FROM ledger_row WHERE act = 'act_13' AND json_extract(detail, '$.d02') IS NOT NULL) THEN 1 ELSE 0 END
        """);

    /// <summary>The file kind the game's file names give: a name with "_Character_" is a Character file, anything else Social.</summary>
    public static LedgerFileKind KindOf(string file) =>
        file.Contains("_Character_", StringComparison.OrdinalIgnoreCase) ? LedgerFileKind.Character : LedgerFileKind.Social;

    public static bool IsArchived(string file) => file.StartsWith("Archive", StringComparison.OrdinalIgnoreCase);

    public long RowCount() => Scalar("SELECT count(*) FROM ledger_row");

    public long FileCount() => Scalar("SELECT count(*) FROM ledger_file");

    /// <summary>How many rows of each act the database holds, most first.</summary>
    public IReadOnlyList<(string Act, string Meaning, long Rows)> RowsByAct()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT r.act, coalesce(c.meaning, 'not in the act table'), count(*) FROM ledger_row r
                LEFT JOIN act_code c ON c.act = r.act GROUP BY r.act ORDER BY count(*) DESC
                """;
            using var r = cmd.ExecuteReader();
            var list = new List<(string, string, long)>();
            while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetInt64(2)));
            return list;
        }
    }

    private long Scalar(string sql)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Runs <paramref name="work"/> on the connection while holding the lock, for the classes of this project that read and write the event tables.</summary>
    internal T With<T>(Func<SqliteConnection, T> work)
    {
        lock (_gate) return work(_db);
    }

    /// <summary>
    /// The random id this PC shares data under: made the first time it is asked for and kept, so a player who sends their file again
    /// replaces what they sent before. It is not derived from anything about the player, their characters or this PC.
    /// </summary>
    public string ContributorId() => With(conn =>
    {
        using var read = conn.CreateCommand();
        read.CommandText = "SELECT value FROM meta WHERE key = 'contributor_id'";
        if (read.ExecuteScalar() is string have && have.Length > 0) return have;
        var made = Guid.NewGuid().ToString("N");
        using var write = conn.CreateCommand();
        write.CommandText = "INSERT INTO meta (key, value) VALUES ('contributor_id', $v) ON CONFLICT (key) DO NOTHING";
        write.Parameters.AddWithValue("$v", made);
        write.ExecuteNonQuery();
        return made;
    });

    private void Exec(string sql)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>The row's time in UTC, read from the text as the game wrote it (so a clock change cannot move it).</summary>
    private static DateTime AtUtc(LedgerRow row) =>
        DateTimeOffset.TryParse(row.Get("f04"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.UtcDateTime : row.Time.ToUniversalTime();

    private static string Uid(string server, string character, string kind, string raw, int occurrence) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{server}\n{character}\n{kind}\n{occurrence}\n{raw}")));

    internal static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        lock (_gate) _db.Dispose();
    }
}
