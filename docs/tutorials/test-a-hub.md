# Test a real-time hub

Bookshop's staff watch orders come in live. A SignalR hub, `OrderFeed` at `/hubs/orders`, lets clerks connect, and each
order a customer places is sent to them as an `OrderPlaced` message. This tutorial connects to the hub as a clerk, places
an order as a customer, and waits for the message, then checks that a customer can't listen in.

Hub tests usually need a handler factory, a signed-in transport and an observer that collects messages and waits for
one. The `Fixtura.SignalR` package does that part, so the test only holds what it checks:

```
dotnet add package Fixtura.SignalR
```

## The hub

The hub has no methods of its own and needs the `Clerk` role:

[!code-csharp[](../samples/Bookshop/Shop.cs#order-feed)]

Program.cs maps it with `app.MapHub<OrderFeed>("/hubs/orders")`, and once `POST /orders` has saved an order, it tells
every connected client with `feed.Clients.All.SendAsync("OrderPlaced", order)`.

## The fixture

[!code-csharp[](../samples/Bookshop.Tests/OrderFeed.cs#feed-fixture)]

It uses the features from the [secured API](test-a-secured-api.md) tutorial and free shipping, so no test reaches the
shipping partner, and sends the app's logs to the test's output. The hub needs nothing more from the fixture.

## Wait for the message

[!code-csharp[](../samples/Bookshop.Tests/OrderFeed.cs#feed-tests)]

- `shop.CreateHubConnectionAsync("/hubs/orders", user)` returns the app's hub connection, signed in as `sam` through
  Bookshop's own JWT authentication, as `CreateClientAsync(user)` signs in an HTTP client. It reaches the in-memory test
  server through the fixture's handler, so there's no URL or transport to set up.
- `feed.Record<Order>("OrderPlaced")` keeps every `OrderPlaced` message the hub sends this client. Recording before
  `StartAsync` means no message can arrive before the test is listening.
- The customer's request can return before the clerk's message arrives, so the test waits for it.
  `WaitForAsync(order => order.Customer == "ann")` returns the first message that matches, and fails after five seconds
  if none does, naming the method it waited for.
- A user without the `Clerk` role is turned away when the connection starts: SignalR's negotiate request answers 403,
  and `StartAsync` throws an `HttpRequestException` with that status.

The whole file is [OrderFeed.cs](https://github.com/zacharydf7/fixtura/blob/main/docs/samples/Bookshop.Tests/OrderFeed.cs).

## What you have

A hub test that reads like the HTTP tests: who connects, what they do, what arrives. The [SignalR guide](../guides/signalr.md)
covers the rest, such as changing the connection's JSON options and reading every message received so far.
