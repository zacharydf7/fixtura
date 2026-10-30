# Getting started

This page sets up integration tests for an existing ASP.NET Core app. The examples test Bookshop, a small minimal API
with a catalog, orders and JWT sign-in. Its code is in
[docs/samples/Bookshop](https://github.com/zacharydf7/fixtura/tree/main/docs/samples/Bookshop).

## 1. Create the test project

Run these in the folder that holds the app's project folder. If the app already has a solution there, skip
`dotnet new sln` and the `dotnet sln add` line for the app. If it already has a `global.json` there, skip the `mv`
line: add the `test` section below to that file and delete `Bookshop.Tests/global.json` instead.

```
dotnet new install xunit.v3.templates
dotnet new sln -n Bookshop
dotnet sln add Bookshop/Bookshop.csproj
dotnet new xunit3 -o Bookshop.Tests -f net10.0
dotnet sln add Bookshop.Tests/Bookshop.Tests.csproj
mv Bookshop.Tests/global.json .
cd Bookshop.Tests
dotnet add reference ../Bookshop/Bookshop.csproj
dotnet add package Fixtura
dotnet add package Fixtura.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.InMemory
```

The template writes a `global.json` into `Bookshop.Tests` that runs the tests on Microsoft.Testing.Platform.
`dotnet test` reads it from the folder it runs in or above, so the `mv` (`move` in cmd) puts it next to the solution;
left in the test project, `dotnet test` on the solution fails with "Testing with VSTest target is no longer supported".
The file holds one section:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

Every Fixtura type is in the `Fixtura` namespace, so one `using Fixtura;` covers them all. The template already adds
`Xunit` for the whole project.

The tests reach the app through its `Program` class, which .NET 10 makes public for apps with top-level statements.

## 2. Write a fixture

A fixture describes one shape of host: what the tests change from production. Bookshop stores its catalog in SQL
Server and sends a restock reminder every hour. For a first test, an in-memory database and no background work are
enough. Start a file, `BookTests.cs`, with the fixture:

[!code-csharp[](samples/Bookshop.Tests/GettingStarted.cs#fixture)]

- `ReplaceDbContext` points the app's `ShopDb` at an in-memory database, whatever way the app registered it.
- `host.UniqueName("Shop")` gives this fixture instance its own database, so fixtures never share data.
- `RemoveHostedService` takes the reminder out of the host.
- Each change throws if it matches nothing in the app, so renaming `RestockReminder` fails the test run instead of
  quietly leaving the reminder in.

`Configure` only records changes. The host is built the first time a test uses the fixture.

## 3. Write a test

Add the test class below the fixture:

[!code-csharp[](samples/Bookshop.Tests/GettingStarted.cs#first-test)]

`IClassFixture<ShopFixture>` shares one host among the tests in the class. `CreateClientAsync()` returns a client for
the app; pass a `TestUser` to sign in, as the next tutorial shows.

## 4. Run it

```
dotnet test
```

On Microsoft.Testing.Platform, filter with `--filter-class` and `--filter-method`.

The whole file is [GettingStarted.cs](https://github.com/zacharydf7/fixtura/blob/main/docs/samples/Bookshop.Tests/GettingStarted.cs).

## Next

[Test a secured API](tutorials/test-a-secured-api.md) signs users in, changes settings and moves shared setup into
features.
