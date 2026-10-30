using Fixtura.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Fixtura;

/// <summary>Records what a test host changes from production. Nothing runs until the host is built.</summary>
public sealed class TestHostBuilder
{
    private readonly HashSet<Type> _features = [];
    private readonly HashSet<ITestHostFeature> _featureInstances = new(ReferenceEqualityComparer.Instance);
    private readonly List<ITestResource> _resources = [];
    private readonly List<Action<IServiceCollection>> _serviceSteps = [];
    private readonly List<Func<IServiceProvider, Task>> _resetSteps = [];
    private readonly List<Action<IWebHostBuilder>> _webHostSteps = [];
    private readonly List<Func<SettingsOverlay>> _settings = [];
    private readonly string _namePrefix = "fixtura_" + Guid.NewGuid().ToString("N")[..8];
    private readonly TestHostBuilder? _parent;
    private LogLevel? _testOutputLevel;
    private FakeTimeProvider? _clock;
    private DateTimeOffset _clockStart;

    internal TestHostBuilder()
    {
        Services = new TestServices(_serviceSteps);
    }

    // A copy for WithOverrides: the parent's steps, settings and names, but none of its resources, which the parent owns.
    private TestHostBuilder(TestHostBuilder parent) : this()
    {
        _parent = parent._parent ?? parent;
        _features.UnionWith(parent._features);
        _featureInstances.UnionWith(parent._featureInstances);
        _serviceSteps.AddRange(parent._serviceSteps);
        _resetSteps.AddRange(parent._resetSteps);
        _webHostSteps.AddRange(parent._webHostSteps);
        Environment = parent.Environment;
        _settings.AddRange(parent._settings);
        _namePrefix = parent._namePrefix;
        IsOverride = true;
        Users = parent.Users.Copy();
        Http = parent.Http.Copy();
        Server = parent.Server;
        _testOutputLevel = parent._testOutputLevel;
        _clock = parent._clock;
        _clockStart = parent._clockStart;
        FailsOnBackgroundServiceCrash = parent.FailsOnBackgroundServiceCrash;
    }

    /// <summary>Typed swaps of the app's service registrations.</summary>
    public TestServices Services { get; }

    /// <summary>How test users sign in to this host.</summary>
    public TestUsers Users { get; } = new();

    /// <summary>Fakes for the app's outbound HTTP clients.</summary>
    public TestHttp Http { get; } = new();

    /// <summary>Which server the app runs on; the in-memory <see cref="ServerKind.TestServer"/> unless set.</summary>
    public ServerKind Server { get; set; } = ServerKind.TestServer;

    /// <summary>
    /// The app's environment, such as "Testing" to load <c>appsettings.Testing.json</c>. Development unless set, as
    /// WebApplicationFactory runs apps; <see cref="Microsoft.Extensions.Hosting.Environments"/> names the standard ones.
    /// </summary>
    public string? Environment { get; set; }

    internal bool IsOverride { get; }

    internal FakeTimeProvider? Clock => _clock;

    internal bool FailsOnBackgroundServiceCrash { get; private set; }

    internal bool LogsToTestOutput => _testOutputLevel is not null;

