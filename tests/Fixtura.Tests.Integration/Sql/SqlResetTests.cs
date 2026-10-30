using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Fixtura.Tests.Integration.Sql;

public class SqlResetTests
{
    [Fact]
    public async Task ResetAsync_ShouldEmptyEveryTable_WhenTablesReferenceEachOther()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync();

        await db.Sql.ResetAsync(TestContext.Current.CancellationToken);

        int rows = await db.ScalarAsync(
            "SELECT (SELECT COUNT(*) FROM Customers) + (SELECT COUNT(*) FROM Orders) + (SELECT COUNT(*) FROM Departments) + (SELECT COUNT(*) FROM Staff)");
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task ResetAsync_ShouldKeepTheMigrationsHistoryAndTheListedTables_WhenItEmptiesTheRest()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync();

        await db.Sql.ResetAsync(TestContext.Current.CancellationToken);

        int kept = await db.ScalarAsync("""
            SELECT (SELECT COUNT(*) FROM Countries) + (SELECT COUNT(*) FROM Currencies) + (SELECT COUNT(*) FROM Regions)
                + (SELECT COUNT(*) FROM __EFMigrationsHistory) + (SELECT COUNT(*) FROM MigrationLog)
            """);
        Assert.Equal(5, kept);
    }

    [Fact]
    public async Task ResetAsync_ShouldRestartOnlySequencesThatEmptiedTablesDrawFrom_WhenValuesWereDrawn()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync();

        await db.Sql.ResetAsync(TestContext.Current.CancellationToken);

        int invoices = await db.ScalarAsync("SELECT NEXT VALUE FOR InvoiceNumbers");
        int regions = await db.ScalarAsync("SELECT NEXT VALUE FOR RegionNumbers");
        int hiLo = await db.ScalarAsync("SELECT NEXT VALUE FOR HiLoNumbers");
        Assert.Equal((100, 2, 2), (invoices, regions, hiLo));
    }

    [Fact]
    public async Task ResetAsync_ShouldThrow_WhenAKeptNameMatchesNoTable()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync(keep: ["Countries", "Countrys"]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Sql.ResetAsync(TestContext.Current.CancellationToken));

        Assert.StartsWith("KeepOnReset names Countrys, which match no table", error.Message);
    }

    [Fact]
    public async Task ResetAsync_ShouldThrow_WhenAKeptNameIsAHistoryTable()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync(keep: ["Prices", "PricesHistory"]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Sql.ResetAsync(TestContext.Current.CancellationToken));

        Assert.StartsWith("KeepOnReset names PricesHistory, the history table of dbo.Prices.", error.Message);
    }

    // dbo.Rates keeps its history in history.Rates, so the name matches both.
    [Fact]
    public async Task ResetAsync_ShouldKeepTheTable_WhenAHistoryTableInAnotherSchemaHasItsName()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync(keep: ["Rates"]);

        await db.Sql.ResetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.Rates"));
    }

    [Fact]
    public async Task ResetAsync_ShouldNotCheckTheKeptNames_WhenTheAppHasNotCreatedItsDatabaseYet()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync(keep: ["Countrys"], createDatabase: false);

        Exception? error = await Record.ExceptionAsync(() => db.Sql.ResetAsync(TestContext.Current.CancellationToken));

        Assert.Null(error);
    }

    [Fact]
    public async Task ResetAsync_ShouldStartIdentityColumnsAgain_WhenRowsWereInserted()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync();

        await db.Sql.ResetAsync(TestContext.Current.CancellationToken);

        int id = await db.ScalarAsync("INSERT INTO Customers (CountryId) VALUES (1); SELECT CAST(SCOPE_IDENTITY() AS int)");
        Assert.Equal(1, id);
    }

    [Fact]
    public async Task ResetAsync_ShouldLeaveEveryForeignKeyEnabledAndTrusted_WhenTablesFormACycle()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync();

        await db.Sql.ResetAsync(TestContext.Current.CancellationToken);

        int untrusted = await db.ScalarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1");
        Assert.Equal(0, untrusted);
    }

    [Fact]
    public async Task ResetAsync_ShouldEmptyTheHistoryAndKeepVersioning_WhenATableIsSystemVersioned()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync();

        await db.Sql.ResetAsync(TestContext.Current.CancellationToken);

        int rows = await db.ScalarAsync("SELECT (SELECT COUNT(*) FROM Prices) + (SELECT COUNT(*) FROM PricesHistory)");
        int retention = await db.ScalarAsync("SELECT history_retention_period FROM sys.tables WHERE name = 'Prices' AND temporal_type = 2");
        Assert.Equal((0, 6), (rows, retention));
    }

    // The audit trigger writes to a kept table and the soft-delete trigger keeps its rows, so either one running shows.
    [Fact]
    public async Task ResetAsync_ShouldNotRunTheAppsDeleteTriggers_WhenItEmptiesTables()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync();

        await db.Sql.ResetAsync(TestContext.Current.CancellationToken);

        int rows = await db.ScalarAsync("SELECT (SELECT COUNT(*) FROM AuditLog) + (SELECT COUNT(*) FROM SoftBooks)");
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task ResetAsync_ShouldLeaveEachTriggerAsItWas_WhenItPausedTheEnabledOnes()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync();

        await db.Sql.ResetAsync(TestContext.Current.CancellationToken);

        int unchanged = await db.ScalarAsync("""
            SELECT COUNT(*) FROM sys.triggers
            WHERE (name IN ('AuditOrders', 'SoftDelete') AND is_disabled = 0) OR (name = 'Dormant' AND is_disabled = 1)
            """);
        Assert.Equal(3, unchanged);
    }

    // Cultures such as Swedish write -1 with a Unicode minus sign, which T-SQL rejects; the table's seed of 0 needs a
    // reseed to -1.
    [Fact]
    public async Task ResetAsync_ShouldRestartIdentityColumns_WhenTheCultureWritesNegativeNumbersDifferently()
    {
        await using ResetDatabase db = await ResetDatabase.CreateAsync();
        CultureInfo culture = CultureInfo.CurrentCulture;
        var unicodeMinus = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        unicodeMinus.NumberFormat.NegativeSign = "\u2212";
        CultureInfo.CurrentCulture = unicodeMinus;
        try
        {
            await db.Sql.ResetAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        int id = await db.ScalarAsync("INSERT INTO Tickets (Note) VALUES ('a'); SELECT CAST(SCOPE_IDENTITY() AS int)");
        Assert.Equal(0, id);
    }

    // A fixture's server with one database: kept tables, a self-reference, a chain, a cycle, a system-versioned table,
    // delete triggers, sequences drawn by defaults and by the app, and migrations histories under EF Core's default name
    // and a custom one.
    private sealed class ResetDatabase : IAsyncDisposable
    {
        private const string Schema = """
            CREATE TABLE Countries (Id int IDENTITY PRIMARY KEY, Name nvarchar(50) NOT NULL);
            CREATE TABLE Customers (Id int IDENTITY PRIMARY KEY, CountryId int NOT NULL REFERENCES Countries (Id),
                ReferredById int NULL REFERENCES Customers (Id));
            CREATE TABLE Orders (Id int IDENTITY PRIMARY KEY, CustomerId int NOT NULL REFERENCES Customers (Id));
            CREATE TABLE Departments (Id int PRIMARY KEY, HeadId int NULL);
            CREATE TABLE Staff (Id int PRIMARY KEY, DepartmentId int NOT NULL REFERENCES Departments (Id));
            ALTER TABLE Departments ADD CONSTRAINT FK_Departments_Head FOREIGN KEY (HeadId) REFERENCES Staff (Id);
            CREATE TABLE Currencies (Code nchar(3) PRIMARY KEY);
            CREATE TABLE Prices (Id int IDENTITY PRIMARY KEY, Amount int NOT NULL,
                ValidFrom datetime2 GENERATED ALWAYS AS ROW START NOT NULL, ValidTo datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
                PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo))
                WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.PricesHistory, HISTORY_RETENTION_PERIOD = 6 MONTHS));
            CREATE TABLE Tickets (Id int IDENTITY(0, 1) PRIMARY KEY, Note nvarchar(10) NULL);
            CREATE TABLE AuditLog (Id int IDENTITY PRIMARY KEY, Entry nvarchar(50) NOT NULL);
            CREATE TABLE SoftBooks (Id int PRIMARY KEY, Deleted bit NOT NULL DEFAULT 0);
            CREATE SEQUENCE InvoiceNumbers AS int START WITH 100;
            CREATE SEQUENCE RegionNumbers AS int START WITH 1;
            CREATE SEQUENCE HiLoNumbers AS int START WITH 1;
            CREATE TABLE Invoices (Id int PRIMARY KEY DEFAULT (NEXT VALUE FOR InvoiceNumbers));
            CREATE TABLE Regions (Id int PRIMARY KEY DEFAULT (NEXT VALUE FOR RegionNumbers));
            CREATE TABLE __EFMigrationsHistory (MigrationId nvarchar(150) PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
            CREATE TABLE MigrationLog (MigrationId nvarchar(150) PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
            INSERT INTO __EFMigrationsHistory VALUES ('20260101000000_Initial', '10.0.0');
            INSERT INTO MigrationLog VALUES ('20260101000000_Initial', '10.0.0');
            INSERT INTO Invoices DEFAULT VALUES;
            INSERT INTO Regions DEFAULT VALUES;
            DECLARE @drawn int = NEXT VALUE FOR HiLoNumbers;
            INSERT INTO Countries (Name) VALUES ('North');
            INSERT INTO Currencies VALUES ('EUR');
            INSERT INTO Tickets (Note) VALUES ('a');
            INSERT INTO SoftBooks (Id) VALUES (1);
            INSERT INTO Prices (Amount) VALUES (1);
            UPDATE Prices SET Amount = 2;
            INSERT INTO Customers (CountryId) VALUES (1);
            INSERT INTO Customers (CountryId, ReferredById) VALUES (1, 1);
            INSERT INTO Orders (CustomerId) VALUES (2);
            INSERT INTO Departments (Id) VALUES (1);
            INSERT INTO Staff (Id, DepartmentId) VALUES (1, 1);
            UPDATE Departments SET HeadId = 1;
            """;

        // CREATE TRIGGER and CREATE SCHEMA have to start their own batch, and Rates needs the history schema first.
        private static readonly string[] Batches =
        [
            "CREATE TRIGGER AuditOrders ON Orders AFTER DELETE AS "
                + "INSERT INTO AuditLog (Entry) SELECT 'deleted' FROM deleted;",
            "CREATE TRIGGER SoftDelete ON SoftBooks INSTEAD OF DELETE AS "
                + "UPDATE SoftBooks SET Deleted = 1 WHERE Id IN (SELECT Id FROM deleted);",
            "CREATE TRIGGER Dormant ON Staff AFTER DELETE AS SET NOCOUNT ON;",
            "DISABLE TRIGGER Dormant ON Staff;",
            "CREATE SCHEMA history;",
            "CREATE TABLE Rates (Id int PRIMARY KEY, ValidFrom datetime2 GENERATED ALWAYS AS ROW START NOT NULL, "
                + "ValidTo datetime2 GENERATED ALWAYS AS ROW END NOT NULL, PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)) "
                + "WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = history.Rates)); INSERT INTO Rates (Id) VALUES (1);"
        ];

        private readonly string _database;

        private ResetDatabase(SqlServerResource sql, string database)
        {
            Sql = sql;
            _database = database;
        }

        public SqlServerResource Sql { get; }

        public static async Task<ResetDatabase> CreateAsync(IReadOnlyList<string>? keep = null, bool createDatabase = true)
        {
            var sql = new SqlServerResource(new SqlServerOptions
            {
                Source = SqlServerSource.EnvironmentVariableOrLocalDb("FIXTURA_SQLSERVER"),
                RequiredWhenSet = "FIXTURA_SQLSERVER_REQUIRED",
                ReadyTimeout = TimeSpan.FromSeconds(15),
                KeepOnReset = keep ?? ["dbo.Countries", "Currencies", "AuditLog", "[dbo].[Regions]"]
            });
            var context = new TestResourceContext("fixtura_" + Guid.NewGuid().ToString("N")[..8]);
            string? unavailable = (await sql.ProvisionAsync(context)).UnavailableReason;
            Assert.SkipWhen(unavailable is not null, unavailable ?? "");

            var db = new ResetDatabase(sql, sql.Database("Shop"));
            if (!createDatabase)
            {
                return db;
            }

            var master = new SqlConnectionStringBuilder(db._database) { InitialCatalog = "master" };
            await ExecuteAsync(master.ConnectionString, $"CREATE DATABASE [{new SqlConnectionStringBuilder(db._database).InitialCatalog}]");
            await ExecuteAsync(db._database, Schema);
            foreach (string batch in Batches)
            {
                await ExecuteAsync(db._database, batch);
            }

            return db;
        }

        public async Task<int> ScalarAsync(string query)
        {
            await using var connection = new SqlConnection(_database);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new SqlCommand(query, connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        public ValueTask DisposeAsync() => Sql.DisposeAsync();

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
