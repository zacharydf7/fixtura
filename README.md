# Fixtura

Compose ASP.NET Core integration-test hosts from small, reusable features instead of a fixture inheritance tree. A test
project states once what differs from production; no test repeats setup.

Fixtura wraps `WebApplicationFactory<TProgram>` and targets xUnit v3 on .NET 10. It covers the jobs every team rebuilds
by hand:

- **Typed service swaps** that keep the app's lifetimes and fail loudly when a renamed service no longer matches.
- **Settings as objects**, seen by `Program.cs` before `Build()`, where an array replaces the app's array whole.
- **Test users** signed in through the app's own JWT or cookie authentication, events included.
- **Infrastructure** that skips tests when it's missing locally and fails them in CI, with no skip call in any test.
- **Data resets** between tests that share a fixture, without recreating databases.
- **HTTP fakes** for the app's outbound clients, which record what the app sent.
- **The app's logs** in the output of the test that caused them.

**Documentation:** <https://zacharydf7.github.io/fixtura/>, with a getting-started guide, tutorials and the API
reference. This README is the short overview.

```
dotnet add package Fixtura
dotnet add package Fixtura.EntityFrameworkCore
dotnet add package Fixtura.Sqlite
dotnet add package Fixtura.SqlServer
dotnet add package Fixtura.SqlServer.Testcontainers
dotnet add package Fixtura.PostgreSql
dotnet add package Fixtura.PostgreSql.Testcontainers
```

`Fixtura` is the core. Add the others when the app uses EF Core, when tests need a relational database without a
server, when they need a real SQL Server or PostgreSQL, and when that server should run in Docker.

## A fixture per host shape

Write one small fixture class for each shape of host your tests need. Tests share it through xUnit's class, collection or
assembly fixtures.

```csharp
public sealed class ApiFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Settings(new { Features = new { Reports = true } });
        host.Services
            .RemoveHostedService<OutboxWorker>()
            .Replace<IOrderPublisher, FakeOrderPublisher>();
    }
}

public class OrderTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetMe_ShouldReturnTheUser_WhenSignedIn()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Manager"));

        HttpResponseMessage response = await client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

`Configure` only records changes. The host is built the first time a test uses the fixture, through `CreateClientAsync`,
`Services` or `CreateScope`.

## Features

Move setup that several fixtures share into a feature. Adding the same feature twice runs it once, so features can build
on each other.

```csharp
public sealed class QuietBackground : ITestHostFeature
{
    public void Configure(TestHostBuilder host) =>
        host.Services.RemoveHostedService<OutboxWorker>().RemoveHostedService<ReportScheduler>();
}

public sealed class ApiFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Use<QuietBackground>().Use<FakeBus>();
}
```

A feature that takes settings of its own is added as an instance, `host.Use(new TenantFeature("north"))`; the same
instance added twice runs once.

A test reaches what the fixture set up without a property for each: `api.HttpFake("rates")`, `api.HttpFake<TClient>()`
and `api.Resource<SqlServerResource>()`. A fixture can override `OnInitializedAsync`, which runs once its resources are
provisioned, and `OnDisposingAsync`, which runs before the app and its resources are disposed. `await
api.CreateScopeAsync()` builds the host without blocking a thread.

## Swapping services

| Call | What it does |
| --- | --- |
| `Replace<TService, TImplementation>(lifetime?)` | Replaces every registration of the service, keeping the lifetime the app gave it unless you pass one |
| `Replace<TService>(instance)` | Replaces every registration with one instance the test can inspect |
| `ReplaceKeyed<TService, TImplementation>(key)` | Replaces one keyed registration; other keys stay |
| `Remove<TService>()` | Removes every registration of the service |
| `RemoveHostedService<THostedService>()` | Removes a hosted service registered with `AddHostedService<T>()` or its factory overload |
| `DeferHostedService<THostedService>()` | Keeps a hosted service from starting with the host, until a test calls `api.StartHostedServiceAsync<T>()` |
| `Record<TService>()` | Replaces an interface, such as an email sender, with a stand-in whose calls a test reads with `api.Recorder<TService>().Calls` |
| `Configure(services => ...)` | Anything else, as plain `IServiceCollection` code |

Every typed swap throws `NothingMatchedException` when the app has no matching registration, so a renamed service fails
the test run instead of silently testing the wrong thing. The same swaps, except `DeferHostedService`, exist as
`IServiceCollection` extension methods for code that builds its own factory.

`await api.EventuallyAsync(services => Assert...)` runs an assertion in a new scope until it passes, for work the app
finishes after a request returns; after five seconds, or the timeout you give, its last failure is the test's. Pass
`advanceClockBy` to move a fake clock between attempts, so a timer the app waits on comes due.

A deferred service starts when a test has put its data in place, and the host still stops it. A `BackgroundService`'s
`ExecuteTask` completes when its `ExecuteAsync` returns, so awaiting it waits out a worker that makes one pass; a worker
that loops runs until the host stops it. `host.FailOnBackgroundServiceCrash()` fails the
fixture's next client, scope, reset or disposal once one of the app's background services has crashed.

## Swapping DbContexts

The `Fixtura.EntityFrameworkCore` package points the app's DbContexts at test databases. EF Core's in-memory provider
isn't relational, so for a database without a server prefer SQLite in memory, below.

```csharp
host.Services
    .ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders")))
    .ReplaceDbContext<AuditedDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Audit")), AppDbOptions.Keep)
    .ReplaceDbContexts(typeof(SomeLibraryType).Assembly, db => db.UseInMemoryDatabase(host.UniqueName("Library")));
