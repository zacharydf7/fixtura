using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using Fixtura.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Fixtura;

/// <summary>
/// An xUnit fixture for one host shape. Override <see cref="Configure"/> to record what differs from production; the
/// host is built the first time a test uses it.
/// </summary>
/// <typeparam name="TProgram">Any public type in the app's entry assembly, usually <c>Program</c>.</typeparam>
public abstract class TestHost<TProgram> : IAsyncLifetime where TProgram : class
{
    private readonly TestHostBuilder _builder;
    private readonly Lock _gate = new();

    // The app's background services, checked for a crash whenever a test uses the fixture.
    private readonly List<BackgroundService> _watched = [];
    private Task<HostFactory>? _build;
    private string? _skipReason;
    private int _disposed;

    // Every host from WithOverrides, disposed with the fixture; disposing one a test already disposed does nothing.
    private readonly List<TestHost<TProgram>> _overrides = [];

    protected TestHost()
    {
        _builder = new TestHostBuilder();
    }

    private TestHost(TestHostBuilder builder, string? skipReason)
    {
        _builder = builder;
        _skipReason = skipReason;
    }

    /// <summary>The app's services. Skips the test when a resource such as a database is unavailable.</summary>
    public IServiceProvider Services => Factory.Services;

    /// <summary>Where a real-socket client, such as a browser, reaches the app; needs <see cref="ServerKind.Kestrel"/>.</summary>
    public Uri BaseAddress => _builder.Server == ServerKind.Kestrel
        ? Factory.ClientOptions.BaseAddress
        : throw new InvalidOperationException(
            "BaseAddress needs host.Server = ServerKind.Kestrel; on TestServer use CreateClientAsync.");

    private HostFactory Factory => FactoryAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Where the fixture's own clients send their requests: <c>http://localhost</c> on the test server, which only a
    /// handler from <see cref="CreateHandlerAsync"/> reaches, or the app's address on Kestrel. Pair the two for a client
    /// the fixture doesn't build, such as a gRPC channel.
    /// </summary>
    public Uri ClientAddress => Factory.ClientOptions.BaseAddress;

    private BackgroundService? CrashedBackgroundService()
    {
        lock (_gate)
        {
            return _watched.FirstOrDefault(service => service.ExecuteTask?.IsFaulted == true);
        }
    }

    private void ThrowIfABackgroundServiceCrashed()
    {
        BackgroundService? crashed = CrashedBackgroundService();
        if (crashed is not null)
        {
            Exception error = crashed.ExecuteTask!.Exception!.InnerException!;
            throw new InvalidOperationException($"The app's background service {crashed.GetType().Name} crashed: {error.Message}", error);
        }
    }

