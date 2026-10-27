using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ciclo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentStatusAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAt",
                table: "Enrollments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StatusChangedBy",
                table: "Enrollments",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "StatusChangedBy",
                table: "Enrollments");
        }
    }
}
