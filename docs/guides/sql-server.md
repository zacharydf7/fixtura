# SQL Server

The `Fixtura.SqlServer` package adds a SQL Server resource: each fixture gets its own databases on a test server, and
they're dropped when the fixture is done.

[!code-csharp[](../samples/Bookshop.Tests/SqlServer.cs#sql-fixture)]

## Where the server comes from

| `SqlServerSource` | Server |
| --- | --- |
| `LocalDb` | SQL Server LocalDB's default instance, which ships with Visual Studio on Windows. |
| `EnvironmentVariableOrLocalDb(name)` | The connection string in the variable, or LocalDB when it isn't set or is blank. |
| `Container(image?)` | A SQL Server container; needs `Fixtura.SqlServer.Testcontainers`. |
| `EnvironmentVariableOrContainer(name, image?)` | The variable's server, or a container when it isn't set or is blank. |
| `From(description, resolveAsync)` | A server from your own code. |

The connection string names a server, not a database.

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#custom-source)]

## Databases

`sql.Database("Shop")` returns the connection string for this fixture's `Shop` database, named with the fixture's
prefix. Call it inside the swap's lambda, because the server is only known once the fixture has provisioned it. The app
creates the database, for example by running its migrations or `EnsureCreated` at start-up.

Databases that a killed test run left behind are dropped once they're a day old and, when the login can view
server state, nobody is connected to them.

## When there's no server

Without a reachable server, every test that uses the fixture is skipped with the reason. Set the `RequiredWhenSet`
variable to `true` (or `1`) in CI, and the fixture fails instead.

`ReadyTimeout` (5 seconds unless set) is how long to wait for the server, rounded up to whole seconds. A LocalDB
instance that isn't running yet can need longer on its first connection. Each server is checked once per test run, so
an unreachable one costs the timeout once, not once per fixture.

## In a container

The `Fixtura.SqlServer.Testcontainers` package runs SQL Server in Docker:

[!code-csharp[](../samples/Bookshop.Tests/SqlServer.cs#container-fixture)]

- One container per image starts on first use and is shared by every fixture in the test run; each fixture still gets
  its own databases.
- Testcontainers' resource reaper removes the container when the run ends. With the reaper turned off, as some CI agents
  and Podman setups do, remove it yourself.
- The image is `SqlServerContainer.DefaultImage` (SQL Server 2022) unless given. Without Docker, the fixture's tests
  skip with the reason, or fail when the `RequiredWhenSet` variable is set.

## Resetting

`ResetDataAsync` empties the fixture's tables between tests; see [Resetting data](resetting-data.md).
