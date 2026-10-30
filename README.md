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
dotnet add package Fixtura.SqlServer
dotnet add package Fixtura.SqlServer.Testcontainers
```

`Fixtura` is the core. Add the others when the app uses EF Core, when tests need a real SQL Server, and when that
server should run in Docker.

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

## Swapping services

| Call | What it does |
| --- | --- |
| `Replace<TService, TImplementation>(lifetime?)` | Replaces every registration of the service, keeping the lifetime the app gave it unless you pass one |
| `Replace<TService>(instance)` | Replaces every registration with one instance the test can inspect |
| `ReplaceKeyed<TService, TImplementation>(key)` | Replaces one keyed registration; other keys stay |
| `Remove<TService>()` | Removes every registration of the service |
| `RemoveHostedService<THostedService>()` | Removes a hosted service registered with `AddHostedService<T>()` or its factory overload |
| `Configure(services => ...)` | Anything else, as plain `IServiceCollection` code |

Every typed swap throws `NothingMatchedException` when the app has no matching registration, so a renamed service fails
the test run instead of silently testing the wrong thing. The same swaps exist as `IServiceCollection` extension methods
for code that builds its own factory.

## Swapping DbContexts

The `Fixtura.EntityFrameworkCore` package points the app's DbContexts at test databases.

```csharp
host.Services
    .ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders")))
    .ReplaceDbContext<AuditedDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Audit")), AppDbOptions.Keep)
    .ReplaceDbContexts(typeof(SomeLibraryType).Assembly, db => db.UseInMemoryDatabase(host.UniqueName("Library")));
```

- One call works for every registration shape: `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` and
  `AddPooledDbContextFactory`. Only the options configuration is replaced, so the app's lifetimes and pooling stay.
- `AppDbOptions.Drop` (the default) configures the context from scratch. `AppDbOptions.Keep` still runs the app's own
  configuration, such as interceptors, and replaces only the database provider. If the app's configuration throws, for
  example on a missing connection string, supply the value with `host.Settings`.
- `ReplaceDbContexts(assembly, ...)` reaches every registered context whose type is declared in that assembly, including
  ones a library keeps internal.
- A swap throws when the context's options are registered by hand rather than through EF's `Add...` methods, because it
  couldn't reach them and the tests would run against the app's database.
- Name in-memory databases with `host.UniqueName(...)`, so fixtures don't share data. An override from `WithOverrides`
  gets the same names, so it shares its fixture's data.

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
- Settings reach `Program.cs` before `builder.Build()`, so code that reads configuration early sees them. After
  `Build()` they win over every app source.
- An array in the overlay replaces the app's array whole, so a 3-entry list in appsettings.json becomes the 1 entry you
  set. A later `Settings` call replaces an earlier call's array the same way. Before `Build()`, arrays still merge by
  index with the app's, because the only early channel is command-line arguments; read lists after `Build()`.
- `Options<TOptions>` changes the bound options after the app's own post-configuration, which suits typed lists. It
  reaches every instance, named or not, such as each authentication scheme's; `Options<TOptions>(name, ...)` changes
  one.

## Test users

```csharp
HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Manager").WithClaim("tenant", "north"));
HttpClient anonymous = await api.CreateClientAsync();
```

The user signs in through the app's own authentication handler, so its token or cookie events and account checks still
run. Choose how with `host.Users.SignIn`:

| `UserSignIn` | How |
| --- | --- |
| `AppJwtBearer` (default) | A bearer token signed with the app's own `JwtBearerOptions` key, issuer and audience. Apps that trust an Authority get a test signing key, so no metadata request leaves the process. |
| `AppCookie` | An authentication cookie protected with the app's own cookie options, the one its login page would issue. The app's security-stamp check runs on the first request, and its sign-out works. Clients talk to the test server over HTTPS, so a Secure cookie keeps working after the app renews it. |

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
- `Respond(method, path, status, json)` answers one route, and `Respond(status, json)` or `Respond(request => ...)`
  answers everything else. The latest matching response wins. A request nothing answers throws, naming the request.
- `Requests` lists what the app sent: method, URI, headers and body, with `Json<T>()` to read the body.
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
- Each request runs in its test's execution context, so the test's culture and other ambient state reach the app.
- Logs that belong to no test, such as start-up's, a background service's or those of a request over Kestrel, become
  xUnit diagnostic messages, which show when diagnostic messages are turned on.

## Missing infrastructure

A resource, such as a database server, is provisioned in the fixture's `InitializeAsync`. When it's unavailable, every
test that uses the fixture is skipped with the resource's reason, and no test has to call a skip helper. A resource can
also be required, for example in CI, and then the fixture fails instead. Implement `ITestResource` and add it with
`host.AddResource(...)`. Resources provision in the order they are added, so one can build on another, such as
migrations on a database server; the first unavailable one skips the rest.

### SQL Server

The `Fixtura.SqlServer` package adds a SQL Server resource. Each fixture gets its own databases, and they're dropped when
the fixture is done.

```csharp
SqlServerResource sql = host.UseSqlServer(new SqlServerOptions
{
    Source = SqlServerSource.EnvironmentVariableOrLocalDb("TEST_SQLSERVER"),
    RequiredWhenSet = "TEST_SQLSERVER_REQUIRED"
});
host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseSqlServer(sql.Database("Orders")));
```

- `SqlServerSource.EnvironmentVariableOrLocalDb(name)` takes a server connection string from the variable, or uses
  LocalDB when it isn't set or is blank. `SqlServerSource.LocalDb` always uses LocalDB.
- `sql.Database("Orders")` names a database with the fixture's prefix. Call it inside the swap's lambda, because the
  server is only known once the fixture has provisioned it. The app creates the database, for example by running its
  migrations or `EnsureCreated` at startup.
- Without a reachable server, every test that uses the fixture is skipped with the reason. Set the `RequiredWhenSet`
  variable to `true` in CI, and the fixture fails instead.
- `ReadyTimeout` (5 seconds unless set) is how long to wait for the server, rounded up to whole seconds; it must be
  positive. A LocalDB instance that isn't running yet can need longer on its first connection. The server is checked
  once per test run, so an unreachable one costs the timeout once, not once per fixture.
- Databases that a killed test run left behind are dropped once they're a day old and, when the login can view
  server state, nobody is connected to them.
- `SqlServerSource.From(description, async () => ...)` takes a server from your own code. Returning null, or throwing,
  skips the fixture's tests with the reason, or fails them when the `RequiredWhenSet` variable is set.

### SQL Server in a container

The `Fixtura.SqlServer.Testcontainers` package runs SQL Server in Docker, so a machine without one still runs the SQL
tests:

```csharp
SqlServerResource sql = host.UseSqlServer(new SqlServerOptions
{
    Source = SqlServerSource.EnvironmentVariableOrContainer("TEST_SQLSERVER"),
    RequiredWhenSet = "TEST_SQLSERVER_REQUIRED"
});
```

- `SqlServerSource.Container(image)` starts one container on first use and shares it with every fixture in the test
  run; each fixture still gets its own databases. Testcontainers' resource reaper removes the container when the run
  ends; with the reaper turned off, as some CI agents and Podman setups do, remove it yourself.
- `SqlServerSource.EnvironmentVariableOrContainer(name, image)` uses the variable's server when it's set, such as a CI
  service, and a container otherwise.
- The image is `SqlServerContainer.DefaultImage` (SQL Server 2022) unless given. Without Docker, the fixture's tests skip
  with the reason, or fail when the `RequiredWhenSet` variable is set.

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
