namespace Fixtura;

/// <summary>Where the test SQL Server comes from. The connection string names a server, not a database.</summary>
public sealed class SqlServerSource
{
    private const string LocalDbServer = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";

    private readonly Func<Task<string?>> _resolve;
    private readonly string _description;

    private SqlServerSource(Func<Task<string?>> resolve, string description)
    {
        _resolve = resolve;
        _description = description;
    }

    /// <summary>SQL Server LocalDB's default instance, which ships with Visual Studio on Windows.</summary>
    public static SqlServerSource LocalDb { get; } = From(@"(localdb)\MSSQLLocalDB", () => Task.FromResult<string?>(LocalDbServer));

    /// <summary>The connection string in the environment variable, or LocalDB when the variable isn't set or is blank.</summary>
    public static SqlServerSource EnvironmentVariableOrLocalDb(string name) =>
        From($"{name} or LocalDB", () => Task.FromResult<string?>(Variable(name) ?? LocalDbServer));

    /// <summary>
    /// A server your own code provides, such as one it starts. Return a connection string that names a server, or null when
    /// there is none. Either null or an exception skips the fixture's tests with the reason, or fails them when required.
    /// </summary>
    /// <param name="description">Names the source in skip reasons, such as "a SQL Server container".</param>
    public static SqlServerSource From(string description, Func<Task<string?>> resolveAsync) => new(resolveAsync, description);

    internal Task<string?> ResolveAsync() => _resolve();

    // A blank variable counts as unset: CI leaves a secret blank in a fork's pull request.
    private static string? Variable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public override string ToString() => _description;
}
