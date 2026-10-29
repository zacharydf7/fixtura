namespace Fixtura.Spike;

// A reusable piece of host configuration, added with host.Use<TFeature>().
public interface ITestHostFeature
{
    void Configure(TestHostBuilder host);
}
