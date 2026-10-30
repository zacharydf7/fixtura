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

## Unfaked clients

`host.Http.Unfaked = UnfakedHttpClients.Block` makes requests through any client without a fake throw, naming the
client and how to fake it. The default, `Allow`, lets them reach the network.

## Resets and overrides

- `ResetDataAsync` clears the recorded requests and brings back the responses set in `Configure`, undoing any a test
  set.
- In `WithOverrides`, faking a client again gives that host its own fake. Clients it doesn't fake again share the
  fixture's fakes, and their requests show up in the fixture's `Requests`.
