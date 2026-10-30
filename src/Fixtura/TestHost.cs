using System.Net;
using System.Net.Http.Headers;
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

    private void ThrowIfABackgroundServiceCrashed()
    {
        BackgroundService? crashed;
        lock (_gate)
        {
            crashed = _watched.FirstOrDefault(service => service.ExecuteTask?.IsFaulted == true);
        }

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
            if (_builder.LogsToTestOutput)
            {
                // The host outlives this test, so its background work mustn't carry this test's context and send logs to
                // its output.
                using (ExecutionContext.SuppressFlow())
                {
                    _ = factory.Services;
                }

                TestOutputLoggerProvider.EnsureReachable(factory.Services);
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
        _skipReason = await _builder.ProvisionAsync();
    }

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
            // TestServer accepts any scheme, so a cookie user's client uses HTTPS, where a renewed Secure cookie keeps being
            // sent, whatever base address the test gave.
            Uri address = options.BaseAddress ?? factory.ClientOptions.BaseAddress;
            if (signedIn?.Cookie is not null && _builder.Server == ServerKind.TestServer)
            {
                address = new UriBuilder(address) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
            }

            // In a cookie container, unlike a header, the app's own responses can renew or clear the cookie. The sign-in
            // cookie holds the whole ticket unsplit, so the container must accept any size.
            var cookies = new CookieContainer { MaxCookieSize = int.MaxValue };
            if (signedIn?.Cookie is not null)
            {
                cookies.Add(address, signedIn.Cookie);
            }

            List<DelegatingHandler> handlers = [.. options.Handlers];
            if (options.AllowAutoRedirect)
            {
                handlers.Add(new RedirectHandler());
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

    public IServiceScope CreateScope()
    {
        IServiceProvider services = Services;
        ThrowIfABackgroundServiceCrashed();
        return services.CreateScope();
    }

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
    /// Puts the fixture's data back to its starting state: each resource resets, such as SQL Server emptying its tables,
    /// HTTP fakes forget their requests and a test's responses, then the steps added with
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
    /// resources and database names, and costs a full host build. Dispose it when the test ends.
    /// </summary>
    public TestHost<TProgram> WithOverrides(Action<TestHostBuilder> configure)
    {
        TestHostBuilder copy = _builder.CopyForOverrides();
        configure(copy);
        copy.EndConfigure();
        return new OverrideHost(copy, _skipReason);
    }

    public async ValueTask DisposeAsync()
    {
        // The app goes first, so it has closed its connections before resources such as databases are dropped, and the
        // resources go even if the app fails to shut down.
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
        ThrowIfABackgroundServiceCrashed();
    }

    private sealed class HostFactory(Action<IWebHostBuilder> configure) : WebApplicationFactory<TProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => configure(builder);
    }

    private sealed class OverrideHost(TestHostBuilder builder, string? skipReason) : TestHost<TProgram>(builder, skipReason)
    {
        protected override void Configure(TestHostBuilder host)
        {
        }
    }
}
