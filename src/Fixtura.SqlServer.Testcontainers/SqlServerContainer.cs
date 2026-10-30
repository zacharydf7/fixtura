using System.Collections.Concurrent;
using Testcontainers.MsSql;

namespace Fixtura;

/// <summary>
/// SQL Server sources that run the server in a Docker container: <c>SqlServerSource.Container(image)</c> and
/// <c>SqlServerSource.EnvironmentVariableOrContainer(name, image)</c>, from <see cref="DefaultImage"/> unless an image is
/// given.
/// </summary>
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
            SqlServerSource.From($"a SQL Server container ({image})", async cancellationToken => await StartOnceAsync(image).WaitAsync(cancellationToken));

        /// <summary>
        /// The connection string in the environment variable, such as a CI service's server, or a SQL Server container
        /// from <paramref name="image"/> when the variable isn't set or is blank.
        /// </summary>
        public static SqlServerSource EnvironmentVariableOrContainer(string name, string image = DefaultImage) =>
            SqlServerSource.From($"the {name} variable, or a SQL Server container ({image}) when it isn't set",
                async cancellationToken =>
                {
                    // A blank variable counts as unset: CI leaves a secret blank in a fork's pull request.
                    string? server = Environment.GetEnvironmentVariable(name);
                    return string.IsNullOrWhiteSpace(server) ? await StartOnceAsync(image).WaitAsync(cancellationToken) : server;
                });
    }

    private static Task<string> StartOnceAsync(string image) =>
        Containers.GetOrAdd(image, _ => new Lazy<Task<string>>(() => StartAsync(image))).Value;

    private static async Task<string> StartAsync(string image)
    {
        // Testcontainers' default SA password is public. The port stays on all interfaces, so a remote Docker host works.
        MsSqlContainer container = new MsSqlBuilder(image).WithPassword($"Fx-{Guid.NewGuid():N}").Build();
        await container.StartAsync();
        return container.GetConnectionString();
    }
}
