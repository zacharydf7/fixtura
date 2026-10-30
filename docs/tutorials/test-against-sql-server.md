# Test against SQL Server

This tutorial runs Bookshop's order tests against a real SQL Server, gives each fixture its own databases, and resets
the data between tests. It needs the `Fixtura.SqlServer` package:

```
dotnet add package Fixtura.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
```

## A database per fixture

[!code-csharp[](../samples/Bookshop.Tests/SqlServer.cs#sql-fixture)]

- `UseSqlServer` adds a resource that's provisioned before the fixture's first test.
  `EnvironmentVariableOrLocalDb("FIXTURA_SQLSERVER")` takes a server from that variable, or uses LocalDB when it isn't
  set. Pick your own variable names.
- `sql.Database("Shop")` names a database with the fixture's own prefix. Call it inside the swap's lambda, because the
  server is only known once the fixture has provisioned it.
- Bookshop creates its schema at start-up with `EnsureCreated`, and that now happens in the fixture's database. An app
  that runs migrations at start-up does the same.
- When the fixture is done, its databases are dropped.

When no server is reachable, every test that uses the fixture is skipped with the reason; no test calls a skip helper.
In CI, set the `RequiredWhenSet` variable to `true` so a missing server fails the build instead.

## Reset the data before each test

Tests that share a fixture share its databases. Without a reset, an order one test placed shows up in the next test's
list, and the tests start depending on their order. Reset before each test with xUnit's `IAsyncLifetime`:

[!code-csharp[](../samples/Bookshop.Tests/SqlServer.cs#reset)]

`ResetDataAsync` empties every table in the fixture's databases in foreign-key order and restarts identity columns. It
deletes rows rather than recreating databases, so it takes milliseconds. Now each test sees only its own orders:

[!code-csharp[](../samples/Bookshop.Tests/SqlServer.cs#mine)]

Because identity columns restart, the first order in every test is order 1:

[!code-csharp[](../samples/Bookshop.Tests/SqlServer.cs#numbering)]

## Keep reference data

Bookshop seeds its two books with `HasData`, so `EnsureCreated` inserts them once, at start-up. A reset would empty
`Books` too, which is why the fixture lists it in `KeepOnReset`:

[!code-csharp[](../samples/Bookshop.Tests/SqlServer.cs#kept)]

EF Core's migrations history is always kept, so an app that migrates at start-up doesn't try to migrate again.

## No SQL Server on the machine?

The `Fixtura.SqlServer.Testcontainers` package runs SQL Server in Docker. Swap the source, and nothing else changes:

[!code-csharp[](../samples/Bookshop.Tests/SqlServer.cs#container-fixture)]

`EnvironmentVariableOrContainer` uses the variable's server when it's set, such as a CI service, and otherwise starts
one container for the whole test run, shared by every fixture.

## Next

[Fake a partner API](fake-a-partner-api.md) tests the orders that do charge shipping, without calling the real
shipping partner.
