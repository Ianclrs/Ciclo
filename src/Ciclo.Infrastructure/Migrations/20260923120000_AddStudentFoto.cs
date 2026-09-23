using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ciclo.Infrastructure.Migrations;

/// <inheritdoc />
[Migration("20260923120000")]
public partial class AddStudentFoto : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Foto",
            table: "Students",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Foto", table: "Students");
    }
}
