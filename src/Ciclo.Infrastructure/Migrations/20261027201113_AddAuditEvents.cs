using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ciclo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TenantId_Action_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "TenantId", "Action", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TenantId_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TenantId_TargetType_TargetId",
                table: "AuditEvents",
                columns: new[] { "TenantId", "TargetType", "TargetId" });
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION audit_events_append_only_guard()
                RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION 'audit_events_are_append_only';
                    END IF;

                    IF TG_OP = 'DELETE' AND OLD."OccurredAt" > (now() - interval '90 days') THEN
                        RAISE EXCEPTION 'audit_events_within_the_retention_floor_cannot_be_deleted';
                    END IF;

                    RETURN OLD;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_events_no_update
                    BEFORE UPDATE ON "AuditEvents"
                    FOR EACH ROW EXECUTE FUNCTION audit_events_append_only_guard();

                CREATE TRIGGER audit_events_no_delete
                    BEFORE DELETE ON "AuditEvents"
                    FOR EACH ROW EXECUTE FUNCTION audit_events_append_only_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS audit_events_no_update ON "AuditEvents";
                DROP TRIGGER IF EXISTS audit_events_no_delete ON "AuditEvents";
                DROP FUNCTION IF EXISTS audit_events_append_only_guard();
                """);

            migrationBuilder.DropTable(
                name: "AuditEvents");
        }
    }
}
