# API reference

Every public type is in the `Fixtura` namespace, whichever package it comes from:

| Package | Types |
| --- | --- |
| `Fixtura` | `TestHost<TProgram>`, `TestHostBuilder`, `ITestHostFeature`, the service swaps, settings, test users, HTTP fakes, `ITestResource` |
| `Fixtura.EntityFrameworkCore` | `DbContextSwaps`, `AppDbOptions`, `DatabaseSetup`, `InMemoryDatabaseReset` |
| `Fixtura.Sqlite` | `SqliteResource`, `SqliteTestHostExtensions` |
| `Fixtura.SqlServer` | `SqlServerResource`, `SqlServerOptions`, `SqlServerSource` |
| `Fixtura.SqlServer.Testcontainers` | `SqlServerContainer` |
| `Fixtura.PostgreSql` | `PostgreSqlResource`, `PostgreSqlOptions`, `PostgreSqlSource` |
| `Fixtura.PostgreSql.Testcontainers` | `PostgreSqlContainer` |

Start with [TestHost&lt;TProgram&gt;](Fixtura.TestHost-1.yml) and [TestHostBuilder](Fixtura.TestHostBuilder.yml).
