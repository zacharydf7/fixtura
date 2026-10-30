# Settings

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#settings)]

- `Settings(object)` takes an object shaped like `appsettings.json`. Enums are written by name.
- `Settings(json)` takes JSON shaped the same way; comments and trailing commas are allowed.
- `Options<TOptions>(...)` changes the bound options after the app's own configuration and post-configuration, which
  suits typed lists.
- Later calls win, key by key.

## When the app reads them

Settings reach `Program.cs` before `builder.Build()`, so code that reads configuration early, such as a feature flag
that decides which endpoints to map, sees them. After `Build()` they win over every source the app has.

## Arrays

An array in the settings replaces the app's array whole: a three-entry list in `appsettings.json` becomes the one entry
you set, not a merge. A later `Settings` call replaces an earlier call's array the same way.

One exception: before `Build()`, the only way in is through command-line arguments, which merge with the app's arrays by
index. Code that reads a list in `Program.cs` before `Build()` sees the merged list; read lists after `Build()`, or use
`Options<TOptions>`.
