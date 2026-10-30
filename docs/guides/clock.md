# The clock

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#fake-clock)]

`host.UseFakeClock(start)` gives the app a `FakeTimeProvider`, from `Microsoft.Extensions.TimeProvider.Testing`, that
stands still at `start`, or at the current time, until a test moves it through the fixture's `Clock` with `Advance` or
`SetUtcNow`.

- It replaces the app's `TimeProvider`, so code that reads `GetUtcNow()` or starts timers through it sees the fake time.
- The app's JWT bearer and cookie handlers check expiry against it, and test users' tokens and cookies are issued at its
  time, so a test can advance past a sign-in's lifetime. An app that validates token lifetimes itself keeps its own
  check.
- `ResetDataAsync` sets it back to its start, so each test that resets starts at the same time. Timers the app set
  keep their due times.
- A host from `WithOverrides` shares its fixture's clock, so moving one moves both.
- Code that reads `DateTime.UtcNow` directly still sees the real time.
