using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Fixtura;

// Ties a request to the test that sent it with a header, so the request's logs reach that test. Running the request in
// the test's execution context would do the same, but would also hand the app the test's culture and other ambient
// state, and only on the in-memory test server.
internal static class TestRequests
{
    private const string Header = "Fixtura-Test";
    private static readonly ConcurrentDictionary<string, TaggedRequest> Tagged = new();
    private static readonly AsyncLocal<TaggedRequest?> Current = new();

    // The test that sent the request the current work belongs to, or null outside such a request.
    public static ITestContext? Sender => Current.Value?.Sender;

    public static IStartupFilter StartupFilter { get; } = new ReadHeaderFilter();

    // Where an error the app logs while handling the current request is kept, for FailOnAppErrors; null outside one.
    public static ILogger? ErrorLogger(string category, IExternalScopeProvider? scopes)
    {
        if (Current.Value is not { } tagged)
        {
            return null;
        }

        if (scopes is not null)
        {
            tagged.Errors.SetScopeProvider(scopes);
        }

        return tagged.Errors.CreateLogger(category);
    }

    // Tags each request a test sends, including each redirect it follows. With isFailure, a request that logged an error
    // it accepts throws once the response starts, without waiting for the body, which may stream without end.
    public sealed class TagHandler(Func<FakeLogRecord, bool>? isFailure) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ITestContext test = TestContext.Current;
            if (test.Test is null)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            string id = Guid.NewGuid().ToString("N");
            var tagged = new TaggedRequest(test);
            Tagged[id] = tagged;
            request.Headers.Remove(Header);
            request.Headers.Add(Header, id);
            try
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                List<FakeLogRecord> errors =
                    isFailure is null ? [] : [.. tagged.Errors.Collector.GetSnapshot().Where(isFailure)];
                if (errors.Count > 0)
                {
                    response.Dispose();
                    Throw(request, errors);
                }

                return response;
            }
            finally
            {
                Tagged.TryRemove(id, out _);
            }
        }

        private static void Throw(HttpRequestMessage request, List<FakeLogRecord> errors)
        {
            IEnumerable<string> lines = errors.Select(error => $"{error.Level} {error.Category}: {error.Message}"
                + (error.Exception is null ? "" : Environment.NewLine + error.Exception));
            throw new InvalidOperationException($"The app logged {errors.Count} error(s) while handling "
                + $"{request.Method} {request.RequestUri}, and the fixture fails on app errors. Accept errors a test "
                + "expects with FailOnAppErrors(except: ...)." + Environment.NewLine
                + string.Join(Environment.NewLine, lines));
        }
    }

    private sealed class TaggedRequest(ITestContext sender)
    {
        public ITestContext Sender { get; } = sender;

        public FakeLoggerProvider Errors { get; } = new(new FakeLogCollector());
    }

    // Runs ahead of the app's own middleware and takes the header away, so the app never sees it.
    private sealed class ReadHeaderFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.Remove(Header, out StringValues id)
                    && Tagged.TryGetValue(id.ToString(), out TaggedRequest? tagged))
                {
                    Current.Value = tagged;
                }

                await nextMiddleware(context);
            });
            next(app);
        };
    }
}
