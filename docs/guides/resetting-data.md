# Resetting data

Tests that share a fixture also share its data. Reset it before each test that needs a clean start:

[!code-csharp[](../samples/Bookshop.Tests/SqlServer.cs#reset)]

`ResetDataAsync` resets, in order:

1. Each resource, such as the SQL Server, PostgreSQL or SQLite resource.
2. The [HTTP fakes](http-fakes.md): recorded requests are cleared, and responses set in `Configure` come back.
3. The steps added with `host.OnReset(...)`, in the order they were added. Each runs in a scope of its own, so it can
   use scoped services such as a DbContext. They only run once the host has been built.

## SQL Server

The SQL Server resource empties every table in the fixture's databases and restarts identity columns:

- Tables empty in foreign-key order. Tables that reference each other in a loop pause those keys while they empty, and
  the keys are checked again afterward.
- System-versioned tables lose their history too.
- The app's delete triggers don't run, so an audit trigger doesn't refill a table and a soft-delete trigger doesn't keep
  rows. Triggers the app disabled stay disabled.
- EF Core's migrations history keeps its rows, whatever its table's name, and so do the tables in
  `SqlServerOptions.KeepOnReset`, written as `"Table"`, `"schema.Table"` or `"[schema].[Table]"`. List reference data
  the app inserts there.
- A `KeepOnReset` name that matches no table in the fixture's databases throws, so a typo can't empty the table it
  meant to keep without anyone noticing.
- A sequence restarts when only emptied tables draw from it in a column default, as with EF Core's TPC keys. A sequence
  the app draws from itself, such as EF Core's HiLo, keeps counting, because the app still holds values it reserved.
- The schema stays, so the app doesn't migrate again.

## PostgreSQL

The [PostgreSQL resource](postgresql.md) truncates every table except EF Core's migrations history and `KeepOnReset`,
and restarts identity columns and the sequences only those tables' defaults draw from. The app's delete triggers don't
run.

## SQLite in memory

The [SQLite resource](sqlite.md) empties every table in its in-memory databases, keeping the schema and EF Core's
migrations history. Full-text tables that store their own text, and spatial indexes, empty with their tables, and
the app's triggers don't run. Rows the
model seeds with `HasData` go too; add reference data with `SeedOnReset`.

## Seeding after a reset

`host.SeedOnReset<TContext>(db => ...)`, from `Fixtura.EntityFrameworkCore`, runs as the host starts, after the
migrations and before any of the app's workers, and after every reset, and saves what it adds. Use it for reference data
the tests need, instead of keeping tables with `KeepOnReset`. The schema must exist before the app's hosted services
start, from `MigrateBeforeStart` or `CreateDatabasesOnStart`, or created in Program.cs before the app runs. Reset steps
run in the order they're added, so add it after `RecreateInMemoryDatabasesOnReset`, which would otherwise delete the
seed.

## EF Core in-memory databases

`host.RecreateInMemoryDatabasesOnReset()`, from `Fixtura.EntityFrameworkCore`, deletes and recreates every in-memory
database the app's contexts use, so only data seeded with `HasData` is left.

## Your own steps and resources

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#on-reset)]

A custom resource resets its own data in `ResetAsync`. `ProvisionAsync` gets the fixture's name prefix and a
cancellation token, and returns `TestResourceStatus.Ready`, or `TestResourceStatus.Unavailable(reason)` to skip the
fixture's tests:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#custom-resource)]

## Test users

Users the seeder created are gone after a reset of their store, and they're seeded again at their next sign-in.

## When not to reset

Never reset while another test uses the fixture. Tests sharing a class or collection fixture run one at a time, but
tests sharing an assembly fixture can run in parallel.

xUnit 4.0's `parallelMode: all`, set in `xunit.runner.json`, on the command line or with
`[assembly: Parallelization(Mode = ParallelMode.All)]`, runs tests in the same class in parallel too. A class whose tests
reset a shared fixture then needs them run one at a time again: mark it `[TestClass(DisableParallelism = true)]`. Fixtura
can't tell which mode is on, so it can't warn you; a reset under a running test shows up as data that vanishes mid-test.
