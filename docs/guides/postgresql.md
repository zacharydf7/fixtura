# PostgreSQL

The `Fixtura.PostgreSql` package gives each fixture its own PostgreSQL databases, on the same model as the
[SQL Server resource](sql-server.md):

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#postgresql)]

- `UsePostgreSql` adds a resource that's provisioned before the fixture's first test. `sql.Database("Shop")` names a
  database with the fixture's prefix; call it inside the swap's lambda, because the server is only known once the fixture
  has provisioned it. The app creates the database, as EF Core's `EnsureCreated` or migrations do.
- When the fixture is done, its databases are dropped, even with connections still open.
- Without a reachable server, the fixture's tests skip with the reason. Set the `RequiredWhenSet` variable to `true` in CI
  so a missing server fails the build instead.

## Where the server comes from

| `PostgreSqlSource` | Server |
| --- | --- |
| `EnvironmentVariable(name)` | The connection string in the variable; none when it isn't set or is blank. |
| `Container(image?)` | A PostgreSQL container; needs `Fixtura.PostgreSql.Testcontainers`. |
| `EnvironmentVariableOrContainer(name, image?)` | The variable's server, or a container when it isn't set or is blank. |
| `From(description, resolve)` | A server your own code provides. |

A container starts on first use and is shared by every fixture in the test run, with a random password. Its image is
`PostgreSqlContainer.DefaultImage`, PostgreSQL 17, unless given.

## Resetting data

`ResetDataAsync` truncates every table in the fixture's databases in one statement, so foreign keys don't need an order:

- Identity columns, sequences an emptied table's column owns, such as a serial's, and sequences that only emptied tables'
  column defaults draw from, start over. A sequence the app draws from itself, such as EF Core's HiLo, keeps counting.
- The app's delete triggers don't run.
- EF Core's migrations history keeps its rows, whatever its table's name, and so do the tables in
  `PostgreSqlOptions.KeepOnReset`, written as `Table`, `schema.Table` or `"schema"."Table"`. A name that matches no
  table throws. A kept table can't reference an emptied one.
- Tables an extension owns, such as PostGIS's `spatial_ref_sys`, keep their rows.

## Leftovers

PostgreSQL doesn't record when a database was created, so databases that a killed test run left behind aren't swept:
drop the `fixtura_*` databases by hand. A container's databases go with the container.
