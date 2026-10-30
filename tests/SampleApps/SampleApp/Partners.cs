using System.Net.Http.Json;

namespace SampleApp;

public sealed record Rate(string Currency, decimal Value);

public sealed record TrackRequest(string Number);

public sealed record TrackResponse(string Status);

// A typed client for a parcel-tracking partner.
public sealed class TrackingClient(HttpClient http)
{
    public async Task<string> TrackAsync(string number)
    {
        HttpResponseMessage response = await http.PostAsJsonAsync("track", new TrackRequest(number));
        response.EnsureSuccessStatusCode();
        TrackResponse? body = await response.Content.ReadFromJsonAsync<TrackResponse>();
        return body!.Status;
    }
}

// The app's own handler on its rates client, which a test fake must not remove.
public sealed class ApiKeyHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Api-Key", "sample-key");
        return base.SendAsync(request, cancellationToken);
    }
}
