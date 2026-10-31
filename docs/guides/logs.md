# The app's logs

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#logs)]

`LogToTestOutput()` writes the app's logs to the output of the test whose request caused them. xUnit shows a test's
output when it fails, so a 500 comes with the app's own explanation.

- The app's own `Logging` settings still decide what's logged. Change them for tests with `host.Settings`, as above,
  where EF Core's SQL commands are added to the output.
- `minimumLevel` (`Information` unless given) raises the floor on top of the app's settings:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#logs-warnings)]

- The fixture's clients add a header to each request that ties it to its test, which is how a log finds its test, on
  the test server and over Kestrel. The app never sees the header and behaves as it does without logging.
- Logs that belong to no test become xUnit diagnostic messages: start-up's, a background service's, and those the server
  writes around the app's middleware, such as each request's start and finish lines or Kestrel's report of an exception
  the app didn't handle. They show when diagnostic messages are turned on, for example with `"diagnosticMessages": true`
  in `xunit.runner.json`.

Logging to test output is off unless a fixture asks for it.

## Asserting on logs

`host.CaptureLogs()` keeps what the app logs for each test, so a test can check what the app logged, such as the line
Bookshop writes for each order:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#capture-logs)]

`shop.Logs.Current` holds the running test's `FakeLogRecord`s, oldest first: those the app logged while handling the
test's requests through the fixture's clients, and those of work the test ran itself, such as a service it called. The
app's own `Logging` settings filter them, as they do the test output. Logs that belong to no test, such as start-up's
or a background service's, aren't kept.

## Failing on app errors

A response can look fine while the app logged an exception it swallowed. `host.FailOnAppErrors()` turns an error the
app logs while handling a request from the fixture's clients into a failure: the request throws in the test, listing
the errors. Errors a test expects can pass:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#fail-on-app-errors)]

An unhandled exception that becomes a 500 is logged as an error too, so a test that expects a 500 needs `except`. The
check runs once the app starts its response, without waiting for a body that may stream without end, so errors logged
after that, or outside such a request, such as in a background service, don't fail anything. `FailOnAppErrors` captures
logs as `CaptureLogs` does.

## Serilog and other logger factories

Serilog's `UseSerilog` and `AddSerilog` replace the app's logger factory with Serilog's, which ignores the providers a
test adds unless it's told otherwise, so neither the test output nor `CaptureLogs` and `FailOnAppErrors` would see the
app's logs. When the app's factory isn't the framework's, Fixtura sends it one critical log
once the host is built, which the app's other sinks see too. If that log doesn't reach the test output, the build fails
with an error that names the fix, instead of the output staying empty. Pass `writeToProviders: true`:

```csharp
builder.Services.AddSerilog((services, logger) => logger.ReadFrom.Configuration(builder.Configuration),
    writeToProviders: true);
```
