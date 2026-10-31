using Grpc.Net.Client;

namespace Fixtura;

public static class GrpcTestHostExtensions
{
    extension<TProgram>(TestHost<TProgram> api) where TProgram : class
    {
        /// <summary>
        /// A channel to the app's gRPC services through the fixture's own handler, signed in as the user through the
        /// app's own authentication, or anonymous when no user is given. Pass it to the client the gRPC tooling generated
        /// from the app's .proto file, such as <c>new Greeter.GreeterClient(channel)</c>, and dispose it with
        /// <c>using</c>. It needs the in-memory test server, which speaks HTTP/2 to the fixture's handler; Kestrel serves
        /// plain-HTTP gRPC only to an app that sets its endpoints to HTTP/2.
        /// </summary>
        /// <param name="configure">Changes the channel further, such as its message size limits.</param>
        public async Task<GrpcChannel> CreateGrpcChannelAsync(TestUser? user = null,
            Action<GrpcChannelOptions>? configure = null)
        {
            HttpMessageHandler handler = await api.CreateHandlerAsync(user);
            var options = new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = true };
            configure?.Invoke(options);
            return GrpcChannel.ForAddress(api.ClientAddress, options);
        }
    }
}
