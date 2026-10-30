using Microsoft.Data.Sqlite;

namespace Fixtura;

// Empties one database's tables, keeping the schema and EF Core's migrations history, which it recognizes by its columns.
internal static class SqliteReset
{
    // Real and virtual tables only: a full-text or spatial index keeps its data in shadow tables that empty with it.
    private const string ReadSchema = """
        SELECT t.name FROM pragma_table_list t
        WHERE t.schema = 'main' AND t.type IN ('table', 'virtual') AND t.name NOT LIKE 'sqlite\_%' ESCAPE '\'
            AND NOT (EXISTS (SELECT 1 FROM pragma_table_info(t.name) WHERE name = 'MigrationId')
                AND EXISTS (SELECT 1 FROM pragma_table_info(t.name) WHERE name = 'ProductVersion'));
        SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND sql IS NOT NULL;
        SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'sqlite_sequence';
        """;

    public static async Task ResetAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // SQLite ignores this inside a transaction; it holds for this connection only, so tables empty in any order.
        await using (var foreignKeysOff = new SqliteCommand("PRAGMA foreign_keys = OFF;", connection))
        {
            await foreignKeysOff.ExecuteNonQueryAsync(cancellationToken);
        }

        var tables = new List<string>();
        var triggers = new List<(string Name, string Sql)>();
        bool hasSequence;
        await using (var read = new SqliteCommand(ReadSchema, connection))
        await using (SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }

            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                triggers.Add((reader.GetString(0), reader.GetString(1)));
            }

            await reader.NextResultAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            hasSequence = reader.GetInt64(0) > 0;
        }

        // The app's triggers are dropped and recreated around the deletes, so an audit trigger doesn't refill a table and a
        // soft-delete trigger doesn't keep rows. A failure rolls everything back, leaving the connection usable.
        var script = new List<string>();
        script.AddRange(triggers.Select(trigger => $"DROP TRIGGER {Quote(trigger.Name)};"));
        script.AddRange(tables.Select(table => $"DELETE FROM {Quote(table)};"));
        if (hasSequence)
        {
            script.Add("DELETE FROM sqlite_sequence;");
        }

        script.AddRange(triggers.Select(trigger => trigger.Sql + ";"));
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqliteCommand(string.Join(Environment.NewLine, script), connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string Quote(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
}
