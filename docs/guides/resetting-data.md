# Resetting data

Tests that share a fixture also share its data. Reset it before each test that needs a clean start:

[!code-csharp[](../samples/Bookshop.Tests/SqlServer.cs#reset)]

`ResetDataAsync` resets, in order:

1. Each resource, such as the SQL Server resource.
2. The [HTTP fakes](http-fakes.md): recorded requests are cleared, and responses set in `Configure` come back.
3. The steps added with `host.OnReset(...)`, in the order they were added. Each runs in a scope of its own, so it can
   use scoped services such as a DbContext. They only run once the host has been built.

## SQL Server

The SQL Server resource empties every table in the fixture's databases and restarts identity columns:

- Tables empty in foreign-key order. Tables that reference each other in a loop pause those keys while they empty, and
  the keys are checked again afterward.
- System-versioned tables lose their history too.
- EF Core's migrations history keeps its rows, and so do the tables in `SqlServerOptions.KeepOnReset`, written as
  `"Table"` or `"schema.Table"`. List reference data the app inserts there.
- The schema stays, so the app doesn't migrate again.

## EF Core in-memory databases

`host.RecreateInMemoryDatabasesOnReset()`, from `Fixtura.EntityFrameworkCore`, deletes and recreates every in-memory
database the app's contexts use, so only data seeded with `HasData` is left.

## Your own steps and resources

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#on-reset)]

A custom resource resets its own data in `ResetAsync`:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#custom-resource)]

## Test users

Users the seeder created are gone after a reset of their store, and they're seeded again at their next sign-in.

## When not to reset

Never reset while another test uses the fixture. Tests sharing a class or collection fixture run one at a time, but
tests sharing an assembly fixture can run in parallel.