```

- One call works for every registration shape: `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` and
  `AddPooledDbContextFactory`. Only the options configuration is replaced, so the app's lifetimes and pooling stay.
- `AppDbOptions.Drop` (the default) configures the context from scratch, provider options included: an app whose
  migrations live in another assembly needs `MigrationsAssembly` in the lambda. `AppDbOptions.Keep` still runs the app's
  own configuration, such as interceptors, and replaces only the database provider; on the app's own provider, the app's
  migrations assembly and history table, command timeout and retries fill whatever the lambda leaves unset. If the
  app's configuration throws, for example on a missing connection string, supply the value with `host.Settings`.
- `ReplaceDbContexts(assembly, ...)` reaches every registered context whose type is declared in that assembly, including
  ones a library keeps internal.
- A swap throws when the context's options are registered by hand rather than through EF's `Add...` methods, because it
  couldn't reach them and the tests would run against the app's database.
- Name in-memory databases with `host.UniqueName(...)`, so fixtures don't share data. An override from `WithOverrides`
  gets the same names, so it shares its fixture's data.

### SQLite in memory

```csharp
SqliteResource sqlite = host.AddSqliteInMemory();
host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseSqlite(sqlite.ConnectionString("Orders")));
```

- A relational database with no server, which EF Core's docs recommend over its in-memory provider for tests: keys,
  constraints and SQL behave as a database does.
- Each name is a database of its own for the fixture, kept alive by a connection the resource holds, so it doesn't
  vanish when the app closes its connections, and gone when the fixture is done.
- `ResetDataAsync` empties its tables, keeping the schema and EF Core's migrations history; the app's triggers don't
  run. Rows the model seeds with `HasData` go too, so add reference data with `SeedOnReset`.
- The app's SQLite provider, such as `Microsoft.EntityFrameworkCore.Sqlite`, brings the native library.

### Migrations, seeding and the model

```csharp
host.MigrateBeforeStart<OrdersDbContext>();
host.SeedOnReset<OrdersDbContext>(db => db.Countries.Add(new Country { Name = "North" }));
api.Services.AssertNoPendingModelChanges<OrdersDbContext>();   // in a test
```

- `MigrateBeforeStart` applies the context's migrations as the host starts, before any hosted service starts, so a
  worker never sees a missing table.
- `SeedOnReset` runs as the fixture's host starts, after the migrations and before any worker, and after every
  `ResetDataAsync`, and saves what it adds, for reference data the tests need. The schema must exist by then: from
  `MigrateBeforeStart`, or created in Program.cs before the app runs.
- `AssertNoPendingModelChanges` throws when the model has changes no migration covers, so a forgotten migration fails a
  test.

## Settings

```csharp
host.Settings(new
{
    Features = new { Reports = true },
    Shipping = new { Carriers = new[] { new { Name = "Test" } } }
});
host.Settings("""{ "Features": { "Reports": true } }""");
host.Options<ShippingOptions>(shipping => shipping.Carriers = [new Carrier { Name = "Test" }]);
```

- `Settings` takes an object or JSON shaped like appsettings.json. JSON may contain comments and trailing commas, and
  enums are written by name.
- `Settings(() => new { ... })` makes its object when the host is built, after the fixture's resources are provisioned,
  so it can hold what they provide:
  `host.Settings(() => new { ConnectionStrings = new { Main = sql.ConnectionString("Main") } })`.
- Settings reach `Program.cs` before `builder.Build()`, so code that reads configuration early sees them. After
  `Build()` they win over every app source.
- An array in the overlay replaces the app's array whole, so a 3-entry list in appsettings.json becomes the 1 entry you
  set. A later `Settings` call replaces an earlier call's array the same way. Before `Build()`, arrays still merge by
  index with the app's, because the only early channel is command-line arguments; read lists after `Build()`.
- `Options<TOptions>` changes the bound options after the app's own post-configuration, which suits typed lists. It
  reaches every instance, named or not, such as each authentication scheme's; `Options<TOptions>(name, ...)` changes
  one.
- `host.Environment = "Testing"` runs the app in that environment, so it loads `appsettings.Testing.json`; it's
  Development unless set. `host.WebHost(web => ...)` reaches the web host builder for anything else, such as the
  content root; it runs after the fixture's other changes.

## The clock

```csharp
host.UseFakeClock(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
api.Clock.Advance(TimeSpan.FromDays(30));   // in a test
```

`UseFakeClock` replaces the app's `TimeProvider` with a `FakeTimeProvider` that stands still until a test advances it.
The app's JWT and cookie handlers check expiry against it, and test users' tokens and cookies are issued at its time, so
a test can let a sign-in expire. `ResetDataAsync` sets it back to its start, and a host from `WithOverrides` shares
it.

## Test users

```csharp
HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Manager").WithClaim("tenant", "north"));
HttpClient anonymous = await api.CreateClientAsync();
HttpClient noRedirects = await api.CreateClientAsync(null, new TestClientOptions { AllowAutoRedirect = false });
```

`TestClientOptions` also sets a client's `BaseAddress`, its own `DelegatingHandler`s and the `Scheme` its user signs in
through. For clients the fixture doesn't build, `api.CreateHandlerAsync(user)` returns a signed-in handler, such as for
SignalR's `HttpMessageHandlerFactory`, and `api.SignInAsync(user)` returns the user's `BearerToken` or `Cookie`, such as
for a WebSocket or a browser, which connect to `api.BaseAddress` on `ServerKind.Kestrel`.

The user signs in through the app's own authentication handler, so its token or cookie events and account checks still
run, through the app's default authenticate scheme unless `host.Users.Scheme` names another, as an app whose default
forwards to others needs. Choose how with `host.Users.SignIn`:

| `UserSignIn` | How |
| --- | --- |
| `AppJwtBearer` (default) | A bearer token signed with the app's own `JwtBearerOptions` key, issuer and audience, with the algorithm its key type needs (HMAC, RSA or ECDsa). Apps that trust an Authority, or pin only public keys, trust a test signing key as well, so no metadata request leaves the process. |
| `AppCookie` | An authentication cookie protected with the app's own cookie options, the one its login page would issue. With a cookie `SessionStore`, the ticket goes into the app's store and the cookie carries its session id. The app's security-stamp check runs on the first request, and its sign-out works. Clients talk to the test server over HTTPS, so a Secure cookie keeps working after the app renews it. |

Apps that check each request against their user store need the user to exist there. Register a seeder:

```csharp
host.Users.SeedWith<IdentityUserSeeder<AppUser, IdentityRole>>();
```

The seeder runs before each sign-in, one at a time per fixture. Give a user a fixed id with `TestUser.WithId(id)` when
tests check ownership; the seeder then finds the user by id, even after the app renamed it.
`IdentityUserSeeder<TUser, TRole>` creates an ASP.NET Identity user, or gives an existing one the test user's roles and
claims, and returns the claims Identity signs in. A role or claim that an earlier sign-in added is removed when this one
doesn't ask for it, so a user that one test made a `Manager` is a plain user again in the next. Roles and claims the app
gave the user stay. Override `NewUser` to set your own fields on a user it creates, or implement `ITestUserSeeder` for
any other store.

## Faking outbound HTTP

Answer the app's `IHttpClientFactory` clients in-process and check what it sent:

```csharp
public sealed class ApiFixture : TestHost<Program>
{
    public HttpFake Rates { get; private set; } = null!;

    protected override void Configure(TestHostBuilder host)
    {
        Rates = host.Http.Fake("rates").Respond(HttpMethod.Get, "/today/EUR", HttpStatusCode.OK, new { Value = 1.1 });
        host.Http.Fake<TrackingClient>().Respond(HttpStatusCode.OK, new { Status = "Delivered" });
    }
}

FakeHttpRequest sent = api.Rates.Requests[^1];
Assert.Equal("sample-key", sent.Headers["X-Api-Key"]);
```

- `Fake(name)` fakes a named client and `Fake<TClient>()` a typed one; a typed client the app doesn't register throws
  `NothingMatchedException`. A typed client registered under a name of its own needs `Fake(name)`. Only the primary
  handler is replaced, so the app's own handlers, such as retries and auth headers, still run.
  `host.Http.Resilience = FakedClientResilience.Remove` takes resilience handlers, such as
  `AddStandardResilienceHandler`'s retries, off faked clients, so a fake's 503 reaches the app at once.
- `Respond(method, path, FakeResponse...)` answers in order, for retries: statuses, `ConnectionFailure()`, `Timeout()`
  and `.After(delay)`; a `when: request => ...` condition matches by header or body, and `VerifyAllResponsesUsed()`
  checks every response the test set was sent.
- `Respond(method, path, status, json)` answers one route, and `Respond(status, json)` or `Respond(request => ...)`
  answers everything else. The latest matching response wins. A request nothing answers throws, naming the request.
- `Requests` lists what the app sent: method, URI, headers and body, with `Json<T>()` to read the body.
  `WaitForRequestAsync(request => ...)` waits for one the app sends later, such as from a background service.
- Requests through clients without a fake throw, naming the client, so a forgotten fake or one under the wrong name
  can't reach the network. `host.Http.Unfaked = UnfakedHttpClients.Allow` lets them through. Clients the app creates
  with `new HttpClient()` can't be faked.
- `ResetDataAsync` clears the recorded requests and brings back the responses set in `Configure`, undoing ones a test
  set. In `WithOverrides`, faking a client again replaces the fixture's fake for that host only.

## The app's logs

```csharp
host.LogToTestOutput();                 // Information and above
host.LogToTestOutput(LogLevel.Warning); // or a higher minimum
```

- Each request's logs go to the output of the test that sent it, which xUnit shows when the test fails, so a 500 comes
  with the app's reason. The app's own `Logging` settings still filter them; change them for tests with `host.Settings`.
- A header the app never sees ties each request to its test, so the app behaves as it does without logging.
- Logs that belong to no test, such as start-up's, a background service's or the server's own request start and finish
  lines, become xUnit diagnostic messages, which show when diagnostic messages are turned on.
- An app with a logger factory of its own, as Serilog sets up, fails the host build with an error that names the fix
  unless the factory passes logs on to other providers: with Serilog, `UseSerilog(..., writeToProviders: true)`.
- `host.CaptureLogs()` keeps each test's logs for it to assert on: `api.Logs.Current` holds the running test's
  `FakeLogRecord`s. `host.FailOnAppErrors(except: ...)` makes a request throw in the test when the app logs an error
  while handling it, so a 200 that hid a swallowed exception fails.

## Missing infrastructure

A resource, such as a database server, is provisioned in the fixture's `InitializeAsync`. When it's unavailable, every
test that uses the fixture is skipped with the resource's reason, and no test has to call a skip helper. A resource can
also be required, for example in CI, and then the fixture fails instead. Implement `ITestResource` and add it with
`host.AddResource(...)`: `ProvisionAsync` gets the fixture's name prefix and a cancellation token in a
`TestResourceContext`, and returns `TestResourceStatus.Ready` or `TestResourceStatus.Unavailable(reason)`, or throws
when the resource is required. Resources provision in the order they are added, so one can build on another, such as
migrations on a database server; the first unavailable one skips the rest.

### SQL Server

The `Fixtura.SqlServer` package adds a SQL Server resource. Each fixture gets its own databases, and they're dropped when
the fixture is done.

```csharp
SqlServerResource sql = host.AddSqlServer(new SqlServerOptions
{
    Source = SqlServerSource.EnvironmentVariableOrLocalDb("TEST_SQLSERVER"),
    RequiredWhenVariableIsTrue = "TEST_SQLSERVER_REQUIRED"
});
host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseSqlServer(sql.ConnectionString("Orders")));
```

- `SqlServerSource.EnvironmentVariableOrLocalDb(name)` takes a server connection string from the variable, or uses
  LocalDB when it isn't set or is blank. `SqlServerSource.LocalDb` always uses LocalDB, and so does
  `host.AddSqlServer()` with no options.
- `sql.ConnectionString("Orders")` names a database with the fixture's prefix. Call it inside the swap's lambda, because
  the server is only known once the fixture has provisioned it. The app creates the database, for example by running its
  migrations or `EnsureCreated` at startup.
- Without a reachable server, every test that uses the fixture is skipped with the reason. Set the
  `RequiredWhenVariableIsTrue` variable to `true` in CI, and the fixture fails instead.
- `ReadyTimeout` (5 seconds unless set) is how long to wait for the server, rounded up to whole seconds; it must be
  positive. A LocalDB instance that isn't running yet can need longer on its first connection. The server is checked
  once per test run, so an unreachable one costs the timeout once, not once per fixture.
- Databases that a killed test run left behind are dropped once they're a day old and, when the login can view
  server state, nobody is connected to them.
- `SqlServerSource.From(description, async cancellationToken => ...)` takes a server from your own code. Returning null,
  or throwing, skips the fixture's tests with the reason, or fails them when the `RequiredWhenVariableIsTrue` variable
  is `true`.

### SQL Server in a container

The `Fixtura.SqlServer.Testcontainers` package runs SQL Server in Docker, so a machine without one still runs the SQL
tests:

```csharp
SqlServerResource sql = host.AddSqlServer(new SqlServerOptions
{
    Source = SqlServerSource.EnvironmentVariableOrContainer("TEST_SQLSERVER"),
    RequiredWhenVariableIsTrue = "TEST_SQLSERVER_REQUIRED"
});
```

- `SqlServerSource.Container(image)` starts one container on first use and shares it with every fixture in the test
  run; each fixture still gets its own databases. Testcontainers' resource reaper removes the container when the run
  ends; with the reaper turned off, as some CI agents and Podman setups do, remove it yourself.
- `SqlServerSource.EnvironmentVariableOrContainer(name, image)` uses the variable's server when it's set, such as a CI
  service, and a container otherwise.
- The image is `SqlServerContainer.DefaultImage` (SQL Server 2022) unless given. Without Docker, the fixture's tests
  skip with the reason, or fail when the `RequiredWhenVariableIsTrue` variable is `true`.

### PostgreSQL

```csharp
PostgreSqlResource postgres = host.AddPostgreSql(new PostgreSqlOptions
{
    Source = PostgreSqlSource.EnvironmentVariableOrContainer("TEST_POSTGRES"),
    RequiredWhenVariableIsTrue = "TEST_POSTGRES_REQUIRED",
    KeepOnReset = ["Countries"]
});
host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseNpgsql(postgres.ConnectionString("Orders")));
```

- `Fixtura.PostgreSql` works like the SQL Server resource: one prefixed database per name per fixture, dropped when the
  fixture is done, and the fixture's tests skip without a server or fail when the `RequiredWhenVariableIsTrue` variable
  is `true`.
- `PostgreSqlSource.EnvironmentVariable(name)` takes a server from the variable; `Fixtura.PostgreSql.Testcontainers` adds
  `Container(image)` and `EnvironmentVariableOrContainer(name, image)`, one container per test run with its own password.
- `ResetDataAsync` truncates every table except EF Core's migrations history and `KeepOnReset`, restarting identity
  columns and the sequences only those tables' defaults draw from, without the app's delete triggers. Tables an extension owns, such as PostGIS's, stay.
- Databases a killed test run left behind stay on the server; drop the `fixtura_*` ones by hand.

## Resetting data between tests

Tests that share a fixture also share its data. Reset it before each test that needs a clean start:

```csharp
public class OrderTests(ApiFixture api) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await api.ResetDataAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- The SQL Server resource empties every table in the fixture's databases, in foreign-key order, and restarts identity
  columns, and sequences that only emptied tables' defaults draw from. The schema stays, so the app doesn't migrate
  again. System-versioned tables lose their history too, and the app's delete triggers don't run. EF Core's migrations
  history keeps its rows, whatever its table's name, and so do the tables in `SqlServerOptions.KeepOnReset`; list
  reference data the app inserts there. A name that matches no table throws.
