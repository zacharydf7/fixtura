# Test a secured API

Bookshop's `POST /orders` needs a signed-in user with the `Customer` role. It stamps each order with the time, and
charges shipping unless the book costs more than a configured amount. This tutorial tests who may order and what an
order records, starting from the fixture in [Getting started](../getting-started.md).

## Move shared setup into features

More fixtures are coming, and each needs the in-memory database and the quiet background. A feature is setup that
several fixtures share:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#features)]

A fixture adds features with `host.Use<TFeature>()`. Adding the same feature twice runs it once, so features can build
on each other without double-registering anything.

## Control the clock and the settings

Orders record `TimeProvider.GetUtcNow()`. To check that time, replace the clock with one that stands still:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#clock)]

The fixture for this tutorial combines the features, a fixed clock and one setting:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#customer-fixture)]

- `host.Settings` takes an object shaped like `appsettings.json`. Setting `FreeShippingOver` to 0 makes every order ship
  free, so these tests never call the shipping partner. A later tutorial fakes that partner instead.
- `Replace<TimeProvider>(instance)` replaces every registration of the clock with one instance.

## Anonymous and signed-in users

Without a user, the client is anonymous:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#anonymous)]

`TestUser.Named(...)` signs a user in through the app's own JWT bearer setup: Fixtura issues a token with the app's
signing key, issuer and audience, so the app validates it as it would a real one. A user without the role is turned
away:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#forbidden)]

And a customer's order records who placed it and when:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#customer)]

## What you have

- One fixture class per host shape, each a few lines, built from shared features.
- Tests that sign in as whoever they need, through the app's real authentication.
- Settings and services changed once, in the fixture, not in each test.

Apps that sign in with cookies, or check each request against an ASP.NET Identity store, work the same way with a line
of setup: see [Test users](../guides/test-users.md).

## Next

The in-memory database doesn't enforce what SQL Server does, such as foreign keys and decimal precision.
[Test against SQL Server](test-against-sql-server.md) moves the tests to a real database.
