using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Fixtura;

// Empties one database's tables and restarts their identity columns and the sequences their defaults draw from, leaving
// the schema and the kept tables alone.
internal static class SqlServerReset
{
    // A history table is emptied with the system-versioned table it belongs to, so it isn't listed on its own.
    private const string ListTables = """
        SELECT t.object_id, s.name, t.name, CAST(ic.seed_value AS bigint), CAST(ic.increment_value AS bigint),
            CAST(CASE WHEN ic.last_value IS NULL THEN 0 ELSE 1 END AS bit), hs.name, h.name,
            CASE WHEN t.history_retention_period > 0
                THEN CONCAT(t.history_retention_period, ' ', t.history_retention_period_unit_desc) END,
            CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = 'MigrationId')
                AND EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = 'ProductVersion')
                THEN 1 ELSE 0 END AS bit)
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        LEFT JOIN sys.identity_columns ic ON ic.object_id = t.object_id
        LEFT JOIN sys.tables h ON h.object_id = t.history_table_id
        LEFT JOIN sys.schemas hs ON hs.schema_id = h.schema_id
        WHERE t.is_ms_shipped = 0 AND t.temporal_type <> 1;
        SELECT fk.name, fk.parent_object_id, fk.referenced_object_id, fk.is_disabled FROM sys.foreign_keys fk;
        SELECT tr.parent_id, tr.name FROM sys.triggers tr WHERE tr.parent_class = 1 AND tr.is_disabled = 0;
        SELECT s.name, q.name, dc.parent_object_id
        FROM sys.sequences q
        JOIN sys.schemas s ON s.schema_id = q.schema_id
        JOIN sys.sql_expression_dependencies d ON d.referenced_id = q.object_id
        JOIN sys.default_constraints dc ON dc.object_id = d.referencing_id;
        """;

    // Returns the kept names this database has, so the caller can tell which names match nothing in any database.
    public static async Task<List<string>> ResetAsync(string connectionString, IReadOnlyList<string> keep,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        (List<Table> tables, List<ForeignKey> foreignKeys, List<Trigger> triggers, List<SequenceDefault> defaults) =
            await ReadSchemaAsync(connection, cancellationToken);
        List<string> found = keep.Where(name => tables.Any(table => Names(name, table.Schema, table.Name))).ToList();

        // A name that also matches a table, such as dbo.Prices next to its history table history.Prices, means the table.
        foreach (Table table in tables.Where(table => table.HistoryName is not null))
        {
            string? history = keep.FirstOrDefault(name =>
                !found.Contains(name) && Names(name, table.HistorySchema!, table.HistoryName!));
            if (history is not null)
            {
                throw new InvalidOperationException($"KeepOnReset names {history}, the history table of {table.Schema}." +
                    $"{table.Name}. A history table keeps or loses its rows with its table, so name {table.Schema}.{table.Name}.");
            }
        }

        // EF Core's migrations history always stays, or the app would try to migrate an existing schema again.
        List<Table> cleared = tables
            .Where(table => !table.IsMigrationsHistory && !IsKept(table.Schema, table.Name, keep)).ToList();

        // A sequence restarts only when all the column defaults that draw from it are on emptied tables. One no default draws
        // from, such as EF Core's HiLo, keeps counting: the app still holds the values it reserved.
        var clearedIds = cleared.Select(table => table.Id).ToHashSet();
        List<string> restarted = defaults.GroupBy(sequence => $"{Quote(sequence.Schema)}.{Quote(sequence.Name)}")
            .Where(sequence => sequence.All(use => clearedIds.Contains(use.Table)))
            .Select(sequence => sequence.Key).ToList();
        if (cleared.Count == 0)
        {
            return found;
        }

        await using var command = new SqlCommand(BuildScript(cleared, restarted, foreignKeys, triggers), connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return found;
    }

    private static async Task<(List<Table>, List<ForeignKey>, List<Trigger>, List<SequenceDefault>)> ReadSchemaAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        var tables = new List<Table>();
        var foreignKeys = new List<ForeignKey>();
        var triggers = new List<Trigger>();
        var defaults = new List<SequenceDefault>();
        await using var command = new SqlCommand(ListTables, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(new Table(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetBoolean(9)));
        }

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            foreignKeys.Add(new ForeignKey(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetBoolean(3)));
        }

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            triggers.Add(new Trigger(reader.GetInt32(0), reader.GetString(1)));
        }

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            defaults.Add(new SequenceDefault(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return (tables, foreignKeys, triggers, defaults);
    }

    private static bool IsKept(string schema, string name, IReadOnlyList<string> keep) =>
        keep.Any(entry => Names(entry, schema, name));

    // A kept name is written "Name", "schema.Name", "[Name]" or "[schema].[Name]".
    private static bool Names(string entry, string schema, string name) =>
        new[] { name, $"{schema}.{name}", Quote(name), $"{Quote(schema)}.{Quote(name)}" }
            .Any(spelling => spelling.Equals(entry, StringComparison.OrdinalIgnoreCase));

    private static string BuildScript(List<Table> cleared, List<string> restarted, List<ForeignKey> foreignKeys,
        List<Trigger> triggers)
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

        // Without a value, a sequence restarts at its own start value.
        script.AddRange(restarted.Select(sequence => $"ALTER SEQUENCE {sequence} RESTART;"));
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

    // A migrations history is recognized by EF Core's columns, so one renamed with MigrationsHistoryTable stays too.
    private sealed record Table(int Id, string Schema, string Name, long? Seed, long? Increment, bool IdentityUsed,
        string? HistorySchema, string? HistoryName, string? Retention, bool IsMigrationsHistory)
    {
        public string QuotedName => $"{Quote(Schema)}.{Quote(Name)}";

        public string? History => HistoryName is null ? null : $"{Quote(HistorySchema!)}.{Quote(HistoryName)}";

        public string VersioningOptions => Retention is null
            ? $"HISTORY_TABLE = {History}"
            : $"HISTORY_TABLE = {History}, HISTORY_RETENTION_PERIOD = {Retention}";
    }

    private sealed record ForeignKey(string Name, int Child, int Parent, bool IsDisabled);

    private sealed record Trigger(int Table, string Name);

    // A default constraint on a table that draws from a sequence.
    private sealed record SequenceDefault(string Schema, string Name, int Table);
}
