using System.Net;
using System.Net.Http.Json;
using Fixtura.Spike.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleApp;
using SampleApp.Data;

namespace Fixtura.Spike.Tests.Settings;

public sealed class ObjectSettingsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Settings(new
    {
        Features = new { Reports = true },
        Shipping = new { Carriers = new[] { new { Name = "Only", TrackingUrl = "https://only.test/{0}" } } }
    });
}

// A feature sets a list and the fixture narrows it, the way features are meant to stack.
public sealed class StackedSettingsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Settings(new { Shipping = new { Carriers = new[] { new { Name = "First" }, new { Name = "Second" } } } });
        host.Settings(new { Shipping = new { Carriers = new[] { new { Name = "Last" } } } });
    }
}

public sealed class JsonSettingsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Settings("""{ "Features": { "Reports": true }, "Shipping": { "Carriers": [ { "Name": "Json" } ] } }""");
}

public sealed class TypedOptionsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Options<ShippingOptions>(shipping => shipping.Carriers = [new Carrier { Name = "Typed" }]);
}

// The app reads its Orders connection string inside the same lambda that adds its interceptor; a late setting supplies it.
public sealed class KeptOrdersOptionsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Settings(new { ConnectionStrings = new { Orders = "Server=unused" } });
        host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders")), AppDbOptions.Keep);
    }
}

public class ObjectSettingsTests(ObjectSettingsFixture api) : IClassFixture<ObjectSettingsFixture>
{
    [Fact]
    public async Task Settings_ShouldReachAnEagerReadInProgram_WhenGivenAnObject()
    {
        HttpResponseMessage response = await api.CreateClient().GetAsync("/reports", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Settings_ShouldReplaceTheAppsWholeArray_WhenTheOverlayHasAShorterOne()
    {
        string[]? carriers = await api.CreateClient().GetFromJsonAsync<string[]>("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(["Only"], carriers!);
    }
}

public class StackedSettingsTests(StackedSettingsFixture api) : IClassFixture<StackedSettingsFixture>
{
    [Fact]
    public async Task Settings_ShouldReplaceAnEarlierOverlaysArray_WhenCalledTwice()
    {
        string[]? carriers = await api.CreateClient().GetFromJsonAsync<string[]>("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(["Last"], carriers!);
    }
}

public class JsonSettingsTests(JsonSettingsFixture api) : IClassFixture<JsonSettingsFixture>
{
    [Fact]
    public async Task Settings_ShouldApplyEarlyAndLate_WhenGivenJson()
    {
        HttpClient client = api.CreateClient();
        CancellationToken cancel = TestContext.Current.CancellationToken;

        HttpResponseMessage reports = await client.GetAsync("/reports", cancel);
        string[]? carriers = await client.GetFromJsonAsync<string[]>("/carriers", cancel);

        Assert.Equal(HttpStatusCode.OK, reports.StatusCode);
        Assert.Equal(["Json"], carriers!);
    }
}

public class TypedOptionsTests(TypedOptionsFixture api) : IClassFixture<TypedOptionsFixture>
{
    [Fact]
    public async Task Options_ShouldReplaceTheWholeList_WhenTyped()
    {
        string[]? carriers = await api.CreateClient().GetFromJsonAsync<string[]>("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(["Typed"], carriers!);
    }
}

public class KeptOrdersOptionsTests(KeptOrdersOptionsFixture api) : IClassFixture<KeptOrdersOptionsFixture>
{
    [Fact]
    public async Task KeptAppOptions_ShouldStillRunTheAppsInterceptor_WhenItsConnectionStringIsSupplied()
    {
        using IServiceScope scope = api.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var order = new Order { Product = "Desk", Quantity = 1 };

        db.Orders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(db.Database.IsInMemory());
        Assert.NotNull(order.AuditedAt);
    }
}

// What plain WebApplicationFactory gives today, for comparison: index keys merge, so a list can't get shorter.
public class IndexKeyBaselineTests
{
    [Fact]
    public async Task UseSetting_ShouldOnlyChangeTheFirstEntry_WhenGivenAnIndexKey()
    {
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(web => web.UseSetting("Shipping:Carriers:0:Name", "Only"));

        string[]? carriers = await factory.CreateClient().GetFromJsonAsync<string[]>("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(["Only", "Air", "Freight"], carriers!);
    }
}
