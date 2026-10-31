# Settings

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#settings)]

- `Settings(object)` takes an object shaped like `appsettings.json`. Enums are written by name.
- `Settings(json)` takes JSON shaped the same way; comments and trailing commas are allowed.
- `Options<TOptions>(...)` changes the bound options after the app's own configuration and post-configuration, which
  suits typed lists. It reaches every instance, named or not, such as each authentication scheme's or each HTTP
  client's; `Options<TOptions>(name, ...)` changes only the one with that name.
- `Settings(() => new { ... })` makes its object when the host is built, once the fixture's resources are provisioned,
  so it can hold what they provide, such as a database's connection string for code that reads it from configuration.
- Later calls win, key by key, whichever kind they are.

## Changing settings during a test

A test that needs one setting changed doesn't need a host of its own. `ChangeSettingsAsync` changes settings while the
host runs, without a rebuild:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#change-settings)]

- It takes an object or JSON shaped like `appsettings.json`, as `Settings` does, and wins over the fixture's settings.
  Later calls win, key by key.
- It can replace an array the fixture's `Settings` set, whole, as `Settings` does. Any other array throws, naming it:
  set that array in the fixture's `Settings`, even to the app's own values, and a test can then replace it.
- `IConfiguration` and `IOptionsSnapshot` see the change, and so do feature flags read from configuration, such as
  Microsoft.FeatureManagement's. Bookshop reads `ShopOptions` through `IOptionsSnapshot`, so the next order uses the new
  free-shipping price.
- `IOptionsMonitor` sees it for options bound to a configuration section, with `Configure<T>(section)` or
  `BindConfiguration`. Options filled in by a lambda that reads `IConfiguration` have no change to watch, so their
  monitor keeps the old value.
- An `IOptions` value is read once and kept, and so is a setting `Program.cs` read at start-up, such as one that decides
  which endpoints to map. For those, use a [per-test override](kestrel-and-overrides.md#per-test-overrides).
- `ResetDataAsync` brings back the fixture's settings, so a test that resets first never sees another's change. Like
  `ResetDataAsync`, never call it while another test uses the same fixture.

## The environment

`host.Environment = "Testing"` runs the app in that environment, so it loads `appsettings.Testing.json` and its
environment checks see `Testing`. Apps run as Development unless it's set, as WebApplicationFactory runs them.

`host.WebHost(web => ...)` reaches the web host builder itself, for anything the fixture has no member for, such as the
content root or a third-party container's test configuration. It runs after the fixture's other changes, though a key
set there with `UseSetting` still loses to `host.Settings`, which the app reads last.

## When the app reads them

Settings reach `Program.cs` before `builder.Build()`, so code that reads configuration early, such as a feature flag
that decides which endpoints to map, sees them. After `Build()` they win over every source the app has.

## Arrays

An array in the settings replaces the app's array whole: a three-entry list in `appsettings.json` becomes the one entry
you set, not a merge. A later `Settings` call replaces an earlier call's array the same way.

One exception: before `Build()`, the only way in is through command-line arguments, which merge with the app's arrays by
index. Code that reads a list in `Program.cs` before `Build()` sees the merged list; read lists after `Build()`, or use
`Options<TOptions>`.
