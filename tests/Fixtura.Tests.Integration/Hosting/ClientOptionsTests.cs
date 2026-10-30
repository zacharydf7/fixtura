using System.Net;
using System.Net.Http.Json;
using Fixtura.Tests.Integration.Users;

namespace Fixtura.Tests.Integration.Hosting;

// Records each request it passes on, so a test can see what the client sent.
public sealed class RecordingHandler : DelegatingHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return base.SendAsync(request, cancellationToken);
    }
}

public class ClientOptionsTests(PlainFixture api) : IClassFixture<PlainFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldReturnTheRedirect_WhenAutoRedirectIsOff()
    {
        HttpClient client = await api.CreateClientAsync(null, new TestClientOptions { AllowAutoRedirect = false });

        HttpResponseMessage response = await client.GetAsync("/home", TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Redirect, "/ping"), (response.StatusCode, response.Headers.Location?.OriginalString));
    }

    [Fact]
    public async Task CreateClientAsync_ShouldFollowTheRedirect_WhenGivenNoOptions()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.GetAsync("/home", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldSendThroughTheHandlersAgainstTheBaseAddress_WhenGivenBoth()
    {
        var recording = new RecordingHandler();
        var options = new TestClientOptions { BaseAddress = new Uri("http://localhost/api/"), Handlers = [recording] };
        HttpClient client = await api.CreateClientAsync(null, options);

        await client.GetAsync("orders", TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("http://localhost/api/orders"), Assert.Single(recording.Requests));
    }
}

// A cookie user's client, with the app's Secure cookie that its first request renews.
public class CookieClientOptionsTests(SecureCookieFixture api) : IClassFixture<SecureCookieFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldStaySignedIn_WhenACookieUsersClientTurnsRedirectsOff()
    {
        var options = new TestClientOptions { AllowAutoRedirect = false };
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"), options);

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("ann", me!.Name);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldStaySignedIn_WhenACookieUsersClientHasAnHttpBaseAddress()
    {
        var options = new TestClientOptions { BaseAddress = new Uri("http://localhost/") };
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"), options);
        await client.GetAsync("/me", TestContext.Current.CancellationToken);

        HttpResponseMessage second = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}
