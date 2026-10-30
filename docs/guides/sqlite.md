# SQLite in memory

The `Fixtura.Sqlite` package gives each fixture SQLite in-memory databases: relational, with keys, constraints and real
SQL, and with no server to install. EF Core's own docs recommend SQLite in memory over its in-memory provider for tests.

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#sqlite)]

- `UseSqliteInMemory()` adds the resource; `sqlite.Database("Shop")` names a database with the fixture's prefix. Call it
  inside the swap's lambda, because the names are only known once the fixture has been provisioned.
- An in-memory SQLite database vanishes when its last connection closes, and EF Core closes its connections between
  operations. The resource holds one connection open per database for the fixture's lifetime, so the data stays, and
  closes it when the fixture is done, so the database goes.
- The app creates its schema as it does on any database, for example with `EnsureCreated` or
  [`MigrateBeforeStart`](dbcontext-swaps.md#migrations-and-the-model).
- `ResetDataAsync` empties every table, keeping the schema and EF Core's migrations history. Full-text and spatial
  indexes empty with their tables, and the app's triggers don't run. Rows the model seeds with `HasData` go too, so add
  reference data with [`SeedOnReset`](resetting-data.md#seeding-after-a-reset).
- The native SQLite library comes from the app's provider, such as `Microsoft.EntityFrameworkCore.Sqlite`. Resets need
  SQLite 3.37 or later, which EF Core 10's provider ships.

Two setups aren't supported: SpatiaLite, whose metadata tables a reset would empty, and FTS5 tables that store no text
or keep it in another table (`content=''` or `content='Table'`), which a reset can't empty.

SQLite isn't SQL Server: it has its own SQL dialect and types. When the app depends on SQL Server's behavior, such as
its collation or its date functions, test against [SQL Server](sql-server.md) too.
