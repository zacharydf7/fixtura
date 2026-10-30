using System.Net;

namespace Fixtura.Tests.Unit;

public class HttpFakeTests
{
    [Fact]
    public async Task SendAsync_ShouldPreferAMethodAndPathResponse_WhenAnAnyRequestResponseWasAddedLater()
    {
        HttpFake fake = new HttpFake("rates").Respond(HttpMethod.Get, "/today", HttpStatusCode.OK).Respond(HttpStatusCode.NotFound);

        HttpResponseMessage response = await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_ShouldUseTheLatestResponse_WhenTwoMatch()
    {
        HttpFake fake = new HttpFake("rates")
            .Respond(HttpMethod.Get, "/today", HttpStatusCode.OK)
            .Respond(HttpMethod.Get, "/today", HttpStatusCode.ServiceUnavailable);

        HttpResponseMessage response = await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("/today?currency=EUR", HttpStatusCode.OK)]
    [InlineData("/today?currency=USD", HttpStatusCode.NotFound)]
    public async Task SendAsync_ShouldMatchTheQuery_WhenThePathHasOne(string path, HttpStatusCode expected)
    {
        HttpFake fake = new HttpFake("rates").Respond(HttpMethod.Get, path, HttpStatusCode.OK).Respond(HttpStatusCode.NotFound);

        HttpResponseMessage response = await SendAsync(fake, HttpMethod.Get, "https://rates.test/today?currency=EUR");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_ShouldNameTheClientAndRequest_WhenNoResponseMatches()
    {
        HttpFake fake = new HttpFake("rates").Respond(HttpMethod.Get, "/today", HttpStatusCode.OK);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(fake, HttpMethod.Post, "https://rates.test/today"));

        Assert.Contains("'rates' has no response for POST https://rates.test/today", error.Message);
    }

    [Fact]
    public async Task Requests_ShouldReadTheBodyWithWebDefaults_WhenTheAppSentJson()
    {
        HttpFake fake = new HttpFake("tracking").Respond(HttpStatusCode.OK);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://tracking.test/track")
        {
            Content = new StringContent("""{"number":"123"}""")
        };

        await fake.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("123", fake.Requests[0].Json<TrackRequest>()?.Number);
    }

    [Fact]
    public async Task Reset_ShouldBringBackTheBaselineAndForgetRequests_WhenATestChangedTheResponses()
    {
        HttpFake fake = new HttpFake("rates").Respond(HttpStatusCode.OK);
        fake.SaveBaseline();
        fake.Respond(HttpStatusCode.ServiceUnavailable);
        await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");

        fake.Reset();

        HttpResponseMessage response = await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");
        Assert.Equal((HttpStatusCode.OK, 1), (response.StatusCode, fake.Requests.Count));
    }

    [Fact]
    public async Task SaveBaseline_ShouldKeepTheFirstBaseline_WhenCalledAgainAfterATestChangedTheResponses()
    {
        HttpFake fake = new HttpFake("rates").Respond(HttpStatusCode.OK);
        fake.SaveBaseline();
        fake.Respond(HttpStatusCode.ServiceUnavailable);
        fake.SaveBaseline();

        fake.Reset();

        HttpResponseMessage response = await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Respond_ShouldAnswerWithTheFunction_WhenGivenOne()
    {
        HttpFake fake = new HttpFake("rates").Respond(request => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            ReasonPhrase = request.RequestUri!.AbsolutePath
        });

        HttpResponseMessage response = await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");

        Assert.Equal((HttpStatusCode.Accepted, "/today"), (response.StatusCode, response.ReasonPhrase));
    }

    [Fact]
    public void Respond_ShouldShowTheExpectedForm_WhenThePathHasNoLeadingSlash()
    {
        var fake = new HttpFake("rates");

        var error = Assert.Throws<ArgumentException>(() => fake.Respond(HttpMethod.Get, "today", HttpStatusCode.OK));

        Assert.Contains("such as \"/today\"", error.Message);
    }

    [Fact]
    public void Fake_ShouldPointToTheNamedOverload_WhenTheTypedClientIsGeneric()
    {
        var http = new TestHttp();

        var error = Assert.Throws<ArgumentException>(() => http.Fake<List<string>>());

        Assert.Contains("use Fake(name)", error.Message);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpFake fake, HttpMethod method, string uri) =>
        fake.SendAsync(new HttpRequestMessage(method, uri), TestContext.Current.CancellationToken);

    private sealed record TrackRequest(string Number);
}
