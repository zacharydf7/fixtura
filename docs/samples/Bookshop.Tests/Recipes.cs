using System.Net;
using Fixtura;
using Microsoft.Data.SqlClient;
using Respawn;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Bookshop.Tests;

// Recipes for tools that do jobs next to Fixtura. They compile with the rest of the samples, but never run.
internal static class Recipes
{
    public static void RespawnFixture(TestHostBuilder host, string connectionString)
    {
        // <respawn-fixture>
        host.AddResource(new SharedDatabase(connectionString));
        // </respawn-fixture>
    }

    public static void WireMockFixture(TestHostBuilder host)
    {
        // <wiremock-fixture>
        ShippingPartner partner = host.AddResource(new ShippingPartner());
        host.Settings(() => new { Shipping = new { Url = partner.Server.Url } });
        host.Http.Unfaked = UnfakedHttpClients.Allow;
        // </wiremock-fixture>
    }

    public static void WireMockTest(TestHost<Program> shop)
    {
        // <wiremock-test>
        shop.Resource<ShippingPartner>().Server
            .Given(Request.Create().WithPath("/quotes").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBodyAsJson(new Quote(4.99m)));
        // </wiremock-test>
    }

    public static void FakeTimeProvider(TestHostBuilder host, TestHost<Program> shop)
    {
        // <fake-time-provider>
        host.UseFakeClock(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));

        // In a test, the clock is the FakeTimeProvider itself:
        shop.Clock.AutoAdvanceAmount = TimeSpan.FromSeconds(1);
        shop.Clock.SetUtcNow(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        // </fake-time-provider>
    }
}

// <respawn>
public sealed class SharedDatabase(string connectionString) : ITestResource
{
    private Respawner? _respawner;

    public Task<TestResourceStatus> ProvisionAsync(TestResourceContext context) => Task.FromResult(TestResourceStatus.Ready);

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        _respawner ??= await Respawner.CreateAsync(connection,
            new RespawnerOptions { TablesToIgnore = ["Books", "__EFMigrationsHistory"] });
        await _respawner.ResetAsync(connection);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
// </respawn>

// <wiremock-resource>
public sealed class ShippingPartner : ITestResource
{
    private WireMockServer? _server;

    public WireMockServer Server => _server ?? throw new InvalidOperationException("The partner starts with the fixture.");

    public Task<TestResourceStatus> ProvisionAsync(TestResourceContext context)
    {
        _server = WireMockServer.Start();
        return Task.FromResult(TestResourceStatus.Ready);
    }

    public Task ResetAsync(CancellationToken cancellationToken)
    {
        Server.Reset();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _server?.Stop();
        return ValueTask.CompletedTask;
    }
}
// </wiremock-resource>
