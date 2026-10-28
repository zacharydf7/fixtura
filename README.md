# Fixtura

Compose ASP.NET Core integration-test hosts from small, reusable features instead of a fixture inheritance tree: typed
service and `DbContext` swaps, settings as objects, a test user signed in through the app's real authentication, and
infrastructure that skips tests locally but fails them in CI when it is missing. Fixtura wraps
`WebApplicationFactory<TProgram>` and targets xUnit v3.

**Status:** design spikes in progress. Nothing is published yet.

## License

MIT
