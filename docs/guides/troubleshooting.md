# Troubleshooting

## A test was skipped

The skip reason names the resource, why it's unavailable and where the server came from, such as "SQL Server not
reachable at (localdb)\MSSQLLocalDB: ... The server came from the FIXTURA_SQLSERVER variable, or LocalDB when it isn't
set." or "Couldn't get a SQL Server from a SQL Server container (mcr.microsoft.com/mssql/server:2022-latest): ...". Start
the server, set the variable it names, or install Docker for a container source. In CI, set the `RequiredWhenSet`
variable so the same problem fails the build.

## The first test waits several seconds

That's usually a server that doesn't answer: the resource waits up to `ReadyTimeout` once per test run before skipping.
A stopped LocalDB instance also takes a few seconds to start on its first connection; raise `ReadyTimeout` if it skips.

## SQL commands time out when several test projects run

SQL Server creates databases one at a time. When several test projects with SQL fixtures run in parallel against one
server, especially LocalDB, the last databases can wait past the 30-second command timeout. Run the test projects one at
a time with `dotnet test --max-parallel-test-modules 1`; it's often faster overall.

## NothingMatchedException

A typed swap, a DbContext swap or `Http.Fake<TClient>()` found nothing to change in the app. The service or client was
probably renamed or removed. Update the swap, or remove it if the app no longer has the service. The message lists
every swap that matched nothing, so one run shows all that a rename broke.

## A list setting has the app's entries mixed in

Code that reads a list in `Program.cs` before `builder.Build()` sees settings merged with the app's by index, because
command-line arguments are the only early channel. Read the list after `Build()`, or use `host.Options<TOptions>`. See
[Settings](settings.md).

## A request to a fake throws "has no response for"

No response matches the request's method and path. The message lists the method and path of each response the fake
has, so a typo such as `/quote` against `/quotes` shows. Paths start with `/` and are matched exactly, including the
query when the response's path has one. Add a response, or a catch-all with `Respond(status)`.

## "is not provisioned yet"

A resource's `Database(...)` was called directly in `Configure`, before the fixture provisioned the resource. Call it
inside a lambda that runs when the host is built, such as the one given to `ReplaceDbContext` or `Settings(() => ...)`.

## The app returns 500 and the reason is unclear

Turn on [`host.LogToTestOutput()`](logs.md): the app's logs then show with the failing test. Under
`WebApplicationFactory` the app runs in the Development environment, so the response body also carries the exception
page.

## Data from one test shows up in another

Tests that share a fixture share its data. Reset it before each test with [`ResetDataAsync`](resetting-data.md), or
give the tests their own fixture. On EF Core's in-memory provider, the reset only empties the databases if the fixture
calls `host.RecreateInMemoryDatabasesOnReset()`.
