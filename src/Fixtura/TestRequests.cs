using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Fixtura;

// Ties a request to the test that sent it with a header, so the request's logs reach that test's output. Running the
// request in the test's execution context would do the same, but would also hand the app the test's culture and other
// ambient state, and only on the in-memory test server.
internal static class TestRequests
{
    private const string Header = "Fixtura-Test";
    private static readonly ConcurrentDictionary<string, ITestContext> Senders = new();
    private static readonly AsyncLocal<ITestContext?> CurrentSender = new();

    // The test that sent the request the current work belongs to, or null outside such a request.
    public static ITestContext? Sender => CurrentSender.Value;

    public static IStartupFilter StartupFilter { get; } = new ReadHeaderFilter();

    // Tags each request a test sends, including each redirect it follows.
    public sealed class TagHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ITestContext test = TestContext.Current;
            if (test.Test is null)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            string id = Guid.NewGuid().ToString("N");
            Senders[id] = test;
            request.Headers.Remove(Header);
            request.Headers.Add(Header, id);
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                Senders.TryRemove(id, out _);
            }
        }
    }

    // Runs ahead of the app's own middleware and takes the header away, so the app never sees it.
    private sealed class ReadHeaderFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.Remove(Header, out StringValues id)
                    && Senders.TryGetValue(id.ToString(), out ITestContext? sender))
                {
                    CurrentSender.Value = sender;
                }

                await nextMiddleware(context);
            });
            next(app);
        };
    }
}
