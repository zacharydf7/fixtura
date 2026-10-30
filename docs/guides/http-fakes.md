# Faking outbound HTTP

`host.Http` fakes the app's `IHttpClientFactory` clients in-process:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#http-fakes)]

## Which clients

- `Fake("name")` fakes a named client, registered with `AddHttpClient("name")`.
- `Fake<TClient>()` fakes a typed client registered with `AddHttpClient<TClient>()`, whose name is its type name.
  Building the host throws `NothingMatchedException` when the app registers no such client. A typed client registered
  under a name of its own needs `Fake(name)`.
- Only the client's primary handler is replaced, after any primary handler the app configures, so the app's delegating
  handlers, such as retries and auth headers, still run.
- Clients the app creates with `new HttpClient()` can't be faked.

## Responses

| Call | Answers |
| --- | --- |
| `Respond(method, path, status, json?)` | Requests with that method and path. The path starts with `/`; with a `?` it also matches the query. |
| `Respond(status, json?)` | Every request no method-and-path response matches. |
| `Respond(request => response)` | The same, with a function. |

Method-and-path responses win over the others, and among those that match, the latest wins, so a test can override a
response the fixture set. A request nothing answers throws an exception naming the client and the request. JSON is
written with the web defaults (camelCase).

## Recorded requests

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#recorded-requests)]

`Requests` lists what the app sent, oldest first, after the app's own handlers ran: method, URI, headers (by
case-insensitive name) and body. `Json<T>()` reads the body with the web defaults.

When the app sends the request later, from a background service or an outbox, wait for it instead of sleeping:
`await fake.WaitForRequestAsync(request => ...)` returns the first request the condition accepts, or the first request
at all without one, and fails after five seconds, or the timeout you give, naming the client.

## Unfaked clients

Requests through a client without a fake throw, naming the client and how to fake it. So a forgotten fake, or one
under a name the app doesn't use, such as a Refit client's generated name, fails instead of reaching the network.
Register a Refit client with `AddRefitClient<TApi>(httpClientName: "name")` and fake it with `Fake("name")`, or copy
the name from the error.
`host.Http.Unfaked = UnfakedHttpClients.Allow` lets unfaked clients reach the network, as in production.

## Retries and circuit breakers

The app's resilience handlers, such as those `AddStandardResilienceHandler` or `AddResilienceHandler` add, still run on
a faked client, so a test can check how the app retries. Against a fake that answers with an error, though, each
request then waits through the retries and reaches the fake several times, and a circuit breaker's state carries from
one test to the next. `host.Http.Resilience = FakedClientResilience.Remove` takes those handlers off faked clients, so
each request reaches the fake once. Clients without a fake keep theirs, and so do handlers from the older
Microsoft.Extensions.Http.Polly package.

## Resets and overrides

- `ResetDataAsync` clears the recorded requests and brings back the responses set in `Configure`, undoing any a test
  set.
- In `WithOverrides`, faking a client again gives that host its own fake. Clients it doesn't fake again share the
  fixture's fakes, and their requests show up in the fixture's `Requests`.
