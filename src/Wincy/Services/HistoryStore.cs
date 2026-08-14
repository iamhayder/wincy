using System.IO;
using Microsoft.Data.Sqlite;
using Wincy.Models;

namespace Wincy.Services;

/// <summary>
/// SQLite-backed persistence for the clipboard history.
///
/// Two tables, mirroring Maccy's SwiftData model: one row per copy, one row per
/// representation of that copy. Contents are stored as raw blobs so an item can be
/// put back on the clipboard byte-for-byte.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();
    private bool _disposed;

    public string DatabasePath { get; }

    public HistoryStore(string databasePath)
    {
        DatabasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());

        _connection.Open();
        Execute("PRAGMA journal_mode = WAL;");
        Execute("PRAGMA synchronous = NORMAL;");
        Execute("PRAGMA foreign_keys = ON;");
        CreateSchema();
    }

    private void CreateSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS items (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                title           TEXT    NOT NULL DEFAULT '',
                application     TEXT,
                first_copied_at TEXT    NOT NULL,
                last_copied_at  TEXT    NOT NULL,
                number_of_copies INTEGER NOT NULL DEFAULT 1,
                pin             TEXT
            );
            """);

        Execute("""
            CREATE TABLE IF NOT EXISTS contents (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                item_id INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                format  TEXT    NOT NULL,
                hash    TEXT,
                length  INTEGER NOT NULL DEFAULT 0,
                value   BLOB
            );
            """);

        // Bring forward any database created before hash/length existed.
        AddColumnIfMissing("contents", "hash", "TEXT");
        AddColumnIfMissing("contents", "length", "INTEGER NOT NULL DEFAULT 0");

        Execute("CREATE INDEX IF NOT EXISTS idx_contents_item ON contents(item_id);");
        Execute("CREATE INDEX IF NOT EXISTS idx_items_last_copied ON items(last_copied_at DESC);");
        Execute("CREATE INDEX IF NOT EXISTS idx_items_pin ON items(pin);");
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>SQLite has no ALTER TABLE ... IF NOT EXISTS, so the columns are checked first.</summary>
    private void AddColumnIfMissing(string table, string column, string definition)
    {
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info({table});";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition};");
        Log.Info($"Added column {table}.{column}");
    }

    // ------------------------------------------------------------------ reads

    public List<ClipItem> LoadAll()
    {
        lock (_gate)
        {
            var items = new Dictionary<long, ClipItem>();

            using (var command = _connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT id, title, application, first_copied_at, last_copied_at, number_of_copies, pin FROM items;";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var item = new ClipItem
                    {
                        Id = reader.GetInt64(0),
                        Title = reader.GetString(1),
                        Application = reader.IsDBNull(2) ? null : reader.GetString(2),
                        FirstCopiedAt = ParseDate(reader.GetString(3)),
                        LastCopiedAt = ParseDate(reader.GetString(4)),
                        NumberOfCopies = reader.GetInt32(5),
                        Pin = reader.IsDBNull(6) ? null : reader.GetString(6)
                    };

                    items[item.Id] = item;
                }
            }

            // Deliberately no `value` column here: blobs are fetched on demand.
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT id, item_id, format, hash, length FROM contents;";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var itemId = reader.GetInt64(1);
                    if (!items.TryGetValue(itemId, out var item))
                    {
                        continue;
                    }

                    var content = new ClipContent
                    {
                        Id = reader.GetInt64(0),
                        ItemId = itemId,
                        Format = reader.GetString(2),
                        Hash = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Length = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                    };

                    content.DeferValue(LoadContentValue);
                    item.Contents.Add(content);
                }
            }

            return [.. items.Values];
        }
    }

    /// <summary>Fetches one content blob. Wired into every deferred <see cref="ClipContent"/>.</summary>
    public byte[]? LoadContentValue(long contentId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT value FROM contents WHERE id = $id;";
            command.Parameters.AddWithValue("$id", contentId);

            using var reader = command.ExecuteReader();
            if (reader.Read() && !reader.IsDBNull(0))
            {
                return (byte[])reader.GetValue(0);
            }

            return null;
        }
    }

    public long CountItems() => ScalarLong("SELECT COUNT(*) FROM items;");

    public long DatabaseSizeBytes()
    {
        try
        {
            var length = new FileInfo(DatabasePath).Length;

            // Include the write-ahead log, which can be a large share of the total.
            var wal = DatabasePath + "-wal";
            if (File.Exists(wal))
            {
                length += new FileInfo(wal).Length;
            }

            return length;
        }
        catch
        {
            return 0;
        }
    }

    private long ScalarLong(string sql)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
        }
    }

    // ----------------------------------------------------------------- writes

    public void Insert(ClipItem item)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO items (title, application, first_copied_at, last_copied_at, number_of_copies, pin)
                    VALUES ($title, $application, $first, $last, $copies, $pin);
                    SELECT last_insert_rowid();
                    """;
                command.Parameters.AddWithValue("$title", item.Title);
                command.Parameters.AddWithValue("$application", (object?)item.Application ?? DBNull.Value);
                command.Parameters.AddWithValue("$first", FormatDate(item.FirstCopiedAt));
                command.Parameters.AddWithValue("$last", FormatDate(item.LastCopiedAt));
                command.Parameters.AddWithValue("$copies", item.NumberOfCopies);
                command.Parameters.AddWithValue("$pin", (object?)item.Pin ?? DBNull.Value);

                item.Id = Convert.ToInt64(command.ExecuteScalar());
            }

            foreach (var content in item.Contents)
            {
                content.ItemId = item.Id;

                var bytes = content.Value;

                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO contents (item_id, format, hash, length, value)
                    VALUES ($item, $format, $hash, $length, $value);
                    SELECT last_insert_rowid();
                    """;
                command.Parameters.AddWithValue("$item", item.Id);
                command.Parameters.AddWithValue("$format", content.Format);
                command.Parameters.AddWithValue("$hash", (object?)content.Hash ?? DBNull.Value);
                command.Parameters.AddWithValue("$length", bytes?.Length ?? 0);
                command.Parameters.AddWithValue("$value", (object?)bytes ?? DBNull.Value);

                content.Id = Convert.ToInt64(command.ExecuteScalar());
                content.Length = bytes?.Length ?? 0;
            }

            transaction.Commit();
        }
    }

    public void UpdateMetadata(ClipItem item)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE items
                   SET title = $title,
                       application = $application,
                       first_copied_at = $first,
                       last_copied_at = $last,
                       number_of_copies = $copies,
                       pin = $pin
                 WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$title", item.Title);
            command.Parameters.AddWithValue("$application", (object?)item.Application ?? DBNull.Value);
            command.Parameters.AddWithValue("$first", FormatDate(item.FirstCopiedAt));
            command.Parameters.AddWithValue("$last", FormatDate(item.LastCopiedAt));
            command.Parameters.AddWithValue("$copies", item.NumberOfCopies);
            command.Parameters.AddWithValue("$pin", (object?)item.Pin ?? DBNull.Value);
            command.Parameters.AddWithValue("$id", item.Id);
            command.ExecuteNonQuery();
        }
    }

    public void Delete(ClipItem item)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM items WHERE id = $id;";
            command.Parameters.AddWithValue("$id", item.Id);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Deletes every unpinned item. Used by the footer's Clear action.</summary>
    public void DeleteUnpinned()
    {
        lock (_gate)
        {
            Execute("DELETE FROM items WHERE pin IS NULL;");
        }
    }

    public void DeleteAll()
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM contents; DELETE FROM items;";
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            Execute("VACUUM;");
        }
    }

    /// <summary>Removes content rows whose parent went away, as Maccy's orphan cleanup does.</summary>
    public int CleanupOrphanedContents()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM contents WHERE item_id NOT IN (SELECT id FROM items);";
            return command.ExecuteNonQuery();
        }
    }

    public void Compact()
    {
        lock (_gate)
        {
            Execute("VACUUM;");
        }
    }

    private static string FormatDate(DateTime value) =>
        value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.UtcNow;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Close();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
    }
}
