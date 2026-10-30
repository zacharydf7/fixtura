using Fixtura.Tests.Integration.Data.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura.Tests.Integration.Data;

// Counts the notes as it starts, as a worker that reads the database at start-up does.
public sealed class NotesReader(IServiceProvider services) : IHostedService
{
    public int? CountAtStart { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        CountAtStart = await scope.ServiceProvider.GetRequiredService<NotesDb>().Notes.CountAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class MigratedNotesFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        SqlServerResource sql = host.UseSqlServer(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrLocalDb("FIXTURA_SQLSERVER"),
            RequiredWhenSet = "FIXTURA_SQLSERVER_REQUIRED",
            ReadyTimeout = TimeSpan.FromSeconds(15)
        });
        host.Services.Configure(services => services
            .AddDbContext<NotesDb>(db => db.UseSqlServer(sql.Database("Notes")))
            .AddSingleton<NotesReader>()
            .AddHostedService(provider => provider.GetRequiredService<NotesReader>()));
        host.MigrateBeforeStart<NotesDb>();
        host.SeedOnReset<NotesDb>(db => db.Notes.Add(new Note { Text = "welcome" }));
    }
}

// No server needed: a model check builds the model without connecting.
public sealed class NotesModelFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Services.Configure(services => services
        .AddDbContext<NotesDb>(db => db.UseSqlServer("Server=unused"))
        .AddDbContext<DriftedNotesDb>(db => db.UseSqlServer("Server=unused")));
}

public class DatabaseSetupTests(MigratedNotesFixture api) : IClassFixture<MigratedNotesFixture>
{
    // The reader counts without failing only if the table is there, and finds the seed only if it ran first too.
    [Fact]
    public void MigrateBeforeStart_ShouldHaveTheSchemaAndSeedReady_WhenTheAppsWorkersStart()
    {
        int? countAtStart = api.Services.GetRequiredService<NotesReader>().CountAtStart;

        Assert.Equal(1, countAtStart);
    }

    [Fact]
    public async Task SeedOnReset_ShouldLeaveOnlyTheSeed_WhenTheDataIsReset()
    {
        using (IServiceScope scope = api.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotesDb>();
            db.Notes.Add(new Note { Text = "from a test" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await api.ResetDataAsync();

        using IServiceScope check = api.CreateScope();
        List<string> texts = await check.ServiceProvider.GetRequiredService<NotesDb>().Notes
            .Select(note => note.Text).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["welcome"], texts);
    }
}

public class SeedAtStartTests
{
    // As in a test class that resets in InitializeAsync: its first test resets before anything has built the host.
    [Fact]
    public async Task SeedOnReset_ShouldSeedTheFirstTest_WhenTheResetCameBeforeTheHostWasBuilt()
    {
        await using var api = new MigratedNotesFixture();
        await api.InitializeAsync();

        await api.ResetDataAsync();

        using IServiceScope scope = api.CreateScope();
        List<string> texts = await scope.ServiceProvider.GetRequiredService<NotesDb>().Notes
            .Select(note => note.Text).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["welcome"], texts);
    }

    [Fact]
    public async Task SeedOnReset_ShouldSeedOnce_WhenAnOverrideHostStartsOnTheFixturesDatabase()
    {
        await using var api = new MigratedNotesFixture();
        await api.InitializeAsync();
        _ = api.Services;

        await using (TestHost<Program> overridden = api.WithOverrides(_ => { }))
        {
            _ = overridden.Services;
        }

        using IServiceScope scope = api.CreateScope();
        List<string> texts = await scope.ServiceProvider.GetRequiredService<NotesDb>().Notes
            .Select(note => note.Text).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["welcome"], texts);
    }
}

public class ModelChangeTests(NotesModelFixture api) : IClassFixture<NotesModelFixture>
{
    [Fact]
    public void AssertNoPendingModelChanges_ShouldPass_WhenTheMigrationsCoverTheModel()
    {
        Exception? error = Record.Exception(api.Services.AssertNoPendingModelChanges<NotesDb>);

        Assert.Null(error);
    }

    [Fact]
    public void AssertNoPendingModelChanges_ShouldThrow_WhenNoMigrationCoversTheModel()
    {
        var error = Assert.Throws<InvalidOperationException>(api.Services.AssertNoPendingModelChanges<DriftedNotesDb>);

        Assert.Contains("DriftedNotesDb's model has changes that no migration covers", error.Message);
    }
}
