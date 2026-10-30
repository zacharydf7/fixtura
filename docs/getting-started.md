# Getting started

> [!NOTE]
> Fixtura is pre-release, and its packages aren't on nuget.org yet. Until they are, reference the projects in `src/`
> from a clone of the repository.

This page sets up integration tests for an existing ASP.NET Core app. The examples test Bookshop, a small minimal API
with a catalog, orders and JWT sign-in. Its code is in
[docs/samples/Bookshop](https://github.com/zacharydf7/fixtura/tree/main/docs/samples/Bookshop).

## 1. Create the test project

```
dotnet new install xunit.v3.templates
dotnet new xunit3 -o Bookshop.Tests -f net10.0
cd Bookshop.Tests
dotnet add reference ../Bookshop/Bookshop.csproj
dotnet add package Fixtura
dotnet add package Fixtura.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.InMemory
```

Every Fixtura type is in the `Fixtura` namespace, so one `using Fixtura;` is all a test file needs. The samples add it
once for the project with `<Using Include="Fixtura" />` in the project file.

The tests reach the app through its `Program` class, which .NET 10 makes public for apps with top-level statements.

`dotnet test` runs xUnit v3 tests on Microsoft.Testing.Platform once a `global.json` next to the solution says so:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

## 2. Write a fixture

A fixture describes one shape of host: what the tests change from production. Bookshop stores its catalog in SQL
Server and sends a restock reminder every hour. For a first test, an in-memory database and no background work are
enough:

[!code-csharp[](samples/Bookshop.Tests/GettingStarted.cs#fixture)]

- `ReplaceDbContext` points the app's `ShopDb` at an in-memory database, whatever way the app registered it.
- `host.UniqueName("Shop")` gives this fixture instance its own database, so fixtures never share data.
- `RemoveHostedService` takes the reminder out of the host.
- Each change throws if it matches nothing in the app, so renaming `RestockReminder` fails the test run instead of
  quietly leaving the reminder in.

`Configure` only records changes. The host is built the first time a test uses the fixture.

## 3. Write a test

[!code-csharp[](samples/Bookshop.Tests/GettingStarted.cs#first-test)]

`IClassFixture<ShopFixture>` shares one host among the tests in the class. `CreateClientAsync()` returns a client for
the app; pass a `TestUser` to sign in, as the next tutorial shows.

## 4. Run it

```
dotnet test
```

On Microsoft.Testing.Platform, filter with `--filter-class` and `--filter-method`.

## Next

[Test a secured API](tutorials/test-a-secured-api.md) signs users in, changes settings and moves shared setup into
features.
