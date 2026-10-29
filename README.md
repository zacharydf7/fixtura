# Fixtura

Compose ASP.NET Core integration-test hosts from small, reusable features instead of a fixture inheritance tree. A test
project states once what differs from production; no test repeats setup.

Fixtura wraps `WebApplicationFactory<TProgram>` and targets xUnit v3 on .NET 10. It covers the jobs every team rebuilds
by hand:

- **Typed service swaps** that keep the app's lifetimes and fail loudly when a renamed service no longer matches.
- **Settings as objects**, seen by `Program.cs` before `Build()`, where an array replaces the app's array whole.
- **Test users** signed in through the app's own JWT or cookie authentication, events included.
- **Infrastructure** that skips tests when it's missing locally and fails them in CI, with no skip call in any test.

**Status:** pre-release. Nothing is published to nuget.org yet.

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
| `Replace<TService, TImplementation>()` | Replaces every registration of the service, keeping the lifetime the app gave it |
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
- `Options<TOptions>` changes the bound options after the app's own post-configuration, which suits typed lists.

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
| `AppCookie` | An authentication cookie protected with the app's own cookie options, the one its login page would issue. The app's security-stamp check runs on the first request, and its sign-out works. |

Apps that check each request against their user store need the user to exist there. Register a seeder:

```csharp
host.Users.SeedWith<IdentityUserSeeder<AppUser, IdentityRole>>();
```

The seeder runs before each sign-in, one at a time per fixture. `IdentityUserSeeder<TUser, TRole>` brings an ASP.NET
Identity user to exactly the test user's roles and claims and returns the claims Identity signs in. Override `NewUser` to
set your own fields on a user it creates, or implement `ITestUserSeeder` for any other store.

## Missing infrastructure

A resource, such as a database server, is provisioned in the fixture's `InitializeAsync`. When it's unavailable, every
test that uses the fixture is skipped with the resource's reason, and no test has to call a skip helper. A resource can
also be required, for example in CI, and then the fixture fails instead. Implement `ITestResource` and add it with
`host.AddResource(...)`.

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
  LocalDB when it isn't set. `SqlServerSource.LocalDb` always uses LocalDB.
- `sql.Database("Orders")` names a database with the fixture's prefix. Call it inside the swap's lambda, because the
  server is only known once the fixture has provisioned it. The app creates the database, for example by running its
  migrations or `EnsureCreated` at startup.
- Without a reachable server, every test that uses the fixture is skipped with the reason. Set the `RequiredWhenSet`
  variable to `true` in CI, and the fixture fails instead.
- `ReadyTimeout` (5 seconds unless set) is how long to wait for the server, rounded up to whole seconds; it must be
  positive. A LocalDB instance that isn't running yet can need longer on its first connection.
- Databases that a killed test run left behind are dropped once they're an hour old.

## Kestrel and per-test overrides

- `host.Server = ServerKind.Kestrel` runs the app on Kestrel on a free port. `BaseAddress` is where a browser or another
  process reaches it; signed-in clients work as before.
- `api.WithOverrides(host => ...)` builds a second host from the fixture's recorded changes plus the overrides. It
  shares the fixture's resources and database names. Each one costs a full host build, so dispose it with `await using`
  and keep it for the few tests that need it.

## License

MIT
