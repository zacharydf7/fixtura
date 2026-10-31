# SignalR

The `Fixtura.SignalR` package connects a test to the app's hubs, signed in as a test user, and keeps the messages a hub
sends the client, so a test can wait for one instead of writing its own observer:

```csharp
await using HubConnection hub = await api.CreateHubConnectionAsync("/hubs/jobs", TestUser.Named("ann"));
HubMessages<JobUpdate> updates = hub.Record<JobUpdate>("JobUpdated");
await hub.StartAsync();

await hub.InvokeAsync("Watch", jobId);

JobUpdate update = await updates.WaitForAsync(update => update.JobId == jobId);
```

## Connecting

`CreateHubConnectionAsync(path, user?)` returns the app's hub connection, not yet started:

- It goes through the fixture's own handler, so it works on the in-memory test server and on Kestrel alike, with no
  handler factory, URL or transport to set up.
- The user signs in through the app's own authentication, as `CreateClientAsync(user)` does, whether by bearer token or
  cookie. Without a user the connection is anonymous, and a hub that needs one turns it away when it starts.
- The connection uses long polling, the transport the test server can serve through a handler. The hub's code runs the
  same as over WebSockets.
- The hub's logs go to the test that connected, with `LogToTestOutput` or `CaptureLogs`.
- `configure` changes the connection further, such as its JSON options: `api.CreateHubConnectionAsync("/hubs/jobs",
  user, hub => hub.AddJsonProtocol(...))`.

## Waiting for messages

`hub.Record<TMessage>(method)` keeps every message the hub sends that client method, with one argument of type
`TMessage`:

- `Received` lists them, oldest first.
- `WaitForAsync(message => ...)` waits for one that matches, any message unless given, and fails after five seconds, or
  the timeout you give, naming the method.
- Record before `StartAsync` to keep the messages the hub sends as the client connects, such as a greeting from
  `OnConnectedAsync`.
