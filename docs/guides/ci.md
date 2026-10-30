# Running in CI

Locally, tests that need a missing server skip with the reason. In CI they should fail instead, so a broken server
can't turn the build green. Set the `RequiredWhenSet` variable to `true` there.

## GitHub Actions with a SQL Server service

Give the job a SQL Server container, point the source's variable at it, and require it:

```yaml
jobs:
  test:
    runs-on: ubuntu-latest
    services:
      sql:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          ACCEPT_EULA: Y
          MSSQL_SA_PASSWORD: ${{ secrets.TEST_SQL_PASSWORD }}
        ports:
          - 1433:1433
    env:
      TEST_SQLSERVER: Server=localhost,1433;User Id=sa;Password=${{ secrets.TEST_SQL_PASSWORD }};TrustServerCertificate=true
      TEST_SQLSERVER_REQUIRED: true
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet test -c Release
```

with this source in the fixture:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#ci-source)]

The resource waits up to `ReadyTimeout` for the server to answer. A service that's still starting can need more than
the 5-second default, so raise it in CI or wait for the server in a step before the tests.

## With Docker instead of a service

On a Linux runner, `SqlServerSource.EnvironmentVariableOrContainer("TEST_SQLSERVER")` with no variable set starts a
container itself. Keep the `RequiredWhenSet` variable set, so a runner without Docker fails instead of skipping.

## Windows runners

GitHub's Windows runners have SQL Server LocalDB, so `EnvironmentVariableOrLocalDb` works there with no variable set.
They can't run Linux containers, so container sources skip on Windows; leave the `RequiredWhenSet` variable unset there
if a Linux job covers them.
