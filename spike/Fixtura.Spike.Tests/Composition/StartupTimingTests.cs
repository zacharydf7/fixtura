using System.Diagnostics;
using Fixtura.Spike.EntityFrameworkCore;
using Fixtura.Spike.Tests.Swaps;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleApp;
using SampleApp.Data;
using SampleApp.Inventory;

namespace Fixtura.Spike.Tests.Composition;

// Spike S1's timing check. Opt in with FIXTURA_TIMING=1; each run builds about 150 hosts.
public class StartupTimingTests
{
    private const int Warmups = 2;

    [Fact]
    public async Task TestHost_ShouldAddUnderFivePercentToStartup_WhenComparedWithAHandWrittenFactory()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FIXTURA_TIMING") == "1", "Set FIXTURA_TIMING=1 to time host startup.");
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;

        // SQL hosts are timed alone: creating databases varies too much between runs to show a 5% difference.
        Comparison inMemory = await CompareAsync(50, () => TimeFixtureAsync(new SwapFixture()), TimeRawInMemoryAsync);
        double recording = Median(await RepeatAsync(50, TimeRecordingAsync));
        double api = Median(await RepeatAsync(8, () => TimeFixtureAsync(new ApiSqlFixture())));
        double worker = Median(await RepeatAsync(8, () => TimeFixtureAsync(new ApiWorkerSqlFixture())));
        double partner = Median(await RepeatAsync(8, () => TimeFixtureAsync(new ApiWorkerPartnerSqlFixture())));

        output.WriteLine($"In-memory swaps: {inMemory}");
        output.WriteLine($"Recording the in-memory fixture's swaps: {recording:F3} ms");
        output.WriteLine($"API + SQL: TestHost {api:F0} ms");
        output.WriteLine($"API + worker + SQL: TestHost {worker:F0} ms");
        output.WriteLine($"API + worker + partner + SQL: TestHost {partner:F0} ms");
        Assert.True(inMemory.MedianOverhead < 5);
    }

    // Pairs each wrapped build with a hand-written one, swapping which goes first, so machine drift cancels out.
    private static async Task<Comparison> CompareAsync(int pairs, Func<Task<double>> wrapped, Func<Task<double>> raw)
    {
        List<(double Wrapped, double Raw)> times = [];
        for (int i = 0; i < pairs + Warmups; i++)
        {
            (double w, double r) = i % 2 == 0 ? (await wrapped(), await raw()) : Swap(await raw(), await wrapped());
            if (i >= Warmups)
            {
                times.Add((w, r));
            }
        }

        return new Comparison(times);
    }

    private static (double, double) Swap(double first, double second) => (second, first);

    private static async Task<List<double>> RepeatAsync(int runs, Func<Task<double>> run)
    {
        List<double> times = [];
        for (int i = 0; i < runs + Warmups; i++)
        {
            double time = await run();
            if (i >= Warmups)
            {
                times.Add(time);
            }
        }

        return times;
    }

    // The wrapper's own work before the host exists: running Configure and recording every swap.
    private static async Task<double> TimeRecordingAsync()
    {
        await using var fixture = new SwapFixture();
        var watch = Stopwatch.StartNew();
        await fixture.InitializeAsync();
        return watch.Elapsed.TotalMilliseconds;
    }

    // Host build and start only; provisioning resources is timed by neither side.
    private static async Task<double> TimeFixtureAsync<TFixture>(TFixture fixture) where TFixture : TestHost<Program>
    {
        await using (fixture)
        {
            await fixture.InitializeAsync();
            var watch = Stopwatch.StartNew();
            _ = fixture.Services;
            return watch.Elapsed.TotalMilliseconds;
        }
    }

    private static Task<double> TimeRawInMemoryAsync()
    {
        string prefix = Guid.NewGuid().ToString("N");
        return TimeRawAsync(services =>
        {
            services.ReplaceDbContext<AppIdentityDbContext>(db => db.UseInMemoryDatabase(prefix + "Identity"));
            services.ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(prefix + "Orders"));
            services.ReplaceDbContext<CatalogDbContext>(db => db.UseInMemoryDatabase(prefix + "Catalog"));
            services.ReplaceDbContext<ReportsDbContext>(db => db.UseInMemoryDatabase(prefix + "Reports"));
            services.ReplaceDbContext<AnalyticsDbContext>(db => db.UseInMemoryDatabase(prefix + "Analytics"));
            services.ReplaceDbContexts(typeof(IInventory).Assembly, db => db.UseInMemoryDatabase(prefix + "Inventory"));
            services.RemoveHostedService<OutboxWorker>();
            services.RemoveHostedService<ReportScheduler>();
            services.Replace<IOrderPublisher, NullOrderPublisher>();
            services.ReplaceKeyed<IPaymentGateway, TestCardGateway>(PaymentMethods.Card);
        });
    }

    private static async Task<double> TimeRawAsync(Action<IServiceCollection> configure)
    {
        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.ConfigureTestServices(configure));
        var watch = Stopwatch.StartNew();
        _ = factory.Services;
        return watch.Elapsed.TotalMilliseconds;
    }

    private static double Median(List<double> values)
    {
        List<double> sorted = values.Order().ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
    }

    private sealed class Comparison(List<(double Wrapped, double Raw)> times)
    {
        private readonly List<double> _overheads = times.Select(pair => (pair.Wrapped - pair.Raw) / pair.Raw * 100).Order().ToList();

        public double MedianOverhead => Median(_overheads);

        public override string ToString() =>
            $"TestHost {Median(times.Select(pair => pair.Wrapped).ToList()):F0} ms, "
            + $"hand-written {Median(times.Select(pair => pair.Raw).ToList()):F0} ms, {times.Count} pairs, "
            + $"median overhead {MedianOverhead:+0.0;-0.0}% (middle half {_overheads[_overheads.Count / 4]:+0.0;-0.0}% "
            + $"to {_overheads[_overheads.Count * 3 / 4]:+0.0;-0.0}%)";
    }
}
