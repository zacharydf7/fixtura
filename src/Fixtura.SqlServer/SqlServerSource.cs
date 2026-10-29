namespace Fixtura;

/// <summary>Where the test SQL Server comes from. The connection string names a server, not a database.</summary>
public sealed class SqlServerSource
{
    private const string LocalDbServer = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";

    private readonly Func<string?> _resolve;
    private readonly string _description;

    private SqlServerSource(Func<string?> resolve, string description)
    {
        _resolve = resolve;
        _description = description;
    }

    /// <summary>SQL Server LocalDB's default instance, which ships with Visual Studio on Windows.</summary>
    public static SqlServerSource LocalDb { get; } = new(() => LocalDbServer, @"(localdb)\MSSQLLocalDB");

    /// <summary>The connection string in the environment variable, or LocalDB when the variable isn't set.</summary>
    public static SqlServerSource EnvironmentVariableOrLocalDb(string name) =>
        new(() => Environment.GetEnvironmentVariable(name) ?? LocalDbServer, $"{name} or LocalDB");

    internal string? Resolve() => _resolve();

    public override string ToString() => _description;
}
