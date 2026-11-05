// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Trayloom.Features.Agents;

/// <summary>
/// Reads OpenCode sessions and messages from its SQLite database (<c>%USERPROFILE%\.local\share\opencode\opencode.db</c>), strictly
/// read-only. Only counters, ids, times and folder names are extracted (spec 03 A4.5): prompts, replies and error text never leave
/// the database.
/// </summary>
internal static class OpenCodeDatabase
{
    public const string FileName = "opencode.db";

    private const int TailLength = 64;
    private static readonly TimeSpan Abandoned = TimeSpan.FromHours(24);

    private static readonly (string Name, string Path)[] Values =
    [
        ("role", "$.role"), ("parentID", "$.parentID"), ("modelID", "$.modelID"), ("model_id", "$.model_id"),
        ("model", "$.model"), ("tokens", "$.tokens"), ("cost", "$.cost"), ("finish", "$.finish"),
        ("time", "$.time"), ("cwd", "$.path.cwd"), ("status", "$.status"), ("outcome", "$.outcome"),
    ];

    /// <summary>When the database or its write-ahead log last changed; null when there is no database.</summary>
    public static DateTime? Modified(string path)
    {
        try
        {
            var db = new FileInfo(path);
            if (!db.Exists)
            {
                return null;
            }

            var wal = new FileInfo(path + "-wal");
            var walTime = wal.Exists ? wal.LastWriteTimeUtc : DateTime.MinValue;
            return db.LastWriteTimeUtc > walTime ? db.LastWriteTimeUtc : walTime;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void ReadAppended(AgentLogCursor cursor, DateTime horizon, Func<bool> shouldContinue, Action<ReadOnlyMemory<byte>> line)
    {
        if (!shouldContinue() || !File.Exists(cursor.Path))
        {
            return;
        }

        ulong identity;
        try
        {
            using var handle = File.OpenHandle(cursor.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            identity = AgentNative.FileIdentity(handle);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        // The database was replaced or recreated.
        if (identity != cursor.Identity)
        {
            var replaced = cursor.Identity != 0;
            cursor.Identity = identity;
            Restart(cursor, replaced, line);
        }

        cursor.Modified = Modified(cursor.Path) ?? DateTime.UtcNow;

        string? copyDir = null;
        try
        {
            SqliteConnection? connection = null;
            try
            {
                connection = Open(cursor.Path);
            }
            catch (SqliteException)
            {
                // The write-ahead log needs its -shm: read a copy instead.
                copyDir = Copy(cursor.Path);
                if (copyDir is null)
                {
                    return;
                }

                try
                {
                    connection = Open(Path.Combine(copyDir, FileName));
                }
                catch (SqliteException)
                {
                    return;
                }
            }

            using (connection)
            {
                try
                {
                    Read(connection, cursor, horizon, shouldContinue, line);
                }
                catch (SqliteException)
                {
                    // A locked or half-written database is read again at the next change.
                }
            }
        }
        finally
        {
            if (copyDir is not null)
            {
                try
                {
                    Directory.Delete(copyDir, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try
        {
            connection.Open();
            using var busy = connection.CreateCommand();
            busy.CommandText = "PRAGMA busy_timeout = 2000";
            busy.ExecuteNonQuery();
            // The file belongs to another program: never let SQL stored inside it (views, triggers, generated columns) call functions.
            using var trust = connection.CreateCommand();
            trust.CommandText = "PRAGMA trusted_schema = OFF";
            trust.ExecuteNonQuery();
            using var probe = connection.CreateCommand();
            probe.CommandText = "SELECT 1 FROM sqlite_master LIMIT 1";
            probe.ExecuteScalar();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string? Copy(string path)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "trayloom-opencode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var source = path + suffix;
                if (!File.Exists(source))
                {
                    continue;
                }

                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var output = new FileStream(Path.Combine(dir, FileName + suffix), FileMode.Create, FileAccess.Write);
                input.CopyTo(output);
            }

            return dir;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Restart(AgentLogCursor cursor, bool announce, Action<ReadOnlyMemory<byte>> line)
    {
        cursor.Offset = 0;
        cursor.OpenCode.Clear();
        cursor.State = new AgentLogState();
        if (announce)
        {
            line("{\"type\":\"reset\"}"u8.ToArray());
        }
    }

    private static string FieldOf(string column, string path) => $"(CASE WHEN json_valid({column}) THEN json_extract({column}, '{path}') END)";

    private static bool IsPlainTable(SqliteConnection db, string name)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT type FROM sqlite_master WHERE name = $n";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() as string == "table";
    }

    private static bool Exists(SqliteConnection db, string query)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = query;
        using var reader = cmd.ExecuteReader();
        return reader.Read();
    }

    private static void Read(SqliteConnection db, AgentLogCursor cursor, DateTime horizon, Func<bool> shouldContinue, Action<ReadOnlyMemory<byte>> line)
    {
        // OpenCode 1 keeps a session_message table of its own since 1.15, so only the session_v2 table tells OpenCode 2 apart.
        var version2 = Exists(db, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'session_v2'");
        var messageTable = version2 ? "session_message" : "message";
        var sessionTable = version2 ? "session_v2" : "session";
        if (!IsPlainTable(db, messageTable) || !IsPlainTable(db, sessionTable))
        {
            // A view in place of a table could run arbitrary queries of the other program's choosing: read nothing.
            return;
        }

        if (!Resumes(db, cursor, messageTable))
        {
            Restart(cursor, true, line);
        }

        var floor = (long)(Math.Max(0, (horizon - AgentTime.Epoch).TotalSeconds) * 1000);
        if (cursor.Offset == 0)
        {
            var indexed = version2 ? FirstIndexedRow(db, floor) : null;
            cursor.Offset = (ulong)(indexed ?? FirstRow(db, messageTable, floor));
        }

        var recent = version2 && floor > 0 ? $"m.time_created >= {floor} AND " : "";
        var hasParentID = Exists(db, $"SELECT 1 FROM pragma_table_info('{sessionTable}') WHERE name = 'parent_id'");
        var hasParts = Exists(db, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'part'");
        var role = version2 ? "m.type" : FieldOf("m.data", "$.role");

        string toolCalls;
        if (version2)
        {
            toolCalls = $@"CASE WHEN {recent}{role} = 'assistant'
                AND {FieldOf("m.data", "$.finish")} NOT IN ('tool-calls', 'unknown')
            THEN EXISTS (SELECT 1 FROM json_each(CASE WHEN json_valid(m.data)
                THEN json_extract(m.data, '$.content') ELSE '[]' END) c
                WHERE json_extract(c.value, '$.type') = 'tool'
                    AND coalesce(json_extract(c.value, '$.executed'), 0) = 0)
            ELSE 0 END";
        }
        else if (hasParts)
        {
            toolCalls = $@"CASE WHEN {FieldOf("m.data", "$.role")} = 'assistant'
                AND {FieldOf("m.data", "$.finish")} NOT IN ('tool-calls', 'unknown')
            THEN EXISTS (SELECT 1 FROM part p WHERE p.message_id = m.id
                AND {FieldOf("p.data", "$.type")} = 'tool'
                AND coalesce({FieldOf("p.data", "$.metadata.providerExecuted")}, 0) = 0
                AND NOT (coalesce({FieldOf("p.data", "$.state.status")}, '') = 'error'
                    AND coalesce({FieldOf("p.data", "$.state.metadata.interrupted")}, 0) = 1))
            ELSE 0 END";
        }
        else
        {
            toolCalls = "0";
        }

        var autoCompaction = !version2 && hasParts
            ? $@"CASE WHEN {FieldOf("m.data", "$.role")} = 'assistant' AND {FieldOf("m.data", "$.summary")} = 1
            THEN EXISTS (SELECT 1 FROM part p WHERE p.message_id = {FieldOf("m.data", "$.parentID")}
                AND {FieldOf("p.data", "$.type")} = 'compaction' AND {FieldOf("p.data", "$.auto")} = 1)
            ELSE 0 END"
            : "0";
        var writing = $@"({recent}(coalesce({role} = 'assistant'
                AND {FieldOf("m.data", "$.time.completed")} IS NULL, 0)
            OR coalesce({role} = 'compaction' AND {FieldOf("m.data", "$.status")} = 'running', 0)))";
        var parentCol = hasParentID ? "s.parent_id" : "NULL";
        var fields = string.Join(", ", Values.Select(v => $"json_extract(m.data, '{v.Path}')"));
        var size = recent.Length == 0 ? "length(m.data)" : $"CASE WHEN {recent}1 THEN length(m.data) END";
        var select = $@"SELECT m.rowid, m.id, m.session_id, m.time_created, m.time_updated, {size}, s.directory,
            {parentCol}, CASE WHEN {recent}json_valid(m.data) THEN json_array({role}, {fields}) END,
            {toolCalls}, {autoCompaction}, {writing},
            CASE WHEN {recent}json_valid(m.data) THEN json_type(m.data, '$.error') END
        FROM {messageTable} m
        JOIN {sessionTable} s ON m.session_id = s.id";
        var now = DateTime.UtcNow;

        // Replies still being written, before anything saved after them.
        var open = cursor.OpenCode.Open.Keys.ToList();
        if (open.Count > 0)
        {
            using var cmd = db.CreateCommand();
            var marks = string.Join(", ", open.Select((_, i) => "$o" + i));
            cmd.CommandText = select + $" WHERE m.id IN ({marks}) ORDER BY m.rowid";
            for (var i = 0; i < open.Count; i++)
            {
                cmd.Parameters.AddWithValue("$o" + i, open[i]);
            }

            var found = new HashSet<string>(StringComparer.Ordinal);
            using (var reader = cmd.ExecuteReader())
            {
                while (shouldContinue() && reader.Read())
                {
                    var id = Text(reader, 1);
                    if (id is null || !cursor.OpenCode.Open.TryGetValue(id, out var known))
                    {
                        continue;
                    }

                    found.Add(id);
                    var updated = Long(reader, 4);
                    var length = Long(reader, 5);
                    // Unchanged since the last read: nothing is copied or parsed.
                    if (updated == known.Updated && length == known.Length)
                    {
                        if (now - known.Changed > Abandoned)
                        {
                            cursor.OpenCode.Open.Remove(id);
                        }

                        continue;
                    }

                    HandOver(reader, cursor, now, version2, line);
                }
            }

            if (!shouldContinue())
            {
                return;
            }

            // A reply deleted with its session or by a revert is written no more.
            foreach (var id in open.Where(id => !found.Contains(id)))
            {
                cursor.OpenCode.Open.Remove(id);
            }
        }

        // Everything saved since the last read.
        using var all = db.CreateCommand();
        all.CommandText = select + " WHERE m.rowid > $r ORDER BY m.rowid";
        all.Parameters.AddWithValue("$r", (long)cursor.Offset);
        using var rows = all.ExecuteReader();
        while (shouldContinue() && rows.Read())
        {
            var rowid = Long(rows, 0);
            cursor.Offset = (ulong)Math.Max(0, rowid);
            if (Text(rows, 1) is { } id)
            {
                cursor.OpenCode.Tail.Add((rowid, id));
                if (cursor.OpenCode.Tail.Count > TailLength)
                {
                    cursor.OpenCode.Tail.RemoveAt(0);
                }
            }

            HandOver(rows, cursor, now, version2, line);
        }
    }

    private static long Long(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();

    /// <summary>Hands over the row the reader stands on, and keeps a reply still being written to look at again.</summary>
    private static void HandOver(SqliteDataReader r, AgentLogCursor cursor, DateTime now, bool version2, Action<ReadOnlyMemory<byte>> line)
    {
        // A later version could leave any of these empty; such a row is skipped.
        if (Text(r, 1) is not { } id || Text(r, 2) is not { } sessionID || r.IsDBNull(3))
        {
            return;
        }

        var created = Long(r, 3);
        var updated = r.IsDBNull(4) ? created : Long(r, 4);
        if (Long(r, 11) != 0)
        {
            cursor.OpenCode.Open[id] = (Long(r, 4), Long(r, 5), now);
        }
        else
        {
            cursor.OpenCode.Open.Remove(id);
        }

        // A row whose data cannot be read is let go above, not looked at again.
        if (Text(r, 8) is not { } extracted)
        {
            return;
        }

        using var found = AgentLogParser.Parse(System.Text.Encoding.UTF8.GetBytes(extracted));
        if (found is null || found.RootElement.ValueKind != JsonValueKind.Array || found.RootElement.GetArrayLength() != Values.Length + 1)
        {
            return;
        }

        var items = found.RootElement.EnumerateArray().ToArray();
        var json = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (items[0].ValueKind != JsonValueKind.Null)
        {
            json["role"] = items[0];
        }

        for (var i = 0; i < Values.Length; i++)
        {
            if (items[i + 1].ValueKind != JsonValueKind.Null)
            {
                json[Values[i].Name] = items[i + 1];
            }
        }

        // OpenCode 2 completes a step it cut short only as the next step starts. Without a finish, an error or usage, it neither ends nor starts a turn.
        var hasError = Text(r, 12) is { } err && err != "null";
        if (version2 && json.TryGetValue("role", out var roleEl) && roleEl.ValueKind == JsonValueKind.String && roleEl.GetString() == "assistant"
            && !json.ContainsKey("finish") && !hasError && !json.ContainsKey("tokens")
            && json.TryGetValue("time", out var time) && time.ValueKind == JsonValueKind.Object && time.TryGetProperty("completed", out var completed) && completed.ValueKind != JsonValueKind.Null)
        {
            return;
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var (name, value) in json)
            {
                if (name == "cwd")
                {
                    w.WritePropertyName("path");
                    w.WriteStartObject();
                    w.WritePropertyName("cwd");
                    value.WriteTo(w);
                    w.WriteEndObject();
                    continue;
                }

                w.WritePropertyName(name);
                value.WriteTo(w);
            }

            w.WriteString("id", id);
            w.WriteString("session_id", sessionID);
            w.WriteString("parent_session_id", Text(r, 7) ?? "");
            w.WriteString("directory", Text(r, 6) ?? "");
            w.WriteNumber("time_created", created);
            w.WriteNumber("time_updated", updated);
            if (Long(r, 9) != 0)
            {
                w.WriteBoolean("tool_calls", true);
            }

            if (Long(r, 10) != 0)
            {
                w.WriteBoolean("auto_compaction", true);
            }

            if (hasError)
            {
                w.WriteBoolean("error", true);
            }

            if (version2)
            {
                w.WriteNumber("version", 2);
            }

            w.WriteEndObject();
        }

        line(ms.ToArray());
    }

    /// <summary>False when every row read last is gone or holds another message; when only the newest were deleted, reading goes on after the newest one still there.</summary>
    private static bool Resumes(SqliteConnection db, AgentLogCursor cursor, string table)
    {
        var tail = cursor.OpenCode.Tail;
        if (tail.Count == 0)
        {
            return true;
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT id FROM {table} WHERE rowid = $r";
        var p = cmd.Parameters.Add("$r", SqliteType.Integer);
        for (var position = tail.Count - 1; position >= 0; position--)
        {
            p.Value = tail[position].RowId;
            var id = cmd.ExecuteScalar()?.ToString();
            if (id != tail[position].Id)
            {
                continue;
            }

            cursor.Offset = (ulong)tail[position].RowId;
            tail.RemoveRange(position + 1, tail.Count - 1 - position);
            return true;
        }

        return false;
    }

    /// <summary>The rowid just before the first message created at <paramref name="floor"/> or later (rows are stored in the order they were saved).</summary>
    private static long FirstRow(SqliteConnection db, string table, long floor)
    {
        if (floor <= 0)
        {
            return 0;
        }

        long high;
        using (var max = db.CreateCommand())
        {
            max.CommandText = $"SELECT max(rowid) FROM {table}";
            high = max.ExecuteScalar() is long l ? l : 0;
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT rowid, time_created FROM {table} WHERE rowid >= $r ORDER BY rowid LIMIT 1";
        var p = cmd.Parameters.Add("$r", SqliteType.Integer);
        long low = 0;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            p.Value = middle;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                high = middle - 1;
                continue;
            }

            var rowid = reader.GetInt64(0);
            if (reader.IsDBNull(1) || reader.GetInt64(1) < floor)
            {
                low = rowid;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    private static long? FirstIndexedRow(SqliteConnection db, long floor)
    {
        if (floor <= 0)
        {
            return 0;
        }

        try
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"SELECT (SELECT min(rowid) FROM session_message INDEXED BY session_message_time_created_idx WHERE time_created >= $f),
                (SELECT max(rowid) FROM session_message)";
            cmd.Parameters.AddWithValue("$f", floor);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            if (reader.IsDBNull(0))
            {
                return reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            }

            return Math.Max(0, reader.GetInt64(0) - 1);
        }
        catch (SqliteException)
        {
            return null;
        }
    }
}
