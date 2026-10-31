# Fake a partner API

Bookshop asks a shipping partner for a quote when a book costs less than the free-shipping price. The partner is
reached through a typed `HttpClient`, `ShippingClient`, registered with `AddHttpClient<ShippingClient>()`. This tutorial
fakes the partner in-process, checks what Bookshop sent it, and tests what happens when it's down.

## Fake the client

[!code-csharp[](../samples/Bookshop.Tests/PartnerApi.cs#shipping-fixture)]

- `host.Http.Fake<ShippingClient>()` replaces the client's primary handler with a fake. The app's own handlers on that
  client, such as retries or auth headers, still run.
- `Respond(method, path, status, json)` answers one route. The fixture keeps the fake in a property, so tests can read
  what the app sent.
- Any other client throws instead of reaching the network, so a test can't depend on a real service by accident.
- `LogToTestOutput()` writes the app's logs to the output of the test that caused them. When a test fails, xUnit shows
  them with the failure.

## Check the request and the result

[!code-csharp[](../samples/Bookshop.Tests/PartnerApi.cs#shipping-tests)]

`Requests` lists what the app sent through the client, with its headers and body; `Json<T>()` reads the body into a
type of the test's own, here `QuoteRequest`.
`ResetDataAsync` clears the recorded requests, so the second test can check that none were sent.

## Test the failure path

A test can change a fake's response for itself, as the last test does with
`shop.Shipping.Respond(HttpMethod.Post, "/quotes", HttpStatusCode.ServiceUnavailable)`. The class's tests share the
fixture, so the changed response would leak into the test that runs next. It doesn't, because `ResetDataAsync` also
brings back the responses set in `Configure`. Since the app's logs go to the test's output, a failure here shows the
app's exception and its reason.

The whole file is [PartnerApi.cs](https://github.com/zacharydf7/fixtura/blob/main/docs/samples/Bookshop.Tests/PartnerApi.cs).

## What you have

Bookshop's tests now cover sign-in, settings, a real database and an outside partner, with three short fixtures and no
setup inside any test. The [guides](../guides/fixtures-and-features.md) cover each feature in more depth, and
[Test a real-time hub](test-a-hub.md) adds a SignalR hub.
