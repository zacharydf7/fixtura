using Microsoft.AspNetCore.Hosting;
using Fixtura.Spike.Settings;
using Fixtura.Spike.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura.Spike;

// Records what a test host changes from production. Nothing runs until the host is built.
public sealed class TestHostBuilder
{
    private readonly HashSet<Type> _features = [];
    private readonly List<ITestResource> _resources = [];
    private readonly List<Action<IServiceCollection>> _serviceSteps = [];
    private readonly SettingsOverlay _settings = new();
    private readonly string _namePrefix = "fixtura_" + Guid.NewGuid().ToString("N")[..8];

    public TestHostBuilder()
    {
        Services = new TestServices(_serviceSteps);
    }

    public TestServices Services { get; }

    public TestUsers Users { get; } = new();

    // Adding the same feature twice runs it once, so features can depend on each other freely.
    public TestHostBuilder Use<TFeature>() where TFeature : ITestHostFeature, new()
    {
        if (_features.Add(typeof(TFeature)))
        {
            new TFeature().Configure(this);
        }

        return this;
    }

    public TResource AddResource<TResource>(TResource resource) where TResource : ITestResource
    {
        _resources.Add(resource);
        return resource;
    }

    // Settings reach Program.cs before Build() and win over every app source after it; arrays are replaced whole after it.
    public TestHostBuilder Settings(object settings)
    {
        _settings.Merge(SettingsOverlay.FromObject(settings));
        return this;
    }

    public TestHostBuilder Settings(string json)
    {
        _settings.Merge(SettingsOverlay.FromJson(json));
        return this;
    }

    public TestHostBuilder Options<TOptions>(Action<TOptions> configure) where TOptions : class
    {
        _serviceSteps.Add(services => services.PostConfigure(configure));
        return this;
    }

    // A name no other fixture instance uses, for in-memory databases and similar shared stores.
    public string UniqueName(string name) => $"{_namePrefix}_{name}";

    internal async Task<string?> ProvisionAsync()
    {
        string?[] reasons = await Task.WhenAll(_resources.Select(resource => resource.ProvisionAsync(_namePrefix)));
        return reasons.FirstOrDefault(reason => reason is not null);
    }

    internal void Apply(IWebHostBuilder web)
    {
        // Host settings become Program.Main arguments, the only configuration an eager read in Program.cs can see.
        foreach (KeyValuePair<string, string?> setting in _settings.Values)
        {
            web.UseSetting(setting.Key, setting.Value);
        }

        web.ConfigureAppConfiguration(configuration => ApplyLateSettings(configuration));
        web.ConfigureTestServices(services =>
        {
            foreach (Action<IServiceCollection> step in _serviceSteps)
            {
                step(services);
            }

            Users.Apply(services);
        });
    }

    private void ApplyLateSettings(IConfigurationBuilder configuration)
    {
        if (_settings.ArrayPaths.Count > 0)
        {
            for (int i = 0; i < configuration.Sources.Count; i++)
            {
                configuration.Sources[i] = new HiddenSectionsSource(configuration.Sources[i], _settings.ArrayPaths);
            }
        }

        configuration.AddInMemoryCollection(_settings.Values);
    }

    internal async Task DisposeResourcesAsync()
    {
        for (int i = _resources.Count - 1; i >= 0; i--)
        {
            await _resources[i].DisposeAsync();
        }
    }
}
