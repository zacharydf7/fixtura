# Kestrel and per-test overrides

## Kestrel

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#kestrel)]

`ServerKind.Kestrel` runs the app on Kestrel on a free port instead of the in-memory test server. `BaseAddress` is
where a browser, such as one driven by Playwright, or another process reaches it. Signed-in clients from
`CreateClientAsync` work as before, except that Kestrel serves plain HTTP: an app whose cookie is Secure stops
receiving it once it renews the cookie.

Requests from the fixture's clients log to their test over Kestrel too. Requests from anywhere else, such as a browser
at `BaseAddress`, belong to no test, so their logs become diagnostic messages; see [The app's logs](logs.md).

## Per-test overrides

A few tests sometimes need a host that differs from the fixture's. `WithOverrides` builds a second host from the
fixture's recorded changes plus a few more:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#overrides)]

- The override shares the fixture's resources and database names, so it sees the same data.
- It can't add resources; add them in the fixture's `Configure`.
- Each override costs a full host build. Dispose it with `await using`, and keep overrides for the few tests that need
  them; a shape many tests need deserves its own fixture. The fixture disposes any override still running when it's
  disposed itself.
