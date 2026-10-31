using System.Net;
using System.Net.Http.Json;

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
    public async Task SendAsync_ShouldListTheResponses_WhenNoResponseMatches()
    {
        HttpFake fake = new HttpFake("rates")
            .Respond(HttpMethod.Get, "/quotes", HttpStatusCode.OK)
            .Respond(HttpMethod.Post, "/orders", HttpStatusCode.Created)
            .Respond(HttpMethod.Get, "/quotes", HttpStatusCode.NotFound);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(fake, HttpMethod.Get, "https://rates.test/quote"));

        Assert.EndsWith("It answers GET /quotes, POST /orders; add one with Respond.", error.Message);
    }

    [Fact]
    public async Task SendAsync_ShouldSayItHasNoResponses_WhenNoneWereSet()
    {
        var fake = new HttpFake("rates");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(fake, HttpMethod.Get, "https://rates.test/quote"));

        Assert.EndsWith("It has no responses; add one with Respond.", error.Message);
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

    [Fact]
    public async Task SendAsync_ShouldAnswerInOrderThenRepeatTheLast_WhenGivenASequence()
    {
        HttpFake fake = new HttpFake("rates").Respond(HttpMethod.Get, "/today",
            FakeResponse.Status(HttpStatusCode.ServiceUnavailable), FakeResponse.Status(HttpStatusCode.OK));

        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < 3; i++)
        {
            statuses.Add((await SendAsync(fake, HttpMethod.Get, "https://rates.test/today")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK, HttpStatusCode.OK], statuses);
    }

    [Fact]
    public async Task SendAsync_ShouldThrowAConnectionError_WhenTheResponseIsAConnectionFailure()
    {
        HttpFake fake = new HttpFake("rates").Respond(HttpMethod.Get, "/today", FakeResponse.ConnectionFailure());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            SendAsync(fake, HttpMethod.Get, "https://rates.test/today"));

        Assert.Equal(HttpRequestError.ConnectionError, error.HttpRequestError);
    }

    [Fact]
    public async Task SendAsync_ShouldThrowWhatHttpClientsTimeoutThrows_WhenTheResponseIsATimeout()
    {
        HttpFake fake = new HttpFake("rates").Respond(HttpMethod.Get, "/today", FakeResponse.Timeout());

        var error = await Assert.ThrowsAsync<TaskCanceledException>(() =>
            SendAsync(fake, HttpMethod.Get, "https://rates.test/today"));

        Assert.IsType<TimeoutException>(error.InnerException);
    }

    [Fact]
    public async Task SendAsync_ShouldStopWaiting_WhenTheAppCancelsADelayedResponse()
    {
        HttpFake fake = new HttpFake("rates")
            .Respond(HttpMethod.Get, "/today", FakeResponse.Status(HttpStatusCode.OK).After(TimeSpan.FromMinutes(5)));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Task send = fake.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://rates.test/today"), cancel.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_ShouldUseTheResponseWhoseConditionHolds_WhenGivenAHeaderCondition()
    {
        HttpFake fake = new HttpFake("payments")
            .Respond(HttpMethod.Post, "/charges", FakeResponse.Status(HttpStatusCode.BadRequest))
            .Respond(HttpMethod.Post, "/charges", request => request.Headers.ContainsKey("Idempotency-Key"),
                FakeResponse.Status(HttpStatusCode.Created));
        var keyed = new HttpRequestMessage(HttpMethod.Post, "https://payments.test/charges");
        keyed.Headers.Add("Idempotency-Key", "abc");

        HttpResponseMessage withKey = await fake.SendAsync(keyed, TestContext.Current.CancellationToken);
        HttpResponseMessage withoutKey = await SendAsync(fake, HttpMethod.Post, "https://payments.test/charges");

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.BadRequest), (withKey.StatusCode, withoutKey.StatusCode));
    }

    [Fact]
    public async Task SendAsync_ShouldUseTheResponseWhoseConditionHolds_WhenGivenABodyCondition()
    {
        HttpFake fake = new HttpFake("tracking")
            .Respond(HttpStatusCode.NotFound)
            .Respond(HttpMethod.Post, "/track", request => request.Json<TrackRequest>()?.Number == "123",
                FakeResponse.Status(HttpStatusCode.OK));
        var known = new HttpRequestMessage(HttpMethod.Post, "https://tracking.test/track")
        {
            Content = JsonContent.Create(new TrackRequest("123"))
        };
        var unknown = new HttpRequestMessage(HttpMethod.Post, "https://tracking.test/track")
        {
            Content = JsonContent.Create(new TrackRequest("999"))
        };

        HttpResponseMessage knownResponse = await fake.SendAsync(known, TestContext.Current.CancellationToken);
        HttpResponseMessage unknownResponse = await fake.SendAsync(unknown, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound), (knownResponse.StatusCode, unknownResponse.StatusCode));
    }

    [Fact]
    public async Task VerifyAllResponsesUsed_ShouldListWhatNoRequestReached_WhenSomeResponsesWentUnsent()
    {
        HttpFake fake = new HttpFake("rates")
            .Respond(HttpMethod.Get, "/today", FakeResponse.Status(HttpStatusCode.ServiceUnavailable),
                FakeResponse.Status(HttpStatusCode.OK))
            .Respond(HttpMethod.Get, "/history", HttpStatusCode.OK);
        await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");

        var error = Assert.Throws<InvalidOperationException>(fake.VerifyAllResponsesUsed);

        Assert.EndsWith("GET /today (1 of 2 unsent), GET /history.", error.Message);
    }

    [Fact]
    public async Task VerifyAllResponsesUsed_ShouldPass_WhenEveryResponseWasSent()
    {
        HttpFake fake = new HttpFake("rates")
            .Respond(HttpMethod.Get, "/today", FakeResponse.Status(HttpStatusCode.ServiceUnavailable),
                FakeResponse.Status(HttpStatusCode.OK));
        await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");
        await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");

        Exception? error = Record.Exception(fake.VerifyAllResponsesUsed);

        Assert.Null(error);
    }

    [Fact]
    public async Task Reset_ShouldStartAConfiguredSequenceOver_WhenATestUsedPartOfIt()
    {
        HttpFake fake = new HttpFake("rates").Respond(HttpMethod.Get, "/today",
            FakeResponse.Status(HttpStatusCode.ServiceUnavailable), FakeResponse.Status(HttpStatusCode.OK));
        fake.SaveBaseline();
        await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");

        fake.Reset();
        HttpResponseMessage response = await SendAsync(fake, HttpMethod.Get, "https://rates.test/today");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // Responses set in Configure are the fixture's defaults, which a test needn't use.
    [Fact]
    public void VerifyAllResponsesUsed_ShouldPass_WhenOnlyConfiguredResponsesWentUnsent()
    {
        HttpFake fake = new HttpFake("rates").Respond(HttpMethod.Get, "/today", HttpStatusCode.OK);
        fake.SaveBaseline();

        Exception? error = Record.Exception(fake.VerifyAllResponsesUsed);

        Assert.Null(error);
    }

    [Fact]
    public async Task SendAsync_ShouldSayARouteHasACondition_WhenTheConditionTurnsTheRequestAway()
    {
        HttpFake fake = new HttpFake("payments").Respond(HttpMethod.Post, "/charges",
            request => request.Headers.ContainsKey("Idempotency-Key"), FakeResponse.Status(HttpStatusCode.Created));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SendAsync(fake, HttpMethod.Post, "https://payments.test/charges"));

        Assert.Contains("It answers POST /charges (when its condition holds);", error.Message);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpFake fake, HttpMethod method, string uri) =>
        fake.SendAsync(new HttpRequestMessage(method, uri), TestContext.Current.CancellationToken);

    private sealed record TrackRequest(string Number);
}
