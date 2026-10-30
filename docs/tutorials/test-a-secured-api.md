# Test a secured API

Bookshop's `POST /orders` needs a signed-in user with the `Customer` role. It stamps each order with the time, and
charges shipping unless the book costs more than a configured amount. This tutorial tests who may order and what an
order records, starting from the fixture in [Getting started](../getting-started.md).

## Move shared setup into features

More fixtures are coming, and each needs the in-memory database and the quiet background. A feature is setup that
several fixtures share. This tutorial's code goes in one file, which starts with them:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#features)]

`RecreateInMemoryDatabasesOnReset()` lets `ResetDataAsync` empty the in-memory database, back to the catalog Bookshop
seeds; without it, the in-memory provider keeps every test's data. A later tutorial resets before each test.

A fixture adds features with `host.Use<TFeature>()`. Adding the same feature twice runs it once, so features can build
on each other without double-registering anything.

## Control the clock and the settings

Orders record `TimeProvider.GetUtcNow()`. To check that time, the fixture gives the app a clock that stands still. The
fixture for this tutorial combines the features, that clock and one setting:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#customer-fixture)]

- `host.Settings` takes an object shaped like `appsettings.json`. Setting `FreeShippingOver` to 0 makes every order ship
  free, so these tests never call the shipping partner. A later tutorial fakes that partner instead.
- `host.UseFakeClock(Now)` replaces the app's `TimeProvider` with one that stays at `Now` until a test moves it with
  `shop.Clock.Advance(...)`. See [The clock](../guides/clock.md).

## Anonymous and signed-in users

The tests use the fixture through `IClassFixture`:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#order-tests)]

- Without a user, the client is anonymous, so the first test's order is turned away as unauthorized.
- `TestUser.Named(...)` signs a user in through the app's own JWT bearer setup: Fixtura issues a token with the app's
  signing key, issuer and audience, so the app validates it as it would a real one. The second test's user lacks the
  `Customer` role and is forbidden.
- The third test's customer places an order, which records who placed it and when.

The whole file is [SecuredApi.cs](https://github.com/zacharydf7/fixtura/blob/main/docs/samples/Bookshop.Tests/SecuredApi.cs).

## What you have

- One fixture class per host shape, each a few lines, built from shared features.
- Tests that sign in as whoever they need, through the app's real authentication.
- Settings and services changed once, in the fixture, not in each test.

Apps that sign in with cookies, or check each request against an ASP.NET Identity store, work the same way with a line
of setup: see [Test users](../guides/test-users.md).

## Next

The in-memory database doesn't enforce what SQL Server does, such as foreign keys and decimal precision.
[Test against SQL Server](test-against-sql-server.md) moves the tests to a real database.
