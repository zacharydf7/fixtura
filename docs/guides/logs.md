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
