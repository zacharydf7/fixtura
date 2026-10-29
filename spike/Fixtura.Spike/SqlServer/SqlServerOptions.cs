namespace Fixtura.Spike.SqlServer;

public sealed class SqlServerOptions
{
    public SqlServerSource Source { get; init; } = SqlServerSource.LocalDb;

    // When this environment variable is "true" or "1", a missing server fails the fixture instead of skipping its tests.
    public string? RequiredWhenSet { get; init; }

    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
