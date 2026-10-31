using System.Net;
using System.Net.Http.Json;

namespace Fixtura;

/// <summary>
/// One answer from an <see cref="HttpFake"/>: a status, or a fault the network or the client would raise, optionally
/// after a delay. Give several to <c>Respond</c> to answer a route's requests in order, as retries see them.
/// </summary>
public sealed class FakeResponse
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    private readonly TimeSpan _delay;

    private FakeResponse(Func<HttpRequestMessage, HttpResponseMessage> respond, TimeSpan delay)
    {
        _respond = respond;
        _delay = delay;
    }

    /// <summary>A response with this status, and with JSON when given.</summary>
    public static FakeResponse Status(HttpStatusCode status, object? json = null) => new(_ => new HttpResponseMessage(status)
    {
        Content = json is null ? null : JsonContent.Create(json, json.GetType())
    }, TimeSpan.Zero);

    /// <summary>The <see cref="HttpRequestException"/> a refused or dropped connection raises.</summary>
    public static FakeResponse ConnectionFailure() =>
        new(request => throw new HttpRequestException(HttpRequestError.ConnectionError,
            $"The fake refused the connection for {request.Method} {request.RequestUri}."), TimeSpan.Zero);

    /// <summary>What <c>HttpClient</c> throws when its <c>Timeout</c> passes, at once instead of after it.</summary>
    public static FakeResponse Timeout() =>
        new(request => throw new TaskCanceledException($"The fake timed out {request.Method} {request.RequestUri}.",
            new TimeoutException("The fake's request timed out.")), TimeSpan.Zero);

    /// <summary>This answer, sent after <paramref name="delay"/>, unless the app cancels the request first.</summary>
    public FakeResponse After(TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        return new FakeResponse(_respond, delay);
    }

    internal static FakeResponse From(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(respond, TimeSpan.Zero);

    internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, cancellationToken);
        }

        return _respond(request);
    }
}
