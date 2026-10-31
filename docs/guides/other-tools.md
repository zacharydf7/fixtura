# Other tools

Some jobs next to Fixtura are better done by tools built for them. Most below plug in as a resource or a swap, so a
fixture still states its setup once; Verify works on what the fixture's clients return.

## Respawn: a database Fixtura doesn't own

Fixtura's SQL Server, PostgreSQL and SQLite resources reset the databases they create. For one they don't, such as a
shared database the app reaches through its own settings, [Respawn](https://github.com/jbogard/Respawn) empties the
tables in foreign-key order. As a resource, it resets with the fixture's other resources, even before the host is built:

[!code-csharp[](../samples/Bookshop.Tests/Recipes.cs#respawn)]

[!code-csharp[](../samples/Bookshop.Tests/Recipes.cs#respawn-fixture)]

- `Respawner.CreateAsync` reads the schema once, at the first reset; later resets reuse it.
- `TablesToIgnore` keeps reference data, as `KeepOnReset` does for Fixtura's own databases. Keep EF Core's
  `__EFMigrationsHistory` too, which Fixtura's resets always keep: emptied, it makes the next migration recreate tables
  that are still there.
- Fixtures that share the database share its data, and xUnit runs separate class fixtures in parallel. Put every test
  class that uses it in one collection, so one fixture's reset never empties tables under another's test; see
  [Resetting data](resetting-data.md#when-not-to-reset).

## WireMock.Net: a partner on a real port

[Fixtura's HTTP fakes](http-fakes.md) answer the clients the app gets from `IHttpClientFactory`, in-process. A call
from `new HttpClient()`, or from another process, needs a server on a real port. [WireMock.Net](https://github.com/wiremock/WireMock.Net)
runs one; as a resource, the fixture starts it, resets it between tests and stops it:

[!code-csharp[](../samples/Bookshop.Tests/Recipes.cs#wiremock-resource)]

The fixture points the app at it, once the resource has started:

[!code-csharp[](../samples/Bookshop.Tests/Recipes.cs#wiremock-fixture)]

Bookshop reaches the partner through `ShippingClient`, a client from `IHttpClientFactory`, and Fixtura blocks those
unless they're faked, so this fixture lets unfaked clients reach the network. A call from `new HttpClient()` needs no
such line.

A test stubs what it needs, and `ResetDataAsync` clears the stubs and the requests the server saw:

[!code-csharp[](../samples/Bookshop.Tests/Recipes.cs#wiremock-test)]

## Verify: snapshot a response

[Verify](https://github.com/VerifyTests/Verify) compares a value with an approved snapshot file, so a test of a large
response doesn't spell out every field. Read the body with the app's own JSON options first, so the snapshot holds what
a client sees:

```csharp
HttpResponseMessage response = await client.GetAsync("/books");

await Verify(await shop.ReadAsync<List<Book>>(response));
```

- With xUnit v3, add `Verify.XunitV3` and `using static VerifyXunit.Verifier;`.
- Verify replaces dates and GUIDs with placeholders by default, so ids and timestamps the app makes don't break the
  snapshot.
- Recent versions fail the build until the project declares one of Verify's sponsorship options, an MSBuild property
  described in its maintenance-fee docs.

## FakeTimeProvider: time under the test's control

`host.UseFakeClock()` already gives the app Microsoft's `FakeTimeProvider`, and `shop.Clock` is that provider, so its
whole API works on it:

[!code-csharp[](../samples/Bookshop.Tests/Recipes.cs#fake-time-provider)]

`ResetDataAsync` sets the time back to the start, but leaves settings such as `AutoAdvanceAmount` as a test left them,
so a test that changes one sets it back when it's done. See [The clock](clock.md) for what the fake clock reaches, such
as JWT expiry.
