using System.Net;
using System.Net.Http.Json;
using Fixtura.Tests.Integration.Hosting;
using Microsoft.AspNetCore.Http;
using SampleApp;

namespace Fixtura.Tests.Integration.Http;

// The app's controllers name properties in snake_case, which the minimal APIs' options would read as missing.
public sealed class ControllersJsonFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Json = AppJson.Controllers;
}

public class ReadResponseTests(PlainFixture api) : IClassFixture<PlainFixture>
{
    // The app writes enums as names, which the default options can't read back.
    [Fact]
    public async Task ReadAsync_ShouldReadWithTheMinimalApisJsonOptions_WhenTheFixtureSetsNone()
    {
        HttpClient client = await api.CreateClientAsync();
        HttpResponseMessage response = await client.GetAsync("/parcels/7", TestContext.Current.CancellationToken);

        Parcel parcel = await api.ReadAsync<Parcel>(response);

        Assert.Equal(new Parcel("7", ParcelStatus.InTransit), parcel);
    }

    [Fact]
    public async Task ReadAsync_ShouldShowTheBody_WhenItIsNotTheType()
    {
        HttpClient client = await api.CreateClientAsync();
        HttpResponseMessage response = await client.GetAsync("/ping", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.ReadAsync<List<Parcel>>(response));

        Assert.Contains("200 response isn't a List<Parcel>: pong", error.Message);
    }

    [Fact]
    public async Task ReadProblemAsync_ShouldReadTheValidationErrors_WhenTheStatusMatches()
    {
        HttpClient client = await api.CreateClientAsync();
        HttpResponseMessage response = await client.PostAsJsonAsync("/returns", new ReturnRequest(""),
            TestContext.Current.CancellationToken);

        HttpValidationProblemDetails problem = await api.ReadProblemAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal(["Say why the parcel is going back."], problem.Errors["Reason"]);
    }

    [Fact]
    public async Task ReadProblemAsync_ShouldThrow_WhenTheStatusDiffers()
    {
        HttpClient client = await api.CreateClientAsync();
        HttpResponseMessage response = await client.PostAsJsonAsync("/returns", new ReturnRequest("Damaged"),
            TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.ReadProblemAsync(response, HttpStatusCode.BadRequest));

        Assert.Contains("Expected 400 BadRequest, but the app answered 200 OK", error.Message);
    }

    [Fact]
    public async Task ReadProblemAsync_ShouldThrow_WhenTheBodyIsNotAProblem()
    {
        HttpClient client = await api.CreateClientAsync();
        HttpResponseMessage response = await client.GetAsync("/parcels/7", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.ReadProblemAsync(response, HttpStatusCode.OK));

        Assert.Contains("The app's 200 response holds no problem details: {", error.Message);
    }
}

public class ControllersJsonTests(ControllersJsonFixture api) : IClassFixture<ControllersJsonFixture>
{
    [Fact]
    public async Task ReadAsync_ShouldReadWithTheControllersJsonOptions_WhenTheFixtureAsksForThem()
    {
        HttpClient client = await api.CreateClientAsync();
        HttpResponseMessage response = await client.GetAsync("/labels/7", TestContext.Current.CancellationToken);

        ParcelLabel label = await api.ReadAsync<ParcelLabel>(response);

        Assert.Equal(new ParcelLabel("7", "Ground"), label);
    }

    // [ApiController] answers an invalid model with MVC's own validation problem.
    [Fact]
    public async Task ReadProblemAsync_ShouldReadAControllersValidationProblem_WhenTheModelIsInvalid()
    {
        HttpClient client = await api.CreateClientAsync();
        HttpResponseMessage response = await client.PostAsJsonAsync("/labels", new { carrier_name = "Ground" },
            TestContext.Current.CancellationToken);

        HttpValidationProblemDetails problem = await api.ReadProblemAsync(response, HttpStatusCode.BadRequest);

        Assert.Contains("ParcelNumber", problem.Errors.Keys);
    }
}