- The PostgreSQL resource truncates every table except EF Core's migrations history and `KeepOnReset`, and restarts
  identity columns and the sequences only emptied tables' defaults draw from.
- The SQLite resource empties every table in its in-memory databases, keeping the schema and EF Core's migrations
  history, without running the app's triggers.
- `host.RecreateInMemoryDatabasesOnReset()`, from `Fixtura.EntityFrameworkCore`, deletes and recreates every EF Core
  in-memory database, so only data seeded with `HasData` is left.
- `host.OnReset(services => ...)` adds a step of your own, such as clearing what a fake recorded. Steps run after the
  resources reset, once the host has been built, each in a scope of its own. A custom `ITestResource` resets its data in
  `ResetAsync`.
- Test users are seeded again at their next sign-in.
- Never reset while another test uses the fixture. Tests sharing a class or collection fixture run one at a time, but
  tests sharing an assembly fixture can run in parallel.

## Kestrel and per-test overrides

- `host.Server = ServerKind.Kestrel` runs the app on Kestrel on a free port. `BaseAddress` is where a browser or another
  process reaches it; signed-in clients work as before.
- `api.WithOverrides(host => ...)` builds a second host from the fixture's recorded changes plus the overrides. It
  shares the fixture's resources and database names. Each one costs a full host build, so dispose it with `await using`
  and keep it for the few tests that need it.

## License

MIT
