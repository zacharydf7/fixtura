using Fixtura.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fixtura;

/// <summary>Records what a test host changes from production. Nothing runs until the host is built.</summary>
public sealed class TestHostBuilder
{
    private readonly HashSet<Type> _features = [];
    private readonly List<ITestResource> _resources = [];
    private readonly List<Action<IServiceCollection>> _serviceSteps = [];
    private readonly List<Func<IServiceProvider, Task>> _resetSteps = [];
    private readonly SettingsOverlay _settings = new();
    private readonly string _namePrefix = "fixtura_" + Guid.NewGuid().ToString("N")[..8];
    private readonly TestHostBuilder? _parent;
    private LogLevel? _testOutputLevel;

    internal TestHostBuilder()
    {
        Services = new TestServices(_serviceSteps);
    }

    // A copy for WithOverrides: the parent's steps, settings and names, but none of its resources, which the parent owns.
    private TestHostBuilder(TestHostBuilder parent) : this()
    {
        _parent = parent._parent ?? parent;
        _features.UnionWith(parent._features);
        _serviceSteps.AddRange(parent._serviceSteps);
        _resetSteps.AddRange(parent._resetSteps);
        _settings.Merge(parent._settings);
        _namePrefix = parent._namePrefix;
        IsOverride = true;
        Users = parent.Users.Copy();
        Http = parent.Http.Copy();
        Server = parent.Server;
        _testOutputLevel = parent._testOutputLevel;
    }

    /// <summary>Typed swaps of the app's service registrations.</summary>
    public TestServices Services { get; }

    /// <summary>How test users sign in to this host.</summary>
    public TestUsers Users { get; } = new();

    /// <summary>Fakes for the app's outbound HTTP clients.</summary>
    public TestHttp Http { get; } = new();

    /// <summary>Which server the app runs on; the in-memory <see cref="ServerKind.TestServer"/> unless set.</summary>
    public ServerKind Server { get; set; } = ServerKind.TestServer;

    internal bool IsOverride { get; }

    internal bool LogsToTestOutput => _testOutputLevel is not null;

    /// <summary>
    /// Adds a reusable feature. Adding the same feature twice runs it once, so features can build on each other.
    /// </summary>
    public TestHostBuilder Use<TFeature>() where TFeature : ITestHostFeature, new()
    {
        if (_features.Add(typeof(TFeature)))
        {
            new TFeature().Configure(this);
        }

        return this;
    }

    /// <summary>
    /// Adds infrastructure that is provisioned before the first test and disposed after the last. Resources provision in the
    /// order they are added, so one can build on another, such as migrations on a database server.
    /// </summary>
    public TResource AddResource<TResource>(TResource resource) where TResource : ITestResource
    {
        if (IsOverride)
        {
            throw new InvalidOperationException(
                "WithOverrides shares the fixture's resources; add resources in the fixture's Configure.");
        }

        _resources.Add(resource);
        return resource;
    }

    /// <summary>
    /// Overrides settings with an object shaped like appsettings.json. Program.cs sees them before <c>Build()</c>, they win
    /// over every app source after it, and an array replaces the app's array whole after it. Later calls win.
    /// </summary>
    public TestHostBuilder Settings(object settings)
    {
        _settings.Merge(SettingsOverlay.FromObject(settings));
        return this;
    }

    /// <summary>Overrides settings with JSON shaped like appsettings.json; comments and trailing commas are allowed.</summary>
    public TestHostBuilder Settings(string json)
    {
        _settings.Merge(SettingsOverlay.FromJson(json));
        return this;
    }

    /// <summary>
    /// Changes every instance of the app's options, named or not, after the app has bound and post-configured them, so a
    /// list can be replaced whole. Named options include each authentication scheme's and each HTTP client's.
    /// </summary>
    public TestHostBuilder Options<TOptions>(Action<TOptions> configure) where TOptions : class
    {
        _serviceSteps.Add(services => services.PostConfigureAll(configure));
        return this;
    }

    /// <summary>Changes one named instance of the app's options, such as one authentication scheme's.</summary>
    public TestHostBuilder Options<TOptions>(string name, Action<TOptions> configure) where TOptions : class
    {
        _serviceSteps.Add(services => services.PostConfigure(name, configure));
        return this;
    }

    /// <summary>
    /// Adds a step that <c>ResetDataAsync</c> runs against the app's services after the resources have reset, such as
    /// clearing what a fake recorded. Each step gets a scope of its own, so it can use scoped services such as a
    /// DbContext. Steps run in the order they are added, and only once the host has been built.
    /// </summary>
    public TestHostBuilder OnReset(Func<IServiceProvider, Task> reset)
    {
        _resetSteps.Add(reset);
        return this;
    }

    /// <inheritdoc cref="OnReset(Func{IServiceProvider, Task})"/>
    public TestHostBuilder OnReset(Action<IServiceProvider> reset) =>
        OnReset(services =>
        {
            reset(services);
            return Task.CompletedTask;
        });

    /// <summary>
    /// Writes the app's logs to the output of the test whose request caused them, which xUnit shows when the test fails.
    /// The app's own logging settings still filter them, and <paramref name="minimumLevel"/> raises the floor. Each request
    /// runs in its test's execution context, so the test's culture and other ambient state reach the app. Logs that belong
    /// to no test, such as start-up's, a background service's or a request's over Kestrel, become xUnit diagnostic
    /// messages.
    /// </summary>
    public TestHostBuilder LogToTestOutput(LogLevel minimumLevel = LogLevel.Information)
    {
        _testOutputLevel = minimumLevel;
        return this;
    }

    /// <summary>A name no other fixture instance uses, for in-memory databases and similar shared stores.</summary>
    public string UniqueName(string name) => $"{_namePrefix}_{name}";

    internal TestHostBuilder CopyForOverrides() => new(this);

    // The first unavailable resource skips the rest, which may depend on it.
    internal async Task<string?> ProvisionAsync()
    {
        foreach (ITestResource resource in _resources)
        {
            string? reason = await resource.ProvisionAsync(_namePrefix);
            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    internal void Apply(IWebHostBuilder web, Action<string> sendDiagnosticMessage)
    {
        // Host settings become Program.Main arguments, the only configuration an eager read in Program.cs can see.
        foreach (KeyValuePair<string, string?> setting in _settings.Values)
        {
            web.UseSetting(setting.Key, setting.Value);
        }

        web.ConfigureAppConfiguration(ApplyLateSettings);
        web.ConfigureTestServices(services =>
        {
            foreach (Action<IServiceCollection> step in _serviceSteps)
            {
                step(services);
            }

            Users.Apply(services);
            Http.Apply(services);
            if (_testOutputLevel is LogLevel level)
            {
                TestOutputLoggerProvider.AddTo(services, level, sendDiagnosticMessage);
            }
        });
    }

    // Responses set while configuring are what a reset brings back.
    internal void EndConfigure() => Http.SaveBaseline();

    // An override resets its parent's resources, because it shares their data.
    internal async Task ResetAsync(IServiceProvider? services)
    {
        foreach (ITestResource resource in (_parent ?? this)._resources)
        {
            await resource.ResetAsync();
        }

        Http.Reset();

        if (services is null)
        {
            return;
        }

        foreach (Func<IServiceProvider, Task> step in _resetSteps)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            await step(scope.ServiceProvider);
        }
    }

    internal async Task DisposeResourcesAsync()
    {
        for (int i = _resources.Count - 1; i >= 0; i--)
        {
            await _resources[i].DisposeAsync();
        }
    }

    private void ApplyLateSettings(WebHostBuilderContext context, IConfigurationBuilder configuration)
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
}
