using System.Collections.Concurrent;
using Testcontainers.MsSql;

namespace Fixtura;

/// <summary>SQL Server sources that run the server in a Docker container.</summary>
public static class SqlServerContainer
{
    /// <summary>The SQL Server image used unless another is given.</summary>
    public const string DefaultImage = "mcr.microsoft.com/mssql/server:2022-latest";

    // One container per image per test run, shared by every fixture. Testcontainers' resource reaper removes it when the
    // run ends, unless the reaper is turned off.
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Containers = new();

    extension(SqlServerSource)
    {
        /// <summary>
        /// A SQL Server container from <paramref name="image"/>, started on first use and shared by every fixture in the
        /// test run. Without Docker the fixture's tests skip with the reason.
        /// </summary>
        public static SqlServerSource Container(string image = DefaultImage) =>
            SqlServerSource.From($"a SQL Server container ({image})", async () => await StartOnceAsync(image));

        /// <summary>
        /// The connection string in the environment variable, such as a CI service's server, or a SQL Server container
        /// from <paramref name="image"/> when the variable isn't set.
        /// </summary>
        public static SqlServerSource EnvironmentVariableOrContainer(string name, string image = DefaultImage) =>
            SqlServerSource.From($"{name} or a SQL Server container ({image})",
                async () => Environment.GetEnvironmentVariable(name) ?? await StartOnceAsync(image));
    }

    private static Task<string> StartOnceAsync(string image) =>
        Containers.GetOrAdd(image, _ => new Lazy<Task<string>>(() => StartAsync(image))).Value;

    private static async Task<string> StartAsync(string image)
    {
        MsSqlContainer container = new MsSqlBuilder(image).Build();
        await container.StartAsync();
        return container.GetConnectionString();
    }
}
