using Fixtura.Tests.Integration.Data.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Fixtura.Tests.Integration.PostgreSql;

// Without Docker these tests skip; with FIXTURA_POSTGRES_REQUIRED=true, as on CI's Linux job, they fail.
public sealed class PostgresNotesFixture : TestHost<Program>
{
    public PostgreSqlResource Postgres { get; private set; } = null!;

    public IReadOnlyList<string> Keep { get; init; } = ["Countries"];

    protected override void Configure(TestHostBuilder host)
    {
        PostgreSqlResource postgres = Postgres = host.UsePostgreSql(new PostgreSqlOptions
        {
            Source = PostgreSqlSource.EnvironmentVariableOrContainer("FIXTURA_POSTGRES"),
            RequiredWhenSet = "FIXTURA_POSTGRES_REQUIRED",
            ReadyTimeout = TimeSpan.FromSeconds(15),
            KeepOnReset = Keep
        });
        host.Services.Configure(services => services.AddDbContext<NotesDb>(db => db.UseNpgsql(postgres.Database("Notes"))));
    }

    // TestHost only records an unavailable server, and a test that reaches for a database first would fail instead of skip.
    public static async Task SkipWithoutServerAsync()
    {
        await using var postgres = new PostgreSqlResource(new PostgreSqlOptions
        {
            Source = PostgreSqlSource.EnvironmentVariableOrContainer("FIXTURA_POSTGRES"),
            RequiredWhenSet = "FIXTURA_POSTGRES_REQUIRED",
            ReadyTimeout = TimeSpan.FromSeconds(15)
        });
        string? unavailable = await postgres.ProvisionAsync("fixtura_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.SkipWhen(unavailable is not null, unavailable ?? "");
    }

    // The app's schema, plus a kept reference table and a migrations history as an app that migrates has.
    public async Task CreateSchemaAsync()
    {
        using (IServiceScope scope = CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotesDb>().Database.EnsureCreatedAsync();
        }

        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS "Countries" ("Name" text PRIMARY KEY);
            INSERT INTO "Countries" VALUES ('North') ON CONFLICT DO NOTHING;
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" ("MigrationId" text PRIMARY KEY, "ProductVersion" text NOT NULL);
            INSERT INTO "__EFMigrationsHistory" VALUES ('20260101000000_Initial', '10.0.0') ON CONFLICT DO NOTHING;
            """);
    }

    public async Task<object?> ScalarAsync(string sql, string? database = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(Postgres.Database("Notes"));
        if (database is not null)
        {
            builder.Database = database;
        }

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private Task ExecuteAsync(string sql) => ScalarAsync(sql);
}

public sealed class ContainerPostgresFixture : TestHost<Program>
{
    public PostgreSqlResource Postgres { get; private set; } = null!;

    protected override void Configure(TestHostBuilder host) =>
        Postgres = host.UsePostgreSql(new PostgreSqlOptions
        {
            Source = PostgreSqlSource.Container(),
            RequiredWhenSet = "FIXTURA_POSTGRES_REQUIRED"
        });
}

public class PostgreSqlTests(PostgresNotesFixture api) : IClassFixture<PostgresNotesFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await api.CreateSchemaAsync();
        await api.ResetDataAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Database_ShouldBeTheFixturesOwn_WhenTheAppUsesIt()
    {
        await AddNoteAsync("first");

        string database = new NpgsqlConnectionStringBuilder(api.Postgres.Database("Notes")).Database!;

        Assert.Matches("^fixtura_[0-9a-f]{8}_Notes$", database);
        Assert.Equal(["first"], await NoteTextsAsync());
    }

    [Fact]
    public async Task ResetDataAsync_ShouldEmptyTablesAndRestartIds_WhenRowsWereAdded()
    {
        await AddNoteAsync("first");

        await api.ResetDataAsync();

        Assert.Equal(1, await AddNoteAsync("second"));
    }

    [Fact]
    public async Task ResetDataAsync_ShouldKeepTheListedTablesAndMigrationsHistory_WhenItEmptiesTheRest()
    {
        await AddNoteAsync("first");

        await api.ResetDataAsync();

        object? kept = await api.ScalarAsync("""SELECT (SELECT COUNT(*) FROM "Countries") + (SELECT COUNT(*) FROM "__EFMigrationsHistory")""");
        Assert.Equal(2L, kept);
    }

    private async Task<int> AddNoteAsync(string text)
    {
        using IServiceScope scope = api.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotesDb>();
        var note = new Note { Text = text };
        db.Notes.Add(note);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return note.Id;
    }

    private async Task<List<string>> NoteTextsAsync()
    {
        using IServiceScope scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotesDb>().Notes
            .Select(note => note.Text).ToListAsync(TestContext.Current.CancellationToken);
    }
}

public class PostgreSqlLifetimeTests
{
    [Fact]
    public async Task ResetDataAsync_ShouldThrow_WhenAKeptNameMatchesNoTable()
    {
        await using var api = new PostgresNotesFixture { Keep = ["Countries", "Countrys"] };
        await api.InitializeAsync();
        await api.CreateSchemaAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(api.ResetDataAsync);

        Assert.StartsWith("KeepOnReset names Countrys, which match no table", error.Message);
    }

    [Fact]
    public async Task DisposeAsync_ShouldDropTheFixturesDatabases_WhenItCreatedSome()
    {
        var api = new PostgresNotesFixture();
        await api.InitializeAsync();
        await api.CreateSchemaAsync();
        string database = new NpgsqlConnectionStringBuilder(api.Postgres.Database("Notes")).Database!;
        string server = new NpgsqlConnectionStringBuilder(api.Postgres.Database("Notes")) { Database = "postgres" }.ConnectionString;

        await api.DisposeAsync();

        await using var connection = new NpgsqlConnection(server);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", database);
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}

public class PostgreSqlContainerTests(ContainerPostgresFixture api) : IClassFixture<ContainerPostgresFixture>
{
    // Testcontainers' default password is public, and the container's port is published on every interface.
    [Fact]
    public void Container_ShouldUseItsOwnPassword_WhenStarted()
    {
        _ = api.Services;

        string? password = new NpgsqlConnectionStringBuilder(api.Postgres.Database("Notes")).Password;

        Assert.False(string.IsNullOrEmpty(password));
        Assert.NotEqual(PostgreSqlBuilder.DefaultPassword, password);
    }
}

// The resource's own skip-or-fail decision, against an address that refuses connections, so no server is needed.
public class PostgreSqlGateTests
{
    [Fact]
    public async Task ProvisionAsync_ShouldReturnAReadableReason_WhenTheServerIsUnreachable()
    {
        await using PostgreSqlResource postgres = Unreachable(required: null);

        string? reason = await postgres.ProvisionAsync("fixtura_00000000");

        Assert.StartsWith("PostgreSQL not reachable at 127.0.0.1:1", reason);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldThrow_WhenTheServerIsUnreachableAndRequired()
    {
        await using PostgreSqlResource postgres = Unreachable(required: "true");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => postgres.ProvisionAsync("fixtura_00000000"));

        Assert.EndsWith("is set, so PostgreSQL is required.", error.Message);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldSayNoServerIsConfigured_WhenTheVariableIsBlank()
    {
        string name = $"FIXTURA_TEST_POSTGRES_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, " ");
        await using var postgres = new PostgreSqlResource(new PostgreSqlOptions { Source = PostgreSqlSource.EnvironmentVariable(name) });

        string? reason = await postgres.ProvisionAsync("fixtura_00000000");

        Assert.Equal($"No PostgreSQL server is configured in {name}.", reason);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldThrow_WhenGivenAPrefixTheFixtureDidNotMake()
    {
        await using PostgreSqlResource postgres = Unreachable(required: null);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => postgres.ProvisionAsync("Sales"));

        Assert.Equal("namePrefix", error.ParamName);
    }

    // A unique application name per test, because the resource checks each connection string once per process.
    private static PostgreSqlResource Unreachable(string? required)
    {
        string variable = $"FIXTURA_TEST_REQUIRED_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, required);
        string server = $"Host=127.0.0.1;Port=1;Username=test;Password=test;Application Name={Guid.NewGuid():N}";
        return new PostgreSqlResource(new PostgreSqlOptions
        {
            Source = PostgreSqlSource.From("the test's server", () => Task.FromResult<string?>(server)),
            RequiredWhenSet = variable,
            ReadyTimeout = TimeSpan.FromSeconds(1)
        });
    }
}

// The image can't start, so a test passes only if the source chose as it should.
public class PostgreSqlContainerGateTests
{
    private const string MissingImage = "fixtura.invalid/no-such-image:1";

    [Fact]
    public async Task ProvisionAsync_ShouldReturnTheReason_WhenTheContainerCannotStart()
    {
        await using var postgres = new PostgreSqlResource(new PostgreSqlOptions { Source = PostgreSqlSource.Container(MissingImage) });

        string? reason = await postgres.ProvisionAsync("fixtura_00000000");

        Assert.StartsWith($"a PostgreSQL container ({MissingImage}) has no PostgreSQL server: ", reason);
    }

    [Fact]
    public async Task EnvironmentVariableOrContainer_ShouldUseTheVariablesServer_WhenTheVariableIsSet()
    {
        string name = $"FIXTURA_TEST_POSTGRES_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, "Host=ci-postgres");

        string? server = await PostgreSqlSource.EnvironmentVariableOrContainer(name, MissingImage).ResolveAsync();

        Assert.Equal("Host=ci-postgres", server);
    }

    [Fact]
    public async Task EnvironmentVariableOrContainer_ShouldStartAContainer_WhenTheVariableIsBlank()
    {
        string name = $"FIXTURA_TEST_POSTGRES_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, " ");
        PostgreSqlSource source = PostgreSqlSource.EnvironmentVariableOrContainer(name, MissingImage);

        await Assert.ThrowsAnyAsync<Exception>(source.ResolveAsync);
    }
}