    /// <summary>
    /// Adds a feature instance, such as one that takes settings of its own. Adding the same instance twice runs it once.
    /// </summary>
    public TestHostBuilder Use(ITestHostFeature feature)
    {
        if (_featureInstances.Add(feature))
        {
            feature.Configure(this);
        }

        return this;
    }

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
        SettingsOverlay overlay = SettingsOverlay.FromObject(settings);
        _settings.Add(() => overlay);
        return this;
    }

    /// <summary>Overrides settings with JSON shaped like appsettings.json; comments and trailing commas are allowed.</summary>
    public TestHostBuilder Settings(string json)
    {
        SettingsOverlay overlay = SettingsOverlay.FromJson(json);
        _settings.Add(() => overlay);
        return this;
    }

    /// <summary>
    /// Overrides settings with an object made when the host is built, after the fixture's resources are provisioned, so it
    /// can hold what they provide, such as <c>new { ConnectionStrings = new { Main = sql.Database("Main") } }</c>.
    /// Otherwise like <see cref="Settings(object)"/>, in the same order as the other calls.
    /// </summary>
    public TestHostBuilder Settings(Func<object> settings)
    {
        _settings.Add(() => SettingsOverlay.FromObject(settings()));
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
    /// messages. The host build fails if the app's logger factory doesn't pass logs on, as Serilog's doesn't without
    /// <c>writeToProviders: true</c>.
    /// </summary>
    public TestHostBuilder LogToTestOutput(LogLevel minimumLevel = LogLevel.Information)
    {
        _testOutputLevel = minimumLevel;
        return this;
    }

    /// <summary>
    /// Changes the web host directly, for what the builder has no member for, such as the content root or a third-party
    /// container's test configuration. Runs after the builder's other changes, though a key set there with
    /// <c>UseSetting</c> still loses to <c>Settings</c>.
    /// </summary>
    public TestHostBuilder WebHost(Action<IWebHostBuilder> configure)
    {
        _webHostSteps.Add(configure);
        return this;
    }

    /// <summary>
    /// Gives the app a clock that stands still at <paramref name="start"/>, now unless given, until a test advances it
    /// through the fixture's <c>Clock</c>, and that <c>ResetDataAsync</c> sets back to the start. It replaces the app's
    /// <see cref="TimeProvider"/>, and its JWT and cookie handlers check expiry against it. A host from
    /// <c>WithOverrides</c> shares its fixture's clock.
    /// </summary>
    public TestHostBuilder UseFakeClock(DateTimeOffset? start = null)
    {
        if (_clock is not null)
        {
            throw new InvalidOperationException(
                "This host already has a fake clock; a host from WithOverrides shares its fixture's. Move it with Clock instead.");
        }

        _clockStart = start ?? DateTimeOffset.UtcNow;
        FakeTimeProvider clock = _clock = new FakeTimeProvider(_clockStart);
        Users.Clock = clock;
        Services.Configure(services => FakeClock.AddTo(services, clock));
        return this;
    }

    /// <summary>
    /// Fails the fixture's next client, scope, reset or disposal once one of the app's background services has crashed,
    /// instead of the crash showing only in the log.
    /// </summary>
    public TestHostBuilder FailOnBackgroundServiceCrash()
    {
        FailsOnBackgroundServiceCrash = true;
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
            var context = new TestResourceContext(_namePrefix, TestContext.Current.CancellationToken);
            TestResourceStatus status = await resource.ProvisionAsync(context);
            if (status.UnavailableReason is not null)
            {
                return status.UnavailableReason;
            }
        }

        return null;
    }

    internal void Apply(IWebHostBuilder web, Action<string> sendDiagnosticMessage)
    {
        if (Environment is not null)
        {
            web.UseEnvironment(Environment);
        }

        // Made now, once the fixture's resources are provisioned, in the order the calls were made.
        var settings = new SettingsOverlay();
        foreach (Func<SettingsOverlay> overlay in _settings)
        {
            settings.Merge(overlay());
        }

        // Host settings become Program.Main arguments, the only configuration an eager read in Program.cs can see.
        foreach (KeyValuePair<string, string?> setting in settings.Values)
        {
            web.UseSetting(setting.Key, setting.Value);
        }

        web.ConfigureAppConfiguration((_, configuration) => ApplyLateSettings(settings, configuration));
        web.ConfigureTestServices(services =>
        {
            // Every stale swap is reported at once, so a rename shows all the swaps it broke in one run.
            List<Action<IServiceCollection>> steps = [.. _serviceSteps, Users.Apply, Http.Apply];
            List<string> unmatched = [];
            foreach (Action<IServiceCollection> step in steps)
            {
                try
                {
                    step(services);
                }
                catch (NothingMatchedException error)
                {
                    unmatched.AddRange(error.Calls);
                }
            }

            if (unmatched.Count > 0)
            {
                throw new NothingMatchedException(unmatched);
            }

            if (_testOutputLevel is LogLevel level)
            {
                TestOutputLoggerProvider.AddTo(services, level, sendDiagnosticMessage);
            }
        });

        foreach (Action<IWebHostBuilder> step in _webHostSteps)
        {
            step(web);
        }
    }

    // Responses set while configuring are what a reset brings back.
    internal void EndConfigure() => Http.SaveBaseline();

    // An override resets its parent's resources, because it shares their data.
    internal async Task ResetAsync(IServiceProvider? services)
    {
        _clock?.AdjustTime(_clockStart);
        foreach (ITestResource resource in (_parent ?? this)._resources)
        {
            await resource.ResetAsync(TestContext.Current.CancellationToken);
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

    private static void ApplyLateSettings(SettingsOverlay settings, IConfigurationBuilder configuration)
    {
        if (settings.ArrayPaths.Count > 0)
        {
            for (int i = 0; i < configuration.Sources.Count; i++)
            {
                configuration.Sources[i] = new HiddenSectionsSource(configuration.Sources[i], settings.ArrayPaths);
            }
        }

        configuration.AddInMemoryCollection(settings.Values);
    }

    internal TResource FindResource<TResource>() where TResource : ITestResource
    {
        List<TResource> found = (_parent ?? this)._resources.OfType<TResource>().ToList();
        return found.Count switch
        {
            1 => found[0],
            0 => throw new InvalidOperationException($"The fixture added no {typeof(TResource).Name} in Configure."),
            _ => throw new InvalidOperationException($"The fixture added {found.Count} of {typeof(TResource).Name}; keep a "
                + "reference to the one you need from Configure instead.")
        };
    }
}
