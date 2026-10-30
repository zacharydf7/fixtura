using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura.Tests.Integration.Hosting;

public class WebHostTests(TestingEnvironmentFixture api) : IClassFixture<TestingEnvironmentFixture>
{
    [Fact]
    public void Environment_ShouldLoadTheEnvironmentsSettings_WhenSet()
    {
        string environment = api.Services.GetRequiredService<IHostEnvironment>().EnvironmentName;

        string? source = api.Services.GetRequiredService<IConfiguration>()["Probe:Source"];

        Assert.Equal(("Testing", "appsettings.Testing.json"), (environment, source));
    }

    [Fact]
    public void WebHost_ShouldChangeTheWebHost_WhenGivenASetting()
    {
        IConfiguration configuration = api.Services.GetRequiredService<IConfiguration>();

        string? probe = configuration["Probe:WebHost"];

        Assert.Equal("applied", probe);
    }

    [Fact]
    public async Task WithOverrides_ShouldKeepTheEnvironmentAndWebHostSteps_WhenTheOverrideAddsNothing()
    {
        await using TestHost<Program> overridden = api.WithOverrides(_ => { });

        IServiceProvider services = overridden.Services;

        string environment = services.GetRequiredService<IHostEnvironment>().EnvironmentName;
        Assert.Equal(("Testing", "applied"), (environment, services.GetRequiredService<IConfiguration>()["Probe:WebHost"]));
    }
}
