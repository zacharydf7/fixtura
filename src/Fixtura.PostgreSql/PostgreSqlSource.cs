namespace Fixtura;

/// <summary>Where the test PostgreSQL server comes from. The connection string names a server; its database is replaced.</summary>
public sealed class PostgreSqlSource
{
    private readonly Func<Task<string?>> _resolve;
    private readonly string _description;

    private PostgreSqlSource(Func<Task<string?>> resolve, string description)
    {
        _resolve = resolve;
        _description = description;
    }

    /// <summary>The connection string in the environment variable; no server when it isn't set or is blank.</summary>
    public static PostgreSqlSource EnvironmentVariable(string name) =>
        From($"the {name} variable", () => Task.FromResult(Variable(name)));

    /// <summary>
    /// A server your own code provides, such as one it starts. Return a connection string that names a server, or null when
    /// there is none. Either null or an exception skips the fixture's tests with the reason, or fails them when required.
    /// </summary>
    /// <param name="description">Names the source in skip reasons, such as "a PostgreSQL container".</param>
    public static PostgreSqlSource From(string description, Func<Task<string?>> resolveAsync) => new(resolveAsync, description);

    internal Task<string?> ResolveAsync() => _resolve();

    public override string ToString() => _description;

    // A blank variable counts as unset: CI leaves a secret blank in a fork's pull request.
    private static string? Variable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
