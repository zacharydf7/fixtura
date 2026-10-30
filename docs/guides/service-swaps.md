# Swapping services

`host.Services` changes the app's service registrations after the app has made them:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#service-swaps)]

| Call | What it does |
| --- | --- |
| `Replace<TService, TImplementation>(lifetime?)` | Replaces every registration of the service. Keeps the lifetime the app gave it, unless you pass one, for example `Singleton` so a fake keeps its state for the whole host. |
| `Replace<TService>(instance)` | Replaces every registration with one instance the test can inspect. |
| `ReplaceKeyed<TService, TImplementation>(key)` | Replaces one keyed registration; other keys stay. |
| `Remove<TService>()` | Removes every registration of the service. |
| `RemoveHostedService<THostedService>()` | Removes a hosted service registered with `AddHostedService<T>()` or its factory overload. |
| `DeferHostedService<THostedService>()` | Keeps a hosted service from starting with the host, until a test starts it; see below. |
| `Configure(services => ...)` | Anything else, as plain `IServiceCollection` code. |

## Renamed services fail loudly

Every typed swap throws `NothingMatchedException` when the app has no matching registration. When someone renames
`RestockReminder`, the test run fails at the swap instead of quietly testing with the reminder still running.

## Hosted services under test control

Removing a background worker keeps it out of the way, but then its job is never tested. Defer it instead, and start it
in the test once its data is in place:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#deferred-service)]

- `StartHostedServiceAsync<T>()` starts the service and returns it. A `BackgroundService`'s `ExecuteTask` completes when
  its `ExecuteAsync` returns, so awaiting it waits out a worker that makes one pass. A worker that loops, such as one on
  a timer, runs until the host stops it: check what it did instead, and with [a fake clock](clock.md), advance it.
- The host stops a started service when it stops, as it would any other.
- `host.FailOnBackgroundServiceCrash()` watches the app's background services. Once one has crashed, the fixture's next
  client, scope, reset or disposal fails with the crash, instead of it showing only in the log.

## Outside a fixture

The same swaps, except `DeferHostedService`, which only a fixture can start, exist as `IServiceCollection` extension
methods, for code that builds its own `WebApplicationFactory` or service collection:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#collection-swaps)]
