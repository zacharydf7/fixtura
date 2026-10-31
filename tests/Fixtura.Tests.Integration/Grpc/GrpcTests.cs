using Fixtura.Tests.Integration.Users;
using Grpc.Core;
using Grpc.Net.Client;
using SymmetricJwtApp.Greeting;

namespace Fixtura.Tests.Integration.Grpc;

// The greeter needs a signed-in caller and answers with the caller's name.
public class GrpcTests(SymmetricJwtFixture api) : IClassFixture<SymmetricJwtFixture>
{
    [Fact]
    public async Task CreateGrpcChannelAsync_ShouldSignTheUserIn_WhenGivenAUser()
    {
        using GrpcChannel channel = await api.CreateGrpcChannelAsync(TestUser.Named("ann"));
        var greeter = new Greeter.GreeterClient(channel);

        GreetReply reply = await greeter.GreetAsync(new GreetRequest { Greeting = "Hello" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Hello, ann", reply.Message);
    }

    [Fact]
    public async Task CreateGrpcChannelAsync_ShouldCallAnonymously_WhenGivenNoUser()
    {
        using GrpcChannel channel = await api.CreateGrpcChannelAsync();
        var greeter = new Greeter.GreeterClient(channel);

        var error = await Assert.ThrowsAsync<RpcException>(async () => await greeter.GreetAsync(
            new GreetRequest { Greeting = "Hello" }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Unauthenticated, error.StatusCode);
    }
}
