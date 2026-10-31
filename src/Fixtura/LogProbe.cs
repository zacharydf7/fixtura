using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fixtura;

// A logger provider a fixture adds, which notes the probe log instead of keeping it.
internal interface IProbedLoggerProvider
{
    // The setting that added the provider, for the error.
    string Setting { get; }

    bool Probed { get; }
}

// Apps can swap in a logger factory of their own, as Serilog does, which may ignore the providers a test adds. The
// framework's factory always uses them, so only a replaced one gets the probe, which its other sinks see too.
internal static class LogProbe
{
    public const string Category = "Fixtura.LogProbe";

    public static void EnsureReachable(IServiceProvider services)
    {
        ILoggerFactory factory = services.GetRequiredService<ILoggerFactory>();
        List<IProbedLoggerProvider> providers =
            services.GetServices<ILoggerProvider>().OfType<IProbedLoggerProvider>().ToList();
        if (providers.Count == 0 || factory.GetType() == typeof(LoggerFactory))
        {
            return;
        }

        factory.CreateLogger(Category).Log(LogLevel.Critical, "Checking that the app's logs reach the test.");
        List<string> unreached = [.. providers.Where(provider => !provider.Probed).Select(provider => provider.Setting)];
        if (unreached.Count > 0)
        {
            throw new InvalidOperationException($"{string.Join(" and ", unreached)} can't see the app's logs: its logger "
                + $"factory, {factory.GetType().FullName}, doesn't pass them to the providers a test adds. With Serilog, "
                + "pass writeToProviders: true to UseSerilog or AddSerilog.");
        }
    }
}
