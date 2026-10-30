# Swapping DbContexts

The `Fixtura.EntityFrameworkCore` package points the app's DbContexts at test databases:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#dbcontext-swaps)]

## Any registration shape

One call works however the app registered the context: `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` or
`AddPooledDbContextFactory`. Only the options configuration is replaced, so the app's lifetimes and pooling stay.

A swap throws when the context's options are registered by hand instead of through EF Core's `Add...` methods, because
it couldn't reach them and the tests would run against the app's own database.

## Keep or drop the app's options

| `AppDbOptions` | What happens |
| --- | --- |
| `Drop` (default) | The context is configured from scratch by your lambda, provider options included. |
| `Keep` | The app's own configuration still runs, such as interceptors, and only the database provider is replaced. On the app's own provider, the app's migrations and command settings fill what your lambda leaves unset. |

With `Keep`, the app's configuration runs as it would in production. If it throws, for example because a connection
string is missing, supply the value with [`host.Settings`](settings.md).

Provider options are the ones set inside the provider call, such as `UseSqlServer(..., sql => sql.MigrationsAssembly(...))`:
the migrations assembly, retries and the command timeout. With `Drop`, an app whose migrations live in another assembly
needs `MigrationsAssembly` in the lambda too, or its `Migrate()` finds no migrations. With `Keep` and the same provider,
the app's migrations assembly and history table, command timeout and retries apply wherever your lambda doesn't set its
own. Everything else, such as the connection and, for SQL Server, the engine `UseAzureSql` picks, comes from your lambda,
because it describes the test's database.

## Contexts a library keeps internal

`ReplaceDbContexts(assembly, ...)` reaches every registered context whose type is declared in that assembly, including
internal ones a library registers for itself:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#library-contexts)]

Each swap replaces the context's options configuration, so a later swap of the same context wins over an earlier one.

## Naming in-memory databases

Name in-memory databases with `host.UniqueName(...)`, so fixtures don't share data. A host from `WithOverrides` gets the
same names, so it shares its fixture's data.

For SQL Server, use the [SQL Server resource](sql-server.md) instead, which gives each fixture its own databases.
