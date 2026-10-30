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
| `Configure(services => ...)` | Anything else, as plain `IServiceCollection` code. |

## Renamed services fail loudly

Every typed swap throws `NothingMatchedException` when the app has no matching registration. When someone renames
`RestockReminder`, the test run fails at the swap instead of quietly testing with the reminder still running.

## Outside a fixture

The same swaps exist as `IServiceCollection` extension methods, for code that builds its own `WebApplicationFactory` or
service collection:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#collection-swaps)]
