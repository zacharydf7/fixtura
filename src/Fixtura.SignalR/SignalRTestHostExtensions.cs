using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Fixtura;

public static class SignalRTestHostExtensions
{
    extension<TProgram>(TestHost<TProgram> api) where TProgram : class
    {
        /// <summary>
        /// A connection to the app's hub at <paramref name="path"/>, such as <c>/hubs/jobs</c>, signed in as the user
        /// through the app's own authentication, or anonymous when no user is given. It isn't started: record the
        /// messages the test waits for with <c>Record</c>, then call <c>StartAsync</c>. Dispose it with
        /// <c>await using</c>.
        /// </summary>
        /// <param name="configure">Changes the connection further, such as its JSON options or reconnect policy.</param>
        public async Task<HubConnection> CreateHubConnectionAsync(string path, TestUser? user = null,
            Action<IHubConnectionBuilder>? configure = null)
        {
            HttpMessageHandler handler = await api.CreateHandlerAsync(user);
            Uri url = new(api.ClientAddress, path);

            // Long polling runs through the fixture's handler, so it works on the test server, which can't take a
            // WebSocket from a handler, and carries the user's token or cookie and the header tying logs to the test.
            IHubConnectionBuilder builder = new HubConnectionBuilder().WithUrl(url, options =>
            {
                options.HttpMessageHandlerFactory = _ => new SharedHandler(handler);
                options.Transports = HttpTransportType.LongPolling;
            });
            configure?.Invoke(builder);
            return builder.Build();
        }
    }

    // SignalR disposes the handler it gets for each start; this one leaves the fixture's handler for the next start.
    private sealed class SharedHandler(HttpMessageHandler shared) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _invoker = new(shared, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => _invoker.SendAsync(request, cancellationToken);
    }

    extension(HubConnection connection)
    {
        /// <summary>
        /// Keeps every message the hub sends this client's <paramref name="method"/>, such as <c>"JobUpdated"</c>, with
        /// one argument of type <typeparamref name="TMessage"/>. Record before <c>StartAsync</c> to catch messages the
        /// hub sends as the client connects.
        /// </summary>
        public HubMessages<TMessage> Record<TMessage>(string method)
        {
            var messages = new HubMessages<TMessage>(method);
            connection.On<TMessage>(method, messages.Add);
            return messages;
        }
    }
}
