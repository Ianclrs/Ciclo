using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ciclo.Infrastructure.Migrations;

/// <inheritdoc />
[Migration("20260925000000")]
public partial class AddTenantCountryFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Country",
            table: "Tenants",
            type: "character varying(2)",
            maxLength: 2,
            nullable: false,
            defaultValue: "BR");

        migrationBuilder.AddColumn<string>(
            name: "DocumentoFiscal",
            table: "Tenants",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Telefone",
            table: "Tenants",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Endereco",
            table: "Tenants",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Cidade",
            table: "Tenants",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Estado",
            table: "Tenants",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Cep",
            table: "Tenants",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Cep", table: "Tenants");
        migrationBuilder.DropColumn(name: "Estado", table: "Tenants");
        migrationBuilder.DropColumn(name: "Cidade", table: "Tenants");
        migrationBuilder.DropColumn(name: "Endereco", table: "Tenants");
        migrationBuilder.DropColumn(name: "Telefone", table: "Tenants");
        migrationBuilder.DropColumn(name: "DocumentoFiscal", table: "Tenants");
        migrationBuilder.DropColumn(name: "Country", table: "Tenants");
    }
}
