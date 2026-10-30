using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Fixtura;

/// <summary>
/// Fakes for the app's outbound HTTP through <c>IHttpClientFactory</c>. Set in <c>Configure</c>. A fake replaces only the
/// client's primary handler, so the app's own handlers, such as retries or auth headers, still run.
/// </summary>
public sealed class TestHttp
{
    private readonly Dictionary<string, HttpFake> _fakes = [];
    private readonly HashSet<string> _inherited = [];
    private readonly List<Type> _typedClients = [];

    internal TestHttp()
    {
    }

    /// <summary>What happens to requests through clients that have no fake; <see cref="UnfakedHttpClients.Allow"/> unless set.</summary>
    public UnfakedHttpClients Unfaked { get; set; } = UnfakedHttpClients.Allow;

    /// <summary>
    /// The fake for the named client, as registered with <c>AddHttpClient(name)</c>. Asking twice returns the same fake; in
    /// <c>WithOverrides</c> it returns a new one that replaces the fixture's for that host only.
    /// </summary>
    public HttpFake Fake(string clientName)
    {
        if (_inherited.Remove(clientName) || !_fakes.ContainsKey(clientName))
        {
            _fakes[clientName] = new HttpFake(clientName);
        }

        return _fakes[clientName];
    }

    /// <summary>
    /// The fake for the typed client registered with <c>AddHttpClient&lt;TClient&gt;()</c>, whose client name is its type
    /// name. Building the host throws <see cref="NothingMatchedException"/> when the app registers no such client. A typed
    /// client registered under a name of its own, as with <c>AddHttpClient&lt;TClient&gt;("name")</c>, needs
    /// <see cref="Fake(string)"/> with that name.
    /// </summary>
    public HttpFake Fake<TClient>() where TClient : class
    {
        if (typeof(TClient).IsGenericType)
        {
            throw new ArgumentException(
                $"{typeof(TClient).Name} is generic, so its client name isn't its type name; use Fake(name) with the name it was registered under.");
        }

        _typedClients.Add(typeof(TClient));
        return Fake(typeof(TClient).Name);
    }

    // Shares the fakes, which a test reads through the fixture, until the override fakes a client again.
    internal TestHttp Copy()
    {
        var copy = new TestHttp { Unfaked = Unfaked };
        foreach (KeyValuePair<string, HttpFake> fake in _fakes)
        {
            copy._fakes[fake.Key] = fake.Value;
            copy._inherited.Add(fake.Key);
        }

        copy._typedClients.AddRange(_typedClients);
        return copy;
    }

    internal void Apply(IServiceCollection services)
    {
        foreach (Type client in _typedClients)
        {
            if (!services.Any(descriptor => descriptor.ServiceType == client))
            {
                throw new NothingMatchedException($"Http.Fake<{client.Name}>");
            }
        }

        if (_fakes.Count > 0 || Unfaked == UnfakedHttpClients.Block)
        {
            // Post-configured, so the fake replaces any primary handler the app's own configuration set.
            services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(SwapPrimaryHandler));
        }
    }

    internal void SaveBaseline()
    {
        foreach (HttpFake fake in _fakes.Values)
        {
            fake.SaveBaseline();
        }
    }

    internal void Reset()
    {
        foreach (HttpFake fake in _fakes.Values)
        {
            fake.Reset();
        }
    }

    // A new handler each time, because the factory disposes a client's handlers when it rotates them.
    private void SwapPrimaryHandler(HttpMessageHandlerBuilder builder)
    {
        string name = builder.Name ?? Options.DefaultName;
        if (_fakes.TryGetValue(name, out HttpFake? fake))
        {
            builder.PrimaryHandler = new FakeHandler(fake.SendAsync);
        }
        else if (Unfaked == UnfakedHttpClients.Block)
        {
            builder.PrimaryHandler = new FakeHandler((request, _) => throw new InvalidOperationException(
                $"HTTP client '{name}' has no fake and host.Http.Unfaked is Block, so {request.Method} {request.RequestUri} "
                + $"was not sent. Fake it with host.Http.Fake(\"{name}\")."));
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
