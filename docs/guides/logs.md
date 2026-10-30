# The app's logs

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#logs)]

`LogToTestOutput()` writes the app's logs to the output of the test whose request caused them. xUnit shows a test's
output when it fails, so a 500 comes with the app's own explanation.

- The app's own `Logging` settings still decide what's logged. Change them for tests with `host.Settings`, as above,
  where EF Core's SQL commands are added to the output.
- `minimumLevel` (`Information` unless given) raises the floor on top of the app's settings:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#logs-warnings)]

- Each request runs in its test's execution context, which is how a log finds its test. The test's culture and other
  ambient state reach the app too.
- Logs that belong to no test, such as start-up's, a background service's or those of a request over Kestrel, become
  xUnit diagnostic messages. They show when diagnostic messages are turned on, for example with `"diagnosticMessages":
  true` in `xunit.runner.json`.

Logging to test output is off unless a fixture asks for it.

## Serilog and other logger factories

Serilog's `UseSerilog` and `AddSerilog` replace the app's logger factory with Serilog's, which ignores the providers a
test adds unless it's told otherwise. When the app's factory isn't the framework's, Fixtura sends it one critical log
once the host is built, which the app's other sinks see too. If that log doesn't reach the test output, the build fails
with an error that names the fix, instead of the output staying empty. Pass `writeToProviders: true`:

```csharp
builder.Services.AddSerilog((services, logger) => logger.ReadFrom.Configuration(builder.Configuration),
    writeToProviders: true);
```
