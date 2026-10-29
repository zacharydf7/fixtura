namespace Fixtura;

/// <summary>A reusable piece of host configuration, added with <see cref="TestHostBuilder.Use{TFeature}"/>.</summary>
public interface ITestHostFeature
{
    void Configure(TestHostBuilder host);
}
