using System.Net;
using System.Net.Http.Headers;
using Fixtura.Spike.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fixtura.Spike;

// Building on first use, after InitializeAsync, lets a missing resource skip the test instead of failing the fixture.
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

    public IServiceProvider Services => Factory.Services;

    // Where a real-socket client, such as a browser, reaches the app; TestServer has no such address.
    public Uri BaseAddress => _builder.Server == ServerKind.Kestrel
        ? Factory.ClientOptions.BaseAddress
        : throw new InvalidOperationException("BaseAddress needs host.Server = ServerKind.Kestrel; on TestServer use CreateClientAsync.");

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

    public HttpClient CreateClient() => Factory.CreateClient();

    // A client signed in as the user through the app's own authentication, or anonymous when no user is given.
    public async Task<HttpClient> CreateClientAsync(TestUser? user = null)
    {
        WebApplicationFactory<TProgram> factory = Factory;
        if (user is null)
        {
            return factory.CreateClient();
        }

        SignedInUser signedIn = await _builder.Users.SignInAsync(user, factory.Services);
        if (signedIn.Cookie is not null)
        {
            // In a cookie container, unlike a header, the app's own responses can renew or clear the cookie.
            var cookies = new CookieContainer();
            cookies.Add(factory.ClientOptions.BaseAddress, signedIn.Cookie);
            return factory.CreateDefaultClient(new RedirectHandler(), new CookieContainerHandler(cookies));
        }

        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.BearerToken);
        return client;
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    // A second host from the same recorded steps plus the overrides; it shares this fixture's resources and costs a host build.
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
