using Npgsql;

namespace Fixtura;

// Empties one database's tables and restarts their identity columns and the sequences their defaults draw from, leaving
// the schema and the kept tables alone.
internal static class PostgreSqlReset
{
    // Partitions empty with their parent table, and copy its defaults, so they're left to it. Tables an extension owns, such
    // as PostGIS's spatial_ref_sys, stay.
    private const string ReadSchema = """
        SELECT c.oid, n.nspname, c.relname,
            EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'MigrationId' AND NOT a.attisdropped)
            AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'ProductVersion' AND NOT a.attisdropped)
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p') AND NOT c.relispartition
            AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\_%'
            AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e');
        SELECT DISTINCT sn.nspname, s.relname, ad.adrelid
        FROM pg_depend d
        JOIN pg_attrdef ad ON d.classid = 'pg_attrdef'::regclass AND d.objid = ad.oid
        JOIN pg_class s ON d.refclassid = 'pg_class'::regclass AND d.refobjid = s.oid AND s.relkind = 'S'
        JOIN pg_namespace sn ON sn.oid = s.relnamespace
        JOIN pg_class t ON t.oid = ad.adrelid
        WHERE NOT t.relispartition;
        """;

    // Returns the kept names this database has, so the caller can tell which names match nothing in any database.
    public static async Task<List<string>> ResetAsync(string connectionString, IReadOnlyList<string> keep)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var tables = new List<(uint Id, string Schema, string Name, bool IsMigrationsHistory)>();
        var defaults = new List<(string Sequence, uint Table)>();
        await using (var read = new NpgsqlCommand(ReadSchema, connection))
        await using (NpgsqlDataReader reader = await read.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add((reader.GetFieldValue<uint>(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
            }

            await reader.NextResultAsync();
            while (await reader.ReadAsync())
            {
                defaults.Add(($"{Quote(reader.GetString(0))}.{Quote(reader.GetString(1))}", reader.GetFieldValue<uint>(2)));
            }
        }

        List<string> found = keep.Where(name => tables.Any(table => Names(name, table.Schema, table.Name))).ToList();

        // EF Core's migrations history always stays, or the app would try to migrate an existing schema again.
        var cleared = tables
            .Where(table => !table.IsMigrationsHistory && !keep.Any(name => Names(name, table.Schema, table.Name)))
            .ToList();
        if (cleared.Count == 0)
        {
            return found;
        }

        // A sequence restarts when all the column defaults that draw from it are on emptied tables, or when an emptied
        // table's column owns it, as a serial's does. One no default draws from, such as EF Core's HiLo, keeps counting: the
        // app still holds the values it reserved.
        var clearedIds = cleared.Select(table => table.Id).ToHashSet();
        IEnumerable<string> restarted = defaults.GroupBy(use => use.Sequence)
            .Where(sequence => sequence.All(use => clearedIds.Contains(use.Table)))
            .Select(sequence => $"ALTER SEQUENCE {sequence.Key} RESTART;");

        // One statement empties the tables in any order, without the app's delete triggers, and restarts the identity
        // columns and sequences they own. Without CASCADE, a kept table that references an emptied one fails the reset.
        string truncate = "TRUNCATE TABLE "
            + string.Join(", ", cleared.Select(table => $"{Quote(table.Schema)}.{Quote(table.Name)}")) + " RESTART IDENTITY;";
        await using var command = new NpgsqlCommand(string.Join(Environment.NewLine, [truncate, .. restarted]), connection);
        await command.ExecuteNonQueryAsync();
        return found;
    }

    // A kept name is written "Name", "schema.Name", "\"Name\"" or "\"schema\".\"Name\"".
    private static bool Names(string entry, string schema, string name) =>
        new[] { name, $"{schema}.{name}", Quote(name), $"{Quote(schema)}.{Quote(name)}" }
            .Any(spelling => spelling.Equals(entry, StringComparison.OrdinalIgnoreCase));

    private static string Quote(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
}
