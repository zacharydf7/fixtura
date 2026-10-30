using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
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
    private HostFactory? _factory;
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

    // Building on first use, after InitializeAsync, lets a missing resource skip the test instead of failing the fixture.
    private WebApplicationFactory<TProgram> Factory
    {
        get
        {
            if (_skipReason is not null)
            {
                throw new TestSkippedException(_skipReason);
            }

            // WebApplicationFactory starts the host on first use without locking, and assembly fixtures are shared in parallel.
            lock (_gate)
            {
                if (_factory is null)
                {
                    var factory = new HostFactory(_builder.Apply);
                    if (_builder.Server == ServerKind.Kestrel)
                    {
                        factory.UseKestrel(0);
                    }

                    _ = factory.Services;
                    _factory = factory;
                }

                return _factory;
            }
        }
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
        _skipReason = await _builder.ProvisionAsync();
    }

    /// <summary>
    /// A client signed in as the user through the app's own authentication, or anonymous when no user is given.
    /// </summary>
    public async Task<HttpClient> CreateClientAsync(TestUser? user = null)
    {
        WebApplicationFactory<TProgram> factory = Factory;
        SignedInUser? signedIn = user is null ? null : await _builder.Users.SignInAsync(user, factory.Services);

        // WebApplicationFactory records every client it creates in a list that isn't thread-safe.
        lock (_gate)
        {
            if (signedIn?.Cookie is not null)
            {
                // In a cookie container, unlike a header, the app's own responses can renew or clear the cookie. The sign-in
                // cookie holds the whole ticket unsplit, so the container must accept any size.
                var cookies = new CookieContainer { MaxCookieSize = int.MaxValue };
                cookies.Add(factory.ClientOptions.BaseAddress, signedIn.Cookie);
                return factory.CreateDefaultClient(new RedirectHandler(), new CookieContainerHandler(cookies));
            }

            HttpClient client = factory.CreateClient();
            if (signedIn is not null)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.BearerToken);
            }

            return client;
        }
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    /// <summary>
    /// Puts the fixture's data back to its starting state: each resource resets, such as SQL Server emptying its tables,
    /// then the steps added with <see cref="TestHostBuilder.OnReset"/> run. Call it before a test, never while another
    /// test uses the same fixture, as tests sharing an assembly fixture can.
    /// </summary>
    public Task ResetDataAsync()
    {
        if (_skipReason is not null)
        {
            throw new TestSkippedException(_skipReason);
        }

        // Before the host is built the app hasn't run, so only the resources can hold data.
        HostFactory? factory;
        lock (_gate)
        {
            factory = _factory;
        }

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
        return new OverrideHost(copy, _skipReason);
    }

    public async ValueTask DisposeAsync()
    {
        // The app goes first, so it has closed its connections before resources such as databases are dropped.
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _builder.DisposeResourcesAsync();
        GC.SuppressFinalize(this);
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
