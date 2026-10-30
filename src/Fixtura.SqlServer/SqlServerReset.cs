using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Fixtura;

// Empties one database's tables and restarts their identity columns, leaving the schema and the kept tables alone.
internal static class SqlServerReset
{
    // A history table is emptied with the system-versioned table it belongs to, so it isn't listed on its own.
    private const string ListTables = """
        SELECT t.object_id, s.name, t.name, CAST(ic.seed_value AS bigint), CAST(ic.increment_value AS bigint),
            CAST(CASE WHEN ic.last_value IS NULL THEN 0 ELSE 1 END AS bit), hs.name, h.name,
            CASE WHEN t.history_retention_period > 0
                THEN CONCAT(t.history_retention_period, ' ', t.history_retention_period_unit_desc) END
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        LEFT JOIN sys.identity_columns ic ON ic.object_id = t.object_id
        LEFT JOIN sys.tables h ON h.object_id = t.history_table_id
        LEFT JOIN sys.schemas hs ON hs.schema_id = h.schema_id
        WHERE t.is_ms_shipped = 0 AND t.temporal_type <> 1;
        SELECT fk.name, fk.parent_object_id, fk.referenced_object_id, fk.is_disabled FROM sys.foreign_keys fk;
        SELECT tr.parent_id, tr.name FROM sys.triggers tr WHERE tr.parent_class = 1 AND tr.is_disabled = 0;
        """;

    public static async Task ResetAsync(string connectionString, IReadOnlyList<string> keep)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        (List<Table> tables, List<ForeignKey> foreignKeys, List<Trigger> triggers) = await ReadSchemaAsync(connection);
        List<Table> cleared = tables.Where(table => !IsKept(table, keep)).ToList();
        if (cleared.Count == 0)
        {
            return;
        }

