using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura.Tests.Integration.Hosting;

// Records resets and reset steps in one list, so a test can check what ran and in which order.
public sealed class ResettableFixture : TestHost<Program>
{
    public List<string> Resets { get; } = [];

    protected override void Configure(TestHostBuilder host)
    {
        host.AddResource(new ResettingResource(Resets));
        host.OnReset(services =>
        {
            Resets.Add(services.GetService<IConfiguration>() is null ? "step without services" : "step");
            return Task.CompletedTask;
        });
    }
}

public sealed class ResettingResource(List<string> resets) : ITestResource
{
    public Task<string?> ProvisionAsync(string namePrefix) => Task.FromResult<string?>(null);

    public Task ResetAsync()
    {
        resets.Add("resource");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class ResetTests(ResettableFixture api) : IClassFixture<ResettableFixture>
{
    [Fact]
    public async Task ResetDataAsync_ShouldResetTheResourcesThenRunTheSteps_WhenTheHostIsBuilt()
    {
        _ = api.Services;
        api.Resets.Clear();

        await api.ResetDataAsync();

        Assert.Equal(["resource", "step"], api.Resets);
    }

    [Fact]
    public async Task ResetDataAsync_ShouldResetTheParentsResources_WhenCalledOnAnOverride()
    {
        await using TestHost<Program> overridden = api.WithOverrides(_ => { });
        _ = overridden.Services;
        api.Resets.Clear();

        await overridden.ResetDataAsync();

        Assert.Equal(["resource", "step"], api.Resets);
    }

    [Fact]
    public async Task ResetDataAsync_ShouldResetTheFixturesResources_WhenCalledOnAnOverrideOfAnOverride()
    {
        await using TestHost<Program> outer = api.WithOverrides(_ => { });
        await using TestHost<Program> inner = outer.WithOverrides(_ => { });
        _ = inner.Services;
        api.Resets.Clear();

        await inner.ResetDataAsync();

        Assert.Equal(["resource", "step"], api.Resets);
    }
}

// A fixture of its own, so no other test has built its host first.
public class ResetBeforeBuildTests(ResettableFixture api) : IClassFixture<ResettableFixture>
{
    [Fact]
    public async Task ResetDataAsync_ShouldOnlyResetTheResources_WhenTheHostIsNotBuiltYet()
    {
        await api.ResetDataAsync();

        Assert.Equal(["resource"], api.Resets);
    }
}

public sealed class ScopedNote;

// Two reset steps that need a scoped service, as a step that re-seeds through a DbContext does.
public sealed class ScopedResetFixture : TestHost<Program>
{
    public List<ScopedNote> Notes { get; } = [];

    protected override void Configure(TestHostBuilder host)
    {
        host.Services.Configure(services => services.AddScoped<ScopedNote>());
        host.OnReset(services => Notes.Add(services.GetRequiredService<ScopedNote>()));
        host.OnReset(services => Notes.Add(services.GetRequiredService<ScopedNote>()));
    }
}

public class ScopedResetTests(ScopedResetFixture api) : IClassFixture<ScopedResetFixture>
{
    [Fact]
    public async Task ResetDataAsync_ShouldGiveEachStepItsOwnScope_WhenStepsNeedScopedServices()
    {
        _ = api.Services;

        await api.ResetDataAsync();

        Assert.Equal(2, api.Notes.Count);
        Assert.NotSame(api.Notes[0], api.Notes[1]);
    }
}