    // Building on first use, after InitializeAsync, lets a missing resource skip the test instead of failing the fixture.
    // The host builds once, on a thread of its own: its start-up waits on async work, and callers that block thread-pool
    // threads while it runs can starve it. A failed build stays failed, so later tests don't run Program.cs again.
    private Task<HostFactory> FactoryAsync()
    {
        if (_skipReason is not null)
        {
            throw new TestSkippedException(_skipReason);
        }

        lock (_gate)
        {
            // Taken on the test's thread: the app builds on a thread of its own, outside any test.
            Action<string> sendDiagnosticMessage = TestContext.Current.SendDiagnosticMessage;
            _build ??= Task.Factory.StartNew(() => Build(sendDiagnosticMessage), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return _build;
        }
    }

    private HostFactory Build(Action<string> sendDiagnosticMessage)
    {
        var factory = new HostFactory(web => _builder.Apply(web, sendDiagnosticMessage));
        if (_builder.Server == ServerKind.Kestrel)
        {
            factory.UseKestrel(0);
        }

        try
        {
            if (_builder.TagsRequests)
            {
                // The host outlives this test, so its start-up and background work mustn't carry this test's context
                // and have their logs taken for this test's.
                using (ExecutionContext.SuppressFlow())
                {
                    _ = factory.Services;
                }

                LogProbe.EnsureReachable(factory.Services);
            }
            else
            {
                _ = factory.Services;
            }

            if (_builder.FailsOnBackgroundServiceCrash)
            {
                lock (_gate)
                {
                    _watched.AddRange(factory.Services.GetServices<IHostedService>().OfType<BackgroundService>());
                }
            }
        }
        catch
        {
            factory.Dispose();
            throw;
        }

        return factory;
    }

    /// <summary>Records what this host shape changes from production. Runs once, before any resource is provisioned.</summary>
    protected abstract void Configure(TestHostBuilder host);

    public async ValueTask InitializeAsync()
    {
        // An override already carries its parent's configuration, resources and skip gate.
        if (_builder.IsOverride)
        {
            return;
        }

        Configure(_builder);
        _builder.EndConfigure();
        _skipReason = await _builder.ProvisionAsync(GetType());
        if (_skipReason is null)
        {
            await OnInitializedAsync();
        }
    }

    /// <summary>
    /// Runs once the fixture is configured and its resources are provisioned, before its first test; the host isn't built
    /// yet. Override it for setup of the fixture's own, such as reading a resource. It doesn't run when a resource is
    /// unavailable and the fixture's tests are skipped.
    /// </summary>
    protected virtual ValueTask OnInitializedAsync() => ValueTask.CompletedTask;

    /// <summary>Runs as the fixture is disposed, before the app and its resources are. Override it for cleanup of its own.</summary>
    protected virtual ValueTask OnDisposingAsync() => ValueTask.CompletedTask;

    /// <summary>The fake the fixture set up in <c>Configure</c> for the named client.</summary>
    public HttpFake HttpFake(string clientName) => _builder.Http.Find(clientName);

    /// <summary>The fake the fixture set up in <c>Configure</c> for the typed client.</summary>
    public HttpFake HttpFake<TClient>() where TClient : class => _builder.Http.Find<TClient>();

    /// <summary>
    /// What the app logged for each test; needs <c>host.CaptureLogs()</c> or <c>host.FailOnAppErrors()</c>.
    /// </summary>
    public TestLogs Logs => _builder.CapturesLogs
        ? _builder.Logs
        : throw new InvalidOperationException("The fixture captures no logs; call host.CaptureLogs() in Configure.");

    /// <summary>
    /// The calls the app made on <typeparamref name="TService"/>, which the fixture records with
    /// <c>host.Services.Record&lt;TService&gt;()</c>.
    /// </summary>
    public CallRecorder<TService> Recorder<TService>() where TService : class => _builder.Recorders.Find<TService>();

    /// <summary>The one resource of this type the fixture added in <c>Configure</c>, such as its database server.</summary>
    public TResource Resource<TResource>() where TResource : ITestResource => _builder.FindResource<TResource>();

    /// <summary>
    /// A client signed in as the user through the app's own authentication, or anonymous when no user is given.
    /// </summary>
    public Task<HttpClient> CreateClientAsync(TestUser? user = null) => CreateClientAsync(user, new TestClientOptions());

    /// <summary>
    /// A client signed in as the user, or anonymous when no user is given, built with <paramref name="options"/>, such as
    /// one that doesn't follow redirects.
    /// </summary>
    public async Task<HttpClient> CreateClientAsync(TestUser? user, TestClientOptions options)
    {
        HostFactory factory = await FactoryAsync();
        ThrowIfABackgroundServiceCrashed();
        SignedInUser? signedIn = user is null ? null : await _builder.Users.SignInAsync(user, factory.Services, options.Scheme);

        // WebApplicationFactory records every client it creates in a list that isn't thread-safe.
        lock (_gate)
        {
            (Uri address, CookieContainer cookies) = Cookies(signedIn, options.BaseAddress ?? factory.ClientOptions.BaseAddress);
            List<DelegatingHandler> handlers = [.. options.Handlers];
            if (options.AllowAutoRedirect)
            {
                handlers.Add(new RedirectHandler());
            }

            if (_builder.TagsRequests)
            {
                handlers.Add(new TestRequests.TagHandler(_builder.IsAppErrorFailure));
            }

            handlers.Add(new CookieContainerHandler(cookies));
            HttpClient client = factory.CreateDefaultClient(address, [.. handlers]);
            if (signedIn?.BearerToken is not null)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.BearerToken);
            }

            return client;
        }
    }

    /// <summary>
    /// Signs the user in through the app's own authentication, as <c>CreateClientAsync(user)</c> does, and returns the
    /// bearer token or cookie, for a client the fixture doesn't build, such as a WebSocket or a browser. Those need a real
    /// socket: <see cref="ServerKind.Kestrel"/> and <see cref="BaseAddress"/>.
    /// </summary>
    /// <param name="scheme">The scheme to sign in through, instead of <see cref="TestUsers.Scheme"/>.</param>
    public async Task<SignedInUser> SignInAsync(TestUser user, string? scheme = null)
    {
        HostFactory factory = await FactoryAsync();
        ThrowIfABackgroundServiceCrashed();
        return await _builder.Users.SignInAsync(user, factory.Services, scheme);
    }

    /// <summary>
    /// A handler that reaches the app signed in as the user, or anonymously when no user is given, for a client that takes
    /// a handler, such as SignalR's <c>HttpMessageHandlerFactory</c>. Like a client's, it keeps the cookies the app sets.
    /// </summary>
    /// <param name="scheme">The scheme to sign in through, instead of <see cref="TestUsers.Scheme"/>.</param>
    public async Task<HttpMessageHandler> CreateHandlerAsync(TestUser? user = null, string? scheme = null)
    {
        HostFactory factory = await FactoryAsync();
        ThrowIfABackgroundServiceCrashed();
        SignedInUser? signedIn = user is null ? null : await _builder.Users.SignInAsync(user, factory.Services, scheme);
        (Uri address, CookieContainer cookies) = Cookies(signedIn, factory.ClientOptions.BaseAddress);

        // The cookie container below is the only cookie store, over Kestrel as on TestServer.
        HttpMessageHandler server = _builder.Server == ServerKind.Kestrel
            ? new SocketsHttpHandler { UseCookies = false }
            : factory.Server.CreateHandler();
        bool asHttps = _builder.Server == ServerKind.TestServer && address.Scheme == Uri.UriSchemeHttps;
        HttpMessageHandler inner = new CookieContainerHandler(cookies) { InnerHandler = server };
        if (_builder.TagsRequests)
        {
            inner = new TestRequests.TagHandler(_builder.IsAppErrorFailure) { InnerHandler = inner };
        }

        return new SignedInHandler(signedIn?.BearerToken, asHttps) { InnerHandler = inner };
    }

    // TestServer accepts any scheme, so a cookie user's requests go as HTTPS, where a renewed Secure cookie keeps being
    // sent. In a cookie container, unlike a header, the app's own responses can renew or clear the cookie. The sign-in
    // cookie holds the whole ticket unsplit, so the container must accept any size.
    private (Uri Address, CookieContainer Cookies) Cookies(SignedInUser? signedIn, Uri address)
    {
        if (signedIn?.Cookie is not null && _builder.Server == ServerKind.TestServer)
        {
            address = new UriBuilder(address) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
        }

        var cookies = new CookieContainer { MaxCookieSize = int.MaxValue };
        if (signedIn?.Cookie is not null)
        {
            cookies.Add(address, signedIn.Cookie);
        }

        return (address, cookies);
    }

    /// <summary>A scope for resolving scoped services, building the host without blocking a thread. Dispose it with await using.</summary>
    public async Task<AsyncServiceScope> CreateScopeAsync()
    {
        HostFactory factory = await FactoryAsync();
        ThrowIfABackgroundServiceCrashed();
        return factory.Services.CreateAsyncScope();
    }

    public IServiceScope CreateScope()
    {
        IServiceProvider services = Services;
        ThrowIfABackgroundServiceCrashed();
        return services.CreateScope();
    }

    /// <summary>
    /// Runs <paramref name="assertion"/> in a new scope until it passes, for work the app finishes after a request returns,
    /// such as a background service's. After <paramref name="timeout"/>, five seconds unless given, its last failure is the
    /// test's, and a background service's crash ends the wait at once when the fixture fails on crashes. With a fake clock,
    /// <paramref name="advanceClockBy"/> moves it that much between attempts, so a timer the app waits on comes due.
    /// </summary>
    public async Task EventuallyAsync(Func<IServiceProvider, Task> assertion, TimeSpan? timeout = null,
        TimeSpan? advanceClockBy = null)
    {
        HostFactory factory = await FactoryAsync();
        ThrowIfABackgroundServiceCrashed();
        FakeTimeProvider? clock = advanceClockBy is null ? null : _builder.Clock
            ?? throw new InvalidOperationException("advanceClockBy needs host.UseFakeClock() in the fixture's Configure.");
        bool retrying = false;
        try
        {
            await Waiting.UntilAsync(async () =>
            {
                if (retrying)
                {
                    clock?.Advance(advanceClockBy!.Value);
                }

                retrying = true;
                await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
                await assertion(scope.ServiceProvider);
                return true;
            }, timeout, giveUp: () => CrashedBackgroundService() is not null);
        }
        catch when (CrashedBackgroundService() is not null)
        {
            ThrowIfABackgroundServiceCrashed();
        }
    }

    /// <inheritdoc cref="EventuallyAsync(Func{IServiceProvider, Task}, TimeSpan?, TimeSpan?)"/>
    public Task EventuallyAsync(Action<IServiceProvider> assertion, TimeSpan? timeout = null, TimeSpan? advanceClockBy = null) =>
        EventuallyAsync(services =>
        {
            assertion(services);
            return Task.CompletedTask;
        }, timeout, advanceClockBy);

    /// <summary>
    /// Starts a hosted service the fixture deferred with <c>host.Services.DeferHostedService&lt;T&gt;()</c>, and returns it.
    /// A <see cref="BackgroundService"/>'s <c>ExecuteTask</c> then shows when its work ends. The host stops it when it stops.
    /// </summary>
    public async Task<THostedService> StartHostedServiceAsync<THostedService>(CancellationToken cancellationToken = default)
        where THostedService : class, IHostedService
    {
        HostFactory factory = await FactoryAsync();
        var deferred = factory.Services.GetService<DeferredHostedService<THostedService>>() ?? throw new InvalidOperationException(
            $"{typeof(THostedService).Name} isn't deferred: add host.Services.DeferHostedService<{typeof(THostedService).Name}>().");
        ThrowIfABackgroundServiceCrashed();
        await deferred.StartNowAsync(cancellationToken);
        if (_builder.FailsOnBackgroundServiceCrash && deferred.Service is BackgroundService background)
        {
            lock (_gate)
            {
                _watched.Add(background);
            }
        }

        return deferred.Service;
    }

    /// <summary>The app's clock, which a test advances with <c>Advance</c> or <c>SetUtcNow</c>; set up with <c>host.UseFakeClock()</c>.</summary>
    public FakeTimeProvider Clock =>
        _builder.Clock ?? throw new InvalidOperationException("Clock needs host.UseFakeClock() in the fixture's Configure.");

    /// <summary>
    /// Changes settings while the host runs, without a rebuild, with an object shaped like appsettings.json as
    /// <see cref="TestHostBuilder.Settings(object)"/> takes, such as <c>new { Features = new { Reports = false } }</c>.
    /// <c>IConfiguration</c>, <c>IOptionsSnapshot</c>, <c>IOptionsMonitor</c> of options bound to a configuration section,
    /// and feature flags read from configuration see the change. An <c>IOptions</c> value, a monitor of options filled in
    /// by a lambda, and a setting Program.cs read at start-up keep what they had, so change those with
    /// <see cref="WithOverrides"/>. It can replace an array only if the fixture's settings set it. Later calls win, and
    /// <see cref="ResetDataAsync"/> brings back the fixture's settings. Never call it while another test uses the same
    /// fixture.
    /// </summary>
    public async Task ChangeSettingsAsync(object settings)
    {
        SettingsOverlay change = SettingsOverlay.FromObject(settings);
        await FactoryAsync();
        _builder.ChangeSettings(change);
    }

    /// <summary>
    /// Like <see cref="ChangeSettingsAsync(object)"/>, with JSON shaped like appsettings.json; comments and trailing commas
    /// are allowed.
    /// </summary>
    public async Task ChangeSettingsAsync(string json)
    {
        SettingsOverlay change = SettingsOverlay.FromJson(json);
        await FactoryAsync();
        _builder.ChangeSettings(change);
    }

    /// <summary>
    /// Puts the fixture's data back to its starting state: each resource resets, such as SQL Server emptying its tables,
    /// HTTP fakes and call recorders forget their requests and calls and a test's responses and results, settings a test
    /// changed go back to the fixture's, then the steps added with
    /// <see cref="TestHostBuilder.OnReset(Func{IServiceProvider, Task})"/> run. Call it before a test, never while another
    /// test uses the same fixture, as tests sharing an assembly fixture can.
    /// </summary>
    public Task ResetDataAsync()
    {
        if (_skipReason is not null)
        {
            throw new TestSkippedException(_skipReason);
        }

        // Before the host is built the app hasn't run, so only the resources can hold data.
        HostFactory? factory = _build is { IsCompletedSuccessfully: true } ? _build.Result : null;
        ThrowIfABackgroundServiceCrashed();
        return _builder.ResetAsync(factory?.Services);
    }

    /// <summary>
    /// A second host built from this fixture's recorded steps plus <paramref name="configure"/>. It shares this fixture's
    /// resources and database names, and costs a full host build. Dispose it when the test ends; the fixture disposes any
    /// that are still running when it is.
    /// </summary>
    public TestHost<TProgram> WithOverrides(Action<TestHostBuilder> configure)
    {
        TestHostBuilder copy = _builder.CopyForOverrides();
        configure(copy);
        copy.EndConfigure();
        var overridden = new OverrideHost(copy, _skipReason);
        lock (_gate)
        {
            _overrides.Add(overridden);
        }

        return overridden;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        List<TestHost<TProgram>> overrides;
        lock (_gate)
        {
            overrides = [.. _overrides];
        }

        // The fixture's cleanup, overrides and the app go first, so they have closed their connections before resources
        // such as databases are dropped. Each step runs even if an earlier one fails, and the failures are thrown after.
        List<Exception> errors = [];
        try
        {
            await OnDisposingAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }

        foreach (TestHost<TProgram> overridden in overrides)
        {
            try
            {
                await overridden.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        try
        {
            if (_build is { IsCompletedSuccessfully: true })
            {
                await _build.Result.DisposeAsync();
            }
        }
        finally
        {
            await _builder.DisposeResourcesAsync();
        }

        GC.SuppressFinalize(this);
        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Throw(errors[0]);
        }

        if (errors.Count > 1)
        {
            throw new AggregateException(errors);
        }

        ThrowIfABackgroundServiceCrashed();
    }

    private sealed class HostFactory(Action<IWebHostBuilder> configure) : WebApplicationFactory<TProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => configure(builder);
    }

    private sealed class SignedInHandler(string? bearerToken, bool asHttps) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (bearerToken is not null)
            {
                request.Headers.Authorization ??= new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            if (asHttps && request.RequestUri is { IsAbsoluteUri: true } uri && uri.Scheme != Uri.UriSchemeHttps)
            {
                request.RequestUri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class OverrideHost(TestHostBuilder builder, string? skipReason) : TestHost<TProgram>(builder, skipReason)
    {
        protected override void Configure(TestHostBuilder host)
        {
        }
    }
}
