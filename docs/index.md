---
_layout: landing
---

# Fixtura

Integration tests for ASP.NET Core apps, composed from small features instead of a fixture inheritance tree. A test
project states once what differs from production, and no test repeats setup.

Fixtura builds on `WebApplicationFactory<TProgram>` and targets xUnit v3 on .NET 10. It covers the jobs every team
writes by hand:

- **Typed service swaps** that keep the app's lifetimes and fail loudly when a renamed service no longer matches.
- **Settings as objects**, seen by `Program.cs` before `Build()`, where an array replaces the app's array whole.
- **Test users** signed in through the app's own JWT or cookie authentication.
- **HTTP fakes** for the app's outbound clients, which record what the app sent.
- **SQL Server** databases per fixture, from LocalDB, a CI service or a container, that skip tests locally when there's
  no server and fail them in CI.
- **Data resets** between tests that share a fixture, without recreating databases.
- **The app's logs** in the output of the test that caused them.

## A test project in two classes

[!code-csharp[](samples/Bookshop.Tests/GettingStarted.cs#fixture)]

[!code-csharp[](samples/Bookshop.Tests/GettingStarted.cs#first-test)]

## Install

> [!NOTE]
> Fixtura is pre-release, and these packages aren't on nuget.org yet.

```
dotnet add package Fixtura
dotnet add package Fixtura.EntityFrameworkCore
dotnet add package Fixtura.SqlServer
dotnet add package Fixtura.SqlServer.Testcontainers
```

`Fixtura` is the core. Add the others when the app uses EF Core, when tests need a real SQL Server, and when that
server should run in Docker.

## Where to go next

- [Getting started](getting-started.md) sets up a test project for an existing app in five minutes.
- The [tutorials](tutorials/test-a-secured-api.md) build up the tests for a small bookshop API, one topic at a time.
- The [guides](guides/fixtures-and-features.md) cover each feature in full.
- The [API reference](api/index.md) lists every type and member.
