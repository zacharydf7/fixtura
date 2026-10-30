using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fixtura.Tests.Integration.Data.Notes;

public sealed class Note
{
    public int Id { get; set; }

    public required string Text { get; set; }
}

// A context whose schema comes only from migrations, as in an app that migrates instead of calling EnsureCreated.
public sealed class NotesDb(DbContextOptions<NotesDb> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
}

// The same model with no migrations, as a context gets when someone forgets to add one.
public sealed class DriftedNotesDb(DbContextOptions<DriftedNotesDb> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
}

[DbContext(typeof(NotesDb))]
[Migration("20260101000000_Initial")]
public sealed class Initial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable("Notes", table => new
        {
            Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Notes", note => note.Id));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("Notes");
}

// Written as `dotnet ef migrations add` would, so the migrations cover the model.
[DbContext(typeof(NotesDb))]
public sealed class NotesDbModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0").HasAnnotation("Relational:MaxIdentifierLength", 128);
        SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);
        modelBuilder.Entity("Fixtura.Tests.Integration.Data.Notes.Note", note =>
        {
            note.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(note.Property<int>("Id"));
            note.Property<string>("Text").IsRequired().HasColumnType("nvarchar(max)");
            note.HasKey("Id");
            note.ToTable("Notes");
        });
    }
}
