namespace Fixtura;

/// <summary>Where the test SQL Server is and what happens when it can't be reached.</summary>
public sealed class SqlServerOptions
{
    /// <summary>Where the server comes from; <see cref="SqlServerSource.LocalDb"/> unless set.</summary>
    public SqlServerSource Source { get; init; } = SqlServerSource.LocalDb;

    /// <summary>
    /// An environment variable that, when "true" or "1", makes a missing server fail the fixture instead of skipping its
    /// tests. Set it in CI.
    /// </summary>
    public string? RequiredWhenVariableIsTrue { get; init; }

    /// <summary>
    /// How long to wait for the server to answer, rounded up to whole seconds; an unreachable server costs this once per
    /// test run, because every later fixture reuses the answer. Must be positive.
    /// </summary>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Tables whose rows <c>ResetDataAsync</c> keeps, such as reference data the app's migrations insert, as "Table",
    /// "schema.Table" or "[schema].[Table]". A name that matches no table throws. EF Core's migrations history is always
    /// kept.
    /// </summary>
    public IReadOnlyList<string> KeepOnReset { get; init; } = [];
}
