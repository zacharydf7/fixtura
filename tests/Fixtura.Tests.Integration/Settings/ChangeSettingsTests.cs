using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SampleApp;

namespace Fixtura.Tests.Integration.Settings;

// Sets the carriers, so a test may replace them while the host runs.
public sealed class ChangedSettingsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Settings(new
    {
        Features = new { Reports = true },
        Shipping = new { Carriers = new[] { new { Name = "Fixture", TrackingUrl = "https://fixture.test/{0}" } } }
    });
}

public class ChangeSettingsTests(ChangedSettingsFixture api) : IClassFixture<ChangedSettingsFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await api.ResetDataAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // The monitor has a value before the change, so only a reload can bring it the new one.
    [Fact]
    public async Task ChangeSettingsAsync_ShouldReachMonitoredAndScopedOptions_WhenGivenAnObject()
    {
        var monitor = api.Services.GetRequiredService<IOptionsMonitor<ShippingOptions>>();
        _ = monitor.CurrentValue;

        await api.ChangeSettingsAsync(new { Shipping = new { Carriers = new[] { new { Name = "Live" } } } });

        using IServiceScope scope = api.CreateScope();
        string monitored = CarrierNames(monitor.CurrentValue);
        string scoped = CarrierNames(scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<ShippingOptions>>().Value);
        Assert.Equal(("Live", "Live"), (monitored, scoped));
    }

    // The fixture's carrier has a tracking URL, which mustn't show through the entry that replaced it.
    [Fact]
    public async Task ChangeSettingsAsync_ShouldLeaveOutTheReplacedEntrysFields_WhenAnArrayEntryOmitsThem()
    {
        await api.ChangeSettingsAsync(new { Shipping = new { Carriers = new[] { new { Name = "Live" } } } });

        using IServiceScope scope = api.CreateScope();
        Carrier carrier = Assert.Single(scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<ShippingOptions>>().Value.Carriers);
        Assert.Equal(("Live", ""), (carrier.Name, carrier.TrackingUrl));
    }

    [Fact]
    public async Task ChangeSettingsAsync_ShouldThrow_WhenGivenAnArrayTheFixtureDoesNotSet()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.ChangeSettingsAsync(new { Features = new { Regions = new[] { "EU" } } }));

        Assert.Contains("Features:Regions", error.Message);
    }

    [Fact]
    public async Task ChangeSettingsAsync_ShouldOverrideTheFixturesSettings_WhenGivenJson()
    {
        await api.ChangeSettingsAsync("""{ "Features": { "Reports": false } }""");

        Assert.Equal("false", api.Services.GetRequiredService<IConfiguration>()["Features:Reports"]);
    }

    [Fact]
    public async Task ChangeSettingsAsync_ShouldKeepEarlierChanges_WhenCalledTwice()
    {
        await api.ChangeSettingsAsync(new { Features = new { Reports = false } });

        await api.ChangeSettingsAsync(new { Features = new { Exports = true } });

        IConfiguration configuration = api.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(("false", "true"), (configuration["Features:Reports"], configuration["Features:Exports"]));
    }

    // The monitor holds the changed list, so only a reload after the reset can show the fixture's list again.
    [Fact]
    public async Task ResetDataAsync_ShouldBringBackTheFixturesSettings_WhenATestChangedThem()
    {
        await api.ChangeSettingsAsync(new
        {
            Features = new { Reports = false },
            Shipping = new { Carriers = new[] { new { Name = "Live" }, new { Name = "Second" } } }
        });
        var monitor = api.Services.GetRequiredService<IOptionsMonitor<ShippingOptions>>();
        _ = monitor.CurrentValue;

        await api.ResetDataAsync();

        IConfiguration configuration = api.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(("true", "Fixture"), (configuration["Features:Reports"], CarrierNames(monitor.CurrentValue)));
    }

    private static string CarrierNames(ShippingOptions shipping) => string.Join(", ", shipping.Carriers.Select(carrier => carrier.Name));
}
