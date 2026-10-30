# Fixtures and features

## A fixture per host shape

A fixture is a class that derives from `TestHost<TProgram>`, where `TProgram` is any public type in the app's entry
assembly, usually `Program`. Override `Configure` to record what differs from production:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#customer-fixture)]

Write one fixture for each shape of host the tests need: one with the background services off, one that adds a fake
partner, one on a real database. Tests then pick the shape they need.

- `Configure` runs once, in the fixture's `InitializeAsync`, before any resource such as a database server is
  provisioned. It only records changes.
- The host is built the first time a test uses the fixture, through `CreateClientAsync`, `Services` or `CreateScope`.
- A fixture is disposed after its last test: first the app, then its resources, so databases are dropped only after the
  app has closed its connections.

## Sharing a fixture

Fixtures are ordinary xUnit fixtures:

- `IClassFixture<TFixture>` shares one host among the tests in one class.
- `ICollectionFixture<TFixture>` shares it among the classes in a collection. Tests in one collection run one at a
  time.
- xUnit v3's `AssemblyFixture` shares it with the whole test assembly. Tests in different collections can then use it in
  parallel, so don't reset its data while they run; see [Resetting data](resetting-data.md).

## Features

A feature is setup that several fixtures share. Implement `ITestHostFeature` and add it with `host.Use<TFeature>()`:

[!code-csharp[](../samples/Bookshop.Tests/SecuredApi.cs#features)]

Adding the same feature twice runs it once. That lets one feature use another, and lets a fixture add both without
caring that one already includes the other.

A feature that takes settings of its own is added as an instance: `host.Use(new TenantFeature("north"))`. The same
instance added twice runs once.

## Fixture hooks

A fixture can override two hooks for setup and cleanup of its own:

- `OnInitializedAsync` runs once `Configure` has run and the resources are provisioned, before the first test. The host
  isn't built yet, and it doesn't run when an unavailable resource skips the fixture's tests.
- `OnDisposingAsync` runs as the fixture is disposed, before the app and its resources are. If it throws, the app and
  the resources are still disposed, and the error is thrown after.

## What a fixture offers tests

| Member | What it does |
| --- | --- |
| `CreateClientAsync(user?)` | A client for the app, signed in as the user or anonymous |
| `CreateClientAsync(user, options)` | The same, built with `TestClientOptions`; see below |
| `CreateHandlerAsync(user?)` | A handler for the app, signed in as the user, for clients that take one, such as SignalR's |
| `SignInAsync(user)` | The user's bearer token or cookie, for a client the fixture doesn't build, such as a browser on Kestrel |
| `Services` | The app's root services |
| `CreateScope()` | A scope for resolving scoped services such as a DbContext |
| `CreateScopeAsync()` | The same, built without blocking a thread; dispose it with `await using` |
| `HttpFake(name)`, `HttpFake<TClient>()` | The fake the fixture set up for that client |
| `Resource<TResource>()` | The one resource of that type the fixture added, such as its `SqlServerResource` |
| `EventuallyAsync(services => ...)` | Runs an assertion in a new scope until it passes, for work the app finishes later; see below |
| `StartHostedServiceAsync<T>()` | Starts a hosted service the fixture deferred; see [Swapping services](service-swaps.md) |
| `ResetDataAsync()` | Puts the fixture's data back to its starting state; see [Resetting data](resetting-data.md) |
| `WithOverrides(...)` | A second host with extra changes for a few tests; see [Kestrel and per-test overrides](kestrel-and-overrides.md) |
| `BaseAddress` | Where a browser or another process reaches a Kestrel-hosted app |

## Waiting for work the app finishes later

After a request returns, the app may still be working: a background service drains a queue, an outbox sends mail.
Instead of a `Task.Delay`, wait for the result:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#eventually)]

- `EventuallyAsync` runs the assertion in a new scope every few milliseconds until it passes. After five seconds, or the
  timeout you give, the assertion's own last failure fails the test, with its usual message.
- With a background service crashing while it waits, it stops at once with the crash, when the fixture uses
  `host.FailOnBackgroundServiceCrash()`.
- A fake clock stays put unless you pass `advanceClockBy`, which moves it that much between attempts, so a timer the app
  waits on comes due.
- For a request the app sends through a faked client, `WaitForRequestAsync` on the fake does the same; see
  [Faking outbound HTTP](http-fakes.md).

## Client options

Most tests use the default client. `TestClientOptions` changes it for the few that need something else:

| Option | What it does |
| --- | --- |
| `AllowAutoRedirect` | `false` returns redirects instead of following them, so a test can assert a 302 or a redirect to the login page |
| `BaseAddress` | Where relative request URIs resolve, such as a path base |
| `Handlers` | `DelegatingHandler`s each request passes through first, outermost first; give each client new ones |
| `Scheme` | The scheme this client's user signs in through; see [Test users](test-users.md) |
