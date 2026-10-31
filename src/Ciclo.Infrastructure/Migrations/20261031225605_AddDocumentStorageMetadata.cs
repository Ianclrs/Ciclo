using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ciclo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentStorageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Padrão do design (AD-170-6): tenant existente passa a ter 1 GiB de cota, não zero —
            // zero recusaria todo upload e quebraria o fluxo de documentos no deploy.
            migrationBuilder.AddColumn<long>(
                name: "StorageQuotaBytes",
                table: "Tenants",
                type: "bigint",
                nullable: false,
                defaultValue: 1073741824L);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "EnviadoPorUsuarioId",
                table: "Documents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "HashSha256",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "TamanhoBytes",
                table: "Documents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageQuotaBytes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EnviadoPorUsuarioId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "HashSha256",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TamanhoBytes",
                table: "Documents");
        }
    }
}
