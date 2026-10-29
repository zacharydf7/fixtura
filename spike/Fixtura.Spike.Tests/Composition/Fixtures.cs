namespace Fixtura.Spike.Tests.Composition;

// The three SQL host shapes, built from shared features with no inheritance between them.
public sealed class ApiSqlFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Use<SampleSqlDatabases>().Use<FakeBus>().Use<QuietBackground>();
}

public sealed class ApiWorkerSqlFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Use<SampleSqlDatabases>().Use<FakeBus>().Use<NoReportScheduler>();
}

public sealed class ApiWorkerPartnerSqlFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Use<SampleSqlDatabases>().Use<FakeBus>().Use<NoReportScheduler>().Use<PartnerCarrier>();
}
