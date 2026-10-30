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

## What a fixture offers tests

| Member | What it does |
| --- | --- |
| `CreateClientAsync(user?)` | A client for the app, signed in as the user or anonymous |
| `Services` | The app's root services |
| `CreateScope()` | A scope for resolving scoped services such as a DbContext |
| `ResetDataAsync()` | Puts the fixture's data back to its starting state; see [Resetting data](resetting-data.md) |
| `WithOverrides(...)` | A second host with extra changes for a few tests; see [Kestrel and per-test overrides](kestrel-and-overrides.md) |
| `BaseAddress` | Where a browser or another process reaches a Kestrel-hosted app |