        await using var command = new SqlCommand(BuildScript(cleared, foreignKeys, triggers), connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(List<Table>, List<ForeignKey>, List<Trigger>)> ReadSchemaAsync(SqlConnection connection)
    {
        var tables = new List<Table>();
        var foreignKeys = new List<ForeignKey>();
        var triggers = new List<Trigger>();
        await using var command = new SqlCommand(ListTables, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(new Table(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.GetBoolean(5),
                reader.IsDBNull(7) ? null : $"{Quote(reader.GetString(6))}.{Quote(reader.GetString(7))}",
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            foreignKeys.Add(new ForeignKey(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetBoolean(3)));
        }

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            triggers.Add(new Trigger(reader.GetInt32(0), reader.GetString(1)));
        }

        return (tables, foreignKeys, triggers);
    }

    // EF Core's migrations history always stays, or the app would try to migrate an existing schema again.
    private static bool IsKept(Table table, IReadOnlyList<string> keep) =>
        table.Name.Equals("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)
        || keep.Any(name => name.Equals(table.Name, StringComparison.OrdinalIgnoreCase)
            || name.Equals($"{table.Schema}.{table.Name}", StringComparison.OrdinalIgnoreCase));

    private static string BuildScript(List<Table> cleared, List<ForeignKey> foreignKeys, List<Trigger> triggers)
    {
        (List<Table> ordered, List<Table> cyclic) = DeleteOrder(cleared, foreignKeys);

        // Tables that reference each other in a cycle have no safe order, so their enabled keys pause while they empty.
        var cyclicIds = cyclic.Select(table => table.Id).ToHashSet();
        List<ForeignKey> paused = foreignKeys
            .Where(key => !key.IsDisabled && cyclicIds.Contains(key.Child) && cyclicIds.Contains(key.Parent))
            .ToList();
        Dictionary<int, Table> byId = cleared.ToDictionary(table => table.Id);

        // Deleting from a system-versioned table copies its rows into history, where restarted ids would match them, so
        // versioning pauses while both empty.
        List<Table> versioned = cleared.Where(table => table.History is not null).ToList();

        // The app's delete triggers would refill audit tables or keep soft-deleted rows, so the enabled ones pause too.
        List<Trigger> quiet = triggers.Where(trigger => byId.ContainsKey(trigger.Table)).ToList();
        string TriggerOnTable(Trigger trigger) =>
            $"{Quote(byId[trigger.Table].Schema)}.{Quote(trigger.Name)} ON {byId[trigger.Table].QuotedName}";

        var script = new List<string> { "SET XACT_ABORT ON;", "BEGIN TRANSACTION;" };
        script.AddRange(versioned.Select(table => $"ALTER TABLE {table.QuotedName} SET (SYSTEM_VERSIONING = OFF);"));
        script.AddRange(paused.Select(key => $"ALTER TABLE {byId[key.Child].QuotedName} NOCHECK CONSTRAINT {Quote(key.Name)};"));
        script.AddRange(quiet.Select(trigger => $"DISABLE TRIGGER {TriggerOnTable(trigger)};"));
        script.AddRange(ordered.Concat(cyclic).Select(table => $"DELETE FROM {table.QuotedName};"));
        // EXEC compiles the delete once versioning is off; compiled with the batch, it would still see a history table.
        script.AddRange(versioned.Select(table => $"EXEC (N'DELETE FROM {table.History!.Replace("'", "''")}');"));
        script.AddRange(quiet.Select(trigger => $"ENABLE TRIGGER {TriggerOnTable(trigger)};"));
        script.AddRange(paused.Select(key =>
            $"ALTER TABLE {byId[key.Child].QuotedName} WITH CHECK CHECK CONSTRAINT {Quote(key.Name)};"));
        script.AddRange(versioned.Select(table => $"ALTER TABLE {table.QuotedName} SET (SYSTEM_VERSIONING = ON ({table.VersioningOptions}));"));

        // Reseeding to one step before the seed makes the next row get the seed, as in a new table. The number is
        // written invariantly: some cultures write a minus sign T-SQL doesn't accept.
        script.AddRange(cleared.Where(table => table.IdentityUsed).Select(table =>
            string.Create(CultureInfo.InvariantCulture, $"DBCC CHECKIDENT ('{table.QuotedName.Replace("'", "''")}', "
                + $"RESEED, {checked(table.Seed!.Value - table.Increment!.Value)}) WITH NO_INFOMSGS;")));
        script.Add("COMMIT;");
        return string.Join(Environment.NewLine, script);
    }

    // Referencing tables empty before the tables they reference. A table referencing itself empties in one statement.
    private static (List<Table> Ordered, List<Table> Cyclic) DeleteOrder(List<Table> cleared, List<ForeignKey> foreignKeys)
    {
        var remaining = cleared.ToDictionary(table => table.Id);
        var ordered = new List<Table>();
        bool progress = true;
        while (progress)
        {
            progress = false;
            foreach (Table table in remaining.Values.ToList())
            {
                bool referenced = foreignKeys.Any(key =>
                    key.Parent == table.Id && key.Child != table.Id && remaining.ContainsKey(key.Child));
                if (!referenced)
                {
                    ordered.Add(table);
                    remaining.Remove(table.Id);
                    progress = true;
                }
            }
        }

        return (ordered, remaining.Values.ToList());
    }

    private static string Quote(string name) => $"[{name.Replace("]", "]]")}]";

    private sealed record Table(int Id, string Schema, string Name, long? Seed, long? Increment, bool IdentityUsed,
        string? History, string? Retention)
    {
        public string QuotedName => $"{Quote(Schema)}.{Quote(Name)}";

        public string VersioningOptions => Retention is null
            ? $"HISTORY_TABLE = {History}"
            : $"HISTORY_TABLE = {History}, HISTORY_RETENTION_PERIOD = {Retention}";
    }

    private sealed record ForeignKey(string Name, int Child, int Parent, bool IsDisabled);

    private sealed record Trigger(int Table, string Name);
}
